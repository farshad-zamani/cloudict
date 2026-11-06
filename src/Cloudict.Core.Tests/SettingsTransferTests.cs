using System;
using System.Collections.Generic;
using System.IO;
using Cloudict.Abstractions;
using Cloudict.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Cloudict.Core.Tests;

public class SettingsTransferTests
{
    private static AppSettings Example()
    {
        var settings = new AppSettings
        {
            UILanguage = "fa", TypingLanguage = "fa", KeepMicrophoneActive = false,
            LiveTransferEnabled = true, ProcessDelayMs = 777, HelperBrowser = "edge",
            ShortcutShift = true, EnableVoiceCommands = false, CaseSensitiveCommands = true,
            CommandDetectionDelay = 75, LegacyVoiceCommandsMigrated = true
        };
        settings.SetVoiceCommandsFor("fa", new() { new(7, "توقف تایپ", CommandActionType.SendKeys, "Ctrl+Alt+S") { IsEnabled = false } });
        settings.SetVoiceCommandsFor("en", new() { new(3, "signature", CommandActionType.TypeText, "Regards\nفرشاد 👋") });
        settings.SetVoiceCommandsFor("ar", new());
        return settings;
    }

    [Fact]
    public void Full_backup_round_trips_every_setting_and_inactive_language_without_mutating_source()
    {
        var source = Example();
        var before = JsonConvert.SerializeObject(source);
        var restored = SettingsTransfer.ImportSettings(SettingsTransfer.ExportSettings(source));
        Assert.Equal(JsonConvert.SerializeObject(source), JsonConvert.SerializeObject(restored));
        Assert.Equal(before, JsonConvert.SerializeObject(source));
        Assert.False(restored.KeepMicrophoneActive);
        Assert.False(restored.VoiceCommandSets["fa"][0].IsEnabled);
        Assert.Empty(restored.GetVoiceCommandsFor("ar"));
        restored.VoiceCommandSets["fa"][0].Phrase = "changed";
        Assert.Equal("توقف تایپ", source.VoiceCommandSets["fa"][0].Phrase);
    }

    [Fact]
    public void Command_import_preserves_unrelated_settings_and_languages_but_replaces_included_languages()
    {
        var target = new AppSettings { TypingLanguage = "de", ProcessDelayMs = 999, KeepMicrophoneActive = true };
        target.SetVoiceCommandsFor("de", new() { new(1, "pause", CommandActionType.SendKeys, "Space") });
        target.SetVoiceCommandsFor("fa", new() { new(1, "قبلی", CommandActionType.TypeText, "old") });
        target.SetVoiceCommandsFor("ar", new() { new(1, "قديم", CommandActionType.TypeText, "old") });
        var before = JsonConvert.SerializeObject(target);
        var imported = SettingsTransfer.ImportCommands(SettingsTransfer.ExportCommands(Example()), target);
        Assert.Equal("de", imported.TypingLanguage);
        Assert.Equal(999, imported.ProcessDelayMs);
        Assert.True(imported.KeepMicrophoneActive);
        Assert.Single(imported.VoiceCommandSets["de"]);
        Assert.Equal("Ctrl+Alt+S", imported.VoiceCommandSets["fa"][0].ActionValue);
        Assert.Empty(imported.VoiceCommandSets["ar"]);
        Assert.False(imported.EnableVoiceCommands);
        Assert.True(imported.CaseSensitiveCommands);
        Assert.Equal(75, imported.CommandDetectionDelay);
        Assert.Equal(before, JsonConvert.SerializeObject(target));
    }

    [Fact]
    public void Export_materializes_only_the_active_default_set_and_migrates_legacy_commands_on_a_copy()
    {
        var source = new AppSettings { TypingLanguage = "fa" };
        var imported = SettingsTransfer.ImportSettings(SettingsTransfer.ExportSettings(source));
        Assert.NotEmpty(imported.GetVoiceCommandsFor("fa"));
        Assert.Empty(source.VoiceCommandSets);
        Assert.False(source.LegacyVoiceCommandsMigrated);
        source.VoiceCommands.Add(new(99, "امضای من", CommandActionType.TypeText, "با احترام"));
        imported = SettingsTransfer.ImportSettings(SettingsTransfer.ExportSettings(source));
        Assert.Equal("امضای من", Assert.Single(imported.GetVoiceCommandsFor("fa")).Phrase);
    }

    [Fact]
    public void Backup_types_cannot_be_confused()
    {
        Assert.Throws<InvalidDataException>(() => SettingsTransfer.ImportSettings(SettingsTransfer.ExportCommands(Example())));
        Assert.Throws<InvalidDataException>(() => SettingsTransfer.ImportCommands(SettingsTransfer.ExportSettings(Example()), Example()));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"Format\":\"Cloudict.Settings\",\"Version\":999,\"Data\":{}}")]
    [InlineData("{\"Format\":\"Cloudict.Settings\",\"Version\":1,\"Data\":{}}")]
    public void Unrelated_incomplete_and_unsupported_files_are_rejected(string json) =>
        Assert.ThrowsAny<Exception>(() => SettingsTransfer.ImportSettings(json));

    [Theory]
    [InlineData("nullSets")]
    [InlineData("nullList")]
    [InlineData("nullCommand")]
    [InlineData("badKey")]
    [InlineData("badEnum")]
    [InlineData("emptyPhrase")]
    [InlineData("badLanguage")]
    [InlineData("badDelay")]
    public void Corrupt_command_import_leaves_current_settings_untouched(string corruption)
    {
        var current = Example();
        var before = JsonConvert.SerializeObject(current);
        var document = JObject.Parse(SettingsTransfer.ExportCommands(current));
        var data = (JObject)document["Data"];
        var sets = (JObject)data["VoiceCommandSets"];
        var command = (JObject)sets["fa"][0];
        switch (corruption)
        {
            case "nullSets": data["VoiceCommandSets"] = null; break;
            case "nullList": sets["fa"] = null; break;
            case "nullCommand": sets["fa"][0] = null; break;
            case "badKey": command["ActionValue"] = "Ctrl+NoSuchKey"; break;
            case "badEnum": command["ActionType"] = 999; break;
            case "emptyPhrase": command["Phrase"] = " "; break;
            case "badLanguage": sets["../other"] = new JArray(); break;
            case "badDelay": data["CommandDetectionDelay"] = -1; break;
        }
        Assert.ThrowsAny<Exception>(() => SettingsTransfer.ImportCommands(document.ToString(), current));
        Assert.Equal(before, JsonConvert.SerializeObject(current));
    }

    [Fact]
    public void Invalid_full_settings_are_rejected()
    {
        var document = JObject.Parse(SettingsTransfer.ExportSettings(Example()));
        document["Data"]["ProcessDelayMs"] = -1;
        Assert.Throws<InvalidDataException>(() => SettingsTransfer.ImportSettings(document.ToString()));
    }

    [Fact]
    public void Saving_and_loading_an_import_preserves_selector_lists_and_creates_a_rollback_backup()
    {
        var paths = new Paths();
        try
        {
            var store = new SettingsManager(paths);
            var old = new AppSettings();
            Assert.True(store.SaveSettings(old));
            var imported = SettingsTransfer.ImportSettings(SettingsTransfer.ExportSettings(Example()));
            Assert.True(store.SaveSettings(imported));
            var loaded = store.LoadSettings();
            Assert.Equal(imported.TextBoxAriaLabels, loaded.TextBoxAriaLabels);
            Assert.Equal(imported.TextBoxClassSelectors, loaded.TextBoxClassSelectors);
            Assert.False(loaded.KeepMicrophoneActive);
            Assert.Equal("Ctrl+Alt+S", loaded.VoiceCommandSets["fa"][0].ActionValue);
            Assert.True(File.Exists(store.BackupFilePath));
            Assert.Contains("\"KeepMicrophoneActive\": true", File.ReadAllText(store.BackupFilePath));
        }
        finally { Directory.Delete(paths.ConfigDirectory, recursive: true); }
    }

    private sealed class Paths : IAppPaths
    {
        public string ConfigDirectory { get; } = Path.Combine(Path.GetTempPath(), "Cloudict-transfer-test-" + Guid.NewGuid().ToString("N"));
        public string DataDirectory => ConfigDirectory;
        public string LogDirectory => ConfigDirectory;
        public void EnsureCreated() => Directory.CreateDirectory(ConfigDirectory);
    }

    [Fact]
    public void Duplicate_properties_trailing_documents_and_oversized_files_are_rejected()
    {
        var json = SettingsTransfer.ExportSettings(Example());
        Assert.ThrowsAny<Exception>(() => SettingsTransfer.ImportSettings(json.Replace("\"Version\": 1", "\"Version\": 1, \"Version\": 1")));
        Assert.ThrowsAny<Exception>(() => SettingsTransfer.ImportSettings(json + " {}"));
        Assert.Throws<InvalidDataException>(() => SettingsTransfer.ImportSettings(new string('x', SettingsTransfer.MaximumBytes + 1)));
    }
}

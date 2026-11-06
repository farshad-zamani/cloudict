using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cloudict.Services;

/// <summary>Portable, versioned backups. Reading never modifies the current settings.</summary>
public static class SettingsTransfer
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    private const string SettingsKind = "Cloudict.Settings";
    private const string CommandsKind = "Cloudict.VoiceCommands";
    private static JsonSerializer Serializer() => JsonSerializer.Create(new JsonSerializerSettings
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        TypeNameHandling = TypeNameHandling.None
    });

    public static string ExportSettings(AppSettings settings)
    {
        var copy = Clone(settings);
        Validate(copy);
        return Envelope(SettingsKind, JObject.FromObject(copy));
    }

    public static AppSettings ImportSettings(string json)
    {
        var data = Read(json, SettingsKind);
        // An arbitrary JSON object must not silently become a fresh default configuration.
        if (data["TypingLanguage"] == null || data["VoiceCommandSets"] == null)
            throw new InvalidDataException("Missing settings data.");
        var settings = data.ToObject<AppSettings>(Serializer());
        Validate(settings);
        settings.MigrateLegacyVoiceCommands();
        return settings;
    }

    /// <summary>Exports every saved language, including deliberately empty command sets.</summary>
    public static string ExportCommands(AppSettings settings)
    {
        var copy = Clone(settings);
        ValidateCommands(copy.VoiceCommandSets);
        return Envelope(CommandsKind, new JObject
        {
            ["VoiceCommandSets"] = JObject.FromObject(copy.VoiceCommandSets),
            ["EnableVoiceCommands"] = copy.EnableVoiceCommands,
            ["CaseSensitiveCommands"] = copy.CaseSensitiveCommands,
            ["CommandDetectionDelay"] = copy.CommandDetectionDelay
        });
    }

    /// <summary>Replaces only languages present in the file; other languages and settings survive.</summary>
    public static AppSettings ImportCommands(string json, AppSettings current)
    {
        var data = Read(json, CommandsKind);
        if (data["VoiceCommandSets"] is not JObject sets ||
            data["EnableVoiceCommands"]?.Type != JTokenType.Boolean ||
            data["CaseSensitiveCommands"]?.Type != JTokenType.Boolean ||
            data["CommandDetectionDelay"]?.Type != JTokenType.Integer)
            throw new InvalidDataException("Missing voice command data.");

        var commands = sets.ToObject<Dictionary<string, List<VoiceCommand>>>();
        ValidateCommands(commands);
        var delay = data["CommandDetectionDelay"].Value<int>();
        if (delay < 10 || delay > 1000) throw new InvalidDataException("Invalid command delay.");

        var copy = Clone(current);
        foreach (var set in commands) copy.VoiceCommandSets[set.Key] = set.Value;
        copy.EnableVoiceCommands = data["EnableVoiceCommands"].Value<bool>();
        copy.CaseSensitiveCommands = data["CaseSensitiveCommands"].Value<bool>();
        copy.CommandDetectionDelay = delay;
        Validate(copy);
        return copy;
    }

    private static AppSettings Clone(AppSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        var copy = JObject.FromObject(settings).ToObject<AppSettings>(Serializer());
        copy.MigrateLegacyVoiceCommands();
        // Include the active language even before a command editor has ever been opened.
        copy.GetVoiceCommandsFor(copy.TypingLanguage);
        return copy;
    }

    private static string Envelope(string kind, JObject data)
    {
        var json = new JObject { ["Format"] = kind, ["Version"] = 1, ["Data"] = data }.ToString(Formatting.Indented);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes)
            throw new InvalidDataException("Backup too large.");
        return json;
    }

    private static JObject Read(string json, string kind)
    {
        if (string.IsNullOrWhiteSpace(json) || System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes)
            throw new InvalidDataException("Empty or oversized backup.");
        using var input = new StringReader(json);
        using var reader = new JsonTextReader(input) { MaxDepth = 64, DateParseHandling = DateParseHandling.None };
        var document = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (reader.Read()) throw new InvalidDataException("Unexpected content after backup.");
        if (document["Format"]?.Value<string>() != kind || document["Version"]?.Type != JTokenType.Integer ||
            document["Version"].Value<int>() != 1 || document["Data"] is not JObject data)
            throw new InvalidDataException("Unsupported backup format or version.");
        return data;
    }

    private static void Validate(AppSettings settings)
    {
        if (settings == null || !settings.IsValid() ||
            (settings.UILanguage != "en" && settings.UILanguage != "fa") ||
            !Language(settings.TypingLanguage) || string.IsNullOrWhiteSpace(settings.ShortcutKey) ||
            string.IsNullOrWhiteSpace(settings.StopShortcutKey) ||
            settings.TextBoxAriaLabels.Any(string.IsNullOrWhiteSpace) ||
            settings.TextBoxClassSelectors.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Invalid settings.");
        ValidateCommands(settings.VoiceCommandSets);
        if (settings.VoiceCommands == null) throw new InvalidDataException("Missing legacy command list.");
        ValidateCommands(new Dictionary<string, List<VoiceCommand>> { ["fa"] = settings.VoiceCommands });
    }

    private static bool Language(string code) => code != null && Regex.IsMatch(code, @"^[a-z]{2,3}(-[a-z0-9]{2,8})*$");

    private static void ValidateCommands(Dictionary<string, List<VoiceCommand>> sets)
    {
        if (sets == null) throw new InvalidDataException("Missing command sets.");
        foreach (var set in sets)
        {
            if (!Language(set.Key) || set.Value == null) throw new InvalidDataException("Invalid command language.");
            foreach (var command in set.Value)
            {
                if (command == null || string.IsNullOrWhiteSpace(command.Phrase) ||
                    !Enum.IsDefined(command.ActionType) || command.ActionValue == null ||
                    (command.ActionType == CommandActionType.SendKeys && !KeyCommandParser.TryParse(command.ActionValue, out _)))
                    throw new InvalidDataException("Invalid voice command.");
            }
        }
    }
}

using System;
using System.IO;
using Cloudict.Abstractions;
using Newtonsoft.Json;

namespace Cloudict.Services
{
    /// <summary>
    /// Loads and saves <see cref="AppSettings"/> as JSON, with a backup copy and validation.
    ///
    /// <para>Two things changed when this moved into Core. It no longer calls
    /// <c>MessageBox.Show</c> — it raises <see cref="UserMessage"/> and lets the UI decide how to
    /// present the problem, which is what makes the class usable on every platform and testable
    /// without a screen. And it no longer writes beside the executable: that only ever worked
    /// because the Windows build runs elevated, and would fail outright under a Linux install
    /// prefix or inside a signed macOS app bundle. Settings now live in the user's own config
    /// directory, with a one-time import of any file left in the old location.</para>
    /// </summary>
    public class SettingsManager : IUserMessageSource
    {
        private const string SettingsFileName = "settings.json";
        private const string BackupFileName = "settings.backup.json";

        /// <summary>Raised when the user needs to be told something. See <see cref="UserMessageEventArgs"/>.</summary>
        public event EventHandler<UserMessageEventArgs> UserMessage;

        public string SettingsFilePath { get; }
        public string BackupFilePath { get; }

        public SettingsManager(IAppPaths paths)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));

            paths.EnsureCreated();
            SettingsFilePath = Path.Combine(paths.ConfigDirectory, SettingsFileName);
            BackupFilePath = Path.Combine(paths.ConfigDirectory, BackupFileName);

            MigrateFromLegacyLocation();
        }

        /// <summary>
        /// Brings settings written by 2.x — which stored them next to the executable — into the
        /// per-user config directory, once. Without this an upgrading Windows user silently loses
        /// their delays, shortcuts and voice commands.
        /// </summary>
        private void MigrateFromLegacyLocation()
        {
            try
            {
                if (File.Exists(SettingsFilePath)) return;

                var legacy = Path.Combine(AppContext.BaseDirectory, SettingsFileName);
                if (!File.Exists(legacy)) return;

                File.Copy(legacy, SettingsFilePath, overwrite: false);

                var legacyBackup = Path.Combine(AppContext.BaseDirectory, BackupFileName);
                if (File.Exists(legacyBackup) && !File.Exists(BackupFilePath))
                    File.Copy(legacyBackup, BackupFilePath, overwrite: false);
            }
            catch (Exception ex)
            {
                // A failed migration must never stop the app starting — defaults are a fine outcome.
                System.Diagnostics.Debug.WriteLine($"[SettingsManager] legacy migration skipped: {ex.Message}");
            }
        }

        private void Notify(string messageKey, string titleKey, UserMessageSeverity severity, params object[] args) =>
            UserMessage?.Invoke(this, new UserMessageEventArgs(messageKey, titleKey, severity, args));

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json);

                    if (settings != null && settings.IsValid())
                    {
                        if (settings.VoiceCommands == null || settings.VoiceCommands.Count == 0)
                            settings.VoiceCommands = AppSettings.GetDefaultCommands();

                        return settings;
                    }

                    Notify("SettingsMgr_InvalidLoaded", "SettingsMgr_LoadError_Title", UserMessageSeverity.Warning);
                    return GetDefaultSettings();
                }
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(BackupFilePath))
                    {
                        string backupJson = File.ReadAllText(BackupFilePath);
                        var backupSettings = JsonConvert.DeserializeObject<AppSettings>(backupJson);

                        if (backupSettings != null && backupSettings.IsValid())
                        {
                            Notify("SettingsMgr_LoadedFromBackup", "SettingsMgr_RestoredFromBackup_Title",
                                   UserMessageSeverity.Information, ex.Message);
                            return backupSettings;
                        }
                    }
                }
                catch (Exception backupEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[SettingsManager] backup unusable: {backupEx.Message}");
                }

                Notify("SettingsMgr_LoadErrorDefaults", "SettingsMgr_LoadError_Title",
                       UserMessageSeverity.Warning, ex.Message);
            }

            return GetDefaultSettings();
        }

        public bool SaveSettings(AppSettings settings)
        {
            if (settings == null)
            {
                Notify("SettingsMgr_Invalid", "SettingsMgr_SaveError_Title", UserMessageSeverity.Error);
                return false;
            }

            if (!settings.IsValid())
            {
                Notify("SettingsMgr_InvalidEntered", "SettingsMgr_ValidationError_Title", UserMessageSeverity.Error);
                return false;
            }

            try
            {
                CreateBackup();

                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);

                // Write-then-replace, so an interruption cannot leave a half-written settings file
                // that the next launch would reject as invalid.
                var temp = SettingsFilePath + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, SettingsFilePath, overwrite: true);

                return true;
            }
            catch (Exception ex)
            {
                Notify("SettingsMgr_SaveError", "SettingsMgr_SaveError_Title", UserMessageSeverity.Error, ex.Message);
                return false;
            }
        }

        public AppSettings GetDefaultSettings()
        {
            return new AppSettings { VoiceCommands = AppSettings.GetDefaultCommands() };
        }

        private void CreateBackup()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                    File.Copy(SettingsFilePath, BackupFilePath, true);
            }
            catch (Exception ex)
            {
                // Losing the backup is not worth failing the save for.
                System.Diagnostics.Debug.WriteLine($"[SettingsManager] backup skipped: {ex.Message}");
            }
        }

        public bool SettingsFileExists() => File.Exists(SettingsFilePath);

        public bool ResetToDefaults()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                    File.Delete(SettingsFilePath);
                return true;
            }
            catch (Exception ex)
            {
                Notify("SettingsMgr_ResetError", "Common_Error_Title", UserMessageSeverity.Error, ex.Message);
                return false;
            }
        }
    }
}

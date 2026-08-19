using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Cloudict.App.Services;
using Cloudict.Services;

namespace Cloudict.App.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly VoiceCommandManager _commandManager;
        private readonly ObservableCollection<VoiceCommand> _commands = new ObservableCollection<VoiceCommand>();

        private AppSettings _settings;

        /// <summary>True when the user saved; the caller reloads settings only then.</summary>
        public bool Saved { get; private set; }

        public SettingsWindow(VoiceCommandManager commandManager)
        {
            InitializeComponent();

            WindowSizing.FitToWorkArea(this, 960, 700);
            TxtVersion.Text = AppInfo.DisplayVersion;

            _settings = AppServices.Settings.LoadSettings();
            _commandManager = commandManager ?? new VoiceCommandManager(_settings);

            GridCommands.ItemsSource = _commands;

            PopulateLanguages();
            PopulateFromSettings();
            DescribePlatform();
        }


        #region Populate

        private void PopulateLanguages()
        {
            // Dictation languages, shown in their own script so they are recognizable.
            var typing = new (string Code, string Label)[]
            {
                ("en", "English"), ("fa", "فارسی — Persian"), ("ar", "العربية — Arabic"),
                ("fr", "Français — French"), ("de", "Deutsch — German"), ("es", "Español — Spanish"),
                ("it", "Italiano — Italian"), ("pt", "Português — Portuguese"), ("ru", "Русский — Russian"),
                ("tr", "Türkçe — Turkish"), ("nl", "Nederlands — Dutch"), ("pl", "Polski — Polish"),
                ("uk", "Українська — Ukrainian"), ("sv", "Svenska — Swedish"), ("hi", "हिन्दी — Hindi"),
                ("zh", "中文 — Chinese"), ("ja", "日本語 — Japanese"), ("ko", "한국어 — Korean"),
            };

            CmbTypingLanguage.ItemsSource = typing.Select(t => t.Label).ToList();
            CmbTypingLanguage.Tag = typing.Select(t => t.Code).ToList();

            var ui = new (string Code, string Label)[] { ("en", "English"), ("fa", "فارسی") };
            CmbUiLanguage.ItemsSource = ui.Select(u => u.Label).ToList();
            CmbUiLanguage.Tag = ui.Select(u => u.Code).ToList();
        }

        private void PopulateFromSettings()
        {
            SelectByCode(CmbTypingLanguage, _settings.TypingLanguage, "en");
            SelectByCode(CmbUiLanguage, _settings.UILanguage, "en");

            TxtProcessDelay.Text = _settings.ProcessDelayMs.ToString(CultureInfo.InvariantCulture);
            TxtWordDelay.Text = _settings.WordByWordDelayMs.ToString(CultureInfo.InvariantCulture);
            TxtStartDelay.Text = _settings.TransferStartDelayMs.ToString(CultureInfo.InvariantCulture);
            TxtInactivityDelay.Text = _settings.InactivityDelayMs.ToString(CultureInfo.InvariantCulture);

            TxtMicXPath.Text = _settings.MicButtonXPath;
            TxtAriaLabels.Text = string.Join(Environment.NewLine, _settings.TextBoxAriaLabels ?? new List<string>());
            TxtClassSelectors.Text = string.Join(Environment.NewLine, _settings.TextBoxClassSelectors ?? new List<string>());

            ChkShortcutEnabled.IsChecked = _settings.GlobalShortcutEnabled;
            ChkToggleCtrl.IsChecked = _settings.ShortcutCtrl;
            ChkToggleAlt.IsChecked = _settings.ShortcutAlt;
            TxtToggleKey.Text = _settings.ShortcutKey;
            ChkStopCtrl.IsChecked = _settings.StopShortcutCtrl;
            ChkStopAlt.IsChecked = _settings.StopShortcutAlt;
            TxtStopKey.Text = _settings.StopShortcutKey;

            ReloadCommands();
        }

        private static void SelectByCode(ComboBox combo, string code, string fallback)
        {
            var codes = combo.Tag as List<string>;
            if (codes == null) return;

            var index = codes.IndexOf(string.IsNullOrWhiteSpace(code) ? fallback : code);
            combo.SelectedIndex = index >= 0 ? index : codes.IndexOf(fallback);
        }

        private static string SelectedCode(ComboBox combo, string fallback)
        {
            var codes = combo.Tag as List<string>;
            if (codes == null || combo.SelectedIndex < 0 || combo.SelectedIndex >= codes.Count) return fallback;
            return codes[combo.SelectedIndex];
        }

        private void ReloadCommands()
        {
            _commands.Clear();

            var language = SelectedCode(CmbTypingLanguage, "en");
            foreach (var command in _settings.GetVoiceCommandsFor(language))
                _commands.Add(command);
        }

        /// <summary>
        /// Shows what this platform can actually do. On Linux and macOS a capability may be missing
        /// for reasons the user can fix — a Wayland session, a permission not yet granted — so the
        /// interface says so here rather than leaving them to guess why nothing is typed.
        /// </summary>
        private void DescribePlatform()
        {
            var caps = AppServices.Capabilities;
            if (caps == null) return;

            TxtPlatformSummary.Text = Loc.Get("Settings_PlatformSummary_Fmt",
                caps.InjectionBackend ?? "?",
                Yes(caps.CanInjectText),
                Yes(caps.CanRegisterGlobalHotkeys),
                Yes(caps.CanSwitchKeyboardLayout));

            var limitation = caps.LimitationKeys?.FirstOrDefault();
            if (limitation != null)
            {
                TxtShortcutLimitation.Text = Loc.Get(limitation);
                TxtShortcutLimitation.IsVisible = true;
            }
        }

        private static string Yes(bool value) => Loc.Get(value ? "Common_Yes" : "Common_No");

        #endregion

        #region Actions

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _settings.TypingLanguage = SelectedCode(CmbTypingLanguage, "en");
                _settings.UILanguage = SelectedCode(CmbUiLanguage, "en");

                _settings.ProcessDelayMs = ParseDelay(TxtProcessDelay.Text, _settings.ProcessDelayMs);
                _settings.WordByWordDelayMs = ParseDelay(TxtWordDelay.Text, _settings.WordByWordDelayMs);
                _settings.TransferStartDelayMs = ParseDelay(TxtStartDelay.Text, _settings.TransferStartDelayMs);
                _settings.InactivityDelayMs = ParseDelay(TxtInactivityDelay.Text, _settings.InactivityDelayMs);

                _settings.MicButtonXPath = TxtMicXPath.Text?.Trim();
                _settings.TextBoxAriaLabels = SplitLines(TxtAriaLabels.Text);
                _settings.TextBoxClassSelectors = SplitLines(TxtClassSelectors.Text);

                _settings.GlobalShortcutEnabled = ChkShortcutEnabled.IsChecked == true;
                _settings.ShortcutCtrl = ChkToggleCtrl.IsChecked == true;
                _settings.ShortcutAlt = ChkToggleAlt.IsChecked == true;
                _settings.ShortcutKey = string.IsNullOrWhiteSpace(TxtToggleKey.Text) ? "A" : TxtToggleKey.Text.Trim();
                _settings.StopShortcutCtrl = ChkStopCtrl.IsChecked == true;
                _settings.StopShortcutAlt = ChkStopAlt.IsChecked == true;
                _settings.StopShortcutKey = string.IsNullOrWhiteSpace(TxtStopKey.Text) ? "S" : TxtStopKey.Text.Trim();

                _settings.SetVoiceCommandsFor(_settings.TypingLanguage, _commands.ToList());

                if (AppServices.Settings.SaveSettings(_settings))
                {
                    Saved = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsWindow] save failed: {ex.Message}");
            }
        }

        /// <summary>Keeps an unparseable or out-of-range entry from corrupting a working delay.</summary>
        private static int ParseDelay(string text, int fallback)
        {
            if (!int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return fallback;

            return value < 50 || value > 10000 ? fallback : value;
        }

        private static List<string> SplitLines(string text) =>
            (text ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .Distinct()
                .ToList();

        private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            _settings = AppServices.Settings.GetDefaultSettings();
            PopulateFromSettings();
        }

        private void OnAddCommandClick(object sender, RoutedEventArgs e)
        {
            var nextId = _commands.Count == 0 ? 1 : _commands.Max(c => c.Id) + 1;
            _commands.Add(new VoiceCommand(nextId, Loc.Get("SW_NewCommandPhrase"), CommandActionType.TypeText, ""));
        }

        private void OnDeleteCommandClick(object sender, RoutedEventArgs e)
        {
            if (GridCommands.SelectedItem is VoiceCommand selected) _commands.Remove(selected);
        }

        private void OnRestoreCommandsClick(object sender, RoutedEventArgs e)
        {
            var language = SelectedCode(CmbTypingLanguage, "en");
            _commands.Clear();

            foreach (var command in AppSettings.GetDefaultCommandsForLanguage(language))
                _commands.Add(command);
        }

        private async void OnCopyDiagnosticsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var clipboard = GetTopLevel(this)?.Clipboard;
                if (clipboard != null) await clipboard.SetTextAsync(Diagnostics.Describe());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsWindow] copy diagnostics failed: {ex.Message}");
            }
        }

        #endregion
    }
}

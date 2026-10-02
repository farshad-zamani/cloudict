using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
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

        /// <summary>Which dictation language the grid's contents belong to.</summary>
        private string _commandLanguage;

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

            var browsers = new (string Code, string LabelKey)[]
            {
                (Cloudict.Speech.HelperBrowsers.Auto, "Settings_HelperBrowser_Auto"),
                (Cloudict.Speech.HelperBrowsers.Chrome, "Settings_HelperBrowser_Chrome"),
                (Cloudict.Speech.HelperBrowsers.Edge, "Settings_HelperBrowser_Edge"),
                (Cloudict.Speech.HelperBrowsers.ChromeForTesting, "Settings_HelperBrowser_Cft")
            };
            CmbHelperBrowser.ItemsSource = browsers.Select(b => Loc.Get(b.LabelKey)).ToList();
            CmbHelperBrowser.Tag = browsers.Select(b => b.Code).ToList();
        }

        private void PopulateFromSettings()
        {
            // The grid is about to be reloaded for whichever language ends up selected. Without
            // this, changing the selection below would first bank the grid's current rows into the
            // settings being populated — under the old language — and carry them across a reset.
            _commandLanguage = null;

            SelectByCode(CmbTypingLanguage, _settings.TypingLanguage, "en");
            SelectByCode(CmbUiLanguage, _settings.UILanguage, "en");
            SelectByCode(CmbHelperBrowser, Cloudict.Speech.HelperBrowsers.Normalise(_settings.HelperBrowser), Cloudict.Speech.HelperBrowsers.Auto);

            TxtProcessDelay.Text = _settings.ProcessDelayMs.ToString(CultureInfo.InvariantCulture);
            TxtWordDelay.Text = _settings.WordByWordDelayMs.ToString(CultureInfo.InvariantCulture);
            TxtStartDelay.Text = _settings.TransferStartDelayMs.ToString(CultureInfo.InvariantCulture);
            TxtInactivityDelay.Text = _settings.InactivityDelayMs.ToString(CultureInfo.InvariantCulture);

            TxtMicXPath.Text = _settings.MicButtonXPath;
            TxtAriaLabels.Text = string.Join(Environment.NewLine, _settings.TextBoxAriaLabels ?? new List<string>());
            TxtClassSelectors.Text = string.Join(Environment.NewLine, _settings.TextBoxClassSelectors ?? new List<string>());

            ChkMinimizeToTray.IsChecked = _settings.MinimizeToTray;
            ChkShowIndicator.IsChecked = _settings.ShowStatusIndicator;
            ChkOpenBrowserOnStartup.IsChecked = _settings.OpenBrowserOnStartup;
            ChkCheckForUpdates.IsChecked = _settings.CheckForUpdates;

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

        /// <summary>
        /// Fills the grid with the commands of the selected dictation language.
        ///
        /// <para>The rows are copies, and the grid is detached while they go in. Switching languages
        /// used to put the very same command objects back into the grid that had displayed them a
        /// moment earlier, and the grid could hand those back rows laid out for the previous
        /// contents — the scrambled Persian list that only "Restore defaults" cured, because that is
        /// the one path that always created new objects. Every load now goes the way that worked.
        /// The copies are what gets banked and saved, so nothing is lost by not editing the
        /// originals.</para>
        /// </summary>
        private void ReloadCommands()
        {
            _commandLanguage = SelectedCode(CmbTypingLanguage, "en");
            FillGrid(_settings.GetVoiceCommandsFor(_commandLanguage));
        }

        private void FillGrid(IEnumerable<VoiceCommand> commands)
        {
            GridCommands.ItemsSource = null;
            _commands.Clear();

            foreach (var command in commands)
                _commands.Add(command.Clone());

            GridCommands.ItemsSource = _commands;
        }

        /// <summary>
        /// Voice commands are stored per dictation language, so switching that language has to swap
        /// the grid over — banking what is on screen under the language it was loaded for first.
        ///
        /// <para>Without this the grid kept showing the previous language's commands, and saving
        /// wrote them under the newly chosen one. Switching to a language with no commands of its
        /// own therefore saved an <em>empty</em> set over a full one, which is how a set of voice
        /// commands could disappear from a window the user never touched.</para>
        /// </summary>
        private void OnTypingLanguageChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_commandLanguage == null) return;     // still populating the window

            var chosen = SelectedCode(CmbTypingLanguage, "en");
            if (chosen == _commandLanguage) return;

            _settings.SetVoiceCommandsFor(_commandLanguage, _commands.ToList());
            ReloadCommands();
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
                _settings.HelperBrowser = SelectedCode(CmbHelperBrowser, Cloudict.Speech.HelperBrowsers.Auto);

                _settings.ProcessDelayMs = ParseDelay(TxtProcessDelay.Text, _settings.ProcessDelayMs);
                _settings.WordByWordDelayMs = ParseDelay(TxtWordDelay.Text, _settings.WordByWordDelayMs);
                _settings.TransferStartDelayMs = ParseDelay(TxtStartDelay.Text, _settings.TransferStartDelayMs);
                _settings.InactivityDelayMs = ParseDelay(TxtInactivityDelay.Text, _settings.InactivityDelayMs);

                _settings.MicButtonXPath = TxtMicXPath.Text?.Trim();
                _settings.TextBoxAriaLabels = SplitLines(TxtAriaLabels.Text);
                _settings.TextBoxClassSelectors = SplitLines(TxtClassSelectors.Text);

                _settings.MinimizeToTray = ChkMinimizeToTray.IsChecked == true;
                _settings.ShowStatusIndicator = ChkShowIndicator.IsChecked == true;
                _settings.OpenBrowserOnStartup = ChkOpenBrowserOnStartup.IsChecked == true;
                _settings.CheckForUpdates = ChkCheckForUpdates.IsChecked == true;

                // Live transfer belongs to the main window, which saves it the moment it is
                // toggled. Re-read it so a toggle made while this dialog was open is not
                // overwritten by the value that was loaded when the dialog opened.
                _settings.LiveTransferEnabled = AppServices.Settings.LoadSettings().LiveTransferEnabled;

                _settings.GlobalShortcutEnabled = ChkShortcutEnabled.IsChecked == true;
                _settings.ShortcutCtrl = ChkToggleCtrl.IsChecked == true;
                _settings.ShortcutAlt = ChkToggleAlt.IsChecked == true;
                _settings.ShortcutKey = string.IsNullOrWhiteSpace(TxtToggleKey.Text) ? "A" : TxtToggleKey.Text.Trim();
                _settings.StopShortcutCtrl = ChkStopCtrl.IsChecked == true;
                _settings.StopShortcutAlt = ChkStopAlt.IsChecked == true;
                _settings.StopShortcutKey = string.IsNullOrWhiteSpace(TxtStopKey.Text) ? "S" : TxtStopKey.Text.Trim();

                _settings.SetVoiceCommandsFor(_commandLanguage ?? _settings.TypingLanguage, _commands.ToList());

                // Saving deliberately leaves the window open. Closing on save meant anyone
                // adjusting two things in different tabs had to reopen Settings between them, and
                // it threw away where they were. The window closes when the user says so.
                if (AppServices.Settings.SaveSettings(_settings))
                {
                    Saved = true;
                    ShowSaveResult(Loc.Get("Settings_Saved"), success: true);
                }
                else
                {
                    ShowSaveResult(Loc.Get("Settings_SaveFailed"), success: false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsWindow] save failed: {ex.Message}");
                ShowSaveResult(Loc.Get("Settings_SaveFailed"), success: false);
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

        /// <summary>
        /// Reports the outcome beside the buttons, and clears it after a few seconds so it does not
        /// sit there claiming a save that happened several edits ago.
        /// </summary>
        private void ShowSaveResult(string message, bool success)
        {
            TxtSaveResult.Text = message;
            TxtSaveResult.IsVisible = true;
            TxtSaveResult.Foreground = this.FindResource(success ? "SuccessBrush" : "AccentHoverBrush") as Avalonia.Media.IBrush
                                       ?? TxtSaveResult.Foreground;

            var shown = ++_saveResultToken;

            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                if (_saveResultToken == shown) TxtSaveResult.IsVisible = false;
            }, TimeSpan.FromSeconds(4));
        }

        private int _saveResultToken;

        /// <summary>
        /// Puts every setting back to its default — except the two languages, which are choices
        /// rather than tuning, and which decide what "default" means for the voice commands.
        ///
        /// <para>This used to reset the dictation language to English along with everything else,
        /// so the commands grid showed the English set, which is empty: to a Persian user the reset
        /// simply erased their commands, with the Persian defaults nowhere to be seen. The fresh
        /// settings carry no command sets at all, so each language now receives its own defaults
        /// the first time it is shown — the Persian set for Persian, an empty one elsewhere.</para>
        ///
        /// <para>Nothing is written until Save, as before.</para>
        /// </summary>
        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            var typingLanguage = SelectedCode(CmbTypingLanguage, _settings.TypingLanguage ?? "en");
            var uiLanguage = SelectedCode(CmbUiLanguage, _settings.UILanguage ?? "en");

            _settings = AppServices.Settings.GetDefaultSettings();
            _settings.TypingLanguage = typingLanguage;
            _settings.UILanguage = uiLanguage;

            PopulateFromSettings();
        }

        /// <summary>
        /// Opens the command editor. Until it existed, "Add" put a row called "new command" into a
        /// read-only grid, which is to say it did nothing a user could finish.
        /// </summary>
        private async void OnAddCommandClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var editor = new CommandEditorWindow();
                await editor.ShowDialog(this);

                if (editor.Result == null) return;

                editor.Result.Id = _commands.Count == 0 ? 1 : _commands.Max(c => c.Id) + 1;
                _commands.Add(editor.Result);
                GridCommands.SelectedItem = editor.Result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsWindow] add command failed: {ex.Message}");
            }
        }

        private void OnEditCommandClick(object sender, RoutedEventArgs e) => EditSelectedCommand();

        private void OnCommandDoubleTapped(object sender, TappedEventArgs e) => EditSelectedCommand();

        private async void EditSelectedCommand()
        {
            if (GridCommands.SelectedItem is not VoiceCommand selected) return;

            try
            {
                // The editor works on a copy: cancelling has to leave the original untouched, and the
                // grid is bound straight to these objects.
                var draft = new VoiceCommand(selected.Id, selected.Phrase, selected.ActionType, selected.ActionValue);

                var editor = new CommandEditorWindow(draft);
                await editor.ShowDialog(this);

                if (editor.Result == null) return;

                selected.Phrase = editor.Result.Phrase;
                selected.ActionType = editor.Result.ActionType;
                selected.ActionValue = editor.Result.ActionValue;
                selected.UpdatedAt = DateTime.Now;

                // The row shows a converted value, which no property change on the model announces.
                var index = _commands.IndexOf(selected);
                if (index >= 0)
                {
                    _commands.RemoveAt(index);
                    _commands.Insert(index, selected);
                    GridCommands.SelectedItem = selected;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsWindow] edit command failed: {ex.Message}");
            }
        }

        private void OnDeleteCommandClick(object sender, RoutedEventArgs e)
        {
            if (GridCommands.SelectedItem is VoiceCommand selected) _commands.Remove(selected);
        }

        private void OnRestoreCommandsClick(object sender, RoutedEventArgs e)
        {
            FillGrid(AppSettings.GetDefaultCommandsForLanguage(SelectedCode(CmbTypingLanguage, "en")));
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

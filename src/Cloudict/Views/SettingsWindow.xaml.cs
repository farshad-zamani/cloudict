using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using Microsoft.Win32;
using System.Collections.ObjectModel;

namespace Cloudict
{
    public partial class SettingsWindow : GlassWindow
    {
        private AppSettings _settings;
        private SettingsManager _settingsManager;
        private bool _hasChanges = false;
        private bool _suppressEngineChange = true;
        private string _currentVoiceLang = "fa";
        private VoiceCommandManager _voiceCommandManager;
        
        // Voice Commands
        private ObservableCollection<VoiceCommand> _voiceCommands;
        private ObservableCollection<VoiceCommand> _filteredVoiceCommands;
        private VoiceCommand _selectedCommand;
        private bool _isEditingCommand = false;
        private string _currentSortColumn = "";
        private bool _sortAscending = true;

        public AppSettings Settings => _settings;
        public bool HasChanges => _hasChanges;

        public SettingsWindow(VoiceCommandManager voiceCommandManager = null)
        {
            InitializeComponent();
            _settingsManager = new SettingsManager();
            _voiceCommandManager = voiceCommandManager;
            LoadSettings();
            PopulateControls();
        }

        private void LoadSettings()
        {
            try
            {
                _settings = _settingsManager.LoadSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SW_LoadError", ex.Message), 
                    Loc.Get("Common_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                _settings = _settingsManager.GetDefaultSettings();
            }
        }

        private void PopulateControls()
        {
            // Text Transfer Delays
            txtProcessDelay.Text = _settings.ProcessDelayMs.ToString();
            txtWordByWordDelay.Text = _settings.WordByWordDelayMs.ToString();
            txtTransferStartDelay.Text = _settings.TransferStartDelayMs.ToString();
            txtInactivityDelay.Text = _settings.InactivityDelayMs.ToString();

            // Google Translate Settings
            txtMicButtonXPath.Text = _settings.MicButtonXPath;
            txtTextBoxAriaLabels.Text = string.Join(", ", _settings.TextBoxAriaLabels.Distinct());
            txtTextBoxClassSelectors.Text = string.Join(", ", _settings.TextBoxClassSelectors.Distinct());
            txtPreloadDelay.Text = _settings.PreloadDelayMs.ToString();
            txtMicActivationDelay.Text = _settings.MicActivationDelayMs.ToString();

            // Global Shortcut Settings
            PopulateShortcutSettings();
            
            // Voice Commands Settings
            PopulateVoiceCommandsSettings();

            // Speech Engine Settings
            PopulateEngineSettings();

            // Add event handlers for change detection
            AddChangeDetectionHandlers();

            _suppressEngineChange = false;
        }

        private void PopulateEngineSettings()
        {
            if (rbEngineGoogleTranslate != null)
                rbEngineGoogleTranslate.IsChecked = (_settings.SpeechEngine ?? "GoogleTranslate") == "GoogleTranslate";

            if (cmbTypingLanguage != null)
            {
                string lang = string.IsNullOrWhiteSpace(_settings.TypingLanguage) ? "fa" : _settings.TypingLanguage;
                bool matched = false;
                foreach (var obj in cmbTypingLanguage.Items)
                {
                    if (obj is ComboBoxItem it && (it.Tag?.ToString() ?? "") == lang)
                    { cmbTypingLanguage.SelectedItem = it; matched = true; break; }
                }
                if (!matched && cmbTypingLanguage.Items.Count > 0) cmbTypingLanguage.SelectedIndex = 0;
                cmbTypingLanguage.SelectionChanged += OnSettingChanged;
                cmbTypingLanguage.SelectionChanged += TypingLanguage_Changed;
            }

            UpdateGoogleTranslateTabVisibility();
        }

        private void UpdateGoogleTranslateTabVisibility()
        {
            if (tabGoogleTranslate != null)
                tabGoogleTranslate.Visibility = (rbEngineGoogleTranslate?.IsChecked == true)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        private void EngineRadio_Checked(object sender, RoutedEventArgs e)
        {
            UpdateGoogleTranslateTabVisibility();
            if (!_suppressEngineChange) _hasChanges = true;
        }

        /// <summary>Selects the Speech Engine tab (used by the main window's "Select engine" button).</summary>
        public void SelectEngineTab()
        {
            if (tabControl != null && tabEngine != null)
                tabControl.SelectedItem = tabEngine;
        }

        private string GetSelectedTypingLanguage()
        {
            if (cmbTypingLanguage?.SelectedItem is ComboBoxItem it && it.Tag != null)
                return it.Tag.ToString();
            return string.IsNullOrWhiteSpace(_settings?.TypingLanguage) ? "fa" : _settings.TypingLanguage;
        }

        private void TypingLanguage_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEngineChange) return;
            // Persist edits for the previous language, then load the newly-selected one.
            if (!string.IsNullOrEmpty(_currentVoiceLang) && _voiceCommands != null)
                _settings.SetVoiceCommandsFor(_currentVoiceLang, new List<VoiceCommand>(_voiceCommands));
            ReloadVoiceCommandsForCurrentLanguage();
        }

        private void ReloadVoiceCommandsForCurrentLanguage()
        {
            _currentVoiceLang = GetSelectedTypingLanguage();
            _settings.VoiceCommands = _settings.GetVoiceCommandsFor(_currentVoiceLang);
            if (_voiceCommands == null) _voiceCommands = new ObservableCollection<VoiceCommand>();
            _voiceCommands.Clear();
            foreach (var c in _settings.VoiceCommands) _voiceCommands.Add(c);
            FilterCommands();
            UpdateVoiceCommandsTabBadge(_currentVoiceLang);
        }

        /// <summary>Appends a small colored language code (e.g. FA / EN) to the Voice Commands tab header.</summary>
        private void UpdateVoiceCommandsTabBadge(string lang)
        {
            if (tabVoiceCommands == null) return;
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = Loc.Get("Settings_Tab_VoiceCommands"),
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(new Border
            {
                Background = Application.Current.TryFindResource("AccentSoftBrush") as System.Windows.Media.Brush,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = (lang ?? "fa").ToUpperInvariant(),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = Application.Current.TryFindResource("AccentBrush") as System.Windows.Media.Brush
                }
            });
            tabVoiceCommands.Header = panel;
        }

        private void AddChangeDetectionHandlers()
        {
            try
            {
                txtProcessDelay.TextChanged += OnSettingChanged;
                txtWordByWordDelay.TextChanged += OnSettingChanged;
                txtTransferStartDelay.TextChanged += OnSettingChanged;
                txtInactivityDelay.TextChanged += OnSettingChanged;
                txtMicButtonXPath.TextChanged += OnSettingChanged;
                txtTextBoxAriaLabels.TextChanged += OnSettingChanged;
                txtTextBoxClassSelectors.TextChanged += OnSettingChanged;
                txtPreloadDelay.TextChanged += OnSettingChanged;
                txtMicActivationDelay.TextChanged += OnSettingChanged;
                
                // Global shortcut settings - with null checks
                if (chkEnableGlobalShortcut != null)
                {
                    chkEnableGlobalShortcut.Checked += OnSettingChanged;
                    chkEnableGlobalShortcut.Unchecked += OnSettingChanged;
                }
                
                // Minimize to tray setting
                if (chkMinimizeToTray != null)
                {
                    chkMinimizeToTray.Checked += OnSettingChanged;
                    chkMinimizeToTray.Unchecked += OnSettingChanged;
                }

                // UI language selector
                if (cmbLanguage != null)
                {
                    cmbLanguage.SelectionChanged += OnSettingChanged;
                }

                if (txtShortcutKey != null)
                {
                    txtShortcutKey.TextChanged += OnSettingChanged;
                    txtShortcutKey.TextChanged += txtShortcutKey_TextChanged;
                }
                
                if (chkShiftModifier != null)
                {
                    chkShiftModifier.Checked += OnSettingChanged;
                    chkShiftModifier.Unchecked += OnSettingChanged;
                    chkShiftModifier.Checked += chkModifier_CheckedChanged;
                    chkShiftModifier.Unchecked += chkModifier_CheckedChanged;
                }
                
                if (chkAltModifier != null)
                {
                    chkAltModifier.Checked += OnSettingChanged;
                    chkAltModifier.Unchecked += OnSettingChanged;
                    chkAltModifier.Checked += chkModifier_CheckedChanged;
                    chkAltModifier.Unchecked += chkModifier_CheckedChanged;
                }
                
                if (chkCtrlModifier != null)
                {
                    chkCtrlModifier.Checked += OnSettingChanged;
                    chkCtrlModifier.Unchecked += OnSettingChanged;
                    chkCtrlModifier.Checked += chkModifier_CheckedChanged;
                    chkCtrlModifier.Unchecked += chkModifier_CheckedChanged;
                }
                
                // Voice Commands event handlers
                if (chkEnableVoiceCommands != null)
                {
                    chkEnableVoiceCommands.Checked += OnSettingChanged;
                    chkEnableVoiceCommands.Unchecked += OnSettingChanged;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SW_EventHandlerError", ex.Message), Loc.Get("Common_Error_Title"), 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnSettingChanged(object sender, EventArgs e)
        {
            _hasChanges = true;
        }

        private bool ValidateSettings()
        {
            var errors = new System.Collections.Generic.List<string>();

            // Validate numeric fields
            if (!ValidateNumericField(txtProcessDelay.Text, "تاخیر پردازش متن", 50, 10000, out int processDelay))
                errors.Add(Loc.Get("SW_Val_ProcessDelay"));

            if (!ValidateNumericField(txtWordByWordDelay.Text, "تاخیر کلمه به کلمه", 50, 10000, out int wordDelay))
                errors.Add(Loc.Get("SW_Val_WordDelay"));

            if (!ValidateNumericField(txtTransferStartDelay.Text, "تاخیر شروع انتقال", 50, 10000, out int transferDelay))
                errors.Add(Loc.Get("SW_Val_StartDelay"));

            if (!ValidateNumericField(txtInactivityDelay.Text, "مدت مکث ریست میکروفون", 1000, 60000, out int inactivityDelay))
                errors.Add(Loc.Get("SW_Val_InactivityDelay"));

            if (!ValidateNumericField(txtPreloadDelay.Text, "تاخیر پیش‌بارگذاری", 100, 10000, out int preloadDelay))
                errors.Add(Loc.Get("SW_Val_PreloadDelay"));

            if (!ValidateNumericField(txtMicActivationDelay.Text, "تاخیر فعال‌سازی میکروفون", 100, 10000, out int micActivationDelay))
                errors.Add(Loc.Get("SW_Val_MicActivationDelay"));

            // Validate required text fields
            if (string.IsNullOrWhiteSpace(txtMicButtonXPath.Text))
                errors.Add(Loc.Get("SW_Val_MicXPathEmpty"));

            if (string.IsNullOrWhiteSpace(txtTextBoxAriaLabels.Text))
                errors.Add(Loc.Get("SW_Val_AriaLabelRequired"));

            if (errors.Any())
            {
                MessageBox.Show(string.Join("\n", errors), Loc.Get("Settings_ValidationErrors_Title"), 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private bool ValidateNumericField(string value, string fieldName, int min, int max, out int result)
        {
            result = 0;
            if (!int.TryParse(value, out result))
                return false;
            return result >= min && result <= max;
        }

        private void UpdateSettingsFromControls()
        {
            // Text Transfer Delays
            _settings.ProcessDelayMs = int.Parse(txtProcessDelay.Text);
            _settings.WordByWordDelayMs = int.Parse(txtWordByWordDelay.Text);
            _settings.TransferStartDelayMs = int.Parse(txtTransferStartDelay.Text);
            _settings.InactivityDelayMs = int.Parse(txtInactivityDelay.Text);

            // Google Translate Settings
            _settings.MicButtonXPath = txtMicButtonXPath.Text;
            _settings.TextBoxAriaLabels = new List<string>(txtTextBoxAriaLabels.Text.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Distinct());
            _settings.TextBoxClassSelectors = new List<string>(txtTextBoxClassSelectors.Text.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Distinct());

            // Browser Configuration
            _settings.PreloadDelayMs = int.Parse(txtPreloadDelay.Text);
            _settings.MicActivationDelayMs = int.Parse(txtMicActivationDelay.Text);

            // Global Shortcut Settings
            UpdateShortcutSettingsFromControls();
            
            // Voice Commands Settings
            UpdateVoiceCommandsSettingsFromControls();

            // Speech Engine Settings
            if (rbEngineGoogleTranslate?.IsChecked == true)
                _settings.SpeechEngine = "GoogleTranslate";
            if (cmbTypingLanguage?.SelectedItem is ComboBoxItem typingItem && typingItem.Tag != null)
                _settings.TypingLanguage = typingItem.Tag.ToString();
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateSettings())
                return;

            try
            {
                UpdateSettingsFromControls();
                _settingsManager.SaveSettings(_settings);
                
                // Update VoiceCommandManager with new commands
                if (_voiceCommandManager != null)
                {
                    _voiceCommandManager.RefreshCommands();
                }
                
                _hasChanges = false;
                
                MessageBox.Show(Loc.Get("Settings_Saved"), Loc.Get("Common_Success_Title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);

                // Prompt for restart if the UI language differs from the one currently running.
                if (_settings.UILanguage != LocalizationManager.CurrentLanguage)
                {
                    MessageBox.Show(Loc.Get("Settings_LanguageRestartPrompt"), Loc.Get("Settings_LanguageRestart_Title"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SW_SaveError", ex.Message), Loc.Get("Common_Error_Title"), 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_hasChanges)
            {
                var result = MessageBox.Show(Loc.Get("Settings_UnsavedConfirm"), 
                    Loc.Get("Common_ConfirmExit_Title"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                    return;
            }

            DialogResult = false;
            Close();
        }

        private void btnRestoreDefaults_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(Loc.Get("Settings_ConfirmResetAll"), 
                "بازگردانی به پیش‌فرض", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _settings = _settingsManager.GetDefaultSettings();
                    PopulateControls();
                    _hasChanges = true;
                    
                    MessageBox.Show(Loc.Get("Settings_ResetDoneSavePrompt"), 
                        Loc.Get("Common_Success_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Loc.Get("SW_ResetError", ex.Message), Loc.Get("Common_Error_Title"), 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void btnTestSelectors_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var testMessage = "تست سلکتورها:\n\n";
                testMessage += $"XPath دکمه میکروفون: {txtMicButtonXPath.Text}\n";
                testMessage += $"تعداد Aria-Labels: {txtTextBoxAriaLabels.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Length}\n";
                testMessage += $"تعداد Class Selectors: {txtTextBoxClassSelectors.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Length}\n\n";
                testMessage += "نکته: برای تست کامل، برنامه را راه‌اندازی کرده و عملکرد گوگل ترنسلیت را بررسی کنید.";
                
                MessageBox.Show(testMessage, Loc.Get("Settings_TestResult_Title"), 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SW_TestSelectorsError", ex.Message), Loc.Get("Common_Error_Title"), 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region Global Shortcut Methods

        private void PopulateShortcutSettings()
        {
            try
            {
                // Ensure settings object is not null
                if (_settings == null)
                {
                    _settings = new AppSettings();
                }

                // Minimize to tray setting
                if (chkMinimizeToTray != null)
                    chkMinimizeToTray.IsChecked = _settings.MinimizeToTray;

                // UI language: select the saved language in the combo box
                if (cmbLanguage != null)
                {
                    string lang = string.IsNullOrWhiteSpace(_settings.UILanguage) ? "en" : _settings.UILanguage;
                    bool matched = false;
                    foreach (var obj in cmbLanguage.Items)
                    {
                        if (obj is ComboBoxItem item && (item.Tag?.ToString() ?? "") == lang)
                        {
                            cmbLanguage.SelectedItem = item;
                            matched = true;
                            break;
                        }
                    }
                    if (!matched && cmbLanguage.Items.Count > 0)
                        cmbLanguage.SelectedIndex = 0;
                }

                // Enable/Disable checkbox
                if (chkEnableGlobalShortcut != null)
                    chkEnableGlobalShortcut.IsChecked = _settings.GlobalShortcutEnabled;

                // Set main key in TextBox
                var mainKey = _settings.ShortcutKey ?? "A";
                if (txtShortcutKey != null)
                    txtShortcutKey.Text = mainKey;

                // Modifier keys
                if (chkCtrlModifier != null)
                    chkCtrlModifier.IsChecked = _settings.ShortcutCtrl;
                if (chkShiftModifier != null)
                    chkShiftModifier.IsChecked = _settings.ShortcutShift;
                if (chkAltModifier != null)
                    chkAltModifier.IsChecked = _settings.ShortcutAlt;

                // Stop shortcut settings
                var stopKey = _settings.StopShortcutKey ?? "S";
                if (txtStopShortcutKey != null)
                    txtStopShortcutKey.Text = stopKey;
                    
                if (chkStopCtrlModifier != null)
                    chkStopCtrlModifier.IsChecked = _settings.StopShortcutCtrl;
                if (chkStopShiftModifier != null)
                    chkStopShiftModifier.IsChecked = _settings.StopShortcutShift;
                if (chkStopAltModifier != null)
                    chkStopAltModifier.IsChecked = _settings.StopShortcutAlt;

                // Update current shortcut display
                UpdateCurrentShortcutDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SW_LoadShortcutError", ex.Message), Loc.Get("Common_Error_Title"), 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UpdateShortcutSettingsFromControls()
        {
            try
            {
                if (_settings == null)
                    _settings = new AppSettings();

                _settings.MinimizeToTray = chkMinimizeToTray?.IsChecked ?? false;

                if (cmbLanguage?.SelectedItem is ComboBoxItem langItem && langItem.Tag != null)
                    _settings.UILanguage = langItem.Tag.ToString();
                _settings.GlobalShortcutEnabled = chkEnableGlobalShortcut?.IsChecked ?? false;
                _settings.ShortcutKey = string.IsNullOrWhiteSpace(txtShortcutKey?.Text) ? "A" : txtShortcutKey.Text.ToUpper();
                _settings.ShortcutCtrl = chkCtrlModifier?.IsChecked ?? false;
                _settings.ShortcutShift = chkShiftModifier?.IsChecked ?? false;
                _settings.ShortcutAlt = chkAltModifier?.IsChecked ?? false;
                
                _settings.StopShortcutKey = string.IsNullOrWhiteSpace(txtStopShortcutKey?.Text) ? "S" : txtStopShortcutKey.Text.ToUpper();
                _settings.StopShortcutCtrl = chkStopCtrlModifier?.IsChecked ?? false;
                _settings.StopShortcutShift = chkStopShiftModifier?.IsChecked ?? false;
                _settings.StopShortcutAlt = chkStopAltModifier?.IsChecked ?? false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SW_UpdateShortcutError", ex.Message), Loc.Get("Common_Error_Title"), 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UpdateCurrentShortcutDisplay()
        {
            try
            {
                var shortcutParts = new List<string>();
                
                if (chkCtrlModifier?.IsChecked == true) shortcutParts.Add("Ctrl");
                if (chkShiftModifier?.IsChecked == true) shortcutParts.Add("Shift");
                if (chkAltModifier?.IsChecked == true) shortcutParts.Add("Alt");
                
                var mainKey = string.IsNullOrWhiteSpace(txtShortcutKey?.Text) ? "A" : txtShortcutKey.Text.ToUpper();
                shortcutParts.Add(mainKey);
                
                var shortcutText = string.Join("+", shortcutParts);
                
                // Update start/stop shortcut display
                if (txtCurrentShortcut != null)
                    txtCurrentShortcut.Text = Loc.Get("SW_CurrentStartStop", shortcutText);
                
                // Update stop shortcut display
                var stopShortcutParts = new List<string>();
                
                if (chkStopCtrlModifier?.IsChecked == true) stopShortcutParts.Add("Ctrl");
                if (chkStopShiftModifier?.IsChecked == true) stopShortcutParts.Add("Shift");
                if (chkStopAltModifier?.IsChecked == true) stopShortcutParts.Add("Alt");
                
                var stopKey = string.IsNullOrWhiteSpace(txtStopShortcutKey?.Text) ? "S" : txtStopShortcutKey.Text.ToUpper();
                stopShortcutParts.Add(stopKey);
                
                var stopShortcutText = string.Join("+", stopShortcutParts);
                
                if (txtStopShortcut != null)
                    txtStopShortcut.Text = Loc.Get("SW_CurrentStop", stopShortcutText);
            }
            catch (Exception ex)
            {
                // Log error but don't show message to avoid interrupting user experience
                System.Diagnostics.Debug.WriteLine($"Error updating shortcut display: {ex.Message}");
            }
        }

        private void txtShortcutKey_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.TextBox textBox && textBox != null)
                {
                    // Ensure only single character and uppercase
                    if (textBox.Text.Length > 1)
                    {
                        textBox.Text = textBox.Text.Substring(0, 1);
                        textBox.SelectionStart = 1;
                    }
                    textBox.Text = textBox.Text.ToUpper();
                    textBox.SelectionStart = textBox.Text.Length;
                }
                UpdateCurrentShortcutDisplay();
                _hasChanges = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in txtShortcutKey_TextChanged: {ex.Message}");
            }
        }

        private void chkModifier_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateCurrentShortcutDisplay();
                _hasChanges = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in chkModifier_CheckedChanged: {ex.Message}");
            }
        }
        
        private void txtStopShortcutKey_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.TextBox textBox && textBox != null)
                {
                    // Ensure only single character and uppercase
                    if (textBox.Text.Length > 1)
                    {
                        textBox.Text = textBox.Text.Substring(0, 1);
                        textBox.SelectionStart = 1;
                    }
                    textBox.Text = textBox.Text.ToUpper();
                    textBox.SelectionStart = textBox.Text.Length;
                }
                UpdateCurrentShortcutDisplay();
                _hasChanges = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in txtStopShortcutKey_TextChanged: {ex.Message}");
            }
        }

        private void chkStopModifier_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateCurrentShortcutDisplay();
                _hasChanges = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in chkStopModifier_CheckedChanged: {ex.Message}");
            }
        }

        #endregion

        #region Voice Commands Methods

        /// <summary>
        /// پر کردن تنظیمات دستورات صوتی
        /// </summary>
        private void PopulateVoiceCommandsSettings()
        {
            try
            {
                // مقداردهی اولیه لیست اصلی
                if (_voiceCommands == null)
                {
                    _voiceCommands = new ObservableCollection<VoiceCommand>();
                }
                else
                {
                    _voiceCommands.Clear();
                }

                // مقداردهی اولیه لیست فیلتر شده
                if (_filteredVoiceCommands == null)
                {
                    _filteredVoiceCommands = new ObservableCollection<VoiceCommand>();
                }
                else
                {
                    _filteredVoiceCommands.Clear();
                }

                // Load the command set for the active typing/dictation language.
                _currentVoiceLang = GetSelectedTypingLanguage();
                _settings.VoiceCommands = _settings.GetVoiceCommandsFor(_currentVoiceLang);

                // کپی کردن دستورات به لیست اصلی
                foreach (var command in _settings.VoiceCommands)
                {
                    _voiceCommands.Add(command);
                }

                // اتصال به ListView
                lstVoiceCommands.ItemsSource = _filteredVoiceCommands;

                // فیلتر کردن و نمایش دستورات
                FilterCommands();

                // فعال کردن چک‌باکس دستورات صوتی
                chkEnableVoiceCommands.IsChecked = _settings.EnableVoiceCommands;

                UpdateVoiceCommandsTabBadge(_currentVoiceLang);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SW_LoadVoiceError", ex.Message), Loc.Get("Common_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateVoiceCommandsSettingsFromControls()
        {
            try
            {
                _settings.EnableVoiceCommands = chkEnableVoiceCommands?.IsChecked ?? false;
                _settings.VoiceCommands = new List<VoiceCommand>(_voiceCommands ?? new ObservableCollection<VoiceCommand>());
                _settings.SetVoiceCommandsFor(_currentVoiceLang ?? GetSelectedTypingLanguage(), _settings.VoiceCommands);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SW_UpdateVoiceError", ex.Message), Loc.Get("Common_Error_Title"), 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void btnAddCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ClearCommandEditor();
                _isEditingCommand = false;
                _selectedCommand = null;
                
                // Enable editor
                SetCommandEditorEnabled(true);
                
                // Focus on command text
                txtCommandText?.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SW_AddCommandError", ex.Message), Loc.Get("Common_Error_Title"), 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnEditCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (lstVoiceCommands?.SelectedItem is VoiceCommand selectedCommand)
                {
                    _selectedCommand = selectedCommand;
                    _isEditingCommand = true;
                    
                    // Populate editor with selected command
                    PopulateCommandEditor(selectedCommand);
                    
                    // Enable editor
                    SetCommandEditorEnabled(true);
                    
                    // Focus on command text
                    txtCommandText?.Focus();
                }
                else
                {
                    MessageBox.Show(Loc.Get("Settings_SelectCommandToEdit"), Loc.Get("Common_Warning_Title"), 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SW_EditCommandError", ex.Message), Loc.Get("Common_Error_Title"), 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnDeleteCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (lstVoiceCommands?.SelectedItem is VoiceCommand selectedCommand)
                {
                    var result = MessageBox.Show(Loc.Get("SW_ConfirmDeleteCommand", selectedCommand.Phrase), 
                        "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        
                    if (result == MessageBoxResult.Yes)
                    {
                        _settings.VoiceCommands.Remove(selectedCommand);
                        _voiceCommands.Remove(selectedCommand);
                        _hasChanges = true;
                        
                        // Refresh filtered list and update row numbers
                        FilterCommands();
                        
                        // Clear editor if this command was being edited
                        if (_selectedCommand == selectedCommand)
                        {
                            ClearCommandEditor();
                            SetCommandEditorEnabled(false);
                            _selectedCommand = null;
                            _isEditingCommand = false;
                        }
                    }
                }
                else
                {
                    MessageBox.Show(Loc.Get("Settings_SelectCommandToDelete"), Loc.Get("Common_Warning_Title"), 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SW_DeleteCommandError", ex.Message), Loc.Get("Common_Error_Title"), 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnSaveCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateCommandEditor())
                    return;
                    
                var commandText = txtCommandText?.Text?.Trim() ?? "";
                var actionText = txtCommandParameter?.Text?.Trim() ?? "";
                var commandType = (cmbActionType?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Text";
                var isEnabled = chkCommandEnabled?.IsChecked ?? true;
                
                if (_isEditingCommand && _selectedCommand != null)
                {
                    // Update existing command
                    _selectedCommand.Phrase = commandText;
                    _selectedCommand.ActionValue = actionText;
                    _selectedCommand.ActionType = Enum.Parse<CommandActionType>(commandType);
                    _selectedCommand.IsEnabled = isEnabled;
                    _selectedCommand.UpdatedAt = DateTime.Now;
                }
                else
                {
                    // Add new command with unique ID
                    var maxId = _settings.VoiceCommands.Count > 0 ? _settings.VoiceCommands.Max(c => c.Id) : 0;
                    var newCommand = new VoiceCommand
                    {
                        Id = maxId + 1,
                        Phrase = commandText,
                        ActionValue = actionText,
                        ActionType = Enum.Parse<CommandActionType>(commandType),
                        IsEnabled = isEnabled
                    };
                    
                    _settings.VoiceCommands.Add(newCommand);
                    _voiceCommands.Add(newCommand);
                }
                
                _hasChanges = true;
                
                // Refresh filtered list and update row numbers
                FilterCommands();
                
                // Clear and disable editor
                ClearCommandEditor();
                SetCommandEditorEnabled(false);
                _selectedCommand = null;
                _isEditingCommand = false;
                
                MessageBox.Show(Loc.Get("Settings_CommandSaved"), Loc.Get("Common_Success_Title"), 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SW_SaveCommandError", ex.Message), Loc.Get("Common_Error_Title"), 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnCancelCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ClearCommandEditor();
                SetCommandEditorEnabled(false);
                _selectedCommand = null;
                _isEditingCommand = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SW_CancelEditError", ex.Message), Loc.Get("Common_Error_Title"), 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearCommandEditor()
        {
            try
            {
                if (txtCommandText != null) txtCommandText.Text = "";
                if (txtCommandParameter != null) txtCommandParameter.Text = "";
                if (cmbActionType != null) cmbActionType.SelectedIndex = 0;
                if (chkCommandEnabled != null) chkCommandEnabled.IsChecked = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing command editor: {ex.Message}");
            }
        }

        private void PopulateCommandEditor(VoiceCommand command)
        {
            try
            {
                if (txtCommandText != null) txtCommandText.Text = command.Phrase ?? "";
                if (txtCommandParameter != null) txtCommandParameter.Text = command.ActionValue ?? "";
                if (chkCommandEnabled != null) chkCommandEnabled.IsChecked = command.IsEnabled;
                
                // Set command type
                if (cmbActionType != null)
                {
                    var commandTypeString = command.ActionType.ToString();
                    for (int i = 0; i < cmbActionType.Items.Count; i++)
                    {
                        if (cmbActionType.Items[i] is ComboBoxItem item && 
                            item.Tag?.ToString() == commandTypeString)
                        {
                            cmbActionType.SelectedIndex = i;
                            break;
                        }}
                }
                
                // Update key selection button visibility based on action type
                UpdateKeySelectionButtonVisibility();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error populating command editor: {ex.Message}");
            }
        }

        private void SetCommandEditorEnabled(bool enabled)
        {
            try
            {
                // Show/hide the command editor group
                if (grpCommandEditor != null)
                    grpCommandEditor.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
                    
                if (txtCommandText != null) txtCommandText.IsEnabled = enabled;
                if (txtCommandParameter != null) txtCommandParameter.IsEnabled = enabled;
                if (cmbActionType != null) cmbActionType.IsEnabled = enabled;
                if (chkCommandEnabled != null) chkCommandEnabled.IsEnabled = enabled;
                if (btnSaveCommand != null) btnSaveCommand.IsEnabled = enabled;
                if (btnCancelCommand != null) btnCancelCommand.IsEnabled = enabled;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting command editor enabled state: {ex.Message}");
            }
        }

        private bool ValidateCommandEditor()
        {
            try
            {
                var errors = new List<string>();
                
                var commandText = txtCommandText?.Text?.Trim() ?? "";
                var actionText = txtCommandParameter?.Text?.Trim() ?? "";
                
                if (string.IsNullOrWhiteSpace(commandText))
                    errors.Add(Loc.Get("SW_Val_CommandTextEmpty"));
                    
                if (string.IsNullOrWhiteSpace(actionText))
                    errors.Add(Loc.Get("SW_Val_ActionTextEmpty"));
                    
                // Check for duplicate command text (excluding current command if editing)
                var existingCommand = _voiceCommands?.FirstOrDefault(c => 
                    c.Phrase?.Equals(commandText, StringComparison.OrdinalIgnoreCase) == true &&
                    c != _selectedCommand);
                    
                if (existingCommand != null)
                    errors.Add($"دستور '{commandText}' قبلاً وجود دارد.");
                
                if (errors.Any())
                {
                    MessageBox.Show(string.Join("\n", errors), Loc.Get("Settings_ValidationErrors_Title"), 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SW_ValidationError", ex.Message), Loc.Get("Common_Error_Title"), 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// بازگردانی دستورات صوتی به حالت پیش‌فرض
        /// </summary>
        private void RestoreDefaultVoiceCommands()
        {
            try
            {
                var result = MessageBox.Show(
                    Loc.Get("Settings_ConfirmRestoreVoice"),
                    Loc.Get("Settings_ConfirmRestoreVoice_Title"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // پاک کردن دستورات فعلی
                    _settings.VoiceCommands.Clear();
                    _voiceCommands.Clear();

                    // اضافه کردن دستورات پیش‌فرض برای زبان فعلی
                    var defaultCommands = AppSettings.GetDefaultCommandsForLanguage(_currentVoiceLang);
                    foreach (var command in defaultCommands)
                    {
                        _settings.VoiceCommands.Add(command);
                        _voiceCommands.Add(command);
                    }

                    // فیلتر کردن و به‌روزرسانی نمایش
                    FilterCommands();

                    // علامت‌گذاری تغییرات
                    _hasChanges = true;

                    MessageBox.Show(Loc.Get("Settings_VoiceResetDone"), Loc.Get("Common_Success_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SW_RestoreCommandsError", ex.Message), Loc.Get("Common_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnRestoreDefaultCommands_Click(object sender, RoutedEventArgs e)
        {
            RestoreDefaultVoiceCommands();
        }



        private void chkEnableVoiceCommands_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                _hasChanges = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in chkEnableVoiceCommands_CheckedChanged: {ex.Message}");
            }
        }
        
        private void cmbActionType_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                UpdateKeySelectionButtonVisibility();
                _hasChanges = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in cmbActionType_SelectionChanged: {ex.Message}");
            }
        }
        
        private void UpdateKeySelectionButtonVisibility()
        {
            try
            {
                if (btnSelectKey == null || cmbActionType == null)
                    return;
                    
                var selectedItem = cmbActionType.SelectedItem as ComboBoxItem;
                var actionType = selectedItem?.Tag?.ToString();
                
                // Show button only for SendKeys (now supports both single and combination keys)
                bool shouldShow = actionType == "SendKeys";
                btnSelectKey.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating key selection button visibility: {ex.Message}");
            }
        }
        
        private void btnSelectKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var keySelectionWindow = new KeySelectionWindow()
                {
                    Owner = this
                };
                
                keySelectionWindow.ShowDialog();
                
                if (keySelectionWindow.DialogResult && !string.IsNullOrEmpty(keySelectionWindow.SelectedKeys))
                {
                    if (txtCommandParameter != null)
                    {
                        txtCommandParameter.Text = keySelectionWindow.SelectedKeys;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("SW_SelectKeyError", ex.Message), Loc.Get("Common_Error_Title"), 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void lstVoiceCommands_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var selectedCommand = lstVoiceCommands.SelectedItem as VoiceCommand;
                _selectedCommand = selectedCommand;
                
                // Enable/disable Edit and Delete buttons based on selection
                if (btnEditCommand != null) btnEditCommand.IsEnabled = selectedCommand != null;
                if (btnDeleteCommand != null) btnDeleteCommand.IsEnabled = selectedCommand != null;
                
                if (selectedCommand != null)
                {
                    PopulateCommandEditor(selectedCommand);
                }
                else
                {
                    ClearCommandEditor();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in lstVoiceCommands_SelectionChanged: {ex.Message}");
            }
        }

        // Search functionality
        private void txtSearchCommands_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            try
            {
                FilterCommands();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in search: {ex.Message}");
            }
        }

        private void btnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (txtSearchCommands != null)
                {
                    txtSearchCommands.Text = "";
                    FilterCommands();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing search: {ex.Message}");
            }
        }

        private void FilterCommands()
        {
            try
            {
                var searchText = txtSearchCommands?.Text?.Trim().ToLower() ?? "";
                
                _filteredVoiceCommands.Clear();
                
                var filteredItems = string.IsNullOrEmpty(searchText) 
                    ? _voiceCommands
                    : _voiceCommands.Where(c => 
                        (c.Phrase?.ToLower().Contains(searchText) == true) ||
                        (c.ActionValue?.ToLower().Contains(searchText) == true) ||
                        (c.ActionType.ToString().ToLower().Contains(searchText)));
                
                foreach (var item in filteredItems)
                {
                    _filteredVoiceCommands.Add(item);
                }
                
                UpdateRowNumbers();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error filtering commands: {ex.Message}");
            }
        }

        // Column sorting functionality
        /// <summary>
        /// کلیک روی هدر ستون برای مرتب‌سازی
        /// </summary>
        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is GridViewColumnHeader header && header.Content != null)
                {
                    string columnName = header.Content.ToString();
                    SortByColumn(columnName);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in column sort: {ex.Message}");
            }
        }

        /// <summary>
        /// کلیک روی هدر ستون برای مرتب‌سازی (متد قدیمی برای سازگاری)
        /// </summary>
        private void ColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            GridViewColumnHeader_Click(sender, e);
        }

        private void SortByColumn(string columnName)
        {
            try
            {
                if (_filteredVoiceCommands == null || _filteredVoiceCommands.Count == 0)
                    return;

                if (_currentSortColumn == columnName)
                {
                    _sortAscending = !_sortAscending;
                }
                else
                {
                    _currentSortColumn = columnName;
                    _sortAscending = true;
                }

                var sortedItems = columnName switch
                {
                    "#" => _sortAscending 
                        ? _filteredVoiceCommands.OrderBy(c => c.RowNumber)
                        : _filteredVoiceCommands.OrderByDescending(c => c.RowNumber),
                    "دستور" => _sortAscending 
                        ? _filteredVoiceCommands.OrderBy(c => c.Phrase)
                        : _filteredVoiceCommands.OrderByDescending(c => c.Phrase),
                    "عمل" => _sortAscending 
                        ? _filteredVoiceCommands.OrderBy(c => c.ActionType.ToString())
                        : _filteredVoiceCommands.OrderByDescending(c => c.ActionType.ToString()),
                    "پارامتر" => _sortAscending 
                        ? _filteredVoiceCommands.OrderBy(c => c.ActionValue)
                        : _filteredVoiceCommands.OrderByDescending(c => c.ActionValue),
                    "فعال" => _sortAscending 
                        ? _filteredVoiceCommands.OrderBy(c => c.IsEnabled)
                        : _filteredVoiceCommands.OrderByDescending(c => c.IsEnabled),
                    _ => _filteredVoiceCommands.AsEnumerable()
                };

                var tempList = sortedItems.ToList();
                _filteredVoiceCommands.Clear();
                foreach (var item in tempList)
                {
                    _filteredVoiceCommands.Add(item);
                }
                
                UpdateRowNumbers();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sorting by column: {ex.Message}");
            }
        }

        // Row numbering
        private void UpdateRowNumbers()
        {
            try
            {
                for (int i = 0; i < _filteredVoiceCommands.Count; i++)
                {
                    _filteredVoiceCommands[i].RowNumber = i + 1;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating row numbers: {ex.Message}");
            }
        }

        #endregion

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_hasChanges)
            {
                var result = MessageBox.Show(Loc.Get("Settings_UnsavedConfirm"), 
                    Loc.Get("Common_ConfirmExit_Title"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            base.OnClosing(e);
        }
    }
}
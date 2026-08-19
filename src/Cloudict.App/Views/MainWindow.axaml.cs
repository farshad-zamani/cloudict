using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Cloudict.Abstractions;
using Cloudict.App.Services;
using Cloudict.Services;
using Cloudict.Speech;

namespace Cloudict.App.Views
{
    /// <summary>
    /// The main window.
    ///
    /// <para>Deliberately thin compared with its 3,600-line WPF predecessor. Recognition, timing and
    /// text transfer now live in <see cref="DictationSession"/> and <see cref="GoogleTranslateEngine"/>
    /// in Core; this class only shows state and forwards clicks. That is what makes the same
    /// behaviour available on three operating systems instead of one.</para>
    /// </summary>
    public partial class MainWindow : Window, IDictationOutput
    {
        private readonly GoogleTranslateEngine _engine;
        private readonly DictationSession _session;
        private readonly GlobalShortcuts _shortcuts;

        private AppSettings _settings;
        private VoiceCommandManager _commandManager;
        private bool _closing;

        public MainWindow()
        {
            InitializeComponent();

            WindowSizing.FitToWorkArea(this, 520, 820);
            TxtVersion.Text = AppInfo.DisplayVersion;

            _settings = AppServices.Settings.LoadSettings();

            _engine = new GoogleTranslateEngine(AppServices.Browser, () => _settings);
            _engine.StatusChanged += OnEngineStatus;
            _engine.BrowserOpenChanged += OnBrowserOpenChanged;

            _session = new DictationSession(_engine, AppServices.Platform.TextInjector, () => _settings, this);
            _session.StatusChanged += OnEngineStatus;
            _session.RecognizedTextChanged += OnRecognizedTextChanged;
            _session.CommandExecuted += OnCommandExecuted;

            _shortcuts = new GlobalShortcuts(AppServices.Platform.GlobalHotkeys);

            AppServices.UserMessage += OnUserMessage;
            SingleInstance.CommandReceived += OnInstanceCommand;

            Opened += OnOpened;
            Closing += OnClosing;

            SetStatus(Loc.Get("Main_Ready"));
        }


        #region Lifecycle

        private void OnOpened(object sender, EventArgs e)
        {
            ReloadCommands();
            RegisterShortcuts();
            ReportPlatformLimitations();
        }

        private async void OnClosing(object sender, WindowClosingEventArgs e)
        {
            if (_closing) return;

            // Shutting the browser down takes a moment; let it finish rather than orphaning Chrome.
            e.Cancel = true;
            _closing = true;

            try
            {
                _shortcuts.Dispose();
                await _session.StopAsync();
                await _engine.CloseBrowserAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] shutdown: {ex.Message}");
            }
            finally
            {
                _session.Dispose();
                _engine.Dispose();
                Close();
            }
        }

        /// <summary>
        /// Surfaces anything this platform cannot do, once, at startup. A user whose Wayland session
        /// silently cannot receive typed text should be told so, not left to conclude the app is
        /// broken.
        /// </summary>
        private void ReportPlatformLimitations()
        {
            var caps = AppServices.Capabilities;
            if (caps?.LimitationKeys == null) return;

            foreach (var key in caps.LimitationKeys)
            {
                SetStatus(Loc.Get(key));
                break;   // the first is the most relevant; the rest are in --diagnose
            }
        }

        #endregion

        #region Buttons

        private async void OnHelperBrowserClick(object sender, RoutedEventArgs e)
        {
            BtnHelperBrowser.IsEnabled = false;

            try
            {
                if (_engine.IsBrowserOpen)
                {
                    await _session.StopAsync();
                    await _engine.CloseBrowserAsync();
                }
                else
                {
                    await _engine.OpenBrowserAsync();
                }
            }
            catch (Exception ex)
            {
                SetStatus(Loc.Get("Main_St_OpenBrowserErrorPrefix") + ex.Message);
            }
            finally
            {
                BtnHelperBrowser.IsEnabled = true;
            }
        }

        private async void OnStartClick(object sender, RoutedEventArgs e) => await StartDictationAsync();

        private async void OnStopClick(object sender, RoutedEventArgs e) => await StopDictationAsync();

        private async Task StartDictationAsync()
        {
            BtnStart.IsEnabled = false;

            try
            {
                if (!AppServices.Platform.TextInjector.IsAvailable && _session.IsLiveTransfer)
                {
                    // Typing into other applications is exactly what live transfer needs.
                    var reason = AppServices.Platform.TextInjector.UnavailableReasonKey;
                    if (reason != null) SetStatus(Loc.Get(reason));
                }

                await _session.StartAsync();
            }
            catch (Exception ex)
            {
                SetStatus(Loc.Get("Main_St_MicEnableErrorPrefix") + ex.Message);
            }
            finally
            {
                BtnStart.IsEnabled = true;
            }
        }

        private async Task StopDictationAsync()
        {
            BtnStop.IsEnabled = false;

            try { await _session.StopAsync(); }
            catch (Exception ex) { SetStatus(Loc.Get("Main_St_MicDisableErrorPrefix") + ex.Message); }
            finally { BtnStop.IsEnabled = true; }
        }

        private async void OnQuickTransferClick(object sender, RoutedEventArgs e)
        {
            try { await _session.FlushPendingWordsAsync(); SetStatus(Loc.Get("Main_St_AllTransferred")); }
            catch (Exception ex) { SetStatus(Loc.Get("Main_St_QuickTransferErrorPrefix") + ex.Message); }
        }

        private void OnLiveTransferClick(object sender, RoutedEventArgs e)
        {
            _session.IsLiveTransfer = BtnLiveTransfer.IsChecked == true;
            SetStatus(Loc.Get(_session.IsLiveTransfer ? "Main_St_LiveOn" : "Main_St_LiveOff"));
        }

        private async void OnCopyRecognizedClick(object sender, RoutedEventArgs e) =>
            await CopyAsync(TxtRecognized.Text, "Main_St_RecognizedCopied");

        private async void OnCopyFinalClick(object sender, RoutedEventArgs e) =>
            await CopyAsync(TxtFinal.Text, "Main_St_FinalCopied");

        private void OnClearRecognizedClick(object sender, RoutedEventArgs e)
        {
            TxtRecognized.Text = string.Empty;
            SetStatus(Loc.Get("Main_St_RecognizedCleared"));
        }

        private void OnClearFinalClick(object sender, RoutedEventArgs e)
        {
            TxtFinal.Text = string.Empty;
            SetStatus(Loc.Get("Main_St_FinalCleared"));
        }

        private async Task CopyAsync(string text, string statusKey)
        {
            try
            {
                var clipboard = GetTopLevel(this)?.Clipboard;
                if (clipboard == null || string.IsNullOrEmpty(text)) return;

                await clipboard.SetTextAsync(text);
                SetStatus(Loc.Get(statusKey));
            }
            catch (Exception ex)
            {
                SetStatus(Loc.Get("Main_St_CopyErrorPrefix") + ex.Message);
            }
        }

        private async void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = new SettingsWindow(_commandManager);
                await window.ShowDialog(this);

                if (window.Saved)
                {
                    _settings = AppServices.Settings.LoadSettings();
                    ReloadCommands();
                    RegisterShortcuts();
                }
            }
            catch (Exception ex)
            {
                SetStatus(Loc.Get("Main_OpenSettingsError", ex.Message));
            }
        }

        private void OnOpenUrlClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Control control || control.Tag is not string url) return;

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetStatus(Loc.Get("Main_OpenLinkError", ex.Message));
            }
        }

        #endregion

        #region Engine events

        private void OnEngineStatus(object sender, EngineStatusEventArgs e) =>
            Dispatcher.UIThread.Post(() => SetStatus(
                e.Args.Length > 0 ? Loc.Get(e.MessageKey, e.Args) : Loc.Get(e.MessageKey)));

        private void OnRecognizedTextChanged(object sender, string text) =>
            Dispatcher.UIThread.Post(() => TxtRecognized.Text = text);

        private void OnCommandExecuted(object sender, string phrase) =>
            Dispatcher.UIThread.Post(() => SetStatus(Loc.Get("Main_St_CommandExecuted_Fmt", phrase)));

        private void OnBrowserOpenChanged(object sender, bool open) =>
            Dispatcher.UIThread.Post(() =>
            {
                // Swap the vector geometry rather than an emoji glyph: a Linux box without an
                // emoji font renders those as empty squares.
                var iconKey = open ? "IconClose" : "IconGlobe";
                if (Avalonia.Application.Current?.Resources.TryGetResource(iconKey, null, out var geometry) == true
                    && geometry is Avalonia.Media.Geometry shape)
                    IconBrowser.Data = shape;

                TxtBrowserLabel.Text = Loc.Get(open ? "Main_CloseHelperBrowser" : "Main_OpenHelperBrowser");
            });

        private void OnUserMessage(object sender, UserMessageEventArgs e) =>
            Dispatcher.UIThread.Post(() => SetStatus(
                e.Args.Length > 0 ? Loc.Get(e.MessageKey, e.Args) : Loc.Get(e.MessageKey)));

        private void SetStatus(string text) => TxtStatus.Text = text;

        #endregion

        #region Shortcuts, commands and instance verbs

        private void RegisterShortcuts()
        {
            _shortcuts.Apply(
                _settings,
                onToggle: () => Dispatcher.UIThread.Post(async () => await ToggleDictationAsync()),
                onStop: () => Dispatcher.UIThread.Post(async () => await StopDictationAsync()));
        }

        private async Task ToggleDictationAsync()
        {
            if (_session.IsRunning) await StopDictationAsync();
            else await StartDictationAsync();
        }

        private void ReloadCommands()
        {
            try
            {
                _commandManager = new VoiceCommandManager(_settings);
                _commandManager.LoadCommands();

                _session.UpdateCommands(new VoiceCommandProcessor(
                    _commandManager.ActiveCommands,
                    AppServices.Platform.TextInjector,
                    AppServices.Platform.KeyboardLayout,
                    _settings.CaseSensitiveCommands));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] could not load voice commands: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles a verb sent by a second launch — the route a Wayland user's desktop shortcut
        /// takes, since no application can grab a global key there.
        /// </summary>
        private void OnInstanceCommand(object sender, InstanceCommand command) =>
            Dispatcher.UIThread.Post(async () =>
            {
                switch (command)
                {
                    case InstanceCommand.Toggle: await ToggleDictationAsync(); break;
                    case InstanceCommand.Start: await StartDictationAsync(); break;
                    case InstanceCommand.Stop: await StopDictationAsync(); break;
                    case InstanceCommand.Show:
                        Show();
                        WindowState = WindowState.Normal;
                        Activate();
                        break;
                }
            });

        #endregion

        #region IDictationOutput

        /// <summary>
        /// The session writes buffered words here. Both members are touched from background loops,
        /// so each hops to the UI thread; Avalonia controls may only be read or written there.
        /// </summary>
        string IDictationOutput.FinalText
        {
            get => Dispatcher.UIThread.Invoke(() => TxtFinal.Text ?? string.Empty);
            set => Dispatcher.UIThread.Post(() => TxtFinal.Text = value);
        }

        int IDictationOutput.CaretIndex
        {
            get => Dispatcher.UIThread.Invoke(() => TxtFinal.CaretIndex);
            set => Dispatcher.UIThread.Post(() => TxtFinal.CaretIndex = value);
        }

        void IDictationOutput.FocusFinalText() => Dispatcher.UIThread.Post(() => TxtFinal.Focus());

        #endregion
    }
}

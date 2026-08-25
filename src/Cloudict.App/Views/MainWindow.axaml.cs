using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
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

        private StatusIndicatorWindow _indicator;
        private TrayIcon _trayIcon;

        private CancellationTokenSource _micWatch;
        private bool _micReportedLive;
        private bool _micLossAnnounced;

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

            // A voice command fires while the user is working in another application, so the
            // feedback has to come from the desktop rather than from a window they cannot see.
            _session.CommandExecuted += OnCommandNotification;

            _session.AutoStopped += OnSessionAutoStopped;

            Opened += OnOpened;
            Closing += OnClosing;
            PropertyChanged += OnWindowPropertyChanged;

            SetStatus(Loc.Get("Main_Ready"));
        }


        #region Lifecycle

        private void OnOpened(object sender, EventArgs e)
        {
            ReloadCommands();
            RegisterShortcuts();
            SetUpTray();
            ShowIndicator();
            StartMicrophoneWatch();
            ReportPlatformLimitations();
            RestoreLiveTransfer();
            OpenBrowserOnStartup();
        }

        /// <summary>Brings back the live-transfer choice from the last run.</summary>
        private void RestoreLiveTransfer()
        {
            _session.IsLiveTransfer = _settings?.LiveTransferEnabled == true;
            BtnLiveTransfer.IsChecked = _session.IsLiveTransfer;
        }

        /// <summary>
        /// Opens the helper browser as the window comes up, unless the user has turned that off.
        ///
        /// <para>Deliberately not awaited: the browser takes several seconds to start and the window
        /// has to be usable in the meantime. The button is disabled while it happens, and the same
        /// lock that serialises a manual open covers this one, so pressing start during the launch
        /// waits for it rather than racing it.</para>
        /// </summary>
        private void OpenBrowserOnStartup()
        {
            if (_settings?.OpenBrowserOnStartup != true) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    // A moment's grace before launching Chrome. Starting a browser in the same
                    // breath as the window is what made this the least reliable part of a cold
                    // start — most visibly just after a reboot, when Cloudict can be up before the
                    // network is, and the page then loads as an error.
                    await Task.Delay(TimeSpan.FromMilliseconds(1200));

                    await Dispatcher.UIThread.InvokeAsync(() => BtnHelperBrowser.IsEnabled = false);
                    await _engine.OpenBrowserAsync();
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() =>
                        SetStatus(Loc.Get("Main_St_OpenBrowserErrorPrefix") + ex.Message));
                }
                finally
                {
                    Dispatcher.UIThread.Post(() => BtnHelperBrowser.IsEnabled = true);
                }
            });
        }

        /// <summary>
        /// Keeps the corner badge honest by asking the operating system whether the microphone is
        /// actually being captured, rather than only whether Cloudict last pressed start.
        ///
        /// <para>The two are not the same. Google Translate stops listening on its own — a network
        /// hiccup, its own recognition error, or simply too long a silence — and Cloudict is not told.
        /// The badge then sat there green while nothing was being heard, which is worse than no badge
        /// at all: the user carries on speaking into a microphone that is off.</para>
        ///
        /// <para>Green means <em>both</em> that dictation is running and that the system reports the
        /// microphone in use, so another application holding the microphone never turns it green on
        /// its own. Where the platform cannot tell (Linux, macOS) it falls back to the session's own
        /// state, which is what it did before.</para>
        /// </summary>
        private void StartMicrophoneWatch()
        {
            _micWatch = new CancellationTokenSource();
            var token = _micWatch.Token;
            var monitor = AppServices.Platform.MicrophoneMonitor;

            // A background loop rather than a UI timer: the WASAPI query is a blocking COM call and
            // has no business running on the thread that draws the window.
            _ = Task.Run(async () =>
            {
                var missed = 0;
                var seenLive = false;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), token);

                        bool live;

                        if (!_session.IsRunning)
                        {
                            missed = 0;
                            seenLive = false;
                            live = false;
                        }
                        else if (!monitor.IsSupported || _session.IsResetting)
                        {
                            // A reset switches the microphone off for about a second by design;
                            // reporting that would make the badge flicker on every pause.
                            missed = 0;
                            live = true;
                        }
                        else if (monitor.IsMicrophoneInUse())
                        {
                            missed = 0;
                            seenLive = true;
                            live = true;
                        }
                        else
                        {
                            // Chrome releases the capture stream a moment after the page stops, so
                            // one quiet sample proves nothing. Three in a row does — and before the
                            // microphone has ever come on, allow longer, because a cold start can
                            // take a few seconds to open the capture stream.
                            missed++;
                            live = missed < (seenLive ? 3 : 8);
                        }

                        Dispatcher.UIThread.Post(() => ApplyMicrophoneState(live));
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MainWindow] microphone watch: {ex.Message}");
                    }
                }
            }, token);
        }

        /// <summary>
        /// Paints the badge and, when the microphone has genuinely gone under a running session,
        /// winds that session down.
        ///
        /// <para>Stopping matters as much as the badge. A session left "running" over a microphone
        /// the page has switched off is a lie the rest of the app then acts on — most visibly, the
        /// start shortcut reads it as "stop" and the user has to press twice.</para>
        /// </summary>
        private async void ApplyMicrophoneState(bool live)
        {
            _indicator?.SetActive(live);

            if (live == _micReportedLive) return;
            _micReportedLive = live;

            if (live || !_session.IsRunning) return;

            SetStatus(Loc.Get("Main_St_MicLost"));

            // Google Translate drops the microphone between phrases often enough that announcing
            // every one would be a stream of pop-ups, so this is said once per session.
            if (!_micLossAnnounced)
            {
                _micLossAnnounced = true;
                Notify(Loc.Get("Notify_MicLost"));
            }

            try
            {
                await StopDictationAsync(notify: false);
                SetStatus(Loc.Get("Main_St_MicLost"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] could not wind down a dead session: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows the corner badge that reports whether the microphone is live. Cloudict's own window
        /// is normally covered by the helper browser and whatever the user is dictating into, so
        /// without this there is no way to tell at a glance.
        /// </summary>
        private void ShowIndicator()
        {
            try
            {
                if (_settings?.ShowStatusIndicator == false)
                {
                    _indicator?.Hide();
                    return;
                }

                _indicator ??= new StatusIndicatorWindow();
                _indicator.Show();
                _indicator.SetActive(_session.IsRunning);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] status indicator unavailable: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets up the tray presence.
        ///
        /// <para>Windows supplies its own through the platform layer, because a balloon notification
        /// there is only shown on behalf of a registered tray icon - so whatever notifies must also
        /// own the icon, and creating a second one here is what produced the stray blue information
        /// icon in 2.x. Elsewhere the platform returns null and Avalonia's own tray icon is used.</para>
        /// </summary>
        private void SetUpTray()
        {
            var native = AppServices.Platform.TrayPresence;
            if (native != null)
            {
                native.Activated += (_, __) => Dispatcher.UIThread.Post(RestoreFromTray);
                native.SetTooltip("Cloudict");
                return;
            }

            try
            {
                var show = new NativeMenuItem(Loc.Get("Tray_Show"));
                show.Click += (_, __) => Dispatcher.UIThread.Post(RestoreFromTray);

                var quit = new NativeMenuItem(Loc.Get("Tray_Quit"));
                quit.Click += (_, __) => Dispatcher.UIThread.Post(RequestExit);

                _trayIcon = new TrayIcon
                {
                    ToolTipText = "Cloudict",
                    IsVisible = true,
                    Menu = new NativeMenu { Items = { show, quit } }
                };

                _trayIcon.Clicked += (_, __) => Dispatcher.UIThread.Post(RestoreFromTray);

                using var stream = AssetLoader.Open(new Uri("avares://Cloudict/Assets/app-icon.ico"));
                _trayIcon.Icon = new WindowIcon(stream);
            }
            catch (Exception ex)
            {
                // Optional: GNOME needs an extension to show one at all, and may simply not.
                Debug.WriteLine($"[MainWindow] tray icon unavailable: {ex.Message}");
            }
        }

        /// <summary>Sends the window to the tray rather than the taskbar, when the user asked for that.</summary>
        private void OnWindowPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != WindowStateProperty) return;
            if (WindowState != WindowState.Minimized) return;
            if (_settings?.MinimizeToTray != true) return;

            Hide();
            ShowInTaskbar = false;
        }

        private void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void RequestExit() => Close();

        private async void OnClosing(object sender, WindowClosingEventArgs e)
        {
            if (_closing) return;

            // Shutting the browser down takes a moment; let it finish rather than orphaning Chrome.
            e.Cancel = true;
            _closing = true;

            try
            {
                try { _micWatch?.Cancel(); } catch (Exception ex) { Debug.WriteLine(ex.Message); }

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
                try { _indicator?.Close(); } catch (Exception ex) { Debug.WriteLine(ex.Message); }
                try { _trayIcon?.Dispose(); } catch (Exception ex) { Debug.WriteLine(ex.Message); }
                try { _micWatch?.Dispose(); } catch (Exception ex) { Debug.WriteLine(ex.Message); }

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

        /// <summary>
        /// Starts listening. Only ever starts — pressing it twice does not stop anything.
        ///
        /// <para>When the session still believes it is running but the microphone is not actually
        /// live, this winds the dead session down first. Without that, <c>StartAsync</c> sees
        /// <c>IsRunning</c>, returns "already started", and the microphone stays off — which is what
        /// made the shortcut need a second press after Google Translate had switched itself off.</para>
        /// </summary>
        private async Task StartDictationAsync()
        {
            if (_session.IsRunning && !_micReportedLive)
                await StopDictationAsync(notify: false);

            if (_session.IsRunning)
            {
                // Genuinely already listening: say so rather than doing nothing silently.
                SetStatus(Loc.Get("Main_St_AlreadyListening"));
                return;
            }

            BtnStart.IsEnabled = false;
            _micLossAnnounced = false;

            try
            {
                if (!AppServices.Platform.TextInjector.IsAvailable && _session.IsLiveTransfer)
                {
                    // Typing into other applications is exactly what live transfer needs.
                    var reason = AppServices.Platform.TextInjector.UnavailableReasonKey;
                    if (reason != null) SetStatus(Loc.Get(reason));
                }

                // Start and stop are usually driven by the shortcut while another application has
                // focus, so this is exactly the moment the user cannot see Cloudict's own status
                // line and the desktop has to say it instead.
                if (await _session.StartAsync()) Notify(Loc.Get("Notify_Started"));
            }
            catch (Exception ex)
            {
                SetStatus(Loc.Get("Main_St_MicEnableErrorPrefix") + ex.Message);
            }
            finally
            {
                BtnStart.IsEnabled = true;
                ReflectRunningState();
            }
        }

        /// <param name="notify">
        /// False when the caller has its own, more specific thing to say — announcing a plain
        /// "dictation stopped" alongside it would be two pop-ups for one event.
        /// </param>
        private async Task StopDictationAsync(bool notify = true)
        {
            BtnStop.IsEnabled = false;

            var wasRunning = _session.IsRunning;

            try
            {
                await _session.StopAsync();
                if (wasRunning && notify) Notify(Loc.Get("Notify_Stopped"));
            }
            catch (Exception ex) { SetStatus(Loc.Get("Main_St_MicDisableErrorPrefix") + ex.Message); }
            finally { BtnStop.IsEnabled = true; ReflectRunningState(); }
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

            // Saved as soon as it is flipped, not when Settings is next opened: this is a main-window
            // control and the user has no reason to expect a separate save step for it.
            if (_settings == null) return;

            try
            {
                _settings.LiveTransferEnabled = _session.IsLiveTransfer;
                AppServices.Settings.SaveSettings(_settings);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] could not remember the live-transfer choice: {ex.Message}");
            }
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
                    ShowIndicator();
                    RestoreLiveTransfer();
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

                // Colour carries the state as well as the icon. Closed is the accent red — something
                // still to be done, since nothing can be dictated until the browser is up. Open is
                // the calm panel colour with the cross and its label in red, so the button reads as
                // "this is running, and this is how you close it" at a glance.
                BtnHelperBrowser.Classes.Set("accent", !open);
                BtnHelperBrowser.Classes.Set("subtle", open);

                var foreground = open
                    ? Application.Current?.FindResource("AccentHoverBrush") as IBrush
                    : Brushes.White;

                if (foreground != null)
                {
                    IconBrowser.Fill = foreground;
                    TxtBrowserLabel.Foreground = foreground;
                }
            });

        private void OnUserMessage(object sender, UserMessageEventArgs e) =>
            Dispatcher.UIThread.Post(() => SetStatus(
                e.Args.Length > 0 ? Loc.Get(e.MessageKey, e.Args) : Loc.Get(e.MessageKey)));

        private void SetStatus(string text) => TxtStatus.Text = text;

        /// <summary>
        /// Keeps the corner badge and the tray tooltip in step with the dictation state.
        ///
        /// <para>This paints the badge straight away on a button press or shortcut, so the feedback
        /// is immediate; the microphone watch then keeps it truthful from one second onwards.</para>
        /// </summary>
        private void ReflectRunningState()
        {
            var running = _session.IsRunning;

            _indicator?.SetActive(running);
            _micReportedLive = running;

            var tooltip = "Cloudict - " + Loc.Get(running ? "Indicator_Listening" : "Indicator_Idle");
            AppServices.Platform.TrayPresence?.SetTooltip(tooltip);
            if (_trayIcon != null) _trayIcon.ToolTipText = tooltip;
        }

        /// <summary>
        /// Shows a desktop notification, the only kind visible while the user is dictating into
        /// another application. Never fatal: a system with notifications switched off must still
        /// dictate.
        /// </summary>
        private void Notify(string message)
        {
            try
            {
                AppServices.Platform.Notifier?.Show("Cloudict", message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] notification failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Google Translate's microphone died and would not restart. The session cannot tear itself
        /// down from inside its own loops, so it asks here.
        /// </summary>
        private void OnSessionAutoStopped(object sender, EventArgs e) =>
            Dispatcher.UIThread.Post(async () =>
            {
                if (!_session.IsRunning) return;

                var alreadyTold = _micLossAnnounced;
                _micLossAnnounced = true;

                await StopDictationAsync(notify: false);

                SetStatus(Loc.Get("Main_St_MicLost"));
                if (!alreadyTold) Notify(Loc.Get("Notify_MicLost"));
            });

        /// <summary>
        /// Announces a voice command through the desktop's own notification system, which is the
        /// only kind visible while the user is working in another application.
        /// </summary>
        private void OnCommandNotification(object sender, string phrase) =>
            Notify(Loc.Get("Main_St_CommandExecuted_Fmt", phrase));

        #endregion

        #region Shortcuts, commands and instance verbs

        private void RegisterShortcuts()
        {
            _shortcuts.Apply(
                _settings,
                onStart: () => Dispatcher.UIThread.Post(async () => await StartDictationAsync()),
                onStop: () => Dispatcher.UIThread.Post(async () => await StopDictationAsync()));
        }

        /// <summary>
        /// The <c>--toggle</c> command-line verb, which exists for desktop environments that can
        /// bind a shortcut to a command but cannot hand Cloudict a global hotkey — Wayland, mainly.
        /// The keyboard shortcuts themselves do not toggle: start starts and stop stops.
        /// </summary>
        private async Task ToggleDictationAsync()
        {
            if (_session.IsRunning && _micReportedLive)
            {
                await StopDictationAsync();
                return;
            }

            await StartDictationAsync();
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

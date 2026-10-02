using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Cloudict.App.Views
{
    /// <summary>
    /// The small badge in the corner of the screen showing whether the microphone is live.
    ///
    /// <para>It exists because Cloudict's own window is, by design, not the one you are looking at:
    /// the helper browser and whatever you are dictating into sit on top of it. Without this there
    /// is no way to tell at a glance whether speaking will produce text.</para>
    ///
    /// <para>State is carried by both colour and shape — teal microphone when listening, muted red
    /// with a slash through it when not — because colour alone is not readable for everyone.</para>
    /// </summary>
    public partial class StatusIndicatorWindow : Window
    {
        private static readonly Color ActiveColor = Color.Parse("#3E9080");
        private static readonly Color IdleColor = Color.Parse("#C2454E");

        // Null until the first paint, so the opening call is never mistaken for "no change".
        private bool? _isActive;
        private bool _listeningToSystem;

        /// <summary>
        /// Switches the badge between the microphone glyph and the speaker one.
        ///
        /// <para>Which source is being listened to matters as much as whether anything is: the two
        /// modes are exclusive, and someone who has left system audio on and starts talking would
        /// otherwise have no way of knowing why nothing is being typed.</para>
        /// </summary>
        public void SetListeningToSystemAudio(bool system)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => SetListeningToSystemAudio(system));
                return;
            }

            if (_listeningToSystem == system) return;
            _listeningToSystem = system;

            SystemGlyph.IsVisible = system;
            MicGlyph.IsVisible = !system;

            // Repaint the tooltip through the normal path.
            var state = _isActive;
            _isActive = null;
            SetActive(state == true);
        }

        public StatusIndicatorWindow()
        {
            InitializeComponent();

            // Never take focus from the application being dictated into — not when shown, and not
            // when clicked. See MakeNonActivating.
            ShowActivated = false;

            Opened += (_, __) =>
            {
                MakeNonActivating();
                PositionInCorner();
            };

            if (ClickToToggle)
                Root.Cursor = new Cursor(StandardCursorType.Hand);

            SetActive(false);
        }

        /// <summary>
        /// Raised when the badge is clicked. The owner starts or stops dictation, exactly as the
        /// shortcuts do.
        /// </summary>
        public event EventHandler Clicked;

        /// <summary>
        /// Clicking is offered on Windows only, for now.
        ///
        /// <para>The click is only useful if it leaves keyboard focus where it was — in the
        /// document the user is dictating into. On Windows that is guaranteed by the window style
        /// below, and verified. On Linux and macOS, clicking a window normally hands it the focus;
        /// dictation would then type into the badge, which takes no text, and the words would be
        /// lost. Until that can be verified on those systems the badge stays display-only there.</para>
        /// </summary>
        private static bool ClickToToggle => OperatingSystem.IsWindows();

        private bool _pressed;
        private DateTime _lastClick = DateTime.MinValue;

        private void RaiseClicked()
        {
            // A double-click would otherwise start and immediately stop.
            if (DateTime.UtcNow - _lastClick < TimeSpan.FromMilliseconds(600)) return;
            _lastClick = DateTime.UtcNow;

            Clicked?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Makes the badge a window that is never activated and never takes the keyboard focus, so
        /// clicking it leaves the focus in the document being dictated into.
        ///
        /// <para>Two things are needed, and the second is the one that mattered. The
        /// <c>WS_EX_NOACTIVATE</c> style stops Windows activating the badge. But Avalonia, on every
        /// mouse button press, calls <c>SetFocus</c> on the window it arrives in — and a badge that
        /// takes the focus has taken it from the user's application. Measured with the style alone:
        /// every second click toggled nothing and left the focus on the desktop. Avalonia's own popup
        /// windows switch that behaviour off; this version of Avalonia gives a plain window no way to,
        /// so the badge's window procedure answers the activation question itself and handles the
        /// button presses before Avalonia sees them. Everything else — hover, tooltip, cursor,
        /// painting — still goes to Avalonia unchanged.</para>
        /// </summary>
        private void MakeNonActivating()
        {
            if (!OperatingSystem.IsWindows()) return;

            try
            {
                var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (handle == IntPtr.Zero) return;

                var style = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64();
                SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));

                if (ClickToToggle && _originalWndProc == IntPtr.Zero)
                {
                    // Held in a field for the window's lifetime: the native side keeps only a
                    // pointer, and a collected delegate would crash on the next message.
                    _wndProc = BadgeWndProc;
                    _originalWndProc = SetWindowLongPtr(handle, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProc));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StatusIndicator] could not make the badge non-activating: {ex.Message}");
            }
        }

        private IntPtr BadgeWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WM_MOUSEACTIVATE:
                    return (IntPtr)MA_NOACTIVATE;

                case WM_LBUTTONDOWN:
                    _pressed = true;
                    return IntPtr.Zero;

                case WM_LBUTTONUP:
                    if (_pressed)
                    {
                        _pressed = false;
                        Dispatcher.UIThread.Post(RaiseClicked);
                    }
                    return IntPtr.Zero;

                // Swallowed so they cannot reach Avalonia's focus-taking path either.
                case WM_LBUTTONDBLCLK:
                case WM_RBUTTONDOWN:
                case WM_RBUTTONUP:
                case WM_MBUTTONDOWN:
                case WM_MBUTTONUP:
                    return IntPtr.Zero;
            }

            return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private WndProcDelegate _wndProc;
        private IntPtr _originalWndProc;

        private const int GWL_EXSTYLE = -20;
        private const int GWLP_WNDPROC = -4;
        private const long WS_EX_NOACTIVATE = 0x08000000;
        private const long WS_EX_TOOLWINDOW = 0x00000080;
        private const uint WM_MOUSEACTIVATE = 0x0021;
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_LBUTTONDBLCLK = 0x0203;
        private const uint WM_RBUTTONDOWN = 0x0204;
        private const uint WM_RBUTTONUP = 0x0205;
        private const uint WM_MBUTTONDOWN = 0x0207;
        private const uint WM_MBUTTONUP = 0x0208;
        private const int MA_NOACTIVATE = 3;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        /// <summary>Hover text, showing the current state in the user's language.</summary>
        public static readonly StyledProperty<string> StatusTextProperty =
            AvaloniaProperty.Register<StatusIndicatorWindow, string>(nameof(StatusText));

        public string StatusText
        {
            get => GetValue(StatusTextProperty);
            set => SetValue(StatusTextProperty, value);
        }

        /// <summary>Switches the badge between listening and idle.</summary>
        public void SetActive(bool active)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => SetActive(active));
                return;
            }

            // The state is polled roughly once a second; repainting only on a real change keeps
            // that from becoming a stream of pointless work and log noise.
            if (_isActive == active) return;

            _isActive = active;

            var color = active ? ActiveColor : IdleColor;

            Disc.Background = new SolidColorBrush(color);
            Halo.Fill = BuildHalo(color);
            MutedSlash.IsVisible = !active;

            StatusText = Loc.Get(_listeningToSystem
                ? (active ? "Indicator_ListeningSystem" : "Indicator_IdleSystem")
                : (active ? "Indicator_Listening" : "Indicator_Idle"))
                + (ClickToToggle ? Environment.NewLine + Loc.Get(active ? "Indicator_ClickToStop" : "Indicator_ClickToStart") : string.Empty);
        }

        /// <summary>
        /// A soft glow in the state colour, fading to transparent, so the badge stays legible over
        /// any wallpaper or window behind it.
        /// </summary>
        private static IBrush BuildHalo(Color color) => new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(color, 0.35),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1.0)
            }
        };

        /// <summary>
        /// Places the badge just inside the bottom trailing corner of the working area, so it sits
        /// clear of the taskbar or dock rather than under it.
        /// </summary>
        private void PositionInCorner()
        {
            try
            {
                var screen = Screens?.Primary ?? Screens?.ScreenFromWindow(this);
                if (screen == null) return;

                var scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
                var area = screen.WorkingArea;

                const int margin = 16;
                var x = area.X + area.Width - (int)(Width * scale) - (int)(margin * scale);
                var y = area.Y + area.Height - (int)(Height * scale) - (int)(margin * scale);

                Position = new PixelPoint(x, y);
            }
            catch (Exception ex)
            {
                // A badge in the wrong place is far better than a crash on an unusual display setup.
                Debug.WriteLine($"[StatusIndicator] could not position the badge: {ex.Message}");
            }
        }
    }
}

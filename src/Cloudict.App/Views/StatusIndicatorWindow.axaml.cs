using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
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

            Opened += (_, __) => PositionInCorner();
            SetActive(false);
        }

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
                : (active ? "Indicator_Listening" : "Indicator_Idle"));
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

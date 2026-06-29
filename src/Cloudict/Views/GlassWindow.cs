using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace Cloudict
{
    /// <summary>
    /// Base window for the app's frameless "glass" chrome: a per-pixel-transparent window
    /// with a dark translucent background, rounded corners and a custom title bar (defined
    /// by the GlassWindowStyle in Themes/Window.xaml). Windows opt in by using
    /// <c>&lt;local:GlassWindow ...&gt;</c> as their root element.
    ///
    /// The style is assigned explicitly in the constructor because an implicit style keyed
    /// on GlassWindow would not apply to subclasses (MainWindow, SettingsWindow, …).
    /// </summary>
    public class GlassWindow : Window
    {
        public GlassWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;

            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 42,
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });

            if (Application.Current != null &&
                Application.Current.TryFindResource("GlassWindowStyle") is Style style)
            {
                Style = style;
            }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (GetTemplateChild("PART_MinimizeButton") is Button minimize)
                minimize.Click += (_, __) => WindowState = WindowState.Minimized;

            if (GetTemplateChild("PART_MaximizeButton") is Button maximize)
                maximize.Click += (_, __) =>
                    WindowState = WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;

            if (GetTemplateChild("PART_CloseButton") is Button close)
                close.Click += (_, __) => Close();
        }
    }
}

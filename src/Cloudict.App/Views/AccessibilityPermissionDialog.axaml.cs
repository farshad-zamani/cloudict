using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Cloudict.Abstractions;

namespace Cloudict.App.Views
{
    /// <summary>
    /// Explains, and helps grant, the macOS Accessibility permission that typing into other
    /// applications needs. Closes by itself once macOS reports the permission granted.
    /// </summary>
    public partial class AccessibilityPermissionDialog : Window
    {
        private readonly ITextInjector _injector;
        private readonly DispatcherTimer _poll;

        /// <summary>True when the dialog closed because the permission is now on.</summary>
        public bool Granted { get; private set; }

        // Required by the XAML loader; the dialog is always created with an injector in practice.
        public AccessibilityPermissionDialog() : this(null) { }

        public AccessibilityPermissionDialog(ITextInjector injector)
        {
            InitializeComponent();
            _injector = injector;

            var version = Environment.OSVersion.Version;
            TxtVersion.Text = Loc.Get("Access_YourMac_Fmt", $"{version.Major}.{version.Minor}");

            // System Settings and its Privacy & Security page arrived with macOS 13 Ventura; before
            // that it was System Preferences, Security & Privacy, a Privacy tab and a padlock. The
            // steps follow whichever this Mac has.
            TxtSteps.Text = Loc.Get(OperatingSystem.IsMacOSVersionAtLeast(13) ? "Access_Steps_Ventura" : "Access_Steps_Legacy");

            // Watches for the permission while the window is open, so the user does not have to
            // come back and press anything once the switch is on.
            _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _poll.Tick += (_, _) => { if (IsGranted()) CloseGranted(); };
            Opened += (_, _) => _poll.Start();
            Closed += (_, _) => _poll.Stop();
        }

        private bool IsGranted()
        {
            if (_injector == null) return false;

            try
            {
                _injector.Refresh();
                return _injector.IsAvailable;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AccessibilityPermissionDialog] check: {ex.Message}");
                return false;
            }
        }

        private void CloseGranted()
        {
            Granted = true;
            Close();
        }

        private void OnOpenClick(object sender, RoutedEventArgs e) => _injector?.RequestPermission();

        private void OnCheckClick(object sender, RoutedEventArgs e)
        {
            if (IsGranted())
            {
                CloseGranted();
                return;
            }

            TxtResult.Text = Loc.Get("Access_NotYet");
            TxtResult.IsVisible = true;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}

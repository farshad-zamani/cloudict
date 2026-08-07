using System;
using System.Windows;

namespace Cloudict
{
    /// <summary>
    /// Keeps windows inside the screen they open on.
    ///
    /// The XAML sizes are what the app *would like*; on a laptop they often exceed the display —
    /// a 1366×768 panel only leaves about 728 usable pixels once the taskbar is subtracted, so a
    /// window asking for 950 simply had its bottom (status bar, action buttons) pushed off-screen.
    /// Every window therefore asks for its preferred size through here and gets the largest size
    /// that actually fits.
    /// </summary>
    public static class WindowSizing
    {
        /// <summary>
        /// Sizes <paramref name="window"/> to its preferred dimensions, shrunk as needed to leave
        /// <paramref name="margin"/> device-independent pixels of breathing room inside the work
        /// area (the desktop minus the taskbar). The window's own MinWidth/MinHeight are respected.
        /// </summary>
        public static void FitToWorkArea(Window window, double preferredWidth, double preferredHeight, double margin = 40)
        {
            var work = SystemParameters.WorkArea;

            window.Width = Math.Min(preferredWidth, Math.Max(window.MinWidth, work.Width - margin));
            window.Height = Math.Min(preferredHeight, Math.Max(window.MinHeight, work.Height - margin));
        }
    }
}

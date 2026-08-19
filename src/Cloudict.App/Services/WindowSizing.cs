using System;
using System.Diagnostics;
using Avalonia.Controls;

namespace Cloudict.App.Services
{
    /// <summary>
    /// Keeps windows inside the screen they open on.
    ///
    /// <para>The XAML sizes say what a window would like; on a laptop they often exceed the display.
    /// A 1366×768 panel leaves roughly 728 usable pixels once the taskbar is gone, so a window
    /// asking for 820 simply had its bottom — status bar, action buttons — pushed off-screen. This
    /// matters more now than it did on Windows alone: Linux and macOS run on an even wider range of
    /// panel sizes and scaling factors.</para>
    /// </summary>
    internal static class WindowSizing
    {
        /// <summary>
        /// Sizes <paramref name="window"/> to its preferred dimensions, shrunk as needed to leave
        /// <paramref name="margin"/> logical pixels of room inside the screen's working area.
        /// </summary>
        public static void FitToWorkArea(Window window, double preferredWidth, double preferredHeight, double margin = 40)
        {
            try
            {
                var screen = window.Screens?.ScreenFromWindow(window) ?? window.Screens?.Primary;
                if (screen == null) return;

                // WorkingArea is in physical pixels; window sizes are logical. Dividing by the
                // screen's scaling is what makes this correct on a HiDPI panel rather than
                // shrinking the window to a quarter of the screen.
                var scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
                var availableWidth = screen.WorkingArea.Width / scale;
                var availableHeight = screen.WorkingArea.Height / scale;

                window.Width = Math.Min(preferredWidth, Math.Max(window.MinWidth, availableWidth - margin));
                window.Height = Math.Min(preferredHeight, Math.Max(window.MinHeight, availableHeight - margin));
            }
            catch (Exception ex)
            {
                // Never let sizing stop a window from opening.
                Debug.WriteLine($"[WindowSizing] could not fit window: {ex.Message}");
            }
        }
    }
}

using System;
using System.Diagnostics;

namespace Cloudict.Platform.Linux
{
    /// <summary>Which display server the current login session is running.</summary>
    internal enum LinuxDisplayServer
    {
        /// <summary>No graphical session was detected at all (a console login, or a service).</summary>
        None,

        /// <summary>A real X11 session, where XTEST can drive every window.</summary>
        X11,

        /// <summary>
        /// A Wayland session. XTEST may still appear to work through XWayland, but it only reaches
        /// X11 clients — native Wayland windows never see the events.
        /// </summary>
        Wayland
    }

    /// <summary>
    /// Works out what kind of graphical session Cloudict is running in, which decides how — and
    /// whether — keystrokes can be injected.
    ///
    /// <para>This distinction matters more on Linux than anywhere else. Wayland deliberately offers
    /// no way for one application to synthesise input into another, so an app that assumes XTEST
    /// will work ends up silently typing into nothing on most modern desktops. Detecting the session
    /// up front is what lets Cloudict pick the uinput route instead, and explain itself when neither
    /// is available.</para>
    /// </summary>
    internal static class LinuxSession
    {
        /// <summary>
        /// Detects the session type. <c>XDG_SESSION_TYPE</c> is authoritative when set; otherwise the
        /// presence of <c>WAYLAND_DISPLAY</c> or <c>DISPLAY</c> is used, in that order — a Wayland
        /// session usually also exports <c>DISPLAY</c> for XWayland, so Wayland must be checked first.
        /// </summary>
        public static LinuxDisplayServer Detect()
        {
            var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            if (!string.IsNullOrWhiteSpace(sessionType))
            {
                switch (sessionType.Trim().ToLowerInvariant())
                {
                    case "wayland": return LinuxDisplayServer.Wayland;
                    case "x11": return LinuxDisplayServer.X11;
                }
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                return LinuxDisplayServer.Wayland;

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
                return LinuxDisplayServer.X11;

            return LinuxDisplayServer.None;
        }

        /// <summary>
        /// True when an X server can actually be reached, regardless of what the environment claims.
        /// Under XWayland both a Wayland and an X display exist at once.
        /// </summary>
        public static bool HasReachableXServer()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
                return false;

            try
            {
                var display = X11Interop.XOpenDisplay(null);
                if (display == IntPtr.Zero) return false;

                X11Interop.XCloseDisplay(display);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinuxSession] X server probe failed: {ex.Message}");
                return false;
            }
        }
    }
}

using System;
using System.Diagnostics;
using Cloudict.Abstractions;
using Cloudict.Platform;
using Cloudict.Services;
using Cloudict.Speech;

namespace Cloudict.App
{
    /// <summary>
    /// The composition root: builds the operating-system services once and hands them to whoever
    /// needs them.
    ///
    /// <para>Everything platform-specific enters the application here and nowhere else. That is what
    /// keeps OS checks from spreading back through the codebase the way they had in 2.x, where
    /// <c>user32</c> calls sat directly inside window code.</para>
    /// </summary>
    internal static class AppServices
    {
        private static readonly object Gate = new object();
        private static bool _initialized;

        public static IPlatformServices Platform { get; private set; }
        public static SettingsManager Settings { get; private set; }
        public static BrowserProvisioner Browser { get; private set; }

        /// <summary>What this platform can and cannot do, for the UI to explain.</summary>
        public static PlatformCapabilities Capabilities { get; private set; }

        /// <summary>Raised when Core wants to tell the user something; the UI turns it into a dialog.</summary>
        public static event EventHandler<UserMessageEventArgs> UserMessage;

        public static void Initialize()
        {
            if (_initialized) return;

            lock (Gate)
            {
                if (_initialized) return;

                Platform = PlatformServices.Create();
                Platform.Paths.EnsureCreated();

                // Opt-in via CLOUDICT_DEBUG=1; writes nothing otherwise.
                DiagnosticLog.Initialize(Platform.Paths.LogDirectory);

                Settings = new SettingsManager(Platform.Paths);
                Settings.UserMessage += (s, e) => UserMessage?.Invoke(s, e);

                Browser = new BrowserProvisioner(Platform.Paths, Platform.Info, Platform.BrowserLocator);
                Capabilities = Platform.GetCapabilities();

                _initialized = true;
            }
        }

        public static void Shutdown()
        {
            try { Platform?.Dispose(); }
            catch (Exception ex) { Debug.WriteLine($"[AppServices] shutdown: {ex.Message}"); }
        }
    }
}

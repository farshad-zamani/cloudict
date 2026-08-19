using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cloudict.Abstractions;
using Cloudict.Platform.Unix;
using Cloudict.Platform.Unsupported;
using Cloudict.Platform.Windows;

namespace Cloudict.Platform
{
    /// <summary>
    /// The one place in Cloudict that asks which operating system it is running on.
    ///
    /// <para>Everything else takes <see cref="IPlatformServices"/> and stays oblivious. That is what
    /// keeps the OS check from spreading back through the codebase the way it had in 2.x, where
    /// <c>user32</c> calls sat directly inside window code.</para>
    /// </summary>
    public static class PlatformServices
    {
        /// <summary>Builds the service set for the current operating system.</summary>
        public static IPlatformServices Create()
        {
            if (OperatingSystem.IsWindows()) return CreateWindows();
            if (OperatingSystem.IsMacOS()) return CreateUnix(isMacOS: true);
            if (OperatingSystem.IsLinux()) return CreateUnix(isMacOS: false);

            throw new PlatformNotSupportedException(
                $"Cloudict does not support {RuntimeInformationDescription()}.");
        }

        private static string RuntimeInformationDescription() =>
            System.Runtime.InteropServices.RuntimeInformation.OSDescription;

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static IPlatformServices CreateWindows()
        {
            var info = new WindowsPlatformInfo();

            return new CompositePlatformServices
            {
                Paths = new WindowsAppPaths(),
                Info = info,
                BrowserLocator = new WindowsBrowserLocator(info),
                TextInjector = new WindowsTextInjector(),
                GlobalHotkeys = new WindowsGlobalHotkeys(),
                KeyboardLayout = new WindowsKeyboardLayout(),
                MicrophoneMonitor = new WindowsMicrophoneMonitor()
            };
        }

        /// <summary>
        /// Linux and macOS. Storage, browser discovery and driver handling are fully implemented, so
        /// the speech pipeline resolves Chrome and its driver correctly on both. Key injection,
        /// global shortcuts, layout switching and microphone detection are still stubs that report
        /// themselves as unavailable — they arrive with the Linux and macOS milestones.
        /// </summary>
        private static IPlatformServices CreateUnix(bool isMacOS)
        {
            var info = new UnixPlatformInfo(isMacOS);

            ITextInjector injector = isMacOS
                ? new NullTextInjector("Platform_Err_MacInjectionNotImplemented")
                : new Cloudict.Platform.Linux.LinuxTextInjector();

            var hotkeyReason = isMacOS
                ? "Platform_Err_MacHotkeysNotImplemented"
                : "Platform_Err_LinuxHotkeysNotImplemented";

            return new CompositePlatformServices
            {
                Paths = new UnixAppPaths(isMacOS),
                Info = info,
                BrowserLocator = new UnixBrowserLocator(isMacOS, info),
                TextInjector = injector,
                GlobalHotkeys = new NullGlobalHotkeys(hotkeyReason),
                KeyboardLayout = new NullKeyboardLayout(),
                MicrophoneMonitor = new NullMicrophoneMonitor()
            };
        }

        private sealed class CompositePlatformServices : IPlatformServices
        {
            public IAppPaths Paths { get; init; }
            public IPlatformInfo Info { get; init; }
            public IBrowserLocator BrowserLocator { get; init; }
            public ITextInjector TextInjector { get; init; }
            public IGlobalHotkeys GlobalHotkeys { get; init; }
            public IKeyboardLayout KeyboardLayout { get; init; }
            public IMicrophoneMonitor MicrophoneMonitor { get; init; }

            public PlatformCapabilities GetCapabilities()
            {
                var limitations = new List<string>();

                // Report the reason whenever there is one, not only when the capability is missing
                // entirely. A backend can work *partially* — XTEST under XWayland reaches X11 windows
                // but silently misses native Wayland ones — and that is precisely the case a user
                // needs told, because everything looks fine until it inexplicably does not.
                if (TextInjector.UnavailableReasonKey != null)
                    limitations.Add(TextInjector.UnavailableReasonKey);

                if (GlobalHotkeys.UnsupportedReasonKey != null)
                    limitations.Add(GlobalHotkeys.UnsupportedReasonKey);

                return new PlatformCapabilities
                {
                    InjectionBackend = TextInjector.BackendName,
                    CanInjectText = TextInjector.IsAvailable,
                    CanRegisterGlobalHotkeys = GlobalHotkeys.IsSupported,
                    CanSwitchKeyboardLayout = KeyboardLayout.IsSupported,
                    CanDetectMicrophone = MicrophoneMonitor.IsSupported,
                    LimitationKeys = limitations
                };
            }

            public void Dispose()
            {
                foreach (var disposable in new IDisposable[] { TextInjector, GlobalHotkeys, MicrophoneMonitor })
                {
                    try { disposable?.Dispose(); }
                    catch (Exception ex) { Debug.WriteLine($"[PlatformServices] dispose: {ex.Message}"); }
                }
            }
        }
    }
}

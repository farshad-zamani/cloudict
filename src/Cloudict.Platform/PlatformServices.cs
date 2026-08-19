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
            if (OperatingSystem.IsMacOS()) return CreateMacOS();
            if (OperatingSystem.IsLinux()) return CreateLinux();

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
        /// Linux: storage, browser discovery, driver handling, key injection (XTEST, or ydotool on
        /// Wayland) and global shortcuts (XGrabKey). Keyboard-layout switching and microphone
        /// detection remain stubs and report themselves as unavailable.
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("linux")]
        private static IPlatformServices CreateLinux()
        {
            var info = new UnixPlatformInfo(isMacOS: false);

            return new CompositePlatformServices
            {
                Paths = new UnixAppPaths(isMacOS: false),
                Info = info,
                BrowserLocator = new UnixBrowserLocator(isMacOS: false, info),
                TextInjector = new Linux.LinuxTextInjector(),
                GlobalHotkeys = new Linux.LinuxGlobalHotkeys(),
                KeyboardLayout = new NullKeyboardLayout(),
                MicrophoneMonitor = new NullMicrophoneMonitor()
            };
        }

        /// <summary>
        /// macOS: storage, browser discovery, driver handling, key injection (Quartz) and global
        /// shortcuts (Carbon). Keyboard-layout switching and microphone detection remain stubs.
        /// </summary>
        [System.Runtime.Versioning.SupportedOSPlatform("macos")]
        private static IPlatformServices CreateMacOS()
        {
            var info = new UnixPlatformInfo(isMacOS: true);

            return new CompositePlatformServices
            {
                Paths = new UnixAppPaths(isMacOS: true),
                Info = info,
                BrowserLocator = new UnixBrowserLocator(isMacOS: true, info),
                TextInjector = new MacOS.MacTextInjector(),
                GlobalHotkeys = new MacOS.MacGlobalHotkeys(),
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

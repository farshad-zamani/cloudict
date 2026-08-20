using System;
using System.Collections.Generic;

namespace Cloudict.Abstractions
{
    /// <summary>Switches the system keyboard layout, used by the "type in Persian / English" voice commands.</summary>
    public interface IKeyboardLayout
    {
        bool IsSupported { get; }

        /// <summary>Switches to the layout for a language code such as "fa" or "en". False if it could not.</summary>
        bool TrySwitchTo(string languageCode);

        /// <summary>The active layout's language code, or null when it cannot be determined.</summary>
        string GetCurrentLanguage();
    }

    /// <summary>
    /// Reports whether the microphone is currently being captured by any application, which drives
    /// the desktop status light. Purely cosmetic — when unsupported the UI falls back to the app's
    /// own dictation state.
    /// </summary>
    public interface IMicrophoneMonitor : IDisposable
    {
        bool IsSupported { get; }
        bool IsMicrophoneInUse();
    }

    /// <summary>
    /// Where Cloudict may write. Previously everything went next to the executable, which only
    /// worked because the Windows build runs elevated; on Linux the install prefix is root-owned
    /// and on macOS the app bundle is read-only and signed, so writing there is not an option.
    /// </summary>
    public interface IAppPaths
    {
        /// <summary>User settings (<c>settings.json</c> and its backup).</summary>
        string ConfigDirectory { get; }

        /// <summary>Downloaded ChromeDriver cache and other regenerable data.</summary>
        string DataDirectory { get; }

        /// <summary>Crash logs and diagnostics.</summary>
        string LogDirectory { get; }

        /// <summary>Creates any of the above that do not exist yet.</summary>
        void EnsureCreated();
    }

    /// <summary>A Chrome installation found on this machine.</summary>
    public sealed class BrowserInstall
    {
        public string Path { get; init; }
        public Version Version { get; init; }
        public int Major => Version?.Major ?? 0;
    }

    /// <summary>
    /// Finds Google Chrome. Chrome specifically, not Chromium: Chromium builds omit Google's API
    /// keys, so the Web Speech API that Google Translate depends on silently returns nothing.
    /// </summary>
    public interface IBrowserLocator
    {
        /// <summary>The newest Chrome on this machine, or null when none is installed.</summary>
        BrowserInstall FindChrome();
    }

    /// <summary>
    /// The handful of facts about the host OS that <see cref="Speech.BrowserProvisioner"/> needs in
    /// order to pick and unpack the right ChromeDriver build.
    /// </summary>
    public interface IPlatformInfo
    {
        /// <summary>Chrome-for-Testing platform id: <c>win64</c>, <c>linux64</c>, <c>mac-x64</c> or <c>mac-arm64</c>.</summary>
        string DriverPlatformKey { get; }

        /// <summary><c>chromedriver.exe</c> on Windows, <c>chromedriver</c> elsewhere.</summary>
        string DriverFileName { get; }

        /// <summary>Extra directories worth searching for a driver the user already has.</summary>
        IEnumerable<string> AdditionalDriverSearchPaths { get; }

        /// <summary>
        /// Marks a freshly extracted driver as executable. A no-op on Windows; on Linux and macOS a
        /// driver without the execute bit fails to start with a bare "permission denied".
        /// </summary>
        void MakeExecutable(string path);

        /// <summary>Reads a browser or driver executable's version without running it, where possible.</summary>
        Version ReadExecutableVersion(string path);
    }

    /// <summary>
    /// What this machine can and cannot do, so the UI can tell the user up front rather than
    /// letting them discover that dictation types nothing.
    /// </summary>
    public sealed class PlatformCapabilities
    {
        /// <summary>Which injection mechanism was selected (see <see cref="ITextInjector.BackendName"/>).</summary>
        public string InjectionBackend { get; init; }

        public bool CanInjectText { get; init; }
        public bool CanRegisterGlobalHotkeys { get; init; }
        public bool CanSwitchKeyboardLayout { get; init; }
        public bool CanDetectMicrophone { get; init; }

        /// <summary>Localization keys for every limitation worth showing the user. Empty when all is well.</summary>
        public IReadOnlyList<string> LimitationKeys { get; init; } = Array.Empty<string>();

        public bool IsFullyCapable => CanInjectText && CanRegisterGlobalHotkeys;
    }

    /// <summary>
    /// Everything the application needs from the operating system, resolved once at startup.
    /// <c>Cloudict.Platform.PlatformServices.Create()</c> returns the implementation for the
    /// current OS; no other code performs an OS check.
    /// </summary>
    public interface IPlatformServices : IDisposable
    {
        IAppPaths Paths { get; }
        IPlatformInfo Info { get; }
        IBrowserLocator BrowserLocator { get; }
        ITextInjector TextInjector { get; }
        IGlobalHotkeys GlobalHotkeys { get; }
        IKeyboardLayout KeyboardLayout { get; }
        IMicrophoneMonitor MicrophoneMonitor { get; }

        /// <summary>Shows desktop notifications. Never null; may report itself unsupported.</summary>
        INotifier Notifier { get; }

        /// <summary>
        /// A tray presence owned by the platform, or null when the application should provide its
        /// own. Only Windows needs this, because a balloon there requires a registered tray icon.
        /// </summary>
        ITrayPresence TrayPresence { get; }

        /// <summary>Recomputed from the services above; call after a permission may have changed.</summary>
        PlatformCapabilities GetCapabilities();
    }
}

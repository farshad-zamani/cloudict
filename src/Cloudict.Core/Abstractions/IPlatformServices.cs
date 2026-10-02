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

    /// <summary>
    /// The browsers Cloudict can use as its helper.
    ///
    /// <para>Only these, and deliberately. Google Translate's microphone runs on the browser's own
    /// speech service. Chrome and Chrome for Testing send the audio to Google with Google's own API
    /// key; Edge sends it to Microsoft's speech service. Other Chromium browsers — Brave, Vivaldi,
    /// Opera, plain Chromium — also send it to Google but with a key Google does not accept: Brave's
    /// request was measured coming back <c>403 Forbidden</c>, so the page simply hears nothing.</para>
    /// </summary>
    public enum BrowserKind
    {
        Chrome,
        Edge,
        ChromeForTesting
    }

    /// <summary>A browser installation found on this machine.</summary>
    public sealed class BrowserInstall
    {
        public string Path { get; init; }
        public Version Version { get; init; }
        public BrowserKind Kind { get; init; } = BrowserKind.Chrome;
        public int Major => Version?.Major ?? 0;

        /// <summary>The name to show the user.</summary>
        public string DisplayName => Kind switch
        {
            BrowserKind.Edge => "Microsoft Edge",
            BrowserKind.ChromeForTesting => "Chrome for Testing",
            _ => "Google Chrome"
        };
    }

    /// <summary>
    /// Finds the browsers that can serve as the helper — see <see cref="BrowserKind"/> for why it is
    /// these two and not any Chromium-based browser.
    /// </summary>
    public interface IBrowserLocator
    {
        /// <summary>The newest Google Chrome on this machine, or null when none is installed.</summary>
        BrowserInstall FindChrome();

        /// <summary>Microsoft Edge, or null when it is not installed.</summary>
        BrowserInstall FindEdge();
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

        /// <summary>Microsoft's platform id for EdgeDriver: <c>win64</c>, <c>linux64</c>, <c>mac64</c> or <c>mac64_m1</c>.</summary>
        string EdgeDriverPlatformKey { get; }

        /// <summary><c>msedgedriver.exe</c> on Windows, <c>msedgedriver</c> elsewhere.</summary>
        string EdgeDriverFileName { get; }

        /// <summary>
        /// Where the browser executable sits inside an unpacked Chrome-for-Testing download, relative
        /// to the folder it was unpacked into.
        /// </summary>
        string ChromeForTestingExecutable { get; }

        /// <summary>
        /// Unpacks a downloaded browser archive. A platform concern because macOS app bundles are
        /// full of symbolic links and permission bits that a general-purpose zip reader drops, which
        /// leaves an app that will not start; there the system's own tool does it.
        /// </summary>
        void ExtractArchive(string zipPath, string destination);
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
        /// Sends the machine's own audio to the speech engine in place of the microphone. Never
        /// null; may report itself unsupported or in need of a helper the user installs once.
        /// </summary>
        IAudioRouting AudioRouting { get; }

        /// <summary>
        /// A tray presence owned by the platform, or null when the application should provide its
        /// own. Only Windows needs this, because a balloon there requires a registered tray icon.
        /// </summary>
        ITrayPresence TrayPresence { get; }

        /// <summary>
        /// Reads what precedes the caret in the application being typed into. Never null; on a
        /// platform that cannot ask, every probe reports unknown.
        /// </summary>
        ICaretContext CaretContext { get; }

        /// <summary>Recomputed from the services above; call after a permission may have changed.</summary>
        PlatformCapabilities GetCapabilities();
    }
}

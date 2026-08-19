using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Cloudict.Abstractions;

namespace Cloudict.Platform.Unix
{
    /// <summary>
    /// Storage locations for Linux and macOS.
    ///
    /// <para>On Linux this follows the XDG Base Directory specification, honouring
    /// <c>XDG_CONFIG_HOME</c> and <c>XDG_DATA_HOME</c> when the user has set them. On macOS it uses
    /// <c>~/Library/Application Support</c> and <c>~/Library/Logs</c> as Apple expects. Neither
    /// writes anywhere near the installed application, which on both systems is read-only.</para>
    /// </summary>
    internal sealed class UnixAppPaths : IAppPaths
    {
        public UnixAppPaths(bool isMacOS)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (isMacOS)
            {
                var appSupport = Path.Combine(home, "Library", "Application Support", "Cloudict");
                ConfigDirectory = appSupport;
                DataDirectory = appSupport;
                LogDirectory = Path.Combine(home, "Library", "Logs", "Cloudict");
            }
            else
            {
                var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrWhiteSpace(configHome)) configHome = Path.Combine(home, ".config");

                var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (string.IsNullOrWhiteSpace(dataHome)) dataHome = Path.Combine(home, ".local", "share");

                var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
                if (string.IsNullOrWhiteSpace(stateHome)) stateHome = Path.Combine(home, ".local", "state");

                ConfigDirectory = Path.Combine(configHome, "cloudict");
                DataDirectory = Path.Combine(dataHome, "cloudict");
                LogDirectory = Path.Combine(stateHome, "cloudict");
            }
        }

        public string ConfigDirectory { get; }
        public string DataDirectory { get; }
        public string LogDirectory { get; }

        public void EnsureCreated()
        {
            foreach (var dir in new[] { ConfigDirectory, DataDirectory, LogDirectory })
            {
                try { Directory.CreateDirectory(dir); }
                catch (Exception ex) { Debug.WriteLine($"[UnixAppPaths] cannot create {dir}: {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// Driver naming, permissions and version reading for Linux and macOS.
    ///
    /// <para>Two things differ from Windows and both are easy to get wrong. Zip archives do not
    /// carry the Unix execute bit, so a freshly extracted driver must be chmod'ed or it fails with a
    /// bare "permission denied". And ELF/Mach-O binaries have no version resource, so the version
    /// has to come from running <c>chromedriver --version</c> — which is why
    /// <see cref="Speech.BrowserProvisioner"/> prefers the version encoded in the folder name and
    /// only falls back to this.</para>
    /// </summary>
    internal sealed class UnixPlatformInfo : IPlatformInfo
    {
        private readonly bool _isMacOS;

        public UnixPlatformInfo(bool isMacOS) => _isMacOS = isMacOS;

        public string DriverPlatformKey =>
            _isMacOS
                ? (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "mac-arm64" : "mac-x64")
                : "linux64";

        public string DriverFileName => "chromedriver";

        public IEnumerable<string> AdditionalDriverSearchPaths
        {
            get
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home))
                    yield return Path.Combine(home, ".cache", "selenium", "chromedriver");

                yield return "/usr/local/bin";
                yield return "/usr/bin";
                if (_isMacOS) yield return "/opt/homebrew/bin";
            }
        }

        /// <summary>Adds the execute bit for user, group and other (0755-style), preserving read/write bits.</summary>
        public void MakeExecutable(string path)
        {
            // Windows has no execute bit, and the file-mode APIs throw there.
            if (OperatingSystem.IsWindows()) return;

            try
            {
                var mode = File.GetUnixFileMode(path);
                File.SetUnixFileMode(path, mode
                    | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute
                    | UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UnixPlatformInfo] chmod +x failed for {path}: {ex.Message}");
            }
        }

        /// <summary>Runs the executable with <c>--version</c> and parses the first version-looking token.</summary>
        public Version ReadExecutableVersion(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                var psi = new ProcessStartInfo(path, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                string output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                    return null;
                }

                return ParseVersionToken(output);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UnixPlatformInfo] cannot read version of {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Pulls "151.0.7922.77" out of "ChromeDriver 151.0.7922.77 (abc...)" or "Google Chrome 151.0.7922.76".</summary>
        internal static Version ParseVersionToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            foreach (var token in text.Split(new[] { ' ', '\t', '\r', '\n', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = new string(token.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
                if (trimmed.Count(c => c == '.') >= 2 && Version.TryParse(trimmed, out var v))
                    return v;
            }

            return null;
        }
    }

    /// <summary>
    /// Finds Google Chrome on Linux and macOS. Chrome specifically, not Chromium: Chromium builds
    /// omit Google's API keys, so the Web Speech API that Google Translate depends on silently
    /// returns nothing — the app would appear to work while transcribing not a single word.
    /// </summary>
    internal sealed class UnixBrowserLocator : IBrowserLocator
    {
        private readonly bool _isMacOS;
        private readonly IPlatformInfo _info;

        public UnixBrowserLocator(bool isMacOS, IPlatformInfo info)
        {
            _isMacOS = isMacOS;
            _info = info;
        }

        public BrowserInstall FindChrome()
        {
            return CandidatePaths()
                .Distinct()
                .Select(TryRead)
                .Where(c => c != null)
                .OrderByDescending(c => c.Version)
                .FirstOrDefault();
        }

        private IEnumerable<string> CandidatePaths()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (_isMacOS)
            {
                yield return "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
                if (!string.IsNullOrEmpty(home))
                    yield return Path.Combine(home, "Applications", "Google Chrome.app", "Contents", "MacOS", "Google Chrome");
                yield break;
            }

            yield return "/usr/bin/google-chrome";
            yield return "/usr/bin/google-chrome-stable";
            yield return "/opt/google/chrome/chrome";
            yield return "/opt/google/chrome/google-chrome";
            yield return "/usr/local/bin/google-chrome";
            yield return "/snap/bin/google-chrome";

            // Flatpak installs are not on PATH and are launched through the runtime.
            yield return "/var/lib/flatpak/exports/bin/com.google.Chrome";
            if (!string.IsNullOrEmpty(home))
                yield return Path.Combine(home, ".local", "share", "flatpak", "exports", "bin", "com.google.Chrome");
        }

        private BrowserInstall TryRead(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

                var version = _isMacOS ? ReadMacBundleVersion(path) : null;
                version ??= _info.ReadExecutableVersion(path);

                return version == null ? null : new BrowserInstall { Path = path, Version = version };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UnixBrowserLocator] cannot read Chrome at {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads <c>CFBundleShortVersionString</c> from the app bundle's Info.plist. Cheaper and more
        /// reliable than launching Chrome, which on macOS can take a second and may open a window.
        /// </summary>
        private static Version ReadMacBundleVersion(string executablePath)
        {
            try
            {
                // .../Contents/MacOS/Google Chrome  ->  .../Contents/Info.plist
                var contents = Path.GetDirectoryName(Path.GetDirectoryName(executablePath));
                if (contents == null) return null;

                var plist = Path.Combine(contents, "Info.plist");
                if (!File.Exists(plist)) return null;

                var xml = File.ReadAllText(plist);
                var marker = "<key>CFBundleShortVersionString</key>";
                var at = xml.IndexOf(marker, StringComparison.Ordinal);
                if (at < 0) return null;

                var open = xml.IndexOf("<string>", at, StringComparison.Ordinal);
                var close = xml.IndexOf("</string>", open < 0 ? at : open, StringComparison.Ordinal);
                if (open < 0 || close < 0) return null;

                var value = xml.Substring(open + "<string>".Length, close - open - "<string>".Length).Trim();
                return Version.TryParse(value, out var v) ? v : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UnixBrowserLocator] Info.plist read failed: {ex.Message}");
                return null;
            }
        }
    }
}

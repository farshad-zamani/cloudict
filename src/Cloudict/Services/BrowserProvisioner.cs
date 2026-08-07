using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Microsoft.Win32;

namespace Cloudict.Services
{
    /// <summary>
    /// Locates the Chrome browser and a compatible ChromeDriver <b>without requiring an
    /// internet connection</b>.
    ///
    /// <para>Cloudict used to hand this job to WebDriverManager, which always went out to
    /// Google's servers on startup. Those servers
    /// (<c>storage.googleapis.com/chrome-for-testing-public</c>) are unreachable from a number of
    /// regions — they answer <c>403 Forbidden</c>, or the TLS handshake is intercepted and the
    /// request dies with an SSL error — so the browser could never be prepared and the app was
    /// unusable. The download was also re-triggered every time Chrome auto-updated to a new major
    /// version, which is why machines that had worked for months suddenly broke.</para>
    ///
    /// <para>The driver is now resolved from disk first: a driver ships inside the installer, and
    /// any newer driver already present on the machine is preferred over it. The network is only
    /// consulted as a last resort — when the installed Chrome is newer than every driver on the
    /// machine — and even then it tries reachable mirrors before Google's own host.</para>
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static class BrowserProvisioner
    {
        /// <summary>Directory (beside the executable) holding the driver shipped in the installer.</summary>
        private const string BundledDriverFolder = "Drivers";

        /// <summary>Legacy WebDriverManager cache inside the app folder, kept so existing installs keep working.</summary>
        private const string LegacyDriverFolder = "Chrome";

        private const string DriverFileName = "chromedriver.exe";

        /// <summary>How deep to look for <c>chromedriver.exe</c> inside a search root.</summary>
        private const int MaxSearchDepth = 4;

        private static readonly HttpClient Http = CreateHttpClient();

        #region Public API

        /// <summary>The outcome of a successful <see cref="Provision"/> call.</summary>
        public sealed class Provision
        {
            public string DriverPath { get; init; }
            public string DriverVersion { get; init; }
            public string DriverSource { get; init; }
            public string ChromePath { get; init; }
            public string ChromeVersion { get; init; }

            /// <summary>
            /// True when the driver's major version differs from Chrome's. ChromeDriver refuses to
            /// start in that case unless the build check is turned off, so the caller passes this
            /// through to <c>ChromeDriverService.DisableBuildCheck</c>.
            /// </summary>
            public bool RequiresBuildCheckOverride { get; init; }

            public string DriverDirectory => Path.GetDirectoryName(DriverPath);
            public string DriverFileName => Path.GetFileName(DriverPath);
        }

        /// <summary>
        /// Thrown when no usable Chrome/driver combination could be produced. <see cref="MessageKey"/>
        /// names a localized string so the UI can show something the user can act on.
        /// </summary>
        public sealed class ProvisionException : Exception
        {
            public ProvisionException(string messageKey, string detail = null)
                : base(Loc.Get(messageKey) + (string.IsNullOrWhiteSpace(detail) ? "" : " " + detail))
            {
                MessageKey = messageKey;
            }

            public string MessageKey { get; }
        }

        /// <summary>
        /// Finds Chrome and the best matching driver.
        /// </summary>
        /// <param name="report">Receives short progress messages for the status bar.</param>
        /// <param name="allowDownload">
        /// When false, the method never touches the network — it either finds a local driver or throws.
        /// </param>
        /// <exception cref="ProvisionException">Chrome is missing, or no driver could be obtained.</exception>
        public static Provision Resolve(Action<string> report, bool allowDownload = true, CancellationToken ct = default)
        {
            report?.Invoke(Loc.Get("Browser_St_LookingForChrome"));

            var chrome = FindChrome();
            if (chrome == null)
                throw new ProvisionException("Browser_Err_ChromeNotInstalled");

            report?.Invoke(Loc.Get("Browser_St_LookingForDriver"));

            var candidates = FindLocalDrivers();
            var best = PickBest(candidates, chrome.Major);

            if (best != null)
            {
                return new Provision
                {
                    DriverPath = best.Path,
                    DriverVersion = best.Version.ToString(),
                    DriverSource = best.Source,
                    ChromePath = chrome.Path,
                    ChromeVersion = chrome.Version.ToString(),
                    RequiresBuildCheckOverride = false
                };
            }

            // Nothing on disk matches this Chrome. Chrome has almost certainly auto-updated past
            // the driver that shipped with the installer, so fetch the matching one — once.
            if (allowDownload)
            {
                try
                {
                    var downloaded = Download(chrome, report, ct);
                    if (downloaded != null)
                    {
                        return new Provision
                        {
                            DriverPath = downloaded.Path,
                            DriverVersion = downloaded.Version.ToString(),
                            DriverSource = downloaded.Source,
                            ChromePath = chrome.Path,
                            ChromeVersion = chrome.Version.ToString(),
                            RequiresBuildCheckOverride = false
                        };
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BrowserProvisioner] download failed: {ex.Message}");
                }
            }

            // Offline (or blocked) and no exact match. Rather than leaving the user with a dead
            // app, drive Chrome with the closest driver we have and tell ChromeDriver to skip its
            // version check — across nearby versions the DevTools protocol is stable enough that
            // this normally just works.
            var fallback = PickClosest(candidates, chrome.Major);
            if (fallback != null)
            {
                Debug.WriteLine($"[BrowserProvisioner] falling back to driver {fallback.Version} for Chrome {chrome.Version}");
                return new Provision
                {
                    DriverPath = fallback.Path,
                    DriverVersion = fallback.Version.ToString(),
                    DriverSource = fallback.Source,
                    ChromePath = chrome.Path,
                    ChromeVersion = chrome.Version.ToString(),
                    RequiresBuildCheckOverride = true
                };
            }

            throw new ProvisionException("Browser_Err_NoDriver", $"(Chrome {chrome.Version})");
        }

        /// <summary>Directory this app may write downloaded drivers into (never the install folder).</summary>
        public static string UserDriverCache => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cloudict", "Drivers");

        #endregion

        #region Chrome detection

        private sealed class ChromeInstall
        {
            public string Path { get; init; }
            public Version Version { get; init; }
            public int Major => Version.Major;
        }

        /// <summary>
        /// Finds the highest-versioned Google Chrome on the machine. Only real Chrome is accepted:
        /// Chromium and its derivatives are built without Google's API keys, so the Web Speech API
        /// that Google Translate relies on silently returns nothing there.
        /// </summary>
        private static ChromeInstall FindChrome()
        {
            var paths = new List<string>();

            // "App Paths" is what Windows itself uses to resolve `chrome.exe`, so it finds Chrome
            // wherever it was installed — including per-user installs outside Program Files.
            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (var key in new[]
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"
                })
                {
                    try
                    {
                        using var sub = root.OpenSubKey(key);
                        if (sub?.GetValue(null) is string p && !string.IsNullOrWhiteSpace(p))
                            paths.Add(p.Trim('"'));
                    }
                    catch (Exception ex) { Debug.WriteLine($"[BrowserProvisioner] registry probe failed: {ex.Message}"); }
                }
            }

            foreach (var folder in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            })
            {
                if (!string.IsNullOrEmpty(folder))
                    paths.Add(Path.Combine(folder, "Google", "Chrome", "Application", "chrome.exe"));
            }

            return paths
                .Select(TryReadChrome)
                .Where(c => c != null)
                .OrderByDescending(c => c.Version)
                .FirstOrDefault();
        }

        private static ChromeInstall TryReadChrome(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                var version = ReadVersion(path);
                return version == null ? null : new ChromeInstall { Path = path, Version = version };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BrowserProvisioner] cannot read Chrome at {path}: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Driver discovery

        private sealed class DriverCandidate
        {
            public string Path { get; init; }
            public Version Version { get; init; }
            public string Source { get; init; }
        }

        /// <summary>
        /// Collects every <c>chromedriver.exe</c> reachable on this machine, newest first.
        /// Drivers the user already has — a previous Cloudict download, a Selenium cache, one on
        /// PATH — are all fair game, so a machine that is already ahead of the bundled driver is
        /// served from disk instead of being sent to the network.
        /// </summary>
        private static List<DriverCandidate> FindLocalDrivers()
        {
            var found = new List<DriverCandidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Scan(string root, string source)
            {
                if (string.IsNullOrWhiteSpace(root)) return;
                foreach (var file in EnumerateDrivers(root, MaxSearchDepth))
                {
                    if (!seen.Add(Path.GetFullPath(file))) continue;
                    var version = ReadVersion(file);
                    if (version != null)
                        found.Add(new DriverCandidate { Path = file, Version = version, Source = source });
                }
            }

            var appDir = AppContext.BaseDirectory;
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // A driver the user downloaded through us wins over the bundled one when both match,
            // because it is the newer of the two by construction.
            Scan(UserDriverCache, "cache");
            Scan(Path.Combine(appDir, BundledDriverFolder), "bundled");
            Scan(Path.Combine(appDir, LegacyDriverFolder), "legacy");

            if (!string.IsNullOrEmpty(localAppData))
                Scan(Path.Combine(localAppData, "ChromeDriver"), "system");
            if (!string.IsNullOrEmpty(userProfile))
                Scan(Path.Combine(userProfile, ".cache", "selenium", "chromedriver"), "system");

            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), DriverFileName);
                    if (File.Exists(candidate) && seen.Add(Path.GetFullPath(candidate)))
                    {
                        var version = ReadVersion(candidate);
                        if (version != null)
                            found.Add(new DriverCandidate { Path = candidate, Version = version, Source = "path" });
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[BrowserProvisioner] PATH probe failed: {ex.Message}"); }
            }

            return found;
        }

        /// <summary>Depth-limited search that tolerates folders we are not allowed to read.</summary>
        private static IEnumerable<string> EnumerateDrivers(string root, int depth)
        {
            if (depth < 0 || !Directory.Exists(root)) yield break;

            string[] files;
            try { files = Directory.GetFiles(root, DriverFileName); }
            catch (Exception ex) { Debug.WriteLine($"[BrowserProvisioner] cannot list {root}: {ex.Message}"); yield break; }

            foreach (var f in files) yield return f;

            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch (Exception ex) { Debug.WriteLine($"[BrowserProvisioner] cannot list {root}: {ex.Message}"); yield break; }

            foreach (var d in dirs)
                foreach (var f in EnumerateDrivers(d, depth - 1))
                    yield return f;
        }

        /// <summary>
        /// The best driver for a given Chrome: same major version, highest patch. ChromeDriver and
        /// Chrome are released in lockstep, so a matching major is the compatibility contract.
        /// </summary>
        private static DriverCandidate PickBest(List<DriverCandidate> candidates, int chromeMajor) =>
            candidates
                .Where(c => c.Version.Major == chromeMajor)
                .OrderByDescending(c => c.Version)
                .FirstOrDefault();

        /// <summary>Last-resort pick when nothing matches: the driver closest to Chrome's major version.</summary>
        private static DriverCandidate PickClosest(List<DriverCandidate> candidates, int chromeMajor) =>
            candidates
                .OrderBy(c => Math.Abs(c.Version.Major - chromeMajor))
                .ThenByDescending(c => c.Version)
                .FirstOrDefault();

        private static Version ReadVersion(string exePath)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                var raw = info.ProductVersion ?? info.FileVersion;
                if (string.IsNullOrWhiteSpace(raw)) return null;

                // Trim any suffix such as "151.0.7922.77 (abcdef)" before parsing.
                var numeric = new string(raw.TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray()).Trim('.');
                return Version.TryParse(numeric, out var v) ? v : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BrowserProvisioner] cannot read version of {exePath}: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Download (last resort)

        /// <summary>
        /// Metadata hosts, in the order they are tried. The version index lives on GitHub Pages,
        /// which stays reachable in places where Google's storage host does not.
        /// </summary>
        private static readonly string[] VersionIndexUrls =
        {
            "https://googlechromelabs.github.io/chrome-for-testing/latest-patch-versions-per-build.json",
            "https://cdn.npmmirror.com/binaries/chrome-for-testing/latest-patch-versions-per-build.json"
        };

        /// <summary>
        /// Binary hosts, in the order they are tried. Google's own host is last on purpose: it is
        /// the one that answers <c>403 Forbidden</c> for a large share of this app's users, so the
        /// mirrors get first refusal.
        /// </summary>
        private static readonly string[] DriverZipUrlTemplates =
        {
            "https://cdn.npmmirror.com/binaries/chrome-for-testing/{0}/win64/chromedriver-win64.zip",
            "https://registry.npmmirror.com/-/binary/chrome-for-testing/{0}/win64/chromedriver-win64.zip",
            "https://storage.googleapis.com/chrome-for-testing-public/{0}/win64/chromedriver-win64.zip"
        };

        private static DriverCandidate Download(ChromeInstall chrome, Action<string> report, CancellationToken ct)
        {
            report?.Invoke(Loc.Get("Browser_St_DownloadingDriver"));

            foreach (var version in CandidateDriverVersions(chrome, ct))
            {
                foreach (var template in DriverZipUrlTemplates)
                {
                    ct.ThrowIfCancellationRequested();
                    var url = string.Format(template, version);
                    try
                    {
                        var bytes = Http.GetByteArrayAsync(url, ct).GetAwaiter().GetResult();
                        var path = ExtractDriver(bytes, version);
                        if (path != null)
                        {
                            var actual = ReadVersion(path) ?? Version.Parse(version);
                            report?.Invoke(Loc.Get("Browser_St_DriverDownloaded", actual.ToString()));
                            return new DriverCandidate { Path = path, Version = actual, Source = "downloaded" };
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[BrowserProvisioner] {url} -> {ex.Message}");
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Driver versions worth trying for this Chrome, best guess first: the exact Chrome build,
        /// then the published patch for that build, then the newest patch of the same major.
        /// </summary>
        private static IEnumerable<string> CandidateDriverVersions(ChromeInstall chrome, CancellationToken ct)
        {
            var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var exact = chrome.Version.ToString();
            if (tried.Add(exact)) yield return exact;

            foreach (var v in VersionsFromIndex(chrome, ct))
                if (tried.Add(v)) yield return v;
        }

        private static IEnumerable<string> VersionsFromIndex(ChromeInstall chrome, CancellationToken ct)
        {
            var results = new List<string>();

            foreach (var indexUrl in VersionIndexUrls)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var json = Http.GetStringAsync(indexUrl, ct).GetAwaiter().GetResult();
                    var builds = Newtonsoft.Json.Linq.JObject.Parse(json)["builds"] as Newtonsoft.Json.Linq.JObject;
                    if (builds == null) continue;

                    var v = chrome.Version;
                    var exactBuild = $"{v.Major}.{v.Minor}.{v.Build}";

                    if (builds[exactBuild]?["version"]?.ToString() is string patched && !string.IsNullOrWhiteSpace(patched))
                        results.Add(patched);

                    // Chrome may sit on a build that has no published driver; the newest driver of
                    // the same major is the next best thing.
                    var sameMajor = builds.Properties()
                        .Where(p => p.Name.StartsWith(v.Major + ".", StringComparison.Ordinal))
                        .Select(p => p.Value?["version"]?.ToString())
                        .Where(s => Version.TryParse(s, out _))
                        .OrderByDescending(Version.Parse)
                        .FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(sameMajor))
                        results.Add(sameMajor);

                    if (results.Count > 0) break;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BrowserProvisioner] version index {indexUrl} -> {ex.Message}");
                }
            }

            return results;
        }

        /// <summary>
        /// Unpacks <c>chromedriver.exe</c> from a Chrome-for-Testing zip into the per-user cache.
        /// Writing under LocalAppData rather than the install folder keeps this working without
        /// elevation and never disturbs the driver that shipped with the installer.
        /// </summary>
        private static string ExtractDriver(byte[] zipBytes, string version)
        {
            using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
            var entry = archive.Entries.FirstOrDefault(
                e => string.Equals(e.Name, DriverFileName, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;

            var dir = Path.Combine(UserDriverCache, version);
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, DriverFileName);

            // Extract next to the target and swap, so an interrupted download can never leave a
            // truncated chromedriver.exe behind for the next run to pick up.
            var temp = target + ".tmp";
            entry.ExtractToFile(temp, overwrite: true);
            File.Move(temp, target, overwrite: true);

            return target;
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                UseProxy = true
            };

            var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Cloudict");
            return client;
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Cloudict.Abstractions;

namespace Cloudict.Speech
{
    /// <summary>
    /// Locates a ChromeDriver compatible with the installed Chrome, <b>without requiring an
    /// internet connection</b>.
    ///
    /// <para>Cloudict used to let WebDriverManager fetch the driver from Google on every startup.
    /// That host (<c>storage.googleapis.com</c>) answers <c>403 Forbidden</c> in a number of
    /// regions, and elsewhere the TLS handshake is intercepted, so the browser could never be
    /// prepared. The download was also re-triggered by every Chrome auto-update, which is why
    /// machines that had worked for months broke on their own.</para>
    ///
    /// <para>The driver is now resolved from disk first: one ships inside the installer, and any
    /// newer driver already on the machine is preferred over it. The network is consulted only when
    /// Chrome has outrun every local driver, and even then mirrors are tried before Google's host.</para>
    ///
    /// <para>Nothing here is Windows-specific. Everything the host OS knows — where Chrome lives,
    /// what a driver executable is called, which Chrome-for-Testing build to download — arrives
    /// through <see cref="IPlatformInfo"/> and <see cref="IBrowserLocator"/>.</para>
    /// </summary>
    public sealed class BrowserProvisioner
    {
        /// <summary>Directory beside the executable holding the driver shipped in the installer.</summary>
        private const string BundledDriverFolder = "Drivers";

        /// <summary>Legacy WebDriverManager cache in the app folder, still honoured so 2.x installs keep working.</summary>
        private const string LegacyDriverFolder = "Chrome";

        /// <summary>How deep to look for a driver inside a search root.</summary>
        private const int MaxSearchDepth = 4;

        private static readonly HttpClient Http = CreateHttpClient();

        private readonly IAppPaths _paths;
        private readonly IPlatformInfo _info;
        private readonly IBrowserLocator _locator;

        public BrowserProvisioner(IAppPaths paths, IPlatformInfo info, IBrowserLocator locator)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _info = info ?? throw new ArgumentNullException(nameof(info));
            _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        }

        #region Public API

        /// <summary>A step worth showing the user, as a localization key plus its arguments.</summary>
        public sealed class ProvisionStatus
        {
            public ProvisionStatus(string messageKey, params object[] args)
            {
                MessageKey = messageKey;
                Args = args ?? Array.Empty<object>();
            }

            public string MessageKey { get; }
            public object[] Args { get; }
        }

        /// <summary>The outcome of a successful <see cref="Resolve"/> call.</summary>
        public sealed class Provision
        {
            public string DriverPath { get; init; }
            public string DriverVersion { get; init; }
            public string DriverSource { get; init; }
            public string ChromePath { get; init; }
            public string ChromeVersion { get; init; }

            /// <summary>
            /// True when the driver's major version differs from Chrome's. ChromeDriver refuses to
            /// start in that case unless its build check is disabled, so the caller passes this
            /// through to the driver service.
            /// </summary>
            public bool RequiresBuildCheckOverride { get; init; }

            public string DriverDirectory => Path.GetDirectoryName(DriverPath);
            public string DriverFileName => Path.GetFileName(DriverPath);
        }

        /// <summary>
        /// Thrown when no usable Chrome/driver combination could be produced.
        /// <see cref="MessageKey"/> names a localized string so the UI can show something actionable.
        /// </summary>
        public sealed class ProvisionException : Exception
        {
            public ProvisionException(string messageKey, string detail = null)
                : base(messageKey + (string.IsNullOrWhiteSpace(detail) ? "" : " " + detail))
            {
                MessageKey = messageKey;
                Detail = detail;
            }

            public string MessageKey { get; }
            public string Detail { get; }
        }

        /// <summary>
        /// Finds Chrome and the best matching driver.
        /// </summary>
        /// <param name="report">Receives progress steps for the status bar.</param>
        /// <param name="allowDownload">When false, never touches the network.</param>
        /// <exception cref="ProvisionException">Chrome is missing, or no driver could be obtained.</exception>
        public Provision Resolve(Action<ProvisionStatus> report, bool allowDownload = true, CancellationToken ct = default)
        {
            report?.Invoke(new ProvisionStatus("Browser_St_LookingForChrome"));

            var chrome = _locator.FindChrome();
            if (chrome == null)
                throw new ProvisionException("Browser_Err_ChromeNotInstalled");

            report?.Invoke(new ProvisionStatus("Browser_St_LookingForDriver"));

            var candidates = FindLocalDrivers();
            var best = PickBest(candidates, chrome.Major);

            if (best != null)
                return Describe(best, chrome, buildCheckOverride: false);

            // Nothing on disk matches. Chrome has almost certainly auto-updated past the driver that
            // shipped with the installer, so fetch the matching one — once.
            if (allowDownload)
            {
                try
                {
                    var downloaded = Download(chrome, report, ct);
                    if (downloaded != null)
                        return Describe(downloaded, chrome, buildCheckOverride: false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BrowserProvisioner] download failed: {ex.Message}");
                }
            }

            // Offline or blocked. Rather than leaving the user with a dead app, drive Chrome with the
            // closest driver available and skip the version check — across nearby versions the
            // DevTools protocol is stable enough that this normally just works.
            var fallback = PickClosest(candidates, chrome.Major);
            if (fallback != null)
            {
                Debug.WriteLine($"[BrowserProvisioner] falling back to driver {fallback.Version} for Chrome {chrome.Version}");
                return Describe(fallback, chrome, buildCheckOverride: true);
            }

            throw new ProvisionException("Browser_Err_NoDriver", $"(Chrome {chrome.Version})");
        }

        private static Provision Describe(DriverCandidate driver, BrowserInstall chrome, bool buildCheckOverride) =>
            new Provision
            {
                DriverPath = driver.Path,
                DriverVersion = driver.Version.ToString(),
                DriverSource = driver.Source,
                ChromePath = chrome.Path,
                ChromeVersion = chrome.Version.ToString(),
                RequiresBuildCheckOverride = buildCheckOverride
            };

        /// <summary>Directory this app may write downloaded drivers into (never the install folder).</summary>
        public string UserDriverCache => Path.Combine(_paths.DataDirectory, "Drivers");

        #endregion

        #region Driver discovery

        private sealed class DriverCandidate
        {
            public string Path { get; init; }
            public Version Version { get; init; }
            public string Source { get; init; }
        }

        /// <summary>
        /// Collects every driver reachable on this machine, so a machine already ahead of the
        /// bundled driver is served from disk instead of being sent to the network. Drivers the user
        /// already has are never overwritten or downgraded.
        /// </summary>
        private List<DriverCandidate> FindLocalDrivers()
        {
            var found = new List<DriverCandidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Scan(string root, string source)
            {
                if (string.IsNullOrWhiteSpace(root)) return;
                foreach (var file in EnumerateDrivers(root, MaxSearchDepth))
                {
                    if (!seen.Add(Path.GetFullPath(file))) continue;
                    var version = ReadDriverVersion(file);
                    if (version != null)
                        found.Add(new DriverCandidate { Path = file, Version = version, Source = source });
                }
            }

            var appDir = AppContext.BaseDirectory;

            // A driver downloaded through us wins over the bundled one when both match, because it
            // is the newer of the two by construction.
            Scan(UserDriverCache, "cache");
            Scan(Path.Combine(appDir, BundledDriverFolder), "bundled");
            Scan(Path.Combine(appDir, LegacyDriverFolder), "legacy");

            foreach (var extra in _info.AdditionalDriverSearchPaths ?? Enumerable.Empty<string>())
                Scan(extra, "system");

            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), _info.DriverFileName);
                    if (File.Exists(candidate) && seen.Add(Path.GetFullPath(candidate)))
                    {
                        var version = ReadDriverVersion(candidate);
                        if (version != null)
                            found.Add(new DriverCandidate { Path = candidate, Version = version, Source = "path" });
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[BrowserProvisioner] PATH probe failed: {ex.Message}"); }
            }

            return found;
        }

        /// <summary>Depth-limited search that tolerates folders we are not allowed to read.</summary>
        private IEnumerable<string> EnumerateDrivers(string root, int depth)
        {
            if (depth < 0 || !Directory.Exists(root)) yield break;

            string[] files;
            try { files = Directory.GetFiles(root, _info.DriverFileName); }
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
        /// A driver's version. Drivers are always stored under a folder named for their version, so
        /// that is tried first — on Linux and macOS the alternative is executing the binary with
        /// <c>--version</c>, which costs about a tenth of a second per candidate.
        /// </summary>
        private Version ReadDriverVersion(string driverPath)
        {
            var folder = Path.GetFileName(Path.GetDirectoryName(driverPath) ?? "");
            if (TryParseVersion(folder, out var fromFolder))
                return fromFolder;

            // WebDriverManager's layout was <version>/X64/chromedriver.exe — check the grandparent.
            var grandparent = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(driverPath) ?? "") ?? "");
            if (TryParseVersion(grandparent, out var fromGrandparent))
                return fromGrandparent;

            return _info.ReadExecutableVersion(driverPath);
        }

        internal static bool TryParseVersion(string raw, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            // Tolerate suffixes such as "151.0.7922.77 (abcdef)".
            var numeric = new string(raw.TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray()).Trim('.');
            if (numeric.Length == 0 || !numeric.Contains('.')) return false;

            return Version.TryParse(numeric, out version);
        }

        /// <summary>
        /// The best driver for a given Chrome: same major version, highest patch. Chrome and
        /// ChromeDriver are released in lockstep, so a matching major is the compatibility contract.
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

        #endregion

        #region Download (last resort)

        /// <summary>
        /// Metadata hosts in the order tried. The version index lives on GitHub Pages, which stays
        /// reachable in places where Google's storage host does not.
        /// </summary>
        private static readonly string[] VersionIndexUrls =
        {
            "https://googlechromelabs.github.io/chrome-for-testing/latest-patch-versions-per-build.json",
            "https://cdn.npmmirror.com/binaries/chrome-for-testing/latest-patch-versions-per-build.json"
        };

        /// <summary>
        /// Binary hosts in the order tried, with <c>{0}</c> = version and <c>{1}</c> = platform key.
        /// Google's own host is last on purpose: it is the one that answers <c>403 Forbidden</c> for
        /// a large share of this app's users, so the mirrors get first refusal.
        /// </summary>
        private static readonly string[] DriverZipUrlTemplates =
        {
            "https://cdn.npmmirror.com/binaries/chrome-for-testing/{0}/{1}/chromedriver-{1}.zip",
            "https://registry.npmmirror.com/-/binary/chrome-for-testing/{0}/{1}/chromedriver-{1}.zip",
            "https://storage.googleapis.com/chrome-for-testing-public/{0}/{1}/chromedriver-{1}.zip"
        };

        private DriverCandidate Download(BrowserInstall chrome, Action<ProvisionStatus> report, CancellationToken ct)
        {
            report?.Invoke(new ProvisionStatus("Browser_St_DownloadingDriver"));

            foreach (var version in CandidateDriverVersions(chrome, ct))
            {
                foreach (var template in DriverZipUrlTemplates)
                {
                    ct.ThrowIfCancellationRequested();
                    var url = string.Format(template, version, _info.DriverPlatformKey);
                    try
                    {
                        var bytes = Http.GetByteArrayAsync(url, ct).GetAwaiter().GetResult();
                        var path = ExtractDriver(bytes, version);
                        if (path != null)
                        {
                            var actual = ReadDriverVersion(path) ?? Version.Parse(version);
                            report?.Invoke(new ProvisionStatus("Browser_St_DriverDownloaded", actual.ToString()));
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
        private IEnumerable<string> CandidateDriverVersions(BrowserInstall chrome, CancellationToken ct)
        {
            var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var exact = chrome.Version.ToString();
            if (tried.Add(exact)) yield return exact;

            foreach (var v in VersionsFromIndex(chrome, ct))
                if (tried.Add(v)) yield return v;
        }

        private static IEnumerable<string> VersionsFromIndex(BrowserInstall chrome, CancellationToken ct)
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

                    // Chrome may sit on a build with no published driver; the newest driver of the
                    // same major is the next best thing.
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
        /// Unpacks the driver from a Chrome-for-Testing zip into the per-user cache. Writing under
        /// the user's data directory rather than the install folder keeps this working without
        /// elevation and never disturbs the driver that shipped with the installer.
        /// </summary>
        private string ExtractDriver(byte[] zipBytes, string version)
        {
            using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
            var entry = archive.Entries.FirstOrDefault(
                e => string.Equals(e.Name, _info.DriverFileName, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;

            var dir = Path.Combine(UserDriverCache, version);
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, _info.DriverFileName);

            // Extract next to the target and swap, so an interrupted download can never leave a
            // truncated driver behind for the next run to pick up.
            var temp = target + ".tmp";
            entry.ExtractToFile(temp, overwrite: true);
            File.Move(temp, target, overwrite: true);

            // Zip archives do not carry the Unix execute bit; without this the driver fails to start
            // on Linux and macOS with a bare "permission denied".
            _info.MakeExecutable(target);

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

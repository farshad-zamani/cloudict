using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
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

            /// <summary>The helper browser's executable. Named for Chrome, which it was until 3.2.6.</summary>
            public string ChromePath { get; init; }
            public string ChromeVersion { get; init; }

            /// <summary>Which browser this is — and so which driver protocol to speak to it.</summary>
            public BrowserKind Kind { get; init; } = BrowserKind.Chrome;

            /// <summary>The browser, as installed — for its display name and version.</summary>
            public BrowserInstall Browser { get; init; }

            /// <summary>
            /// True when this browser's speech recognition has not yet been seen to work on this
            /// machine and network, so the engine should check before relying on it. Only ever set
            /// for Edge — see <see cref="RecordSpeechCheck"/>.
            /// </summary>
            public bool NeedsSpeechCheck { get; init; }

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
        /// Picks the helper browser and the driver to go with it.
        ///
        /// <para>Automatic — the default — goes in the order that keeps Google's own speech
        /// recognition wherever it can: installed Chrome; a Chrome for Testing that Cloudict already
        /// downloaded; Edge, if its speech service has not been seen failing here; and finally a
        /// Chrome for Testing downloaded now. A named preference uses that browser or explains why
        /// it cannot.</para>
        /// </summary>
        /// <param name="report">Receives progress steps for the status bar.</param>
        /// <param name="allowDownload">When false, never touches the network.</param>
        /// <param name="preference">One of <see cref="HelperBrowsers"/>.</param>
        /// <param name="skip">Browsers already tried and found unusable in this session.</param>
        /// <exception cref="ProvisionException">No usable browser, or no driver could be obtained.</exception>
        public Provision Resolve(Action<ProvisionStatus> report, bool allowDownload = true, CancellationToken ct = default,
                                 string preference = HelperBrowsers.Auto, ICollection<BrowserKind> skip = null)
        {
            skip ??= Array.Empty<BrowserKind>();
            report?.Invoke(new ProvisionStatus("Browser_St_LookingForChrome"));

            switch (HelperBrowsers.Normalise(preference))
            {
                case HelperBrowsers.Chrome:
                    return ResolveChromium(_locator.FindChrome() ?? throw new ProvisionException("Browser_Err_ChromeNotInstalled"),
                                           report, allowDownload, ct);

                case HelperBrowsers.Edge:
                    var chosenEdge = _locator.FindEdge() ?? throw new ProvisionException("Browser_Err_EdgeNotInstalled");
                    return ResolveEdge(chosenEdge, report, allowDownload, ct);

                case HelperBrowsers.ChromeForTesting:
                    return ResolveChromeForTesting(report, allowDownload, ct);
            }

            // Automatic.
            if (!skip.Contains(BrowserKind.Chrome) && _locator.FindChrome() is BrowserInstall chrome)
                return ResolveChromium(chrome, report, allowDownload, ct);

            // A Chrome for Testing already on disk is Google's recognition with nothing to fetch:
            // better than Edge's, and it means Edge's speech is not re-tested on every start.
            if (!skip.Contains(BrowserKind.ChromeForTesting) && FindInstalledChromeForTesting() is BrowserInstall cached)
                return ResolveChromium(cached, report, allowDownload, ct);

            if (!skip.Contains(BrowserKind.Edge) && _locator.FindEdge() is BrowserInstall edge && !SpeechRecentlyFailed(edge))
            {
                try { return ResolveEdge(edge, report, allowDownload, ct); }
                catch (ProvisionException ex) { Debug.WriteLine($"[BrowserProvisioner] Edge unusable: {ex.Message}"); }
            }

            if (!skip.Contains(BrowserKind.ChromeForTesting) && allowDownload)
                return ResolveChromeForTesting(report, allowDownload, ct);

            throw new ProvisionException("Browser_Err_NoBrowser");
        }

        /// <summary>
        /// Chrome or Chrome for Testing, with a matching ChromeDriver: from disk first, the network
        /// only when the browser has outrun every local driver. Unchanged from when Chrome was the
        /// only choice.
        /// </summary>
        private Provision ResolveChromium(BrowserInstall chrome, Action<ProvisionStatus> report, bool allowDownload, CancellationToken ct)
        {
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

        private static Provision Describe(DriverCandidate driver, BrowserInstall browser, bool buildCheckOverride,
                                          bool needsSpeechCheck = false) =>
            new Provision
            {
                DriverPath = driver.Path,
                DriverVersion = driver.Version.ToString(),
                DriverSource = driver.Source,
                ChromePath = browser.Path,
                ChromeVersion = browser.Version.ToString(),
                Kind = browser.Kind,
                Browser = browser,
                RequiresBuildCheckOverride = buildCheckOverride,
                NeedsSpeechCheck = needsSpeechCheck
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

            // Inside a macOS .app the driver lives in Contents/Resources rather than beside the
            // executable in Contents/MacOS. That is where Apple expects a helper tool, and it is
            // not merely a convention: codesign treats any directory holding a binary under
            // Contents/MacOS as a nested bundle, could not make sense of "Drivers/152.0.7977.42",
            // and refused to sign the app at all — leaving a build macOS would not launch.
            // Harmless everywhere else, where the path simply does not exist.
            Scan(Path.Combine(appDir, "..", "Resources", BundledDriverFolder), "bundled");

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
        private IEnumerable<string> EnumerateDrivers(string root, int depth) => EnumerateFiles(root, _info.DriverFileName, depth);

        private static IEnumerable<string> EnumerateFiles(string root, string fileName, int depth)
        {
            if (depth < 0 || !Directory.Exists(root)) yield break;

            string[] files;
            try { files = Directory.GetFiles(root, fileName); }
            catch (Exception ex) { Debug.WriteLine($"[BrowserProvisioner] cannot list {root}: {ex.Message}"); yield break; }

            foreach (var f in files) yield return f;

            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch (Exception ex) { Debug.WriteLine($"[BrowserProvisioner] cannot list {root}: {ex.Message}"); yield break; }

            foreach (var d in dirs)
                foreach (var f in EnumerateFiles(d, fileName, depth - 1))
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
                        var bytes = GetBytes(url, ct);
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
                    var json = System.Text.Encoding.UTF8.GetString(GetBytes(indexUrl, ct));
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

        #endregion

        #region Microsoft Edge

        /// <summary>Where downloaded EdgeDrivers are kept, one folder per version.</summary>
        private string EdgeDriverCache => Path.Combine(UserDriverCache, "edge");

        /// <summary>
        /// Microsoft's hosts for EdgeDriver. Both serve the same files; the second is the older
        /// address, kept because some networks reach one and not the other.
        /// </summary>
        private static readonly string[] EdgeDriverUrlTemplates =
        {
            "https://msedgedriver.microsoft.com/{0}/edgedriver_{1}.zip",
            "https://msedgedriver.azureedge.net/{0}/edgedriver_{1}.zip"
        };

        /// <summary>
        /// Edge with a matching EdgeDriver. ChromeDriver cannot drive Edge — measured: it times out
        /// creating the session — so Edge needs Microsoft's driver, matched to Edge's version. None
        /// ships in the installer; it is fetched once, on the first use of Edge, and kept.
        /// </summary>
        private Provision ResolveEdge(BrowserInstall edge, Action<ProvisionStatus> report, bool allowDownload, CancellationToken ct)
        {
            report?.Invoke(new ProvisionStatus("Browser_St_LookingForDriver"));

            var local = EnumerateFiles(EdgeDriverCache, _info.EdgeDriverFileName, 2)
                .Select(path => new DriverCandidate { Path = path, Version = ReadDriverVersion(path), Source = "cache" })
                .Where(c => c.Version != null)
                .ToList();

            var needsCheck = !SpeechKnownToWork(edge);

            var exact = local.FirstOrDefault(c => c.Version == edge.Version);
            if (exact != null) return Describe(exact, edge, buildCheckOverride: false, needsCheck);

            var sameMajor = PickBest(local, edge.Major);

            if (sameMajor == null && allowDownload)
            {
                var downloaded = DownloadEdgeDriver(edge, report, ct);
                if (downloaded != null) return Describe(downloaded, edge, buildCheckOverride: false, needsCheck);
            }

            if (sameMajor != null) return Describe(sameMajor, edge, buildCheckOverride: false, needsCheck);

            var closest = PickClosest(local, edge.Major);
            if (closest != null) return Describe(closest, edge, buildCheckOverride: true, needsCheck);

            throw new ProvisionException("Browser_Err_NoDriver", $"(Microsoft Edge {edge.Version})");
        }

        private DriverCandidate DownloadEdgeDriver(BrowserInstall edge, Action<ProvisionStatus> report, CancellationToken ct)
        {
            report?.Invoke(new ProvisionStatus("Browser_St_DownloadingDriver"));

            var versions = new List<string> { edge.Version.ToString() };
            var latest = LatestEdgeDriverForMajor(edge.Major, ct);
            if (latest != null && !versions.Contains(latest)) versions.Add(latest);

            foreach (var version in versions)
            {
                foreach (var template in EdgeDriverUrlTemplates)
                {
                    ct.ThrowIfCancellationRequested();
                    var url = string.Format(template, version, _info.EdgeDriverPlatformKey);
                    try
                    {
                        var path = ExtractNamedFile(GetBytes(url, ct), _info.EdgeDriverFileName, Path.Combine(EdgeDriverCache, version));
                        if (path == null) continue;

                        report?.Invoke(new ProvisionStatus("Browser_St_DriverDownloaded", version));
                        return new DriverCandidate { Path = path, Version = Version.Parse(version), Source = "downloaded" };
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { Debug.WriteLine($"[BrowserProvisioner] {url} -> {ex.Message}"); }
                }
            }

            return null;
        }

        /// <summary>
        /// The newest EdgeDriver Microsoft has published for an Edge major version, for when the
        /// exact build has none. The file is UTF-16 with a byte-order mark, so it is decoded by the
        /// mark rather than assumed.
        /// </summary>
        private string LatestEdgeDriverForMajor(int major, CancellationToken ct)
        {
            var os = _info.EdgeDriverPlatformKey.StartsWith("win", StringComparison.Ordinal) ? "WINDOWS"
                   : _info.EdgeDriverPlatformKey.StartsWith("mac", StringComparison.Ordinal) ? "MACOS" : "LINUX";

            foreach (var host in new[] { "https://msedgedriver.microsoft.com", "https://msedgedriver.azureedge.net" })
            {
                try
                {
                    var bytes = GetBytes($"{host}/LATEST_RELEASE_{major}_{os}", ct);
                    using var reader = new StreamReader(new MemoryStream(bytes), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    var text = reader.ReadToEnd().Trim().Trim('\0');
                    if (Version.TryParse(text, out _)) return text;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Debug.WriteLine($"[BrowserProvisioner] latest EdgeDriver from {host}: {ex.Message}"); }
            }

            return null;
        }

        #endregion

        #region Speech check results

        private sealed class SpeechCheck
        {
            public string Version { get; set; }
            public bool Works { get; set; }
            public DateTime CheckedUtc { get; set; }
        }

        private string SpeechCheckFile => Path.Combine(_paths.DataDirectory, "speech-checks.json");

        private static readonly object SpeechCheckGate = new object();

        /// <summary>
        /// Remembers whether a browser's speech recognition worked here, so the check runs once per
        /// browser version rather than at every start.
        ///
        /// <para>Edge is the only browser this is kept for. Its recognition runs on Microsoft's
        /// service, which a network may not reach — measured from one network, the connection was
        /// refused while Chrome's, to Google, went through — and the page gives no sign of that: the
        /// microphone button simply hears nothing. Chrome and Chrome for Testing reach Google
        /// exactly as Chrome always has.</para>
        /// </summary>
        public void RecordSpeechCheck(BrowserInstall browser, bool works)
        {
            if (browser == null) return;

            lock (SpeechCheckGate)
            {
                try
                {
                    var all = ReadSpeechChecks();
                    all[browser.Kind.ToString()] = new SpeechCheck { Version = browser.Version?.ToString(), Works = works, CheckedUtc = DateTime.UtcNow };
                    Directory.CreateDirectory(_paths.DataDirectory);
                    File.WriteAllText(SpeechCheckFile, Newtonsoft.Json.JsonConvert.SerializeObject(all, Newtonsoft.Json.Formatting.Indented));
                }
                catch (Exception ex) { Debug.WriteLine($"[BrowserProvisioner] speech check not saved: {ex.Message}"); }
            }
        }

        /// <summary>Seen working on this version.</summary>
        private bool SpeechKnownToWork(BrowserInstall browser)
        {
            var check = ReadSpeechCheck(browser);
            return check != null && check.Works && check.Version == browser.Version?.ToString();
        }

        /// <summary>
        /// Seen failing on this version within the last day. A failure is not kept for longer
        /// because the network may change — a VPN switched on, a firewall rule lifted — and a day's
        /// wait before trying again costs nothing when Chrome for Testing is covering meanwhile.
        /// </summary>
        private bool SpeechRecentlyFailed(BrowserInstall browser)
        {
            var check = ReadSpeechCheck(browser);
            return check != null && !check.Works
                   && check.Version == browser.Version?.ToString()
                   && DateTime.UtcNow - check.CheckedUtc < TimeSpan.FromDays(1);
        }

        private SpeechCheck ReadSpeechCheck(BrowserInstall browser)
        {
            lock (SpeechCheckGate)
            {
                return ReadSpeechChecks().TryGetValue(browser.Kind.ToString(), out var check) ? check : null;
            }
        }

        private Dictionary<string, SpeechCheck> ReadSpeechChecks()
        {
            try
            {
                if (File.Exists(SpeechCheckFile))
                    return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, SpeechCheck>>(File.ReadAllText(SpeechCheckFile))
                           ?? new Dictionary<string, SpeechCheck>();
            }
            catch (Exception ex) { Debug.WriteLine($"[BrowserProvisioner] speech checks unreadable: {ex.Message}"); }

            return new Dictionary<string, SpeechCheck>();
        }

        #endregion

        #region Chrome for Testing

        /// <summary>Where Chrome for Testing is unpacked, one folder per version.</summary>
        public string ChromeForTestingRoot => Path.Combine(_paths.DataDirectory, "Browser", "chrome-for-testing");

        /// <summary>Written last, so a half-unpacked download is never mistaken for a browser.</summary>
        private const string CompleteMarker = ".complete";

        /// <summary>
        /// How far behind the bundled driver a downloaded Chrome for Testing may fall before it is
        /// replaced. It does not update itself, and Google Translate eventually stops supporting
        /// old browsers; four major versions is about six months.
        /// </summary>
        private const int MaxChromeForTestingLag = 4;

        private static readonly string[] ChromeForTestingZipUrlTemplates =
        {
            "https://cdn.npmmirror.com/binaries/chrome-for-testing/{0}/{1}/chrome-{1}.zip",
            "https://registry.npmmirror.com/-/binary/chrome-for-testing/{0}/{1}/chrome-{1}.zip",
            "https://storage.googleapis.com/chrome-for-testing-public/{0}/{1}/chrome-{1}.zip"
        };

        private static readonly string[] StableVersionUrls =
        {
            "https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions.json",
            "https://cdn.npmmirror.com/binaries/chrome-for-testing/last-known-good-versions.json"
        };

        /// <summary>
        /// Google's own Chrome build for automation: the same browser as Chrome, carrying the same
        /// Google API key — measured: its speech requests went to the same Google endpoint with the
        /// same key and came back 200, exactly like Chrome's — but portable, never auto-updating, and
        /// published alongside a ChromeDriver of the identical version. That last part is why the
        /// version downloaded is the one matching the driver already in the installer: no driver
        /// download, and no version mismatch, ever.
        ///
        /// <para>Downloaded rather than shipped: it is about 200 MB, and only users without Chrome
        /// need it.</para>
        /// </summary>
        private Provision ResolveChromeForTesting(Action<ProvisionStatus> report, bool allowDownload, CancellationToken ct)
        {
            var target = BundledDriverVersion();
            var installed = FindInstalledChromeForTesting();

            var installedIsCurrent = installed != null && (target == null || installed.Major >= target.Major - MaxChromeForTestingLag);
            if (installedIsCurrent) return ResolveChromium(installed, report, allowDownload, ct);

            if (!allowDownload)
            {
                if (installed != null) return ResolveChromium(installed, report, allowDownload, ct);
                throw new ProvisionException("Browser_Err_NoBrowser");
            }

            var version = target?.ToString() ?? LatestStableChromeForTesting(ct)
                          ?? throw new ProvisionException("Browser_Err_ChromeForTestingDownloadFailed");

            var fresh = DownloadChromeForTesting(version, report, ct);
            if (fresh != null) return ResolveChromium(fresh, report, allowDownload, ct);

            if (installed != null) return ResolveChromium(installed, report, allowDownload, ct);
            throw new ProvisionException("Browser_Err_ChromeForTestingDownloadFailed");
        }

        /// <summary>The newest Chrome for Testing already unpacked, or null.</summary>
        public BrowserInstall FindInstalledChromeForTesting()
        {
            try
            {
                if (!Directory.Exists(ChromeForTestingRoot)) return null;

                return Directory.GetDirectories(ChromeForTestingRoot)
                    .Select(dir => new
                    {
                        Dir = dir,
                        Ok = TryParseVersion(Path.GetFileName(dir), out var v),
                        Version = v
                    })
                    .Where(x => x.Ok && File.Exists(Path.Combine(x.Dir, CompleteMarker)))
                    .Select(x => new BrowserInstall
                    {
                        Path = Path.Combine(x.Dir, _info.ChromeForTestingExecutable),
                        Version = x.Version,
                        Kind = BrowserKind.ChromeForTesting
                    })
                    .Where(b => File.Exists(b.Path))
                    .OrderByDescending(b => b.Version)
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BrowserProvisioner] cannot read Chrome for Testing folder: {ex.Message}");
                return null;
            }
        }

        /// <summary>The version of the newest driver shipped in the installer.</summary>
        private Version BundledDriverVersion()
        {
            var appDir = AppContext.BaseDirectory;
            return new[] { Path.Combine(appDir, BundledDriverFolder), Path.Combine(appDir, "..", "Resources", BundledDriverFolder) }
                .SelectMany(root => EnumerateDrivers(root, MaxSearchDepth))
                .Select(ReadDriverVersion)
                .Where(v => v != null)
                .OrderByDescending(v => v)
                .FirstOrDefault();
        }

        private static string LatestStableChromeForTesting(CancellationToken ct)
        {
            foreach (var url in StableVersionUrls)
            {
                try
                {
                    var json = Newtonsoft.Json.Linq.JObject.Parse(System.Text.Encoding.UTF8.GetString(GetBytes(url, ct)));
                    var version = json["channels"]?["Stable"]?["version"]?.ToString();
                    if (Version.TryParse(version, out _)) return version;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Debug.WriteLine($"[BrowserProvisioner] stable version from {url}: {ex.Message}"); }
            }

            return null;
        }

        private BrowserInstall DownloadChromeForTesting(string version, Action<ProvisionStatus> report, CancellationToken ct)
        {
            Directory.CreateDirectory(ChromeForTestingRoot);
            var zip = Path.Combine(ChromeForTestingRoot, $"download-{version}.zip");
            var unpacking = Path.Combine(ChromeForTestingRoot, version + ".partial");
            var final = Path.Combine(ChromeForTestingRoot, version);

            foreach (var template in ChromeForTestingZipUrlTemplates)
            {
                ct.ThrowIfCancellationRequested();
                var url = string.Format(template, version, _info.DriverPlatformKey);

                try
                {
                    DownloadToFile(url, zip, (done, total) =>
                    {
                        var percent = total > 0 ? (int)(done * 100 / total) : 0;
                        var megabytes = total > 0 ? (int)(total / (1024 * 1024)) : 0;
                        report?.Invoke(new ProvisionStatus("Browser_St_DownloadingBrowser_Fmt", percent, megabytes));
                    }, ct);

                    report?.Invoke(new ProvisionStatus("Browser_St_UnpackingBrowser"));

                    if (Directory.Exists(unpacking)) Directory.Delete(unpacking, recursive: true);
                    _info.ExtractArchive(zip, unpacking);

                    var exe = Path.Combine(unpacking, _info.ChromeForTestingExecutable);
                    if (!File.Exists(exe)) throw new IOException($"{_info.ChromeForTestingExecutable} missing from the archive");
                    _info.MakeExecutable(exe);

                    if (Directory.Exists(final)) Directory.Delete(final, recursive: true);
                    Directory.Move(unpacking, final);
                    File.WriteAllText(Path.Combine(final, CompleteMarker), DateTime.UtcNow.ToString("o"));

                    RemoveOtherChromeForTestingVersions(keep: final);

                    return new BrowserInstall
                    {
                        Path = Path.Combine(final, _info.ChromeForTestingExecutable),
                        Version = Version.Parse(version),
                        Kind = BrowserKind.ChromeForTesting
                    };
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BrowserProvisioner] Chrome for Testing from {url}: {ex.Message}");
                }
                finally
                {
                    TryDelete(zip);
                    try { if (Directory.Exists(unpacking)) Directory.Delete(unpacking, recursive: true); }
                    catch (Exception ex) { Debug.WriteLine($"[BrowserProvisioner] cleanup: {ex.Message}"); }
                }
            }

            return null;
        }

        /// <summary>Older downloads are hundreds of megabytes each; only the one in use is kept.</summary>
        private void RemoveOtherChromeForTestingVersions(string keep)
        {
            foreach (var dir in Directory.GetDirectories(ChromeForTestingRoot))
            {
                if (string.Equals(Path.GetFullPath(dir), Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase)) continue;
                try { Directory.Delete(dir, recursive: true); }
                catch (Exception ex) { Debug.WriteLine($"[BrowserProvisioner] could not remove {dir}: {ex.Message}"); }
            }
        }

        private static void TryDelete(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (Exception ex) { Debug.WriteLine($"[BrowserProvisioner] could not delete {file}: {ex.Message}"); }
        }

        /// <summary>
        /// Streams a large download to disk with progress, giving up only if the connection stalls —
        /// a 200 MB file on a slow line can legitimately take longer than any fixed timeout.
        /// </summary>
        private static void DownloadToFile(string url, string path, Action<long, long> progress, CancellationToken ct)
        {
            var temp = path + ".tmp";
            TryDelete(temp);

            using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stall.CancelAfter(TimeSpan.FromSeconds(60));

            HttpResponseMessage response;
            try
            {
                response = LargeDownloads.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token).GetAwaiter().GetResult();
            }
            catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException)
            {
                response = LargeDownloadsLenient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token).GetAwaiter().GetResult();
            }

            using (response)
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? -1;

                using (var source = response.Content.ReadAsStream(stall.Token))
                using (var target = File.Create(temp))
                {
                    var buffer = new byte[128 * 1024];
                    long done = 0;
                    var lastPercent = -1L;

                    while (true)
                    {
                        stall.CancelAfter(TimeSpan.FromSeconds(60));
                        var read = source.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;

                        target.Write(buffer, 0, read);
                        done += read;

                        var percent = total > 0 ? done * 100 / total : -1;
                        if (percent != lastPercent && percent % 2 == 0) { progress?.Invoke(done, total); lastPercent = percent; }
                    }

                    if (total > 0 && done != total) throw new IOException($"download ended at {done} of {total} bytes");
                }
            }

            File.Move(temp, path, overwrite: true);
        }

        private static readonly HttpClient LargeDownloads = CreateLargeDownloadClient(allowCertificateDownloads: false);
        private static readonly HttpClient LargeDownloadsLenient = CreateLargeDownloadClient(allowCertificateDownloads: true);

        private static HttpClient CreateLargeDownloadClient(bool allowCertificateDownloads)
        {
            var client = CreateHttpClient(allowCertificateDownloads);
            client.Timeout = Timeout.InfiniteTimeSpan;
            return client;
        }

        /// <summary>Extracts one named file from a zip into a folder, via a temporary name.</summary>
        private string ExtractNamedFile(byte[] zipBytes, string fileName, string directory)
        {
            using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
            var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Name, fileName, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;

            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, fileName);
            var temp = target + ".tmp";
            entry.ExtractToFile(temp, overwrite: true);
            File.Move(temp, target, overwrite: true);
            _info.MakeExecutable(target);
            return target;
        }

        #endregion

        #region HTTP

        /// <summary>
        /// Fetches a URL without letting Windows write to its certificate store, falling back to
        /// a normal fetch only when that is the reason the first one failed.
        ///
        /// <para>Building a TLS chain for a host whose root the machine has not cached makes
        /// CryptoAPI download the root and write it into HKLM's AuthRoot store — from inside this
        /// process — and an antivirus reports that as the application "modifying certificate
        /// publisher". The first attempt forbids the download. On the rare machine that genuinely
        /// lacks the root, the handshake fails with an authentication error, and only then is the
        /// download allowed: getting a driver still matters more than avoiding one prompt.</para>
        /// </summary>
        private static byte[] GetBytes(string url, CancellationToken ct)
        {
            try
            {
                return Http.GetByteArrayAsync(url, ct).GetAwaiter().GetResult();
            }
            catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException)
            {
                Debug.WriteLine($"[BrowserProvisioner] chain could not be built offline for {url}; retrying with certificate downloads allowed");
                return HttpLenient.GetByteArrayAsync(url, ct).GetAwaiter().GetResult();
            }
        }

        private static readonly HttpClient HttpLenient = CreateHttpClient(allowCertificateDownloads: true);

        private static HttpClient CreateHttpClient() => CreateHttpClient(allowCertificateDownloads: false);

        private static HttpClient CreateHttpClient(bool allowCertificateDownloads)
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                UseProxy = true
            };

            if (!allowCertificateDownloads)
            {
                // Revocation stays off, as it is for HttpClient by default.
                handler.SslOptions.CertificateChainPolicy = new X509ChainPolicy
                {
                    DisableCertificateDownloads = true,
                    RevocationMode = X509RevocationMode.NoCheck
                };
            }

            var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Cloudict");
            return client;
        }

        #endregion
    }
}

using System;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Cloudict.Services
{
    /// <summary>A release newer than the one running.</summary>
    public sealed class UpdateInfo
    {
        /// <summary>The new version, without the leading "v".</summary>
        public string Version { get; init; }

        /// <summary>The release page, which is where the user is sent.</summary>
        public string ReleaseUrl { get; init; }

        /// <summary>
        /// The installer for this machine, when one could be identified. Null when the release
        /// carries nothing recognisable for this platform, in which case only the page is offered.
        /// </summary>
        public string DownloadUrl { get; init; }

        /// <summary>The installer's file name, for showing what the direct link will fetch.</summary>
        public string DownloadName { get; init; }
    }

    /// <summary>
    /// Asks GitHub whether a newer release exists.
    ///
    /// <para>Deliberately quiet about failure. Cloudict is used behind restrictive networks — the
    /// driver downloads already have to route around a host that answers 403 in some countries — so
    /// an update check that cannot reach GitHub must look exactly like one that found nothing. It
    /// never blocks startup, never retries in a loop, and never reports an error to the user: the
    /// worst outcome of this feature failing is that someone keeps the version they already have.
    /// </para>
    ///
    /// <para>Read-only and unauthenticated. The endpoint allows 60 requests an hour from an address,
    /// and Cloudict asks once a day.</para>
    /// </summary>
    public sealed class UpdateChecker
    {
        private const string LatestReleaseUrl =
            "https://api.github.com/repos/farshad-zamani/cloudict/releases/latest";

        /// <summary>Where to send someone whose platform has no recognisable asset.</summary>
        public const string ReleasesPage = "https://github.com/farshad-zamani/cloudict/releases/latest";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            // Short on purpose: this runs in the background and nothing waits for it, but a socket
            // left hanging for minutes on a blocked network is still a socket left hanging.
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // GitHub rejects requests without one.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Cloudict/" + AppInfo.Version);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            return client;
        }

        /// <summary>
        /// Returns the newer release, or null when this is the newest, when the answer cannot be
        /// understood, or when GitHub could not be reached at all.
        /// </summary>
        public async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
        {
            try
            {
                var json = await Http.GetStringAsync(LatestReleaseUrl, ct).ConfigureAwait(false);
                var release = JObject.Parse(json);

                var tag = (string)release["tag_name"];
                if (string.IsNullOrWhiteSpace(tag)) return null;

                var latest = tag.TrimStart('v', 'V').Trim();
                if (!IsNewerThanRunning(latest)) return null;

                var asset = PickAssetForThisMachine(release["assets"] as JArray);

                DiagnosticLog.Write("UpdateChecker", $"{latest} is available (running {AppInfo.Version})");

                return new UpdateInfo
                {
                    Version = latest,
                    ReleaseUrl = (string)release["html_url"] ?? ReleasesPage,
                    DownloadUrl = asset?.Url,
                    DownloadName = asset?.Name
                };
            }
            catch (Exception ex)
            {
                // Offline, blocked, rate-limited, or GitHub changed its shape. None of these are
                // the user's problem and none of them are worth a message.
                DiagnosticLog.Write("UpdateChecker", $"check skipped: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Compares by component rather than by string, so 3.1.20 is correctly newer than 3.1.9 —
        /// which "3.1.20" &gt; "3.1.9" as text is not.
        /// </summary>
        internal static bool IsNewerThanRunning(string candidate) =>
            IsNewer(candidate, AppInfo.Version);

        internal static bool IsNewer(string candidate, string running)
        {
            if (!Version.TryParse(Normalise(candidate), out var newer)) return false;
            if (!Version.TryParse(Normalise(running), out var current)) return false;

            return newer > current;
        }

        /// <summary>Version.TryParse wants at least two components and no extra label.</summary>
        private static string Normalise(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var trimmed = value.Trim().TrimStart('v', 'V');

            // Drop anything after a pre-release or build marker: "3.2.0-beta1" compares as 3.2.0.
            var cut = trimmed.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut > 0) trimmed = trimmed.Substring(0, cut);

            return trimmed.Contains('.') ? trimmed : trimmed + ".0";
        }

        private sealed record Asset(string Name, string Url);

        /// <summary>
        /// Picks the file this machine can actually install, so the user is offered a download
        /// rather than a list to choose from. Returns null when nothing matches, and the caller
        /// then offers only the release page.
        /// </summary>
        private static Asset PickAssetForThisMachine(JArray assets)
        {
            if (assets == null) return null;

            var candidates = assets
                .Select(a => new Asset((string)a["name"], (string)a["browser_download_url"]))
                .Where(a => !string.IsNullOrEmpty(a.Name) && !string.IsNullOrEmpty(a.Url))
                .ToList();

            if (candidates.Count == 0) return null;

            bool Named(Asset a, params string[] parts) =>
                parts.All(p => a.Name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);

            if (OperatingSystem.IsWindows())
                return candidates.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (OperatingSystem.IsMacOS())
            {
                // The wrong architecture will not launch, so an unmatched Mac gets the page instead.
                var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
                return candidates.FirstOrDefault(a => Named(a, ".dmg", arch));
            }

            if (OperatingSystem.IsLinux())
            {
                // Whichever package manager this machine actually uses; the AppImage suits neither
                // and both, so it comes last.
                if (System.IO.File.Exists("/usr/bin/dpkg"))
                    return candidates.FirstOrDefault(a => a.Name.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
                        ?? candidates.FirstOrDefault(a => a.Name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase));

                if (System.IO.File.Exists("/usr/bin/rpm"))
                    return candidates.FirstOrDefault(a => a.Name.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase))
                        ?? candidates.FirstOrDefault(a => a.Name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase));

                return candidates.FirstOrDefault(a => a.Name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }
    }
}

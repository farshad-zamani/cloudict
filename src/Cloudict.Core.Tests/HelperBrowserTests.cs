using System;
using System.Collections.Generic;
using System.IO;
using Cloudict.Abstractions;
using Cloudict.Speech;
using Xunit;

namespace Cloudict.Core.Tests
{
    /// <summary>
    /// Which browser hosts Google Translate. Measured before this was written: Chrome and Chrome for
    /// Testing reach Google's speech service with Google's key (200); Brave reaches it with its own
    /// key and is refused (403); Edge uses Microsoft's service, which a network may not reach.
    /// </summary>
    public sealed class HelperBrowserTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "cloudict-browser-tests-" + Guid.NewGuid().ToString("N"));
        private readonly FakeInfo _info = new FakeInfo();
        private readonly FakeLocator _locator = new FakeLocator();
        private readonly FakePaths _paths;

        public HelperBrowserTests()
        {
            _paths = new FakePaths(_root);
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        private BrowserProvisioner Provisioner() => new BrowserProvisioner(_paths, _info, _locator);

        [Theory]
        [InlineData(null, "auto")]
        [InlineData("", "auto")]
        [InlineData("brave", "auto")]
        [InlineData("CHROME", "chrome")]
        [InlineData(" edge ", "edge")]
        [InlineData("cft", "cft")]
        public void Unknown_or_old_values_mean_automatic(string stored, string expected) =>
            Assert.Equal(expected, HelperBrowsers.Normalise(stored));

        [Fact]
        public void A_new_settings_file_is_automatic() =>
            Assert.Equal(HelperBrowsers.Auto, new AppSettings().HelperBrowser);

        [Fact]
        public void Automatic_uses_installed_Chrome_first()
        {
            _locator.Chrome = Install("chrome", "154.0.8037.95", BrowserKind.Chrome);
            _locator.Edge = Install("edge", "154.0.4258.53", BrowserKind.Edge);
            Driver(Path.Combine(_root, "data", "Drivers", "154.0.8037.92"), "chromedriver.exe");

            var p = Provisioner().Resolve(null, allowDownload: false);

            Assert.Equal(BrowserKind.Chrome, p.Kind);
            Assert.False(p.NeedsSpeechCheck);
        }

        [Fact]
        public void Without_Chrome_automatic_takes_Edge_and_asks_for_a_speech_check()
        {
            _locator.Edge = Install("edge", "154.0.4258.53", BrowserKind.Edge);
            EdgeDriver("154.0.4258.53");

            var p = Provisioner().Resolve(null, allowDownload: false);

            Assert.Equal(BrowserKind.Edge, p.Kind);
            Assert.True(p.NeedsSpeechCheck);
            Assert.EndsWith("msedgedriver.exe", p.DriverPath);
        }

        [Fact]
        public void Edge_seen_working_is_not_checked_again()
        {
            _locator.Edge = Install("edge", "154.0.4258.53", BrowserKind.Edge);
            EdgeDriver("154.0.4258.53");
            var provisioner = Provisioner();

            provisioner.RecordSpeechCheck(_locator.Edge, works: true);

            Assert.False(provisioner.Resolve(null, allowDownload: false).NeedsSpeechCheck);
        }

        [Fact]
        public void A_new_Edge_version_is_checked_again()
        {
            _locator.Edge = Install("edge", "154.0.4258.53", BrowserKind.Edge);
            EdgeDriver("155.0.4300.10");
            var provisioner = Provisioner();
            provisioner.RecordSpeechCheck(_locator.Edge, works: true);

            _locator.Edge = Install("edge2", "155.0.4300.10", BrowserKind.Edge);

            Assert.True(provisioner.Resolve(null, allowDownload: false).NeedsSpeechCheck);
        }

        [Fact]
        public void Edge_seen_failing_is_skipped_in_automatic_mode()
        {
            _locator.Edge = Install("edge", "154.0.4258.53", BrowserKind.Edge);
            EdgeDriver("154.0.4258.53");
            var provisioner = Provisioner();
            provisioner.RecordSpeechCheck(_locator.Edge, works: false);

            // Nothing else is available offline, so this has to say so rather than hand back Edge.
            var ex = Assert.Throws<BrowserProvisioner.ProvisionException>(() => provisioner.Resolve(null, allowDownload: false));
            Assert.Equal("Browser_Err_NoBrowser", ex.MessageKey);
        }

        [Fact]
        public void A_downloaded_Chrome_for_Testing_comes_before_Edge()
        {
            _locator.Edge = Install("edge", "154.0.4258.53", BrowserKind.Edge);
            EdgeDriver("154.0.4258.53");
            ChromeForTesting("154.0.8037.92");
            Driver(Path.Combine(_root, "data", "Drivers", "154.0.8037.92"), "chromedriver.exe");

            var p = Provisioner().Resolve(null, allowDownload: false);

            Assert.Equal(BrowserKind.ChromeForTesting, p.Kind);
            Assert.False(p.NeedsSpeechCheck);
        }

        [Fact]
        public void A_half_unpacked_Chrome_for_Testing_is_ignored()
        {
            ChromeForTesting("154.0.8037.92", complete: false);
            Assert.Null(Provisioner().FindInstalledChromeForTesting());
        }

        [Fact]
        public void Skipping_Edge_after_a_failed_check_moves_on()
        {
            _locator.Edge = Install("edge", "154.0.4258.53", BrowserKind.Edge);
            EdgeDriver("154.0.4258.53");
            ChromeForTesting("154.0.8037.92");
            Driver(Path.Combine(_root, "data", "Drivers", "154.0.8037.92"), "chromedriver.exe");

            var p = Provisioner().Resolve(null, allowDownload: false, skip: new List<BrowserKind> { BrowserKind.ChromeForTesting });
            Assert.Equal(BrowserKind.Edge, p.Kind);

            var q = Provisioner().Resolve(null, allowDownload: false, skip: new List<BrowserKind> { BrowserKind.Edge });
            Assert.Equal(BrowserKind.ChromeForTesting, q.Kind);
        }

        [Fact]
        public void Choosing_Chrome_without_Chrome_says_so()
        {
            _locator.Edge = Install("edge", "154.0.4258.53", BrowserKind.Edge);
            var ex = Assert.Throws<BrowserProvisioner.ProvisionException>(
                () => Provisioner().Resolve(null, allowDownload: false, preference: HelperBrowsers.Chrome));
            Assert.Equal("Browser_Err_ChromeNotInstalled", ex.MessageKey);
        }

        [Fact]
        public void Choosing_Edge_uses_Edge_even_with_Chrome_installed()
        {
            _locator.Chrome = Install("chrome", "154.0.8037.95", BrowserKind.Chrome);
            _locator.Edge = Install("edge", "154.0.4258.53", BrowserKind.Edge);
            EdgeDriver("154.0.4258.53");

            var p = Provisioner().Resolve(null, allowDownload: false, preference: HelperBrowsers.Edge);
            Assert.Equal(BrowserKind.Edge, p.Kind);
        }

        // ---------------------------------------------------------------- fakes

        private BrowserInstall Install(string folder, string version, BrowserKind kind)
        {
            var dir = Path.Combine(_root, "installs", folder);
            Directory.CreateDirectory(dir);
            var exe = Path.Combine(dir, kind == BrowserKind.Edge ? "msedge.exe" : "chrome.exe");
            File.WriteAllText(exe, "");
            return new BrowserInstall { Path = exe, Version = Version.Parse(version), Kind = kind };
        }

        private void EdgeDriver(string version) =>
            Driver(Path.Combine(_root, "data", "Drivers", "edge", version), "msedgedriver.exe");

        private static void Driver(string dir, string name)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, name), "");
        }

        private void ChromeForTesting(string version, bool complete = true)
        {
            var dir = Path.Combine(_root, "data", "Browser", "chrome-for-testing", version);
            Directory.CreateDirectory(Path.Combine(dir, "chrome-win64"));
            File.WriteAllText(Path.Combine(dir, "chrome-win64", "chrome.exe"), "");
            if (complete) File.WriteAllText(Path.Combine(dir, ".complete"), "");
        }

        private sealed class FakeLocator : IBrowserLocator
        {
            public BrowserInstall Chrome { get; set; }
            public BrowserInstall Edge { get; set; }
            public BrowserInstall FindChrome() => Chrome;
            public BrowserInstall FindEdge() => Edge;
        }

        private sealed class FakePaths : IAppPaths
        {
            public FakePaths(string root)
            {
                ConfigDirectory = Path.Combine(root, "config");
                DataDirectory = Path.Combine(root, "data");
                LogDirectory = Path.Combine(root, "logs");
            }

            public string ConfigDirectory { get; }
            public string DataDirectory { get; }
            public string LogDirectory { get; }
            public void EnsureCreated() { }
        }

        private sealed class FakeInfo : IPlatformInfo
        {
            public string DriverPlatformKey => "win64";
            public string DriverFileName => "chromedriver.exe";
            public IEnumerable<string> AdditionalDriverSearchPaths => Array.Empty<string>();
            public void MakeExecutable(string path) { }
            public Version ReadExecutableVersion(string path) => null;
            public string EdgeDriverPlatformKey => "win64";
            public string EdgeDriverFileName => "msedgedriver.exe";
            public string ChromeForTestingExecutable => Path.Combine("chrome-win64", "chrome.exe");
            public void ExtractArchive(string zipPath, string destination) => throw new NotSupportedException();
        }
    }
}

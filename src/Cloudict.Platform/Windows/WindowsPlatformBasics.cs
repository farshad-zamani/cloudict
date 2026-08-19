using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Cloudict.Abstractions;
using Microsoft.Win32;

namespace Cloudict.Platform.Windows
{
    /// <summary>Per-user storage under <c>%APPDATA%</c> / <c>%LOCALAPPDATA%</c>.</summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsAppPaths : IAppPaths
    {
        public string ConfigDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cloudict");

        public string DataDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cloudict");

        public string LogDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cloudict", "Logs");

        public void EnsureCreated()
        {
            foreach (var dir in new[] { ConfigDirectory, DataDirectory, LogDirectory })
            {
                try { Directory.CreateDirectory(dir); }
                catch (Exception ex) { Debug.WriteLine($"[WindowsAppPaths] cannot create {dir}: {ex.Message}"); }
            }
        }
    }

    /// <summary>Driver naming and version reading for Windows.</summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsPlatformInfo : IPlatformInfo
    {
        public string DriverPlatformKey => "win64";
        public string DriverFileName => "chromedriver.exe";

        public IEnumerable<string> AdditionalDriverSearchPaths
        {
            get
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                if (!string.IsNullOrEmpty(localAppData))
                    yield return Path.Combine(localAppData, "ChromeDriver");

                // Selenium Manager's own cache — if the user has ever run Selenium, reuse its driver.
                if (!string.IsNullOrEmpty(userProfile))
                    yield return Path.Combine(userProfile, ".cache", "selenium", "chromedriver");
            }
        }

        /// <summary>No-op: Windows has no execute permission bit.</summary>
        public void MakeExecutable(string path) { }

        public Version ReadExecutableVersion(string path)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                var raw = info.ProductVersion ?? info.FileVersion;
                if (string.IsNullOrWhiteSpace(raw)) return null;

                var numeric = new string(raw.TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray()).Trim('.');
                return Version.TryParse(numeric, out var v) ? v : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsPlatformInfo] cannot read version of {path}: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Finds Google Chrome through the Windows "App Paths" registry entries — the same mechanism
    /// the shell uses to resolve <c>chrome.exe</c>, so it locates per-user installs and installs
    /// outside Program Files — falling back to the standard install folders.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsBrowserLocator : IBrowserLocator
    {
        private readonly IPlatformInfo _info;

        public WindowsBrowserLocator(IPlatformInfo info) => _info = info;

        public BrowserInstall FindChrome()
        {
            var paths = new List<string>();

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
                    catch (Exception ex) { Debug.WriteLine($"[WindowsBrowserLocator] registry probe failed: {ex.Message}"); }
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
                .Select(TryRead)
                .Where(c => c != null)
                .OrderByDescending(c => c.Version)
                .FirstOrDefault();
        }

        private BrowserInstall TryRead(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                var version = _info.ReadExecutableVersion(path);
                return version == null ? null : new BrowserInstall { Path = path, Version = version };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsBrowserLocator] cannot read Chrome at {path}: {ex.Message}");
                return null;
            }
        }
    }
}

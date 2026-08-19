using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Cloudict.Platform;
using Cloudict.Speech;

namespace Cloudict.App.Services
{
    /// <summary>
    /// Produces the report printed by <c>cloudict --diagnose</c>.
    ///
    /// <para>On Linux especially, "dictation types nothing" almost always comes down to the session
    /// type, a missing helper or a permission — none of which are visible from the interface. One
    /// command that prints exactly what was resolved turns a long support exchange into a single
    /// pasted block.</para>
    /// </summary>
    public static class Diagnostics
    {
        /// <summary>
        /// Whether this process has administrator/root rights. Cloudict does not need them, with one
        /// exception on Windows: synthetic input is refused by a window running at a higher
        /// integrity level, so dictating into an elevated application requires an elevated Cloudict.
        /// </summary>
        private static string IsElevated()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator) ? "yes" : "no";
                }

                return Environment.GetEnvironmentVariable("USER") == "root" ? "yes (root)" : "no";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diagnostics] elevation check failed: {ex.Message}");
                return "unknown";
            }
        }

        public static string Describe()
        {
            var report = new StringBuilder();

            report.AppendLine($"Cloudict {AppInfo.Version}");
            report.AppendLine($"  OS          : {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
            report.AppendLine($"  Runtime     : {RuntimeInformation.FrameworkDescription}");
            report.AppendLine($"  Session     : {Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "n/a"}");
            report.AppendLine($"  DISPLAY     : {Environment.GetEnvironmentVariable("DISPLAY") ?? "n/a"}");
            report.AppendLine($"  WAYLAND     : {Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "n/a"}");
            report.AppendLine($"  Elevated    : {IsElevated()}   (needed only to type into windows running as administrator)");
            report.AppendLine();

            try
            {
                using var platform = PlatformServices.Create();
                var caps = platform.GetCapabilities();

                report.AppendLine("Capabilities");
                report.AppendLine($"  type into other apps : {caps.CanInjectText}  (backend: {caps.InjectionBackend})");
                report.AppendLine($"  global shortcuts     : {caps.CanRegisterGlobalHotkeys}");
                report.AppendLine($"  switch keyboard layout: {caps.CanSwitchKeyboardLayout}");
                report.AppendLine($"  detect microphone    : {caps.CanDetectMicrophone}");

                foreach (var key in caps.LimitationKeys)
                    report.AppendLine($"  note                 : {Loc.Get(key)}");

                report.AppendLine();
                report.AppendLine("Paths");
                report.AppendLine($"  config : {platform.Paths.ConfigDirectory}");
                report.AppendLine($"  data   : {platform.Paths.DataDirectory}");
                report.AppendLine($"  logs   : {platform.Paths.LogDirectory}");

                report.AppendLine();
                report.AppendLine("Browser");

                var chrome = platform.BrowserLocator.FindChrome();
                report.AppendLine(chrome == null
                    ? "  Chrome : not found"
                    : $"  Chrome : {chrome.Version} @ {chrome.Path}");

                var provisioner = new BrowserProvisioner(platform.Paths, platform.Info, platform.BrowserLocator);
                try
                {
                    var provision = provisioner.Resolve(_ => { }, allowDownload: false);
                    report.AppendLine($"  Driver : {provision.DriverVersion} [{provision.DriverSource}] @ {provision.DriverPath}");
                    if (provision.RequiresBuildCheckOverride)
                        report.AppendLine("  Driver : version differs from Chrome; running with the build check disabled");
                }
                catch (BrowserProvisioner.ProvisionException ex)
                {
                    report.AppendLine($"  Driver : unavailable offline — {Loc.Get(ex.MessageKey)}");
                }
            }
            catch (Exception ex)
            {
                report.AppendLine($"Platform services failed to start: {ex.Message}");
            }

            return report.ToString();
        }
    }
}

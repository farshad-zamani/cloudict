using System;
using System.Diagnostics;
using System.IO;
using Cloudict.Abstractions;

namespace Cloudict.Platform.Unix
{
    /// <summary>
    /// Desktop notifications on Linux and macOS, through each system's standard command-line entry
    /// point: <c>notify-send</c> (the freedesktop notification service) and <c>osascript</c>
    /// (User Notifications).
    ///
    /// <para>Shelling out rather than binding the native APIs is deliberate. On Linux the real
    /// interface is a D-Bus service whose availability depends on the desktop environment, and on
    /// macOS the framework route requires a signed, bundled app to work at all. Both of those turn a
    /// nicety — telling the user a voice command ran — into a source of hard failures. The command
    /// either exists and works, or it does not and notifications are quietly reported unsupported.</para>
    /// </summary>
    internal sealed class UnixNotifier : INotifier
    {
        private readonly bool _isMacOS;
        private readonly string _tool;

        public UnixNotifier(bool isMacOS)
        {
            _isMacOS = isMacOS;
            _tool = isMacOS ? Find("osascript") : Find("notify-send");
        }

        public bool IsSupported => _tool != null;

        public void Show(string title, string message)
        {
            if (_tool == null || string.IsNullOrWhiteSpace(message)) return;

            try
            {
                var psi = new ProcessStartInfo(_tool)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                if (_isMacOS)
                {
                    // AppleScript string literals: escape backslashes first, then quotes.
                    psi.ArgumentList.Add("-e");
                    psi.ArgumentList.Add(
                        $"display notification \"{Escape(message)}\" with title \"{Escape(title)}\"");
                }
                else
                {
                    psi.ArgumentList.Add("--app-name=Cloudict");
                    psi.ArgumentList.Add("--expire-time=4000");
                    psi.ArgumentList.Add(title ?? "Cloudict");
                    psi.ArgumentList.Add(message);
                }

                using var process = Process.Start(psi);

                // Do not block dictation waiting for a notification daemon to answer.
                process?.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UnixNotifier] notification failed: {ex.Message}");
            }
        }

        private static string Escape(string value) =>
            (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string Find(string name)
        {
            foreach (var dir in new[] { "/usr/bin", "/bin", "/usr/local/bin", "/opt/homebrew/bin" })
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return candidate;
            }

            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), name);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (Exception ex) { Debug.WriteLine($"[UnixNotifier] PATH probe: {ex.Message}"); }
            }

            return null;
        }

        public void Dispose() { }
    }
}

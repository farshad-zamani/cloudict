using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Cloudict.Abstractions;

namespace Cloudict.Platform.Linux
{
    /// <summary>
    /// System audio on Linux, where it needs nothing installed.
    ///
    /// <para>PulseAudio and PipeWire give every output a <c>.monitor</c> source that carries exactly
    /// what that output is playing. It is a real recording source as far as any application is
    /// concerned, it is passive, and it is already there — so where Windows needs a virtual cable and
    /// a bridge, here it is one <c>pactl</c> call to point the default source at the monitor of the
    /// current output.</para>
    /// </summary>
    [SupportedOSPlatform("linux")]
    internal sealed class LinuxAudioRouting : IAudioRouting
    {
        private readonly string _restoreFile;
        private string _previousSource;

        public LinuxAudioRouting(IAppPaths paths)
        {
            _restoreFile = Path.Combine(paths.DataDirectory, "audio-restore.txt");
        }

        public bool IsSupported => HasPactl();
        public bool IsActive { get; private set; }

        /// <summary>Unknown here: the monitor source is read by Chrome directly, never through Cloudict.</summary>
        public float CurrentLevel => -1f;

        private static void Log(string message) => DiagnosticLog.Write("LinuxAudioRouting", message);

        public AudioRoutingStatus Probe()
        {
            if (!HasPactl())
                return new AudioRoutingStatus
                {
                    State = AudioRoutingState.HelperMissing,
                    HelperName = "PulseAudio / PipeWire",
                    MessageKey = "SystemAudio_HelperMissing"
                };

            var monitor = FindMonitorSource();

            if (monitor == null)
                return new AudioRoutingStatus
                {
                    State = AudioRoutingState.HelperMissing,
                    HelperName = "PulseAudio / PipeWire",
                    MessageKey = "SystemAudio_HelperMissing"
                };

            return new AudioRoutingStatus
            {
                State = IsActive ? AudioRoutingState.Active : AudioRoutingState.Ready,
                CaptureDevice = monitor
            };
        }

        public AudioRoutingStatus Enable()
        {
            var status = Probe();
            if (status.State == AudioRoutingState.HelperMissing) return status;

            try
            {
                _previousSource = Run("pactl", "get-default-source")?.Trim();
                if (!string.IsNullOrEmpty(_previousSource))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_restoreFile) ?? ".");
                    File.WriteAllText(_restoreFile, _previousSource);
                }

                Run("pactl", $"set-default-source {status.CaptureDevice}");
                IsActive = true;
                Log($"default source is now '{status.CaptureDevice}'");

                return new AudioRoutingStatus
                {
                    State = AudioRoutingState.Active,
                    CaptureDevice = status.CaptureDevice
                };
            }
            catch (Exception ex)
            {
                Log($"enable failed: {ex.Message}");
                Disable();
                return new AudioRoutingStatus { State = AudioRoutingState.Ready, MessageKey = "SystemAudio_SwitchFailed" };
            }
        }

        public void Disable()
        {
            try
            {
                if (!string.IsNullOrEmpty(_previousSource))
                    Run("pactl", $"set-default-source {_previousSource}");
            }
            catch (Exception ex) { Log($"restore failed: {ex.Message}"); }

            _previousSource = null;
            IsActive = false;

            try { if (File.Exists(_restoreFile)) File.Delete(_restoreFile); }
            catch (Exception ex) { Debug.WriteLine(ex.Message); }
        }

        public void RecoverInterruptedSession()
        {
            try
            {
                if (!File.Exists(_restoreFile)) return;

                var source = File.ReadAllText(_restoreFile).Trim();
                if (!string.IsNullOrEmpty(source))
                {
                    Log($"a previous session left the default source changed; restoring '{source}'");
                    Run("pactl", $"set-default-source {source}");
                }

                File.Delete(_restoreFile);
            }
            catch (Exception ex) { Log($"recovery failed: {ex.Message}"); }
        }

        /// <summary>The monitor of the current output, which is what the speakers are playing.</summary>
        private static string FindMonitorSource()
        {
            try
            {
                var sink = Run("pactl", "get-default-sink")?.Trim();
                if (string.IsNullOrWhiteSpace(sink)) return null;

                var candidate = sink + ".monitor";
                var sources = Run("pactl", "list short sources") ?? string.Empty;

                if (sources.Split('\n').Any(l => l.Contains(candidate, StringComparison.Ordinal)))
                    return candidate;

                // Any monitor is better than none — a machine with an unusual default still works.
                return sources.Split('\n')
                              .Select(l => l.Split('\t').Skip(1).FirstOrDefault())
                              .FirstOrDefault(n => n != null && n.EndsWith(".monitor", StringComparison.Ordinal));
            }
            catch (Exception ex)
            {
                Log($"could not find a monitor source: {ex.Message}");
                return null;
            }
        }

        private static bool HasPactl()
        {
            try { return Run("pactl", "--version") != null; }
            catch (Exception ex) { Debug.WriteLine(ex.Message); return false; }
        }

        private static string Run(string file, string arguments)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(file, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null) return null;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(4000);

                return process.ExitCode == 0 ? output : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinuxAudioRouting] {file}: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            if (IsActive) Disable();
        }
    }
}

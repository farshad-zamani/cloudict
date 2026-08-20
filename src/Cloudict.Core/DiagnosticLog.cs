using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Cloudict
{
    /// <summary>
    /// An opt-in trace file, enabled by setting <c>CLOUDICT_DEBUG=1</c>.
    ///
    /// <para>Two things make this necessary rather than a convenience. <c>Debug.WriteLine</c> is
    /// compiled out of release builds — which is exactly the build a user is running when something
    /// goes wrong — and Cloudict is a windowed application, so on Windows it has no console and
    /// anything written to stderr disappears. Neither leaves a way to find out why the browser would
    /// not come up on someone else's machine.</para>
    ///
    /// <para>Off by default, and writes nothing at all until enabled, so it costs a boolean check on
    /// the hot paths.</para>
    /// </summary>
    public static class DiagnosticLog
    {
        private static readonly object Gate = new object();
        private static readonly System.Collections.Generic.List<string> Pending =
            new System.Collections.Generic.List<string>();

        private static string _path;

        /// <summary>True when <c>CLOUDICT_DEBUG=1</c> is set in the environment.</summary>
        public static bool IsEnabled { get; } =
            Environment.GetEnvironmentVariable("CLOUDICT_DEBUG") == "1";

        /// <summary>
        /// Points the trace at a directory. Called once at startup with the platform's log location;
        /// until then, and if it fails, entries still reach the debugger.
        /// </summary>
        public static void Initialize(string logDirectory)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(logDirectory)) return;

            try
            {
                Directory.CreateDirectory(logDirectory);

                lock (Gate)
                {
                    _path = Path.Combine(logDirectory, "cloudict-debug.log");

                    var header = string.Format(CultureInfo.InvariantCulture,
                        "{0}=== session started {1:yyyy-MM-dd HH:mm:ss} ==={0}", Environment.NewLine, DateTime.Now);

                    // Anything written before the log directory was known is flushed now. The
                    // platform services are built first - they are what needs a path - so without
                    // this the trace would miss exactly the startup it exists to explain.
                    var buffered = string.Concat(Pending);
                    Pending.Clear();

                    File.AppendAllText(_path, header + buffered, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DiagnosticLog] could not open the trace file: {ex.Message}");
            }
        }

        /// <summary>Records one line, tagged with its source.</summary>
        public static void Write(string source, string message)
        {
            Debug.WriteLine($"[{source}] {message}");
            if (!IsEnabled) return;

            try
            {
                // Invariant formatting: under a Persian locale the timestamp and any numbers in
                // the message come out with different separators, which makes a trace sent by a
                // user harder to read and impossible to parse.
                var line = string.Format(CultureInfo.InvariantCulture,
                    "{0:HH:mm:ss.fff} [{1}] {2}{3}", DateTime.Now, source, message, Environment.NewLine);
                lock (Gate)
                {
                    // Before Initialize there is nowhere to write yet, so hold the line instead of
                    // losing it. Capped, because a failure before startup completes must not grow
                    // without bound.
                    if (_path == null)
                    {
                        if (Pending.Count < 500) Pending.Add(line);
                        return;
                    }

                    File.AppendAllText(_path, line, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                // Tracing must never be able to break the thing it is tracing.
                Debug.WriteLine($"[DiagnosticLog] write failed: {ex.Message}");
            }
        }

        /// <summary>Where the trace is being written, or null when disabled.</summary>
        public static string Path_ { get { lock (Gate) return _path; } }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Avalonia;
using Cloudict.App.Services;

namespace Cloudict.App
{
    internal static class Program
    {
        /// <summary>
        /// Entry point.
        ///
        /// <para>Before any window is created this handles the command-line verbs, because they are
        /// how a <em>second</em> invocation talks to the instance already running. That matters most
        /// on Wayland, where no application can claim a global shortcut: the user binds one in their
        /// desktop settings to run <c>cloudict --toggle</c>, and this is what makes that work.</para>
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (HandleDiagnostics(args)) return 0;

                var command = SingleInstance.ParseCommand(args);

                // A verb is a message for the running instance, never a reason to start a new one.
                if (command != InstanceCommand.None)
                    return SingleInstance.TrySend(command) ? 0 : 1;

                if (!SingleInstance.TryAcquire())
                {
                    // Already running: surface that instance instead of starting a second.
                    SingleInstance.TrySend(InstanceCommand.Show);
                    return 0;
                }

                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                LogFatal(ex);
                return 1;
            }
            finally
            {
                SingleInstance.Release();
            }
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                      .UsePlatformDetect()
                      .WithInterFont()
                      .LogToTrace();

        /// <summary>
        /// <c>--diagnose</c> prints what the platform layer resolved and exits.
        ///
        /// <para>Worth having as a shipped feature rather than a debug aid: on Linux the answer to
        /// "why does dictation type nothing" is usually the session type or a missing helper, and
        /// this turns a support conversation into one pasted block of text.</para>
        /// </summary>
        private static bool HandleDiagnostics(string[] args)
        {
            if (Array.IndexOf(args, "--diagnose") < 0 && Array.IndexOf(args, "--version") < 0)
                return false;

            if (Array.IndexOf(args, "--version") >= 0)
            {
                Console.WriteLine($"Cloudict {AppInfo.Version}");
                return true;
            }

            Console.WriteLine(Diagnostics.Describe());
            return true;
        }

        private static void LogFatal(Exception ex)
        {
            Debug.WriteLine(ex.ToString());

            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cloudict");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "startup_error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch (Exception logEx)
            {
                Debug.WriteLine($"could not write startup log: {logEx.Message}");
            }
        }
    }
}

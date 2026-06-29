using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Threading;
using System.Windows;

namespace Cloudict
{
    public partial class App : Application
    {
        private static Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Apply the saved UI language first so even early startup dialogs are localized.
            try { LocalizationManager.Apply(new SettingsManager().LoadSettings().UILanguage); }
            catch { LocalizationManager.Apply(LocalizationManager.DefaultLanguage); }

            // Require administrator rights: needed to register global hotkeys and to send
            // simulated input to windows running with elevated privileges.
            if (OperatingSystem.IsWindows() && !IsRunningAsAdministrator())
            {
                RestartAsAdministrator();
                return;
            }

            // Ensure only a single instance runs at a time.
            const string mutexName = "CloudictMutex";
            _mutex = new Mutex(true, mutexName, out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show(Loc.Get("App_AlreadyRunning"), Loc.Get("Common_Info_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);

            // Global error handling.
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Exception ex = (Exception)args.ExceptionObject;
                LogError(ex);
                MessageBox.Show(Loc.Get("App_UnexpectedError_MustClose", ex.Message), Loc.Get("Common_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            };

            Current.DispatcherUnhandledException += (s, args) =>
            {
                LogError(args.Exception);
                MessageBox.Show(Loc.Get("App_UnexpectedError", args.Exception.Message), Loc.Get("Common_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            // This is a free and open-source build: launch straight into the main window.
            if (OperatingSystem.IsWindows())
            {
                ShowMainWindow();
            }
        }

        /// <summary>Writes full exception details (including inner exceptions) to a log file for diagnostics.</summary>
        private static void LogError(Exception ex)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Cloudict");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "startup_error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch (Exception logEx)
            {
                Debug.WriteLine($"Failed to write error log: {logEx.Message}");
            }
        }

        [SupportedOSPlatform("windows")]
        private void ShowMainWindow()
        {
            if (Current.MainWindow == null)
            {
                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Show();
            }
        }

        [SupportedOSPlatform("windows")]
        private static bool IsRunningAsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private void RestartAsAdministrator()
        {
            try
            {
                string fileName = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(fileName))
                {
                    MessageBox.Show(Loc.Get("App_AdminPathError"),
                        Loc.Get("App_AdminRequired_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    Shutdown();
                    return;
                }

                var processInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = fileName,
                    Verb = "runas"
                };

                Process.Start(processInfo);
                Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Get("App_AdminRequired", ex.Message),
                    Loc.Get("App_AdminRequired_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (Exception ex)
            {
                // ReleaseMutex throws if this process never owned it (e.g. second-instance shutdown path).
                // Safe to ignore — we still want to dispose the handle below.
                Debug.WriteLine($"Mutex release skipped: {ex.Message}");
            }

            _mutex?.Dispose();
            _mutex = null;
            base.OnExit(e);
        }
    }
}

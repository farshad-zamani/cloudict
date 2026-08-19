using System;
using System.Diagnostics;
using System.Windows;
using Cloudict.Abstractions;
using Cloudict.Platform;
using Cloudict.Services;
using Cloudict.Speech;

namespace Cloudict
{
    /// <summary>
    /// The application's composition root: it builds the operating-system services once, hands them
    /// to the pieces that need them, and connects Core's <see cref="UserMessageEventArgs"/>
    /// notifications to actual dialogs.
    ///
    /// <para>Core deliberately cannot show a message box or ask the OS anything. Something has to
    /// bridge that gap, and doing it in exactly one place is what stops platform checks and
    /// <c>MessageBox.Show</c> calls from creeping back through the codebase — which is how the
    /// Windows-only coupling accumulated in the first place.</para>
    /// </summary>
    internal static class AppServices
    {
        private static readonly object Gate = new object();
        private static bool _initialized;

        /// <summary>Everything Cloudict needs from the operating system.</summary>
        public static IPlatformServices Platform { get; private set; }

        /// <summary>Shared settings store. Its notifications are already wired to dialogs.</summary>
        public static SettingsManager Settings { get; private set; }

        /// <summary>Resolves Chrome and a compatible driver, disk-first.</summary>
        public static BrowserProvisioner Browser { get; private set; }

        /// <summary>Builds the services. Safe to call more than once; only the first call does work.</summary>
        public static void Initialize()
        {
            if (_initialized) return;

            lock (Gate)
            {
                if (_initialized) return;

                Platform = PlatformServices.Create();
                Platform.Paths.EnsureCreated();

                Settings = new SettingsManager(Platform.Paths);
                Settings.UserMessage += ShowUserMessage;

                Browser = new BrowserProvisioner(Platform.Paths, Platform.Info, Platform.BrowserLocator);

                _initialized = true;
            }
        }

        /// <summary>
        /// Turns a Core notification into a dialog. Core supplies resource keys rather than finished
        /// sentences, so the text is localized here where the dictionaries are loaded.
        /// </summary>
        private static void ShowUserMessage(object sender, UserMessageEventArgs e)
        {
            try
            {
                var body = e.Args.Length > 0 ? Loc.Get(e.MessageKey, e.Args) : Loc.Get(e.MessageKey);
                var title = Loc.Get(e.TitleKey);

                var icon = e.Severity switch
                {
                    UserMessageSeverity.Error => MessageBoxImage.Error,
                    UserMessageSeverity.Warning => MessageBoxImage.Warning,
                    _ => MessageBoxImage.Information
                };

                void Show() => MessageBox.Show(body, title, MessageBoxButton.OK, icon);

                // Settings can be loaded before the UI thread exists, and saved from background work.
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.CheckAccess()) Show();
                else dispatcher.Invoke(Show);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppServices] could not present user message '{e.MessageKey}': {ex.Message}");
            }
        }

        /// <summary>Releases the OS services. Called from application shutdown.</summary>
        public static void Shutdown()
        {
            try
            {
                if (Settings != null) Settings.UserMessage -= ShowUserMessage;
                Platform?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AppServices] shutdown: {ex.Message}");
            }
        }
    }
}

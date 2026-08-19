using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Cloudict.App.Views;

namespace Cloudict.App
{
    public partial class App : Avalonia.Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            try
            {
                // Build the OS services and settings store before any view asks for them.
                AppServices.Initialize();

                var settings = AppServices.Settings.LoadSettings();
                LocalizationManager.Apply(settings.UILanguage);
                ApplyLanguageResources();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] startup configuration failed: {ex.Message}");
                LocalizationManager.Apply(LocalizationManager.DefaultLanguage);
            }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
                desktop.ShutdownRequested += (_, __) => AppServices.Shutdown();
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Publishes the font and flow direction for the active language, which every window binds
        /// to. Persian needs both a different face and right-to-left layout, and doing it here means
        /// no view has to know which language is active.
        /// </summary>
        private void ApplyLanguageResources()
        {
            var fontKey = LocalizationManager.IsRightToLeft ? "VazirmatnFont" : "InterFont";

            if (Resources.TryGetResource(fontKey, null, out var font) && font is FontFamily family)
                Resources["AppFontFamily"] = family;

            Resources["AppFlowDirection"] = LocalizationManager.IsRightToLeft
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
        }
    }
}

using System;

namespace Cloudict.Abstractions
{
    /// <summary>
    /// A tray/menu-bar presence owned by the platform layer.
    ///
    /// <para>This exists because of a Windows constraint rather than a design preference: Windows
    /// only shows a balloon notification on behalf of a registered tray icon, so whatever shows
    /// notifications there must also own the icon. Having the UI create a second one is what
    /// produced the stray blue "i" icon in 2.x.</para>
    ///
    /// <para>Platforms that do not need this return null from
    /// <see cref="IPlatformServices.TrayPresence"/>, and the application falls back to the
    /// cross-platform tray icon its UI framework provides.</para>
    /// </summary>
    public interface ITrayPresence
    {
        /// <summary>Raised when the user clicks the icon and expects the window back.</summary>
        event EventHandler Activated;

        /// <summary>Sets the hover text, e.g. to reflect whether dictation is running.</summary>
        void SetTooltip(string text);
    }
}

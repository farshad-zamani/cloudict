using System;

namespace Cloudict.Abstractions
{
    /// <summary>
    /// Shows a desktop notification — the small pop-up the operating system owns, not an in-app
    /// banner.
    ///
    /// <para>Cloudict uses these sparingly and for one purpose: telling the user a voice command
    /// ran. That feedback has to be visible while they are working in <em>another</em> application,
    /// which is exactly when Cloudict's own window is not on screen, so an in-app message would be
    /// useless.</para>
    ///
    /// <para>Each system does this completely differently — a tray balloon on Windows, the
    /// freedesktop notification service on Linux, User Notifications on macOS — and any of them can
    /// be unavailable or switched off by the user. Implementations therefore report
    /// <see cref="IsSupported"/> and never throw, because a missing notification is not a reason to
    /// interrupt dictation.</para>
    /// </summary>
    public interface INotifier : IDisposable
    {
        /// <summary>False when this system offers no way to show a desktop notification.</summary>
        bool IsSupported { get; }

        /// <summary>
        /// Shows a notification. Silently does nothing when unsupported; callers are not expected to
        /// check first.
        /// </summary>
        void Show(string title, string message);
    }
}

using System;

namespace Cloudict.Abstractions
{
    /// <summary>
    /// System-wide keyboard shortcuts that work while another application has focus (start/stop
    /// dictation without leaving what you are typing into).
    ///
    /// <para>Windows has <c>RegisterHotKey</c> and X11 has <c>XGrabKey</c>, but Wayland
    /// deliberately provides no way for an application to grab keys globally. There, the app falls
    /// back to having the desktop environment run <c>cloudict --toggle</c>, which reaches the
    /// running instance over the single-instance channel. Callers should treat
    /// <see cref="IsSupported"/> as false meaning "tell the user to bind it in their desktop
    /// settings", not as a failure.</para>
    /// </summary>
    public interface IGlobalHotkeys : IDisposable
    {
        /// <summary>False when this platform/session cannot grab global shortcuts at all.</summary>
        bool IsSupported { get; }

        /// <summary>Localization key explaining the alternative when unsupported, else null.</summary>
        string UnsupportedReasonKey { get; }

        /// <summary>
        /// Associates the hotkey manager with the host window. Windows routes <c>WM_HOTKEY</c> to a
        /// window handle; other platforms ignore this entirely.
        /// </summary>
        void Attach(IntPtr nativeWindowHandle);

        /// <summary>
        /// Registers one shortcut. Returns false if the combination is invalid or already claimed by
        /// another application — a normal, recoverable outcome worth surfacing to the user.
        /// </summary>
        bool Register(HotkeyBinding binding, Action onPressed);

        /// <summary>Releases every shortcut registered through this instance.</summary>
        void UnregisterAll();
    }
}

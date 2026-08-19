using System;
using System.Diagnostics;
using System.Windows;
using Cloudict.Abstractions;

namespace Cloudict
{
    /// <summary>
    /// Registers Cloudict's two global shortcuts (start/stop and stop).
    ///
    /// <para>This used to call <c>RegisterHotKey</c> itself and hook WPF's <c>HwndSource</c>. It is
    /// now a thin adapter over <see cref="IGlobalHotkeys"/>: it reads the user's settings, turns
    /// them into <see cref="HotkeyBinding"/> values and hands them to whichever implementation the
    /// current platform provides. The public shape is unchanged, so the windows that use it did not
    /// have to be touched during the cross-platform restructure.</para>
    /// </summary>
    public class GlobalShortcutManager : IDisposable
    {
        private readonly IGlobalHotkeys _hotkeys;
        private readonly AppSettings _settings;
        private readonly Action _onToggleShortcutPressed;
        private readonly Action _onStopShortcutPressed;

        public GlobalShortcutManager(Window window, AppSettings settings, Action onToggleShortcutPressed, Action onStopShortcutPressed)
        {
            _settings = settings;
            _onToggleShortcutPressed = onToggleShortcutPressed;
            _onStopShortcutPressed = onStopShortcutPressed;

            _hotkeys = AppServices.Platform.GlobalHotkeys;

            // Platforms that route shortcuts through a window want its handle; the Windows
            // implementation ignores this and owns a message loop of its own.
            try
            {
                var handle = window == null
                    ? IntPtr.Zero
                    : new System.Windows.Interop.WindowInteropHelper(window).Handle;
                _hotkeys.Attach(handle);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlobalShortcutManager] could not attach to window: {ex.Message}");
            }
        }

        /// <summary>True when this platform cannot grab global shortcuts at all.</summary>
        public bool IsSupported => _hotkeys.IsSupported;

        /// <summary>Localization key explaining the alternative when shortcuts are unsupported.</summary>
        public string UnsupportedReasonKey => _hotkeys.UnsupportedReasonKey;

        /// <summary>Registers both configured shortcuts. Returns false if either could not be claimed.</summary>
        public bool RegisterShortcuts()
        {
            if (_settings == null || !_settings.GlobalShortcutEnabled) return false;
            if (!_hotkeys.IsSupported) return false;

            UnregisterShortcuts();

            bool success = true;

            var toggle = BuildBinding(_settings.ShortcutKey ?? "A",
                                      _settings.ShortcutCtrl, _settings.ShortcutShift, _settings.ShortcutAlt);
            if (toggle.IsValid)
                success &= _hotkeys.Register(toggle, _onToggleShortcutPressed);

            var stop = BuildBinding(_settings.StopShortcutKey ?? "S",
                                    _settings.StopShortcutCtrl, _settings.StopShortcutShift, _settings.StopShortcutAlt);
            if (stop.IsValid)
                success &= _hotkeys.Register(stop, _onStopShortcutPressed);

            return success;
        }

        private static HotkeyBinding BuildBinding(string keyName, bool ctrl, bool shift, bool alt)
        {
            var modifiers = KeyModifiers.None;
            if (ctrl) modifiers |= KeyModifiers.Control;
            if (shift) modifiers |= KeyModifiers.Shift;
            if (alt) modifiers |= KeyModifiers.Alt;

            return new HotkeyBinding(KeyNames.Parse(keyName), modifiers);
        }

        public void UnregisterShortcuts() => _hotkeys.UnregisterAll();

        /// <summary>
        /// Releases this manager's registrations. The underlying hotkey service belongs to
        /// <see cref="AppServices"/> and outlives individual managers, so it is not disposed here.
        /// </summary>
        public void Dispose() => UnregisterShortcuts();
    }
}

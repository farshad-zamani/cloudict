using System;
using System.Diagnostics;
using Cloudict.Abstractions;

namespace Cloudict.App.Services
{
    /// <summary>
    /// Turns the user's saved shortcut settings into registrations on whichever
    /// <see cref="IGlobalHotkeys"/> the platform provides.
    ///
    /// <para>The platform layer knows how to grab a key; it does not know that Cloudict has a
    /// "start/stop" and a "stop" shortcut, or how those are stored. Keeping that translation here
    /// means the Windows, X11 and Carbon implementations stay interchangeable.</para>
    /// </summary>
    internal sealed class GlobalShortcuts : IDisposable
    {
        private readonly IGlobalHotkeys _hotkeys;

        public GlobalShortcuts(IGlobalHotkeys hotkeys) => _hotkeys = hotkeys;

        /// <summary>True when this platform cannot register global shortcuts at all.</summary>
        public bool IsSupported => _hotkeys.IsSupported;

        /// <summary>Re-registers both shortcuts from current settings. Safe to call repeatedly.</summary>
        public void Apply(AppSettings settings, Action onToggle, Action onStop)
        {
            if (settings == null || !_hotkeys.IsSupported) return;

            _hotkeys.UnregisterAll();

            if (!settings.GlobalShortcutEnabled) return;

            Register(settings.ShortcutKey ?? "A",
                     settings.ShortcutCtrl, settings.ShortcutShift, settings.ShortcutAlt, onToggle);

            Register(settings.StopShortcutKey ?? "S",
                     settings.StopShortcutCtrl, settings.StopShortcutShift, settings.StopShortcutAlt, onStop);
        }

        private void Register(string keyName, bool ctrl, bool shift, bool alt, Action handler)
        {
            var modifiers = KeyModifiers.None;
            if (ctrl) modifiers |= KeyModifiers.Control;
            if (shift) modifiers |= KeyModifiers.Shift;
            if (alt) modifiers |= KeyModifiers.Alt;

            var binding = new HotkeyBinding(KeyNames.Parse(keyName), modifiers);
            if (!binding.IsValid) return;

            if (!_hotkeys.Register(binding, handler))
                Debug.WriteLine($"[GlobalShortcuts] {binding} was refused — most likely already claimed");
        }

        /// <summary>
        /// Releases this object's registrations. The hotkey service itself belongs to
        /// <see cref="AppServices"/> and outlives individual windows, so it is not disposed here.
        /// </summary>
        public void Dispose()
        {
            try { _hotkeys?.UnregisterAll(); }
            catch (Exception ex) { Debug.WriteLine($"[GlobalShortcuts] dispose: {ex.Message}"); }
        }
    }
}

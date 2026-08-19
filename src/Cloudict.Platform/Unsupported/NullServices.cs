using System;
using Cloudict.Abstractions;

namespace Cloudict.Platform.Unsupported
{
    /// <summary>
    /// Stand-ins for capabilities not yet implemented on a platform.
    ///
    /// <para>These deliberately do nothing and say so, rather than throwing. A missing capability is
    /// a normal condition — Wayland has no key-injection API, macOS withholds one until the user
    /// grants Accessibility — and the application is expected to keep running and explain itself.
    /// Each carries a localization key so the UI can tell the user exactly what is unavailable and
    /// what to do about it, which is the difference between "this app is broken" and "this app needs
    /// one permission".</para>
    ///
    /// <para>They are replaced by real implementations as the Linux and macOS milestones land.</para>
    /// </summary>
    internal sealed class NullTextInjector : ITextInjector
    {
        public NullTextInjector(string reasonKey) => UnavailableReasonKey = reasonKey;

        public string BackendName => "none";

        public bool IsAvailable => false;
        public string UnavailableReasonKey { get; }

        public void TypeText(string text) { }
        public void SendKey(InjectedKey key) { }
        public void SendChord(InjectedKey key, KeyModifiers modifiers) { }
        public void Refresh() { }
        public void Dispose() { }
    }

    internal sealed class NullGlobalHotkeys : IGlobalHotkeys
    {
        public NullGlobalHotkeys(string reasonKey) => UnsupportedReasonKey = reasonKey;

        public bool IsSupported => false;
        public string UnsupportedReasonKey { get; }

        public void Attach(IntPtr nativeWindowHandle) { }
        public bool Register(HotkeyBinding binding, Action onPressed) => false;
        public void UnregisterAll() { }
        public void Dispose() { }
    }

    internal sealed class NullKeyboardLayout : IKeyboardLayout
    {
        public bool IsSupported => false;
        public bool TrySwitchTo(string languageCode) => false;
        public string GetCurrentLanguage() => null;
    }

    internal sealed class NullMicrophoneMonitor : IMicrophoneMonitor
    {
        public bool IsSupported => false;

        /// <summary>
        /// Always false. The status light falls back to the app's own dictation state, which is
        /// accurate enough for its purpose.
        /// </summary>
        public bool IsMicrophoneInUse() => false;

        public void Dispose() { }
    }
}

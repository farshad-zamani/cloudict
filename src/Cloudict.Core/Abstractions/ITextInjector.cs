using System;

namespace Cloudict.Abstractions
{
    /// <summary>
    /// Types text and presses keys in whatever application currently has focus. This is Cloudict's
    /// central capability — everything else exists to feed it.
    ///
    /// <para>Each operating system exposes this completely differently, and each has its own way of
    /// refusing: Windows blocks injection into windows running at a higher integrity level, macOS
    /// requires the user to grant Accessibility permission, and Wayland has no injection API at all
    /// unless a helper such as ydotool is installed. Implementations therefore report
    /// <see cref="IsAvailable"/> honestly rather than silently doing nothing, so the UI can explain
    /// the situation instead of appearing broken.</para>
    /// </summary>
    public interface ITextInjector : IDisposable
    {
        /// <summary>
        /// False when this machine cannot inject input right now — permission not granted, no
        /// injection backend present, or an unsupported session type. <see cref="UnavailableReasonKey"/>
        /// explains why.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Localization key describing why injection is unavailable, or null when it works.
        /// A key rather than a sentence, so Core stays free of presentation concerns.
        /// </summary>
        string UnavailableReasonKey { get; }

        /// <summary>
        /// Short, non-localized name of the mechanism actually in use — <c>sendinput</c>,
        /// <c>xtest</c>, <c>ydotool</c>, <c>cgevent</c>, <c>none</c>.
        ///
        /// <para>On Linux the answer is not implied by the platform: the same build picks XTEST or
        /// ydotool depending on the session, and may fall back to XWayland where only some windows
        /// receive input. When a user reports that dictation types nothing, this is the first thing
        /// worth knowing, so it is surfaced rather than buried in a debug log.</para>
        /// </summary>
        string BackendName { get; }

        /// <summary>
        /// Types <paramref name="text"/> as literal characters, independent of the user's keyboard
        /// layout. Must handle non-Latin scripts — Persian and Arabic are primary use cases.
        /// </summary>
        void TypeText(string text);

        /// <summary>Presses and releases a single key.</summary>
        void SendKey(InjectedKey key);

        /// <summary>Presses <paramref name="key"/> while holding <paramref name="modifiers"/> (e.g. Ctrl+Backspace).</summary>
        void SendChord(InjectedKey key, KeyModifiers modifiers);

        /// <summary>
        /// Re-checks availability. Called after the user has been sent to grant a permission, so the
        /// app can notice the change without a restart.
        /// </summary>
        void Refresh();
    }
}

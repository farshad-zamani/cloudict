using System;

namespace Cloudict.Abstractions
{
    /// <summary>What system-audio capture can do on this machine right now.</summary>
    public enum AudioRoutingState
    {
        /// <summary>This platform has no implementation at all.</summary>
        Unsupported,

        /// <summary>Nothing on this machine can carry the system's output into a recording device.</summary>
        HelperMissing,

        /// <summary>A suitable device exists; the mode can be switched on.</summary>
        Ready,

        /// <summary>The mode is on: what the machine plays is what Google Translate hears.</summary>
        Active
    }

    /// <summary>The result of asking what is available, and what to tell the user if it is not.</summary>
    public sealed class AudioRoutingStatus
    {
        public AudioRoutingState State { get; init; }

        /// <summary>The recording device that carries the system's output, when one was found.</summary>
        public string CaptureDevice { get; init; }

        /// <summary>Name of the free helper this platform needs, when one is missing.</summary>
        public string HelperName { get; init; }

        /// <summary>Where to get it.</summary>
        public string HelperUrl { get; init; }

        /// <summary>Localization key describing what to do, when something needs doing.</summary>
        public string MessageKey { get; init; }
    }

    /// <summary>
    /// Sends what the machine is playing to the speech engine instead of the microphone, so a voice
    /// note or a podcast is transcribed exactly as speaking into the microphone would be.
    ///
    /// <para>The shape of this interface is forced by a limitation in Chrome, not by choice. Google
    /// Translate's voice input is the Web Speech API, and that API reads from <em>one audio input
    /// device</em> — there is no way to hand it a stream. So the only way to have it hear the system
    /// is to make the input device it reads from carry the system's output. Every platform does that
    /// differently, and on two of the three it needs a small free driver the user installs once.</para>
    ///
    /// <para>Everything this does is temporary and must survive Cloudict being killed outright: the
    /// previous device is written to disk before anything is changed, and
    /// <see cref="RecoverInterruptedSession"/> puts it back at the next start if the app never got
    /// the chance to.</para>
    /// </summary>
    public interface IAudioRouting : IDisposable
    {
        /// <summary>False when this platform has no implementation.</summary>
        bool IsSupported { get; }

        /// <summary>True while the machine's output is what the speech engine hears.</summary>
        bool IsActive { get; }

        /// <summary>
        /// Recent peak level of the audio being routed, from 0 to 1, or -1 where this platform
        /// cannot tell. Used to find a gap in the audio to reset in, rather than cutting a word in
        /// half.
        /// </summary>
        float CurrentLevel { get; }

        /// <summary>What is available right now, without changing anything.</summary>
        AudioRoutingStatus Probe();

        /// <summary>
        /// Routes the system's output to the speech engine. Returns the resulting status: an
        /// unchanged <see cref="AudioRoutingState.HelperMissing"/> means nothing was touched and the
        /// user needs to install something first.
        /// </summary>
        AudioRoutingStatus Enable();

        /// <summary>Puts the machine's audio back exactly as it was. Safe to call when not active.</summary>
        void Disable();

        /// <summary>
        /// Restores a previous session's audio settings if Cloudict was killed while they were
        /// changed. Called once at startup, before anything else touches audio.
        /// </summary>
        void RecoverInterruptedSession();
    }
}

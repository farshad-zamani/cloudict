using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudict.Speech
{
    /// <summary>
    /// What <see cref="DictationSession"/> needs from a speech engine.
    ///
    /// <para>There is one implementation, <see cref="GoogleTranslateEngine"/>, and this exists for a
    /// narrower reason than swapping engines: the session's timing rules are the most delicate code
    /// in Cloudict and the hardest to reason about from the outside. Behind an interface they can be
    /// exercised against a page that misbehaves on demand — one that refuses to clear its text box,
    /// or hands back the phrase it was just told to forget — which is exactly the behaviour that
    /// produced the worst bug the app has had, and not something a live Google Translate can be
    /// asked to reproduce.</para>
    /// </summary>
    public interface ISpeechEngine
    {
        /// <summary>Opens the helper browser on the recognition page. Safe to call when already open.</summary>
        Task<bool> OpenBrowserAsync(CancellationToken ct = default);

        /// <summary>Starts listening.</summary>
        Task<bool> StartListeningAsync();

        /// <summary>Stops listening.</summary>
        Task<bool> StopListeningAsync();

        /// <summary>The text the page is currently showing, or empty.</summary>
        Task<string> ReadRecognizedTextAsync();

        /// <summary>
        /// Empties the source box, returning whatever is still in it — empty when the clear worked,
        /// null when the page could not be asked at all.
        /// </summary>
        Task<string> ClearSourceTextAsync();

        /// <summary>
        /// Restarts listening from an empty box, reporting what actually happened rather than
        /// assuming it worked.
        /// </summary>
        Task<MicResetResult> ResetMicrophoneAsync();
    }
}

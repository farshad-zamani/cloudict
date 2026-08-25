using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cloudict.Abstractions;
using Cloudict.Services;
using Cloudict.Speech;
using Xunit;

namespace Cloudict.Core.Tests
{
    /// <summary>
    /// Covers the reset that exists for audio which never pauses.
    ///
    /// <para>Every other reset is triggered by the recognised text going still — which is what a
    /// person does constantly, pausing to breathe. A recording played straight through never does,
    /// so Google Translate was left revising one ever-growing phrase and its output degraded the
    /// longer it ran. These tests pin down both halves: that a run which never goes quiet is cut
    /// anyway, and that a microphone session — where the cap is left off — behaves exactly as it
    /// always has.</para>
    /// </summary>
    public class ContinuousAudioTests
    {
        [Fact]
        public async Task Speech_that_never_pauses_is_still_reset()
        {
            var engine = new TalkingEngine();
            var output = new CapturingOutput();
            var session = Build(engine, output);

            session.MaxContinuousMs = 600;      // the cap under test

            Assert.True(await session.StartAsync());
            try
            {
                Assert.True(await WaitFor(() => engine.ResetCount >= 2, TimeSpan.FromSeconds(10)),
                    $"a run that never goes quiet should still be reset; resets seen: {engine.ResetCount}");
            }
            finally { await session.StopAsync(); session.Dispose(); }
        }

        [Fact]
        public async Task A_microphone_session_is_never_cut_short()
        {
            // MaxContinuousMs stays at its default of zero, which is what the microphone path uses.
            var engine = new TalkingEngine();
            var output = new CapturingOutput();
            var session = Build(engine, output);

            Assert.Equal(0, session.MaxContinuousMs);

            Assert.True(await session.StartAsync());
            try
            {
                await Task.Delay(2500);
                Assert.Equal(0, engine.ResetCount);
            }
            finally { await session.StopAsync(); session.Dispose(); }
        }

        [Fact]
        public async Task A_gap_in_the_audio_is_used_to_reset_sooner()
        {
            var engine = new TalkingEngine();
            var output = new CapturingOutput();
            var session = Build(engine, output);

            // Silent from the start: the cap is met at a third of its length, so a reset should
            // arrive well before the full 6 seconds.
            session.MaxContinuousMs = 6000;
            session.SourceIsQuiet = () => true;

            Assert.True(await session.StartAsync());
            try
            {
                Assert.True(await WaitFor(() => engine.ResetCount >= 1, TimeSpan.FromSeconds(5)),
                    "a quiet source should let the reset land early rather than waiting out the cap");
            }
            finally { await session.StopAsync(); session.Dispose(); }
        }

        [Fact]
        public async Task A_level_probe_that_throws_cannot_stop_dictation()
        {
            var engine = new TalkingEngine();
            var output = new CapturingOutput();
            var session = Build(engine, output);

            session.MaxContinuousMs = 600;
            session.SourceIsQuiet = () => throw new InvalidOperationException("the audio device went away");

            Assert.True(await session.StartAsync());
            try
            {
                Assert.True(await WaitFor(() => engine.ResetCount >= 1, TimeSpan.FromSeconds(10)),
                    "the hard cap should still apply when the level cannot be read");
            }
            finally { await session.StopAsync(); session.Dispose(); }
        }

        // ------------------------------------------------------------------ harness

        private static DictationSession Build(ISpeechEngine engine, CapturingOutput output)
        {
            var settings = new AppSettings
            {
                ProcessDelayMs = 50,
                WordByWordDelayMs = 50,
                TransferStartDelayMs = 100,

                // Long enough that the silence trigger cannot fire during these tests, so anything
                // that happens is the continuous-run cap and nothing else.
                InactivityDelayMs = 10000
            };

            return new DictationSession(engine, new NullInjector(), () => settings, output);
        }

        private static async Task<bool> WaitFor(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                await Task.Delay(25);
            }
            return condition();
        }

        /// <summary>A page whose text never stops growing — a recording played straight through.</summary>
        private sealed class TalkingEngine : ISpeechEngine
        {
            private readonly object _gate = new object();
            private int _words;

            public int ResetCount { get; private set; }

            public Task<bool> OpenBrowserAsync(CancellationToken ct = default) => Task.FromResult(true);
            public Task<bool> StartListeningAsync() => Task.FromResult(true);
            public Task<bool> StopListeningAsync() => Task.FromResult(true);
            public Task<string> ClearSourceTextAsync() => Task.FromResult(string.Empty);

            public Task<string> ReadRecognizedTextAsync()
            {
                lock (_gate)
                {
                    _words++;
                    return Task.FromResult(string.Join(" ", BuildWords()));
                }
            }

            private IEnumerable<string> BuildWords()
            {
                for (int i = 1; i <= _words; i++) yield return "word" + i;
            }

            public Task<MicResetResult> ResetMicrophoneAsync()
            {
                lock (_gate)
                {
                    ResetCount++;
                    _words = 0;
                    return Task.FromResult(new MicResetResult(true, string.Empty, true));
                }
            }
        }

        private sealed class CapturingOutput : IDictationOutput
        {
            public string FinalText { get; set; } = string.Empty;
            public int CaretIndex { get; set; }
            public void FocusFinalText() { }
        }

        private sealed class NullInjector : ITextInjector
        {
            public bool IsAvailable => true;
            public string UnavailableReasonKey => null;
            public string BackendName => "test";
            public void TypeText(string text) { }
            public void SendKey(InjectedKey key) { }
            public void SendChord(InjectedKey key, KeyModifiers modifiers) { }
            public void Refresh() { }
            public void Dispose() { }
        }
    }
}

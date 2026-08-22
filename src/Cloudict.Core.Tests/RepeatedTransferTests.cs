using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cloudict.Abstractions;
using Cloudict.Services;
using Cloudict.Speech;
using Xunit;

namespace Cloudict.Core.Tests
{
    /// <summary>
    /// The regression suite for the worst bug Cloudict has had: a phrase that had been dictated,
    /// transferred and finished being typed out again, and again, and again, until the user stopped
    /// it by hand.
    ///
    /// <para>It needed a page that misbehaves — one that will not empty its text box, or that hands
    /// back the phrase it was just told to forget — which is not something a live Google Translate
    /// can be asked to do. Hence the stand-in engine below.</para>
    /// </summary>
    public class RepeatedTransferTests
    {
        [Fact]
        public async Task A_source_box_that_will_not_clear_does_not_cause_the_phrase_to_be_typed_again()
        {
            // The page keeps showing the same phrase no matter how often it is cleared, which is
            // what it does once its own speech recognition has errored.
            var engine = new StubEngine { ClearSucceeds = false };
            var output = new CapturingOutput();

            await using var run = await Run.Start(engine, output);

            engine.Speak("سلام دنیا");
            await run.WaitForSilenceCycles(3);

            Assert.Equal("سلام دنیا", output.Typed);
        }

        [Fact]
        public async Task A_phrase_the_page_restores_after_a_successful_clear_is_not_typed_again()
        {
            // The box reports itself empty, and then the phrase reappears anyway.
            var engine = new StubEngine { ClearSucceeds = true };
            var output = new CapturingOutput();

            await using var run = await Run.Start(engine, output);

            engine.Speak("سلام دنیا");
            await run.WaitForSilenceCycles(3);

            Assert.Equal("سلام دنیا", output.Typed);
        }

        [Fact]
        public async Task New_speech_after_a_reset_is_still_transferred()
        {
            // The guard must not be so eager that it swallows what the user actually says next.
            var engine = new StubEngine { ClearSucceeds = true };
            var output = new CapturingOutput();

            await using var run = await Run.Start(engine, output);

            engine.Speak("سلام دنیا");
            await run.WaitForSilenceCycles(1);

            engine.Speak("خداحافظ");
            await run.WaitForSilenceCycles(2);

            Assert.Equal("سلام دنیا خداحافظ", output.Typed);
        }

        [Fact]
        public async Task When_the_box_never_empties_only_the_newly_spoken_words_go_out()
        {
            // The page keeps the finished phrase and appends what is said next, which is exactly
            // what a box that refuses to clear looks like once the user carries on talking.
            var engine = new StubEngine { ClearSucceeds = false };
            var output = new CapturingOutput();

            await using var run = await Run.Start(engine, output);

            engine.Speak("سلام دنیا");
            await run.WaitForSilenceCycles(1);

            engine.Speak("سلام دنیا حال شما");
            await run.WaitForSilenceCycles(2);

            Assert.Equal("سلام دنیا حال شما", output.Typed);
        }

        [Fact]
        public async Task Text_left_on_the_page_by_the_previous_session_is_never_typed()
        {
            // What the user sees: Google Translate switched its own microphone off, leaving the last
            // phrase in the box. Pressing start again typed that whole phrase a second time.
            var engine = new StubEngine { ClearSucceeds = false };
            engine.Speak("سلام دنیا");   // already on the page before this session begins

            var output = new CapturingOutput();

            await using var run = await Run.Start(engine, output);
            await run.WaitForSilenceCycles(2);

            Assert.Equal(string.Empty, output.Typed);
        }

        [Fact]
        public async Task Speech_after_a_start_over_leftover_text_is_transferred_once()
        {
            // And the leftover must not swallow what is said next.
            var engine = new StubEngine { ClearSucceeds = false };
            engine.Speak("سلام دنیا");

            var output = new CapturingOutput();

            await using var run = await Run.Start(engine, output);

            engine.Speak("سلام دنیا حال شما");
            await run.WaitForSilenceCycles(2);

            Assert.Equal("حال شما", output.Typed);
        }

        [Fact]
        public async Task A_microphone_that_will_not_restart_stops_the_session_instead_of_looping()
        {
            var engine = new StubEngine { ClearSucceeds = false, CanRestart = false };
            var output = new CapturingOutput();

            await using var run = await Run.Start(engine, output);

            engine.Speak("سلام دنیا");

            Assert.True(await run.WaitForAutoStop(TimeSpan.FromSeconds(10)),
                "the session should have asked to be stopped once the microphone could not be restarted");
        }

        // ------------------------------------------------------------------ harness

        /// <summary>Drives a session with delays short enough for a test but the same shape as real ones.</summary>
        private sealed class Run : IAsyncDisposable
        {
            private readonly DictationSession _session;
            private readonly StubEngine _engine;
            private readonly ManualResetEventSlim _autoStopped = new ManualResetEventSlim(false);

            private Run(DictationSession session, StubEngine engine)
            {
                _session = session;
                _engine = engine;
                _session.AutoStopped += (_, __) => _autoStopped.Set();
            }

            public static async Task<Run> Start(StubEngine engine, CapturingOutput output)
            {
                var settings = new AppSettings
                {
                    ProcessDelayMs = 50,
                    WordByWordDelayMs = 50,
                    TransferStartDelayMs = 100,
                    InactivityDelayMs = 300
                };

                var session = new DictationSession(engine, new NullInjector(), () => settings, output);
                var run = new Run(session, engine);

                Assert.True(await session.StartAsync());
                return run;
            }

            /// <summary>Waits until the engine has been reset the given number of times.</summary>
            public async Task WaitForSilenceCycles(int count)
            {
                var target = _engine.ResetCount + count;
                var deadline = DateTime.UtcNow.AddSeconds(10);

                while (_engine.ResetCount < target && DateTime.UtcNow < deadline)
                    await Task.Delay(25);

                // Let any transfer that the last reset might have triggered actually happen, so a
                // regression shows up as extra text rather than as a race the test wins by luck.
                await Task.Delay(400);
            }

            public async Task<bool> WaitForAutoStop(TimeSpan timeout)
            {
                var deadline = DateTime.UtcNow + timeout;
                while (!_autoStopped.IsSet && DateTime.UtcNow < deadline)
                    await Task.Delay(25);

                return _autoStopped.IsSet;
            }

            public async ValueTask DisposeAsync()
            {
                await _session.StopAsync();
                _session.Dispose();
                _autoStopped.Dispose();
            }
        }

        /// <summary>A Google Translate page that can be told to misbehave.</summary>
        private sealed class StubEngine : ISpeechEngine
        {
            private readonly object _gate = new object();
            private string _pageText = string.Empty;

            /// <summary>False for a page that refuses to empty its source box.</summary>
            public bool ClearSucceeds { get; set; } = true;

            /// <summary>False for a page whose voice button will not come back on.</summary>
            public bool CanRestart { get; set; } = true;

            public int ResetCount { get; private set; }

            /// <summary>Puts a phrase on the page, as speaking into the microphone would.</summary>
            public void Speak(string text)
            {
                lock (_gate) _pageText = text;
            }

            public Task<bool> OpenBrowserAsync(CancellationToken ct = default) => Task.FromResult(true);
            public Task<bool> StartListeningAsync() => Task.FromResult(true);
            public Task<bool> StopListeningAsync() => Task.FromResult(true);

            public Task<string> ReadRecognizedTextAsync()
            {
                lock (_gate) return Task.FromResult(_pageText);
            }

            public Task<string> ClearSourceTextAsync()
            {
                lock (_gate) return Task.FromResult(ClearSucceeds ? string.Empty : _pageText);
            }

            public Task<MicResetResult> ResetMicrophoneAsync()
            {
                lock (_gate)
                {
                    ResetCount++;

                    // Either way the phrase is still on the page for the next read: that is the whole
                    // point. ClearSucceeds only decides whether the page *admits* it is still there.
                    var remaining = ClearSucceeds ? string.Empty : _pageText;
                    return Task.FromResult(new MicResetResult(ClearSucceeds, remaining, CanRestart));
                }
            }
        }

        /// <summary>Collects what would have gone into the final-text box.</summary>
        private sealed class CapturingOutput : IDictationOutput
        {
            public string FinalText { get; set; } = string.Empty;
            public int CaretIndex { get; set; }
            public void FocusFinalText() { }

            public string Typed => FinalText.Trim();
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

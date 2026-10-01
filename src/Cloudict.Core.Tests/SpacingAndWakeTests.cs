using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cloudict.Abstractions;
using Cloudict.Speech;
using Xunit;

namespace Cloudict.Core.Tests
{
    /// <summary>
    /// Two things live transfer got wrong in 3.2.3, both about what happens at the edges of a phrase.
    ///
    /// <para><b>Spacing.</b> Only the current phrase was consulted when deciding on a leading space,
    /// and every pause wipes that record — so the first word after each pause, and the first word of
    /// every session, ran straight into the text before it.</para>
    ///
    /// <para><b>Sleep.</b> After the machine woke from sleep or hibernation the loops carried on with
    /// a page and a word record that no longer agreed, and typed old and new words interleaved.</para>
    /// </summary>
    public class SpacingAndWakeTests
    {
        // ---------------------------------------------------------------- spacing rules

        [Theory]
        [InlineData('a', true)]
        [InlineData('م', true)]
        [InlineData('.', true)]
        [InlineData('،', true)]
        [InlineData('؟', true)]
        [InlineData(' ', false)]
        [InlineData('\n', false)]
        [InlineData('\t', false)]
        [InlineData('(', false)]
        [InlineData('«', false)]
        [InlineData('“', false)]
        public void A_space_is_needed_after_anything_but_whitespace_or_an_opening_mark(char before, bool needed) =>
            Assert.Equal(needed, DictationSession.NeedsSpaceAfter(before));

        [Fact]
        public void No_space_at_the_very_start_of_a_field() =>
            Assert.False(DictationSession.NeedsSpaceAfter(null));

        [Theory]
        [InlineData("Space", " ")]
        [InlineData("Enter", "\n")]
        [InlineData(" enter ", "\n")]
        [InlineData("Tab", "\t")]
        [InlineData("Backspace", null)]
        [InlineData("Ctrl+Backspace", null)]
        [InlineData(null, null)]
        public void Key_commands_that_leave_whitespace_are_recognised(string key, string whitespace) =>
            Assert.Equal(whitespace, DictationSession.WhitespaceForKey(key));

        [Fact]
        public void The_field_itself_decides_when_it_can_be_asked()
        {
            var caret = new FakeCaret();
            var session = NewSession(new StubEngine(), new RecordingInjector(), caret);

            caret.Probe = CaretProbe.After('x');
            Assert.True(session.NeedsSeparatorBeforePhrase());

            caret.Probe = CaretProbe.After(' ');
            Assert.False(session.NeedsSeparatorBeforePhrase());

            caret.Probe = CaretProbe.AtStart;
            Assert.False(session.NeedsSeparatorBeforePhrase());
        }

        [Fact]
        public void With_nothing_known_no_space_is_invented()
        {
            var session = NewSession(new StubEngine(), new RecordingInjector(), new FakeCaret { Probe = CaretProbe.Unknown });
            Assert.False(session.NeedsSeparatorBeforePhrase());
        }

        // ---------------------------------------------------------------- spacing, end to end

        [Fact]
        public async Task The_first_word_after_a_pause_is_separated_from_the_last_one()
        {
            // The application cannot be asked, so this rests on what Cloudict typed itself.
            var engine = new StubEngine();
            var injector = new RecordingInjector();
            var caret = new FakeCaret { Probe = CaretProbe.Unknown, Window = 42 };

            await using var run = await Run.Start(engine, injector, caret);

            engine.Speak("سلام دنیا");
            await run.WaitForResets(1);
            engine.Speak(string.Empty);
            await Task.Delay(300);

            engine.Speak("حال شما");
            await run.WaitForResets(1);

            Assert.Equal("سلام دنیا حال شما", injector.Typed);
        }

        [Fact]
        public async Task The_first_word_of_a_session_is_separated_from_text_already_in_the_field()
        {
            var engine = new StubEngine();
            var injector = new RecordingInjector();
            var caret = new FakeCaret { Probe = CaretProbe.After('d'), Window = 7 };

            await using var run = await Run.Start(engine, injector, caret);

            engine.Speak("hello world");
            await run.WaitForResets(1);

            Assert.Equal(" hello world", injector.Raw);
        }

        [Fact]
        public async Task Nothing_is_added_when_the_field_already_ends_in_a_space()
        {
            var engine = new StubEngine();
            var injector = new RecordingInjector();
            var caret = new FakeCaret { Probe = CaretProbe.After(' '), Window = 7 };

            await using var run = await Run.Start(engine, injector, caret);

            engine.Speak("hello world");
            await run.WaitForResets(1);

            Assert.Equal("hello world", injector.Raw);
        }

        [Fact]
        public async Task A_phrase_dictated_into_another_window_gets_no_stray_space()
        {
            var engine = new StubEngine();
            var injector = new RecordingInjector();
            var caret = new FakeCaret { Probe = CaretProbe.Unknown, Window = 1 };

            await using var run = await Run.Start(engine, injector, caret);

            engine.Speak("first");
            await run.WaitForResets(1);
            engine.Speak(string.Empty);
            await Task.Delay(300);

            caret.Window = 2;   // the user switched to another application
            engine.Speak("second");
            await run.WaitForResets(1);

            Assert.Equal("firstsecond", injector.Raw);
        }

        // ---------------------------------------------------------------- sleep

        [Theory]
        [InlineData(600, 700, false)]
        [InlineData(600, 5_000, false)]       // a slow moment is not sleep
        [InlineData(600, 20_000, true)]
        [InlineData(2000, 3_600_000, true)]   // an hour of hibernation
        public void Sleep_is_a_wait_that_took_far_longer_than_asked(int requestedMs, int measuredMs, bool slept) =>
            Assert.Equal(slept, SuspendDetector.SleptDuring(TimeSpan.FromMilliseconds(requestedMs), TimeSpan.FromMilliseconds(measuredMs)));

        [Fact]
        public async Task Nothing_is_typed_after_the_machine_wakes_and_the_owner_is_told_once()
        {
            var engine = new StubEngine();
            var injector = new RecordingInjector();
            var caret = new FakeCaret { Probe = CaretProbe.Unknown, Window = 1 };
            var clock = new FakeClock();

            await using var run = await Run.Start(engine, injector, caret, clock);

            engine.Speak("before sleep");
            await run.WaitForResets(1);
            var typedBeforeSleep = injector.Raw;

            // The lid closes. When it opens, an hour has passed and the page holds something else.
            clock.Advance(TimeSpan.FromHours(1));
            engine.Speak("garbled after wake");
            await Task.Delay(800);

            Assert.Equal(1, run.ResumedCount);
            Assert.Equal(typedBeforeSleep, injector.Raw);
        }

        [Fact]
        public async Task A_session_started_again_after_waking_works_normally()
        {
            var engine = new StubEngine();
            var injector = new RecordingInjector();
            var clock = new FakeClock();

            await using var run = await Run.Start(engine, injector, new FakeCaret { Window = 1 }, clock);

            // Let the loops reach their first wait: a sleep always lands inside one.
            await Task.Delay(200);
            clock.Advance(TimeSpan.FromHours(1));
            await Task.Delay(400);
            Assert.Equal(1, run.ResumedCount);

            await run.Restart();
            engine.Speak("fresh start");
            await run.WaitForResets(1);

            Assert.Equal("fresh start", injector.Typed);
        }

        // ---------------------------------------------------------------- harness

        private static DictationSession NewSession(StubEngine engine, RecordingInjector injector, FakeCaret caret, FakeClock clock = null)
        {
            var settings = new AppSettings
            {
                ProcessDelayMs = 50,
                WordByWordDelayMs = 50,
                TransferStartDelayMs = 100,
                InactivityDelayMs = 300
            };

            var session = new DictationSession(engine, injector, () => settings, new NullOutput())
            {
                IsLiveTransfer = true,
                CaretContext = caret
            };

            if (clock != null) session.Clock = clock.Now;
            return session;
        }

        private sealed class Run : IAsyncDisposable
        {
            private readonly DictationSession _session;
            private readonly StubEngine _engine;
            private int _resumed;

            private Run(DictationSession session, StubEngine engine)
            {
                _session = session;
                _engine = engine;
                _session.SystemResumed += (_, __) => Interlocked.Increment(ref _resumed);
            }

            public int ResumedCount => Volatile.Read(ref _resumed);

            public static async Task<Run> Start(StubEngine engine, RecordingInjector injector, FakeCaret caret, FakeClock clock = null)
            {
                var session = NewSession(engine, injector, caret, clock);
                var run = new Run(session, engine);
                Assert.True(await session.StartAsync());
                return run;
            }

            public async Task Restart()
            {
                await _session.StopAsync();
                Assert.True(await _session.StartAsync());
            }

            public async Task WaitForResets(int count)
            {
                var target = _engine.ResetCount + count;
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (_engine.ResetCount < target && DateTime.UtcNow < deadline)
                    await Task.Delay(25);
                await Task.Delay(300);
            }

            public async ValueTask DisposeAsync()
            {
                await _session.StopAsync();
                _session.Dispose();
            }
        }

        private sealed class FakeClock
        {
            private long _offsetTicks;
            public DateTime Now() => DateTime.UtcNow.AddTicks(Interlocked.Read(ref _offsetTicks));
            public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);
        }

        private sealed class FakeCaret : ICaretContext
        {
            public CaretProbe Probe { get; set; } = CaretProbe.Unknown;
            public long Window { get; set; }
            public CaretProbe ProbeCharBeforeCaret() => Probe;
            public long ForegroundWindowId() => Window;
        }

        private sealed class StubEngine : ISpeechEngine
        {
            private readonly object _gate = new object();
            private string _page = string.Empty;
            private int _resets;

            public int ResetCount => Volatile.Read(ref _resets);
            public void Speak(string text) { lock (_gate) _page = text; }

            public Task<bool> OpenBrowserAsync(CancellationToken ct = default) => Task.FromResult(true);
            public Task<bool> StartListeningAsync() => Task.FromResult(true);
            public Task<bool> StopListeningAsync() => Task.FromResult(true);
            public Task<string> ReadRecognizedTextAsync() { lock (_gate) return Task.FromResult(_page); }

            public Task<string> ClearSourceTextAsync()
            {
                lock (_gate) { _page = string.Empty; return Task.FromResult(string.Empty); }
            }

            public Task<MicResetResult> ResetMicrophoneAsync()
            {
                lock (_gate)
                {
                    Interlocked.Increment(ref _resets);
                    _page = string.Empty;
                    return Task.FromResult(new MicResetResult(true, string.Empty, true));
                }
            }
        }

        private sealed class RecordingInjector : ITextInjector
        {
            private readonly List<string> _sent = new List<string>();
            public string Raw { get { lock (_sent) return string.Concat(_sent); } }
            public string Typed => Raw.Trim();

            public bool IsAvailable => true;
            public string UnavailableReasonKey => null;
            public string BackendName => "test";
            public void TypeText(string text) { lock (_sent) _sent.Add(text); }
            public void SendKey(InjectedKey key) { }
            public void SendChord(InjectedKey key, KeyModifiers modifiers) { }
            public void Refresh() { }
            public void Dispose() { }
        }

        private sealed class NullOutput : IDictationOutput
        {
            public string FinalText { get; set; } = string.Empty;
            public int CaretIndex { get; set; }
            public void FocusFinalText() { }
        }
    }
}

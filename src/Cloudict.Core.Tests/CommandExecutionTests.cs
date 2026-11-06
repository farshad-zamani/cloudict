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
    /// Voice commands spoken during live transfer must run once, at the point in the text where they
    /// were spoken, and leave the words on either side of them alone.
    ///
    /// <para>Before 3.2.8 a command was looked for in the last <em>two</em> words, so it matched
    /// twice — as the word just reached and again as the "previous word" of the next one — and a
    /// command that began a longer phrase was typed as an ordinary word first and then erased with
    /// Backspace, taking the space before it (and, for a key command, the key itself) along. These
    /// tests drive a real <see cref="DictationSession"/> into a small simulated text field that
    /// honours Backspace and Enter, so what is asserted is what the user would have seen.</para>
    /// </summary>
    public class CommandExecutionTests
    {
        private static readonly List<VoiceCommand> Commands = new List<VoiceCommand>
        {
            new VoiceCommand(1, "comma", CommandActionType.TypeText, ","),
            new VoiceCommand(2, "enter", CommandActionType.SendKeys, "Enter"),
            new VoiceCommand(3, "new line", CommandActionType.SendKeys, "Enter"),
            new VoiceCommand(4, "back back", CommandActionType.SendKeys, "Backspace"),
            new VoiceCommand(5, "open bracket", CommandActionType.TypeText, "("),
            new VoiceCommand(6, "cap", CommandActionType.TypeText, "CAP"),
            new VoiceCommand(7, "cap lock", CommandActionType.SendKeys, "Tab"),
            new VoiceCommand(8, "nuke word", CommandActionType.SendKeys, "Ctrl+Backspace"),
            new VoiceCommand(9, "stop typing", CommandActionType.SendKeys, "Ctrl+Alt+S"),
        };

        // ---------------------------------------------------------------- one run, in the right place

        [Fact]
        public async Task A_multi_modifier_voice_command_sends_the_whole_chord_once()
        {
            var field = await Dictate("one stop typing");
            Assert.Equal("one", field.Text);
            Assert.Single(field.Log, e => e == "C(Control, Alt+S)");
            Assert.DoesNotContain(field.Log, e => e == "K(S)");
        }

        [Fact]
        public async Task A_stop_shortcut_during_a_flush_prevents_later_text_and_commands()
        {
            var engine = new Stub();
            var field = new Field();
            var settings = new AppSettings { ProcessDelayMs = 50, TransferStartDelayMs = 10000, InactivityDelayMs = 60000 };
            using var session = new DictationSession(engine, field, () => settings, new NullOutput()) { IsLiveTransfer = true };
            session.UpdateCommands(new VoiceCommandProcessor(Commands, field));
            var recognized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.RecognizedTextChanged += (_, _) => recognized.TrySetResult();
            Task stopping = null;
            field.OnChord = () => stopping = session.StopAsync();
            try
            {
                Assert.True(await session.StartAsync());
                engine.Speak("one stop typing two enter three");
                await recognized.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await session.FlushPendingWordsAsync();
                Assert.NotNull(stopping);
                await stopping;
                Assert.Equal("one", field.Text);
                Assert.Equal(new[] { "T(one)", "C(Control, Alt+S)" }, field.Log);
            }
            finally { await session.StopAsync(); }
        }

        [Fact]
        public async Task A_typed_command_runs_once_between_its_neighbours()
        {
            var field = await Dictate("one two comma three four");
            Assert.Equal("one two , three four", field.Text);
        }

        [Fact]
        public async Task A_command_is_not_run_again_as_the_previous_word_of_the_next_one()
        {
            var field = await Dictate("one comma two");

            Assert.Equal("one , two", field.Text);
            Assert.Equal(1, field.Log.Count(e => e.Contains(",")));
        }

        [Fact]
        public async Task The_same_command_spoken_twice_runs_twice()
        {
            var field = await Dictate("one comma comma two");
            Assert.Equal("one , , two", field.Text);
        }

        [Fact]
        public async Task A_key_command_presses_the_key_once_and_the_next_word_starts_the_new_line()
        {
            var field = await Dictate("one two enter three four");

            Assert.Equal("one two\nthree four", field.Text);
            Assert.Equal(1, field.Log.Count(e => e == "K(Enter)"));
        }

        [Fact]
        public async Task The_key_is_pressed_after_the_words_before_it_and_before_the_words_after_it()
        {
            var field = await Dictate("one two enter three");

            var log = field.Log;
            Assert.True(log.IndexOf("K(Enter)") > log.IndexOf("T( two)"));
            Assert.True(log.IndexOf("K(Enter)") < log.IndexOf("T(three)"));
        }

        [Fact]
        public async Task A_command_at_the_very_end_is_run_and_nothing_is_erased()
        {
            var field = await Dictate("one two comma");

            Assert.Equal("one two ,", field.Text);
            Assert.DoesNotContain("K(Backspace)", field.Log);
        }

        [Fact]
        public async Task A_command_at_the_very_start_is_run_without_a_stray_space()
        {
            var field = await Dictate("comma one two");
            Assert.Equal(", one two", field.Text);
        }

        // ---------------------------------------------------------------- longer commands

        [Fact]
        public async Task A_two_word_command_never_types_its_first_word_or_erases_anything()
        {
            var field = await Dictate("one two new line three");

            Assert.Equal("one two\nthree", field.Text);
            Assert.DoesNotContain("K(Backspace)", field.Log);
            Assert.DoesNotContain(field.Log, e => e.Contains("new"));
        }

        [Fact]
        public async Task A_typed_two_word_command_keeps_the_space_before_it()
        {
            var field = await Dictate("one two open bracket three");
            Assert.Equal("one two ( three", field.Text);
        }

        [Fact]
        public async Task A_word_that_only_starts_a_command_is_typed_normally_when_the_next_word_does_not_finish_it()
        {
            var field = await Dictate("one new year");
            Assert.Equal("one new year", field.Text);
        }

        [Fact]
        public async Task A_word_that_starts_a_command_is_typed_when_the_speaker_stops_on_it()
        {
            var field = await Dictate("one two new");
            Assert.Equal("one two new", field.Text);
        }

        [Fact]
        public async Task A_shorter_command_wins_when_the_longer_one_is_not_completed()
        {
            // "cap" is a command and also the start of "cap lock".
            var field = await Dictate("one cap now");
            Assert.Equal("one CAP now", field.Text);
        }

        [Fact]
        public async Task The_longer_command_wins_when_it_is_completed()
        {
            var field = await Dictate("one cap lock two");

            Assert.Equal("one\ttwo", field.Text);
            Assert.DoesNotContain(field.Log, e => e.Contains("CAP"));
        }

        [Fact]
        public async Task A_shorter_command_at_the_end_is_run_when_the_speaker_stops()
        {
            var field = await Dictate("one cap");
            Assert.Equal("one CAP", field.Text);
        }

        // ---------------------------------------------------------------- keys that edit

        [Fact]
        public async Task Backspace_takes_back_exactly_one_character()
        {
            var field = await Dictate("one two back back three");

            Assert.Equal("one tw three", field.Text);
            Assert.Equal(1, field.Log.Count(e => e == "K(Backspace)"));
        }

        [Fact]
        public async Task The_word_after_a_word_delete_is_not_given_a_guessed_space()
        {
            // Ctrl+Backspace leaves the caret somewhere Cloudict cannot know without being told.
            var field = await Dictate("one two nuke word three");

            Assert.Contains("C(Control+Backspace)", field.Log);
            Assert.Equal("T(three)", field.Log.Last());
        }

        // ---------------------------------------------------------------- pacing and order

        [Fact]
        public async Task A_phrase_shown_all_at_once_is_still_typed_in_order_with_the_command_in_place()
        {
            // Google shows everything immediately; the paced loop works through it slowly. The
            // command must wait its turn instead of running the moment it is visible.
            var field = await Dictate("a b c d e f enter g h i j", wordByWordMs: 120);

            Assert.Equal("a b c d e f\ng h i j", field.Text);
            Assert.True(field.Log.IndexOf("K(Enter)") > field.Log.IndexOf("T( f)"));
        }

        [Fact]
        public async Task Words_revealed_gradually_reach_the_field_in_order()
        {
            var engine = new Stub();
            var field = new Field();
            await using var run = await Run.Start(engine, field, inactivityMs: 1200);

            var words = "one two comma three enter four five".Split(' ');
            for (int i = 1; i <= words.Length; i++)
            {
                engine.Speak(string.Join(" ", words.Take(i)));
                await Task.Delay(90);
            }

            await run.WaitForReset(engine);
            Assert.Equal("one two , three\nfour five", field.Text);
        }

        [Fact]
        public async Task Flushing_while_the_paced_loop_is_running_never_reorders_words()
        {
            var engine = new Stub();
            var field = new Field();
            await using var run = await Run.Start(engine, field, wordByWordMs: 80, inactivityMs: 10_000);

            var spoken = string.Join(" ", Enumerable.Range(1, 14).Select(i => "w" + i));
            engine.Speak(spoken);

            // Let the loop get part way, then ask for everything at once the way the button does.
            await Task.Delay(500);
            Assert.NotEqual(spoken, field.Text);
            await run.Flush();

            // Everything is out, once and in order; the loop has nothing left to add.
            Assert.Equal(spoken, field.Text);
            await Task.Delay(400);
            Assert.Equal(spoken, field.Text);
        }

        [Fact]
        public async Task A_command_split_by_a_pause_is_not_completed_across_it()
        {
            var engine = new Stub();
            var field = new Field();
            await using var run = await Run.Start(engine, field);

            engine.Speak("one new");
            await run.WaitForReset(engine);
            engine.Speak("line two");
            await run.WaitForReset(engine);

            Assert.Equal("one new line two", field.Text);
        }

        // ---------------------------------------------------------------- the matcher on its own

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Commands_and_typing_across_pause_resets_work_identically_in_both_keep_alive_modes(bool keepAlive)
        {
            var engine = new Stub();
            var field = new Field();
            await using var run = await Run.Start(engine, field, keepAlive: keepAlive);
            engine.Speak("one two new line three comma");
            await run.WaitForReset(engine);
            Assert.Equal(1, engine.ResetCount);
            Assert.Equal("one two\nthree ,", field.Text);
            engine.Speak("four enter five stop typing");
            await run.WaitForReset(engine);
            Assert.Equal(2, engine.ResetCount);
            Assert.Equal("one two\nthree , four\nfive", field.Text);
            Assert.Equal(2, field.Log.Count(e => e == "K(Enter)"));
            Assert.Single(field.Log, e => e == "C(Control, Alt+S)");
            Assert.Single(field.Log, e => e.StartsWith("T(", StringComparison.Ordinal) && e.Contains(","));
        }

        [Fact]
        public void The_matcher_prefers_the_longest_command_and_reports_how_many_words_it_used()
        {
            var processor = new VoiceCommandProcessor(Commands);

            var match = processor.LongestMatchAt(new[] { "cap", "lock", "x" });
            Assert.Equal("cap lock", match.Command.Phrase);
            Assert.Equal(2, match.WordCount);

            Assert.Equal("cap", processor.LongestMatchAt(new[] { "cap", "x" }).Command.Phrase);
            Assert.Null(processor.LongestMatchAt(new[] { "x", "comma" }));
        }

        [Fact]
        public void The_matcher_knows_when_a_run_could_still_become_a_longer_command()
        {
            var processor = new VoiceCommandProcessor(Commands);

            Assert.True(processor.CouldGrowInto(new[] { "new" }));
            Assert.True(processor.CouldGrowInto(new[] { "cap" }));
            Assert.False(processor.CouldGrowInto(new[] { "new", "line" }));
            Assert.False(processor.CouldGrowInto(new[] { "comma" }));
            Assert.False(processor.CouldGrowInto(new[] { "new", "year" }));
        }

        [Fact]
        public void Disabled_commands_are_ignored()
        {
            var off = new VoiceCommand(1, "comma", CommandActionType.TypeText, ",") { IsEnabled = false };
            var processor = new VoiceCommandProcessor(new List<VoiceCommand> { off });

            Assert.Null(processor.LongestMatchAt(new[] { "comma" }));
            Assert.False(processor.HasCommands);
        }

        [Fact]
        public void Case_is_ignored_unless_asked_for()
        {
            Assert.NotNull(new VoiceCommandProcessor(Commands).LongestMatchAt(new[] { "Comma" }));
            Assert.Null(new VoiceCommandProcessor(Commands, caseSensitive: true).LongestMatchAt(new[] { "Comma" }));
        }

        [Fact]
        public void A_stored_command_matches_what_google_shows_despite_joiners_and_arabic_letters()
        {
            // The default "نقطه ویر" is stored with a zero-width non-joiner that Google does not echo
            // back; some keyboards also produce the Arabic ye and kaf.
            var zwnj = "‌";
            var arabicYe = "ي";
            var stored = new VoiceCommand(1, "نقطه" + zwnj + " ویر", CommandActionType.TypeText, "؛");
            var processor = new VoiceCommandProcessor(new List<VoiceCommand> { stored });

            Assert.NotNull(processor.LongestMatchAt(new[] { "نقطه", "ویر" }));
            Assert.NotNull(processor.LongestMatchAt(new[] { "نقطه" + zwnj, "و" + arabicYe + "ر" }));
            Assert.True(processor.CouldGrowInto(new[] { "نقطه" }));
        }

        [Fact]
        public void A_typed_command_returns_its_text_and_other_commands_return_nothing()
        {
            var processor = new VoiceCommandProcessor(Commands, new Field());

            Assert.Equal(",", processor.Execute(Commands[0]));
            Assert.Null(processor.Execute(Commands[1]));
        }

        // ---------------------------------------------------------------- harness

        private static async Task<Field> Dictate(string spoken, int wordByWordMs = 50)
        {
            var engine = new Stub();
            var field = new Field();

            await using var run = await Run.Start(engine, field, wordByWordMs);

            engine.Speak(spoken);
            await run.WaitForReset(engine);

            return field;
        }

        private sealed class Run : IAsyncDisposable
        {
            private readonly DictationSession _session;

            private Run(DictationSession session) => _session = session;

            public static async Task<Run> Start(Stub engine, Field field, int wordByWordMs = 50, int inactivityMs = 300, bool keepAlive = true)
            {
                var settings = new AppSettings
                {
                    ProcessDelayMs = 50,
                    WordByWordDelayMs = wordByWordMs,
                    TransferStartDelayMs = 100,
                    InactivityDelayMs = inactivityMs,
                    KeepMicrophoneActive = keepAlive
                };

                var session = new DictationSession(engine, field, () => settings, new NullOutput()) { IsLiveTransfer = true };
                session.UpdateCommands(new VoiceCommandProcessor(Commands, field));

                Assert.True(await session.StartAsync());
                return new Run(session);
            }

            public Task Flush() => _session.FlushPendingWordsAsync();

            /// <summary>Waits for the pause-reset that follows every phrase, then for the dust to settle.</summary>
            public async Task WaitForReset(Stub engine)
            {
                var target = engine.ResetCount + 1;
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (engine.ResetCount < target && DateTime.UtcNow < deadline) await Task.Delay(25);
                await Task.Delay(300);
            }

            public async ValueTask DisposeAsync()
            {
                await _session.StopAsync();
                _session.Dispose();
            }
        }

        /// <summary>A text field: typed text lands at the end, Backspace removes a character, Enter a newline.</summary>
        private sealed class Field : ITextInjector
        {
            public Action OnChord { get; set; }
            private readonly object _gate = new object();
            private string _text = string.Empty;
            private readonly List<string> _log = new List<string>();

            public string Text { get { lock (_gate) return _text; } }
            public List<string> Log { get { lock (_gate) return _log.ToList(); } }

            public bool IsAvailable => true;
            public string UnavailableReasonKey => null;
            public string BackendName => "test";

            public void TypeText(string text)
            {
                lock (_gate) { _text += text; _log.Add("T(" + text + ")"); }
            }

            public void SendKey(InjectedKey key)
            {
                lock (_gate)
                {
                    _log.Add("K(" + key + ")");
                    if (key == InjectedKey.Backspace && _text.Length > 0) _text = _text.Substring(0, _text.Length - 1);
                    else if (key == InjectedKey.Enter) _text += "\n";
                    else if (key == InjectedKey.Tab) _text += "\t";
                }
            }

            public void SendChord(InjectedKey key, KeyModifiers modifiers)
            {
                lock (_gate) _log.Add("C(" + modifiers + "+" + key + ")");
                OnChord?.Invoke();
            }

            public void Refresh() { }
            public void Dispose() { }
        }

        private sealed class NullOutput : IDictationOutput
        {
            public string FinalText { get; set; } = string.Empty;
            public int CaretIndex { get; set; }
            public void FocusFinalText() { }
        }

        private sealed class Stub : ISpeechEngine
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
            public Task<string> ClearSourceTextAsync() { lock (_gate) { _page = string.Empty; return Task.FromResult(string.Empty); } }

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
    }
}

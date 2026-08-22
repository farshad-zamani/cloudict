using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cloudict.Abstractions;
using Cloudict.Services;

namespace Cloudict.Speech
{
    /// <summary>
    /// Turns what Google Translate hears into typed output.
    ///
    /// <para>This is the timing-sensitive heart of Cloudict, and every delay in it exists for a
    /// reason found the hard way. Google Translate keeps <em>revising</em> the words it has already
    /// shown while the user carries on speaking, so text cannot simply be forwarded as it appears:
    /// the session waits before starting (<see cref="AppSettings.TransferStartDelayMs"/>), paces
    /// itself word by word (<see cref="AppSettings.WordByWordDelayMs"/>), and after a silence
    /// flushes what is pending and restarts the microphone so the page's buffer never grows long
    /// enough to be rewritten wholesale.</para>
    ///
    /// <para>It was previously spread through the WPF window, tangled with dispatcher calls and text
    /// boxes. The logic is ported here unchanged — including the subtleties noted inline — so that
    /// one implementation serves every platform. Only the edges moved: keystrokes go through
    /// <see cref="ITextInjector"/>, buffered words through <see cref="IDictationOutput"/>, and
    /// status through events carrying localization keys.</para>
    /// </summary>
    public sealed class DictationSession : IDisposable
    {
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        private readonly ISpeechEngine _engine;
        private readonly ITextInjector _injector;
        private readonly Func<AppSettings> _settings;
        private readonly IDictationOutput _output;

        private readonly object _gate = new object();

        private CancellationTokenSource _cancellation;
        private Task _pollLoop;
        private Task _transferLoop;

        // --- recognition state -------------------------------------------------------------
        private readonly List<string> _allWords = new List<string>();
        private int _lastProcessedWordIndex = -1;
        private string _lastRecognizedText = string.Empty;
        private bool _hasRecognizedText;
        private bool _transferStarted;
        private bool _initialDelayCompleted;
        private DateTime _firstTextDetectedTime;
        private DateTime _lastTextUpdateTime;

        /// <summary>
        /// Set once a real word has reached the target since the last reset. Without it an idle
        /// pause would trigger a "reset" that re-sent text already typed — the phantom-reset bug.
        /// </summary>
        private bool _wordTransferredSinceReset;

        /// <summary>Suspends reading while a reset is in progress, so nothing stale is re-read.</summary>
        private volatile bool _suspendRecognition;

        /// <summary>
        /// True between asking Google Translate to empty its source box and actually seeing it
        /// empty. Until then the record of which words have already been typed is kept, because the
        /// box may well still be showing them.
        /// </summary>
        private bool _awaitingEmptyPage;

        private string _lastSentText = string.Empty;

        private VoiceCommandProcessor _commands;

        public DictationSession(
            ISpeechEngine engine,
            ITextInjector injector,
            Func<AppSettings> settingsAccessor,
            IDictationOutput output)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _injector = injector ?? throw new ArgumentNullException(nameof(injector));
            _settings = settingsAccessor ?? throw new ArgumentNullException(nameof(settingsAccessor));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        /// <summary>Status updates, as localization keys.</summary>
        public event EventHandler<EngineStatusEventArgs> StatusChanged;

        /// <summary>The raw text currently shown by Google Translate.</summary>
        public event EventHandler<string> RecognizedTextChanged;

        /// <summary>A voice command matched and ran; carries the command's phrase.</summary>
        public event EventHandler<string> CommandExecuted;

        /// <summary>
        /// Raised when recognition has died and cannot be brought back — Google Translate's own
        /// microphone has stopped and will not restart. The session cannot tear itself down from
        /// inside its own loops, so the owner is asked to call <see cref="StopAsync"/>.
        /// </summary>
        public event EventHandler AutoStopped;

        /// <summary>True while dictation is running.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// True while the microphone is being restarted. The microphone genuinely is off during this
        /// second or so, which the status badge would otherwise report as the user having lost it.
        /// </summary>
        public bool IsResetting => _suspendRecognition;

        /// <summary>
        /// When true, words are typed straight into the focused application. When false they
        /// accumulate in the final-text box for the user to review and copy.
        /// </summary>
        public bool IsLiveTransfer { get; set; }

        /// <summary>Replaces the active voice-command set, e.g. after the user edits it in Settings.</summary>
        public void UpdateCommands(VoiceCommandProcessor processor)
        {
            lock (_gate) _commands = processor;
        }

        private void Report(string key, params object[] args) =>
            StatusChanged?.Invoke(this, new EngineStatusEventArgs(key, args));

        #region Start / stop

        /// <summary>Starts listening and begins transferring recognized words.</summary>
        public async Task<bool> StartAsync()
        {
            if (IsRunning) return true;

            if (!await _engine.OpenBrowserAsync()) return false;

            // Whatever is on the page belongs to the session before this one — most often because
            // Google Translate switched its own microphone off mid-dictation and left the phrase
            // sitting there. Starting on top of it read it as brand-new speech on the first poll and
            // typed the whole thing again: the rare "it suddenly sent text I had already had".
            var leftover = await _engine.ClearSourceTextAsync();

            if (!await _engine.StartListeningAsync()) return false;

            ResetRecognitionState();

            // If it would not clear, the text is still there to be read. Record it as already
            // handled rather than hoping it goes away.
            if (!string.IsNullOrWhiteSpace(leftover)) MarkAlreadyHandled(leftover);

            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;

            IsRunning = true;
            _pollLoop = Task.Run(() => PollLoopAsync(token), token);
            _transferLoop = Task.Run(() => TransferLoopAsync(token), token);

            Report("Main_St_StartingRecognition");
            return true;
        }

        /// <summary>
        /// Stops listening. Buffered text is left alone: when the user is dictating into Cloudict's
        /// own box rather than another application, clearing it on Stop would throw away exactly
        /// what they were collecting.
        /// </summary>
        public async Task StopAsync()
        {
            if (!IsRunning) return;

            IsRunning = false;

            try { _cancellation?.Cancel(); }
            catch (Exception ex) { Debug.WriteLine($"[DictationSession] cancel: {ex.Message}"); }

            await _engine.StopListeningAsync();

            try
            {
                if (_pollLoop != null) await _pollLoop.WaitAsync(TimeSpan.FromSeconds(2));
                if (_transferLoop != null) await _transferLoop.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex) { Debug.WriteLine($"[DictationSession] loop shutdown: {ex.Message}"); }

            _cancellation?.Dispose();
            _cancellation = null;

            Report("Main_St_MicStopped");
        }

        private void ResetRecognitionState()
        {
            lock (_gate)
            {
                _allWords.Clear();
                _lastProcessedWordIndex = -1;
                _lastRecognizedText = string.Empty;
                _hasRecognizedText = false;
                _transferStarted = false;
                _initialDelayCompleted = false;
                _wordTransferredSinceReset = false;
                _lastSentText = string.Empty;
                _awaitingEmptyPage = false;
            }
        }

        /// <summary>
        /// Takes text that is already on the page as read and typed, so it is never sent again.
        /// Used at the start of a session for anything the previous one left behind.
        /// </summary>
        private void MarkAlreadyHandled(string text)
        {
            lock (_gate)
            {
                _lastRecognizedText = text;

                _allWords.Clear();
                _allWords.AddRange(Whitespace.Split(text.Trim()).Where(w => w.Length > 0));
                _lastProcessedWordIndex = _allWords.Count - 1;

                _hasRecognizedText = true;
                _firstTextDetectedTime = DateTime.Now;
                _lastTextUpdateTime = DateTime.Now;

                // Not "transferred since the last reset" — nothing was sent, so an idle pause must
                // not treat this as a phrase worth flushing and resetting around.
                _wordTransferredSinceReset = false;
                _awaitingEmptyPage = true;
            }
        }

        /// <summary>
        /// Re-arms the inactivity trigger without touching the record of which words have already
        /// been typed.
        ///
        /// <para>This is what a reset does now, and it is the fix for Cloudict's worst bug. The old
        /// code wiped the word bookkeeping the moment it <em>asked</em> Google Translate to empty its
        /// source box. When the box did not actually empty — which is what happens once the page's
        /// own speech recognition has errored, typically after a long idle spell — the leftover text
        /// was read on the very next poll, matched nothing in the wiped list, and went out again. The
        /// next silence repeated the whole cycle, so a finished sentence was retyped over and over
        /// until the user stopped it by hand.</para>
        ///
        /// <para>Keeping the record means leftover text stays recognised as already handled, and the
        /// bookkeeping is cleared later, when the box is actually <em>seen</em> empty.</para>
        /// </summary>
        private void ReArmWithoutForgetting()
        {
            lock (_gate)
            {
                _wordTransferredSinceReset = false;
                _transferStarted = false;
                _initialDelayCompleted = false;
                _lastTextUpdateTime = DateTime.Now;
                _awaitingEmptyPage = true;
            }
        }

        #endregion

        #region Reading loop

        /// <summary>
        /// Samples the page and keeps the word list current, then decides when a silence has gone on
        /// long enough to warrant flushing and restarting the microphone.
        /// </summary>
        private async Task PollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Math.Max(50, Settings.ProcessDelayMs), token);
                    if (token.IsCancellationRequested) break;
                    if (_suspendRecognition) continue;

                    var text = await _engine.ReadRecognizedTextAsync();

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        // An empty box is the proof that the last reset landed, and the only safe
                        // moment to forget which words have already been typed.
                        if (_awaitingEmptyPage) ResetRecognitionState();
                        else if (_hasRecognizedText) await CheckForInactivityAsync();

                        continue;
                    }

                    if (text == _lastRecognizedText)
                    {
                        if (_hasRecognizedText) await CheckForInactivityAsync();
                        continue;
                    }

                    // The box changed while it was still expected to empty. If it is the finished
                    // phrase with more speech added, the word bookkeeping still holds and only the
                    // new tail goes out; if it is an unrelated utterance, the box did empty at some
                    // point between two polls and the slate really is clear.
                    if (_awaitingEmptyPage && !ContinuesFrom(text, _lastRecognizedText))
                        ResetRecognitionState();

                    OnRecognizedText(text);
                    RecognizedTextChanged?.Invoke(this, text);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DictationSession] poll: {ex.Message}");
                }
            }
        }

        private void OnRecognizedText(string text)
        {
            lock (_gate)
            {
                if (!_hasRecognizedText)
                {
                    _firstTextDetectedTime = DateTime.Now;
                    _hasRecognizedText = true;
                }

                _lastTextUpdateTime = DateTime.Now;
                _lastRecognizedText = text;

                // The page reports the whole phrase each time, revising earlier words as it goes,
                // so the list is rebuilt rather than appended to. Words already transferred are
                // protected by _lastProcessedWordIndex.
                _allWords.Clear();
                _allWords.AddRange(Whitespace.Split(text.Trim()).Where(w => w.Length > 0));
            }
        }

        /// <summary>
        /// True when <paramref name="text"/> is <paramref name="previous"/> with nothing removed —
        /// either exactly it, or it followed by more words.
        ///
        /// <para>This is how a phrase Google Translate hands back after being told to forget it is
        /// told apart from a genuinely new utterance. It is a comparison of what is on the page
        /// rather than a guess about timing, so a user who really does repeat themselves is not
        /// silently ignored: by then the box will have been seen empty and the slate wiped.</para>
        /// </summary>
        internal static bool ContinuesFrom(string text, string previous)
        {
            if (string.IsNullOrWhiteSpace(previous) || string.IsNullOrWhiteSpace(text)) return false;

            var a = text.Trim();
            var b = previous.Trim();

            if (string.Equals(a, b, StringComparison.Ordinal)) return true;

            // The boundary check matters: "سلامت" starts with "سلام" but is a different word, and
            // skipping it would swallow speech the user never got back.
            return a.Length > b.Length
                   && a.StartsWith(b, StringComparison.Ordinal)
                   && char.IsWhiteSpace(a[b.Length]);
        }

        /// <summary>
        /// After a silence, sends anything still pending and restarts the microphone so Google's
        /// buffer stays short. Only fires when a word actually went out since the last reset.
        /// </summary>
        private async Task CheckForInactivityAsync()
        {
            bool shouldReset;

            lock (_gate)
            {
                shouldReset = _hasRecognizedText
                              && _wordTransferredSinceReset
                              && (DateTime.Now - _lastTextUpdateTime).TotalMilliseconds >= Settings.InactivityDelayMs;
            }

            if (!shouldReset) return;

            // Stop observing *before* the reset: reading or transferring while the box is being
            // cleared is what used to re-send text that had already been typed.
            _suspendRecognition = true;

            try
            {
                Report("Main_St_QuickTransferReset");
                await FlushPendingWordsAsync();

                var reset = await _engine.ResetMicrophoneAsync();

                // Whether or not the page claims to have emptied its box, what has already been
                // typed stays on the record until an empty box is actually observed.
                ReArmWithoutForgetting();

                if (!reset.Listening)
                {
                    // Nothing more will ever be heard, so sitting here pretending to listen only
                    // invites the page to hand back the same text again.
                    Report("Main_St_MicLost");
                    AutoStopped?.Invoke(this, EventArgs.Empty);
                    return;
                }

                Report(reset.SourceCleared ? "Main_St_MicReset" : "Main_St_ResetIncomplete");
            }
            catch (Exception ex)
            {
                Report("Main_St_MicResetErrorPrefix_Fmt", ex.Message);
            }
            finally
            {
                _suspendRecognition = false;
            }
        }

        /// <summary>Sends every word not yet transferred, without waiting for the paced loop.</summary>
        public async Task FlushPendingWordsAsync()
        {
            List<string> pending;

            lock (_gate)
            {
                int start = _lastProcessedWordIndex + 1;
                pending = start < _allWords.Count
                    ? _allWords.GetRange(start, _allWords.Count - start)
                    : new List<string>();

                _lastProcessedWordIndex = _allWords.Count - 1;
            }

            foreach (var word in pending)
            {
                if (await TryRunCommandAsync(word, null)) continue;
                Emit(word);
            }
        }

        #endregion

        #region Transfer loop

        /// <summary>
        /// Paces words to the target. The initial delay gives Google Translate time to settle on its
        /// first few words before anything is typed, which is what stops half-formed guesses
        /// reaching the user's document.
        /// </summary>
        private async Task TransferLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Math.Max(50, Settings.WordByWordDelayMs), token);
                    if (token.IsCancellationRequested) break;
                    if (_suspendRecognition) continue;

                    string word;
                    string previousWord;

                    lock (_gate)
                    {
                        if (!_hasRecognizedText) continue;

                        if (!_transferStarted)
                        {
                            if (!_initialDelayCompleted)
                            {
                                var elapsed = DateTime.Now - _firstTextDetectedTime;
                                if (elapsed.TotalMilliseconds < Settings.TransferStartDelayMs) continue;

                                _initialDelayCompleted = true;
                                Report("Main_St_StartTransfer");
                            }

                            _transferStarted = true;
                        }

                        int next = _lastProcessedWordIndex + 1;
                        if (next >= _allWords.Count) continue;

                        word = _allWords[next];
                        previousWord = next > 0 ? _allWords[next - 1] : null;
                        _lastProcessedWordIndex = next;
                    }

                    if (string.IsNullOrEmpty(word)) continue;

                    if (await TryRunCommandAsync(word, previousWord)) continue;

                    Emit(word);

                    lock (_gate) _wordTransferredSinceReset = true;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Report("Main_St_ProcessErrorPrefix_Fmt", ex.Message);
                }
            }
        }

        /// <summary>
        /// Checks a word against the voice commands, two-word phrases first.
        ///
        /// <para>Two-word commands are tried ahead of single words so a phrase like "علامت سوال"
        /// wins over any single word inside it. When one matches, the previous word has usually
        /// already been typed, so it is erased with backspaces before the replacement goes out.</para>
        /// </summary>
        private async Task<bool> TryRunCommandAsync(string word, string previousWord)
        {
            VoiceCommandProcessor commands;
            lock (_gate) commands = _commands;

            if (commands == null) return false;

            try
            {
                if (previousWord != null)
                {
                    var phrase = previousWord + " " + word;
                    var result = commands.ProcessText(phrase);

                    if (result.CommandExecuted)
                    {
                        await EraseAsync(previousWord);
                        AnnounceCommand(result, phrase);

                        if (!string.IsNullOrEmpty(result.ProcessedText)) Emit(result.ProcessedText, prefixSpace: false);
                        return true;
                    }
                }

                var single = commands.ProcessText(word);
                if (single.CommandExecuted)
                {
                    AnnounceCommand(single, word);
                    if (!string.IsNullOrEmpty(single.ProcessedText)) Emit(single.ProcessedText, prefixSpace: false);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DictationSession] command '{word}': {ex.Message}");
            }

            return false;
        }

        private void AnnounceCommand(CommandProcessResult result, string fallbackPhrase)
        {
            var phrase = result.CommandsExecuted?.FirstOrDefault()?.Phrase ?? fallbackPhrase;
            CommandExecuted?.Invoke(this, phrase);
        }

        /// <summary>Removes a word already sent to the target, so a command can replace it.</summary>
        private Task EraseAsync(string word)
        {
            if (string.IsNullOrEmpty(word)) return Task.CompletedTask;

            if (IsLiveTransfer)
            {
                var trailing = " " + word;
                var toRemove = _lastSentText.EndsWith(trailing, StringComparison.Ordinal) ? trailing
                             : _lastSentText.EndsWith(word, StringComparison.Ordinal) ? word
                             : null;

                if (toRemove != null)
                {
                    for (int i = 0; i < toRemove.Length; i++) _injector.SendKey(InjectedKey.Backspace);
                    _lastSentText = _lastSentText.Substring(0, _lastSentText.Length - toRemove.Length);
                }

                return Task.CompletedTask;
            }

            var text = _output.FinalText ?? string.Empty;
            var withSpace = " " + word;

            if (text.EndsWith(withSpace, StringComparison.Ordinal))
                _output.FinalText = text.Substring(0, text.Length - withSpace.Length);
            else if (text.EndsWith(word, StringComparison.Ordinal))
                _output.FinalText = text.Substring(0, text.Length - word.Length);

            _output.CaretIndex = (_output.FinalText ?? string.Empty).Length;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Sends one piece of text to wherever output is going.
        ///
        /// <para>The leading-space decision is made <em>here, at send time</em>, reading the
        /// up-to-date destination. Deciding it when the word was queued raced with the previous
        /// word's delayed send and produced the classic "first two words stick together" bug.</para>
        /// </summary>
        private void Emit(string text, bool prefixSpace = true)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (IsLiveTransfer)
            {
                var toSend = text;
                if (prefixSpace && !string.IsNullOrEmpty(_lastSentText) && !_lastSentText.EndsWith(" ", StringComparison.Ordinal))
                    toSend = " " + toSend;

                try
                {
                    _injector.TypeText(toSend);
                    _lastSentText += toSend;
                }
                catch (Exception ex)
                {
                    Report("Main_St_SendErrorPrefix_Fmt", ex.Message);
                }

                return;
            }

            // Buffered: insert at the caret so the user can direct where words land.
            var current = _output.FinalText ?? string.Empty;
            var caret = Math.Clamp(_output.CaretIndex, 0, current.Length);

            var insert = text;
            if (prefixSpace && caret > 0 && current[caret - 1] != ' ')
                insert = " " + insert;

            _output.FinalText = current.Substring(0, caret) + insert + current.Substring(caret);
            _output.CaretIndex = caret + insert.Length;
            _output.FocusFinalText();
        }

        #endregion

        private AppSettings Settings => _settings() ?? new AppSettings();

        public void Dispose()
        {
            try { _cancellation?.Cancel(); }
            catch (Exception ex) { Debug.WriteLine($"[DictationSession] dispose: {ex.Message}"); }

            _cancellation?.Dispose();
            _cancellation = null;
        }
    }
}

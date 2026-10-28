using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cloudict.Abstractions;

namespace Cloudict.Services
{
    /// <summary>A voice command found at the start of a run of words, and how many words it used.</summary>
    public sealed class CommandMatch
    {
        public CommandMatch(VoiceCommand command, int wordCount)
        {
            Command = command;
            WordCount = wordCount;
        }

        public VoiceCommand Command { get; }
        public int WordCount { get; }
    }

    /// <summary>
    /// Recognises voice commands in the stream of words Google Translate produces, and carries them
    /// out.
    ///
    /// <para>It is deliberately a <em>matcher</em>, not a text processor. Commands used to be found by
    /// searching the last two words for any command they contained, which meant a command was found
    /// twice — once as the word that was just reached, again as the "previous word" of the next one —
    /// and a command that began a longer phrase could only be recognised after its first word had
    /// already been typed, which then had to be erased. The session now offers words strictly in the
    /// order they were spoken and asks two questions of this class: <em>does what I hold so far
    /// match a command</em>, and <em>could it still become a longer one</em>. Each word is therefore
    /// seen exactly once, at the moment its turn comes.</para>
    /// </summary>
    public class VoiceCommandProcessor
    {
        private readonly ITextInjector _injector;
        private readonly IKeyboardLayout _keyboardLayout;
        private readonly StringComparison _comparison;

        /// <summary>Enabled commands as word sequences, longest first so the first hit is the best one.</summary>
        private readonly Entry[] _entries;

        private sealed class Entry
        {
            public VoiceCommand Command;
            public string[] Words;
        }

        /// <param name="commands">The active commands.</param>
        /// <param name="injector">Presses keys in the focused application; may be null in tests.</param>
        /// <param name="keyboardLayout">Switches the system keyboard layout; may be null in tests.</param>
        /// <param name="caseSensitive">Whether "Comma" and "comma" are different words.</param>
        public VoiceCommandProcessor(
            List<VoiceCommand> commands,
            ITextInjector injector = null,
            IKeyboardLayout keyboardLayout = null,
            bool caseSensitive = false)
        {
            _injector = injector;
            _keyboardLayout = keyboardLayout;
            _comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            _entries = (commands ?? new List<VoiceCommand>())
                .Where(c => c != null && c.IsEnabled && !string.IsNullOrWhiteSpace(c.Phrase))
                .Select(c => new Entry { Command = c, Words = Split(c.Phrase) })
                .Where(e => e.Words.Length > 0)
                .OrderByDescending(e => e.Words.Length)
                .ThenByDescending(e => e.Command.Phrase.Length)
                .ToArray();
        }

        /// <summary>True when there is at least one command to look for.</summary>
        public bool HasCommands => _entries.Length > 0;

        /// <summary>
        /// The longest command that the run of words starts with, or null.
        /// "علامت سوال" wins over a single-word command inside it, because the longer phrase is the
        /// more specific thing the speaker said.
        /// </summary>
        public CommandMatch LongestMatchAt(IReadOnlyList<string> words)
        {
            if (words == null || words.Count == 0) return null;

            foreach (var entry in _entries)
            {
                if (entry.Words.Length > words.Count) continue;
                if (StartsWith(words, entry.Words, entry.Words.Length))
                    return new CommandMatch(entry.Command, entry.Words.Length);
            }

            return null;
        }

        /// <summary>
        /// True when <paramref name="words"/>, taken whole, is the beginning of a longer command —
        /// the signal to wait for the next word before deciding what any of them mean.
        /// </summary>
        public bool CouldGrowInto(IReadOnlyList<string> words)
        {
            if (words == null || words.Count == 0) return false;

            foreach (var entry in _entries)
            {
                if (entry.Words.Length <= words.Count) continue;
                if (StartsWith(entry.Words, words, words.Count)) return true;
            }

            return false;
        }

        /// <summary>
        /// Carries a command out. Keys and layout switches happen here; for a typed command the text
        /// to type is returned so the caller can place it with the same spacing rules as any word.
        /// </summary>
        /// <returns>The text to type, or null when there is nothing to type.</returns>
        public string Execute(VoiceCommand command)
        {
            if (command == null) return null;

            if (command.ActionType == CommandActionType.TypeText)
                return string.IsNullOrWhiteSpace(command.ActionValue) ? null : command.ActionValue;

            ExecuteSystemCommand(command);
            return null;
        }

        private void ExecuteSystemCommand(VoiceCommand command)
        {
            if (string.IsNullOrWhiteSpace(command.ActionValue)) return;

            try
            {
                switch (command.ActionType)
                {
                    case CommandActionType.SendKeys:
                        if (_injector != null && KeyCommandParser.TryParse(command.ActionValue, out var key))
                        {
                            if (key.Modifiers == KeyModifiers.None) _injector.SendKey(key.Key);
                            else _injector.SendChord(key.Key, key.Modifiers);
                        }
                        break;

                    case CommandActionType.ChangeToFarsi:
                        _keyboardLayout?.TrySwitchTo("fa");
                        break;

                    case CommandActionType.ChangeToEnglish:
                        _keyboardLayout?.TrySwitchTo("en");
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceCommandProcessor] command '{command.Phrase}': {ex.Message}");
            }
        }

        // ------------------------------------------------------------------ word comparison

        private bool StartsWith(IReadOnlyList<string> haystack, IReadOnlyList<string> needle, int count)
        {
            for (int i = 0; i < count; i++)
                if (!string.Equals(Normalize(haystack[i]), Normalize(needle[i]), _comparison))
                    return false;

            return true;
        }

        private static string[] Split(string phrase) =>
            phrase.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>
        /// Makes spellings that look identical compare equal: the zero-width joiners Persian writes
        /// between the halves of a word (a command stored as "نقطه‌ ویر" must match what Google
        /// shows), and the Arabic forms of ye and kaf some keyboards produce.
        /// </summary>
        private static string Normalize(string word)
        {
            if (string.IsNullOrEmpty(word)) return string.Empty;

            StringBuilder sb = null;

            for (int i = 0; i < word.Length; i++)
            {
                var c = word[i];
                char mapped = c;
                bool drop = false;

                switch (c)
                {
                    case '\u200C': case '\u200D': case '\u200E': case '\u200F': drop = true; break;
                    case '\u064A': mapped = '\u06CC'; break;   // Arabic ye -> Persian ye
                    case '\u0643': mapped = '\u06A9'; break;   // Arabic kaf -> Persian kaf
                }

                if ((drop || mapped != c) && sb == null)
                    sb = new StringBuilder(word, 0, i, word.Length);

                if (sb != null && !drop) sb.Append(mapped);
            }

            return sb?.ToString() ?? word;
        }
    }
}

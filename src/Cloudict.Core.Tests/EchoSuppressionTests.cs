using Cloudict.Speech;
using Xunit;

namespace Cloudict.Core.Tests
{
    /// <summary>
    /// Covers the comparison that tells a phrase Google Translate has handed back apart from a
    /// genuinely new utterance.
    ///
    /// <para>After a silence Cloudict flushes what it has, asks the page to empty its source box and
    /// restarts the microphone. When the box does not actually empty, the next poll reads text that
    /// looks brand new. Getting this comparison wrong in one direction retypes a finished sentence
    /// on a loop; getting it wrong in the other silently swallows what the user just said.</para>
    /// </summary>
    public class EchoSuppressionTests
    {
        [Fact]
        public void The_same_phrase_coming_back_unchanged_is_a_continuation()
        {
            Assert.True(DictationSession.ContinuesFrom("سلام دنیا", "سلام دنیا"));
        }

        [Fact]
        public void The_phrase_with_more_words_added_is_a_continuation()
        {
            Assert.True(DictationSession.ContinuesFrom("سلام دنیا حال شما", "سلام دنیا"));
        }

        [Fact]
        public void Unrelated_speech_is_not_a_continuation()
        {
            Assert.False(DictationSession.ContinuesFrom("خداحافظ", "سلام دنیا"));
        }

        [Fact]
        public void A_longer_word_that_merely_starts_the_same_is_not_a_continuation()
        {
            // "سلامت" begins with "سلام" but is a different word: treating it as already typed
            // would lose it.
            Assert.False(DictationSession.ContinuesFrom("سلامت باشید", "سلام"));
        }

        [Fact]
        public void A_shortened_phrase_is_not_a_continuation()
        {
            // Google Translate revising downwards means it has changed its mind, not carried on.
            Assert.False(DictationSession.ContinuesFrom("سلام", "سلام دنیا"));
        }

        [Fact]
        public void Nothing_continues_from_nothing()
        {
            Assert.False(DictationSession.ContinuesFrom("سلام دنیا", null));
            Assert.False(DictationSession.ContinuesFrom("سلام دنیا", "   "));
            Assert.False(DictationSession.ContinuesFrom("", "سلام دنیا"));
        }

        [Fact]
        public void Surrounding_whitespace_does_not_matter()
        {
            Assert.True(DictationSession.ContinuesFrom("  سلام دنیا  ", "سلام دنیا"));
        }
    }
}

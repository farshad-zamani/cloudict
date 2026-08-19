using System.Linq;
using Cloudict;
using Xunit;

namespace Cloudict.Tests
{
    /// <summary>
    /// The dictionaries are the app's entire user-facing vocabulary, and a key present in one
    /// language but not the other shows up as a raw identifier in the interface. These checks are
    /// cheap and catch that at build time rather than in a screenshot.
    /// </summary>
    public class LocalizationTests
    {
        [Fact]
        public void EnglishLoadsAndIsTheDefault()
        {
            LocalizationManager.Apply("en");
            Assert.Equal("en", LocalizationManager.CurrentLanguage);
            Assert.False(LocalizationManager.IsRightToLeft);
            Assert.NotEmpty(LocalizationManager.Keys);
        }

        [Fact]
        public void PersianLoadsAndIsRightToLeft()
        {
            LocalizationManager.Apply("fa");
            Assert.Equal("fa", LocalizationManager.CurrentLanguage);
            Assert.True(LocalizationManager.IsRightToLeft);
            LocalizationManager.Apply("en");
        }

        [Fact]
        public void EveryEnglishKeyHasAPersianTranslation()
        {
            LocalizationManager.Apply("en");
            var english = LocalizationManager.Keys.ToHashSet();

            LocalizationManager.Apply("fa");
            var persian = LocalizationManager.Keys.ToHashSet();

            LocalizationManager.Apply("en");

            Assert.Empty(english.Except(persian).OrderBy(k => k));
            Assert.Empty(persian.Except(english).OrderBy(k => k));
        }

        [Fact]
        public void UnknownKeyFallsBackToTheKeyItself()
        {
            Assert.Equal("No_Such_Key_Here", LocalizationManager.Get("No_Such_Key_Here"));
        }

        [Fact]
        public void FormatArgumentsAreSubstituted()
        {
            // Settings_Milliseconds has no placeholder; use a key that does.
            var text = LocalizationManager.Get("Browser_St_DriverDownloaded", "151.0.0.1");
            Assert.Contains("151.0.0.1", text);
        }

        [Fact]
        public void MalformedFormatDoesNotThrow()
        {
            // Passing no arguments to a format string must not crash a status update.
            var text = LocalizationManager.Get("Browser_St_VersionMismatch");
            Assert.False(string.IsNullOrEmpty(text));
        }
    }
}

using Cloudict.Services;
using Xunit;

namespace Cloudict.Core.Tests
{
    /// <summary>
    /// The version comparison behind the update notice.
    ///
    /// <para>This is the part worth testing: a wrong answer here is silent. Comparing versions as
    /// text — which is the obvious way to write it — tells 3.1.20 it is older than 3.1.9, and
    /// everyone stops being offered updates the moment the patch number passes 9. The network call
    /// itself is not tested; it is allowed to fail, and does nothing when it does.</para>
    /// </summary>
    public class UpdateCheckerTests
    {
        [Theory]
        [InlineData("3.1.21", "3.1.20")]
        [InlineData("3.2.0", "3.1.20")]
        [InlineData("4.0.0", "3.9.9")]
        [InlineData("3.1.20", "3.1.9")]   // the string comparison gets this one wrong
        [InlineData("3.10.0", "3.9.0")]
        public void RecognisesANewerRelease(string candidate, string running) =>
            Assert.True(UpdateChecker.IsNewer(candidate, running));

        [Theory]
        [InlineData("3.1.20", "3.1.20")]
        [InlineData("3.1.19", "3.1.20")]
        [InlineData("3.0.0", "3.1.20")]
        [InlineData("2.3.1", "3.0.0")]
        [InlineData("3.1.9", "3.1.20")]
        public void DoesNotOfferTheSameOrAnOlderRelease(string candidate, string running) =>
            Assert.False(UpdateChecker.IsNewer(candidate, running));

        [Theory]
        [InlineData("v3.2.0", "3.1.20")]
        [InlineData("V3.2.0", "3.1.20")]
        [InlineData(" 3.2.0 ", "3.1.20")]
        public void IgnoresTheTagsLeadingLetterAndSpacing(string candidate, string running) =>
            Assert.True(UpdateChecker.IsNewer(candidate, running));

        /// <summary>A pre-release tag compares as its base version rather than failing to parse.</summary>
        [Theory]
        [InlineData("3.2.0-beta1", "3.1.20", true)]
        [InlineData("3.1.20-rc2", "3.1.20", false)]
        public void ComparesPreReleaseTagsByTheirBaseVersion(string candidate, string running, bool newer) =>
            Assert.Equal(newer, UpdateChecker.IsNewer(candidate, running));

        /// <summary>
        /// Anything unparseable means "no update". GitHub could change shape, or a tag could be
        /// named something else entirely; none of that should produce a bar offering a download.
        /// </summary>
        [Theory]
        [InlineData("", "3.1.20")]
        [InlineData(null, "3.1.20")]
        [InlineData("latest", "3.1.20")]
        [InlineData("nightly-2026-09-08", "3.1.20")]
        [InlineData("3.2.0", "")]
        public void TreatsAnythingItCannotParseAsNoUpdate(string candidate, string running) =>
            Assert.False(UpdateChecker.IsNewer(candidate, running));

        /// <summary>A two-component tag is still a version.</summary>
        [Fact]
        public void AcceptsATwoComponentVersion()
        {
            Assert.True(UpdateChecker.IsNewer("3.2", "3.1.20"));
            Assert.False(UpdateChecker.IsNewer("3.1", "3.1.20"));
        }
    }
}

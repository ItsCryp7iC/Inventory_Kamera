using Xunit;

namespace InventoryKamera.Tests
{
    public class ArtifactLevelParserTests
    {
        [Theory]
        [InlineData("+0", 0)]
        [InlineData("0", 0)]
        [InlineData("+20", 20)]
        [InlineData("  + 20  ", 20)]
        [InlineData("7", 7)]
        public void CompleteSupportedLevel_IsAccepted(string text, int expected)
        {
            Assert.True(ArtifactLevelParser.TryParse(text, out int level));
            Assert.Equal(expected, level);
        }

        [Theory]
        [InlineData("")]
        [InlineData("-1")]
        [InlineData("21")]
        [InlineData("+21")]
        [InlineData("Level 20")]
        [InlineData("+2O")]
        [InlineData("4/20")]
        [InlineData("20 garbage")]
        public void MalformedOrUnsupportedLevel_IsRejected(string text)
        {
            Assert.False(ArtifactLevelParser.TryParse(text, out int level));
            Assert.Equal(-1, level);
        }
    }
}

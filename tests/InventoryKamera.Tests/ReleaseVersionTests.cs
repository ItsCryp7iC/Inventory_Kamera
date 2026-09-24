using Xunit;

namespace InventoryKamera.Tests
{
    public class ReleaseVersionTests
    {
        [Theory]
        [InlineData("v0.1", "0.1")]
        [InlineData("v2.0.0", "2.0.0")]
        [InlineData("v1.3.17.2", "1.3.17.2")]
        public void TryParse_StableVersion_Succeeds(string tag, string expected)
        {
            Assert.True(ReleaseVersion.TryParse(tag, out var version));
            Assert.Equal(expected, version.ToString());
        }

        [Theory]
        [InlineData("v2.0.0-alpha", "2.0.0-alpha")]
        [InlineData("2.0.0-alpha", "2.0.0-alpha")]
        [InlineData("v2.0.0-preview", "2.0.0-preview")]
        [InlineData("v2.0.0-a", "2.0.0-a")]
        public void TryParse_PrereleaseVersion_Succeeds(string tag, string expected)
        {
            Assert.True(ReleaseVersion.TryParse(tag, out var version));
            Assert.Equal(expected, version.ToString());
        }

        [Fact]
        public void CompareTo_PrereleaseIsOlderThanStableWithSameNumericCore()
        {
            Assert.True(ReleaseVersion.TryParse("v2.0.0-alpha", out var prerelease));
            Assert.True(ReleaseVersion.TryParse("v2.0.0", out var stable));

            Assert.True(prerelease.CompareTo(stable) < 0);
            Assert.True(stable.CompareTo(prerelease) > 0);
        }

        [Fact]
        public void CompareTo_DifferentPrereleaseLabelsUseSemanticOrdering()
        {
            Assert.True(ReleaseVersion.TryParse("v2.0.0-a", out var abbreviatedAlpha));
            Assert.True(ReleaseVersion.TryParse("v2.0.0-alpha", out var alpha));
            Assert.True(ReleaseVersion.TryParse("v2.0.0-preview", out var preview));

            Assert.True(abbreviatedAlpha.CompareTo(alpha) < 0);
            Assert.True(alpha.CompareTo(preview) < 0);
            Assert.NotEqual(alpha, preview);
        }

        [Fact]
        public void CompareTo_NumericPrereleaseIdentifiersUseNumericOrdering()
        {
            Assert.True(ReleaseVersion.TryParse("v2.0.0-alpha.2", out var alpha2));
            Assert.True(ReleaseVersion.TryParse("v2.0.0-alpha.10", out var alpha10));

            Assert.True(alpha2.CompareTo(alpha10) < 0);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("v2.0.0-")]
        [InlineData("v2.alpha.0")]
        [InlineData("v2.0.0-alpha..1")]
        [InlineData("v2.0.0-alpha.01")]
        [InlineData("v2.0.0 alpha")]
        public void TryParse_InvalidTag_ReturnsFalse(string tag)
        {
            Assert.False(ReleaseVersion.TryParse(tag, out var version));
            Assert.Null(version);
        }
    }
}

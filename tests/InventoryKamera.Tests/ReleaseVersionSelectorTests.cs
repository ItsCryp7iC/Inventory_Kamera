using System.Collections.Generic;
using Xunit;

namespace InventoryKamera.Tests
{
    public class ReleaseVersionSelectorTests
    {
        [Fact]
        public void TrySelectFirst_MalformedFirstRelease_SelectsFollowingValidRelease()
        {
            var malformedTags = new List<string>();
            var releases = new[] { "not-a-version", "v2.0.0-alpha", "v2.0.0" };

            var found = ReleaseVersionSelector.TrySelectFirst(
                releases,
                tag => tag,
                malformedTags.Add,
                out var selectedTag,
                out var selectedVersion);

            Assert.True(found);
            Assert.Equal("v2.0.0-alpha", selectedTag);
            Assert.Equal("2.0.0-alpha", selectedVersion.ToString());
            Assert.Equal(new[] { "not-a-version" }, malformedTags);
        }

        [Fact]
        public void TrySelectFirst_MultipleMalformedReleases_SelectsFollowingValidRelease()
        {
            var malformedTags = new List<string>();
            var releases = new[] { "invalid", "v2.0.0-", "v2.0.0-preview", "v2.0.0" };

            var found = ReleaseVersionSelector.TrySelectFirst(
                releases,
                tag => tag,
                malformedTags.Add,
                out var selectedTag,
                out var selectedVersion);

            Assert.True(found);
            Assert.Equal("v2.0.0-preview", selectedTag);
            Assert.Equal("2.0.0-preview", selectedVersion.ToString());
            Assert.Equal(new[] { "invalid", "v2.0.0-" }, malformedTags);
        }

        [Fact]
        public void TrySelectFirst_NoValidReleases_ReturnsFalse()
        {
            var malformedTags = new List<string>();
            var releases = new[] { "invalid", "v2.0.0-", "v2.alpha.0" };

            var found = ReleaseVersionSelector.TrySelectFirst(
                releases,
                tag => tag,
                malformedTags.Add,
                out var selectedTag,
                out var selectedVersion);

            Assert.False(found);
            Assert.Null(selectedTag);
            Assert.Null(selectedVersion);
            Assert.Equal(releases, malformedTags);
        }
    }
}

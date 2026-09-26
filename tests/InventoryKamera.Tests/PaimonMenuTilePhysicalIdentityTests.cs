using InventoryKamera.game;
using System.Drawing;
using Xunit;

namespace InventoryKamera.Tests
{
    public class PaimonMenuTilePhysicalIdentityTests
    {
        [Fact]
        public void SameLabelAtClearlyDifferentCentersIsDifferent()
        {
            PaimonMenuTile first = Tile("Archive", 0, 0);
            PaimonMenuTile adjacent = Tile("Archive", 130, 0);

            Assert.False(PaimonMenuTilePhysicalIdentity.AreSame(first, adjacent));
        }

        [Fact]
        public void DifferentLabelsAtSameCenterAreSame()
        {
            PaimonMenuTile first = Tile("Archive", 0, 0);
            PaimonMenuTile noisy = Tile("Character Archive", 0, 0);

            Assert.True(PaimonMenuTilePhysicalIdentity.AreSame(first, noisy));
        }

        [Fact]
        public void SmallCenterJitterIsSame()
        {
            PaimonMenuTile first = Tile("Archive", 100, 100);
            PaimonMenuTile jittered = Tile("Archive", 104, 97);

            Assert.True(PaimonMenuTilePhysicalIdentity.AreSame(first, jittered));
        }

        [Fact]
        public void SmallBoundsJitterIsSame()
        {
            PaimonMenuTile first = Tile("Archive", new Rectangle(100, 100, 120, 108));
            PaimonMenuTile jittered = Tile("Archive", new Rectangle(97, 103, 126, 102));

            Assert.True(PaimonMenuTilePhysicalIdentity.AreSame(first, jittered));
        }

        [Fact]
        public void AdjacentTileMovementIsDifferent()
        {
            PaimonMenuTile first = Tile("First", 100, 100);
            PaimonMenuTile adjacent = Tile("Second", 100, 218);

            Assert.False(PaimonMenuTilePhysicalIdentity.AreSame(first, adjacent));
        }

        [Fact]
        public void TargetCorrespondenceRejectsAmbiguousOverlappingDetections()
        {
            PaimonMenuTile selected = Tile("Selected", 100, 100);
            PaimonMenuTile semanticTarget = Tile("Character", 102, 99);
            PaimonMenuTile duplicate = Tile("Duplicate", 98, 102);

            Assert.False(PaimonMenuTilePhysicalIdentity.AreUniqueCorrespondingTiles(
                selected,
                semanticTarget,
                new[] { semanticTarget, duplicate }));
        }

        private static PaimonMenuTile Tile(string label, int x, int y) =>
            Tile(label, new Rectangle(x, y, 120, 108));

        private static PaimonMenuTile Tile(string label, Rectangle bounds) =>
            new PaimonMenuTile(label, Rectangle.Empty, bounds, 1f);
    }
}

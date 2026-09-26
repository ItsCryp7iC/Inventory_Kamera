using InventoryKamera;
using Xunit;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// Characterization tests for the manequin placeholder entries the game-data loader adds when
    /// hosted data omits them. These tests pin the shape of the pure object-model entry builder.
    /// </summary>
    public class ManequinEntryTests
    {
        [Theory]
        [InlineData("manequin1", "Manequin1")]
        [InlineData("manequin2", "Manequin2")]
        public void BuildManequinEntry_SetsGoodNameFromKey(string key, string expectedGood)
        {
            var entry = GenshinProcesor.BuildManequinEntry(key);
            Assert.Equal(expectedGood, (string)entry["GOOD"]);
        }

        [Fact]
        public void BuildManequinEntry_HasAllSixElements()
        {
            var entry = GenshinProcesor.BuildManequinEntry("manequin1");
            var elements = entry["Element"].ToObject<string[]>();

            Assert.Equal(new[] { "electro", "pyro", "dendro", "geo", "hydro", "anemo" }, elements);
        }

        [Fact]
        public void BuildManequinEntry_HasBurstThenSkillConstellationOrder()
        {
            var entry = GenshinProcesor.BuildManequinEntry("manequin1");
            var order = entry["ConstellationOrder"].ToObject<string[]>();

            Assert.Equal(new[] { "burst", "skill" }, order);
        }

        [Fact]
        public void BuildManequinEntry_DefaultsToSwordWeaponType()
        {
            var entry = GenshinProcesor.BuildManequinEntry("manequin1");
            Assert.Equal(0, (int)entry["WeaponType"]);
        }
    }
}

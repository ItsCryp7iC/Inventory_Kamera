using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Xunit;

namespace InventoryKamera.Tests
{
    public class WeaponGoodExportTests
    {
        [Fact]
        public void InvalidWeaponIsOmittedWhileLaterValidWeaponRemainsUnchanged()
        {
            Weapon invalid = WeaponTestData.WeaponAt(1, "UnknownWeapon", "Jean");
            Weapon valid = WeaponTestData.WeaponAt(3, "FavoniusSword", "Zhongli");

            GOOD good = Export(new[] { invalid, valid });
            JObject json = JObject.Parse(good.ToString());

            Weapon exported = Assert.Single(good.Weapons);
            Assert.Same(valid, exported);
            Assert.Equal("FavoniusSword", exported.Name);
            Assert.Equal("Zhongli", exported.EquippedCharacter);
            Assert.Equal(3, (int)json["weapons"][0]["id"]);
        }

        [Fact]
        public void WeaponWithoutExplicitGameDataCannotCrossGoodBoundary()
        {
            var unbound = new Weapon("FavoniusSword", 90, true, 1, _rarity: 5);

            GOOD good = Export(new[] { unbound });

            Assert.Null(good.Weapons);
            Assert.Null(JObject.Parse(good.ToString())["weapons"]);
        }

        [Fact]
        public void ValidDefaultWeaponStillReachesGood()
        {
            var weapon = new Weapon(WeaponType.Sword, "Jean", WeaponTestData.Snapshot);

            GOOD good = Export(new[] { weapon });

            Assert.Same(weapon, Assert.Single(good.Weapons));
            Assert.Equal("DullBlade", good.Weapons[0].Name);
        }

        private static GOOD Export(IEnumerable<Weapon> weapons) =>
            new GOOD(
                Array.Empty<Character>(),
                weapons,
                Array.Empty<Artifact>(),
                Array.Empty<Material>(),
                equipWeapons: true,
                equipArtifacts: true);
    }
}

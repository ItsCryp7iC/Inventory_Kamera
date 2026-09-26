using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Xunit;

namespace InventoryKamera.Tests
{
    public class GameDataSnapshotTests
    {
        [Fact]
        public void ExposesExpectedReadOnlyLookupCollections()
        {
            GameDataSnapshot snapshot = CreateSnapshot("oldblade", "OldBlade", "diluc", "Diluc");

            Assert.Equal("OldBlade", snapshot.Weapons["oldblade"]);
            Assert.Equal("Diluc", (string)snapshot.Characters["diluc"]["GOOD"]);
            Assert.Equal("GladiatorsFinale", (string)snapshot.Artifacts["gladiatorsfinale"]["GOOD"]);
            Assert.Equal("Mora", snapshot.Materials["mora"]);
            Assert.Equal("HerosWit", snapshot.CharacterDevelopmentItems["heroswit"]);
            Assert.Equal("critRate_", snapshot.Stats["critrate"]);
            Assert.Equal("Pyro", snapshot.Elements["pyro"]);
            Assert.Contains("flower", snapshot.GearSlots);
            Assert.Contains("enhancementore", snapshot.EnhancementMaterials);
        }

        [Fact]
        public void PublicCollectionsCannotBeMutatedOrReplaced()
        {
            GameDataSnapshot snapshot = CreateSnapshot("oldblade", "OldBlade", "diluc", "Diluc");
            var weapons = Assert.IsAssignableFrom<IDictionary<string, string>>(snapshot.Weapons);
            var characters = Assert.IsAssignableFrom<IDictionary<string, JObject>>(snapshot.Characters);

            Assert.Throws<NotSupportedException>(() => weapons.Add("newblade", "NewBlade"));
            Assert.Throws<NotSupportedException>(() => characters["diluc"] = new JObject());
        }

        [Fact]
        public void ConstructingSecondSnapshotDoesNotAlterFirst()
        {
            var sourceWeapons = new Dictionary<string, string> { ["oldblade"] = "OldBlade" };
            GameDataSnapshot first = CreateSnapshot(
                sourceWeapons, Character("Diluc", "pyro", rarity: 5), "diluc");

            sourceWeapons.Clear();
            sourceWeapons["newblade"] = "NewBlade";
            GameDataSnapshot second = CreateSnapshot(
                sourceWeapons, Character("Jean", "anemo", rarity: 5), "jean");

            Assert.True(first.Weapons.ContainsKey("oldblade"));
            Assert.False(first.Weapons.ContainsKey("newblade"));
            Assert.True(second.Weapons.ContainsKey("newblade"));
            Assert.False(second.Characters.ContainsKey("diluc"));
        }

        [Fact]
        public void CustomNameSnapshotClonesAffectedCharacterOnly()
        {
            GameDataSnapshot original = CreateSnapshot("oldblade", "OldBlade", "wanderer", "Wanderer");

            GameDataSnapshot customized = original.WithCharacterCustomNames(
                new Dictionary<string, string> { ["wanderer"] = "Hat Guy" });

            Assert.Null(original.Characters["wanderer"]["CustomName"]);
            Assert.Equal("hatguy", (string)customized.Characters["wanderer"]["CustomName"]);
            Assert.NotSame(original.Characters["wanderer"], customized.Characters["wanderer"]);
        }

        [Fact]
        public void LookupAndNormalizationUseOnlySuppliedSnapshot()
        {
            GameDataSnapshot snapshot = CreateSnapshot("favoniussword", "FavoniusSword", "diluc", "Diluc");

            Assert.True(LookupService.IsValidWeapon("FavoniusSword", snapshot));
            Assert.True(LookupService.IsValidSetName("GladiatorsFinale", snapshot));
            Assert.True(LookupService.IsValidMaterial("Mora", snapshot));
            Assert.Equal("FavoniusSword", TextNormalizer.FindClosestWeapon("favoniussword", snapshot));
            Assert.Equal("GladiatorsFinale",
                TextNormalizer.FindClosestArtifactSetFromArtifactName("gladiatorsnostalgia", snapshot));
            Assert.Equal("HerosWit", TextNormalizer.FindClosestDevelopmentName("heroswit", snapshot));
        }

        [Fact]
        public void CharacterRarityElementAndWeaponTypeUseSuppliedSnapshot()
        {
            GameDataSnapshot snapshot = CreateSnapshot("oldblade", "OldBlade", "gaming", "Gaming", rarity: 4);
            var character = new Character
            {
                NameGOOD = "Gaming",
                Element = "Pyro",
                Level = 90,
                Constellation = 0,
            };
            character.Talents["auto"] = 1;
            character.Talents["skill"] = 1;
            character.Talents["burst"] = 1;
            character.UseGameData(snapshot);

            Assert.True(LookupService.IsFourStarCharacter("Gaming", snapshot));
            Assert.True(LookupService.CharacterMatchesElement("gaming", "Pyro", snapshot));
            Assert.Equal(WeaponType.Sword, character.WeaponType);
            Assert.True(character.IsValid());
        }

        [Fact]
        public void WeaponAndArtifactValidationUseSuppliedSnapshot()
        {
            GameDataSnapshot snapshot = CreateSnapshot("favoniussword", "FavoniusSword", "diluc", "Diluc");
            var weapon = new Weapon("FavoniusSword", 90, true, 5, _rarity: 5, gameData: snapshot);
            var artifact = new Artifact(
                "GladiatorsFinale",
                5,
                20,
                "flower",
                "hp",
                new List<Artifact.SubStat> { new Artifact.SubStat { stat = "critRate_", value = 3.9m } },
                new List<Artifact.SubStat>(),
                gameData: snapshot);

            Assert.True(weapon.IsValid());
            Assert.True(artifact.IsValid());
        }

        [Fact]
        public void ScannerDependencyGraphsKeepIndependentSnapshots()
        {
            GameDataSnapshot firstSnapshot = CreateSnapshot("oldblade", "OldBlade", "diluc", "Diluc");
            GameDataSnapshot secondSnapshot = CreateSnapshot("newblade", "NewBlade", "jean", "Jean");
            using var firstSession = new ScanSession();
            using var secondSession = new ScanSession();
            var first = new GameScanner(new ScanViewModel(), firstSession, firstSnapshot);
            var second = new GameScanner(new ScanViewModel(), secondSession, secondSnapshot);

            Assert.Same(firstSnapshot, first.GameData);
            Assert.Same(secondSnapshot, second.GameData);
            Assert.True(LookupService.IsValidWeapon("OldBlade", first.GameData));
            Assert.False(LookupService.IsValidWeapon("OldBlade", second.GameData));
        }

        [Fact]
        public void NewApplicationSnapshotDoesNotChangeExistingConsumer()
        {
            GameDataSnapshot oldSnapshot = CreateSnapshot("oldblade", "OldBlade", "diluc", "Diluc");
            var existingWeapon = new Weapon("OldBlade", 90, true, 5, _rarity: 5, gameData: oldSnapshot);
            GameDataSnapshot newSnapshot = CreateSnapshot("newblade", "NewBlade", "jean", "Jean");
            var futureWeapon = new Weapon("NewBlade", 90, true, 5, _rarity: 5, gameData: newSnapshot);

            Assert.True(existingWeapon.HasValidWeaponName());
            Assert.True(futureWeapon.HasValidWeaponName());
            Assert.False(LookupService.IsValidWeapon("NewBlade", oldSnapshot));
        }

        private static GameDataSnapshot CreateSnapshot(
            string weaponKey,
            string weaponName,
            string characterKey,
            string characterName,
            int rarity = 5) =>
            CreateSnapshot(
                new Dictionary<string, string> { [weaponKey] = weaponName },
                Character(characterName, "pyro", rarity),
                characterKey);

        private static GameDataSnapshot CreateSnapshot(
            IReadOnlyDictionary<string, string> weapons,
            JObject character,
            string characterKey)
        {
            var artifact = new JObject
            {
                ["GOOD"] = "GladiatorsFinale",
                ["artifacts"] = new JObject
                {
                    ["flower"] = new JObject { ["normalizedName"] = "gladiatorsnostalgia" },
                },
            };
            return new GameDataSnapshot(
                new Dictionary<string, JObject> { [characterKey] = character },
                new Dictionary<string, JObject> { ["gladiatorsfinale"] = artifact },
                weapons,
                new Dictionary<string, string> { ["heroswit"] = "HerosWit" },
                new Dictionary<string, string> { ["mora"] = "Mora" },
                new Dictionary<string, string> { ["hp"] = "hp", ["critrate"] = "critRate_" },
                new Dictionary<string, string> { ["pyro"] = "Pyro", ["anemo"] = "Anemo" },
                new[] { "flower", "plume", "sands", "goblet", "circlet" },
                new[] { "enhancementore" });
        }

        private static JObject Character(string name, string element, int rarity) => new JObject
        {
            ["GOOD"] = name,
            ["Element"] = new JArray(element),
            ["Rarity"] = rarity,
            ["WeaponType"] = (int)WeaponType.Sword,
            ["ConstellationOrder"] = new JArray("skill", "burst"),
        };
    }
}

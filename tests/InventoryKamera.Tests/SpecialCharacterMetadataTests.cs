using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace InventoryKamera.Tests
{
    public class SpecialCharacterMetadataTests
    {
        [Fact]
        public void TravelerMissingCryoIsRepairedWithoutMutatingSource()
        {
            JObject traveler = Character("Traveler", "anemo");
            var source = new Dictionary<string, JObject> { ["traveler"] = traveler };

            Dictionary<string, JObject> normalized =
                GameDataSnapshotFactory.NormalizeSpecialCharacterMetadata(source);

            Assert.Contains("cryo", Elements(normalized["traveler"]));
            Assert.Equal(
                new[] { "burst", "skill" },
                normalized["traveler"]["ConstellationOrder"]["cryo"].Values<string>().ToArray());
            Assert.DoesNotContain("cryo", Elements(traveler));
            Assert.NotSame(traveler, normalized["traveler"]);
        }

        [Fact]
        public void TravelerAlreadyContainingCryoRemainsIdempotent()
        {
            var source = new Dictionary<string, JObject>
            {
                ["traveler"] = Character("Traveler", "anemo", "cryo"),
            };

            Dictionary<string, JObject> first =
                GameDataSnapshotFactory.NormalizeSpecialCharacterMetadata(source);
            Dictionary<string, JObject> second =
                GameDataSnapshotFactory.NormalizeSpecialCharacterMetadata(first);

            Assert.Equal(1, Elements(first["traveler"]).Count(IsCryo));
            Assert.Equal(1, Elements(second["traveler"]).Count(IsCryo));
            Assert.Equal(Elements(first["traveler"]), Elements(second["traveler"]));
        }

        [Theory]
        [InlineData("manequin1")]
        [InlineData("manequin2")]
        public void GeneratedManequinContainsCryo(string key)
        {
            JObject entry = GameDataSnapshotFactory.BuildManequinEntry(key);

            Assert.Contains("cryo", Elements(entry));
            Assert.Equal(1, Elements(entry).Count(IsCryo));
        }

        [Theory]
        [InlineData("manequin1", "Manequin1")]
        [InlineData("manequin2", "Manequin2")]
        public void ExistingManequinMissingCryoIsRepaired(string key, string goodName)
        {
            JObject existing = Character(goodName, "anemo");
            var source = new Dictionary<string, JObject> { [key] = existing };

            Dictionary<string, JObject> normalized =
                GameDataSnapshotFactory.NormalizeSpecialCharacterMetadata(source);

            Assert.Contains("cryo", Elements(normalized[key]));
            Assert.DoesNotContain("cryo", Elements(existing));
            Assert.NotSame(existing, normalized[key]);
        }

        [Fact]
        public void RepeatedNormalizationDoesNotDuplicateCryo()
        {
            var source = new Dictionary<string, JObject>
            {
                ["traveler"] = Character("Traveler", "anemo"),
                ["manequin1"] = Character("Manequin1", "anemo"),
                ["manequin2"] = Character("Manequin2", "anemo"),
            };

            Dictionary<string, JObject> first =
                GameDataSnapshotFactory.NormalizeSpecialCharacterMetadata(source);
            Dictionary<string, JObject> second =
                GameDataSnapshotFactory.NormalizeSpecialCharacterMetadata(first);

            foreach (string key in new[] { "traveler", "manequin1", "manequin2" })
                Assert.Equal(1, Elements(second[key]).Count(IsCryo));
        }

        [Theory]
        [InlineData("Luna", "cryo", "Manequin1")]
        [InlineData("luna", "cryo", "Manequin1")]
        [InlineData("Cryptic", "cryo", "Manequin2")]
        [InlineData("cryptic.", "cryo", "Manequin2")]
        [InlineData("Cryp7iC", "cryo", "Traveler")]
        [InlineData("cryp7ic.", "cryo", "Traveler")]
        public void CustomNameAndCryoResolveToCanonicalCharacter(
            string displayedName,
            string element,
            string expectedCanonicalName)
        {
            GameDataSnapshot snapshot = CreateCustomizedSnapshot();

            string canonicalName = Resolve(displayedName, element, snapshot);

            Assert.Equal(expectedCanonicalName, canonicalName);
        }

        [Fact]
        public void OrdinaryCharacterMatchingIsUnchanged()
        {
            GameDataSnapshot snapshot = CreateCustomizedSnapshot();

            string canonicalName = Resolve("Diluc", "pyro", snapshot);

            Assert.Equal("Diluc", canonicalName);
        }

        [Theory]
        [InlineData("Luna", "Manequin1")]
        [InlineData("Cryptic", "Manequin2")]
        public void RecognizedManequinIsTreatedAsNonExportedPlaceholder(
            string displayedName,
            string expectedCanonicalName)
        {
            GameDataSnapshot snapshot = CreateCustomizedSnapshot();

            string canonicalName = Resolve(displayedName, "cryo", snapshot);

            Assert.Equal(expectedCanonicalName, canonicalName);
            Assert.True(CharacterScraper.IsManequinPlaceholder(canonicalName));
        }

        [Fact]
        public void CryoTravelerRetainsElementSpecificGoodKey()
        {
            GameDataSnapshot snapshot = CreateCustomizedSnapshot();
            string canonicalName = Resolve("Cryp7iC", "cryo", snapshot);
            var character = new Character(snapshot)
            {
                NameGOOD = canonicalName,
                Element = "Cryo",
                Level = 90,
                Constellation = 0,
            };
            character.Talents["auto"] = 1;
            character.Talents["skill"] = 1;
            character.Talents["burst"] = 1;

            Assert.True(character.IsValid());
            Assert.Equal("TravelerCryo", character.NameGOOD);
        }

        [Fact]
        public void CustomizingSnapshotDoesNotChangeSourceSnapshot()
        {
            GameDataSnapshot source = CreateBaseSnapshot();

            GameDataSnapshot customized = source.WithCharacterCustomNames(CustomNames());

            Assert.Null(source.Characters["traveler"]["CustomName"]);
            Assert.Null(source.Characters["manequin1"]["CustomName"]);
            Assert.Equal("cryp7ic", (string)customized.Characters["traveler"]["CustomName"]);
            Assert.Equal("luna", (string)customized.Characters["manequin1"]["CustomName"]);
        }

        [Fact]
        public void MatchingUsesOnlySuppliedSnapshot()
        {
            GameDataSnapshot adversarial = CreateBaseSnapshot().WithCharacterCustomNames(
                new Dictionary<string, string> { ["manequin1"] = "Unrelated Alias" });
            GameDataSnapshot supplied = CreateCustomizedSnapshot();

            Assert.Equal("Manequin1", Resolve("Luna", "cryo", supplied));
            Assert.NotEqual(
                "Manequin1",
                TextNormalizer.FindClosestCharacterName("luna", adversarial));
        }

        private static GameDataSnapshot CreateCustomizedSnapshot() =>
            CreateBaseSnapshot().WithCharacterCustomNames(CustomNames());

        private static GameDataSnapshot CreateBaseSnapshot()
        {
            Dictionary<string, JObject> characters =
                GameDataSnapshotFactory.NormalizeSpecialCharacterMetadata(
                    new Dictionary<string, JObject>
                    {
                        ["traveler"] = Character("Traveler", "anemo"),
                        ["diluc"] = Character("Diluc", "pyro"),
                    });

            return new GameDataSnapshot(
                characters,
                new Dictionary<string, JObject>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string> { ["hp"] = "hp" },
                new Dictionary<string, string>
                {
                    ["pyro"] = "Pyro",
                    ["hydro"] = "Hydro",
                    ["dendro"] = "Dendro",
                    ["electro"] = "Electro",
                    ["anemo"] = "Anemo",
                    ["cryo"] = "Cryo",
                    ["geo"] = "Geo",
                },
                new[] { "flower" },
                new[] { "enhancementore" });
        }

        private static Dictionary<string, string> CustomNames() => new Dictionary<string, string>
        {
            ["traveler"] = "Cryp7iC",
            ["manequin1"] = "Luna",
            ["manequin2"] = "Cryptic",
        };

        private static string Resolve(string displayedName, string element, GameDataSnapshot snapshot)
        {
            string normalizedName = Regex.Replace(displayedName.ToLowerInvariant(), @"[\W]", string.Empty);
            string canonicalName = TextNormalizer.FindClosestCharacterName(normalizedName, snapshot);
            Assert.True(LookupService.CharacterMatchesElement(canonicalName, element, snapshot));
            return canonicalName;
        }

        private static JObject Character(string goodName, params string[] elements) => new JObject
        {
            ["GOOD"] = goodName,
            ["Element"] = new JArray(elements),
            ["Rarity"] = 5,
            ["WeaponType"] = (int)WeaponType.Sword,
            ["ConstellationOrder"] = new JArray("skill", "burst"),
        };

        private static string[] Elements(JObject character) =>
            character["Element"].Values<string>().ToArray();

        private static bool IsCryo(string element) =>
            string.Equals(element, "cryo", StringComparison.OrdinalIgnoreCase);
    }
}

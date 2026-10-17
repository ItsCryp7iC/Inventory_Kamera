using Newtonsoft.Json.Linq;
using System.Linq;
using Xunit;

namespace InventoryKamera.Tests
{
    public class TravelerCryoConstellationOrderTests
    {
        [Fact]
        public void MissingCryoOrderIsRepairedWithoutMutatingSource()
        {
            JObject source = Traveler(new JObject
            {
                ["anemo"] = new JArray("burst", "skill"),
            });

            JObject normalized = GameDataSnapshotFactory.NormalizeTravelerMetadata(source);

            Assert.Equal(new[] { "burst", "skill" }, Order(normalized, "cryo"));
            Assert.Null(((JObject)source["ConstellationOrder"])["cryo"]);
            Assert.DoesNotContain("cryo", source["Element"].Values<string>());
        }

        [Fact]
        public void ExistingCorrectCryoOrderIsPreserved()
        {
            JObject source = Traveler(new JObject
            {
                ["cryo"] = new JArray("burst", "skill"),
            }, "anemo", "cryo");

            JObject normalized = GameDataSnapshotFactory.NormalizeTravelerMetadata(source);

            Assert.Equal(new[] { "burst", "skill" }, Order(normalized, "cryo"));
            Assert.Equal(new[] { "burst", "skill" }, Order(source, "cryo"));
        }

        [Fact]
        public void IncorrectCryoOrderIsRepaired()
        {
            JObject source = Traveler(new JObject
            {
                ["cryo"] = new JArray("skill", "burst"),
            }, "cryo");

            JObject normalized = GameDataSnapshotFactory.NormalizeTravelerMetadata(source);

            Assert.Equal(new[] { "burst", "skill" }, Order(normalized, "cryo"));
            Assert.Equal(new[] { "skill", "burst" }, Order(source, "cryo"));
        }

        [Fact]
        public void RepeatedNormalizationIsIdempotent()
        {
            JObject first = GameDataSnapshotFactory.NormalizeTravelerMetadata(
                Traveler(new JObject
                {
                    ["anemo"] = new JArray("burst", "skill"),
                }));

            JObject second = GameDataSnapshotFactory.NormalizeTravelerMetadata(first);

            Assert.True(JToken.DeepEquals(first, second));
            Assert.Single(((JObject)second["ConstellationOrder"]).Properties(),
                property => property.Name == "cryo");
        }

        [Fact]
        public void ExistingNonCryoOrdersAreUnchanged()
        {
            var existingOrders = new JObject
            {
                ["anemo"] = new JArray("burst", "skill"),
                ["geo"] = new JArray("burst", "skill"),
                ["electro"] = new JArray("burst", "skill"),
                ["dendro"] = new JArray("skill", "burst"),
                ["hydro"] = new JArray("skill", "burst"),
                ["pyro"] = new JArray("skill", "burst"),
            };
            JObject source = Traveler(existingOrders);

            JObject normalized = GameDataSnapshotFactory.NormalizeTravelerMetadata(source);
            var normalizedOrders = (JObject)normalized["ConstellationOrder"];

            foreach (JProperty existing in existingOrders.Properties())
                Assert.True(JToken.DeepEquals(existing.Value, normalizedOrders[existing.Name]));
        }

        [Fact]
        public void NormalizedTravelerIsIsolatedFromLaterSourceChanges()
        {
            JObject source = Traveler(new JObject
            {
                ["anemo"] = new JArray("burst", "skill"),
            });
            JObject normalized = GameDataSnapshotFactory.NormalizeTravelerMetadata(source);

            ((JArray)((JObject)source["ConstellationOrder"])["anemo"])[0] = "skill";
            ((JArray)((JObject)source["ConstellationOrder"])["anemo"])[1] = "burst";

            Assert.Equal(new[] { "burst", "skill" }, Order(normalized, "anemo"));
            Assert.Equal(new[] { "burst", "skill" }, Order(normalized, "cryo"));
        }

        [Theory]
        [InlineData(0, 8, 10)]
        [InlineData(1, 8, 10)]
        [InlineData(2, 8, 10)]
        [InlineData(3, 8, 7)]
        [InlineData(4, 8, 7)]
        [InlineData(5, 5, 7)]
        [InlineData(6, 5, 7)]
        public void CryoTravelerAdjustsBurstAtC3AndSkillAtC5(
            int constellation,
            int expectedSkill,
            int expectedBurst)
        {
            var traveler = new Character
            {
                NameGOOD = "Traveler",
                Element = "Cryo",
                Constellation = constellation,
            };
            traveler.Talents["auto"] = 6;
            traveler.Talents["skill"] = 8;
            traveler.Talents["burst"] = 10;

            CharacterScraper.ApplyConstellationTalentAdjustments(traveler, "burst", "skill");

            Assert.Equal(6, traveler.Talents["auto"]);
            Assert.Equal(expectedSkill, traveler.Talents["skill"]);
            Assert.Equal(expectedBurst, traveler.Talents["burst"]);
        }

        private static JObject Traveler(JObject constellationOrder, params string[] elements) =>
            new JObject
            {
                ["GOOD"] = "Traveler",
                ["Element"] = new JArray(elements.Length == 0 ? new[] { "anemo" } : elements),
                ["ConstellationOrder"] = constellationOrder,
                ["WeaponType"] = (int)WeaponType.Sword,
                ["Rarity"] = 5,
            };

        private static string[] Order(JObject traveler, string element) =>
            ((JObject)traveler["ConstellationOrder"])[element].Values<string>().ToArray();
    }
}

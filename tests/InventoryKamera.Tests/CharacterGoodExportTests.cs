using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Xunit;

namespace InventoryKamera.Tests
{
    public class CharacterGoodExportTests
    {
        [Fact]
        public void SuccessfulValidCharacterIsExportedUnchanged()
        {
            Character jean = CompleteCharacter("Jean", "Anemo");

            var good = Export(new[] { jean });
            JObject json = JObject.Parse(good.ToString());

            Assert.Single(good.Characters);
            Assert.Equal("Jean", (string)json["characters"][0]["key"]);
            Assert.Equal(0, (int)json["characters"][0]["constellation"]);
            Assert.Equal(6, (int)json["characters"][0]["talent"]["auto"]);
        }

        [Fact]
        public void VerificationFailureSentinelsNeverCrossGoodBoundary()
        {
            Character zhongli = CompleteCharacter("Zhongli", "Geo");
            CharacterScraper.MarkCharacterPhaseUnavailable(
                zhongli,
                CharacterVerificationPhase.Constellation);
            CharacterScraper.MarkCharacterPhaseUnavailable(
                zhongli,
                CharacterVerificationPhase.Talent);

            var good = Export(new[] { zhongli });
            JObject json = JObject.Parse(good.ToString());

            Assert.Null(good.Characters);
            Assert.Null(json["characters"]);
            Assert.DoesNotContain("-1", good.ToString());
        }

        [Fact]
        public void TalentOcrFailureIsOmittedFromGood()
        {
            Character zhongli = CharacterWithAttributes("Zhongli", "Geo");
            zhongli.Constellation = 0;
            zhongli.MarkConstellationScanSucceeded();
            bool consensusReached = TalentReadConsensus.TryRead(
                2,
                attempt => attempt == 1
                    ? "Lv. 1\nLv. 10\nLv. 9"
                    : "Lv. 1\nLv. 10",
                null,
                null,
                out _);
            bool talentsSucceeded = CharacterScraper.TrySetScannedTalents(
                zhongli,
                consensusReached
                    ? throw new InvalidOperationException("The test input must not reach consensus.")
                    : CharacterScraper.UnavailableTalents());

            var good = Export(new[] { zhongli });

            Assert.False(consensusReached);
            Assert.False(talentsSucceeded);
            Assert.Equal(CharacterScanPhaseStatus.Failed, zhongli.TalentScanStatus);
            Assert.Null(good.Characters);
        }

        [Fact]
        public void ConstructorDefaultsAndNotAttemptedPhasesAreNotExported()
        {
            Character zhongli = CharacterWithAttributes("Zhongli", "Geo");

            var good = Export(new[] { zhongli });

            Assert.Equal(CharacterScanPhaseStatus.NotAttempted, zhongli.ConstellationScanStatus);
            Assert.Equal(CharacterScanPhaseStatus.NotAttempted, zhongli.TalentScanStatus);
            Assert.Null(good.Characters);
        }

        [Fact]
        public void ModelValidationFailurePreventsExportEvenAfterSuccessfulPhases()
        {
            Character zhongli = CompleteCharacter("Zhongli", "Geo");
            zhongli.Level = 0;

            var good = Export(new[] { zhongli });

            Assert.False(zhongli.IsValid());
            Assert.Null(good.Characters);
        }

        [Fact]
        public void SuccessfulTravelerKeepsElementSpecificGoodKey()
        {
            Character traveler = CompleteCharacter("Traveler", "Cryo");

            var good = Export(new[] { traveler });
            JObject json = JObject.Parse(good.ToString());

            Assert.Equal("Traveler", traveler.CanonicalName);
            Assert.Equal("TravelerCryo", (string)json["characters"][0]["key"]);
        }

        [Fact]
        public void OmittedCharacterDoesNotClearEquipmentLocations()
        {
            Character zhongli = CompleteCharacter("Zhongli", "Geo");
            CharacterScraper.MarkCharacterPhaseUnavailable(
                zhongli,
                CharacterVerificationPhase.Talent);
            var weapon = new Weapon(
                "TestPolearm",
                90,
                true,
                1,
                _equippedCharacter: "Zhongli",
                _rarity: 5);
            var artifact = new Artifact(
                "TestSet",
                5,
                20,
                "flower",
                "hp",
                new List<Artifact.SubStat>(),
                new List<Artifact.SubStat>(),
                _equippedCharacter: "Zhongli");

            var good = Export(new[] { zhongli }, new[] { weapon }, new[] { artifact });
            JObject json = JObject.Parse(good.ToString());

            Assert.Null(json["characters"]);
            Assert.Equal("Zhongli", (string)json["weapons"][0]["location"]);
            Assert.Equal("Zhongli", (string)json["artifacts"][0]["location"]);
        }

        private static GOOD Export(
            IEnumerable<Character> characters,
            IEnumerable<Weapon> weapons = null,
            IEnumerable<Artifact> artifacts = null) =>
            new GOOD(
                characters,
                weapons ?? Array.Empty<Weapon>(),
                artifacts ?? Array.Empty<Artifact>(),
                Array.Empty<Material>(),
                equipWeapons: true,
                equipArtifacts: true);

        private static Character CompleteCharacter(string name, string element)
        {
            Character character = CharacterWithAttributes(name, element);
            character.Constellation = 0;
            character.MarkConstellationScanSucceeded();
            bool talentsSucceeded = CharacterScraper.TrySetScannedTalents(
                character,
                new Dictionary<string, int>
                {
                    ["auto"] = 6,
                    ["skill"] = 9,
                    ["burst"] = 10,
                });
            Assert.True(talentsSucceeded);
            return character;
        }

        private static Character CharacterWithAttributes(string name, string element) =>
            new Character(CreateSnapshot())
            {
                NameGOOD = name,
                Element = element,
                Level = 90,
                Ascended = false,
            };

        private static GameDataSnapshot CreateSnapshot() =>
            new GameDataSnapshot(
                new Dictionary<string, JObject>
                {
                    ["jean"] = CharacterData("Jean", "anemo"),
                    ["zhongli"] = CharacterData("Zhongli", "geo"),
                    ["traveler"] = CharacterData("Traveler", "cryo"),
                },
                new Dictionary<string, JObject>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>
                {
                    ["anemo"] = "Anemo",
                    ["geo"] = "Geo",
                    ["cryo"] = "Cryo",
                },
                Array.Empty<string>(),
                Array.Empty<string>());

        private static JObject CharacterData(string goodName, string element) => new JObject
        {
            ["GOOD"] = goodName,
            ["Element"] = new JArray(element),
            ["WeaponType"] = WeaponType.Sword.ToString(),
        };
    }
}

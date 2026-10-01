using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace InventoryKamera.Tests
{
    public class CharacterPostProcessingTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void TartagliaInPartialPartyAdjustsOnlyCollectedMembers(int characterCount)
        {
            List<Character> characters = Party(characterCount, tartagliaIndex: 0);

            Exception exception = Record.Exception(() => CharacterScraper.ApplyTartagliaFix(characters));

            Assert.Null(exception);
            Assert.All(characters, character => Assert.Equal(5, character.Talents["auto"]));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void TartagliaInFirstFourPreservesFullPartyBehavior(int tartagliaIndex)
        {
            List<Character> characters = Party(4, tartagliaIndex);

            CharacterScraper.ApplyTartagliaFix(characters);

            Assert.All(characters, character => Assert.Equal(5, character.Talents["auto"]));
        }

        [Fact]
        public void TartagliaInFirstFourDoesNotAdjustLaterRosterEntries()
        {
            List<Character> characters = Party(6, tartagliaIndex: 2);

            CharacterScraper.ApplyTartagliaFix(characters);

            Assert.All(characters.Take(4), character => Assert.Equal(5, character.Talents["auto"]));
            Assert.All(characters.Skip(4), character => Assert.Equal(6, character.Talents["auto"]));
        }

        [Fact]
        public void TartagliaOutsideFirstFourPreservesSelfOnlyBehavior()
        {
            List<Character> characters = Party(5, tartagliaIndex: 4);

            CharacterScraper.ApplyTartagliaFix(characters);

            Assert.All(characters.Take(4), character => Assert.Equal(6, character.Talents["auto"]));
            Assert.Equal(5, characters[4].Talents["auto"]);
        }

        [Fact]
        public void TartagliaAbsentDoesNotAdjustParty()
        {
            List<Character> characters = Party(4, tartagliaIndex: null);

            CharacterScraper.ApplyTartagliaFix(characters);

            Assert.All(characters, character => Assert.Equal(6, character.Talents["auto"]));
        }

        [Fact]
        public void TartagliaDoesNotAdjustFailedTalentPhase()
        {
            List<Character> characters = Party(4, tartagliaIndex: 0);
            characters[2].MarkTalentScanFailed();

            CharacterScraper.ApplyTartagliaFix(characters);

            Assert.Equal(5, characters[0].Talents["auto"]);
            Assert.Equal(5, characters[1].Talents["auto"]);
            Assert.Equal(6, characters[2].Talents["auto"]);
            Assert.Equal(5, characters[3].Talents["auto"]);
        }

        [Fact]
        public void CancellationStylePartialRosterWithoutTalentScansDoesNotThrow()
        {
            var characters = new List<Character>
            {
                UnscannedCharacter("Tartaglia", "Hydro"),
                UnscannedCharacter("Jean", "Anemo"),
                UnscannedCharacter("Zhongli", "Geo"),
            };

            Exception exception = Record.Exception(() => CharacterScraper.ApplyTartagliaFix(characters));

            Assert.Null(exception);
            Assert.All(characters, character => Assert.Equal(0, character.Talents["auto"]));
        }

        [Fact]
        public void SkirkPartialRosterReturnsWithoutAdjustment()
        {
            var characters = new List<Character>
            {
                PartyCharacter("Skirk", "Cryo"),
                PartyCharacter("Tartaglia", "Hydro"),
                PartyCharacter("Qiqi", "Cryo"),
            };

            Exception exception = Record.Exception(() => CharacterScraper.ApplySkirkFix(characters));

            Assert.Null(exception);
            Assert.All(characters, character => Assert.Equal(6, character.Talents["skill"]));
        }

        [Fact]
        public void SkirkAdjustsEligibleHydroCryoParty()
        {
            List<Character> characters = SkirkParty("Cryo");

            CharacterScraper.ApplySkirkFix(characters);

            Assert.All(characters, character => Assert.Equal(5, character.Talents["skill"]));
        }

        [Fact]
        public void SkirkDoesNotAdjustPartyWithIneligibleElement()
        {
            List<Character> characters = SkirkParty("Pyro");

            CharacterScraper.ApplySkirkFix(characters);

            Assert.All(characters, character => Assert.Equal(6, character.Talents["skill"]));
        }

        [Fact]
        public void SkirkDoesNotAdjustFailedTalentPhase()
        {
            List<Character> characters = SkirkParty("Cryo");
            characters[2].MarkTalentScanFailed();

            CharacterScraper.ApplySkirkFix(characters);

            Assert.Equal(5, characters[0].Talents["skill"]);
            Assert.Equal(5, characters[1].Talents["skill"]);
            Assert.Equal(6, characters[2].Talents["skill"]);
            Assert.Equal(5, characters[3].Talents["skill"]);
        }

        [Fact]
        public void SkirkOutsideFirstFourDoesNotAdjustParty()
        {
            List<Character> characters = SkirkParty("Cryo");
            characters[0].NameGOOD = "Qiqi";
            characters.Add(PartyCharacter("Skirk", "Cryo"));

            CharacterScraper.ApplySkirkFix(characters);

            Assert.All(characters, character => Assert.Equal(6, character.Talents["skill"]));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        public void MissingMetadataIsNotRequiredBelowConstellationThree(int constellation)
        {
            GameDataSnapshot snapshot = Snapshot("Albedo", "Geo", constellationOrder: null);
            Character character = CompletedCharacter(snapshot, "Albedo", "Geo", constellation);

            bool succeeded = CharacterScraper.TryApplyConstellationTalentScaling(
                character,
                snapshot,
                out string failureReason);

            Assert.True(succeeded);
            Assert.Null(failureReason);
            Assert.Equal(CharacterScanPhaseStatus.Succeeded, character.TalentScanStatus);
            Assert.Equal(8, character.Talents["skill"]);
            Assert.Equal(10, character.Talents["burst"]);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(5)]
        [InlineData(6)]
        public void MissingMetadataFailsTalentFinalizationAtConstellationThreeOrHigher(int constellation)
        {
            GameDataSnapshot snapshot = Snapshot("Albedo", "Geo", constellationOrder: null);
            Character character = CompletedCharacter(snapshot, "Albedo", "Geo", constellation);

            bool succeeded = CharacterScraper.TryApplyConstellationTalentScaling(
                character,
                snapshot,
                out string failureReason);

            Assert.False(succeeded);
            Assert.Contains("ConstellationOrder", failureReason);
            Assert.Equal(CharacterScanPhaseStatus.Failed, character.TalentScanStatus);
            Assert.Equal(8, character.Talents["skill"]);
            Assert.Equal(10, character.Talents["burst"]);
        }

        [Fact]
        public void NonArrayConstellationOrderFailsSafely()
        {
            GameDataSnapshot snapshot = Snapshot("Albedo", "Geo", new JObject { ["skill"] = "burst" });
            Character character = CompletedCharacter(snapshot, "Albedo", "Geo", constellation: 3);

            bool succeeded = CharacterScraper.TryApplyConstellationTalentScaling(
                character,
                snapshot,
                out _);

            Assert.False(succeeded);
            Assert.Equal(CharacterScanPhaseStatus.Failed, character.TalentScanStatus);
        }

        [Fact]
        public void ShortConstellationOrderFailsSafely()
        {
            GameDataSnapshot snapshot = Snapshot("Albedo", "Geo", new JArray("skill"));
            Character character = CompletedCharacter(snapshot, "Albedo", "Geo", constellation: 3);

            bool succeeded = CharacterScraper.TryApplyConstellationTalentScaling(
                character,
                snapshot,
                out _);

            Assert.False(succeeded);
            Assert.Equal(CharacterScanPhaseStatus.Failed, character.TalentScanStatus);
        }

        [Fact]
        public void UnsupportedTalentKeyFailsSafely()
        {
            GameDataSnapshot snapshot = Snapshot("Albedo", "Geo", new JArray("skill", "unknown"));
            Character character = CompletedCharacter(snapshot, "Albedo", "Geo", constellation: 5);

            bool succeeded = CharacterScraper.TryApplyConstellationTalentScaling(
                character,
                snapshot,
                out _);

            Assert.False(succeeded);
            Assert.Equal(CharacterScanPhaseStatus.Failed, character.TalentScanStatus);
        }

        [Theory]
        [InlineData(3, 5, 10)]
        [InlineData(4, 5, 10)]
        [InlineData(5, 5, 7)]
        [InlineData(6, 5, 7)]
        public void ValidMetadataPreservesExistingAdjustment(
            int constellation,
            int expectedSkill,
            int expectedBurst)
        {
            GameDataSnapshot snapshot = Snapshot("Albedo", "Geo", new JArray("skill", "burst"));
            Character character = CompletedCharacter(snapshot, "Albedo", "Geo", constellation);

            bool succeeded = CharacterScraper.TryApplyConstellationTalentScaling(
                character,
                snapshot,
                out string failureReason);

            Assert.True(succeeded);
            Assert.Null(failureReason);
            Assert.Equal(CharacterScanPhaseStatus.Succeeded, character.TalentScanStatus);
            Assert.Equal(expectedSkill, character.Talents["skill"]);
            Assert.Equal(expectedBurst, character.Talents["burst"]);
        }

        [Fact]
        public void TravelerUsesElementSpecificConstellationOrder()
        {
            var orders = new JObject
            {
                ["anemo"] = new JArray("burst", "skill"),
            };
            GameDataSnapshot snapshot = Snapshot("Traveler", "Anemo", orders);
            Character traveler = CompletedCharacter(snapshot, "Traveler", "Anemo", constellation: 5);

            bool succeeded = CharacterScraper.TryApplyConstellationTalentScaling(
                traveler,
                snapshot,
                out string failureReason);

            Assert.True(succeeded);
            Assert.Null(failureReason);
            Assert.Equal(5, traveler.Talents["skill"]);
            Assert.Equal(7, traveler.Talents["burst"]);
            Assert.Equal("TravelerAnemo", traveler.NameGOOD);
        }

        [Fact]
        public void TravelerMissingElementOrderFailsSafely()
        {
            var orders = new JObject
            {
                ["anemo"] = new JArray("burst", "skill"),
            };
            GameDataSnapshot snapshot = Snapshot("Traveler", "Geo", orders);
            Character traveler = CompletedCharacter(snapshot, "Traveler", "Geo", constellation: 3);

            bool succeeded = CharacterScraper.TryApplyConstellationTalentScaling(
                traveler,
                snapshot,
                out string failureReason);

            Assert.False(succeeded);
            Assert.Contains("ConstellationOrder", failureReason);
            Assert.Equal(CharacterScanPhaseStatus.Failed, traveler.TalentScanStatus);
        }

        [Fact]
        public void PyroTravelerInferenceDoesNotRequireConstellationOrder()
        {
            GameDataSnapshot snapshot = Snapshot("Traveler", "Pyro", constellationOrder: null);
            Character traveler = CompletedCharacter(
                snapshot,
                "Traveler",
                "Pyro",
                constellation: 6,
                skill: 11,
                burst: 11);

            bool succeeded = CharacterScraper.TryApplyConstellationTalentScaling(
                traveler,
                snapshot,
                out string failureReason);

            Assert.True(succeeded);
            Assert.Null(failureReason);
            Assert.Equal(CharacterScanPhaseStatus.Succeeded, traveler.TalentScanStatus);
            Assert.Equal(5, traveler.Constellation);
            Assert.Equal(8, traveler.Talents["skill"]);
            Assert.Equal(8, traveler.Talents["burst"]);
        }

        [Fact]
        public void MetadataFailureAfterSuccessfulOcrOmitsCharacterFromGood()
        {
            GameDataSnapshot snapshot = Snapshot("Albedo", "Geo", constellationOrder: null);
            Character character = CompletedCharacter(
                snapshot,
                "Albedo",
                "Geo",
                constellation: 3,
                skill: 11,
                burst: 10);

            bool succeeded = CharacterScraper.TryApplyConstellationTalentScaling(
                character,
                snapshot,
                out _);
            var good = new GOOD(
                new[] { character },
                Array.Empty<Weapon>(),
                Array.Empty<Artifact>(),
                Array.Empty<Material>(),
                equipWeapons: true,
                equipArtifacts: true);

            Assert.False(succeeded);
            Assert.Equal(CharacterScanPhaseStatus.Failed, character.TalentScanStatus);
            Assert.Equal(11, character.Talents["skill"]);
            Assert.Null(good.Characters);
            Assert.Null(JObject.Parse(good.ToString())["characters"]);
        }

        private static List<Character> Party(int count, int? tartagliaIndex)
        {
            var characters = new List<Character>();
            for (int index = 0; index < count; index++)
            {
                string name = index == tartagliaIndex ? "Tartaglia" : $"Character{index}";
                characters.Add(PartyCharacter(name, index % 2 == 0 ? "Hydro" : "Cryo"));
            }
            return characters;
        }

        private static List<Character> SkirkParty(string lastElement) =>
            new List<Character>
            {
                PartyCharacter("Skirk", "Cryo"),
                PartyCharacter("Tartaglia", "Hydro"),
                PartyCharacter("Qiqi", "Cryo"),
                PartyCharacter("Fourth", lastElement),
            };

        private static Character PartyCharacter(string name, string element)
        {
            var character = new Character
            {
                NameGOOD = name,
                Element = element,
                Level = 90,
            };
            Assert.True(CharacterScraper.TrySetScannedTalents(
                character,
                new Dictionary<string, int>
                {
                    ["auto"] = 6,
                    ["skill"] = 6,
                    ["burst"] = 6,
                }));
            return character;
        }

        private static Character UnscannedCharacter(string name, string element) =>
            new Character
            {
                NameGOOD = name,
                Element = element,
                Level = 90,
            };

        private static Character CompletedCharacter(
            GameDataSnapshot snapshot,
            string name,
            string element,
            int constellation,
            int skill = 8,
            int burst = 10)
        {
            var character = new Character(snapshot)
            {
                NameGOOD = name,
                Element = element,
                Level = 90,
                Constellation = constellation,
            };
            character.MarkConstellationScanSucceeded();
            Assert.True(CharacterScraper.TrySetScannedTalents(
                character,
                new Dictionary<string, int>
                {
                    ["auto"] = 6,
                    ["skill"] = skill,
                    ["burst"] = burst,
                }));
            return character;
        }

        private static GameDataSnapshot Snapshot(
            string goodName,
            string element,
            JToken constellationOrder)
        {
            string key = GameDataSnapshot.NormalizeKey(goodName);
            string elementKey = GameDataSnapshot.NormalizeKey(element);
            var characterData = new JObject
            {
                ["GOOD"] = goodName,
                ["Element"] = new JArray(elementKey),
                ["WeaponType"] = WeaponType.Sword.ToString(),
                ["Rarity"] = 5,
            };
            if (constellationOrder != null)
                characterData["ConstellationOrder"] = constellationOrder;

            return new GameDataSnapshot(
                new Dictionary<string, JObject> { [key] = characterData },
                new Dictionary<string, JObject>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string> { [elementKey] = element },
                Array.Empty<string>(),
                Array.Empty<string>());
        }
    }
}

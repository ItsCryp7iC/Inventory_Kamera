using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using InventoryKamera;
using Xunit;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// Characterization tests for the Artifact model. These pin substat integrity,
    /// validation ranges, substat formatting, and GOOD serialization shape. Snapshot-dependent
    /// set/slot/stat/character validation is exercised separately with synthetic lookup data.
    /// </summary>
    public class ArtifactTests
    {
        private static Artifact.SubStat Sub(string stat, decimal value) =>
            new Artifact.SubStat { stat = stat, value = value };

        [Fact]
        public void Constructor_PreservesMalformedSubstatsForValidation()
        {
            var subs = new List<Artifact.SubStat> { Sub("atk_", 5.8m), Sub("critRate_", 0m) };
            var unactivated = new List<Artifact.SubStat> { Sub("def", 0m) };

            var artifact = ArtifactWith(subs, unactivated);

            Assert.Equal(2, artifact.SubStats.Count);
            Assert.Single(artifact.unactivatedSubstats);
            Assert.False(artifact.IsValid());
            Assert.Contains("invalid_active_substat", artifact.Validate().FailureCodes);
            Assert.Contains("invalid_unactivated_substat", artifact.Validate().FailureCodes);
        }

        [Fact]
        public void ValidUnactivatedSubstat_IsAccepted()
        {
            Artifact artifact = ArtifactWith(
                new[] { Sub("critRate_", 3.9m) },
                new[] { Sub("atk_", 5.8m) });

            Assert.True(artifact.IsValid(), artifact.Validate().Summary);
        }

        [Theory]
        [InlineData("unknown", 5.8)]
        [InlineData("atk_", 0)]
        [InlineData("", 5.8)]
        public void InvalidUnactivatedSubstat_IsRejected(string stat, decimal value)
        {
            Artifact artifact = ArtifactWith(
                new[] { Sub("critRate_", 3.9m) },
                new[] { Sub(stat, value) });

            Assert.False(artifact.IsValid());
            Assert.Contains("invalid_unactivated_substat", artifact.Validate().FailureCodes);
        }

        [Fact]
        public void MissingUnactivatedCollection_IsRejectedAsMalformed()
        {
            Artifact artifact = ArtifactWith(
                new[] { Sub("critRate_", 3.9m) },
                null);

            Assert.False(artifact.IsValid());
            Assert.Contains("invalid_unactivated_substat", artifact.Validate().FailureCodes);
        }

        [Fact]
        public void DuplicateActiveSubstat_IsRejected()
        {
            Artifact artifact = ArtifactWith(
                new[] { Sub("critRate_", 3.9m), Sub("critRate_", 7.8m) },
                Array.Empty<Artifact.SubStat>());

            Assert.False(artifact.IsValid());
            Assert.Contains("duplicate_substat", artifact.Validate().FailureCodes);
        }

        [Fact]
        public void DuplicateUnactivatedSubstat_IsRejected()
        {
            Artifact artifact = ArtifactWith(
                Array.Empty<Artifact.SubStat>(),
                new[] { Sub("atk_", 5.8m), Sub("atk_", 11.6m) });

            Assert.False(artifact.IsValid());
            Assert.Contains("duplicate_substat", artifact.Validate().FailureCodes);
        }

        [Fact]
        public void SameActiveAndUnactivatedStat_IsRejected()
        {
            Artifact artifact = ArtifactWith(
                new[] { Sub("atk_", 5.8m) },
                new[] { Sub("atk_", 11.6m) });

            Assert.False(artifact.IsValid());
            Assert.Contains("duplicate_substat", artifact.Validate().FailureCodes);
        }

        [Fact]
        public void MainStatCannotAlsoBeSubstat()
        {
            Artifact artifact = ArtifactWith(
                new[] { Sub("hp", 239m) },
                Array.Empty<Artifact.SubStat>());

            Assert.False(artifact.IsValid());
            Assert.Contains("main_stat_as_substat", artifact.Validate().FailureCodes);
        }

        [Fact]
        public void DistinctActiveAndUnactivatedCollections_AreAccepted()
        {
            Artifact artifact = ArtifactWith(
                new[] { Sub("critRate_", 3.9m), Sub("def", 19m) },
                new[] { Sub("atk_", 5.8m) });

            Assert.True(artifact.IsValid(), artifact.Validate().Summary);
        }

        [Theory]
        [InlineData("UnknownSet", "flower", 5, 20, "hp", "invalid_set_key")]
        [InlineData("GladiatorsFinale", "unknown", 5, 20, "hp", "invalid_slot")]
        [InlineData("GladiatorsFinale", "flower", 6, 20, "hp", "invalid_rarity")]
        [InlineData("GladiatorsFinale", "flower", 5, 21, "hp", "invalid_level")]
        [InlineData("GladiatorsFinale", "flower", 5, 20, "unknown", "invalid_main_stat")]
        public void InvalidCoreField_IsRejected(
            string setName,
            string slot,
            int rarity,
            int level,
            string mainStat,
            string expectedReason)
        {
            var artifact = new Artifact(
                setName,
                rarity,
                level,
                slot,
                mainStat,
                new List<Artifact.SubStat> { Sub("critRate_", 3.9m) },
                new List<Artifact.SubStat>(),
                gameData: Snapshot());

            Assert.False(artifact.IsValid());
            Assert.Contains(expectedReason, artifact.Validate().FailureCodes);
        }

        [Theory]
        [InlineData(-1, false)]
        [InlineData(0, true)]
        [InlineData(20, true)]
        [InlineData(21, false)]
        public void HasValidLevel_AcceptsZeroThroughTwenty(int level, bool expected)
        {
            var artifact = MakeArtifact(level: level, rarity: 5);
            Assert.Equal(expected, artifact.HasValidLevel());
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(5, true)]
        [InlineData(6, false)]
        public void HasValidRarity_AcceptsOneThroughFive(int rarity, bool expected)
        {
            var artifact = MakeArtifact(level: 20, rarity: rarity);
            Assert.Equal(expected, artifact.HasValidRarity());
        }

        [Fact]
        public void SubStat_ToString_AppendsPercentForUnderscoreStats()
        {
            Assert.Equal("critRate_ + 5.8%", Sub("critRate_", 5.8m).ToString());
            Assert.Equal("atk + 16", Sub("atk", 16m).ToString());
            Assert.Equal("NULL", default(Artifact.SubStat).ToString());
        }

        [Fact]
        public void EmptyConstructor_HasSentinelDefaults()
        {
            var artifact = new Artifact();

            Assert.Equal(-1, artifact.Rarity);
            Assert.Equal(-1, artifact.Level);
            Assert.Null(artifact.SetName);
            Assert.Empty(artifact.SubStats);
        }

        [Fact]
        public void Serialization_UsesGoodKeys()
        {
            var artifact = MakeArtifact(level: 20, rarity: 5);

            var json = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(artifact));

            Assert.Equal("GladiatorsFinale", (string)json["setKey"]);
            Assert.Equal("flower", (string)json["slotKey"]);
            Assert.Equal("hp", (string)json["mainStatKey"]);
            Assert.Equal(5, (int)json["rarity"]);
            Assert.Equal(20, (int)json["level"]);
            Assert.True(json.ContainsKey("substats"));
        }

        private static Artifact MakeArtifact(int level, int rarity) =>
            new Artifact(
                _setName: "GladiatorsFinale",
                _rarity: rarity,
                _level: level,
                _gearSlot: "flower",
                _mainStat: "hp",
                _subStats: new List<Artifact.SubStat> { Sub("atk_", 5.8m) },
                _unactivatedSubStats: new List<Artifact.SubStat>());

        private static Artifact ArtifactWith(
            IEnumerable<Artifact.SubStat> active,
            IEnumerable<Artifact.SubStat> unactivated) =>
            new Artifact(
                "GladiatorsFinale",
                5,
                20,
                "flower",
                "hp",
                active?.ToList(),
                unactivated?.ToList(),
                gameData: Snapshot());

        internal static GameDataSnapshot Snapshot() =>
            new GameDataSnapshot(
                new Dictionary<string, JObject>
                {
                    ["zhongli"] = new JObject { ["GOOD"] = "Zhongli" },
                    ["jean"] = new JObject { ["GOOD"] = "Jean" },
                },
                new Dictionary<string, JObject>
                {
                    ["gladiatorsfinale"] = new JObject { ["GOOD"] = "GladiatorsFinale" },
                    ["otherset"] = new JObject { ["GOOD"] = "OtherSet" },
                },
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>
                {
                    ["hp"] = "hp",
                    ["atkpercent"] = "atk_",
                    ["def"] = "def",
                    ["critrate"] = "critRate_",
                    ["critdmg"] = "critDMG_",
                },
                new Dictionary<string, string>(),
                new[] { "flower", "plume", "sands", "goblet", "circlet" },
                Array.Empty<string>());
    }
}

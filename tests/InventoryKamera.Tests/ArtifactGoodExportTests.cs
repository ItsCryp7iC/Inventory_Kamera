using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Xunit;

namespace InventoryKamera.Tests
{
    public class ArtifactGoodExportTests
    {
        [Fact]
        public void InvalidArtifactIsOmittedWhileLaterValidArtifactRemains()
        {
            Artifact invalid = ArtifactAt(1, "UnknownSet");
            Artifact valid = ArtifactAt(3, "GladiatorsFinale");

            GOOD good = Export(new[] { invalid, valid });
            JObject json = JObject.Parse(good.ToString());

            Assert.Equal(3, Assert.Single(good.Artifacts).Id);
            Assert.Equal(3, (int)json["artifacts"][0]["id"]);
        }

        [Fact]
        public void ArtifactWithoutExplicitGameDataCannotCrossGoodBoundary()
        {
            var unbound = new Artifact(
                "GladiatorsFinale",
                5,
                20,
                "flower",
                "hp",
                new List<Artifact.SubStat>(),
                new List<Artifact.SubStat>());

            GOOD good = Export(new[] { unbound });

            Assert.Null(good.Artifacts);
            Assert.Null(JObject.Parse(good.ToString())["artifacts"]);
        }

        private static Artifact ArtifactAt(int id, string setName) =>
            new Artifact(
                setName,
                5,
                20,
                "flower",
                "hp",
                new List<Artifact.SubStat>
                {
                    new Artifact.SubStat { stat = "critRate_", value = 3.9m },
                },
                new List<Artifact.SubStat>(),
                _id: id,
                gameData: ArtifactTests.Snapshot());

        private static GOOD Export(IEnumerable<Artifact> artifacts) =>
            new GOOD(
                Array.Empty<Character>(),
                Array.Empty<Weapon>(),
                artifacts,
                Array.Empty<Material>(),
                equipWeapons: true,
                equipArtifacts: true);
    }
}

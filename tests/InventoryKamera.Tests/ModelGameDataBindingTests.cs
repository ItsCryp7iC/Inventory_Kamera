using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Xunit;

namespace InventoryKamera.Tests
{
    [Collection(GameDataProviderCollection.Name)]
    public class ModelGameDataBindingTests
    {
        [Fact]
        public void ExplicitSnapshotsProvideCharacterWeaponAndArtifactLookups()
        {
            GameDataSnapshot snapshot = CreateSnapshotA();
            Character character = CreateCharacter(snapshot, "Diluc", "Pyro");
            Weapon weapon = CreateWeapon(snapshot, "OldBlade", "Diluc");
            Artifact artifact = CreateArtifact(
                snapshot,
                "GladiatorsFinale",
                "flower",
                "hp",
                "critRate_",
                "Diluc");

            Assert.True(character.HasGameData);
            Assert.Equal(WeaponType.Sword, character.WeaponType);
            Assert.True(character.IsValid());
            Assert.True(weapon.HasGameData);
            Assert.True(weapon.IsValid());
            Assert.True(artifact.HasGameData);
            Assert.True(artifact.IsValid());
        }

        [Fact]
        public void MissingLookupDependencyThrowsInsteadOfReadingCompatibilitySnapshot()
        {
            GameDataSnapshot original = GenshinProcesor.CompatibilitySnapshot;
            try
            {
                // Every value below is valid in this installed compatibility snapshot. If any model
                // still falls back globally, these calls would succeed instead of throwing.
                GenshinProcesor.InstallCompatibilitySnapshot(CreateSnapshotA());
                Character character = CreateCharacter(null, "Diluc", "Pyro");
                Weapon weapon = CreateWeapon(null, "OldBlade", "Diluc");
                Artifact artifact = CreateArtifact(
                    null,
                    "GladiatorsFinale",
                    "flower",
                    "hp",
                    "critRate_",
                    "Diluc");

                Assert.False(character.HasGameData);
                AssertMissingLookup(() => { _ = character.WeaponType; });
                AssertMissingLookup(() => character.HasValidName());
                AssertMissingLookup(() => character.HasValidElement());

                Assert.False(weapon.HasGameData);
                AssertMissingLookup(() => weapon.HasValidWeaponName());
                AssertMissingLookup(() => weapon.HasValidEquippedCharacter());

                Assert.False(artifact.HasGameData);
                AssertMissingLookup(() => artifact.HasValidSlot());
                AssertMissingLookup(() => artifact.HasValidSetName());
                AssertMissingLookup(() => artifact.HasValidMainStat());
                AssertMissingLookup(() => artifact.HasValidSubStats());
                AssertMissingLookup(() => artifact.HasValidEquippedCharacter());
            }
            finally
            {
                GenshinProcesor.InstallCompatibilitySnapshot(original);
            }
        }

        [Fact]
        public void ModelsBoundToSnapshotARemainIsolatedAfterApplicationMovesToSnapshotB()
        {
            GameDataSnapshot original = GenshinProcesor.CompatibilitySnapshot;
            try
            {
                GameDataSnapshot snapshotA = CreateSnapshotA();
                GameDataSnapshot snapshotB = CreateSnapshotB();
                Character characterA = CreateCharacter(snapshotA, "Diluc", "Pyro");
                Weapon weaponA = CreateWeapon(snapshotA, "OldBlade", "Diluc");
                Artifact artifactA = CreateArtifact(
                    snapshotA,
                    "GladiatorsFinale",
                    "flower",
                    "hp",
                    "critRate_",
                    "Diluc");

                var provider = new GameDataSnapshotProvider(snapshotA);
                provider.Replace(snapshotB);

                Character characterB = CreateCharacter(provider.Current, "Jean", "Anemo");
                Weapon weaponB = CreateWeapon(provider.Current, "NewBow", "Jean");
                Artifact artifactB = CreateArtifact(
                    provider.Current,
                    "ViridescentVenerer",
                    "plume",
                    "atk",
                    "atk_",
                    "Jean");

                Assert.Equal(WeaponType.Sword, characterA.WeaponType);
                Assert.True(characterA.IsValid());
                Assert.True(weaponA.IsValid());
                Assert.True(artifactA.IsValid());

                Assert.Equal(WeaponType.Bow, characterB.WeaponType);
                Assert.True(characterB.IsValid());
                Assert.True(weaponB.IsValid());
                Assert.True(artifactB.IsValid());
            }
            finally
            {
                GenshinProcesor.InstallCompatibilitySnapshot(original);
            }
        }

        [Fact]
        public void DeserializedModelsRemainUnboundUntilExplicitAttachment()
        {
            GameDataSnapshot snapshot = CreateSnapshotA();
            Character originalCharacter = CreateCharacter(snapshot, "Diluc", "Pyro");
            Weapon originalWeapon = CreateWeapon(snapshot, "OldBlade", "Diluc");
            Artifact originalArtifact = CreateArtifact(
                snapshot,
                "GladiatorsFinale",
                "flower",
                "hp",
                "critRate_",
                "Diluc");

            Character character = JsonConvert.DeserializeObject<Character>(
                JsonConvert.SerializeObject(originalCharacter));
            Weapon weapon = JsonConvert.DeserializeObject<Weapon>(
                JsonConvert.SerializeObject(originalWeapon));
            Artifact artifact = JsonConvert.DeserializeObject<Artifact>(
                JsonConvert.SerializeObject(originalArtifact));

            Assert.False(character.HasGameData);
            Assert.False(weapon.HasGameData);
            Assert.False(artifact.HasGameData);
            AssertMissingLookup(() => { _ = character.WeaponType; });
            AssertMissingLookup(() => weapon.HasValidWeaponName());
            AssertMissingLookup(() => artifact.HasValidSetName());

            character.AttachGameData(snapshot);
            weapon.AttachGameData(snapshot);
            artifact.AttachGameData(snapshot);

            Assert.Equal(WeaponType.Sword, character.WeaponType);
            Assert.True(weapon.HasValidWeaponName());
            Assert.True(weapon.HasValidEquippedCharacter());
            Assert.True(artifact.IsValid());

            Assert.DoesNotContain("HasGameData", JsonConvert.SerializeObject(character));
            Assert.DoesNotContain("HasGameData", JsonConvert.SerializeObject(weapon));
            Assert.DoesNotContain("HasGameData", JsonConvert.SerializeObject(artifact));
        }

        [Fact]
        public void AttachedModelCannotBeSilentlyReboundToDifferentSnapshot()
        {
            GameDataSnapshot snapshotA = CreateSnapshotA();
            GameDataSnapshot snapshotB = CreateSnapshotB();
            Character character = CreateCharacter(snapshotA, "Diluc", "Pyro");
            Weapon weapon = CreateWeapon(snapshotA, "OldBlade", "Diluc");
            Artifact artifact = CreateArtifact(
                snapshotA,
                "GladiatorsFinale",
                "flower",
                "hp",
                "critRate_",
                "Diluc");

            Assert.Throws<InvalidOperationException>(() => character.AttachGameData(snapshotB));
            Assert.Throws<InvalidOperationException>(() => weapon.AttachGameData(snapshotB));
            Assert.Throws<InvalidOperationException>(() => artifact.AttachGameData(snapshotB));
            Assert.Equal(WeaponType.Sword, character.WeaponType);
            Assert.True(weapon.IsValid());
            Assert.True(artifact.IsValid());
        }

        private static void AssertMissingLookup(Action action)
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(action);
            Assert.Contains("AttachGameData", error.Message);
        }

        private static Character CreateCharacter(
            GameDataSnapshot snapshot,
            string name,
            string element)
        {
            Character character = snapshot == null ? new Character() : new Character(snapshot);
            character.NameGOOD = name;
            character.Element = element;
            character.Level = 90;
            character.Constellation = 0;
            character.Talents["auto"] = 1;
            character.Talents["skill"] = 1;
            character.Talents["burst"] = 1;
            return character;
        }

        private static Weapon CreateWeapon(
            GameDataSnapshot snapshot,
            string name,
            string equippedCharacter) =>
            new Weapon(
                name,
                90,
                true,
                5,
                _equippedCharacter: equippedCharacter,
                _rarity: 5,
                gameData: snapshot);

        private static Artifact CreateArtifact(
            GameDataSnapshot snapshot,
            string setName,
            string slot,
            string mainStat,
            string subStat,
            string equippedCharacter) =>
            new Artifact(
                setName,
                5,
                20,
                slot,
                mainStat,
                new List<Artifact.SubStat>
                {
                    new Artifact.SubStat { stat = subStat, value = 3.9m },
                },
                new List<Artifact.SubStat>(),
                equippedCharacter,
                gameData: snapshot);

        private static GameDataSnapshot CreateSnapshotA() => CreateSnapshot(
            characterKey: "diluc",
            characterName: "Diluc",
            elementKey: "pyro",
            elementName: "Pyro",
            weaponType: WeaponType.Sword,
            weaponKey: "oldblade",
            weaponName: "OldBlade",
            artifactKey: "gladiatorsfinale",
            artifactName: "GladiatorsFinale",
            slot: "flower",
            mainStatKey: "hp",
            mainStatName: "hp",
            subStatKey: "critrate",
            subStatName: "critRate_");

        private static GameDataSnapshot CreateSnapshotB() => CreateSnapshot(
            characterKey: "jean",
            characterName: "Jean",
            elementKey: "anemo",
            elementName: "Anemo",
            weaponType: WeaponType.Bow,
            weaponKey: "newbow",
            weaponName: "NewBow",
            artifactKey: "viridescentvenerer",
            artifactName: "ViridescentVenerer",
            slot: "plume",
            mainStatKey: "atk",
            mainStatName: "atk",
            subStatKey: "atkpercent",
            subStatName: "atk_");

        private static GameDataSnapshot CreateSnapshot(
            string characterKey,
            string characterName,
            string elementKey,
            string elementName,
            WeaponType weaponType,
            string weaponKey,
            string weaponName,
            string artifactKey,
            string artifactName,
            string slot,
            string mainStatKey,
            string mainStatName,
            string subStatKey,
            string subStatName)
        {
            var character = new JObject
            {
                ["GOOD"] = characterName,
                ["Element"] = new JArray(elementKey),
                ["Rarity"] = 5,
                ["WeaponType"] = (int)weaponType,
                ["ConstellationOrder"] = new JArray("skill", "burst"),
            };
            var artifact = new JObject { ["GOOD"] = artifactName };
            return new GameDataSnapshot(
                new Dictionary<string, JObject> { [characterKey] = character },
                new Dictionary<string, JObject> { [artifactKey] = artifact },
                new Dictionary<string, string> { [weaponKey] = weaponName },
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>
                {
                    [mainStatKey] = mainStatName,
                    [subStatKey] = subStatName,
                },
                new Dictionary<string, string> { [elementKey] = elementName },
                new[] { slot },
                Array.Empty<string>());
        }
    }
}

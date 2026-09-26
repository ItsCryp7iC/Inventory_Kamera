using InventoryKamera.ui;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Xunit;

namespace InventoryKamera.Tests
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class GameDataProviderCollection
    {
        public const string Name = "Game data provider compatibility state";
    }

    [Collection(GameDataProviderCollection.Name)]
    public class GameDataSnapshotProviderTests
    {
        [Fact]
        public void InitialSnapshotIsAvailable()
        {
            WithRestoredCompatibility(() =>
            {
                GameDataSnapshot first = CreateSnapshot("oldblade", "OldBlade", "diluc");

                var provider = new GameDataSnapshotProvider(first);

                Assert.Same(first, provider.Current);
            });
        }

        [Fact]
        public void LoaderInitializesAndReloadsCompleteSnapshots()
        {
            WithRestoredCompatibility(() =>
            {
                GameDataSnapshot first = CreateSnapshot("oldblade", "OldBlade", "diluc");
                GameDataSnapshot second = CreateSnapshot("newblade", "NewBlade", "jean");
                var snapshots = new Queue<GameDataSnapshot>(new[] { first, second });
                var provider = new GameDataSnapshotProvider(() => snapshots.Dequeue());

                bool reloaded = provider.TryReload(out Exception error);

                Assert.Null(error);
                Assert.True(reloaded);
                Assert.Same(second, provider.Current);
            });
        }

        [Fact]
        public void ReplaceMakesNewSnapshotCurrentWithoutMutatingPreviousSnapshot()
        {
            WithRestoredCompatibility(() =>
            {
                GameDataSnapshot first = CreateSnapshot("oldblade", "OldBlade", "diluc");
                GameDataSnapshot second = CreateSnapshot("newblade", "NewBlade", "jean");
                var provider = new GameDataSnapshotProvider(first);
                GameDataSnapshot captured = provider.Current;

                provider.Replace(second);

                Assert.Same(second, provider.Current);
                Assert.Same(first, captured);
                Assert.True(captured.Weapons.ContainsKey("oldblade"));
                Assert.False(captured.Weapons.ContainsKey("newblade"));
            });
        }

        [Fact]
        public void SequentialReplacementsInstallCompleteSnapshots()
        {
            WithRestoredCompatibility(() =>
            {
                GameDataSnapshot first = CreateSnapshot("first", "First", "diluc");
                GameDataSnapshot second = CreateSnapshot("second", "Second", "jean");
                GameDataSnapshot third = CreateSnapshot("third", "Third", "gaming");
                var provider = new GameDataSnapshotProvider(first);

                provider.Replace(second);
                provider.Replace(third);

                Assert.Same(third, provider.Current);
                Assert.True(provider.Current.Weapons.ContainsKey("third"));
                Assert.False(provider.Current.Characters.ContainsKey("jean"));
            });
        }

        [Fact]
        public void FailedReloadRetainsCurrentSnapshot()
        {
            WithRestoredCompatibility(() =>
            {
                GameDataSnapshot first = CreateSnapshot("oldblade", "OldBlade", "diluc");
                var provider = new GameDataSnapshotProvider(
                    first,
                    () => throw new InvalidOperationException("invalid updated files"));

                bool reloaded = provider.TryReload(out Exception error);

                Assert.False(reloaded);
                Assert.Equal("invalid updated files", error.Message);
                Assert.Same(first, provider.Current);
            });
        }

        [Fact]
        public void ExistingScanKeepsCapturedSnapshotAndNewScanReceivesReplacement()
        {
            WithRestoredCompatibility(() =>
            {
                GameDataSnapshot first = CreateSnapshot("oldblade", "OldBlade", "diluc");
                GameDataSnapshot second = CreateSnapshot("newblade", "NewBlade", "jean");
                var provider = new GameDataSnapshotProvider(first);
                var owner = new ScanRunOwner();

                Assert.True(owner.TryCreate(new ScanViewModel(), out ScanRun firstRun));
                GameScanner firstScanner = firstRun.InitializeScanner(provider.Current);
                provider.Replace(second);
                owner.Complete(firstRun);

                Assert.True(owner.TryCreate(new ScanViewModel(), out ScanRun secondRun));
                GameScanner secondScanner = secondRun.InitializeScanner(provider.Current);

                Assert.Same(first, firstScanner.GameData);
                Assert.Same(second, secondScanner.GameData);
                owner.Complete(secondRun);
            });
        }

        [Fact]
        public void SettingsNameLookupUsesSuppliedSnapshot()
        {
            GameDataSnapshot first = CreateSnapshot("oldblade", "OldBlade", "diluc");
            GameDataSnapshot second = CreateSnapshot("newblade", "NewBlade", "jean");

            Assert.True(SettingsForm.IsExistingCharacterName("Diluc", first));
            Assert.False(SettingsForm.IsExistingCharacterName("Diluc", second));
            Assert.True(SettingsForm.IsExistingCharacterName("Jean", second));
        }

        [Fact]
        public void CompatibilitySurfaceChangesAsOneSnapshotReference()
        {
            WithRestoredCompatibility(() =>
            {
                GameDataSnapshot first = CreateSnapshot("oldblade", "OldBlade", "diluc");
                GameDataSnapshot second = CreateSnapshot("newblade", "NewBlade", "jean");
                var provider = new GameDataSnapshotProvider(first);

                provider.Replace(second);
                GameDataSnapshot compatibility = GenshinProcesor.CompatibilitySnapshot;

                Assert.Same(second, compatibility);
                Assert.True(compatibility.Weapons.ContainsKey("newblade"));
                Assert.True(compatibility.Characters.ContainsKey("jean"));
                Assert.False(compatibility.Weapons.ContainsKey("oldblade"));
                Assert.False(compatibility.Characters.ContainsKey("diluc"));
            });
        }

        [Fact]
        public void LegacyCharacterFallbackUsesInstalledCompatibilitySnapshot()
        {
            WithRestoredCompatibility(() =>
            {
                GameDataSnapshot snapshot = CreateSnapshot("newblade", "NewBlade", "jean");
                var provider = new GameDataSnapshotProvider(snapshot);
                var character = new Character { NameGOOD = "Jean" };

                Assert.Same(snapshot, GenshinProcesor.CompatibilitySnapshot);
                Assert.Equal(WeaponType.Sword, character.WeaponType);
            });
        }

        private static void WithRestoredCompatibility(Action test)
        {
            GameDataSnapshot original = GenshinProcesor.CompatibilitySnapshot;
            try
            {
                test();
            }
            finally
            {
                GenshinProcesor.InstallCompatibilitySnapshot(original);
            }
        }

        private static GameDataSnapshot CreateSnapshot(
            string weaponKey,
            string weaponName,
            string characterKey)
        {
            var character = new JObject
            {
                ["GOOD"] = char.ToUpper(characterKey[0]) + characterKey.Substring(1),
                ["Element"] = new JArray("pyro"),
                ["Rarity"] = 5,
                ["WeaponType"] = (int)WeaponType.Sword,
                ["ConstellationOrder"] = new JArray("skill", "burst"),
            };
            return new GameDataSnapshot(
                new Dictionary<string, JObject> { [characterKey] = character },
                new Dictionary<string, JObject>(),
                new Dictionary<string, string> { [weaponKey] = weaponName },
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string> { ["hp"] = "hp" },
                new Dictionary<string, string> { ["pyro"] = "Pyro" },
                new[] { "flower" },
                new[] { "enhancementore" });
        }
    }
}

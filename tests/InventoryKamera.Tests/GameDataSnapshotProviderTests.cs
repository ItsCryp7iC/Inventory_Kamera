using InventoryKamera.ui;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Xunit;

namespace InventoryKamera.Tests
{
    public class GameDataSnapshotProviderTests
    {
        [Fact]
        public void InitialSnapshotIsAvailable()
        {
            GameDataSnapshot first = CreateSnapshot("oldblade", "OldBlade", "diluc");

            var provider = new GameDataSnapshotProvider(first);

            Assert.Same(first, provider.Current);
        }

        [Fact]
        public void LoaderInitializesAndReloadsCompleteSnapshots()
        {
            GameDataSnapshot first = CreateSnapshot("oldblade", "OldBlade", "diluc");
            GameDataSnapshot second = CreateSnapshot("newblade", "NewBlade", "jean");
            var snapshots = new Queue<GameDataSnapshot>(new[] { first, second });
            var provider = new GameDataSnapshotProvider(() => snapshots.Dequeue());

            bool reloaded = provider.TryReload(out Exception error);

            Assert.Null(error);
            Assert.True(reloaded);
            Assert.Same(second, provider.Current);
        }

        [Fact]
        public void ReplaceMakesNewSnapshotCurrentWithoutMutatingPreviousSnapshot()
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
        }

        [Fact]
        public void SequentialReplacementsPublishCompleteSnapshots()
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
        }

        [Fact]
        public void FailedReloadRetainsCurrentSnapshot()
        {
            GameDataSnapshot first = CreateSnapshot("oldblade", "OldBlade", "diluc");
            var provider = new GameDataSnapshotProvider(
                first,
                () => throw new InvalidOperationException("invalid updated files"));

            bool reloaded = provider.TryReload(out Exception error);

            Assert.False(reloaded);
            Assert.Equal("invalid updated files", error.Message);
            Assert.Same(first, provider.Current);
        }

        [Fact]
        public void ExistingScanKeepsCapturedSnapshotAndNewScanReceivesReplacement()
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
        public void UnboundCharacterRequiresExplicitProviderSnapshotAttachment()
        {
            GameDataSnapshot snapshot = CreateSnapshot("newblade", "NewBlade", "jean");
            var provider = new GameDataSnapshotProvider(snapshot);
            var character = new Character { NameGOOD = "Jean" };

            Assert.False(character.HasGameData);
            Assert.Throws<InvalidOperationException>(() => character.WeaponType);

            character.AttachGameData(provider.Current);
            Assert.Equal(WeaponType.Sword, character.WeaponType);
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

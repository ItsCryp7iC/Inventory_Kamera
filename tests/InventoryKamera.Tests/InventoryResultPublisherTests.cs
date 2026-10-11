using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace InventoryKamera.Tests
{
    public class InventoryResultPublisherTests
    {
        [Fact]
        public async Task TwoWorkersPublishingManyWeapons_CommitEveryResultInSourceOrder()
        {
            var publisher = new InventoryResultPublisher();
            using var secondWorkerMayRun = new ManualResetEventSlim(false);
            using var firstWorkerMayRun = new ManualResetEventSlim(false);
            using var secondWorkerFinished = new ManualResetEventSlim(false);

            Task firstWorker = Task.Run(() =>
            {
                firstWorkerMayRun.Wait();
                for (int sequence = 0; sequence < 40; sequence += 2)
                    publisher.StageWeapon(sequence, WeaponAt(sequence), isPublishable: true);
            });
            Task secondWorker = Task.Run(() =>
            {
                secondWorkerMayRun.Wait();
                for (int sequence = 39; sequence >= 1; sequence -= 2)
                    publisher.StageWeapon(sequence, WeaponAt(sequence), isPublishable: true);
                secondWorkerFinished.Set();
            });

            secondWorkerMayRun.Set();
            secondWorkerFinished.Wait();
            firstWorkerMayRun.Set();
            await Task.WhenAll(firstWorker, secondWorker);

            Inventory inventory = Commit(publisher);

            Assert.Equal(40, inventory.Weapons.Count);
            Assert.Equal(Enumerable.Range(0, 40), inventory.Weapons.Select(weapon => weapon.Id));
        }

        [Fact]
        public async Task ThreeWorkersPublishingManyArtifacts_CommitEveryResultInSourceOrder()
        {
            var publisher = new InventoryResultPublisher();
            using var release = new ManualResetEventSlim(false);
            using var ready = new CountdownEvent(3);

            Task[] workers = Enumerable.Range(0, 3)
                .Select(worker => Task.Run(() =>
                {
                    ready.Signal();
                    release.Wait();
                    for (int sequence = 59 - worker; sequence >= 0; sequence -= 3)
                        publisher.StageArtifact(sequence, ArtifactAt(sequence), isPublishable: true);
                }))
                .ToArray();

            ready.Wait();
            release.Set();
            await Task.WhenAll(workers);

            Inventory inventory = Commit(publisher);

            Assert.Equal(60, inventory.Artifacts.Count);
            Assert.Equal(Enumerable.Range(0, 60), inventory.Artifacts.Select(artifact => artifact.Id));
        }

        [Fact]
        public void ReverseCompletionAndRepeatedRuns_AlwaysProduceIdenticalOrdering()
        {
            int[] expected = Enumerable.Range(0, 12).ToArray();

            for (int run = 0; run < 5; run++)
            {
                var publisher = new InventoryResultPublisher();
                foreach (int sequence in expected.Reverse())
                    publisher.StageWeapon(sequence, WeaponAt(sequence), isPublishable: true);

                Inventory inventory = Commit(publisher);

                Assert.Equal(expected, inventory.Weapons.Select(weapon => weapon.Id));
            }
        }

        [Fact]
        public void InvalidAndMissingSequencesLeaveGapsWithoutBlockingLaterResults()
        {
            var publisher = new InventoryResultPublisher();
            publisher.StageWeapon(0, WeaponAt(0), isPublishable: true);
            publisher.StageWeapon(1, WeaponAt(1), isPublishable: false);
            // Sequence 2 represents a filtered item or worker exception and is never staged.
            publisher.StageWeapon(3, WeaponAt(3), isPublishable: true);

            Inventory inventory = Commit(publisher);

            Assert.Equal(new[] { 0, 3 }, inventory.Weapons.Select(weapon => weapon.Id));
        }

        [Fact]
        public void StagingDoesNotMutateFinalCollectionsAndCommitIsIdempotent()
        {
            var publisher = new InventoryResultPublisher();
            var inventory = new Inventory();
            var equippedWeapons = new List<Weapon>();
            var equippedArtifacts = new List<Artifact>();
            publisher.StageWeapon(0, WeaponAt(0), isPublishable: true);
            publisher.StageArtifact(0, ArtifactAt(0), isPublishable: true);

            Assert.Empty(inventory.Weapons);
            Assert.Empty(inventory.Artifacts);

            publisher.CommitTo(inventory, equippedWeapons, equippedArtifacts);
            publisher.CommitTo(inventory, equippedWeapons, equippedArtifacts);

            Assert.Single(inventory.Weapons);
            Assert.Single(inventory.Artifacts);
        }

        [Fact]
        public void DuplicateSequenceCannotBeCommittedTwice()
        {
            var publisher = new InventoryResultPublisher();
            publisher.StageWeapon(4, WeaponAt(4), isPublishable: true);

            Assert.Throws<InvalidOperationException>(() =>
                publisher.StageWeapon(4, WeaponAt(4), isPublishable: true));

            Inventory inventory = Commit(publisher);
            Assert.Single(inventory.Weapons);
        }

        [Fact]
        public void DeferredCorrectionCanPromoteInvalidEntryBeforeCommit()
        {
            var publisher = new InventoryResultPublisher();
            StagedInventoryResult<Weapon> staged =
                publisher.StageWeapon(7, WeaponAt(7), isPublishable: false);

            Assert.True(staged.MarkPublishable());
            Assert.False(staged.MarkPublishable());

            Inventory inventory = Commit(publisher);
            Assert.Equal(7, Assert.Single(inventory.Weapons).Id);
        }

        [Fact]
        public void EquipmentAssociationUsesTheSameCommittedItems()
        {
            var publisher = new InventoryResultPublisher();
            Weapon equippedWeapon = WeaponAt(5, "Jean");
            Artifact equippedArtifact = ArtifactAt(2, "Zhongli");
            publisher.StageWeapon(5, equippedWeapon, isPublishable: true);
            publisher.StageWeapon(1, WeaponAt(1), isPublishable: true);
            publisher.StageArtifact(2, equippedArtifact, isPublishable: true);

            var inventory = new Inventory();
            var equippedWeapons = new List<Weapon>();
            var equippedArtifacts = new List<Artifact>();
            publisher.CommitTo(inventory, equippedWeapons, equippedArtifacts);

            Assert.Same(equippedWeapon, Assert.Single(equippedWeapons));
            Assert.Same(equippedArtifact, Assert.Single(equippedArtifacts));
            Assert.Same(equippedWeapon, inventory.Weapons.Last());
            Assert.Same(equippedArtifact, Assert.Single(inventory.Artifacts));
        }

        [Fact]
        public async Task WorkerExceptionDoesNotBlockLaterValidResult()
        {
            var publisher = new InventoryResultPublisher();
            Task failingWorker = Task.Run(() =>
            {
                publisher.StageWeapon(0, WeaponAt(0), isPublishable: true);
                throw new InvalidOperationException("synthetic worker failure");
            });
            Task laterWorker = Task.Run(() =>
                publisher.StageWeapon(2, WeaponAt(2), isPublishable: true));

            await Assert.ThrowsAsync<InvalidOperationException>(() => failingWorker);
            await laterWorker;

            Inventory inventory = Commit(publisher);
            Assert.Equal(new[] { 0, 2 }, inventory.Weapons.Select(weapon => weapon.Id));
        }

        [Fact]
        public void CancellationStylePartialCompletionPublishesOnlyCompletedResults()
        {
            var publisher = new InventoryResultPublisher();
            publisher.StageArtifact(0, ArtifactAt(0), isPublishable: true);
            publisher.StageArtifact(1, ArtifactAt(1), isPublishable: true);
            // Cancellation prevents later queued work from producing a staged result.

            Inventory inventory = Commit(publisher);

            Assert.Equal(new[] { 0, 1 }, inventory.Artifacts.Select(artifact => artifact.Id));
        }

        [Fact]
        public void ModelIdMustMatchProducerSequence()
        {
            var publisher = new InventoryResultPublisher();

            Assert.Throws<ArgumentException>(() =>
                publisher.StageWeapon(3, WeaponAt(4), isPublishable: true));
            Assert.Throws<ArgumentException>(() =>
                publisher.StageArtifact(3, ArtifactAt(4), isPublishable: true));
        }

        private static Inventory Commit(InventoryResultPublisher publisher)
        {
            var inventory = new Inventory();
            publisher.CommitTo(
                inventory,
                new List<Weapon>(),
                new List<Artifact>());
            return inventory;
        }

        private static Weapon WeaponAt(int sequence, string equippedCharacter = null) =>
            new Weapon(
                "TestWeapon" + sequence,
                90,
                true,
                1,
                _equippedCharacter: equippedCharacter,
                _id: sequence,
                _rarity: 5);

        private static Artifact ArtifactAt(int sequence, string equippedCharacter = null) =>
            new Artifact(
                "TestSet",
                5,
                20,
                "flower",
                "hp",
                new List<Artifact.SubStat>(),
                new List<Artifact.SubStat>(),
                equippedCharacter,
                sequence);
    }
}

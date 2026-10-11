using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;

namespace InventoryKamera
{
    /// <summary>
    /// Represents one parsed inventory item after bitmap-backed OCR has completed. The item carries
    /// no image ownership; <see cref="OCRImageCollection"/> remains responsible for every bitmap.
    /// </summary>
    internal sealed class StagedInventoryResult<T> where T : class
    {
        private int publishable;

        internal StagedInventoryResult(int sourceSequence, T item, bool isPublishable)
        {
            if (sourceSequence < 0) throw new ArgumentOutOfRangeException(nameof(sourceSequence));
            SourceSequence = sourceSequence;
            Item = item ?? throw new ArgumentNullException(nameof(item));
            publishable = isPublishable ? 1 : 0;
        }

        internal int SourceSequence { get; }
        internal T Item { get; }
        internal bool IsPublishable => Volatile.Read(ref publishable) != 0;

        /// <summary>Promotes an initially invalid result after deferred correction makes it valid.</summary>
        internal bool MarkPublishable() => Interlocked.Exchange(ref publishable, 1) == 0;

        /// <summary>Demotes a result when deferred correction makes the staged item invalid.</summary>
        internal bool MarkUnpublishable() => Interlocked.Exchange(ref publishable, 0) != 0;
    }

    /// <summary>
    /// Collects weapon/artifact parse results from parallel OCR workers and publishes them once, in
    /// producer sequence order, after worker processing and deferred correction have completed.
    /// </summary>
    internal sealed class InventoryResultPublisher
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly object gate = new object();
        private readonly Dictionary<int, StagedInventoryResult<Weapon>> weapons =
            new Dictionary<int, StagedInventoryResult<Weapon>>();
        private readonly Dictionary<int, StagedInventoryResult<Artifact>> artifacts =
            new Dictionary<int, StagedInventoryResult<Artifact>>();
        private bool committed;

        internal StagedInventoryResult<Weapon> StageWeapon(
            int sourceSequence,
            Weapon weapon,
            bool isPublishable)
        {
            if (weapon == null) throw new ArgumentNullException(nameof(weapon));
            if (weapon.Id != sourceSequence)
                throw new ArgumentException("Weapon ID must match its producer source sequence.", nameof(weapon));
            var result = new StagedInventoryResult<Weapon>(sourceSequence, weapon, isPublishable);
            lock (gate)
            {
                ThrowIfCommitted();
                if (weapons.ContainsKey(sourceSequence))
                    throw new InvalidOperationException($"Weapon sequence {sourceSequence} was staged more than once.");
                weapons.Add(sourceSequence, result);
            }
            return result;
        }

        internal StagedInventoryResult<Artifact> StageArtifact(
            int sourceSequence,
            Artifact artifact,
            bool isPublishable)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            if (artifact.Id != sourceSequence)
                throw new ArgumentException("Artifact ID must match its producer source sequence.", nameof(artifact));
            var result = new StagedInventoryResult<Artifact>(sourceSequence, artifact, isPublishable);
            lock (gate)
            {
                ThrowIfCommitted();
                if (artifacts.ContainsKey(sourceSequence))
                    throw new InvalidOperationException($"Artifact sequence {sourceSequence} was staged more than once.");
                artifacts.Add(sourceSequence, result);
            }
            return result;
        }

        internal void CommitTo(
            Inventory inventory,
            ICollection<Weapon> equippedWeapons,
            ICollection<Artifact> equippedArtifacts)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (equippedWeapons == null) throw new ArgumentNullException(nameof(equippedWeapons));
            if (equippedArtifacts == null) throw new ArgumentNullException(nameof(equippedArtifacts));

            lock (gate)
            {
                if (committed) return;

                foreach (StagedInventoryResult<Weapon> result in weapons.Values
                    .Where(result => result.IsPublishable)
                    .OrderBy(result => result.SourceSequence))
                {
                    inventory.Add(result.Item);
                    if (!string.IsNullOrWhiteSpace(result.Item.EquippedCharacter))
                        equippedWeapons.Add(result.Item);
                }

                foreach (StagedInventoryResult<Artifact> result in artifacts.Values
                    .Where(result => result.IsPublishable)
                    .OrderBy(result => result.SourceSequence))
                {
                    if (!ArtifactResultIntegrity.IsPublishable(result.Item, out ArtifactValidationResult validation))
                    {
                        Logger.Warn(
                            "Artifact #{0} was omitted at deterministic publication: {1}.",
                            result.SourceSequence,
                            validation.Summary);
                        continue;
                    }

                    inventory.Add(result.Item);
                    if (!string.IsNullOrWhiteSpace(result.Item.EquippedCharacter))
                        equippedArtifacts.Add(result.Item);
                }

                committed = true;
            }
        }

        private void ThrowIfCommitted()
        {
            if (committed)
                throw new InvalidOperationException("Inventory results have already been committed.");
        }
    }
}

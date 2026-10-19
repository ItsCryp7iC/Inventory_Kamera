using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace InventoryKamera
{
    public enum AchievementTitleGroupKind
    {
        Unique,
        StageFamily,
        UnrelatedCollision,
    }

    /// <summary>
    /// Immutable catalog-level classification of every canonical entry sharing an exact title.
    /// Candidate order is deterministic and never implies a preferred collision winner.
    /// </summary>
    public sealed class AchievementTitleGroup
    {
        internal AchievementTitleGroup(
            string canonicalTitle,
            IEnumerable<AchievementCatalogEntry> entries)
        {
            CanonicalTitle = canonicalTitle ?? throw new ArgumentNullException(nameof(canonicalTitle));
            AchievementCatalogEntry[] candidates = (entries ??
                    throw new ArgumentNullException(nameof(entries)))
                .ToArray();
            if (candidates.Length == 0)
                throw new ArgumentException("A title group must contain at least one entry.", nameof(entries));

            bool oneStageFamily = candidates.Length > 1 &&
                candidates.All(entry => entry.StageGroupId.HasValue) &&
                candidates.Select(entry => entry.StageGroupId.Value).Distinct().Count() == 1;

            Kind = candidates.Length == 1
                ? AchievementTitleGroupKind.Unique
                : oneStageFamily
                    ? AchievementTitleGroupKind.StageFamily
                    : AchievementTitleGroupKind.UnrelatedCollision;
            StageGroupId = oneStageFamily ? candidates[0].StageGroupId : null;

            AchievementCatalogEntry[] ordered = oneStageFamily
                ? candidates.OrderBy(entry => entry.StageIndex).ThenBy(entry => entry.Id).ToArray()
                : candidates.OrderBy(entry => entry.Id).ToArray();
            Entries = Array.AsReadOnly(ordered);
            CandidateIds = new ReadOnlyCollection<int>(ordered.Select(entry => entry.Id).ToArray());
        }

        public string CanonicalTitle { get; }
        public AchievementTitleGroupKind Kind { get; }
        public IReadOnlyList<AchievementCatalogEntry> Entries { get; }
        public IReadOnlyList<int> CandidateIds { get; }
        public int? StageGroupId { get; }
        public bool IsUnique => Kind == AchievementTitleGroupKind.Unique;
        public bool IsStageFamily => Kind == AchievementTitleGroupKind.StageFamily;
        public bool IsUnrelatedCollision => Kind == AchievementTitleGroupKind.UnrelatedCollision;
    }
}

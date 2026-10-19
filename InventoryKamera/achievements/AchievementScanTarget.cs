using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace InventoryKamera
{
    /// <summary>
    /// One deterministic full-scan search target. The requested ID drives the existing single
    /// scanner only; title-group candidates remain the canonical identity evidence.
    /// </summary>
    internal sealed class AchievementScanTarget
    {
        private AchievementScanTarget(
            string canonicalTitle,
            AchievementTitleGroupKind titleGroupKind,
            int requestedCanonicalId,
            IEnumerable<int> candidateIds,
            int? stageGroupId)
        {
            CanonicalTitle = canonicalTitle ?? throw new ArgumentNullException(nameof(canonicalTitle));
            TitleGroupKind = titleGroupKind;
            RequestedCanonicalId = requestedCanonicalId;
            CandidateIds = new ReadOnlyCollection<int>(
                (candidateIds ?? throw new ArgumentNullException(nameof(candidateIds))).ToArray());
            StageGroupId = stageGroupId;
        }

        internal string CanonicalTitle { get; }
        internal AchievementTitleGroupKind TitleGroupKind { get; }
        internal int RequestedCanonicalId { get; }
        internal IReadOnlyList<int> CandidateIds { get; }
        internal int? StageGroupId { get; }

        internal static IReadOnlyList<AchievementScanTarget> FromCatalog(AchievementCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            var targets = new List<AchievementScanTarget>(catalog.TitleGroups.Count);
            foreach (AchievementTitleGroup group in catalog.TitleGroups)
            {
                int requestedId = group.Kind == AchievementTitleGroupKind.StageFamily
                    ? group.StageGroupId.Value
                    : catalog.FindByExactName(group.CanonicalTitle)[0].Id;
                targets.Add(new AchievementScanTarget(
                    group.CanonicalTitle,
                    group.Kind,
                    requestedId,
                    group.CandidateIds,
                    group.StageGroupId));
            }

            return Array.AsReadOnly(targets.ToArray());
        }
    }
}

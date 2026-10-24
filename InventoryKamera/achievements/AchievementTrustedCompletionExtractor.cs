using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace InventoryKamera
{
    /// <summary>
    /// Extracts canonical completions from unique positive completion evidence and from the exact
    /// completed IDs supplied by resolved staged progress. Collision and unresolved evidence never
    /// contributes IDs. The same rules are used by update scanning and export preparation.
    /// </summary>
    internal static class AchievementTrustedCompletionExtractor
    {
        internal static IReadOnlyList<int> Extract(
            IEnumerable<AchievementSingleScanResult> results) =>
            Merge(Array.Empty<int>(), results);

        /// <summary>
        /// Applies the same canonical trust boundary to bulk-list rows. Positive unique completion
        /// evidence contributes its single ID; staged rows contribute only the IDs returned by the
        /// shared stage-progress resolver.
        /// </summary>
        internal static IReadOnlyList<int> Extract(
            IEnumerable<AchievementVisibleRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            var trustedIds = new HashSet<int>();
            foreach (AchievementVisibleRow row in rows)
            {
                if (row == null || !row.SuitableForTrustedCompletionExtraction)
                    continue;

                if (TryGetTrustedCompletedId(
                    row.IdentityState == AchievementIdentityState.Identified
                        ? AchievementTitleGroupKind.Unique
                        : AchievementTitleGroupKind.StageFamily,
                    row.CandidateIds,
                    row.CompletionState == AchievementCompletionState.CompletedClaimed ||
                        row.CompletionState == AchievementCompletionState.CompletedUnclaimed,
                    out int id))
                {
                    trustedIds.Add(id);
                }

                if (row.StageProgressResult?.State == AchievementStageProgressState.Resolved)
                {
                    foreach (int stageId in row.StageProgressResult.CompletedStageIds)
                        trustedIds.Add(stageId);
                }
            }

            return new ReadOnlyCollection<int>(trustedIds.OrderBy(id => id).ToArray());
        }

        internal static IReadOnlyList<int> Merge(
            IEnumerable<int> previousTrustedIds,
            IEnumerable<AchievementSingleScanResult> results)
        {
            if (previousTrustedIds == null)
                throw new ArgumentNullException(nameof(previousTrustedIds));
            if (results == null)
                throw new ArgumentNullException(nameof(results));

            var trustedIds = new HashSet<int>(previousTrustedIds);
            MergeInto(trustedIds, results);
            return new ReadOnlyCollection<int>(trustedIds.OrderBy(id => id).ToArray());
        }

        internal static void MergeInto(
            ISet<int> trustedIds,
            IEnumerable<AchievementSingleScanResult> results)
        {
            if (trustedIds == null) throw new ArgumentNullException(nameof(trustedIds));
            if (results == null) throw new ArgumentNullException(nameof(results));

            foreach (AchievementSingleScanResult result in results)
            {
                if (TryGetTrustedCompletedId(result, out int id))
                    trustedIds.Add(id);

                if (result?.TitleGroupKind == AchievementTitleGroupKind.StageFamily &&
                    result.StageProgressResult?.State == AchievementStageProgressState.Resolved)
                {
                    foreach (int stageId in result.StageProgressResult.CompletedStageIds)
                        trustedIds.Add(stageId);
                }
            }
        }

        internal static bool TryGetTrustedCompletedId(
            AchievementSingleScanResult result,
            out int id)
        {
            if (result == null)
            {
                id = 0;
                return false;
            }

            return TryGetTrustedCompletedId(
                result.TitleGroupKind,
                result.CandidateIds,
                result.State,
                out id);
        }

        internal static bool TryGetTrustedCompletedId(
            AchievementTitleGroupKind? titleGroupKind,
            IReadOnlyList<int> candidateIds,
            AchievementSingleScanState state,
            out int id) => TryGetTrustedCompletedId(
                titleGroupKind,
                candidateIds,
                state == AchievementSingleScanState.Completed,
                out id);

        private static bool TryGetTrustedCompletedId(
            AchievementTitleGroupKind? titleGroupKind,
            IReadOnlyList<int> candidateIds,
            bool positivelyCompleted,
            out int id)
        {
            if (positivelyCompleted &&
                titleGroupKind == AchievementTitleGroupKind.Unique &&
                candidateIds != null &&
                candidateIds.Count == 1)
            {
                id = candidateIds[0];
                return true;
            }

            id = 0;
            return false;
        }
    }
}

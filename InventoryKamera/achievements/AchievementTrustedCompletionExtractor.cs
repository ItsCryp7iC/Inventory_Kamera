using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace InventoryKamera
{
    /// <summary>
    /// Extracts only canonical completions that the current scanner can identify without stage or
    /// collision inference. The same predicate is used by update scanning and export preparation.
    /// </summary>
    internal static class AchievementTrustedCompletionExtractor
    {
        internal static IReadOnlyList<int> Extract(
            IEnumerable<AchievementSingleScanResult> results) =>
            Merge(Array.Empty<int>(), results);

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
            out int id)
        {
            if (state == AchievementSingleScanState.Completed &&
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

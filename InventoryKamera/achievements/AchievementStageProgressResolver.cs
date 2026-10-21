using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace InventoryKamera
{
    internal enum AchievementStageProgressState
    {
        Resolved,
        PendingClaim,
        AmbiguousReward,
        UnresolvedReward,
        InvalidContext,
    }

    internal sealed class AchievementStageProgressResult
    {
        internal AchievementStageProgressResult(
            AchievementStageProgressState state,
            int? stageGroupId,
            int? currentStageIndex,
            int? currentStageId,
            IEnumerable<int> completedStageIds,
            AchievementRewardResult rewardResult,
            string reason,
            string diagnostics)
        {
            State = state;
            StageGroupId = stageGroupId;
            CurrentStageIndex = currentStageIndex;
            CurrentStageId = currentStageId;
            CompletedStageIds = new ReadOnlyCollection<int>(
                (completedStageIds ?? Array.Empty<int>()).ToArray());
            RewardResult = rewardResult;
            Reason = reason ?? string.Empty;
            Diagnostics = diagnostics ?? string.Empty;
        }

        internal AchievementStageProgressState State { get; }
        internal int? StageGroupId { get; }
        internal int? CurrentStageIndex { get; }
        internal int? CurrentStageId { get; }
        internal IReadOnlyList<int> CompletedStageIds { get; }
        internal AchievementRewardResult RewardResult { get; }
        internal string Reason { get; }
        internal string Diagnostics { get; }
    }

    /// <summary>
    /// Maps a staged title-family's visible reward and completion state to conservative canonical
    /// progress. Reward meaning comes only from that catalog family; no global reward table exists.
    /// </summary>
    internal sealed class AchievementStageProgressResolver
    {
        internal AchievementStageProgressResult ResolveExplicitNoResults(
            AchievementTitleGroup stageFamily)
        {
            if (stageFamily?.Kind != AchievementTitleGroupKind.StageFamily ||
                stageFamily.Entries.Any(entry => !entry.Hidden))
            {
                return Finish(
                    AchievementStageProgressState.InvalidContext,
                    stageFamily,
                    null,
                    null,
                    Array.Empty<int>(),
                    "Explicit no-results stage resolution requires an all-hidden canonical stage family.");
            }

            return Finish(
                AchievementStageProgressState.Resolved,
                stageFamily,
                null,
                null,
                Array.Empty<int>(),
                "The exact-title no-results screen proves stage 1 is not complete, so zero stages are complete.");
        }

        internal AchievementStageProgressResult Resolve(
            AchievementIdentityResult identity,
            AchievementCompletionResult completion,
            AchievementRewardResult reward,
            AchievementTitleGroup stageFamily)
        {
            if (identity?.State != AchievementIdentityState.StageGroupIdentified ||
                stageFamily?.Kind != AchievementTitleGroupKind.StageFamily ||
                !identity.StageGroupId.HasValue ||
                identity.StageGroupId != stageFamily.StageGroupId)
            {
                return Finish(
                    AchievementStageProgressState.InvalidContext,
                    stageFamily,
                    null,
                    reward,
                    Array.Empty<int>(),
                    "A matching positively identified canonical stage family was not supplied.");
            }

            if (completion == null)
            {
                return Finish(
                    AchievementStageProgressState.InvalidContext,
                    stageFamily,
                    null,
                    reward,
                    Array.Empty<int>(),
                    "A completion classification was not supplied.");
            }

            if (completion.State == AchievementCompletionState.CompletedUnclaimed)
            {
                return Finish(
                    AchievementStageProgressState.PendingClaim,
                    stageFamily,
                    null,
                    reward,
                    Array.Empty<int>(),
                    "Claim is pending, so reward-to-stage inference is blocked.");
            }

            if (completion.State != AchievementCompletionState.Incomplete &&
                completion.State != AchievementCompletionState.CompletedClaimed)
            {
                return Finish(
                    AchievementStageProgressState.InvalidContext,
                    stageFamily,
                    null,
                    reward,
                    Array.Empty<int>(),
                    $"Completion state {completion.State} cannot establish staged progress.");
            }

            if (reward?.RewardValue == null)
            {
                return Finish(
                    AchievementStageProgressState.UnresolvedReward,
                    stageFamily,
                    null,
                    reward,
                    Array.Empty<int>(),
                    reward?.Reason ?? "Reward OCR did not provide a resolved value.");
            }

            AchievementCatalogEntry[] matchingStages = stageFamily.Entries
                .Where(entry => entry.Primogems == reward.RewardValue.Value)
                .ToArray();
            if (matchingStages.Length == 0)
            {
                return Finish(
                    AchievementStageProgressState.UnresolvedReward,
                    stageFamily,
                    null,
                    reward,
                    Array.Empty<int>(),
                    "The recognized reward does not map to a stage in this canonical family.");
            }
            if (matchingStages.Length > 1)
            {
                return Finish(
                    AchievementStageProgressState.AmbiguousReward,
                    stageFamily,
                    null,
                    reward,
                    Array.Empty<int>(),
                    "The recognized reward maps to multiple stages in this canonical family.");
            }

            AchievementCatalogEntry current = matchingStages[0];
            int inclusiveLimit = completion.State == AchievementCompletionState.CompletedClaimed
                ? current.StageIndex.Value
                : current.StageIndex.Value - 1;
            int[] completedIds = stageFamily.Entries
                .Where(entry => entry.StageIndex.Value <= inclusiveLimit)
                .OrderBy(entry => entry.StageIndex.Value)
                .Select(entry => entry.Id)
                .ToArray();
            string reason = completion.State == AchievementCompletionState.CompletedClaimed
                ? "The visible claimed stage and every earlier stage are complete."
                : "Only stages before the visible incomplete stage are complete.";
            return Finish(
                AchievementStageProgressState.Resolved,
                stageFamily,
                current,
                reward,
                completedIds,
                reason);
        }

        private static AchievementStageProgressResult Finish(
            AchievementStageProgressState state,
            AchievementTitleGroup stageFamily,
            AchievementCatalogEntry current,
            AchievementRewardResult reward,
            IEnumerable<int> completedIds,
            string reason)
        {
            int[] completed = (completedIds ?? Array.Empty<int>()).ToArray();
            string boundedReason = Bound(reason, 240);
            var diagnostics = new StringBuilder(480);
            diagnostics.Append("stageProgressState=").Append(state);
            diagnostics.Append("; stageGroupId=").Append(
                stageFamily?.StageGroupId?.ToString(CultureInfo.InvariantCulture) ?? "(none)");
            diagnostics.Append("; stageCandidateCount=").Append(
                (stageFamily?.Entries.Count ?? 0).ToString(CultureInfo.InvariantCulture));
            diagnostics.Append("; rewardValue=").Append(
                reward?.RewardValue?.ToString(CultureInfo.InvariantCulture) ?? "(none)");
            diagnostics.Append("; stageResolution=").Append(state);
            diagnostics.Append("; currentStageIndex=").Append(
                current?.StageIndex?.ToString(CultureInfo.InvariantCulture) ?? "(none)");
            diagnostics.Append("; currentStageId=").Append(
                current?.Id.ToString(CultureInfo.InvariantCulture) ?? "(none)");
            diagnostics.Append("; completedStageIds=").Append(string.Join(",", completed));
            diagnostics.Append("; reason=").Append(boundedReason);

            return new AchievementStageProgressResult(
                state,
                stageFamily?.StageGroupId,
                current?.StageIndex,
                current?.Id,
                completed,
                reward,
                boundedReason,
                diagnostics.ToString());
        }

        private static string Bound(string value, int maximumLength)
        {
            string cleaned = string.Join(" ", (value ?? string.Empty)
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            return cleaned.Length <= maximumLength
                ? cleaned
                : cleaned.Substring(0, maximumLength) + "...";
        }
    }
}

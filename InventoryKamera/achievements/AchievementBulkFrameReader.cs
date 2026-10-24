using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;

namespace InventoryKamera
{
    internal sealed class AchievementRowFingerprint
    {
        internal AchievementRowFingerprint(string value, bool canonical)
        {
            Value = value ?? string.Empty;
            Canonical = canonical;
        }

        internal string Value { get; }
        internal bool Canonical { get; }
        internal bool SuitableForDeduplication => Canonical;
    }

    internal sealed class AchievementVisibleRow
    {
        internal AchievementVisibleRow(
            int rowIndex,
            NormalizedRectangle normalizedBounds,
            AchievementVisibleRowVisibility visibility,
            string rawTitle,
            string normalizedTitle,
            AchievementIdentityState identityState,
            IEnumerable<int> candidateIds,
            int? stageGroupId,
            AchievementCompletionState completionState,
            int? progressCurrent,
            int? progressTarget,
            int? rewardValue,
            double identityScore,
            double runnerUpScore,
            float ocrConfidence,
            bool suitableForTrustedCompletionExtraction,
            AchievementRowFingerprint fingerprint,
            AchievementIdentityResult identityResult,
            AchievementCompletionResult completionResult,
            AchievementStageProgressResult stageProgressResult,
            string diagnostics)
        {
            RowIndex = rowIndex;
            NormalizedBounds = normalizedBounds;
            Visibility = visibility;
            RawTitle = rawTitle ?? string.Empty;
            NormalizedTitle = normalizedTitle ?? string.Empty;
            IdentityState = identityState;
            CandidateIds = new ReadOnlyCollection<int>((candidateIds ?? Array.Empty<int>()).ToArray());
            StageGroupId = stageGroupId;
            CompletionState = completionState;
            ProgressCurrent = progressCurrent;
            ProgressTarget = progressTarget;
            RewardValue = rewardValue;
            IdentityScore = identityScore;
            RunnerUpScore = runnerUpScore;
            OcrConfidence = ocrConfidence;
            SuitableForTrustedCompletionExtraction = suitableForTrustedCompletionExtraction;
            Fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
            IdentityResult = identityResult;
            CompletionResult = completionResult;
            StageProgressResult = stageProgressResult;
            Diagnostics = diagnostics ?? string.Empty;
        }

        internal int RowIndex { get; }
        internal double NormalizedVerticalPosition => NormalizedBounds.Y;
        internal NormalizedRectangle NormalizedBounds { get; }
        internal AchievementVisibleRowVisibility Visibility { get; }
        internal string RawTitle { get; }
        internal string NormalizedTitle { get; }
        internal AchievementIdentityState IdentityState { get; }
        internal IReadOnlyList<int> CandidateIds { get; }
        internal int? StageGroupId { get; }
        internal AchievementCompletionState CompletionState { get; }
        internal int? ProgressCurrent { get; }
        internal int? ProgressTarget { get; }
        internal int? RewardValue { get; }
        internal double IdentityScore { get; }
        internal double RunnerUpScore { get; }
        internal float OcrConfidence { get; }
        internal bool SuitableForTrustedCompletionExtraction { get; }
        internal AchievementRowFingerprint Fingerprint { get; }
        internal AchievementIdentityResult IdentityResult { get; }
        internal AchievementCompletionResult CompletionResult { get; }
        internal AchievementStageProgressResult StageProgressResult { get; }
        internal string Diagnostics { get; }
    }

    internal sealed class AchievementBulkFrameTiming
    {
        internal AchievementBulkFrameTiming(
            TimeSpan capture,
            TimeSpan rowDetection,
            TimeSpan titleOcr,
            TimeSpan completionOcr,
            TimeSpan rewardOcr,
            TimeSpan total,
            int rowsProcessed,
            int rowsResolved)
        {
            Capture = capture;
            RowDetection = rowDetection;
            TitleOcr = titleOcr;
            CompletionOcr = completionOcr;
            RewardOcr = rewardOcr;
            Total = total;
            RowsProcessed = rowsProcessed;
            RowsResolved = rowsResolved;
        }

        internal TimeSpan Capture { get; }
        internal TimeSpan RowDetection { get; }
        internal TimeSpan TitleOcr { get; }
        internal TimeSpan CompletionOcr { get; }
        internal TimeSpan RewardOcr { get; }
        internal TimeSpan Total { get; }
        internal int RowsProcessed { get; }
        internal int RowsResolved { get; }
        internal double? MillisecondsPerResolvedRow => RowsResolved == 0
            ? null
            : Total.TotalMilliseconds / RowsResolved;
    }

    internal sealed class AchievementBulkFrameResult
    {
        internal AchievementBulkFrameResult(
            AchievementScreenState screenState,
            IEnumerable<AchievementVisibleRow> visibleRows,
            int? completedTotal,
            AchievementBulkFrameTiming timing,
            string diagnostics)
        {
            ScreenState = screenState;
            VisibleRows = new ReadOnlyCollection<AchievementVisibleRow>(
                (visibleRows ?? Array.Empty<AchievementVisibleRow>()).ToArray());
            IdentifiedRows = new ReadOnlyCollection<AchievementVisibleRow>(VisibleRows
                .Where(row => row.IdentityState == AchievementIdentityState.Identified ||
                    row.IdentityState == AchievementIdentityState.StageGroupIdentified)
                .ToArray());
            UnresolvedRows = new ReadOnlyCollection<AchievementVisibleRow>(VisibleRows
                .Where(row => row.IdentityState != AchievementIdentityState.Identified &&
                    row.IdentityState != AchievementIdentityState.StageGroupIdentified ||
                    row.CompletionState == AchievementCompletionState.Unresolved ||
                    row.IdentityState == AchievementIdentityState.StageGroupIdentified &&
                    row.StageProgressResult?.State != AchievementStageProgressState.Resolved)
                .ToArray());
            CompletedTotal = completedTotal;
            Timing = timing ?? throw new ArgumentNullException(nameof(timing));
            Diagnostics = diagnostics ?? string.Empty;
        }

        internal AchievementScreenState ScreenState { get; }
        internal IReadOnlyList<AchievementVisibleRow> VisibleRows { get; }
        internal IReadOnlyList<AchievementVisibleRow> IdentifiedRows { get; }
        internal IReadOnlyList<AchievementVisibleRow> UnresolvedRows { get; }
        internal int? CompletedTotal { get; }
        internal AchievementBulkFrameTiming Timing { get; }
        internal string Diagnostics { get; }
    }

    internal interface IAchievementListFrameReader
    {
        AchievementBulkFrameResult Read(Bitmap screenshot, TimeSpan? captureDuration = null);
    }

    internal sealed class AchievementVisibleRowIdentityResolver
    {
        private readonly AchievementCatalog catalog;
        private readonly AchievementTitleGroup[] groups;

        internal AchievementVisibleRowIdentityResolver(IAchievementCatalogProvider catalogProvider)
        {
            if (catalogProvider == null) throw new ArgumentNullException(nameof(catalogProvider));
            catalog = catalogProvider.Catalog ?? throw new ArgumentException(
                "The catalog provider returned no catalog.", nameof(catalogProvider));
            groups = catalog.TitleGroups.ToArray();
        }

        internal AchievementIdentityResult Resolve(AchievementResultTitleRead read)
        {
            AchievementTitleOcrAttempt[] attempts = read?.Attempts?.ToArray() ??
                Array.Empty<AchievementTitleOcrAttempt>();
            var evaluated = attempts.Select(Evaluate).Where(value => value != null).ToArray();
            EvaluatedTitleAttempt selected = evaluated
                .Where(IsPositive)
                .OrderByDescending(value => value.Score)
                .ThenByDescending(value => value.Attempt.Confidence)
                .FirstOrDefault() ?? evaluated
                .Where(value => value.Attempt.NormalizedText.Length > 0)
                .OrderByDescending(value => value.Score)
                .ThenByDescending(value => value.Attempt.Confidence)
                .FirstOrDefault();

            if (selected == null)
                return Finish(AchievementIdentityState.Unresolved, null, attempts, null,
                    "Visible-row title OCR returned no usable text.");
            if (selected.Attempt.Confidence < AchievementTitleMatcher.MinimumOcrConfidence)
                return Finish(AchievementIdentityState.Unresolved, selected.Group, attempts, selected,
                    "Visible-row title OCR confidence was below the established threshold.");
            if (selected.Score < AchievementTitleMatcher.MinimumScore)
                return Finish(AchievementIdentityState.Unresolved, selected.Group, attempts, selected,
                    "Visible-row title score was below the established identity threshold.");
            if (selected.Margin < AchievementTitleMatcher.MinimumRunnerUpMargin)
            {
                int[] ambiguousIds = selected.Group.CandidateIds
                    .Concat(selected.RunnerUpGroup?.CandidateIds ?? Array.Empty<int>())
                    .Distinct()
                    .OrderBy(id => id)
                    .ToArray();
                return Finish(AchievementIdentityState.Ambiguous, selected.Group, attempts, selected,
                    "The visible-row title winner did not exceed the runner-up by the required margin.",
                    ambiguousIds);
            }
            if (selected.Group.Kind == AchievementTitleGroupKind.UnrelatedCollision)
                return Finish(AchievementIdentityState.Ambiguous, selected.Group, attempts, selected,
                    "The visible title matches multiple unrelated canonical achievements.");

            AchievementIdentityState state = selected.Group.Kind == AchievementTitleGroupKind.StageFamily
                ? AchievementIdentityState.StageGroupIdentified
                : AchievementIdentityState.Identified;
            return Finish(state, selected.Group, attempts, selected,
                state == AchievementIdentityState.StageGroupIdentified
                    ? "The visible title positively identifies a canonical staged family."
                    : "The visible title positively identifies one canonical achievement.");
        }

        internal bool IsPositiveAttempt(AchievementTitleOcrAttempt attempt)
        {
            EvaluatedTitleAttempt value = Evaluate(attempt);
            return IsPositive(value);
        }

        private EvaluatedTitleAttempt Evaluate(AchievementTitleOcrAttempt attempt)
        {
            if (attempt == null || groups.Length == 0) return null;
            var ranked = groups
                .Select(group => new
                {
                    Group = group,
                    Score = AchievementTitleMatcher.Similarity(
                        attempt.ComparisonText,
                        AchievementTitleNormalizer.NormalizeForComparison(group.CanonicalTitle)),
                })
                .OrderByDescending(value => value.Score)
                .ThenBy(value => value.Group.CanonicalTitle, StringComparer.Ordinal)
                .ToArray();
            return new EvaluatedTitleAttempt(
                attempt,
                ranked[0].Group,
                ranked[0].Score,
                ranked.Length > 1 ? ranked[1].Group : null,
                ranked.Length > 1 ? ranked[1].Score : 0);
        }

        private static bool IsPositive(EvaluatedTitleAttempt value) =>
            value != null &&
            value.Attempt.NormalizedText.Length > 0 &&
            value.Attempt.Confidence >= AchievementTitleMatcher.MinimumOcrConfidence &&
            value.Score >= AchievementTitleMatcher.MinimumScore &&
            value.Margin >= AchievementTitleMatcher.MinimumRunnerUpMargin;

        private static AchievementIdentityResult Finish(
            AchievementIdentityState state,
            AchievementTitleGroup group,
            IEnumerable<AchievementTitleOcrAttempt> sourceAttempts,
            EvaluatedTitleAttempt selected,
            string reason,
            IEnumerable<int> candidateIds = null)
        {
            AchievementIdentityOcrAttempt[] attempts = (sourceAttempts ?? Array.Empty<AchievementTitleOcrAttempt>())
                .Select(attempt => new AchievementIdentityOcrAttempt(
                    attempt,
                    ReferenceEquals(attempt, selected?.Attempt) ? selected.Score : 0,
                    ReferenceEquals(attempt, selected?.Attempt) ? selected.RunnerUpGroup?.CanonicalTitle : string.Empty,
                    ReferenceEquals(attempt, selected?.Attempt) ? selected.RunnerUpScore : 0,
                    ReferenceEquals(attempt, selected?.Attempt) ? selected.Margin : 0,
                    ReferenceEquals(attempt, selected?.Attempt) ? reason : "Not selected."))
                .ToArray();
            int[] ids = (candidateIds ?? group?.CandidateIds ?? Array.Empty<int>()).ToArray();
            int? identifiedId = state == AchievementIdentityState.Identified && ids.Length == 1 ? ids[0] : null;
            int? stageGroupId = state == AchievementIdentityState.StageGroupIdentified ? group?.StageGroupId : null;
            int expectedId = group?.Entries.FirstOrDefault()?.Id ?? 0;
            string title = group?.CanonicalTitle ?? string.Empty;
            string diagnostics = string.Format(
                CultureInfo.InvariantCulture,
                "identityState={0}; rawTitle={1}; normalizedTitle={2}; score={3:0.0}; runnerUpScore={4:0.0}; margin={5:0.0}; confidence={6:0.000}; candidates={7}; reason={8}",
                state,
                selected?.Attempt.RawText ?? string.Empty,
                selected?.Attempt.NormalizedText ?? string.Empty,
                selected?.Score ?? 0,
                selected?.RunnerUpScore ?? 0,
                selected?.Margin ?? 0,
                selected?.Attempt.Confidence ?? 0,
                string.Join(",", ids),
                reason);
            return new AchievementIdentityResult(
                state,
                expectedId,
                title,
                identifiedId,
                stageGroupId,
                ids,
                selected?.Attempt.RawText,
                selected?.Attempt.NormalizedText,
                selected?.Score ?? 0,
                selected?.RunnerUpScore ?? 0,
                selected?.Margin ?? 0,
                selected?.Attempt.Confidence ?? 0,
                attempts,
                null,
                reason,
                diagnostics);
        }

        private sealed class EvaluatedTitleAttempt
        {
            internal EvaluatedTitleAttempt(
                AchievementTitleOcrAttempt attempt,
                AchievementTitleGroup group,
                double score,
                AchievementTitleGroup runnerUpGroup,
                double runnerUpScore)
            {
                Attempt = attempt;
                Group = group;
                Score = score;
                RunnerUpGroup = runnerUpGroup;
                RunnerUpScore = runnerUpScore;
            }

            internal AchievementTitleOcrAttempt Attempt { get; }
            internal AchievementTitleGroup Group { get; }
            internal double Score { get; }
            internal AchievementTitleGroup RunnerUpGroup { get; }
            internal double RunnerUpScore { get; }
            internal double Margin => Score - RunnerUpScore;
        }
    }

    internal static class AchievementVisibleRowCompletionResolver
    {
        internal static bool CanStopAfterVariant(IReadOnlyList<AchievementCompletionOcrAttempt> attempts)
        {
            AchievementCompletionResult result = Resolve(null, attempts);
            return result.State != AchievementCompletionState.Unresolved;
        }

        internal static AchievementCompletionResult Resolve(
            AchievementIdentityResult identity,
            IEnumerable<AchievementCompletionOcrAttempt> sourceAttempts)
        {
            AchievementCompletionOcrAttempt[] attempts =
                (sourceAttempts ?? Array.Empty<AchievementCompletionOcrAttempt>()).ToArray();
            if (identity != null && identity.State != AchievementIdentityState.Identified &&
                identity.State != AchievementIdentityState.StageGroupIdentified)
                return Finish(AchievementCompletionState.Unresolved, identity, attempts,
                    "Only a positively identified row is eligible for completion extraction.");

            AchievementCompletionOcrAttempt[] labels = attempts
                .Where(attempt => attempt.Scope == AchievementCompletionOcrScope.StateLabel)
                .ToArray();
            bool labelHasToken = labels.Any(attempt => attempt.ClaimTokenDetected || attempt.CompletedTokenDetected);
            AchievementCompletionOcrAttempt[] semantic = labelHasToken ? labels : attempts;
            bool claimObserved = semantic.Any(attempt => attempt.ClaimTokenDetected);
            bool completedObserved = semantic.Any(attempt => attempt.CompletedTokenDetected);
            bool claimAccepted = semantic.Any(attempt => attempt.ClaimTokenDetected &&
                attempt.Confidence >= AchievementCompletionClassifier.MinimumEvidenceConfidence);
            bool completedAccepted = semantic.Any(attempt => attempt.CompletedTokenDetected &&
                attempt.Confidence >= AchievementCompletionClassifier.MinimumEvidenceConfidence);
            AchievementCompletionOcrAttempt[] progressAttempts = labels
                .Where(attempt => attempt.Progress.Accepted)
                .ToArray();
            var distinctProgress = progressAttempts
                .Select(attempt => (attempt.Progress.Current.Value, attempt.Progress.Target.Value))
                .Distinct()
                .ToArray();
            bool progressConflict = labels.Any(attempt => attempt.Progress.MultipleFractions) ||
                distinctProgress.Length > 1;
            bool incompleteObserved = progressAttempts.Any(attempt =>
                attempt.Progress.Current < attempt.Progress.Target);
            bool incompleteAccepted = progressAttempts.Any(attempt =>
                attempt.Progress.Current < attempt.Progress.Target &&
                attempt.Confidence >= AchievementCompletionClassifier.MinimumEvidenceConfidence);

            if (progressConflict)
                return Finish(AchievementCompletionState.Unresolved, identity, attempts,
                    "Multiple contradictory or non-unique progress fractions were observed.");
            if (claimObserved && completedObserved)
                return Finish(AchievementCompletionState.Unresolved, identity, attempts,
                    "Claim and Completed evidence conflict.");
            if (incompleteObserved && (claimObserved || completedObserved))
                return Finish(AchievementCompletionState.Unresolved, identity, attempts,
                    "Completion token evidence conflicts with explicit incomplete progress.");
            if (claimAccepted)
                return Finish(AchievementCompletionState.CompletedUnclaimed, identity, attempts,
                    "A positive row-level Claim token was recognized above the confidence threshold.");
            if (completedAccepted)
                return Finish(AchievementCompletionState.CompletedClaimed, identity, attempts,
                    "A positive row-level Completed token was recognized above the confidence threshold.");
            if (incompleteAccepted)
                return Finish(AchievementCompletionState.Incomplete, identity, attempts,
                    "A positive progress fraction with current less than target was recognized.");
            return Finish(AchievementCompletionState.Unresolved, identity, attempts,
                "No strong, non-conflicting row-level completion evidence was recognized.");
        }

        private static AchievementCompletionResult Finish(
            AchievementCompletionState state,
            AchievementIdentityResult identity,
            AchievementCompletionOcrAttempt[] attempts,
            string reason)
        {
            AchievementCompletionOcrAttempt claim = attempts
                .Where(attempt => attempt.ClaimTokenDetected)
                .OrderByDescending(attempt => attempt.Confidence)
                .FirstOrDefault();
            AchievementCompletionOcrAttempt completed = attempts
                .Where(attempt => attempt.CompletedTokenDetected)
                .OrderByDescending(attempt => attempt.Confidence)
                .FirstOrDefault();
            AchievementCompletionOcrAttempt progress = attempts
                .Where(attempt => attempt.Scope == AchievementCompletionOcrScope.StateLabel &&
                    attempt.Progress.Accepted)
                .OrderByDescending(attempt => attempt.Confidence)
                .FirstOrDefault();
            AchievementCompletionOcrAttempt selected = state switch
            {
                AchievementCompletionState.CompletedUnclaimed => claim,
                AchievementCompletionState.CompletedClaimed => completed,
                AchievementCompletionState.Incomplete => progress,
                _ => attempts.OrderByDescending(attempt => attempt.Confidence).FirstOrDefault(),
            };
            return new AchievementCompletionResult(
                state,
                identity,
                null,
                state == AchievementCompletionState.CompletedUnclaimed,
                state == AchievementCompletionState.CompletedClaimed,
                claim?.RawText,
                completed?.RawText,
                progress?.Progress.RawText,
                progress?.Progress.Current,
                progress?.Progress.Target,
                attempts.Select(attempt => attempt.DateRaw).FirstOrDefault(value => value.Length > 0),
                selected?.RawText,
                selected?.Confidence ?? 0,
                attempts,
                reason,
                $"completionState={state}; reason={reason}");
        }
    }

    internal sealed class AchievementListFrameReader : IAchievementListFrameReader
    {
        private readonly AchievementCatalog catalog;
        private readonly IAchievementScreenDetector screenDetector;
        private readonly IAchievementVisibleRowDetector rowDetector;
        private readonly IAchievementListRowTitleReader titleReader;
        private readonly IAchievementListRowCompletionReader completionReader;
        private readonly IAchievementListRowRewardReader rewardReader;
        private readonly IAchievementCompletedTotalReader completedTotalReader;
        private readonly AchievementVisibleRowIdentityResolver identityResolver;
        private readonly AchievementStageProgressResolver stageResolver;

        internal AchievementListFrameReader(
            IAchievementCatalogProvider catalogProvider,
            IAchievementScreenDetector screenDetector,
            IAchievementVisibleRowDetector rowDetector,
            IAchievementListRowTitleReader titleReader,
            IAchievementListRowCompletionReader completionReader,
            IAchievementListRowRewardReader rewardReader,
            IAchievementCompletedTotalReader completedTotalReader,
            AchievementStageProgressResolver stageResolver)
        {
            if (catalogProvider == null) throw new ArgumentNullException(nameof(catalogProvider));
            catalog = catalogProvider.Catalog ?? throw new ArgumentException(
                "The catalog provider returned no catalog.", nameof(catalogProvider));
            this.screenDetector = screenDetector ?? throw new ArgumentNullException(nameof(screenDetector));
            this.rowDetector = rowDetector ?? throw new ArgumentNullException(nameof(rowDetector));
            this.titleReader = titleReader ?? throw new ArgumentNullException(nameof(titleReader));
            this.completionReader = completionReader ?? throw new ArgumentNullException(nameof(completionReader));
            this.rewardReader = rewardReader ?? throw new ArgumentNullException(nameof(rewardReader));
            this.completedTotalReader = completedTotalReader ?? throw new ArgumentNullException(nameof(completedTotalReader));
            this.stageResolver = stageResolver ?? throw new ArgumentNullException(nameof(stageResolver));
            identityResolver = new AchievementVisibleRowIdentityResolver(catalogProvider);
        }

        public AchievementBulkFrameResult Read(Bitmap screenshot, TimeSpan? captureDuration = null)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));

            Stopwatch total = Stopwatch.StartNew();
            AchievementScreenDetection screen = screenDetector.Detect(screenshot);
            AchievementCompletedTotalResult completedTotal = completedTotalReader.Read(screenshot);
            if (screen.ScreenState != AchievementScreenState.Idle)
            {
                total.Stop();
                var emptyTiming = new AchievementBulkFrameTiming(
                    captureDuration ?? TimeSpan.Zero,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    total.Elapsed,
                    0,
                    0);
                return new AchievementBulkFrameResult(
                    screen.ScreenState,
                    Array.Empty<AchievementVisibleRow>(),
                    completedTotal.CompletedTotal,
                    emptyTiming,
                    "Bulk row parsing requires a positively detected ordinary Idle Achievement list.");
            }

            Stopwatch part = Stopwatch.StartNew();
            AchievementVisibleRowDetection detected = rowDetector.Detect(screenshot);
            part.Stop();
            TimeSpan rowDetectionTime = part.Elapsed;
            TimeSpan titleTime = TimeSpan.Zero;
            TimeSpan completionTime = TimeSpan.Zero;
            TimeSpan rewardTime = TimeSpan.Zero;
            var rows = new List<AchievementVisibleRow>(detected.VisibleRows.Count);

            // Deliberately serial: IOcrService exposes no thread-safety contract and production uses
            // a shared engine pool. Parallel row OCR requires an explicit service-level guarantee.
            foreach (AchievementListRowGeometry geometry in detected.VisibleRows)
            {
                try
                {
                if (geometry.Visibility == AchievementVisibleRowVisibility.Uncertain ||
                    !geometry.TitleRegionVisible || !geometry.CompletionRegionVisible)
                {
                    rows.Add(CreateUnobservableRow(geometry,
                        "The visible row is clipped and does not contain complete title and completion regions."));
                    continue;
                }
                part.Restart();
                AchievementResultTitleRead titleRead = titleReader.Read(
                    screenshot,
                    geometry,
                    identityResolver.IsPositiveAttempt);
                AchievementIdentityResult identity = identityResolver.Resolve(titleRead);
                part.Stop();
                titleTime += part.Elapsed;

                AchievementCompletionResult completion;
                if (identity.State == AchievementIdentityState.Identified ||
                    identity.State == AchievementIdentityState.StageGroupIdentified)
                {
                    part.Restart();
                    AchievementCompletionRead completionRead = completionReader.Read(
                        screenshot,
                        geometry,
                        AchievementVisibleRowCompletionResolver.CanStopAfterVariant);
                    completion = AchievementVisibleRowCompletionResolver.Resolve(identity, completionRead.Attempts);
                    part.Stop();
                    completionTime += part.Elapsed;
                }
                else
                {
                    completion = AchievementVisibleRowCompletionResolver.Resolve(identity,
                        Array.Empty<AchievementCompletionOcrAttempt>());
                }

                AchievementStageProgressResult stageProgress = null;
                AchievementRewardResult reward = null;
                if (identity.State == AchievementIdentityState.StageGroupIdentified)
                {
                    AchievementTitleGroup group = catalog.GetTitleGroupForId(identity.CandidateIds.First());
                    if (completion.State == AchievementCompletionState.CompletedUnclaimed)
                    {
                        stageProgress = stageResolver.Resolve(identity, completion, null, group);
                    }
                    else if (completion.State == AchievementCompletionState.Incomplete ||
                        completion.State == AchievementCompletionState.CompletedClaimed)
                    {
                        if (geometry.RewardRegionVisible)
                        {
                            part.Restart();
                            reward = rewardReader.Read(screenshot, geometry);
                            part.Stop();
                            rewardTime += part.Elapsed;
                        }
                        stageProgress = stageResolver.Resolve(identity, completion, reward, group);
                    }
                }

                bool trusted = IsSuitableForTrust(identity, completion, stageProgress);
                AchievementRowFingerprint fingerprint = CreateFingerprint(identity, geometry);
                string diagnostics = string.Join(Environment.NewLine, new[]
                {
                    $"rowIndex={geometry.Index}; normalizedY={geometry.NormalizedBounds.Y.ToString("0.000000", CultureInfo.InvariantCulture)}",
                    $"visibility={geometry.Visibility}; titleRegionVisible={geometry.TitleRegionVisible}; completionRegionVisible={geometry.CompletionRegionVisible}; rewardRegionVisible={geometry.RewardRegionVisible}",
                    identity.Diagnostics,
                    completion.Diagnostics,
                    stageProgress?.Diagnostics ?? "stageProgressState=(not-applicable)",
                    $"fingerprint={fingerprint.Value}; canonicalFingerprint={fingerprint.Canonical}; trusted={trusted}",
                });
                rows.Add(new AchievementVisibleRow(
                    geometry.Index,
                    geometry.NormalizedBounds,
                    geometry.Visibility,
                    identity.RawTitle,
                    identity.NormalizedTitle,
                    identity.State,
                    identity.CandidateIds,
                    identity.StageGroupId,
                    completion.State,
                    completion.ProgressCurrent,
                    completion.ProgressTarget,
                    reward?.RewardValue,
                    identity.Score,
                    identity.RunnerUpScore,
                    identity.OcrConfidence,
                    trusted,
                    fingerprint,
                    identity,
                    completion,
                    stageProgress,
                    diagnostics));
                }
                catch (Exception ex)
                {
                    part.Stop();
                    rows.Add(CreateFailedRow(geometry, ex));
                }
            }

            total.Stop();
            int resolved = rows.Count(row =>
                (row.IdentityState == AchievementIdentityState.Identified ||
                 row.IdentityState == AchievementIdentityState.StageGroupIdentified) &&
                row.CompletionState != AchievementCompletionState.Unresolved);
            var timing = new AchievementBulkFrameTiming(
                captureDuration ?? TimeSpan.Zero,
                rowDetectionTime,
                titleTime,
                completionTime,
                rewardTime,
                total.Elapsed,
                rows.Count,
                resolved);
            var frameDiagnostics = new StringBuilder();
            frameDiagnostics.AppendLine($"screenState={screen.ScreenState}; completedTotal={completedTotal.CompletedTotal?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}");
            frameDiagnostics.AppendLine(detected.Diagnostics);
            frameDiagnostics.Append(
                $"rowsProcessed={rows.Count}; rowsResolved={resolved}; rowDetectionMs={timing.RowDetection.TotalMilliseconds:0.00}; " +
                $"titleOcrMs={timing.TitleOcr.TotalMilliseconds:0.00}; completionOcrMs={timing.CompletionOcr.TotalMilliseconds:0.00}; " +
                $"rewardOcrMs={timing.RewardOcr.TotalMilliseconds:0.00}; totalFrameMs={timing.Total.TotalMilliseconds:0.00}; " +
                $"msPerResolvedRow={timing.MillisecondsPerResolvedRow?.ToString("0.00", CultureInfo.InvariantCulture) ?? "(none)"}");
            return new AchievementBulkFrameResult(
                screen.ScreenState,
                rows,
                completedTotal.CompletedTotal,
                timing,
                frameDiagnostics.ToString());
        }

        private static AchievementVisibleRow CreateFailedRow(
            AchievementListRowGeometry geometry,
            Exception exception)
        {
            string reason = $"Visible row {geometry.Index} failed independently: " +
                $"{exception.GetType().Name}: {exception.Message}";
            var identity = new AchievementIdentityResult(
                AchievementIdentityState.Unresolved,
                0,
                string.Empty,
                null,
                null,
                Array.Empty<int>(),
                string.Empty,
                string.Empty,
                0,
                0,
                0,
                0,
                Array.Empty<AchievementIdentityOcrAttempt>(),
                null,
                reason,
                reason);
            AchievementCompletionResult completion = AchievementVisibleRowCompletionResolver.Resolve(
                identity,
                Array.Empty<AchievementCompletionOcrAttempt>());
            var fingerprint = new AchievementRowFingerprint(
                $"diagnostic:@{geometry.NormalizedBounds.Y:0.000}",
                canonical: false);
            return new AchievementVisibleRow(
                geometry.Index,
                geometry.NormalizedBounds,
                geometry.Visibility,
                string.Empty,
                string.Empty,
                AchievementIdentityState.Unresolved,
                Array.Empty<int>(),
                null,
                AchievementCompletionState.Unresolved,
                null,
                null,
                null,
                0,
                0,
                0,
                false,
                fingerprint,
                identity,
                completion,
                null,
                reason);
        }

        private static AchievementVisibleRow CreateUnobservableRow(
            AchievementListRowGeometry geometry,
            string reason)
        {
            var identity = new AchievementIdentityResult(
                AchievementIdentityState.Unresolved, 0, string.Empty, null, null,
                Array.Empty<int>(), string.Empty, string.Empty, 0, 0, 0, 0,
                Array.Empty<AchievementIdentityOcrAttempt>(), null, reason, reason);
            AchievementCompletionResult completion = AchievementVisibleRowCompletionResolver.Resolve(
                identity, Array.Empty<AchievementCompletionOcrAttempt>());
            var fingerprint = new AchievementRowFingerprint(
                $"diagnostic:partial@{geometry.NormalizedBounds.Y:0.000}", canonical: false);
            return new AchievementVisibleRow(
                geometry.Index, geometry.NormalizedBounds, geometry.Visibility,
                string.Empty, string.Empty, AchievementIdentityState.Unresolved,
                Array.Empty<int>(), null, AchievementCompletionState.Unresolved,
                null, null, null, 0, 0, 0, false, fingerprint, identity,
                completion, null,
                $"visibility={geometry.Visibility}; titleRegionVisible={geometry.TitleRegionVisible}; " +
                $"completionRegionVisible={geometry.CompletionRegionVisible}; reason={reason}");
        }

        private static bool IsSuitableForTrust(
            AchievementIdentityResult identity,
            AchievementCompletionResult completion,
            AchievementStageProgressResult stageProgress)
        {
            if (identity.State == AchievementIdentityState.Identified &&
                identity.CandidateIds.Count == 1)
            {
                return completion.State == AchievementCompletionState.CompletedClaimed ||
                    completion.State == AchievementCompletionState.CompletedUnclaimed;
            }

            return identity.State == AchievementIdentityState.StageGroupIdentified &&
                stageProgress?.State == AchievementStageProgressState.Resolved;
        }

        private static AchievementRowFingerprint CreateFingerprint(
            AchievementIdentityResult identity,
            AchievementListRowGeometry geometry)
        {
            if (identity.State == AchievementIdentityState.Identified ||
                identity.State == AchievementIdentityState.StageGroupIdentified)
            {
                return new AchievementRowFingerprint(
                    "canonical:" + string.Join(",", identity.CandidateIds.OrderBy(id => id)),
                    canonical: true);
            }

            return new AchievementRowFingerprint(
                $"diagnostic:{identity.NormalizedTitle}@{geometry.NormalizedBounds.Y:0.000}",
                canonical: false);
        }
    }
}

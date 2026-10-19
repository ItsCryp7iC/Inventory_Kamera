using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace InventoryKamera
{
    internal sealed class AchievementResumeSeed
    {
        internal AchievementResumeSeed(IEnumerable<int> trustedCompletedIds)
        {
            if (trustedCompletedIds == null)
                throw new ArgumentNullException(nameof(trustedCompletedIds));
            TrustedCompletedIds = new ReadOnlyCollection<int>(
                trustedCompletedIds.Distinct().OrderBy(id => id).ToArray());
        }

        internal IReadOnlyList<int> TrustedCompletedIds { get; }
    }

    internal enum AchievementSkippedTargetReason
    {
        PreviouslyTrustedComplete,
    }

    internal sealed class AchievementSkippedTarget
    {
        internal AchievementSkippedTarget(AchievementScanTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            CanonicalTitle = target.CanonicalTitle;
            RequestedCanonicalId = target.RequestedCanonicalId;
            CandidateIds = new ReadOnlyCollection<int>(target.CandidateIds.ToArray());
            Reason = AchievementSkippedTargetReason.PreviouslyTrustedComplete;
        }

        internal string CanonicalTitle { get; }
        internal int RequestedCanonicalId { get; }
        internal IReadOnlyList<int> CandidateIds { get; }
        internal AchievementSkippedTargetReason Reason { get; }
    }

    internal sealed class AchievementUpdatePlan
    {
        private AchievementUpdatePlan(
            IEnumerable<int> trustedCompletedIds,
            IEnumerable<AchievementSkippedTarget> skippedTargets,
            IEnumerable<AchievementScanTarget> scanTargets)
        {
            TrustedCompletedIds = new ReadOnlyCollection<int>(trustedCompletedIds.ToArray());
            SkippedTargets = new ReadOnlyCollection<AchievementSkippedTarget>(skippedTargets.ToArray());
            ScanTargets = new ReadOnlyCollection<AchievementScanTarget>(scanTargets.ToArray());
        }

        internal IReadOnlyList<int> TrustedCompletedIds { get; }
        internal IReadOnlyList<AchievementSkippedTarget> SkippedTargets { get; }
        internal IReadOnlyList<AchievementScanTarget> ScanTargets { get; }

        internal static bool TryCreate(
            AchievementCatalog catalog,
            AchievementResumeSeed seed,
            out AchievementUpdatePlan plan,
            out IReadOnlyList<int> invalidIds,
            out string failureReason)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            plan = null;
            if (seed == null)
            {
                invalidIds = Array.Empty<int>();
                failureReason = "No trusted achievement resume seed was supplied.";
                return false;
            }

            int[] invalid = seed.TrustedCompletedIds
                .Where(id => id <= 0 || catalog.GetById(id) == null)
                .ToArray();
            invalidIds = Array.AsReadOnly(invalid);
            if (invalid.Length > 0)
            {
                failureReason = "The trusted completion seed contains noncanonical achievement IDs.";
                return false;
            }

            var trusted = new HashSet<int>(seed.TrustedCompletedIds);
            var skipped = new List<AchievementSkippedTarget>();
            var scan = new List<AchievementScanTarget>();
            foreach (AchievementScanTarget target in AchievementScanTarget.FromCatalog(catalog))
            {
                if (target.CandidateIds.All(trusted.Contains))
                    skipped.Add(new AchievementSkippedTarget(target));
                else
                    scan.Add(target);
            }

            plan = new AchievementUpdatePlan(seed.TrustedCompletedIds, skipped, scan);
            failureReason = null;
            return true;
        }
    }

    internal interface IAchievementUpdateScanProgressReporter
    {
        void Report(AchievementUpdateScanProgress progress);
    }

    internal sealed class AchievementUpdateScanProgress
    {
        internal AchievementUpdateScanProgress(
            int totalCatalogTargets,
            int skippedTrustedTargets,
            int plannedScanTargets,
            AchievementCatalogScanProgress scanProgress,
            int newlyVerifiedCompletedCount)
        {
            TotalCatalogTargets = totalCatalogTargets;
            SkippedTrustedTargets = skippedTrustedTargets;
            PlannedScanTargets = plannedScanTargets;
            ProcessedScanTargets = scanProgress.CurrentIndex;
            CurrentTitle = scanProgress.CurrentTitle;
            CurrentRequestedId = scanProgress.CurrentRequestedId;
            NewlyVerifiedCompletedCount = newlyVerifiedCompletedCount;
            CompletedCount = scanProgress.CompletedCount;
            IncompleteCount = scanProgress.IncompleteCount;
            UnresolvedCount = scanProgress.UnresolvedCount;
            AmbiguousCount = scanProgress.AmbiguousCount;
            PendingClaimCount = scanProgress.PendingClaimCount;
            FailedCount = scanProgress.FailedCount;
        }

        internal int TotalCatalogTargets { get; }
        internal int SkippedTrustedTargets { get; }
        internal int PlannedScanTargets { get; }
        internal int ProcessedScanTargets { get; }
        internal string CurrentTitle { get; }
        internal int? CurrentRequestedId { get; }
        internal int NewlyVerifiedCompletedCount { get; }
        internal int CompletedCount { get; }
        internal int IncompleteCount { get; }
        internal int UnresolvedCount { get; }
        internal int AmbiguousCount { get; }
        internal int PendingClaimCount { get; }
        internal int FailedCount { get; }
    }

    internal sealed class AchievementUpdateScanResult
    {
        internal AchievementUpdateScanResult(
            AchievementCatalogScanState state,
            int totalCatalogTargets,
            AchievementUpdatePlan plan,
            AchievementCatalogScanResult scanResult,
            IEnumerable<int> updatedTrustedCompletedIds,
            IEnumerable<int> invalidIds,
            bool validationSucceeded,
            bool stoppedEarly,
            string stopReason,
            string diagnostics)
        {
            State = state;
            TotalCatalogTargets = totalCatalogTargets;
            SkippedTargets = new ReadOnlyCollection<AchievementSkippedTarget>(
                (plan?.SkippedTargets ?? Array.Empty<AchievementSkippedTarget>()).ToArray());
            SkippedTrustedTargets = SkippedTargets.Count;
            PlannedScanTargets = plan?.ScanTargets.Count ?? 0;
            Results = new ReadOnlyCollection<AchievementSingleScanResult>(
                (scanResult?.Results ?? Array.Empty<AchievementSingleScanResult>()).ToArray());
            ProcessedScanTargets = Results.Count;
            RemainingUnprocessedTargets = Math.Max(0, PlannedScanTargets - ProcessedScanTargets);
            PreviousTrustedCompletedCount = plan?.TrustedCompletedIds.Count ?? 0;
            UpdatedTrustedCompletedIds = new ReadOnlyCollection<int>(
                (updatedTrustedCompletedIds ?? Array.Empty<int>()).OrderBy(id => id).ToArray());
            UpdatedTrustedCompletedCount = UpdatedTrustedCompletedIds.Count;
            NewlyVerifiedCompletedCount = Math.Max(
                0,
                UpdatedTrustedCompletedCount - PreviousTrustedCompletedCount);
            CompletedCount = scanResult?.CompletedCount ?? 0;
            IncompleteCount = scanResult?.IncompleteCount ?? 0;
            UnresolvedCount = scanResult?.UnresolvedCount ?? 0;
            AmbiguousCount = scanResult?.AmbiguousCount ?? 0;
            PendingClaimCount = scanResult?.PendingClaimCount ?? 0;
            FailedCount = scanResult?.FailedCount ?? 0;
            CancelledCount = scanResult?.CancelledCount ?? 0;
            InvalidIds = new ReadOnlyCollection<int>(
                (invalidIds ?? Array.Empty<int>()).Distinct().OrderBy(id => id).ToArray());
            ValidationSucceeded = validationSucceeded;
            StoppedEarly = stoppedEarly;
            StopReason = stopReason ?? string.Empty;
            Diagnostics = diagnostics ?? string.Empty;
        }

        internal AchievementCatalogScanState State { get; }
        internal int TotalCatalogTargets { get; }
        internal int SkippedTrustedTargets { get; }
        internal int PlannedScanTargets { get; }
        internal int ProcessedScanTargets { get; }
        internal int RemainingUnprocessedTargets { get; }
        internal int PreviousTrustedCompletedCount { get; }
        internal int UpdatedTrustedCompletedCount { get; }
        internal int NewlyVerifiedCompletedCount { get; }
        internal int CompletedCount { get; }
        internal int IncompleteCount { get; }
        internal int UnresolvedCount { get; }
        internal int AmbiguousCount { get; }
        internal int PendingClaimCount { get; }
        internal int FailedCount { get; }
        internal int CancelledCount { get; }
        internal IReadOnlyList<AchievementSingleScanResult> Results { get; }
        internal IReadOnlyList<AchievementSkippedTarget> SkippedTargets { get; }
        internal IReadOnlyList<int> UpdatedTrustedCompletedIds { get; }
        internal IReadOnlyList<int> InvalidIds { get; }
        internal bool ValidationSucceeded { get; }
        internal bool StoppedEarly { get; }
        internal string StopReason { get; }
        internal string Diagnostics { get; }
    }

    internal sealed class AchievementUpdateScanner
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly AchievementCatalog catalog;
        private readonly AchievementCatalogScanner catalogScanner;
        private readonly int totalCatalogTargets;

        internal AchievementUpdateScanner(
            IAchievementCatalogProvider catalogProvider,
            IAchievementSingleScanner singleScanner)
        {
            if (catalogProvider == null) throw new ArgumentNullException(nameof(catalogProvider));
            catalog = catalogProvider.Catalog ?? throw new ArgumentException(
                "The catalog provider returned no catalog.", nameof(catalogProvider));
            catalogScanner = new AchievementCatalogScanner(catalogProvider, singleScanner);
            totalCatalogTargets = catalogScanner.Targets.Count;
        }

        internal AchievementUpdateScanResult Scan(
            AchievementResumeSeed seed,
            CancellationToken cancellationToken,
            IAchievementUpdateScanProgressReporter progress = null)
        {
            if (!AchievementUpdatePlan.TryCreate(
                catalog,
                seed,
                out AchievementUpdatePlan plan,
                out IReadOnlyList<int> invalidIds,
                out string validationFailure))
            {
                return FinishValidationFailure(invalidIds, validationFailure);
            }

            var updatedIds = new HashSet<int>(plan.TrustedCompletedIds);
            if (cancellationToken.IsCancellationRequested)
                return Finish(
                    AchievementCatalogScanState.Cancelled,
                    plan,
                    scanResult: null,
                    updatedIds,
                    invalidIds,
                    stoppedEarly: plan.ScanTargets.Count > 0,
                    "Achievement update scanning was cancelled before the first planned target.");

            var adapter = new UpdateProgressAdapter(
                totalCatalogTargets,
                plan,
                updatedIds,
                progress);
            AchievementCatalogScanResult scanResult = catalogScanner.ScanTargets(
                plan.ScanTargets,
                cancellationToken,
                adapter);

            MergeVerifiedUniqueCompletions(plan.ScanTargets, scanResult.Results, updatedIds);
            return Finish(
                scanResult.State,
                plan,
                scanResult,
                updatedIds,
                invalidIds,
                scanResult.StoppedEarly,
                scanResult.StopReason);
        }

        private AchievementUpdateScanResult FinishValidationFailure(
            IReadOnlyList<int> invalidIds,
            string reason)
        {
            string boundedReason = Bound(reason, 240);
            string diagnostics = BuildDiagnostics(
                AchievementCatalogScanState.Failed,
                totalCatalogTargets,
                plan: null,
                scanResult: null,
                updatedCount: 0,
                newlyVerifiedCount: 0,
                invalidIds,
                validationSucceeded: false,
                stoppedEarly: true,
                boundedReason);
            Logger.Warn(diagnostics);
            return new AchievementUpdateScanResult(
                AchievementCatalogScanState.Failed,
                totalCatalogTargets,
                plan: null,
                scanResult: null,
                updatedTrustedCompletedIds: Array.Empty<int>(),
                invalidIds,
                validationSucceeded: false,
                stoppedEarly: true,
                boundedReason,
                diagnostics);
        }

        private AchievementUpdateScanResult Finish(
            AchievementCatalogScanState state,
            AchievementUpdatePlan plan,
            AchievementCatalogScanResult scanResult,
            IReadOnlyCollection<int> updatedIds,
            IReadOnlyList<int> invalidIds,
            bool stoppedEarly,
            string reason)
        {
            string boundedReason = Bound(reason, 240);
            int newlyVerified = Math.Max(0, updatedIds.Count - plan.TrustedCompletedIds.Count);
            string diagnostics = BuildDiagnostics(
                state,
                totalCatalogTargets,
                plan,
                scanResult,
                updatedIds.Count,
                newlyVerified,
                invalidIds,
                validationSucceeded: true,
                stoppedEarly,
                boundedReason);
            if (state == AchievementCatalogScanState.Completed)
                Logger.Info(diagnostics);
            else
                Logger.Warn(diagnostics);
            return new AchievementUpdateScanResult(
                state,
                totalCatalogTargets,
                plan,
                scanResult,
                updatedIds,
                invalidIds,
                validationSucceeded: true,
                stoppedEarly,
                boundedReason,
                diagnostics);
        }

        private static void MergeVerifiedUniqueCompletions(
            IReadOnlyList<AchievementScanTarget> targets,
            IReadOnlyList<AchievementSingleScanResult> results,
            ISet<int> updatedIds)
        {
            for (int index = 0; index < results.Count && index < targets.Count; index++)
            {
                AchievementScanTarget target = targets[index];
                if (results[index].State == AchievementSingleScanState.Completed &&
                    target.TitleGroupKind == AchievementTitleGroupKind.Unique &&
                    target.CandidateIds.Count == 1)
                {
                    updatedIds.Add(target.CandidateIds[0]);
                }
            }
        }

        private static string BuildDiagnostics(
            AchievementCatalogScanState state,
            int totalCatalogTargets,
            AchievementUpdatePlan plan,
            AchievementCatalogScanResult scanResult,
            int updatedCount,
            int newlyVerifiedCount,
            IReadOnlyList<int> invalidIds,
            bool validationSucceeded,
            bool stoppedEarly,
            string stopReason)
        {
            var builder = new StringBuilder(900);
            builder.AppendLine("operation=achievement-update-scan");
            builder.AppendLine($"catalogTargets={totalCatalogTargets.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"previousTrustedIds={(plan?.TrustedCompletedIds.Count ?? 0).ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"skippedTrustedTargets={(plan?.SkippedTargets.Count ?? 0).ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"plannedScanTargets={(plan?.ScanTargets.Count ?? 0).ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"processedScanTargets={(scanResult?.ProcessedTargets ?? 0).ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"newlyVerifiedUniqueCompleted={newlyVerifiedCount.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"updatedTrustedIds={updatedCount.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"completed={(scanResult?.CompletedCount ?? 0).ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"incomplete={(scanResult?.IncompleteCount ?? 0).ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"unresolved={(scanResult?.UnresolvedCount ?? 0).ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"ambiguous={(scanResult?.AmbiguousCount ?? 0).ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"pendingClaim={(scanResult?.PendingClaimCount ?? 0).ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"failed={(scanResult?.FailedCount ?? 0).ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"cancelled={(scanResult?.CancelledCount ?? 0).ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"validationSucceeded={validationSucceeded}");
            builder.AppendLine($"invalidIds={FormatIds(invalidIds, 12)}");
            builder.AppendLine($"stoppedEarly={stoppedEarly}");
            builder.AppendLine($"state={state}");
            builder.AppendLine($"stopReason={stopReason}");
            return builder.ToString().TrimEnd();
        }

        private static string FormatIds(IEnumerable<int> ids, int maximum)
        {
            int[] values = (ids ?? Array.Empty<int>()).Take(maximum + 1).ToArray();
            return values.Length <= maximum
                ? string.Join(",", values)
                : string.Join(",", values.Take(maximum)) + ",...";
        }

        private static string Bound(string value, int maximumLength)
        {
            string cleaned = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
            return cleaned.Length <= maximumLength
                ? cleaned
                : cleaned.Substring(0, maximumLength) + "...";
        }

        private sealed class UpdateProgressAdapter : IAchievementCatalogScanProgressReporter
        {
            private readonly int totalCatalogTargets;
            private readonly AchievementUpdatePlan plan;
            private readonly ISet<int> updatedIds;
            private readonly IAchievementUpdateScanProgressReporter progress;
            private int lastCompletedCount;

            internal UpdateProgressAdapter(
                int totalCatalogTargets,
                AchievementUpdatePlan plan,
                ISet<int> updatedIds,
                IAchievementUpdateScanProgressReporter progress)
            {
                this.totalCatalogTargets = totalCatalogTargets;
                this.plan = plan;
                this.updatedIds = updatedIds;
                this.progress = progress;
            }

            public void Report(AchievementCatalogScanProgress value)
            {
                if (value.CurrentIndex > 0 && value.CompletedCount > lastCompletedCount)
                {
                    AchievementScanTarget target = plan.ScanTargets[value.CurrentIndex - 1];
                    if (target.TitleGroupKind == AchievementTitleGroupKind.Unique &&
                        target.CandidateIds.Count == 1)
                    {
                        updatedIds.Add(target.CandidateIds[0]);
                    }
                }
                lastCompletedCount = value.CompletedCount;

                progress?.Report(new AchievementUpdateScanProgress(
                    totalCatalogTargets,
                    plan.SkippedTargets.Count,
                    plan.ScanTargets.Count,
                    value,
                    Math.Max(0, updatedIds.Count - plan.TrustedCompletedIds.Count)));
            }
        }
    }
}

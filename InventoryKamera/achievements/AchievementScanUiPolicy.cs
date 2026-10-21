using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace InventoryKamera
{
    internal enum ScanCancellationHotkeyMode
    {
        LegacyInventory,
        Achievement,
    }

    internal static class ScanCancellationHotkeyPolicy
    {
        internal const string RegistrationName = "Stop";

        internal static Keys KeyFor(ScanCancellationHotkeyMode mode) => mode switch
        {
            ScanCancellationHotkeyMode.LegacyInventory => Keys.Enter,
            ScanCancellationHotkeyMode.Achievement => Keys.F8,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        internal static string DisplayNameFor(ScanCancellationHotkeyMode mode) =>
            KeyFor(mode) == Keys.F8 ? "F8" : "ENTER";

        internal static bool IsCancellationKey(ScanCancellationHotkeyMode mode, Keys key) =>
            KeyFor(mode) == key;
    }

    internal sealed class AchievementScanSelection
    {
        internal AchievementScanSelection(
            bool achievements,
            bool weapons,
            bool artifacts,
            bool characters,
            bool characterDevelopmentItems,
            bool materials)
        {
            Achievements = achievements;
            Weapons = weapons;
            Artifacts = artifacts;
            Characters = characters;
            CharacterDevelopmentItems = characterDevelopmentItems;
            Materials = materials;
        }

        internal bool Achievements { get; }
        internal bool Weapons { get; }
        internal bool Artifacts { get; }
        internal bool Characters { get; }
        internal bool CharacterDevelopmentItems { get; }
        internal bool Materials { get; }
        internal bool HasLegacySelection => Weapons || Artifacts || Characters ||
            CharacterDevelopmentItems || Materials;
        internal bool LegacyChoicesEnabled => !Achievements;
        internal bool AchievementChoiceEnabled => !HasLegacySelection;
        internal bool AchievementImportEnabled => Achievements;
    }

    internal static class AchievementScanSelectionPolicy
    {
        internal static AchievementScanSelection SelectAchievements(bool selected) =>
            selected
                ? new AchievementScanSelection(true, false, false, false, false, false)
                : new AchievementScanSelection(false, false, false, false, false, false);

        internal static AchievementScanSelection SelectLegacy(
            bool weapons,
            bool artifacts,
            bool characters,
            bool characterDevelopmentItems,
            bool materials) =>
            new(
                achievements: false,
                weapons,
                artifacts,
                characters,
                characterDevelopmentItems,
                materials);

        internal static bool IsValid(AchievementScanSelection selection) =>
            selection != null && !(selection.Achievements && selection.HasLegacySelection);
    }

    internal static class AchievementScanStartPolicy
    {
        internal static bool ShouldShowAchievementWarning(bool achievementsSelected) =>
            achievementsSelected;

        internal static bool IsAchievementOnlySelection(
            bool achievements,
            bool weapons,
            bool artifacts,
            bool characters,
            bool characterDevelopmentItems,
            bool materials) =>
            achievements && !weapons && !artifacts && !characters &&
            !characterDevelopmentItems && !materials;
    }

    internal sealed class AchievementResumeDataState
    {
        private readonly AchievementToolkitImporter importer;

        internal AchievementResumeDataState(AchievementToolkitImporter importer)
        {
            this.importer = importer ?? throw new ArgumentNullException(nameof(importer));
        }

        internal AchievementResumeSeed ResumeSeed { get; private set; }
        internal AchievementImportResult LastImportResult { get; private set; }
        internal string SourceFileName { get; private set; } = string.Empty;
        internal string BlockingError { get; private set; } = string.Empty;
        internal bool HasResumeSeed => ResumeSeed != null;
        internal bool HasBlockingError => BlockingError.Length > 0;
        internal int CompletedCount => ResumeSeed?.TrustedCompletedIds.Count ?? 0;

        internal AchievementImportResult Import(string json, string sourceFileName)
        {
            AchievementImportResult result = importer.Import(json);
            LastImportResult = result;
            if (result.Success)
            {
                ResumeSeed = result.ResumeSeed;
                SourceFileName = sourceFileName ?? string.Empty;
                BlockingError = string.Empty;
            }
            else
            {
                ResumeSeed = null;
                SourceFileName = string.Empty;
                BlockingError = result.FailureReason.Length > 0
                    ? result.FailureReason
                    : "The selected achievement progress file could not be used safely.";
            }
            return result;
        }

        internal void SetFileReadFailure(string reason)
        {
            ResumeSeed = null;
            LastImportResult = null;
            SourceFileName = string.Empty;
            BlockingError = string.IsNullOrWhiteSpace(reason)
                ? "The selected achievement progress file could not be read."
                : reason.Trim();
        }

        internal void Clear()
        {
            ResumeSeed = null;
            LastImportResult = null;
            SourceFileName = string.Empty;
            BlockingError = string.Empty;
        }

        internal string StatusText => HasResumeSeed
            ? string.Format(
                CultureInfo.InvariantCulture,
                "Resume data loaded: {0:N0} completed",
                CompletedCount)
            : HasBlockingError
                ? "Resume data invalid - clear or import another file"
                : "No achievement resume data loaded";
    }

    internal sealed class AchievementScanReportItem
    {
        internal AchievementScanReportItem(string title, string status)
        {
            Title = title ?? string.Empty;
            Status = status ?? string.Empty;
        }

        internal string Title { get; }
        internal string Status { get; }
    }

    internal static class AchievementScanReportBuilder
    {
        internal static string BuildProgress(AchievementInteractiveScanProgress progress)
        {
            if (progress == null) return string.Empty;
            string primary = string.Format(
                CultureInfo.InvariantCulture,
                "{0:N0} / {1:N0} checked",
                progress.ProcessedScanTargets,
                progress.PlannedScanTargets);
            if (progress.SkippedTrustedTargets > 0)
            {
                primary += string.Format(
                    CultureInfo.InvariantCulture,
                    " ({0:N0} previously complete)",
                    progress.SkippedTrustedTargets);
            }
            return primary;
        }

        internal static string BuildProgressDetails(AchievementInteractiveScanProgress progress)
        {
            if (progress == null) return string.Empty;
            return string.Format(
                CultureInfo.InvariantCulture,
                "Completed {0:N0}   Incomplete {1:N0}\nUnresolved {2:N0}   Pending/skipped {3:N0}",
                progress.CompletedCount,
                progress.IncompleteCount,
                progress.UnresolvedCount + progress.AmbiguousCount,
                Math.Max(progress.PendingClaimCount, progress.UserSkippedCount));
        }

        internal static string BuildSummary(AchievementInteractiveScanResult result)
        {
            if (result == null) return "No achievement scan result is available.";
            var builder = new StringBuilder(480);
            builder.AppendLine(result.State switch
            {
                AchievementCatalogScanState.Completed => "Achievement scan finished.",
                AchievementCatalogScanState.Cancelled => "Achievement scan was cancelled.",
                AchievementCatalogScanState.StoppedUnsafe => "Achievement scan stopped because the screen was not safely reset.",
                _ => "Achievement scan stopped after an operational failure.",
            });
            builder.AppendLine();
            builder.AppendLine($"Checked: {result.ProcessedScanTargets.ToString("N0", CultureInfo.InvariantCulture)} / {result.PlannedScanTargets.ToString("N0", CultureInfo.InvariantCulture)}");
            if (result.SkippedTrustedTargets > 0)
                builder.AppendLine($"Skipped from previous progress: {result.SkippedTrustedTargets.ToString("N0", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"Completed results: {result.CompletedCount.ToString("N0", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"Incomplete results: {result.IncompleteCount.ToString("N0", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"Unresolved/ambiguous: {(result.UnresolvedCount + result.AmbiguousCount).ToString("N0", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"Pending/skipped: {Math.Max(result.PendingClaimCount, result.UserSkippedTargets.Count).ToString("N0", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"Newly trusted completed IDs: {result.NewlyTrustedCompletedCount.ToString("N0", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"Trusted completed total: {result.TrustedCompletedIds.Count.ToString("N0", CultureInfo.InvariantCulture)}");
            if (result.TimingSummary.CompletedTargetCount > 0)
            {
                builder.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "Target timing (mean / median / P95): {0:0} / {1:0} / {2:0} ms",
                    result.TimingSummary.MeanTotalMilliseconds,
                    result.TimingSummary.MedianTotalMilliseconds,
                    result.TimingSummary.P95TotalMilliseconds));
            }
            if (result.StopReason.Length > 0 && result.State != AchievementCatalogScanState.Completed)
                builder.AppendLine($"Reason: {result.StopReason}");
            return builder.ToString().TrimEnd();
        }

        internal static IReadOnlyList<AchievementScanReportItem> GetIssues(
            AchievementInteractiveScanResult result)
        {
            if (result == null) return Array.Empty<AchievementScanReportItem>();
            var issues = new List<AchievementScanReportItem>();
            issues.AddRange(result.UserSkippedTargets.Select(skipped =>
                new AchievementScanReportItem(
                    skipped.CanonicalTitle,
                    skipped.Reason == AchievementUserSkippedReason.PendingUnclaimedReward
                        ? "skipped: unclaimed reward"
                        : "skipped by user")));

            foreach (AchievementSingleScanResult scan in result.Results)
            {
                string status = scan.State switch
                {
                    AchievementSingleScanState.AmbiguousCollision => "unresolved: title collision",
                    AchievementSingleScanState.AmbiguousMultiMatch => "unresolved: multiple matches",
                    AchievementSingleScanState.Unresolved when
                        scan.StageProgressResult?.State == AchievementStageProgressState.AmbiguousReward =>
                        "unresolved: stage reward ambiguity",
                    AchievementSingleScanState.Unresolved => UnresolvedStatus(scan),
                    AchievementSingleScanState.PendingClaimRequiresUserAction =>
                        "paused: unclaimed reward",
                    AchievementSingleScanState.Failed => "failed: operational error",
                    _ => null,
                };
                if (status != null)
                    issues.Add(new AchievementScanReportItem(scan.CanonicalTitle, status));
            }
            return new ReadOnlyCollection<AchievementScanReportItem>(issues);
        }

        private static string UnresolvedStatus(AchievementSingleScanResult scan)
        {
            AchievementIdentityResult identity = scan.IdentityResult;
            if (identity == null)
                return "unresolved: could not identify safely";
            if (identity.State == AchievementIdentityState.Ambiguous)
                return "unresolved: multiple possible canonical titles";
            if (identity.State == AchievementIdentityState.Identified ||
                identity.State == AchievementIdentityState.StageGroupIdentified)
                return "unresolved: completion state uncertain";
            if (identity.Attempts.Count == 0 ||
                identity.Attempts.All(attempt => attempt.OcrAttempt.NormalizedText.Length == 0) ||
                identity.Reason.Contains("clipped", StringComparison.OrdinalIgnoreCase))
                return "unresolved: title clipped/unreadable";
            return "unresolved: title OCR uncertain";
        }

        internal static string BuildIssueReport(AchievementInteractiveScanResult result)
        {
            IReadOnlyList<AchievementScanReportItem> issues = GetIssues(result);
            return issues.Count == 0
                ? "No skipped or unresolved achievements."
                : string.Join(Environment.NewLine, issues.Select(item => $"{item.Title} - {item.Status}"));
        }
    }
}

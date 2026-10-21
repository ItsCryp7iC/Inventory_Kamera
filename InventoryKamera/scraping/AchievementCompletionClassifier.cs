using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
    internal interface IAchievementCompletionClassifier
    {
        AchievementCompletionResult Classify(
            AchievementIdentityResult identity,
            Bitmap screenshot);
    }

    internal interface IAchievementSharedScreenCompletionClassifier
    {
        AchievementCompletionResult Classify(
            AchievementIdentityResult identity,
            Bitmap screenshot,
            AchievementScreenDetection screenDetection);
    }

    internal enum AchievementCompletionState
    {
        CompletedUnclaimed,
        CompletedClaimed,
        Incomplete,
        Unresolved,
    }

    internal sealed class AchievementCompletionResult
    {
        internal AchievementCompletionResult(
            AchievementCompletionState state,
            AchievementIdentityResult identity,
            AchievementScreenDetection screenDetection,
            bool claimDetected,
            bool completedDetected,
            string claimEvidence,
            string completedEvidence,
            string progressRaw,
            int? progressCurrent,
            int? progressTarget,
            string dateRaw,
            string rawText,
            float ocrConfidence,
            IEnumerable<AchievementCompletionOcrAttempt> attempts,
            string reason,
            string diagnostics)
        {
            State = state;
            Identity = identity;
            ScreenDetection = screenDetection;
            ClaimDetected = claimDetected;
            CompletedDetected = completedDetected;
            ClaimEvidence = claimEvidence ?? string.Empty;
            CompletedEvidence = completedEvidence ?? string.Empty;
            ProgressRaw = progressRaw ?? string.Empty;
            ProgressCurrent = progressCurrent;
            ProgressTarget = progressTarget;
            DateRaw = dateRaw ?? string.Empty;
            RawText = rawText ?? string.Empty;
            OcrConfidence = ocrConfidence;
            Attempts = new ReadOnlyCollection<AchievementCompletionOcrAttempt>(
                (attempts ?? Array.Empty<AchievementCompletionOcrAttempt>()).ToArray());
            Reason = reason ?? string.Empty;
            Diagnostics = diagnostics ?? string.Empty;
        }

        internal AchievementCompletionState State { get; }
        internal AchievementIdentityResult Identity { get; }
        internal AchievementScreenDetection ScreenDetection { get; }
        internal bool ClaimDetected { get; }
        internal bool CompletedDetected { get; }
        internal string ClaimEvidence { get; }
        internal string CompletedEvidence { get; }
        internal string ProgressRaw { get; }
        internal int? ProgressCurrent { get; }
        internal int? ProgressTarget { get; }
        internal string DateRaw { get; }
        internal string RawText { get; }
        internal float OcrConfidence { get; }
        internal IReadOnlyList<AchievementCompletionOcrAttempt> Attempts { get; }
        internal string Reason { get; }
        internal string Diagnostics { get; }
    }

    /// <summary>
    /// Read-only Phase 6E classifier. It consumes a positive Phase 6D identity and fresh
    /// SearchActive screen evidence, then classifies only explicit row-level semantic evidence.
    /// It never infers completion from absence and never selects a staged canonical ID.
    /// </summary>
    internal sealed class AchievementCompletionClassifier :
        IAchievementCompletionClassifier,
        IAchievementSharedScreenCompletionClassifier
    {
        // This is the established semantic OCR floor used by the Phase 6A screen detector and
        // Phase 6D title verifier. Exact whole-token/fraction parsing remains mandatory above it.
        internal const float MinimumEvidenceConfidence = 0.35f;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IAchievementScreenDetector screenDetector;
        private readonly IAchievementCompletionReader completionReader;

        internal AchievementCompletionClassifier(
            IAchievementScreenDetector screenDetector,
            IAchievementCompletionReader completionReader)
        {
            this.screenDetector = screenDetector ?? throw new ArgumentNullException(nameof(screenDetector));
            this.completionReader = completionReader ?? throw new ArgumentNullException(nameof(completionReader));
        }

        public AchievementCompletionResult Classify(
            AchievementIdentityResult identity,
            Bitmap screenshot) => Classify(identity, screenshot, null);

        public AchievementCompletionResult Classify(
            AchievementIdentityResult identity,
            Bitmap screenshot,
            AchievementScreenDetection screenDetection)
        {
            if (identity == null)
                return Finish(
                    AchievementCompletionState.Unresolved,
                    null,
                    null,
                    Array.Empty<AchievementCompletionOcrAttempt>(),
                    "A positive Phase 6D identity was not supplied.");

            if (identity.State != AchievementIdentityState.Identified &&
                identity.State != AchievementIdentityState.StageGroupIdentified)
            {
                return Finish(
                    AchievementCompletionState.Unresolved,
                    identity,
                    null,
                    Array.Empty<AchievementCompletionOcrAttempt>(),
                    $"Identity state {identity.State} is not eligible for completion classification.");
            }

            if (screenshot == null)
                return Finish(
                    AchievementCompletionState.Unresolved,
                    identity,
                    null,
                    Array.Empty<AchievementCompletionOcrAttempt>(),
                    "The current full capture was not supplied.");

            AchievementScreenDetection screen = screenDetection;
            if (screen == null)
            {
                try
                {
                    screen = screenDetector.Detect(screenshot);
                }
                catch (Exception ex)
                {
                    return Finish(
                        AchievementCompletionState.Unresolved,
                        identity,
                        null,
                        Array.Empty<AchievementCompletionOcrAttempt>(),
                        $"Current-screen detection failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            bool corroboratedUnknownSearchResult =
                screen.ScreenState == AchievementScreenState.Unknown &&
                identity.UsedCorroboratedSearchResult &&
                HasSingleResultPageMarkers(screen) &&
                HasSingleResultPageMarkers(identity.ScreenDetection);
            if (screen.ScreenState != AchievementScreenState.SearchActive &&
                !corroboratedUnknownSearchResult)
                return Finish(
                    AchievementCompletionState.Unresolved,
                    identity,
                    screen,
                    Array.Empty<AchievementCompletionOcrAttempt>(),
                    $"Current screen is not positively SearchActive; observed {screen.ScreenState}.");

            if (!screen.MatchCountAccepted || screen.MatchCountValue != 1)
                return Finish(
                    AchievementCompletionState.Unresolved,
                    identity,
                    screen,
                    Array.Empty<AchievementCompletionOcrAttempt>(),
                    "Completion classification requires exactly one positively verified match.");

            AchievementCompletionRead read;
            try
            {
                read = completionReader is IAchievementLazyCompletionReader lazyReader
                    ? lazyReader.Read(screenshot, CanStopAfterVariant)
                    : completionReader.Read(screenshot);
            }
            catch (Exception ex)
            {
                return Finish(
                    AchievementCompletionState.Unresolved,
                    identity,
                    screen,
                    Array.Empty<AchievementCompletionOcrAttempt>(),
                    $"Completion-state OCR failed: {ex.GetType().Name}: {ex.Message}");
            }

            AchievementCompletionOcrAttempt[] attempts = read?.Attempts?.ToArray() ??
                Array.Empty<AchievementCompletionOcrAttempt>();
            if (attempts.Length == 0 || attempts.All(attempt =>
                string.IsNullOrWhiteSpace(attempt.RawText)))
            {
                return Finish(
                    AchievementCompletionState.Unresolved,
                    identity,
                    screen,
                    attempts,
                    "Completion-state OCR returned no usable text.");
            }

            AchievementCompletionOcrAttempt[] labelAttempts = attempts
                .Where(attempt => attempt.Scope == AchievementCompletionOcrScope.StateLabel)
                .ToArray();
            bool labelHasSemanticToken = labelAttempts.Any(attempt =>
                attempt.ClaimTokenDetected || attempt.CompletedTokenDetected);
            AchievementCompletionOcrAttempt[] semanticAttempts = labelHasSemanticToken
                ? labelAttempts
                : attempts;

            bool claimObserved = semanticAttempts.Any(attempt => attempt.ClaimTokenDetected);
            bool completedObserved = semanticAttempts.Any(attempt => attempt.CompletedTokenDetected);
            bool claimAccepted = semanticAttempts.Any(attempt =>
                attempt.ClaimTokenDetected && attempt.Confidence >= MinimumEvidenceConfidence);
            bool completedAccepted = semanticAttempts.Any(attempt =>
                attempt.CompletedTokenDetected && attempt.Confidence >= MinimumEvidenceConfidence);

            // Fractions from the broad completion crop are not progress evidence. Live logs show
            // that completion dates and reward decoration in that crop are routinely OCR'd as
            // contradictory fractions. Progress is accepted only from the dedicated label band.
            AchievementCompletionOcrAttempt[] parsedProgress = labelAttempts
                .Where(attempt => attempt.Progress.Accepted)
                .ToArray();
            var distinctProgress = parsedProgress
                .Select(attempt => (attempt.Progress.Current.Value, attempt.Progress.Target.Value))
                .Distinct()
                .ToArray();
            bool progressConflict = labelAttempts.Any(attempt => attempt.Progress.MultipleFractions) ||
                distinctProgress.Length > 1;
            bool incompleteObserved = parsedProgress.Any(attempt =>
                attempt.Progress.Current < attempt.Progress.Target);
            bool incompleteAccepted = parsedProgress.Any(attempt =>
                attempt.Progress.Current < attempt.Progress.Target &&
                attempt.Confidence >= MinimumEvidenceConfidence);

            if (progressConflict)
                return Finish(
                    AchievementCompletionState.Unresolved,
                    identity,
                    screen,
                    attempts,
                    "Multiple contradictory or non-unique progress fractions were observed.");

            if (claimObserved && completedObserved)
                return Finish(
                    AchievementCompletionState.Unresolved,
                    identity,
                    screen,
                    attempts,
                    "Claim and Completed evidence conflict.");

            if (incompleteObserved && (claimObserved || completedObserved))
                return Finish(
                    AchievementCompletionState.Unresolved,
                    identity,
                    screen,
                    attempts,
                    "Completion token evidence conflicts with explicit incomplete progress.");

            if (claimAccepted)
                return Finish(
                    AchievementCompletionState.CompletedUnclaimed,
                    identity,
                    screen,
                    attempts,
                    "A positive row-level Claim token was recognized above the confidence threshold.");

            if (completedAccepted)
                return Finish(
                    AchievementCompletionState.CompletedClaimed,
                    identity,
                    screen,
                    attempts,
                    "A positive row-level Completed token was recognized above the confidence threshold.");

            if (incompleteAccepted)
                return Finish(
                    AchievementCompletionState.Incomplete,
                    identity,
                    screen,
                    attempts,
                    "A positive progress fraction with current less than target was recognized.");

            if (claimObserved || completedObserved || incompleteObserved)
                return Finish(
                    AchievementCompletionState.Unresolved,
                    identity,
                    screen,
                    attempts,
                    "Semantic state evidence was present only below the confidence threshold.");

            if (parsedProgress.Any(attempt =>
                attempt.Progress.Current == attempt.Progress.Target))
            {
                return Finish(
                    AchievementCompletionState.Unresolved,
                    identity,
                    screen,
                    attempts,
                    "Equal progress does not independently prove completion.");
            }

            return Finish(
                AchievementCompletionState.Unresolved,
                identity,
                screen,
                attempts,
                "No positive row-level completion-state evidence was recognized.");
        }

        private static bool CanStopAfterVariant(
            IReadOnlyList<AchievementCompletionOcrAttempt> attempts)
        {
            AchievementCompletionOcrAttempt[] labelAttempts = attempts
                .Where(attempt => attempt.Scope == AchievementCompletionOcrScope.StateLabel)
                .ToArray();
            bool claimObserved = labelAttempts.Any(attempt => attempt.ClaimTokenDetected);
            bool completedObserved = labelAttempts.Any(attempt => attempt.CompletedTokenDetected);
            var progress = labelAttempts
                .Where(attempt => attempt.Progress.Accepted)
                .Select(attempt => (attempt.Progress.Current.Value, attempt.Progress.Target.Value))
                .Distinct()
                .ToArray();
            bool incompleteObserved = labelAttempts.Any(attempt =>
                attempt.Progress.Accepted && attempt.Progress.Current < attempt.Progress.Target);
            bool conflict = labelAttempts.Any(attempt => attempt.Progress.MultipleFractions) ||
                progress.Length > 1 ||
                (claimObserved && completedObserved) ||
                (incompleteObserved && (claimObserved || completedObserved));
            if (conflict) return false;

            return labelAttempts.Any(attempt => attempt.Confidence >= MinimumEvidenceConfidence &&
                (attempt.ClaimTokenDetected ||
                 attempt.CompletedTokenDetected ||
                 (attempt.Progress.Accepted && attempt.Progress.Current < attempt.Progress.Target)));
        }

        private static bool HasSingleResultPageMarkers(AchievementScreenDetection screen) =>
            screen?.MatchCountAccepted == true &&
            screen.MatchCountValue == 1 &&
            screen.ResultPageAccepted &&
            screen.ResultPageCurrent == 1 &&
            screen.ResultPageTotal == 1;

        private static AchievementCompletionResult Finish(
            AchievementCompletionState state,
            AchievementIdentityResult identity,
            AchievementScreenDetection screen,
            IEnumerable<AchievementCompletionOcrAttempt> attempts,
            string reason)
        {
            AchievementCompletionOcrAttempt[] attemptArray =
                (attempts ?? Array.Empty<AchievementCompletionOcrAttempt>()).ToArray();
            bool labelHasSemanticToken = attemptArray.Any(attempt =>
                attempt.Scope == AchievementCompletionOcrScope.StateLabel &&
                (attempt.ClaimTokenDetected || attempt.CompletedTokenDetected));
            AchievementCompletionOcrAttempt[] semanticAttempts = labelHasSemanticToken
                ? attemptArray.Where(attempt =>
                    attempt.Scope == AchievementCompletionOcrScope.StateLabel).ToArray()
                : attemptArray;
            AchievementCompletionOcrAttempt claim = Select(
                semanticAttempts, attempt => attempt.ClaimTokenDetected);
            AchievementCompletionOcrAttempt completed = Select(
                semanticAttempts, attempt => attempt.CompletedTokenDetected);
            AchievementCompletionOcrAttempt progress = Select(
                attemptArray,
                attempt => attempt.Scope == AchievementCompletionOcrScope.StateLabel &&
                    attempt.Progress.Accepted);
            AchievementCompletionOcrAttempt date = Select(attemptArray, attempt => attempt.DateRaw.Length > 0);
            AchievementCompletionOcrAttempt selected = state switch
            {
                AchievementCompletionState.CompletedUnclaimed => claim,
                AchievementCompletionState.CompletedClaimed => completed,
                AchievementCompletionState.Incomplete => progress,
                _ => Select(attemptArray, attempt => !string.IsNullOrWhiteSpace(attempt.RawText)),
            };

            bool claimAccepted = claim != null && claim.Confidence >= MinimumEvidenceConfidence;
            bool completedAccepted = completed != null && completed.Confidence >= MinimumEvidenceConfidence;
            string diagnostics = BuildDiagnostics(
                state,
                identity,
                screen,
                claimAccepted,
                completedAccepted,
                progress,
                date,
                selected,
                attemptArray,
                reason);
            if (state == AchievementCompletionState.Unresolved)
                Logger.Warn(diagnostics);
            else
                Logger.Info(diagnostics);

            return new AchievementCompletionResult(
                state,
                identity,
                screen,
                claimAccepted,
                completedAccepted,
                claim?.RawText,
                completed?.RawText,
                progress?.Progress.RawText,
                progress?.Progress.Current,
                progress?.Progress.Target,
                date?.DateRaw,
                selected?.RawText,
                selected?.Confidence ?? 0,
                attemptArray,
                reason,
                diagnostics);
        }

        private static AchievementCompletionOcrAttempt Select(
            IEnumerable<AchievementCompletionOcrAttempt> attempts,
            Func<AchievementCompletionOcrAttempt, bool> predicate) =>
            attempts
                .Where(predicate)
                .OrderByDescending(attempt => attempt.Confidence)
                .FirstOrDefault();

        private static string BuildDiagnostics(
            AchievementCompletionState state,
            AchievementIdentityResult identity,
            AchievementScreenDetection screen,
            bool claimDetected,
            bool completedDetected,
            AchievementCompletionOcrAttempt progress,
            AchievementCompletionOcrAttempt date,
            AchievementCompletionOcrAttempt selected,
            IReadOnlyList<AchievementCompletionOcrAttempt> attempts,
            string reason)
        {
            var builder = new StringBuilder(1800);
            builder.AppendLine("operation=completion-classify");
            builder.AppendLine($"identityState={identity?.State.ToString() ?? "(none)"}");
            builder.AppendLine($"expectedId={Format(identity?.ExpectedId)}");
            builder.AppendLine($"stageGroupId={Format(identity?.StageGroupId)}");
            builder.AppendLine($"screenState={screen?.ScreenState.ToString() ?? "(none)"}");
            builder.AppendLine($"matchCount={Format(screen?.MatchCountValue)}");
            builder.AppendLine($"claimDetected={claimDetected}");
            builder.AppendLine($"completedDetected={completedDetected}");
            builder.AppendLine($"progressRaw={Bound(progress?.Progress.RawText)}");
            builder.AppendLine($"progressCurrent={Format(progress?.Progress.Current)}");
            builder.AppendLine($"progressTarget={Format(progress?.Progress.Target)}");
            builder.AppendLine($"dateRaw={Bound(date?.DateRaw)}");
            builder.AppendLine($"rawText={Bound(selected?.RawText)}");
            builder.AppendLine($"ocrConfidence={Format(selected?.Confidence)}");
            builder.AppendLine($"selectedState={state}");
            builder.AppendLine($"attemptCount={attempts.Count.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"reason={Bound(reason)}");
            for (int i = 0; i < attempts.Count; i++)
            {
                AchievementCompletionOcrAttempt attempt = attempts[i];
                builder.AppendLine(
                    $"attempt[{i.ToString(CultureInfo.InvariantCulture)}]=" +
                    $"scope={attempt.Scope}; variant={attempt.Variant}; raw={Bound(attempt.RawText)}; " +
                    $"normalized={Bound(attempt.NormalizedText)}; " +
                    $"confidence={attempt.Confidence.ToString("0.000", CultureInfo.InvariantCulture)}; " +
                    $"claim={attempt.ClaimTokenDetected}; completed={attempt.CompletedTokenDetected}; " +
                    $"progress={Bound(attempt.Progress.RawText)}; date={Bound(attempt.DateRaw)}; " +
                    $"reason={Bound(attempt.Reason)}");
            }
            return builder.ToString().TrimEnd();
        }

        private static string Bound(string value)
        {
            string cleaned = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
            return cleaned.Length <= 160 ? cleaned : cleaned.Substring(0, 160) + "...";
        }

        private static string Format(int? value) =>
            value?.ToString(CultureInfo.InvariantCulture) ?? "(none)";

        private static string Format(float? value) =>
            value?.ToString("0.000", CultureInfo.InvariantCulture) ?? "(none)";
    }
}

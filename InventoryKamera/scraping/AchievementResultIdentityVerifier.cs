using InventoryKamera.game;
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
    internal interface IAchievementResultIdentityVerifier
    {
        AchievementIdentityResult Verify(
            AchievementSearchNavigationResult searchResult,
            int expectedId,
            Bitmap screenshot);
    }

    internal enum AchievementIdentityState
    {
        Identified,
        StageGroupIdentified,
        Ambiguous,
        Mismatch,
        Unresolved,
    }

    internal sealed class AchievementIdentityOcrAttempt
    {
        internal AchievementIdentityOcrAttempt(
            AchievementTitleOcrAttempt ocrAttempt,
            double score,
            double runnerUpScore,
            double margin,
            string reason)
        {
            OcrAttempt = ocrAttempt ?? throw new ArgumentNullException(nameof(ocrAttempt));
            Score = score;
            RunnerUpScore = runnerUpScore;
            Margin = margin;
            Reason = reason ?? string.Empty;
        }

        internal AchievementTitleOcrAttempt OcrAttempt { get; }
        internal double Score { get; }
        internal double RunnerUpScore { get; }
        internal double Margin { get; }
        internal string Reason { get; }
    }

    internal sealed class AchievementIdentityResult
    {
        internal AchievementIdentityResult(
            AchievementIdentityState state,
            int expectedId,
            string expectedTitle,
            int? identifiedId,
            int? stageGroupId,
            IEnumerable<int> candidateIds,
            string rawTitle,
            string normalizedTitle,
            double score,
            double runnerUpScore,
            double margin,
            float ocrConfidence,
            IEnumerable<AchievementIdentityOcrAttempt> attempts,
            AchievementScreenDetection screenDetection,
            string reason,
            string diagnostics)
        {
            State = state;
            ExpectedId = expectedId;
            ExpectedTitle = expectedTitle ?? string.Empty;
            IdentifiedId = identifiedId;
            StageGroupId = stageGroupId;
            CandidateIds = new ReadOnlyCollection<int>((candidateIds ?? Array.Empty<int>()).ToArray());
            RawTitle = rawTitle ?? string.Empty;
            NormalizedTitle = normalizedTitle ?? string.Empty;
            Score = score;
            RunnerUpScore = runnerUpScore;
            Margin = margin;
            OcrConfidence = ocrConfidence;
            Attempts = new ReadOnlyCollection<AchievementIdentityOcrAttempt>(
                (attempts ?? Array.Empty<AchievementIdentityOcrAttempt>()).ToArray());
            ScreenDetection = screenDetection;
            Reason = reason ?? string.Empty;
            Diagnostics = diagnostics ?? string.Empty;
        }

        internal AchievementIdentityState State { get; }
        internal int ExpectedId { get; }
        internal string ExpectedTitle { get; }
        internal int? IdentifiedId { get; }
        internal int? StageGroupId { get; }
        internal IReadOnlyList<int> CandidateIds { get; }
        internal string RawTitle { get; }
        internal string NormalizedTitle { get; }
        internal double Score { get; }
        internal double RunnerUpScore { get; }
        internal double Margin { get; }
        internal float OcrConfidence { get; }
        internal IReadOnlyList<AchievementIdentityOcrAttempt> Attempts { get; }
        internal AchievementScreenDetection ScreenDetection { get; }
        internal string Reason { get; }
        internal string Diagnostics { get; }
    }

    internal readonly struct AchievementTitleMatch
    {
        internal AchievementTitleMatch(double score, double runnerUpScore)
        {
            Score = score;
            RunnerUpScore = runnerUpScore;
            Margin = score - runnerUpScore;
        }

        internal double Score { get; }
        internal double RunnerUpScore { get; }
        internal double Margin { get; }
    }

    internal static class AchievementTitleMatcher
    {
        // Calibration set: sanitized positive fixtures score 100.0 at confidence 0.84-0.96;
        // a one-character OCR substitution in "You There! Fly!" scores 93.3 and remains accepted;
        // missing both exclamation marks scores 86.7 and is rejected; the deliberately close
        // Blows/Flows runner-up pair scores 92.9/92.9 and is rejected at margin 0.0. Moonmist's
        // nearest relevant catalog runner-up scores 79.2, leaving a measured 20.8 exact-read margin.
        // A live clipped long-title read is allowed only to remain unresolved when its visible
        // prefix scores at least 90.0; it can never identify. The 0.35 confidence floor matches
        // Phase 6A's existing semantic OCR floor.
        internal const double MinimumScore = 90.0;
        internal const double MinimumRunnerUpMargin = 5.0;
        internal const double CandidateRelevanceScore = 70.0;
        internal const double DefiniteMismatchMaximumScore = 60.0;
        internal const double MinimumClippedPrefixScore = 90.0;
        internal const float MinimumOcrConfidence = 0.35f;

        internal static AchievementTitleMatch Match(
            string observedTitle,
            string expectedTitle,
            IEnumerable<string> alternativeTitles)
        {
            string observed = AchievementTitleNormalizer.Normalize(observedTitle);
            string expected = AchievementTitleNormalizer.Normalize(expectedTitle);
            double score = Similarity(observed, expected);
            double runnerUp = (alternativeTitles ?? Array.Empty<string>())
                .Select(AchievementTitleNormalizer.Normalize)
                .Where(title => title.Length > 0 && !string.Equals(title, expected, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Select(title => Similarity(observed, title))
                .DefaultIfEmpty(0.0)
                .Max();
            return new AchievementTitleMatch(score, runnerUp);
        }

        internal static double Similarity(string left, string right)
        {
            left = AchievementTitleNormalizer.Normalize(left);
            right = AchievementTitleNormalizer.Normalize(right);
            if (left.Length == 0 || right.Length == 0) return 0;
            if (string.Equals(left, right, StringComparison.Ordinal)) return 100;

            var previous = new int[right.Length + 1];
            var current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++) previous[j] = j;
            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= right.Length; j++)
                {
                    int substitution = previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1);
                    current[j] = Math.Min(
                        Math.Min(previous[j] + 1, current[j - 1] + 1),
                        substitution);
                }
                (previous, current) = (current, previous);
            }

            return (1.0 - previous[right.Length] /
                (double)Math.Max(left.Length, right.Length)) * 100.0;
        }
    }

    /// <summary>
    /// Read-only Phase 6D verifier. It requires a successful Phase 6C result and fresh positive
    /// screen evidence, then proves the visible title against the supplied canonical ID. It never
    /// interprets completion state or chooses the first catalog/result candidate.
    /// </summary>
    internal sealed class AchievementResultIdentityVerifier : IAchievementResultIdentityVerifier
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly AchievementCatalog catalog;
        private readonly IAchievementScreenDetector screenDetector;
        private readonly IAchievementResultTitleReader titleReader;

        internal AchievementResultIdentityVerifier(
            IAchievementCatalogProvider catalogProvider,
            IAchievementScreenDetector screenDetector,
            IAchievementResultTitleReader titleReader)
        {
            if (catalogProvider == null) throw new ArgumentNullException(nameof(catalogProvider));
            catalog = catalogProvider.Catalog ?? throw new ArgumentException(
                "The catalog provider returned no catalog.", nameof(catalogProvider));
            this.screenDetector = screenDetector ?? throw new ArgumentNullException(nameof(screenDetector));
            this.titleReader = titleReader ?? throw new ArgumentNullException(nameof(titleReader));
        }

        public AchievementIdentityResult Verify(
            AchievementSearchNavigationResult searchResult,
            int expectedId,
            Bitmap screenshot)
        {
            AchievementCatalogEntry expected = catalog.GetById(expectedId);
            if (expected == null)
                return Finish(
                    AchievementIdentityState.Unresolved,
                    expectedId,
                    null,
                    null,
                    null,
                    Array.Empty<int>(),
                    null,
                    null,
                    "The expected canonical achievement ID was not found.");

            AchievementTitleGroup titleGroup = catalog.GetTitleGroupForId(expected.Id);
            int[] candidateIds = titleGroup?.CandidateIds.ToArray() ?? Array.Empty<int>();
            if (searchResult == null || !searchResult.Succeeded ||
                searchResult.AfterDetection?.ScreenState != AchievementScreenState.SearchActive)
            {
                return Finish(
                    AchievementIdentityState.Unresolved,
                    expectedId,
                    expected,
                    null,
                    null,
                    candidateIds,
                    null,
                    null,
                    "Identity verification requires a successful Phase 6C SearchActive result.");
            }

            if (screenshot == null)
            {
                return Finish(
                    AchievementIdentityState.Unresolved,
                    expectedId,
                    expected,
                    null,
                    null,
                    candidateIds,
                    null,
                    null,
                    "The current full capture was not supplied.");
            }

            AchievementScreenDetection screen;
            try
            {
                screen = screenDetector.Detect(screenshot);
            }
            catch (Exception ex)
            {
                return Finish(
                    AchievementIdentityState.Unresolved,
                    expectedId,
                    expected,
                    null,
                    null,
                    candidateIds,
                    null,
                    null,
                    $"Current-screen detection failed: {ex.GetType().Name}: {ex.Message}");
            }

            if (screen.ScreenState != AchievementScreenState.SearchActive)
                return Finish(
                    AchievementIdentityState.Unresolved,
                    expectedId,
                    expected,
                    null,
                    null,
                    candidateIds,
                    screen,
                    null,
                    $"Current screen is not positively SearchActive; observed {screen.ScreenState}.");

            if (!screen.MatchCountAccepted || !screen.MatchCountValue.HasValue)
                return Finish(
                    AchievementIdentityState.Unresolved,
                    expectedId,
                    expected,
                    null,
                    null,
                    candidateIds,
                    screen,
                    null,
                    "SearchActive identity verification requires a valid Match(es) count.");

            if (!string.IsNullOrWhiteSpace(screen.ResultPageRaw) && !screen.ResultPageAccepted)
                return Finish(
                    AchievementIdentityState.Unresolved,
                    expectedId,
                    expected,
                    null,
                    null,
                    candidateIds,
                    screen,
                    null,
                    "The visible result page counter was not valid.");

            if (screen.MatchCountValue.Value > 1)
                return Finish(
                    AchievementIdentityState.Ambiguous,
                    expectedId,
                    expected,
                    null,
                    null,
                    candidateIds,
                    screen,
                    null,
                    "Multiple visible matches do not provide deterministic first-result identity.");

            if (screen.MatchCountValue.Value != 1)
                return Finish(
                    AchievementIdentityState.Unresolved,
                    expectedId,
                    expected,
                    null,
                    null,
                    candidateIds,
                    screen,
                    null,
                    "The ordinary identity path requires exactly one positively verified match.");

            AchievementResultTitleRead titleRead;
            try
            {
                titleRead = titleReader.Read(screenshot);
            }
            catch (Exception ex)
            {
                return Finish(
                    AchievementIdentityState.Unresolved,
                    expectedId,
                    expected,
                    null,
                    null,
                    candidateIds,
                    screen,
                    null,
                    $"Result-title OCR failed: {ex.GetType().Name}: {ex.Message}");
            }

            string[] alternatives = BuildRelevantAlternativeTitles(expected.Name);
            var attempts = titleRead.Attempts
                .Select(attempt => EvaluateAttempt(attempt, expected.Name, alternatives))
                .ToArray();
            AchievementIdentityOcrAttempt selected = attempts
                .Where(attempt => attempt.OcrAttempt.NormalizedText.Length > 0)
                .OrderByDescending(attempt => attempt.Score)
                .ThenByDescending(attempt => attempt.OcrAttempt.Confidence)
                .FirstOrDefault();

            if (selected == null)
                return Finish(
                    AchievementIdentityState.Unresolved,
                    expectedId,
                    expected,
                    null,
                    null,
                    candidateIds,
                    screen,
                    attempts,
                    "Result-title OCR returned no usable text.");

            if (selected.OcrAttempt.Confidence < AchievementTitleMatcher.MinimumOcrConfidence)
                return Finish(
                    AchievementIdentityState.Unresolved,
                    expectedId,
                    expected,
                    null,
                    null,
                    candidateIds,
                    screen,
                    attempts,
                    "Result-title OCR confidence was below the required threshold.",
                    selected);

            if (selected.Score < AchievementTitleMatcher.MinimumScore)
            {
                string observed = selected.OcrAttempt.NormalizedText;
                string canonical = AchievementTitleNormalizer.Normalize(expected.Name);
                if (IsPlausibleClippedPrefix(observed, canonical))
                    return Finish(
                        AchievementIdentityState.Unresolved,
                        expectedId,
                        expected,
                        null,
                        null,
                        candidateIds,
                        screen,
                        attempts,
                        "Visible title may be a clipped canonical prefix; full identity was not proven.",
                        selected);

                bool competingTitleWon = selected.RunnerUpScore >= AchievementTitleMatcher.MinimumScore &&
                    selected.RunnerUpScore - selected.Score >= AchievementTitleMatcher.MinimumRunnerUpMargin;
                AchievementIdentityState state = competingTitleWon ||
                    selected.Score <= AchievementTitleMatcher.DefiniteMismatchMaximumScore
                        ? AchievementIdentityState.Mismatch
                        : AchievementIdentityState.Unresolved;
                return Finish(
                    state,
                    expectedId,
                    expected,
                    null,
                    null,
                    candidateIds,
                    screen,
                    attempts,
                    state == AchievementIdentityState.Mismatch
                        ? "Visible title positively conflicts with the expected canonical title."
                        : "Visible title score was below the positive identity threshold.",
                    selected);
            }

            if (selected.Margin < AchievementTitleMatcher.MinimumRunnerUpMargin)
                return Finish(
                    AchievementIdentityState.Ambiguous,
                    expectedId,
                    expected,
                    null,
                    null,
                    candidateIds,
                    screen,
                    attempts,
                    "The title winner did not exceed the runner-up by the required margin.",
                    selected);

            if (titleGroup?.Kind == AchievementTitleGroupKind.StageFamily)
            {
                return Finish(
                    AchievementIdentityState.StageGroupIdentified,
                    expectedId,
                    expected,
                    null,
                    titleGroup.StageGroupId,
                    titleGroup.CandidateIds,
                    screen,
                    attempts,
                    "Visible title identifies the canonical staged title-family, not an individual stage.",
                    selected);
            }

            if (titleGroup?.Kind == AchievementTitleGroupKind.UnrelatedCollision)
                return Finish(
                    AchievementIdentityState.Ambiguous,
                    expectedId,
                    expected,
                    null,
                    null,
                    candidateIds,
                    screen,
                    attempts,
                    "Visible title matches multiple non-stage canonical achievements.",
                    selected);

            return Finish(
                AchievementIdentityState.Identified,
                expectedId,
                expected,
                expected.Id,
                null,
                candidateIds,
                screen,
                attempts,
                "Visible result title positively matches the unique canonical achievement.",
                selected);
        }

        private static bool IsPlausibleClippedPrefix(string observed, string canonical)
        {
            if (observed.Length < 12 || canonical.Length <= observed.Length)
                return false;

            string canonicalVisiblePrefix = canonical.Substring(0, observed.Length);
            return AchievementTitleMatcher.Similarity(observed, canonicalVisiblePrefix) >=
                AchievementTitleMatcher.MinimumClippedPrefixScore;
        }

        private AchievementIdentityOcrAttempt EvaluateAttempt(
            AchievementTitleOcrAttempt attempt,
            string expectedTitle,
            IReadOnlyList<string> alternatives)
        {
            AchievementTitleMatch match = AchievementTitleMatcher.Match(
                attempt.NormalizedText,
                expectedTitle,
                alternatives);
            string reason = attempt.NormalizedText.Length == 0
                ? "Rejected because OCR returned no normalized title text."
                : attempt.Confidence < AchievementTitleMatcher.MinimumOcrConfidence
                    ? "Rejected because OCR confidence was below threshold."
                    : match.Score < AchievementTitleMatcher.MinimumScore
                        ? "Rejected because canonical title score was below threshold."
                        : match.Margin < AchievementTitleMatcher.MinimumRunnerUpMargin
                            ? "Rejected because the runner-up margin was insufficient."
                            : "Accepted as positive title evidence.";
            return new AchievementIdentityOcrAttempt(
                attempt,
                match.Score,
                match.RunnerUpScore,
                match.Margin,
                reason);
        }

        private string[] BuildRelevantAlternativeTitles(string expectedTitle)
        {
            string normalizedExpected = AchievementTitleNormalizer.Normalize(expectedTitle);
            return catalog.All
                .Select(entry => entry.Name)
                .Distinct(StringComparer.Ordinal)
                .Where(title => !string.Equals(
                    AchievementTitleNormalizer.Normalize(title),
                    normalizedExpected,
                    StringComparison.Ordinal))
                .Where(title => AchievementTitleMatcher.Similarity(title, expectedTitle) >=
                    AchievementTitleMatcher.CandidateRelevanceScore)
                .OrderBy(title => title, StringComparer.Ordinal)
                .ToArray();
        }

        private AchievementIdentityResult Finish(
            AchievementIdentityState state,
            int expectedId,
            AchievementCatalogEntry expected,
            int? identifiedId,
            int? stageGroupId,
            IEnumerable<int> candidateIds,
            AchievementScreenDetection screen,
            IEnumerable<AchievementIdentityOcrAttempt> attempts,
            string reason,
            AchievementIdentityOcrAttempt selected = null)
        {
            AchievementIdentityOcrAttempt[] attemptArray =
                (attempts ?? Array.Empty<AchievementIdentityOcrAttempt>()).ToArray();
            int[] candidateArray = (candidateIds ?? Array.Empty<int>()).ToArray();
            AchievementTitleGroup titleGroup = expected == null
                ? null
                : catalog.GetTitleGroupForId(expected.Id);
            string diagnostics = BuildDiagnostics(
                state,
                expectedId,
                expected?.Name,
                titleGroup,
                screen,
                selected,
                candidateArray,
                reason,
                attemptArray);
            if (state == AchievementIdentityState.Identified ||
                state == AchievementIdentityState.StageGroupIdentified)
                Logger.Info(diagnostics);
            else
                Logger.Warn(diagnostics);

            return new AchievementIdentityResult(
                state,
                expectedId,
                expected?.Name,
                identifiedId,
                stageGroupId,
                candidateArray,
                selected?.OcrAttempt.RawText,
                selected?.OcrAttempt.NormalizedText,
                selected?.Score ?? 0,
                selected?.RunnerUpScore ?? 0,
                selected?.Margin ?? 0,
                selected?.OcrAttempt.Confidence ?? 0,
                attemptArray,
                screen,
                reason,
                diagnostics);
        }

        private static string BuildDiagnostics(
            AchievementIdentityState state,
            int expectedId,
            string expectedTitle,
            AchievementTitleGroup titleGroup,
            AchievementScreenDetection screen,
            AchievementIdentityOcrAttempt selected,
            IReadOnlyList<int> candidateIds,
            string reason,
            IReadOnlyList<AchievementIdentityOcrAttempt> attempts)
        {
            var builder = new StringBuilder(1400);
            builder.AppendLine("operation=identity-verify");
            builder.AppendLine($"expectedId={expectedId.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"expectedTitleLength={(expectedTitle?.Length ?? 0).ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"screenState={screen?.ScreenState.ToString() ?? "(none)"}");
            builder.AppendLine($"matchCount={Format(screen?.MatchCountValue)}");
            builder.AppendLine($"page={Format(screen?.ResultPageCurrent)}/{Format(screen?.ResultPageTotal)}");
            builder.AppendLine($"rawTitle={Bound(selected?.OcrAttempt.RawText)}");
            builder.AppendLine($"normalizedTitle={Bound(selected?.OcrAttempt.NormalizedText)}");
            builder.AppendLine($"ocrConfidence={Format(selected?.OcrAttempt.Confidence)}");
            builder.AppendLine($"score={Format(selected?.Score)}");
            builder.AppendLine($"runnerUpScore={Format(selected?.RunnerUpScore)}");
            builder.AppendLine($"margin={Format(selected?.Margin)}");
            builder.AppendLine($"titleGroupKind={titleGroup?.Kind.ToString() ?? "(none)"}");
            builder.AppendLine($"candidateCount={candidateIds.Count.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"candidateIds={string.Join(",", candidateIds)}");
            builder.AppendLine($"collision={titleGroup?.IsUnrelatedCollision == true}");
            builder.AppendLine($"multiMatch={screen?.MatchCountValue > 1}");
            builder.AppendLine($"state={state}");
            builder.AppendLine($"reason={Bound(reason)}");
            for (int i = 0; i < attempts.Count; i++)
            {
                AchievementIdentityOcrAttempt attempt = attempts[i];
                builder.AppendLine(
                    $"attempt[{i.ToString(CultureInfo.InvariantCulture)}]=" +
                    $"variant={attempt.OcrAttempt.Variant}; " +
                    $"raw={Bound(attempt.OcrAttempt.RawText)}; " +
                    $"normalized={Bound(attempt.OcrAttempt.NormalizedText)}; " +
                    $"confidence={attempt.OcrAttempt.Confidence.ToString("0.000", CultureInfo.InvariantCulture)}; " +
                    $"score={attempt.Score.ToString("0.0", CultureInfo.InvariantCulture)}; " +
                    $"runnerUp={attempt.RunnerUpScore.ToString("0.0", CultureInfo.InvariantCulture)}; " +
                    $"margin={attempt.Margin.ToString("0.0", CultureInfo.InvariantCulture)}; " +
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

        private static string Format(double? value) =>
            value?.ToString("0.0", CultureInfo.InvariantCulture) ?? "(none)";
    }
}

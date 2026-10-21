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

    internal interface IAchievementSharedScreenIdentityVerifier
    {
        AchievementIdentityResult Verify(
            AchievementSearchNavigationResult searchResult,
            int expectedId,
            Bitmap screenshot,
            AchievementScreenDetection screenDetection);
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
            string runnerUpTitle,
            double runnerUpScore,
            double margin,
            string reason)
        {
            OcrAttempt = ocrAttempt ?? throw new ArgumentNullException(nameof(ocrAttempt));
            Score = score;
            RunnerUpTitle = runnerUpTitle ?? string.Empty;
            RunnerUpScore = runnerUpScore;
            Margin = margin;
            Reason = reason ?? string.Empty;
        }

        internal AchievementTitleOcrAttempt OcrAttempt { get; }
        internal double Score { get; }
        internal string RunnerUpTitle { get; }
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
            string diagnostics,
            bool usedCorroboratedSearchResult = false,
            bool usedClippedPrefixEvidence = false)
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
            UsedCorroboratedSearchResult = usedCorroboratedSearchResult;
            UsedClippedPrefixEvidence = usedClippedPrefixEvidence;
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
        internal bool UsedCorroboratedSearchResult { get; }
        internal bool UsedClippedPrefixEvidence { get; }
    }

    internal readonly struct AchievementTitleMatch
    {
        internal AchievementTitleMatch(double score, string runnerUpTitle, double runnerUpScore)
        {
            Score = score;
            RunnerUpTitle = runnerUpTitle ?? string.Empty;
            RunnerUpScore = runnerUpScore;
            Margin = score - runnerUpScore;
        }

        internal double Score { get; }
        internal string RunnerUpTitle { get; }
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
        internal const int MinimumClippedPrefixLength = 20;
        internal const float MinimumOcrConfidence = 0.35f;

        internal static AchievementTitleMatch Match(
            string observedTitle,
            string expectedTitle,
            IEnumerable<string> alternativeTitles)
        {
            string observed = AchievementTitleNormalizer.NormalizeForComparison(observedTitle);
            string expected = AchievementTitleNormalizer.NormalizeForComparison(expectedTitle);
            double score = Similarity(observed, expected);
            var runnerUp = (alternativeTitles ?? Array.Empty<string>())
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Distinct(StringComparer.Ordinal)
                .Select(title => new
                {
                    Title = title,
                    Score = Similarity(observed, AchievementTitleNormalizer.NormalizeForComparison(title)),
                })
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Title, StringComparer.Ordinal)
                .FirstOrDefault();
            return new AchievementTitleMatch(
                score,
                runnerUp?.Title,
                runnerUp?.Score ?? 0.0);
        }

        internal static double Similarity(string left, string right)
        {
            left = AchievementTitleNormalizer.NormalizeForComparison(left);
            right = AchievementTitleNormalizer.NormalizeForComparison(right);
            if (left.Length == 0 || right.Length == 0) return 0;
            if (string.Equals(left, right, StringComparison.Ordinal)) return 100;

            double fullScore = EditSimilarity(left, right);
            if (left.Length <= right.Length || !HasNoisyTrailingContent(left, right.Length))
                return fullScore;

            // Live evidence shows the high-threshold title variant can retain a clean title at
            // the start of the crop and then OCR description/card decoration as a noisy suffix.
            // Score only the equally-sized leading span as an additional comparison, while the
            // runner-up and collision gates remain fully enforced for every candidate title.
            double leadingSpanScore = EditSimilarity(left.Substring(0, right.Length), right);
            return Math.Max(fullScore, leadingSpanScore);
        }

        private static bool HasNoisyTrailingContent(string observed, int titleLength)
        {
            int symbolCount = 0;
            for (int i = titleLength; i < observed.Length; i++)
            {
                char character = observed[i];
                if (!char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character))
                    symbolCount++;
            }
            return symbolCount >= 2;
        }

        private static double EditSimilarity(string left, string right)
        {
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
    internal sealed class AchievementResultIdentityVerifier :
        IAchievementResultIdentityVerifier,
        IAchievementSharedScreenIdentityVerifier
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
            Bitmap screenshot) => Verify(searchResult, expectedId, screenshot, null);

        public AchievementIdentityResult Verify(
            AchievementSearchNavigationResult searchResult,
            int expectedId,
            Bitmap screenshot,
            AchievementScreenDetection screenDetection)
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
                !HasUsableResultState(searchResult.AfterDetection))
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
            }

            bool corroboratedUnknownSearchResult =
                screen.ScreenState == AchievementScreenState.Unknown &&
                screen.MatchCountAccepted &&
                screen.ResultPageAccepted &&
                screen.ResultPageCurrent == 1 &&
                screen.ResultPageTotal == 1;
            if (screen.ScreenState != AchievementScreenState.SearchActive &&
                !corroboratedUnknownSearchResult)
                return Finish(
                    AchievementIdentityState.Unresolved,
                    expectedId,
                    expected,
                    null,
                    null,
                    candidateIds,
                    screen,
                    null,
                    $"Current screen is not positively SearchActive and lacks corroborated current result markers; observed {screen.ScreenState}.");

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

            string[] alternatives = BuildRelevantAlternativeTitles(expected.Name);
            AchievementResultTitleRead titleRead;
            try
            {
                titleRead = titleReader is IAchievementLazyResultTitleReader lazyReader
                    ? lazyReader.Read(
                        screenshot,
                        attempt => IsPositiveAttempt(
                            EvaluateAttempt(attempt, expected.Name, alternatives)))
                    : titleReader.Read(screenshot);
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

            var attempts = titleRead.Attempts
                .Select(attempt => EvaluateAttempt(attempt, expected.Name, alternatives))
                .ToArray();
            AchievementIdentityOcrAttempt selected = attempts
                .Where(IsPositiveAttempt)
                .OrderByDescending(attempt => attempt.Score)
                .ThenByDescending(attempt => attempt.OcrAttempt.Confidence)
                .FirstOrDefault() ?? attempts
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
                {
                    if (IsSafeCorroboratedClippedPrefix(
                        selected,
                        expected.Name,
                        titleGroup,
                        screen))
                    {
                        AchievementIdentityState clippedState =
                            titleGroup?.Kind == AchievementTitleGroupKind.StageFamily
                                ? AchievementIdentityState.StageGroupIdentified
                                : AchievementIdentityState.Identified;
                        return Finish(
                            clippedState,
                            expectedId,
                            expected,
                            clippedState == AchievementIdentityState.Identified ? expected.Id : null,
                            clippedState == AchievementIdentityState.StageGroupIdentified
                                ? titleGroup.StageGroupId
                                : null,
                            candidateIds,
                            screen,
                            attempts,
                            "A conservative left-anchored canonical prefix was corroborated by the exact query and a single result page.",
                            selected,
                            usedClippedPrefixEvidence: true);
                    }
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
                }

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

        private bool IsSafeCorroboratedClippedPrefix(
            AchievementIdentityOcrAttempt selected,
            string expectedTitle,
            AchievementTitleGroup titleGroup,
            AchievementScreenDetection screen)
        {
            if (selected.OcrAttempt.Confidence < AchievementTitleMatcher.MinimumOcrConfidence ||
                titleGroup == null ||
                titleGroup.Kind == AchievementTitleGroupKind.UnrelatedCollision ||
                screen?.MatchCountAccepted != true || screen.MatchCountValue != 1 ||
                !screen.ResultPageAccepted || screen.ResultPageCurrent != 1 || screen.ResultPageTotal != 1)
                return false;

            string observed = AchievementTitleNormalizer.NormalizeForComparison(
                selected.OcrAttempt.RawText);
            string expected = AchievementTitleNormalizer.NormalizeForComparison(expectedTitle);
            int usableLength = Math.Min(observed.Length, expected.Length - 1);
            while (usableLength >= AchievementTitleMatcher.MinimumClippedPrefixLength)
            {
                string observedPrefix = observed.Substring(0, usableLength);
                string expectedPrefix = expected.Substring(0, usableLength);
                if (AchievementTitleMatcher.Similarity(observedPrefix, expectedPrefix) >=
                    AchievementTitleMatcher.MinimumClippedPrefixScore)
                {
                    bool unsafeAlternate = catalog.All
                        .Select(entry => AchievementTitleNormalizer.NormalizeForComparison(entry.Name))
                        .Distinct(StringComparer.Ordinal)
                        .Where(title => !string.Equals(title, expected, StringComparison.Ordinal))
                        .Any(title => title.Length >= usableLength &&
                            AchievementTitleMatcher.Similarity(
                                observedPrefix,
                                title.Substring(0, usableLength)) >=
                            AchievementTitleMatcher.MinimumClippedPrefixScore);
                    return !unsafeAlternate;
                }

                // Only discard a bounded damaged/clipped tail. An arbitrary substring or a weak
                // leading read never reaches the positive prefix path.
                if (observed.Length - usableLength >= 3) break;
                usableLength--;
            }
            return false;
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
                match.RunnerUpTitle,
                match.RunnerUpScore,
                match.Margin,
                reason);
        }

        private static bool IsPositiveAttempt(AchievementIdentityOcrAttempt attempt) =>
            attempt.OcrAttempt.NormalizedText.Length > 0 &&
            attempt.OcrAttempt.Confidence >= AchievementTitleMatcher.MinimumOcrConfidence &&
            attempt.Score >= AchievementTitleMatcher.MinimumScore &&
            attempt.Margin >= AchievementTitleMatcher.MinimumRunnerUpMargin;

        private string[] BuildRelevantAlternativeTitles(string expectedTitle)
        {
            string normalizedExpected = AchievementTitleNormalizer.Normalize(expectedTitle);
            string comparisonExpected = AchievementTitleNormalizer.NormalizeForComparison(expectedTitle);
            return catalog.All
                .Select(entry => entry.Name)
                .Distinct(StringComparer.Ordinal)
                .Where(title => !string.Equals(
                    AchievementTitleNormalizer.Normalize(title),
                    normalizedExpected,
                    StringComparison.Ordinal))
                .Where(title =>
                {
                    string comparisonTitle = AchievementTitleNormalizer.NormalizeForComparison(title);
                    return AchievementTitleMatcher.Similarity(comparisonTitle, comparisonExpected) >=
                        AchievementTitleMatcher.CandidateRelevanceScore ||
                        comparisonTitle.StartsWith(comparisonExpected, StringComparison.Ordinal) ||
                        comparisonExpected.StartsWith(comparisonTitle, StringComparison.Ordinal);
                })
                .OrderBy(title => title, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool HasUsableResultState(AchievementScreenDetection detection) =>
            detection?.ScreenState == AchievementScreenState.SearchActive ||
            (detection?.ScreenState == AchievementScreenState.Unknown &&
             detection.MatchCountAccepted && detection.MatchCountValue == 1 &&
             detection.ResultPageAccepted && detection.ResultPageCurrent == 1 &&
             detection.ResultPageTotal == 1);

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
            AchievementIdentityOcrAttempt selected = null,
            bool usedClippedPrefixEvidence = false)
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
                attemptArray,
                usedClippedPrefixEvidence);
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
                diagnostics,
                usedCorroboratedSearchResult:
                    (state == AchievementIdentityState.Identified ||
                     state == AchievementIdentityState.StageGroupIdentified) &&
                    screen?.ScreenState == AchievementScreenState.Unknown,
                usedClippedPrefixEvidence: usedClippedPrefixEvidence);
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
            IReadOnlyList<AchievementIdentityOcrAttempt> attempts,
            bool usedClippedPrefixEvidence)
        {
            var builder = new StringBuilder(1400);
            builder.AppendLine("operation=identity-verify");
            builder.AppendLine($"expectedId={expectedId.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"expectedTitle={Bound(expectedTitle)}");
            builder.AppendLine($"expectedTitleLength={(expectedTitle?.Length ?? 0).ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"screenState={screen?.ScreenState.ToString() ?? "(none)"}");
            builder.AppendLine($"matchCountRaw={Bound(screen?.MatchCountRaw)}");
            builder.AppendLine($"matchCount={Format(screen?.MatchCountValue)}");
            builder.AppendLine($"page={Format(screen?.ResultPageCurrent)}/{Format(screen?.ResultPageTotal)}");
            builder.AppendLine($"rawTitle={Bound(selected?.OcrAttempt.RawText)}");
            builder.AppendLine($"normalizedTitle={Bound(selected?.OcrAttempt.NormalizedText)}");
            builder.AppendLine($"comparisonTitle={Bound(selected?.OcrAttempt.ComparisonText)}");
            builder.AppendLine($"ocrConfidence={Format(selected?.OcrAttempt.Confidence)}");
            builder.AppendLine($"score={Format(selected?.Score)}");
            builder.AppendLine($"bestAlternateTitle={Bound(selected?.RunnerUpTitle)}");
            builder.AppendLine($"runnerUpScore={Format(selected?.RunnerUpScore)}");
            builder.AppendLine($"margin={Format(selected?.Margin)}");
            builder.AppendLine($"minimumScore={AchievementTitleMatcher.MinimumScore.ToString("0.0", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"minimumRunnerUpMargin={AchievementTitleMatcher.MinimumRunnerUpMargin.ToString("0.0", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"minimumOcrConfidence={AchievementTitleMatcher.MinimumOcrConfidence.ToString("0.000", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"titleGroupKind={titleGroup?.Kind.ToString() ?? "(none)"}");
            builder.AppendLine($"candidateCount={candidateIds.Count.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"candidateIds={string.Join(",", candidateIds)}");
            builder.AppendLine($"collision={titleGroup?.IsUnrelatedCollision == true}");
            builder.AppendLine($"multiMatch={screen?.MatchCountValue > 1}");
            builder.AppendLine($"usedCorroboratedSearchResult={
                (state == AchievementIdentityState.Identified ||
                 state == AchievementIdentityState.StageGroupIdentified) &&
                screen?.ScreenState == AchievementScreenState.Unknown}");
            builder.AppendLine($"usedClippedPrefixEvidence={usedClippedPrefixEvidence}");
            builder.AppendLine($"state={state}");
            builder.AppendLine($"finalIdentityState={state}");
            builder.AppendLine($"reason={Bound(reason)}");
            builder.AppendLine($"finalRejectionReason={Bound(reason)}");
            for (int i = 0; i < attempts.Count; i++)
            {
                AchievementIdentityOcrAttempt attempt = attempts[i];
                builder.AppendLine(
                    $"attempt[{i.ToString(CultureInfo.InvariantCulture)}]=" +
                    $"variant={attempt.OcrAttempt.Variant}; " +
                    $"scope={attempt.OcrAttempt.Scope}; " +
                    $"raw={Bound(attempt.OcrAttempt.RawText)}; " +
                    $"normalized={Bound(attempt.OcrAttempt.NormalizedText)}; " +
                    $"comparison={Bound(attempt.OcrAttempt.ComparisonText)}; " +
                    $"confidence={attempt.OcrAttempt.Confidence.ToString("0.000", CultureInfo.InvariantCulture)}; " +
                    $"score={attempt.Score.ToString("0.0", CultureInfo.InvariantCulture)}; " +
                    $"bestAlternate={Bound(attempt.RunnerUpTitle)}; " +
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

using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Tesseract;

namespace InventoryKamera
{
    internal enum AchievementScreenState
    {
        NotAchievementScreen,
        Idle,
        SearchActive,
        SearchNoResults,
        Unknown,
    }

    internal enum AchievementResultCountState
    {
        NoValidCount,
        NoResults,
        SingleResult,
        MultipleResults,
    }

    internal sealed class AchievementOcrAttempt
    {
        internal AchievementOcrAttempt(
            bool inverted,
            string rawText,
            string normalizedText,
            float confidence,
            bool accepted,
            string reason)
        {
            Inverted = inverted;
            RawText = rawText ?? string.Empty;
            NormalizedText = normalizedText ?? string.Empty;
            Confidence = confidence;
            Accepted = accepted;
            Reason = reason ?? string.Empty;
        }

        internal bool Inverted { get; }
        internal string RawText { get; }
        internal string NormalizedText { get; }
        internal float Confidence { get; }
        internal bool Accepted { get; }
        internal string Reason { get; }
    }

    internal sealed class AchievementTextEvidence
    {
        internal AchievementTextEvidence(
            string rawText,
            string normalizedText,
            float confidence,
            bool accepted,
            string reason,
            IEnumerable<AchievementOcrAttempt> attempts = null)
        {
            RawText = rawText ?? string.Empty;
            NormalizedText = normalizedText ?? string.Empty;
            Confidence = confidence;
            Accepted = accepted;
            Reason = reason ?? string.Empty;
            Attempts = new ReadOnlyCollection<AchievementOcrAttempt>(
                (attempts ?? Array.Empty<AchievementOcrAttempt>()).ToList());
        }

        internal string RawText { get; }
        internal string NormalizedText { get; }
        internal float Confidence { get; }
        internal bool Accepted { get; }
        internal string Reason { get; }
        internal IReadOnlyList<AchievementOcrAttempt> Attempts { get; }

        internal static AchievementTextEvidence ForTest(
            string rawText,
            bool accepted,
            float confidence = 1.0f,
            string reason = null) =>
            new AchievementTextEvidence(
                rawText,
                AchievementScreenDetector.Normalize(rawText),
                confidence,
                accepted,
                reason ?? (accepted ? "Accepted test evidence." : "Rejected test evidence."));
    }

    internal sealed class AchievementSearchFieldEvidence
    {
        internal AchievementSearchFieldEvidence(
            AchievementTextEvidence text,
            bool idlePlaceholderAccepted,
            bool idleControllerPromptAccepted,
            bool activeControlsAccepted)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            IdlePlaceholderAccepted = idlePlaceholderAccepted;
            IdleControllerPromptAccepted = idleControllerPromptAccepted;
            ActiveControlsAccepted = activeControlsAccepted;
        }

        internal AchievementTextEvidence Text { get; }
        internal bool IdlePlaceholderAccepted { get; }
        internal bool IdleControllerPromptAccepted { get; }
        internal bool ActiveControlsAccepted { get; }
        internal bool IdleAccepted => IdlePlaceholderAccepted || IdleControllerPromptAccepted;
        internal bool Accepted => IdleAccepted || ActiveControlsAccepted;

        internal static AchievementSearchFieldEvidence ForTest(
            bool idlePlaceholderAccepted = false,
            bool idleControllerPromptAccepted = false,
            bool activeControlsAccepted = false,
            string rawText = "") =>
            new AchievementSearchFieldEvidence(
                AchievementTextEvidence.ForTest(
                    rawText,
                    idlePlaceholderAccepted || idleControllerPromptAccepted || activeControlsAccepted),
                idlePlaceholderAccepted,
                idleControllerPromptAccepted,
                activeControlsAccepted);
    }

    internal sealed class AchievementScreenDetection
    {
        internal AchievementScreenDetection(
            AchievementScreenState screenState,
            AchievementTextEvidence achievementHeading,
            AchievementSearchFieldEvidence searchField,
            AchievementTextEvidence completedCount,
            AchievementTextEvidence matchCountEvidence,
            AchievementMatchCountParseResult matchCount,
            AchievementTextEvidence resultPageEvidence,
            AchievementPageCounterParseResult resultPage,
            AchievementScreenRegions regions,
            string reason,
            AchievementTextEvidence noResultsEvidence = null,
            AchievementResultRowEvidence resultRowEvidence = null)
        {
            ScreenState = screenState;
            AchievementHeading = achievementHeading ?? throw new ArgumentNullException(nameof(achievementHeading));
            SearchField = searchField ?? throw new ArgumentNullException(nameof(searchField));
            CompletedCount = completedCount ?? throw new ArgumentNullException(nameof(completedCount));
            MatchCountEvidence = matchCountEvidence ?? throw new ArgumentNullException(nameof(matchCountEvidence));
            MatchCount = matchCount;
            ResultPageEvidence = resultPageEvidence ?? throw new ArgumentNullException(nameof(resultPageEvidence));
            ResultPage = resultPage;
            NoResultsEvidence = noResultsEvidence ?? AchievementTextEvidence.ForTest(
                string.Empty,
                accepted: false,
                reason: "The explicit no-results phrase was not read for this state.");
            ResultRowEvidence = resultRowEvidence ??
                AchievementResultRowEvidence.ForTest(AchievementResultRowPresence.Unknown);
            Regions = regions ?? throw new ArgumentNullException(nameof(regions));
            Reason = reason ?? string.Empty;
            Diagnostics = BuildDiagnostics();
        }

        internal AchievementScreenState ScreenState { get; }
        internal AchievementTextEvidence AchievementHeading { get; }
        internal AchievementSearchFieldEvidence SearchField { get; }
        internal AchievementTextEvidence CompletedCount { get; }
        internal AchievementTextEvidence MatchCountEvidence { get; }
        internal AchievementMatchCountParseResult MatchCount { get; }
        internal AchievementTextEvidence ResultPageEvidence { get; }
        internal AchievementPageCounterParseResult ResultPage { get; }
        internal AchievementTextEvidence NoResultsEvidence { get; }
        internal AchievementResultRowEvidence ResultRowEvidence { get; }
        internal AchievementScreenRegions Regions { get; }
        internal string Reason { get; }
        internal string Diagnostics { get; }

        internal bool AchievementHeadingAccepted => AchievementHeading.Accepted;
        internal bool SearchFieldAccepted => SearchField.Accepted;
        internal bool IsKeyboardMouseIdle =>
            ScreenState == AchievementScreenState.Idle && SearchField.IdlePlaceholderAccepted;
        internal bool CompletedCountAccepted => CompletedCount.Accepted;
        internal string MatchCountRaw => MatchCount.RawText;
        internal int? MatchCountValue => MatchCount.Value;
        internal bool MatchCountAccepted => MatchCount.Accepted;
        internal AchievementResultCountState ResultCountState => ScreenState == AchievementScreenState.SearchNoResults
            ? AchievementResultCountState.NoResults
            : !MatchCount.Value.HasValue
            ? AchievementResultCountState.NoValidCount
            : MatchCount.Value.Value == 0
                ? AchievementResultCountState.NoResults
                : MatchCount.Value.Value == 1
                    ? AchievementResultCountState.SingleResult
                    : AchievementResultCountState.MultipleResults;
        internal string ResultPageRaw => ResultPage.RawText;
        internal int? ResultPageCurrent => ResultPage.Current;
        internal int? ResultPageTotal => ResultPage.Total;
        internal bool ResultPageAccepted => ResultPage.Accepted;

        private string BuildDiagnostics()
        {
            var builder = new StringBuilder(768);
            builder.AppendLine($"screenState={ScreenState}");
            AppendText(builder, "achievementHeading", AchievementHeading);
            AppendText(builder, "searchField", SearchField.Text);
            builder.AppendLine(
                $"  idlePlaceholder={SearchField.IdlePlaceholderAccepted}; " +
                $"idleControllerPrompt={SearchField.IdleControllerPromptAccepted}; " +
                $"activeControls={SearchField.ActiveControlsAccepted}");
            AppendText(builder, "completedCount", CompletedCount);
            builder.AppendLine("matchCount:");
            builder.AppendLine($"  raw=\"{Bound(MatchCount.RawText)}\"");
            builder.AppendLine($"  normalized=\"{Bound(MatchCountEvidence.NormalizedText)}\"");
            builder.AppendLine($"  confidence={MatchCountEvidence.Confidence.ToString("0.000", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"  parsed={Format(MatchCount.Value)}");
            builder.AppendLine($"  accepted={MatchCount.Accepted}");
            builder.AppendLine($"  reason={Bound(MatchCount.Reason)}");
            builder.AppendLine("pageCounter:");
            builder.AppendLine($"  raw=\"{Bound(ResultPage.RawText)}\"");
            builder.AppendLine($"  normalized=\"{Bound(ResultPageEvidence.NormalizedText)}\"");
            builder.AppendLine($"  confidence={ResultPageEvidence.Confidence.ToString("0.000", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"  current={Format(ResultPage.Current)}");
            builder.AppendLine($"  total={Format(ResultPage.Total)}");
            builder.AppendLine($"  accepted={ResultPage.Accepted}");
            builder.AppendLine($"  reason={Bound(ResultPage.Reason)}");
            AppendText(builder, "noResultsMessage", NoResultsEvidence);
            builder.AppendLine($"resultRow: {ResultRowEvidence.Diagnostics}");
            builder.AppendLine($"finalState={ScreenState}");
            builder.Append($"reason={Bound(Reason)}");
            return builder.ToString();
        }

        private static void AppendText(StringBuilder builder, string name, AchievementTextEvidence evidence)
        {
            builder.AppendLine(name + ":");
            builder.AppendLine($"  raw=\"{Bound(evidence.RawText)}\"");
            builder.AppendLine($"  normalized=\"{Bound(evidence.NormalizedText)}\"");
            builder.AppendLine($"  confidence={evidence.Confidence.ToString("0.000", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"  accepted={evidence.Accepted}");
            builder.AppendLine($"  reason={Bound(evidence.Reason)}");
        }

        private static string Bound(string value)
        {
            string cleaned = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
            return cleaned.Length <= 240 ? cleaned : cleaned.Substring(0, 240) + "...";
        }

        private static string Format(int? value) =>
            value?.ToString(CultureInfo.InvariantCulture) ?? "(none)";
    }

    internal interface IAchievementScreenDetector
    {
        AchievementScreenDetection Detect(Bitmap screenshot);
    }

    /// <summary>
    /// Detects the Achievement screen and its idle/search-active state from independent positive
    /// OCR signals. Missing evidence never implies a state, and contradictory strong evidence is
    /// returned as Unknown.
    /// </summary>
    internal sealed class AchievementScreenDetector : IAchievementScreenDetector
    {
        private const float FuzzyMinimumConfidence = 0.35f;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IOcrService ocrService;
        private readonly IImagePreprocessor imagePreprocessor;

        internal AchievementScreenDetector(IOcrService ocrService, IImagePreprocessor imagePreprocessor)
        {
            this.ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            this.imagePreprocessor = imagePreprocessor ?? throw new ArgumentNullException(nameof(imagePreprocessor));
        }

        public AchievementScreenDetection Detect(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));

            AchievementScreenRegions regions = AchievementScreenRegions.For(screenshot.Size);
            AchievementTextEvidence heading = ReadSemanticRegion(
                screenshot,
                regions.Heading,
                PageSegMode.SingleLine,
                EvaluateHeading);
            AchievementTextEvidence searchText = ReadSemanticRegion(
                screenshot,
                regions.SearchField,
                PageSegMode.SingleLine,
                EvaluateSearchField);
            AchievementSearchFieldEvidence search = AnalyzeSearchField(searchText);
            AchievementTextEvidence completed = ReadSemanticRegion(
                screenshot,
                regions.CompletedCount,
                PageSegMode.SingleLine,
                EvaluateCompletedCount);

            AchievementTextEvidence matchText = ReadSemanticRegion(
                screenshot,
                regions.MatchCount,
                PageSegMode.SingleLine,
                (raw, _) => ParseEvaluation(AchievementMatchCountParser.Parse(raw)));
            AchievementMatchCountParseResult match = AchievementMatchCountParser.Parse(matchText.RawText);

            AchievementTextEvidence pageText = ReadSemanticRegion(
                screenshot,
                regions.PageCounter,
                PageSegMode.SingleLine,
                (raw, _) => ParseEvaluation(AchievementPageCounterParser.Parse(raw)));
            AchievementPageCounterParseResult page = AchievementPageCounterParser.Parse(pageText.RawText);

            AchievementTextEvidence noResults = AchievementTextEvidence.ForTest(
                string.Empty,
                accepted: false,
                reason: "No-results OCR was not needed for this screen state.");
            AchievementResultRowEvidence resultRow =
                AchievementResultRowEvidence.ForTest(AchievementResultRowPresence.Unknown);
            int achievementSignals =
                (heading.Accepted ? 1 : 0) +
                (search.Accepted ? 1 : 0) +
                (completed.Accepted ? 1 : 0);
            if (achievementSignals >= 2 && search.ActiveControlsAccepted && !match.Accepted)
            {
                resultRow = AchievementFirstResultRowDetector.Detect(
                    screenshot,
                    regions.FirstResultTitle);
                if (!resultRow.Present)
                {
                    noResults = ReadSemanticRegion(
                        screenshot,
                        regions.NoResultsMessage,
                        PageSegMode.SingleLine,
                        EvaluateNoResultsMessage);
                }
            }

            AchievementScreenDetection detection = Classify(
                screenshot.Size,
                heading,
                search,
                completed,
                match,
                page,
                regions,
                matchText,
                pageText,
                noResults,
                resultRow);
            Logger.Debug(detection.Diagnostics);
            return detection;
        }

        internal static AchievementScreenDetection Classify(
            Size captureSize,
            AchievementTextEvidence heading,
            AchievementSearchFieldEvidence search,
            AchievementTextEvidence completedCount,
            AchievementMatchCountParseResult matchCount,
            AchievementPageCounterParseResult pageCounter,
            AchievementScreenRegions regions = null,
            AchievementTextEvidence matchCountEvidence = null,
            AchievementTextEvidence pageCounterEvidence = null,
            AchievementTextEvidence noResultsEvidence = null,
            AchievementResultRowEvidence resultRowEvidence = null)
        {
            if (heading == null) throw new ArgumentNullException(nameof(heading));
            if (search == null) throw new ArgumentNullException(nameof(search));
            if (completedCount == null) throw new ArgumentNullException(nameof(completedCount));

            regions ??= AchievementScreenRegions.For(captureSize);
            matchCountEvidence ??= new AchievementTextEvidence(
                matchCount.RawText,
                Normalize(matchCount.RawText),
                1.0f,
                matchCount.Accepted,
                matchCount.Reason);
            pageCounterEvidence ??= new AchievementTextEvidence(
                pageCounter.RawText,
                Normalize(pageCounter.RawText),
                1.0f,
                pageCounter.Accepted,
                pageCounter.Reason);
            noResultsEvidence ??= AchievementTextEvidence.ForTest(
                string.Empty,
                accepted: false,
                reason: "No explicit no-results phrase was supplied.");
            resultRowEvidence ??=
                AchievementResultRowEvidence.ForTest(AchievementResultRowPresence.Unknown);
            int achievementSignals =
                (heading.Accepted ? 1 : 0) +
                (search.Accepted ? 1 : 0) +
                (completedCount.Accepted ? 1 : 0);
            bool strongAchievementScreen = achievementSignals >= 2;
            bool strongIdle = search.IdlePlaceholderAccepted;
            bool anyIdle = search.IdleAccepted;
            bool strongActive = matchCount.Accepted;
            bool contradictoryPositiveMatch =
                matchCount.Accepted && matchCount.Value.GetValueOrDefault() > 0;
            bool strongNoResults = search.ActiveControlsAccepted &&
                (resultRowEvidence.AbsenceAccepted ||
                 (noResultsEvidence.Accepted && !resultRowEvidence.Present)) &&
                !contradictoryPositiveMatch;

            AchievementScreenState state;
            string reason;
            if (strongAchievementScreen && strongIdle && (strongActive || strongNoResults))
            {
                state = AchievementScreenState.Unknown;
                reason = "Strong idle placeholder and active-search evidence conflict.";
            }
            else if (strongAchievementScreen && strongNoResults)
            {
                state = AchievementScreenState.SearchNoResults;
                reason = resultRowEvidence.AbsenceAccepted
                    ? "Achievement screen and populated search controls were recognized, with no first-result row structure present."
                    : "Achievement screen, populated search controls, and the explicit no-results phrase were positively recognized.";
            }
            else if (strongAchievementScreen && strongActive)
            {
                state = AchievementScreenState.SearchActive;
                reason = "Achievement screen has multiple positive signals and a valid Match(es) count.";
            }
            else if (strongAchievementScreen && anyIdle)
            {
                state = AchievementScreenState.Idle;
                reason = search.IdlePlaceholderAccepted
                    ? "Achievement screen has multiple positive signals and the idle placeholder."
                    : "Achievement screen has multiple positive signals and the controller Search prompt.";
            }
            else if (achievementSignals == 0 && !matchCount.Accepted && !pageCounter.Accepted &&
                !noResultsEvidence.Accepted)
            {
                state = AchievementScreenState.NotAchievementScreen;
                reason = "No positive Achievement-screen or search-result evidence was found.";
            }
            else
            {
                state = AchievementScreenState.Unknown;
                reason = strongAchievementScreen
                    ? "Achievement screen is recognized, but neither idle nor active state has sufficient positive evidence."
                    : "Some Achievement-related evidence was found, but fewer than two independent screen signals were accepted.";
            }

            return new AchievementScreenDetection(
                state,
                heading,
                search,
                completedCount,
                matchCountEvidence,
                matchCount,
                pageCounterEvidence,
                pageCounter,
                regions,
                reason,
                noResultsEvidence,
                resultRowEvidence);
        }

        private AchievementTextEvidence ReadSemanticRegion(
            Bitmap screenshot,
            Rectangle region,
            PageSegMode pageMode,
            Func<string, float, SignalEvaluation> evaluate)
        {
            using Bitmap crop = screenshot.Clone(region, PixelFormat.Format24bppRgb);
            using Bitmap resized = GenshinProcesor.ResizeImage(
                crop,
                Math.Max(1, crop.Width * 3),
                Math.Max(1, crop.Height * 3));
            var attempts = new List<AchievementOcrAttempt>(2);

            foreach (bool inverted in new[] { false, true })
            {
                Bitmap processed = imagePreprocessor.ConvertToGrayscale(resized);
                try
                {
                    imagePreprocessor.SetContrast(60.0, ref processed);
                    if (inverted) imagePreprocessor.SetInvert(ref processed);
                    (string text, float confidence) = ocrService.AnalyzeTextWithConfidence(processed, pageMode);
                    string raw = (text ?? string.Empty).Trim();
                    string normalized = Normalize(raw);
                    SignalEvaluation evaluation = evaluate(raw, confidence);
                    attempts.Add(new AchievementOcrAttempt(
                        inverted,
                        raw,
                        normalized,
                        confidence,
                        evaluation.Accepted,
                        evaluation.Reason));
                    if (evaluation.Accepted) break;
                }
                finally
                {
                    processed.Dispose();
                }
            }

            AchievementOcrAttempt selected = attempts
                .OrderByDescending(attempt => attempt.Accepted)
                .ThenByDescending(attempt => attempt.Confidence)
                .First();
            return new AchievementTextEvidence(
                selected.RawText,
                selected.NormalizedText,
                selected.Confidence,
                selected.Accepted,
                selected.Reason,
                attempts);
        }

        private static SignalEvaluation EvaluateHeading(string raw, float confidence)
        {
            string normalized = Normalize(raw);
            bool exact = Regex.IsMatch(normalized, @"(?:^|\s)achievements(?:\s|$)");
            if (exact) return SignalEvaluation.Accept("The Achievements heading token was found.");

            string letters = LettersOnly(normalized);
            bool fuzzy = letters.Length >= 10 && letters.Length <= 14 &&
                Similarity(letters, "achievements") >= 82.0;
            return fuzzy && confidence >= FuzzyMinimumConfidence
                ? SignalEvaluation.Accept("The heading passed the local OCR-tolerant comparison.")
                : SignalEvaluation.Reject("The Achievements heading was not recognized.");
        }

        private static SignalEvaluation EvaluateCompletedCount(string raw, float confidence)
        {
            string normalized = Normalize(raw);
            if (Regex.IsMatch(normalized, @"(?:^|\s)achievements\s+completed(?:\s|$)"))
                return SignalEvaluation.Accept("The Achievements completed label was found.");

            string letters = LettersOnly(normalized);
            bool fuzzy = letters.Length >= 19 &&
                Similarity(letters.Substring(0, Math.Min(letters.Length, 21)), "achievementscompleted") >= 82.0;
            return fuzzy && confidence >= FuzzyMinimumConfidence
                ? SignalEvaluation.Accept("The completed-count label passed the local OCR-tolerant comparison.")
                : SignalEvaluation.Reject("The Achievements completed label was not recognized.");
        }

        private static SignalEvaluation EvaluateSearchField(string raw, float confidence)
        {
            AchievementSearchAnalysis analysis = AnalyzeSearchText(raw, confidence);
            return analysis.Accepted
                ? SignalEvaluation.Accept(analysis.Reason)
                : SignalEvaluation.Reject(analysis.Reason);
        }

        private static SignalEvaluation EvaluateNoResultsMessage(string raw, float confidence)
        {
            const string expected = "noachievementsrelatedto";
            string normalized = Normalize(raw);
            if (normalized.Contains("no achievements related to", StringComparison.Ordinal))
                return SignalEvaluation.Accept("The explicit no-results phrase was found.");

            string letters = LettersOnly(normalized);
            if (confidence >= FuzzyMinimumConfidence && letters.Length >= expected.Length)
            {
                for (int start = 0; start <= letters.Length - expected.Length; start++)
                {
                    if (Similarity(letters.Substring(start, expected.Length), expected) >= 88.0)
                        return SignalEvaluation.Accept(
                            "The explicit no-results phrase passed the local OCR-tolerant comparison.");
                }
            }

            return SignalEvaluation.Reject("The explicit no-results phrase was not recognized.");
        }

        private static AchievementSearchFieldEvidence AnalyzeSearchField(AchievementTextEvidence text)
        {
            AchievementSearchAnalysis analysis = AnalyzeSearchText(text.RawText, text.Confidence);
            return new AchievementSearchFieldEvidence(
                text,
                analysis.IdlePlaceholder,
                analysis.IdleControllerPrompt,
                analysis.ActiveControls);
        }

        private static AchievementSearchAnalysis AnalyzeSearchText(string raw, float confidence)
        {
            string normalized = Normalize(raw);
            string letters = LettersOnly(normalized);
            bool exactPlaceholder = normalized.Contains("search for achievements", StringComparison.Ordinal);
            bool fuzzyPlaceholder = !exactPlaceholder && confidence >= FuzzyMinimumConfidence &&
                letters.Length >= 20 &&
                Similarity(
                    letters.Substring(0, Math.Min(letters.Length, 21)),
                    "searchforachievements") >= 82.0;
            bool searchToken = HasToken(normalized, "search");
            bool clearToken = HasToken(normalized, "clear");
            bool exitToken = HasToken(normalized, "exit");
            // In the bounded search-field crop, Clear/Exit only appears once a query is active.
            // Tesseract can omit the light Search button while still reading Clear reliably.
            bool activeControls = clearToken || exitToken;
            bool controllerIdle = searchToken && !clearToken && !exitToken && !exactPlaceholder && !fuzzyPlaceholder;

            string reason = exactPlaceholder || fuzzyPlaceholder
                ? "The idle Search for Achievements placeholder was found."
                : activeControls
                    ? "Positive active-search controls were found."
                    : controllerIdle
                        ? "A controller Search action prompt was found without active-search controls."
                        : "No idle placeholder or active-search controls were recognized.";
            return new AchievementSearchAnalysis(
                exactPlaceholder || fuzzyPlaceholder,
                controllerIdle,
                activeControls,
                reason);
        }

        private static SignalEvaluation ParseEvaluation(AchievementMatchCountParseResult parse) =>
            parse.Accepted ? SignalEvaluation.Accept(parse.Reason) : SignalEvaluation.Reject(parse.Reason);

        private static SignalEvaluation ParseEvaluation(AchievementPageCounterParseResult parse) =>
            parse.Accepted ? SignalEvaluation.Accept(parse.Reason) : SignalEvaluation.Reject(parse.Reason);

        internal static string Normalize(string text) =>
            Regex.Replace((text ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();

        private static string LettersOnly(string text) =>
            Regex.Replace(text ?? string.Empty, @"[^a-z]", string.Empty);

        private static bool HasToken(string normalized, string token) =>
            Regex.IsMatch(normalized ?? string.Empty, $@"(?:^|\s){Regex.Escape(token)}(?:\s|$)");

        private static double Similarity(string source, string target)
        {
            if (source.Length == 0 || target.Length == 0) return 0;
            if (source == target) return 100;

            var distance = new int[source.Length + 1, target.Length + 1];
            for (int i = 0; i <= source.Length; i++) distance[i, 0] = i;
            for (int j = 0; j <= target.Length; j++) distance[0, j] = j;
            for (int i = 1; i <= source.Length; i++)
            {
                for (int j = 1; j <= target.Length; j++)
                {
                    int cost = source[i - 1] == target[j - 1] ? 0 : 1;
                    distance[i, j] = Math.Min(
                        Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                        distance[i - 1, j - 1] + cost);
                }
            }
            return (1.0 - distance[source.Length, target.Length] /
                (double)Math.Max(source.Length, target.Length)) * 100.0;
        }

        private readonly struct SignalEvaluation
        {
            private SignalEvaluation(bool accepted, string reason)
            {
                Accepted = accepted;
                Reason = reason;
            }

            internal bool Accepted { get; }
            internal string Reason { get; }
            internal static SignalEvaluation Accept(string reason) => new SignalEvaluation(true, reason);
            internal static SignalEvaluation Reject(string reason) => new SignalEvaluation(false, reason);
        }

        private readonly struct AchievementSearchAnalysis
        {
            internal AchievementSearchAnalysis(
                bool idlePlaceholder,
                bool idleControllerPrompt,
                bool activeControls,
                string reason)
            {
                IdlePlaceholder = idlePlaceholder;
                IdleControllerPrompt = idleControllerPrompt;
                ActiveControls = activeControls;
                Reason = reason;
            }

            internal bool IdlePlaceholder { get; }
            internal bool IdleControllerPrompt { get; }
            internal bool ActiveControls { get; }
            internal bool Accepted => IdlePlaceholder || IdleControllerPrompt || ActiveControls;
            internal string Reason { get; }
        }
    }
}

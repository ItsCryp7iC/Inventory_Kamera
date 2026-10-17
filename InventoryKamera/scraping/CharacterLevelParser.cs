using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

namespace InventoryKamera
{
    /// <summary>
    /// Parses one Character Attributes level read and applies the progression constraints needed
    /// before the scanner may trust it. This stays separate from OCR/capture so malformed reads can
    /// be regression-tested without Genshin or Tesseract.
    /// </summary>
    internal static class CharacterLevelParser
    {
        internal const int MinimumLevel = 1;
        internal const int MaximumLevel = 100;

        private static readonly Regex FractionCandidatePattern = new Regex(
            @"(?<![\d-])(\d+)[\s\.'’]*/[\s\.'’]*(\d+)(?!\d)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly HashSet<int> SupportedMaximumLevels = new HashSet<int>
        {
            20, 40, 50, 60, 70, 80, 90, 100,
        };

        internal static CharacterLevelParseResult Parse(string rawText)
        {
            rawText ??= string.Empty;
            string filteredText = Regex.Replace(rawText, @"[^0-9/]", string.Empty);

            var extractedPairs = new List<string>();
            var parsedPairs = new List<NumericPair>();
            var plausiblePairs = new List<NumericPair>();

            foreach (Match match in FractionCandidatePattern.Matches(rawText))
            {
                // Preserve the existing safety behavior for corrupt reads such as
                // "Level 20 4/20": a numeric token between the active Level label and the
                // fraction means the fraction is not an independently delimited level pair.
                if (HasConflictingLevelPrefix(rawText, match)) continue;

                string normalizedPair = $"{match.Groups[1].Value}/{match.Groups[2].Value}";
                extractedPairs.Add(normalizedPair);

                if (!int.TryParse(match.Groups[1].Value, out int candidateLevel)
                    || !int.TryParse(match.Groups[2].Value, out int candidateMaxLevel))
                {
                    continue;
                }

                var pair = new NumericPair(normalizedPair, candidateLevel, candidateMaxLevel);
                parsedPairs.Add(pair);
                if (GetPlausibilityFailure(candidateLevel, candidateMaxLevel) == null) plausiblePairs.Add(pair);
            }

            IReadOnlyList<string> extracted = Array.AsReadOnly(extractedPairs.ToArray());
            IReadOnlyList<string> plausible = Array.AsReadOnly(
                plausiblePairs.ConvertAll(pair => pair.Text).ToArray());

            if (Regex.IsMatch(rawText, @"-\s*\d"))
            {
                return CharacterLevelParseResult.Rejected(
                    rawText,
                    filteredText,
                    extracted,
                    Array.Empty<string>(),
                    rejectionReason: "a signed negative value was present");
            }

            if (plausiblePairs.Count == 0)
            {
                NumericPair parsedPair = parsedPairs.Count == 1 ? parsedPairs[0] : null;
                return CharacterLevelParseResult.Rejected(
                    rawText,
                    filteredText,
                    extracted,
                    plausible,
                    parsedPair?.Level,
                    parsedPair?.MaxLevel,
                    extractedPairs.Count == 0
                        ? "no independently delimited numeric level/max-level pair was found"
                        : "no extracted numeric level/max-level pair satisfied the progression constraints");
            }

            if (plausiblePairs.Count > 1)
            {
                return CharacterLevelParseResult.Rejected(
                    rawText,
                    filteredText,
                    extracted,
                    plausible,
                    rejectionReason: "multiple plausible numeric level/max-level pairs made the OCR result ambiguous");
            }

            NumericPair selected = plausiblePairs[0];
            int level = selected.Level;
            int maxLevel = selected.MaxLevel;
            bool ascended = 20 <= level && level < maxLevel;
            return CharacterLevelParseResult.Accepted(
                rawText,
                filteredText,
                extracted,
                plausible,
                selected.Text,
                level,
                maxLevel,
                ascended);
        }

        private static string GetPlausibilityFailure(int level, int maxLevel)
        {
            if (level < MinimumLevel) return $"level was below {MinimumLevel}";
            if (level > MaximumLevel) return $"level exceeded the supported maximum of {MaximumLevel}";
            if (!SupportedMaximumLevels.Contains(maxLevel))
                return "max level was not a supported character ascension cap";
            if (level > maxLevel) return "level exceeded max level";
            return null;
        }

        private static bool HasConflictingLevelPrefix(string rawText, Match candidate)
        {
            int levelLabel = rawText.LastIndexOf("Level", candidate.Index, StringComparison.OrdinalIgnoreCase);
            if (levelLabel < 0) return false;

            int precedingSlash = rawText.LastIndexOf('/', Math.Max(0, candidate.Index - 1));
            if (precedingSlash > levelLabel) return false;

            int prefixStart = levelLabel + "Level".Length;
            for (int i = prefixStart; i < candidate.Index; i++)
            {
                if (char.IsDigit(rawText[i])) return true;
            }

            return false;
        }

        private sealed class NumericPair
        {
            internal string Text { get; }
            internal int Level { get; }
            internal int MaxLevel { get; }

            internal NumericPair(string text, int level, int maxLevel)
            {
                Text = text;
                Level = level;
                MaxLevel = maxLevel;
            }
        }

        /// <summary>
        /// Invokes <paramref name="readAttempt"/> once per attempt until a plausible pair is found.
        /// Production supplies a fresh screen capture on every invocation; tests can provide a
        /// deterministic sequence of OCR strings.
        /// </summary>
        internal static CharacterLevelParseResult ReadFirstPlausible(
            int maxAttempts,
            Func<int, string> readAttempt,
            Action<int, CharacterLevelParseResult> rejectedAttempt = null)
        {
            return ReadFirstPlausible(
                maxAttempts,
                readAttempt,
                rejectedAttempt,
                waitBeforeRetry: null,
                CancellationToken.None);
        }

        internal static CharacterLevelParseResult ReadFirstPlausible(
            int maxAttempts,
            Func<int, string> readAttempt,
            Action<int, CharacterLevelParseResult> rejectedAttempt,
            Action waitBeforeRetry,
            CancellationToken cancellationToken)
        {
            if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            if (readAttempt == null) throw new ArgumentNullException(nameof(readAttempt));

            CharacterLevelParseResult lastResult = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string rawText = readAttempt(attempt);
                cancellationToken.ThrowIfCancellationRequested();
                lastResult = Parse(rawText);
                cancellationToken.ThrowIfCancellationRequested();
                if (lastResult.Success) return lastResult;
                rejectedAttempt?.Invoke(attempt, lastResult);
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt < maxAttempts)
                {
                    waitBeforeRetry?.Invoke();
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            return lastResult;
        }
    }

    internal sealed class CharacterLevelParseResult
    {
        internal bool Success { get; }
        internal string RawText { get; }
        internal string FilteredText { get; }
        internal IReadOnlyList<string> ExtractedPairs { get; }
        internal IReadOnlyList<string> PlausiblePairs { get; }
        internal string SelectedPair { get; }
        internal int? Level { get; }
        internal int? MaxLevel { get; }
        internal bool Ascended { get; }
        internal string RejectionReason { get; }

        private CharacterLevelParseResult(
            bool success,
            string rawText,
            string filteredText,
            IReadOnlyList<string> extractedPairs,
            IReadOnlyList<string> plausiblePairs,
            string selectedPair,
            int? level,
            int? maxLevel,
            bool ascended,
            string rejectionReason)
        {
            Success = success;
            RawText = rawText;
            FilteredText = filteredText;
            ExtractedPairs = extractedPairs ?? Array.Empty<string>();
            PlausiblePairs = plausiblePairs ?? Array.Empty<string>();
            SelectedPair = selectedPair;
            Level = level;
            MaxLevel = maxLevel;
            Ascended = ascended;
            RejectionReason = rejectionReason;
        }

        internal static CharacterLevelParseResult Accepted(
            string rawText,
            string filteredText,
            IReadOnlyList<string> extractedPairs,
            IReadOnlyList<string> plausiblePairs,
            string selectedPair,
            int level,
            int maxLevel,
            bool ascended) =>
            new CharacterLevelParseResult(
                success: true,
                rawText,
                filteredText,
                extractedPairs,
                plausiblePairs,
                selectedPair,
                level,
                maxLevel,
                ascended,
                rejectionReason: null);

        internal static CharacterLevelParseResult Rejected(
            string rawText,
            string filteredText,
            IReadOnlyList<string> extractedPairs,
            IReadOnlyList<string> plausiblePairs,
            int? level = null,
            int? maxLevel = null,
            string rejectionReason = null) =>
            new CharacterLevelParseResult(
                success: false,
                rawText,
                filteredText,
                extractedPairs,
                plausiblePairs,
                selectedPair: null,
                level,
                maxLevel,
                ascended: false,
                rejectionReason);
    }
}

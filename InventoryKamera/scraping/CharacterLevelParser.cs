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

        private static readonly HashSet<int> SupportedMaximumLevels = new HashSet<int>
        {
            20, 40, 50, 60, 70, 80, 90, 100,
        };

        internal static CharacterLevelParseResult Parse(string rawText)
        {
            rawText ??= string.Empty;
            string filteredText = Regex.Replace(rawText, @"[^0-9/]", string.Empty);

            if (Regex.IsMatch(rawText, @"-\s*\d"))
            {
                return CharacterLevelParseResult.Rejected(
                    rawText,
                    filteredText,
                    rejectionReason: "a signed negative value was present");
            }

            Match pair = Regex.Match(filteredText, @"^(\d+)/(\d+)$");
            if (!pair.Success
                || !int.TryParse(pair.Groups[1].Value, out int level)
                || !int.TryParse(pair.Groups[2].Value, out int maxLevel))
            {
                return CharacterLevelParseResult.Rejected(
                    rawText,
                    filteredText,
                    rejectionReason: "the filtered text was not exactly one numeric level/max-level pair");
            }

            if (level < MinimumLevel)
            {
                return CharacterLevelParseResult.Rejected(
                    rawText, filteredText, level, maxLevel,
                    $"level was below {MinimumLevel}");
            }

            if (level > MaximumLevel)
            {
                return CharacterLevelParseResult.Rejected(
                    rawText, filteredText, level, maxLevel,
                    $"level exceeded the supported maximum of {MaximumLevel}");
            }

            if (!SupportedMaximumLevels.Contains(maxLevel))
            {
                return CharacterLevelParseResult.Rejected(
                    rawText, filteredText, level, maxLevel,
                    "max level was not a supported character ascension cap");
            }

            if (level > maxLevel)
            {
                return CharacterLevelParseResult.Rejected(
                    rawText, filteredText, level, maxLevel,
                    "level exceeded max level");
            }

            bool ascended = 20 <= level && level < maxLevel;
            return CharacterLevelParseResult.Accepted(rawText, filteredText, level, maxLevel, ascended);
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
        internal int? Level { get; }
        internal int? MaxLevel { get; }
        internal bool Ascended { get; }
        internal string RejectionReason { get; }

        private CharacterLevelParseResult(
            bool success,
            string rawText,
            string filteredText,
            int? level,
            int? maxLevel,
            bool ascended,
            string rejectionReason)
        {
            Success = success;
            RawText = rawText;
            FilteredText = filteredText;
            Level = level;
            MaxLevel = maxLevel;
            Ascended = ascended;
            RejectionReason = rejectionReason;
        }

        internal static CharacterLevelParseResult Accepted(
            string rawText,
            string filteredText,
            int level,
            int maxLevel,
            bool ascended) =>
            new CharacterLevelParseResult(
                success: true,
                rawText,
                filteredText,
                level,
                maxLevel,
                ascended,
                rejectionReason: null);

        internal static CharacterLevelParseResult Rejected(
            string rawText,
            string filteredText,
            int? level = null,
            int? maxLevel = null,
            string rejectionReason = null) =>
            new CharacterLevelParseResult(
                success: false,
                rawText,
                filteredText,
                level,
                maxLevel,
                ascended: false,
                rejectionReason);
    }
}

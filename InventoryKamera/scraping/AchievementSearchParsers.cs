using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
    internal readonly struct AchievementMatchCountParseResult
    {
        internal AchievementMatchCountParseResult(string rawText, int? value, string reason)
        {
            RawText = rawText ?? string.Empty;
            Value = value;
            Reason = reason ?? string.Empty;
        }

        internal string RawText { get; }
        internal int? Value { get; }
        internal bool Accepted => Value.HasValue;
        internal string Reason { get; }
    }

    internal static class AchievementMatchCountParser
    {
        internal const int MaximumCount = 9999;

        // The alternatives cover the observed label and conservative OCR punctuation/spacing loss:
        // Match(es), Matches, and Match es. A bare "match" is deliberately not accepted.
        private static readonly Regex CandidatePattern = new Regex(
            @"\bmatch\s*(?:\(\s*e\s*s\s*\)|e\s*s)\s*[:;]?\s*(?<count>-?\d{1,6})\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        internal static AchievementMatchCountParseResult Parse(string rawText)
        {
            string raw = rawText ?? string.Empty;
            MatchCollection matches = CandidatePattern.Matches(raw);
            if (matches.Count == 0)
                return new AchievementMatchCountParseResult(raw, null, "No Match(es) count was found.");

            var values = new List<int>();
            foreach (Match match in matches)
            {
                if (!int.TryParse(
                    match.Groups["count"].Value,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out int value))
                {
                    return new AchievementMatchCountParseResult(raw, null, "The Match(es) count was not a valid integer.");
                }
                if (value < 0)
                    return new AchievementMatchCountParseResult(raw, null, "Negative Match(es) counts are invalid.");
                if (value > MaximumCount)
                    return new AchievementMatchCountParseResult(raw, null, "The Match(es) count exceeded the supported bound.");
                values.Add(value);
            }

            int[] distinct = values.Distinct().ToArray();
            if (distinct.Length != 1)
                return new AchievementMatchCountParseResult(raw, null, "Conflicting Match(es) counts were found.");

            return new AchievementMatchCountParseResult(raw, distinct[0], "A bounded Match(es) count was parsed.");
        }
    }

    internal readonly struct AchievementPageCounterParseResult
    {
        internal AchievementPageCounterParseResult(
            string rawText,
            int? current,
            int? total,
            string reason)
        {
            RawText = rawText ?? string.Empty;
            Current = current;
            Total = total;
            Reason = reason ?? string.Empty;
        }

        internal string RawText { get; }
        internal int? Current { get; }
        internal int? Total { get; }
        internal bool Accepted => Current.HasValue && Total.HasValue;
        internal string Reason { get; }
    }

    internal static class AchievementPageCounterParser
    {
        internal const int MaximumPage = 9999;

        private static readonly Regex CandidatePattern = new Regex(
            @"(?<!\d)(?<current>\d{1,5})\s*[/|]\s*(?<total>\d{1,5})(?!\d)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        internal static AchievementPageCounterParseResult Parse(string rawText)
        {
            string raw = rawText ?? string.Empty;
            MatchCollection matches = CandidatePattern.Matches(raw);
            if (matches.Count == 0)
                return new AchievementPageCounterParseResult(raw, null, null, "No result-page counter was found.");

            var values = new List<(int Current, int Total)>();
            foreach (Match match in matches)
            {
                bool currentParsed = int.TryParse(
                    match.Groups["current"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int current);
                bool totalParsed = int.TryParse(
                    match.Groups["total"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int total);
                if (!currentParsed || !totalParsed || current < 1 || total < 1 ||
                    current > total || current > MaximumPage || total > MaximumPage)
                {
                    return new AchievementPageCounterParseResult(raw, null, null, "The result-page counter was outside valid bounds.");
                }
                values.Add((current, total));
            }

            (int Current, int Total)[] distinct = values.Distinct().ToArray();
            if (distinct.Length != 1)
                return new AchievementPageCounterParseResult(raw, null, null, "Conflicting result-page counters were found.");

            return new AchievementPageCounterParseResult(
                raw,
                distinct[0].Current,
                distinct[0].Total,
                "A bounded result-page counter was parsed.");
        }
    }
}

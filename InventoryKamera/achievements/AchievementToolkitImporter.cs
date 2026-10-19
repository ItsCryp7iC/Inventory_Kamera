using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
    internal enum AchievementImportFailure
    {
        None,
        MalformedJson,
        UnsupportedFormat,
        UnsupportedVersion,
        MalformedAchievementsArray,
        MalformedAchievementEntry,
        MalformedNativeProgress,
        InvalidCompletedAt,
        UnknownCanonicalIds,
    }

    internal sealed class AchievementImportResult
    {
        internal AchievementImportResult(
            AchievementToolkitFormat format,
            bool parseSucceeded,
            AchievementResumeSeed resumeSeed,
            IEnumerable<int> validIds,
            IEnumerable<string> unknownIds,
            IEnumerable<string> duplicateIds,
            AchievementImportFailure failure,
            string failureReason,
            string diagnostics)
        {
            Format = format;
            ParseSucceeded = parseSucceeded;
            ResumeSeed = resumeSeed;
            SeedCreationSucceeded = resumeSeed != null;
            ValidIds = new ReadOnlyCollection<int>(
                (validIds ?? Array.Empty<int>()).OrderBy(id => id).ToArray());
            UnknownIds = new ReadOnlyCollection<string>(
                OrderIdText(unknownIds).ToArray());
            DuplicateIds = new ReadOnlyCollection<string>(
                OrderIdText(duplicateIds).ToArray());
            Failure = failure;
            FailureReason = failureReason ?? string.Empty;
            Diagnostics = diagnostics ?? string.Empty;
        }

        internal bool Success => ParseSucceeded && SeedCreationSucceeded;
        internal AchievementToolkitFormat Format { get; }
        internal bool ParseSucceeded { get; }
        internal bool SeedCreationSucceeded { get; }
        internal IReadOnlyList<int> ValidIds { get; }
        internal IReadOnlyList<string> UnknownIds { get; }
        internal IReadOnlyList<string> DuplicateIds { get; }
        internal AchievementResumeSeed ResumeSeed { get; }
        internal AchievementImportFailure Failure { get; }
        internal string FailureReason { get; }
        internal string Diagnostics { get; }

        private static IOrderedEnumerable<string> OrderIdText(IEnumerable<string> values) =>
            (values ?? Array.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => ParseOrder(value))
                .ThenBy(value => value, StringComparer.Ordinal);

        private static int ParseOrder(string value) =>
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                ? id
                : int.MaxValue;
    }

    /// <summary>
    /// Parses untrusted achievement JSON without type-name materialization or file-system access.
    /// Structural validity and readiness for a trusted Phase 6I seed are reported independently.
    /// </summary>
    internal sealed class AchievementToolkitImporter
    {
        private static readonly Regex StrictDigits = new(@"^\d+$", RegexOptions.CultureInvariant);
        private static readonly Regex StrictCanonicalTimestamp = new(
            @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$",
            RegexOptions.CultureInvariant);
        private static readonly HashSet<string> IgnoredPrototypeKeys = new(
            new[] { "__proto__", "constructor", "prototype" },
            StringComparer.Ordinal);

        private readonly AchievementCatalog catalog;

        internal AchievementToolkitImporter(IAchievementCatalogProvider catalogProvider)
        {
            if (catalogProvider == null) throw new ArgumentNullException(nameof(catalogProvider));
            catalog = catalogProvider.Catalog ?? throw new ArgumentException(
                "The catalog provider returned no catalog.", nameof(catalogProvider));
        }

        internal AchievementImportResult Import(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Failure(
                    AchievementToolkitFormat.Unsupported,
                    parseSucceeded: false,
                    AchievementImportFailure.MalformedJson,
                    "Achievement import JSON is empty.");

            JToken rootToken;
            try
            {
                using var stringReader = new StringReader(json);
                using var jsonReader = new JsonTextReader(stringReader)
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Decimal,
                    MaxDepth = 64,
                };
                rootToken = JToken.ReadFrom(
                    jsonReader,
                    new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Ignore,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Load,
                    });
                if (jsonReader.Read())
                    return Failure(
                        AchievementToolkitFormat.Unsupported,
                        parseSucceeded: false,
                        AchievementImportFailure.MalformedJson,
                        "Achievement import contains content after the root value.");
            }
            catch (Exception ex) when (ex is JsonException || ex is OverflowException)
            {
                return Failure(
                    AchievementToolkitFormat.Unsupported,
                    parseSucceeded: false,
                    AchievementImportFailure.MalformedJson,
                    $"Achievement import JSON is malformed ({ex.GetType().Name}).");
            }

            if (!(rootToken is JObject root))
                return Failure(
                    AchievementToolkitFormat.Unsupported,
                    parseSucceeded: false,
                    AchievementImportFailure.MalformedJson,
                    "Achievement import root must be a JSON object.");

            if (root.TryGetValue("format", StringComparison.Ordinal, out JToken formatToken) &&
                formatToken.Type == JTokenType.String &&
                string.Equals(
                    formatToken.Value<string>(),
                    AchievementToolkitExporter.NativeFormatName,
                    StringComparison.Ordinal))
            {
                return ImportNative(root);
            }

            if (root.TryGetValue("achievements", StringComparison.Ordinal, out JToken achievementsToken))
                return ImportGood(achievementsToken);

            return Failure(
                AchievementToolkitFormat.Unsupported,
                parseSucceeded: false,
                AchievementImportFailure.UnsupportedFormat,
                "The JSON object contains no supported achievement data.");
        }

        private AchievementImportResult ImportGood(JToken achievementsToken)
        {
            const AchievementToolkitFormat format = AchievementToolkitFormat.Good;
            if (!(achievementsToken is JArray achievements))
                return Failure(
                    format,
                    parseSucceeded: false,
                    AchievementImportFailure.MalformedAchievementsArray,
                    "The achievements property must be an array.");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var valid = new HashSet<int>();
            var unknown = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);

            for (int index = 0; index < achievements.Count; index++)
            {
                if (!TryReadStrictId(achievements[index], out string idText, out int id))
                    return Failure(
                        format,
                        parseSucceeded: false,
                        AchievementImportFailure.MalformedAchievementEntry,
                        $"Achievement entry at index {index.ToString(CultureInfo.InvariantCulture)} must be a bounded positive decimal integer or digit string.",
                        valid,
                        unknown,
                        duplicates);

                if (!seen.Add(idText))
                {
                    duplicates.Add(idText);
                    continue;
                }

                if (IsCanonicalIdText(idText, id))
                    valid.Add(id);
                else
                    unknown.Add(idText);
            }

            return Complete(format, valid, unknown, duplicates);
        }

        private AchievementImportResult ImportNative(JObject root)
        {
            const AchievementToolkitFormat format = AchievementToolkitFormat.TravelersToolkitNative;
            if (!root.TryGetValue("version", StringComparison.Ordinal, out JToken versionToken) ||
                versionToken.Type != JTokenType.Integer ||
                !TryReadInt32(versionToken, out int version) ||
                version != AchievementToolkitExporter.NativeFormatVersion)
            {
                return Failure(
                    format,
                    parseSucceeded: false,
                    AchievementImportFailure.UnsupportedVersion,
                    "Unsupported Traveler's Toolkit achievement format version.");
            }

            if (!root.TryGetValue(
                    "achievementProgress",
                    StringComparison.Ordinal,
                    out JToken progressToken) ||
                !(progressToken is JObject progress))
            {
                return Failure(
                    format,
                    parseSucceeded: false,
                    AchievementImportFailure.MalformedNativeProgress,
                    "Native achievementProgress must be a JSON object.");
            }

            var valid = new HashSet<int>();
            var unknown = new HashSet<string>(StringComparer.Ordinal);
            foreach (JProperty property in progress.Properties())
            {
                if (IgnoredPrototypeKeys.Contains(property.Name))
                    continue;
                if (!(property.Value is JObject entry))
                    return Failure(
                        format,
                        parseSucceeded: false,
                        AchievementImportFailure.MalformedNativeProgress,
                        "Native achievement progress entries must be JSON objects.",
                        valid,
                        unknown);

                bool completed = entry.TryGetValue(
                    "completed",
                    StringComparison.Ordinal,
                    out JToken completedToken) &&
                    completedToken.Type == JTokenType.Boolean &&
                    completedToken.Value<bool>();
                if (!completed)
                    continue;

                if (!entry.TryGetValue(
                        "completedAt",
                        StringComparison.Ordinal,
                        out JToken completedAtToken) ||
                    !IsValidCompletedAt(completedAtToken))
                {
                    return Failure(
                        format,
                        parseSucceeded: false,
                        AchievementImportFailure.InvalidCompletedAt,
                        "A completed native achievement has an invalid completedAt timestamp.",
                        valid,
                        unknown);
                }

                if (!TryReadStrictId(property.Name, out string idText, out int id))
                    return Failure(
                        format,
                        parseSucceeded: false,
                        AchievementImportFailure.MalformedNativeProgress,
                        "A completed native achievement key must be a bounded positive decimal integer.",
                        valid,
                        unknown);

                if (IsCanonicalIdText(idText, id))
                    valid.Add(id);
                else
                    unknown.Add(idText);
            }

            return Complete(format, valid, unknown, Array.Empty<string>());
        }

        private AchievementImportResult Complete(
            AchievementToolkitFormat format,
            IEnumerable<int> validIds,
            IEnumerable<string> unknownIds,
            IEnumerable<string> duplicateIds)
        {
            int[] valid = validIds.Distinct().OrderBy(id => id).ToArray();
            string[] unknown = unknownIds.Distinct(StringComparer.Ordinal).ToArray();
            string[] duplicates = duplicateIds.Distinct(StringComparer.Ordinal).ToArray();
            if (unknown.Length > 0)
                return Failure(
                    format,
                    parseSucceeded: true,
                    AchievementImportFailure.UnknownCanonicalIds,
                    "Unknown canonical achievement IDs prevent creation of a trusted resume seed.",
                    valid,
                    unknown,
                    duplicates);

            var seed = new AchievementResumeSeed(valid);
            string diagnostics = BuildDiagnostics(
                format,
                parseSucceeded: true,
                seedReady: true,
                valid.Length,
                unknown.Length,
                duplicates.Length,
                failureReason: string.Empty);
            return new AchievementImportResult(
                format,
                parseSucceeded: true,
                seed,
                valid,
                unknown,
                duplicates,
                AchievementImportFailure.None,
                failureReason: string.Empty,
                diagnostics);
        }

        private static AchievementImportResult Failure(
            AchievementToolkitFormat format,
            bool parseSucceeded,
            AchievementImportFailure failure,
            string reason,
            IEnumerable<int> validIds = null,
            IEnumerable<string> unknownIds = null,
            IEnumerable<string> duplicateIds = null)
        {
            int[] valid = (validIds ?? Array.Empty<int>()).Distinct().OrderBy(id => id).ToArray();
            string[] unknown = (unknownIds ?? Array.Empty<string>()).ToArray();
            string[] duplicates = (duplicateIds ?? Array.Empty<string>()).ToArray();
            string boundedReason = Bound(reason, 240);
            string diagnostics = BuildDiagnostics(
                format,
                parseSucceeded,
                seedReady: false,
                valid.Length,
                unknown.Distinct(StringComparer.Ordinal).Count(),
                duplicates.Distinct(StringComparer.Ordinal).Count(),
                boundedReason);
            return new AchievementImportResult(
                format,
                parseSucceeded,
                resumeSeed: null,
                valid,
                unknown,
                duplicates,
                failure,
                boundedReason,
                diagnostics);
        }

        private bool IsCanonicalIdText(string idText, int id) =>
            string.Equals(
                idText,
                id.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal) &&
            catalog.GetById(id) != null;

        private static bool TryReadStrictId(JToken token, out string idText, out int id)
        {
            if (token == null ||
                (token.Type != JTokenType.Integer && token.Type != JTokenType.String))
            {
                idText = null;
                id = 0;
                return false;
            }

            idText = token.Type == JTokenType.String
                ? token.Value<string>()
                : token.ToString(Formatting.None);
            return TryReadStrictId(idText, out idText, out id);
        }

        private static bool TryReadStrictId(string value, out string idText, out int id)
        {
            idText = value;
            id = 0;
            return !string.IsNullOrEmpty(value) &&
                StrictDigits.IsMatch(value) &&
                int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) &&
                id > 0;
        }

        private static bool TryReadInt32(JToken token, out int value)
        {
            string text = token.ToString(Formatting.None);
            return int.TryParse(
                text,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static bool IsValidCompletedAt(JToken token)
        {
            if (token.Type == JTokenType.Null)
                return true;
            if (token.Type != JTokenType.String)
                return false;

            string value = token.Value<string>();
            if (!StrictCanonicalTimestamp.IsMatch(value ?? string.Empty))
                return false;
            if (!DateTimeOffset.TryParseExact(
                    value,
                    "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset parsed))
            {
                return false;
            }

            return string.Equals(
                parsed.UtcDateTime.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                    CultureInfo.InvariantCulture),
                value,
                StringComparison.Ordinal);
        }

        private static string BuildDiagnostics(
            AchievementToolkitFormat format,
            bool parseSucceeded,
            bool seedReady,
            int validIds,
            int unknownIds,
            int duplicateIds,
            string failureReason)
        {
            var builder = new StringBuilder(360);
            builder.AppendLine("operation=achievement-import");
            builder.AppendLine($"format={format}");
            builder.AppendLine($"parseSucceeded={parseSucceeded}");
            builder.AppendLine($"seedReady={seedReady}");
            builder.AppendLine($"validIds={validIds.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"unknownIds={unknownIds.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"duplicateIds={duplicateIds.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"failureReason={failureReason}");
            return builder.ToString().TrimEnd();
        }

        private static string Bound(string value, int maximumLength)
        {
            string cleaned = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
            return cleaned.Length <= maximumLength
                ? cleaned
                : cleaned.Substring(0, maximumLength) + "...";
        }
    }
}

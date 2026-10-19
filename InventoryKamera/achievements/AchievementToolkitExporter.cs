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
    internal enum AchievementToolkitFormat
    {
        Unsupported,
        Good,
        TravelersToolkitNative,
    }

    internal enum AchievementExportFailure
    {
        None,
        InvalidExportIds,
    }

    internal sealed class AchievementToolkitExportResult
    {
        internal AchievementToolkitExportResult(
            bool success,
            AchievementToolkitFormat format,
            int inputIdCount,
            IEnumerable<int> canonicalIds,
            IEnumerable<int> invalidIds,
            string json,
            AchievementExportFailure failure,
            string failureReason,
            string diagnostics)
        {
            Success = success;
            Format = format;
            InputIdCount = inputIdCount;
            CanonicalIds = new ReadOnlyCollection<int>(
                (canonicalIds ?? Array.Empty<int>()).ToArray());
            InvalidIds = new ReadOnlyCollection<int>(
                (invalidIds ?? Array.Empty<int>()).ToArray());
            Json = json;
            Failure = failure;
            FailureReason = failureReason ?? string.Empty;
            Diagnostics = diagnostics ?? string.Empty;
        }

        internal bool Success { get; }
        internal AchievementToolkitFormat Format { get; }
        internal int InputIdCount { get; }
        internal IReadOnlyList<int> CanonicalIds { get; }
        internal IReadOnlyList<int> InvalidIds { get; }
        internal string Json { get; }
        internal AchievementExportFailure Failure { get; }
        internal string FailureReason { get; }
        internal string Diagnostics { get; }
    }

    /// <summary>
    /// Serializes already-trusted canonical completion IDs into Traveler's Toolkit-compatible
    /// documents. File selection and persistence remain outside this UI-independent component.
    /// </summary>
    internal sealed class AchievementToolkitExporter
    {
        internal const string NativeFormatName = "TRAVELERS_TOOLKIT_ACHIEVEMENTS";
        internal const int NativeFormatVersion = 1;

        private readonly AchievementCatalog catalog;

        internal AchievementToolkitExporter(IAchievementCatalogProvider catalogProvider)
        {
            if (catalogProvider == null) throw new ArgumentNullException(nameof(catalogProvider));
            catalog = catalogProvider.Catalog ?? throw new ArgumentException(
                "The catalog provider returned no catalog.", nameof(catalogProvider));
        }

        internal AchievementToolkitExportResult ExportGood(IEnumerable<int> completedIds) =>
            Export(completedIds, AchievementToolkitFormat.Good);

        internal AchievementToolkitExportResult ExportNative(IEnumerable<int> completedIds) =>
            Export(completedIds, AchievementToolkitFormat.TravelersToolkitNative);

        private AchievementToolkitExportResult Export(
            IEnumerable<int> completedIds,
            AchievementToolkitFormat format)
        {
            if (completedIds == null)
                return Failure(
                    format,
                    inputIdCount: 0,
                    canonicalIds: Array.Empty<int>(),
                    invalidIds: Array.Empty<int>(),
                    "No trusted completed-ID collection was supplied.");

            int[] input = completedIds.ToArray();
            int[] invalid = input
                .Where(id => id <= 0 || catalog.GetById(id) == null)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();
            int[] canonical = input
                .Where(id => id > 0 && catalog.GetById(id) != null)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            if (invalid.Length > 0)
                return Failure(
                    format,
                    input.Length,
                    canonical,
                    invalid,
                    "The trusted completion collection contains invalid or unknown canonical achievement IDs.");

            JObject document = format == AchievementToolkitFormat.Good
                ? CreateGoodDocument(canonical)
                : CreateNativeDocument(canonical);
            string json = Serialize(document);
            string diagnostics = BuildDiagnostics(
                format,
                input.Length,
                canonical.Length,
                success: true,
                failureReason: string.Empty);
            return new AchievementToolkitExportResult(
                success: true,
                format,
                input.Length,
                canonical,
                Array.Empty<int>(),
                json,
                AchievementExportFailure.None,
                failureReason: string.Empty,
                diagnostics);
        }

        private static JObject CreateGoodDocument(IEnumerable<int> canonicalIds) =>
            new JObject
            {
                ["achievements"] = new JArray(canonicalIds),
            };

        private static JObject CreateNativeDocument(IEnumerable<int> canonicalIds)
        {
            var progress = new JObject();
            foreach (int id in canonicalIds)
            {
                progress.Add(
                    id.ToString(CultureInfo.InvariantCulture),
                    new JObject
                    {
                        ["completed"] = true,
                        ["completedAt"] = JValue.CreateNull(),
                    });
            }

            return new JObject
            {
                ["format"] = NativeFormatName,
                ["version"] = NativeFormatVersion,
                ["achievementProgress"] = progress,
            };
        }

        private static string Serialize(JObject document)
        {
            var builder = new StringBuilder();
            using var textWriter = new StringWriter(builder, CultureInfo.InvariantCulture);
            textWriter.NewLine = "\n";
            using var jsonWriter = new JsonTextWriter(textWriter)
            {
                Formatting = Formatting.Indented,
                IndentChar = ' ',
                Indentation = 2,
                CloseOutput = false,
            };
            document.WriteTo(jsonWriter);
            jsonWriter.Flush();
            return builder.ToString();
        }

        private static AchievementToolkitExportResult Failure(
            AchievementToolkitFormat format,
            int inputIdCount,
            IEnumerable<int> canonicalIds,
            IEnumerable<int> invalidIds,
            string reason)
        {
            string boundedReason = Bound(reason, 240);
            int[] canonical = canonicalIds.ToArray();
            int[] invalid = invalidIds.ToArray();
            string diagnostics = BuildDiagnostics(
                format,
                inputIdCount,
                canonical.Length,
                success: false,
                boundedReason);
            return new AchievementToolkitExportResult(
                success: false,
                format,
                inputIdCount,
                canonical,
                invalid,
                json: null,
                AchievementExportFailure.InvalidExportIds,
                boundedReason,
                diagnostics);
        }

        private static string BuildDiagnostics(
            AchievementToolkitFormat format,
            int inputIds,
            int canonicalIds,
            bool success,
            string failureReason)
        {
            var builder = new StringBuilder(280);
            builder.AppendLine("operation=achievement-export");
            builder.AppendLine($"format={format}");
            builder.AppendLine($"inputIds={inputIds.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"canonicalIds={canonicalIds.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"success={success}");
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

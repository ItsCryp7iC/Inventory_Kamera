using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace InventoryKamera
{
    /// <summary>
    /// Developer-only transformation from Traveler's Toolkit's canonical achievements.json to the
    /// scanner snapshot. The runtime provider never accesses the Toolkit repository or the network.
    /// </summary>
    public static class AchievementCatalogGenerator
    {
        public const string SourceIdentifier =
            "ItsCryp7iC/travelers-toolkit:src/data/achievements/achievements.json";

        public static AchievementCatalogGenerationResult Generate(string sourcePath, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("A Toolkit achievements.json path is required.", nameof(sourcePath));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("An output path is required.", nameof(outputPath));

            string sourceJson = File.ReadAllText(sourcePath, Encoding.UTF8);
            AchievementCatalogGenerationResult result = Generate(sourceJson, out string snapshotJson);
            string fullOutputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath));
            File.WriteAllText(fullOutputPath, snapshotJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return result;
        }

        public static AchievementCatalogGenerationResult Generate(
            string sourceJson,
            out string snapshotJson)
        {
            if (sourceJson == null) throw new ArgumentNullException(nameof(sourceJson));

            JArray source = ParseSource(sourceJson);
            var entries = new List<AchievementCatalogEntry>(source.Count);
            for (int index = 0; index < source.Count; index++)
            {
                if (!(source[index] is JObject item))
                    throw new AchievementCatalogException(
                        $"Toolkit achievement at index {index} must be an object.");
                entries.Add(ReadSourceEntry(item, index));
            }

            var catalog = new AchievementCatalog(entries);
            snapshotJson = Serialize(catalog.All);
            return Analyze(catalog);
        }

        private static JArray ParseSource(string sourceJson)
        {
            try
            {
                using var stringReader = new StringReader(sourceJson);
                using var jsonReader = new JsonTextReader(stringReader)
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Decimal,
                };
                JToken token = JToken.ReadFrom(jsonReader, new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Ignore,
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    LineInfoHandling = LineInfoHandling.Load,
                });
                if (jsonReader.Read())
                    throw new AchievementCatalogException(
                        "Toolkit achievement source contains content after the root value.");
                return token as JArray
                    ?? throw new AchievementCatalogException(
                        "Toolkit achievement source root must be an array.");
            }
            catch (AchievementCatalogException)
            {
                throw;
            }
            catch (JsonException ex)
            {
                throw new AchievementCatalogException("Toolkit achievement source JSON is malformed.", ex);
            }
        }

        private static AchievementCatalogEntry ReadSourceEntry(JObject item, int index)
        {
            int id = ReadCanonicalIntegerString(item, "id", index, minimum: 1);
            int categoryId = ReadCanonicalIntegerString(item, "categoryId", index, minimum: 0);
            string name = ReadSourceString(item, "name", index, allowEmpty: false);
            string description = ReadSourceString(item, "description", index, allowEmpty: true);
            int primogems = ReadSourceInteger(item, "primogems", index, minimum: 0);
            bool hidden = ReadSourceBoolean(item, "hidden", index);
            string version = ReadSourceString(item, "version", index, allowEmpty: false);
            int order = ReadSourceInteger(item, "order", index, minimum: 0);

            bool hasGroup = item.TryGetValue("stageGroupId", StringComparison.Ordinal, out JToken groupToken);
            bool hasIndex = item.TryGetValue("stageIndex", StringComparison.Ordinal, out JToken stageIndexToken);
            bool hasCount = item.TryGetValue("stageCount", StringComparison.Ordinal, out JToken stageCountToken);
            if ((hasGroup ? 1 : 0) + (hasIndex ? 1 : 0) + (hasCount ? 1 : 0) is not (0 or 3))
                throw new AchievementCatalogException(
                    $"Toolkit achievement '{id}' has incomplete stage metadata.");

            int? stageGroupId = hasGroup
                ? ParseCanonicalIntegerString(groupToken, "stageGroupId", index, minimum: 1)
                : null;
            int? stageIndex = hasIndex
                ? ReadSourceInteger(stageIndexToken, "stageIndex", index, minimum: 1)
                : null;
            int? stageCount = hasCount
                ? ReadSourceInteger(stageCountToken, "stageCount", index, minimum: 1)
                : null;

            return new AchievementCatalogEntry(
                id,
                name,
                description,
                categoryId,
                hidden,
                version,
                order,
                stageGroupId,
                stageIndex,
                stageCount,
                primogems);
        }

        private static string Serialize(IReadOnlyList<AchievementCatalogEntry> entries)
        {
            var builder = new StringBuilder(capacity: 512 * 1024);
            using var stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture)
            {
                NewLine = "\n",
            };
            using (var writer = new JsonTextWriter(stringWriter)
            {
                Formatting = Formatting.Indented,
                Indentation = 2,
                IndentChar = ' ',
                Culture = CultureInfo.InvariantCulture,
                StringEscapeHandling = StringEscapeHandling.Default,
            })
            {
                writer.WriteStartObject();
                writer.WritePropertyName("schemaVersion");
                writer.WriteValue(AchievementCatalogLoader.SupportedSchemaVersion);
                writer.WritePropertyName("source");
                writer.WriteValue(SourceIdentifier);
                writer.WritePropertyName("achievements");
                writer.WriteStartArray();
                foreach (AchievementCatalogEntry entry in entries)
                {
                    writer.WriteStartObject();
                    Write(writer, "id", entry.Id);
                    Write(writer, "name", entry.Name);
                    Write(writer, "description", entry.Description);
                    Write(writer, "primogems", entry.Primogems);
                    Write(writer, "categoryId", entry.CategoryId);
                    Write(writer, "hidden", entry.Hidden);
                    Write(writer, "version", entry.Version);
                    Write(writer, "order", entry.Order);
                    if (entry.IsStaged)
                    {
                        Write(writer, "stageGroupId", entry.StageGroupId.Value);
                        Write(writer, "stageIndex", entry.StageIndex.Value);
                        Write(writer, "stageCount", entry.StageCount.Value);
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            builder.Append('\n');
            return builder.ToString();
        }

        private static AchievementCatalogGenerationResult Analyze(AchievementCatalog catalog)
        {
            IGrouping<string, AchievementCatalogEntry>[] duplicateNames = catalog.All
                .GroupBy(entry => entry.Name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .ToArray();
            int stagedDuplicateNames = duplicateNames.Count(group =>
                group.All(entry => entry.StageGroupId.HasValue)
                && group.Select(entry => entry.StageGroupId.Value).Distinct().Count() == 1);
            string[] nonStageDuplicateNames = duplicateNames
                .Where(group => !(group.All(entry => entry.StageGroupId.HasValue)
                    && group.Select(entry => entry.StageGroupId.Value).Distinct().Count() == 1))
                .Select(group => group.Key)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            IGrouping<int, AchievementCatalogEntry>[] stageGroups = catalog.All
                .Where(entry => entry.StageGroupId.HasValue)
                .GroupBy(entry => entry.StageGroupId.Value)
                .ToArray();
            int rewardResolvableStageGroups = stageGroups.Count(group =>
                group.Select(entry => entry.Primogems).Distinct().Count() == group.Count());
            string[] rewardAmbiguousStageGroupNames = stageGroups
                .Where(group => group.Select(entry => entry.Primogems).Distinct().Count() != group.Count())
                .Select(group => group.First().Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            return new AchievementCatalogGenerationResult(
                catalog.All.Count,
                catalog.All.Count(entry => entry.Hidden),
                catalog.All.Count(entry => !entry.Hidden),
                catalog.All.Count(entry => entry.IsStaged),
                catalog.All.Where(entry => entry.StageGroupId.HasValue)
                    .Select(entry => entry.StageGroupId.Value).Distinct().Count(),
                duplicateNames.Length,
                stagedDuplicateNames,
                nonStageDuplicateNames,
                rewardResolvableStageGroups,
                rewardAmbiguousStageGroupNames);
        }

        private static void Write(JsonWriter writer, string name, object value)
        {
            writer.WritePropertyName(name);
            writer.WriteValue(value);
        }

        private static int ReadCanonicalIntegerString(
            JObject item,
            string propertyName,
            int index,
            int minimum)
        {
            if (!item.TryGetValue(propertyName, StringComparison.Ordinal, out JToken token))
                throw SourceError(propertyName, index, "is required");
            return ParseCanonicalIntegerString(token, propertyName, index, minimum);
        }

        private static int ParseCanonicalIntegerString(
            JToken token,
            string propertyName,
            int index,
            int minimum)
        {
            if (token?.Type != JTokenType.String)
                throw SourceError(propertyName, index, "must be a decimal string");
            string text = token.Value<string>();
            if (string.IsNullOrEmpty(text)
                || (text.Length > 1 && text[0] == '0')
                || text.Any(character => character < '0' || character > '9')
                || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                || value < minimum)
            {
                throw SourceError(propertyName, index, "must be a canonical non-negative decimal string");
            }
            return value;
        }

        private static int ReadSourceInteger(
            JObject item,
            string propertyName,
            int index,
            int minimum)
        {
            if (!item.TryGetValue(propertyName, StringComparison.Ordinal, out JToken token))
                throw SourceError(propertyName, index, "is required");
            return ReadSourceInteger(token, propertyName, index, minimum);
        }

        private static int ReadSourceInteger(
            JToken token,
            string propertyName,
            int index,
            int minimum)
        {
            if (token?.Type != JTokenType.Integer)
                throw SourceError(propertyName, index, "must be an integer");
            long value = token.Value<long>();
            if (value < minimum || value > int.MaxValue)
                throw SourceError(propertyName, index, "is outside the supported integer range");
            return (int)value;
        }

        private static string ReadSourceString(
            JObject item,
            string propertyName,
            int index,
            bool allowEmpty)
        {
            if (!item.TryGetValue(propertyName, StringComparison.Ordinal, out JToken token))
                throw SourceError(propertyName, index, "is required");
            if (token?.Type != JTokenType.String)
                throw SourceError(propertyName, index, "must be a string");
            string value = token.Value<string>();
            if (!allowEmpty && string.IsNullOrWhiteSpace(value))
                throw SourceError(propertyName, index, "must not be empty");
            return value;
        }

        private static bool ReadSourceBoolean(JObject item, string propertyName, int index)
        {
            if (!item.TryGetValue(propertyName, StringComparison.Ordinal, out JToken token))
                throw SourceError(propertyName, index, "is required");
            if (token?.Type != JTokenType.Boolean)
                throw SourceError(propertyName, index, "must be a boolean");
            return token.Value<bool>();
        }

        private static AchievementCatalogException SourceError(
            string propertyName,
            int index,
            string problem) =>
            new AchievementCatalogException(
                $"Toolkit achievement at index {index} property '{propertyName}' {problem}.");
    }

    public sealed class AchievementCatalogGenerationResult
    {
        internal AchievementCatalogGenerationResult(
            int totalCount,
            int hiddenCount,
            int nonHiddenCount,
            int stagedEntryCount,
            int stageGroupCount,
            int duplicateNameCount,
            int stagedDuplicateNameCount,
            IEnumerable<string> nonStageDuplicateNames,
            int rewardResolvableStageGroupCount,
            IEnumerable<string> rewardAmbiguousStageGroupNames)
        {
            TotalCount = totalCount;
            HiddenCount = hiddenCount;
            NonHiddenCount = nonHiddenCount;
            StagedEntryCount = stagedEntryCount;
            StageGroupCount = stageGroupCount;
            DuplicateNameCount = duplicateNameCount;
            StagedDuplicateNameCount = stagedDuplicateNameCount;
            NonStageDuplicateNames = new ReadOnlyCollection<string>(nonStageDuplicateNames.ToArray());
            RewardResolvableStageGroupCount = rewardResolvableStageGroupCount;
            RewardAmbiguousStageGroupNames = new ReadOnlyCollection<string>(
                rewardAmbiguousStageGroupNames.ToArray());
        }

        public int TotalCount { get; }
        public int HiddenCount { get; }
        public int NonHiddenCount { get; }
        public int StagedEntryCount { get; }
        public int StageGroupCount { get; }
        public int DuplicateNameCount { get; }
        public int StagedDuplicateNameCount { get; }
        public IReadOnlyList<string> NonStageDuplicateNames { get; }
        public int RewardResolvableStageGroupCount { get; }
        public IReadOnlyList<string> RewardAmbiguousStageGroupNames { get; }
    }
}

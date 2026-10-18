using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace InventoryKamera
{
    public static class AchievementCatalogLoader
    {
        public const int SupportedSchemaVersion = 1;
        internal const string PackagedResourceName =
            "InventoryKamera.Resources.Data.achievements-scanner.json";

        public static AchievementCatalog LoadPackaged()
        {
            Assembly assembly = typeof(AchievementCatalogLoader).Assembly;
            using Stream stream = assembly.GetManifestResourceStream(PackagedResourceName)
                ?? throw new FileNotFoundException(
                    $"Packaged achievement catalog resource '{PackagedResourceName}' was not found.");
            return Load(stream);
        }

        public static AchievementCatalog Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A path is required.", nameof(path));
            using FileStream stream = File.OpenRead(path);
            return Load(stream);
        }

        public static AchievementCatalog Load(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            try
            {
                using var streamReader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                using var jsonReader = new JsonTextReader(streamReader)
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Decimal,
                };
                var settings = new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Ignore,
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    LineInfoHandling = LineInfoHandling.Load,
                };
                JToken rootToken = JToken.ReadFrom(jsonReader, settings);
                if (jsonReader.Read())
                    throw new AchievementCatalogException(
                        "Achievement catalog contains content after the root value.");
                if (!(rootToken is JObject root))
                    throw new AchievementCatalogException("Achievement catalog root must be an object.");

                int schemaVersion = ReadInteger(root, "schemaVersion", minimum: 1);
                if (schemaVersion != SupportedSchemaVersion)
                    throw new AchievementCatalogException(
                        $"Unsupported achievement catalog schema version '{schemaVersion}'.");

                if (!root.TryGetValue("achievements", StringComparison.Ordinal, out JToken achievementsToken)
                    || !(achievementsToken is JArray achievements))
                {
                    throw new AchievementCatalogException(
                        "Achievement catalog must contain an achievements array.");
                }

                var entries = new List<AchievementCatalogEntry>(achievements.Count);
                for (int index = 0; index < achievements.Count; index++)
                {
                    if (!(achievements[index] is JObject item))
                        throw new AchievementCatalogException(
                            $"Achievement at index {index} must be an object.");
                    entries.Add(ReadEntry(item, index));
                }

                return new AchievementCatalog(entries);
            }
            catch (AchievementCatalogException)
            {
                throw;
            }
            catch (Exception ex) when (ex is JsonException || ex is OverflowException || ex is InvalidCastException)
            {
                throw new AchievementCatalogException("Achievement catalog JSON is malformed.", ex);
            }
        }

        private static AchievementCatalogEntry ReadEntry(JObject item, int index)
        {
            int id = ReadInteger(item, "id", minimum: 1, context: index);
            string name = ReadString(item, "name", index, allowEmpty: false);
            string description = ReadString(item, "description", index, allowEmpty: true);
            int categoryId = ReadInteger(item, "categoryId", minimum: 0, context: index);
            bool hidden = ReadBoolean(item, "hidden", index);
            string version = ReadString(item, "version", index, allowEmpty: false);
            int order = ReadInteger(item, "order", minimum: 0, context: index);

            bool hasGroup = item.TryGetValue("stageGroupId", StringComparison.Ordinal, out JToken groupToken);
            bool hasIndex = item.TryGetValue("stageIndex", StringComparison.Ordinal, out JToken stageIndexToken);
            bool hasCount = item.TryGetValue("stageCount", StringComparison.Ordinal, out JToken stageCountToken);
            if ((hasGroup ? 1 : 0) + (hasIndex ? 1 : 0) + (hasCount ? 1 : 0) is not (0 or 3))
                throw new AchievementCatalogException(
                    $"Achievement '{id}' has incomplete stage metadata.");

            int? stageGroupId = hasGroup ? ReadInteger(groupToken, "stageGroupId", 1, index) : null;
            int? stageIndex = hasIndex ? ReadInteger(stageIndexToken, "stageIndex", 1, index) : null;
            int? stageCount = hasCount ? ReadInteger(stageCountToken, "stageCount", 1, index) : null;

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
                stageCount);
        }

        private static int ReadInteger(JObject item, string propertyName, int minimum, int? context = null)
        {
            if (!item.TryGetValue(propertyName, StringComparison.Ordinal, out JToken token))
                throw MissingProperty(propertyName, context);
            return ReadInteger(token, propertyName, minimum, context);
        }

        private static int ReadInteger(JToken token, string propertyName, int minimum, int? context)
        {
            if (token?.Type != JTokenType.Integer)
                throw InvalidProperty(propertyName, context, "must be an integer");

            long value = token.Value<long>();
            if (value < minimum || value > int.MaxValue)
                throw InvalidProperty(
                    propertyName,
                    context,
                    $"must be between {minimum.ToString(CultureInfo.InvariantCulture)} and {int.MaxValue.ToString(CultureInfo.InvariantCulture)}");
            return (int)value;
        }

        private static string ReadString(JObject item, string propertyName, int context, bool allowEmpty)
        {
            if (!item.TryGetValue(propertyName, StringComparison.Ordinal, out JToken token))
                throw MissingProperty(propertyName, context);
            if (token?.Type != JTokenType.String)
                throw InvalidProperty(propertyName, context, "must be a string");

            string value = token.Value<string>();
            if (!allowEmpty && string.IsNullOrWhiteSpace(value))
                throw InvalidProperty(propertyName, context, "must not be empty");
            return value;
        }

        private static bool ReadBoolean(JObject item, string propertyName, int context)
        {
            if (!item.TryGetValue(propertyName, StringComparison.Ordinal, out JToken token))
                throw MissingProperty(propertyName, context);
            if (token?.Type != JTokenType.Boolean)
                throw InvalidProperty(propertyName, context, "must be a boolean");
            return token.Value<bool>();
        }

        private static AchievementCatalogException MissingProperty(string propertyName, int? context) =>
            InvalidProperty(propertyName, context, "is required");

        private static AchievementCatalogException InvalidProperty(
            string propertyName,
            int? context,
            string problem) =>
            new AchievementCatalogException(
                context.HasValue
                    ? $"Achievement at index {context.Value} property '{propertyName}' {problem}."
                    : $"Catalog property '{propertyName}' {problem}.");
    }
}

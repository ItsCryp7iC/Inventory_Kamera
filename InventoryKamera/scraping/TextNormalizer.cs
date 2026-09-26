using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace InventoryKamera
{
    /// <summary>
    /// Fuzzy-matches noisy OCR text against the game's lookup data (gear slots/stats/elements/
    /// weapons/artifact sets/characters/materials). Scanner consumers pass one stable
    /// <see cref="GameDataSnapshot"/> for the complete run; raw read-only collection overloads remain
    /// useful for synthetic tests and the legacy GenshinProcesor forwarding surface.
    /// </summary>
    internal static class TextNormalizer
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        internal static string FindClosestGearSlot(string input, IEnumerable<string> gearSlots)
        {
            foreach (var slot in gearSlots)
            {
                if (input.Contains(slot))
                {
                    return slot;
                }
            }
            return input;
        }

        internal static string FindClosestStat(string stat, IReadOnlyDictionary<string, string> stats, int minConfidence = 90) =>
            FindClosestInDict(source: stat, targets: stats, minConfidence: minConfidence);

        internal static string FindElementByName(string name, IReadOnlyDictionary<string, string> elements, int minConfidence = 90) =>
            FindClosestInDict(source: name, targets: elements, minConfidence: minConfidence);

        internal static string FindClosestWeapon(string name, IReadOnlyDictionary<string, string> weapons, int maxEdits = 90) =>
            FindClosestInDict(source: name, targets: weapons, minConfidence: maxEdits);

        internal static string FindClosestSetName(string name, IReadOnlyDictionary<string, JObject> artifacts, int minConfidence = 90) =>
            FindClosestInDict(source: name, targets: artifacts, minConfidence: minConfidence);

        internal static string FindClosestArtifactSetFromArtifactName(string name, IReadOnlyDictionary<string, JObject> artifacts, int minConfidence = 90)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string closestMatch = null;
            double highestConfidence = 0;

            foreach (var artifactSet in artifacts)
            {
                string currentSet = artifactSet.Value["GOOD"].ToString();

                foreach (var slot in artifactSet.Value["artifacts"].Values())
                {
                    string artifactName = slot["normalizedName"].ToString();
                    if (artifactName == name) return currentSet;

                    double artifactSimilarity = StringSimilarity(name, artifactName);

                    if (artifactSimilarity > minConfidence && artifactSimilarity > highestConfidence)
                    {
                        highestConfidence = artifactSimilarity;
                        closestMatch = currentSet;
                    }
                }
            }

            return closestMatch;
        }

        internal static string FindClosestCharacterName(string name, IReadOnlyDictionary<string, JObject> characters, int minConfidence = 90)
        {
            var temp = new Dictionary<string, JObject>();
            foreach (var character in characters)
            {
                if (character.Value.TryGetValue("CustomName", out var customName)) temp.Add(((string)customName), character.Value);
                else temp.Add(character.Key, character.Value);
            }
            return FindClosestInDict(source: name, targets: temp, minConfidence: minConfidence);
        }

        internal static string FindClosestDevelopmentName(string name, IReadOnlyDictionary<string, string> devItems, IReadOnlyDictionary<string, string> materials, int minConfidence = 90)
        {
            string value = FindClosestInDict(source: name, targets: devItems, minConfidence: minConfidence);
            return !string.IsNullOrWhiteSpace(value) ? value : FindClosestInDict(source: name, targets: materials, minConfidence: minConfidence);
        }

        internal static string FindClosestMaterialName(string name, IReadOnlyDictionary<string, string> materials, int minConfidence = 90)
        {
            string value = FindClosestInDict(source: name, targets: materials, minConfidence: minConfidence);
            return !string.IsNullOrWhiteSpace(value) ? value : FindClosestInDict(source: name, targets: materials, minConfidence: minConfidence);
        }

        private static string FindClosestInDict(string source, IReadOnlyDictionary<string, string> targets, int minConfidence)
        {
            if (string.IsNullOrWhiteSpace(source)) return "";
            if (targets.TryGetValue(source, out string value)) return value;

            HashSet<string> keys = new HashSet<string>(targets.Keys);

            if (keys.Where(key => key.Contains(source)).Count() == 1) return targets[keys.First(key => key.Contains(source))];

            source = FindClosestInList(source, keys, minConfidence);

            return targets.TryGetValue(source, out value) ? value : source;
        }

        private static string FindClosestInDict(string source, IReadOnlyDictionary<string, JObject> targets, int minConfidence)
        {
            if (string.IsNullOrWhiteSpace(source)) return "";
            if (targets.TryGetValue(source, out JObject value)) return (string)value["GOOD"];

            HashSet<string> keys = new HashSet<string>(targets.Keys);

            if (keys.Where(key => key.Contains(source)).Count() == 1) return (string)targets[keys.First(key => key.Contains(source))]["GOOD"];

            source = FindClosestInList(source, keys, minConfidence);

            return targets.TryGetValue(source, out value) ? (string)value["GOOD"] : source;
        }

        /// <summary>
        /// Fuzzy-matches <paramref name="source"/> against a flat set of candidate strings (not a
        /// name-to-value lookup dictionary like the other <c>FindClosestX</c> methods) -- exposed
        /// (rather than the usual <c>private</c>) for ad-hoc matching against small hardcoded lists
        /// that don't warrant their own lookup dictionary, e.g. Phase 3 §6c's inventory tab names.
        /// </summary>
        internal static string FindClosestInList(string source, HashSet<string> targets, double minConfidence = 80)
        {
            if (targets.Contains(source)) return source;
            if (string.IsNullOrWhiteSpace(source)) return null;

            string mostSimilarString = "";
            double mostSimilarValue = 0;

            foreach (var target in targets)
            {
                double similarityValue = StringSimilarity(source, target);

                if (similarityValue > minConfidence && similarityValue > mostSimilarValue)
                {
                    mostSimilarValue = similarityValue;
                    mostSimilarString = target;
                }
            }

            if (!string.IsNullOrWhiteSpace(mostSimilarString) && !targets.Contains("critrate"))   // Only print this statement when not looking to match for a closest stat
                Logger.Debug("Most similar string found for {0} as {1} ({2}%)", source, mostSimilarString, mostSimilarValue);

            return mostSimilarString;
        }

        internal static string FindClosestGearSlot(string input, GameDataSnapshot data) =>
            FindClosestGearSlot(input, data.GearSlots);

        internal static string FindClosestStat(string input, GameDataSnapshot data, int minConfidence = 90) =>
            FindClosestStat(input, data.Stats, minConfidence);

        internal static string FindElementByName(string input, GameDataSnapshot data, int minConfidence = 90) =>
            FindElementByName(input, data.Elements, minConfidence);

        internal static string FindClosestWeapon(string input, GameDataSnapshot data, int minConfidence = 90) =>
            FindClosestWeapon(input, data.Weapons, minConfidence);

        internal static string FindClosestSetName(string input, GameDataSnapshot data, int minConfidence = 90) =>
            FindClosestSetName(input, data.Artifacts, minConfidence);

        internal static string FindClosestArtifactSetFromArtifactName(string input, GameDataSnapshot data, int minConfidence = 90) =>
            FindClosestArtifactSetFromArtifactName(input, data.Artifacts, minConfidence);

        internal static string FindClosestCharacterName(string input, GameDataSnapshot data, int minConfidence = 90) =>
            FindClosestCharacterName(input, data.Characters, minConfidence);

        internal static string FindClosestDevelopmentName(string input, GameDataSnapshot data, int minConfidence = 90) =>
            FindClosestDevelopmentName(input, data.CharacterDevelopmentItems, data.Materials, minConfidence);

        internal static string FindClosestMaterialName(string input, GameDataSnapshot data, int minConfidence = 90) =>
            FindClosestMaterialName(input, data.Materials, minConfidence);

        private static int LevenshteinDistance(string s1, string s2)
        {
            int m = s1.Length;
            int n = s2.Length;
            int[,] dp = new int[m + 1, n + 1];

            for (int i = 0; i <= m; i++)
            {
                for (int j = 0; j <= n; j++)
                {
                    if (i == 0)
                    {
                        dp[i, j] = j;
                    }
                    else if (j == 0)
                    {
                        dp[i, j] = i;
                    }
                    else if (s1[i - 1] == s2[j - 1])
                    {
                        dp[i, j] = dp[i - 1, j - 1];
                    }
                    else
                    {
                        dp[i, j] = 1 + Math.Min(Math.Min(dp[i - 1, j], dp[i, j - 1]), dp[i - 1, j - 1]);
                    }
                }
            }

            return dp[m, n];
        }

        private static double StringSimilarity(string s1, string s2)
        {
            int distance = LevenshteinDistance(s1, s2);
            int maxLength = Math.Max(s1.Length, s2.Length);
            double similarity = 1.0 - (distance / (double)maxLength);
            return similarity * 100.0;
        }
    }
}

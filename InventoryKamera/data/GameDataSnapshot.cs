using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
    /// <summary>
    /// One coherent, read-only view of game lookup data used by a scan. Collection topology is copied
    /// and wrapped, so consumers cannot add, remove, or replace entries and later file reloads cannot
    /// affect this instance. The retained <see cref="JObject"/> values are technically mutable; scanner
    /// consumers must treat them as read-only. Per-scan custom names clone only the affected objects.
    /// </summary>
    public sealed class GameDataSnapshot
    {
        public IReadOnlyDictionary<string, JObject> Characters { get; }
        public IReadOnlyDictionary<string, JObject> Artifacts { get; }
        public IReadOnlyDictionary<string, string> Weapons { get; }
        public IReadOnlyDictionary<string, string> CharacterDevelopmentItems { get; }
        public IReadOnlyDictionary<string, string> Materials { get; }
        public IReadOnlyDictionary<string, string> Stats { get; }
        public IReadOnlyDictionary<string, string> Elements { get; }
        public IReadOnlyCollection<string> GearSlots { get; }
        public IReadOnlyCollection<string> EnhancementMaterials { get; }

        public GameDataSnapshot(
            IReadOnlyDictionary<string, JObject> characters,
            IReadOnlyDictionary<string, JObject> artifacts,
            IReadOnlyDictionary<string, string> weapons,
            IReadOnlyDictionary<string, string> characterDevelopmentItems,
            IReadOnlyDictionary<string, string> materials,
            IReadOnlyDictionary<string, string> stats,
            IReadOnlyDictionary<string, string> elements,
            IEnumerable<string> gearSlots,
            IEnumerable<string> enhancementMaterials)
        {
            Characters = Wrap(characters, nameof(characters));
            Artifacts = Wrap(artifacts, nameof(artifacts));
            Weapons = Wrap(weapons, nameof(weapons));
            CharacterDevelopmentItems = Wrap(characterDevelopmentItems, nameof(characterDevelopmentItems));
            Materials = Wrap(materials, nameof(materials));
            Stats = Wrap(stats, nameof(stats));
            Elements = Wrap(elements, nameof(elements));
            GearSlots = Array.AsReadOnly((gearSlots ?? throw new ArgumentNullException(nameof(gearSlots))).ToArray());
            EnhancementMaterials = Array.AsReadOnly(
                (enhancementMaterials ?? throw new ArgumentNullException(nameof(enhancementMaterials))).ToArray());
        }

        internal GameDataSnapshot WithCharacterCustomNames(IReadOnlyDictionary<string, string> customNames)
        {
            if (customNames == null || customNames.Count == 0) return this;

            var characters = new Dictionary<string, JObject>(Characters);
            foreach (var pair in customNames)
            {
                string target = NormalizeKey(pair.Key);
                string name = NormalizeKey(pair.Value);
                if (target == name) continue;
                if (!characters.TryGetValue(target, out JObject existing))
                    throw new KeyNotFoundException($"Could not find '{target}' entry in characters.json");

                var customized = (JObject)existing.DeepClone();
                customized["CustomName"] = name;
                characters[target] = customized;
            }

            return new GameDataSnapshot(
                characters,
                Artifacts,
                Weapons,
                CharacterDevelopmentItems,
                Materials,
                Stats,
                Elements,
                GearSlots,
                EnhancementMaterials);
        }

        internal static string NormalizeKey(string text)
        {
            text = (text ?? string.Empty).ToLower();
            string pascal = CultureInfo.GetCultureInfo("en-US").TextInfo.ToTitleCase(text);
            return Regex.Replace(pascal, @"[\W]", string.Empty).ToLower();
        }

        private static IReadOnlyDictionary<TKey, TValue> Wrap<TKey, TValue>(
            IReadOnlyDictionary<TKey, TValue> source,
            string parameterName)
        {
            if (source == null) throw new ArgumentNullException(parameterName);
            return new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>(source));
        }
    }

    /// <summary>
    /// Loads the current on-disk lookup files into one snapshot. DatabaseManager remains responsible
    /// for downloading/updating those files; this factory only defines the scan-consumption boundary.
    /// </summary>
    internal sealed class GameDataSnapshotFactory
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private static readonly string[] ManequinKeys = { "manequin1", "manequin2" };

        internal GameDataSnapshot Load()
        {
            var manager = new DatabaseManager();
            Dictionary<string, JObject> characters = manager.LoadCharacters();
            EnsureManequinEntriesExist(characters, manager.ListsDir);

            return new GameDataSnapshot(
                characters,
                manager.LoadArtifacts(),
                manager.LoadWeapons(),
                manager.LoadDevItems(),
                manager.LoadMaterials(),
                CreateStats(),
                CreateElements(),
                new[] { "flower", "plume", "sands", "goblet", "circlet" },
                new[]
                {
                    "enhancementore",
                    "fineenhancementore",
                    "mysticenhancementore",
                    "sanctifyingunction",
                    "sanctifyingessence",
                });
        }

        internal static JObject BuildManequinEntry(string key)
        {
            string good = char.ToUpper(key[0]) + key.Substring(1);
            return new JObject
            {
                ["GOOD"] = good,
                ["ConstellationName"] = new JArray(
                    "Support entry to omit manequins during scanning; GOOD does not support manequins."),
                ["ConstellationOrder"] = new JArray("burst", "skill"),
                ["Element"] = new JArray("electro", "pyro", "dendro", "geo", "hydro", "anemo"),
                ["WeaponType"] = 0,
            };
        }

        private static void EnsureManequinEntriesExist(Dictionary<string, JObject> characters, string listsDir)
        {
            bool added = false;
            foreach (string key in ManequinKeys)
            {
                if (characters.ContainsKey(key)) continue;
                characters[key] = BuildManequinEntry(key);
                added = true;
            }

            if (!added) return;
            File.WriteAllText(
                Path.Combine(listsDir, "characters.json"),
                JsonConvert.SerializeObject(
                    new SortedDictionary<string, JObject>(characters),
                    Formatting.Indented));
            Logger.Info("Added missing manequin entries to characters.json");
        }

        private static Dictionary<string, string> CreateStats()
        {
            var stats = new Dictionary<string, string>
            {
                ["hp"] = "hp",
                ["hp%"] = "hp_",
                ["atk"] = "atk",
                ["atk%"] = "atk_",
                ["def"] = "def",
                ["def%"] = "def_",
                ["energyrecharge"] = "enerRech_",
                ["elementalmastery"] = "eleMas",
                ["healingbonus"] = "heal_",
                ["critrate"] = "critRate_",
                ["critdmg"] = "critDMG_",
                ["physicaldmgbonus"] = "physical_dmg_",
            };
            foreach (string element in ElementNames)
                stats[$"{element}dmgbonus"] = $"{element}_dmg_";
            return stats;
        }

        private static Dictionary<string, string> CreateElements() => ElementNames.ToDictionary(
            element => element,
            element => char.ToUpper(element[0]) + element.Substring(1));

        private static readonly string[] ElementNames =
        {
            "pyro", "hydro", "dendro", "electro", "anemo", "cryo", "geo",
        };
    }
}

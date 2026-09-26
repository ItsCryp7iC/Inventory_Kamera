using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace InventoryKamera
{
    /// <summary>
    /// Pure validity checks against explicitly supplied game lookup data. Scanner consumers pass one
    /// stable <see cref="GameDataSnapshot"/> for the complete run; raw read-only collection overloads
    /// remain useful for synthetic tests and the legacy GenshinProcesor forwarding surface.
    /// </summary>
    internal static class LookupService
    {
        internal static bool IsValidSetName(string setName, IReadOnlyDictionary<string, JObject> artifacts)
        {
            if (artifacts.TryGetValue(setName, out _) || artifacts.TryGetValue(setName.ToLower(), out _)) return true;
            foreach (var artifactSet in artifacts.Values)
                foreach (var field in artifactSet)
                    if (field.ToString() == setName) return true;

            return false;
        }

        internal static bool IsValidMaterial(string name, IReadOnlyDictionary<string, string> materials)
        {
            return materials.Values.Contains(name) || materials.ContainsKey(name.ToLower());
        }

        internal static bool IsValidStat(string stat, IReadOnlyDictionary<string, string> stats)
        {
            return stats.Values.Contains(stat);
        }

        internal static bool IsValidSlot(string gearSlot, IEnumerable<string> gearSlots)
        {
            return gearSlots.Contains(gearSlot);
        }

        internal static bool IsValidCharacter(string character, IReadOnlyDictionary<string, JObject> characters)
        {
            return character.Contains("Traveler") || character == "Wanderer" || character == "Manequin1" || character == "Manequin2" || characters.ContainsKey(character.ToLower());
        }

        internal static bool IsValidElement(string element, IReadOnlyDictionary<string, string> elements)
        {
            return elements.Values.Contains(element) || elements.ContainsKey(element.ToLower());
        }

        internal static bool IsEnhancementMaterial(string material, IEnumerable<string> enhancementMaterials, IReadOnlyDictionary<string, string> materials)
        {
            return enhancementMaterials.Contains(material.ToLower()) || materials.Values.Contains(material) || materials.ContainsKey(material.ToLower());
        }

        internal static bool IsValidWeapon(string weapon, IReadOnlyDictionary<string, string> weapons)
        {
            return weapons.Values.Contains(weapon) || weapons.ContainsKey(weapon.ToLower());
        }

        internal static bool IsValidSetName(string value, GameDataSnapshot data) => IsValidSetName(value, data.Artifacts);
        internal static bool IsValidMaterial(string value, GameDataSnapshot data) => IsValidMaterial(value, data.Materials);
        internal static bool IsValidStat(string value, GameDataSnapshot data) => IsValidStat(value, data.Stats);
        internal static bool IsValidSlot(string value, GameDataSnapshot data) => IsValidSlot(value, data.GearSlots);
        internal static bool IsValidCharacter(string value, GameDataSnapshot data) => IsValidCharacter(value, data.Characters);
        internal static bool IsValidElement(string value, GameDataSnapshot data) => IsValidElement(value, data.Elements);
        internal static bool IsEnhancementMaterial(string value, GameDataSnapshot data) =>
            IsEnhancementMaterial(value, data.EnhancementMaterials, data.Materials);
        internal static bool IsValidWeapon(string value, GameDataSnapshot data) => IsValidWeapon(value, data.Weapons);

        internal static IReadOnlyList<string> GetCharacterElements(string name, GameDataSnapshot data)
        {
            if (string.IsNullOrWhiteSpace(name)) return new string[0];
            return data.Characters.TryGetValue(name.ToLower(), out JObject character)
                ? character["Element"]?.ToObject<List<string>>()
                : null;
        }

        internal static bool CharacterMatchesElement(string name, string element, GameDataSnapshot data)
        {
            IReadOnlyList<string> elements = GetCharacterElements(name, data);
            return elements != null && !string.IsNullOrWhiteSpace(element) && elements.Contains(element.ToLower());
        }

        internal static bool IsFourStarCharacter(string name, GameDataSnapshot data)
        {
            string key = name.Contains("Traveler") ? "traveler" : name.ToLower();
            return data.Characters.TryGetValue(key, out JObject character)
                && character["Rarity"] != null
                && character["Rarity"].ToObject<int>() == 4;
        }
    }
}

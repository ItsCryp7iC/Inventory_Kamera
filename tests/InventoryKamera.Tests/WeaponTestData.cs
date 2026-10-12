using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace InventoryKamera.Tests
{
    internal static class WeaponTestData
    {
        private static readonly GameDataSnapshot snapshot = CreateSnapshot();

        internal static GameDataSnapshot Snapshot => snapshot;

        internal static Weapon WeaponAt(
            int sequence,
            string name = null,
            string equippedCharacter = null) =>
            new Weapon(
                name ?? "TestWeapon" + sequence,
                90,
                true,
                1,
                _equippedCharacter: equippedCharacter,
                _id: sequence,
                _rarity: 5,
                gameData: snapshot);

        private static GameDataSnapshot CreateSnapshot()
        {
            var weapons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["favoniussword"] = "FavoniusSword",
                ["sacrificialsword"] = "SacrificialSword",
                ["dullblade"] = "DullBlade",
                ["wastergreatsword"] = "WasterGreatsword",
                ["beginnersprotector"] = "BeginnersProtector",
                ["huntersbow"] = "HuntersBow",
                ["apprenticesnotes"] = "ApprenticesNotes",
            };
            for (int i = 0; i < 100; i++)
                weapons["testweapon" + i] = "TestWeapon" + i;

            return new GameDataSnapshot(
                new Dictionary<string, JObject>
                {
                    ["jean"] = new JObject { ["GOOD"] = "Jean" },
                    ["zhongli"] = new JObject { ["GOOD"] = "Zhongli" },
                },
                new Dictionary<string, JObject>(),
                weapons,
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                Array.Empty<string>(),
                Array.Empty<string>());
        }
    }
}

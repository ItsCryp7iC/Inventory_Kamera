using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
    public class GOOD
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        [JsonProperty("format")]
        public string Format { get; private set; }

        [JsonProperty("version")]
        public int Version { get; private set; }

        [JsonProperty("kamera_version")]
        public string AppVersion { get; private set; }

        [JsonProperty("source")]
        public string Source { get; private set; }

        [JsonProperty("weapons", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<Weapon> Weapons { get; private set; }

        [JsonProperty("artifacts", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<Artifact> Artifacts { get; private set; }

        [JsonProperty("characters", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<Character> Characters { get; private set; }

        [JsonProperty("materials", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Dictionary<string, int> Materials { get; private set; }

        public GOOD()
        {
            Format = "EMPTY";
            Version = 0;
            Source = "NOT FILLED";
        }

        public GOOD(GameScanner genshinData) : this(
            genshinData?.Characters,
            genshinData?.Inventory?.Weapons,
            genshinData?.Inventory?.Artifacts,
            genshinData?.Inventory?.AllMaterials,
            Properties.Settings.Default.EquipWeapons,
            Properties.Settings.Default.EquipArtifacts)
        {
        }

        internal GOOD(
            IEnumerable<Character> characters,
            IEnumerable<Weapon> weapons,
            IEnumerable<Artifact> artifacts,
            IEnumerable<Material> materials,
            bool equipWeapons,
            bool equipArtifacts) : this()
        {
            if (characters == null) throw new ArgumentNullException(nameof(characters));
            if (weapons == null) throw new ArgumentNullException(nameof(weapons));
            if (artifacts == null) throw new ArgumentNullException(nameof(artifacts));
            if (materials == null) throw new ArgumentNullException(nameof(materials));

            // Get rid of VS warning since we are converting this class to JSON
            Format = "GOOD";
            Version = 3;
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            Source = "Inventory_Kamera";

            // Assign Characters
            var exportableCharacters = new List<Character>();
            foreach (Character character in characters)
            {
                if (character.IsExportableToGood(out string reason))
                {
                    exportableCharacters.Add(character);
                }
                else
                {
                    Logger.Warn(
                        "Omitting character {0} from GOOD export: {1}.",
                        character.NameGOOD ?? "(unnamed)",
                        reason);
                }
            }

            if (exportableCharacters.Count > 0)
            {
                Characters = exportableCharacters;

                if (!equipWeapons)
                {
                    foreach (Character character in Characters) character.Weapon = null;
                }
            }



            // Assign Weapons
            var exportedWeapons = weapons.ToList();
            if (exportedWeapons.Count > 0)
            {
                Weapons = exportedWeapons;

                if (!equipWeapons)
                {
                    foreach (Weapon weapon in Weapons) weapon.EquippedCharacter = "";
                }
            }

            // Assign Artifacts
            var exportedArtifacts = artifacts.ToList();
            if (exportedArtifacts.Count > 0)
            {
                Artifacts = exportedArtifacts;

                if (!equipArtifacts)
                {
                    foreach (Artifact artifact in Artifacts) artifact.EquippedCharacter = "";
                }
            }

            // Assign materials
            var exportedMaterials = materials.ToList();
            if (exportedMaterials.Count > 0) Materials = new Dictionary<string, int>();
            exportedMaterials.ForEach(material => Materials.Add(material.name, material.count));
        }

        internal void WriteToJSON(string outputDirectory, IScanProgressReporter progressReporter)
        {
            // Creates directory if doesn't exist
            Directory.CreateDirectory(outputDirectory);

            // Create file with timestamp in name
            string fileName = $"genshinData_GOOD_{DateTime.Now.ToString("yyyy_MM_dd_HH_mm")}.json";
            string outputFile = Path.Combine(outputDirectory, fileName);

            // Write file
            WriteToJson(outputFile);


            if (!File.Exists(outputFile)) // did not make file
            {
                progressReporter.AddError($"Failed to output at : {outputDirectory}");
            }
        }

        private void WriteToJson(string outputFile)
        {
            using (var streamWriter = new StreamWriter(outputFile))
            {
                streamWriter.WriteLine(ToString());
            }
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this).ToString();
        }
    }
}

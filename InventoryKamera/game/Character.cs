using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace InventoryKamera
{
    [Serializable]
    public class Character
    {
        private string _nameKey;
        private string _element;
        private WeaponType _weaponType;
        private GameDataSnapshot gameData;

        [JsonProperty("key")]
        public string NameGOOD
        {
            get
            {
                return _nameKey == "Traveler" ? _nameKey + Element : _nameKey;
            }
            internal set
            {
                _nameKey = value;
            }
        }

        [JsonIgnore]
        internal string CanonicalName => _nameKey;

        [JsonProperty("level")]
        public int Level { get; internal set; }

        [JsonProperty("constellation")]
        public int Constellation { get; internal set; }

        [JsonProperty("ascension")]
        public int Ascension
        { get { return AscensionLevel(); } internal set { } }

        [JsonProperty("talent")]
        public Dictionary<string, int> Talents { get; internal set; }

        [JsonIgnore]
        public string Element { get => _element; internal set => _element = value; }

        [JsonIgnore]
        public bool Ascended { get; internal set; }

        [JsonIgnore]
        public int Experience { get; internal set; }

        [JsonIgnore]
        public Weapon Weapon { get; internal set; }

        [JsonIgnore]
        public Dictionary<string, Artifact> Artifacts { get; internal set; }

        [JsonIgnore]
        public bool HasGameData => gameData != null;

        [JsonIgnore]
        public WeaponType WeaponType { 
            get => RequireGameData().Characters[_nameKey.ToLower()]["WeaponType"].ToObject<WeaponType>();
            
            internal set { WeaponType = value; } 
        }

        public Character()
        {
            Talents = new Dictionary<string, int>
            {
                ["auto"] = 0,
                ["skill"] = 0,
                ["burst"] = 0
            };
            Artifacts = new Dictionary<string, Artifact>();
        }

        public Character(GameDataSnapshot gameData) : this()
        {
            AttachGameData(gameData);
        }

        public Character(string _name, string _element, int _level, bool _ascension, int _experience, int _constellation, int[] _talents) : this()
        {
            Element = _element;
            Level = _level;
            Ascended = _ascension;
            Experience = _experience;
            Constellation = _constellation;
            try
            {
                Talents["auto"] = _talents[0];
                Talents["skill"] = _talents[1];
                Talents["burst"] = _talents[2];
            }
            catch (Exception)
            { }
        }

        public bool IsValid()
        {
            return HasValidName() && HasValidLevel() && HasValidElement() && HasValidConstellation() && HasValidTalents();
        }

        public bool HasValidName()
        {
            return !string.IsNullOrWhiteSpace(NameGOOD) &&
                LookupService.IsValidCharacter(NameGOOD, RequireGameData());
        }

        public bool HasValidLevel()
        {
            return 1 <= Level && Level <= 100;
        }

        public bool HasValidElement()
        {
            return !string.IsNullOrWhiteSpace(Element) &&
                LookupService.IsValidElement(Element, RequireGameData());
        }

        public bool HasValidConstellation()
        {
            return 0 <= Constellation && Constellation <= 6;
        }

        public bool HasValidTalents()
        {
            if (Talents is null || Talents.Keys.Count != 3) return false;

            foreach (var value in Talents.Values) if (value < 1 || value > 15) return false;

            return true;
        }

        public void AssignWeapon(Weapon newWeapon)
        {
            Weapon = newWeapon;
        }

        /// <summary>
        /// Explicitly binds lookup data after parameterless construction or deserialization.
        /// A model cannot be rebound to a different snapshot after lookup data is attached.
        /// </summary>
        public void AttachGameData(GameDataSnapshot data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (gameData != null && !ReferenceEquals(gameData, data))
                throw new InvalidOperationException("Character game data is already attached.");
            gameData = data;
        }

        private GameDataSnapshot RequireGameData() => gameData ??
            throw new InvalidOperationException(
                "Character lookup data is not attached. Call AttachGameData before using lookup-dependent properties or validation.");

        public void AssignArtifact(Artifact artifact)
        {
            Artifacts[artifact.GearSlot] = artifact;
        }

        public int AscensionLevel()
        {
            if (Level < 20 || (Level == 20 && !Ascended))
            {
                return 0;
            }
            else if (Level < 40 || (Level == 40 && !Ascended))
            {
                return 1;
            }
            else if (Level < 50 || (Level == 50 && !Ascended))
            {
                return 2;
            }
            else if (Level < 60 || (Level == 60 && !Ascended))
            {
                return 3;
            }
            else if (Level < 70 || (Level == 70 && !Ascended))
            {
                return 4;
            }
            else if (Level < 80 || (Level == 80 && !Ascended))
            {
                return 5;
            }
            else if (Level <= 100 || (Level == 100 && !Ascended))
            {
                return 6;
            }
            return 0;
        }

        public override string ToString()
        {
            string output = "Character\n";
            output += $"Name: {NameGOOD}\n";
            output += $"Element: {Element}\n";
            output += $"Level: {Level}{(Ascended ? "+" : "")}\n";
            output += $"Ascension Level: {Ascension}\n";
            output += $"Constellation: {Constellation}\n";
            foreach (var item in Talents)
            {
                output += $"{item.Key.ToUpper()} : {item.Value}\n";
            }
            return output;
        }
    }
}

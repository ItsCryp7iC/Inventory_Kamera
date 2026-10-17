using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;

namespace InventoryKamera
{
	[Serializable]
	public class Artifact
	{
		private GameDataSnapshot gameData;
		[JsonProperty("setKey")]
		public string SetName { get; private set; }

		[JsonProperty("slotKey")]
		public string GearSlot { get; private set; }

		[JsonProperty("rarity")]
		public int Rarity { get; private set; }

		[JsonProperty("mainStatKey")]
		public string MainStat { get; private set; }

		[JsonProperty("level")]
		public int Level { get; private set; }

		[JsonProperty("substats")]
		public List<SubStat> SubStats { get; private set; }

		[JsonProperty("unactivatedSubstats")]
		public List<SubStat> unactivatedSubstats { get; private set; }

		[JsonProperty("location")]
		public string EquippedCharacter { get; internal set; }	

		[JsonProperty("lock")]
		public bool Lock { get; private set; }

        [JsonProperty("id")]
		public int Id { get; private set; }

		[JsonIgnore]
		public bool HasGameData => gameData != null;
		
		public Artifact()
		{
			Rarity = -1;
			GearSlot = null;
			MainStat = null;
			Level = -1;
			SubStats = new List<SubStat>(4);
			unactivatedSubstats = new List<SubStat>(1);
			SetName = null;
			EquippedCharacter = null;
			Lock = false;
			Id = 0;
		}

		public Artifact(string _setName, int _rarity, int _level, string _gearSlot, string _mainStat, List<SubStat> _subStats, List<SubStat> _unactivatedSubStats, string _equippedCharacter = null, int _id = 0, bool _Lock = false, GameDataSnapshot gameData = null)
		{
			if (gameData != null) AttachGameData(gameData);
			GearSlot = string.IsNullOrWhiteSpace(_gearSlot) ? "" : _gearSlot;
			Rarity = _rarity;
			MainStat = string.IsNullOrWhiteSpace(_mainStat) ? "" : _mainStat;
			Level = _level;
			// Preserve every parsed entry so validation can reject malformed OCR instead of silently
			// dropping only the bad row and exporting a plausible-looking partial Artifact.
			SubStats = _subStats?.ToList();
			unactivatedSubstats = _unactivatedSubStats?.ToList();
			SetName = string.IsNullOrWhiteSpace(_setName) ? "" : _setName;
			EquippedCharacter = string.IsNullOrWhiteSpace(_equippedCharacter) ? "" : _equippedCharacter;
			Lock = _Lock;
			Id = _id;
		}

		/// <summary>
		/// Overwrites the artifact set name after construction. Used only by the deferred OCR-correction
		/// flush (GameScanner) to apply a set the user corrected after the scan finished; mirrors the
		/// constructor's blank-to-empty-string normalization so validity checks behave identically.
		/// </summary>
		internal void UpdateSetName(string setName) => SetName = string.IsNullOrWhiteSpace(setName) ? "" : setName;

		/// <summary>
		/// Explicitly binds lookup data after parameterless construction or deserialization.
		/// A model cannot be rebound to a different snapshot after lookup data is attached.
		/// </summary>
		public void AttachGameData(GameDataSnapshot data)
		{
			if (data == null) throw new ArgumentNullException(nameof(data));
			if (gameData != null && !ReferenceEquals(gameData, data))
				throw new InvalidOperationException("Artifact game data is already attached.");
			gameData = data;
		}

		public bool IsValid()
		{
			return Validate().IsValid;
		}

		internal ArtifactValidationResult Validate()
		{
			var failures = new List<string>();
			if (!HasValidSetName()) failures.Add("invalid_set_key");
			if (!HasValidSlot()) failures.Add("invalid_slot");
			if (!HasValidRarity()) failures.Add("invalid_rarity");
			if (!HasValidLevel()) failures.Add("invalid_level");
			if (!HasValidMainStat()) failures.Add("invalid_main_stat");
			if (!HasValidActiveSubStats()) failures.Add("invalid_active_substat");
			if (!HasValidUnactivatedSubStats()) failures.Add("invalid_unactivated_substat");
			if (HasDuplicateSubStats()) failures.Add("duplicate_substat");
			if (HasMainStatSubStatCollision()) failures.Add("main_stat_as_substat");
			if (!HasValidSubStatCounts()) failures.Add("invalid_substat_structure");
			if (!HasValidEquippedCharacter()) failures.Add("invalid_location");
			return new ArtifactValidationResult(failures);
		}

		public bool HasValidLevel()
		{
			return 0 <= Level && Level <= 20;
		}

		public bool HasValidRarity()
		{
			return 1 <= Rarity && Rarity <= 5;
		}

		public bool HasValidSlot()
		{
			return LookupService.IsValidSlot(GearSlot, RequireGameData());
		}

		public bool HasValidSetName()
		{
			return LookupService.IsValidSetName(SetName, RequireGameData());
		}

		public bool HasValidMainStat()
		{
			return LookupService.IsValidStat(MainStat, RequireGameData());
		}

		public bool HasValidSubStats()
		{
			return HasValidActiveSubStats()
				&& HasValidUnactivatedSubStats()
				&& !HasDuplicateSubStats()
				&& !HasMainStatSubStatCollision()
				&& HasValidSubStatCounts();
		}

		internal bool HasValidActiveSubStats() => HasValidSubStatCollection(SubStats);

		internal bool HasValidUnactivatedSubStats() =>
			HasValidSubStatCollection(unactivatedSubstats);

		private bool HasValidSubStatCollection(IReadOnlyCollection<SubStat> substats)
		{
			if (substats == null) return false;
			GameDataSnapshot data = RequireGameData();
			return substats.All(substat =>
				!string.IsNullOrWhiteSpace(substat.stat)
				&& LookupService.IsValidStat(substat.stat, data)
				&& substat.value > 0);
		}

		private bool HasDuplicateSubStats()
		{
			if (SubStats == null || unactivatedSubstats == null) return false;
			var keys = new HashSet<string>(StringComparer.Ordinal);
			return SubStats.Concat(unactivatedSubstats)
				.Where(substat => !string.IsNullOrWhiteSpace(substat.stat))
				.Any(substat => !keys.Add(substat.stat));
		}

		private bool HasMainStatSubStatCollision()
		{
			if (string.IsNullOrWhiteSpace(MainStat) ||
				SubStats == null || unactivatedSubstats == null)
			{
				return false;
			}

			return SubStats.Concat(unactivatedSubstats)
				.Any(substat => string.Equals(substat.stat, MainStat, StringComparison.Ordinal));
		}

		private bool HasValidSubStatCounts()
		{
			if (SubStats == null || unactivatedSubstats == null) return false;
			return unactivatedSubstats.Count <= 1
				&& SubStats.Count + unactivatedSubstats.Count <= 4;
		}

		public bool HasValidEquippedCharacter()
		{
			return string.IsNullOrWhiteSpace(EquippedCharacter) ||
				LookupService.IsValidCharacter(EquippedCharacter, RequireGameData());
		}

		private GameDataSnapshot RequireGameData() => gameData ??
			throw new InvalidOperationException(
				"Artifact lookup data is not attached. Call AttachGameData before using lookup-dependent validation.");

		[Serializable]
		public struct SubStat
		{
			[JsonProperty("key")]
			[DefaultValue("")]
			public string stat;

			[JsonProperty("value")]
			[DefaultValue(-1)]
			public decimal value;

			public override string ToString()
			{
				return stat is null
					? "NULL"
					: stat.Contains("_") ? $"{stat} + {value}%" : $"{stat} + {value}";
			}
		}

		public override bool Equals(object obj) => this.Equals(obj as Artifact);

		public bool Equals(Artifact artifact)
		{
			if (artifact is null)
			{
				return false;
			}

			if (Object.ReferenceEquals(this, artifact))
			{
				return true;
			}

			if (GetType() != artifact.GetType())
			{
				return false;
			}

			return GearSlot == artifact.GearSlot
				&& Rarity == artifact.Rarity
				&& MainStat == artifact.MainStat
				&& Level == artifact.Level
				&& SubStats == artifact.SubStats
				&& SetName == artifact.SetName
				&& EquippedCharacter == artifact.EquippedCharacter
				&& Lock == artifact.Lock;
		}

		public override string ToString()
		{
			string output = $"Artifact ID: {Id}\n"
				+ $"Slot: {GearSlot}\n"
				+ $"Set: {SetName}\n"
				+ $"Rarity: {Rarity}\n"
				+ $"Level: {Level}\n"
				+ $"Main Stat: {MainStat}\n";

			SubStats?.ForEach(s => output += $"Substat {SubStats.IndexOf(s)+1}: {s}\n");

			output += $"Locked: {Lock}\n";

			if (!string.IsNullOrWhiteSpace(EquippedCharacter)) output += $"Equipped character: {EquippedCharacter}\n";
			return output;
		}

		public override int GetHashCode() => (GearSlot, Rarity, MainStat, Level, SubStats, SetName, EquippedCharacter, Lock).GetHashCode();

		public static bool operator ==(Artifact lhs, Artifact rhs)
		{
			if (lhs is null)
			{
				if (rhs is null)
				{
					return true;
				}

				return false;
			}

			return lhs.Equals(rhs);
		}

		public static bool operator !=(Artifact lhs, Artifact rhs) => !( lhs == rhs );
	}
}

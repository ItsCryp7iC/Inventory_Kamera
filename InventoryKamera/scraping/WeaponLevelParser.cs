using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
	/// <summary>
	/// Parses the complete level/cap pair displayed on a weapon card. Unlike the legacy OCR cleanup,
	/// this parser does not repair malformed denominators or discard unrelated characters.
	/// </summary>
	internal static class WeaponLevelParser
	{
		private static readonly Regex LevelPair = new Regex(
			@"^\s*(?:[Ll][Vv]\.?\s*)?(\d+)\s*/\s*(\d+)\s*$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private static readonly HashSet<int> SupportedMaximumLevels = new HashSet<int>
		{
			20, 40, 50, 60, 70, 80, 90,
		};

		internal static WeaponLevelParseResult Parse(string rawText)
		{
			string text = rawText ?? string.Empty;
			Match pair = LevelPair.Match(text);
			if (!pair.Success)
				return WeaponLevelParseResult.Rejected(text, "malformed_pair");

			if (!int.TryParse(pair.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int level) ||
				!int.TryParse(pair.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int maximumLevel))
			{
				return WeaponLevelParseResult.Rejected(text, "numeric_parse_failed");
			}

			if (level < 1)
				return WeaponLevelParseResult.Rejected(text, "level_below_one", level, maximumLevel);

			if (!SupportedMaximumLevels.Contains(maximumLevel))
				return WeaponLevelParseResult.Rejected(text, "unsupported_maximum", level, maximumLevel);

			if (level > maximumLevel)
				return WeaponLevelParseResult.Rejected(text, "level_exceeds_maximum", level, maximumLevel);

			bool ascended = 20 <= level && level < maximumLevel;
			return WeaponLevelParseResult.Accepted(text, level, maximumLevel, ascended);
		}
	}

	internal sealed class WeaponLevelParseResult
	{
		private WeaponLevelParseResult(
			bool success,
			string rawText,
			int? level,
			int? maximumLevel,
			bool ascended,
			string reasonCode)
		{
			Success = success;
			RawText = rawText;
			Level = level;
			MaximumLevel = maximumLevel;
			Ascended = ascended;
			ReasonCode = reasonCode;
		}

		internal bool Success { get; }
		internal string RawText { get; }
		internal int? Level { get; }
		internal int? MaximumLevel { get; }
		internal bool Ascended { get; }
		internal string ReasonCode { get; }

		internal static WeaponLevelParseResult Accepted(
			string rawText,
			int level,
			int maximumLevel,
			bool ascended) =>
			new WeaponLevelParseResult(
				true, rawText, level, maximumLevel, ascended, "accepted");

		internal static WeaponLevelParseResult Rejected(
			string rawText,
			string reasonCode,
			int? level = null,
			int? maximumLevel = null) =>
			new WeaponLevelParseResult(
				false, rawText, level, maximumLevel, false, reasonCode);
	}
}

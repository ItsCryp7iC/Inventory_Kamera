using System.Globalization;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
    /// <summary>
    /// Parses the complete Artifact level label. It accepts the game's optional leading plus sign
    /// but does not salvage digits from otherwise malformed OCR text.
    /// </summary>
    internal static class ArtifactLevelParser
    {
        private static readonly Regex LevelPattern = new Regex(
            @"^\s*\+?\s*(?<level>\d{1,2})\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        internal static bool TryParse(string rawText, out int level)
        {
            level = -1;
            Match match = LevelPattern.Match(rawText ?? string.Empty);
            if (!match.Success ||
                !int.TryParse(
                    match.Groups["level"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int parsed) ||
                parsed < 0 || parsed > 20)
            {
                return false;
            }

            level = parsed;
            return true;
        }
    }
}

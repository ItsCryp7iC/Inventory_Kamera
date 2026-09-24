using System;
using System.Globalization;
using System.Linq;

namespace InventoryKamera
{
    /// <summary>
    /// Parses and compares the version tags used by Inventory Kamera releases. Numeric cores may
    /// contain two to four components for compatibility with historical tags, while prerelease
    /// identifiers follow Semantic Versioning precedence rules.
    /// </summary>
    internal sealed class ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
    {
        private readonly int[] numericComponents;
        private readonly string[] prereleaseIdentifiers;
        private readonly int displayedComponentCount;

        private ReleaseVersion(int[] numericComponents, string[] prereleaseIdentifiers, int displayedComponentCount)
        {
            this.numericComponents = numericComponents;
            this.prereleaseIdentifiers = prereleaseIdentifiers;
            this.displayedComponentCount = displayedComponentCount;
        }

        public static bool TryParse(string tag, out ReleaseVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(tag) || tag != tag.Trim()) return false;

            var value = tag;
            if (value[0] == 'v' || value[0] == 'V') value = value.Substring(1);
            if (value.Length == 0) return false;

            var prereleaseSeparator = value.IndexOf('-');
            var numericCore = prereleaseSeparator >= 0 ? value.Substring(0, prereleaseSeparator) : value;
            var prerelease = prereleaseSeparator >= 0 ? value.Substring(prereleaseSeparator + 1) : null;

            var coreParts = numericCore.Split('.');
            if (coreParts.Length < 2 || coreParts.Length > 4) return false;

            var numericComponents = new int[4];
            for (var i = 0; i < coreParts.Length; i++)
            {
                if (!IsValidNumericIdentifier(coreParts[i]) ||
                    !int.TryParse(coreParts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numericComponents[i]))
                {
                    return false;
                }
            }

            string[] prereleaseIdentifiers = null;
            if (prerelease != null)
            {
                prereleaseIdentifiers = prerelease.Split('.');
                if (prereleaseIdentifiers.Length == 0 || prereleaseIdentifiers.Any(identifier => !IsValidPrereleaseIdentifier(identifier)))
                {
                    return false;
                }
            }

            version = new ReleaseVersion(numericComponents, prereleaseIdentifiers, coreParts.Length);
            return true;
        }

        public int CompareTo(ReleaseVersion other)
        {
            if (other == null) return 1;

            for (var i = 0; i < numericComponents.Length; i++)
            {
                var numericComparison = numericComponents[i].CompareTo(other.numericComponents[i]);
                if (numericComparison != 0) return numericComparison;
            }

            if (prereleaseIdentifiers == null) return other.prereleaseIdentifiers == null ? 0 : 1;
            if (other.prereleaseIdentifiers == null) return -1;

            var sharedIdentifierCount = Math.Min(prereleaseIdentifiers.Length, other.prereleaseIdentifiers.Length);
            for (var i = 0; i < sharedIdentifierCount; i++)
            {
                var identifierComparison = ComparePrereleaseIdentifiers(prereleaseIdentifiers[i], other.prereleaseIdentifiers[i]);
                if (identifierComparison != 0) return identifierComparison;
            }

            return prereleaseIdentifiers.Length.CompareTo(other.prereleaseIdentifiers.Length);
        }

        public bool Equals(ReleaseVersion other) => CompareTo(other) == 0;

        public override bool Equals(object obj) => Equals(obj as ReleaseVersion);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var component in numericComponents) hash.Add(component);
            if (prereleaseIdentifiers != null)
            {
                foreach (var identifier in prereleaseIdentifiers) hash.Add(identifier, StringComparer.Ordinal);
            }
            return hash.ToHashCode();
        }

        public override string ToString()
        {
            var numericCore = string.Join(".", numericComponents.Take(displayedComponentCount));
            return prereleaseIdentifiers == null
                ? numericCore
                : numericCore + "-" + string.Join(".", prereleaseIdentifiers);
        }

        private static int ComparePrereleaseIdentifiers(string left, string right)
        {
            var leftIsNumeric = IsNumericIdentifier(left);
            var rightIsNumeric = IsNumericIdentifier(right);

            if (leftIsNumeric && rightIsNumeric)
            {
                var lengthComparison = left.Length.CompareTo(right.Length);
                return lengthComparison != 0 ? lengthComparison : StringComparer.Ordinal.Compare(left, right);
            }
            if (leftIsNumeric) return -1;
            if (rightIsNumeric) return 1;
            return StringComparer.Ordinal.Compare(left, right);
        }

        private static bool IsNumericIdentifier(string value) =>
            value.Length > 0 && value.All(character => character >= '0' && character <= '9');

        private static bool IsValidNumericIdentifier(string value) =>
            IsNumericIdentifier(value) && (value.Length == 1 || value[0] != '0');

        private static bool IsValidPrereleaseIdentifier(string value) =>
            value.Length > 0 && value.All(character =>
                character >= '0' && character <= '9' ||
                character >= 'A' && character <= 'Z' ||
                character >= 'a' && character <= 'z' ||
                character == '-') &&
            (!IsNumericIdentifier(value) || IsValidNumericIdentifier(value));
    }
}

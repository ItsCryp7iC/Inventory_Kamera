using System;
using System.Globalization;

namespace InventoryKamera
{
    /// <summary>
    /// Immutable Character-scan timing profile. Live validation established the existing Slower
    /// (1.0x) scanner tier as the fastest reliable Character timing, so faster requested speeds are
    /// clamped to that exact profile without changing Navigation's global delay or other scanners.
    /// </summary>
    internal sealed class CharacterNavigationTiming
    {
        internal const double SafeMinimumMultiplier = 1.0;
        internal const string SafeTierName = "Slower";

        internal double RequestedMultiplier { get; }
        internal double EffectiveMultiplier { get; }
        internal string RequestedTier => DescribeTier(RequestedMultiplier);
        internal string EffectiveTier => DescribeTier(EffectiveMultiplier);

        private CharacterNavigationTiming(double requestedMultiplier)
        {
            if (double.IsNaN(requestedMultiplier) || double.IsInfinity(requestedMultiplier) || requestedMultiplier <= 0)
                throw new ArgumentOutOfRangeException(nameof(requestedMultiplier));

            RequestedMultiplier = requestedMultiplier;
            EffectiveMultiplier = Math.Max(requestedMultiplier, SafeMinimumMultiplier);
        }

        internal static CharacterNavigationTiming FromCurrentScanSpeed()
        {
            return FromMultiplier(Navigation.GetDelay());
        }

        internal static CharacterNavigationTiming FromMultiplier(double requestedMultiplier)
        {
            return new CharacterNavigationTiming(requestedMultiplier);
        }

        /// <summary>
        /// Scales every post-entry Character input hold, transition wait, and OCR retry from the
        /// effective profile. The 20ms guard matches the existing controller-delay calculation, but
        /// the safe 1.0x minimum means current Character timings never approach it.
        /// </summary>
        internal int Scale(int baseMilliseconds)
        {
            if (baseMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(baseMilliseconds));
            return Math.Max(20, (int)(baseMilliseconds * EffectiveMultiplier));
        }

        private static string DescribeTier(double multiplier)
        {
            if (Math.Abs(multiplier - 0.5) < 0.0001) return "Fast";
            if (Math.Abs(multiplier - SafeMinimumMultiplier) < 0.0001) return SafeTierName;
            if (Math.Abs(multiplier - 1.5) < 0.0001) return "Slow";
            return multiplier.ToString("0.###", CultureInfo.InvariantCulture) + "x";
        }
    }
}

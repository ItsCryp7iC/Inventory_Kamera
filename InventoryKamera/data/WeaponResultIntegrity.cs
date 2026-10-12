using System;
using System.Collections.Generic;

namespace InventoryKamera
{
    internal enum WeaponCorrectionOutcome
    {
        Retained,
        Promoted,
        Invalidated,
        RemainedInvalid,
    }

    internal sealed class WeaponValidationResult
    {
        internal WeaponValidationResult(IEnumerable<string> failureCodes)
        {
            FailureCodes = new List<string>(failureCodes ?? Array.Empty<string>()).AsReadOnly();
        }

        internal IReadOnlyList<string> FailureCodes { get; }
        internal bool IsValid => FailureCodes.Count == 0;
        internal string Summary => IsValid ? "valid" : string.Join(", ", FailureCodes);
    }

    /// <summary>
    /// Keeps staged Weapon publication state synchronized with deferred name correction and provides
    /// the common validation boundary used by deterministic publication and GOOD export.
    /// </summary>
    internal static class WeaponResultIntegrity
    {
        internal static WeaponCorrectionOutcome ApplyNameCorrection(
            Weapon weapon,
            StagedInventoryResult<Weapon> stagedResult,
            string correctedName,
            out WeaponValidationResult validation)
        {
            if (weapon == null) throw new ArgumentNullException(nameof(weapon));
            if (stagedResult == null) throw new ArgumentNullException(nameof(stagedResult));
            if (!ReferenceEquals(weapon, stagedResult.Item))
                throw new ArgumentException("The staged result must own the corrected Weapon.", nameof(stagedResult));

            weapon.UpdateName(correctedName);
            if (IsPublishable(weapon, out validation))
            {
                return stagedResult.MarkPublishable()
                    ? WeaponCorrectionOutcome.Promoted
                    : WeaponCorrectionOutcome.Retained;
            }

            return stagedResult.MarkUnpublishable()
                ? WeaponCorrectionOutcome.Invalidated
                : WeaponCorrectionOutcome.RemainedInvalid;
        }

        internal static bool IsPublishable(
            Weapon weapon,
            out WeaponValidationResult validation)
        {
            if (weapon == null)
            {
                validation = new WeaponValidationResult(new[] { "invalid_weapon" });
                return false;
            }

            var failures = new List<string>();
            try
            {
                if (string.IsNullOrWhiteSpace(weapon.Name) || !weapon.HasValidWeaponName())
                    failures.Add("invalid_weapon_key");
                if (!weapon.HasValidLevel()) failures.Add("invalid_level");
                if (!weapon.HasValidRarity()) failures.Add("invalid_rarity");
                if (!weapon.HasValidRefinementLevel()) failures.Add("invalid_refinement");
                if (!weapon.HasValidEquippedCharacter()) failures.Add("invalid_location");
            }
            catch (InvalidOperationException)
            {
                failures.Clear();
                failures.Add("missing_game_data");
            }

            validation = new WeaponValidationResult(failures);
            return validation.IsValid;
        }
    }
}

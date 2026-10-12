using System;
using System.Collections.Generic;
using Xunit;

namespace InventoryKamera.Tests
{
    public class WeaponResultIntegrityTests
    {
        [Fact]
        public void ValidToValidCorrection_RemainsPublished()
        {
            var publisher = new InventoryResultPublisher();
            Weapon weapon = WeaponTestData.WeaponAt(1, "FavoniusSword");
            StagedInventoryResult<Weapon> staged =
                publisher.StageWeapon(1, weapon, isPublishable: true);

            WeaponCorrectionOutcome outcome = WeaponResultIntegrity.ApplyNameCorrection(
                weapon,
                staged,
                "SacrificialSword",
                out WeaponValidationResult validation);

            Assert.Equal(WeaponCorrectionOutcome.Retained, outcome);
            Assert.True(validation.IsValid, validation.Summary);
            Assert.Equal("SacrificialSword", Assert.Single(Commit(publisher).Weapons).Name);
        }

        [Fact]
        public void InvalidToValidCorrection_IsPromoted()
        {
            var publisher = new InventoryResultPublisher();
            Weapon weapon = WeaponTestData.WeaponAt(1, "UnrecognizedWeapon");
            StagedInventoryResult<Weapon> staged =
                publisher.StageWeapon(1, weapon, isPublishable: false);

            WeaponCorrectionOutcome outcome = WeaponResultIntegrity.ApplyNameCorrection(
                weapon,
                staged,
                "FavoniusSword",
                out WeaponValidationResult validation);

            Assert.Equal(WeaponCorrectionOutcome.Promoted, outcome);
            Assert.True(validation.IsValid, validation.Summary);
            Assert.Same(weapon, Assert.Single(Commit(publisher).Weapons));
        }

        [Fact]
        public void ValidToInvalidCorrection_IsDemoted()
        {
            var publisher = new InventoryResultPublisher();
            Weapon weapon = WeaponTestData.WeaponAt(1, "FavoniusSword", "Jean");
            Weapon later = WeaponTestData.WeaponAt(3, "SacrificialSword", "Zhongli");
            StagedInventoryResult<Weapon> staged =
                publisher.StageWeapon(1, weapon, isPublishable: true);
            publisher.StageWeapon(3, later, isPublishable: true);

            WeaponCorrectionOutcome outcome = WeaponResultIntegrity.ApplyNameCorrection(
                weapon,
                staged,
                "Raw unmatched correction",
                out WeaponValidationResult validation);

            Assert.Equal(WeaponCorrectionOutcome.Invalidated, outcome);
            Assert.False(validation.IsValid);
            Assert.Contains("invalid_weapon_key", validation.FailureCodes);
            var inventory = new Inventory();
            var equipped = new List<Weapon>();
            publisher.CommitTo(inventory, equipped, new List<Artifact>());
            Assert.Equal(3, Assert.Single(inventory.Weapons).Id);
            Assert.Same(later, Assert.Single(equipped));
            Assert.Equal("Zhongli", later.EquippedCharacter);
        }

        [Fact]
        public void InvalidToInvalidCorrection_RemainsUnpublished()
        {
            var publisher = new InventoryResultPublisher();
            Weapon weapon = WeaponTestData.WeaponAt(1, "UnrecognizedWeapon");
            StagedInventoryResult<Weapon> staged =
                publisher.StageWeapon(1, weapon, isPublishable: false);

            WeaponCorrectionOutcome outcome = WeaponResultIntegrity.ApplyNameCorrection(
                weapon,
                staged,
                "StillUnrecognized",
                out WeaponValidationResult validation);

            Assert.Equal(WeaponCorrectionOutcome.RemainedInvalid, outcome);
            Assert.False(validation.IsValid);
            Assert.Empty(Commit(publisher).Weapons);
        }

        [Fact]
        public void CorrectionMustOwnTheStagedWeapon()
        {
            var publisher = new InventoryResultPublisher();
            Weapon stagedWeapon = WeaponTestData.WeaponAt(1);
            StagedInventoryResult<Weapon> staged =
                publisher.StageWeapon(1, stagedWeapon, isPublishable: true);

            Assert.Throws<ArgumentException>(() =>
                WeaponResultIntegrity.ApplyNameCorrection(
                    WeaponTestData.WeaponAt(2), staged, "FavoniusSword", out _));
        }

        [Fact]
        public void DefaultWeaponsRemainValid()
        {
            foreach (WeaponType type in Enum.GetValues<WeaponType>())
            {
                var weapon = new Weapon(type, "Jean", WeaponTestData.Snapshot);

                Assert.True(
                    WeaponResultIntegrity.IsPublishable(weapon, out WeaponValidationResult validation),
                    validation.Summary);
            }
        }

        private static Inventory Commit(InventoryResultPublisher publisher)
        {
            var inventory = new Inventory();
            publisher.CommitTo(inventory, new List<Weapon>(), new List<Artifact>());
            return inventory;
        }
    }
}

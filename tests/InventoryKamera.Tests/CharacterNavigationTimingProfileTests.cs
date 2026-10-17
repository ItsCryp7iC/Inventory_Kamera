using System;
using Xunit;

namespace InventoryKamera.Tests
{
    public class CharacterNavigationTimingProfileTests
    {
        [Fact]
        public void Fast_ResolvesToKnownWorkingSlowerProfile()
        {
            CharacterNavigationTiming timing = CharacterNavigationTiming.FromMultiplier(0.5);

            Assert.Equal("Fast", timing.RequestedTier);
            Assert.Equal(0.5, timing.RequestedMultiplier);
            Assert.Equal(CharacterNavigationTiming.SafeTierName, timing.EffectiveTier);
            Assert.Equal(CharacterNavigationTiming.SafeMinimumMultiplier, timing.EffectiveMultiplier);
        }

        [Theory]
        [InlineData(80, 80)]
        [InlineData(100, 100)]
        [InlineData(150, 150)]
        [InlineData(200, 200)]
        [InlineData(250, 250)]
        [InlineData(300, 300)]
        [InlineData(400, 400)]
        [InlineData(600, 600)]
        public void Fast_UsesCentralizedSlowerValuesForEveryCharacterTimingBase(
            int baseMilliseconds,
            int expectedMilliseconds)
        {
            CharacterNavigationTiming timing = CharacterNavigationTiming.FromMultiplier(0.5);

            Assert.Equal(expectedMilliseconds, timing.Scale(baseMilliseconds));
        }

        [Theory]
        [InlineData(1.0, "Slower", 100)]
        [InlineData(1.5, "Slow", 150)]
        public void AlreadySafeProfiles_RemainUnchanged(
            double requestedMultiplier,
            string expectedTier,
            int expectedScaledValue)
        {
            CharacterNavigationTiming timing = CharacterNavigationTiming.FromMultiplier(requestedMultiplier);

            Assert.Equal(requestedMultiplier, timing.RequestedMultiplier);
            Assert.Equal(requestedMultiplier, timing.EffectiveMultiplier);
            Assert.Equal(expectedTier, timing.RequestedTier);
            Assert.Equal(expectedTier, timing.EffectiveTier);
            Assert.Equal(expectedScaledValue, timing.Scale(100));
        }

        [Fact]
        public void CreatingCharacterProfile_DoesNotChangeGlobalOrInventoryTiming()
        {
            double originalDelay = Navigation.GetDelay();
            try
            {
                Navigation.SetDelay(0.5);
                int inventoryBefore = InventoryScraper.ScaledControllerDelay(80);

                CharacterNavigationTiming timing = CharacterNavigationTiming.FromCurrentScanSpeed();

                Assert.Equal(80, timing.Scale(80));
                Assert.Equal(0.5, Navigation.GetDelay());
                Assert.Equal(inventoryBefore, InventoryScraper.ScaledControllerDelay(80));
                Assert.Equal(20, inventoryBefore);
            }
            finally
            {
                Navigation.SetDelay(originalDelay);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void InvalidRequestedMultiplier_IsRejected(double requestedMultiplier)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CharacterNavigationTiming.FromMultiplier(requestedMultiplier));
        }

        [Fact]
        public void NegativeBaseDuration_IsRejected()
        {
            CharacterNavigationTiming timing = CharacterNavigationTiming.FromMultiplier(1.0);

            Assert.Throws<ArgumentOutOfRangeException>(() => timing.Scale(-1));
        }
    }
}

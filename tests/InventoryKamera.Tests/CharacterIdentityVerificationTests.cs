using Newtonsoft.Json;
using Xunit;

namespace InventoryKamera.Tests
{
    public class CharacterIdentityVerificationTests
    {
        [Theory]
        [InlineData("Jean", "Anemo")]
        [InlineData("Skirk", "Cryo")]
        public void OrdinaryCharacterCanonicalNameMatchesGoodName(string name, string element)
        {
            Character character = CreateCharacter(name, element);

            Assert.Equal(name, character.CanonicalName);
            Assert.Equal(name, character.NameGOOD);
        }

        [Theory]
        [InlineData("Anemo")]
        [InlineData("Geo")]
        [InlineData("Electro")]
        [InlineData("Dendro")]
        [InlineData("Hydro")]
        [InlineData("Pyro")]
        [InlineData("Cryo")]
        public void TravelerCanonicalVerificationSucceedsForEverySupportedElement(string element)
        {
            Character traveler = CreateCharacter("Traveler", element);
            bool callbackRan = false;

            bool verified = CharacterScraper.TryRunVerifiedCharacterAction(
                "Traveler",
                traveler,
                () => callbackRan = true);

            Assert.True(verified);
            Assert.True(callbackRan);
            Assert.Equal("Traveler", traveler.CanonicalName);
            Assert.Equal("Traveler" + element, traveler.NameGOOD);
        }

        [Fact]
        public void CanonicalNameIsExcludedFromGoodJson()
        {
            Character traveler = CreateCharacter("Traveler", "Cryo");

            string json = JsonConvert.SerializeObject(traveler);

            Assert.Contains("\"key\":\"TravelerCryo\"", json);
            Assert.DoesNotContain("CanonicalName", json);
        }

        [Theory]
        [InlineData("Jean", "Jean")]
        [InlineData("Skirk", "Skirk")]
        public void OrdinaryCharacterVerificationStillRunsCallback(
            string expectedName,
            string observedName)
        {
            Character character = CreateCharacter(expectedName, "Anemo");
            int callbackCount = 0;

            bool verified = CharacterScraper.TryRunVerifiedCharacterAction(
                observedName,
                character,
                () => callbackCount++);

            Assert.True(verified);
            Assert.Equal(1, callbackCount);
        }

        [Fact]
        public void WrongCharacterDoesNotRunCallback()
        {
            Character traveler = CreateCharacter("Traveler", "Cryo");
            int callbackCount = 0;

            bool verified = CharacterScraper.TryRunVerifiedCharacterAction(
                "Jean",
                traveler,
                () => callbackCount++);

            Assert.False(verified);
            Assert.Equal(0, callbackCount);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void UnresolvedCharacterDoesNotRunCallback(string observedName)
        {
            Character traveler = CreateCharacter("Traveler", "Cryo");
            int callbackCount = 0;

            bool verified = CharacterScraper.TryRunVerifiedCharacterAction(
                observedName,
                traveler,
                () => callbackCount++);

            Assert.False(verified);
            Assert.Equal(0, callbackCount);
        }

        [Fact]
        public void TravelerVerificationAllowsConstellationAndTalentCallbacks()
        {
            Character traveler = CreateCharacter("Traveler", "Cryo");
            bool constellationScanned = false;
            bool talentsScanned = false;

            bool constellationVerified = CharacterScraper.TryRunVerifiedCharacterAction(
                "Traveler",
                traveler,
                () => constellationScanned = true);
            bool talentVerified = CharacterScraper.TryRunVerifiedCharacterAction(
                "Traveler",
                traveler,
                () => talentsScanned = true);

            Assert.True(constellationVerified);
            Assert.True(talentVerified);
            Assert.True(constellationScanned);
            Assert.True(talentsScanned);
        }

        [Fact]
        public void FailedVerificationBlocksConstellationAndTalentCallbacks()
        {
            Character traveler = CreateCharacter("Traveler", "Cryo");
            bool constellationScanned = false;
            bool talentsScanned = false;

            bool constellationVerified = CharacterScraper.TryRunVerifiedCharacterAction(
                "Jean",
                traveler,
                () => constellationScanned = true);
            bool talentVerified = CharacterScraper.TryRunVerifiedCharacterAction(
                null,
                traveler,
                () => talentsScanned = true);

            Assert.False(constellationVerified);
            Assert.False(talentVerified);
            Assert.False(constellationScanned);
            Assert.False(talentsScanned);
        }

        [Theory]
        [InlineData("Manequin1")]
        [InlineData("Manequin2")]
        public void ManequinCanonicalIdentityRemainsAPlaceholder(string canonicalName)
        {
            Character manequin = CreateCharacter(canonicalName, "Cryo");

            Assert.Equal(canonicalName, manequin.CanonicalName);
            Assert.Equal(canonicalName, manequin.NameGOOD);
            Assert.True(CharacterScraper.IsManequinPlaceholder(manequin.CanonicalName));
        }

        private static Character CreateCharacter(string canonicalName, string element) => new Character
        {
            NameGOOD = canonicalName,
            Element = element,
        };
    }
}

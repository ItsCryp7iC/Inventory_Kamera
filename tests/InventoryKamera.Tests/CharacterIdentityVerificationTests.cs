using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
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

        [Fact]
        public void OrdinaryCharacterIsVerifiedOnFirstAttempt()
        {
            Character jean = CreateCharacter("Jean", "Anemo");
            int reads = 0;
            int callbacks = 0;

            bool verified = ExpectedCharacterVerifier.TryVerify(
                jean,
                maxAttempts: 5,
                _ =>
                {
                    reads++;
                    return Attempt(Resolved("Jean", "Anemo"), Unresolved());
                },
                observeAttempt: null,
                waitAfterRejectedAttempt: null,
                verifiedAction: () => callbacks++);

            Assert.True(verified);
            Assert.Equal(1, reads);
            Assert.Equal(1, callbacks);
        }

        [Fact]
        public void RejectedAttemptsRequestFreshReadsUntilLaterAttemptSucceeds()
        {
            Character jean = CreateCharacter("Jean", "Anemo");
            var attempts = new Queue<ExpectedCharacterVerificationAttempt>(new[]
            {
                Attempt(Unresolved("first"), Unresolved("first")),
                Attempt(Unresolved("second"), Unresolved("second")),
                Attempt(Resolved("Jean", "Anemo"), Resolved("Jean", "Anemo")),
            });
            int reads = 0;
            int waits = 0;
            bool callbackRan = false;

            bool verified = ExpectedCharacterVerifier.TryVerify(
                jean,
                maxAttempts: 5,
                _ =>
                {
                    reads++;
                    return attempts.Dequeue();
                },
                observeAttempt: null,
                waitAfterRejectedAttempt: () => waits++,
                verifiedAction: () => callbackRan = true);

            Assert.True(verified);
            Assert.Equal(3, reads);
            Assert.Equal(2, waits);
            Assert.True(callbackRan);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void OneUnreadableOcrPathDoesNotHideAValidIndependentSignal(bool blockSucceeds)
        {
            ExpectedCharacterVerificationAttempt attempt = blockSucceeds
                ? Attempt(Resolved("Jean", "Anemo"), Unresolved("noise"))
                : Attempt(Unresolved("noise"), Resolved("Jean", "Anemo"));

            ExpectedCharacterVerificationDecision result = ExpectedCharacterVerifier.Evaluate("Jean", attempt);

            Assert.True(result.Accepted);
            Assert.Equal("Jean", result.CanonicalName);
            Assert.Equal("Anemo", result.Element);
        }

        [Fact]
        public void MatchingBlockAndLineSignalsAreAccepted()
        {
            ExpectedCharacterVerificationDecision result = ExpectedCharacterVerifier.Evaluate(
                "Jean",
                Attempt(Resolved("Jean", "Anemo"), Resolved("Jean", "Anemo")));

            Assert.True(result.Accepted);
            Assert.Contains("agree", result.Reason);
        }

        [Fact]
        public void RawHeaderPathsAreResolvedIndependentlyWithExistingNormalization()
        {
            GameDataSnapshot snapshot = CreateVerificationSnapshot();

            ExpectedCharacterVerificationAttempt attempt = ExpectedCharacterVerifier.ResolveAttempt(
                "Anemo / Jean",
                "unreadable particles",
                snapshot);
            ExpectedCharacterVerificationDecision result = ExpectedCharacterVerifier.Evaluate("Jean", attempt);

            Assert.True(attempt.Block.IsResolved);
            Assert.False(attempt.Line.IsResolved);
            Assert.True(result.Accepted);
        }

        [Fact]
        public void BothUnreadablePathsExhaustBoundedAttemptsWithoutCallback()
        {
            Character jean = CreateCharacter("Jean", "Anemo");
            int reads = 0;
            int callbackCount = 0;

            bool verified = ExpectedCharacterVerifier.TryVerify(
                jean,
                maxAttempts: 5,
                _ =>
                {
                    reads++;
                    return Attempt(Unresolved("noise"), Unresolved("noise"));
                },
                observeAttempt: null,
                waitAfterRejectedAttempt: null,
                verifiedAction: () => callbackCount++);

            Assert.False(verified);
            Assert.Equal(5, reads);
            Assert.Equal(0, callbackCount);
        }

        [Fact]
        public void WrongCharacterWithSameElementIsRejected()
        {
            ExpectedCharacterVerificationDecision result = ExpectedCharacterVerifier.Evaluate(
                "Jean",
                Attempt(Resolved("Venti", "Anemo"), Unresolved()));

            Assert.False(result.Accepted);
            Assert.Equal("Venti", result.CanonicalName);
        }

        [Fact]
        public void ConflictingCanonicalIdentitiesAreRejectedEvenWhenOneMatchesExpected()
        {
            ExpectedCharacterVerificationDecision result = ExpectedCharacterVerifier.Evaluate(
                "Jean",
                Attempt(Resolved("Jean", "Anemo"), Resolved("Skirk", "Cryo")));

            Assert.False(result.Accepted);
            Assert.Contains("conflicting", result.Reason);
        }

        [Fact]
        public void NameThatDidNotResolveIsRejectedEvenWhenElementDid()
        {
            ExpectedCharacterVerificationDecision result = ExpectedCharacterVerifier.Evaluate(
                "Jean",
                Attempt(new CharacterIdentitySignal("Anemo / ???", null, "Anemo"), Unresolved()));

            Assert.False(result.Accepted);
            Assert.Contains("unresolved", result.Reason);
        }

        [Fact]
        public void TravelerUsesCanonicalIdentityWhileKeepingElementSpecificGoodKey()
        {
            Character traveler = CreateCharacter("Traveler", "Cryo");
            bool callbackRan = false;

            bool verified = ExpectedCharacterVerifier.TryVerify(
                traveler,
                maxAttempts: 1,
                _ => Attempt(Resolved("Traveler", "Cryo"), Unresolved()),
                observeAttempt: null,
                waitAfterRejectedAttempt: null,
                verifiedAction: () => callbackRan = true);

            Assert.True(verified);
            Assert.True(callbackRan);
            Assert.Equal("TravelerCryo", traveler.NameGOOD);
        }

        [Theory]
        [InlineData("Manequin1")]
        [InlineData("Manequin2")]
        public void ManequinCanonicalVerificationBehaviorIsUnchanged(string canonicalName)
        {
            Character manequin = CreateCharacter(canonicalName, "Cryo");
            bool callbackRan = false;

            bool verified = ExpectedCharacterVerifier.TryVerify(
                manequin,
                maxAttempts: 1,
                _ => Attempt(Resolved(canonicalName, "Cryo"), Unresolved()),
                observeAttempt: null,
                waitAfterRejectedAttempt: null,
                verifiedAction: () => callbackRan = true);

            Assert.True(verified);
            Assert.True(callbackRan);
            Assert.Equal(canonicalName, manequin.NameGOOD);
            Assert.Equal(CharacterScanPhaseStatus.NotAttempted, manequin.ConstellationScanStatus);
            Assert.Equal(CharacterScanPhaseStatus.NotAttempted, manequin.TalentScanStatus);
        }

        [Fact]
        public void TalentVerificationFailureMarksPhaseFailedAndKeepsInternalSentinel()
        {
            Character character = CreateCharacter("Albedo", "Geo");

            CharacterScraper.MarkCharacterPhaseUnavailable(character, CharacterVerificationPhase.Talent);

            Assert.Equal(CharacterScanPhaseStatus.Failed, character.TalentScanStatus);
            Assert.Equal(-1, character.Talents["auto"]);
            Assert.Equal(-1, character.Talents["skill"]);
            Assert.Equal(-1, character.Talents["burst"]);
        }

        [Fact]
        public void ConstellationVerificationFailureMarksPhaseFailedAndKeepsInternalSentinel()
        {
            Character character = CreateCharacter("Albedo", "Geo");

            CharacterScraper.MarkCharacterPhaseUnavailable(character, CharacterVerificationPhase.Constellation);

            Assert.Equal(CharacterScanPhaseStatus.Failed, character.ConstellationScanStatus);
            Assert.Equal(-1, character.Constellation);
        }

        [Fact]
        public void TalentOcrFailureMarksPhaseFailed()
        {
            Character character = CreateCharacter("Albedo", "Geo");

            bool succeeded = CharacterScraper.TrySetScannedTalents(
                character,
                CharacterScraper.UnavailableTalents());

            Assert.False(succeeded);
            Assert.Equal(CharacterScanPhaseStatus.Failed, character.TalentScanStatus);
        }

        [Fact]
        public void FailedTalentResultCannotBeMutatedByPostProcessing()
        {
            Character character = CreateCharacter("Albedo", "Geo");
            CharacterScraper.MarkCharacterPhaseUnavailable(character, CharacterVerificationPhase.Talent);

            bool adjusted = CharacterScraper.TryApplyTalentAdjustment(character, "auto", -1);

            Assert.False(adjusted);
            Assert.Equal(-1, character.Talents["auto"]);
        }

        [Fact]
        public void ValidTalentResultMarksPhaseSucceeded()
        {
            Character character = CreateCharacter("Jean", "Anemo");

            bool succeeded = CharacterScraper.TrySetScannedTalents(
                character,
                new Dictionary<string, int>
                {
                    ["auto"] = 6,
                    ["skill"] = 9,
                    ["burst"] = 10,
                });

            Assert.True(succeeded);
            Assert.Equal(CharacterScanPhaseStatus.Succeeded, character.TalentScanStatus);
        }

        [Fact]
        public void PhaseStatusesAreInternalOnly()
        {
            Character character = CreateCharacter("Jean", "Anemo");
            character.Constellation = 0;
            character.MarkConstellationScanSucceeded();
            CharacterScraper.TrySetScannedTalents(
                character,
                new Dictionary<string, int>
                {
                    ["auto"] = 6,
                    ["skill"] = 9,
                    ["burst"] = 10,
                });

            string json = JsonConvert.SerializeObject(character);

            Assert.DoesNotContain("ConstellationScanStatus", json);
            Assert.DoesNotContain("TalentScanStatus", json);
        }

        [Fact]
        public void SuccessfulVerificationDoesNotReplaceValidTalentValues()
        {
            Character character = CreateCharacter("Jean", "Anemo");
            character.Talents = new Dictionary<string, int>
            {
                ["auto"] = 6,
                ["skill"] = 9,
                ["burst"] = 10,
            };

            bool verified = ExpectedCharacterVerifier.TryVerify(
                character,
                maxAttempts: 1,
                _ => Attempt(Resolved("Jean", "Anemo"), Unresolved()),
                observeAttempt: null,
                waitAfterRejectedAttempt: null,
                verifiedAction: () => { });

            Assert.True(verified);
            Assert.Equal(6, character.Talents["auto"]);
            Assert.Equal(9, character.Talents["skill"]);
            Assert.Equal(10, character.Talents["burst"]);
        }

        private static Character CreateCharacter(string canonicalName, string element) => new Character
        {
            NameGOOD = canonicalName,
            Element = element,
        };

        private static CharacterIdentitySignal Resolved(string name, string element) =>
            new CharacterIdentitySignal($"{element} / {name}", name, element);

        private static CharacterIdentitySignal Unresolved(string raw = "") =>
            new CharacterIdentitySignal(raw, null, null);

        private static ExpectedCharacterVerificationAttempt Attempt(
            CharacterIdentitySignal block,
            CharacterIdentitySignal line) =>
            new ExpectedCharacterVerificationAttempt(block, line);

        private static GameDataSnapshot CreateVerificationSnapshot()
        {
            var characters = new Dictionary<string, JObject>
            {
                ["jean"] = CharacterData("Jean", "anemo"),
                ["venti"] = CharacterData("Venti", "anemo"),
                ["skirk"] = CharacterData("Skirk", "cryo"),
                ["traveler"] = CharacterData(
                    "Traveler",
                    "anemo",
                    "geo",
                    "electro",
                    "dendro",
                    "hydro",
                    "pyro",
                    "cryo"),
            };

            return new GameDataSnapshot(
                characters,
                new Dictionary<string, JObject>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, string>
                {
                    ["anemo"] = "Anemo",
                    ["cryo"] = "Cryo",
                },
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        private static JObject CharacterData(string goodName, params string[] elements) => new JObject
        {
            ["GOOD"] = goodName,
            ["Element"] = new JArray(elements),
        };
    }
}

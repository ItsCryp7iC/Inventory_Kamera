using InventoryKamera.game;
using InventoryKamera.ui;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class AchievementPhase7B5DiagnosticsTests
    {
        [Fact]
        public void FourOutstandingIntervals_AreSeparateAndDeterministicallyNumbered()
        {
            AchievementObservedRow[] rows =
            {
                UnresolvedInterval("four", 4, 0.60),
                UnresolvedInterval("one", 1, 0.20),
                UnresolvedInterval("three", 3, 0.40),
                UnresolvedInterval("two", 1, 0.50),
            };

            AchievementCategoryCrawlResult result = Result(rows);

            Assert.Equal(4, result.OutstandingIntervals.Count);
            Assert.Equal(new[] { 1, 2, 3, 4 },
                result.OutstandingIntervals.Select(value => value.IntervalNumber));
            Assert.Equal(new[] { "one", "two", "three", "four" },
                result.OutstandingIntervals.Select(value => value.OcrExcerpt));
            Assert.All(result.OutstandingIntervals, value =>
                Assert.Equal(AchievementUnresolvedCause.TitleOcrUnresolved, value.PrimaryCause));
            Assert.False(result.RecognitionCoverageSufficient);
        }

        [Fact]
        public void ReconciledPartial_IsExcludedFromOutstandingDiagnostics()
        {
            AchievementVisibleRow partial = Visible("clipped", 0.70,
                AchievementVisibleRowVisibility.Partial,
                AchievementIdentityState.Unresolved, canonical: false);
            AchievementVisibleRow complete = Visible("Known title", 0.35,
                AchievementVisibleRowVisibility.Complete,
                AchievementIdentityState.Identified, canonical: true,
                completion: AchievementCompletionState.CompletedClaimed);
            var row = new AchievementObservedRow("canonical:test", true,
                new[]
                {
                    Observation(0, partial,
                        AchievementObservationReconciliationStatus.ReconciledToCanonical,
                        "accepted-adjacent-geometric-match(error=0.0px)"),
                    Observation(1, complete,
                        AchievementObservationReconciliationStatus.CanonicalIdentity),
                }, complete, conflicting: false);

            AchievementCategoryCrawlResult result = Result(new[] { row });

            Assert.Empty(result.OutstandingIntervals);
            Assert.Empty(result.DiagnosticIntervals);
            Assert.True(result.RecognitionCoverageSufficient);
            Assert.True(result.TrustedCompletionCoverageSufficient);
        }

        [Fact]
        public void IdentifiedRowWithUnresolvedCompletion_PreservesIdentityCoverageOnly()
        {
            AchievementVisibleRow visible = Visible("Known title", 0.30,
                AchievementVisibleRowVisibility.Complete,
                AchievementIdentityState.Identified, canonical: true,
                completion: AchievementCompletionState.Unresolved);
            var row = new AchievementObservedRow("canonical:test", true,
                new[] { Observation(0, visible, AchievementObservationReconciliationStatus.CanonicalIdentity) },
                visible, conflicting: false);

            AchievementCategoryCrawlResult result = Result(new[] { row });
            AchievementUnresolvedIntervalDiagnostic diagnostic =
                Assert.Single(result.CompletionLimitedIntervals);

            Assert.True(result.RecognitionCoverageSufficient);
            Assert.False(result.TrustedCompletionCoverageSufficient);
            Assert.False(diagnostic.IdentityOutstanding);
            Assert.Equal(AchievementUnresolvedCause.CompletionEvidenceUnresolved,
                diagnostic.PrimaryCause);
            Assert.Equal(AchievementCompletionState.Unresolved, diagnostic.CompletionState);
            Assert.True(diagnostic.CandidateCanonicalGroupEstablished);
            Assert.True(diagnostic.ExactTitleVerificationUsefulLater);
        }

        [Theory]
        [InlineData((int)AchievementVisibleRowVisibility.Uncertain, (int)AchievementIdentityState.Unresolved,
            "none", false, (int)AchievementUnresolvedCause.UncertainRowBoundary)]
        [InlineData((int)AchievementVisibleRowVisibility.Partial, (int)AchievementIdentityState.Unresolved,
            "missing-or-unverified-movement", false, (int)AchievementUnresolvedCause.InsufficientMotionEvidence)]
        [InlineData((int)AchievementVisibleRowVisibility.Partial, (int)AchievementIdentityState.Unresolved,
            "ambiguous-geometric-match", false, (int)AchievementUnresolvedCause.GeometryMatchAmbiguous)]
        [InlineData((int)AchievementVisibleRowVisibility.Partial, (int)AchievementIdentityState.Unresolved,
            "none", false, (int)AchievementUnresolvedCause.UnrecoveredPartial)]
        [InlineData((int)AchievementVisibleRowVisibility.Complete, (int)AchievementIdentityState.Ambiguous,
            "none", false, (int)AchievementUnresolvedCause.CanonicalIdentityAmbiguous)]
        [InlineData((int)AchievementVisibleRowVisibility.Complete, (int)AchievementIdentityState.Unresolved,
            "none", true, (int)AchievementUnresolvedCause.ConflictingObservation)]
        public void CauseClassification_UsesObservedEvidence(
            int visibilityValue,
            int identityValue,
            string reason,
            bool conflicting,
            int expectedValue)
        {
            var visibility = (AchievementVisibleRowVisibility)visibilityValue;
            var identity = (AchievementIdentityState)identityValue;
            var expected = (AchievementUnresolvedCause)expectedValue;
            AchievementVisibleRow visible = Visible("raw", 0.30, visibility, identity,
                canonical: false, candidateCount: identity == AchievementIdentityState.Ambiguous ? 2 : 0);
            var row = new AchievementObservedRow("interval:test", false,
                new[] { Observation(0, visible, AchievementObservationReconciliationStatus.Unresolved, reason) },
                visible, conflicting);

            AchievementUnresolvedIntervalDiagnostic diagnostic =
                Assert.Single(Result(new[] { row }).OutstandingIntervals);

            Assert.Equal(expected, diagnostic.PrimaryCause);
            Assert.False(diagnostic.CandidateCanonicalGroupEstablished);
            Assert.Equal(AchievementCompletionState.Unresolved, diagnostic.CompletionState);
        }

        [Fact]
        public void DiagnosticFormatting_IsBoundedPrivacySafeAndDoesNotPrintCandidateIds()
        {
            AchievementObservedRow[] rows = Enumerable.Range(0, 200)
                .Select(index => UnresolvedInterval(
                    $"title {index} account 123456789 repeated diagnostic text " + new string('x', 120),
                    index, (index % 8) / 10.0))
                .ToArray();
            var diagnostic = new AchievementBulkDiagnosticResult(Result(rows));

            string details = AchievementBulkDiagnosticReportBuilder.BuildDiagnosticDetails(diagnostic);

            Assert.True(details.Length <=
                AchievementBulkDiagnosticReportBuilder.MaximumDiagnosticDetailCharacters);
            Assert.Contains("[redacted-number]", details);
            Assert.DoesNotContain("123456789", details);
            Assert.Contains("canonical IDs", details);
            Assert.Contains("diagnostic details truncated", details);
        }

        [Fact]
        public void ResultDialog_ProvidesReadOnlyBoundedCopyAction()
        {
            string copied = null;
            using var form = new AchievementBulkDiagnosticResultForm(
                "summary", "sanitized details", value => copied = value);

            form.CopyDiagnosticDetails();

            Assert.True(form.SummaryBox.ReadOnly);
            Assert.Equal("summary", form.SummaryBox.Text);
            Assert.Equal("Copy Diagnostic Details", form.CopyButton.Text);
            Assert.Equal("sanitized details", copied);
            Assert.True(form.MinimumSize.Width > 0);
        }

        [Theory]
        [InlineData((int)AchievementCategoryCrawlState.Cancelled)]
        [InlineData((int)AchievementCategoryCrawlState.Failed)]
        public void TerminalDiagnosticWithoutFrames_IsSanitizedAndBounded(int stateValue)
        {
            var state = (AchievementCategoryCrawlState)stateValue;
            var diagnostic = new AchievementBulkDiagnosticResult(state,
                "reason with account 123456789\r\nsecond line");

            string details = AchievementBulkDiagnosticReportBuilder.BuildDiagnosticDetails(diagnostic);

            Assert.Contains($"state={state}", details);
            Assert.Contains("[redacted-number]", details);
            Assert.DoesNotContain("123456789", details);
            Assert.True(details.Length <=
                AchievementBulkDiagnosticReportBuilder.MaximumDiagnosticDetailCharacters);
        }

        [Fact]
        public void TimingAccounting_SeparatesExclusiveAndNestedDurations()
        {
            var timing = new AchievementCategoryCrawlTiming(
                capture: Ms(2), frameProcessing: Ms(20), scrolling: Ms(3), settling: Ms(4),
                deduplication: Ms(1), total: Ms(40), rowDetection: Ms(5), titleOcr: Ms(6),
                completionOcr: Ms(4), rewardOcr: Ms(0), stabilizationAnalysis: Ms(1),
                frameEvidenceDetection: Ms(2), viewportFingerprinting: Ms(3),
                frameReader: Ms(10), layoutProcessing: Ms(1), observationCollection: Ms(1),
                motionAnalysis: Ms(1), reconciliation: Ms(1), progressReporting: Ms(1),
                screenDetection: Ms(1), completedTotal: Ms(1), ocrCropResize: Ms(2),
                ocrPreprocessing: Ms(3), ocrEngine: Ms(4), frameReaderOther: Ms(1));

            Assert.Equal(Ms(3), timing.OtherFrameProcessing);
            Assert.Equal(Ms(30), timing.AccountedExclusive);
            Assert.Equal(Ms(10), timing.Unattributed);
            Assert.Equal(Ms(6), timing.TitleOcr); // Nested, not added to AccountedExclusive.
        }

        private static AchievementCategoryCrawlResult Result(
            IEnumerable<AchievementObservedRow> rows)
        {
            return new AchievementCategoryCrawlResult(
                new AchievementCategoryCrawlContext("test"),
                AchievementCategoryCrawlState.Complete,
                "verified bottom",
                1,
                0,
                rows,
                Array.Empty<AchievementScrollObservation>(),
                Array.Empty<AchievementCategoryFrameObservation>(),
                AchievementScrollProgressState.ReachedBottom,
                "test",
                new AchievementCategoryCrawlTiming(TimeSpan.Zero, TimeSpan.Zero,
                    TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
                string.Empty);
        }

        private static AchievementObservedRow UnresolvedInterval(string title, int frame, double y)
        {
            AchievementVisibleRow visible = Visible(title, y,
                AchievementVisibleRowVisibility.Complete,
                AchievementIdentityState.Unresolved, canonical: false);
            return new AchievementObservedRow($"interval:{frame}:{y}", false,
                new[] { Observation(frame, visible, AchievementObservationReconciliationStatus.Unresolved) },
                visible, conflicting: false);
        }

        private static AchievementRowFrameObservation Observation(int frame,
            AchievementVisibleRow row, AchievementObservationReconciliationStatus status,
            string reason = "none") =>
            new(frame, row, status, reconciliationReason: reason);

        private static AchievementVisibleRow Visible(string title, double y,
            AchievementVisibleRowVisibility visibility, AchievementIdentityState identity,
            bool canonical, AchievementCompletionState completion = AchievementCompletionState.Unresolved,
            int candidateCount = 0)
        {
            int[] candidates = Enumerable.Range(700, candidateCount).ToArray();
            return new AchievementVisibleRow(
                rowIndex: 2,
                normalizedBounds: new NormalizedRectangle(0.52, y, 0.42, 0.10),
                visibility: visibility,
                rawTitle: title,
                normalizedTitle: title.ToLowerInvariant(),
                identityState: identity,
                candidateIds: candidates,
                stageGroupId: null,
                completionState: completion,
                progressCurrent: null,
                progressTarget: null,
                rewardValue: null,
                identityScore: 0,
                runnerUpScore: 0,
                ocrConfidence: 0,
                suitableForTrustedCompletionExtraction: canonical,
                fingerprint: new AchievementRowFingerprint(canonical ? "canonical:test" : "unresolved", canonical),
                identityResult: null,
                completionResult: null,
                stageProgressResult: null,
                diagnostics: string.Empty);
        }

        private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);
    }
}

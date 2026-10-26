using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class AchievementCategoryCrawlerTests
    {
        [Fact]
        public void OverlappingFrames_DeduplicateCanonicalRows_AndRequirePositiveStableBottom()
        {
            AchievementBulkFrameResult first = Frame(Row(1), Row(2), Row(3), Row(4));
            AchievementBulkFrameResult second = Frame(Row(3), Row(4), Row(5), Row(6));
            AchievementBulkFrameResult stableBottom = Frame(Row(3), Row(4), Row(5), Row(6));
            using Bitmap start = Image(16);
            using Bitmap moved = Image(64);
            using Bitmap stable = Image(64);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { first, second, stableBottom },
                new[] { moved, stable },
                new[] { Evidence(top: true), Evidence(bottom: true), Evidence(bottom: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(
                Context(), start, CancellationToken.None);

            Assert.True(result.State == AchievementCategoryCrawlState.Complete,
                result.Reason + Environment.NewLine + result.Diagnostics);
            Assert.Equal(AchievementScrollProgressState.ReachedBottom, result.FinalScrollState);
            Assert.Equal(6, result.IdentifiedRows.Count);
            Assert.Equal(6, result.RepeatedObservations);
            Assert.Equal(3, result.ProcessedFrames);
            Assert.Equal(2, result.ScrollActions);
            Assert.Contains("stable viewport", result.Reason);
            Assert.True(result.RecognitionCoverageSufficient);
            Assert.Equal(2, result.OverlapVerifiedScrolls);
            Assert.Equal(0, result.UnobservedMovementGaps);
        }

        [Fact]
        public void PartialRowThatBecomesComplete_IsRecoveredWithoutLosingProvenance()
        {
            AchievementVisibleRow partial = Row(10, AchievementVisibleRowVisibility.Partial);
            AchievementVisibleRow complete = Row(10);
            using Bitmap start = Image(16);
            using Bitmap moved = Image(80);
            using Bitmap stable = Image(80);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(partial), Frame(complete), Frame(complete) },
                new[] { moved, stable },
                new[] { Evidence(top: true), Evidence(bottom: true), Evidence(bottom: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            AchievementObservedRow observed = Assert.Single(result.IdentifiedRows);
            Assert.Equal(3, observed.ObservationCount);
            Assert.True(observed.EverFullyVisible);
            Assert.Equal(AchievementVisibleRowVisibility.Complete, observed.SelectedObservation.Visibility);
            Assert.Equal(1, result.PartialRowsRecovered);
            Assert.Equal(0, result.PartialRowsOutstanding);
            Assert.True(result.RecognitionCoverageSufficient);
        }

        [Fact]
        public void UnidentifiedPartialThatBecomesComplete_IsGeometricallyReconciled()
        {
            AchievementCategoryCrawlResult result = ReconciliationCrawl(
                new[]
                {
                    Frame(Row(1, normalizedY: 0.70), Unresolved("clipped", 0.80, AchievementVisibleRowVisibility.Partial)),
                    Frame(Row(1, normalizedY: 0.35), Row(10, normalizedY: 0.45)),
                    Frame(Row(1, normalizedY: 0.35), Row(10, normalizedY: 0.45)),
                });

            Assert.True(result.State == AchievementCategoryCrawlState.Complete,
                result.Reason + Environment.NewLine + result.Diagnostics);
            Assert.Equal(6, result.TotalRawObservations);
            Assert.Equal(1, result.RawUnresolvedObservations);
            Assert.Equal(1, result.ReconciledUnresolvedObservations);
            Assert.Equal(0, result.OutstandingUnresolvedObservations);
            Assert.Equal(1, result.PartialObservationsRecovered);
            Assert.Equal(0, result.DistinctUnresolvedRowIntervals);
            Assert.True(result.RecognitionCoverageSufficient);
            AchievementRowFrameObservation recovered = Assert.Single(result.IdentifiedRows
                .Single(row => row.Key == "canonical:10").Observations,
                value => value.ReconciledToCanonical);
            Assert.Equal(1, recovered.MatchedFrameIndex);
            Assert.Equal(1, recovered.MovementBatchIndex);
            Assert.InRange(recovered.GeometricErrorPixels.Value, 0, 0.01);
            Assert.Contains("accepted-adjacent-geometric-match", recovered.ReconciliationReason);
        }

        [Fact]
        public void MultiplePartialObservations_ReconcileToOneLaterCanonicalRow()
        {
            AchievementCategoryCrawlResult result = ReconciliationCrawl(
                new[]
                {
                    Frame(Row(1, normalizedY: 0.70), Unresolved("first", 0.80, AchievementVisibleRowVisibility.Partial)),
                    Frame(Row(1, normalizedY: 0.35), Unresolved("second", 0.45, AchievementVisibleRowVisibility.Partial)),
                    Frame(Row(1, normalizedY: 0.00), Row(10, normalizedY: 0.10)),
                    Frame(Row(1, normalizedY: 0.00), Row(10, normalizedY: 0.10)),
                });

            AchievementObservedRow row = result.IdentifiedRows.Single(value => value.Key == "canonical:10");
            Assert.Equal(2, row.ReconciledObservationCount);
            Assert.Equal(2, result.PartialObservationsRecovered);
            Assert.Equal(0, result.OutstandingUnresolvedObservations);
            Assert.True(result.RecognitionCoverageSufficient);
        }

        [Fact]
        public void LaterPartial_CanLinkToPreviouslyIdentifiedRowWithVerifiedMotion()
        {
            AchievementCategoryCrawlResult result = ReconciliationCrawl(
                new[]
                {
                    Frame(Row(1, normalizedY: 0.70), Row(10, normalizedY: 0.80)),
                    Frame(Row(1, normalizedY: 0.35),
                        Unresolved("clipped", 0.45, AchievementVisibleRowVisibility.Partial),
                        Row(20, normalizedY: 0.70)),
                    Frame(Row(1, normalizedY: 0.35),
                        Unresolved("clipped", 0.45, AchievementVisibleRowVisibility.Partial),
                        Row(20, normalizedY: 0.70)),
                });

            Assert.Equal(2, result.ObservationsLinkedToPreviouslyIdentifiedRows);
            Assert.Equal(0, result.OutstandingUnresolvedObservations);
            Assert.True(result.RecognitionCoverageSufficient);
        }

        [Fact]
        public void PartialReconciledToStagedFamily_PreservesStageTrustBoundary()
        {
            AchievementVisibleRow staged = Row(20,
                completion: AchievementCompletionState.Incomplete,
                candidateIds: new[] { 20, 21 },
                identity: AchievementIdentityState.StageGroupIdentified,
                stageGroupId: 20,
                fingerprint: "canonical:20,21",
                normalizedY: 0.45);
            AchievementCategoryCrawlResult result = ReconciliationCrawl(
                new[]
                {
                    Frame(Row(1, normalizedY: 0.70),
                        Unresolved("stage clipped", 0.80, AchievementVisibleRowVisibility.Partial)),
                    Frame(Row(1, normalizedY: 0.35), staged),
                    Frame(Row(1, normalizedY: 0.35), staged),
                });

            AchievementObservedRow observed = result.IdentifiedRows.Single(row => row.Key == "canonical:20,21");
            Assert.Equal(new[] { 20, 21 }, observed.SelectedObservation.CandidateIds);
            Assert.Equal(1, observed.ReconciledObservationCount);
            Assert.False(observed.HasTrustedCompletionEvidence);
            Assert.False(result.TrustedCompletionCoverageSufficient);
        }

        [Fact]
        public void PartialWithGeometryIncompatibleWithMeasuredMotion_RemainsUnresolved()
        {
            AchievementCategoryCrawlResult result = ReconciliationCrawl(
                new[]
                {
                    Frame(Row(1, normalizedY: 0.70), Unresolved("clipped", 0.80, AchievementVisibleRowVisibility.Partial)),
                    Frame(Row(1, normalizedY: 0.35), Row(10, normalizedY: 0.50)),
                    Frame(Row(1, normalizedY: 0.35), Row(10, normalizedY: 0.50)),
                });

            Assert.Equal(1, result.OutstandingUnresolvedObservations);
            Assert.False(result.RecognitionCoverageSufficient);
            Assert.Contains(result.UnresolvedRows.SelectMany(row => row.Observations),
                value => value.ReconciliationReason == "geometry-incompatible-with-measured-movement");
        }

        [Fact]
        public void AmbiguousGeometricMatch_RemainsUnresolved()
        {
            AchievementCategoryCrawlResult result = ReconciliationCrawl(
                new[]
                {
                    Frame(Row(1, normalizedY: 0.70), Unresolved("clipped", 0.80, AchievementVisibleRowVisibility.Partial)),
                    Frame(Row(1, normalizedY: 0.35), Row(10, normalizedY: 0.447), Row(20, normalizedY: 0.453)),
                    Frame(Row(1, normalizedY: 0.35), Row(10, normalizedY: 0.447), Row(20, normalizedY: 0.453)),
                });

            Assert.Equal(1, result.OutstandingUnresolvedObservations);
            Assert.False(result.RecognitionCoverageSufficient);
            Assert.Contains(result.UnresolvedRows.SelectMany(row => row.Observations),
                value => value.ReconciliationReason == "ambiguous-geometric-match");
        }

        [Fact]
        public void MissingMovementMeasurement_DoesNotReconcilePartialByPositionGuess()
        {
            AchievementCategoryCrawlResult result = ReconciliationCrawl(
                new[]
                {
                    Frame(Unresolved("clipped", 0.80, AchievementVisibleRowVisibility.Partial)),
                    Frame(Row(10, normalizedY: 0.45)),
                    Frame(Row(10, normalizedY: 0.45)),
                });

            Assert.Equal(1, result.OutstandingUnresolvedObservations);
            Assert.False(result.RecognitionCoverageSufficient);
            Assert.Contains(result.UnresolvedRows.SelectMany(row => row.Observations),
                value => value.ReconciliationReason == "missing-or-unverified-movement");
        }

        [Fact]
        public void PartialChainConnectingDifferentCanonicalRows_FailsClosedAsConflict()
        {
            AchievementCategoryCrawlResult result = ReconciliationCrawl(
                new[]
                {
                    Frame(Row(99, normalizedY: 0.70), Row(10, normalizedY: 0.80)),
                    Frame(Row(99, normalizedY: 0.35), Unresolved("clipped", 0.45, AchievementVisibleRowVisibility.Partial)),
                    Frame(Row(99, normalizedY: 0.00), Row(20, normalizedY: 0.10)),
                    Frame(Row(99, normalizedY: 0.00), Row(20, normalizedY: 0.10)),
                });

            Assert.Equal(1, result.ReconciliationConflicts);
            Assert.NotEmpty(result.ConflictingRows);
            Assert.False(result.RecognitionCoverageSufficient);
            Assert.Contains(result.ConflictingRows.SelectMany(row => row.Observations),
                value => value.ReconciliationReason.Contains("conflicting-canonical-identities"));
        }

        [Fact]
        public void ReconciledIdentity_DoesNotFabricateCompletionTrust()
        {
            AchievementCategoryCrawlResult result = ReconciliationCrawl(
                new[]
                {
                    Frame(Row(1, normalizedY: 0.70), Unresolved("clipped", 0.80, AchievementVisibleRowVisibility.Partial)),
                    Frame(Row(1, normalizedY: 0.35),
                        Row(10, completion: AchievementCompletionState.Unresolved, normalizedY: 0.45)),
                    Frame(Row(1, normalizedY: 0.35),
                        Row(10, completion: AchievementCompletionState.Unresolved, normalizedY: 0.45)),
                });

            Assert.True(result.RecognitionCoverageSufficient);
            Assert.False(result.TrustedCompletionCoverageSufficient);
            Assert.Equal(1, result.IdentifiedGroupsLackingTrustedCompletionEvidence);
            Assert.Equal(AchievementCompletionState.Unresolved,
                result.IdentifiedRows.Single(row => row.Key == "canonical:10")
                    .SelectedObservation.CompletionState);
        }

        [Fact]
        public void UncertainOrCompleteUnreadableRows_AreNeverReconciledByGeometry()
        {
            AchievementCategoryCrawlResult result = ReconciliationCrawl(
                new[]
                {
                    Frame(Row(1, normalizedY: 0.70),
                        Unresolved("uncertain", 0.80, AchievementVisibleRowVisibility.Uncertain),
                        Unresolved("complete collision", 0.60, AchievementVisibleRowVisibility.Complete)),
                    Frame(Row(1, normalizedY: 0.35), Row(10, normalizedY: 0.45), Row(20, normalizedY: 0.25)),
                    Frame(Row(1, normalizedY: 0.35), Row(10, normalizedY: 0.45), Row(20, normalizedY: 0.25)),
                });

            Assert.Equal(2, result.OutstandingUnresolvedObservations);
            Assert.Equal(0, result.ReconciledUnresolvedObservations);
            Assert.False(result.RecognitionCoverageSufficient);
        }

        [Fact]
        public void FrameOcrTiming_IsAggregatedWithoutChangingTraversalBehavior()
        {
            AchievementCategoryCrawlResult result = ReconciliationCrawl(
                new[]
                {
                    TimedFrame(Row(1, normalizedY: 0.70)),
                    TimedFrame(Row(1, normalizedY: 0.35), Row(10, normalizedY: 0.45)),
                    TimedFrame(Row(1, normalizedY: 0.35), Row(10, normalizedY: 0.45)),
                });

            Assert.Equal(TimeSpan.FromMilliseconds(3), result.Timing.RowDetection);
            Assert.Equal(TimeSpan.FromMilliseconds(6), result.Timing.TitleOcr);
            Assert.Equal(TimeSpan.FromMilliseconds(9), result.Timing.CompletionOcr);
            Assert.Equal(TimeSpan.FromMilliseconds(12), result.Timing.RewardOcr);
            Assert.Equal(TimeSpan.FromMilliseconds(0.75), result.Timing.ScreenDetection);
            Assert.Equal(TimeSpan.FromMilliseconds(0.75), result.Timing.CompletedTotal);
            Assert.Equal(TimeSpan.FromMilliseconds(1.5), result.Timing.OcrCropResize);
            Assert.Equal(TimeSpan.FromMilliseconds(3), result.Timing.OcrPreprocessing);
            Assert.Equal(TimeSpan.FromMilliseconds(6), result.Timing.OcrEngine);
            Assert.Equal(TimeSpan.FromMilliseconds(13.5), result.Timing.FrameReaderOther);
            Assert.Equal(result.Timing.TitleOcr,
                TimeSpan.FromTicks(result.FrameObservations.Sum(frame =>
                    frame.Timing.ReaderTiming.TitleOcr.Ticks)));
            Assert.Equal(result.ProcessedFrames, result.FrameObservations.Count);
        }

        [Fact]
        public void LaterUnreadableObservation_DoesNotReplaceStrongerCanonicalEvidence()
        {
            using Bitmap start = Image(16);
            using Bitmap moved = Image(80);
            using Bitmap stable = Image(80);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(10)), Frame(Unresolved("damage"), Row(10)), Frame(Unresolved("damage"), Row(10)) },
                new[] { moved, stable },
                new[] { Evidence(top: true), Evidence(bottom: true), Evidence(bottom: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Single(result.IdentifiedRows);
            Assert.Equal(2, result.UnresolvedRows.Count);
            Assert.Equal(3, result.IdentifiedRows[0].ObservationCount);
            Assert.False(result.RecognitionCoverageSufficient);
        }

        [Fact]
        public void EquallyStrongConflictingCanonicalObservations_AreMarkedUnresolved()
        {
            using Bitmap start = Image(16);
            using Bitmap moved = Image(80);
            using Bitmap stable = Image(80);
            AchievementVisibleRow completed = Row(10, completion: AchievementCompletionState.CompletedClaimed);
            AchievementVisibleRow incomplete = Row(10, completion: AchievementCompletionState.Incomplete);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(completed), Frame(incomplete), Frame(incomplete) },
                new[] { moved, stable },
                new[] { Evidence(top: true), Evidence(bottom: true), Evidence(bottom: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Empty(result.IdentifiedRows);
            Assert.Single(result.ConflictingRows);
            Assert.False(result.IsComplete && result.ConflictingRows.Count == 0);
        }

        [Fact]
        public void MixedUniqueStageCollisionAndUnresolved_UsesOnlyTrustedCanonicalFingerprints()
        {
            AchievementVisibleRow unique = Row(10);
            AchievementVisibleRow stage = Row(20, candidateIds: new[] { 20, 21 },
                identity: AchievementIdentityState.StageGroupIdentified, stageGroupId: 20,
                fingerprint: "canonical:20,21");
            AchievementVisibleRow collision = Row(30, candidateIds: new[] { 30, 31 },
                identity: AchievementIdentityState.Ambiguous, canonical: false,
                fingerprint: "diagnostic:let the wind lead");
            using Bitmap start = Image(16);
            using Bitmap stable = Image(16);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(unique, stage, collision, Unresolved("noise")), Frame(unique, stage, collision, Unresolved("noise")) },
                new[] { stable },
                new[] { Evidence(top: true, bottom: true), Evidence(bottom: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Equal(2, result.IdentifiedRows.Count);
            Assert.Equal(4, result.UnresolvedRows.Count);
            Assert.DoesNotContain(result.IdentifiedRows, row => row.Key.Contains("let the wind lead"));
            Assert.Equal(2, result.AmbiguousRows.Count);
            Assert.Equal(1, result.AmbiguousGroupCount);
            Assert.False(result.RecognitionCoverageSufficient);
        }

        [Fact]
        public void CompleteTraversal_WithUnobservedMovementGap_ReportsCoverageInsufficient()
        {
            using Bitmap start = BandedImage(0);
            using Bitmap moved = BandedImage(-60);
            using Bitmap stable = BandedImage(-60);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)), Frame(Row(10)), Frame(Row(10)) },
                new[] { moved, stable },
                new[] { Evidence(top: true), Evidence(bottom: true), Evidence(bottom: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(
                Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Complete, result.State);
            Assert.Equal(1, result.UnobservedMovementGaps);
            Assert.False(result.ScrollObservations[0].OverlapVerified);
            Assert.True(result.ScrollObservations[1].OverlapVerified);
            Assert.False(result.RecognitionCoverageSufficient);
        }

        [Fact]
        public void CompleteTraversal_WithPartialRowNeverRecovered_ReportsCoverageInsufficient()
        {
            AchievementVisibleRow partial = Row(10, AchievementVisibleRowVisibility.Partial);
            using Bitmap start = Image(16);
            using Bitmap stable = Image(16);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1), partial, Row(20)), Frame(Row(1), partial, Row(20)) },
                new[] { stable },
                new[] { Evidence(top: true, bottom: true), Evidence(bottom: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(
                Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Complete, result.State);
            Assert.Equal(1, result.PartialRowsOutstanding);
            Assert.False(result.RecognitionCoverageSufficient);
        }

        [Fact]
        public void BottomThumbWithoutStablePostScrollViewport_RemainsIncomplete()
        {
            using Bitmap start = BandedImage(0);
            using Bitmap moved = BandedImage(-60);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)), Frame(Row(1, normalizedY: 0.15)) },
                new[] { moved },
                new[] { Evidence(top: true, bottom: true), Evidence(bottom: true) },
                new AchievementCategoryCrawlOptions(maximumCapturedFrames: 2,
                    scrollSettleMilliseconds: 0, maximumStabilizationCaptures: 1));

            AchievementCategoryCrawlResult result = crawler.Crawl(
                Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Incomplete, result.State);
            Assert.False(Assert.Single(result.ScrollObservations).ViewportStable);
            Assert.All(result.FrameObservations, frame => Assert.True(frame.BottomAtExtent));
        }

        [Fact]
        public void SummaryCardNeverEntersCrawlerObservations()
        {
            using Bitmap start = Image(16);
            using Bitmap stable = Image(16);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)), Frame(Row(1)) },
                new[] { stable },
                new[] { Evidence(top: true, bottom: true), Evidence(bottom: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Single(result.ObservedRows);
            Assert.DoesNotContain(result.ObservedRows, row =>
                row.SelectedObservation?.RawTitle == "Achievement Progress");
        }

        [Fact]
        public void UnexpectedCategoryChange_StopsWithoutMergingLaterRows()
        {
            using Bitmap start = Image(16);
            using Bitmap moved = Image(64);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)), Frame(Row(2)) },
                new[] { moved },
                new[] { Evidence(top: true, signature: "A"), Evidence(signature: "B") });

            AchievementCategoryCrawlResult result = crawler.Crawl(
                new AchievementCategoryCrawlContext("Category", "A"), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Failed, result.State);
            Assert.Equal(AchievementScrollProgressState.LostScreen, result.FinalScrollState);
            Assert.Contains("signature", result.Reason);
        }

        [Fact]
        public void RequiredCategorySignature_BecomingAmbiguousStopsTraversal()
        {
            using Bitmap start = Image(16);
            using Bitmap moved = Image(64);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)), Frame(Row(1), Row(2)) },
                new[] { moved },
                new[] { Evidence(top: true, signature: "A"), Evidence() });

            AchievementCategoryCrawlResult result = crawler.Crawl(
                new AchievementCategoryCrawlContext("Category",
                    requireVerifiedCategorySignature: true),
                start,
                CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Failed, result.State);
            Assert.Equal(AchievementScrollProgressState.LostScreen, result.FinalScrollState);
            Assert.Contains("could not be positively verified after scrolling", result.Reason);
        }

        [Fact]
        public void RequiredCategorySignature_MissingAtStartStopsWithoutScrolling()
        {
            using Bitmap start = Image(16);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)) }, Array.Empty<Bitmap>(),
                new[] { Evidence(top: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(
                new AchievementCategoryCrawlContext("Category",
                    requireVerifiedCategorySignature: true),
                start,
                CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Failed, result.State);
            Assert.Equal(0, result.ScrollActions);
            Assert.Contains("starting frame", result.Reason);
        }

        [Theory]
        [InlineData((int)AchievementScreenState.SearchActive)]
        [InlineData((int)AchievementScreenState.SearchNoResults)]
        [InlineData((int)AchievementScreenState.Unknown)]
        [InlineData((int)AchievementScreenState.NotAchievementScreen)]
        public void UnsupportedStartingScreen_IsRejected(int stateValue)
        {
            using Bitmap start = Image(16);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame((AchievementScreenState)stateValue, Row(1)) },
                Array.Empty<Bitmap>(),
                new[] { Evidence(top: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Failed, result.State);
            Assert.Equal(0, result.ScrollActions);
        }

        [Fact]
        public void SearchBecomesActiveAfterScroll_IsReportedAsLostScreen()
        {
            using Bitmap start = Image(16);
            using Bitmap moved = Image(64);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)), Frame(AchievementScreenState.SearchActive, Row(2)) },
                new[] { moved }, new[] { Evidence(top: true), Evidence() });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Failed, result.State);
            Assert.Equal(AchievementScrollProgressState.LostScreen, result.FinalScrollState);
        }

        [Fact]
        public void UnsupportedStartPosition_ReturnsIncompleteWithoutScrolling()
        {
            using Bitmap start = Image(16);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)) }, Array.Empty<Bitmap>(), new[] { Evidence() });

            AchievementCategoryCrawlResult result = crawler.Crawl(
                new AchievementCategoryCrawlContext("Category"), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Incomplete, result.State);
            Assert.Equal(0, result.ScrollActions);
            Assert.Contains("top", result.Reason);
        }

        [Fact]
        public void ControllerIdleLayout_IsNotAcceptedForMouseWheelTraversal()
        {
            using Bitmap start = Image(16);
            AchievementBulkFrameResult frame = new(
                AchievementScreenState.Idle, new[] { Row(1) }, 17,
                new AchievementBulkFrameTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
                    TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 1, 1),
                "Controller idle.", isKeyboardMouseIdle: false);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { frame }, Array.Empty<Bitmap>(), new[] { Evidence(top: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Failed, result.State);
            Assert.Contains("Keyboard/Mouse", result.Reason);
        }

        [Fact]
        public void NoMovementWithoutBottomProof_UsesBoundedRecoveryThenReturnsIncomplete()
        {
            using Bitmap start = Image(16);
            using Bitmap duplicate1 = Image(16);
            using Bitmap duplicate2 = Image(16);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)), Frame(Row(1)), Frame(Row(1)) },
                new[] { duplicate1, duplicate2 },
                new[] { Evidence(top: true), Evidence(), Evidence() },
                new AchievementCategoryCrawlOptions(maximumNoMovementRecoveryAttempts: 1,
                    scrollSettleMilliseconds: 0, maximumStabilizationCaptures: 1));

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Incomplete, result.State);
            Assert.Equal(2, result.ScrollActions);
            Assert.Equal(AchievementScrollProgressState.NoMovementUnknown, result.FinalScrollState);
            Assert.Contains("bottom proof", result.Reason);
        }

        [Fact]
        public void NoNewCanonicalIdsAfterMovement_DoesNotProveBottom()
        {
            using Bitmap start = Image(16);
            using Bitmap moved = Image(64);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1, normalizedY: 0.20)), Frame(Row(1, normalizedY: 0.18)) },
                new[] { moved },
                new[] { Evidence(top: true), Evidence() },
                new AchievementCategoryCrawlOptions(maximumCapturedFrames: 2,
                    scrollSettleMilliseconds: 0, maximumStabilizationCaptures: 1));

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Incomplete, result.State);
            Assert.Equal(AchievementScrollProgressState.ScrollAdvanced, result.FinalScrollState);
        }

        [Fact]
        public void MaximumFrameLimit_IsIncomplete()
        {
            using Bitmap start = Image(16);
            using Bitmap moved = Image(64);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)), Frame(Row(2)) }, new[] { moved },
                new[] { Evidence(top: true), Evidence() },
                new AchievementCategoryCrawlOptions(maximumCapturedFrames: 2,
                    scrollSettleMilliseconds: 0, maximumStabilizationCaptures: 1));

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Incomplete, result.State);
            Assert.Contains("limit", result.Reason);
        }

        [Fact]
        public void FullTraversalProfile_LargeCategoryStopsAtEightyFrameLimit()
        {
            const int frameCount = AchievementBulkDiagnosticOptions.FullTraversalMaximumCapturedFrames;
            var frames = Enumerable.Range(0, frameCount)
                .Select(index => Frame(Row(1,
                    normalizedY: (index & 1) == 0 ? 0.20 : 0.30)))
                .ToArray();
            var evidence = Enumerable.Range(0, frameCount)
                .Select(index => Evidence(top: index == 0, signature: "A"))
                .ToArray();
            var captures = Enumerable.Range(1, frameCount - 1)
                .Select(index => SmallImage((byte)((index & 1) == 0 ? 20 : 80)))
                .ToArray();
            using Bitmap start = SmallImage(20);
            try
            {
                AchievementBulkDiagnosticOptions profile =
                    AchievementBulkDiagnosticOptions.FullTraversalProfile;
                var options = new AchievementCategoryCrawlOptions(
                    maximumCapturedFrames: profile.MaximumCapturedFrames,
                    maximumScrollActions: profile.MaximumScrollBatches,
                    scrollSettleMilliseconds: 0,
                    wheelNotchesPerBatch: profile.WheelNotchesPerBatch,
                    maximumStabilizationCaptures: 1);
                AchievementCategoryCrawler crawler = Crawler(
                    frames,
                    captures,
                    evidence,
                    options);

                AchievementCategoryCrawlResult result = crawler.Crawl(
                    new AchievementCategoryCrawlContext("Category",
                        requireVerifiedCategorySignature: true),
                    start,
                    CancellationToken.None);

                Assert.Equal(AchievementCategoryCrawlState.Incomplete, result.State);
                Assert.Equal(80, result.ProcessedFrames);
                Assert.Equal(79, result.ScrollActions);
                Assert.Contains("maximum captured-frame limit", result.Reason);
            }
            finally
            {
                foreach (Bitmap capture in captures) capture.Dispose();
            }
        }

        [Fact]
        public void CancellationBetweenFrames_PreservesObservedRows()
        {
            using Bitmap start = Image(16);
            using var cancellation = new CancellationTokenSource();
            var progress = new ImmediateProgress<AchievementCategoryCrawlProgress>(_ => cancellation.Cancel());
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)) }, Array.Empty<Bitmap>(), new[] { Evidence(top: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(
                Context(), start, cancellation.Token, progress);

            Assert.Equal(AchievementCategoryCrawlState.Cancelled, result.State);
            Assert.Single(result.IdentifiedRows);
            Assert.Equal(AchievementScrollProgressState.Cancelled, result.FinalScrollState);
        }

        [Fact]
        public void CancellationDuringSettling_StopsBeforeCapture()
        {
            using Bitmap start = Image(16);
            using var cancellation = new CancellationTokenSource();
            var capture = new SequenceCapture(Array.Empty<Bitmap>());
            var crawler = new AchievementCategoryCrawler(
                new SequenceFrameReader(new[] { Frame(Row(1)) }), capture,
                new RecordingScrollInput(),
                new SequenceEvidenceDetector(new[] { Evidence(top: true) }),
                new AchievementCategoryCrawlOptions(scrollSettleMilliseconds: 1,
                    maximumStabilizationCaptures: 1),
                (_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, cancellation.Token);

            Assert.Equal(AchievementCategoryCrawlState.Cancelled, result.State);
            Assert.Equal(0, capture.Captures);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ForegroundOrCaptureFailure_ReturnsStructuredFailure(bool scrollFailure)
        {
            using Bitmap start = Image(16);
            IAchievementListScrollInput scroll = scrollFailure
                ? new RecordingScrollInput(fail: true)
                : new RecordingScrollInput();
            IGameScreenCapture capture = scrollFailure
                ? new SequenceCapture(Array.Empty<Bitmap>())
                : new ThrowingCapture();
            var crawler = new AchievementCategoryCrawler(
                new SequenceFrameReader(new[] { Frame(Row(1)) }), capture, scroll,
                new SequenceEvidenceDetector(new[] { Evidence(top: true) }),
                new AchievementCategoryCrawlOptions(scrollSettleMilliseconds: 0,
                    maximumStabilizationCaptures: 1));

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Failed, result.State);
            Assert.Single(result.IdentifiedRows);
        }

        [Fact]
        public void WindowsScrollInput_RejectsLeftPanelTarget_AndUsesNormalizedRightPanelTarget()
        {
            var pointer = new RecordingPointer();
            var foreground = new ForegroundActivator { IsForegroundValue = true };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AchievementWindowsListScrollInput(pointer, foreground, targetX: 0.20));
            var input = new AchievementWindowsListScrollInput(pointer, foreground);

            var request = new AchievementListScrollRequest(-120, 3, 54);
            AchievementListScrollCommandResult result = input.Scroll(request, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal((AchievementWindowsListScrollInput.ScrollTargetX,
                AchievementWindowsListScrollInput.ScrollTargetY), pointer.Position);
            Assert.Equal(new[] { -120, -120, -120 }, pointer.WheelDeltas);
        }

        [Fact]
        public void WindowsScrollInput_DialogGatePreventsWheelInput()
        {
            var pointer = new RecordingPointer();
            var foreground = new ForegroundActivator { IsForegroundValue = true };
            var input = new AchievementWindowsListScrollInput(
                pointer, foreground, inputAllowed: () => false);

            AchievementListScrollCommandResult result = input.Scroll(
                new AchievementListScrollRequest(-120, 3, 54), CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Empty(pointer.WheelDeltas);
            Assert.Contains("dialog", result.FailureReason);
        }

        [Fact]
        public void ViewportFingerprint_ExcludesPointerTargetButDetectsListChange()
        {
            using Bitmap original = Image(32);
            using Bitmap cursorOnly = Image(32);
            using Bitmap changed = Image(96);
            int x = (int)Math.Round((cursorOnly.Width - 1) * AchievementWindowsListScrollInput.ScrollTargetX);
            int y = (int)Math.Round((cursorOnly.Height - 1) * AchievementWindowsListScrollInput.ScrollTargetY);
            using (Graphics graphics = Graphics.FromImage(cursorOnly))
                graphics.FillRectangle(Brushes.White, x - 20, y - 20, 40, 40);

            AchievementViewportFingerprint first = AchievementViewportFingerprint.Create(original);
            AchievementViewportFingerprint cursor = AchievementViewportFingerprint.Create(cursorOnly);
            AchievementViewportFingerprint different = AchievementViewportFingerprint.Create(changed);

            Assert.Equal(first, cursor);
            Assert.NotEqual(first, different);
        }

        [Fact]
        public void UncalibratedFallbackEvidence_NeverClaimsTopOrBottom()
        {
            using Bitmap screenshot = Image(32);
            AchievementCategoryFrameEvidence evidence =
                new UncalibratedAchievementCategoryFrameEvidenceDetector().Detect(screenshot);

            Assert.False(evidence.TopAtExtent);
            Assert.False(evidence.BottomAtExtent);
            Assert.False(evidence.CategorySignatureVerified);
        }

        [Fact]
        public void ContextualReader_SamplesInitialCheckpointAndBottomFramesOnly()
        {
            var reader = new RecordingContextFrameReader(new[]
            {
                FrameWithTotal(17, observed: true, observationFrame: 0, rowId: 1),
                FrameWithTotal(17, observed: false, observationFrame: 0, rowId: 2),
                FrameWithTotal(17, observed: true, observationFrame: 2, rowId: 3),
                FrameWithTotal(17, observed: true, observationFrame: 2, rowId: 3),
            });
            using Bitmap start = BandedImage(0);
            using Bitmap moved = BandedImage(-20);
            using Bitmap bottom = BandedImage(-40);
            using Bitmap stable = BandedImage(-40);
            var crawler = new AchievementCategoryCrawler(reader,
                new SequenceCapture(new[] { moved, bottom, stable }),
                new RecordingScrollInput(),
                new SequenceEvidenceDetector(new[]
                {
                    Evidence(top: true), Evidence(), Evidence(bottom: true), Evidence(bottom: true),
                }),
                new AchievementCategoryCrawlOptions(scrollSettleMilliseconds: 0,
                    maximumStabilizationCaptures: 1,
                    completedTotalCheckpointInterval: 5),
                (_, token) => token.ThrowIfCancellationRequested());

            AchievementCategoryCrawlResult result = crawler.Crawl(
                Context(), start, CancellationToken.None);

            Assert.Equal(new[] { true, false, true, true },
                reader.Contexts.Select(value => value.ObserveCompletedTotal));
            Assert.True(reader.Contexts[0].ForceFullScreenValidation);
            Assert.All(reader.Contexts.Skip(1), value =>
                Assert.False(value.ForceFullScreenValidation));
            Assert.Equal(3, result.CompletedTotalObservations.Count);
            Assert.Equal(17, result.LastObservedCompletedTotal);
            Assert.Equal(3, result.LastObservedCompletedTotalFrame);
            Assert.False(result.CompletedTotalInconsistent);
        }

        [Fact]
        public void CompletedTotalChanges_AreReportedWithoutChangingTraversalCompletion()
        {
            var reader = new RecordingContextFrameReader(new[]
            {
                FrameWithTotal(17, observed: true, observationFrame: 0),
                FrameWithTotal(18, observed: true, observationFrame: 1),
            });
            using Bitmap start = Image(16);
            using Bitmap stable = Image(16);
            var crawler = new AchievementCategoryCrawler(reader,
                new SequenceCapture(new[] { stable }), new RecordingScrollInput(),
                new SequenceEvidenceDetector(new[]
                {
                    Evidence(top: true, bottom: true), Evidence(bottom: true),
                }),
                new AchievementCategoryCrawlOptions(scrollSettleMilliseconds: 0,
                    maximumStabilizationCaptures: 1,
                    completedTotalCheckpointInterval: 1),
                (_, token) => token.ThrowIfCancellationRequested());

            AchievementCategoryCrawlResult result = crawler.Crawl(
                Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Complete, result.State);
            Assert.True(result.CompletedTotalInconsistent);
            Assert.Equal(18, result.LastObservedCompletedTotal);
            Assert.Contains("completedTotalInconsistent=True", result.Diagnostics);
        }

        private static AchievementCategoryCrawler Crawler(
            IEnumerable<AchievementBulkFrameResult> frames,
            IEnumerable<Bitmap> captures,
            IEnumerable<AchievementCategoryFrameEvidence> evidence,
            AchievementCategoryCrawlOptions options = null) => new(
                new SequenceFrameReader(frames),
                new SequenceCapture(captures),
                new RecordingScrollInput(),
                new SequenceEvidenceDetector(evidence),
                options ?? new AchievementCategoryCrawlOptions(scrollSettleMilliseconds: 0,
                    maximumStabilizationCaptures: 1),
                (_, token) => token.ThrowIfCancellationRequested());

        private static AchievementCategoryCrawlContext Context() =>
            new("Category", knownTraversalOrigin: true);

        private static AchievementCategoryCrawlResult ReconciliationCrawl(
            AchievementBulkFrameResult[] frames)
        {
            using Bitmap start = Image(16);
            Bitmap[] captures = Enumerable.Range(1, frames.Length - 1)
                .Select(index => Image(index >= frames.Length - 2 ? (byte)80 : (byte)(16 + index * 32)))
                .ToArray();
            try
            {
                AchievementCategoryFrameEvidence[] evidence = Enumerable.Range(0, frames.Length)
                    .Select(index => Evidence(top: index == 0,
                        bottom: index >= frames.Length - 2))
                    .ToArray();
                return Crawler(frames, captures, evidence).Crawl(
                    Context(), start, CancellationToken.None);
            }
            finally
            {
                foreach (Bitmap capture in captures) capture.Dispose();
            }
        }

        private static AchievementCategoryFrameEvidence Evidence(
            bool top = false, bool bottom = false, string signature = null) => new(
                top, bottom, signature ?? string.Empty, signature != null, "Synthetic evidence.");

        private static AchievementBulkFrameResult Frame(params AchievementVisibleRow[] rows) =>
            Frame(AchievementScreenState.Idle, rows);

        private static AchievementBulkFrameResult Frame(
            AchievementScreenState state, params AchievementVisibleRow[] rows) => new(
                state, rows, 17,
                new AchievementBulkFrameTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
                    TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, rows.Length, rows.Length),
                "Synthetic frame.");

        private static AchievementBulkFrameResult TimedFrame(params AchievementVisibleRow[] rows) => new(
            AchievementScreenState.Idle, rows, 17,
            new AchievementBulkFrameTiming(TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2),
                TimeSpan.FromMilliseconds(3), TimeSpan.FromMilliseconds(4),
                TimeSpan.FromMilliseconds(15), rows.Length, rows.Length,
                screenDetection: TimeSpan.FromMilliseconds(0.25),
                completedTotal: TimeSpan.FromMilliseconds(0.25),
                ocrCropResize: TimeSpan.FromMilliseconds(0.5),
                ocrPreprocessing: TimeSpan.FromMilliseconds(1),
                ocrEngine: TimeSpan.FromMilliseconds(2)),
            "Synthetic timed frame.");

        private static AchievementBulkFrameResult FrameWithTotal(int value,
            bool observed, int observationFrame, int rowId = 1)
        {
            AchievementVisibleRow row = Row(rowId);
            var evidence = new AchievementCompletedTotalFrameEvidence(value,
                observed, observed, !observed, observationFrame,
                observed ? AchievementCompletedTotalEvidenceSource.DedicatedOcr :
                    AchievementCompletedTotalEvidenceSource.ContextReuse,
                "Synthetic completed-total evidence.");
            return new AchievementBulkFrameResult(AchievementScreenState.Idle,
                new[] { row }, value,
                new AchievementBulkFrameTiming(TimeSpan.Zero, TimeSpan.Zero,
                    TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
                    1, 1, completedTotalOcrExecutions: observed ? 1 : 0,
                    completedTotalReuseCount: observed ? 0 : 1),
                "Synthetic contextual frame.", completedTotalEvidence: evidence,
                screenValidationPath: observationFrame == 0
                    ? AchievementBulkScreenValidationPath.Full
                    : AchievementBulkScreenValidationPath.Fast);
        }

        private static AchievementVisibleRow Row(
            int id,
            AchievementVisibleRowVisibility visibility = AchievementVisibleRowVisibility.Complete,
            AchievementCompletionState completion = AchievementCompletionState.CompletedClaimed,
            IEnumerable<int> candidateIds = null,
            AchievementIdentityState identity = AchievementIdentityState.Identified,
            int? stageGroupId = null,
            bool canonical = true,
            string fingerprint = null,
            double? normalizedY = null)
        {
            int[] ids = (candidateIds ?? new[] { id }).ToArray();
            return new AchievementVisibleRow(
                id, new NormalizedRectangle(0.38,
                    normalizedY ?? Math.Min(0.8, id / 100.0), 0.56, 0.1),
                visibility, "Title " + id, "title " + id, identity, ids, stageGroupId,
                completion, completion == AchievementCompletionState.Incomplete ? 0 : null,
                completion == AchievementCompletionState.Incomplete ? 1 : null,
                null, 100, 0, 0.99f, false,
                new AchievementRowFingerprint(fingerprint ??
                    (canonical ? "canonical:" + string.Join(",", ids) : "diagnostic:" + id), canonical),
                null, null, null, "Synthetic row.");
        }

        private static AchievementVisibleRow Unresolved(string value,
            double normalizedY = 0.2,
            AchievementVisibleRowVisibility visibility = AchievementVisibleRowVisibility.Uncertain) => new(
            0, new NormalizedRectangle(0.38, normalizedY, 0.56, 0.1),
            visibility, value, value,
            AchievementIdentityState.Unresolved, Array.Empty<int>(), null,
            AchievementCompletionState.Unresolved, null, null, null, 0, 0, 0,
            false, new AchievementRowFingerprint("diagnostic:" + value, false),
            null, null, null, "Synthetic unresolved row.");

        private static Bitmap Image(byte value)
        {
            var bitmap = new Bitmap(1919, 1079);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.FromArgb(value, value, value));
            return bitmap;
        }

        private static Bitmap BandedImage(int verticalOffset)
        {
            var bitmap = new Bitmap(1919, 1079);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Black);
            Rectangle viewport = AchievementListRegions.For(bitmap.Size).RowViewport;
            for (int y = viewport.Top - 200; y < viewport.Bottom + 200; y += 80)
            {
                using var brush = new SolidBrush(((y / 80) & 1) == 0
                    ? Color.FromArgb(220, 220, 220)
                    : Color.FromArgb(90, 90, 90));
                graphics.FillRectangle(brush, viewport.Left, y + verticalOffset,
                    viewport.Width, 24);
            }
            return bitmap;
        }

        private static Bitmap SmallImage(byte value)
        {
            var bitmap = new Bitmap(320, 180);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.FromArgb(value, value, value));
            return bitmap;
        }

        private sealed class SequenceFrameReader : IAchievementListFrameReader
        {
            private readonly Queue<AchievementBulkFrameResult> frames;
            internal SequenceFrameReader(IEnumerable<AchievementBulkFrameResult> frames) =>
                this.frames = new Queue<AchievementBulkFrameResult>(frames);
            public AchievementBulkFrameResult Read(Bitmap screenshot, TimeSpan? captureDuration = null) => frames.Dequeue();
        }

        private sealed class RecordingContextFrameReader : IContextualAchievementListFrameReader
        {
            private readonly Queue<AchievementBulkFrameResult> frames;
            internal RecordingContextFrameReader(IEnumerable<AchievementBulkFrameResult> frames) =>
                this.frames = new Queue<AchievementBulkFrameResult>(frames);
            internal List<AchievementBulkFrameReadContext> Contexts { get; } = new();
            public AchievementBulkFrameResult Read(Bitmap screenshot,
                TimeSpan? captureDuration = null) => frames.Dequeue();
            public AchievementBulkFrameResult Read(Bitmap screenshot,
                AchievementBulkFrameReadContext context,
                TimeSpan? captureDuration = null)
            {
                Contexts.Add(context);
                return frames.Dequeue();
            }
        }

        private sealed class SequenceCapture : IGameScreenCapture
        {
            private readonly Queue<Bitmap> frames;
            internal SequenceCapture(IEnumerable<Bitmap> frames) =>
                this.frames = new Queue<Bitmap>(frames.Select(frame => new Bitmap(frame)));
            internal int Captures { get; private set; }
            public Bitmap CaptureWindow() { Captures++; return frames.Dequeue(); }
        }

        private sealed class ThrowingCapture : IGameScreenCapture
        {
            public Bitmap CaptureWindow() => throw new InvalidOperationException("Capture failed.");
        }

        private sealed class SequenceEvidenceDetector : IAchievementCategoryFrameEvidenceDetector
        {
            private readonly Queue<AchievementCategoryFrameEvidence> evidence;
            internal SequenceEvidenceDetector(IEnumerable<AchievementCategoryFrameEvidence> evidence) =>
                this.evidence = new Queue<AchievementCategoryFrameEvidence>(evidence);
            public AchievementCategoryFrameEvidence Detect(Bitmap screenshot) => evidence.Dequeue();
        }

        private sealed class RecordingScrollInput : IAchievementListScrollInput
        {
            private readonly bool fail;
            internal RecordingScrollInput(bool fail = false) => this.fail = fail;
            public AchievementListScrollCommandResult Scroll(
                AchievementListScrollRequest request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new AchievementListScrollCommandResult(!fail, !fail,
                    fail ? "Foreground verification failed." : null, "Synthetic scroll.");
            }
        }

        private sealed class RecordingPointer : IAchievementPointerInput
        {
            internal (double X, double Y) Position { get; private set; }
            internal List<int> WheelDeltas { get; } = new();
            public void MoveNormalized(double x, double y, CancellationToken cancellationToken) => Position = (x, y);
            public bool IsAtNormalized(double x, double y) => Position == (x, y);
            public void Click(CancellationToken cancellationToken) { }
            public void ScrollWheel(int delta, CancellationToken cancellationToken) => WheelDeltas.Add(delta);
        }

        private sealed class ForegroundActivator : IGameForegroundActivator
        {
            internal bool IsForegroundValue { get; set; }
            public bool IsForeground => IsForegroundValue;
            public void Activate() => IsForegroundValue = true;
        }

        private sealed class ImmediateProgress<T> : IProgress<T>
        {
            private readonly Action<T> report;
            internal ImmediateProgress(Action<T> report) => this.report = report;
            public void Report(T value) => report(value);
        }
    }
}

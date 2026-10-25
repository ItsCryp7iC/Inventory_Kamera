using InventoryKamera.game;
using InventoryKamera.ui;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class AchievementBulkDiagnosticTests
    {
        [Fact]
        public void DiagnosticControl_UsesMainFormParentAndFitsNormalScanColumn()
        {
            using DiagnosticLayout layout = DiagnosticLayout.Create(scale: 1.0);

            Assert.Same(layout.Parent, layout.Diagnostic.Parent);
            Assert.True(layout.Parent.Controls.Contains(layout.Diagnostic));
            Assert.Equal(layout.Start.Left, layout.Diagnostic.Left);
            Assert.Equal(layout.Start.Width, layout.Diagnostic.Width);
            Assert.True(layout.Diagnostic.Top >= layout.Start.Bottom);
            Assert.True(layout.Parent.ClientRectangle.Contains(layout.Diagnostic.Bounds));
            Assert.DoesNotContain(layout.FollowingControls,
                control => control.Bounds.IntersectsWith(layout.Diagnostic.Bounds));
            Assert.True(layout.NavigationImage.Bottom < layout.ScannerOutputTop);
            Assert.False(layout.ProgramStatus.AutoSize);
            Assert.True(layout.ProgramStatus.AutoEllipsis);
            Assert.Equal(layout.Start.Width, layout.ProgramStatus.Width);
        }

        [Fact]
        public void DiagnosticControl_LayoutScalesWithDesignerCreatedScanButton()
        {
            using DiagnosticLayout normal = DiagnosticLayout.Create(scale: 1.0);
            using DiagnosticLayout scaled = DiagnosticLayout.Create(scale: 1.5);

            Assert.Equal(29, normal.Diagnostic.Height);
            Assert.Equal(44, scaled.Diagnostic.Height);
            Assert.Equal(183, normal.Diagnostic.Width);
            Assert.Equal(scaled.Start.Width, scaled.Diagnostic.Width);
            Assert.True(scaled.NavigationImage.Bottom < scaled.ScannerOutputTop);
            Assert.DoesNotContain(scaled.FollowingControls,
                control => control.Bounds.IntersectsWith(scaled.Diagnostic.Bounds));
        }

        [Fact]
        public void DiagnosticControl_VisibilityFollowsParentAndAchievementMode()
        {
            using DiagnosticLayout layout = DiagnosticLayout.Create(scale: 1.0);

            layout.Binding.ApplyState(
                achievementModeSelected: true, activeRun: false);
            Assert.True(layout.Parent.Visible);
            Assert.True(layout.Diagnostic.Visible);

            layout.Parent.Visible = false;
            Assert.False(layout.Diagnostic.Visible);
            layout.Parent.Visible = true;
            layout.Binding.ApplyState(
                achievementModeSelected: false, activeRun: false);
            Assert.False(layout.Diagnostic.Visible);
        }

        [Fact]
        public void LegacyMode_RestoresOriginalScanStatusLayout()
        {
            using DiagnosticLayout layout = DiagnosticLayout.Create(scale: 1.0);

            layout.Binding.ApplyState(
                achievementModeSelected: false, activeRun: false);

            Assert.False(layout.Diagnostic.Visible);
            Assert.Equal(new[] { 143, 179, 236, 268, 307 },
                layout.FollowingControls.Select(control => control.Top));
            Assert.True(layout.ProgramStatus.AutoSize);
            Assert.False(layout.ProgramStatus.AutoEllipsis);
        }

        [Fact]
        public void DiagnosticControl_IsClickableOnlyInIdleAchievementMode()
        {
            int clicks = 0;
            using DiagnosticLayout layout = DiagnosticLayout.Create(
                scale: 1.0, onClick: (_, _) => clicks++);

            layout.Binding.ApplyState(
                achievementModeSelected: true, activeRun: false);
            layout.Diagnostic.PerformClick();
            Assert.Equal(1, clicks);

            layout.Binding.ApplyState(
                achievementModeSelected: true, activeRun: true);
            Assert.True(layout.Diagnostic.Visible);
            Assert.False(layout.Diagnostic.Enabled);
            layout.Diagnostic.PerformClick();
            Assert.Equal(1, clicks);

            layout.Binding.ApplyState(
                achievementModeSelected: false, activeRun: false);
            Assert.False(layout.Diagnostic.Visible);
            Assert.False(layout.Diagnostic.Enabled);
        }

        [Fact]
        public void DiagnosticControl_IsAvailableOnlyInAchievementMode()
        {
            Assert.True(AchievementBulkDiagnosticUiPolicy.IsControlVisible(true));
            Assert.True(AchievementBulkDiagnosticUiPolicy.IsControlEnabled(true, activeRun: false));
        }

        [Fact]
        public void DiagnosticControl_DoesNotInterfereWithLegacyMode()
        {
            Assert.False(AchievementBulkDiagnosticUiPolicy.IsControlVisible(false));
            Assert.False(AchievementBulkDiagnosticUiPolicy.IsControlEnabled(false, activeRun: false));
        }

        [Fact]
        public void DiagnosticStatusText_IsConciseAndBoundedByScanColumn()
        {
            using var font = new Font("Segoe UI", 12F);
            string[] statuses =
            {
                AchievementBulkDiagnosticUiPolicy.RunningStatus,
                AchievementBulkDiagnosticUiPolicy.CancelledStatus,
                AchievementBulkDiagnosticUiPolicy.FinishedStatus,
                AchievementBulkDiagnosticUiPolicy.AbortedStatus,
            };

            Assert.All(statuses, status =>
                Assert.True(TextRenderer.MeasureText(status, font).Width <= 183, status));
        }

        [Fact]
        public void ConstructingRunner_DoesNotCaptureOrStartCrawler()
        {
            var capture = new FakeCapture();
            var crawler = new FakeCrawler(Result(AchievementCategoryCrawlState.Incomplete));

            _ = new AchievementBulkDiagnosticRunner(capture, crawler);

            Assert.Equal(0, capture.CaptureCount);
            Assert.Equal(0, crawler.CallCount);
        }

        [Fact]
        public async Task Runner_AllowsOnlyOneActiveDiagnostic()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var crawler = new FakeCrawler(Result(AchievementCategoryCrawlState.Incomplete), () =>
            {
                entered.Set();
                release.Wait();
            });
            var runner = new AchievementBulkDiagnosticRunner(new FakeCapture(), crawler);
            Task<AchievementBulkDiagnosticResult> first = Task.Run(() => runner.Run(CancellationToken.None));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

            AchievementBulkDiagnosticResult second = runner.Run(CancellationToken.None);
            release.Set();
            await first;

            Assert.Equal(AchievementCategoryCrawlState.Failed, second.State);
            Assert.Contains("already active", second.Reason);
            Assert.Equal(1, crawler.CallCount);
        }

        [Fact]
        public void ProductionFactory_WiresDiagnosticToStandaloneRuntime()
        {
            using var ocr = new OcrService(engineCount: 1);
            var runtime = new AchievementScanRuntimeContext(
                ocr, new ImageProcessor(), new FakeCapture(), new ForegroundActivator(),
                CancellationToken.None);

            AchievementBulkDiagnosticRunner runner =
                AchievementScannerProductionFactory.CreateBulkDiagnostic(
                    new AchievementCatalogProvider(), runtime);

            Assert.NotNull(runner);
            Assert.False(runner.IsActive);
        }

        [Fact]
        public void Defaults_AreShortBoundedAndUseCalibratedBatch()
        {
            AchievementBulkDiagnosticOptions options = AchievementBulkDiagnosticOptions.Default;
            AchievementCategoryCrawlOptions crawler = options.ToCrawlerOptions();

            Assert.Equal(5, options.MaximumCapturedFrames);
            Assert.Equal(4, options.MaximumScrollBatches);
            Assert.Equal(21, options.WheelNotchesPerBatch);
            Assert.Equal(5, crawler.MaximumCapturedFrames);
            Assert.Equal(4, crawler.MaximumScrollActions);
            Assert.Equal(21, crawler.WheelNotchesPerBatch);
            Assert.Equal(AchievementBulkDiagnosticMode.Short, options.Mode);
            Assert.Same(AchievementBulkDiagnosticOptions.ShortProfile, options);
        }

        [Fact]
        public void FullTraversalProfile_IsSeparateNamedAndBounded()
        {
            AchievementBulkDiagnosticOptions options =
                AchievementBulkDiagnosticOptions.FullTraversalProfile;
            AchievementCategoryCrawlOptions crawler = options.ToCrawlerOptions();

            Assert.Equal(AchievementBulkDiagnosticMode.FullTraversal, options.Mode);
            Assert.Equal(80, options.MaximumCapturedFrames);
            Assert.Equal(79, options.MaximumScrollBatches);
            Assert.Equal(21, options.WheelNotchesPerBatch);
            Assert.Equal(80, crawler.MaximumCapturedFrames);
            Assert.Equal(79, crawler.MaximumScrollActions);
            Assert.Equal(21, crawler.WheelNotchesPerBatch);
        }

        [Fact]
        public void DiagnosticProfiles_RejectUnboundedOrInconsistentLimits()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AchievementBulkDiagnosticOptions(maximumCapturedFrames: 81));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AchievementBulkDiagnosticOptions(maximumCapturedFrames: 5,
                    maximumScrollBatches: 5));
        }

        [Fact]
        public void DiagnosticDialog_OffersExplicitFullTraversalChoiceAndSafetyText()
        {
            using var dialog = new AchievementBulkDiagnosticStartForm();

            Assert.Equal(AchievementBulkDiagnosticMode.Short, dialog.SelectedMode);
            Assert.Contains("5 frames", dialog.ModeDescription);
            dialog.SelectMode(AchievementBulkDiagnosticMode.FullTraversal);

            Assert.Equal(AchievementBulkDiagnosticMode.FullTraversal, dialog.SelectedMode);
            Assert.Contains("real bottom", dialog.ModeDescription);
            Assert.Contains("longer", dialog.ModeDescription);
            Assert.Contains("F8", dialog.ModeDescription);
            Assert.Contains("No progress is saved or exported", dialog.ModeDescription);
            Assert.Contains(dialog.Controls.OfType<GroupBox>().SelectMany(group =>
                group.Controls.OfType<RadioButton>()), radio =>
                radio.Text == "Verify Full Category Traversal");
        }

        [Fact]
        public void RunnerAndReport_PreserveSelectedFullTraversalMode()
        {
            var runner = new AchievementBulkDiagnosticRunner(
                new FakeCapture(),
                new FakeCrawler(Result(AchievementCategoryCrawlState.Incomplete)),
                AchievementBulkDiagnosticMode.FullTraversal);

            AchievementBulkDiagnosticResult result = runner.Run(CancellationToken.None);
            string report = AchievementBulkDiagnosticReportBuilder.BuildSummary(result);

            Assert.Equal(AchievementBulkDiagnosticMode.FullTraversal, result.Mode);
            Assert.Contains("Diagnostic mode: Verify Full Category Traversal", report);
        }

        [Fact]
        public void SuccessfulBoundedTraversal_ReturnsCrawlerEvidence()
        {
            AchievementCategoryCrawlResult expected = Result(
                AchievementCategoryCrawlState.Incomplete, reason: "bounded traversal finished");
            var crawler = new FakeCrawler(expected);
            var runner = new AchievementBulkDiagnosticRunner(
                new FakeCapture(), crawler);

            AchievementBulkDiagnosticResult actual = runner.Run(CancellationToken.None);

            Assert.Same(expected, actual.CrawlResult);
            Assert.Equal("memories of the heart", actual.CategorySignature);
            Assert.True(crawler.LastContext.RequireVerifiedCategorySignature);
            Assert.False(crawler.LastContext.KnownTraversalOrigin);
        }

        [Fact]
        public void CancelledWheelBatch_RecordsActualEventsAlreadySent()
        {
            using var cancellation = new CancellationTokenSource();
            var pointer = new CancellingPointer(cancellation, cancelAfter: 2);
            var input = new AchievementWindowsListScrollInput(pointer, new ForegroundActivator());

            OperationCanceledException error = Assert.Throws<OperationCanceledException>(() =>
                input.Scroll(AchievementCategoryCrawlOptions.Default.ScrollRequest, cancellation.Token));

            Assert.Equal(2, error.Data["AchievementWheelEventsSent"]);
            Assert.Equal(2, pointer.EventsSent);
        }

        [Fact]
        public void DiagnosticLimit_RemainsIncomplete()
        {
            var runner = Runner(Result(AchievementCategoryCrawlState.Incomplete,
                reason: "The maximum captured-frame limit was reached without positive bottom proof."));

            AchievementBulkDiagnosticResult result = runner.Run(CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Incomplete, result.State);
            Assert.False(result.HasBottomProof);
            Assert.Contains("limit", result.Reason);
        }

        [Fact]
        public void PositiveBottomEvidence_IsReportedAsComplete()
        {
            var runner = Runner(Result(AchievementCategoryCrawlState.Complete,
                completeness: "bottom extent plus stable viewport and final row"));

            AchievementBulkDiagnosticResult result = runner.Run(CancellationToken.None);

            Assert.True(result.HasBottomProof);
            Assert.Contains("bottom extent", AchievementBulkDiagnosticReportBuilder.BuildSummary(result));
        }

        [Fact]
        public void UnknownStartingPosition_StopsIncompleteWithoutScroll()
        {
            var runner = Runner(Result(AchievementCategoryCrawlState.Incomplete,
                scrollActions: 0, reason: "top position unsupported"));

            AchievementBulkDiagnosticResult result = runner.Run(CancellationToken.None);

            Assert.Equal(0, result.CrawlResult.ScrollActions);
            Assert.Contains("top", result.Reason);
        }

        [Fact]
        public void WrongForeground_IsPreservedAsFailedCrawlerResult()
        {
            var runner = Runner(Result(AchievementCategoryCrawlState.Failed,
                reason: "Genshin was not the verified foreground target"));

            AchievementBulkDiagnosticResult result = runner.Run(CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Failed, result.State);
            Assert.Contains("foreground", result.Reason);
        }

        [Fact]
        public void UnexpectedCategoryChange_IsPreservedAsFailure()
        {
            var runner = Runner(Result(AchievementCategoryCrawlState.Failed,
                reason: "category changed unexpectedly"));

            Assert.Contains("category changed", runner.Run(CancellationToken.None).Reason);
        }

        [Fact]
        public void CaptureFailure_ReturnsStructuredFailureWithoutCallingCrawler()
        {
            var crawler = new FakeCrawler(Result(AchievementCategoryCrawlState.Complete));
            var runner = new AchievementBulkDiagnosticRunner(
                new FakeCapture(new InvalidOperationException("capture unavailable")), crawler);

            AchievementBulkDiagnosticResult result = runner.Run(CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Failed, result.State);
            Assert.Contains("capture unavailable", result.Reason);
            Assert.Equal(0, crawler.CallCount);
        }

        [Fact]
        public void CancellationBeforeStarting_DoesNotCaptureOrCallCrawler()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var capture = new FakeCapture();
            var crawler = new FakeCrawler(Result(AchievementCategoryCrawlState.Complete));
            var runner = new AchievementBulkDiagnosticRunner(capture, crawler);

            AchievementBulkDiagnosticResult result = runner.Run(cancellation.Token);

            Assert.Equal(AchievementCategoryCrawlState.Cancelled, result.State);
            Assert.Equal(0, capture.CaptureCount);
            Assert.Equal(0, crawler.CallCount);
        }

        [Fact]
        public void CancellationDuringSettling_PreservesCrawlerResult()
        {
            AchievementCategoryCrawlResult crawl = Result(AchievementCategoryCrawlState.Cancelled,
                processedFrames: 1, reason: "cancelled during settling");

            AchievementBulkDiagnosticResult result = Runner(crawl).Run(CancellationToken.None);

            Assert.Same(crawl, result.CrawlResult);
            Assert.Equal(1, result.CrawlResult.ProcessedFrames);
        }

        [Fact]
        public void CancellationBetweenBatches_PreservesCrawlerResult()
        {
            AchievementCategoryCrawlResult crawl = Result(AchievementCategoryCrawlState.Cancelled,
                processedFrames: 2, scrollActions: 1, reason: "cancelled between batches");

            AchievementBulkDiagnosticResult result = Runner(crawl).Run(CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Cancelled, result.State);
            Assert.Equal(1, result.CrawlResult.ScrollActions);
        }

        [Fact]
        public void DiagnosticRun_DoesNotMutateSavedAchievementState()
        {
            var provider = new AchievementCatalogProvider();
            var state = new AchievementResumeDataState(new AchievementToolkitImporter(provider));
            int before = state.CompletedCount;

            _ = Runner(Result(AchievementCategoryCrawlState.Incomplete)).Run(CancellationToken.None);

            Assert.Equal(before, state.CompletedCount);
            Assert.False(state.HasResumeSeed);
        }

        [Fact]
        public void RunnerDependencySurface_HasNoExportOrRewardServices()
        {
            string[] dependencyTypes = typeof(AchievementBulkDiagnosticRunner)
                .GetFields(System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                .Select(field => field.FieldType.Name)
                .ToArray();

            Assert.DoesNotContain(dependencyTypes, name => name.Contains("Export"));
            Assert.DoesNotContain(dependencyTypes, name => name.Contains("Reward"));
            Assert.DoesNotContain(dependencyTypes, name => name.Contains("Resume"));
        }

        [Fact]
        public void Report_DistinguishesRequestedInputFromObservedMovement()
        {
            string report = AchievementBulkDiagnosticReportBuilder.BuildSummary(
                new AchievementBulkDiagnosticResult(Result(
                    AchievementCategoryCrawlState.Incomplete,
                    scrollActions: 1,
                    scrolls: new[] { ScrollObservation() })));

            Assert.Contains("Requested input: 21 notches, logical delta -2520", report);
            Assert.Contains("Wheel events confirmed sent: 21", report);
            Assert.Contains("Observed visual displacement by batch: not measured", report);
            Assert.DoesNotContain("378px", report);
        }

        [Fact]
        public void Report_SeparatesTraversalCompletionFromRecognitionCoverage()
        {
            AchievementCategoryCrawlResult crawl = Result(
                AchievementCategoryCrawlState.Complete,
                processedFrames: 2,
                completeness: "verified stationary bottom");
            string report = AchievementBulkDiagnosticReportBuilder.BuildSummary(
                new AchievementBulkDiagnosticResult(crawl,
                    AchievementBulkDiagnosticMode.FullTraversal));

            Assert.Contains("Traversal complete: yes", report);
            Assert.Contains("Recognition coverage sufficient: yes", report);
            Assert.Contains("Positive top evidence: True", report);
            Assert.Contains("Positive bottom-position evidence: True", report);
        }

        [Theory]
        [InlineData((int)AchievementCategoryCrawlState.Complete)]
        [InlineData((int)AchievementCategoryCrawlState.Incomplete)]
        [InlineData((int)AchievementCategoryCrawlState.Cancelled)]
        [InlineData((int)AchievementCategoryCrawlState.Failed)]
        public void Runner_RestoresInactiveStateAfterEveryTerminalOutcome(
            int stateValue)
        {
            var state = (AchievementCategoryCrawlState)stateValue;
            var runner = Runner(Result(state));

            _ = runner.Run(CancellationToken.None);

            Assert.False(runner.IsActive);
            Assert.True(AchievementBulkDiagnosticUiPolicy.IsControlEnabled(true, runner.IsActive));
        }

        private static AchievementBulkDiagnosticRunner Runner(AchievementCategoryCrawlResult result) =>
            new(new FakeCapture(), new FakeCrawler(result));

        private static AchievementCategoryCrawlResult Result(
            AchievementCategoryCrawlState state,
            int processedFrames = 1,
            int scrollActions = 0,
            string reason = "test",
            string completeness = "",
            IEnumerable<AchievementScrollObservation> scrolls = null)
        {
            var evidence = new AchievementCategoryFrameEvidence(
                topAtExtent: true,
                bottomAtExtent: state == AchievementCategoryCrawlState.Complete,
                categorySignature: "memories of the heart",
                categorySignatureVerified: true,
                diagnostics: "scrollbarThumb=test");
            var frames = Enumerable.Range(0, Math.Max(0, processedFrames))
                .Select(index => new AchievementCategoryFrameObservation(
                    index, AchievementScreenState.Idle, 0, evidence));
            return new AchievementCategoryCrawlResult(
                new AchievementCategoryCrawlContext("Manually selected category"),
                state,
                completeness,
                processedFrames,
                scrollActions,
                Array.Empty<AchievementObservedRow>(),
                scrolls ?? Array.Empty<AchievementScrollObservation>(),
                frames,
                state == AchievementCategoryCrawlState.Cancelled
                    ? AchievementScrollProgressState.Cancelled
                    : state == AchievementCategoryCrawlState.Complete
                        ? AchievementScrollProgressState.ReachedBottom
                        : AchievementScrollProgressState.NoMovementUnknown,
                reason,
                new AchievementCategoryCrawlTiming(
                    TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
                    TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(10)),
                "diagnostic=test");
        }

        private static AchievementScrollObservation ScrollObservation()
        {
            var request = new AchievementListScrollRequest(-120, 21, 378);
            var command = new AchievementListScrollCommandResult(
                succeeded: true,
                foregroundVerified: true,
                failureReason: null,
                diagnostics: "test",
                wheelEventsSent: 21);
            return new AchievementScrollObservation(1, request, null, command);
        }

        private sealed class FakeCapture : IGameScreenCapture
        {
            private readonly Exception failure;

            internal FakeCapture(Exception failure = null)
            {
                this.failure = failure;
            }

            internal int CaptureCount { get; private set; }

            public Bitmap CaptureWindow()
            {
                CaptureCount++;
                if (failure != null) throw failure;
                return new Bitmap(32, 32);
            }
        }

        private sealed class FakeCrawler : IAchievementCategoryCrawler
        {
            private readonly AchievementCategoryCrawlResult result;
            private readonly Action beforeReturn;

            internal FakeCrawler(AchievementCategoryCrawlResult result, Action beforeReturn = null)
            {
                this.result = result;
                this.beforeReturn = beforeReturn;
            }

            internal int CallCount { get; private set; }
            internal AchievementCategoryCrawlContext LastContext { get; private set; }

            public AchievementCategoryCrawlResult Crawl(
                AchievementCategoryCrawlContext context,
                Bitmap startingFrame,
                CancellationToken cancellationToken,
                IProgress<AchievementCategoryCrawlProgress> progress = null,
                TimeSpan? startingCaptureDuration = null)
            {
                CallCount++;
                LastContext = context;
                beforeReturn?.Invoke();
                return result;
            }
        }

        private sealed class CancellingPointer : IAchievementPointerInput
        {
            private readonly CancellationTokenSource cancellation;
            private readonly int cancelAfter;

            internal CancellingPointer(CancellationTokenSource cancellation, int cancelAfter)
            {
                this.cancellation = cancellation;
                this.cancelAfter = cancelAfter;
            }

            internal int EventsSent { get; private set; }
            public void MoveNormalized(double x, double y, CancellationToken cancellationToken) { }
            public bool IsAtNormalized(double x, double y) => true;
            public void Click(CancellationToken cancellationToken) { }
            public void ScrollWheel(int delta, CancellationToken cancellationToken)
            {
                EventsSent++;
                if (EventsSent == cancelAfter) cancellation.Cancel();
            }
        }

        private sealed class ForegroundActivator : IGameForegroundActivator
        {
            public bool IsForeground => true;
            public void Activate() { }
        }

        private sealed class DiagnosticLayout : IDisposable
        {
            private DiagnosticLayout(Panel parent, Button start,
                AchievementBulkDiagnosticControl binding,
                Control[] followingControls, PictureBox navigationImage, int scannerOutputTop)
            {
                Parent = parent;
                Start = start;
                Binding = binding;
                FollowingControls = followingControls;
                NavigationImage = navigationImage;
                ScannerOutputTop = scannerOutputTop;
            }

            internal Panel Parent { get; }
            internal Button Start { get; }
            internal AchievementBulkDiagnosticControl Binding { get; }
            internal Button Diagnostic => Binding.Button;
            internal Control[] FollowingControls { get; }
            internal PictureBox NavigationImage { get; }
            internal Label ProgramStatus => (Label)FollowingControls[1];
            internal int ScannerOutputTop { get; }

            internal static DiagnosticLayout Create(double scale, EventHandler onClick = null)
            {
                int S(int value) => (int)Math.Round(value * scale);
                var parent = new Panel { Size = new Size(S(1215), S(1065)), Visible = true };
                var start = new Button
                {
                    Location = new Point(S(771), S(68)),
                    Size = new Size(S(183), S(68)),
                };
                var cancellation = new Label
                    { Location = new Point(S(771), S(143)), Size = new Size(S(170), S(21)) };
                var status = new Label
                {
                    AutoSize = true,
                    Location = new Point(S(771), S(179)),
                    Size = new Size(S(183), S(32)),
                    Text = "Scanning Status",
                };
                var eta = new Label
                    { Location = new Point(S(771), S(236)), Size = new Size(S(64), S(25)) };
                var navigationLabel = new Label
                    { Location = new Point(S(771), S(268)), Size = new Size(S(102), S(25)) };
                var navigationImage = new PictureBox
                    { Location = new Point(S(771), S(307)), Size = new Size(S(183), S(57)) };
                Control[] following =
                    { cancellation, status, eta, navigationLabel, navigationImage };
                parent.Controls.Add(start);
                parent.Controls.AddRange(following);

                AchievementBulkDiagnosticControl binding = AchievementBulkDiagnosticControl.Create(
                    parent, start, cancellation, status, eta, navigationLabel,
                    navigationImage, onClick);
                binding.ApplyState(achievementModeSelected: true, activeRun: false);
                return new DiagnosticLayout(parent, start, binding, following,
                    navigationImage, S(415));
            }

            public void Dispose() => Parent.Dispose();
        }
    }
}

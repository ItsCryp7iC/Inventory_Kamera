using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace InventoryKamera
{
    internal enum AchievementCategoryCrawlState { Complete, Incomplete, Cancelled, Failed }
    internal enum AchievementScrollProgressState
    {
        ScrollAdvanced,
        ReachedBottom,
        NoMovementUnknown,
        LostScreen,
        Cancelled,
    }

    internal sealed class AchievementCategoryCrawlContext
    {
        internal AchievementCategoryCrawlContext(
            string categoryName,
            string expectedCategorySignature = null,
            bool knownTraversalOrigin = false)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
                throw new ArgumentException("A category name is required.", nameof(categoryName));
            CategoryName = categoryName.Trim();
            ExpectedCategorySignature = expectedCategorySignature?.Trim() ?? string.Empty;
            KnownTraversalOrigin = knownTraversalOrigin;
        }

        internal string CategoryName { get; }
        internal string ExpectedCategorySignature { get; }
        internal bool KnownTraversalOrigin { get; }
    }

    internal sealed class AchievementCategoryCrawlOptions
    {
        internal static AchievementCategoryCrawlOptions Default { get; } = new();

        internal AchievementCategoryCrawlOptions(
            int maximumCapturedFrames = 100,
            int maximumScrollActions = 100,
            int maximumConsecutiveUncertainFrames = 3,
            int maximumNoMovementRecoveryAttempts = 1,
            int scrollSettleMilliseconds = 350,
            int wheelDelta = -360)
        {
            MaximumCapturedFrames = Positive(maximumCapturedFrames, nameof(maximumCapturedFrames));
            MaximumScrollActions = Positive(maximumScrollActions, nameof(maximumScrollActions));
            MaximumConsecutiveUncertainFrames = NonNegative(
                maximumConsecutiveUncertainFrames, nameof(maximumConsecutiveUncertainFrames));
            MaximumNoMovementRecoveryAttempts = NonNegative(
                maximumNoMovementRecoveryAttempts, nameof(maximumNoMovementRecoveryAttempts));
            ScrollSettleMilliseconds = NonNegative(
                scrollSettleMilliseconds, nameof(scrollSettleMilliseconds));
            if (wheelDelta > -120 || wheelDelta < -1200 || wheelDelta % 120 != 0)
                throw new ArgumentOutOfRangeException(nameof(wheelDelta),
                    "The downward wheel delta must be a bounded multiple of 120.");
            WheelDelta = wheelDelta;
        }

        internal int MaximumCapturedFrames { get; }
        internal int MaximumScrollActions { get; }
        internal int MaximumConsecutiveUncertainFrames { get; }
        internal int MaximumNoMovementRecoveryAttempts { get; }
        internal int ScrollSettleMilliseconds { get; }
        internal int WheelDelta { get; }

        private static int Positive(int value, string name) =>
            value > 0 ? value : throw new ArgumentOutOfRangeException(name);
        private static int NonNegative(int value, string name) =>
            value >= 0 ? value : throw new ArgumentOutOfRangeException(name);
    }

    internal sealed class AchievementCategoryFrameEvidence
    {
        internal AchievementCategoryFrameEvidence(
            bool topAtExtent,
            bool bottomAtExtent,
            string categorySignature,
            bool categorySignatureVerified,
            string diagnostics)
        {
            TopAtExtent = topAtExtent;
            BottomAtExtent = bottomAtExtent;
            CategorySignature = categorySignature ?? string.Empty;
            CategorySignatureVerified = categorySignatureVerified;
            Diagnostics = diagnostics ?? string.Empty;
        }

        internal bool TopAtExtent { get; }
        internal bool BottomAtExtent { get; }
        internal string CategorySignature { get; }
        internal bool CategorySignatureVerified { get; }
        internal string Diagnostics { get; }
    }

    internal interface IAchievementCategoryFrameEvidenceDetector
    {
        AchievementCategoryFrameEvidence Detect(Bitmap screenshot);
    }

    /// <summary>
    /// The Phase 7A.1 fixtures contain no verified bottom example. Production therefore returns
    /// no positional proof until a later live-calibration phase supplies safe scrollbar evidence.
    /// </summary>
    internal sealed class UncalibratedAchievementCategoryFrameEvidenceDetector :
        IAchievementCategoryFrameEvidenceDetector
    {
        public AchievementCategoryFrameEvidence Detect(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));
            return new AchievementCategoryFrameEvidence(
                topAtExtent: false,
                bottomAtExtent: false,
                categorySignature: string.Empty,
                categorySignatureVerified: false,
                diagnostics: "Right-list scrollbar/category thresholds are not live-calibrated; no positional proof was asserted.");
        }
    }

    internal sealed class AchievementListScrollCommandResult
    {
        internal AchievementListScrollCommandResult(
            bool succeeded,
            bool foregroundVerified,
            string failureReason,
            string diagnostics)
        {
            Succeeded = succeeded;
            ForegroundVerified = foregroundVerified;
            FailureReason = failureReason ?? string.Empty;
            Diagnostics = diagnostics ?? string.Empty;
        }

        internal bool Succeeded { get; }
        internal bool ForegroundVerified { get; }
        internal string FailureReason { get; }
        internal string Diagnostics { get; }
    }

    internal interface IAchievementListScrollInput
    {
        AchievementListScrollCommandResult Scroll(int wheelDelta, CancellationToken cancellationToken);
    }

    internal readonly struct AchievementViewportFingerprint : IEquatable<AchievementViewportFingerprint>
    {
        internal AchievementViewportFingerprint(ulong value) => Value = value;
        internal ulong Value { get; }

        internal static AchievementViewportFingerprint Create(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));
            Rectangle viewport = AchievementListRegions.For(screenshot.Size).RowViewport;
            int cursorX = (int)Math.Round((screenshot.Width - 1) * AchievementWindowsListScrollInput.ScrollTargetX);
            int cursorY = (int)Math.Round((screenshot.Height - 1) * AchievementWindowsListScrollInput.ScrollTargetY);
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            for (int y = viewport.Top; y < viewport.Bottom; y += 12)
            {
                for (int x = viewport.Left; x < viewport.Right; x += 12)
                {
                    if (Math.Abs(x - cursorX) <= 36 && Math.Abs(y - cursorY) <= 36) continue;
                    Color pixel = screenshot.GetPixel(x, y);
                    int luminance = ((54 * pixel.R) + (183 * pixel.G) + (19 * pixel.B)) >> 8;
                    byte quantized = (byte)(luminance >> 4);
                    hash ^= quantized;
                    hash *= prime;
                }
            }
            return new AchievementViewportFingerprint(hash);
        }

        public bool Equals(AchievementViewportFingerprint other) => Value == other.Value;
        public override bool Equals(object obj) => obj is AchievementViewportFingerprint other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString("X16", CultureInfo.InvariantCulture);
    }

    internal sealed class AchievementRowFrameObservation
    {
        internal AchievementRowFrameObservation(int frameIndex, AchievementVisibleRow row)
        {
            FrameIndex = frameIndex;
            Row = row ?? throw new ArgumentNullException(nameof(row));
        }

        internal int FrameIndex { get; }
        internal AchievementVisibleRow Row { get; }
    }

    internal sealed class AchievementObservedRow
    {
        internal AchievementObservedRow(
            string key,
            bool canonical,
            IEnumerable<AchievementRowFrameObservation> observations,
            AchievementVisibleRow selectedObservation,
            bool conflicting)
        {
            Key = key ?? string.Empty;
            Canonical = canonical;
            Observations = new ReadOnlyCollection<AchievementRowFrameObservation>(
                (observations ?? Array.Empty<AchievementRowFrameObservation>()).ToArray());
            SelectedObservation = selectedObservation;
            Conflicting = conflicting;
            FirstObservedFrame = Observations.Count == 0 ? -1 : Observations.Min(value => value.FrameIndex);
            LastObservedFrame = Observations.Count == 0 ? -1 : Observations.Max(value => value.FrameIndex);
            EverFullyVisible = Observations.Any(value =>
                value.Row.Visibility == AchievementVisibleRowVisibility.Complete);
        }

        internal string Key { get; }
        internal bool Canonical { get; }
        internal IReadOnlyList<AchievementRowFrameObservation> Observations { get; }
        internal AchievementVisibleRow SelectedObservation { get; }
        internal int FirstObservedFrame { get; }
        internal int LastObservedFrame { get; }
        internal int ObservationCount => Observations.Count;
        internal bool EverFullyVisible { get; }
        internal bool Conflicting { get; }
        internal bool Resolved => Canonical && !Conflicting && SelectedObservation != null;
    }

    internal sealed class AchievementCategoryCrawlProgress
    {
        internal AchievementCategoryCrawlProgress(int frameCount, int scrollActions,
            int uniqueIdentifiedRows, int unresolvedObservations,
            AchievementScrollProgressState scrollState)
        {
            FrameCount = frameCount;
            ScrollActions = scrollActions;
            UniqueIdentifiedRows = uniqueIdentifiedRows;
            UnresolvedObservations = unresolvedObservations;
            ScrollState = scrollState;
        }

        internal int FrameCount { get; }
        internal int ScrollActions { get; }
        internal int UniqueIdentifiedRows { get; }
        internal int UnresolvedObservations { get; }
        internal AchievementScrollProgressState ScrollState { get; }
    }

    internal sealed class AchievementCategoryCrawlTiming
    {
        internal AchievementCategoryCrawlTiming(TimeSpan capture, TimeSpan frameProcessing,
            TimeSpan scrolling, TimeSpan settling, TimeSpan deduplication, TimeSpan total)
        {
            Capture = capture;
            FrameProcessing = frameProcessing;
            Scrolling = scrolling;
            Settling = settling;
            Deduplication = deduplication;
            Total = total;
        }

        internal TimeSpan Capture { get; }
        internal TimeSpan FrameProcessing { get; }
        internal TimeSpan Scrolling { get; }
        internal TimeSpan Settling { get; }
        internal TimeSpan Deduplication { get; }
        internal TimeSpan Total { get; }
    }

    internal sealed class AchievementCategoryCrawlResult
    {
        internal AchievementCategoryCrawlResult(
            AchievementCategoryCrawlContext context,
            AchievementCategoryCrawlState state,
            string completenessEvidence,
            int processedFrames,
            int scrollActions,
            IEnumerable<AchievementObservedRow> observedRows,
            AchievementScrollProgressState finalScrollState,
            string reason,
            AchievementCategoryCrawlTiming timing,
            string diagnostics)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            State = state;
            CompletenessEvidence = completenessEvidence ?? string.Empty;
            ProcessedFrames = processedFrames;
            ScrollActions = scrollActions;
            ObservedRows = new ReadOnlyCollection<AchievementObservedRow>(
                (observedRows ?? Array.Empty<AchievementObservedRow>()).ToArray());
            IdentifiedRows = new ReadOnlyCollection<AchievementObservedRow>(ObservedRows
                .Where(row => row.Resolved).ToArray());
            UnresolvedRows = new ReadOnlyCollection<AchievementObservedRow>(ObservedRows
                .Where(row => !row.Resolved).ToArray());
            ConflictingRows = new ReadOnlyCollection<AchievementObservedRow>(ObservedRows
                .Where(row => row.Conflicting).ToArray());
            CompleteRowObservations = ObservedRows.Sum(row => row.Observations.Count(value =>
                value.Row.Visibility == AchievementVisibleRowVisibility.Complete));
            PartialRowObservations = ObservedRows.Sum(row => row.Observations.Count(value =>
                value.Row.Visibility == AchievementVisibleRowVisibility.Partial));
            UncertainRowObservations = ObservedRows.Sum(row => row.Observations.Count(value =>
                value.Row.Visibility == AchievementVisibleRowVisibility.Uncertain));
            RepeatedObservations = ObservedRows.Where(row => row.Canonical)
                .Sum(row => Math.Max(0, row.ObservationCount - 1));
            PartialRowsRecovered = ObservedRows.Count(row => row.Canonical && row.EverFullyVisible &&
                row.Observations.Any(value => value.Row.Visibility != AchievementVisibleRowVisibility.Complete));
            FinalScrollState = finalScrollState;
            Reason = reason ?? string.Empty;
            Timing = timing ?? throw new ArgumentNullException(nameof(timing));
            Diagnostics = diagnostics ?? string.Empty;
        }

        internal AchievementCategoryCrawlContext Context { get; }
        internal AchievementCategoryCrawlState State { get; }
        internal bool IsComplete => State == AchievementCategoryCrawlState.Complete;
        internal string CompletenessEvidence { get; }
        internal int ProcessedFrames { get; }
        internal int ScrollActions { get; }
        internal IReadOnlyList<AchievementObservedRow> ObservedRows { get; }
        internal IReadOnlyList<AchievementObservedRow> IdentifiedRows { get; }
        internal IReadOnlyList<AchievementObservedRow> UnresolvedRows { get; }
        internal IReadOnlyList<AchievementObservedRow> ConflictingRows { get; }
        internal int CompleteRowObservations { get; }
        internal int PartialRowObservations { get; }
        internal int UncertainRowObservations { get; }
        internal int RepeatedObservations { get; }
        internal int PartialRowsRecovered { get; }
        internal AchievementScrollProgressState FinalScrollState { get; }
        internal string Reason { get; }
        internal AchievementCategoryCrawlTiming Timing { get; }
        internal string Diagnostics { get; }
    }

    internal sealed class AchievementCategoryCrawler
    {
        private readonly IAchievementListFrameReader frameReader;
        private readonly IGameScreenCapture screenCapture;
        private readonly IAchievementListScrollInput scrollInput;
        private readonly IAchievementCategoryFrameEvidenceDetector frameEvidenceDetector;
        private readonly AchievementCategoryCrawlOptions options;
        private readonly Action<int, CancellationToken> wait;

        internal AchievementCategoryCrawler(
            IAchievementListFrameReader frameReader,
            IGameScreenCapture screenCapture,
            IAchievementListScrollInput scrollInput,
            IAchievementCategoryFrameEvidenceDetector frameEvidenceDetector,
            AchievementCategoryCrawlOptions options = null,
            Action<int, CancellationToken> wait = null)
        {
            this.frameReader = frameReader ?? throw new ArgumentNullException(nameof(frameReader));
            this.screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
            this.scrollInput = scrollInput ?? throw new ArgumentNullException(nameof(scrollInput));
            this.frameEvidenceDetector = frameEvidenceDetector ?? throw new ArgumentNullException(nameof(frameEvidenceDetector));
            this.options = options ?? AchievementCategoryCrawlOptions.Default;
            this.wait = wait ?? WaitWithCancellation;
        }

        internal AchievementCategoryCrawlResult Crawl(
            AchievementCategoryCrawlContext context,
            Bitmap startingFrame,
            CancellationToken cancellationToken,
            IProgress<AchievementCategoryCrawlProgress> progress = null,
            TimeSpan? startingCaptureDuration = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (startingFrame == null) throw new ArgumentNullException(nameof(startingFrame));

            Stopwatch total = Stopwatch.StartNew();
            TimeSpan captureTime = startingCaptureDuration ?? TimeSpan.Zero;
            TimeSpan processingTime = TimeSpan.Zero;
            TimeSpan scrollingTime = TimeSpan.Zero;
            TimeSpan settlingTime = TimeSpan.Zero;
            TimeSpan deduplicationTime = TimeSpan.Zero;
            var aggregate = new ObservationAccumulator();
            var diagnostics = new List<string>();
            int frames = 0;
            int scrolls = 0;
            int uncertainFrames = 0;
            int noMovementAttempts = 0;
            string stableCategorySignature = context.ExpectedCategorySignature;
            AchievementScrollProgressState scrollState = AchievementScrollProgressState.NoMovementUnknown;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                FrameSnapshot previous = ProcessFrame(startingFrame, 0, startingCaptureDuration ?? TimeSpan.Zero,
                    aggregate, ref processingTime, ref deduplicationTime);
                frames = 1;
                diagnostics.Add(previous.Diagnostics);
                if (!TryValidateFrame(context, previous, ref stableCategorySignature, firstFrame: true,
                    out AchievementCategoryCrawlState invalidState, out string invalidReason))
                    return Finish(invalidState, invalidReason, string.Empty);
                if (!context.KnownTraversalOrigin && !previous.Evidence.TopAtExtent)
                    return Finish(AchievementCategoryCrawlState.Incomplete,
                        "The category crawl did not begin from a positively established top or known traversal origin.",
                        "startPosition=unsupported");

                uncertainFrames = IsUncertain(previous.Result) ? 1 : 0;
                if (uncertainFrames > options.MaximumConsecutiveUncertainFrames)
                    return Finish(AchievementCategoryCrawlState.Incomplete,
                        "The starting frame exceeded the uncertain-frame safety limit.",
                        "limit=consecutive-uncertain-frames");
                progress?.Report(Progress(aggregate, frames, scrolls, scrollState));

                while (frames < options.MaximumCapturedFrames && scrolls < options.MaximumScrollActions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Stopwatch part = Stopwatch.StartNew();
                    AchievementListScrollCommandResult command = scrollInput.Scroll(options.WheelDelta, cancellationToken);
                    part.Stop();
                    scrollingTime += part.Elapsed;
                    scrolls++;
                    diagnostics.Add($"scroll={scrolls}; succeeded={command.Succeeded}; foregroundVerified={command.ForegroundVerified}; {command.Diagnostics}");
                    if (!command.Succeeded || !command.ForegroundVerified)
                        return Finish(AchievementCategoryCrawlState.Failed,
                            command.FailureReason.Length == 0 ? "Achievement-list scrolling was not safely performed." : command.FailureReason,
                            string.Empty);

                    cancellationToken.ThrowIfCancellationRequested();
                    part.Restart();
                    wait(options.ScrollSettleMilliseconds, cancellationToken);
                    part.Stop();
                    settlingTime += part.Elapsed;
                    cancellationToken.ThrowIfCancellationRequested();

                    Bitmap captured = null;
                    try
                    {
                        part.Restart();
                        captured = screenCapture.CaptureWindow();
                        part.Stop();
                        captureTime += part.Elapsed;
                        cancellationToken.ThrowIfCancellationRequested();
                        FrameSnapshot current = ProcessFrame(captured, frames, part.Elapsed,
                            aggregate, ref processingTime, ref deduplicationTime);
                        frames++;
                        diagnostics.Add(current.Diagnostics);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!TryValidateFrame(context, current, ref stableCategorySignature, firstFrame: false,
                            out invalidState, out invalidReason))
                        {
                            scrollState = AchievementScrollProgressState.LostScreen;
                            return Finish(invalidState, invalidReason, string.Empty);
                        }

                        uncertainFrames = IsUncertain(current.Result) ? uncertainFrames + 1 : 0;
                        if (uncertainFrames > options.MaximumConsecutiveUncertainFrames)
                            return Finish(AchievementCategoryCrawlState.Incomplete,
                                "The maximum consecutive uncertain-frame limit was reached.",
                                "limit=consecutive-uncertain-frames");

                        bool viewportStable = current.Fingerprint.Equals(previous.Fingerprint);
                        bool layoutStable = StringComparer.Ordinal.Equals(current.LayoutFingerprint, previous.LayoutFingerprint);
                        bool recognizableFinalRow = HasRecognizableFinalRow(current.Result);
                        bool bottomProof = viewportStable && layoutStable && recognizableFinalRow &&
                            current.Evidence.BottomAtExtent && previous.Evidence.BottomAtExtent;
                        if (bottomProof)
                        {
                            scrollState = AchievementScrollProgressState.ReachedBottom;
                            progress?.Report(Progress(aggregate, frames, scrolls, scrollState));
                            return Finish(AchievementCategoryCrawlState.Complete,
                                "The right-list bottom extent, stable viewport, stable row layout, and recognizable final-row evidence agreed after a bounded scroll.",
                                $"bottomAtExtent=true; viewportStable=true; layoutStable=true; finalRowRecognized=true; fingerprint={current.Fingerprint}");
                        }

                        if (viewportStable)
                        {
                            scrollState = AchievementScrollProgressState.NoMovementUnknown;
                            noMovementAttempts++;
                            if (noMovementAttempts > options.MaximumNoMovementRecoveryAttempts)
                                return Finish(AchievementCategoryCrawlState.Incomplete,
                                    "The viewport did not move and independent bottom proof was unavailable.",
                                    "noMovementWithoutBottomProof=true");
                        }
                        else
                        {
                            scrollState = AchievementScrollProgressState.ScrollAdvanced;
                            noMovementAttempts = 0;
                        }

                        previous = current;
                        cancellationToken.ThrowIfCancellationRequested();
                        progress?.Report(Progress(aggregate, frames, scrolls, scrollState));
                    }
                    finally
                    {
                        captured?.Dispose();
                    }
                }

                string limit = frames >= options.MaximumCapturedFrames
                    ? "maximum captured-frame limit"
                    : "maximum scroll-action limit";
                return Finish(AchievementCategoryCrawlState.Incomplete,
                    $"The {limit} was reached without positive bottom proof.",
                    "limitReached=true");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                scrollState = AchievementScrollProgressState.Cancelled;
                return Finish(AchievementCategoryCrawlState.Cancelled,
                    "The category crawl was cancelled; retained observations do not establish completeness.",
                    "cancelled=true");
            }
            catch (Exception ex)
            {
                return Finish(AchievementCategoryCrawlState.Failed,
                    $"The category crawl failed: {ex.GetType().Name}: {ex.Message}",
                    "exception=true");
            }

            AchievementCategoryCrawlResult Finish(
                AchievementCategoryCrawlState state,
                string reason,
                string completenessEvidence)
            {
                total.Stop();
                AchievementObservedRow[] rows = aggregate.Build();
                var timing = new AchievementCategoryCrawlTiming(captureTime, processingTime,
                    scrollingTime, settlingTime, deduplicationTime, total.Elapsed);
                diagnostics.Add($"state={state}; frames={frames}; scrolls={scrolls}; uniqueIdentified={rows.Count(row => row.Resolved)}; " +
                    $"unresolved={rows.Count(row => !row.Resolved)}; repeated={rows.Where(row => row.Canonical).Sum(row => Math.Max(0, row.ObservationCount - 1))}; " +
                    $"captureMs={captureTime.TotalMilliseconds:0.00}; processingMs={processingTime.TotalMilliseconds:0.00}; " +
                    $"scrollMs={scrollingTime.TotalMilliseconds:0.00}; settleMs={settlingTime.TotalMilliseconds:0.00}; " +
                    $"dedupMs={deduplicationTime.TotalMilliseconds:0.00}; totalMs={total.Elapsed.TotalMilliseconds:0.00}");
                return new AchievementCategoryCrawlResult(context, state, completenessEvidence,
                    frames, scrolls, rows, scrollState, reason, timing,
                    string.Join(Environment.NewLine, diagnostics));
            }
        }

        private FrameSnapshot ProcessFrame(Bitmap screenshot, int frameIndex, TimeSpan captureDuration,
            ObservationAccumulator aggregate, ref TimeSpan processingTime, ref TimeSpan deduplicationTime)
        {
            Stopwatch part = Stopwatch.StartNew();
            AchievementCategoryFrameEvidence evidence = frameEvidenceDetector.Detect(screenshot);
            AchievementViewportFingerprint fingerprint = AchievementViewportFingerprint.Create(screenshot);
            AchievementBulkFrameResult result = frameReader.Read(screenshot, captureDuration);
            part.Stop();
            processingTime += part.Elapsed;
            string layout = string.Join("|", result.VisibleRows.Select(row =>
                $"{row.Visibility}:{row.Fingerprint.Value}:{row.IdentityState}:{row.CompletionState}"));
            part.Restart();
            aggregate.Add(frameIndex, result.VisibleRows);
            part.Stop();
            deduplicationTime += part.Elapsed;
            string diagnostics = $"frame={frameIndex}; screen={result.ScreenState}; rows={result.VisibleRows.Count}; " +
                $"fingerprint={fingerprint}; layout={layout}; categorySignature={evidence.CategorySignature}; " +
                $"categoryVerified={evidence.CategorySignatureVerified}; top={evidence.TopAtExtent}; bottom={evidence.BottomAtExtent}; {evidence.Diagnostics}";
            return new FrameSnapshot(result, evidence, fingerprint, layout, diagnostics);
        }

        private static bool TryValidateFrame(AchievementCategoryCrawlContext context,
            FrameSnapshot frame, ref string stableSignature, bool firstFrame,
            out AchievementCategoryCrawlState state, out string reason)
        {
            if (frame.Result.ScreenState != AchievementScreenState.Idle ||
                !frame.Result.IsKeyboardMouseIdle)
            {
                state = AchievementCategoryCrawlState.Failed;
                reason = firstFrame
                    ? $"The starting frame was not a supported ordinary Keyboard/Mouse Achievement list ({frame.Result.ScreenState})."
                    : $"The Keyboard/Mouse Achievement list was lost after scrolling ({frame.Result.ScreenState}).";
                return false;
            }

            if (context.ExpectedCategorySignature.Length > 0)
            {
                if (!frame.Evidence.CategorySignatureVerified ||
                    !StringComparer.Ordinal.Equals(context.ExpectedCategorySignature, frame.Evidence.CategorySignature))
                {
                    state = AchievementCategoryCrawlState.Failed;
                    reason = "The selected Achievement category signature was not positively preserved.";
                    return false;
                }
            }
            else if (frame.Evidence.CategorySignatureVerified)
            {
                if (stableSignature.Length == 0) stableSignature = frame.Evidence.CategorySignature;
                else if (!StringComparer.Ordinal.Equals(stableSignature, frame.Evidence.CategorySignature))
                {
                    state = AchievementCategoryCrawlState.Failed;
                    reason = "The selected Achievement category changed unexpectedly during the crawl.";
                    return false;
                }
            }

            state = default;
            reason = string.Empty;
            return true;
        }

        private static bool IsUncertain(AchievementBulkFrameResult result) =>
            result.VisibleRows.Count == 0 || result.VisibleRows.All(row =>
                row.Visibility != AchievementVisibleRowVisibility.Complete ||
                row.IdentityState == AchievementIdentityState.Unresolved ||
                row.IdentityState == AchievementIdentityState.Ambiguous);

        private static bool HasRecognizableFinalRow(AchievementBulkFrameResult result) =>
            result.VisibleRows.OrderBy(row => row.NormalizedVerticalPosition).LastOrDefault() is AchievementVisibleRow row &&
            row.Visibility == AchievementVisibleRowVisibility.Complete &&
            row.Fingerprint.SuitableForDeduplication &&
            (row.IdentityState == AchievementIdentityState.Identified ||
             row.IdentityState == AchievementIdentityState.StageGroupIdentified);

        private static AchievementCategoryCrawlProgress Progress(ObservationAccumulator aggregate,
            int frames, int scrolls, AchievementScrollProgressState state)
        {
            AchievementObservedRow[] rows = aggregate.Build();
            return new AchievementCategoryCrawlProgress(frames, scrolls,
                rows.Count(row => row.Resolved), rows.Count(row => !row.Resolved), state);
        }

        private static void WaitWithCancellation(int milliseconds, CancellationToken token)
        {
            if (milliseconds <= 0) { token.ThrowIfCancellationRequested(); return; }
            if (token.WaitHandle.WaitOne(milliseconds)) token.ThrowIfCancellationRequested();
        }

        private sealed class FrameSnapshot
        {
            internal FrameSnapshot(AchievementBulkFrameResult result,
                AchievementCategoryFrameEvidence evidence,
                AchievementViewportFingerprint fingerprint,
                string layoutFingerprint,
                string diagnostics)
            {
                Result = result;
                Evidence = evidence;
                Fingerprint = fingerprint;
                LayoutFingerprint = layoutFingerprint;
                Diagnostics = diagnostics;
            }
            internal AchievementBulkFrameResult Result { get; }
            internal AchievementCategoryFrameEvidence Evidence { get; }
            internal AchievementViewportFingerprint Fingerprint { get; }
            internal string LayoutFingerprint { get; }
            internal string Diagnostics { get; }
        }

        private sealed class ObservationAccumulator
        {
            private readonly Dictionary<string, MutableObservedRow> canonical = new(StringComparer.Ordinal);
            private readonly List<MutableObservedRow> unresolved = new();

            internal void Add(int frameIndex, IEnumerable<AchievementVisibleRow> rows)
            {
                foreach (AchievementVisibleRow row in rows ?? Array.Empty<AchievementVisibleRow>())
                {
                    var observation = new AchievementRowFrameObservation(frameIndex, row);
                    if (row.Fingerprint.SuitableForDeduplication)
                    {
                        if (!canonical.TryGetValue(row.Fingerprint.Value, out MutableObservedRow value))
                        {
                            value = new MutableObservedRow(row.Fingerprint.Value, canonical: true);
                            canonical.Add(row.Fingerprint.Value, value);
                        }
                        value.Add(observation);
                    }
                    else
                    {
                        var value = new MutableObservedRow(
                            $"observation:{frameIndex}:{row.RowIndex}:{unresolved.Count}", canonical: false);
                        value.Add(observation);
                        unresolved.Add(value);
                    }
                }
            }

            internal AchievementObservedRow[] Build() => canonical.Values.Concat(unresolved)
                .Select(value => value.Build())
                .OrderBy(value => value.FirstObservedFrame)
                .ThenBy(value => value.Key, StringComparer.Ordinal)
                .ToArray();
        }

        private sealed class MutableObservedRow
        {
            private readonly List<AchievementRowFrameObservation> observations = new();
            private AchievementVisibleRow selected;
            private int selectedStrength = int.MinValue;
            private bool conflicting;

            internal MutableObservedRow(string key, bool canonical)
            {
                Key = key;
                Canonical = canonical;
            }

            internal string Key { get; }
            internal bool Canonical { get; }

            internal void Add(AchievementRowFrameObservation observation)
            {
                observations.Add(observation);
                int strength = Strength(observation.Row);
                if (selected == null || strength > selectedStrength)
                {
                    selected = observation.Row;
                    selectedStrength = strength;
                    conflicting = false;
                }
                else if (strength == selectedStrength && Conflicts(selected, observation.Row))
                {
                    conflicting = true;
                }
            }

            internal AchievementObservedRow Build() => new(Key, Canonical,
                observations, selected, conflicting);

            private static int Strength(AchievementVisibleRow row)
            {
                int value = row.Visibility == AchievementVisibleRowVisibility.Complete ? 8 :
                    row.Visibility == AchievementVisibleRowVisibility.Partial ? 4 : 2;
                if (row.IdentityState == AchievementIdentityState.Identified ||
                    row.IdentityState == AchievementIdentityState.StageGroupIdentified) value += 4;
                if (row.CompletionState != AchievementCompletionState.Unresolved) value += 2;
                if (row.SuitableForTrustedCompletionExtraction) value++;
                return value;
            }

            private static bool Conflicts(AchievementVisibleRow left, AchievementVisibleRow right) =>
                left.CompletionState != right.CompletionState ||
                !left.CandidateIds.SequenceEqual(right.CandidateIds) ||
                left.StageGroupId != right.StageGroupId;
        }
    }
}

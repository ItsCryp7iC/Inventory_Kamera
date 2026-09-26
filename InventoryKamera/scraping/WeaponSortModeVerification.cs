using InventoryKamera.game;
using NLog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Tesseract;

namespace InventoryKamera
{
    internal enum WeaponSortMatchReason
    {
        None,
        Fuzzy,
        SafePrefix,
        Exact,
    }

    internal sealed class WeaponSortRecognition
    {
        public string NormalizedText { get; }
        public string RecognizedMode { get; }
        public WeaponSortMatchReason MatchReason { get; }
        public double Similarity { get; }

        public WeaponSortRecognition(
            string normalizedText,
            string recognizedMode,
            WeaponSortMatchReason matchReason,
            double similarity)
        {
            NormalizedText = normalizedText ?? string.Empty;
            RecognizedMode = recognizedMode;
            MatchReason = matchReason;
            Similarity = similarity;
        }
    }

    /// <summary>
    /// Conservative recognition policy scoped only to the three labels in Genshin's weapon-sort
    /// dropdown. It preserves the existing &gt;80% fuzzy rule, while also accepting a complete known
    /// label followed by at most two vertical glyphs that Tesseract commonly invents for the button
    /// decoration. Short/incomplete prefixes and arbitrary alphabetic suffixes remain untrusted.
    /// </summary>
    internal static class WeaponSortModeRecognizer
    {
        internal const double FuzzyThreshold = 80.0;

        private static readonly IReadOnlyList<string> modeNames =
            Array.AsReadOnly(new[] { "Level", "Quality", "Type" });

        internal static IReadOnlyList<string> ModeNames => modeNames;

        private const int MaximumSafeSuffixLength = 2;

        public static WeaponSortRecognition Recognize(string rawText)
        {
            string normalized = Normalize(rawText);
            if (string.IsNullOrWhiteSpace(normalized))
                return new WeaponSortRecognition(normalized, null, WeaponSortMatchReason.None, 0);

            foreach (string mode in ModeNames)
            {
                string candidate = mode.ToLowerInvariant();
                if (normalized == candidate)
                    return new WeaponSortRecognition(normalized, mode, WeaponSortMatchReason.Exact, 100);
            }

            List<string> safePrefixMatches = ModeNames
                .Where(mode => IsSafePrefix(normalized, mode.ToLowerInvariant()))
                .ToList();
            if (safePrefixMatches.Count == 1)
            {
                string mode = safePrefixMatches[0];
                return new WeaponSortRecognition(
                    normalized,
                    mode,
                    WeaponSortMatchReason.SafePrefix,
                    Similarity(normalized, mode.ToLowerInvariant()));
            }

            string bestMode = null;
            double bestSimilarity = 0;
            bool ambiguous = false;
            foreach (string mode in ModeNames)
            {
                double similarity = Similarity(normalized, mode.ToLowerInvariant());
                if (similarity > bestSimilarity)
                {
                    bestMode = mode;
                    bestSimilarity = similarity;
                    ambiguous = false;
                }
                else if (Math.Abs(similarity - bestSimilarity) < 0.001)
                {
                    ambiguous = true;
                }
            }

            if (!ambiguous && bestSimilarity > FuzzyThreshold)
            {
                return new WeaponSortRecognition(
                    normalized,
                    bestMode,
                    WeaponSortMatchReason.Fuzzy,
                    bestSimilarity);
            }

            return new WeaponSortRecognition(normalized, null, WeaponSortMatchReason.None, bestSimilarity);
        }

        internal static string Normalize(string value) =>
            Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"[\W]", string.Empty);

        internal static int IndexOfMode(string mode)
        {
            for (int index = 0; index < ModeNames.Count; index++)
            {
                if (string.Equals(ModeNames[index], mode, StringComparison.Ordinal)) return index;
            }
            return -1;
        }

        private static bool IsSafePrefix(string text, string candidate)
        {
            if (!text.StartsWith(candidate, StringComparison.Ordinal) || text.Length <= candidate.Length)
                return false;

            string suffix = text.Substring(candidate.Length);
            return suffix.Length <= MaximumSafeSuffixLength &&
                suffix.All(character => character == 'i' || character == 'l' || character == '1');
        }

        private static double Similarity(string left, string right)
        {
            int maxLength = Math.Max(left.Length, right.Length);
            if (maxLength == 0) return 100;

            int[,] distances = new int[left.Length + 1, right.Length + 1];
            for (int i = 0; i <= left.Length; i++) distances[i, 0] = i;
            for (int j = 0; j <= right.Length; j++) distances[0, j] = j;

            for (int i = 1; i <= left.Length; i++)
            {
                for (int j = 1; j <= right.Length; j++)
                {
                    int substitutionCost = left[i - 1] == right[j - 1] ? 0 : 1;
                    distances[i, j] = Math.Min(
                        Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                        distances[i - 1, j - 1] + substitutionCost);
                }
            }

            return (1.0 - distances[left.Length, right.Length] / (double)maxLength) * 100.0;
        }
    }

    internal sealed class WeaponSortOcrAttempt
    {
        public bool Inverted { get; }
        public string RawText { get; }
        public WeaponSortRecognition Recognition { get; }

        public WeaponSortOcrAttempt(bool inverted, string rawText, WeaponSortRecognition recognition)
        {
            Inverted = inverted;
            RawText = rawText ?? string.Empty;
            Recognition = recognition ?? throw new ArgumentNullException(nameof(recognition));
        }
    }

    internal sealed class WeaponSortModeDetection
    {
        public string RecognizedMode { get; }
        public IReadOnlyList<WeaponSortOcrAttempt> OcrAttempts { get; }

        public WeaponSortModeDetection(
            string recognizedMode,
            IReadOnlyList<WeaponSortOcrAttempt> ocrAttempts = null)
        {
            RecognizedMode = recognizedMode;
            OcrAttempts = ocrAttempts ?? Array.Empty<WeaponSortOcrAttempt>();
        }

        internal static WeaponSortModeDetection ForMode(string mode) =>
            new WeaponSortModeDetection(mode);

        internal static WeaponSortModeDetection Unrecognized() =>
            new WeaponSortModeDetection(null);
    }

    internal interface IWeaponSortModeDetector
    {
        WeaponSortModeDetection Detect(Bitmap sortLabelRegion);
    }

    internal sealed class WeaponSortModeDetector : IWeaponSortModeDetector
    {
        private readonly IOcrService ocrService;
        private readonly IImagePreprocessor imagePreprocessor;

        public WeaponSortModeDetector(IOcrService ocrService, IImagePreprocessor imagePreprocessor)
        {
            this.ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            this.imagePreprocessor = imagePreprocessor ?? throw new ArgumentNullException(nameof(imagePreprocessor));
        }

        public WeaponSortModeDetection Detect(Bitmap sortLabelRegion)
        {
            if (sortLabelRegion == null) throw new ArgumentNullException(nameof(sortLabelRegion));

            Bitmap gray = imagePreprocessor.ConvertToGrayscale(sortLabelRegion);
            try
            {
                imagePreprocessor.SetContrast(60.0, ref gray);
                var attempts = new List<WeaponSortOcrAttempt>(2);

                foreach (bool invert in new[] { false, true })
                {
                    Bitmap candidate = (Bitmap)gray.Clone();
                    try
                    {
                        if (invert) imagePreprocessor.SetInvert(ref candidate);
                        string rawText = ocrService
                            .AnalyzeText(candidate, PageSegMode.SingleLine)
                            .Trim();
                        attempts.Add(new WeaponSortOcrAttempt(
                            invert,
                            rawText,
                            WeaponSortModeRecognizer.Recognize(rawText)));
                    }
                    finally
                    {
                        candidate.Dispose();
                    }
                }

                string recognizedMode = SelectStrongestUnambiguousMode(attempts);
                return new WeaponSortModeDetection(recognizedMode, attempts.AsReadOnly());
            }
            finally
            {
                gray.Dispose();
            }
        }

        private static string SelectStrongestUnambiguousMode(IReadOnlyList<WeaponSortOcrAttempt> attempts)
        {
            List<WeaponSortRecognition> matches = attempts
                .Select(attempt => attempt.Recognition)
                .Where(recognition => recognition.RecognizedMode != null)
                .OrderByDescending(recognition => recognition.MatchReason)
                .ThenByDescending(recognition => recognition.Similarity)
                .ToList();
            if (matches.Count == 0) return null;

            WeaponSortRecognition strongest = matches[0];
            bool equallyStrongConflict = matches.Skip(1).Any(other =>
                other.MatchReason == strongest.MatchReason &&
                Math.Abs(other.Similarity - strongest.Similarity) < 0.001 &&
                !string.Equals(other.RecognizedMode, strongest.RecognizedMode, StringComparison.Ordinal));
            return equallyStrongConflict ? null : strongest.RecognizedMode;
        }
    }

    internal interface IWeaponSortModeCapture
    {
        Bitmap CaptureSortLabel();
    }

    internal sealed class NavigationWeaponSortModeCapture : IWeaponSortModeCapture
    {
        public Bitmap CaptureSortLabel() => Navigation.CaptureRegion(
            x: (int)(0.0625 * Navigation.GetWidth()),
            y: (int)((Navigation.IsNormal ? 0.9037 : 0.9162) * Navigation.GetHeight()),
            width: (int)(0.1167 * Navigation.GetWidth()),
            height: (int)(0.0389 * Navigation.GetHeight()));
    }

    internal interface IWeaponSortModeDiagnostics
    {
        void SaveFailure(string phase, IReadOnlyList<Bitmap> attemptCrops);
    }

    internal sealed class WeaponSortModeDiagnostics : IWeaponSortModeDiagnostics
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public void SaveFailure(string phase, IReadOnlyList<Bitmap> attemptCrops)
        {
            if (attemptCrops == null || attemptCrops.Count == 0) return;

            string uniqueName = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) +
                "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + "_" + phase;
            string directory = Path.Combine("./logging", "weapon-sort-mode", uniqueName);
            try
            {
                Directory.CreateDirectory(directory);
                for (int i = 0; i < attemptCrops.Count; i++)
                {
                    attemptCrops[i].Save(Path.Combine(
                        directory,
                        $"attempt_{i + 1:00}_crop.png"));
                }
                Logger.Warn("Weapon sort verification diagnostics saved to {0}.", directory);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not save weapon sort verification diagnostics.");
            }
        }
    }

    internal sealed class WeaponSortModeTiming
    {
        public int DropdownHoldMs { get; }
        public int DropdownSettleMs { get; }
        public int PreConfirmSettleMs { get; }
        public int ConfirmHoldMs { get; }
        public int PostConfirmSettleMs { get; }
        public int DetectionRetryMs { get; }

        public WeaponSortModeTiming(
            int dropdownHoldMs,
            int dropdownSettleMs,
            int preConfirmSettleMs,
            int confirmHoldMs,
            int postConfirmSettleMs,
            int detectionRetryMs)
        {
            DropdownHoldMs = dropdownHoldMs;
            DropdownSettleMs = dropdownSettleMs;
            PreConfirmSettleMs = preConfirmSettleMs;
            ConfirmHoldMs = confirmHoldMs;
            PostConfirmSettleMs = postConfirmSettleMs;
            DetectionRetryMs = detectionRetryMs;
        }
    }

    /// <summary>
    /// Reads and, when needed, changes the weapon sort mode. A requested sort is considered confirmed
    /// only after a fresh post-input capture recognizes that exact mode.
    /// </summary>
    internal sealed class WeaponSortModeVerifier
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        internal const int DefaultVerificationAttempts = 3;

        private readonly IWeaponSortModeCapture capture;
        private readonly IWeaponSortModeDetector detector;
        private readonly IWeaponSortModeDiagnostics diagnostics;
        private readonly Action<int> wait;
        private readonly int verificationAttempts;

        public WeaponSortModeVerifier(
            IWeaponSortModeCapture capture,
            IWeaponSortModeDetector detector,
            IWeaponSortModeDiagnostics diagnostics = null,
            Action<int> wait = null,
            int verificationAttempts = DefaultVerificationAttempts)
        {
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.diagnostics = diagnostics ?? new WeaponSortModeDiagnostics();
            this.wait = wait ?? Thread.Sleep;
            this.verificationAttempts = Math.Max(1, verificationAttempts);
        }

        public bool ConfirmSortMode(
            GameNavigator navigator,
            string desiredMode,
            WeaponSortModeTiming timing)
        {
            if (navigator == null) throw new ArgumentNullException(nameof(navigator));
            if (timing == null) throw new ArgumentNullException(nameof(timing));

            int desiredIndex = WeaponSortModeRecognizer.IndexOfMode(desiredMode);
            if (desiredIndex < 0) throw new ArgumentOutOfRangeException(nameof(desiredMode));

            string currentMode = DetectWithFreshRetries(
                phase: "pre-change",
                desiredMode,
                requiredMode: null,
                timing.DetectionRetryMs);
            if (currentMode == desiredMode)
            {
                Logger.Info("Controller weapon sort: already \"{0}\", no change needed.", currentMode);
                return true;
            }

            int currentIndex = WeaponSortModeRecognizer.IndexOfMode(currentMode);
            if (currentIndex < 0)
            {
                Logger.Warn("Could not confidently detect current weapon sort mode -- skipping sort selection.");
                return false;
            }

            int steps = Math.Abs(desiredIndex - currentIndex);
            GameNavigator.MenuDirection direction = desiredIndex > currentIndex
                ? GameNavigator.MenuDirection.Down
                : GameNavigator.MenuDirection.Up;

            Logger.Info(
                "Controller weapon sort change requested: detected={0}; desired={1}; steps={2}; direction={3}.",
                currentMode,
                desiredMode,
                steps,
                direction);
            navigator.TapDPadDown(holdMs: timing.DropdownHoldMs);
            wait(timing.DropdownSettleMs);
            navigator.Move(direction, steps);
            wait(timing.PreConfirmSettleMs);
            navigator.TapConfirm(holdMs: timing.ConfirmHoldMs);
            wait(timing.PostConfirmSettleMs);

            string verifiedMode = DetectWithFreshRetries(
                phase: "post-change",
                desiredMode,
                requiredMode: desiredMode,
                timing.DetectionRetryMs);
            bool confirmed = verifiedMode == desiredMode;
            Logger.Info(
                "Controller weapon sort change result: before={0}; requested={1}; after={2}; confirmed={3}.",
                currentMode,
                desiredMode,
                verifiedMode ?? "(unrecognized)",
                confirmed);
            return confirmed;
        }

        private string DetectWithFreshRetries(
            string phase,
            string desiredMode,
            string requiredMode,
            int retryMs)
        {
            var failedCrops = new List<Bitmap>();
            string lastRecognizedMode = null;
            try
            {
                for (int attempt = 1; attempt <= verificationAttempts; attempt++)
                {
                    Bitmap crop = capture.CaptureSortLabel();
                    WeaponSortModeDetection detection;
                    try
                    {
                        detection = detector.Detect(crop);
                    }
                    catch
                    {
                        crop.Dispose();
                        throw;
                    }

                    lastRecognizedMode = detection.RecognizedMode;
                    bool accepted = lastRecognizedMode != null &&
                        (requiredMode == null || lastRecognizedMode == requiredMode);
                    LogDetectionAttempt(
                        phase,
                        attempt,
                        desiredMode,
                        accepted,
                        detection);

                    if (accepted)
                    {
                        crop.Dispose();
                        DisposeAll(failedCrops);
                        failedCrops.Clear();
                        return lastRecognizedMode;
                    }

                    failedCrops.Add(crop);
                    if (attempt < verificationAttempts) wait(retryMs);
                }

                diagnostics.SaveFailure(phase, failedCrops);
                return lastRecognizedMode;
            }
            finally
            {
                DisposeAll(failedCrops);
            }
        }

        private void LogDetectionAttempt(
            string phase,
            int attempt,
            string desiredMode,
            bool accepted,
            WeaponSortModeDetection detection)
        {
            if (detection.OcrAttempts.Count == 0)
            {
                Logger.Info(
                    "Weapon sort verification {0} attempt {1}/{2}: no OCR details; recognized={3}; desired={4}; accepted={5}.",
                    phase,
                    attempt,
                    verificationAttempts,
                    detection.RecognizedMode ?? "(none)",
                    desiredMode,
                    accepted);
                return;
            }

            foreach (WeaponSortOcrAttempt ocrAttempt in detection.OcrAttempts)
            {
                WeaponSortRecognition recognition = ocrAttempt.Recognition;
                Logger.Info(
                    "Weapon sort verification {0} attempt {1}/{2}: inverted={3}; raw=\"{4}\"; normalized=\"{5}\"; " +
                    "candidate={6}; reason={7}; similarity={8:0.0}%; desired={9}; accepted={10}.",
                    phase,
                    attempt,
                    verificationAttempts,
                    ocrAttempt.Inverted,
                    SanitizeLogText(ocrAttempt.RawText),
                    recognition.NormalizedText,
                    recognition.RecognizedMode ?? "(none)",
                    recognition.MatchReason.ToString().ToLowerInvariant(),
                    recognition.Similarity,
                    desiredMode,
                    accepted && detection.RecognizedMode == recognition.RecognizedMode);
            }
        }

        private static string SanitizeLogText(string text) =>
            (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();

        private static void DisposeAll(IEnumerable<Bitmap> bitmaps)
        {
            foreach (Bitmap bitmap in bitmaps) bitmap?.Dispose();
        }
    }

    internal readonly struct WeaponFilterDecision
    {
        public bool ShouldStop { get; }
        public bool ShouldDiscard { get; }

        public WeaponFilterDecision(bool shouldStop, bool shouldDiscard)
        {
            ShouldStop = shouldStop;
            ShouldDiscard = shouldDiscard;
        }
    }

    internal static class WeaponFilterPolicy
    {
        public static WeaponFilterDecision Evaluate(
            bool sortModeConfirmed,
            bool sortByLevel,
            bool belowRarity,
            bool belowLevel)
        {
            bool shouldStop = sortModeConfirmed &&
                ((sortByLevel && belowLevel) || (!sortByLevel && belowRarity));
            return new WeaponFilterDecision(
                shouldStop,
                shouldStop || belowRarity || belowLevel);
        }
    }
}

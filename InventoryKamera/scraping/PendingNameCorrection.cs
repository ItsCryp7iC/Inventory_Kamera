using System;

namespace InventoryKamera
{
    /// <summary>
    /// Describes a low-confidence identifying-name OCR result (weapon name / artifact set name) whose
    /// correction dialog is deferred until the whole scan finishes rather than shown mid-scan (Phase 3
    /// §3.3, deferred-correction revision). The scraper produces one of these alongside the record it
    /// built from the raw best-guess; <see cref="GameScanner"/> enqueues it via
    /// <see cref="IScanProgressReporter.EnqueueCorrection"/> and, once every scan phase is done,
    /// <see cref="IScanProgressReporter.FlushDeferredCorrections"/> shows the dialogs back-to-back and
    /// invokes each apply callback.
    ///
    /// <para><see cref="Resolve"/> turns the user's corrected free text into a canonical value (via the
    /// same fuzzy-match the inline path used) and returns <c>null</c> when nothing effectively changed,
    /// so the apply callback can no-op. It closes over its owning scraper, so it must only be invoked
    /// while that scraper is still alive -- which flush, running on the scan thread before the scan
    /// object is torn down, guarantees.</para>
    /// </summary>
    internal sealed class PendingNameCorrection
    {
        /// <summary>The recognized text to seed the correction dialog with (what OCR read).</summary>
        public string RecognizedText { get; }

        /// <summary>Tesseract's mean confidence for this recognition, as a 0-100 percentage.</summary>
        public float ConfidencePercent { get; }

        /// <summary>Human-readable field description shown in the dialog, e.g. "Weapon name".</summary>
        public string FieldLabel { get; }

        /// <summary>
        /// Maps the user's corrected text to a canonical name; returns <c>null</c> if the correction
        /// leaves the value effectively unchanged (blank, or identical to what was already recognized).
        /// </summary>
        public Func<string, string> Resolve { get; }

        public PendingNameCorrection(string recognizedText, float confidencePercent, string fieldLabel, Func<string, string> resolve)
        {
            RecognizedText = recognizedText;
            ConfidencePercent = confidencePercent;
            FieldLabel = fieldLabel;
            Resolve = resolve;
        }
    }
}

using System.Drawing;
using System.Threading;

namespace InventoryKamera
{
    /// <summary>
    /// The scan-progress-reporting surface scan logic calls into during a scan, behind an injectable
    /// seam instead of the concrete static <see cref="UserInterface"/> WinForms class (Phase 2 §2.5).
    /// <see cref="ScanViewModel"/> is the only implementation: most methods still delegate straight to
    /// <see cref="UserInterface"/>'s direct <c>Control.Invoke</c> writes, but it owns real observable
    /// state for the counter group as the first slice of the larger MVVM redesign -- see the plan
    /// doc's §2.5 sequencing note for why the rest is being carved out incrementally instead of all at
    /// once.
    /// </summary>
    internal interface IScanProgressReporter
    {
        void SetGear(Bitmap bm, Weapon weapon);
        void SetGear(Bitmap bm, Artifact artifact);
        void SetGearPictureBox(Bitmap bm);
        void SetGearTextBox(string text);
        void SetMainCharacterName(string text);
        void SetCharacter_NameAndElement(Bitmap bm, string name, string element);
        void SetCharacter_Level(Bitmap bm, int level, int maxLevel);
        void SetCharacter_Constellation(int level);
        void SetCharacter_Constellation(Bitmap bm, int level);
        void SetMaterial(Bitmap nameplate, Bitmap quantity, string name, int count);
        void SetMora(Bitmap mora, int count);
        void SetCharacter_Talent(Bitmap bm, string text, int i);
        void SetWeapon_Max(int value);
        void SetArtifact_Max(int value);
        void SetCharacter_Max(int value);
        void IncrementArtifactCount();
        void IncrementWeaponCount();
        void IncrementCharacterCount();
        void SetProgramStatus(string status, bool ok = true);
        void AddError(string error);
        void SetNavigation_Image(Bitmap bm);
        void ResetCharacterDisplay();
        void ResetGearDisplay();
        void ResetCounters();
        void ResetErrors();
        void ResetAll();

        /// <summary>
        /// Surfaces a low-confidence OCR result for inline user correction (Phase 3 §3.3). Blocks the
        /// calling scan thread until the user resolves it or <paramref name="cancellationToken"/> is
        /// cancelled. Returns <paramref name="recognizedText"/> unchanged if nothing is subscribed,
        /// the user declines to correct it, or cancellation wins the wait.
        /// </summary>
        string RequestCorrection(
            Bitmap image,
            string recognizedText,
            float confidencePercent,
            string fieldLabel,
            CancellationToken cancellationToken);

        /// <summary>
        /// Queues a low-confidence identifying-name OCR result (weapon name / artifact set name) for
        /// correction <em>after the whole scan finishes</em> instead of interrupting it mid-scan like
        /// <see cref="RequestCorrection"/> does. Returns immediately without blocking; the record is
        /// left with its raw best-guess for the duration of the scan. <paramref name="image"/> is
        /// cloned internally (the caller keeps ownership of the original), so the caller may dispose it
        /// as usual right after queuing. When the scan ends, <see cref="FlushDeferredCorrections"/>
        /// shows the dialog and calls <paramref name="apply"/> with the user's (possibly unchanged)
        /// text so it can patch the already-built record. If nothing is subscribed (headless/test),
        /// the request is dropped and <paramref name="apply"/> is never called -- the best-guess the
        /// record already carries stands, matching <see cref="RequestCorrection"/>'s degrade behavior.
        /// </summary>
        void EnqueueCorrection(Bitmap image, string recognizedText, float confidencePercent, string fieldLabel, System.Action<string> apply);

        /// <summary>
        /// Shows every correction queued via <see cref="EnqueueCorrection"/> during the scan, one modal
        /// dialog after another, then invokes each apply callback. Must be called on the scan thread
        /// once all image-processor workers have drained (so the apply callbacks' inventory mutations
        /// don't race the workers) and before per-character assignment runs (so a name correction that
        /// rescues an equipped item still gets assigned). No-op if nothing was queued. Cancellation
        /// stops presenting remaining corrections and releases an active wait without force-closing
        /// its dialog.
        /// </summary>
        void FlushDeferredCorrections(CancellationToken cancellationToken);

        /// <summary>
        /// Blocks the calling thread while any inline correction requested via
        /// <see cref="RequestCorrection"/> is awaiting user input. Scan loops that queue capture work
        /// onto background workers instead of processing it inline (<c>ArtifactScraper</c>/
        /// <c>WeaponScraper</c>'s <c>QueueScan</c>) must call this between items -- otherwise a
        /// worker blocking inside <see cref="RequestCorrection"/> has no effect on the loop that's
        /// still clicking/scrolling the game.
        /// </summary>
        /// <returns>
        /// <c>true</c> when correction work is complete; <c>false</c> when cancellation won the wait.
        /// </returns>
        bool WaitIfCorrectionPending(CancellationToken cancellationToken);
    }
}

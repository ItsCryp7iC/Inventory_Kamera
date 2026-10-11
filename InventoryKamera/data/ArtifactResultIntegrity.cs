using System;

namespace InventoryKamera
{
    internal enum ArtifactCorrectionOutcome
    {
        Retained,
        Promoted,
        Invalidated,
        RemainedInvalid,
    }

    /// <summary>
    /// Keeps staged Artifact publication state synchronized with the mutable set-name correction.
    /// </summary>
    internal static class ArtifactResultIntegrity
    {
        internal static ArtifactCorrectionOutcome ApplySetNameCorrection(
            Artifact artifact,
            StagedInventoryResult<Artifact> stagedResult,
            string correctedSetName,
            out ArtifactValidationResult validation)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            if (stagedResult == null) throw new ArgumentNullException(nameof(stagedResult));
            if (!ReferenceEquals(artifact, stagedResult.Item))
                throw new ArgumentException("The staged result must own the corrected Artifact.", nameof(stagedResult));

            artifact.UpdateSetName(correctedSetName);
            if (IsPublishable(artifact, out validation))
            {
                return stagedResult.MarkPublishable()
                    ? ArtifactCorrectionOutcome.Promoted
                    : ArtifactCorrectionOutcome.Retained;
            }

            return stagedResult.MarkUnpublishable()
                ? ArtifactCorrectionOutcome.Invalidated
                : ArtifactCorrectionOutcome.RemainedInvalid;
        }

        internal static bool IsPublishable(
            Artifact artifact,
            out ArtifactValidationResult validation)
        {
            if (artifact == null)
            {
                validation = new ArtifactValidationResult(new[] { "invalid_artifact" });
                return false;
            }
            try
            {
                validation = artifact.Validate();
            }
            catch (InvalidOperationException)
            {
                validation = new ArtifactValidationResult(new[] { "missing_game_data" });
            }

            return validation.IsValid;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace InventoryKamera
{
    /// <summary>
    /// Immutable validation evidence for one parsed Artifact. Codes are diagnostic-only and do not
    /// alter the GOOD schema.
    /// </summary>
    internal sealed class ArtifactValidationResult
    {
        internal ArtifactValidationResult(IEnumerable<string> failureCodes)
        {
            if (failureCodes == null) throw new ArgumentNullException(nameof(failureCodes));
            FailureCodes = new ReadOnlyCollection<string>(failureCodes.Distinct().ToList());
        }

        internal bool IsValid => FailureCodes.Count == 0;
        internal IReadOnlyList<string> FailureCodes { get; }
        internal string Summary => IsValid ? "valid" : string.Join(",", FailureCodes);
    }
}

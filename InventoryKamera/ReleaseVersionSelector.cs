using System;
using System.Collections.Generic;

namespace InventoryKamera
{
    internal static class ReleaseVersionSelector
    {
        public static bool TrySelectFirst<T>(
            IEnumerable<T> candidates,
            Func<T, string> tagSelector,
            Action<string> malformedTagHandler,
            out T selectedCandidate,
            out ReleaseVersion selectedVersion)
        {
            foreach (var candidate in candidates)
            {
                var tag = tagSelector(candidate);
                if (ReleaseVersion.TryParse(tag, out selectedVersion))
                {
                    selectedCandidate = candidate;
                    return true;
                }

                malformedTagHandler(tag);
            }

            selectedCandidate = default;
            selectedVersion = null;
            return false;
        }
    }
}

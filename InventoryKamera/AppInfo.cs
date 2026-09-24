using System.Reflection;

namespace InventoryKamera
{
    /// <summary>
    /// Single source of the human-facing application version string, used for the window title and
    /// every log that records the running version. The numeric core comes from
    /// <see cref="AssemblyVersion"/>; the prerelease stage used by the update check is the one knob
    /// below.
    /// </summary>
    internal static class AppInfo
    {
        /// <summary>
        /// SemVer prerelease suffix for preview builds, e.g. "-alpha", "-alpha.2", "-preview.1".
        /// Set to "" for a stable release. This is the single line to edit when moving a preview along.
        /// </summary>
        private const string PreReleaseTag = "-alpha";

        /// <summary>e.g. "2.0.0-alpha" for a preview, "2.0.0" once <see cref="PreReleaseTag"/> is "".</summary>
        public static string Version =>
            Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + PreReleaseTag;
    }
}

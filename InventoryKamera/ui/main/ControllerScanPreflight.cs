using System;

namespace InventoryKamera
{
    /// <summary>
    /// Keeps the user-owned requirements prompt ahead of the operation that activates Genshin.
    /// Everything that creates a scan run or controller remains downstream of a successful result.
    /// </summary>
    internal static class ControllerScanPreflight
    {
        internal static bool TryProceed(
            bool controllerScanSelected,
            Func<bool> confirmRequirements,
            Func<bool> validateAndActivateGame)
        {
            if (confirmRequirements == null) throw new ArgumentNullException(nameof(confirmRequirements));
            if (validateAndActivateGame == null) throw new ArgumentNullException(nameof(validateAndActivateGame));

            if (controllerScanSelected && !confirmRequirements()) return false;
            return validateAndActivateGame();
        }
    }
}

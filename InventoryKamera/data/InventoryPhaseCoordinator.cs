using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Threading;

namespace InventoryKamera
{
    internal enum InventoryScanPhase
    {
        Weapons,
        Artifacts,
        CharacterDevelopmentItems,
        Materials,
    }

    internal readonly struct InventoryPhaseRequest
    {
        internal InventoryPhaseRequest(InventoryScanPhase phase, string targetTab)
        {
            Phase = phase;
            TargetTab = targetTab;
        }

        internal InventoryScanPhase Phase { get; }
        internal string TargetTab { get; }
    }

    internal static class InventoryPhasePlan
    {
        internal static IReadOnlyList<InventoryPhaseRequest> Create(
            bool scanWeapons,
            bool scanArtifacts,
            bool scanCharacterDevelopmentItems,
            bool scanMaterials)
        {
            var result = new List<InventoryPhaseRequest>(4);
            if (scanWeapons)
                result.Add(new InventoryPhaseRequest(InventoryScanPhase.Weapons, "Weapons"));
            if (scanArtifacts)
                result.Add(new InventoryPhaseRequest(InventoryScanPhase.Artifacts, "Artifacts"));
            if (scanCharacterDevelopmentItems)
                result.Add(new InventoryPhaseRequest(
                    InventoryScanPhase.CharacterDevelopmentItems,
                    "Character Development Items"));
            if (scanMaterials)
                result.Add(new InventoryPhaseRequest(InventoryScanPhase.Materials, "Materials"));
            return result;
        }
    }

    /// <summary>
    /// Gates every inventory scan phase on a successful visual tab transition. The coordinator owns
    /// no controller or screen state; it only guarantees that an unverified phase is never invoked.
    /// </summary>
    internal sealed class InventoryPhaseCoordinator
    {
        private readonly Func<string, InventoryTabNavigationResult> transition;
        private readonly Action<InventoryPhaseRequest> runPhase;
        private readonly Action<InventoryPhaseRequest, InventoryTabNavigationResult> transitionFailed;
        private readonly CancellationToken cancellationToken;

        internal InventoryPhaseCoordinator(
            Func<string, InventoryTabNavigationResult> transition,
            Action<InventoryPhaseRequest> runPhase,
            Action<InventoryPhaseRequest, InventoryTabNavigationResult> transitionFailed,
            CancellationToken cancellationToken)
        {
            this.transition = transition ?? throw new ArgumentNullException(nameof(transition));
            this.runPhase = runPhase ?? throw new ArgumentNullException(nameof(runPhase));
            this.transitionFailed = transitionFailed ?? throw new ArgumentNullException(nameof(transitionFailed));
            this.cancellationToken = cancellationToken;
        }

        internal bool Run(IReadOnlyList<InventoryPhaseRequest> phases)
        {
            if (phases == null) throw new ArgumentNullException(nameof(phases));

            foreach (InventoryPhaseRequest phase in phases)
            {
                if (cancellationToken.IsCancellationRequested) return false;

                using InventoryTabNavigationResult result = transition(phase.TargetTab);
                if (!result.Success)
                {
                    transitionFailed(phase, result);
                    return false;
                }

                if (cancellationToken.IsCancellationRequested) return false;
                runPhase(phase);
            }

            return !cancellationToken.IsCancellationRequested;
        }
    }
}

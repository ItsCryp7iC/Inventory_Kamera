using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace InventoryKamera.Tests
{
    public class InventoryPhaseCoordinatorTests
    {
        public static IEnumerable<object[]> CategoryCombinations()
        {
            yield return Case(true, false, false, false, "Weapons");
            yield return Case(false, true, false, false, "Artifacts");
            yield return Case(false, false, true, false, "Character Development Items");
            yield return Case(false, false, false, true, "Materials");
            yield return Case(true, true, false, false, "Weapons", "Artifacts");
            yield return Case(true, false, false, true, "Weapons", "Materials");
            yield return Case(false, true, false, true, "Artifacts", "Materials");
            yield return Case(true, true, true, true,
                "Weapons", "Artifacts", "Character Development Items", "Materials");
        }

        [Theory]
        [MemberData(nameof(CategoryCombinations))]
        public void PlanAndCoordinator_RunOnlyEnabledVerifiedTargets(
            bool weapons,
            bool artifacts,
            bool developmentItems,
            bool materials,
            string[] expectedTargets)
        {
            IReadOnlyList<InventoryPhaseRequest> plan = InventoryPhasePlan.Create(
                weapons, artifacts, developmentItems, materials);
            var transitions = new List<string>();
            var phases = new List<string>();
            var coordinator = new InventoryPhaseCoordinator(
                target =>
                {
                    transitions.Add(target);
                    return InventoryTabNavigationResult.Succeeded(
                        InventoryTabNavigationStatus.AlreadyAtTarget,
                        target,
                        target,
                        0);
                },
                phase => phases.Add(phase.TargetTab),
                (_, _) => throw new InvalidOperationException("No transition should fail."),
                CancellationToken.None);

            bool completed = coordinator.Run(plan);

            Assert.True(completed);
            Assert.Equal(expectedTargets, plan.Select(phase => phase.TargetTab));
            Assert.Equal(expectedTargets, transitions);
            Assert.Equal(expectedTargets, phases);
        }

        [Fact]
        public void FailedTransition_DoesNotStartRequestedOrLaterPhase()
        {
            IReadOnlyList<InventoryPhaseRequest> plan = InventoryPhasePlan.Create(
                scanWeapons: true,
                scanArtifacts: true,
                scanCharacterDevelopmentItems: false,
                scanMaterials: true);
            var transitions = new List<string>();
            var phases = new List<string>();
            var failures = new List<string>();
            var coordinator = new InventoryPhaseCoordinator(
                target =>
                {
                    transitions.Add(target);
                    return target == "Artifacts"
                        ? InventoryTabNavigationResult.Failed(
                            InventoryTabNavigationStatus.FailedToReachTarget,
                            "not verified",
                            "Weapons",
                            target,
                            "Weapons",
                            1,
                            null,
                            null)
                        : InventoryTabNavigationResult.Succeeded(
                            InventoryTabNavigationStatus.AlreadyAtTarget,
                            target,
                            target,
                            0);
                },
                phase => phases.Add(phase.TargetTab),
                (phase, _) => failures.Add(phase.TargetTab),
                CancellationToken.None);

            bool completed = coordinator.Run(plan);

            Assert.False(completed);
            Assert.Equal(new[] { "Weapons", "Artifacts" }, transitions);
            Assert.Equal(new[] { "Weapons" }, phases);
            Assert.Equal(new[] { "Artifacts" }, failures);
            Assert.DoesNotContain("Materials", transitions);
        }

        [Fact]
        public void PhaseLocalItemCountFailure_DoesNotPreventNextEnabledTransition()
        {
            IReadOnlyList<InventoryPhaseRequest> plan = InventoryPhasePlan.Create(
                scanWeapons: false,
                scanArtifacts: true,
                scanCharacterDevelopmentItems: false,
                scanMaterials: true);
            var transitions = new List<string>();
            var phases = new List<string>();
            var coordinator = new InventoryPhaseCoordinator(
                target =>
                {
                    transitions.Add(target);
                    return InventoryTabNavigationResult.Succeeded(
                        InventoryTabNavigationStatus.AlreadyAtTarget,
                        target,
                        target,
                        0);
                },
                phase =>
                {
                    phases.Add(phase.TargetTab);
                    // GameScanner contains the item-count FormatException inside this callback.
                    // Returning models the failed category being skipped without failing navigation.
                },
                (_, _) => throw new InvalidOperationException("No transition should fail."),
                CancellationToken.None);

            bool completed = coordinator.Run(plan);

            Assert.True(completed);
            Assert.Equal(new[] { "Artifacts", "Materials" }, transitions);
            Assert.Equal(new[] { "Artifacts", "Materials" }, phases);
        }

        private static object[] Case(
            bool weapons,
            bool artifacts,
            bool developmentItems,
            bool materials,
            params string[] targets) =>
            new object[] { weapons, artifacts, developmentItems, materials, targets };
    }
}

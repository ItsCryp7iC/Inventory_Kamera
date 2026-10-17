using InventoryKamera.game;
using System.Collections.Generic;
using System.Drawing;
using Xunit;

namespace InventoryKamera.Tests
{
    public class PaimonMenuPathPlannerTests
    {
        private readonly PaimonMenuPathPlanner planner = new PaimonMenuPathPlanner();

        [Fact]
        public void Plan_SameRow()
        {
            PaimonMenuTile current = Tile("Current", 0, 0);
            PaimonMenuTile target = Tile("Inventory", 130, 0);

            PaimonMenuPathPlan plan = planner.Plan(new[] { current, target }, current, target);

            Assert.True(plan.Success);
            Assert.Equal(new[] { GameNavigator.MenuDirection.Right }, plan.Steps);
        }

        [Fact]
        public void Plan_SameColumn()
        {
            PaimonMenuTile current = Tile("Current", 0, 0);
            PaimonMenuTile target = Tile("Inventory", 0, 118);

            PaimonMenuPathPlan plan = planner.Plan(new[] { current, target }, current, target);

            Assert.True(plan.Success);
            Assert.Equal(new[] { GameNavigator.MenuDirection.Down }, plan.Steps);
        }

        [Fact]
        public void Plan_MultiStepRoute()
        {
            PaimonMenuTile current = Tile("Current", 0, 0);
            PaimonMenuTile right = Tile("Right", 130, 0);
            PaimonMenuTile target = Tile("Inventory", 130, 118);

            PaimonMenuPathPlan plan = planner.Plan(new[] { current, right, target }, current, target);

            Assert.True(plan.Success);
            Assert.Equal(new[]
            {
                GameNavigator.MenuDirection.Right,
                GameNavigator.MenuDirection.Down,
            }, plan.Steps);
        }

        [Fact]
        public void Plan_InsertedTileAddsARequiredStep()
        {
            PaimonMenuTile current = Tile("Current", 0, 0);
            PaimonMenuTile inserted = Tile("New Feature", 0, 118);
            PaimonMenuTile target = Tile("Inventory", 0, 236);

            PaimonMenuPathPlan plan = planner.Plan(new[] { current, inserted, target }, current, target);

            Assert.True(plan.Success);
            Assert.Equal(new[]
            {
                GameNavigator.MenuDirection.Down,
                GameNavigator.MenuDirection.Down,
            }, plan.Steps);
        }

        [Fact]
        public void Plan_DoesNotDependOnDetectionCollectionOrder()
        {
            PaimonMenuTile current = Tile("Current", 0, 0);
            PaimonMenuTile right = Tile("Right", 130, 0);
            PaimonMenuTile target = Tile("Inventory", 130, 118);
            var reordered = new List<PaimonMenuTile> { target, current, right };

            PaimonMenuPathPlan plan = planner.Plan(reordered, current, target);

            Assert.True(plan.Success);
            Assert.Equal(new[]
            {
                GameNavigator.MenuDirection.Right,
                GameNavigator.MenuDirection.Down,
            }, plan.Steps);
        }

        [Fact]
        public void Plan_NoDirectionalNeighborFails()
        {
            PaimonMenuTile current = Tile("Current", 0, 0);
            PaimonMenuTile diagonalTarget = Tile("Inventory", 130, 118);

            PaimonMenuPathPlan plan = planner.Plan(new[] { current, diagonalTarget }, current, diagonalTarget);

            Assert.False(plan.Success);
            Assert.Empty(plan.Steps);
        }

        [Fact]
        public void Plan_TargetAlreadySelectedReturnsNoSteps()
        {
            PaimonMenuTile target = Tile("Inventory", 0, 0);

            PaimonMenuPathPlan plan = planner.Plan(new[] { target }, target, target);

            Assert.True(plan.Success);
            Assert.True(plan.AlreadyAtTarget);
            Assert.Empty(plan.Steps);
        }

        private static PaimonMenuTile Tile(string label, int x, int y) =>
            new PaimonMenuTile(label, Rectangle.Empty, new Rectangle(x, y, 120, 108), 1f);
    }
}

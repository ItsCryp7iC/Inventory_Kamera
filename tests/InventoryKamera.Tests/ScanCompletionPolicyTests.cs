using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class ScanCompletionPolicyTests
    {
        [Fact]
        public void CompletedDataWithPendingRecovery_IsExportedWithoutCompetingCompletionUi()
        {
            ScanCompletionPlan plan = ScanCompletionPolicy.Evaluate(
                cancelled: false,
                recoveryPending: true);

            Assert.True(plan.ExportGood);
            Assert.False(plan.ShowCompletedOverlay);
            Assert.False(plan.OpenOptimizer);
            Assert.Equal("Keyboard/Mouse restoration required", plan.Status);
            Assert.False(plan.StatusOk);
        }

        [Fact]
        public void CancelledScanWithPendingRecovery_DoesNotChangeCancellationExportSemantics()
        {
            ScanCompletionPlan plan = ScanCompletionPolicy.Evaluate(
                cancelled: true,
                recoveryPending: true);

            Assert.False(plan.ExportGood);
            Assert.False(plan.ShowCompletedOverlay);
            Assert.False(plan.OpenOptimizer);
            Assert.Equal("Keyboard/Mouse restoration required", plan.Status);
        }

        [Fact]
        public void NormalCompletion_ExportsAndShowsExactlyOneCompletionPath()
        {
            ScanCompletionPlan plan = ScanCompletionPolicy.Evaluate(
                cancelled: false,
                recoveryPending: false);

            Assert.True(plan.ExportGood);
            Assert.True(plan.ShowCompletedOverlay);
            Assert.True(plan.OpenOptimizer);
            Assert.Equal("Finished", plan.Status);
        }
    }
}

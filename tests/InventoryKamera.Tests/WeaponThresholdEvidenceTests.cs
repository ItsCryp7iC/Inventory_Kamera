using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace InventoryKamera.Tests
{
	public class WeaponThresholdEvidenceTests
	{
		[Fact]
		public void ConfirmedQuality_FirstBelowAndSecondBelow_Stops()
		{
			int confirmations = 0;
			WeaponThresholdDecision decision = EvaluateQuality(
				Observation(4, "90/90"),
				() => { confirmations++; return Observation(3, "90/90"); });

			Assert.True(decision.ShouldStop);
			Assert.True(decision.ShouldDiscard);
			Assert.Equal(1, confirmations);
			Assert.Equal(WeaponThresholdDecisionReason.ConfirmedBelowThreshold, decision.Reason);
		}

		[Fact]
		public void ConfirmedQuality_FirstBelowAndSecondAbove_ContinuesAndQueuesCurrent()
		{
			WeaponThresholdDecision decision = EvaluateQuality(
				Observation(4, "90/90"),
				() => Observation(5, "90/90"));

			Assert.False(decision.ShouldStop);
			Assert.False(decision.ShouldDiscard);
			Assert.Equal(WeaponThresholdDecisionReason.ConfirmationConflictContinue, decision.Reason);
		}

		[Fact]
		public void ConfirmedQuality_FirstBelowAndSecondUnresolved_ContinuesAndQueuesCurrent()
		{
			WeaponThresholdDecision decision = EvaluateQuality(
				Observation(4, "90/90"),
				() => Observation(0, "90/90"));

			Assert.False(decision.ShouldStop);
			Assert.False(decision.ShouldDiscard);
			Assert.Equal(WeaponThresholdDecisionReason.ConfirmationUnresolvedContinue, decision.Reason);
		}

		[Fact]
		public void UnresolvedFirstEvidence_DoesNotConfirmOrStop()
		{
			int confirmations = 0;
			WeaponThresholdDecision decision = EvaluateQuality(
				Observation(0, "90/90"),
				() => { confirmations++; return Observation(4, "90/90"); });

			Assert.False(decision.ShouldStop);
			Assert.False(decision.ShouldDiscard);
			Assert.Equal(0, confirmations);
		}

		[Fact]
		public void UnresolvedFirstLevel_DoesNotConfirmOrStop()
		{
			int confirmations = 0;
			WeaponThresholdDecision decision = EvaluateLevel(
				Observation(5, "garbled"),
				() => { confirmations++; return Observation(5, "20/40"); });

			Assert.False(decision.ShouldStop);
			Assert.False(decision.ShouldDiscard);
			Assert.Equal(0, confirmations);
		}

		[Fact]
		public void AboveThreshold_DoesNotRequestConfirmation()
		{
			int confirmations = 0;
			WeaponThresholdDecision decision = EvaluateQuality(
				Observation(5, "90/90"),
				() => { confirmations++; return Observation(5, "90/90"); });

			Assert.False(decision.ShouldStop);
			Assert.False(decision.ShouldDiscard);
			Assert.False(decision.ConfirmationRequested);
			Assert.Equal(0, confirmations);
		}

		[Fact]
		public void UnverifiedSort_NeverStopsAndKeepsExistingPerItemFilter()
		{
			int confirmations = 0;
			WeaponThresholdDecision decision = WeaponThresholdEvidence.Evaluate(
				Observation(4, "90/90"),
				minimumRarity: 5,
				minimumLevel: 1,
				sortModeConfirmed: false,
				sortByLevel: false,
				readFreshConfirmation: () => { confirmations++; return Observation(4, "90/90"); },
				cancellationToken: CancellationToken.None);

			Assert.False(decision.ShouldStop);
			Assert.True(decision.ShouldDiscard);
			Assert.Equal(0, confirmations);
			Assert.Equal(WeaponThresholdDecisionReason.SortNotVerifiedContinue, decision.Reason);
		}

		[Fact]
		public void CancellationDuringConfirmation_IsReportedAsCancellation()
		{
			using var cancellation = new CancellationTokenSource();
			WeaponThresholdDecision decision = WeaponThresholdEvidence.Evaluate(
				Observation(4, "90/90"),
				minimumRarity: 5,
				minimumLevel: 1,
				sortModeConfirmed: true,
				sortByLevel: false,
				readFreshConfirmation: () =>
				{
					cancellation.Cancel();
					return Observation(4, "90/90");
				},
				cancellationToken: cancellation.Token);

			Assert.True(decision.IsCancelled);
			Assert.False(decision.ShouldStop);
			Assert.False(decision.ShouldDiscard);
		}

		[Fact]
		public void CancellationBeforeConfirmation_DoesNotInvokeFreshRead()
		{
			using var cancellation = new CancellationTokenSource();
			cancellation.Cancel();
			int confirmations = 0;

			WeaponThresholdDecision decision = WeaponThresholdEvidence.Evaluate(
				Observation(4, "90/90"),
				minimumRarity: 5,
				minimumLevel: 1,
				sortModeConfirmed: true,
				sortByLevel: false,
				readFreshConfirmation: () => { confirmations++; return Observation(4, "90/90"); },
				cancellationToken: cancellation.Token);

			Assert.True(decision.IsCancelled);
			Assert.Equal(0, confirmations);
		}

		[Fact]
		public void ConfirmedLevel_RequiresExactPairAgreement()
		{
			WeaponThresholdDecision confirmed = EvaluateLevel(
				Observation(5, "20/40"),
				() => Observation(5, "20/40"));
			WeaponThresholdDecision conflicting = EvaluateLevel(
				Observation(5, "20/40"),
				() => Observation(5, "10/20"));

			Assert.True(confirmed.ShouldStop);
			Assert.False(conflicting.ShouldStop);
			Assert.False(conflicting.ShouldDiscard);
			Assert.Equal(WeaponThresholdDecisionReason.ConfirmationConflictContinue, conflicting.Reason);
		}

		[Fact]
		public void ConfirmedLevel_UnresolvedConfirmationContinuesAndQueuesCurrent()
		{
			WeaponThresholdDecision decision = EvaluateLevel(
				Observation(5, "20/40"),
				() => Observation(5, "noise"));

			Assert.False(decision.ShouldStop);
			Assert.False(decision.ShouldDiscard);
			Assert.Equal(WeaponThresholdDecisionReason.ConfirmationUnresolvedContinue, decision.Reason);
		}

		[Fact]
		public void FalseFirstLowRead_DoesNotOmitCurrentOrLaterValidWeapons()
		{
			var queued = new List<int>();
			WeaponThresholdDecision first = EvaluateQuality(
				Observation(4, "90/90"),
				() => Observation(5, "90/90"));
			if (!first.ShouldDiscard) queued.Add(0);

			WeaponThresholdDecision second = EvaluateQuality(
				Observation(5, "90/90"),
				() => throw new InvalidOperationException("Above-threshold evidence must not retry."));
			if (!first.ShouldStop && !second.ShouldDiscard) queued.Add(1);

			Assert.Equal(new[] { 0, 1 }, queued);
		}

		private static WeaponThresholdDecision EvaluateQuality(
			WeaponThresholdObservation first,
			Func<WeaponThresholdObservation> confirmation) =>
			WeaponThresholdEvidence.Evaluate(
				first,
				minimumRarity: 5,
				minimumLevel: 1,
				sortModeConfirmed: true,
				sortByLevel: false,
				confirmation,
				CancellationToken.None);

		private static WeaponThresholdDecision EvaluateLevel(
			WeaponThresholdObservation first,
			Func<WeaponThresholdObservation> confirmation) =>
			WeaponThresholdEvidence.Evaluate(
				first,
				minimumRarity: 1,
				minimumLevel: 30,
				sortModeConfirmed: true,
				sortByLevel: true,
				confirmation,
				CancellationToken.None);

		private static WeaponThresholdObservation Observation(int rarity, string level) =>
			new WeaponThresholdObservation(rarity, WeaponLevelParser.Parse(level));
	}
}

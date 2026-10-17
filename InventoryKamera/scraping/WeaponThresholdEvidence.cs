using System;
using System.Threading;

namespace InventoryKamera
{
	internal enum WeaponThresholdDecisionReason
	{
		AboveThreshold,
		CurrentItemBelowThreshold,
		SortNotVerifiedContinue,
		ConfirmedBelowThreshold,
		ConfirmationConflictContinue,
		ConfirmationUnresolvedContinue,
		Cancelled,
	}

	internal sealed class WeaponThresholdObservation
	{
		internal WeaponThresholdObservation(int rarity, WeaponLevelParseResult level)
		{
			Rarity = 1 <= rarity && rarity <= 5 ? rarity : (int?)null;
			Level = level ?? throw new ArgumentNullException(nameof(level));
		}

		internal int? Rarity { get; }
		internal WeaponLevelParseResult Level { get; }
	}

	internal sealed class WeaponThresholdDecision
	{
		internal WeaponThresholdDecision(
			bool shouldStop,
			bool shouldDiscard,
			bool confirmationRequested,
			WeaponThresholdDecisionReason reason,
			WeaponThresholdObservation first,
			WeaponThresholdObservation confirmation)
		{
			ShouldStop = shouldStop;
			ShouldDiscard = shouldDiscard;
			ConfirmationRequested = confirmationRequested;
			Reason = reason;
			First = first;
			Confirmation = confirmation;
		}

		internal bool ShouldStop { get; }
		internal bool ShouldDiscard { get; }
		internal bool ConfirmationRequested { get; }
		internal bool IsCancelled => Reason == WeaponThresholdDecisionReason.Cancelled;
		internal WeaponThresholdDecisionReason Reason { get; }
		internal string ReasonCode => Reason switch
		{
			WeaponThresholdDecisionReason.AboveThreshold => "above_threshold",
			WeaponThresholdDecisionReason.CurrentItemBelowThreshold => "current_item_below_threshold",
			WeaponThresholdDecisionReason.SortNotVerifiedContinue => "sort_not_verified_continue",
			WeaponThresholdDecisionReason.ConfirmedBelowThreshold => "confirmed_below_threshold",
			WeaponThresholdDecisionReason.ConfirmationConflictContinue => "confirmation_conflict_continue",
			WeaponThresholdDecisionReason.ConfirmationUnresolvedContinue => "confirmation_unresolved_continue",
			WeaponThresholdDecisionReason.Cancelled => "cancelled",
			_ => "unknown",
		};
		internal WeaponThresholdObservation First { get; }
		internal WeaponThresholdObservation Confirmation { get; }
	}

	/// <summary>
	/// Separates ordinary per-item filtering from the stronger evidence required to stop a sorted
	/// scan. The confirmation delegate represents one new capture of the still-selected weapon.
	/// </summary>
	internal static class WeaponThresholdEvidence
	{
		internal static WeaponThresholdDecision Evaluate(
			WeaponThresholdObservation first,
			decimal minimumRarity,
			decimal minimumLevel,
			bool sortModeConfirmed,
			bool sortByLevel,
			Func<WeaponThresholdObservation> readFreshConfirmation,
			CancellationToken cancellationToken)
		{
			if (first == null) throw new ArgumentNullException(nameof(first));
			if (readFreshConfirmation == null) throw new ArgumentNullException(nameof(readFreshConfirmation));

			bool belowRarity = first.Rarity.HasValue && first.Rarity.Value < minimumRarity;
			bool belowLevel = first.Level.Success && first.Level.Level.Value < minimumLevel;
			bool activeSortBelow = sortByLevel ? belowLevel : belowRarity;

			if (!sortModeConfirmed || !activeSortBelow)
			{
				WeaponThresholdDecisionReason reason = !sortModeConfirmed && activeSortBelow
					? WeaponThresholdDecisionReason.SortNotVerifiedContinue
					: (belowRarity || belowLevel
						? WeaponThresholdDecisionReason.CurrentItemBelowThreshold
						: WeaponThresholdDecisionReason.AboveThreshold);
				return new WeaponThresholdDecision(
					false,
					belowRarity || belowLevel,
					false,
					reason,
					first,
					null);
			}

			WeaponThresholdObservation confirmation;
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				confirmation = readFreshConfirmation();
				cancellationToken.ThrowIfCancellationRequested();
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return new WeaponThresholdDecision(
					false, false, true, WeaponThresholdDecisionReason.Cancelled, first, null);
			}

			bool confirmationResolved;
			bool confirmedBelow;
			if (sortByLevel)
			{
				confirmationResolved = confirmation?.Level?.Success == true;
				confirmedBelow = confirmationResolved &&
					confirmation.Level.Level.Value < minimumLevel &&
					confirmation.Level.Level == first.Level.Level &&
					confirmation.Level.MaximumLevel == first.Level.MaximumLevel;
			}
			else
			{
				confirmationResolved = confirmation?.Rarity.HasValue == true;
				confirmedBelow = confirmationResolved && confirmation.Rarity.Value < minimumRarity;
			}

			if (confirmedBelow)
			{
				return new WeaponThresholdDecision(
					true, true, true, WeaponThresholdDecisionReason.ConfirmedBelowThreshold,
					first, confirmation);
			}

			return new WeaponThresholdDecision(
				false,
				false,
				true,
				confirmationResolved
					? WeaponThresholdDecisionReason.ConfirmationConflictContinue
					: WeaponThresholdDecisionReason.ConfirmationUnresolvedContinue,
				first,
				confirmation);
		}
	}
}

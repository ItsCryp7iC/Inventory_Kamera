using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace InventoryKamera
{
	internal enum MaterialQuantityReadStatus
	{
		Resolved,
		Unresolved,
		Cancelled,
	}

	internal enum MaterialQuantityDecisionReason
	{
		NoValidCandidates,
		SoleValidCandidate,
		UniqueMajority,
		ConflictingCandidates,
		Cancelled,
	}

	internal enum MaterialQuantityFinalDecisionReason
	{
		PrimaryResolved,
		FallbackNoPrimaryCandidates,
		FallbackConfirmedPrimaryCandidate,
		FallbackConflict,
		FallbackUnreadable,
		Cancelled,
	}

	internal sealed class MaterialQuantityObservation
	{
		internal MaterialQuantityObservation(
			int attempt,
			string rawText,
			string normalizedText,
			int? quantity,
			int candidateFrequency)
		{
			Attempt = attempt;
			RawText = rawText ?? string.Empty;
			NormalizedText = normalizedText ?? string.Empty;
			Quantity = quantity;
			CandidateFrequency = candidateFrequency;
		}

		internal int Attempt { get; }
		internal string RawText { get; }
		internal string NormalizedText { get; }
		internal int? Quantity { get; }
		internal int CandidateFrequency { get; }
		internal bool IsValid => Quantity.HasValue;
	}

	internal sealed class MaterialQuantityReadResult
	{
		internal MaterialQuantityReadResult(
			MaterialQuantityReadStatus status,
			int? quantity,
			MaterialQuantityDecisionReason decisionReason,
			IEnumerable<MaterialQuantityObservation> observations,
			IDictionary<int, int> candidateCounts)
		{
			Status = status;
			Quantity = quantity;
			DecisionReason = decisionReason;
			Observations = Array.AsReadOnly((observations ?? Enumerable.Empty<MaterialQuantityObservation>()).ToArray());
			CandidateCounts = new ReadOnlyDictionary<int, int>(
				new Dictionary<int, int>(candidateCounts ?? new Dictionary<int, int>()));
		}

		internal MaterialQuantityReadStatus Status { get; }
		internal int? Quantity { get; }
		internal MaterialQuantityDecisionReason DecisionReason { get; }
		internal string DecisionReasonCode => DecisionReason switch
		{
			MaterialQuantityDecisionReason.NoValidCandidates => "no_valid_candidates",
			MaterialQuantityDecisionReason.SoleValidCandidate => "sole_valid_candidate",
			MaterialQuantityDecisionReason.UniqueMajority => "unique_majority",
			MaterialQuantityDecisionReason.ConflictingCandidates => "conflicting_candidates",
			MaterialQuantityDecisionReason.Cancelled => "cancelled",
			_ => throw new InvalidOperationException($"Unknown material quantity decision reason: {DecisionReason}"),
		};
		internal IReadOnlyList<MaterialQuantityObservation> Observations { get; }
		internal IReadOnlyDictionary<int, int> CandidateCounts { get; }
	}

	internal sealed class MaterialQuantityFallbackObservation
	{
		private MaterialQuantityFallbackObservation(
			string preprocessingMode,
			string rawText,
			string normalizedText,
			int? quantity,
			float confidence)
		{
			PreprocessingMode = preprocessingMode ?? string.Empty;
			RawText = rawText ?? string.Empty;
			NormalizedText = normalizedText ?? string.Empty;
			Quantity = quantity;
			Confidence = confidence;
		}

		internal string PreprocessingMode { get; }
		internal string RawText { get; }
		internal string NormalizedText { get; }
		internal int? Quantity { get; }
		internal float Confidence { get; }

		internal static MaterialQuantityFallbackObservation Create(
			string preprocessingMode,
			string rawText,
			float confidence)
		{
			string normalized = MaterialQuantityConsensus.Normalize(rawText, repairExactM: false);
			return new MaterialQuantityFallbackObservation(
				preprocessingMode,
				rawText,
				normalized,
				MaterialQuantityConsensus.ParsePositiveQuantity(normalized),
				confidence);
		}
	}

	internal sealed class MaterialQuantityResolutionResult
	{
		internal MaterialQuantityResolutionResult(
			MaterialQuantityReadStatus status,
			int? quantity,
			MaterialQuantityFinalDecisionReason decisionReason,
			MaterialQuantityReadResult primary,
			MaterialQuantityFallbackObservation fallback)
		{
			Status = status;
			Quantity = quantity;
			DecisionReason = decisionReason;
			Primary = primary ?? throw new ArgumentNullException(nameof(primary));
			Fallback = fallback;
		}

		internal MaterialQuantityReadStatus Status { get; }
		internal int? Quantity { get; }
		internal MaterialQuantityFinalDecisionReason DecisionReason { get; }
		internal MaterialQuantityReadResult Primary { get; }
		internal MaterialQuantityFallbackObservation Fallback { get; }
		internal string DecisionReasonCode => DecisionReason switch
		{
			MaterialQuantityFinalDecisionReason.PrimaryResolved => "primary_resolved",
			MaterialQuantityFinalDecisionReason.FallbackNoPrimaryCandidates => "fallback_no_primary_candidates",
			MaterialQuantityFinalDecisionReason.FallbackConfirmedPrimaryCandidate => "fallback_confirmed_primary_candidate",
			MaterialQuantityFinalDecisionReason.FallbackConflict => "fallback_conflict",
			MaterialQuantityFinalDecisionReason.FallbackUnreadable => "fallback_unreadable",
			MaterialQuantityFinalDecisionReason.Cancelled => "cancelled",
			_ => throw new InvalidOperationException($"Unknown material quantity final decision reason: {DecisionReason}"),
		};
	}

	/// <summary>
	/// Parses and combines the five existing multi-scale OCR observations for one material quantity.
	/// It owns no images and does not capture the screen. The observations are alternate OCR scale
	/// variants of one capture, so one distinct positive candidate is sufficient; conflicting candidates
	/// require a repeated unique leader.
	/// </summary>
	internal sealed class MaterialQuantityConsensus
	{
		internal const int RequiredConflictingCandidateAgreement = 2;

		private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);
		private static readonly Regex DigitsOnly = new Regex(@"^[0-9]+$", RegexOptions.Compiled);
		private readonly Dictionary<int, int> counts = new Dictionary<int, int>();
		private readonly List<MaterialQuantityObservation> observations = new List<MaterialQuantityObservation>();

		internal MaterialQuantityObservation Observe(int attempt, string rawText)
		{
			string normalized = Normalize(rawText);
			int? quantity = ParsePositiveQuantity(normalized);
			int candidateFrequency = 0;

			if (quantity.HasValue)
			{
				counts.TryGetValue(quantity.Value, out int count);
				candidateFrequency = count + 1;
				counts[quantity.Value] = candidateFrequency;
			}

			var observation = new MaterialQuantityObservation(
				attempt,
				rawText,
				normalized,
				quantity,
				candidateFrequency);
			observations.Add(observation);

			return observation;
		}

		internal MaterialQuantityReadResult Finish()
		{
			if (counts.Count == 0)
			{
				return BuildResult(
					MaterialQuantityReadStatus.Unresolved,
					null,
					MaterialQuantityDecisionReason.NoValidCandidates);
			}

			if (counts.Count == 1)
			{
				return BuildResult(
					MaterialQuantityReadStatus.Resolved,
					counts.Keys.Single(),
					MaterialQuantityDecisionReason.SoleValidCandidate);
			}

			if (TryGetUniqueLeader(out KeyValuePair<int, int> leader) &&
				leader.Value >= RequiredConflictingCandidateAgreement)
			{
				// Key is the parsed quantity. Value is only its observation frequency.
				return BuildResult(
					MaterialQuantityReadStatus.Resolved,
					leader.Key,
					MaterialQuantityDecisionReason.UniqueMajority);
			}

			return BuildResult(
				MaterialQuantityReadStatus.Unresolved,
				null,
				MaterialQuantityDecisionReason.ConflictingCandidates);
		}

		internal MaterialQuantityReadResult Cancelled() =>
			BuildResult(
				MaterialQuantityReadStatus.Cancelled,
				null,
				MaterialQuantityDecisionReason.Cancelled);

		private MaterialQuantityReadResult BuildResult(
			MaterialQuantityReadStatus status,
			int? quantity,
			MaterialQuantityDecisionReason decisionReason) =>
			new MaterialQuantityReadResult(status, quantity, decisionReason, observations, counts);

		private bool TryGetUniqueLeader(out KeyValuePair<int, int> leader)
		{
			leader = default;
			if (counts.Count == 0) return false;

			int maximumFrequency = counts.Values.Max();
			KeyValuePair<int, int>[] leaders = counts
				.Where(candidate => candidate.Value == maximumFrequency)
				.ToArray();
			if (leaders.Length != 1) return false;

			leader = leaders[0];
			return true;
		}

		internal static string Normalize(string rawText, bool repairExactM = true)
		{
			string normalized = Whitespace.Replace((rawText ?? string.Empty).Trim(), string.Empty);
			// Preserve the existing, narrowly-scoped OCR repair for three adjacent ones.
			return repairExactM && string.Equals(normalized, "M", StringComparison.Ordinal) ? "111" : normalized;
		}

		internal static int? ParsePositiveQuantity(string normalized)
		{
			if (string.IsNullOrEmpty(normalized) || !DigitsOnly.IsMatch(normalized)) return null;
			if (!int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out int quantity))
				return null;
			return quantity > 0 ? quantity : (int?)null;
		}
	}

	internal static class MaterialQuantityResolver
	{
		internal static MaterialQuantityResolutionResult Resolve(
			MaterialQuantityReadResult primary,
			Func<MaterialQuantityFallbackObservation> readFallback,
			CancellationToken cancellationToken)
		{
			if (primary == null) throw new ArgumentNullException(nameof(primary));
			if (readFallback == null) throw new ArgumentNullException(nameof(readFallback));

			if (primary.Status == MaterialQuantityReadStatus.Cancelled ||
				cancellationToken.IsCancellationRequested)
			{
				return Cancelled(primary, null);
			}

			if (primary.Status == MaterialQuantityReadStatus.Resolved)
			{
				return new MaterialQuantityResolutionResult(
					MaterialQuantityReadStatus.Resolved,
					primary.Quantity,
					MaterialQuantityFinalDecisionReason.PrimaryResolved,
					primary,
					null);
			}

			MaterialQuantityFallbackObservation fallback;
			try
			{
				fallback = readFallback();
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return Cancelled(primary, null);
			}

			if (cancellationToken.IsCancellationRequested)
				return Cancelled(primary, fallback);

			if (fallback?.Quantity == null)
			{
				return new MaterialQuantityResolutionResult(
					MaterialQuantityReadStatus.Unresolved,
					null,
					MaterialQuantityFinalDecisionReason.FallbackUnreadable,
					primary,
					fallback);
			}

			int fallbackQuantity = fallback.Quantity.Value;
			if (primary.CandidateCounts.Count == 0)
			{
				return new MaterialQuantityResolutionResult(
					MaterialQuantityReadStatus.Resolved,
					fallbackQuantity,
					MaterialQuantityFinalDecisionReason.FallbackNoPrimaryCandidates,
					primary,
					fallback);
			}

			if (primary.CandidateCounts.ContainsKey(fallbackQuantity))
			{
				return new MaterialQuantityResolutionResult(
					MaterialQuantityReadStatus.Resolved,
					fallbackQuantity,
					MaterialQuantityFinalDecisionReason.FallbackConfirmedPrimaryCandidate,
					primary,
					fallback);
			}

			return new MaterialQuantityResolutionResult(
				MaterialQuantityReadStatus.Unresolved,
				null,
				MaterialQuantityFinalDecisionReason.FallbackConflict,
				primary,
				fallback);
		}

		private static MaterialQuantityResolutionResult Cancelled(
			MaterialQuantityReadResult primary,
			MaterialQuantityFallbackObservation fallback) =>
			new MaterialQuantityResolutionResult(
				MaterialQuantityReadStatus.Cancelled,
				null,
				MaterialQuantityFinalDecisionReason.Cancelled,
				primary,
				fallback);
	}

	internal static class MaterialQuantityReader
	{
		internal const int MaximumAttempts = 5;

		internal static MaterialQuantityReadResult Read(
			Func<int, string> readObservation,
			CancellationToken cancellationToken)
		{
			if (readObservation == null) throw new ArgumentNullException(nameof(readObservation));

			var consensus = new MaterialQuantityConsensus();
			for (int attempt = 1; attempt <= MaximumAttempts; attempt++)
			{
				if (cancellationToken.IsCancellationRequested) return consensus.Cancelled();

				string rawText;
				try
				{
					rawText = readObservation(attempt);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					return consensus.Cancelled();
				}

				// Cancellation wins over accepting or recording an OCR result that completed concurrently.
				if (cancellationToken.IsCancellationRequested) return consensus.Cancelled();

				consensus.Observe(attempt, rawText);
			}

			return consensus.Finish();
		}
	}
}

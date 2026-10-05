using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace InventoryKamera
{
	internal readonly struct TalentLevelTriplet : IEquatable<TalentLevelTriplet>
	{
		internal TalentLevelTriplet(int auto, int skill, int burst)
		{
			Auto = auto;
			Skill = skill;
			Burst = burst;
		}

		internal int Auto { get; }
		internal int Skill { get; }
		internal int Burst { get; }

		internal Dictionary<string, int> ToDictionary() =>
			new Dictionary<string, int>
			{
				["auto"] = Auto,
				["skill"] = Skill,
				["burst"] = Burst,
			};

		public bool Equals(TalentLevelTriplet other) =>
			Auto == other.Auto && Skill == other.Skill && Burst == other.Burst;

		public override bool Equals(object obj) =>
			obj is TalentLevelTriplet other && Equals(other);

		public override int GetHashCode() => (Auto, Skill, Burst).GetHashCode();

		public override string ToString() => $"{Auto}/{Skill}/{Burst}";
	}

	internal sealed class TalentReadObservation
	{
		internal TalentReadObservation(
			string rawText,
			IReadOnlyList<int> parsedRows,
			TalentLevelTriplet? triplet,
			int candidateCount,
			string candidateSummary,
			bool accepted,
			string reason)
		{
			RawText = rawText ?? string.Empty;
			ParsedRows = parsedRows ?? throw new ArgumentNullException(nameof(parsedRows));
			Triplet = triplet;
			CandidateCount = candidateCount;
			CandidateSummary = candidateSummary ?? string.Empty;
			Accepted = accepted;
			Reason = reason ?? string.Empty;
		}

		internal string RawText { get; }
		internal IReadOnlyList<int> ParsedRows { get; }
		internal TalentLevelTriplet? Triplet { get; }
		internal int CandidateCount { get; }
		internal string CandidateSummary { get; }
		internal bool Accepted { get; }
		internal string Reason { get; }
		internal bool HasCompleteValidTriplet => Triplet.HasValue;
	}

	/// <summary>
	/// Collects complete talent reads without combining partial frames. A raw displayed triplet is
	/// accepted only after the exact same positional auto/skill/burst values have appeared on two
	/// independently supplied captures.
	/// </summary>
	internal sealed class TalentReadConsensus
	{
		internal const int MaximumAttempts = 20;
		internal const int RequiredAgreement = 2;

		private readonly Dictionary<TalentLevelTriplet, int> candidateCounts =
			new Dictionary<TalentLevelTriplet, int>();
		private readonly List<TalentLevelTriplet> candidateOrder =
			new List<TalentLevelTriplet>();

		internal TalentReadObservation Observe(string rawText)
		{
			IReadOnlyList<int> rows = ParseRows(rawText);
			TalentLevelTriplet? triplet = rows.Count >= 3
				? new TalentLevelTriplet(rows[0], rows[1], rows[2])
				: null;
			int count = 0;
			bool accepted = false;
			string reason;

			if (triplet.HasValue)
			{
				TalentLevelTriplet candidate = triplet.Value;
				if (!candidateCounts.ContainsKey(candidate))
				{
					candidateCounts[candidate] = 0;
					candidateOrder.Add(candidate);
				}

				count = ++candidateCounts[candidate];
				accepted = count >= RequiredAgreement;
				reason = accepted
					? $"exact triplet agreed across {count} fresh captures"
					: "complete valid triplet observed once; waiting for exact agreement";
			}
			else if (rows.Count == 0)
			{
				reason = "no valid talent rows parsed";
			}
			else
			{
				reason = $"partial read contained {rows.Count}/3 valid talent rows";
			}

			return new TalentReadObservation(
				rawText,
				rows,
				triplet,
				count,
				CandidateSummary(),
				accepted,
				reason);
		}

		internal static bool TryRead(
			int maxAttempts,
			Func<int, string> readFreshCapture,
			Action<int, TalentReadObservation> observeAttempt,
			Action waitAfterRejectedAttempt,
			out TalentLevelTriplet acceptedTriplet)
		{
			return TryRead(
				maxAttempts,
				readFreshCapture,
				observeAttempt,
				waitAfterRejectedAttempt,
				CancellationToken.None,
				out acceptedTriplet);
		}

		internal static bool TryRead(
			int maxAttempts,
			Func<int, string> readFreshCapture,
			Action<int, TalentReadObservation> observeAttempt,
			Action waitAfterRejectedAttempt,
			CancellationToken cancellationToken,
			out TalentLevelTriplet acceptedTriplet)
		{
			if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
			if (readFreshCapture == null) throw new ArgumentNullException(nameof(readFreshCapture));

			var consensus = new TalentReadConsensus();
			for (int attempt = 1; attempt <= maxAttempts; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				string rawText = readFreshCapture(attempt);
				cancellationToken.ThrowIfCancellationRequested();
				TalentReadObservation observation = consensus.Observe(rawText);
				observeAttempt?.Invoke(attempt, observation);
				cancellationToken.ThrowIfCancellationRequested();
				if (observation.Accepted)
				{
					acceptedTriplet = observation.Triplet.Value;
					return true;
				}

				if (attempt < maxAttempts)
				{
					waitAfterRejectedAttempt?.Invoke();
					cancellationToken.ThrowIfCancellationRequested();
				}
			}

			acceptedTriplet = default;
			return false;
		}

		internal static IReadOnlyList<int> ParseRows(string rawText)
		{
			var levels = new List<int>();
			foreach (string line in (rawText ?? string.Empty).Split('\n'))
			{
				Match match = Regex.Match(line, @"[Ll][Vv]\.?\s*(\d+)");
				if (match.Success &&
					int.TryParse(match.Groups[1].Value, out int level) &&
					level >= 1 && level <= 15)
				{
					levels.Add(level);
				}
			}

			return Array.AsReadOnly(levels.ToArray());
		}

		private string CandidateSummary()
		{
			if (candidateOrder.Count == 0) return "none";
			return string.Join(", ", candidateOrder.Select(candidate =>
				$"{candidate} x{candidateCounts[candidate]}"));
		}
	}
}

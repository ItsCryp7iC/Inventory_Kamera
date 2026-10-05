using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace InventoryKamera
{
	internal enum ConstellationNodeState
	{
		Activated,
		Locked,
		Unresolved,
	}

	internal sealed class ConstellationOcrRead
	{
		internal ConstellationOcrRead(string source, string rawText, float confidence = 0f)
		{
			Source = source ?? string.Empty;
			RawText = rawText ?? string.Empty;
			NormalizedText = ConstellationNodeStateResolver.Normalize(rawText);
			Confidence = confidence;
		}

		internal string Source { get; }
		internal string RawText { get; }
		internal string NormalizedText { get; }
		internal float Confidence { get; }
	}

	internal sealed class ConstellationNodeObservation
	{
		internal ConstellationNodeObservation(
			IReadOnlyList<ConstellationOcrRead> activatedReads,
			ConstellationOcrRead lockedRead,
			bool activatedEvidence,
			bool lockedEvidence,
			ConstellationNodeState state,
			string reason)
		{
			ActivatedReads = activatedReads ?? throw new ArgumentNullException(nameof(activatedReads));
			LockedRead = lockedRead ?? throw new ArgumentNullException(nameof(lockedRead));
			ActivatedEvidence = activatedEvidence;
			LockedEvidence = lockedEvidence;
			State = state;
			Reason = reason ?? string.Empty;
		}

		internal IReadOnlyList<ConstellationOcrRead> ActivatedReads { get; }
		internal ConstellationOcrRead LockedRead { get; }
		internal bool ActivatedEvidence { get; }
		internal bool LockedEvidence { get; }
		internal ConstellationNodeState State { get; }
		internal string Reason { get; }
	}

	/// <summary>
	/// Combines two independent semantic signals from one captured frame. The compact status-label
	/// region may establish Activated through any deterministic preprocessing variant. The separate
	/// left-description region may establish Locked only through the exact phrase "Activate required".
	/// Absence is never treated as evidence, and conflicting positive signals remain unresolved.
	/// </summary>
	internal static class ConstellationNodeStateResolver
	{
		internal static ConstellationNodeObservation Resolve(
			IReadOnlyList<ConstellationOcrRead> activatedReads,
			ConstellationOcrRead lockedRead)
		{
			if (activatedReads == null) throw new ArgumentNullException(nameof(activatedReads));
			if (lockedRead == null) throw new ArgumentNullException(nameof(lockedRead));

			bool activatedEvidence = activatedReads.Any(
				read => ContainsWord(read?.NormalizedText, "activated"));
			bool lockedEvidence = ContainsPhrase(
				lockedRead.NormalizedText,
				"activate",
				"required");

			ConstellationNodeState state;
			string reason;
			if (activatedEvidence && lockedEvidence)
			{
				state = ConstellationNodeState.Unresolved;
				reason = "conflicting Activated and Activate required evidence";
			}
			else if (activatedEvidence)
			{
				state = ConstellationNodeState.Activated;
				reason = "exact Activated label recognized by an activated-region variant";
			}
			else if (lockedEvidence)
			{
				state = ConstellationNodeState.Locked;
				reason = "exact Activate required phrase recognized in the description region";
			}
			else
			{
				state = ConstellationNodeState.Unresolved;
				reason = "neither exact Activated nor exact Activate required evidence was recognized";
			}

			return new ConstellationNodeObservation(
				activatedReads,
				lockedRead,
				activatedEvidence,
				lockedEvidence,
				state,
				reason);
		}

		internal static string Normalize(string rawText)
		{
			string lettersAndSpaces = Regex.Replace(
				(rawText ?? string.Empty).ToLowerInvariant(),
				@"[^a-z]+",
				" ");
			return Regex.Replace(lettersAndSpaces, @"\s+", " ").Trim();
		}

		private static bool ContainsWord(string normalizedText, string expected)
		{
			return Words(normalizedText).Contains(expected, StringComparer.Ordinal);
		}

		private static bool ContainsPhrase(string normalizedText, params string[] expectedWords)
		{
			string[] words = Words(normalizedText);
			if (expectedWords == null || expectedWords.Length == 0 || words.Length < expectedWords.Length)
				return false;

			for (int start = 0; start <= words.Length - expectedWords.Length; start++)
			{
				bool matches = true;
				for (int offset = 0; offset < expectedWords.Length; offset++)
				{
					if (!string.Equals(words[start + offset], expectedWords[offset], StringComparison.Ordinal))
					{
						matches = false;
						break;
					}
				}
				if (matches) return true;
			}

			return false;
		}

		private static string[] Words(string normalizedText) =>
			(normalizedText ?? string.Empty).Split(
				new[] { ' ' },
				StringSplitOptions.RemoveEmptyEntries);
	}

	/// <summary>
	/// Normalized capture regions derived from the 1920x1080 live evidence. The Activated label has
	/// a compact fixed location. "Activate required" moves vertically with description length, so its
	/// region spans the relevant left-panel band without including the bottom controller prompt.
	/// </summary>
	internal static class ConstellationCaptureRegions
	{
		internal static Rectangle Activated(Size captureSize) => Create(
			captureSize,
			left: 0.1574,
			top: 0.8323,
			right: 0.2241,
			bottom: 0.8777);

		internal static Rectangle ActivatedText(Size captureSize) => Create(
			captureSize,
			// The live 1920x1080 label occupies x=319..415, y=912..928. Keep 6-7 px of
			// proportional padding while excluding the animation-heavy area surrounding it.
			left: 0.1625,
			top: 0.8389,
			right: 0.2198,
			bottom: 0.8667);

		internal static Rectangle LockedDescription(Size captureSize) => Create(
			captureSize,
			left: 0.0450,
			top: 0.3400,
			right: 0.3300,
			bottom: 0.5400);

		private static Rectangle Create(
			Size captureSize,
			double left,
			double top,
			double right,
			double bottom)
		{
			if (captureSize.Width <= 0 || captureSize.Height <= 0)
				throw new ArgumentOutOfRangeException(nameof(captureSize));

			int x = (int)(left * captureSize.Width);
			int y = (int)(top * captureSize.Height);
			int rightPixel = (int)(right * captureSize.Width);
			int bottomPixel = (int)(bottom * captureSize.Height);
			return Rectangle.FromLTRB(x, y, rightPixel, bottomPixel);
		}
	}

	/// <summary>
	/// Isolates the gold Activated label from the white/cyan constellation animation. Live Qiqi C2
	/// captures showed the label's dominant color was stable at RGB(255,204,50), while grayscale
	/// preprocessing promoted the crossing cyan rays into competing foreground strokes. The relative
	/// channel checks retain anti-aliased gold glyph pixels without admitting white or cyan bloom.
	/// </summary>
	internal static class ConstellationActivatedTextPreprocessor
	{
		internal const int OutputScale = 2;

		internal static Bitmap CreateGoldTextMask(Bitmap source)
		{
			if (source == null) throw new ArgumentNullException(nameof(source));

			var result = new Bitmap(
				source.Width * OutputScale,
				source.Height * OutputScale);
			for (int y = 0; y < source.Height; y++)
			{
				for (int x = 0; x < source.Width; x++)
				{
					Color output = IsLikelyActivatedGold(source.GetPixel(x, y))
						? Color.Black
						: Color.White;
					for (int offsetY = 0; offsetY < OutputScale; offsetY++)
					{
						for (int offsetX = 0; offsetX < OutputScale; offsetX++)
						{
							result.SetPixel(
								x * OutputScale + offsetX,
								y * OutputScale + offsetY,
								output);
						}
					}
				}
			}

			return result;
		}

		internal static bool IsLikelyActivatedGold(Color color) =>
			color.R >= 190 &&
			color.G >= 120 &&
			color.B <= 190 &&
			color.R - color.B >= 40 &&
			color.G - color.B >= 20 &&
			color.R >= color.G;
	}

	internal static class ConstellationNodeRetry
	{
		internal const int MaximumAttempts = 3;

		internal static bool TryResolve(
			int maxAttempts,
			Func<int, ConstellationNodeObservation> readFreshCapture,
			Action<int, ConstellationNodeObservation> observeAttempt,
			Action waitBeforeRetry,
			out ConstellationNodeObservation resolvedObservation)
		{
			return TryResolve(
				maxAttempts,
				readFreshCapture,
				observeAttempt,
				waitBeforeRetry,
				CancellationToken.None,
				out resolvedObservation);
		}

		internal static bool TryResolve(
			int maxAttempts,
			Func<int, ConstellationNodeObservation> readFreshCapture,
			Action<int, ConstellationNodeObservation> observeAttempt,
			Action waitBeforeRetry,
			CancellationToken cancellationToken,
			out ConstellationNodeObservation resolvedObservation)
		{
			if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
			if (readFreshCapture == null) throw new ArgumentNullException(nameof(readFreshCapture));

			ConstellationNodeObservation lastObservation = null;
			for (int attempt = 1; attempt <= maxAttempts; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				lastObservation = readFreshCapture(attempt) ??
					throw new InvalidOperationException("Constellation node reader returned no observation.");
				cancellationToken.ThrowIfCancellationRequested();
				observeAttempt?.Invoke(attempt, lastObservation);
				cancellationToken.ThrowIfCancellationRequested();

				if (lastObservation.State != ConstellationNodeState.Unresolved)
				{
					resolvedObservation = lastObservation;
					return true;
				}

				if (attempt < maxAttempts)
				{
					waitBeforeRetry?.Invoke();
					cancellationToken.ThrowIfCancellationRequested();
				}
			}

			resolvedObservation = lastObservation;
			return false;
		}
	}

	internal readonly struct ConstellationScanResult
	{
		private ConstellationScanResult(
			bool success,
			int constellation,
			int? unresolvedNode,
			string reason)
		{
			Success = success;
			Constellation = constellation;
			UnresolvedNode = unresolvedNode;
			Reason = reason ?? string.Empty;
		}

		internal bool Success { get; }
		internal int Constellation { get; }
		internal int? UnresolvedNode { get; }
		internal string Reason { get; }

		internal static ConstellationScanResult Resolved(int constellation) =>
			new ConstellationScanResult(true, constellation, null, "constellation sequence resolved");

		internal static ConstellationScanResult Failed(int? unresolvedNode, string reason) =>
			new ConstellationScanResult(false, -1, unresolvedNode, reason);
	}

	/// <summary>
	/// Pure sequence rules shared by the forward and four-star greedy navigation paths. An
	/// unresolved node never supplies a lower constellation guess.
	/// </summary>
	internal static class ConstellationSequenceEvaluator
	{
		internal const int NodeCount = 6;

		internal static ConstellationScanResult EvaluateForward(
			IReadOnlyList<ConstellationNodeState> states)
		{
			if (states == null) throw new ArgumentNullException(nameof(states));

			int inspected = Math.Min(states.Count, NodeCount);
			for (int index = 0; index < inspected; index++)
			{
				switch (states[index])
				{
					case ConstellationNodeState.Activated:
						break;
					case ConstellationNodeState.Locked:
						return ConstellationScanResult.Resolved(index);
					default:
						return ConstellationScanResult.Failed(
							index + 1,
							$"constellation node {index + 1} remained unresolved");
				}
			}

			return inspected == NodeCount
				? ConstellationScanResult.Resolved(NodeCount)
				: ConstellationScanResult.Failed(
					inspected + 1,
					"constellation sequence ended before a locked node or C6 was established");
		}

		internal static ConstellationScanResult EvaluateGreedy(
			IReadOnlyList<ConstellationNodeState> statesFromC6Down)
		{
			if (statesFromC6Down == null) throw new ArgumentNullException(nameof(statesFromC6Down));

			int inspected = Math.Min(statesFromC6Down.Count, NodeCount);
			for (int index = 0; index < inspected; index++)
			{
				int node = NodeCount - index;
				switch (statesFromC6Down[index])
				{
					case ConstellationNodeState.Activated:
						return ConstellationScanResult.Resolved(node);
					case ConstellationNodeState.Locked:
						break;
					default:
						return ConstellationScanResult.Failed(
							node,
							$"constellation node {node} remained unresolved");
				}
			}

			return inspected == NodeCount
				? ConstellationScanResult.Resolved(0)
				: ConstellationScanResult.Failed(
					NodeCount - inspected,
					"greedy constellation sequence ended before an activated node or C0 was established");
		}
	}
}

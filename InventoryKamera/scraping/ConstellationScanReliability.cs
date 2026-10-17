using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
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
			ConstellationLockIconDetection lockIcon,
			bool activatedEvidence,
			bool lockedTextEvidence,
			ConstellationNodeState state,
			string reason)
		{
			ActivatedReads = activatedReads ?? throw new ArgumentNullException(nameof(activatedReads));
			LockedRead = lockedRead ?? throw new ArgumentNullException(nameof(lockedRead));
			LockIcon = lockIcon;
			ActivatedEvidence = activatedEvidence;
			LockedTextEvidence = lockedTextEvidence;
			State = state;
			Reason = reason ?? string.Empty;
		}

		internal IReadOnlyList<ConstellationOcrRead> ActivatedReads { get; }
		internal ConstellationOcrRead LockedRead { get; }
		internal ConstellationLockIconDetection LockIcon { get; }
		internal bool ActivatedEvidence { get; }
		internal bool LockIconEvidence => LockIcon.Accepted;
		internal bool LockedTextEvidence { get; }
		internal bool LockedEvidence => LockIconEvidence || LockedTextEvidence;
		internal ConstellationNodeState State { get; }
		internal string Reason { get; }
	}

	/// <summary>
	/// Combines positive signals from one captured frame. The compact status-label region may
	/// establish Activated through any deterministic preprocessing variant. Locked may be established
	/// either by the fixed upper-left padlock or by the exact phrase "Activate required" in the
	/// description. Absence is never treated as evidence, and conflicting positive signals remain
	/// unresolved.
	/// </summary>
	internal static class ConstellationNodeStateResolver
	{
		internal static ConstellationNodeObservation Resolve(
			IReadOnlyList<ConstellationOcrRead> activatedReads,
			ConstellationOcrRead lockedRead)
		{
			return Resolve(
				activatedReads,
				lockedRead,
				ConstellationLockIconDetection.None(ConstellationLockIconDetector.AcceptanceThreshold));
		}

		internal static ConstellationNodeObservation Resolve(
			IReadOnlyList<ConstellationOcrRead> activatedReads,
			ConstellationOcrRead lockedRead,
			ConstellationLockIconDetection lockIcon)
		{
			if (activatedReads == null) throw new ArgumentNullException(nameof(activatedReads));
			if (lockedRead == null) throw new ArgumentNullException(nameof(lockedRead));

			bool activatedEvidence = activatedReads.Any(
				read => ContainsWord(read?.NormalizedText, "activated"));
			bool lockedTextEvidence = ContainsPhrase(
				lockedRead.NormalizedText,
				"activate",
				"required");
			bool lockedEvidence = lockIcon.Accepted || lockedTextEvidence;

			ConstellationNodeState state;
			string reason;
			if (activatedEvidence && lockedEvidence)
			{
				state = ConstellationNodeState.Unresolved;
				reason = "conflicting Activated and Locked evidence";
			}
			else if (activatedEvidence)
			{
				state = ConstellationNodeState.Activated;
				reason = "exact Activated label recognized by an activated-region variant";
			}
			else if (lockedEvidence)
			{
				state = ConstellationNodeState.Locked;
				if (lockIcon.Accepted && lockedTextEvidence)
					reason = "upper-left lock icon and exact Activate required phrase recognized";
				else if (lockIcon.Accepted)
					reason = "upper-left lock icon matched the deterministic template";
				else
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
				lockIcon,
				activatedEvidence,
				lockedTextEvidence,
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
		internal static Rectangle LockStatusIcon(Size captureSize) => Create(
			captureSize,
			// 1920x1080 live evidence: x=320..416, y=70..166. This includes the full
			// fixed status circle plus padding while stopping well above the title text.
			left: 0.1667,
			top: 0.0649,
			right: 0.2167,
			bottom: 0.1538);

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

	internal readonly struct ConstellationLockIconDetection
	{
		internal ConstellationLockIconDetection(double score, double threshold, Rectangle region)
		{
			Score = score;
			Threshold = threshold;
			Region = region;
		}

		internal double Score { get; }
		internal double Threshold { get; }
		internal Rectangle Region { get; }
		internal bool Accepted => Score >= Threshold;

		internal static ConstellationLockIconDetection None(double threshold) =>
			new ConstellationLockIconDetection(0.0, threshold, Rectangle.Empty);
	}

	/// <summary>
	/// Detects the fixed upper-left white padlock through Dice similarity of near-white binary masks.
	/// Only the inner icon area participates in scoring, excluding the element-colored status ring.
	/// The template is a sanitized 96x96 status-icon crop embedded in the application assembly.
	/// </summary>
	internal sealed class ConstellationLockIconDetector
	{
		internal const int NormalizedSize = 96;
		internal const double AcceptanceThreshold = 0.750;
		internal const int WhiteLuminanceThreshold = 205;
		internal const int MaximumChannelSpread = 45;
		internal static readonly Rectangle ComparisonRegion = new Rectangle(26, 22, 44, 52);

		private const string TemplateResourceName =
			"InventoryKamera.assets.constellation_status_locked_template.png";
		private readonly bool[] templateMask;

		internal ConstellationLockIconDetector(Bitmap template)
		{
			if (template == null) throw new ArgumentNullException(nameof(template));
			using Bitmap normalized = Normalize(template);
			templateMask = CreateMask(normalized, processed: null);
		}

		internal static ConstellationLockIconDetector CreateDefault()
		{
			using Stream stream = Assembly.GetExecutingAssembly()
				.GetManifestResourceStream(TemplateResourceName);
			if (stream == null)
				throw new InvalidOperationException(
					$"Embedded constellation lock template was not found: {TemplateResourceName}");

			using var template = new Bitmap(stream);
			return new ConstellationLockIconDetector(template);
		}

		internal ConstellationLockIconDetection Detect(
			Bitmap statusIcon,
			Rectangle sourceRegion,
			out Bitmap processedMask)
		{
			if (statusIcon == null) throw new ArgumentNullException(nameof(statusIcon));

			using Bitmap normalized = Normalize(statusIcon);
			processedMask = new Bitmap(NormalizedSize, NormalizedSize, PixelFormat.Format24bppRgb);
			bool[] candidateMask = CreateMask(normalized, processedMask);

			int candidatePixels = 0;
			int templatePixels = 0;
			int intersection = 0;
			for (int y = ComparisonRegion.Top; y < ComparisonRegion.Bottom; y++)
			{
				for (int x = ComparisonRegion.Left; x < ComparisonRegion.Right; x++)
				{
					int index = y * NormalizedSize + x;
					bool candidate = candidateMask[index];
					bool expected = templateMask[index];
					if (candidate) candidatePixels++;
					if (expected) templatePixels++;
					if (candidate && expected) intersection++;
				}
			}

			double denominator = candidatePixels + templatePixels;
			double score = denominator <= 0.0 ? 0.0 : 2.0 * intersection / denominator;
			return new ConstellationLockIconDetection(score, AcceptanceThreshold, sourceRegion);
		}

		private static Bitmap Normalize(Bitmap source)
		{
			var normalized = new Bitmap(
				NormalizedSize,
				NormalizedSize,
				PixelFormat.Format24bppRgb);
			using Graphics graphics = Graphics.FromImage(normalized);
			graphics.CompositingMode = CompositingMode.SourceCopy;
			graphics.CompositingQuality = CompositingQuality.HighQuality;
			graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
			graphics.DrawImage(
				source,
				new Rectangle(0, 0, NormalizedSize, NormalizedSize),
				new Rectangle(0, 0, source.Width, source.Height),
				GraphicsUnit.Pixel);
			return normalized;
		}

		private static bool[] CreateMask(Bitmap source, Bitmap processed)
		{
			var mask = new bool[NormalizedSize * NormalizedSize];
			for (int y = 0; y < NormalizedSize; y++)
			{
				for (int x = 0; x < NormalizedSize; x++)
				{
					Color color = source.GetPixel(x, y);
					int maximum = Math.Max(color.R, Math.Max(color.G, color.B));
					int minimum = Math.Min(color.R, Math.Min(color.G, color.B));
					double luminance = 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
					bool nearWhite = luminance >= WhiteLuminanceThreshold &&
						maximum - minimum <= MaximumChannelSpread;
					mask[y * NormalizedSize + x] = nearWhite;
					processed?.SetPixel(x, y, nearWhite ? Color.White : Color.Black);
				}
			}
			return mask;
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

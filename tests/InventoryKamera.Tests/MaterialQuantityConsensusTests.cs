using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;
using Tesseract;
using Xunit;

namespace InventoryKamera.Tests
{
	public class MaterialQuantityConsensusTests
	{
		[Fact]
		public void FallbackReturnsQuantityKeyRatherThanObservationFrequency()
		{
			MaterialQuantityReadResult result = Resolve("1234", "5678", "1234", "garbage", "");

			Assert.Equal(MaterialQuantityReadStatus.Resolved, result.Status);
			Assert.Equal(1234, result.Quantity);
			Assert.NotEqual(2, result.Quantity);
			Assert.NotEqual(1, result.Quantity);
			Assert.Equal(MaterialQuantityDecisionReason.UniqueMajority, result.DecisionReason);
		}

		[Fact]
		public void TwoDifferentSingleObservationsNeverFabricateFrequencyOne()
		{
			MaterialQuantityReadResult result = Resolve("1234", "5678");

			Assert.Equal(MaterialQuantityReadStatus.Unresolved, result.Status);
			Assert.Null(result.Quantity);
			Assert.DoesNotContain(1, result.CandidateCounts.Keys);
			Assert.Equal(MaterialQuantityDecisionReason.ConflictingCandidates, result.DecisionReason);
		}

		[Fact]
		public void RepeatedSameQuantityWins()
		{
			MaterialQuantityReadResult result = Resolve("2468", "2468", "noise");

			Assert.Equal(MaterialQuantityReadStatus.Resolved, result.Status);
			Assert.Equal(2468, result.Quantity);
		}

		[Fact]
		public void ConflictingEqualFrequencyCandidatesFailSafely()
		{
			MaterialQuantityReadResult result = Resolve("1234", "5678", "1234", "5678", "noise");

			Assert.Equal(MaterialQuantityReadStatus.Unresolved, result.Status);
			Assert.Null(result.Quantity);
		}

		[Theory]
		[InlineData("")]
		[InlineData("   ")]
		[InlineData("garbage")]
		[InlineData("abc123")]
		[InlineData("12.34")]
		[InlineData("1,234")]
		public void MalformedOrPartialTextIsIgnored(string rawText)
		{
			MaterialQuantityReadResult result = Resolve(rawText, rawText);

			Assert.Equal(MaterialQuantityReadStatus.Unresolved, result.Status);
			Assert.Empty(result.CandidateCounts);
		}

		[Theory]
		[InlineData("0")]
		[InlineData("000")]
		[InlineData("-5")]
		public void NonPositiveQuantityIsRejected(string rawText)
		{
			MaterialQuantityReadResult result = Resolve(rawText, rawText);

			Assert.Equal(MaterialQuantityReadStatus.Unresolved, result.Status);
			Assert.Null(result.Quantity);
		}

		[Fact]
		public void LargeValidQuantityIsPreservedWithoutSpeculativeUpperCap()
		{
			string maximum = int.MaxValue.ToString();
			MaterialQuantityReadResult result = Resolve(maximum, maximum);

			Assert.Equal(int.MaxValue, result.Quantity);
		}

		[Fact]
		public void RepeatedLegitimateQuantityOneRemainsValid()
		{
			MaterialQuantityReadResult result = Resolve("1", "1");

			Assert.Equal(MaterialQuantityReadStatus.Resolved, result.Status);
			Assert.Equal(1, result.Quantity);
		}

		[Fact]
		public void SoleValidObservationAmongInvalidVariantsIsAccepted()
		{
			MaterialQuantityReadResult result = Resolve("9876", "noise", "", "-1", "0");

			Assert.Equal(MaterialQuantityReadStatus.Resolved, result.Status);
			Assert.Equal(9876, result.Quantity);
			Assert.Equal(MaterialQuantityDecisionReason.SoleValidCandidate, result.DecisionReason);
			Assert.DoesNotContain(0, result.CandidateCounts.Keys);
			Assert.DoesNotContain(1, result.CandidateCounts.Keys);
		}

		[Fact]
		public void SoleValidCandidateIsAcceptedRegardlessOfScalePosition()
		{
			for (int validIndex = 0; validIndex < MaterialQuantityReader.MaximumAttempts; validIndex++)
			{
				string[] observations = Enumerable.Repeat("noise", MaterialQuantityReader.MaximumAttempts).ToArray();
				observations[validIndex] = "4321";

				MaterialQuantityReadResult result = Resolve(observations);

				Assert.Equal(4321, result.Quantity);
				Assert.Equal(MaterialQuantityDecisionReason.SoleValidCandidate, result.DecisionReason);
			}
		}

		[Fact]
		public void UniqueRepeatedCandidateWinsAmongConflicts()
		{
			MaterialQuantityReadResult result = Resolve("1234", "5678", "1234", "noise", "9012");

			Assert.Equal(MaterialQuantityReadStatus.Resolved, result.Status);
			Assert.Equal(1234, result.Quantity);
			Assert.Equal(MaterialQuantityDecisionReason.UniqueMajority, result.DecisionReason);
		}

		[Fact]
		public void ThreeDifferentSingletonCandidatesRemainUnresolved()
		{
			MaterialQuantityReadResult result = Resolve("1234", "5678", "9012", "noise", "");

			Assert.Equal(MaterialQuantityReadStatus.Unresolved, result.Status);
			Assert.Null(result.Quantity);
			Assert.Equal(MaterialQuantityDecisionReason.ConflictingCandidates, result.DecisionReason);
		}

		[Fact]
		public void FiveInvalidScaleVariantsHaveNoValidCandidates()
		{
			MaterialQuantityReadResult result = Resolve("", "noise", "0", "-5", "12.34");

			Assert.Equal(MaterialQuantityReadStatus.Unresolved, result.Status);
			Assert.Null(result.Quantity);
			Assert.Empty(result.CandidateCounts);
			Assert.Equal(MaterialQuantityDecisionReason.NoValidCandidates, result.DecisionReason);
			Assert.Equal("no_valid_candidates", result.DecisionReasonCode);
		}

		[Fact]
		public void ThreeToOneConflictAcceptsUniqueMajority()
		{
			MaterialQuantityReadResult result = Resolve("2468", "1357", "2468", "2468", "noise");

			Assert.Equal(2468, result.Quantity);
			Assert.Equal(MaterialQuantityDecisionReason.UniqueMajority, result.DecisionReason);
		}

		[Fact]
		public void CandidateOrderDoesNotChangeOutcome()
		{
			string[] observations = { "2222", "3333", "2222", "noise", "4444" };
			int? expected = Resolve(observations).Quantity;
			Assert.Equal(2222, expected);

			foreach (string[] order in Permutations(observations))
				Assert.Equal(expected, Resolve(order).Quantity);
		}

		[Fact]
		public void RepeatedObservationResolutionIsDeterministic()
		{
			for (int run = 0; run < 20; run++)
			{
				MaterialQuantityReadResult result = Resolve("7000", "8000", "7000", "9000", "noise");
				Assert.Equal(7000, result.Quantity);
			}
		}

		[Fact]
		public void ExistingExactMSubstitutionRemainsSupported()
		{
			MaterialQuantityReadResult result = Resolve("M", "M");

			Assert.Equal(111, result.Quantity);
		}

		[Fact]
		public void ReaderAlwaysEvaluatesAllFiveScaleVariants()
		{
			int calls = 0;
			MaterialQuantityReadResult result = MaterialQuantityReader.Read(
				_ =>
				{
					calls++;
					return "4321";
				},
				CancellationToken.None);

			Assert.Equal(4321, result.Quantity);
			Assert.Equal(MaterialQuantityReader.MaximumAttempts, calls);
			Assert.Equal(MaterialQuantityDecisionReason.SoleValidCandidate, result.DecisionReason);
		}

		[Fact]
		public void ReaderNeverExceedsExistingFiveAttempts()
		{
			int calls = 0;
			MaterialQuantityReadResult result = MaterialQuantityReader.Read(
				attempt =>
				{
					calls++;
					return (1000 + attempt).ToString();
				},
				CancellationToken.None);

			Assert.Equal(MaterialQuantityReadStatus.Unresolved, result.Status);
			Assert.Equal(MaterialQuantityReader.MaximumAttempts, calls);
		}

		[Fact]
		public void CancellationBeforeFirstAttemptDoesNotBecomeQuantityFailure()
		{
			using var cancellation = new CancellationTokenSource();
			cancellation.Cancel();
			int calls = 0;

			MaterialQuantityReadResult result = MaterialQuantityReader.Read(
				_ =>
				{
					calls++;
					return "1234";
				},
				cancellation.Token);

			Assert.Equal(MaterialQuantityReadStatus.Cancelled, result.Status);
			Assert.Equal(MaterialQuantityDecisionReason.Cancelled, result.DecisionReason);
			Assert.Equal("cancelled", result.DecisionReasonCode);
			Assert.Equal(0, calls);
			Assert.Empty(result.Observations);
		}

		[Fact]
		public void CancellationDuringReadWinsBeforeCandidateIsAccepted()
		{
			using var cancellation = new CancellationTokenSource();

			MaterialQuantityReadResult result = MaterialQuantityReader.Read(
				_ =>
				{
					cancellation.Cancel();
					return "1234";
				},
				cancellation.Token);

			Assert.Equal(MaterialQuantityReadStatus.Cancelled, result.Status);
			Assert.Null(result.Quantity);
			Assert.Empty(result.Observations);
		}

		[Fact]
		public void PrimaryResolvedDoesNotInvokeFallback()
		{
			int fallbackCalls = 0;
			MaterialQuantityResolutionResult result = MaterialQuantityResolver.Resolve(
				Resolve("4321", "noise", "", "0", "-1"),
				() =>
				{
					fallbackCalls++;
					return Fallback("9999");
				},
				CancellationToken.None);

			Assert.Equal(MaterialQuantityReadStatus.Resolved, result.Status);
			Assert.Equal(4321, result.Quantity);
			Assert.Equal(0, fallbackCalls);
			Assert.Null(result.Fallback);
			Assert.Equal(MaterialQuantityFinalDecisionReason.PrimaryResolved, result.DecisionReason);
		}

		[Fact]
		public void NoPrimaryCandidatesAndValidFallbackAcceptsFallback()
		{
			MaterialQuantityResolutionResult result = ResolveWithFallback(
				Resolve("noise", "", "0", "-1", "12.34"),
				"57");

			Assert.Equal(MaterialQuantityReadStatus.Resolved, result.Status);
			Assert.Equal(57, result.Quantity);
			Assert.Equal(MaterialQuantityFinalDecisionReason.FallbackNoPrimaryCandidates, result.DecisionReason);
			Assert.Equal("fallback_no_primary_candidates", result.DecisionReasonCode);
		}

		[Fact]
		public void NoPrimaryCandidatesAndUnreadableFallbackRemainsUnresolved()
		{
			MaterialQuantityResolutionResult result = ResolveWithFallback(
				Resolve("noise", "", "0", "-1", "12.34"),
				"garbage");

			Assert.Equal(MaterialQuantityReadStatus.Unresolved, result.Status);
			Assert.Null(result.Quantity);
			Assert.Equal(MaterialQuantityFinalDecisionReason.FallbackUnreadable, result.DecisionReason);
		}

		[Fact]
		public void ConflictingPrimaryAndFallbackMatchingAnyCandidateAcceptsMatchedValue()
		{
			MaterialQuantityResolutionResult result = ResolveWithFallback(
				Resolve("12", "14", "174", "noise", ""),
				"12");

			Assert.Equal(MaterialQuantityReadStatus.Resolved, result.Status);
			Assert.Equal(12, result.Quantity);
			Assert.Equal(MaterialQuantityFinalDecisionReason.FallbackConfirmedPrimaryCandidate, result.DecisionReason);
			Assert.Equal("fallback_confirmed_primary_candidate", result.DecisionReasonCode);
		}

		[Fact]
		public void ConflictingPrimaryAndFallbackOutsideCandidatesRemainsUnresolved()
		{
			MaterialQuantityResolutionResult result = ResolveWithFallback(
				Resolve("12", "14", "174", "noise", ""),
				"99");

			Assert.Equal(MaterialQuantityReadStatus.Unresolved, result.Status);
			Assert.Null(result.Quantity);
			Assert.Equal(MaterialQuantityFinalDecisionReason.FallbackConflict, result.DecisionReason);
			Assert.Equal("fallback_conflict", result.DecisionReasonCode);
		}

		[Theory]
		[InlineData("")]
		[InlineData("0")]
		[InlineData("-1")]
		[InlineData("12.34")]
		[InlineData("abc123")]
		[InlineData("M")]
		public void MalformedOrNonPositiveFallbackIsRejected(string fallbackText)
		{
			MaterialQuantityResolutionResult result = ResolveWithFallback(
				Resolve("noise", "", "0", "-1", "12.34"),
				fallbackText);

			Assert.Equal(MaterialQuantityReadStatus.Unresolved, result.Status);
			Assert.Null(result.Quantity);
			Assert.Equal(MaterialQuantityFinalDecisionReason.FallbackUnreadable, result.DecisionReason);
		}

		[Fact]
		public void CancellationPreventsFallbackAndRemainsDistinct()
		{
			using var cancellation = new CancellationTokenSource();
			cancellation.Cancel();
			int fallbackCalls = 0;

			MaterialQuantityResolutionResult result = MaterialQuantityResolver.Resolve(
				Resolve("12", "14"),
				() =>
				{
					fallbackCalls++;
					return Fallback("12");
				},
				cancellation.Token);

			Assert.Equal(MaterialQuantityReadStatus.Cancelled, result.Status);
			Assert.Equal(MaterialQuantityFinalDecisionReason.Cancelled, result.DecisionReason);
			Assert.Equal("cancelled", result.DecisionReasonCode);
			Assert.Equal(0, fallbackCalls);
		}

		[Fact]
		public void CancellationDuringFallbackWinsBeforeQuantityIsAccepted()
		{
			using var cancellation = new CancellationTokenSource();

			MaterialQuantityResolutionResult result = MaterialQuantityResolver.Resolve(
				Resolve("12", "14"),
				() =>
				{
					cancellation.Cancel();
					return Fallback("12");
				},
				cancellation.Token);

			Assert.Equal(MaterialQuantityReadStatus.Cancelled, result.Status);
			Assert.Null(result.Quantity);
			Assert.Equal(MaterialQuantityFinalDecisionReason.Cancelled, result.DecisionReason);
		}

		[Fact]
		public void NumericFallbackUsesCorpusValidatedPreprocessingAndStrictParsing()
		{
			using var source = new Bitmap(20, 10, PixelFormat.Format24bppRgb);
			using (Graphics graphics = Graphics.FromImage(source)) graphics.Clear(Color.Beige);
			var ocr = new RecordingOcrService(" 321 \r\n", 0.42f);

			MaterialQuantityFallbackObservation result = MaterialQuantityNumericFallback.Read(
				source,
				ocr,
				new ImageProcessor());

			Assert.Equal(321, result.Quantity);
			Assert.Equal("321", result.NormalizedText);
			Assert.Equal(0.42f, result.Confidence);
			Assert.Equal(MaterialQuantityNumericFallback.PreprocessingMode, result.PreprocessingMode);
			Assert.Equal(40, ocr.ObservedWidth);
			Assert.Equal(20, ocr.ObservedHeight);
			Assert.Equal(PixelFormat.Format8bppIndexed, ocr.ObservedPixelFormat);
			Assert.Equal(PageSegMode.SingleLine, ocr.ObservedPageMode);
			Assert.True(ocr.ObservedNumbersOnly);
		}

		[Fact]
		public void UnresolvedItemIsSkippedAndLaterValidMaterialStillExports()
		{
			var inventory = new Inventory();
			MaterialQuantityResolutionResult unresolved = ResolveWithFallback(
				Resolve("1234", "5678", "noise"),
				"9999");
			MaterialQuantityResolutionResult resolved = ResolveWithFallback(
				Resolve("9000", "9000"),
				"not-used");

			Assert.False(MaterialScraper.TryAddResolvedMaterial(inventory, "Bad Item", unresolved, out int badCount));
			Assert.Equal(0, badCount);
			Assert.True(MaterialScraper.TryAddResolvedMaterial(inventory, "Good Item", resolved, out int goodCount));
			Assert.Equal(9000, goodCount);
			Material material = Assert.Single(inventory.Materials);
			Assert.Equal("Good Item", material.name);
			Assert.Equal(9000, material.count);

			var good = new GOOD(
				Array.Empty<Character>(),
				Array.Empty<Weapon>(),
				Array.Empty<Artifact>(),
				inventory.AllMaterials,
				equipWeapons: false,
				equipArtifacts: false);

			Assert.Single(good.Materials);
			Assert.Equal(9000, good.Materials["Good Item"]);
			Assert.DoesNotContain("Bad Item", good.Materials.Keys);
		}

		private static MaterialQuantityReadResult Resolve(params string[] observations)
		{
			var consensus = new MaterialQuantityConsensus();
			for (int i = 0; i < observations.Length; i++)
				consensus.Observe(i + 1, observations[i]);
			return consensus.Finish();
		}

		private static MaterialQuantityResolutionResult ResolveWithFallback(
			MaterialQuantityReadResult primary,
			string fallbackText) =>
			MaterialQuantityResolver.Resolve(
				primary,
				() => Fallback(fallbackText),
				CancellationToken.None);

		private static MaterialQuantityFallbackObservation Fallback(string text) =>
			MaterialQuantityFallbackObservation.Create("test", text, 0.5f);

		private static IEnumerable<string[]> Permutations(string[] values)
		{
			// Fixed rotations and reversal exercise ordering without random or wall-clock behavior.
			for (int offset = 0; offset < values.Length; offset++)
				yield return values.Skip(offset).Concat(values.Take(offset)).ToArray();
			yield return values.Reverse().ToArray();
		}

		private sealed class RecordingOcrService : IOcrService
		{
			private readonly string text;
			private readonly float confidence;

			internal RecordingOcrService(string text, float confidence)
			{
				this.text = text;
				this.confidence = confidence;
			}

			internal int ObservedWidth { get; private set; }
			internal int ObservedHeight { get; private set; }
			internal PixelFormat ObservedPixelFormat { get; private set; }
			internal PageSegMode ObservedPageMode { get; private set; }
			internal bool ObservedNumbersOnly { get; private set; }

			public void Restart() { }

			public string AnalyzeText(
				Bitmap bitmap,
				PageSegMode pageMode = PageSegMode.SingleLine,
				bool numbersOnly = false) =>
				AnalyzeTextWithConfidence(bitmap, pageMode, numbersOnly).Text;

			public (string Text, float Confidence) AnalyzeTextWithConfidence(
				Bitmap bitmap,
				PageSegMode pageMode = PageSegMode.SingleLine,
				bool numbersOnly = false)
			{
				ObservedWidth = bitmap.Width;
				ObservedHeight = bitmap.Height;
				ObservedPixelFormat = bitmap.PixelFormat;
				ObservedPageMode = pageMode;
				ObservedNumbersOnly = numbersOnly;
				return (text, confidence);
			}
		}
	}
}

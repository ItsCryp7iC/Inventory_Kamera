using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace InventoryKamera.Tests
{
	public class CharacterCancellationTests
	{
		[Fact]
		public void NameElementCancellationBeforeFirstCaptureStopsWithoutReading()
		{
			using var cancellation = new CancellationTokenSource();
			cancellation.Cancel();
			int reads = 0;

			Assert.Throws<OperationCanceledException>(() =>
				CharacterNameElementRetry.TryRead(
					CharacterNameElementRetry.MaximumAttempts,
					_ => { reads++; return true; },
					null,
					cancellation.Token));

			Assert.Equal(0, reads);
		}

		[Fact]
		public void NameElementCancellationDuringRetryWaitStopsAdditionalCaptures()
		{
			using var cancellation = new CancellationTokenSource();
			int reads = 0;

			Assert.Throws<OperationCanceledException>(() =>
				CharacterNameElementRetry.TryRead(
					CharacterNameElementRetry.MaximumAttempts,
					_ => { reads++; return false; },
					() => cancellation.Cancel(),
					cancellation.Token));

			Assert.Equal(1, reads);
		}

		[Fact]
		public void LevelCancellationJustBeforeAcceptanceRejectsResult()
		{
			using var cancellation = new CancellationTokenSource();
			int reads = 0;

			Assert.Throws<OperationCanceledException>(() =>
				CharacterLevelParser.ReadFirstPlausible(
					20,
					_ =>
					{
						reads++;
						cancellation.Cancel();
						return "80/90";
					},
					rejectedAttempt: null,
					waitBeforeRetry: null,
					cancellation.Token));

			Assert.Equal(1, reads);
		}

		[Fact]
		public void LevelCancellationDuringRetryWaitStopsFreshReads()
		{
			using var cancellation = new CancellationTokenSource();
			int reads = 0;

			Assert.Throws<OperationCanceledException>(() =>
				CharacterLevelParser.ReadFirstPlausible(
					20,
					_ => { reads++; return "204/20"; },
					rejectedAttempt: null,
					waitBeforeRetry: () => cancellation.Cancel(),
					cancellation.Token));

			Assert.Equal(1, reads);
		}

		[Fact]
		public void ExpectedCharacterCancellationPreventsVerificationCallbackAndSuccessStatus()
		{
			using var cancellation = new CancellationTokenSource();
			var character = new Character { NameGOOD = "Jean", Element = "Anemo" };
			int reads = 0;
			int callbacks = 0;

			Assert.Throws<OperationCanceledException>(() =>
				ExpectedCharacterVerifier.TryVerify(
					character,
					5,
					_ =>
					{
						reads++;
						cancellation.Cancel();
						return ResolvedAttempt("Jean", "Anemo");
					},
					observeAttempt: null,
					waitAfterRejectedAttempt: null,
					verifiedAction: () => callbacks++,
					cancellation.Token));

			Assert.Equal(1, reads);
			Assert.Equal(0, callbacks);
			Assert.Equal(CharacterScanPhaseStatus.NotAttempted, character.ConstellationScanStatus);
			Assert.Equal(CharacterScanPhaseStatus.NotAttempted, character.TalentScanStatus);
		}

		[Fact]
		public void ExpectedCharacterCancellationDuringRetryWaitStopsFreshReads()
		{
			using var cancellation = new CancellationTokenSource();
			var character = new Character { NameGOOD = "Jean", Element = "Anemo" };
			int reads = 0;

			Assert.Throws<OperationCanceledException>(() =>
				ExpectedCharacterVerifier.TryVerify(
					character,
					5,
					_ => { reads++; return UnresolvedAttempt(); },
					observeAttempt: null,
					waitAfterRejectedAttempt: () => cancellation.Cancel(),
					verifiedAction: () => throw new InvalidOperationException("Verification callback must not run."),
					cancellation.Token));

			Assert.Equal(1, reads);
		}

		[Fact]
		public void ConstellationCancellationDuringRetryWaitStopsAtCurrentNode()
		{
			using var cancellation = new CancellationTokenSource();
			int reads = 0;

			Assert.Throws<OperationCanceledException>(() =>
				ConstellationNodeRetry.TryResolve(
					ConstellationNodeRetry.MaximumAttempts,
					_ => { reads++; return UnresolvedConstellation(); },
					observeAttempt: null,
					waitBeforeRetry: () => cancellation.Cancel(),
					cancellation.Token,
					out _));

			Assert.Equal(1, reads);
		}

		[Fact]
		public void ConstellationCancellationJustBeforeResolvedStateIsAcceptedStopsResult()
		{
			using var cancellation = new CancellationTokenSource();
			int reads = 0;

			Assert.Throws<OperationCanceledException>(() =>
				ConstellationNodeRetry.TryResolve(
					ConstellationNodeRetry.MaximumAttempts,
					_ =>
					{
						reads++;
						cancellation.Cancel();
						return ConstellationNodeStateResolver.Resolve(
							new[] { new ConstellationOcrRead("test", "Activated") },
							new ConstellationOcrRead("locked", string.Empty));
					},
					observeAttempt: null,
					waitBeforeRetry: null,
					cancellation.Token,
					out _));

			Assert.Equal(1, reads);
		}

		[Fact]
		public void TalentCancellationDuringRetryWaitStopsAndLeavesPhaseIncomplete()
		{
			using var cancellation = new CancellationTokenSource();
			var character = new Character { NameGOOD = "Jean", Element = "Anemo" };
			int reads = 0;

			Assert.Throws<OperationCanceledException>(() =>
				TalentReadConsensus.TryRead(
					TalentReadConsensus.MaximumAttempts,
					_ => { reads++; return "Lv. 1"; },
					observeAttempt: null,
					waitAfterRejectedAttempt: () => cancellation.Cancel(),
					cancellation.Token,
					out _));

			Assert.Equal(1, reads);
			Assert.Equal(CharacterScanPhaseStatus.NotAttempted, character.TalentScanStatus);
		}

		[Fact]
		public void TalentCancellationJustBeforeConsensusAcceptanceDoesNotReturnCandidate()
		{
			using var cancellation = new CancellationTokenSource();
			int reads = 0;

			Assert.Throws<OperationCanceledException>(() =>
				TalentReadConsensus.TryRead(
					2,
					attempt =>
					{
						reads++;
						if (attempt == 2) cancellation.Cancel();
						return "Lv. 1\nLv. 10\nLv. 9";
					},
					observeAttempt: null,
					waitAfterRejectedAttempt: null,
					cancellation.Token,
					out _));

			Assert.Equal(2, reads);
		}

		[Fact]
		public void CancellationDuringWaitWakesWithoutWaitingForTimeout()
		{
			using var cancellation = new CancellationTokenSource();
			using var entered = new ManualResetEventSlim();
			Task wait = Task.Run(() =>
			{
				entered.Set();
				CharacterCancellation.Wait(cancellation.Token, 30000);
			});

			Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
			cancellation.Cancel();

			Assert.Throws<OperationCanceledException>(() => wait.GetAwaiter().GetResult());
		}

		[Fact]
		public void CancellationStopsControllerStepSequenceAfterCurrentStep()
		{
			using var cancellation = new CancellationTokenSource();
			var steps = new List<int>();

			Assert.Throws<OperationCanceledException>(() =>
				CharacterCancellation.RunSteps(
					cancellation.Token,
					4,
					step =>
					{
						steps.Add(step);
						cancellation.Cancel();
					}));

			Assert.Equal(new[] { 0 }, steps);
		}

		[Fact]
		public void CancellationPropagatesAfterCallerCleanupAndFreshTokenCanScan()
		{
			using var cancelled = new CancellationTokenSource();
			bool disposed = false;

			Assert.Throws<OperationCanceledException>(() =>
				CharacterNameElementRetry.TryRead(
					3,
					_ =>
					{
						try
						{
							cancelled.Cancel();
							return true;
						}
						finally
						{
							disposed = true;
						}
					},
					null,
					cancelled.Token));

			Assert.True(disposed);
			int freshReads = 0;
			Assert.True(CharacterNameElementRetry.TryRead(
				3,
				_ => { freshReads++; return true; },
				null,
				CancellationToken.None));
			Assert.Equal(1, freshReads);
		}

		[Fact]
		public void FinalRejectedAttemptDoesNotSleep()
		{
			int nameWaits = 0;
			int levelWaits = 0;
			int verificationWaits = 0;
			int talentWaits = 0;

			Assert.False(CharacterNameElementRetry.TryRead(2, _ => false, () => nameWaits++, CancellationToken.None));
			Assert.False(CharacterLevelParser.ReadFirstPlausible(
				2, _ => "bad", null, () => levelWaits++, CancellationToken.None).Success);
			Assert.False(ExpectedCharacterVerifier.TryVerify(
				new Character { NameGOOD = "Jean", Element = "Anemo" },
				2,
				_ => UnresolvedAttempt(),
				null,
				() => verificationWaits++,
				() => throw new InvalidOperationException(),
				CancellationToken.None));
			Assert.False(TalentReadConsensus.TryRead(
				2, _ => "bad", null, () => talentWaits++, CancellationToken.None, out _));

			Assert.Equal(1, nameWaits);
			Assert.Equal(1, levelWaits);
			Assert.Equal(1, verificationWaits);
			Assert.Equal(1, talentWaits);
		}

		private static ExpectedCharacterVerificationAttempt ResolvedAttempt(string name, string element) =>
			new ExpectedCharacterVerificationAttempt(
				new CharacterIdentitySignal($"{element} / {name}", name, element),
				new CharacterIdentitySignal(string.Empty, null, null));

		private static ExpectedCharacterVerificationAttempt UnresolvedAttempt() =>
			new ExpectedCharacterVerificationAttempt(
				new CharacterIdentitySignal("noise", null, null),
				new CharacterIdentitySignal("noise", null, null));

		private static ConstellationNodeObservation UnresolvedConstellation() =>
			ConstellationNodeStateResolver.Resolve(
				new[] { new ConstellationOcrRead("test", "noise") },
				new ConstellationOcrRead("locked", "noise"));
	}
}

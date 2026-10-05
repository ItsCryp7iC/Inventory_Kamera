using System;
using System.Text.RegularExpressions;
using System.Threading;

namespace InventoryKamera
{
	internal enum CharacterVerificationPhase
	{
		Constellation,
		Talent,
	}

	internal sealed class CharacterIdentitySignal
	{
		internal CharacterIdentitySignal(string rawText, string canonicalName, string element)
		{
			RawText = rawText ?? string.Empty;
			CanonicalName = canonicalName;
			Element = element;
		}

		internal string RawText { get; }
		internal string CanonicalName { get; }
		internal string Element { get; }
		internal bool IsResolved =>
			!string.IsNullOrWhiteSpace(CanonicalName) && !string.IsNullOrWhiteSpace(Element);
	}

	internal sealed class ExpectedCharacterVerificationAttempt
	{
		internal ExpectedCharacterVerificationAttempt(
			CharacterIdentitySignal block,
			CharacterIdentitySignal line)
		{
			Block = block ?? throw new ArgumentNullException(nameof(block));
			Line = line ?? throw new ArgumentNullException(nameof(line));
		}

		internal CharacterIdentitySignal Block { get; }
		internal CharacterIdentitySignal Line { get; }
	}

	internal sealed class ExpectedCharacterVerificationDecision
	{
		internal ExpectedCharacterVerificationDecision(
			bool accepted,
			string canonicalName,
			string element,
			string reason)
		{
			Accepted = accepted;
			CanonicalName = canonicalName;
			Element = element;
			Reason = reason;
		}

		internal bool Accepted { get; }
		internal string CanonicalName { get; }
		internal string Element { get; }
		internal string Reason { get; }
	}

	/// <summary>
	/// Resolves and combines the two OCR readings of the Character header. OCR thresholds and name
	/// normalization stay in their existing services; this type only keeps the block and single-line
	/// readings independent and applies the expected-character safety decision.
	/// </summary>
	internal static class ExpectedCharacterVerifier
	{
		internal static ExpectedCharacterVerificationAttempt ResolveAttempt(
			string rawBlockText,
			string rawLineText,
			GameDataSnapshot gameData) =>
			new ExpectedCharacterVerificationAttempt(
				ResolveSignal(rawBlockText, gameData),
				ResolveSignal(rawLineText, gameData));

		internal static CharacterIdentitySignal ResolveSignal(
			string rawText,
			GameDataSnapshot gameData)
		{
			if (gameData == null) throw new ArgumentNullException(nameof(gameData));

			string text = (rawText ?? string.Empty).ToLower().Trim();
			if (!text.Contains("/"))
				return new CharacterIdentitySignal(rawText, null, null);

			string[] split = text.Split(new[] { '/' }, 2);
			string namePart1 = string.Empty;
			string element;
			if (!split[0].Contains(" "))
			{
				element = TextNormalizer.FindElementByName(split[0].Trim(), gameData);
			}
			else
			{
				string[] firstLineWords = split[0].Split(new[] { ' ' }, 2);
				element = TextNormalizer.FindElementByName(firstLineWords[0].Trim(), gameData);
				if (firstLineWords.Length > 1) namePart1 = firstLineWords[1];
			}

			string normalizedName = Regex.Replace(namePart1 + split[1], @"[\W]", string.Empty);
			string canonicalName = TextNormalizer.FindClosestCharacterName(normalizedName, gameData);
			if (!LookupService.CharacterMatchesElement(canonicalName, element, gameData))
				return new CharacterIdentitySignal(rawText, null, null);

			return new CharacterIdentitySignal(rawText, canonicalName, element);
		}

		internal static ExpectedCharacterVerificationDecision Evaluate(
			string expectedCanonicalName,
			ExpectedCharacterVerificationAttempt attempt)
		{
			if (string.IsNullOrWhiteSpace(expectedCanonicalName))
				throw new ArgumentException("Expected canonical character identity is required.", nameof(expectedCanonicalName));
			if (attempt == null) throw new ArgumentNullException(nameof(attempt));

			bool blockResolved = attempt.Block.IsResolved;
			bool lineResolved = attempt.Line.IsResolved;
			if (blockResolved && lineResolved &&
				!string.Equals(attempt.Block.CanonicalName, attempt.Line.CanonicalName, StringComparison.Ordinal))
			{
				return new ExpectedCharacterVerificationDecision(
					false,
					null,
					null,
					"conflicting block and single-line canonical identities");
			}

			CharacterIdentitySignal resolved = blockResolved ? attempt.Block : lineResolved ? attempt.Line : null;
			if (resolved == null)
				return new ExpectedCharacterVerificationDecision(false, null, null, "both OCR paths unresolved");

			if (!string.Equals(resolved.CanonicalName, expectedCanonicalName, StringComparison.Ordinal))
			{
				return new ExpectedCharacterVerificationDecision(
					false,
					resolved.CanonicalName,
					resolved.Element,
					"resolved canonical identity does not match the expected character");
			}

			string reason = blockResolved && lineResolved
				? "both OCR paths agree on the expected canonical identity"
				: blockResolved
					? "block OCR resolved the expected canonical identity"
					: "single-line OCR resolved the expected canonical identity";
			return new ExpectedCharacterVerificationDecision(
				true,
				resolved.CanonicalName,
				resolved.Element,
				reason);
		}

		internal static bool TryVerify(
			Character expectedCharacter,
			int maxAttempts,
			Func<int, ExpectedCharacterVerificationAttempt> readAttempt,
			Action<int, ExpectedCharacterVerificationAttempt, ExpectedCharacterVerificationDecision> observeAttempt,
			Action waitAfterRejectedAttempt,
			Action verifiedAction,
			CancellationToken cancellationToken = default)
		{
			if (expectedCharacter == null) throw new ArgumentNullException(nameof(expectedCharacter));
			if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
			if (readAttempt == null) throw new ArgumentNullException(nameof(readAttempt));
			if (verifiedAction == null) throw new ArgumentNullException(nameof(verifiedAction));

			for (int attemptNumber = 1; attemptNumber <= maxAttempts; attemptNumber++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				ExpectedCharacterVerificationAttempt attempt = readAttempt(attemptNumber);
				cancellationToken.ThrowIfCancellationRequested();
				ExpectedCharacterVerificationDecision decision = Evaluate(expectedCharacter.CanonicalName, attempt);
				observeAttempt?.Invoke(attemptNumber, attempt, decision);
				cancellationToken.ThrowIfCancellationRequested();
				if (decision.Accepted)
				{
					verifiedAction();
					cancellationToken.ThrowIfCancellationRequested();
					return true;
				}

				if (attemptNumber < maxAttempts)
				{
					waitAfterRejectedAttempt?.Invoke();
					cancellationToken.ThrowIfCancellationRequested();
				}
			}

			return false;
		}
	}
}

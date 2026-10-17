using System;
using System.Threading;

namespace InventoryKamera
{
	/// <summary>
	/// Character-scan retry waits that wake immediately when the owning scan session is cancelled.
	/// Recognition and parsing helpers remain independent of timing and session ownership.
	/// </summary>
	internal static class CharacterCancellation
	{
		internal static void Wait(CancellationToken cancellationToken, int milliseconds)
		{
			if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
			cancellationToken.ThrowIfCancellationRequested();
			if (cancellationToken.WaitHandle.WaitOne(milliseconds))
				cancellationToken.ThrowIfCancellationRequested();
		}

		internal static void RunSteps(
			CancellationToken cancellationToken,
			int count,
			Action<int> step)
		{
			if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
			if (step == null) throw new ArgumentNullException(nameof(step));

			for (int index = 0; index < count; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				step(index);
				cancellationToken.ThrowIfCancellationRequested();
			}
		}
	}

	/// <summary>
	/// Bounded orchestration for the Attributes header retry. The supplied delegate owns one fresh
	/// capture and returns only whether that capture resolved both name and element.
	/// </summary>
	internal static class CharacterNameElementRetry
	{
		internal const int MaximumAttempts = 20;

		internal static bool TryRead(
			int maxAttempts,
			Func<int, bool> readFreshCapture,
			Action waitBeforeRetry,
			CancellationToken cancellationToken)
		{
			if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
			if (readFreshCapture == null) throw new ArgumentNullException(nameof(readFreshCapture));

			for (int attempt = 1; attempt <= maxAttempts; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				bool resolved = readFreshCapture(attempt);
				cancellationToken.ThrowIfCancellationRequested();
				if (resolved) return true;

				if (attempt < maxAttempts)
				{
					waitBeforeRetry?.Invoke();
					cancellationToken.ThrowIfCancellationRequested();
				}
			}

			return false;
		}
	}
}

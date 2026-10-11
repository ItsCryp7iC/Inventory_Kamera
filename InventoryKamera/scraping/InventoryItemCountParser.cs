using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;

namespace InventoryKamera
{
    internal enum InventoryItemCountParseStatus
    {
        Succeeded,
        Empty,
        Malformed,
        CapacityConflict,
        OutOfRange,
        Overflow,
    }

    internal readonly struct InventoryItemCountParseResult
    {
        private InventoryItemCountParseResult(
            InventoryItemCountParseStatus status,
            int count,
            int capacity,
            string reason)
        {
            Status = status;
            Count = count;
            Capacity = capacity;
            Reason = reason;
        }

        internal InventoryItemCountParseStatus Status { get; }
        internal bool Success => Status == InventoryItemCountParseStatus.Succeeded;
        internal int Count { get; }
        internal int Capacity { get; }
        internal string Reason { get; }

        internal static InventoryItemCountParseResult Succeeded(int count, int capacity) =>
            new InventoryItemCountParseResult(
                InventoryItemCountParseStatus.Succeeded, count, capacity, null);

        internal static InventoryItemCountParseResult Failed(
            InventoryItemCountParseStatus status,
            string reason) =>
            new InventoryItemCountParseResult(status, 0, 0, reason);
    }

    /// <summary>
    /// Parses only the explicit count/capacity form emitted by the game. Slashless OCR is ambiguous
    /// when the live capacity is not known in advance, so it is deliberately left unresolved rather
    /// than split into a plausible-looking pair.
    /// </summary>
    internal static class InventoryItemCountParser
    {
        private const string NumberPattern = @"(?:\d{1,3}(?:,\d{3})+|\d+)";
        private static readonly Regex FractionPattern = new Regex(
            @"^\s*(?<count>" + NumberPattern + @")\s*/\s*(?<capacity>" + NumberPattern + @")\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        internal static InventoryItemCountParseResult Parse(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return InventoryItemCountParseResult.Failed(
                    InventoryItemCountParseStatus.Empty,
                    "OCR text was empty.");

            Match fraction = FractionPattern.Match(rawText);
            if (!fraction.Success)
                return InventoryItemCountParseResult.Failed(
                    InventoryItemCountParseStatus.Malformed,
                    "OCR text did not match the complete count/capacity form.");

            string countText = fraction.Groups["count"].Value;
            string capacityText = fraction.Groups["capacity"].Value;

            if (!TryParseNumber(capacityText, out int capacity) ||
                !TryParseNumber(countText, out int count))
            {
                return InventoryItemCountParseResult.Failed(
                    InventoryItemCountParseStatus.Overflow,
                    "The item count or capacity was outside the supported integer range.");
            }

            if (capacity <= 0)
                return InventoryItemCountParseResult.Failed(
                    InventoryItemCountParseStatus.OutOfRange,
                    $"OCR capacity {capacity} must be greater than zero.");
            if (count > capacity)
                return InventoryItemCountParseResult.Failed(
                    InventoryItemCountParseStatus.OutOfRange,
                    $"OCR item count {count} was outside 0..{capacity}.");

            return InventoryItemCountParseResult.Succeeded(count, capacity);
        }

        private static bool TryParseNumber(string text, out int value) =>
            int.TryParse(
                (text ?? string.Empty).Replace(",", string.Empty),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value);
    }

    internal readonly struct InventoryItemCountObservation
    {
        internal InventoryItemCountObservation(string rawText, float confidencePercent)
        {
            RawText = rawText ?? string.Empty;
            ConfidencePercent = confidencePercent;
        }

        internal string RawText { get; }
        internal float ConfidencePercent { get; }
    }

    internal sealed class InventoryItemCountReadResult
    {
        internal InventoryItemCountReadResult(
            bool success,
            int count,
            int capacity,
            IReadOnlyList<InventoryItemCountObservation> observations,
            InventoryItemCountParseResult lastParse)
        {
            Success = success;
            Count = count;
            Capacity = capacity;
            Observations = observations;
            LastParse = lastParse;
        }

        internal bool Success { get; }
        internal int Count { get; }
        internal int Capacity { get; }
        internal IReadOnlyList<InventoryItemCountObservation> Observations { get; }
        internal InventoryItemCountParseResult LastParse { get; }
    }

    internal static class InventoryItemCountReader
    {
        internal const int DefaultAttempts = 3;

        internal static InventoryItemCountReadResult Read(
            float confidenceThreshold,
            Func<int, InventoryItemCountObservation> readFresh,
            CancellationToken cancellationToken,
            Action<int> waitBetweenAttempts = null,
            int maxAttempts = DefaultAttempts,
            int retryDelayMs = 0)
        {
            if (readFresh == null) throw new ArgumentNullException(nameof(readFresh));
            if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));

            var observations = new List<InventoryItemCountObservation>(maxAttempts);
            InventoryItemCountParseResult lastParse = InventoryItemCountParser.Parse(null);
            InventoryItemCountParseResult capacityConflict = default;
            bool hasCapacityConflict = false;
            int? observedCapacity = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                InventoryItemCountObservation observation = readFresh(attempt);
                observations.Add(observation);
                cancellationToken.ThrowIfCancellationRequested();

                lastParse = InventoryItemCountParser.Parse(observation.RawText);
                if (lastParse.Success)
                {
                    if (observedCapacity.HasValue && observedCapacity.Value != lastParse.Capacity)
                    {
                        capacityConflict = InventoryItemCountParseResult.Failed(
                            InventoryItemCountParseStatus.CapacityConflict,
                            $"Fresh captures reported conflicting capacities " +
                            $"{observedCapacity.Value} and {lastParse.Capacity}.");
                        hasCapacityConflict = true;
                    }
                    else
                    {
                        observedCapacity = lastParse.Capacity;
                    }

                    if (hasCapacityConflict)
                    {
                        lastParse = capacityConflict;
                    }
                    else if (observation.ConfidencePercent >= confidenceThreshold)
                    {
                        return new InventoryItemCountReadResult(
                            true,
                            lastParse.Count,
                            lastParse.Capacity,
                            observations.AsReadOnly(),
                            lastParse);
                    }
                }

                if (attempt < maxAttempts && waitBetweenAttempts != null)
                {
                    waitBetweenAttempts(retryDelayMs);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            return new InventoryItemCountReadResult(
                false,
                0,
                0,
                observations.AsReadOnly(),
                hasCapacityConflict
                    ? capacityConflict
                    : lastParse);
        }
    }
}

using InventoryKamera.game;
using System;
using System.Drawing;

namespace InventoryKamera
{
    /// <summary>
    /// Resolution-relative Achievement-screen regions for the current desktop layout.
    /// Phase 6D consumes the title rectangle for identity verification, and Phase 6E consumes the
    /// completion-state rectangle for positive row-state evidence.
    /// </summary>
    internal sealed class AchievementScreenRegions
    {
        // Calibrated from sanitized 1919x1079 keyboard and controller captures. Expressing the
        // source pixels as ratios keeps the measurements auditable while still scaling to every
        // supported capture size.
        private static readonly NormalizedRectangle HeadingDefinition =
            new NormalizedRectangle(100.0 / 1919.0, 15.0 / 1079.0, 265.0 / 1919.0, 77.0 / 1079.0);
        private static readonly NormalizedRectangle SearchFieldDefinition =
            new NormalizedRectangle(55.0 / 1919.0, 105.0 / 1079.0, 625.0 / 1919.0, 65.0 / 1079.0);
        private static readonly NormalizedRectangle CompletedCountDefinition =
            new NormalizedRectangle(1380.0 / 1919.0, 15.0 / 1079.0, 430.0 / 1919.0, 75.0 / 1079.0);
        private static readonly NormalizedRectangle MatchCountDefinition =
            new NormalizedRectangle(730.0 / 1919.0, 130.0 / 1079.0, 305.0 / 1919.0, 65.0 / 1079.0);
        private static readonly NormalizedRectangle PageCounterDefinition =
            new NormalizedRectangle(1670.0 / 1919.0, 140.0 / 1079.0, 115.0 / 1919.0, 55.0 / 1079.0);
        // The current keyboard/mouse zero-result message is centered in the results panel below
        // the result header. Keep this band away from the search/category column, result-row title
        // band, and bottom-right UID area. It is OCR'd only when active search controls are visible
        // and no valid Match(es) count was recognized.
        private static readonly NormalizedRectangle NoResultsMessageDefinition =
            new NormalizedRectangle(900.0 / 1919.0, 455.0 / 1079.0, 780.0 / 1919.0, 145.0 / 1079.0);
        private static readonly NormalizedRectangle FirstResultTitleDefinition =
            new NormalizedRectangle(840.0 / 1919.0, 245.0 / 1079.0, 590.0 / 1919.0, 70.0 / 1079.0);
        // The keyboard/mouse result card remains clear until the completion column begins near
        // x=1545. Keep a 40 px guard before that column while recovering the right edge omitted by
        // the historical 590 px title crop. This crop is fallback-only.
        private static readonly NormalizedRectangle FirstResultTitleWideDefinition =
            new NormalizedRectangle(840.0 / 1919.0, 245.0 / 1079.0, 665.0 / 1919.0, 70.0 / 1079.0);
        private static readonly NormalizedRectangle FirstResultCompletionStateDefinition =
            new NormalizedRectangle(1545.0 / 1919.0, 235.0 / 1079.0, 290.0 / 1919.0, 120.0 / 1079.0);
        // Measured inside both sanitized completion fixtures after the full crop proved too broad
        // for reliable Claim OCR. This remains wholly inside FirstResultCompletionState and never
        // approaches the UID area.
        private static readonly NormalizedRectangle FirstResultCompletionLabelDefinition =
            new NormalizedRectangle(1670.0 / 1919.0, 280.0 / 1079.0, 150.0 / 1919.0, 50.0 / 1079.0);
        // Narrow number-only crop over the Primogem amount in the first result row. It is wholly
        // inside the completion-state crop, ends before the progress/date column, and is far from
        // the bottom-right UID area.
        private static readonly NormalizedRectangle FirstResultRewardValueDefinition =
            new NormalizedRectangle(1585.0 / 1919.0, 319.0 / 1079.0, 65.0 / 1919.0, 35.0 / 1079.0);

        private AchievementScreenRegions(
            Rectangle heading,
            Rectangle searchField,
            Rectangle completedCount,
            Rectangle matchCount,
            Rectangle pageCounter,
            Rectangle noResultsMessage,
            Rectangle firstResultTitle,
            Rectangle firstResultTitleWide,
            Rectangle firstResultCompletionState,
            Rectangle firstResultCompletionLabel,
            Rectangle firstResultRewardValue)
        {
            Heading = heading;
            SearchField = searchField;
            CompletedCount = completedCount;
            MatchCount = matchCount;
            PageCounter = pageCounter;
            NoResultsMessage = noResultsMessage;
            FirstResultTitle = firstResultTitle;
            FirstResultTitleWide = firstResultTitleWide;
            FirstResultCompletionState = firstResultCompletionState;
            FirstResultCompletionLabel = firstResultCompletionLabel;
            FirstResultRewardValue = firstResultRewardValue;
        }

        internal Rectangle Heading { get; }
        internal Rectangle SearchField { get; }
        internal Rectangle CompletedCount { get; }
        internal Rectangle MatchCount { get; }
        internal Rectangle PageCounter { get; }
        internal Rectangle NoResultsMessage { get; }
        internal Rectangle FirstResultTitle { get; }
        internal Rectangle FirstResultTitleWide { get; }
        internal Rectangle FirstResultCompletionState { get; }
        internal Rectangle FirstResultCompletionLabel { get; }
        internal Rectangle FirstResultRewardValue { get; }

        internal static AchievementScreenRegions For(Size captureSize)
        {
            if (captureSize.Width <= 0 || captureSize.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(captureSize));

            return new AchievementScreenRegions(
                HeadingDefinition.ToRectangle(captureSize),
                SearchFieldDefinition.ToRectangle(captureSize),
                CompletedCountDefinition.ToRectangle(captureSize),
                MatchCountDefinition.ToRectangle(captureSize),
                PageCounterDefinition.ToRectangle(captureSize),
                NoResultsMessageDefinition.ToRectangle(captureSize),
                FirstResultTitleDefinition.ToRectangle(captureSize),
                FirstResultTitleWideDefinition.ToRectangle(captureSize),
                FirstResultCompletionStateDefinition.ToRectangle(captureSize),
                FirstResultCompletionLabelDefinition.ToRectangle(captureSize),
                FirstResultRewardValueDefinition.ToRectangle(captureSize));
        }
    }
}

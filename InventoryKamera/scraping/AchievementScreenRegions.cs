using InventoryKamera.game;
using System;
using System.Drawing;

namespace InventoryKamera
{
    /// <summary>
    /// Resolution-relative Achievement-screen regions for the current desktop layout.
    /// The title and completion-state rectangles are geometry outputs only in Phase 6A; their
    /// contents are not used for achievement identity or completion classification yet.
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
        private static readonly NormalizedRectangle FirstResultTitleDefinition =
            new NormalizedRectangle(840.0 / 1919.0, 245.0 / 1079.0, 590.0 / 1919.0, 70.0 / 1079.0);
        private static readonly NormalizedRectangle FirstResultCompletionStateDefinition =
            new NormalizedRectangle(1545.0 / 1919.0, 235.0 / 1079.0, 290.0 / 1919.0, 120.0 / 1079.0);

        private AchievementScreenRegions(
            Rectangle heading,
            Rectangle searchField,
            Rectangle completedCount,
            Rectangle matchCount,
            Rectangle pageCounter,
            Rectangle firstResultTitle,
            Rectangle firstResultCompletionState)
        {
            Heading = heading;
            SearchField = searchField;
            CompletedCount = completedCount;
            MatchCount = matchCount;
            PageCounter = pageCounter;
            FirstResultTitle = firstResultTitle;
            FirstResultCompletionState = firstResultCompletionState;
        }

        internal Rectangle Heading { get; }
        internal Rectangle SearchField { get; }
        internal Rectangle CompletedCount { get; }
        internal Rectangle MatchCount { get; }
        internal Rectangle PageCounter { get; }
        internal Rectangle FirstResultTitle { get; }
        internal Rectangle FirstResultCompletionState { get; }

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
                FirstResultTitleDefinition.ToRectangle(captureSize),
                FirstResultCompletionStateDefinition.ToRectangle(captureSize));
        }
    }
}

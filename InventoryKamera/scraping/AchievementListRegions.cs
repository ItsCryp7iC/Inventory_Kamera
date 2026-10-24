using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;

namespace InventoryKamera
{
    /// <summary>
    /// Normalized geometry for the ordinary Achievement category list. These measurements are
    /// deliberately separate from the single search-result row geometry.
    /// </summary>
    internal sealed class AchievementListRowGeometry
    {
        internal AchievementListRowGeometry(
            int index,
            NormalizedRectangle normalizedBounds,
            Rectangle bounds,
            Rectangle emblem,
            Rectangle title,
            Rectangle description,
            Rectangle completionState,
            Rectangle completionLabel,
            Rectangle rewardValue)
        {
            Index = index;
            NormalizedBounds = normalizedBounds;
            Bounds = bounds;
            Emblem = emblem;
            Title = title;
            Description = description;
            CompletionState = completionState;
            CompletionLabel = completionLabel;
            RewardValue = rewardValue;
        }

        internal int Index { get; }
        internal NormalizedRectangle NormalizedBounds { get; }
        internal Rectangle Bounds { get; }
        internal Rectangle Emblem { get; }
        internal Rectangle Title { get; }
        internal Rectangle Description { get; }
        internal Rectangle CompletionState { get; }
        internal Rectangle CompletionLabel { get; }
        internal Rectangle RewardValue { get; }
    }

    internal sealed class AchievementListRegions
    {
        internal const int SlotCount = 6;

        // Calibrated as a distinct ordinary-list layout on the established 1919x1079 reference.
        // The last slot ends above the UID-bearing lower-right corner.
        private const double ReferenceWidth = 1919.0;
        private const double ReferenceHeight = 1079.0;
        private const double FirstRowTop = 190.0;
        private const double RowPitch = 141.0;
        private const double RowHeight = 132.0;

        private AchievementListRegions(IReadOnlyList<AchievementListRowGeometry> rows)
        {
            Rows = rows;
        }

        internal IReadOnlyList<AchievementListRowGeometry> Rows { get; }

        internal static AchievementListRegions For(Size captureSize)
        {
            if (captureSize.Width <= 0 || captureSize.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(captureSize));

            var rows = new List<AchievementListRowGeometry>(SlotCount);
            for (int index = 0; index < SlotCount; index++)
            {
                double top = FirstRowTop + (index * RowPitch);
                var boundsDefinition = Definition(720, top, 1115, RowHeight);
                rows.Add(new AchievementListRowGeometry(
                    index,
                    boundsDefinition,
                    boundsDefinition.ToRectangle(captureSize),
                    Definition(748, top + 26, 78, 78).ToRectangle(captureSize),
                    Definition(845, top + 17, 650, 39).ToRectangle(captureSize),
                    Definition(845, top + 58, 650, 48).ToRectangle(captureSize),
                    Definition(1515, top + 12, 300, 105).ToRectangle(captureSize),
                    Definition(1650, top + 37, 165, 48).ToRectangle(captureSize),
                    Definition(1550, top + 82, 76, 34).ToRectangle(captureSize)));
            }

            return new AchievementListRegions(new ReadOnlyCollection<AchievementListRowGeometry>(rows));
        }

        private static NormalizedRectangle Definition(double x, double y, double width, double height) =>
            new(x / ReferenceWidth, y / ReferenceHeight, width / ReferenceWidth, height / ReferenceHeight);
    }
}

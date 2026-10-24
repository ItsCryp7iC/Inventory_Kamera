using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;

namespace InventoryKamera
{
    internal enum AchievementVisibleRowVisibility { Complete, Partial, Uncertain }

    internal sealed class AchievementListRowGeometry
    {
        internal AchievementListRowGeometry(int index, NormalizedRectangle normalizedBounds,
            Rectangle bounds, Rectangle visibleBounds, AchievementVisibleRowVisibility visibility,
            Rectangle emblem, Rectangle title, Rectangle description, Rectangle completionState,
            Rectangle completionLabel, Rectangle rewardValue)
        {
            Index = index;
            NormalizedBounds = normalizedBounds;
            Bounds = bounds;
            VisibleBounds = visibleBounds;
            Visibility = visibility;
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
        internal Rectangle VisibleBounds { get; }
        internal AchievementVisibleRowVisibility Visibility { get; }
        internal Rectangle Emblem { get; }
        internal Rectangle Title { get; }
        internal Rectangle Description { get; }
        internal Rectangle CompletionState { get; }
        internal Rectangle CompletionLabel { get; }
        internal Rectangle RewardValue { get; }
        internal bool TitleRegionVisible => VisibleBounds.Contains(Title);
        internal bool CompletionRegionVisible => VisibleBounds.Contains(CompletionState);
        internal bool RewardRegionVisible => VisibleBounds.Contains(RewardValue);
    }

    internal sealed class AchievementListRegions
    {
        internal const int SlotCount = 7;
        internal const double ReferenceWidth = 1919.0;
        internal const double ReferenceHeight = 1079.0;
        internal const double FirstRowTop = 146.0;
        internal const double RowPitch = 125.0;
        internal const double RowHeight = 121.0;

        private AchievementListRegions(Rectangle rowViewport, Rectangle borderScanBand,
            IReadOnlyList<AchievementListRowGeometry> rows)
        {
            RowViewport = rowViewport;
            BorderScanBand = borderScanBand;
            Rows = rows;
        }

        internal Rectangle RowViewport { get; }
        internal Rectangle BorderScanBand { get; }
        internal IReadOnlyList<AchievementListRowGeometry> Rows { get; }

        internal static AchievementListRegions For(Size captureSize)
        {
            Validate(captureSize);
            Rectangle viewport = Definition(727, 136, 1123, 855).ToRectangle(captureSize);
            Rectangle scanBand = Definition(738, 136, 1081, 855).ToRectangle(captureSize);
            var rows = new List<AchievementListRowGeometry>(SlotCount);
            for (int index = 0; index < SlotCount; index++)
            {
                int top = ScaleY(FirstRowTop + (index * RowPitch), captureSize);
                rows.Add(CreateRow(captureSize, index, top, top,
                    top + ScaleY(RowHeight, captureSize), AchievementVisibleRowVisibility.Complete));
            }
            return new AchievementListRegions(viewport, scanBand,
                new ReadOnlyCollection<AchievementListRowGeometry>(rows));
        }

        internal static AchievementListRowGeometry CreateDetectedRow(Size captureSize, int index,
            int expectedTop, int visibleTop, int visibleBottom,
            AchievementVisibleRowVisibility visibility)
        {
            Validate(captureSize);
            return CreateRow(captureSize, index, expectedTop, visibleTop, visibleBottom, visibility);
        }

        internal static int ScaleReferenceX(double value, Size captureSize) => ScaleX(value, captureSize);
        internal static int ScaleReferenceY(double value, Size captureSize) => ScaleY(value, captureSize);

        private static AchievementListRowGeometry CreateRow(Size captureSize, int index,
            int expectedTop, int visibleTop, int visibleBottom,
            AchievementVisibleRowVisibility visibility)
        {
            int height = ScaleY(RowHeight, captureSize);
            Rectangle bounds = new(ScaleX(738, captureSize), expectedTop,
                ScaleX(1081, captureSize), height);
            Rectangle visible = Rectangle.Intersect(bounds, new Rectangle(bounds.Left,
                Math.Max(bounds.Top, visibleTop), bounds.Width,
                Math.Max(0, Math.Min(bounds.Bottom, visibleBottom) - Math.Max(bounds.Top, visibleTop))));
            double referenceTop = expectedTop * ReferenceHeight / captureSize.Height;
            var normalized = Definition(738, referenceTop, 1081, RowHeight);
            Rectangle At(double x, double y, double width, double regionHeight) => new(
                ScaleX(x, captureSize), expectedTop + ScaleY(y, captureSize),
                ScaleX(width, captureSize), ScaleY(regionHeight, captureSize));
            return new AchievementListRowGeometry(index, normalized, bounds, visible, visibility,
                At(752, 7, 96, 108), At(862, 21, 690, 36), At(862, 58, 690, 53),
                At(1668, 1, 150, 118), At(1680, 29, 134, 52), At(1585, 72, 72, 34));
        }

        private static void Validate(Size captureSize)
        {
            if (captureSize.Width <= 0 || captureSize.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(captureSize));
        }

        private static int ScaleX(double value, Size size) =>
            Math.Max(1, (int)Math.Round(value * size.Width / ReferenceWidth));
        private static int ScaleY(double value, Size size) =>
            Math.Max(1, (int)Math.Round(value * size.Height / ReferenceHeight));
        private static NormalizedRectangle Definition(double x, double y, double width, double height) =>
            new(x / ReferenceWidth, y / ReferenceHeight, width / ReferenceWidth, height / ReferenceHeight);
    }
}

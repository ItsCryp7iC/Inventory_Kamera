using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace InventoryKamera.game
{
    internal sealed class PaimonMenuPathPlan
    {
        public bool Success { get; }
        public IReadOnlyList<GameNavigator.MenuDirection> Steps { get; }
        public string FailureReason { get; }
        public bool AlreadyAtTarget => Success && Steps.Count == 0;

        private PaimonMenuPathPlan(bool success, IEnumerable<GameNavigator.MenuDirection> steps, string failureReason)
        {
            Success = success;
            Steps = new ReadOnlyCollection<GameNavigator.MenuDirection>((steps ?? Array.Empty<GameNavigator.MenuDirection>()).ToList());
            FailureReason = failureReason;
        }

        public static PaimonMenuPathPlan Succeeded(IEnumerable<GameNavigator.MenuDirection> steps) =>
            new PaimonMenuPathPlan(true, steps, null);

        public static PaimonMenuPathPlan Failed(string reason) =>
            new PaimonMenuPathPlan(false, Array.Empty<GameNavigator.MenuDirection>(), reason);
    }

    /// <summary>
    /// Builds directional adjacency from the currently visible tile centers. The collection order,
    /// row count, and the target's position are deliberately irrelevant.
    /// </summary>
    internal sealed class PaimonMenuPathPlanner
    {
        private static readonly GameNavigator.MenuDirection[] Directions =
        {
            GameNavigator.MenuDirection.Up,
            GameNavigator.MenuDirection.Down,
            GameNavigator.MenuDirection.Left,
            GameNavigator.MenuDirection.Right,
        };

        public PaimonMenuPathPlan Plan(
            IReadOnlyList<PaimonMenuTile> tiles,
            PaimonMenuTile current,
            PaimonMenuTile target)
        {
            if (tiles == null || tiles.Count == 0) return PaimonMenuPathPlan.Failed("No visible menu tiles were detected.");
            if (current == null) return PaimonMenuPathPlan.Failed("The selected menu tile was not detected.");
            if (target == null) return PaimonMenuPathPlan.Failed("The requested menu target was not detected.");
            if (ReferenceEquals(current, target)) return PaimonMenuPathPlan.Succeeded(Array.Empty<GameNavigator.MenuDirection>());

            var queue = new Queue<PaimonMenuTile>();
            var visited = new HashSet<PaimonMenuTile>();
            var previous = new Dictionary<PaimonMenuTile, (PaimonMenuTile Tile, GameNavigator.MenuDirection Direction)>();
            queue.Enqueue(current);
            visited.Add(current);

            while (queue.Count > 0)
            {
                PaimonMenuTile tile = queue.Dequeue();
                foreach (GameNavigator.MenuDirection direction in Directions)
                {
                    PaimonMenuTile neighbor = FindNeighbor(tiles, tile, direction);
                    if (neighbor == null || !visited.Add(neighbor)) continue;
                    previous[neighbor] = (tile, direction);
                    if (ReferenceEquals(neighbor, target)) return BuildPlan(previous, current, target);
                    queue.Enqueue(neighbor);
                }
            }

            return PaimonMenuPathPlan.Failed($"No directional path connects {current} to {target} in the detected layout.");
        }

        private static PaimonMenuPathPlan BuildPlan(
            IReadOnlyDictionary<PaimonMenuTile, (PaimonMenuTile Tile, GameNavigator.MenuDirection Direction)> previous,
            PaimonMenuTile start,
            PaimonMenuTile target)
        {
            var reversed = new List<GameNavigator.MenuDirection>();
            PaimonMenuTile cursor = target;
            while (!ReferenceEquals(cursor, start))
            {
                var edge = previous[cursor];
                reversed.Add(edge.Direction);
                cursor = edge.Tile;
            }
            reversed.Reverse();
            return PaimonMenuPathPlan.Succeeded(reversed);
        }

        private static PaimonMenuTile FindNeighbor(
            IReadOnlyList<PaimonMenuTile> tiles,
            PaimonMenuTile origin,
            GameNavigator.MenuDirection direction)
        {
            bool horizontal = direction == GameNavigator.MenuDirection.Left || direction == GameNavigator.MenuDirection.Right;
            double crossTolerance = horizontal ? origin.TileBounds.Height * 0.55 : origin.TileBounds.Width * 0.55;

            return tiles
                .Where(candidate => !ReferenceEquals(candidate, origin))
                .Select(candidate =>
                {
                    double dx = candidate.Center.X - origin.Center.X;
                    double dy = candidate.Center.Y - origin.Center.Y;
                    double main = direction switch
                    {
                        GameNavigator.MenuDirection.Left => -dx,
                        GameNavigator.MenuDirection.Right => dx,
                        GameNavigator.MenuDirection.Up => -dy,
                        GameNavigator.MenuDirection.Down => dy,
                        _ => -1,
                    };
                    double cross = horizontal ? Math.Abs(dy) : Math.Abs(dx);
                    return new { Tile = candidate, Main = main, Cross = cross };
                })
                .Where(x => x.Main > 0 && x.Cross <= crossTolerance)
                .OrderBy(x => x.Main + x.Cross * 2)
                .ThenBy(x => x.Cross)
                .Select(x => x.Tile)
                .FirstOrDefault();
        }
    }
}

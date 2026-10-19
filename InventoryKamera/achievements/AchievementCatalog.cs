using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace InventoryKamera
{
    /// <summary>
    /// An immutable, validated view of the packaged canonical achievement catalog.
    /// Source enumeration order is retained in <see cref="All"/>.
    /// </summary>
    public sealed class AchievementCatalog
    {
        private readonly IReadOnlyDictionary<int, AchievementCatalogEntry> byId;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<AchievementCatalogEntry>> byExactName;
        private readonly IReadOnlyDictionary<int, IReadOnlyList<AchievementCatalogEntry>> stageGroups;
        private readonly IReadOnlyDictionary<string, AchievementTitleGroup> titleGroupsByName;
        private readonly IReadOnlyDictionary<int, AchievementTitleGroup> titleGroupsById;

        internal AchievementCatalog(IEnumerable<AchievementCatalogEntry> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));

            AchievementCatalogEntry[] ordered = entries.ToArray();
            Validate(ordered);
            All = Array.AsReadOnly(ordered);

            byId = new ReadOnlyDictionary<int, AchievementCatalogEntry>(
                ordered.ToDictionary(entry => entry.Id));
            byExactName = new ReadOnlyDictionary<string, IReadOnlyList<AchievementCatalogEntry>>(
                ordered
                    .GroupBy(entry => entry.Name, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<AchievementCatalogEntry>)Array.AsReadOnly(group.ToArray()),
                        StringComparer.Ordinal));
            AchievementTitleGroup[] titleGroups = ordered
                .GroupBy(entry => entry.Name, StringComparer.Ordinal)
                .Select(group => new AchievementTitleGroup(group.Key, group))
                .ToArray();
            TitleGroups = Array.AsReadOnly(titleGroups);
            titleGroupsByName = new ReadOnlyDictionary<string, AchievementTitleGroup>(
                titleGroups.ToDictionary(group => group.CanonicalTitle, StringComparer.Ordinal));
            titleGroupsById = new ReadOnlyDictionary<int, AchievementTitleGroup>(
                titleGroups
                    .SelectMany(group => group.Entries.Select(entry => (entry.Id, Group: group)))
                    .ToDictionary(item => item.Id, item => item.Group));
            stageGroups = new ReadOnlyDictionary<int, IReadOnlyList<AchievementCatalogEntry>>(
                ordered
                    .Where(entry => entry.StageGroupId.HasValue)
                    .GroupBy(entry => entry.StageGroupId.Value)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<AchievementCatalogEntry>)Array.AsReadOnly(
                            group.OrderBy(entry => entry.StageIndex).ThenBy(entry => entry.Id).ToArray())));
        }

        public IReadOnlyList<AchievementCatalogEntry> All { get; }
        public IReadOnlyList<AchievementTitleGroup> TitleGroups { get; }

        public AchievementCatalogEntry GetById(int id) =>
            byId.TryGetValue(id, out AchievementCatalogEntry entry) ? entry : null;

        public IReadOnlyList<AchievementCatalogEntry> FindByExactName(string name)
        {
            if (name == null) return Array.Empty<AchievementCatalogEntry>();
            return byExactName.TryGetValue(name, out IReadOnlyList<AchievementCatalogEntry> entries)
                ? entries
                : Array.Empty<AchievementCatalogEntry>();
        }

        public IReadOnlyList<AchievementCatalogEntry> GetStageGroup(int stageGroupId) =>
            stageGroups.TryGetValue(stageGroupId, out IReadOnlyList<AchievementCatalogEntry> entries)
                ? entries
                : Array.Empty<AchievementCatalogEntry>();

        public AchievementTitleGroup GetTitleGroup(string canonicalTitle)
        {
            if (canonicalTitle == null) return null;
            return titleGroupsByName.TryGetValue(canonicalTitle, out AchievementTitleGroup group)
                ? group
                : null;
        }

        public AchievementTitleGroup GetTitleGroupForId(int id) =>
            titleGroupsById.TryGetValue(id, out AchievementTitleGroup group) ? group : null;

        private static void Validate(IReadOnlyList<AchievementCatalogEntry> entries)
        {
            var ids = new Dictionary<int, AchievementCatalogEntry>();
            foreach (AchievementCatalogEntry entry in entries)
            {
                if (entry == null)
                    throw new AchievementCatalogException("The achievements array contains a null entry.");
                if (entry.Id <= 0)
                    throw new AchievementCatalogException($"Achievement ID '{entry.Id}' must be positive.");
                if (!ids.TryAdd(entry.Id, entry))
                    throw new AchievementCatalogException($"Duplicate achievement ID '{entry.Id}'.");
                if (string.IsNullOrWhiteSpace(entry.Name))
                    throw new AchievementCatalogException($"Achievement '{entry.Id}' has an invalid or empty name.");
                if (entry.Description == null)
                    throw new AchievementCatalogException($"Achievement '{entry.Id}' has no description.");
                if (entry.CategoryId < 0)
                    throw new AchievementCatalogException($"Achievement '{entry.Id}' has an invalid category ID.");
                if (string.IsNullOrWhiteSpace(entry.Version))
                    throw new AchievementCatalogException($"Achievement '{entry.Id}' has an invalid version.");
                if (entry.Order < 0)
                    throw new AchievementCatalogException($"Achievement '{entry.Id}' has an invalid order.");

                int stageFieldCount = (entry.StageGroupId.HasValue ? 1 : 0)
                    + (entry.StageIndex.HasValue ? 1 : 0)
                    + (entry.StageCount.HasValue ? 1 : 0);
                if (stageFieldCount != 0 && stageFieldCount != 3)
                    throw new AchievementCatalogException(
                        $"Achievement '{entry.Id}' has incomplete stage metadata.");

                if (stageFieldCount == 3)
                {
                    if (entry.StageGroupId.Value <= 0)
                        throw new AchievementCatalogException(
                            $"Achievement '{entry.Id}' has an invalid stageGroupId.");
                    if (entry.StageCount.Value < 2)
                        throw new AchievementCatalogException(
                            $"Achievement '{entry.Id}' has an impossible stageCount.");
                    if (entry.StageIndex.Value <= 0 || entry.StageIndex.Value > entry.StageCount.Value)
                        throw new AchievementCatalogException(
                            $"Achievement '{entry.Id}' has an impossible stageIndex/stageCount combination.");
                }
            }

            foreach (IGrouping<int, AchievementCatalogEntry> group in entries
                .Where(entry => entry.StageGroupId.HasValue)
                .GroupBy(entry => entry.StageGroupId.Value))
            {
                if (!ids.TryGetValue(group.Key, out AchievementCatalogEntry root))
                    throw new AchievementCatalogException(
                        $"Stage group '{group.Key}' does not reference a canonical achievement.");
                if (root.StageGroupId != group.Key || root.StageIndex != 1)
                    throw new AchievementCatalogException(
                        $"Stage group '{group.Key}' does not reference its first-stage member.");

                int[] stageCounts = group.Select(entry => entry.StageCount.Value).Distinct().ToArray();
                if (stageCounts.Length != 1)
                    throw new AchievementCatalogException(
                        $"Stage group '{group.Key}' has inconsistent stageCount values.");

                int stageCount = stageCounts[0];
                int[] indices = group.Select(entry => entry.StageIndex.Value).OrderBy(index => index).ToArray();
                if (indices.Distinct().Count() != indices.Length)
                    throw new AchievementCatalogException(
                        $"Stage group '{group.Key}' has a duplicate stageIndex.");
                if (indices.Length != stageCount || !indices.SequenceEqual(Enumerable.Range(1, stageCount)))
                    throw new AchievementCatalogException(
                        $"Stage group '{group.Key}' must contain contiguous stages 1 through {stageCount}.");
            }
        }
    }

    public sealed class AchievementCatalogException : FormatException
    {
        internal AchievementCatalogException(string message)
            : base(message)
        {
        }

        internal AchievementCatalogException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}

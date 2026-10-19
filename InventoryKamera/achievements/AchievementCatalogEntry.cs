namespace InventoryKamera
{
    /// <summary>
    /// One immutable canonical achievement definition generated from Traveler's Toolkit.
    /// </summary>
    public sealed class AchievementCatalogEntry
    {
        internal AchievementCatalogEntry(
            int id,
            string name,
            string description,
            int categoryId,
            bool hidden,
            string version,
            int order,
            int? stageGroupId,
            int? stageIndex,
            int? stageCount,
            int primogems = 0)
        {
            Id = id;
            Name = name;
            Description = description;
            CategoryId = categoryId;
            Hidden = hidden;
            Version = version;
            Order = order;
            StageGroupId = stageGroupId;
            StageIndex = stageIndex;
            StageCount = stageCount;
            Primogems = primogems;
        }

        public int Id { get; }
        public string Name { get; }
        public string Description { get; }
        public int CategoryId { get; }
        public bool Hidden { get; }
        public string Version { get; }
        public int Order { get; }
        public int? StageGroupId { get; }
        public int? StageIndex { get; }
        public int? StageCount { get; }
        public int Primogems { get; }
        public bool IsStaged => StageGroupId.HasValue;
    }
}

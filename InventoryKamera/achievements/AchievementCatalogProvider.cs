using System;

namespace InventoryKamera
{
    public interface IAchievementCatalogProvider
    {
        AchievementCatalog Catalog { get; }
    }

    /// <summary>
    /// Loads the versioned, packaged catalog once and exposes an immutable canonical snapshot.
    /// </summary>
    public sealed class AchievementCatalogProvider : IAchievementCatalogProvider
    {
        public AchievementCatalogProvider()
            : this(AchievementCatalogLoader.LoadPackaged())
        {
        }

        internal AchievementCatalogProvider(AchievementCatalog catalog)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public AchievementCatalog Catalog { get; }
    }
}

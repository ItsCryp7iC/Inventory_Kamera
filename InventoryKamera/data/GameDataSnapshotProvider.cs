using System;
using System.Threading;

namespace InventoryKamera
{
    /// <summary>
    /// Owns the application's current coherent lookup snapshot. A scan captures <see cref="Current"/>
    /// once; replacing this reference affects future consumers without mutating snapshots already in use.
    /// </summary>
    internal sealed class GameDataSnapshotProvider
    {
        private readonly Func<GameDataSnapshot> loader;
        private GameDataSnapshot current;

        internal GameDataSnapshotProvider(Func<GameDataSnapshot> loader)
        {
            this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
            Replace(LoadCompleteSnapshot());
        }

        internal GameDataSnapshotProvider(
            GameDataSnapshot initialSnapshot,
            Func<GameDataSnapshot> loader = null)
        {
            this.loader = loader;
            Replace(initialSnapshot);
        }

        internal GameDataSnapshot Current => Volatile.Read(ref current);

        internal void Replace(GameDataSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            // The legacy compatibility surface is also one snapshot reference, so callers can never
            // observe Characters from one load and Weapons/Artifacts from another.
            GenshinProcesor.InstallCompatibilitySnapshot(snapshot);
            Interlocked.Exchange(ref current, snapshot);
        }

        internal bool TryReload(out Exception error)
        {
            try
            {
                GameDataSnapshot replacement = LoadCompleteSnapshot();
                Replace(replacement);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                // Deliberately leave Current untouched if any file or validation step fails.
                error = ex;
                return false;
            }
        }

        private GameDataSnapshot LoadCompleteSnapshot()
        {
            if (loader == null)
                throw new InvalidOperationException("This game-data provider has no reload source.");
            return loader() ?? throw new InvalidOperationException("The game-data loader returned no snapshot.");
        }
    }
}

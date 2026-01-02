using System;
using System.Collections.Generic;

namespace Aethelgard.Simulation.Core
{
    /// <summary>
    /// Manages user-defined constraints (locks) on the world map.
    /// Supports the "Lock, Unlock, Regenerate" workflow.
    /// Stores locks in a set of parallel arrays or a dictionary to keep the main Tile struct lean
    /// (since locks are sparse relative to total tiles).
    /// </summary>
    public class ConstraintManager
    {
        private readonly int _tileCount;

        // Parallel array for lock flags. 
        // We could use a dictionary if locks are very sparse, but an array is faster 
        // and only ~400KB for 100k tiles.
        private readonly LockFlags[] _lockFlags;

        // Stores actual locked values. 
        // Using a dictionary here because storing every possible value type for every tile 
        // would be memory intensive.
        // Key: TileID, Value: Object containing locked data (e.g. PlateID, Elevation)
        private readonly Dictionary<int, LockedTileData> _lockedData;

        public ConstraintManager(int tileCount)
        {
            _tileCount = tileCount;
            _lockFlags = new LockFlags[tileCount];
            _lockedData = new Dictionary<int, LockedTileData>();
        }

        public void ClearAll()
        {
            Array.Clear(_lockFlags, 0, _tileCount);
            _lockedData.Clear();
        }

        // --------------------------------------------------------------------------------
        // Lock Management
        // --------------------------------------------------------------------------------

        public void LockTile(int tileId, LockFlags flag)
        {
            ValidateTileId(tileId);
            _lockFlags[tileId] |= flag;
        }

        public void UnlockTile(int tileId, LockFlags flag)
        {
            ValidateTileId(tileId);
            _lockFlags[tileId] &= ~flag;

            // If fully unlocked, remove data entry to save memory?
            // Optional optimization.
            if (_lockFlags[tileId] == LockFlags.None)
            {
                _lockedData.Remove(tileId);
            }
        }

        public bool IsLocked(int tileId, LockFlags flag)
        {
            if (tileId < 0 || tileId >= _tileCount) return false;
            return (_lockFlags[tileId] & flag) != 0;
        }

        // --------------------------------------------------------------------------------
        // Value Storage
        // --------------------------------------------------------------------------------

        private LockedTileData GetOrCreateData(int tileId)
        {
            if (!_lockedData.TryGetValue(tileId, out var data))
            {
                data = new LockedTileData();
                _lockedData[tileId] = data;
            }
            return data;
        }

        public void SetLockedPlate(int tileId, int plateId)
        {
            ValidateTileId(tileId);
            var data = GetOrCreateData(tileId);
            data.PlateId = plateId;
            LockTile(tileId, LockFlags.Plate);
        }

        public bool TryGetLockedPlate(int tileId, out int plateId)
        {
            if (IsLocked(tileId, LockFlags.Plate) && _lockedData.TryGetValue(tileId, out var data))
            {
                if (data.PlateId.HasValue)
                {
                    plateId = data.PlateId.Value;
                    return true;
                }
            }
            plateId = -1;
            return false;
        }

        // --------------------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------------------

        private void ValidateTileId(int tileId)
        {
            if (tileId < 0 || tileId >= _tileCount)
                throw new ArgumentOutOfRangeException(nameof(tileId), $"Tile ID {tileId} is out of range (0-{_tileCount - 1})");
        }
    }

    /// <summary>
    /// Bit flags representing what properties of a tile are locked.
    /// </summary>
    [Flags]
    public enum LockFlags : uint
    {
        None = 0,

        /// <summary>Plate ID is fixed.</summary>
        Plate = 1 << 0,

        /// <summary>Elevation is fixed.</summary>
        Elevation = 1 << 1,

        /// <summary>Biome is fixed.</summary>
        Biome = 1 << 2,

        /// <summary>River flow is fixed.</summary>
        River = 1 << 3,

        /// <summary>Feature (e.g. Volcano) is fixed.</summary>
        Feature = 1 << 4
    }

    /// <summary>
    /// Container for locked values. nullable types indicate if a specific value is set.
    /// </summary>
    public class LockedTileData
    {
        public int? PlateId;
        public float? Elevation;
        public int? BiomeId;
        // Add more as needed
    }
}

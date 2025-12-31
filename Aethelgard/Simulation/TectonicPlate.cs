using System.Numerics;

namespace Aethelgard.Simulation
{
    /// <summary>
    /// Represents a single tectonic plate.
    /// Stores identity, physics properties, and statistics.
    /// </summary>
    public class TectonicPlate
    {
        /// <summary>Unique plate identifier (0-based index).</summary>
        public int Id { get; }

        /// <summary>Tile ID where this plate was seeded.</summary>
        public int SeedTileId { get; }

        /// <summary>Crust type: 0=Oceanic, 1=Continental.</summary>
        public byte CrustType { get; set; }

        /// <summary>
        /// Velocity vector in abstract units (0-1 magnitude).
        /// Direction indicates plate movement.
        /// </summary>
        public Vector2 Velocity { get; set; }

        /// <summary>Number of tiles assigned to this plate.</summary>
        public int TileCount { get; set; }

        /// <summary>
        /// Creates a new tectonic plate.
        /// </summary>
        /// <param name="id">Unique plate identifier.</param>
        /// <param name="seedTileId">Starting tile ID.</param>
        /// <param name="crustType">0=Oceanic, 1=Continental.</param>
        public TectonicPlate(int id, int seedTileId, byte crustType)
        {
            Id = id;
            SeedTileId = seedTileId;
            CrustType = crustType;
            Velocity = Vector2.Zero;
            TileCount = 0;
        }

        /// <summary>True if this is a continental plate.</summary>
        public bool IsContinental => CrustType == 1;

        /// <summary>True if this is an oceanic plate.</summary>
        public bool IsOceanic => CrustType == 0;
    }
}

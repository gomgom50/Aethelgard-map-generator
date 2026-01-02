using System.Numerics;

namespace Aethelgard.Simulation
{
    /// <summary>
    /// Type of tectonic plate.
    /// Values match 0x1D0 spec: 0=Continental, 1=Oceanic, 2=Microplate.
    /// </summary>
    public enum PlateType : byte
    {
        Continental = 0,
        Oceanic = 1,
        Microplate = 2 // Terrane subdivision
    }

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

        /// <summary>Plate Logic Type (0=Continental, 1=Oceanic, 2=Microplate).</summary>
        public PlateType Type { get; set; }

        /// <summary>Size Tier (1-4). 4=Large, 1=Tiny.</summary>
        public int SizeTier { get; set; }

        /// <summary>Random seed used for direction/velocity generation.</summary>
        public uint DirectionSeed { get; set; }

        /// <summary>
        /// Expansion weight (Crust Fraction).
        /// Controls how large this plate grows relative to others.
        /// </summary>
        public float CrustFraction { get; set; }

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
        public TectonicPlate(int id, int seedTileId, PlateType type)
        {
            Id = id;
            SeedTileId = seedTileId;
            Type = type;
            Velocity = Vector2.Zero;
            TileCount = 0;
            SizeTier = 1;
            CrustFraction = 0.5f;
            DirectionSeed = 0;
        }

        /// <summary>True if this is a continental plate.</summary>
        public bool IsContinental => Type == PlateType.Continental;

        /// <summary>True if this is an oceanic plate.</summary>
        public bool IsOceanic => Type == PlateType.Oceanic;

        public byte CrustType => (byte)Type;

        // Boundary lists
        public HashSet<int> ConvergentTiles { get; } = new HashSet<int>();
        public HashSet<int> DivergentTiles { get; } = new HashSet<int>();
        public HashSet<int> TransformTiles { get; } = new HashSet<int>();

        /// <summary>
        /// Tiles detected as the "Head" (leading edge) of the plate.
        /// Phase 3 Assignment.
        /// </summary>
        public HashSet<int> HeadTiles { get; } = new HashSet<int>();
    }
}

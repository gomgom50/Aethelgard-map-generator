using System.Numerics;
using Raylib_cs;

namespace Aethelgard.Simulation
{
    public enum PlateType
    {
        Oceanic,
        Continental
    }

    public class Plate
    {
        public int Id { get; }
        public Vector2 Center { get; set; } // Floating point center
        public Color Color { get; }
        public float Weight { get; set; } = 1.0f; // Power Diagram Weight

        // Kinematics
        public Vector2 Velocity { get; set; }
        public bool IsLocked { get; set; } = false;
        public PlateType Type { get; set; } = PlateType.Continental;

        /// <summary>
        /// Base elevation for this plate (random variation per plate).
        /// Continental plates typically 0.5-1.5, Oceanic -0.5 to -1.5.
        /// Final elevation = BaseElevation + BoundaryEffects + Noise
        /// </summary>
        public float BaseElevation { get; set; } = 0f;

        /// <summary>
        /// Default rock type for this plate.
        /// Oceanic = Basalt, Continental = Granite typically.
        /// </summary>
        public RockType BaseRock { get; set; } = RockType.Granite;

        /// <summary>
        /// Hex tile IDs owned by this plate (for hex-based flood fill).
        /// </summary>
        public List<int> TileIds { get; set; } = new List<int>();

        // Composite Tectonics Data
        // Stores the chunk of terrain that this plate "owns" and carries with it.
        public struct TerrainPixel
        {
            public short Dx;
            public short Dy;
            public float Elevation;
            public float Thickness;
        }
        public List<TerrainPixel> TerrainPixels { get; set; } = new List<TerrainPixel>();

        public Plate(int id, Vector2 center, Color color)
        {
            Id = id;
            Center = center;
            Color = color;
            // The following properties now have default initializers or are set later
            // Velocity = Vector2.Zero;
            // Type = PlateType.Oceanic; // Default
            // Weight = 0.5f; // Default weight
        }
    }
}

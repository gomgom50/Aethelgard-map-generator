namespace Aethelgard.Simulation
{
    /// <summary>
    /// The container for all world data layers.
    /// Acts as the central "Database" for the simulation.
    /// Uses dual-grid architecture: pixel grid for data, hex grid for adjacency.
    /// </summary>
    public class WorldMap
    {
        /// <summary>Planet configuration (radius, resolution, seed)</summary>
        public PlanetConfig Config { get; private set; }

        /// <summary>Hex sphere topology for neighbor lookups</summary>
        public HexSphereTopology Topology { get; private set; }

        // Convenience accessors
        public int Width => Config.Width;
        public int Height => Config.Height;

        // Pixel-based data layers (rendering, detailed noise)
        public DataGrid<float> Elevation { get; private set; }
        public DataGrid<float> CrustThickness { get; private set; }
        public Lithosphere Lithosphere { get; private set; }

        /// <summary>
        /// Debug layer: Feature type per pixel.
        /// 0=Ocean, 1=Craton, 2=ActiveBoundary, 3=FossilBoundary, 4=Hotspot, 5=Biogenic
        /// </summary>
        public DataGrid<int> FeatureType { get; private set; }

        // Hex-based data layers (plates, territories)
        /// <summary>Plate ID for each hex tile (-1 = unassigned)</summary>
        public int[] TilePlateId { get; private set; }

        /// <summary>Boundary type for each hex tile</summary>
        public BoundaryType[] TileBoundary { get; private set; }

        /// <summary>Distance from this tile to its plate's seed tile</summary>
        public float[] TileDistanceToSeed { get; private set; }

        /// <summary>Microplate ID within continental plates (for ancient cratons)</summary>
        public int[] TileMicroplateId { get; private set; }

        /// <summary>True if this tile is on a microplate boundary (ancient orogeny)</summary>
        public bool[] TileMicroBoundary { get; private set; }

        /// <summary>Crust age (0 = new/rift, 1 = old/subductable). Affects oceanic depth.</summary>
        public float[] TileAge { get; private set; }

        /// <summary>
        /// Cached mapping from pixel coordinates to hex tile IDs.
        /// Pre-computed at construction to avoid per-frame O(n) lookups.
        /// </summary>
        public DataGrid<int> PixelTileMap { get; private set; }

        /// <summary>
        /// Creates a WorldMap from a PlanetConfig.
        /// This is the preferred constructor for the new architecture.
        /// </summary>
        public WorldMap(PlanetConfig config)
        {
            Config = config;
            Topology = new HexSphereTopology(config.HexResolution);

            // Initialize pixel-based layers
            Elevation = new DataGrid<float>(Config.Width, Config.Height);
            CrustThickness = new DataGrid<float>(Config.Width, Config.Height);
            Lithosphere = new Lithosphere(Config.Width, Config.Height);
            FeatureType = new DataGrid<int>(Config.Width, Config.Height);

            // Initialize hex-based layers
            TilePlateId = new int[Topology.TileCount];
            TileBoundary = new BoundaryType[Topology.TileCount];
            TileDistanceToSeed = new float[Topology.TileCount];
            TileMicroplateId = new int[Topology.TileCount];
            TileMicroBoundary = new bool[Topology.TileCount];
            TileAge = new float[Topology.TileCount];

            // Pre-compute pixel-to-tile mapping (expensive once, but fast lookups)
            PixelTileMap = new DataGrid<int>(Config.Width, Config.Height);
            BuildPixelTileMap();

            // Default initialization
            Elevation.Fill(-1.0f);
            CrustThickness.Fill(1.0f);
            FeatureType.Fill(0);

            for (int i = 0; i < TilePlateId.Length; i++)
            {
                TilePlateId[i] = -1; // Unassigned
                TileBoundary[i] = BoundaryType.None;
                TileDistanceToSeed[i] = float.MaxValue;
                TileMicroplateId[i] = -1; // Unassigned
                TileMicroBoundary[i] = false;
                TileAge[i] = 0f; // Default age 0
            }
        }

        /// <summary>
        /// Pre-computes the mapping from each pixel to its hex tile ID.
        /// This is O(width×height×tiles) but only runs once at construction.
        /// </summary>
        private void BuildPixelTileMap()
        {
            System.Threading.Tasks.Parallel.For(0, Height, y =>
            {
                for (int x = 0; x < Width; x++)
                {
                    var (lon, lat) = SphericalMath.PixelToLatLon(x, y, Width, Height);
                    int tileId = Topology.GetTileAtLatLon(lat, lon);
                    PixelTileMap.Set(x, y, tileId);
                }
            });
        }

        /// <summary>
        /// Legacy constructor for backwards compatibility.
        /// Creates a default Earth-sized planet with given pixel dimensions.
        /// </summary>
        public WorldMap(int width, int height)
            : this(new PlanetConfig
            {
                // Derive radius from pixel width
                RadiusKm = width / (2f * System.MathF.PI * PlanetConfig.PixelsPerKm)
            })
        {
        }

        /// <summary>
        /// Gets the hex tile ID for a given pixel coordinate (O(1) lookup).
        /// </summary>
        public int GetHexTileAt(int pixelX, int pixelY)
        {
            return PixelTileMap.Get(pixelX, pixelY);
        }
    }
}

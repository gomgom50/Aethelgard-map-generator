using Aethelgard.Simulation.Core;
using Aethelgard.Simulation.Generators;
using Aethelgard.Simulation.Systems;

namespace Aethelgard.Simulation
{
    /// <summary>
    /// Central world state container.
    /// Owns the hex sphere topology and all tile data.
    /// Acts as the primary interface for world generation algorithms.
    /// </summary>
    public class WorldMap
    {
        /// <summary>Pre-computed hex sphere geometry and neighbor relationships.</summary>
        public HexSphereTopology Topology { get; }

        /// <summary>Array of all tiles in the world. Indexed by tile ID.</summary>
        public Tile[] Tiles { get; }

        /// <summary>World generation seed for deterministic generation.</summary>
        public int Seed { get; }

        /// <summary>Resolution (subdivisions per icosahedron edge).</summary>
        public int Resolution { get; }

        /// <summary>Generated tectonic plates (null until GenerateTectonics is called).</summary>
        public TectonicPlate[]? Plates { get; private set; }

        /// <summary>Manages subtile data for high-resolution features.</summary>
        public SubtileSystem Subtiles { get; private set; }

        /// <summary>
        /// Creates a new world map with the specified resolution and seed.
        /// Initializes topology, allocates tiles, and generates initial elevation.
        /// </summary>
        /// <param name="resolution">Subdivisions per icosahedron edge (5-50 typical).</param>
        /// <param name="seed">Random seed for deterministic generation.</param>
        public WorldMap(int resolution, int seed)
        {
            Resolution = resolution;
            Seed = seed;

            // 1. Build topology (hex sphere geometry)
            Topology = new HexSphereTopology(resolution);

            // 2. Allocate tiles
            Tiles = new Tile[Topology.TileCount];
            for (int i = 0; i < Tiles.Length; i++)
            {
                Tiles[i] = Tile.Default;
            }

            // 3. Initialize subtile system
            Subtiles = new SubtileSystem(this, seed);

            // 4. Initialize with noise-based elevation (Phase 0 visualization)
            InitializeElevation();
        }

        /// <summary>
        /// Generates tectonic plates and applies Phase 1 tectonics.
        /// Replaces noise-based elevation with plate-based geology.
        /// </summary>
        /// <param name="plateCount">Number of plates to generate (4-20 typical).</param>
        /// <param name="continentalRatio">Fraction of plates that are continental (0-1).</param>
        /// <param name="noiseStrength">Strength of noise for plate boundary irregularity.</param>
        public void GenerateTectonics(
            int plateCount = 12,
            float continentalRatio = 0.4f,
            float noiseStrength = 1.0f,
            // Stack A (Base)
            float noiseAScale = 0.15f,
            float noiseAPersistence = 0.5f,
            float noiseALacunarity = 2.0f,
            float noiseAWeight = 1.0f,
            // Stack B (Detail)
            float noiseBScale = 0.5f,
            float noiseBPersistence = 0.5f,
            float noiseBLacunarity = 2.0f,
            float noiseBWeight = 1.0f,
            // Distance Penalty
            // Distance Penalty
            float distancePenalty = 0.5f,
            // Domain Warping
            float noiseWarping = 2.0f,
            // Microplates
            int microplatesPerPlate = 3
        )
        {
            var generator = new PlateGenerator(this, Seed)
            {
                PlateCount = plateCount,
                ContinentalRatio = continentalRatio,
                NoiseStrength = noiseStrength,

                NoiseAScale = noiseAScale,
                NoiseAPersistence = noiseAPersistence,
                NoiseALacunarity = noiseALacunarity,
                NoiseAWeight = noiseAWeight,

                NoiseBScale = noiseBScale,
                NoiseBPersistence = noiseBPersistence,
                NoiseBLacunarity = noiseBLacunarity,
                NoiseBWeight = noiseBWeight,

                DistancePenalty = distancePenalty,
                NoiseWarping = noiseWarping
            };

            generator.GeneratePlates();
            generator.GenerateMicroplates(microplatesPerPlate);
            generator.AssignVelocitiesAndDirections();
            generator.ClassifyBoundaries();
            generator.DetermineCrustAge(); // Explicit call
            generator.InitializeTileCrust(); // Was AssignRockTypes
            generator.InitializeLandElevation(); // Was GenerateLandmass
            Plates = generator.Plates;

            // Step 2: Refine Boundaries using Subtile System (High Resolution)
            // We recreate the noise instances to match PlateGenerator's logic
            // providing consistent "Micro Noise" at the subtile level.
            var noiseA = new FractalNoise(Seed + 1000, 6, noiseAPersistence, noiseALacunarity, noiseAScale);
            var noiseB = new FractalNoise(Seed + 2000, 8, noiseBPersistence, noiseBLacunarity, noiseBScale);

            Subtiles.RefinePlateBoundaries(
                Plates,
                noiseA, noiseAWeight,
                noiseB, noiseBWeight,
                noiseStrength,
                distancePenalty
            );
        }

        /// <summary>
        /// Initializes tiles with fractal noise elevation for Phase 0 visualization.
        /// This provides immediate visual feedback; will be replaced by tectonics in Phase 1.
        /// </summary>
        private void InitializeElevation()
        {
            // Create noise generator with world seed
            var noise = new FractalNoise(
                seed: Seed,
                octaves: 6,
                persistence: 0.5f,
                lacunarity: 2.0f,
                scale: 0.8f // Coarse scale for continent-sized features
            );

            // Secondary noise for variation
            var detailNoise = new FractalNoise(
                seed: Seed + 1,
                octaves: 4,
                persistence: 0.6f,
                lacunarity: 2.2f,
                scale: 2.0f
            );

            for (int i = 0; i < Tiles.Length; i++)
            {
                var (lat, lon) = Topology.GetTileCenter(i);

                // Convert to 3D for noise sampling (avoids distortion at poles)
                var pos = SphericalMath.LatLonToHypersphere(lon, lat);

                // Sample fractal noise
                float n = noise.GetNoise(pos.X * 10f, pos.Y * 10f, pos.Z * 10f);
                float detail = detailNoise.GetNoise(pos.X * 20f, pos.Y * 20f, pos.Z * 20f) * 0.2f;

                // Combined noise in [-1, 1] range, shifted to elevation
                float combined = n + detail;

                // Map to elevation: -5000m to +5000m
                float elevation = combined * 5000f;

                // Determine land/water
                bool isLand = elevation > 0;

                Tiles[i].Elevation = elevation;
                Tiles[i].IsLand = isLand;
            }
        }

        /// <summary>
        /// Gets a tile by ID. Returns reference for efficient access.
        /// </summary>
        public ref Tile GetTile(int tileId) => ref Tiles[tileId];

        /// <summary>
        /// Gets read-only tile data by ID.
        /// </summary>
        public Tile GetTileReadOnly(int tileId) => Tiles[tileId];
    }
}

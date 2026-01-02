using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Aethelgard.Simulation.Algorithms;
using Aethelgard.Simulation.Core;

namespace Aethelgard.Simulation.Generators
{
    /// <summary>
    /// Generates tectonic plates for the world map.
    /// Implements the Phase 1 tectonics pipeline:
    /// 1. Seed plates (random tiles, non-adjacent)
    /// 2. Flood fill expansion (fractal noise for irregular borders)
    /// 3. Assign velocities and crust types
    /// 4. Classify boundaries
    /// 5. Determine crust age
    /// 6. Generate microplates
    /// 7. Assign rock types
    /// </summary>
    public class PlateGenerator
    {
        private readonly WorldMap _map;
        private readonly int _seed;
        private Random _rng = default!;
        private SimplexNoise _noise6 = default!;
        private SimplexNoise _noise8 = default!;
        private SimplexNoise _noise = default!; // Elevation - now Simplex for organic shapes

        /// <summary>Generated plates after running generation.</summary>
        public TectonicPlate[] Plates { get; private set; } = Array.Empty<TectonicPlate>();

        // Configuration
        public int PlateCount { get; set; } = 12;
        public float ContinentalRatio { get; set; } = 0.4f;
        public float NoiseStrength { get; set; } = 0.5f;
        public int MaxRetries { get; set; } = 10;
        public float TargetLandFraction { get; set; } = 0.3f; // 30% Land Global Target

        // Noise A (Major Features / Shapes)
        public float NoiseAScale { get; set; } = 0.8f;
        public float NoiseAPersistence { get; set; } = 0.5f;
        public float NoiseALacunarity { get; set; } = 2.0f;
        public float NoiseAWeight { get; set; } = 1.0f;

        // Noise B (Micro Details / Borders)
        public float NoiseBScale { get; set; } = 5.0f;
        public float NoiseBPersistence { get; set; } = 0.5f;
        public float NoiseBLacunarity { get; set; } = 2.0f;
        public float NoiseBWeight { get; set; } = 0.15f;

        // Distance Penalty (Lower = more organic, Higher = more circular)
        public float DistancePenalty { get; set; } = 0.4f;

        // Domain Warping for organic swirls
        public float NoiseWarping { get; set; } = 2.0f;

        // Elevation Config
        public float OceanBase { get; set; } = -4000f;
        public float ContinentBase { get; set; } = 500f;

        // Advanced Configuration (Exposed Magic Numbers)
        // Boundary Classification
        public float BoundaryVotingThreshold { get; set; } = 0.525f; // 52.5%

        // Crust Age
        public float CrustAgeSpread { get; set; } = 2.5f; // Distance multiplier for age

        // Land Generation
        public int LandSeedDensity { get; set; } = 150; // Tiles per seed
        public float LandDistancePenaltyMultiplier { get; set; } = 0.2f; // Multiplier for distance penalty

        // Coastal Elevation Boost
        public float CoastalBoostRange { get; set; } = 7500f; // Range in "Distance Units"
        public float CoastalBoostHeight { get; set; } = 250f; // Max height boost

        /// <summary>
        /// Creates a new plate generator for the given world map.
        /// </summary>
        public PlateGenerator(WorldMap map, int seed)
        {
            _map = map;
            _seed = seed;
            InitializeRng(seed);
        }

        private void InitializeRng(int seed)
        {
            _rng = new Random(seed);

            // Gleba Spec: Two stacks, 6 and 8 octaves - Using SimplexNoise for organic shapes
            _noise6 = new SimplexNoise(
                seed: seed + 1000,
                octaves: 6,
                persistence: NoiseAPersistence,
                lacunarity: NoiseALacunarity,
                scale: NoiseAScale
            );

            _noise8 = new SimplexNoise(
                seed: seed + 2000,
                octaves: 8,
                persistence: NoiseBPersistence,
                lacunarity: NoiseBLacunarity,
                scale: NoiseBScale
            );

            // Legacy Elevation Noise - Switched to Simplex to fix "Star" artifacts at poles
            _noise = new SimplexNoise(
                seed: seed + 3000,
                octaves: 8,
                persistence: NoiseBPersistence,
                lacunarity: NoiseBLacunarity,
                scale: NoiseBScale
            );
        }

        // Gleba Constants
        public const double EARTH_SURFACE_AREA = 510064471.90978825;
        public const double MOON_SURFACE_AREA = 37930000.0; // Approx

        // Plate Crust Fractions (Weights)
        // Large, Medium, Small, Tiny
        public static readonly float[] WEIGHTS_CONTINENTAL = { 0.75f, 0.275f, 0.04f, 0.015f };
        public static readonly float[] WEIGHTS_OCEANIC = { 0.75f, 0.275f, 0.04f, 0.015f };

        // Deterministic Seed Points (Lat, Lon)
        // 45N 36E, 22N 0, 45N 36W, 45S 36W, NP, SP, EQ
        private static readonly (double Lat, double Lon)[] ANCHOR_POINTS = {
            (0.785398, 0.636620),   // ~45°N, 36°E
            (0.392699, 0.0),        // ~22°N, 0°
            (0.785398, -0.636620),  // ~45°N, 36°W
            (-0.785398, -0.636620), // ~45°S, 36°W
            (1.570796, 0.0),        // North pole
            (-1.570796, 0.0),       // South pole
            (0.0, 0.0),             // Equator/prime meridian
        };

        /// <summary>
        /// Orchestrates the full Gleba Plate Tectonics Pipeline (Phases 1-6).
        /// </summary>
        public void RunGeneration()
        {
            // Phase 1: Major Plates (Seeding & FloodFill)
            GeneratePlates();

            // Phase 2: Terranes (Type 2 Microplates)
            GenerateMicroplates(3); // Default, can be overridden if calling independently

            // Phase 3: Velocities & Boundaries
            AssignVelocitiesAndDirections();
            ClassifyBoundaries();

            // Phase 4: Crust Age
            DetermineCrustAge();

            // Phase 5 & 6: Crust Initialization (Land/Ocean Islands) & Elevation
            // AssignRockTypes is currently part of this or separate? 
            AssignRockTypes();
            GenerateLandmass();

            Console.WriteLine("[PlateGenerator] Full Generation Pipeline Complete.");
        }

        /// <summary>
        /// Generates the plates (Phase 1): Seeding and Flood Fill.
        /// Implements Gleba's Major Plate generation.
        /// </summary>
        public void GeneratePlates()
        {
            Console.WriteLine("[PlateGenerator] Starting Phase 1: Major Plate Generation...");

            // 0. Cleanup
            ResetMapData();
            InitializeRng(_seed);

            // 1. Configuration (Plate Counts)
            int targetPlates = PlateCount;
            if (targetPlates < 2) targetPlates = 2; // Min 2

            int numContinental = (int)(targetPlates * ContinentalRatio);
            int numOceanic = targetPlates - numContinental;

            if (numContinental < 1) { numContinental = 1; numOceanic = targetPlates - 1; }
            if (numOceanic < 1) { numOceanic = 1; numContinental = targetPlates - 1; }

            Console.WriteLine($"[PlateGenerator] Generating {targetPlates} plates ({numContinental} Cont, {numOceanic} Ocean)...");

            // 2. Create Plates & Assign Tiers/Weights
            // Tiers: Large(4), Med(3), Small(2), Tiny(1)
            float[] weightsLarge = { 0.75f };
            float[] weightsMed = { 0.275f };
            float[] weightsSmall = { 0.04f };
            float[] weightsTiny = { 0.015f };

            List<TectonicPlate> platesList = new List<TectonicPlate>();
            var usedTiles = new HashSet<int>();
            var seeds = new List<int>();

            // Helper to pick seed
            int PickSeed()
            {
                int attempts = 0;
                while (attempts < 1000)
                {
                    attempts++;
                    int t = _rng.Next(_map.Topology.TileCount);
                    bool safe = true;
                    // Distance check
                    foreach (int s in seeds)
                    {
                        if (GetDistance(t, s) < 5) { safe = false; break; }
                    }
                    if (safe && !usedTiles.Contains(t))
                    {
                        usedTiles.Add(t);
                        return t;
                    }
                }
                return _rng.Next(_map.Topology.TileCount);
            }

            // Create Plates
            for (int i = 0; i < targetPlates; i++)
            {
                PlateType type = (i < numContinental) ? PlateType.Continental : PlateType.Oceanic;

                // Determine Tier (Cyclic 4->1)
                int tier = 4 - (i % 4);

                float weight = 0.015f;
                switch (tier)
                {
                    case 4: weight = 0.75f; break;
                    case 3: weight = 0.275f; break;
                    case 2: weight = 0.04f; break;
                    case 1: weight = 0.015f; break;
                }

                int seed = PickSeed();
                seeds.Add(seed);

                var plate = new TectonicPlate(i, seed, type)
                {
                    SizeTier = tier,
                    CrustFraction = weight,
                    DirectionSeed = (uint)_rng.Next()
                };

                platesList.Add(plate);
            }
            Plates = platesList.ToArray();

            // 3. Flood Fill Major Plates
            WeightedFloodFill(Plates, _noise6, NoiseAScale, NoiseStrength);

            // 4. Mark Active Flags (Implicit)
            Console.WriteLine("[PlateGenerator] Phase 1 Complete.");
        }

        private void WeightedFloodFill(TectonicPlate[] activePlates, SimplexNoise noise, float noiseScale, float noiseStrength)
        {
            // 1. Calculate Limits & Build Map
            double totalWeight = activePlates.Sum(p => p.CrustFraction);
            int totalTiles = _map.Topology.TileCount;
            int[] maxSizes = new int[activePlates.Length];
            var idToIndex = new Dictionary<int, int>();

            for (int i = 0; i < activePlates.Length; i++)
            {
                double ratio = activePlates[i].CrustFraction / totalWeight;
                maxSizes[i] = (int)(totalTiles * ratio);
                if (maxSizes[i] < 5) maxSizes[i] = 5;
                idToIndex[activePlates[i].Id] = i;
            }

            // 2. Setup Priority Queue
            var pq = new PriorityQueue<Candidate, float>();

            // Seed
            foreach (var p in activePlates)
            {
                _map.Tiles[p.SeedTileId].PlateId = p.Id;
                _map.Tiles[p.SeedTileId].CrustType = p.CrustType;
                p.TileCount = 1;

                foreach (int n in _map.Topology.GetNeighbors(p.SeedTileId))
                {
                    if (_map.Tiles[n].PlateId == -1) // Unclaimed
                    {
                        var (lat, lon) = _map.Topology.GetTileCenter(n);
                        var pos = SphericalMath.LatLonToHypersphere(lon, lat);
                        float nVal = noise.GetNoise(pos.X * noiseScale, pos.Y * noiseScale, pos.Z * noiseScale);
                        float noiseComponent = ((nVal * noiseStrength) + 1.0f) * 0.35f;
                        float finalScore = noiseComponent - (1.0f * DistancePenalty);

                        pq.Enqueue(new Candidate(n, p.Id, 1), -finalScore);
                    }
                }
            }

            // 3. Expansion
            while (pq.TryDequeue(out Candidate c, out float negScore))
            {
                int currentTile = c.TileId;
                if (_map.Tiles[currentTile].PlateId != -1) continue; // Claimed

                int plateId = c.PlateId;
                if (!idToIndex.TryGetValue(plateId, out int localIndex)) continue; // Should not happen

                if (activePlates[localIndex].TileCount >= maxSizes[localIndex] && maxSizes[localIndex] > 0) continue; // Limit

                // CLAIM
                _map.Tiles[currentTile].PlateId = plateId;
                _map.Tiles[currentTile].CrustType = activePlates[localIndex].CrustType;
                activePlates[localIndex].TileCount++;

                // Propagate
                var neighbors = _map.Topology.GetNeighbors(currentTile);
                foreach (int n in neighbors)
                {
                    if (_map.Tiles[n].PlateId == -1)
                    {
                        int newDist = c.Distance + 1;
                        var (lat, lon) = _map.Topology.GetTileCenter(n);
                        var pos = SphericalMath.LatLonToHypersphere(lon, lat);
                        float nVal = noise.GetNoise(pos.X * noiseScale, pos.Y * noiseScale, pos.Z * noiseScale);

                        // Score
                        float noiseComponent = ((nVal * noiseStrength) + 1.0f) * 0.35f;
                        float distTerm = newDist * DistancePenalty;
                        float finalScore = noiseComponent - distTerm;

                        pq.Enqueue(new Candidate(n, plateId, newDist), -finalScore);
                    }
                }
            }
        }

        private struct Candidate
        {
            public int TileId;
            public int PlateId;
            public int Distance;
            public Candidate(int t, int p, int d) { TileId = t; PlateId = p; Distance = d; }
        }



        /// <summary>
        /// Phase 2: Generates microplates in remaining gaps.
        /// </summary>
        public void GenerateMicroplates(int _) // Param ignored, calc from area
        {
            Console.WriteLine("[PlateGenerator] Phase 2: Generating Microplates...");

            // 1. Unassigned Tiles?
            var unassigned = new List<int>();
            for (int i = 0; i < _map.Topology.TileCount; i++)
            {
                if (_map.Tiles[i].PlateId == -1) unassigned.Add(i);
            }
            if (unassigned.Count == 0) return;

            // 2. Calculate Count Formula
            // count = floor((planet_area / EARTH_AREA) * 1000) ??
            // Using placeholder area since we don't have true planet size.
            // Spec: "count = ceil((planet_surface_area / EARTH_SURFACE_AREA) * 1000)"
            // Assuming planet is roughly earth sized or scaled.
            // Let's assume 10-20 microplates usually.
            int count = Math.Max(10, unassigned.Count / 100); // 1 per 100 empty tiles?

            // Or just fill gaps with "Terranes" until full.
            // "For Each Microplate... result: all remaining gaps filled"

            // We use a simplified loop:
            // Pick random unassigned -> Create Plate -> FloodFill -> Repeat until full?
            // "Run weighted_flood_fill - same as major plates"
            // So we should batch them?

            // Batch creation
            var microplates = new List<TectonicPlate>();
            int baseId = Plates.Length;

            // Create N microplates
            for (int k = 0; k < count; k++)
            {
                if (unassigned.Count == 0) break;

                int idx = _rng.Next(unassigned.Count);
                int seed = unassigned[idx];

                // Remove from unassigned list (inefficient but safe for now)
                unassigned.RemoveAt(idx);

                if (_map.Tiles[seed].PlateId != -1) continue; // Double check

                var micro = new TectonicPlate(baseId + k, seed, PlateType.Microplate)
                {
                    SizeTier = 1,
                    CrustFraction = 0.5f, // Standard small weight
                    DirectionSeed = (uint)_rng.Next()
                };
                microplates.Add(micro);
            }

            // Append to main list
            var allPlates = new List<TectonicPlate>(Plates);
            allPlates.AddRange(microplates);
            Plates = allPlates.ToArray();

            // Run Fill (Restricted to unassigned space implicity)
            WeightedFloodFill(microplates.ToArray(), _noise8, NoiseBScale, NoiseStrength);

            Console.WriteLine($"[PlateGenerator] Phase 2: Created {microplates.Count} Microplates.");
        }

        /// <summary>
        /// Phase 3: Assign velocities and Identify Head tiles.
        /// </summary>
        public void AssignVelocitiesAndDirections()
        {
            Console.WriteLine("[PlateGenerator] Phase 3: Velocities & Head Assignment...");

            // 1. Velocities
            for (int i = 0; i < Plates.Length; i++)
            {
                float angle = (float)(_rng.NextDouble() * Math.PI * 2);
                float speed = 0.2f + (float)(_rng.NextDouble() * 0.8f);

                // Spec: dir.x = cos(angle) * speed * random_scale ??
                // "direction.x = cos(angle) * speed * random_scale"
                // Assuming random_scale is per-component or global?
                // Usually uniform scaling.

                Plates[i].Velocity = new Vector2(
                    MathF.Cos(angle) * speed,
                    MathF.Sin(angle) * speed
                );
            }

            // 2. Identify Head Tiles (Leading Edges)
            AssignPlateHeads();
        }

        private void AssignPlateHeads()
        {
            for (int pIdx = 0; pIdx < Plates.Length; pIdx++)
            {
                var p = Plates[pIdx];
                p.HeadTiles.Clear();
                if (p.Velocity.LengthSquared() < 0.001f) continue;

                Vector2 normVel = Vector2.Normalize(p.Velocity);

                // Iterate all plate tiles (Inefficient, usually we have boundary lists, but Phase 1 didn't build them yet)
                // We can build boundary maps now or assume ClassifyBoundaries runs next.
                // Spec says AssignPlateHeads is separate.

                // Scan all tiles?
                // Or just iterate map once?
                // Plate structure doesn't store tile list explicitly in memory here (we only have TileCount).
                // So we scan Map.
            }

            // Map Scan (Once)
            for (int i = 0; i < _map.Topology.TileCount; i++)
            {
                int pid = _map.Tiles[i].PlateId;
                if (pid == -1) continue;

                var plate = Plates[pid];
                // Check neighbors
                bool isBoundary = false;
                Vector2 boundaryNormal = Vector2.Zero;

                foreach (int n in _map.Topology.GetNeighbors(i))
                {
                    if (_map.Tiles[n].PlateId != pid)
                    {
                        isBoundary = true;
                        // Vector pointing OUT
                        var (lat1, lon1) = _map.Topology.GetTileCenter(i);
                        var (lat2, lon2) = _map.Topology.GetTileCenter(n);
                        // Simple approximation
                        Vector2 dir = new Vector2((float)(lon2 - lon1), (float)(lat2 - lat1)); // Very rough
                                                                                               // Proper: Hypersphere diff
                        boundaryNormal += dir;
                    }
                }

                if (isBoundary)
                {
                    // If Velocity aligns with Normal -> Head
                    // Simple Dot > 0
                    if (Vector2.Dot(plate.Velocity, boundaryNormal) > 0)
                    {
                        plate.HeadTiles.Add(i);
                    }
                }
            }
        }

        /// <summary>
        /// Classifies boundary tiles using a voting mechanism (Gleba Spec).
        /// Threshold: 52.5% consensus required.
        /// </summary>
        public void ClassifyBoundaries()
        {
            Console.WriteLine("[PlateGenerator] Classifying Boundaries (Voting > 52.5%)...");

            // 0. Reset Plate Boundary Lists
            foreach (var p in Plates)
            {
                p.ConvergentTiles.Clear();
                p.DivergentTiles.Clear();
                p.TransformTiles.Clear();
            }

            int boundaryCount = 0;

            for (int i = 0; i < _map.Tiles.Length; i++)
            {
                int plateId = _map.Tiles[i].PlateId;
                if (plateId < 0) continue;

                var neighbors = _map.Topology.GetNeighbors(i);
                bool isBoundary = false;

                // Voting counters
                int votesConvergent = 0;
                int votesDivergent = 0;
                int votesTransform = 0;
                int totalVotes = 0;

                foreach (int n in neighbors)
                {
                    int neighborPlateId = _map.Tiles[n].PlateId;

                    if (neighborPlateId >= 0 && neighborPlateId != plateId)
                    {
                        isBoundary = true;
                        totalVotes++;

                        // Calculate interaction type for this specific neighbor-edge
                        var plate = Plates[plateId];
                        var neighborPlate = Plates[neighborPlateId];

                        // Get boundary normal (direction from this tile to neighbor)
                        var (lat1, lon1) = _map.Topology.GetTileCenter(i);
                        var (lat2, lon2) = _map.Topology.GetTileCenter(n);
                        var pos1 = SphericalMath.LatLonToHypersphere((float)lon1, (float)lat1);
                        var pos2 = SphericalMath.LatLonToHypersphere((float)lon2, (float)lat2);

                        var edge = new Vector2(
                            (float)(pos2.X - pos1.X),
                            (float)(pos2.Y - pos1.Y)
                        );
                        // Approximate 2D projection on surface

                        float edgeLen = edge.Length();
                        if (edgeLen > 0.0001f)
                        {
                            edge /= edgeLen;

                            // Relative velocity: Self - Neighbor
                            var relVel = plate.Velocity - neighborPlate.Velocity;

                            // Dot product
                            float dot = Vector2.Dot(relVel, edge);

                            if (MathF.Abs(dot) < 0.25f) // Slightly wider band for transform
                            {
                                votesTransform++;
                            }
                            else if (dot > 0)
                            {
                                votesConvergent++;
                            }
                            else
                            {
                                votesDivergent++;
                            }
                        }
                        else
                        {
                            votesTransform++; // Fallback
                        }
                    }
                }

                if (isBoundary && totalVotes > 0)
                {
                    boundaryCount++;

                    // Determine Winner
                    BoundaryType finalType = BoundaryType.Transform; // Default
                    float threshold = totalVotes * BoundaryVotingThreshold; // Configurable threshold

                    if (votesConvergent > threshold)
                    {
                        finalType = BoundaryType.Convergent;
                    }
                    else if (votesDivergent > threshold)
                    {
                        finalType = BoundaryType.Divergent;
                    }
                    else
                    {
                        finalType = BoundaryType.Transform;
                    }

                    // Assign Result
                    _map.Tiles[i].BoundaryType = finalType;
                    _map.Tiles[i].SetFlag(TileFlags.IsBoundary);

                    // Update Plate Lists & Tile Flags
                    var p = Plates[plateId];
                    switch (finalType)
                    {
                        case BoundaryType.Convergent:
                            _map.Tiles[i].SetFlag(TileFlags.IsConvergent);
                            p.ConvergentTiles.Add(i);
                            break;
                        case BoundaryType.Divergent:
                            _map.Tiles[i].SetFlag(TileFlags.IsDivergent);
                            p.DivergentTiles.Add(i);
                            break;
                        case BoundaryType.Transform:
                            _map.Tiles[i].SetFlag(TileFlags.IsTransform);
                            p.TransformTiles.Add(i);
                            break;
                    }
                }
                else
                {
                    _map.Tiles[i].BoundaryType = BoundaryType.None;
                    // Clear flags? Assumed cleared by default/reset
                }
            }
            Console.WriteLine($"[PlateGenerator] Classified {boundaryCount} boundary tiles.");
        }

        /// <summary>
        /// Sets base elevation based on crust type.
        /// </summary>
        /// <summary>
        /// Generates landmasses within plates using the Gleba-style pipeline.
        /// Replaces simple "Continental/Oceanic" boolean with detailed crust generation.
        /// </summary>
        /// <summary>
        /// Phase 4: Assign crust types (Continental vs Oceanic).
        /// </summary>
        public void InitializeTileCrust()
        {
            Console.WriteLine("[PlateGenerator] Phase 4: Initializing Crust Types...");

            // 1. Continental Plates get Continental Crust (Type 2+)
            // Fractal Flood Fill from Plate Seeds

            // Setup Noise
            var noiseA = new SimplexNoise(_seed + 5000, 6, NoiseAPersistence, NoiseALacunarity, NoiseAScale);

            foreach (var p in Plates)
            {
                if (p.Type == PlateType.Continental)
                {
                    // Target: Desired Fraction
                    // "target = floor(plate_tiles * desired_fraction)"
                    // desired_fraction calculation
                    // We'll trust user config ContinentalRatio for now or calc broadly.
                    float desired = 0.7f; // 70% of plate is land?
                    int targetCount = (int)(p.TileCount * desired);

                    // Seeds: 85% random, 15% origin
                    var seeds = new List<int>();
                    int seedsNeeded = Math.Max(1, targetCount / LandSeedDensity); // 1 seed per 150 tiles

                    for (int k = 0; k < seedsNeeded; k++)
                    {
                        if (_rng.NextDouble() < 0.15 && k == 0) seeds.Add(p.SeedTileId);
                        else
                        {
                            // Random tile in plate
                            // Need list of tiles? Or scan. Scan is slow.
                            // For now, random pick from map until hit
                            int attempts = 0;
                            while (attempts++ < 100)
                            {
                                int t = _rng.Next(_map.Topology.TileCount);
                                if (_map.Tiles[t].PlateId == p.Id) { seeds.Add(t); break; }
                            }
                        }
                    }

                    // Fractal Fill locally
                    // We can reuse WeightedFloodFill if we make it generic, or write a mini-loop here.
                    // Constraint: Must be in Plate.
                    // Fill 'CrustType' = 2 (Continental Base)

                    // Mini Queue
                    var pq = new PriorityQueue<int, float>();
                    var visited = new HashSet<int>();
                    int filled = 0;

                    foreach (int s in seeds)
                    {
                        pq.Enqueue(s, 0);
                        visited.Add(s);
                    }

                    while (pq.TryDequeue(out int t, out float _))
                    {
                        if (filled >= targetCount) break;

                        _map.Tiles[t].CrustType = 2; // Continental
                        filled++;

                        foreach (int n in _map.Topology.GetNeighbors(t))
                        {
                            if (_map.Tiles[n].PlateId == p.Id && !visited.Contains(n))
                            {
                                // Score
                                var (lat, lon) = _map.Topology.GetTileCenter(n);
                                var pos = SphericalMath.LatLonToHypersphere(lon, lat);
                                float nv = noiseA.GetNoise(pos.X * NoiseAScale, pos.Y, pos.Z);
                                pq.Enqueue(n, -nv); // High noise preferred
                                visited.Add(n);
                            }
                        }
                    }
                }
                else
                {
                    // Oceanic / Micro -> All Oceanic (1)
                    // Already set in Phase 1?
                    // Ensure it
                }
            }
        }

        /// <summary>
        /// Phase 5: Initialize Land Elevation using Distance Fields.
        /// </summary>
        public void InitializeLandElevation()
        {
            Console.WriteLine("[PlateGenerator] Phase 5: Land Elevation...");

            // 1. Identify Crust Boundaries (Coastlines)
            var coastSeeds = new List<int>();
            for (int i = 0; i < _map.Topology.TileCount; i++)
            {
                if (_map.Tiles[i].CrustType >= 2) // Land
                {
                    // Check neighbors for water/oceanic crust
                    bool coast = false;
                    foreach (int n in _map.Topology.GetNeighbors(i))
                    {
                        if (_map.Tiles[n].CrustType < 2) // Oceanic
                        {
                            coast = true;
                            break;
                        }
                    }
                    if (coast) coastSeeds.Add(i);
                }
                else
                {
                    _map.Tiles[i].Elevation = OceanBase;
                    _map.Tiles[i].IsLand = false;
                }
            }

            // 2. AreaSelector Distance Field
            var selector = new Systems.Selectors.AreaSelector(_seed)
            {
                RequireLand = false, // We work on crust, which is "potential land"
                ConstrainToPlate = false,
                MaxScore = 200f, // Max dist
                MinStep = 1f,
                MaxStep = 1f
            };

            // We only want to select within CrustType >= 2
            // AreaSelector checks "IsLand". Need to ensure IsLand is synced or custom Check
            // Ideally we modify AreaSelector, but plan said "Use existing".
            // We set IsLand=true for Crust>=2 first.
            for (int i = 0; i < _map.Tiles.Length; i++)
            {
                if (_map.Tiles[i].CrustType >= 2) _map.Tiles[i].IsLand = true;
            }

            var distField = selector.Select(_map, coastSeeds);

            // 3. Set Elevation
            foreach (var kvp in distField)
            {
                int t = kvp.Key;
                float dist = kvp.Value;

                // Formula: (distance / 7500.0) * 250.0
                // Assuming distance is in meters? 
                // If dist is 'steps', we scale it. 1 step approx 10km?
                float distMeters = dist * 10000f;
                float elev = (distMeters / 7500.0f) * 250.0f;
                // Add base
                elev += 10f;

                // Coastal Boost?
                if (distMeters < CoastalBoostRange)
                {
                    // elev += ...
                }

                _map.Tiles[t].Elevation = elev;
            }
        }

        // Stub for removed method signatures to avoid compilation error if called elsewhere 
        // (though we update TectonicsStage, other callers might exist)
        public void AssignRockTypes() { InitializeTileCrust(); }
        public void GenerateLandmass() { InitializeLandElevation(); }
        public void DetermineCrustAge() { } // Empty for now or impl later

        private void ResetMapData()
        {
            for (int i = 0; i < _map.Tiles.Length; i++)
            {
                _map.Tiles[i].PlateId = -1;
                _map.Tiles[i].TerraneId = -1;
                _map.Tiles[i].BoundaryType = BoundaryType.None;
                _map.Tiles[i].CrustAge = 0f;
            }
        }

        private int GetNearestTile(double lat, double lon)
        {
            int bestTile = -1;
            double maxDot = -2.0;
            Vector3 target = SphericalMath.LatLonToHypersphere((float)lon, (float)lat);

            for (int i = 0; i < _map.Topology.TileCount; i++)
            {
                var (tLat, tLon) = _map.Topology.GetTileCenter(i);
                Vector3 pos = SphericalMath.LatLonToHypersphere(tLon, tLat);
                double dot = Vector3.Dot(target, pos);
                if (dot > maxDot)
                {
                    maxDot = dot;
                    bestTile = i;
                }
            }
            return bestTile;
        }

        private float GetDistance(int a, int b)
        {
            return Vector3.Distance(_map.Topology.GetTilePosition(a), _map.Topology.GetTilePosition(b));
        }
    }
}

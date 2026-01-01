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

        /// <summary>
        /// Expands plates from seeds using fractal flood fill.
        /// </summary>
        private void FloodFillPlates(float[] weights)
        {
            // Initialize owner map with -1 (unassigned)
            int[] ownerMap = new int[_map.Topology.TileCount];
            Array.Fill(ownerMap, -1);

            // Mark seeds
            var seeds = new List<int>();
            Console.WriteLine($"[PlateGenerator.FloodFillPlates] Building seeds from {Plates.Length} plates...");
            for (int i = 0; i < Plates.Length; i++)
            {
                int seedTile = Plates[i].SeedTileId;

                // Validation: Check seed is valid
                if (seedTile < 0 || seedTile >= _map.Topology.TileCount)
                {
                    Console.WriteLine($"[PlateGenerator] ERROR: Plate {i} has invalid SeedTileId={seedTile} (valid: 0-{_map.Topology.TileCount - 1})");
                    throw new InvalidOperationException($"Plate {i} has invalid SeedTileId {seedTile}. Valid range: 0-{_map.Topology.TileCount - 1}");
                }

                ownerMap[seedTile] = i;
                seeds.Add(seedTile);
                Console.WriteLine($"  Plate {i}: SeedTileId={seedTile}");
            }

            // Validation
            if (seeds.Count > _map.Topology.TileCount)
                throw new InvalidOperationException($"More plates ({seeds.Count}) than tiles ({_map.Topology.TileCount}). Impossible to assign.");

            // Calculate Quotas from Weights using Hamilton Method (Largest Remainder)
            int totalTiles = _map.Topology.TileCount;
            double totalWeight = 0;
            foreach (var w in weights) totalWeight += w;

            int[] quotas = new int[weights.Length];
            double[] fractions = new double[weights.Length];
            int assignedTiles = 0;

            for (int i = 0; i < weights.Length; i++)
            {
                double exact = (weights[i] / totalWeight) * totalTiles;
                quotas[i] = (int)exact;
                fractions[i] = exact - quotas[i];

                if (quotas[i] < 1)
                {
                    quotas[i] = 1;
                    fractions[i] = 0;
                }
                assignedTiles += quotas[i];
            }

            // Distribute Remainder
            int remainder = totalTiles - assignedTiles;

            if (remainder > 0)
            {
                // Give to plates with largest fractional claim
                var sortedIndices = Enumerable.Range(0, weights.Length)
                    .OrderByDescending(i => fractions[i])
                    .ToArray();

                for (int i = 0; i < remainder; i++)
                {
                    quotas[sortedIndices[i % weights.Length]]++;
                }
            }
            else if (remainder < 0)
            {
                // We exceeded total due to Min(1). Subtract from largest quotas.
                // Loop until balanced.
                while (remainder < 0)
                {
                    // Find largest quota > 1
                    int bestIdx = -1;
                    int maxQ = -1;

                    for (int i = 0; i < quotas.Length; i++)
                    {
                        if (quotas[i] > 1 && quotas[i] > maxQ)
                        {
                            maxQ = quotas[i];
                            bestIdx = i;
                        }
                    }

                    if (bestIdx == -1)
                    {
                        // Should be impossible if PlateCount <= TotalTiles
                        break;
                    }

                    quotas[bestIdx]--;
                    remainder++;
                }
            }


            // Run fractal flood fill with quotas and dual noise stacks
            FloodFill.Fractal(_map, seeds, ownerMap, _noise6, _noise8, NoiseAWeight, NoiseBWeight, NoiseStrength, quotas, DistancePenalty, NoiseWarping);

            // Apply results to tiles and count tiles per plate
            int[] tileCounts = new int[Plates.Length];
            int unassignedCount = 0;

            // Precompute plate centers for noise score calculation
            System.Numerics.Vector3[] plateCenters = new System.Numerics.Vector3[Plates.Length];
            for (int p = 0; p < Plates.Length; p++)
            {
                var (lat, lon) = _map.Topology.GetTileCenter(Plates[p].SeedTileId);
                plateCenters[p] = SphericalMath.LatLonToHypersphere(lon, lat);
            }

            for (int i = 0; i < _map.Tiles.Length; i++)
            {
                int owner = ownerMap[i];
                if (owner >= 0 && owner < Plates.Length)
                {
                    _map.Tiles[i].PlateId = owner;
                    _map.Tiles[i].CrustType = Plates[owner].CrustType;
                    tileCounts[owner]++;

                    // Calculate and store noise score for debug visualization
                    var (lat, lon) = _map.Topology.GetTileCenter(i);
                    var pos = SphericalMath.LatLonToHypersphere(lon, lat);
                    var offset = SphericalMath.GetDecorrelatedPlateOffset(owner);

                    float n1 = _noise6.GetDomainWarpedNoise(pos.X + offset.X, pos.Y + offset.Y, pos.Z + offset.Z, NoiseWarping);
                    float n2 = _noise8.GetDomainWarpedNoise(pos.X + offset.X + 0.5f, pos.Y + offset.Y + 0.5f, pos.Z + offset.Z + 0.5f, NoiseWarping);
                    float noiseScore = (n1 * NoiseAWeight + n2 * NoiseBWeight) * NoiseStrength;
                    _map.Tiles[i].DebugValue = noiseScore;
                }
                else
                {
                    unassignedCount++;
                    // Fallback handled in FloodFill.Fractal usually, but purely safe side
                }
            }

            // Store tile counts
            for (int i = 0; i < Plates.Length; i++)
            {
                Plates[i].TileCount = tileCounts[i];
            }
        }

        /// <summary>
        /// Generates the plates (Phase 1): Seeding and Flood Fill.
        /// Does NOT run the full pipeline (Landmass, Ages, etc).
        /// </summary>
        public void GeneratePlates()
        {
            bool success = false;
            int attempt = 0;

            while (!success && attempt < MaxRetries)
            {
                attempt++;
                // Reset RNG for this attempt to ensure determinism variation per attempt
                InitializeRng(_seed + attempt * 113);

                // Reset map plate data
                ResetMapData();

                try
                {
                    // 1. Seed plates
                    var seeds = SelectSeeds();

                    // Assign random weights to plates for size variation
                    float[] weights = new float[seeds.Count];
                    for (int i = 0; i < weights.Length; i++) weights[i] = 0.5f + (float)_rng.NextDouble();

                    Plates = CreatePlates(seeds, weights);

                    // 2. Flood fill to assign tiles to plates
                    FloodFillPlates(weights);

                    // Validation
                    if (!ValidatePlates())
                    {
                        Console.WriteLine($"[PlateGenerator] Validation failed on attempt {attempt}. Retrying...");
                        continue;
                    }

                    success = true;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[PlateGenerator] Exception on attempt {attempt}: {e.Message}");
                }
            }

            if (!success)
            {
                Console.WriteLine("[PlateGenerator] Failed to generate valid plates after max retries. Using last attempt.");
            }
        }

        private void ResetMapData()
        {
            for (int i = 0; i < _map.Tiles.Length; i++)
            {
                _map.Tiles[i].PlateId = -1;
                _map.Tiles[i].BoundaryType = BoundaryType.None;
                _map.Tiles[i].CrustAge = 0f;
                // Don't reset everything, just plate related
            }
        }

        private bool ValidatePlates()
        {
            // Check 1: No unassigned tiles (FloodFill handles this, but verify)
            int unassigned = 0;
            for (int i = 0; i < _map.Tiles.Length; i++)
            {
                if (_map.Tiles[i].PlateId == -1) unassigned++;
            }
            if (unassigned > 0) return false;

            // Check 2: Minimum Plate Size
            int minSize = _map.Topology.TileCount / (PlateCount * 5); // Allow small plates but not tiny
            foreach (var p in Plates)
            {
                if (p.TileCount < minSize) return false;
            }

            // Check 3: Connectivity (Optional, expensive DFS, maybe skip for now assuming FloodFill works)

            return true;
        }

        /// <summary>
        /// Selects seed tiles for plates, ensuring they're not adjacent.
        /// </summary>
        private List<int> SelectSeeds()
        {
            Console.WriteLine($"[PlateGenerator.SelectSeeds] Selecting {PlateCount} seeds from {_map.Topology.TileCount} tiles...");

            var seeds = new List<int>();
            var excluded = new HashSet<int>();
            int attempts = 0;
            int maxAttempts = PlateCount * 100;

            while (seeds.Count < PlateCount && attempts < maxAttempts)
            {
                int tileId = _rng.Next(_map.Topology.TileCount);
                attempts++;

                if (excluded.Contains(tileId))
                    continue;

                // Explicit check: must be unassigned (though we just reset)
                if (_map.Tiles[tileId].PlateId != -1) continue;

                // Validation: Double-check tileId is valid
                if (tileId < 0 || tileId >= _map.Topology.TileCount)
                {
                    Console.WriteLine($"[PlateGenerator.SelectSeeds] ERROR: Generated invalid tileId={tileId}");
                    continue;
                }

                // Valid seed - add it
                seeds.Add(tileId);
                excluded.Add(tileId);
                Console.WriteLine($"  Seed {seeds.Count - 1}: tile {tileId}");

                // Exclude neighbors to ensure non-adjacent seeds
                foreach (int neighbor in _map.Topology.GetNeighbors(tileId))
                {
                    excluded.Add(neighbor);
                }
            }

            Console.WriteLine($"[PlateGenerator.SelectSeeds] Selected {seeds.Count} seeds in {attempts} attempts");
            return seeds;
        }

        /// <summary>
        /// Creates plate objects from seed tiles.
        /// </summary>
        private TectonicPlate[] CreatePlates(List<int> seeds, float[] weights)
        {
            Console.WriteLine($"[PlateGenerator.CreatePlates] Creating {seeds.Count} plates...");

            var plates = new TectonicPlate[seeds.Count];
            int continentalCount = (int)(seeds.Count * ContinentalRatio);

            for (int i = 0; i < seeds.Count; i++)
            {
                // Assign continental to first N plates based on ratio
                byte crustType = (byte)(i < continentalCount ? 1 : 0);
                plates[i] = new TectonicPlate(i, seeds[i], crustType);
                Console.WriteLine($"  Plate {i}: seed={seeds[i]}, type={(crustType == 1 ? "Continental" : "Oceanic")}");

                // Store weight if TectonicPlate has it (it doesn't in current def, but used in fill)

                // Mark seed tile with plate ID
                _map.Tiles[seeds[i]].PlateId = i;
            }

            return plates;
        }



        /// <summary>
        /// Assigns random velocities to each plate.
        /// </summary>
        public void AssignVelocitiesAndCrust()
        {
            for (int i = 0; i < Plates.Length; i++)
            {
                // Random direction (0 to 2π)
                float angle = (float)(_rng.NextDouble() * Math.PI * 2);
                // Random speed (0.2 to 1.0 abstract units)
                float speed = 0.2f + (float)_rng.NextDouble() * 0.8f;

                Plates[i].Velocity = new Vector2(
                    MathF.Cos(angle) * speed,
                    MathF.Sin(angle) * speed
                );
            }
        }

        /// <summary>
        /// Classifies boundary tiles as Convergent/Divergent/Transform.
        /// </summary>
        public void ClassifyBoundaries()
        {
            for (int i = 0; i < _map.Tiles.Length; i++)
            {
                int plateId = _map.Tiles[i].PlateId;
                if (plateId < 0) continue;

                var neighbors = _map.Topology.GetNeighbors(i);
                bool isBoundary = false;
                BoundaryType maxBoundaryType = BoundaryType.None;

                foreach (int n in neighbors)
                {
                    int neighborPlateId = _map.Tiles[n].PlateId;

                    if (neighborPlateId >= 0 && neighborPlateId != plateId)
                    {
                        isBoundary = true;

                        // Calculate relative velocity
                        var plate = Plates[plateId];
                        var neighborPlate = Plates[neighborPlateId];

                        // Get boundary normal (direction from this tile to neighbor)
                        var (lat1, lon1) = _map.Topology.GetTileCenter(i);
                        var (lat2, lon2) = _map.Topology.GetTileCenter(n);
                        var pos1 = SphericalMath.LatLonToHypersphere(lon1, lat1);
                        var pos2 = SphericalMath.LatLonToHypersphere(lon2, lat2);

                        var edge = new Vector2(
                            (float)(pos2.X - pos1.X),
                            (float)(pos2.Y - pos1.Y)
                        );
                        float edgeLen = edge.Length();
                        if (edgeLen > 0.001f)
                        {
                            edge /= edgeLen;

                            // Relative velocity
                            var relVel = plate.Velocity - neighborPlate.Velocity;

                            // Dot product with edge direction
                            float dot = Vector2.Dot(relVel, edge);

                            // Classify
                            BoundaryType boundaryType;
                            if (MathF.Abs(dot) < 0.2f)
                            {
                                boundaryType = BoundaryType.Transform;
                            }
                            else if (dot > 0)
                            {
                                boundaryType = BoundaryType.Convergent;
                            }
                            else
                            {
                                boundaryType = BoundaryType.Divergent;
                            }

                            // Keep highest priority boundary type
                            if ((int)boundaryType > (int)maxBoundaryType)
                            {
                                maxBoundaryType = boundaryType;
                            }
                        }
                    }
                }

                if (isBoundary)
                {
                    _map.Tiles[i].BoundaryType = maxBoundaryType;
                    _map.Tiles[i].SetFlag(TileFlags.IsBoundary);

                    // Set specific boundary flags
                    switch (maxBoundaryType)
                    {
                        case BoundaryType.Convergent:
                            _map.Tiles[i].SetFlag(TileFlags.IsConvergent);
                            break;
                        case BoundaryType.Divergent:
                            _map.Tiles[i].SetFlag(TileFlags.IsDivergent);
                            break;
                        case BoundaryType.Transform:
                            _map.Tiles[i].SetFlag(TileFlags.IsTransform);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Sets base elevation based on crust type.
        /// </summary>
        /// <summary>
        /// Generates landmasses within plates using the Gleba-style pipeline.
        /// Replaces simple "Continental/Oceanic" boolean with detailed crust generation.
        /// </summary>
        public void GenerateLandmass()
        {
            Console.WriteLine("[PlateGenerator] Generating Landmass...");

            // 1. Calculate Land Quotas per Plate
            // 1. Calculate Land Quotas per Plate (Strict Gleba Specs)
            // Types: 0x100 (Cont bound), 0x200 (Large Island), 0x300 (Chain), 0x400 (Oceanic)
            // Quotas: 75%, 27.5%, 4%, 1.5%

            // Assign types purely for this landmass pass (round-robin or weighted)
            // Ideally this happens in CreatePlates, but we can retro-fit here for now.

            var plateLandQuotas = new int[Plates.Length];
            int totalTiles = _map.Topology.TileCount;

            // Ensure determinism in type assignment
            var typeRng = new Random(_seed + 555);

            for (int i = 0; i < Plates.Length; i++)
            {
                float fraction = 0.015f; // Default Oceanic (0x400)
                int typeId = 0x400;

                // Determine type based on previous "IsContinental" flag or Random
                // We use the existing ContinentalRatio to decide who gets the big chunks
                if (Plates[i].IsContinental)
                {
                    // Split Continentals into "True Continental" (0x100) and "Large Island" (0x200)
                    if (typeRng.NextDouble() > 0.3) // 70% chance of full continent
                    {
                        typeId = 0x100;
                        fraction = 0.75f;
                    }
                    else
                    {
                        typeId = 0x200;
                        fraction = 0.275f;
                    }
                }
                else
                {
                    // Oceanic plates can be "Island Chain" (0x300) or "Oceanic" (0x400)
                    if (typeRng.NextDouble() > 0.7) // 30% chance of chains
                    {
                        typeId = 0x300;
                        fraction = 0.04f;
                    }
                    else
                    {
                        typeId = 0x400;
                        fraction = 0.015f;
                    }
                }

                int quota = (int)(Plates[i].TileCount * fraction);
                plateLandQuotas[i] = quota;

                // Store type for future reference if needed (hacky storage in Debug flags for now?)
                // Just log it.
                Console.WriteLine($"  Plate {i}: Type 0x{typeId:X} Quota {quota} ({fraction * 100:F1}%)");
            }

            // 2. Select Land Seeds
            var landSeeds = new List<int>();
            var landOwnerMap = new int[_map.Topology.TileCount];
            Array.Fill(landOwnerMap, -1);

            for (int p = 0; p < Plates.Length; p++)
            {
                if (plateLandQuotas[p] <= 0) continue;

                // Gleba algo implies picking specific seeds, but logic works with random internal seeds
                // We keep 1 seed per ~150 tiles to ensure shape coverage for large quotas
                int seedsNeeded = Math.Max(1, plateLandQuotas[p] / 150);
                int found = 0;
                int attempts = 0;

                while (found < seedsNeeded && attempts < seedsNeeded * 20)
                {
                    attempts++;
                    int tile = _rng.Next(totalTiles);
                    if (_map.Tiles[tile].PlateId != p) continue;

                    landSeeds.Add(tile);
                    landOwnerMap[tile] = p;
                    found++;
                }
            }
            int targetLandTotal = 0;
            foreach (var q in plateLandQuotas) targetLandTotal += q;
            Console.WriteLine($"  Selected {landSeeds.Count} land seeds.");

            // 3. Constrained Fractal Flood Fill (Generate Crust)
            // Use FloodFill.Fractal but *constrained* to the PlateId.
            // This fills "Land" within the "Plate".

            int[] crustOwnerMap = new int[totalTiles];
            Array.Fill(crustOwnerMap, -1);

            // Constraint: Can only expand to n if n.PlateId == owner of current
            Func<int, int, bool> crustConstraint = (ownerPlateId, targetTileId) =>
            {
                return _map.Tiles[targetTileId].PlateId == ownerPlateId;
            };

            // We treat "owner" in flood fill as the Plate ID.
            // Since we might have multiple seeds for the same Plate ID, FloodFill.Fractal needs to handle that.
            // Fortunately FloodFill.Fractal takes a list of seeds and an owner map.
            // But if multiple seeds have same OwnerID (PlateID), they merge. That's exactly what we want.

            // Setup dedicated noise for land shapes (Gleba: 6 octaves)
            var landNoiseA = new SimplexNoise(_seed + 5000, 6, 0.5f, 2.0943951f, 1.0f);
            var landNoiseB = new SimplexNoise(_seed + 6000, 6, 0.5f, 2.0943951f, 2.0f); // Backup/Detail (optional)

            // Run Fill
            FloodFill.Fractal(
                _map,
                landSeeds,
                crustOwnerMap,
                landNoiseA,
                landNoiseB,
                1.0f, 0.5f, 2.0f,
                plateLandQuotas,
                0.2f, // Lower distance penalty for blobby shapes
                0.5f, // Warp
                crustConstraint,
                _rng
            );

            // 4. Commit Land & Base Elevation (Gleba Style)
            // Gleba: "Initialize tiles with small positive elevation + land flag"
            int landCount = 0;
            for (int i = 0; i < totalTiles; i++)
            {
                // Reset first to Ocean defaults
                _map.Tiles[i].IsLand = false;
                _map.Tiles[i].Elevation = OceanBase; // -4000
                _map.Tiles[i].SetFlag(TileFlags.IsOceanic);

                if (crustOwnerMap[i] != -1)
                {
                    // It is "Crust" (Potential Land)
                    // Gleba: tile.elev = 10.0 + jitter
                    float jitter = ((float)_rng.NextDouble() * 0.01f);
                    float baseElev = 10.0f + jitter;

                    _map.Tiles[i].Elevation = baseElev;

                    // Final Land Flag is determined by Elevation >= 0
                    if (_map.Tiles[i].Elevation >= 0)
                    {
                        _map.Tiles[i].IsLand = true;
                        landCount++;

                        // Copy continental flag from plate
                        int plateId = _map.Tiles[i].PlateId;
                        if (plateId >= 0 && Plates[plateId].IsContinental)
                            _map.Tiles[i].SetFlag(TileFlags.IsContinental);
                        else
                            // It's land but on oceanic plate (island)
                            _map.Tiles[i].SetFlag(TileFlags.IsOceanic);
                    }
                }
            }
            Console.WriteLine($"  Generated {landCount} land tiles (Target: {targetLandTotal})");

            // 5. Land Shaping (Coast -> Inland)
            // Use AreaSelector to identify land regions and Stamper to raise them.
            // Since we can have many islands, we need to select "all land" and distance-field it?
            // Actually, Gleba suggests: "initialize_land_elevation" uses a selector.
            // We can iterate all land tiles, find "Coast" (land with water neighbor), and BFS inwards.

            ShapeLandElevation();
        }

        private void ShapeLandElevation()
        {
            Console.WriteLine("[PlateGenerator] Shaping Land Elevation (AreaSelector)...");

            // 1. Identify "Coast" seeds (Land tiles next to Water)
            var coastSeeds = new List<int>();
            for (int i = 0; i < _map.Topology.TileCount; i++)
            {
                if (!_map.Tiles[i].IsLand) continue;

                var neighborsSpan = _map.Topology.GetNeighbors(i);
                bool isCoast = false;
                foreach (int n in neighborsSpan)
                {
                    if (!_map.Tiles[n].IsLand)
                    {
                        isCoast = true;
                        break;
                    }
                }

                if (isCoast)
                {
                    coastSeeds.Add(i);
                    // Explicitly set coast elevation low
                    _map.Tiles[i].Elevation = 10f;
                }
            }

            // 2. Use AreaSelector to grow from coast inland
            // This computes a "Distance Field" (score) from coast
            var selector = new Aethelgard.Simulation.Systems.Selectors.AreaSelector(_seed + 9999)
            {
                RequireLand = true,
                ConstrainToPlate = false, // We want to grow across land regardless of plate (it's fused now)
                MaxScore = 150f, // Max search depth (approx tiles inland)
                MinStep = 1.0f,
                MaxStep = 1.0f, // Uniform step for pure distance field
            };

            var distanceField = selector.Select(_map, coastSeeds);

            // 3. Apply Elevation based on Distance
            foreach (var kvp in distanceField)
            {
                int tileId = kvp.Key;
                float distance = kvp.Value;

                // If it's a seed (distance 0), we already handled it or it stays low
                if (distance <= 0) continue;

                // Gleba Formula: Elevation = (distance / 7500.0) * 250.0
                // NOTE: 'distance' in AreaSelector is in tiles (1, 2, 3...). 
                // If the formula assumes distance in meters (e.g. 1 tile = 10km = 10000m), we need to scale.
                // Assuming standard "large map" logic, we scale steps by a large factor or assume formula meant 'steps / max_steps * height'.
                // Given the explicit constants, we'll try to match the slope: 250/7500 = 1/30.
                // So for every 30 units of distance, we gain 1 unit of height? No, that's very flat.
                // If distance is Meters, and one tile is arguably ~5-10km on a planet scale...
                // Let's interpret '7500' as the range in Meters where height reaches 250m.
                // We'll treat 1 tile = 1000 "Distance Units" for now to make the gradient visible.

                float distanceUnits = distance * 1000f;
                float ramp = (distanceUnits / 7500f) * 250f;

                // Add Detail Noise
                var (lat, lon) = _map.Topology.GetTileCenter(tileId);
                var pos = SphericalMath.LatLonToHypersphere(lon, lat);
                float noise = _noise.GetNoise(pos.X * 5f, pos.Y * 5f, pos.Z * 5f) * 50f; // Reduced noise amplitude to not overpower the ramp

                // Set Elevation
                _map.Tiles[tileId].Elevation = 10f + ramp + noise; // Base 10f + ramp
            }
        }

        private float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

        /// <summary>
        /// Determines crust age via BFS from divergent boundaries.
        /// </summary>
        public void DetermineCrustAge()
        {
            // Find all divergent boundary tiles (age = 0)
            var divergentTiles = new List<int>();

            for (int i = 0; i < _map.Tiles.Length; i++)
            {
                if (_map.Tiles[i].BoundaryType == BoundaryType.Divergent)
                {
                    divergentTiles.Add(i);
                    _map.Tiles[i].CrustAge = 0f;
                }
                else
                {
                    _map.Tiles[i].CrustAge = float.MaxValue;
                }
            }

            if (divergentTiles.Count == 0) return;

            // Shuffle divergent tiles to avoid directional bias in BFS expansion from array order
            // Fisher-Yates shuffle
            for (int i = divergentTiles.Count - 1; i > 0; i--)
            {
                int k = _rng.Next(i + 1);
                (divergentTiles[i], divergentTiles[k]) = (divergentTiles[k], divergentTiles[i]);
            }

            // BFS from divergent boundaries
            var queue = new Queue<int>(divergentTiles);
            var visited = new HashSet<int>(divergentTiles);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int currentPlate = _map.Tiles[current].PlateId;
                float currentAge = _map.Tiles[current].CrustAge;

                foreach (int n in _map.Topology.GetNeighbors(current))
                {
                    // Only propagate within same plate
                    if (_map.Tiles[n].PlateId != currentPlate)
                        continue;

                    // Use physical distance for age increment to avoid grid artifacts
                    float dist = Vector3.Distance(
                        _map.Topology.GetTilePosition(current),
                        _map.Topology.GetTilePosition(n)
                    );

                    // Age Scale: 1 unit of distance ≈ how much age?
                    // Sphere radius is 1. Max distance is 2.
                    // Let's say max age is reached quickly near ridge, slowly far away?
                    // Standard: Age = Distance / spread_factor
                    float newAge = currentAge + (dist * 2.5f); // 2.5 factor -> reaches 1.0 in ~0.4 units (approx 35 degrees)

                    if (newAge < _map.Tiles[n].CrustAge)
                    {
                        _map.Tiles[n].CrustAge = MathF.Min(newAge, 1.0f);
                        if (!visited.Contains(n))
                        {
                            visited.Add(n);
                            queue.Enqueue(n);
                        }
                    }
                }
            }

            // Normalize ages that weren't reached
            for (int i = 0; i < _map.Tiles.Length; i++)
            {
                if (_map.Tiles[i].CrustAge == float.MaxValue)
                {
                    _map.Tiles[i].CrustAge = 1.0f; // Max age
                }
            }
        }

        /// <summary>
        /// Assigns initial rock types based on crust type.
        /// </summary>
        public void AssignRockTypes()
        {
            for (int i = 0; i < _map.Tiles.Length; i++)
            {
                int plateId = _map.Tiles[i].PlateId;
                if (plateId < 0)
                {
                    _map.Tiles[i].RockType = RockType.None;
                    continue;
                }

                bool isContinental = Plates[plateId].IsContinental;

                // Sample position for noise variation
                var (lat, lon) = _map.Topology.GetTileCenter(i);
                var pos = SphericalMath.LatLonToHypersphere(lon, lat);
                float noiseVal = _noise.GetNoise(pos.X * 3f, pos.Y * 3f, pos.Z * 3f);

                if (isContinental)
                {
                    // Continental: Granite, Gneiss, Sandstone, Limestone
                    float r = (noiseVal + 1f) * 0.5f; // Normalize to 0-1
                    if (r < 0.4f)
                        _map.Tiles[i].RockType = RockType.Granite;
                    else if (r < 0.6f)
                        _map.Tiles[i].RockType = RockType.Gneiss;
                    else if (r < 0.8f)
                        _map.Tiles[i].RockType = RockType.Sandstone;
                    else
                        _map.Tiles[i].RockType = RockType.Limestone;
                }
                else
                {
                    // Oceanic: Basalt, Gabbro
                    float r = (noiseVal + 1f) * 0.5f;
                    if (r < 0.7f)
                        _map.Tiles[i].RockType = RockType.Basalt;
                    else
                        _map.Tiles[i].RockType = RockType.Gabbro;
                }
            }
        }
        /// <summary>
        /// Generates microplates/terranes within the existing macro plates.
        /// </summary>
        /// <param name="microplatesPerPlate">Target number of microplates per macro plate.</param>
        public void GenerateMicroplates(int microplatesPerPlate = 3)
        {
            if (microplatesPerPlate <= 0) return;

            Console.WriteLine($"[PlateGenerator] Generating ~{microplatesPerPlate} microplates per macro plate...");

            var microSeeds = new List<int>();
            var microplateParentMap = new List<int>(); // map: global microplate index -> parent macro plate ID

            // 1. Select seeds using Reservoir Sampling per Plate
            var reservoirs = new int[Plates.Length][];
            for (int i = 0; i < Plates.Length; i++)
            {
                reservoirs[i] = new int[microplatesPerPlate];
                for (int k = 0; k < microplatesPerPlate; k++) reservoirs[i][k] = -1;
            }
            int[] counts = new int[Plates.Length];

            // Single pass over all tiles to find candidates
            for (int i = 0; i < _map.Tiles.Length; i++)
            {
                int pid = _map.Tiles[i].PlateId;
                if (pid >= 0 && pid < Plates.Length)
                {
                    int count = counts[pid];
                    if (count < microplatesPerPlate)
                    {
                        reservoirs[pid][count] = i;
                    }
                    else
                    {
                        // Standard reservoir replace
                        // k is uniform [0, count]
                        int r = _rng.Next(count + 1);
                        if (r < microplatesPerPlate)
                        {
                            reservoirs[pid][r] = i;
                        }
                    }
                    counts[pid]++;
                }
            }

            // Convert reservoirs to final seed list
            for (int p = 0; p < Plates.Length; p++)
            {
                for (int k = 0; k < microplatesPerPlate; k++)
                {
                    int seed = reservoirs[p][k];
                    if (seed != -1)
                    {
                        // Verify seed ownership immediately
                        if (_map.Tiles[seed].PlateId != p)
                        {
                            Console.WriteLine($"[PlateGenerator] SEED ERROR: Microplate seed {seed} has PlateId {_map.Tiles[seed].PlateId}, expected {p}!");
                        }

                        microSeeds.Add(seed);
                        microplateParentMap.Add(p); // Store which macro plate owns this seed
                    }
                }
            }

            Console.WriteLine($"[PlateGenerator] Seeds selected. {microSeeds.Count} seeds for {Plates.Length} plates.");

            // 2. Run Constrained Flood Fill
            int[] outputMicroMap = new int[_map.Tiles.Length];
            Array.Fill(outputMicroMap, -1);

            // Constraint: target tile must belong to the same macro plate as the microplate's owner
            bool Constraint(int ownerIdx, int targetTileId)
            {
                int parentPlateId = microplateParentMap[ownerIdx];
                return _map.Tiles[targetTileId].PlateId == parentPlateId;
            }

            // Reuse existing noise stacks but maybe higher frequency?
            // For now, reuse same noise logic (organic growth).

            FloodFill.Fractal(
                _map,
                microSeeds,
                outputMicroMap,
                _noise6,
                _noise8,
                NoiseAWeight,
                NoiseBWeight,
                NoiseStrength,
                null, // No quotas
                DistancePenalty,
                NoiseWarping,
                Constraint
            );

            // 3. Assign Microplate IDs
            int assignedCount = 0;
            int violationCount = 0;
            for (int i = 0; i < _map.Tiles.Length; i++)
            {
                int mOwner = outputMicroMap[i];
                if (mOwner != -1)
                {
                    _map.Tiles[i].MicroplateId = mOwner;
                    assignedCount++;

                    // Verify Constraint
                    int parentPlate = microplateParentMap[mOwner];
                    int actualPlate = _map.Tiles[i].PlateId;
                    if (parentPlate != actualPlate)
                    {
                        violationCount++;
                        // Optional: Enforce correction?
                        // _map.Tiles[i].MicroplateId = -1; // Remove violator?
                    }
                }
                else
                {
                    _map.Tiles[i].MicroplateId = -1;
                }
            }
            Console.WriteLine($"[PlateGenerator] Microplates generated. Assigned {assignedCount} tiles.");
            if (violationCount > 0)
            {
                Console.WriteLine($"[PlateGenerator] WARNING: Found {violationCount} tiles where Microplate crossed Macro border!");
            }
            else
            {
                Console.WriteLine($"[PlateGenerator] SUCCESS: 0 Constraint violations found.");
            }
        }
    }
}

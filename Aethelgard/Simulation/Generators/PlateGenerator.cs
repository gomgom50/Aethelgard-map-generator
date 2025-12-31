using System;
using System.Collections.Generic;
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
        private readonly Random _rng;
        private readonly FractalNoise _noise;

        /// <summary>Generated plates after running generation.</summary>
        public TectonicPlate[] Plates { get; private set; } = Array.Empty<TectonicPlate>();

        // Configuration
        public int PlateCount { get; set; } = 12;
        public float ContinentalRatio { get; set; } = 0.4f;
        public float NoiseStrength { get; set; } = 1.0f;

        /// <summary>
        /// Creates a new plate generator for the given world map.
        /// </summary>
        public PlateGenerator(WorldMap map, int seed)
        {
            _map = map;
            _rng = new Random(seed);
            _noise = new FractalNoise(
                seed: seed + 1000,
                octaves: 6,
                persistence: 0.5f,
                lacunarity: 2.0f,
                scale: 1.5f
            );
        }

        /// <summary>
        /// Runs the complete plate generation pipeline.
        /// </summary>
        public void Generate()
        {
            // 1. Seed plates
            var seeds = SelectSeeds();
            Plates = CreatePlates(seeds);

            // 2. Flood fill to assign tiles to plates
            FloodFillPlates();

            // 3. Assign velocities and crust types
            AssignVelocitiesAndCrust();

            // 4. Classify boundaries
            ClassifyBoundaries();

            // 5. Assign base elevation
            AssignBaseElevation();

            // 6. Determine crust age
            DetermineCrustAge();

            // 7. Assign rock types
            AssignRockTypes();
        }

        /// <summary>
        /// Selects seed tiles for plates, ensuring they're not adjacent.
        /// </summary>
        private List<int> SelectSeeds()
        {
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

                // Valid seed - add it
                seeds.Add(tileId);
                excluded.Add(tileId);

                // Exclude neighbors to ensure non-adjacent seeds
                foreach (int neighbor in _map.Topology.GetNeighbors(tileId))
                {
                    excluded.Add(neighbor);
                }
            }

            return seeds;
        }

        /// <summary>
        /// Creates plate objects from seed tiles.
        /// </summary>
        private TectonicPlate[] CreatePlates(List<int> seeds)
        {
            var plates = new TectonicPlate[seeds.Count];
            int continentalCount = (int)(seeds.Count * ContinentalRatio);

            for (int i = 0; i < seeds.Count; i++)
            {
                // Assign continental to first N plates based on ratio
                byte crustType = (byte)(i < continentalCount ? 1 : 0);
                plates[i] = new TectonicPlate(i, seeds[i], crustType);

                // Mark seed tile with plate ID
                _map.Tiles[seeds[i]].PlateId = i;
            }

            return plates;
        }

        /// <summary>
        /// Expands plates from seeds using fractal flood fill.
        /// </summary>
        private void FloodFillPlates()
        {
            // Initialize owner map with -1 (unassigned)
            int[] ownerMap = new int[_map.Topology.TileCount];
            Array.Fill(ownerMap, -1);

            // Mark seeds
            var seeds = new List<int>();
            for (int i = 0; i < Plates.Length; i++)
            {
                int seedTile = Plates[i].SeedTileId;
                ownerMap[seedTile] = i;
                seeds.Add(seedTile);
            }

            // Run fractal flood fill
            FloodFill.Fractal(_map, seeds, ownerMap, _noise, NoiseStrength);

            // Apply results to tiles and count tiles per plate
            int[] tileCounts = new int[Plates.Length];
            int unassignedCount = 0;

            for (int i = 0; i < _map.Tiles.Length; i++)
            {
                int owner = ownerMap[i];
                if (owner >= 0 && owner < Plates.Length)
                {
                    _map.Tiles[i].PlateId = owner;
                    _map.Tiles[i].CrustType = Plates[owner].CrustType;
                    tileCounts[owner]++;
                }
                else
                {
                    unassignedCount++;
                }
            }

            // Fix unassigned tiles by assigning to nearest assigned neighbor
            if (unassignedCount > 0)
            {
                bool changed = true;
                int passes = 0;
                while (changed && passes < 100)
                {
                    changed = false;
                    passes++;

                    for (int i = 0; i < _map.Tiles.Length; i++)
                    {
                        if (_map.Tiles[i].PlateId >= 0) continue; // Already assigned

                        // Find first assigned neighbor
                        foreach (int n in _map.Topology.GetNeighbors(i))
                        {
                            int neighborPlate = _map.Tiles[n].PlateId;
                            if (neighborPlate >= 0)
                            {
                                _map.Tiles[i].PlateId = neighborPlate;
                                _map.Tiles[i].CrustType = Plates[neighborPlate].CrustType;
                                tileCounts[neighborPlate]++;
                                changed = true;
                                break;
                            }
                        }
                    }
                }
            }

            // Store tile counts
            for (int i = 0; i < Plates.Length; i++)
            {
                Plates[i].TileCount = tileCounts[i];
            }

            // DEBUG: Check North Pole Tile
            int northPoleId = _map.Topology.GetTileAtLatLon(90f, 0f);
            ref var poleTile = ref _map.Tiles[northPoleId];
            var poleNeighbors = _map.Topology.GetNeighbors(northPoleId);
            var (pLat, pLon) = _map.Topology.GetTileCenter(northPoleId);

            Console.WriteLine($"[DEBUG] North Pole Tile ID: {northPoleId}");
            Console.WriteLine($"[DEBUG] Lat: {pLat}, Lon: {pLon}");
            Console.WriteLine($"[DEBUG] IsPentagon: {_map.Topology.IsPentagon(northPoleId)}");
            Console.WriteLine($"[DEBUG] PlateId: {poleTile.PlateId}");
            Console.WriteLine($"[DEBUG] Neighbor Count: {poleNeighbors.Length}");
            foreach (var n in poleNeighbors)
            {
                var nTile = _map.Tiles[n];
                var (nLat, nLon) = _map.Topology.GetTileCenter(n);
                Console.WriteLine($"  - Neighbor {n}: PlateId={nTile.PlateId}, Lat={nLat:F2}");
            }
        }

        /// <summary>
        /// Assigns random velocities to each plate.
        /// </summary>
        private void AssignVelocitiesAndCrust()
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
        private void ClassifyBoundaries()
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
        private void AssignBaseElevation()
        {
            for (int i = 0; i < _map.Tiles.Length; i++)
            {
                int plateId = _map.Tiles[i].PlateId;
                if (plateId < 0) continue;

                bool isContinental = Plates[plateId].IsContinental;

                // Sample position for noise
                var (lat, lon) = _map.Topology.GetTileCenter(i);
                var pos = SphericalMath.LatLonToHypersphere(lon, lat);
                float noiseVal = _noise.GetNoise(pos.X * 5f, pos.Y * 5f, pos.Z * 5f);

                float baseElev;
                if (isContinental)
                {
                    // Continental: 200m to 1500m
                    baseElev = 200f + (0.5f + noiseVal * 0.5f) * 1300f;
                    _map.Tiles[i].IsLand = true;
                    _map.Tiles[i].CrustThickness = 35f; // Continental crust ~35km
                    _map.Tiles[i].SetFlag(TileFlags.IsContinental);
                }
                else
                {
                    // Oceanic: -5000m to -2000m
                    baseElev = -5000f + (0.5f + noiseVal * 0.5f) * 3000f;
                    _map.Tiles[i].IsLand = false;
                    _map.Tiles[i].CrustThickness = 7f; // Oceanic crust ~7km
                    _map.Tiles[i].SetFlag(TileFlags.IsOceanic);
                }

                _map.Tiles[i].Elevation = baseElev;
            }
        }

        /// <summary>
        /// Determines crust age via BFS from divergent boundaries.
        /// </summary>
        private void DetermineCrustAge()
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

                    float newAge = currentAge + 0.02f; // Increment per hop (normalized)
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
        private void AssignRockTypes()
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
    }
}

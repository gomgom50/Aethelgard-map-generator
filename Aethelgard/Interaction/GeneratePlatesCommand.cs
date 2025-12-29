using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using System.Linq;
using Aethelgard.Core;
using Aethelgard.Simulation;
using Raylib_cs;

namespace Aethelgard.Interaction
{
    public class GeneratePlatesCommand : ICommand
    {
        private readonly WorldMap _map;
        private readonly PlateGenerationSettings _settings;

        // Undo State: We would need to backup the entire PlateID grid + Plate definitions.
        // For Phase 2 prototype, we might skip deep undo or use a simple backup if memory allows.
        // Let's implement full undo for correctness.
        private int[]? _backupGrid;
        private Dictionary<int, Plate>? _backupPlates;

        public GeneratePlatesCommand(WorldMap map, PlateGenerationSettings settings)
        {
            _map = map;
            _settings = settings;
        }

        public void Execute()
        {
            // Snapshot current state
            _backupGrid = (int[])_map.Lithosphere.PlateIdMap.RawData.Clone();
            _backupPlates = new Dictionary<int, Plate>(_map.Lithosphere.Plates);

            // Centralized Seeding
            int seed = _settings.UseRandomSeed ? new Random().Next() : _settings.Seed;
            // Update settings so UI shows the used seed if it was random
            if (_settings.UseRandomSeed) _settings.Seed = seed;
            Random rnd = new Random(seed);

            if (_settings.Mode == GenerationMode.Random)
            {
                GenerateRandom(rnd);
            }
            else if (_settings.Mode == GenerationMode.FromElevation)
            {
                GenerateFromElevation(rnd);
            }
            else if (_settings.Mode == GenerationMode.Supercontinent)
            {
                GenerateSupercontinent(rnd);
            }
            else if (_settings.Mode == GenerationMode.HexOrganic)
            {
                GenerateHexPlates(rnd);
            }
        }

        private void GenerateSupercontinent(Random rnd)
        {
            // PROTOTYPE 2: Noise-Based Pangea
            // Instead of random points, we generate a high-quality organic heightmap first,
            // then use the "FromElevation" logic to fit plates to it.

            int w = _map.Width;
            int h = _map.Height;
            float centerX = w / 2.0f;
            float centerY = h / 2.0f;

            // Random Offset for Noise to ensure unique shapes each time
            float seedX = (float)(rnd.NextDouble() * 1000.0);
            float seedY = (float)(rnd.NextDouble() * 1000.0);

            // 1. Generate Organic Heightmap
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    // Normalized coords
                    float u = (x - centerX) / (w * 0.5f);
                    float v = (y - centerY) / (h * 0.5f);
                    float dist = (float)Math.Sqrt(u * u + v * v); // 0 to ~1.4

                    // Radial Mask (Soft Circle)
                    // 1.0 at center, 0.0 at edge
                    float mask = 1.0f - (float)Math.Pow(dist, 2.0f);
                    if (mask < 0) mask = 0;

                    // FBM Noise with Seed Offset
                    float noise = SimpleNoise.GetFBM((x + seedX) / 100.0f, (y + seedY) / 100.0f, 4, 0.5f, 2.0f);

                    // Combine
                    // If (Noise * Mask) > Threshold -> Land
                    float val = (noise + 0.5f) * mask;

                    // Set Height & Thickness with isostasy consistency
                    if (val > 0.4f)
                    {
                        // Continental: base thickness 3.0 + variation
                        float thick = 3.0f + val * 0.5f; // Higher val = thicker
                        _map.CrustThickness.Set(x, y, thick);
                        _map.Elevation.Set(x, y, thick - 2.0f); // Isostasy: E = T - 2
                    }
                    else
                    {
                        // Oceanic: base thickness 1.0
                        float thick = 1.0f;
                        _map.CrustThickness.Set(x, y, thick);
                        _map.Elevation.Set(x, y, thick - 2.0f); // E = -1.0
                    }
                }
            });

            // 2. Delegate to the Inverse Generator to build the plates
            GenerateFromElevation(rnd);

            // 3. Assign Divergent Velocities (Breakup)
            // The FromElevation logic sets IsLocked=true. We need to unlock them and push them out.
            Vector2 center = new Vector2(centerX, centerY);
            foreach (var p in _map.Lithosphere.Plates.Values)
            {
                if (p.Type == PlateType.Continental)
                {
                    // Should we unlock? User might want them as Cratons.
                    // But for "Supercontinent Breakup", they must move.
                    // Let's keep them Locked initially? 
                    // No, the user selects "Supercontinent" specifically for Drift.
                    p.IsLocked = false;

                    // Outward Velocity
                    Vector2 dir = Vector2.Normalize(p.Center - center);
                    if (float.IsNaN(dir.X)) dir = new Vector2(1, 0); // Safety
                    p.Velocity = dir * 1.0f; // 1.0 Speed
                }
                else
                {
                    p.Velocity = Vector2.Zero; // Oceans passive
                }
            }
        }

        // ... GenerateFromElevation ...

        private void GenerateFromElevation(Random rnd)
        {
            // 1. Clear Existing Plates
            _map.Lithosphere.Plates.Clear();

            // 2. Identify Land Components (Continents)
            int w = _map.Width;
            int h = _map.Height;
            bool[,] visited = new bool[w, h];
            List<Plate> newPlates = new List<Plate>();
            int nextId = 1;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!visited[x, y] && _map.Elevation.Get(x, y) > 0.0f)
                    {
                        var componentPixels = FloodFill(x, y, w, h, visited);
                        if (componentPixels.Count < 10) continue;

                        // Create Plate (Locked Continental)
                        Vector2 centroid = CalculateCentroid(componentPixels);
                        Color c = new Color(rnd.Next(100, 255), rnd.Next(100, 255), rnd.Next(100, 255), 255);
                        Plate p = new Plate(nextId++, centroid, c);
                        p.Type = PlateType.Continental;
                        p.IsLocked = true;

                        _map.Lithosphere.RegisterPlate(p);
                        newPlates.Add(p);

                        foreach (var pixel in componentPixels)
                        {
                            _map.Lithosphere.PlateIdMap.Set(pixel.X, pixel.Y, p.Id);
                        }
                    }
                }
            }

            // 3. Ocean Filling
            int oceanSeedCount = _settings.TargetPlateCount;
            List<Plate> oceanPlates = new List<Plate>();

            for (int i = 0; i < oceanSeedCount; i++)
            {
                int ox = rnd.Next(0, w);
                int oy = rnd.Next(0, h);
                if (_map.Elevation.Get(ox, oy) <= 0.0f)
                {
                    Color c = new Color(rnd.Next(20, 80), rnd.Next(20, 80), rnd.Next(100, 200), 255);
                    Plate p = new Plate(nextId++, new Vector2(ox, oy), c);
                    p.Type = PlateType.Oceanic;
                    _map.Lithosphere.RegisterPlate(p);
                    oceanPlates.Add(p);
                }
            }

            // 4. Run Voronoi
            FillOceanVoronoi(newPlates, oceanPlates);
        }

        // Helper to replace standard JFA for this specific mode
        private void FillOceanVoronoi(List<Plate> landPlates, List<Plate> oceanPlates)
        {
            // TODO: Implement simple BFS/JFA here or reuse VoronoiGenerator?
            // For now, let's reuse VoronoiGenerator but accept that shapes might drift slightly if we don't use the exact pixel mask.
            // Actually, if we use the FloodFilled pixels as "Seeds" (Set of seeds), we get exact shapes.
            // But VoronoiGenerator takes a list of SINGLE POINTS.

            // FALLBACK: Just use VoronoiGenerator for now. It will approximate the land shape.
            // Ideally we implement the "True Shape" preservation later.
            var allPlates = new List<Plate>();
            allPlates.AddRange(landPlates);
            allPlates.AddRange(oceanPlates);
            VoronoiGenerator.Generate(_map.Lithosphere, allPlates, false, _settings.UseSphericalProjection);
        }

        private Vector2 CalculateCentroid(List<(int X, int Y)> pixels)
        {
            long sumX = 0, sumY = 0;
            foreach (var p in pixels) { sumX += p.X; sumY += p.Y; }
            return new Vector2(sumX / (float)pixels.Count, sumY / (float)pixels.Count);
        }

        private List<(int X, int Y)> FloodFill(int startX, int startY, int w, int h, bool[,] visited)
        {
            var result = new List<(int X, int Y)>();
            var stack = new Stack<(int X, int Y)>();
            stack.Push((startX, startY));

            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                if (x < 0 || x >= w || y < 0 || y >= h) continue;
                if (visited[x, y]) continue;
                if (_map.Elevation.Get(x, y) <= 0.0f) continue; // Boundary

                visited[x, y] = true;
                result.Add((x, y));

                stack.Push((x + 1, y));
                stack.Push((x - 1, y));
                stack.Push((x, y + 1));
                stack.Push((x, y - 1));
            }
            return result;
        }

        private void GenerateRandom(Random rnd)
        {
            // 1. Clear existing
            _map.Lithosphere.Plates.Clear();

            // 2. Over-generate Micro-Plates
            int microPlateCount = _settings.TargetPlateCount * _settings.MicroPlateFactor;

            List<Plate> microPlates = new List<Plate>();

            for (int i = 1; i <= microPlateCount; i++)
            {
                int x = rnd.Next(0, _map.Width);
                int y = rnd.Next(0, _map.Height);
                Color c = new Color(rnd.Next(50, 255), rnd.Next(50, 255), rnd.Next(50, 255), 255);
                Plate p = new Plate(i, new Vector2(x, y), c);

                // Random Velocity
                p.Velocity = new Vector2((float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1));

                // Vary weight for size difference
                float avgArea = (_map.Width * _map.Height) / (float)microPlateCount;
                float maxWeight = avgArea * _settings.WeightVariance;
                p.Weight = (float)rnd.NextDouble() * maxWeight;

                microPlates.Add(p);
                _map.Lithosphere.RegisterPlate(p);
            }

            // 3. Run Initial Voronoi (Smooth / No Warp)
            VoronoiGenerator.Generate(_map.Lithosphere, microPlates, false, _settings.UseSphericalProjection);

            // 4. Build Adjacency Graph & Merge
            MergePlates(microPlates, rnd);

            // 5. Smooth Boundaries (to clean up merge artifacts)
            // Optional: Run cellular automata or mode filter to remove stray pixels

            // 5. Post-Process Distortion (Apply FBM to smooth edges)
            DistortPlates();

            // 6. Assign Crust Types & Base Elevation
            AssignCrustAndElevation(rnd);
        }

        private void AssignCrustAndElevation(Random rnd)
        {
            var plates = _map.Lithosphere.Plates.Values.ToList();
            int total = plates.Count;
            int w = _map.Width;
            int h = _map.Height;

            // LATITUDE BIAS: Calculate plate "equatorial score" based on center Y
            // Plates near poles (top/bottom 15%) are ALWAYS oceanic
            // Other plates get weighted chance of being continental
            float polarZone = 0.15f; // Top/bottom 15% is polar (always ocean)

            // Sort plates by distance from equator (equatorial first)
            var plateSorted = plates.OrderBy(p =>
            {
                float normalizedY = p.Center.Y / h;
                return Math.Abs(normalizedY - 0.5f); // 0 at equator, 0.5 at poles
            }).ToList();

            int continentalCount = (int)(total * _settings.ContinentalRatio);
            int assigned = 0;

            for (int i = 0; i < total; i++)
            {
                var plate = plateSorted[i];
                float normalizedY = plate.Center.Y / h;
                float distFromEquator = Math.Abs(normalizedY - 0.5f); // 0 at equator, 0.5 at poles

                // Force polar plates to be oceanic
                bool isPolar = distFromEquator > (0.5f - polarZone); // Near top or bottom

                bool isCont = false;
                if (!isPolar && assigned < continentalCount)
                {
                    // Weighted probability: closer to equator = higher chance
                    // But since we sorted by distance, we just fill continental quota first
                    isCont = true;
                    assigned++;
                }

                plate.Type = isCont ? PlateType.Continental : PlateType.Oceanic;

                // Random base elevation per plate
                if (isCont)
                    plate.BaseElevation = 0.3f + (float)rnd.NextDouble() * 0.9f;
                else
                    plate.BaseElevation = -1.5f + (float)rnd.NextDouble() * 0.7f;
            }

            // Apply to map: FinalElevation = PlateBase + PixelNoise
            var idMap = _map.Lithosphere.PlateIdMap;
            var elevation = _map.Elevation;
            var thickness = _map.CrustThickness;

            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int id = idMap.Get(x, y);
                    if (id != 0 && _map.Lithosphere.Plates.ContainsKey(id))
                    {
                        var p = _map.Lithosphere.Plates[id];

                        // Per-pixel noise variation (terrain detail)
                        float noise = SimpleNoise.GetFBM(x * 0.015f, y * 0.015f, 3, 0.5f, 2.0f);
                        float pixelVariation = noise * 0.4f; // +/- 0.2 variation

                        // Final elevation = plate base + pixel variation
                        float finalElev = p.BaseElevation + pixelVariation;

                        // Thickness derived from elevation (inverse of isostasy for compatibility)
                        float baseThick = finalElev + 2.0f;
                        baseThick = Math.Clamp(baseThick, 0.5f, 5.0f);

                        elevation.Set(x, y, finalElev);
                        thickness.Set(x, y, baseThick);
                    }
                }
            });
        }

        private void DistortPlates()
        {
            int w = _map.Width;
            int h = _map.Height;
            int[] source = (int[])_map.Lithosphere.PlateIdMap.RawData.Clone();

            // Distortion Settings
            float warpScale = _settings.DistortionScale;
            float warpStr = _settings.DistortionStrength;

            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    // FBM Distortion
                    float ox = SimpleNoise.GetFBM(x * warpScale, y * warpScale, 3) * warpStr;
                    float oy = SimpleNoise.GetFBM((x * warpScale) + 53.0f, (y * warpScale) + 91.0f, 3) * warpStr;

                    // Wrap X, Clamp Y
                    int sampleX = (int)(x + ox);
                    if (sampleX < 0) sampleX = (sampleX % w + w) % w; // Correct neg mod
                    else if (sampleX >= w) sampleX = sampleX % w;

                    int sampleY = Math.Clamp((int)(y + oy), 0, h - 1);

                    _map.Lithosphere.PlateIdMap.Set(x, y, source[sampleY * w + sampleX]);
                }
            });
        }

        private void MergePlates(List<Plate> activePlates, Random rnd)
        {
            // Random rnd = new Random(); // Use passed seeded random
            int currentCount = activePlates.Count;
            int targetCount = _settings.TargetPlateCount;

            // Map from OldID -> ReplacedByID. If Key == Value, it's alive.
            Dictionary<int, int> redirects = new Dictionary<int, int>();
            foreach (var p in activePlates) redirects[p.Id] = p.Id;

            // Helper to find root of a merged set
            int FindRoot(int id)
            {
                while (redirects.ContainsKey(id) && redirects[id] != id) id = redirects[id];
                return id;
            }

            // Loop until we reach target
            // NOTE: We do this iteratively. Re-scanning the grid is expensive, so we scan ONCE to find neighbors,
            // then process the graph.

            // Adjacency: ID -> List<Neighbors>
            var adjacency = BuildAdjacency(activePlates, redirects);

            // List of active roots to pick from
            List<int> activeIds = new List<int>();
            foreach (var p in activePlates) activeIds.Add(p.Id);

            int safety = 0;
            while (currentCount > targetCount && safety++ < 10000)
            {
                // If we run out of mergeable sets (e.g. everything is disconnected)
                if (activeIds.Count <= targetCount) break;

                // Pick random active plate
                int rIdx = rnd.Next(activeIds.Count);
                int predatorId = activeIds[rIdx];

                // Find valid neighbors
                if (!adjacency.ContainsKey(predatorId)) continue;
                var neighbors = adjacency[predatorId];

                // Filter neighbors that are not already merged into us
                var effectiveNeighbors = new List<int>();
                foreach (var nID in neighbors)
                {
                    int root = FindRoot(nID);
                    if (root != predatorId && activeIds.Contains(root)) effectiveNeighbors.Add(root);
                }

                if (effectiveNeighbors.Count == 0) continue;

                // Pick victim
                int victimId = effectiveNeighbors[rnd.Next(effectiveNeighbors.Count)];

                // Merge Victim into Predator
                redirects[victimId] = predatorId;

                // Absorb adjacency (Predator now touches everything Victim touched)
                if (adjacency.ContainsKey(victimId))
                {
                    // Add victim's neighbors to predator's neighbors, avoiding duplicates and self-loops
                    foreach (var victimNeighbor in adjacency[victimId])
                    {
                        int victimNeighborRoot = FindRoot(victimNeighbor);
                        if (victimNeighborRoot != predatorId && !adjacency[predatorId].Contains(victimNeighborRoot))
                        {
                            adjacency[predatorId].Add(victimNeighborRoot);
                        }
                    }
                    adjacency.Remove(victimId); // Victim is no longer an independent entity in the graph
                }

                activeIds.Remove(victimId);
                currentCount--;
            }

            // 5. Finalize Grid & Plates
            // Rewrite the Map IDs
            Parallel.For(0, _map.Lithosphere.PlateIdMap.RawData.Length, i =>
            {
                int oldId = _map.Lithosphere.PlateIdMap.RawData[i];
                if (oldId != 0)
                {
                    _map.Lithosphere.PlateIdMap.RawData[i] = FindRoot(oldId);
                }
            });

            // Clean dictionary
            // Remove plates that were merged
            List<int> toRemove = new List<int>();
            foreach (var p in _map.Lithosphere.Plates.Values)
            {
                if (FindRoot(p.Id) != p.Id) toRemove.Add(p.Id);
            }
            foreach (var id in toRemove) _map.Lithosphere.Plates.Remove(id);
        }

        private Dictionary<int, List<int>> BuildAdjacency(List<Plate> plates, Dictionary<int, int> redirects)
        {
            var adj = new Dictionary<int, List<int>>();
            foreach (var p in plates) adj[p.Id] = new List<int>();

            int w = _map.Width;
            int h = _map.Height;
            var data = _map.Lithosphere.PlateIdMap.RawData;

            // Helper to find root of a merged set
            int FindRoot(int id)
            {
                while (redirects.ContainsKey(id) && redirects[id] != id) id = redirects[id];
                return id;
            }

            // Simple horizontal/vertical scan for boundary pixels
            // Only need to scan X and Y axis neighbors
            // For speed, we can skip some pixels (step 2 or 4), but let's do full since it's "Phase 2"

            // X-Scan
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w - 1; x++)
                {
                    int a = FindRoot(data[y * w + x]);
                    int b = FindRoot(data[y * w + x + 1]);
                    if (a != b && a != 0 && b != 0)
                    {
                        // Add edge a <-> b
                        if (!adj[a].Contains(b)) adj[a].Add(b);
                        if (!adj[b].Contains(a)) adj[b].Add(a);
                    }
                }
            }
            // Y-Scan
            for (int y = 0; y < h - 1; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int a = FindRoot(data[y * w + x]);
                    int b = FindRoot(data[(y + 1) * w + x]);
                    if (a != b && a != 0 && b != 0)
                    {
                        if (!adj[a].Contains(b)) adj[a].Add(b);
                        if (!adj[b].Contains(a)) adj[b].Add(a);
                    }
                }
            }

            // X-Wrap Seam Scan (Right Edge <-> Left Edge)
            for (int y = 0; y < h; y++)
            {
                int a = FindRoot(data[y * w + (w - 1)]); // Rightmost
                int b = FindRoot(data[y * w + 0]);       // Leftmost
                if (a != b && a != 0 && b != 0)
                {
                    if (!adj[a].Contains(b)) adj[a].Add(b);
                    if (!adj[b].Contains(a)) adj[b].Add(a);
                }
            }

            return adj;
        }

        /// <summary>
        /// Hex-based Dijkstra flood fill with noise-weighted costs.
        /// Creates organic, non-geometric plate shapes using the hex topology.
        /// </summary>
        private void GenerateHexPlates(Random rnd)
        {
            var topology = _map.Topology;
            int tileCount = topology.TileCount;
            int plateCount = _settings.TargetPlateCount;
            float ruggedness = _settings.Ruggedness;
            float boundaryThreshold = _settings.BoundaryThreshold;

            // 1. Clear existing plates
            _map.Lithosphere.Plates.Clear();

            // Reset hex tile data
            for (int i = 0; i < tileCount; i++)
            {
                _map.TilePlateId[i] = -1;
                _map.TileBoundary[i] = BoundaryType.None;
                _map.TileDistanceToSeed[i] = float.MaxValue;
            }

            // 2. Pick random seed tiles
            List<Plate> plates = new List<Plate>();
            List<int> seedTiles = new List<int>();

            for (int i = 0; i < plateCount && i < tileCount; i++)
            {
                int candidate = rnd.Next(0, tileCount);
                int attempts = 0;
                while (seedTiles.Contains(candidate) && attempts < 100)
                {
                    candidate = rnd.Next(0, tileCount);
                    attempts++;
                }

                seedTiles.Add(candidate);

                int plateId = i + 1;
                var (lat, lon) = topology.GetTileCenter(candidate);
                var (px, py) = SphericalMath.LatLonToPixel(lon, lat, _map.Width, _map.Height);
                Color color = new Color(rnd.Next(50, 255), rnd.Next(50, 255), rnd.Next(50, 255), 255);

                Plate plate = new Plate(plateId, new Vector2(px, py), color);
                plate.Type = rnd.NextDouble() < _settings.ContinentalRatio ? PlateType.Continental : PlateType.Oceanic;
                plate.BaseRock = plate.Type == PlateType.Continental ? RockType.Granite : RockType.Basalt;
                plate.Velocity = new Vector2((float)(rnd.NextDouble() * 2 - 1), (float)(rnd.NextDouble() * 2 - 1));
                plate.BaseElevation = plate.Type == PlateType.Continental
                    ? 0.3f + (float)rnd.NextDouble() * 0.9f
                    : -1.5f + (float)rnd.NextDouble() * 0.7f;

                plates.Add(plate);
                _map.Lithosphere.RegisterPlate(plate);

                _map.TilePlateId[candidate] = plateId;
                _map.TileDistanceToSeed[candidate] = 0f;
                plate.TileIds.Add(candidate);
            }

            // 3. Dijkstra Flood Fill with noise-weighted costs
            var pq = new SortedSet<(float cost, int tile, int plate)>(
                Comparer<(float cost, int tile, int plate)>.Create((a, b) =>
                {
                    int cmp = a.cost.CompareTo(b.cost);
                    return cmp != 0 ? cmp : a.tile.CompareTo(b.tile);
                })
            );

            for (int i = 0; i < seedTiles.Count; i++)
                pq.Add((0f, seedTiles[i], plates[i].Id));

            float noiseOffsetX = (float)rnd.NextDouble() * 1000f;
            float noiseOffsetY = (float)rnd.NextDouble() * 1000f;

            while (pq.Count > 0)
            {
                var (cost, currentTile, plateId) = pq.Min;
                pq.Remove(pq.Min);

                if (_map.TilePlateId[currentTile] != -1 && _map.TilePlateId[currentTile] != plateId)
                    continue;

                var neighbors = topology.GetNeighbors(currentTile);
                for (int i = 0; i < neighbors.Length; i++)
                {
                    int neighbor = neighbors[i];
                    if (_map.TilePlateId[neighbor] != -1)
                        continue;

                    var (lat, lon) = topology.GetTileCenter(neighbor);
                    float scale = _settings.DistortionScale;
                    // Domain Warped Noise: -1 to 1 (approx)
                    float noise = SimpleNoise.GetDomainWarpedNoise((lon + noiseOffsetX) * scale, (lat + noiseOffsetY) * scale, 3, 4.0f);

                    // Normalize to 0..1
                    float noise01 = (noise + 1.0f) * 0.5f;
                    noise01 = Math.Clamp(noise01, 0f, 1f);

                    // Apply DistortionStrength: High strength = High cost for "peaks", forcing paths into "valleys"
                    // Base cost is 1.0. 
                    // If Strength is 20, Peak cost is 21, Valley cost is 1.
                    // This ratio forces the flood fill to follow the valleys (organic shapes).
                    float strength = _settings.DistortionStrength;
                    float friction = 1.0f + (noise01 * noise01) * strength; // Square it to emphasize peaks further

                    float newCost = cost + friction;

                    _map.TilePlateId[neighbor] = plateId;
                    _map.TileDistanceToSeed[neighbor] = newCost;
                    _map.Lithosphere.Plates[plateId].TileIds.Add(neighbor);

                    pq.Add((newCost, neighbor, plateId));
                }
            }

            // 4. Classify Boundaries
            ClassifyHexBoundaries(plates, boundaryThreshold);

            // 5. Generate Microplates (internal subdivision of continental plates)
            GenerateMicroplates(plates, rnd);

            // 6. Calculate Crust Age (for oceanic depth)
            CalculateCrustAge();

            // 7. Sync to pixel grid
            SyncHexToPixelGrid();
        }

        private void ClassifyHexBoundaries(List<Plate> plates, float threshold)
        {
            var topology = _map.Topology;
            for (int tileId = 0; tileId < topology.TileCount; tileId++)
            {
                int plateId = _map.TilePlateId[tileId];
                if (plateId == -1) continue;

                var neighbors = topology.GetNeighbors(tileId);
                for (int i = 0; i < neighbors.Length; i++)
                {
                    int neighborPlateId = _map.TilePlateId[neighbors[i]];
                    if (neighborPlateId == -1 || neighborPlateId == plateId)
                        continue;

                    var plateA = _map.Lithosphere.Plates[plateId];
                    var plateB = _map.Lithosphere.Plates[neighborPlateId];
                    Vector2 relVelocity = plateA.Velocity - plateB.Velocity;

                    var (latA, lonA) = topology.GetTileCenter(tileId);
                    var (latB, lonB) = topology.GetTileCenter(neighbors[i]);
                    Vector2 direction = new Vector2(lonB - lonA, latB - latA);
                    if (direction.LengthSquared() > 0.0001f)
                        direction = Vector2.Normalize(direction);

                    float pressure = Vector2.Dot(relVelocity, direction);

                    if (pressure > threshold)
                        _map.TileBoundary[tileId] = BoundaryType.Convergent;
                    else if (pressure < -threshold)
                        _map.TileBoundary[tileId] = BoundaryType.Divergent;
                    else if (relVelocity.LengthSquared() > 0.1f)
                        _map.TileBoundary[tileId] = BoundaryType.Transform;
                    break;
                }
            }
        }

        /// <summary>
        /// Subdivides continental plates into microplates (ancient cratons).
        /// Creates internal regions and marks their boundaries for ancient mountain ranges.
        /// </summary>
        private void GenerateMicroplates(List<Plate> plates, Random rnd)
        {
            var topology = _map.Topology;
            int microplateId = 0;
            int microplatesPerContinent = Math.Max(3, _settings.MicroPlateFactor);

            // Process each continental plate
            foreach (var plate in plates)
            {
                if (plate.Type != PlateType.Continental) continue;
                if (plate.TileIds.Count < microplatesPerContinent * 3) continue; // Too small

                // Pick random seed tiles within this plate
                var plateTiles = plate.TileIds.ToList();
                var microSeeds = new List<int>();
                int seedCount = Math.Min(microplatesPerContinent, plateTiles.Count / 10);

                for (int i = 0; i < seedCount; i++)
                {
                    int attempt = 0;
                    int seed = plateTiles[rnd.Next(plateTiles.Count)];

                    // Ensure minimum distance between micro seeds
                    while (attempt++ < 20)
                    {
                        bool tooClose = false;
                        foreach (int existing in microSeeds)
                        {
                            float dist = _map.TileDistanceToSeed[seed];
                            if (dist < 5) // Arbitrary min distance
                            {
                                tooClose = true;
                                break;
                            }
                        }
                        if (!tooClose) break;
                        seed = plateTiles[rnd.Next(plateTiles.Count)];
                    }

                    microSeeds.Add(seed);
                    _map.TileMicroplateId[seed] = microplateId++;
                }

                // Internal flood fill for microplates (restricted to this plate)
                var pq = new SortedSet<(float cost, int tile, int microId)>(
                    Comparer<(float, int, int)>.Create((a, b) =>
                    {
                        int cmp = a.Item1.CompareTo(b.Item1);
                        return cmp != 0 ? cmp : a.Item2.CompareTo(b.Item2);
                    }));

                foreach (int seed in microSeeds)
                {
                    pq.Add((0, seed, _map.TileMicroplateId[seed]));
                }

                while (pq.Count > 0)
                {
                    var (cost, tile, mId) = pq.Min;
                    pq.Remove(pq.Min);

                    if (_map.TileMicroplateId[tile] != -1 && _map.TileMicroplateId[tile] != mId)
                        continue;

                    var neighbors = topology.GetNeighbors(tile);
                    foreach (int neighbor in neighbors)
                    {
                        // Only expand within this plate
                        if (_map.TilePlateId[neighbor] != plate.Id) continue;
                        if (_map.TileMicroplateId[neighbor] != -1) continue;

                        var (lat, lon) = topology.GetTileCenter(neighbor);
                        // Use Domain Warping for consistent style
                        float noise = SimpleNoise.GetDomainWarpedNoise(lon * 0.08f, lat * 0.08f, 2, 4.0f);
                        float newCost = cost + 1.0f + noise * 0.5f;

                        _map.TileMicroplateId[neighbor] = mId;
                        pq.Add((newCost, neighbor, mId));
                    }
                }
            }

            // Detect microplate boundaries (ancient orogeny zones)
            for (int tileId = 0; tileId < topology.TileCount; tileId++)
            {
                int mId = _map.TileMicroplateId[tileId];
                if (mId == -1) continue;

                var neighbors = topology.GetNeighbors(tileId);
                foreach (int neighbor in neighbors)
                {
                    int neighborMId = _map.TileMicroplateId[neighbor];
                    // Same plate but different microplate = ancient boundary
                    if (neighborMId != -1 && neighborMId != mId &&
                        _map.TilePlateId[tileId] == _map.TilePlateId[neighbor])
                    {
                        _map.TileMicroBoundary[tileId] = true;
                        break;
                    }
                }
            }
        }

        private void CalculateCrustAge()
        {
            var topology = _map.Topology;
            var queue = new Queue<int>();
            var visited = new bool[topology.TileCount];

            // 1. Identify Ridge Tiles (Divergent Boundaries)
            for (int i = 0; i < topology.TileCount; i++)
            {
                if (_map.TilePlateId[i] == -1) continue;

                if (_map.TileBoundary[i] == BoundaryType.Divergent)
                {
                    _map.TileAge[i] = 0f;
                    queue.Enqueue(i);
                    visited[i] = true;
                }
                else
                {
                    _map.TileAge[i] = 1.0f; // Default to old
                }
            }

            // 2. BFS Propagate Age
            // We propagate "distance from ridge" which equals age
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                float currentAge = _map.TileAge[current];

                foreach (int neighbor in topology.GetNeighbors(current))
                {
                    if (_map.TilePlateId[neighbor] == -1) continue;

                    // Only propagate within same plate or if it's a valid continued spreading zone
                    // Simplification: Propagate to any unvisited neighbor of the SAME plate
                    if (_map.TilePlateId[neighbor] == _map.TilePlateId[current])
                    {
                        if (!visited[neighbor])
                        {
                            // Increment age
                            // 0.05 per step implies ~20 steps to reach "max age" 1.0
                            // In a large map, this might need dynamic scaling, but linear is a good start
                            float newAge = currentAge + 0.02f;
                            if (newAge > 1.0f) newAge = 1.0f;

                            _map.TileAge[neighbor] = newAge;
                            visited[neighbor] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }
        }

        private void SyncHexToPixelGrid()
        {
            int w = _map.Width;
            int h = _map.Height;

            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int tileId = _map.GetHexTileAt(x, y);
                    int plateId = _map.TilePlateId[tileId];
                    _map.Lithosphere.PlateIdMap.Set(x, y, plateId);

                    if (plateId != -1 && _map.Lithosphere.Plates.ContainsKey(plateId))
                    {
                        var plate = _map.Lithosphere.Plates[plateId];
                        float noise = SimpleNoise.GetDomainWarpedNoise(x * 0.015f, y * 0.015f, 3, 4.0f); // swirly terrain

                        float elev;
                        if (plate.Type == PlateType.Oceanic)
                        {
                            // Oceanic Isostasy: Depth based on Age (Half-space cooling model approx)
                            // New crust (age 0) = Shallow (-2.5km approx)
                            // Old crust (age 1) = Deep (-5.0km approx)
                            float age = _map.TileAge[tileId];
                            float baseDepth = -2.5f - (MathF.Sqrt(age) * 3.0f); // -2.5 to -5.5
                            elev = baseDepth + noise * 0.3f;
                        }
                        else
                        {
                            // Continental
                            elev = plate.BaseElevation + noise * 0.4f;
                        }

                        _map.Elevation.Set(x, y, elev);
                        _map.CrustThickness.Set(x, y, Math.Clamp(elev + 2.0f, 0.5f, 5.0f));
                    }
                }
            });
        }

        public void Undo()
        {
            if (_backupGrid == null || _backupPlates == null) return;

            // Restore Grid
            Array.Copy(_backupGrid, _map.Lithosphere.PlateIdMap.RawData, _backupGrid.Length);

            // Restore Dictionary
            _map.Lithosphere.Plates.Clear();
            foreach (var kvp in _backupPlates)
            {
                _map.Lithosphere.RegisterPlate(kvp.Value);
            }
        }
    }
}

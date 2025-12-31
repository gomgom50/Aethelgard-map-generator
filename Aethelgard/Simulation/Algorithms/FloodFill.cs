using System;
using System.Collections.Generic;
using System.Numerics;
using Aethelgard.Simulation.Core;

namespace Aethelgard.Simulation.Algorithms
{
    public static class FloodFill
    {
        /// <summary>
        /// Simple BFS Flood Fill.
        /// Expands from seeds to all connected tiles matching a predicate.
        /// </summary>
        public static List<int> Simple(WorldMap map, List<int> seeds, Func<int, bool> predicate, int maxTiles = -1)
        {
            var results = new List<int>();
            var queue = new Queue<int>();
            var visited = new HashSet<int>();

            foreach (var seed in seeds)
            {
                if (predicate(seed))
                {
                    visited.Add(seed);
                    queue.Enqueue(seed);
                    results.Add(seed);
                }
            }

            while (queue.Count > 0)
            {
                if (maxTiles > 0 && results.Count >= maxTiles) break;

                int current = queue.Dequeue();
                var neighbors = map.Topology.GetNeighbors(current);

                foreach (int n in neighbors)
                {
                    if (!visited.Contains(n) && predicate(n))
                    {
                        visited.Add(n);
                        queue.Enqueue(n);
                        results.Add(n);
                    }
                }
            }
            return results;
        }

        /// <summary>
        /// Fractal Flood Fill for plate generation.
        /// Priority-Queue based expansion with scoring per Gleba spec:
        /// - Fractal noise for irregular shapes
        /// - Distance penalty from plate center (prevents over-expansion)
        /// - Plate weight boost (configurable plate sizes)
        /// </summary>
        public static void Fractal(
            WorldMap map,
            List<int> seeds,
            int[] outputOwnerMap,
            FractalNoise noise,
            float noiseStrength = 1.0f,
            float[]? plateWeights = null,
            float distancePenaltyWeight = 0.5f)
        {
            // PQ stores (TileId, OwnerId) with priority = -score (lower priority = higher score)
            var pq = new PriorityQueue<(int Tile, int Owner), float>();

            // Track best score per tile (we want max score, so use negative for min-heap)
            float[] bestScores = TransientBufferPool<float>.Get(map.Topology.TileCount);
            Array.Fill(bestScores, float.MinValue);

            // Compute plate centers (average position of seed)
            var plateCenters = new Vector3[seeds.Count];
            for (int i = 0; i < seeds.Count; i++)
            {
                var (lat, lon) = map.Topology.GetTileCenter(seeds[i]);
                plateCenters[i] = SphericalMath.LatLonToHypersphere(lon, lat);
            }

            // Default weights if not provided
            if (plateWeights == null)
            {
                plateWeights = new float[seeds.Count];
                Array.Fill(plateWeights, 1.0f);
            }

            // Seed with initial tiles
            for (int i = 0; i < seeds.Count; i++)
            {
                int seed = seeds[i];
                int owner = outputOwnerMap[seed];
                if (owner == -1) continue;

                bestScores[seed] = float.MaxValue; // Seeds always win
                pq.Enqueue((seed, owner), float.MinValue); // Highest priority
            }

            while (pq.Count > 0)
            {
                if (!pq.TryDequeue(out var item, out float negScore)) break;
                int current = item.Tile;
                int owner = item.Owner;

                // Skip if we've found a better path to this tile
                if (-negScore < bestScores[current] - 0.001f) continue;

                // Claim tile
                outputOwnerMap[current] = owner;

                var neighbors = map.Topology.GetNeighbors(current);
                foreach (int n in neighbors)
                {
                    // Skip already assigned tiles (in multi-source, first to claim wins)
                    if (outputOwnerMap[n] != -1) continue;

                    // Calculate score for this tile from this plate
                    var (lat, lon) = map.Topology.GetTileCenter(n);
                    var pos = SphericalMath.LatLonToHypersphere(lon, lat);

                    // 1. Fractal noise score (higher = more favorable)
                    float nVal = noise.GetNoise(pos.X * 3f, pos.Y * 3f, pos.Z * 3f);
                    float noiseScore = nVal * noiseStrength;

                    // 2. Distance penalty from plate center
                    float dist = Vector3.Distance(pos, plateCenters[owner]);
                    float distScore = -dist * distancePenaltyWeight;

                    // 3. Plate weight boost
                    float weightScore = plateWeights[owner] * 0.1f;

                    // Combined score
                    float score = noiseScore + distScore + weightScore;

                    if (score > bestScores[n])
                    {
                        bestScores[n] = score;
                        pq.Enqueue((n, owner), -score); // Negate for min-heap
                    }
                }
            }

            // Ensure ALL tiles are assigned (fallback for any unreached)
            bool anyUnassigned = true;
            int passes = 0;
            while (anyUnassigned && passes < 50)
            {
                anyUnassigned = false;
                passes++;

                for (int i = 0; i < map.Topology.TileCount; i++)
                {
                    if (outputOwnerMap[i] != -1) continue;
                    anyUnassigned = true;

                    // Assign to first valid neighbor
                    foreach (int n in map.Topology.GetNeighbors(i))
                    {
                        if (outputOwnerMap[n] != -1)
                        {
                            outputOwnerMap[i] = outputOwnerMap[n];
                            break;
                        }
                    }
                }
            }

            TransientBufferPool<float>.Return(bestScores);
        }

        /// <summary>
        /// Weighted Flood Fill.
        /// Expands with a generic cost function.
        /// </summary>
        public static void Weighted(WorldMap map, List<(int Tile, float InitialCost)> seeds, Func<int, int, float> costFunc, Action<int, float> onVisit, float maxCost = float.MaxValue)
        {
            var pq = new PriorityQueue<int, float>();
            float[] costs = TransientBufferPool<float>.Get(map.Topology.TileCount);
            Array.Fill(costs, float.MaxValue);

            foreach (var s in seeds)
            {
                costs[s.Tile] = s.InitialCost;
                pq.Enqueue(s.Tile, s.InitialCost);
            }

            while (pq.TryDequeue(out int current, out float currentCost))
            {
                if (currentCost > costs[current]) continue;
                if (currentCost > maxCost) continue;

                onVisit(current, currentCost);

                var neighbors = map.Topology.GetNeighbors(current);
                foreach (int n in neighbors)
                {
                    float step = costFunc(current, n);
                    if (step < 0) continue; // Impassable

                    float newCost = currentCost + step;
                    if (newCost < costs[n])
                    {
                        costs[n] = newCost;
                        pq.Enqueue(n, newCost);
                    }
                }
            }
            TransientBufferPool<float>.Return(costs);
        }
    }
}

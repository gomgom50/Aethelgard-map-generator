using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
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
        /// Fractal Flood Fill for plate generation (Simplex Noise Overload).
        /// Uses SimplexNoise for organic, grid-artifact-free shapes.
        /// </summary>
        /// <summary>
        /// Fractal Flood Fill for plate generation (Simplex Noise Overload).
        /// Uses SimplexNoise for organic, grid-artifact-free shapes.
        /// </summary>
        public static void Fractal(
            WorldMap map,
            List<int> seeds,
            int[] outputOwnerMap,
            SimplexNoise noiseA,
            SimplexNoise noiseB,
            float weightA = 1.0f,
            float weightB = 1.0f,
            float noiseStrength = 1.0f,
            int[]? plateQuotas = null,
            float distancePenaltyWeight = 0.5f,
            float warpStrength = 0.0f,
            Func<int, int, bool>? constraint = null,
            Random? rng = null,
            bool applyLandElevation = false) // New flag for Continental vs Oceanic
        {
            Console.WriteLine($"[FloodFill.Fractal] Starting (Simplex) with {seeds.Count} seeds, {map.Topology.TileCount} tiles");
            // PQ stores (TileId, OwnerId, PathDistance) with priority = -score
            var pq = new PriorityQueue<(int Tile, int Owner, float Dist), float>();
            long constraintsHit = 0;

            // Optimization: Precompute positions (Sync to avoid thread pool deadlocks)
            // Use local buffer for efficiency if possible or TransientBufferPool if available (using array for now simplicity)
            int tileCount = map.Topology.TileCount;
            Vector3[] tilePositions = new Vector3[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                var (lat, lon) = map.Topology.GetTileCenter(i);
                tilePositions[i] = SphericalMath.LatLonToHypersphere(lon, lat);
            }

            // Quotas (Target sizes)
            int numOwners = seeds.Count;
            if (plateQuotas == null)
            {
                plateQuotas = new int[seeds.Count];
                int fairShare = map.Topology.TileCount / seeds.Count;
                Array.Fill(plateQuotas, fairShare);
            }
            else
            {
                numOwners = plateQuotas.Length;
            }

            // Track current size
            int[] currentCounts = new int[numOwners];
            int totalAssigned = 0;
            int totalQuota = 0;
            foreach (var q in plateQuotas) totalQuota += q;
            int targetTiles = Math.Min(totalQuota, tileCount);

            // Optimization: Precompute Decorrelated Offsets
            Vector3[] plateOffsets = new Vector3[numOwners];
            for (int i = 0; i < numOwners; i++)
            {
                plateOffsets[i] = SphericalMath.GetDecorrelatedPlateOffset(i);
            }

            // Handle Plate Centers for distance penalty
            // In multi-seed mode (Landmass), we might have multiple seeds per owner.
            // Ideally we'd distance to nearest seed, but for now we'll just track A center (e.g. centroid or first seed).
            // Resizing plateCenters to numOwners prevents IndexOutOfRange.
            var plateCenters = new Vector3[numOwners];

            // 1. Initialize Seeds
            for (int i = 0; i < seeds.Count; i++)
            {
                int seed = seeds[i];
                int owner = i;

                // Check if owner is already assigned (Multi-seed support)
                if (outputOwnerMap[seed] != -1)
                {
                    owner = outputOwnerMap[seed];
                }
                else
                {
                    outputOwnerMap[seed] = owner;
                }

                if (owner >= numOwners) continue; // Safety

                // Update Plate Center (Last seed wins for now, sufficient for non-zero penalty)
                plateCenters[owner] = tilePositions[seed];

                if (outputOwnerMap[seed] == owner)
                {
                    currentCounts[owner]++;
                    totalAssigned++;
                }

                // Apply Elevation/Land to Seed if requested
                if (applyLandElevation && rng != null)
                {
                    map.Tiles[seed].IsLand = true;
                    // Formula: 10.0 + (rand * 0.01) + (progress * 0.1)
                    // Progress is approx 0 at start
                    float jitter = (float)rng.NextDouble() * 0.01f;
                    float progressRamp = 0f;
                    map.Tiles[seed].Elevation = 10.0f + jitter + progressRamp;
                }

                var neighbors = map.Topology.GetNeighbors(seed);
                foreach (int n in neighbors)
                {
                    if (outputOwnerMap[n] != -1) continue;

                    // Initial distance is step size
                    float stepDist = Vector3.Distance(tilePositions[seed], tilePositions[n]);

                    Vector3 pos = tilePositions[n];
                    Vector3 plateOffset = plateOffsets[owner];

                    // Gleba: Single 6-octave noise source (configured in PlateGenerator)
                    float noiseVal = noiseA.GetDomainWarpedNoise(pos.X + plateOffset.X, pos.Y + plateOffset.Y, pos.Z + plateOffset.Z, warpStrength);

                    // Score: ((noise * 1.0158 + 1) * 0.35) - distance
                    // noiseVal is approx -1..1
                    float finalScore = (((noiseVal * 1.0158f) + 1.0f) * 0.35f * noiseStrength) - (stepDist * distancePenaltyWeight);

                    pq.Enqueue((n, owner, stepDist), -finalScore);
                }
            }

            // 2. Expansion Loop
            while (pq.TryDequeue(out var item, out float negScore))
            {
                if (totalAssigned >= targetTiles) break;

                int current = item.Tile;
                int owner = item.Owner;
                float currentDist = item.Dist;

                if (currentCounts[owner] >= plateQuotas[owner]) continue;
                if (constraint != null && !constraint(owner, current))
                {
                    constraintsHit++;
                    continue;
                }
                if (outputOwnerMap[current] != -1 && outputOwnerMap[current] != owner) continue;
                if (outputOwnerMap[current] == owner) continue;

                outputOwnerMap[current] = owner;
                currentCounts[owner]++;
                totalAssigned++;

                // Apply Elevation/Land if requested (Continental / Island)
                if (applyLandElevation && rng != null)
                {
                    map.Tiles[current].IsLand = true;
                    // Formula: 10.0 + (rand * 0.01) + (progress * 0.1)
                    float jitter = (float)rng.NextDouble() * 0.01f;

                    // Simple progress ramp: filled / target
                    float saturatedProgress = (float)currentCounts[owner] / (float)plateQuotas[owner]; // 0..1
                    float ramp = saturatedProgress * 0.1f; // Max 0.1m

                    map.Tiles[current].Elevation = 10.0f + jitter + ramp;
                }

                var neighbors = map.Topology.GetNeighbors(current);
                foreach (int n in neighbors)
                {
                    if (outputOwnerMap[n] != -1) continue;
                    if (constraint != null && !constraint(owner, n))
                    {
                        constraintsHit++;
                        continue;
                    }

                    float step = Vector3.Distance(tilePositions[current], tilePositions[n]);
                    float newDist = currentDist + step;

                    Vector3 pos = tilePositions[n];
                    Vector3 plateOffset = plateOffsets[owner];

                    // Gleba: Single 6-octave noise (configured in PlateGen)
                    float noiseVal = noiseA.GetDomainWarpedNoise(pos.X + plateOffset.X, pos.Y + plateOffset.Y, pos.Z + plateOffset.Z, warpStrength);

                    // Score: ((noise * 1.0158 + 1) * 0.35) - distance
                    float finalScore = (((noiseVal * 1.0158f) + 1.0f) * 0.35f * noiseStrength) - (newDist * distancePenaltyWeight);

                    pq.Enqueue((n, owner, newDist), -finalScore);
                }
            }

            // Cleanup pass
            if (constraint != null)
            {
                Console.WriteLine($"[FloodFill.Fractal] Constraints blocked {constraintsHit} expansions.");
            }
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
            FractalNoise noiseA,
            FractalNoise noiseB,
            float weightA = 1.0f,
            float weightB = 1.0f,
            float noiseStrength = 1.0f,
            int[]? plateQuotas = null,
            float distancePenaltyWeight = 0.5f)
        {
            Console.WriteLine($"[FloodFill.Fractal] Starting with {seeds.Count} seeds, {map.Topology.TileCount} tiles");
            Console.WriteLine($"[FloodFill.Fractal] Params: weightA={weightA:F2}, weightB={weightB:F2}, noiseStrength={noiseStrength:F2}, distPenalty={distancePenaltyWeight:F2}");

            // PQ stores (TileId, OwnerId) with priority = -score
            var pq = new PriorityQueue<(int Tile, int Owner), float>();

            // Optimization: Precompute positions (Sync to avoid thread pool deadlocks)
            int tileCount = map.Topology.TileCount;
            // Use standard array to avoid Pool lock issues
            Vector3[] tilePositions = new Vector3[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                var (lat, lon) = map.Topology.GetTileCenter(i);
                tilePositions[i] = SphericalMath.LatLonToHypersphere(lon, lat);
            }

            // Compute plate centers (for distance penalty)
            var plateCenters = new Vector3[seeds.Count];
            for (int i = 0; i < seeds.Count; i++)
            {
                if (seeds[i] < 0 || seeds[i] >= tileCount)
                {
                    Console.WriteLine($"[FloodFill.Fractal] FATAL: Plate {i} has invalid seed tile ID {seeds[i]} (valid: 0-{tileCount - 1})");
                    throw new ArgumentException($"Invalid seed tile ID {seeds[i]} for plate {i}. Valid range is 0-{tileCount - 1}.");
                }
                plateCenters[i] = tilePositions[seeds[i]];
            }

            // Quotas (Target sizes)
            if (plateQuotas == null)
            {
                plateQuotas = new int[seeds.Count];
                int fairShare = map.Topology.TileCount / seeds.Count;
                Array.Fill(plateQuotas, fairShare);
            }

            // Track current size
            int[] currentCounts = new int[seeds.Count];
            int totalAssigned = seeds.Count; // Seeds are pre-assigned
            int totalQuota = 0;
            foreach (var q in plateQuotas) totalQuota += q;
            // Cap to actual tile count to avoid infinite loops
            int targetTiles = Math.Min(totalQuota, tileCount);

            Console.WriteLine($"[FloodFill.Fractal] Quotas: total={totalQuota}, target={targetTiles}, seeds pre-assigned={seeds.Count}");
            for (int i = 0; i < plateQuotas.Length && i < seeds.Count; i++)
            {
                Console.WriteLine($"  Plate {i}: quota={plateQuotas[i]}, seed={seeds[i]}");
            }

            // Optimization: Precompute Decorrelated Offsets
            // Avoids calling Random/Hash per neighbor
            Vector3[] plateOffsets = new Vector3[seeds.Count];
            for (int i = 0; i < seeds.Count; i++)
            {
                plateOffsets[i] = SphericalMath.GetDecorrelatedPlateOffset(i);
            }

            // 1. Initialize Seeds (Claim-at-Push for seeds to guarantee start)
            for (int i = 0; i < seeds.Count; i++)
            {
                int seed = seeds[i];
                int owner = i;

                // Sync with Caller's pre-fill
                if (outputOwnerMap[seed] == -1)
                {
                    outputOwnerMap[seed] = owner;
                }
                else if (outputOwnerMap[seed] != owner)
                {
                    throw new System.InvalidOperationException($"Seed mismatch! Tile {seed} has owner {outputOwnerMap[seed]}, expected {owner}.");
                }

                // Ensure count is correct if we own it
                if (outputOwnerMap[seed] == owner)
                {
                    currentCounts[owner] = 1;
                }

                // Enqueue seed's NEIGHBORS directly (seeds are already claimed)
                var neighbors = map.Topology.GetNeighbors(seed);
                Vector3 seedPos = tilePositions[seed];

                foreach (int n in neighbors)
                {
                    if (outputOwnerMap[n] != -1) continue; // Skip if already claimed

                    Vector3 pos = tilePositions[n];
                    Vector3 plateOffset = plateOffsets[owner];

                    float n1 = noiseA.GetNoise(pos.X + plateOffset.X, pos.Y + plateOffset.Y, pos.Z + plateOffset.Z);
                    float n2 = noiseB.GetNoise(pos.X + plateOffset.X + 0.5f, pos.Y + plateOffset.Y + 0.5f, pos.Z + plateOffset.Z + 0.5f);
                    float noiseScore = (n1 * weightA + n2 * weightB) * noiseStrength;

                    float dist = Vector3.Distance(pos, plateCenters[owner]);
                    float distScore = -dist * distancePenaltyWeight;
                    float finalScore = noiseScore + distScore;

                    pq.Enqueue((n, owner), -finalScore);
                }
            }

            Console.WriteLine($"[FloodFill.Fractal] Seeds initialized, queue size={pq.Count}");

            // 2. Expansion Loop (Claim-at-Pop)
            int iterations = 0;
            int logInterval = Math.Max(1, targetTiles / 10); // Log every 10%
            int lastLoggedAssigned = 0;

            while (pq.TryDequeue(out var item, out float negScore))
            {
                iterations++;

                // Early exit: All tiles assigned
                if (totalAssigned >= targetTiles)
                {
                    Console.WriteLine($"[FloodFill.Fractal] Early exit: assigned {totalAssigned}/{targetTiles} tiles after {iterations} iterations");
                    break;
                }

                // Progress logging (every 10%)
                if (totalAssigned - lastLoggedAssigned >= logInterval)
                {
                    Console.WriteLine($"[FloodFill.Fractal] Progress: {totalAssigned}/{targetTiles} ({100f * totalAssigned / targetTiles:F1}%), queue size={pq.Count}, iterations={iterations}");
                    lastLoggedAssigned = totalAssigned;
                }

                int current = item.Tile;
                int owner = item.Owner;

                // Check Quota
                if (currentCounts[owner] >= plateQuotas[owner]) continue;

                // If this tile is not the seed (already claimed), try to claim it
                // Logic: 
                // - If already claimed by US, we expand neighbors.
                // - If unassigned, we CLAIM, then expand.
                // - If claimed by OTHER, we stop.

                if (outputOwnerMap[current] != -1 && outputOwnerMap[current] != owner)
                    continue; // Lost to someone else

                // If already claimed by us, skip - we already expanded this tile
                if (outputOwnerMap[current] == owner)
                    continue;

                // Claim this tile
                outputOwnerMap[current] = owner;
                currentCounts[owner]++;
                totalAssigned++;

                // Expand to neighbors (only when we newly claim)
                var neighbors = map.Topology.GetNeighbors(current);
                foreach (int n in neighbors)
                {
                    // Skip if neighbor already claimed (any owner)
                    if (outputOwnerMap[n] != -1) continue;

                    // Calculate score
                    Vector3 pos = tilePositions[n];

                    // 1. Noise Offset (Decorrelated)
                    Vector3 plateOffset = plateOffsets[owner];

                    // Stack 1 (Base)
                    float n1 = noiseA.GetNoise(pos.X + plateOffset.X, pos.Y + plateOffset.Y, pos.Z + plateOffset.Z);
                    // Stack 2 (Detail)
                    float n2 = noiseB.GetNoise(pos.X + plateOffset.X + 0.5f, pos.Y + plateOffset.Y + 0.5f, pos.Z + plateOffset.Z + 0.5f);

                    float noiseScore = (n1 * weightA + n2 * weightB) * noiseStrength;

                    // Distance Penalty
                    float dist = Vector3.Distance(pos, plateCenters[owner]);
                    float distScore = -dist * distancePenaltyWeight;

                    float finalScore = noiseScore + distScore;

                    pq.Enqueue((n, owner), -finalScore);
                }
            }

            // CLEANUP PASS: Assign any remaining orphaned tiles to nearest plate
            int orphanCount = 0;
            for (int i = 0; i < tileCount; i++)
            {
                if (outputOwnerMap[i] == -1)
                {
                    orphanCount++;

                    // Find nearest plate by distance to plate center
                    float bestDist = float.MaxValue;
                    int bestPlate = 0;
                    Vector3 tilePos = tilePositions[i];

                    for (int p = 0; p < seeds.Count; p++)
                    {
                        float dist = Vector3.Distance(tilePos, plateCenters[p]);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestPlate = p;
                        }
                    }

                    outputOwnerMap[i] = bestPlate;
                    currentCounts[bestPlate]++;
                    totalAssigned++;
                }
            }

            if (orphanCount > 0)
            {
                Console.WriteLine($"[FloodFill.Fractal] Cleanup: Assigned {orphanCount} orphaned tiles to nearest plates");
            }

            // Summary logging
            Console.WriteLine($"[FloodFill.Fractal] Complete: {totalAssigned} tiles assigned in {iterations} iterations");
            int unassigned = 0;
            for (int i = 0; i < tileCount; i++)
            {
                if (outputOwnerMap[i] == -1) unassigned++;
            }
            if (unassigned > 0)
            {
                Console.WriteLine($"[FloodFill.Fractal] WARNING: {unassigned} tiles remain unassigned!");
            }

            Console.WriteLine("[FloodFill.Fractal] Per-plate counts:");
            for (int i = 0; i < seeds.Count; i++)
            {
                int pct = (int)(100f * currentCounts[i] / targetTiles);
                Console.WriteLine($"  Plate {i}: {currentCounts[i]} tiles ({pct}%), quota was {plateQuotas[i]}");
            }
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

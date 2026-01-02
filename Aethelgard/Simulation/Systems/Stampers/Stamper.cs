using System;
using System.Collections.Generic;
using System.Numerics;
using Aethelgard.Simulation.Core;
using Aethelgard.Simulation.Algorithms;

namespace Aethelgard.Simulation.Systems.Stampers
{
    /// <summary>
    /// A configurable "Masked BFS Flood-Fill" stamper.
    /// Expands from seed tiles and applies modifications to visited tiles based on distance/weight.
    /// </summary>
    public class Stamper : IStamper
    {
        // --- Configuration ---

        // Expansion Constraints
        public float Radius { get; set; } = 10.0f;
        public float MinStep { get; set; } = 0.8f;
        public float MaxStep { get; set; } = 1.2f;

        // Falloff
        public StamperMode FalloffMode { get; set; } = StamperMode.Linear;
        public float FalloffExponent { get; set; } = 2.0f; // For Power mode

        // Actions
        public StamperAction Action { get; set; } = StamperAction.Add;
        public StamperTarget Target { get; set; } = StamperTarget.Elevation;
        public float ActionValue { get; set; } = 1.0f; // The 'X' in Set(X) or Add(X)

        // Ring Flags
        public float RingAThreshold { get; set; } = 0.5f; // Normalized t/r
        public TileFlags RingAFlag { get; set; } = TileFlags.None;

        public float RingBThreshold { get; set; } = 0.8f;
        public TileFlags RingBFlag { get; set; } = TileFlags.None;

        // Masking
        public string? NoiseMaskName { get; set; } = null;
        public float NoiseMaskThreshold { get; set; } = 0.0f;
        public NoiseExpressionSystem? NoiseSystem { get; set; } = null;

        // Constraints
        public bool ConstrainToPlate { get; set; } = false;
        public bool RequireLand { get; set; } = false;

        private Random _rng;

        public Stamper(int seed)
        {
            _rng = new Random(seed);
        }

        public void Apply(WorldMap map, int centerTileId)
        {
            Apply(map, new List<int> { centerTileId });
        }

        public void Apply(WorldMap map, List<int> seeds)
        {
            // Transient memory for BFS
            var visited = new HashSet<int>();
            var queue = new PriorityQueue<int, float>(); // Tile, AccumulatedWeight
            var weights = new Dictionary<int, float>();

            // Initialize seeds
            foreach (var seed in seeds)
            {
                if (seed < 0 || seed >= map.Topology.TileCount) continue;

                // Mask Check (Seed)
                if (!PassesMask(map, seed)) continue;

                visited.Add(seed);
                weights[seed] = 0f;
                queue.Enqueue(seed, 0f);

                // Stamp seed immediately
                StampTile(map, seed, 0f);
            }

            while (queue.TryDequeue(out int current, out float currentWeight))
            {
                // Stop if we exceed radius
                if (currentWeight >= Radius) continue;

                var neighborsSpan = map.Topology.GetNeighbors(current);

                // Shuffle neighbors for organic growth
                // Note: Providing organic look relies on random weights, shuffle helps directionality
                // Copy to list to shuffle (GetNeighbors returns ReadOnlySpan)
                var neighbors = new List<int>();
                foreach (int n in neighborsSpan) neighbors.Add(n);
                Shuffle(neighbors);

                foreach (int n in neighbors)
                {
                    if (visited.Contains(n)) continue;

                    // Mask Check (Neighbor)
                    if (!PassesMask(map, n)) continue;

                    // Plate Constraint
                    if (ConstrainToPlate)
                    {
                        if (map.Tiles[n].PlateId != map.Tiles[current].PlateId) continue;
                    }

                    // Land Constraint
                    if (RequireLand && !map.Tiles[n].IsLand) continue;

                    // Calculate Step
                    // Step = random(min, max)
                    float step = MinStep + (float)_rng.NextDouble() * (MaxStep - MinStep);
                    float newWeight = currentWeight + step;

                    if (newWeight <= Radius)
                    {
                        visited.Add(n);
                        weights[n] = newWeight;
                        queue.Enqueue(n, newWeight);

                        StampTile(map, n, newWeight);
                    }
                }
            }
        }

        private void Shuffle(List<int> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = _rng.Next(n + 1);
                (list[k], list[n]) = (list[n], list[k]);
            }
        }

        private bool PassesMask(WorldMap map, int tileId)
        {
            if (string.IsNullOrEmpty(NoiseMaskName) || NoiseSystem == null) return true;

            var (lat, lon) = map.Topology.GetTileCenter(tileId);
            var pos = SphericalMath.LatLonToHypersphere(lon, lat);

            float val = NoiseSystem.Evaluate(NoiseMaskName, pos);
            return val >= NoiseMaskThreshold;
        }

        private void StampTile(WorldMap map, int tileId, float weight)
        {
            float t = weight;
            float r = Radius;
            float normalizedDist = t / r; // 0 at center, 1 at edge

            // 1. Ring Flags
            // Logic: if dist <= threshold, set flag.
            // Typical usage: RingA is inner core, RingB is wider
            if (normalizedDist <= RingAThreshold)
            {
                map.Tiles[tileId].SetFlag(RingAFlag);
            }

            if (normalizedDist <= RingBThreshold)
            {
                map.Tiles[tileId].SetFlag(RingBFlag);
            }

            // 2. Falloff Calculation
            // x = 1 - t/r
            // optionally pow(x, p)
            float falloff = 1.0f - normalizedDist;
            if (falloff < 0) falloff = 0;

            if (FalloffMode == StamperMode.Power)
            {
                falloff = MathF.Pow(falloff, FalloffExponent);
            }

            // 3. Action Dispatch
            ApplyAction(map, tileId, falloff);
        }

        private void ApplyAction(WorldMap map, int tileId, float falloff)
        {
            ref var tile = ref map.Tiles[tileId];
            float currentValue = 0f;

            // Read target
            switch (Target)
            {
                case StamperTarget.Elevation: currentValue = tile.Elevation; break;
                    // Add logic for other targets
            }

            // Compute new value
            float modification = ActionValue * falloff;
            float newValue = currentValue;

            switch (Action)
            {
                case StamperAction.Set: newValue = modification; break;
                case StamperAction.Add: newValue = currentValue + modification; break;
                case StamperAction.Subtract: newValue = currentValue - modification; break;
                case StamperAction.Max: newValue = MathF.Max(currentValue, modification); break;
                case StamperAction.Min: newValue = MathF.Min(currentValue, modification); break;
                case StamperAction.Lerp:
                    // Lerp(current, ActionValue, falloff) -> blends towards ActionValue based on falloff strength
                    newValue = currentValue + (ActionValue - currentValue) * falloff;
                    break;
            }

            // Write target
            switch (Target)
            {
                case StamperTarget.Elevation: tile.Elevation = newValue; break;
            }
        }
    }
}

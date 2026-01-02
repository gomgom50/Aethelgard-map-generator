using System;
using System.Collections.Generic;
using System.Numerics;
using Aethelgard.Simulation.Core;
using Aethelgard.Simulation.Algorithms;

namespace Aethelgard.Simulation.Systems.Selectors
{
    /// <summary>
    /// Selects a connected region of tiles starting from seeds.
    /// Used for identifying coastlines, landmasses, or specific biome regions.
    /// </summary>
    public class AreaSelector
    {
        // Configuration
        public bool RequireLand { get; set; } = true;
        public bool ConstrainToPlate { get; set; } = true;
        public float MaxScore { get; set; } = 100f;

        // Random Growth step
        public float MinStep { get; set; } = 1.0f;
        public float MaxStep { get; set; } = 2.0f;

        // Masking
        public string? NoiseMaskName { get; set; } = null;
        public float NoiseMaskThreshold { get; set; } = 0.0f;
        public NoiseExpressionSystem? NoiseSystem { get; set; } = null;

        // Debug
        public bool WriteDebugColor { get; set; } = false;
        public byte DebugR { get; set; } = 255;
        public byte DebugG { get; set; } = 0;
        public byte DebugB { get; set; } = 255;

        private Random _rng;

        public AreaSelector(int seed)
        {
            _rng = new Random(seed);
        }

        public Dictionary<int, float> Select(WorldMap map, List<int> seeds)
        {
            var selected = new Dictionary<int, float>(); // Tile -> Score
            var queue = new PriorityQueue<int, float>();
            var visited = new HashSet<int>();

            // Init Seeds
            foreach (var seed in seeds)
            {
                if (CheckTile(map, seed))
                {
                    selected[seed] = 0f;
                    queue.Enqueue(seed, 0f);
                    visited.Add(seed);

                    if (WriteDebugColor) WriteDebug(map, seed);
                }
            }

            while (queue.TryDequeue(out int current, out float score))
            {
                if (score >= MaxScore) continue;

                var neighborsSpan = map.Topology.GetNeighbors(current);

                var neighbors = new List<int>();
                foreach (int n in neighborsSpan) neighbors.Add(n);
                Shuffle(neighbors);

                foreach (int n in neighbors)
                {
                    if (visited.Contains(n)) continue;

                    // Constraints
                    if (!CheckTile(map, n)) continue;

                    // Plate Constraint
                    if (ConstrainToPlate && map.Tiles[n].PlateId != map.Tiles[current].PlateId) continue;

                    // Growing score
                    float step = MinStep + (float)_rng.NextDouble() * (MaxStep - MinStep);
                    float newScore = score + step;

                    if (newScore <= MaxScore)
                    {
                        visited.Add(n);
                        selected[n] = newScore;
                        queue.Enqueue(n, newScore);

                        if (WriteDebugColor) WriteDebug(map, n);
                    }
                }
            }

            return selected;
        }

        private bool CheckTile(WorldMap map, int tileId)
        {
            if (tileId < 0 || tileId >= map.Topology.TileCount) return false;

            // Land/Water
            if (RequireLand && !map.Tiles[tileId].IsLand) return false;
            if (!RequireLand && map.Tiles[tileId].IsLand) return false;

            // Mask
            if (!string.IsNullOrEmpty(NoiseMaskName) && NoiseSystem != null)
            {
                var (lat, lon) = map.Topology.GetTileCenter(tileId);
                var pos = SphericalMath.LatLonToHypersphere(lon, lat);
                float val = NoiseSystem.Evaluate(NoiseMaskName, pos);
                if (val < NoiseMaskThreshold) return false;
            }

            return true;
        }

        private void Shuffle(List<int> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = _rng.Next(n + 1);
                (list[n], list[k]) = (list[k], list[n]);
            }
        }

        private void WriteDebug(WorldMap map, int tileId)
        {
            // Assuming we might have a debug color field or similar in future
            // For now, no-op or console log if needed, but keeping method for structure
        }
    }
}

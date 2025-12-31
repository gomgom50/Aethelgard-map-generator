using System;
using System.Collections.Concurrent;
using System.Numerics;
using Aethelgard.Simulation.Core;

namespace Aethelgard.Simulation.Systems
{
    /// <summary>
    /// Handles the "Noise Expression" abstraction.
    /// Maps specific logical noise layers (e.g. "Elevation", "Moisture") to configured FractalSources.
    /// Replicates the Init/Evaluate dynamic dispatch from the reference.
    /// </summary>
    public class NoiseExpressionSystem
    {
        private readonly int _seed;
        private readonly ConcurrentDictionary<string, FractalNoise> _expressions = new();

        public NoiseExpressionSystem(int seed)
        {
            _seed = seed;
        }

        public void InitializeExpression(string name, int octaves, float persistence, float lacunarity, float scale, int salt = 0)
        {
            // "Init" phase: Prepare the source
            // Mix seed with salt to get unique seeds per expression
            int expressionSeed = _seed ^ (name.GetHashCode() + salt * 31);
            var noise = NoiseFactory.GetNoise(expressionSeed, octaves, persistence, lacunarity, scale);
            _expressions[name] = noise;
        }

        public float Evaluate(string name, Vector3 point)
        {
            if (_expressions.TryGetValue(name, out var noise))
            {
                return noise.GetNoise(point.X, point.Y, point.Z);
            }
            return 0f;
        }

        public float Evaluate(string name, float x, float y)
        {
            if (_expressions.TryGetValue(name, out var noise))
            {
                return noise.GetNoise(x, y);
            }
            return 0f;
        }
    }
}

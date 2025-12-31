using System.Collections.Concurrent;

namespace Aethelgard.Simulation.Core
{
    public static class NoiseFactory
    {
        // Cache to avoid re-initializing permutation tables for same parameters
        private static readonly ConcurrentDictionary<string, FractalNoise> _cache = new();

        public static FractalNoise GetNoise(int seed, int octaves, float persistence, float lacunarity, float scale)
        {
            string key = $"{seed}_{octaves}_{persistence}_{lacunarity}_{scale}";
            return _cache.GetOrAdd(key, _ => new FractalNoise(seed, octaves, persistence, lacunarity, scale));
        }

        public static FractalNoise Create(int seed, int octaves, float scale)
        {
            return GetNoise(seed, octaves, 0.5f, 2.0f, scale);
        }
    }
}

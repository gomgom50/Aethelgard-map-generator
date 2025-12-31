using System;

namespace Aethelgard.Simulation.Core
{
    /// <summary>
    /// Instantiable Fractal Noise generator.
    /// Replaces the static SimpleNoise with a seeded, object-oriented approach.
    /// Implements the "Normalization" logic generic to Gleba's reference.
    /// </summary>
    public class FractalNoise
    {
        private readonly int[] _perm = new int[512];
        private readonly int _octaves;
        private readonly float _persistence;
        private readonly float _lacunarity;
        private readonly float _scale;

        // Normalization factor pre-calculated to ensure result is roughly within [-1, 1]
        private readonly float _normalization;

        public FractalNoise(int seed, int octaves, float persistence = 0.5f, float lacunarity = 2.0f, float scale = 0.01f)
        {
            _octaves = octaves;
            _persistence = persistence;
            _lacunarity = lacunarity;
            _scale = scale;

            // Initialize Permutation Table with Seed
            var rnd = new Random(seed);
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;

            // Shuffle
            for (int i = 0; i < 256; i++)
            {
                int swapIdx = rnd.Next(256);
                (p[i], p[swapIdx]) = (p[swapIdx], p[i]);
            }

            // Duplicate
            for (int i = 0; i < 512; i++)
            {
                _perm[i] = p[i & 255];
            }

            // Calculate Normalization Factor (Geometric Series Sum)
            // Max Amplitude = 1 + p + p^2 + ... + p^(oct-1)
            float maxAmp = 0;
            float amp = 1;
            for (int i = 0; i < _octaves; i++)
            {
                maxAmp += amp;
                amp *= _persistence;
            }
            _normalization = 1.0f / maxAmp;
        }

        public float GetNoise(float x, float y)
        {
            float total = 0;
            float frequency = 1;
            float amplitude = 1;

            float sx = x * _scale;
            float sy = y * _scale;

            for (int i = 0; i < _octaves; i++)
            {
                total += ComputePerlin(sx * frequency, sy * frequency) * amplitude;
                amplitude *= _persistence;
                frequency *= _lacunarity;
            }

            return total * _normalization;
        }

        public float GetNoise(float x, float y, float z)
        {
            float total = 0;
            float frequency = 1;
            float amplitude = 1;

            float sx = x * _scale;
            float sy = y * _scale;
            float sz = z * _scale;

            for (int i = 0; i < _octaves; i++)
            {
                total += ComputePerlin(sx * frequency, sy * frequency, sz * frequency) * amplitude;
                amplitude *= _persistence;
                frequency *= _lacunarity;
            }

            return total * _normalization;
        }

        private float ComputePerlin(float x, float y)
        {
            int X = (int)Math.Floor(x) & 255;
            int Y = (int)Math.Floor(y) & 255;
            x -= (float)Math.Floor(x);
            y -= (float)Math.Floor(y);
            float u = Fade(x);
            float v = Fade(y);
            int A = _perm[X] + Y, AA = _perm[A], AB = _perm[A + 1];
            int B = _perm[X + 1] + Y, BA = _perm[B], BB = _perm[B + 1];
            return Lerp(v, Lerp(u, Grad(_perm[AA], x, y), Grad(_perm[BA], x - 1, y)),
                           Lerp(u, Grad(_perm[AB], x, y - 1), Grad(_perm[BB], x - 1, y - 1)));
        }

        private float ComputePerlin(float x, float y, float z)
        {
            int X = (int)Math.Floor(x) & 255;
            int Y = (int)Math.Floor(y) & 255;
            int Z = (int)Math.Floor(z) & 255;

            x -= (float)Math.Floor(x);
            y -= (float)Math.Floor(y);
            z -= (float)Math.Floor(z);

            float u = Fade(x);
            float v = Fade(y);
            float w = Fade(z);

            int A = _perm[X] + Y, AA = _perm[A] + Z, AB = _perm[A + 1] + Z;
            int B = _perm[X + 1] + Y, BA = _perm[B] + Z, BB = _perm[B + 1] + Z;

            return Lerp(w, Lerp(v, Lerp(u, Grad(_perm[AA], x, y, z), Grad(_perm[BA], x - 1, y, z)),
                                   Lerp(u, Grad(_perm[AB], x, y - 1, z), Grad(_perm[BB], x - 1, y - 1, z))),
                           Lerp(v, Lerp(u, Grad(_perm[AA + 1], x, y, z - 1), Grad(_perm[BA + 1], x - 1, y, z - 1)),
                                   Lerp(u, Grad(_perm[AB + 1], x, y - 1, z - 1), Grad(_perm[BB + 1], x - 1, y - 1, z - 1))));
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
        private static float Lerp(float t, float a, float b) => a + t * (b - a);

        private static float Grad(int hash, float x, float y)
        {
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : h == 12 || h == 14 ? x : 0;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        public float GetDomainWarpedNoise(float x, float y, float z, float warpStrength = 4.0f)
        {
            // Domain Warping in 3D
            // q = fbm(p)
            // r = fbm(p + q*warp)
            // fbm(p + r*warp)

            float qx = GetNoise(x, y, z);
            float qy = GetNoise(x + 5.2f, y + 1.3f, z + 2.8f);
            float qz = GetNoise(x + 1.7f, y + 9.2f, z + 5.2f);

            float rx = GetNoise(x + warpStrength * qx + 1.7f, y + warpStrength * qy + 9.2f, z + warpStrength * qz + 5.2f);
            float ry = GetNoise(x + warpStrength * qx + 8.3f, y + warpStrength * qy + 2.8f, z + warpStrength * qz + 1.3f);
            float rz = GetNoise(x + warpStrength * qx + 2.8f, y + warpStrength * qy + 5.2f, z + warpStrength * qz + 9.2f);

            return GetNoise(x + warpStrength * rx, y + warpStrength * ry, z + warpStrength * rz);
        }

        private static float Grad(int hash, float x, float y, float z)
        {
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : h == 12 || h == 14 ? x : z;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }
    }
}

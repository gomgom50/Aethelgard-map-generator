using System;
using System.Numerics;

namespace Aethelgard.Simulation.Core
{
    /// <summary>
    /// Instantiable Fractal Noise generator.
    /// Corrected to prevent "Zero-Stacking" artifacts and Grid Alignment on spheres.
    /// </summary>
    public class FractalNoise
    {
        private readonly int[] _perm = new int[512];
        private readonly int _octaves;
        private readonly float _persistence;
        private readonly float _lacunarity;
        private readonly float _scale;

        // NEW: Specific offsets for every octave to prevent zero-crossing alignment
        private readonly Vector3[] _octaveOffsets;

        private readonly float _normalization;

        public FractalNoise(int seed, int octaves, float persistence = 0.5f, float lacunarity = 2.0f, float scale = 0.01f)
        {
            _octaves = octaves;
            _persistence = persistence;
            _lacunarity = lacunarity;
            _scale = scale;
            _octaveOffsets = new Vector3[octaves];

            var rnd = new Random(seed);

            // 1. Initialize Permutation Table
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

            // 2. Generate Random Offsets per Octave
            // We pick large offsets to move far away from the origin (0,0,0) where artifacts are worst.
            // This ensures Octave 1's "grid lines" do not line up with Octave 2's.
            for (int i = 0; i < _octaves; i++)
            {
                _octaveOffsets[i] = new Vector3(
                    (float)(rnd.NextDouble() * 20000.0 - 10000.0),
                    (float)(rnd.NextDouble() * 20000.0 - 10000.0),
                    (float)(rnd.NextDouble() * 20000.0 - 10000.0)
                );
            }

            // 3. Normalization
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

            // Apply global scale
            float sx = x * _scale;
            float sy = y * _scale;

            for (int i = 0; i < _octaves; i++)
            {
                // Apply OCTAVE OFFSET
                float ox = sx * frequency + _octaveOffsets[i].X;
                float oy = sy * frequency + _octaveOffsets[i].Y;

                total += ComputePerlin(ox, oy) * amplitude;
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

            // DOMAIN ROTATION:
            // Rotate the input coordinates slightly to misalign the noise grid 
            // from the sphere's primary axes. This hides "Star Patterns" at poles.
            // (Simple rotation around Z axis is usually enough to break the visual symmetry)
            float rx = x * 0.8f + y * 0.6f;
            float ry = y * 0.8f - x * 0.6f;
            float rz = z;

            // Apply global scale
            float sx = rx * _scale;
            float sy = ry * _scale;
            float sz = rz * _scale;

            for (int i = 0; i < _octaves; i++)
            {
                // Apply OCTAVE OFFSET
                // Each layer samples a completely different part of infinite noise space
                float ox = sx * frequency + _octaveOffsets[i].X;
                float oy = sy * frequency + _octaveOffsets[i].Y;
                float oz = sz * frequency + _octaveOffsets[i].Z;

                total += ComputePerlin(ox, oy, oz) * amplitude;
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

            // Fixed Masking: Ensure (X+1) wraps correctly before array access
            // Though _perm is 512, strict math relies on the 256 domain wrap
            int A = (_perm[X] + Y) & 255;
            int B = (_perm[X + 1] + Y) & 255;

            return Lerp(v, Lerp(u, Grad(_perm[A], x, y), Grad(_perm[B], x - 1, y)),
                           Lerp(u, Grad(_perm[A + 1], x, y - 1), Grad(_perm[B + 1], x - 1, y - 1)));
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

            // Fixed Hash Calculation with Masking
            // Old code: int A = _perm[X] + Y; (Could result in 510, which is safe for array but wrong for noise tiling)
            // New code: Mask immediately to ensure we stay in the 0-255 domain logic
            int A = (_perm[X] + Y) & 255;
            int B = (_perm[X + 1] + Y) & 255;

            int AA = (_perm[A] + Z) & 255;
            int AB = (_perm[A + 1] + Z) & 255;
            int BA = (_perm[B] + Z) & 255;
            int BB = (_perm[B + 1] + Z) & 255;

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
            // Note: Domain warping inherits the internal offsets from GetNoise,
            // so this will also be fixed automatically.

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

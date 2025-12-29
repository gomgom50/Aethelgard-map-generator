using System;

namespace Aethelgard.Simulation
{
    /// <summary>
    /// A simple Gradient Noise implementation (similar to Perlin/Simplex).
    /// Used for domain warping and organic generation.
    /// </summary>
    public static class SimpleNoise
    {
        // Permutation table
        private static readonly int[] P = new int[512];

        static SimpleNoise()
        {
            // Pseudo-random deterministic shuffle
            var rnd = new Random(1337);
            for (int i = 0; i < 256; i++) P[i] = i;

            // Shuffle
            for (int i = 0; i < 256; i++)
            {
                int swapIdx = rnd.Next(256);
                int temp = P[i];
                P[i] = P[swapIdx];
                P[swapIdx] = temp;
            }

            // Duplicate for overflow handling
            for (int i = 0; i < 256; i++)
            {
                P[i + 256] = P[i];
            }
        }

        public static float GetNoise(float x, float y)
        {
            // Simple 2D Perlin-ish noise
            int X = (int)Math.Floor(x) & 255;
            int Y = (int)Math.Floor(y) & 255;

            x -= (float)Math.Floor(x);
            y -= (float)Math.Floor(y);

            float u = Fade(x);
            float v = Fade(y);

            int A = P[X] + Y, AA = P[A], AB = P[A + 1];
            int B = P[X + 1] + Y, BA = P[B], BB = P[B + 1];

            return Lerp(v, Lerp(u, Grad(P[AA], x, y), Grad(P[BA], x - 1, y)),
                           Lerp(u, Grad(P[AB], x, y - 1), Grad(P[BB], x - 1, y - 1)));
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

        /// <summary>
        /// Fractal Brownian Motion (FBM) - Layers noise at increasing frequencies.
        /// </summary>
        public static float GetFBM(float x, float y, int octaves, float persistence = 0.5f, float lacunarity = 2.0f)
        {
            float total = 0;
            float frequency = 1;
            float amplitude = 1;
            float maxValue = 0;  // Used for normalizing result to 0.0 - 1.0 (optional) or keeping raw

            for (int i = 0; i < octaves; i++)
            {
                total += GetNoise(x * frequency, y * frequency) * amplitude;

                maxValue += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return total; // Raw result, can be > 1 or < -1
        }

        /// <summary>
        /// Domain Warping - Displaces the coordinates using FBM before sampling noise.
        /// Creates "swirly", fluid-like patterns ideal for organic plate boundaries.
        /// </summary>
        public static float GetDomainWarpedNoise(float x, float y, int octaves, float warpStrength = 4.0f)
        {
            // First layer of displacement
            float qx = GetFBM(x, y, octaves);
            float qy = GetFBM(x + 5.2f, y + 1.3f, octaves);

            // Second layer (optional, but good for extra swirl)
            float rx = GetFBM(x + 4.0f * qx + 1.7f, y + 4.0f * qy + 9.2f, octaves);
            float ry = GetFBM(x + 4.0f * qx + 8.3f, y + 4.0f * qy + 2.8f, octaves);

            // Final sample
            return GetFBM(x + warpStrength * rx, y + warpStrength * ry, octaves);
        }
    }
}

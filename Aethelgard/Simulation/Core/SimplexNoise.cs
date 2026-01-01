using System;
using System.Runtime.CompilerServices;

namespace Aethelgard.Simulation.Core
{
    /// <summary>
    /// Organic Fractal Noise generator using Simplex 3D (OpenSimplex2S style).
    /// Eliminates the "Grid/Star" artifacts found in Perlin noise on spheres.
    /// Replacement for FractalNoise in organic generation contexts.
    /// </summary>
    public class SimplexNoise
    {
        private readonly int[] _perm = new int[512];
        private readonly int _octaves;
        private readonly float _persistence;
        private readonly float _lacunarity;
        private readonly float _scale;

        // Huge offset to ensure we never sample near (0,0,0) where artifacts live
        private const float BASE_OFFSET = 50000.0f;

        public SimplexNoise(int seed, int octaves, float persistence = 0.5f, float lacunarity = 2.0f, float scale = 1.0f)
        {
            _octaves = octaves;
            _persistence = persistence;
            _lacunarity = lacunarity;
            _scale = scale;

            // Initialize Permutation Table (Standard Shuffle)
            var rnd = new Random(seed);
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;

            for (int i = 0; i < 256; i++)
            {
                int swapIdx = rnd.Next(256);
                (p[i], p[swapIdx]) = (p[swapIdx], p[i]);
            }

            for (int i = 0; i < 512; i++)
            {
                _perm[i] = p[i & 255];
            }
        }

        public float GetNoise(float x, float y)
        {
            return GetNoise(x, y, 0.0f);
        }

        public float GetNoise(float x, float y, float z)
        {
            float total = 0;
            float frequency = 1;
            float amplitude = 1;
            float maxAmplitude = 0;

            // Move the sphere far away from origin to avoid axis artifacts
            float sx = x * _scale + BASE_OFFSET;
            float sy = y * _scale + BASE_OFFSET;
            float sz = z * _scale + BASE_OFFSET;

            for (int i = 0; i < _octaves; i++)
            {
                total += Noise3_Classic(sx * frequency, sy * frequency, sz * frequency) * amplitude;
                maxAmplitude += amplitude;

                amplitude *= _persistence;
                frequency *= _lacunarity;
            }

            return total / maxAmplitude; // Normalize to approx [-1, 1]
        }

        public float GetDomainWarpedNoise(float x, float y, float z, float warpStrength = 1.0f)
        {
            // Simple fbm domain warp
            float qx = GetNoise(x + 5.2f, y + 1.3f, z + 2.8f);
            float qy = GetNoise(x + 1.7f, y + 9.2f, z + 5.2f);
            float qz = GetNoise(x + 8.3f, y + 2.8f, z + 1.1f);

            return GetNoise(x + warpStrength * qx, y + warpStrength * qy, z + warpStrength * qz);
        }

        // -------------------------------------------------------------------------------
        // OpenSimplex2S-like 3D Noise Implementation (The "Organic" Fix)
        // -------------------------------------------------------------------------------

        private const double STRETCH_CONSTANT_3D = -1.0 / 6.0; // (1 / sqrt(3 + 1) - 1) / 3
        private const double SQUISH_CONSTANT_3D = 1.0 / 3.0;   // (sqrt(3 + 1) - 1) / 3
        private const double NORM_CONSTANT_3D = 103.0;

        private float Noise3_Classic(float x, float y, float z)
        {
            // skew the input space to determine which simplex (tetrahedron) we are in
            double stretchOffset = (x + y + z) * STRETCH_CONSTANT_3D;
            double xs = x + stretchOffset;
            double ys = y + stretchOffset;
            double zs = z + stretchOffset;

            int xsb = FastFloor(xs);
            int ysb = FastFloor(ys);
            int zsb = FastFloor(zs);

            double squishOffset = (xsb + ysb + zsb) * SQUISH_CONSTANT_3D;
            double dx0 = x - (xsb + squishOffset);
            double dy0 = y - (ysb + squishOffset);
            double dz0 = z - (zsb + squishOffset);

            // Determine which simplex we are in
            int xsv_ext0 = xsb, ysv_ext0 = ysb, zsv_ext0 = zsb;
            int xsv_ext1, ysv_ext1, zsv_ext1;
            int xsv_ext2, ysv_ext2, zsv_ext2;
            int xsv_ext3 = xsb + 1, ysv_ext3 = ysb + 1, zsv_ext3 = zsb + 1;

            if (dx0 >= dy0 && dy0 >= dz0)
            {
                xsv_ext1 = xsb + 1; ysv_ext1 = ysb; zsv_ext1 = zsb;
                xsv_ext2 = xsb + 1; ysv_ext2 = ysb + 1; zsv_ext2 = zsb;
            }
            else if (dx0 >= dz0 && dz0 >= dy0)
            {
                xsv_ext1 = xsb + 1; ysv_ext1 = ysb; zsv_ext1 = zsb;
                xsv_ext2 = xsb + 1; ysv_ext2 = ysb; zsv_ext2 = zsb + 1;
            }
            else if (dz0 >= dx0 && dx0 >= dy0)
            {
                xsv_ext1 = xsb; ysv_ext1 = ysb; zsv_ext1 = zsb + 1;
                xsv_ext2 = xsb + 1; ysv_ext2 = ysb; zsv_ext2 = zsb + 1;
            }
            else if (dz0 >= dy0 && dy0 >= dx0)
            {
                xsv_ext1 = xsb; ysv_ext1 = ysb; zsv_ext1 = zsb + 1;
                xsv_ext2 = xsb; ysv_ext2 = ysb + 1; zsv_ext2 = zsb + 1;
            }
            else if (dy0 >= dz0 && dz0 >= dx0)
            {
                xsv_ext1 = xsb; ysv_ext1 = ysb + 1; zsv_ext1 = zsb;
                xsv_ext2 = xsb; ysv_ext2 = ysb + 1; zsv_ext2 = zsb + 1;
            }
            else // dy0 >= dx0 && dx0 >= dz0
            {
                xsv_ext1 = xsb; ysv_ext1 = ysb + 1; zsv_ext1 = zsb;
                xsv_ext2 = xsb + 1; ysv_ext2 = ysb + 1; zsv_ext2 = zsb;
            }

            double dx1 = dx0 - 1 - SQUISH_CONSTANT_3D;
            double dy1 = dy0 - 0 - SQUISH_CONSTANT_3D;
            double dz1 = dz0 - 0 - SQUISH_CONSTANT_3D;
            double dx2 = dx0 - 1 - 2 * SQUISH_CONSTANT_3D;
            double dy2 = dy0 - 1 - 2 * SQUISH_CONSTANT_3D;
            double dz2 = dz0 - 0 - 2 * SQUISH_CONSTANT_3D;
            double dx3 = dx0 - 1 - 3 * SQUISH_CONSTANT_3D;
            double dy3 = dy0 - 1 - 3 * SQUISH_CONSTANT_3D;
            double dz3 = dz0 - 1 - 3 * SQUISH_CONSTANT_3D;

            // Adjust contributions based on vertex choice logic (simplified for clarity/speed)
            // Note: This block re-aligns the intermediate vertices based on the decomposition
            // But for standard simplex, simply summing contributions is sufficient.

            // Contribution logic
            double attn0 = 2 - dx0 * dx0 - dy0 * dy0 - dz0 * dz0;
            double value = 0;
            if (attn0 > 0)
            {
                attn0 *= attn0;
                value += attn0 * attn0 * Extrapolate(xsv_ext0, ysv_ext0, zsv_ext0, dx0, dy0, dz0);
            }

            double attn1 = 2 - dx1 * dx1 - dy1 * dy1 - dz1 * dz1; // Note: dx1 here depends on the vertex set above
                                                                  // Actually, we must subtract the correct offsets based on which vertex (1,0,0) etc was chosen.
                                                                  // To keep this implementation compact and robust, we use the pre-calculated offsets:

            double dx_ext1 = dx0 - (xsv_ext1 - xsb) + SQUISH_CONSTANT_3D;
            double dy_ext1 = dy0 - (ysv_ext1 - ysb) + SQUISH_CONSTANT_3D;
            double dz_ext1 = dz0 - (zsv_ext1 - zsb) + SQUISH_CONSTANT_3D;

            double attn_ext1 = 2 - dx_ext1 * dx_ext1 - dy_ext1 * dy_ext1 - dz_ext1 * dz_ext1;
            if (attn_ext1 > 0)
            {
                attn_ext1 *= attn_ext1;
                value += attn_ext1 * attn_ext1 * Extrapolate(xsv_ext1, ysv_ext1, zsv_ext1, dx_ext1, dy_ext1, dz_ext1);
            }

            double dx_ext2 = dx0 - (xsv_ext2 - xsb) + 2 * SQUISH_CONSTANT_3D;
            double dy_ext2 = dy0 - (ysv_ext2 - ysb) + 2 * SQUISH_CONSTANT_3D;
            double dz_ext2 = dz0 - (zsv_ext2 - zsb) + 2 * SQUISH_CONSTANT_3D;

            double attn_ext2 = 2 - dx_ext2 * dx_ext2 - dy_ext2 * dy_ext2 - dz_ext2 * dz_ext2;
            if (attn_ext2 > 0)
            {
                attn_ext2 *= attn_ext2;
                value += attn_ext2 * attn_ext2 * Extrapolate(xsv_ext2, ysv_ext2, zsv_ext2, dx_ext2, dy_ext2, dz_ext2);
            }

            double dx_ext3 = dx0 - 1 + 3 * SQUISH_CONSTANT_3D;
            double dy_ext3 = dy0 - 1 + 3 * SQUISH_CONSTANT_3D;
            double dz_ext3 = dz0 - 1 + 3 * SQUISH_CONSTANT_3D;

            double attn_ext3 = 2 - dx_ext3 * dx_ext3 - dy_ext3 * dy_ext3 - dz_ext3 * dz_ext3;
            if (attn_ext3 > 0)
            {
                attn_ext3 *= attn_ext3;
                value += attn_ext3 * attn_ext3 * Extrapolate(xsv_ext3, ysv_ext3, zsv_ext3, dx_ext3, dy_ext3, dz_ext3);
            }

            return (float)(value / NORM_CONSTANT_3D);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FastFloor(double x)
        {
            int xi = (int)x;
            return x < xi ? xi - 1 : xi;
        }

        private double Extrapolate(int xsb, int ysb, int zsb, double dx, double dy, double dz)
        {
            int index = (_perm[(xsb & 0xFF)] + (ysb & 0xFF)) & 0xFF;
            index = (_perm[index] + (zsb & 0xFF)) & 0xFF;

            // Gradients for 3D (12 vectors defined by edge midpoints of a cube)
            // But OpenSimplex logic handles gradients differently. 
            // We'll use a simple bit-manipulation gradient set for speed and consistency.
            int h = _perm[index] & 0x0E;
            return Gradients3D[h] * dx + Gradients3D[h | 1] * dy + Gradients3D[h | 2] * dz;
        }

        // Simple 12-vector gradient set padded to 16
        private static readonly double[] Gradients3D = {
            1,1,0, -1,1,0, 1,-1,0, -1,-1,0,
            1,0,1, -1,0,1, 1,0,-1, -1,0,-1,
            0,1,1, 0,-1,1, 0,1,-1, 0,-1,-1,
            1,1,0, 0,-1,1 // padding
        };
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aethelgard.Simulation
{
    /// <summary>
    /// Generates Organic Voronoi diagrams using Weighted JFA and Domain Warping.
    /// </summary>
    public static class VoronoiGenerator
    {
        private struct PixelInfo
        {
            public int PlateId;
            public short SeedX;
            public short SeedY;
            public float Weight; // Power Diagram Weight
        }

        public static void Generate(Lithosphere lithosphere, List<Plate> seeds, bool useWarping = true, bool useSphericalDistance = false)
        {
            int w = lithosphere.PlateIdMap.Width;
            int h = lithosphere.PlateIdMap.Height;
            _useSpherical = useSphericalDistance;
            _mapWidth = w;
            _mapHeight = h;

            PixelInfo[] bufferA = new PixelInfo[w * h];
            PixelInfo[] bufferB = new PixelInfo[w * h];

            // 1. Initialization logic
            Parallel.For(0, w * h, i =>
            {
                // Init with infinite distance implied (off-screen seed) and min weight
                bufferA[i] = new PixelInfo { PlateId = 0, SeedX = -10000, SeedY = -10000, Weight = 0 };
            });

            // Seed initial points
            foreach (var seed in seeds)
            {
                if (lithosphere.PlateIdMap.IsValid((int)seed.Center.X, (int)seed.Center.Y))
                {
                    int idx = (int)seed.Center.Y * w + (int)seed.Center.X;
                    if (idx >= 0 && idx < bufferA.Length)
                    {
                        bufferA[idx] = new PixelInfo
                        {
                            PlateId = seed.Id,
                            SeedX = (short)seed.Center.X,
                            SeedY = (short)seed.Center.Y,
                            Weight = seed.Weight
                        };
                    }
                }
            }

            // 2. JFA Passes (Weighted)
            int step = Math.Max(w, h);
            // Power of two ceiling approx
            int p2 = 1;
            while (p2 < step) p2 <<= 1;
            step = p2 >> 1;

            bool swap = false;
            while (step >= 1)
            {
                PixelInfo[] source = swap ? bufferB : bufferA;
                PixelInfo[] dest = swap ? bufferA : bufferB;

                Parallel.For(0, h, y =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        int currentIdx = y * w + x;
                        PixelInfo bestPixel = source[currentIdx];
                        float bestDist = GetWeightedDist(x, y, bestPixel);

                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx * step;
                                int ny = y + dy * step;

                                // Cylindrical X-Wrap
                                if (nx < 0) nx += w;
                                if (nx >= w) nx -= w;

                                if (ny >= 0 && ny < h)
                                {
                                    PixelInfo neighbor = source[ny * w + nx];
                                    if (neighbor.PlateId != 0)
                                    {
                                        float d = GetWeightedDist(x, y, neighbor);
                                        if (d < bestDist)
                                        {
                                            bestDist = d;
                                            bestPixel = neighbor;
                                        }
                                    }
                                }
                            }
                        }
                        dest[currentIdx] = bestPixel;
                    }
                });
                step /= 2;
                swap = !swap;
            }

            PixelInfo[] jfaResult = swap ? bufferB : bufferA;

            // 3. Domain Warping (Legacy / Internal)
            // Note: We moved main distortion to Post-Process, but keeping this logic for reference or double-warp.
            // If used, it should also wrap.
            if (useWarping)
            {
                // ... (Keep existing warping logic or disable? Let's leave it but it is mostly unused now)
                // Actually, let's just copy the array if not warping, or warp if requested.
                // But GeneratePlatesCommand passes false now.
            }

            // 3. Apply Result to Map (with optional Distortion)
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int queryX = x;
                    int queryY = y;

                    if (useWarping)
                    {
                        // Domain Warp the lookup coordinates
                        // Use a fixed seed/offset for stability
                        float scale = 0.02f; // Frequency
                        float strength = 20.0f; // Amplitude (Pixels)

                        float noiseX = SimpleNoise.GetNoise(x * scale, y * scale) * strength;
                        float noiseY = SimpleNoise.GetNoise(x * scale + 100.0f, y * scale + 100.0f) * strength;

                        queryX = (int)(x + noiseX);
                        queryY = (int)(y + noiseY);

                        // Wrap Query X
                        while (queryX < 0) queryX += w;
                        while (queryX >= w) queryX -= w;

                        // Clamp Query Y
                        if (queryY < 0) queryY = 0;
                        if (queryY >= h) queryY = h - 1;
                    }

                    lithosphere.PlateIdMap.Set(x, y, jfaResult[queryY * w + queryX].PlateId);
                }
            });
        }

        // Static state for spherical calculations (shared across threads - read-only during Parallel.For)
        private static bool _useSpherical;
        private static int _mapWidth;
        private static int _mapHeight;

        private static float GetWeightedDist(int x, int y, PixelInfo p)
        {
            if (p.SeedX < -1000) return float.MaxValue;

            if (_useSpherical)
            {
                // Use true spherical distance (Haversine)
                float dist = SphericalMath.SphericalDistancePixels(x, y, p.SeedX, p.SeedY, _mapWidth, _mapHeight);
                // Scale to pixel-like units (normalized spherical dist is 0 to PI)
                // Multiply by height to get comparable pixel values
                float scaledDist = dist * _mapHeight / (float)Math.PI;
                return (scaledDist * scaledDist) - p.Weight;
            }
            else
            {
                // Original Euclidean distance
                float dx = Math.Abs(x - p.SeedX);
                float dy = Math.Abs(y - p.SeedY);

                // Cylindrical Wrap for DX
                if (dx > _mapWidth / 2.0f) dx = _mapWidth - dx;

                // Power Diagram: Dist^2 - Weight
                return (dx * dx + dy * dy) - p.Weight;
            }
        }
    }
}

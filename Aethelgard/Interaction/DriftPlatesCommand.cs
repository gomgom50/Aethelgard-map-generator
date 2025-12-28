using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Aethelgard.Core;
using Aethelgard.Simulation;

namespace Aethelgard.Interaction
{
    /// <summary>
    /// Drifts plates and regenerates terrain from boundaries.
    /// 
    /// NEW ARCHITECTURE: Plates define REGIONS, not terrain!
    /// - No TerrainPixels carrying
    /// - Terrain is derived from plate properties + boundary effects
    /// - Voronoi regions shift as plates move
    /// </summary>
    public class DriftPlatesCommand : ICommand
    {
        private readonly WorldMap _map;
        private readonly int _steps;
        private readonly float _speed;

        public DriftPlatesCommand(WorldMap map, int steps = 50, float speed = 1.0f)
        {
            _map = map;
            _steps = steps;
            _speed = speed;
        }

        public void Undo()
        {
            // Simulation is destructive, no undo
        }

        public void Execute()
        {
            var plates = _map.Lithosphere.Plates;
            if (plates.Count == 0) return;

            int w = _map.Width;
            int h = _map.Height;

            for (int step = 0; step < _steps; step++)
            {
                // 1. MOVE PLATE CENTERS
                MovePlates(plates, w, h);

                // 2. REGENERATE VORONOI REGIONS
                VoronoiGenerator.Generate(_map.Lithosphere, new List<Plate>(plates.Values), true);

                // 3. REGENERATE TERRAIN FROM BOUNDARIES
                RegenerateTerrainFromBoundaries(plates, w, h);
            }
        }

        private void MovePlates(Dictionary<int, Plate> plates, int w, int h)
        {
            foreach (var p in plates.Values)
            {
                if (p.IsLocked) continue;

                p.Center += p.Velocity * _speed;

                // Wrap X
                if (p.Center.X < 0) p.Center = new Vector2(p.Center.X + w, p.Center.Y);
                if (p.Center.X >= w) p.Center = new Vector2(p.Center.X - w, p.Center.Y);

                // Bounce Y
                if (p.Center.Y < 0)
                {
                    p.Center = new Vector2(p.Center.X, -p.Center.Y);
                    p.Velocity = new Vector2(p.Velocity.X, -p.Velocity.Y);
                }
                if (p.Center.Y >= h)
                {
                    p.Center = new Vector2(p.Center.X, 2 * h - p.Center.Y);
                    p.Velocity = new Vector2(p.Velocity.X, -p.Velocity.Y);
                }
            }
        }

        private void RegenerateTerrainFromBoundaries(Dictionary<int, Plate> plates, int w, int h)
        {
            var plateGrid = _map.Lithosphere.PlateIdMap;
            var elev = _map.Elevation.RawData;
            var thick = _map.CrustThickness.RawData;

            // Step 1: Calculate boundary stress for each pixel
            float[] boundaryEffect = new float[w * h];
            int[] boundaryType = new int[w * h]; // 0=none, 1=conv, 2=div, 3=trans

            CalculateBoundaryEffects(plates, w, h, boundaryEffect, boundaryType);

            // Step 2: Generate terrain based on plate type + boundary effects + noise
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    int plateId = plateGrid.RawData[idx];

                    if (plateId == 0 || !plates.ContainsKey(plateId))
                    {
                        // Unclaimed - deep ocean
                        elev[idx] = -1.5f;
                        thick[idx] = 0.5f;
                        continue;
                    }

                    Plate p = plates[plateId];

                    // Base elevation from plate type + random per-plate variation
                    float baseElev = p.BaseElevation;

                    // Add per-pixel noise for terrain variation
                    float noise = SimpleNoise.GetFBM(x * 0.02f, y * 0.02f, 3, 0.5f, 2.0f);
                    float pixelVariation = noise * 0.3f;

                    // Add boundary effects (mountains at collision zones)
                    float boundaryBonus = boundaryEffect[idx];

                    // Final elevation
                    float finalElev = baseElev + pixelVariation + boundaryBonus;

                    // Thickness from elevation (isostasy)
                    float finalThick = finalElev + 2.0f;

                    elev[idx] = finalElev;
                    thick[idx] = Math.Clamp(finalThick, 0.5f, 8.0f);
                }
            });
        }

        private void CalculateBoundaryEffects(Dictionary<int, Plate> plates, int w, int h,
            float[] boundaryEffect, int[] boundaryType)
        {
            var plateGrid = _map.Lithosphere.PlateIdMap;
            int[] dx = { 1, -1, 0, 0 };
            int[] dy = { 0, 0, 1, -1 };

            // Pass 1: Find boundary pixels and calculate stress
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    int currentId = plateGrid.RawData[idx];
                    if (currentId == 0 || !plates.ContainsKey(currentId)) continue;

                    Plate pCurrent = plates[currentId];

                    // Check neighbors for boundary
                    for (int i = 0; i < 4; i++)
                    {
                        int nx = x + dx[i];
                        if (nx < 0) nx += w; else if (nx >= w) nx -= w;
                        int ny = y + dy[i];
                        if (ny < 0 || ny >= h) continue;

                        int nId = plateGrid.RawData[ny * w + nx];
                        if (nId != 0 && nId != currentId && plates.ContainsKey(nId))
                        {
                            Plate pNeighbor = plates[nId];

                            // Calculate relative velocity
                            Vector2 normal = new Vector2(dx[i], dy[i]);
                            Vector2 vRel = pCurrent.Velocity - pNeighbor.Velocity;
                            float direct = Vector2.Dot(vRel, normal);

                            // Determine boundary type and effect
                            bool currCont = pCurrent.Type == PlateType.Continental;
                            bool neighCont = pNeighbor.Type == PlateType.Continental;

                            float effect = 0;
                            int type = 0;

                            if (direct > 0.1f) // Convergent
                            {
                                type = 1;
                                if (currCont && neighCont)
                                    effect = 1.5f; // Major mountains (Himalayas)
                                else if (currCont || neighCont)
                                    effect = 0.8f; // Coastal mountains (Andes)
                                else
                                    effect = 0.1f; // Underwater ridge
                            }
                            else if (direct < -0.1f) // Divergent
                            {
                                type = 2;
                                effect = -0.3f; // Rift valley
                            }
                            else // Transform
                            {
                                type = 3;
                                effect = 0.2f; // Minor uplift
                            }

                            // Add noise variation along boundary
                            float boundaryNoise = SimpleNoise.GetFBM(x * 0.1f, y * 0.1f, 2, 0.5f, 2.0f);
                            effect *= (0.5f + boundaryNoise * 0.5f + 0.5f);

                            if (Math.Abs(effect) > Math.Abs(boundaryEffect[idx]))
                            {
                                boundaryEffect[idx] = effect;
                                boundaryType[idx] = type;
                            }
                            break;
                        }
                    }
                }
            });

            // Pass 2: Spread boundary effects inland (distance falloff)
            SpreadBoundaryEffects(w, h, boundaryEffect, boundaryType);
        }

        private void SpreadBoundaryEffects(int w, int h, float[] boundaryEffect, int[] boundaryType)
        {
            // Simple iterative spread with decay
            int spreadPasses = 15;
            float decayRate = 0.85f;

            for (int pass = 0; pass < spreadPasses; pass++)
            {
                float[] source = (float[])boundaryEffect.Clone();

                Parallel.For(0, h, y =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * w + x;
                        if (source[idx] != 0) continue; // Already has effect

                        // Check neighbors for effect to spread
                        float maxNeighborEffect = 0;
                        int[] dx = { 1, -1, 0, 0 };
                        int[] dy = { 0, 0, 1, -1 };

                        for (int i = 0; i < 4; i++)
                        {
                            int nx = x + dx[i];
                            if (nx < 0) nx += w; else if (nx >= w) nx -= w;
                            int ny = y + dy[i];
                            if (ny < 0 || ny >= h) continue;

                            float neighborEffect = source[ny * w + nx];
                            if (Math.Abs(neighborEffect) > Math.Abs(maxNeighborEffect))
                            {
                                maxNeighborEffect = neighborEffect;
                            }
                        }

                        if (maxNeighborEffect != 0)
                        {
                            boundaryEffect[idx] = maxNeighborEffect * decayRate;
                        }
                    }
                });
            }
        }
    }
}

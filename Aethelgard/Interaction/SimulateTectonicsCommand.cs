using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Aethelgard.Core;
using Aethelgard.Simulation;

namespace Aethelgard.Interaction
{
    /// <summary>
    /// One-shot terrain generation based on plate configuration.
    /// 
    /// Generates realistic terrain that LOOKS like it was formed by geology:
    /// 1. Active boundaries: Mountains at plate collisions
    /// 2. Dead seam lines: Fossil mountain ranges inside continents
    /// 3. Hotspots: Random volcanic peaks inside plates
    /// 4. Cratons: Stable continental interiors
    /// 5. Erosion: Smooth and carve terrain
    /// </summary>
    public class SimulateTectonicsCommand : ICommand
    {
        private readonly WorldMap _map;
        private readonly SimulateTectonicsSettings _settings;
        private float[]? _backupElevation;
        private float[]? _backupThickness;

        public SimulateTectonicsCommand(WorldMap map, SimulateTectonicsSettings settings)
        {
            _map = map;
            _settings = settings;
        }

        public void Execute()
        {
            _backupElevation = (float[])_map.Elevation.RawData.Clone();
            _backupThickness = (float[])_map.CrustThickness.RawData.Clone();

            int w = _map.Width;
            int h = _map.Height;
            var plates = _map.Lithosphere.Plates;
            var plateGrid = _map.Lithosphere.PlateIdMap;

            // Working arrays
            float[] elevation = new float[w * h];
            float[] accumulatedEffects = new float[w * h];
            int[] featureType = new int[w * h]; // 0=Ocean, 1=Craton, 2=Active, 3=Fossil, 4=Hotspot, 5=Biogenic

            // Step 1: Calculate distance from coast for each land pixel
            float[] distFromCoast = CalculateDistanceFromCoast(plateGrid, plates, w, h);

            // Step 2: Base elevation from plate type + distance gradient (slopes toward coast)
            GenerateCratons(plates, plateGrid, w, h, elevation, distFromCoast, featureType);

            // Step 3: Active plate boundaries (coastal mountains at subduction zones)
            GenerateActiveBoundaries(plates, plateGrid, w, h, accumulatedEffects, featureType);

            // Step 4: Dead seam lines (fossil boundaries using secondary Voronoi)
            GenerateFossilBoundaries(plates, plateGrid, w, h, accumulatedEffects, featureType);

            // Step 5: Hotspots (intraplate volcanism)
            GenerateHotspots(plates, plateGrid, w, h, accumulatedEffects, featureType);

            // Step 6: Biogenic coastal features (coral reefs, limestone - like Florida/Bahamas)
            GenerateBiogenicCoastalFeatures(plates, plateGrid, w, h, elevation, distFromCoast, featureType);

            // Step 7: Combine base + effects
            CombineElevation(w, h, elevation, accumulatedEffects);

            // Step 8: Erosion pass (smooth transitions)
            ApplyErosion(w, h, elevation);

            // Step 9: Write to map (includes feature types)
            WriteToMap(w, h, elevation, featureType);
        }

        private float[] CalculateDistanceFromCoast(DataGrid<int> plateGrid, Dictionary<int, Plate> plates, int w, int h)
        {
            // Find distance from each continental pixel to nearest ocean
            float[] dist = new float[w * h];
            Array.Fill(dist, float.MaxValue);

            // Pass 1: Mark ocean/land boundaries
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    int pId = plateGrid.RawData[idx];

                    // Check if this is a coast pixel (land next to ocean)
                    if (pId != 0 && plates.TryGetValue(pId, out var p) && p.Type == PlateType.Continental)
                    {
                        bool isCoast = false;
                        for (int dy = -1; dy <= 1 && !isCoast; dy++)
                        {
                            for (int dx = -1; dx <= 1 && !isCoast; dx++)
                            {
                                int nx = x + dx; if (nx < 0) nx += w; if (nx >= w) nx -= w;
                                int ny = y + dy; if (ny < 0 || ny >= h) continue;

                                int nId = plateGrid.RawData[ny * w + nx];
                                if (nId == 0 || (plates.TryGetValue(nId, out var np) && np.Type == PlateType.Oceanic))
                                {
                                    isCoast = true;
                                    dist[idx] = 0;
                                }
                            }
                        }
                    }
                    else
                    {
                        dist[idx] = -1; // Ocean marker
                    }
                }
            }

            // Pass 2: Iterative distance propagation
            for (int pass = 0; pass < 50; pass++)
            {
                bool changed = false;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * w + x;
                        if (dist[idx] < 0) continue; // Ocean

                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx; if (nx < 0) nx += w; if (nx >= w) nx -= w;
                                int ny = y + dy; if (ny < 0 || ny >= h) continue;

                                if (dist[ny * w + nx] >= 0)
                                {
                                    float newDist = dist[ny * w + nx] + 1;
                                    if (newDist < dist[idx])
                                    {
                                        dist[idx] = newDist;
                                        changed = true;
                                    }
                                }
                            }
                        }
                    }
                }
                if (!changed) break;
            }

            return dist;
        }

        private void GenerateCratons(Dictionary<int, Plate> plates, DataGrid<int> plateGrid,
            int w, int h, float[] elevation, float[] distFromCoast, int[] featureType)
        {
            // Continental interiors slope gently from interior highlands to coastal lowlands
            // Oceanic plates are varied depth ocean
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    int plateId = plateGrid.RawData[idx];

                    if (plateId == 0 || !plates.ContainsKey(plateId))
                    {
                        // Varied ocean depth
                        float oceanNoise = SimpleNoise.GetFBM(x * 0.02f, y * 0.02f, 2, 0.5f, 2.0f);
                        elevation[idx] = -1.2f + oceanNoise * 0.4f;
                        featureType[idx] = 0; // Ocean
                        continue;
                    }

                    Plate p = plates[plateId];

                    if (p.Type == PlateType.Oceanic)
                    {
                        // Oceanic plate - varies from shallow to deep
                        float oceanNoise = SimpleNoise.GetFBM(x * 0.015f, y * 0.015f, 3, 0.5f, 2.0f);
                        elevation[idx] = p.BaseElevation + oceanNoise * 0.3f;
                        featureType[idx] = 0; // Ocean
                    }
                    else
                    {
                        // CONTINENTAL PLATE - Partial land coverage
                        // Use noise to determine if this is land or continental shelf
                        // Real plates like Australian plate are mostly ocean with a land portion

                        // Large-scale "continent shape" noise - determines land vs shelf
                        float continentNoise = SimpleNoise.GetFBM(x * 0.008f + p.Id * 100, y * 0.008f + p.Id * 100, 4, 0.5f, 2.0f);

                        // Add distance from plate center as bias - center more likely to be land
                        float dxCenter = x - p.Center.X;
                        if (Math.Abs(dxCenter) > w / 2) dxCenter = w - Math.Abs(dxCenter);
                        float dyCenter = y - p.Center.Y;
                        float distFromCenter = (float)Math.Sqrt(dxCenter * dxCenter + dyCenter * dyCenter);
                        float centerBias = Math.Clamp(1f - distFromCenter / 120f, -0.3f, 0.5f);

                        // LATITUDE BIAS - equatorial vs polar land preference (from settings)
                        // y=0 and y=h are poles, y=h/2 is equator
                        float normalizedY = (float)y / h; // 0 to 1
                        float latitudeFromEquator = Math.Abs(normalizedY - 0.5f) * 2f; // 0 at equator, 1 at poles
                        float latBias = -_settings.LatitudeBias * latitudeFromEquator; // Positive setting = less land at poles

                        // Combine noise + center bias + latitude bias, then apply threshold (from settings)
                        float landScore = continentNoise + centerBias + latBias;

                        if (landScore > _settings.LandCoverageThreshold)
                        {
                            // LAND - generate terrain
                            float dist = distFromCoast[idx];
                            if (dist < 0) dist = 0;

                            float normalizedDist = Math.Clamp(dist / 80f, 0f, 1f);
                            float coastToInteriorSlope = (float)Math.Sqrt(normalizedDist) * _settings.CoastSlopeStrength;

                            float regional = SimpleNoise.GetFBM(x * 0.006f + p.Id, y * 0.006f + p.Id, 4, 0.55f, 2.0f);
                            float regionalVariation = regional * _settings.RegionalNoiseStrength;

                            float local = SimpleNoise.GetFBM(x * 0.025f, y * 0.025f, 3, 0.5f, 2.0f);
                            float localVariation = local * _settings.LocalNoiseStrength;

                            float finalElev = _settings.BaseElevation + coastToInteriorSlope + regionalVariation + localVariation;
                            finalElev = Math.Max(finalElev, 0.02f);

                            elevation[idx] = finalElev;
                            featureType[idx] = 1; // Craton (land)
                        }
                        else
                        {
                            // CONTINENTAL SHELF - shallow ocean within the plate
                            float shelfNoise = SimpleNoise.GetFBM(x * 0.02f, y * 0.02f, 2, 0.5f, 2.0f);
                            elevation[idx] = -0.4f + shelfNoise * 0.2f; // Shallow shelf
                            featureType[idx] = 0; // Treated as ocean for rendering
                        }
                    }
                }
            });
        }

        private void GenerateActiveBoundaries(Dictionary<int, Plate> plates, DataGrid<int> plateGrid,
            int w, int h, float[] effects, int[] featureType)
        {
            int[] dx = { 1, -1, 0, 0 };
            int[] dy = { 0, 0, 1, -1 };

            // Pass 1: Find boundaries with POSITION-BASED boundary type variation
            // This simulates rotating/squishing plates where pressure varies along the edge
            float[] boundaryStress = new float[w * h];

            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    int currentId = plateGrid.RawData[idx];
                    if (currentId == 0 || !plates.ContainsKey(currentId)) continue;

                    Plate pCurrent = plates[currentId];

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
                            bool currCont = pCurrent.Type == PlateType.Continental;
                            bool neighCont = pNeighbor.Type == PlateType.Continental;

                            // POSITION-BASED boundary type!
                            // Varies along the boundary - some parts convergent, some passive, some divergent
                            // Use plate IDs in noise seed for consistency per plate pair
                            float pairSeed = (currentId * 1000 + nId) * 0.001f;
                            float boundaryNoise = SimpleNoise.GetFBM(x * 0.03f + pairSeed, y * 0.03f, 3, 0.5f, 2.0f);

                            // Map noise to boundary type
                            // Continental-Oceanic boundaries are MORE LIKELY to be convergent (subduction zones)
                            float effect = 0;
                            bool isMixedBoundary = (currCont && !neighCont) || (!currCont && neighCont);

                            // Lower threshold for mixed (subduction) boundaries - they're usually convergent
                            float convergentThreshold = isMixedBoundary ? -0.1f : 0.3f;

                            if (boundaryNoise > convergentThreshold)
                            {
                                // CONVERGENT zone - mountains!
                                float thresholdRange = 1.0f - convergentThreshold;
                                float intensity = (boundaryNoise - convergentThreshold) / thresholdRange;
                                intensity = Math.Clamp(intensity, 0f, 1f);

                                if (currCont && neighCont)
                                    effect = 1.0f + intensity * 1.5f; // 1.0 to 2.5 (Himalayas)
                                else if (currCont || neighCont)
                                    effect = 0.4f + intensity * 1.2f; // 0.4 to 1.6 (Andes - coastal mountains)
                                else
                                    effect = 0.1f + intensity * 0.3f; // Underwater ridge
                            }
                            else if (boundaryNoise < -0.3f)
                            {
                                // DIVERGENT zone - rift/depression (only if not a subduction zone)
                                if (!isMixedBoundary)
                                {
                                    float intensity = (-boundaryNoise - 0.3f) / 0.7f;
                                    effect = -0.1f - intensity * 0.3f;
                                }
                            }
                            // Middle range: PASSIVE - no significant effect

                            if (Math.Abs(effect) > 0.05f)
                            {
                                // Add fine-grained variation
                                float fineNoise = SimpleNoise.GetNoise(x * 0.15f, y * 0.15f);
                                effect *= (0.7f + fineNoise * 0.3f);

                                if (effect > 0)
                                {
                                    boundaryStress[idx] = Math.Max(boundaryStress[idx], effect);
                                    featureType[idx] = 2; // Active Boundary
                                }
                                else
                                    boundaryStress[idx] = Math.Min(boundaryStress[idx], effect);
                            }
                            break;
                        }
                    }
                }
            });

            // Pass 2: Spread boundary effects inland
            SpreadEffects(w, h, boundaryStress, effects, 20, 0.88f);
        }

        private void GenerateFossilBoundaries(Dictionary<int, Plate> plates, DataGrid<int> plateGrid,
            int w, int h, float[] effects, int[] featureType)
        {
            // SECONDARY VORONOI for organic fossil boundaries
            // Create sub-regions within continental plates - boundaries between them are fossil mountain ranges

            var random = new Random(12345);
            var continentalPlates = new List<Plate>();

            foreach (var p in plates.Values)
            {
                if (p.Type == PlateType.Continental)
                    continentalPlates.Add(p);
            }

            // Create secondary Voronoi seeds within each continental plate
            int[] subRegion = new int[w * h]; // Which sub-region each pixel belongs to
            var subSeeds = new List<(int x, int y, int region)>();
            int regionId = 1;

            foreach (var plate in continentalPlates)
            {
                // Sub-regions per continental plate (from settings)
                int numSubRegions = _settings.FossilSubRegionsMin + random.Next(_settings.FossilSubRegionsMax - _settings.FossilSubRegionsMin + 1);

                for (int i = 0; i < numSubRegions; i++)
                {
                    int sx = (int)(plate.Center.X + (random.NextDouble() - 0.5) * 150);
                    int sy = (int)(plate.Center.Y + (random.NextDouble() - 0.5) * 150);

                    // Wrap X
                    while (sx < 0) sx += w; while (sx >= w) sx -= w;
                    sy = Math.Clamp(sy, 0, h - 1);

                    subSeeds.Add((sx, sy, regionId++));
                }
            }

            // Assign each continental pixel to nearest sub-seed WITH NOISE OFFSET
            // This makes fossil boundaries organic/curved instead of straight lines
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    int pId = plateGrid.RawData[idx];

                    if (pId == 0 || !plates.TryGetValue(pId, out var p) || p.Type != PlateType.Continental)
                        continue;

                    // Add noise offset for organic boundaries (from settings)
                    float noiseX = SimpleNoise.GetFBM(x * 0.02f, y * 0.02f, 3, 0.5f, 2.0f) * _settings.FossilNoiseOffset;
                    float noiseY = SimpleNoise.GetFBM(x * 0.02f + 100, y * 0.02f + 100, 3, 0.5f, 2.0f) * _settings.FossilNoiseOffset;

                    float minDist = float.MaxValue;
                    int nearestRegion = 0;

                    foreach (var (sx, sy, region) in subSeeds)
                    {
                        float ddx = Math.Abs((x + noiseX) - sx);
                        if (ddx > w / 2) ddx = w - ddx;
                        float ddy = Math.Abs((y + noiseY) - sy);
                        float dist = ddx * ddx + ddy * ddy;

                        if (dist < minDist)
                        {
                            minDist = dist;
                            nearestRegion = region;
                        }
                    }

                    subRegion[idx] = nearestRegion;
                }
            });

            // Find boundaries between sub-regions = fossil mountain ranges
            int[] dx = { 1, -1, 0, 0 };
            int[] dy = { 0, 0, 1, -1 };

            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    if (subRegion[idx] == 0) continue;

                    for (int i = 0; i < 4; i++)
                    {
                        int nx = x + dx[i];
                        if (nx < 0) nx += w; else if (nx >= w) nx -= w;
                        int ny = y + dy[i];
                        if (ny < 0 || ny >= h) continue;

                        int nRegion = subRegion[ny * w + nx];
                        if (nRegion != 0 && nRegion != subRegion[idx])
                        {
                            // Fossil boundary with VARIABLE EROSION
                            // Some sections more eroded (flat), others still prominent
                            float erosionNoise = SimpleNoise.GetFBM(x * 0.015f, y * 0.015f, 2, 0.5f, 2.0f);
                            float erosionFactor = 0.3f + erosionNoise * 0.7f; // 0.0 to 1.0 (0=heavily eroded, 1=intact)
                            erosionFactor = Math.Clamp(erosionFactor, 0.1f, 1.0f);

                            float noise = SimpleNoise.GetFBM(x * 0.1f, y * 0.1f, 2, 0.5f, 2.0f);
                            float effect = _settings.FossilMountainStrength * (0.4f + noise * 0.3f) * erosionFactor;
                            effects[idx] = Math.Max(effects[idx], effect);
                            featureType[idx] = 3; // Fossil Boundary
                            break;
                        }
                    }
                }
            });

            // Spread fossil effects (narrower than active boundaries)
            float[] fossilSpread = new float[w * h];
            Array.Copy(effects, fossilSpread, effects.Length);
            SpreadEffects(w, h, fossilSpread, effects, 6, 0.75f);
        }

        private void GenerateHotspots(Dictionary<int, Plate> plates, DataGrid<int> plateGrid,
            int w, int h, float[] effects, int[] featureType)
        {
            // Hotspots: volcanic peaks in the middle of plates
            // Examples: Hawaii, Ahaggar Mountains

            var random = new Random(54321);

            // Generate hotspots (from settings)
            int numHotspots = _settings.HotspotsMin + random.Next(_settings.HotspotsMax - _settings.HotspotsMin + 1);

            for (int i = 0; i < numHotspots; i++)
            {
                int hx = random.Next(w);
                int hy = random.Next(h);
                int idx = hy * w + hx;

                int plateId = plateGrid.RawData[idx];
                if (plateId == 0 || !plates.ContainsKey(plateId)) continue;

                // HOTSPOT AGE - older hotspots = more magma deposited = larger islands
                float age = (float)random.NextDouble(); // 0.0 = young, 1.0 = old
                int spreadRadius = 5 + (int)(age * 20); // Young: 5px, Old: 25px

                // Volcanic peak (from settings) - older hotspots slightly taller
                float peakHeight = _settings.HotspotPeakMin + (float)random.NextDouble() * (_settings.HotspotPeakMax - _settings.HotspotPeakMin);
                peakHeight *= (0.8f + age * 0.4f); // Age boost

                // Create volcanic cluster (from settings)
                int clusterSize = _settings.HotspotClusterSizeMin + random.Next(_settings.HotspotClusterSizeMax - _settings.HotspotClusterSizeMin + 1);
                clusterSize = (int)(clusterSize * (0.5f + age)); // Older = more peaks

                for (int c = 0; c < clusterSize; c++)
                {
                    int cx = hx + random.Next(-spreadRadius, spreadRadius);
                    int cy = hy + random.Next(-spreadRadius, spreadRadius);

                    while (cx < 0) cx += w; while (cx >= w) cx -= w;
                    if (cy < 0 || cy >= h) continue;

                    int cidx = cy * w + cx;
                    float dist = (float)Math.Sqrt((cx - hx) * (cx - hx) + (cy - hy) * (cy - hy));
                    float falloff = Math.Max(0, 1 - dist / 15);

                    effects[cidx] = Math.Max(effects[cidx], peakHeight * falloff);
                    if (falloff > 0.3f) featureType[cidx] = 4; // Hotspot
                }
            }

            // Spread hotspot effects (very local)
            float[] hotspotSpread = new float[w * h];
            Array.Copy(effects, hotspotSpread, effects.Length);
            SpreadEffects(w, h, hotspotSpread, effects, 5, 0.75f);
        }

        private void GenerateBiogenicCoastalFeatures(Dictionary<int, Plate> plates, DataGrid<int> plateGrid,
            int w, int h, float[] elevation, float[] distFromCoast, int[] featureType)
        {
            // Biogenic features: Coral reefs and limestone plateaus
            // Low-lying coastal land formed from marine life deposits
            // Examples: Florida, Bahamas, White Cliffs of Dover

            var random = new Random(77777);

            // Generate biogenic coastal zones (from settings)
            int numZones = _settings.BiogenicZonesMin + random.Next(_settings.BiogenicZonesMax - _settings.BiogenicZonesMin + 1);

            for (int i = 0; i < numZones; i++)
            {
                // Random coastal point
                int cx = random.Next(w);
                int cy = random.Next(h);
                int idx = cy * w + cx;

                int pId = plateGrid.RawData[idx];
                if (pId == 0 || !plates.TryGetValue(pId, out var p) || p.Type != PlateType.Continental)
                    continue;

                // Only near coast (distance 0-20)
                float dist = distFromCoast[idx];
                if (dist < 0 || dist > 20) continue;

                // Create a biogenic zone - low flat land (from settings)
                int zoneSize = _settings.BiogenicZoneSizeMin + random.Next(_settings.BiogenicZoneSizeMax - _settings.BiogenicZoneSizeMin);

                for (int dy = -zoneSize; dy <= zoneSize; dy++)
                {
                    for (int dx = -zoneSize; dx <= zoneSize; dx++)
                    {
                        int nx = cx + dx; if (nx < 0) nx += w; if (nx >= w) nx -= w;
                        int ny = cy + dy; if (ny < 0 || ny >= h) continue;

                        int nidx = ny * w + nx;
                        float ndist = distFromCoast[nidx];
                        if (ndist < 0 || ndist > 25) continue; // Only coastal areas

                        float zoneDist = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (zoneDist > zoneSize) continue;

                        float falloff = 1f - zoneDist / zoneSize;

                        // Low, flat coastal plain (limestone plateau) (from settings)
                        float biogenicNoise = SimpleNoise.GetFBM(nx * 0.05f, ny * 0.05f, 2, 0.5f, 2.0f);
                        float targetElev = _settings.BiogenicElevation + biogenicNoise * 0.03f;

                        // Blend with existing elevation
                        elevation[nidx] = elevation[nidx] * (1 - falloff * 0.5f) + targetElev * falloff * 0.5f;
                        if (falloff > 0.3f) featureType[nidx] = 5; // Biogenic
                    }
                }
            }
        }

        private void SpreadEffects(int w, int h, float[] source, float[] dest, int passes, float decay)
        {
            float[] current = (float[])source.Clone();

            for (int pass = 0; pass < passes; pass++)
            {
                float[] next = (float[])current.Clone();

                Parallel.For(0, h, y =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * w + x;
                        if (current[idx] > 0.01f) continue;

                        float maxNeighbor = 0;
                        int[] dx = { 1, -1, 0, 0 };
                        int[] dy = { 0, 0, 1, -1 };

                        for (int i = 0; i < 4; i++)
                        {
                            int nx = x + dx[i];
                            if (nx < 0) nx += w; else if (nx >= w) nx -= w;
                            int ny = y + dy[i];
                            if (ny < 0 || ny >= h) continue;

                            maxNeighbor = Math.Max(maxNeighbor, current[ny * w + nx]);
                        }

                        if (maxNeighbor > 0.01f)
                            next[idx] = Math.Max(next[idx], maxNeighbor * decay);
                    }
                });

                current = next;
            }

            // Merge into dest (take max)
            for (int i = 0; i < w * h; i++)
                dest[i] = Math.Max(dest[i], current[i]);
        }

        private void CombineElevation(int w, int h, float[] elevation, float[] effects)
        {
            Parallel.For(0, w * h, i =>
            {
                elevation[i] += effects[i];
            });
        }

        private void ApplyErosion(int w, int h, float[] elevation)
        {
            // Thermal erosion: smooth steep slopes
            for (int iter = 0; iter < 5; iter++)
            {
                float[] source = (float[])elevation.Clone();

                Parallel.For(0, h, y =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * w + x;
                        float current = source[idx];

                        // Skip ocean
                        if (current < 0) continue;

                        float minNeighbor = current;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = x + dx; if (nx < 0) nx += w; if (nx >= w) nx -= w;
                                int ny = y + dy; if (ny < 0 || ny >= h) continue;

                                minNeighbor = Math.Min(minNeighbor, source[ny * w + nx]);
                            }
                        }

                        float diff = current - minNeighbor;
                        if (diff > 0.1f)
                            elevation[idx] -= diff * 0.15f * _settings.ErosionRate;
                    }
                });
            }
        }

        private void WriteToMap(int w, int h, float[] elevation, int[] featureType)
        {
            Parallel.For(0, w * h, i =>
            {
                _map.Elevation.RawData[i] = elevation[i];
                _map.CrustThickness.RawData[i] = Math.Clamp(elevation[i] + 2.0f, 0.5f, 8.0f);
                _map.FeatureType.RawData[i] = featureType[i];
            });
        }

        public void Undo()
        {
            if (_backupElevation != null)
                Array.Copy(_backupElevation, _map.Elevation.RawData, _backupElevation.Length);
            if (_backupThickness != null)
                Array.Copy(_backupThickness, _map.CrustThickness.RawData, _backupThickness.Length);
        }
    }
}

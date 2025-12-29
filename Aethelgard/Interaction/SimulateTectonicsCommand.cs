using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Aethelgard.Core;
using Aethelgard.Simulation;
using System.Linq;

namespace Aethelgard.Interaction
{
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

        private float GetLatitudeScaledX(int x, int y, int h, float baseScale)
        {
            if (!_settings.UseSphericalNoise)
                return x * baseScale;
            float latFactor = SphericalMath.LatitudeFactor(y, h);
            latFactor = Math.Max(latFactor, 0.1f);
            return x * baseScale / latFactor;
        }

        public void Execute()
        {
            _backupElevation = (float[])_map.Elevation.RawData.Clone();
            _backupThickness = (float[])_map.CrustThickness.RawData.Clone();

            int w = _map.Width;
            int h = _map.Height;
            var plates = _map.Lithosphere.Plates;
            var plateGrid = _map.Lithosphere.PlateIdMap;

            float[] thickness = new float[w * h];
            float[] elevation = new float[w * h];
            int[] featureType = new int[w * h];

            GenerateBaseCrustThickness(plates, plateGrid, w, h, thickness, featureType);
            ApplyFossilSutures(plates, plateGrid, w, h, thickness, featureType);
            ApplyBoundaryStress(plates, plateGrid, w, h, thickness, featureType);
            ApplyMantleDynamics(w, h, thickness);
            ApplyHotspots(plates, plateGrid, w, h, thickness, featureType);
            Parallel.For(0, w * h, i => thickness[i] = Math.Clamp(thickness[i], 3f, 85f));
            DeriveElevationFromThickness(w, h, thickness, elevation);
            // Step 10: Glacial Erosion (Fjords & U-Valleys) - BEFORE thermal, to set valley shape
            ApplyGlacialErosion(w, h, elevation);

            // Step 11: Thermal Erosion (Smoothing)
            ApplyErosion(w, h, elevation);

            // Step 12: FLOW EROSION (Carve valleys)
            ApplyFlowErosion(w, h, elevation);

            // Step 13: Sediment Deposition (Plains)
            ApplySedimentation(w, h, elevation);

            // Step 14: Rugged Peaks (Detail)
            ApplyRuggedPeaks(w, h, elevation);

            // Write to map
            WriteToMap(w, h, thickness, elevation, featureType);
        }

        private void GenerateBaseCrustThickness(Dictionary<int, Plate> plates, DataGrid<int> plateGrid,
             int w, int h, float[] thickness, int[] featureType)
        {
            var random = new Random(12345);
            var plateThicknessVar = new Dictionary<int, float>();
            foreach (var p in plates.Values)
                plateThicknessVar[p.Id] = (float)(random.NextDouble() - 0.5) * _settings.ThicknessVariation * 2;

            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    int plateId = plateGrid.RawData[idx];

                    if (plateId == 0 || !plates.ContainsKey(plateId))
                    {
                        thickness[idx] = _settings.OceanicThickness * 0.5f;
                        featureType[idx] = 0;
                        continue;
                    }

                    Plate p = plates[plateId];
                    float baseThick = p.Type == PlateType.Continental
                        ? _settings.ContinentalThickness
                        : _settings.OceanicThickness;

                    float variation = plateThicknessVar.GetValueOrDefault(plateId, 0);
                    float noiseScale = 0.008f;
                    float noise = SimpleNoise.GetFBM(x * noiseScale + plateId * 50, y * noiseScale + plateId * 50, 2, 0.5f, 2.0f);
                    float localVar = noise * _settings.ThicknessVariation * 0.25f;

                    // Accretionary Coastlines (Sediment Noise)
                    // High-frequency noise to break up the perfect isostasy contour
                    float sedNoise = SimpleNoise.GetFBM(x * 0.1f, y * 0.1f, 3, 0.5f, 2.0f);
                    float sedVar = sedNoise * 1.5f; // Small amplitude, but enough to shift the 0-level

                    thickness[idx] = baseThick + variation + localVar + sedVar;
                    featureType[idx] = p.Type == PlateType.Continental ? 1 : 0;
                }
            });
        }

        private void ApplyBoundaryStress(Dictionary<int, Plate> plates, DataGrid<int> plateGrid,
            int w, int h, float[] thickness, int[] featureType)
        {
            int[] dx = { 1, -1, 0, 0 };
            int[] dy = { 0, 0, 1, -1 };

            float[] boundaryDist = new float[w * h];
            float[] boundaryStress = new float[w * h];
            float[] boundaryU = new float[w * h];
            int[] boundaryType = new int[w * h]; // Type: 2=Arc, 10=CollisionPlateau
            bool[] isDeforming = new bool[w * h];

            Array.Fill(boundaryDist, float.MaxValue);
            Array.Fill(boundaryStress, 0f);
            Array.Fill(boundaryType, 0);

            // Pass 1: Identify boundary seeds
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    int currentId = plateGrid.RawData[idx];
                    if (currentId == 0) continue;

                    bool boundaryFound = false;
                    for (int i = 0; i < 4; i++)
                    {
                        int nx = x + dx[i]; if (nx < 0) nx += w; if (nx >= w) nx -= w;
                        int ny = y + dy[i]; if (ny < 0 || ny >= h) continue;

                        int neighborId = plateGrid.RawData[ny * w + nx];
                        if (neighborId != 0 && neighborId != currentId)
                        {
                            if (!plates.TryGetValue(currentId, out var current) ||
                                !plates.TryGetValue(neighborId, out var neighbor)) continue;

                            var relVel = neighbor.Velocity - current.Velocity;
                            float normalX = dx[i], normalY = dy[i];
                            float tangentX = -dy[i], tangentY = dx[i];
                            float approach = -(relVel.X * normalX + relVel.Y * normalY);
                            float shear = Math.Abs(relVel.X * tangentX + relVel.Y * tangentY);

                            if (approach > 0.1f && approach > shear * 0.5f) // Convergent
                            {
                                bool currCont = current.Type == PlateType.Continental;
                                bool neighCont = neighbor.Type == PlateType.Continental;
                                bool currentSubducts;

                                if (currCont && !neighCont) currentSubducts = false;
                                else if (!currCont && neighCont) currentSubducts = true;
                                else if (!currCont && !neighCont) currentSubducts = approach > 0;
                                else currentSubducts = false; // C-C Collision

                                if (currCont && neighCont)
                                {
                                    // C-C Collision: Both sides deform
                                    boundaryDist[idx] = 0;
                                    boundaryStress[idx] = approach;
                                    boundaryU[idx] = x * tangentX + y * tangentY;
                                    // Set Special Type 10 for Plateau/Collision
                                    boundaryType[idx] = 10;
                                    isDeforming[idx] = true;
                                    boundaryFound = true;
                                    featureType[idx] = 2;
                                }
                                else if (!currentSubducts)
                                {
                                    // Overriding plate: Active Orogen (Arc)
                                    boundaryDist[idx] = 0;
                                    boundaryStress[idx] = approach;
                                    boundaryU[idx] = x * tangentX + y * tangentY;
                                    boundaryType[idx] = 2; // Normal Arc
                                    isDeforming[idx] = true;
                                    boundaryFound = true;
                                    featureType[idx] = 2;
                                }
                                else
                                {
                                    thickness[idx] -= 8f * approach;
                                    featureType[idx] = 7;
                                }
                            }
                            else if (approach < -0.1f) // Divergent
                            {
                                bool isOceanic = (current.Type == PlateType.Oceanic && neighbor.Type == PlateType.Oceanic);
                                featureType[idx] = isOceanic ? 5 : 8; // 5=Ridge, 8=Rift

                                // Variable Width Logic (Applicable to both Rifts and Mountains)
                                // Vary width along the strike 'u' to prevent "sausage" uniformity
                                float widthVar = SimpleNoise.GetNoise(x * 0.04f, y * 0.04f); // -1..1
                                float varyFactor = 1.0f + widthVar * 0.4f; // 0.6..1.4 variation

                                if (isOceanic)
                                {
                                    // Ocean Ridge: Just mark type
                                    // Deformation happens in Pass 3
                                }
                                else
                                {
                                    // Continental Rift: Just mark type
                                    // Deformation happens in Pass 3
                                }
                            }
                        }
                        if (boundaryFound) break;
                    }
                }
            });

            // Pass 2: Propagate Orogen Field (Distance + Stress + Type)
            int[] dx8 = { 1, -1, 0, 0, 1, -1, 1, -1 };
            int[] dy8 = { 0, 0, 1, -1, 1, 1, -1, -1 };
            float[] dd8 = { 1f, 1f, 1f, 1f, 1.414f, 1.414f, 1.414f, 1.414f };
            float maxDist = Math.Max(_settings.PlateauWidth + _settings.BoundaryWidth * 2, 100f);

            int passes = (int)maxDist + 5;
            for (int pass = 0; pass < passes; pass++)
            {
                bool changed = false;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * w + x;
                        if (boundaryDist[idx] >= maxDist) continue;

                        float myDist = boundaryDist[idx];
                        float myStress = boundaryStress[idx];
                        float myU = boundaryU[idx];
                        int myType = boundaryType[idx];
                        int myPlate = plateGrid.RawData[idx];

                        if (myDist > maxDist) continue;

                        for (int d = 0; d < 8; d++)
                        {
                            int nx = x + dx8[d]; if (nx < 0) nx += w; if (nx >= w) nx -= w;
                            int ny = y + dy8[d]; if (ny < 0 || ny >= h) continue;
                            int nIdx = ny * w + nx;

                            if (plateGrid.RawData[nIdx] != myPlate) continue; // Boundary constraint

                            float newDist = myDist + dd8[d];
                            if (newDist < boundaryDist[nIdx] && newDist <= maxDist)
                            {
                                boundaryDist[nIdx] = newDist;
                                boundaryStress[nIdx] = myStress;
                                boundaryU[nIdx] = myU;
                                boundaryType[nIdx] = myType; // Propagate TYPE
                                isDeforming[nIdx] = true;
                                changed = true;
                            }
                        }
                    }
                }
                if (!changed) break;
            }

            // Pass 3: Apply Uplift Function
            // -------------------------------------------------------------
            // TYPE 2 & 10: CONVERGENT MOUNTAINS
            // -------------------------------------------------------------
            // Note: baseD0, coreW, platW are now calculated per-pixel inside the loop for variability.

            Parallel.For(0, w * h, i =>
            {
                if (!isDeforming[i]) return;

                float dist = boundaryDist[i];
                if (dist > maxDist) return;

                float stress = boundaryStress[i];
                int plateId = plateGrid.RawData[i];

                // Calculate basic parameters
                bool isReverse = (boundaryU[i] < 0);
                float u = Math.Abs(boundaryU[i]);
                int type = boundaryType[i];

                // Variable Width Logic (Applicable to both Rifts and Mountains)
                float widthVar = SimpleNoise.GetNoise(u * 0.04f, 750f);
                float varyFactor = 1.0f + widthVar * 0.4f;

                // Calculate Mountain Params (Used for Type 2/10 and early exit)
                float mtnWidthVar = SimpleNoise.GetNoise(u * 0.02f, 888f);
                float mtnVaryFactor = 1.0f + mtnWidthVar * 0.4f;

                float widthFactor = (type == 10) ? 1.5f : 1.0f;
                widthFactor *= mtnVaryFactor;

                float effCoreW = _settings.ArcWidth * widthFactor;
                float effPlatW = _settings.PlateauWidth * mtnVaryFactor;
                float effCoastBuf = _settings.CoastBuffer;

                float baseD0 = isReverse ? _settings.ArcOffset : _settings.ArcOffset + 5f;
                float d0 = (type == 10) ? 0f : baseD0;
                float effD0 = d0 * widthFactor;

                // Optimization: Skip if too far
                // We add a safety margin to the check
                if (dist > effD0 + effCoreW + effPlatW + effCoastBuf + 20f) return;

                // -------------------------------------------------------------
                // TYPE 5: OCEAN RIDGE & TYPE 8: CONTINENTAL RIFT
                // -------------------------------------------------------------
                if (type == 5 || type == 8)
                {
                    if (type == 5) // Ocean Ridge
                    {
                        // Simple Bell Curve Thinning
                        float ridgeWidth = 20f * varyFactor;
                        float shape = MathF.Exp(-(dist * dist) / (2 * (ridgeWidth / 2) * (ridgeWidth / 2)));
                        thickness[i] -= _settings.DivergentThinning * shape * stress;
                    }
                    else // Type 8: Continental Rift (Graben)
                    {
                        // Braided Valley Logic
                        float riftWidth = _settings.RiftWidth * 2f * varyFactor;

                        // Normalized distance from center (-1 to 1 approx)
                        float dNorm = dist / (riftWidth * 0.5f);

                        // Stretch noise along strike (u)
                        float riftStretchU = u * 0.15f;
                        float riftStretchD = dNorm * 2.5f;

                        // Ridged Noise for the "Cracks"
                        float riftN = SimpleNoise.GetFBM(riftStretchD, riftStretchU, 3, 0.5f, 2f);
                        float valley = 1f - MathF.Abs(riftN); // Inverted ridge
                        valley = valley * valley * valley; // Sharpen

                        // Falloff at edges
                        float falloff = MathF.Exp(-(dist * dist) / (2 * riftWidth * riftWidth));

                        float thinning = _settings.DivergentThinning * valley * falloff * stress;
                        thickness[i] -= thinning;

                        // Rift Shoulders: Uplift at the edges of the valley
                        // Create a rim around the valley
                        float rim = MathF.Exp(-(dist - riftWidth * 0.8f) * (dist - riftWidth * 0.8f) / (2 * 4f * 4f));
                        thickness[i] += rim * 3.0f * stress;
                    }
                    return; // Skip the rest (mountain logic)
                }

                // -------------------------------------------------------------
                // TYPE 2 & 10: CONVERGENT MOUNTAINS
                // 2. Base Profile: Core + Plateau
                // Core: sharp peak at dist d0
                float core = MathF.Exp(-((dist - effD0) * (dist - effD0)) / (2 * effCoreW * effCoreW));

                // Plateau: broad support
                float plateau = MathF.Exp(-(dist * dist) / (2 * effPlatW * effPlatW));

                // 3. Braided Orogeny (Anisotropic Ridged Noise)
                float bandScale = _settings.BandScale;
                // Stretch noise along 'u' (strike) to make ridges run parallel
                // But let them weave and split randomly
                float stretchU = u * 0.15f; // Low freq along strike
                float stretchD = dist * (1f / bandScale); // High freq across strike

                // Use FBM for complex ridge structure
                float n = SimpleNoise.GetFBM(stretchD, stretchU, 3, 0.5f, 2f);

                // Ridged multifractal: 1 - Abs(noise) makes sharp ridges
                float ridged = 1f - MathF.Abs(n);
                ridged = ridged * ridged; // Sharpen peaks

                // Modulation
                // Use mtnVaryFactor to modulate ridge height too for more variety
                float banding = 0.3f + 0.7f * ridged;

                // 4. Combined Uplift
                // Forearc subsidence: BEFORE the peak (d < d0) - Only for Arcs
                float forearc = 0f;
                if (type == 2 && dist < effD0)
                {
                    forearc = -_settings.ForearcSubsidence * MathF.Exp(-(dist * dist) / (2 * 5f * 5f));
                }

                // AmpFactor derived from width variability for natural look
                float ampFactor = 0.8f + 0.5f * (mtnVaryFactor - 0.5f);
                if (type == 10) ampFactor *= 1.5f; // Collisions are bigger

                float upliftProfile = (0.6f * core + 0.4f * plateau) * banding;
                float totalUplift = _settings.ConvergentThickening * stress * ampFactor * upliftProfile;

                thickness[i] += totalUplift + forearc;
            });
        }

        private void ApplyFlowErosion(int w, int h, float[] elevation)
        {
            float[] water = new float[w * h];
            Array.Fill(water, 1f);

            int[] indices = new int[w * h];
            for (int i = 0; i < w * h; i++) indices[i] = i;

            Array.Sort(indices, (a, b) => elevation[b].CompareTo(elevation[a]));

            int[] dx8 = { 1, -1, 0, 0, 1, -1, 1, -1 };
            int[] dy8 = { 0, 0, 1, -1, 1, 1, -1, -1 };
            float[] dd8 = { 1f, 1f, 1f, 1f, 1.414f, 1.414f, 1.414f, 1.414f };

            float[] erosionAmount = new float[w * h];

            foreach (int idx in indices)
            {
                if (elevation[idx] < 0) continue;

                int cx = idx % w;
                int cy = idx / w;

                float minElev = elevation[idx];
                int targetIdx = -1;

                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + dx8[d]; if (nx < 0) nx += w; if (nx >= w) nx -= w;
                    int ny = cy + dy8[d]; if (ny < 0 || ny >= h) continue;
                    int nIdx = ny * w + nx;

                    if (elevation[nIdx] < minElev)
                    {
                        minElev = elevation[nIdx];
                        targetIdx = nIdx;
                    }
                }

                if (targetIdx != -1)
                {
                    water[targetIdx] += water[idx];
                    if (water[idx] > 10f)
                    {
                        float slope = (elevation[idx] - minElev);
                        float carve = _settings.FlowErosionStrength * MathF.Sqrt(water[idx]) * slope;
                        carve = Math.Min(carve, slope * 0.8f);
                        erosionAmount[idx] = carve;
                    }
                }
            }

            Parallel.For(0, w * h, i =>
            {
                if (erosionAmount[i] > 0)
                {
                    elevation[i] -= erosionAmount[i];
                }
            });
        }

        private void ApplyFossilSutures(Dictionary<int, Plate> plates, DataGrid<int> plateGrid,
            int w, int h, float[] thickness, int[] featureType)
        {
            var random = new Random(55555);
            var continentalPlates = plates.Values.Where(p => p.Type == PlateType.Continental).ToList();
            int[] subRegion = new int[w * h];
            var seedsByPlate = new Dictionary<int, List<(int x, int y, int region)>>();
            int regionId = 1;

            foreach (var plate in continentalPlates)
            {
                seedsByPlate[plate.Id] = new List<(int, int, int)>();
                int numSubRegions = _settings.FossilSubRegionsMin +
                    random.Next(_settings.FossilSubRegionsMax - _settings.FossilSubRegionsMin + 1);

                for (int i = 0; i < numSubRegions; i++)
                {
                    int sx = (int)(plate.Center.X + (random.NextDouble() - 0.5) * 150);
                    int sy = (int)(plate.Center.Y + (random.NextDouble() - 0.5) * 150);
                    while (sx < 0) sx += w; while (sx >= w) sx -= w;
                    sy = Math.Clamp(sy, 0, h - 1);
                    seedsByPlate[plate.Id].Add((sx, sy, regionId++));
                }
            }

            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    int pId = plateGrid.RawData[idx];
                    if (pId == 0 || !plates.TryGetValue(pId, out var p) || p.Type != PlateType.Continental)
                        continue;

                    if (!seedsByPlate.TryGetValue(pId, out var mySeeds) || mySeeds.Count == 0)
                        continue;

                    // Fossil Provinces: Broad, eroded highlands with STRUCTURE (Grain)
                    // Each province maps to a seed which should have a dominant "Strike Angle"
                    // We generate a pseudo-random angle based on seed coordinates to keep it deterministic
                    foreach (var seed in mySeeds)
                    {
                        float dxWrap = Math.Abs(x - seed.x);
                        if (dxWrap > w / 2) dxWrap = w - dxWrap;
                        float dy = y - seed.y;
                        float distSq = dxWrap * dxWrap + dy * dy;

                        // Province Radius (Large area)
                        float radiusSq = 65f * 65f;
                        if (distSq < radiusSq)
                        {
                            // 1. Determine Strike Angle from Seed hash
                            int seedHash = seed.x * 73856093 ^ seed.y * 19349663;
                            float angle = (seedHash % 180) * (MathF.PI / 180f); // 0..Pi radians
                            float cosA = MathF.Cos(angle);
                            float sinA = MathF.Sin(angle);

                            // 2. Rotate coordinates to align with strike
                            // Domain Warp: Bend the grain so it's not perfectly straight ("Cat Claw" fix)
                            float warpX = SimpleNoise.GetNoise(x * 0.015f, y * 0.015f) * 20f;
                            float warpY = SimpleNoise.GetNoise(x * 0.015f + 100, y * 0.015f + 100) * 20f;

                            float u = (x + warpX) * cosA - (y + warpY) * sinA;
                            float v = (x + warpX) * sinA + (y + warpY) * cosA;

                            // 3. Anisotropic Noise (Stretched along U - Strike)
                            // "Wood Grain" effect
                            float grain = SimpleNoise.GetFBM(u * 0.015f, v * 0.08f, 3, 0.5f, 2.0f);

                            // Per-Province Texture Variance: Some are grainy (Fold Belts), some blobby (Shields)
                            float textureType = (seedHash % 100) / 100f; // 0..1
                            float grainStrength = _settings.FossilGrainStrength * (0.3f + 0.7f * textureType);

                            // 4. Threshold & Mix
                            if (grain > -0.2f)
                            {
                                float falloff = 1f - (distSq / radiusSq);
                                falloff = falloff * falloff; // Smooth falloff

                                // Blend Grain vs Blob based on strength
                                // INLINE LERP: a + (b-a)*t
                                float shape = 1f + ((grain * grain) - 1f) * grainStrength;

                                float provinceHeight = _settings.FossilThickening * shape * 2f * falloff;
                                thickness[idx] += provinceHeight;
                                featureType[idx] = 3;
                            }
                        }
                    }
                }
            });

            // Removed the old linear Voronoi pass (ddx/ddy loop) entirely
            // This parallel loop handles the entire placement.
        }

        private void ApplyMantleDynamics(int w, int h, float[] thickness)
        {
            var random = new Random(99999);
            int numLows = _settings.MantleLowsMin + random.Next(_settings.MantleLowsMax - _settings.MantleLowsMin + 1);
            var mantleLows = new List<(int x, int y, float strength)>();
            for (int i = 0; i < numLows; i++)
            {
                mantleLows.Add((
                    random.Next(w),
                    random.Next((int)(h * 0.2), (int)(h * 0.8)),
                    _settings.MantleLowStrength * (0.5f + (float)random.NextDouble())
                ));
            }

            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    foreach (var low in mantleLows)
                    {
                        float dxWrap = Math.Abs(x - low.x);
                        if (dxWrap > w / 2) dxWrap = w - dxWrap;
                        float dy = y - low.y;
                        float dist = (float)Math.Sqrt(dxWrap * dxWrap + dy * dy);
                        if (dist < _settings.MantleLowRadius)
                        {
                            float effect = low.strength * (1f - dist / _settings.MantleLowRadius);
                            thickness[idx] -= effect;
                        }
                    }
                }
            });
        }

        private void ApplyHotspots(Dictionary<int, Plate> plates, DataGrid<int> plateGrid,
             int w, int h, float[] thickness, int[] featureType)
        {
            var random = new Random(77777);
            int numHotspots = _settings.HotspotsMin + random.Next(_settings.HotspotsMax - _settings.HotspotsMin + 1);

            for (int i = 0; i < numHotspots; i++)
            {
                int hx = random.Next(w);
                int hy = random.Next((int)(h * 0.15), (int)(h * 0.85));
                int idx0 = hy * w + hx;
                int pId = plateGrid.RawData[idx0];

                Vector2 velocity = Vector2.Zero;
                if (pId != 0 && plates.TryGetValue(pId, out var p))
                    velocity = p.Velocity;

                // Hotspot Track Logic: Trace BACKWARDS against plate velocity
                // MIX: Some are chains, some are single plumes
                bool isChain = random.NextDouble() < _settings.HotspotChainChance;
                int trackSteps = isChain ? (5 + random.Next(10)) : 1;

                float stepSize = 15f;
                Vector2 currentPos = new Vector2(hx, hy);

                // Effective velocity for trace
                Vector2 traceDir = -velocity;
                if (traceDir.Length() < 0.1f) traceDir = new Vector2((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f);
                traceDir = Vector2.Normalize(traceDir) * stepSize;

                // Track Wiggle Seed (Per-hotspot)
                float wiggleSeed = (float)random.NextDouble() * 100f;

                for (int step = 0; step < trackSteps; step++)
                {
                    // Wiggle: Add noise to path so it's not a ruler-straight line
                    // We stick noise perpendicular to traceDir? Or just generic offset?
                    // Generic offset is easier but might bunch up.
                    float wx = SimpleNoise.GetNoise(step * 0.2f, wiggleSeed) * _settings.HotspotWiggle * 10f;
                    float wy = SimpleNoise.GetNoise(step * 0.2f + 50f, wiggleSeed) * _settings.HotspotWiggle * 10f;

                    Vector2 wiggle = new Vector2(wx, wy);

                    // Update position
                    currentPos += traceDir + wiggle;

                    // Wrap coords
                    float cx = currentPos.X; while (cx < 0) cx += w; while (cx >= w) cx -= w;
                    float cy = currentPos.Y;
                    if (cy < 0 || cy >= h) break;

                    // Island Params
                    float age = step / (float)Math.Max(1, trackSteps); // 0 = New/Active, 1 = Old/Eroded
                    float rad = _settings.HotspotRadius * (0.8f + age * 0.5f); // Older is wider
                    float str = _settings.HotspotThickening * (1f - age * 0.8f); // Older is shorter

                    // Draw Island
                    int iCx = (int)cx;
                    int iCy = (int)cy;
                    int range = (int)rad + 2;

                    for (int dy = -range; dy <= range; dy++)
                    {
                        for (int dx = -range; dx <= range; dx++)
                        {
                            int nx = iCx + dx; while (nx < 0) nx += w; while (nx >= w) nx -= w;
                            int ny = iCy + dy; if (ny < 0 || ny >= h) continue;

                            float dist = MathF.Sqrt(dx * dx + dy * dy);
                            if (dist < rad)
                            {
                                int nIdx = ny * w + nx;
                                float shape = (1f - dist / rad);
                                shape = shape * shape * (3f - 2f * shape); // Smoothstep

                                float uplift = str * shape;

                                // Atoll Logic: If Old and Submerged/Oceanic
                                // We simulate coral ring by keeping the rim high but sinking the center
                                if (isChain && age > 0.6f && dist < rad * 0.8f && dist > rad * 0.4f)
                                {
                                    // Rim stays up (coral)
                                }
                                else if (isChain && age > 0.6f && dist <= rad * 0.4f)
                                {
                                    // Center sinks (lagoon)
                                    uplift *= 0.2f;
                                }

                                thickness[nIdx] += uplift;

                                // Only mark as feature if meaningful
                                if (uplift > 2f) featureType[nIdx] = 4;
                            }
                        }
                    }
                }
            }
        }

        private void DeriveElevationFromThickness(int w, int h, float[] thickness, float[] elevation)
        {
            Parallel.For(0, w * h, i =>
            {
                float thick = thickness[i];
                float relativeThickness = thick - _settings.MantleEquilibrium;
                elevation[i] = relativeThickness * _settings.BuoyancyFactor;
            });
        }

        private void ApplyOceanBathymetry(Dictionary<int, Plate> plates, DataGrid<int> plateGrid,
            int w, int h, int[] featureType, float[] elevation)
        {
            bool[] isRidge = new bool[w * h];
            for (int i = 0; i < w * h; i++) isRidge[i] = featureType[i] == 5;

            float[] ridgeDist = new float[w * h];
            Array.Fill(ridgeDist, float.MaxValue);
            for (int i = 0; i < w * h; i++) if (isRidge[i]) ridgeDist[i] = 0;

            int[] dx8 = { 1, -1, 0, 0, 1, -1, 1, -1 };
            int[] dy8 = { 0, 0, 1, -1, 1, 1, -1, -1 };
            float[] dd8 = { 1f, 1f, 1f, 1f, 1.414f, 1.414f, 1.414f, 1.414f };

            for (int pass = 0; pass < 100; pass++)
            {
                bool changed = false;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * w + x;
                        if (elevation[idx] >= 0) continue;

                        for (int d = 0; d < 8; d++)
                        {
                            int nx = x + dx8[d]; if (nx < 0) nx += w; if (nx >= w) nx -= w;
                            int ny = y + dy8[d]; if (ny < 0 || ny >= h) continue;
                            float newDist = ridgeDist[ny * w + nx] + dd8[d];
                            if (newDist < ridgeDist[idx])
                            {
                                ridgeDist[idx] = newDist;
                                changed = true;
                            }
                        }
                    }
                }
                if (!changed) break;
            }

            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    if (elevation[idx] >= 0) continue;
                    float dist = ridgeDist[idx];
                    if (dist < float.MaxValue && dist > 0)
                    {
                        float depthFromAge = -0.02f * (float)Math.Sqrt(dist);
                        elevation[idx] = Math.Min(elevation[idx], -0.5f + depthFromAge);
                    }
                }
            });
        }

        private void ApplyShelvesAndSlopes(int w, int h, float[] elevation)
        {
            float[] coastDist = new float[w * h];
            Array.Fill(coastDist, -1f);

            int[] dx8 = { 1, -1, 0, 0, 1, -1, 1, -1 };
            int[] dy8 = { 0, 0, 1, -1, 1, 1, -1, -1 };
            float[] dd8 = { 1f, 1f, 1f, 1f, 1.414f, 1.414f, 1.414f, 1.414f };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    if (elevation[idx] >= 0) continue;

                    coastDist[idx] = float.MaxValue;
                    for (int d = 0; d < 8; d++)
                    {
                        int nx = x + dx8[d]; if (nx < 0) nx += w; if (nx >= w) nx -= w;
                        int ny = y + dy8[d]; if (ny < 0 || ny >= h) continue;
                        if (elevation[ny * w + nx] >= 0)
                        {
                            coastDist[idx] = 0;
                            break;
                        }
                    }
                }
            }

            for (int pass = 0; pass < 60; pass++)
            {
                bool changed = false;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * w + x;
                        if (coastDist[idx] < 0) continue;
                        for (int d = 0; d < 8; d++)
                        {
                            int nx = x + dx8[d]; if (nx < 0) nx += w; if (nx >= w) nx -= w;
                            int ny = y + dy8[d]; if (ny < 0 || ny >= h) continue;
                            if (coastDist[ny * w + nx] >= 0)
                            {
                                float newDist = coastDist[ny * w + nx] + dd8[d];
                                if (newDist < coastDist[idx])
                                {
                                    coastDist[idx] = newDist;
                                    changed = true;
                                }
                            }
                        }
                    }
                }
                if (!changed) break;
            }

            float shelfWidth = 20f;
            float slopeWidth = 30f;

            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    if (coastDist[idx] < 0) continue;
                    float dist = coastDist[idx];

                    if (dist < shelfWidth)
                    {
                        float shelfDepth = -0.1f - 0.1f * (dist / shelfWidth);
                        elevation[idx] = Math.Max(elevation[idx], shelfDepth);
                    }
                    else if (dist < shelfWidth + slopeWidth)
                    {
                        float slopeFactor = (dist - shelfWidth) / slopeWidth;
                        float slopeDepth = -0.2f - 0.8f * slopeFactor;
                        elevation[idx] = Math.Max(elevation[idx], slopeDepth);
                    }
                }
            });
        }

        private void ApplyErosion(int w, int h, float[] elevation)
        {
            for (int iter = 0; iter < 5; iter++)
            {
                float[] source = (float[])elevation.Clone();
                Parallel.For(0, h, y =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * w + x;
                        float current = source[idx];
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
                            elevation[idx] -= diff * 0.15f * _settings.ErosionStrength;
                    }
                });
            }
        }

        private void ApplyGlacialErosion(int w, int h, float[] elevation)
        {
            // Glacial Erosion: Carves U-shaped valleys in high latitudes/elevations
            // Uses a "Min-Filter" (Erosion morphology) approach

            float[] tempElev = (float[])elevation.Clone();
            int kernel = 2; // 5x5 area

            Parallel.For(0, h, y =>
            {
                float latFactor = Math.Abs((y / (float)h) * 2f - 1f); // 0 at equator, 1 at poles

                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    float e = tempElev[idx];

                    // Ice Probability: High Lat (> 60 deg approx) or High Altitude
                    // 0.6 is roughly 54 degrees
                    float iceProb = (latFactor - 0.5f) * 2f + (e * 0.5f);

                    if (iceProb > 0.1f)
                    {
                        // Glacial Carving: Find local minimum in window and blend towards it (widening valleys)
                        float minNeighbor = e;
                        for (int dy = -kernel; dy <= kernel; dy++)
                        {
                            for (int dx = -kernel; dx <= kernel; dx++)
                            {
                                int nx = x + dx; while (nx < 0) nx += w; while (nx >= w) nx -= w;
                                int ny = y + dy; if (ny < 0 || ny >= h) continue;
                                minNeighbor = Math.Min(minNeighbor, tempElev[ny * w + nx]);
                            }
                        }

                        // Apply carving
                        // Strength increases with ice probability
                        float strength = Math.Clamp(iceProb, 0f, 1f) * 0.8f;

                        // Fjord Logic: Allow digging deep near coast (e < 0.2)
                        // If we are near sea level in high lat, carve DEEP (U-valley below sea level)
                        if (latFactor > 0.7f && e < 0.2f && e > -0.1f)
                        {
                            strength *= 1.5f; // Super carve for fjords
                        }

                        // Blend current elev towards min neighbor (U-shape)
                        elevation[idx] = e + (minNeighbor - e) * strength * 0.5f;
                    }
                }
            });
        }

        private void ApplySedimentation(int w, int h, float[] elevation)
        {
            // Sedimentation: Fills lowlands to create flat plains (Siberia/Serbia)
            // Simplified "Diffusion" logic for low-slope areas

            float[] tempElev = (float[])elevation.Clone();

            // 1. Identify "Lowlands" (Low elevation, low slope usually)
            // 2. Apply Deposition (Raising) + Smoothing

            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    float e = tempElev[idx];

                    if (e < 0.5f && e > 0f) // Land, but not high peaks
                    {
                        // Calculate local slope
                        float maxDiff = 0f;
                        int[] dx4 = { 1, -1, 0, 0 };
                        int[] dy4 = { 0, 0, 1, -1 };
                        for (int i = 0; i < 4; i++)
                        {
                            int nx = x + dx4[i]; while (nx < 0) nx += w; while (nx >= w) nx -= w;
                            int ny = y + dy4[i]; if (ny < 0 || ny >= h) continue;
                            float diff = Math.Abs(e - tempElev[ny * w + nx]);
                            if (diff > maxDiff) maxDiff = diff;
                        }

                        // If slope is low, we are in a plain/basin -> Deposition
                        if (maxDiff < 0.05f)
                        {
                            // Deposit
                            elevation[idx] += 0.005f;

                            // Strong Smooth to flatten
                            float avg = e;
                            int count = 1;
                            for (int i = 0; i < 4; i++)
                            {
                                int nx = x + dx4[i]; while (nx < 0) nx += w; while (nx >= w) nx -= w;
                                int ny = y + dy4[i]; if (ny < 0 || ny >= h) continue;
                                avg += tempElev[ny * w + nx];
                                count++;
                            }
                            elevation[idx] = avg / count;
                        }
                    }
                    // Continental Shelf Building
                    // If just below sea level and flat-ish, raise to form shelf
                    else if (e > -0.2f && e <= 0f)
                    {
                        elevation[idx] += 0.01f; // Sediment buildup from rivers
                    }
                }
            });
        }

        private void ApplyRuggedPeaks(int w, int h, float[] elevation)
        {
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    float e = elevation[idx];
                    if (e > 0.15f)
                    {
                        float n = SimpleNoise.GetFBM(x * 0.06f, y * 0.06f, 4, 0.5f, 2.0f);
                        float ridged = 1f - MathF.Abs(n);
                        float heightFactor = Math.Min((e - 0.15f) / 0.5f, 1f);
                        elevation[idx] += heightFactor * 0.1f * ridged;
                    }
                }
            });
        }

        private void WriteToMap(int w, int h, float[] thickness, float[] elevation, int[] featureType)
        {
            Parallel.For(0, w * h, i =>
            {
                _map.Elevation.RawData[i] = elevation[i];
                _map.CrustThickness.RawData[i] = thickness[i];
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

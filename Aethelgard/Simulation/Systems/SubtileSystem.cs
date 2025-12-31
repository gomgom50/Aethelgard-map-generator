using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Aethelgard.Simulation.Core;

namespace Aethelgard.Simulation.Systems
{
    /// <summary>
    /// Manages the "Subtile" layer of the world.
    /// Implements the Gleba architecture:
    /// 1. "Micro-Cell" topology (separate from main tiles).
    /// 2. 3D Noise Warping for organic borders (avoiding polar distortion).
    /// 3. Precomputed Blending Weights (Optimization) to make mapmode switches O(1).
    /// </summary>
    public class SubtileSystem
    {
        private readonly WorldMap _map;
        private readonly FractalNoise _warpNoise;
        private readonly int _seed;

        // Explicit "Micro-Cell" grid for subtiles
        private HexSphereTopology _microTopology = null!;
        private int _resolutionMultiplier = 4;

        // CACHED BLEND DATA (The Optimization)
        // Stores pre-calculated weights and neighbor IDs for O(1) rendering
        private struct SubtileBlendData
        {
            public int Id1, Id2, Id3;
            public float W1, W2, W3;
            public float Noise;
            public int WinnerId;
        }

        private SubtileBlendData[] _blendCache = null!;

        // PRECOMPUTED OWNERSHIP (Legacy/Helper access)
        // Kept for simple ownership queries if needed outside rendering
        // But implicitly available via _blendCache[id].WinnerId

        // Configuration matching Gleba's "organic" look
        private float _noiseScale = 100.0f;        // Scale of the 3D Noise Field (Higher = more wiggly)
        private float _warpStrength = 0.002f;     // Magnitude of the 3D displacement
        private float _detailNoiseStrength = 100.0f; // Scale of the added height detail

        public float NoiseScale => _noiseScale;
        public float WarpStrength => _warpStrength;
        public float DetailNoiseStrength => _detailNoiseStrength;

        public int ResolutionMultiplier
        {
            get => _resolutionMultiplier;
            set
            {
                int clamped = Math.Clamp(value, 2, 8);
                if (clamped != _resolutionMultiplier)
                {
                    _resolutionMultiplier = clamped;
                    RebuildMicroTopology();
                }
            }
        }

        public SubtileSystem(WorldMap map, int seed)
        {
            _map = map;
            _seed = seed;

            // 3 Octaves is enough for the structural warp
            _warpNoise = new FractalNoise(seed + 1234, octaves: 3, persistence: 0.5f, lacunarity: 2.0f, scale: 1.0f);

            RebuildMicroTopology();
        }

        private void RebuildMicroTopology()
        {
            int subResolution = Math.Max(_map.Topology.Resolution * _resolutionMultiplier, 80);
            _microTopology = new HexSphereTopology(subResolution);
            RebuildSubtileData();
        }

        /// <summary>
        /// Precomputes blending weights, neighbors, and noise for every subtile.
        /// This is the "Heavy Lifting" done once upon init or config change.
        /// </summary>
        private void RebuildSubtileData()
        {
            int subtileCount = _microTopology.TileCount;
            _blendCache = new SubtileBlendData[subtileCount];

            Parallel.For(0, subtileCount, subtileId =>
            {
                // 1. Get Base Position of the Subtile
                Vector3 pos = _microTopology.GetTilePosition(subtileId);

                // 2. Warp Position (3D Noise) - This creates the organic shapes
                Vector3 warpedPos = WarpPoint(pos);

                // 3. Find Broad-Phase Winner (using Lat/Lon lookup on warped point)
                // Determine geometric lookups in Degrees (Topology expects Degrees)
                var (latRad, lonRad) = CartesianToLatLonRad(warpedPos);
                float latDeg = (float)(latRad * 180.0 / Math.PI);
                float lonDeg = (float)(lonRad * 180.0 / Math.PI);

                int guessId = _map.Topology.GetTileAtLatLon(latDeg, lonDeg);

                // 4. Find True 3 Closest Centroids (Refine guess with 2-ring neighbors)
                // Allocation-free search variables
                int id1 = -1, id2 = -1, id3 = -1;
                float d1 = float.MaxValue, d2 = float.MaxValue, d3 = float.MaxValue;

                // Local function to check and insert a candidate
                void CheckCandidate(int cid)
                {
                    if (cid == -1) return;
                    if (cid == id1 || cid == id2 || cid == id3) return; // Dedup

                    // Distance Squared for speed comparison
                    float dSq = Vector3.DistanceSquared(warpedPos, _map.Topology.GetTilePosition(cid));

                    if (dSq < d1)
                    {
                        d3 = d2; id3 = id2;
                        d2 = d1; id2 = id1;
                        d1 = dSq; id1 = cid;
                    }
                    else if (dSq < d2)
                    {
                        d3 = d2; id3 = id2;
                        d2 = dSq; id2 = cid;
                    }
                    else if (dSq < d3)
                    {
                        d3 = dSq; id3 = cid;
                    }
                }

                // Check the guess
                CheckCandidate(guessId);

                // Check 1-Ring Neighbors
                var neighbors1 = _map.Topology.GetNeighbors(guessId);
                for (int i = 0; i < neighbors1.Length; i++)
                {
                    int n1 = neighbors1[i];
                    CheckCandidate(n1);

                    // Check 2-Ring Neighbors (crucial for warped space accuracy)
                    var neighbors2 = _map.Topology.GetNeighbors(n1);
                    for (int j = 0; j < neighbors2.Length; j++)
                    {
                        CheckCandidate(neighbors2[j]);
                    }
                }

                // Fallback (rare, if isolated)
                if (id1 == -1) id1 = guessId;
                if (id2 == -1) { id2 = id1; d2 = d1 + 1f; }
                if (id3 == -1) { id3 = id2; d3 = d2 + 1f; }

                // 5. Calculate Weights (Inverse Euclidean Distance)
                // We use Sqrt(dSq) because linear distance weighting is smoother/correct for this
                float dist1 = MathF.Sqrt(d1);
                float dist2 = MathF.Sqrt(d2);
                float dist3 = MathF.Sqrt(d3);

                // Inverse Distance Weighting
                float w1 = 1f / Math.Max(dist1, 1e-6f);
                float w2 = 1f / Math.Max(dist2, 1e-6f);
                float w3 = 1f / Math.Max(dist3, 1e-6f);
                float totalW = w1 + w2 + w3;

                // 6. Calculate Detail Noise (3D)
                // Higher freq for texture detail
                float detail = _warpNoise.GetNoise(
                    warpedPos.X * _noiseScale * 4f + 100f,
                    warpedPos.Y * _noiseScale * 4f + 100f,
                    warpedPos.Z * _noiseScale * 4f + 100f
                );

                // Store in Cache
                _blendCache[subtileId] = new SubtileBlendData
                {
                    Id1 = id1,
                    Id2 = id2,
                    Id3 = id3,
                    W1 = w1 / totalW,
                    W2 = w2 / totalW,
                    W3 = w3 / totalW, // Normalize
                    WinnerId = id1,
                    Noise = detail * _detailNoiseStrength
                };
            });
        }

        /// <summary>
        /// Applies 3D Fractal Noise to warp a point slightly off the sphere surface 
        /// (or along it), then renormalizes.
        /// Exposed for MapRenderer to warp the visual grid.
        /// </summary>
        public Vector3 WarpPoint(Vector3 pos)
        {
            // Sample 3D noise for x, y, z displacement
            // We use different offsets to decorrelate dimensions to avoid diagonal artifacts
            float dx = _warpNoise.GetNoise(pos.X * _noiseScale, pos.Y * _noiseScale, pos.Z * _noiseScale);
            float dy = _warpNoise.GetNoise(pos.X * _noiseScale + 12.5f, pos.Y * _noiseScale + 12.5f, pos.Z * _noiseScale + 12.5f);
            float dz = _warpNoise.GetNoise(pos.X * _noiseScale + 25.1f, pos.Y * _noiseScale + 25.1f, pos.Z * _noiseScale + 25.1f);

            // Apply warp
            Vector3 offset = new Vector3(dx, dy, dz) * _warpStrength;
            return Vector3.Normalize(pos + offset);
        }

        public void UpdateConfig(float scale, float strength, float detailStrength)
        {
            bool needsRebuild = Math.Abs(_noiseScale - scale) > 0.0001f ||
                                Math.Abs(_warpStrength - strength) > 0.0001f ||
                                Math.Abs(_detailNoiseStrength - detailStrength) > 0.0001f;

            _noiseScale = scale;
            _warpStrength = strength;
            _detailNoiseStrength = detailStrength;

            if (needsRebuild) RebuildSubtileData();
        }

        public HexSphereTopology MicroTopology => _microTopology;

        // O(1) Lookup using Cache
        public int GetSubtileOwner(Vector3 spherePosition)
        {
            // For rendering we typically iterate by ID. 
            // If querying by position, we map Pos -> ID -> Owner
            int id = GetSubtileId(spherePosition);
            return GetParentTile(id);
        }

        public int GetParentTile(int subtileId)
        {
            if (subtileId < 0 || subtileId >= _blendCache.Length) return -1;
            return _blendCache[subtileId].WinnerId;
        }

        // Fast O(1) Elevation Lookup using Precomputed Weights
        public float GetSubtileElevation(int subtileId)
        {
            if (subtileId < 0 || subtileId >= _blendCache.Length) return 0f;

            ref SubtileBlendData data = ref _blendCache[subtileId];

            float h1 = _map.Tiles[data.Id1].Elevation;
            float h2 = _map.Tiles[data.Id2].Elevation;
            float h3 = _map.Tiles[data.Id3].Elevation;

            return (h1 * data.W1 + h2 * data.W2 + h3 * data.W3) + data.Noise;
        }

        public int GetSubtileId(Vector3 spherePosition)
        {
            var (latRad, lonRad) = CartesianToLatLonRad(Vector3.Normalize(spherePosition));
            // Topology expects degrees
            return _microTopology.GetTileAtLatLon((float)(latRad * 180.0 / Math.PI), (float)(lonRad * 180.0 / Math.PI));
        }

        public Vector3 GetSubtileCenter(int subtileId)
        {
            if (subtileId < 0 || subtileId >= _microTopology.TileCount) return Vector3.UnitY;
            return _microTopology.GetTilePosition(subtileId);
        }

        public int SubtileCount => _microTopology.TileCount;

        public Vector3 GetNoiseVector(Vector3 spherePosition)
        {
            // Debug check of the warp field
            float dx = _warpNoise.GetNoise(spherePosition.X * _noiseScale, spherePosition.Y * _noiseScale, spherePosition.Z * _noiseScale);
            float dy = _warpNoise.GetNoise(spherePosition.X * _noiseScale + 12.5f, spherePosition.Y * _noiseScale + 12.5f, spherePosition.Z * _noiseScale + 12.5f);
            float dz = _warpNoise.GetNoise(spherePosition.X * _noiseScale + 25.1f, spherePosition.Y * _noiseScale + 25.1f, spherePosition.Z * _noiseScale + 25.1f);
            return new Vector3(dx, dy, dz);
        }

        public float GetBlendedElevation(Vector3 spherePosition)
        {
            // Legacy / Point-query access
            // Maps to caching system for consistency
            int id = GetSubtileId(spherePosition);
            return GetSubtileElevation(id);
        }

        /// <summary>
        /// Converts Cartesian unit sphere position to Lat/Lon in Radians.
        /// </summary>
        private static (double latRad, double lonRad) CartesianToLatLonRad(Vector3 pos)
        {
            // Vector3.Normalize is assumed to be done by caller or inputs are unit vectors
            double lat = Math.Asin(pos.Y);
            double lon = Math.Atan2(pos.Z, pos.X);
            return (lat, lon);
        }
    }
}

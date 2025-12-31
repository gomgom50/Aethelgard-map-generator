using System;
using System.Numerics;
using System.Threading.Tasks;
using Aethelgard.Simulation.Core;

namespace Aethelgard.Simulation.Systems
{
    /// <summary>
    /// Manages the "Subtile" layer of the world.
    /// In Gleba, subtiles are spatially perturbed sampling points that define organic borders.
    /// This system PRECOMPUTES which parent tile owns each subtile (with noise warp),
    /// so rendering is just a fast lookup, not per-pixel noise sampling.
    /// </summary>
    public class SubtileSystem
    {
        private readonly WorldMap _map;
        private readonly FractalNoise _warpNoise;
        private readonly int _seed;

        // Explicit "Micro-Cell" grid for subtiles (Gleba architecture)
        private HexSphereTopology _microTopology = null!;
        private int _resolutionMultiplier = 4;

        // PRECOMPUTED OWNERSHIP: subtileId → parentTileId
        // This is the key optimization - computed once, not per-pixel
        private int[] _subtileToParent = null!;

        // Configuration matching Gleba's "organic" look
        private float _noiseScale = 0.05f;
        private float _warpStrength = 0.15f;

        public float NoiseScale => _noiseScale;
        public float WarpStrength => _warpStrength;

        /// <summary>
        /// Subtile resolution multiplier (2-8). Higher = finer subtile grid.
        /// Changing this regenerates everything (expensive).
        /// </summary>
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

            // 4 Octaves, high lacunarity for crinkly borders
            _warpNoise = new FractalNoise(seed + 1234, octaves: 4, persistence: 0.5f, lacunarity: 2.2f, scale: 1.0f);

            // Initialize Subtile Topology and ownership
            RebuildMicroTopology();
        }

        private void RebuildMicroTopology()
        {
            int subResolution = Math.Max(_map.Topology.Resolution * _resolutionMultiplier, 80);
            _microTopology = new HexSphereTopology(subResolution);
            RebuildOwnership();
        }

        /// <summary>
        /// Precomputes which parent tile owns each subtile.
        /// This applies the noise warp ONCE per subtile, not per pixel.
        /// </summary>
        private void RebuildOwnership()
        {
            int subtileCount = _microTopology.TileCount;
            _subtileToParent = new int[subtileCount];

            // Parallel computation for speed
            Parallel.For(0, subtileCount, subtileId =>
            {
                // Get this subtile's position
                Vector3 pos = _microTopology.GetTilePosition(subtileId);
                var (lat, lon) = UseCartesianToLatLon(pos);

                // Apply noise warp (same logic as before, but ONCE per subtile)
                float nx = _warpNoise.GetNoise(lon * _noiseScale + 10f, lat * _noiseScale, 0);
                float ny = _warpNoise.GetNoise(lon * _noiseScale, lat * _noiseScale + 10f, 0);

                float angularWarp = _warpStrength * 10f;
                float newLat = Math.Clamp(lat + ny * angularWarp, -89.9f, 89.9f);
                float newLon = lon + nx * angularWarp;

                // Find parent tile at warped position
                _subtileToParent[subtileId] = _map.Topology.GetTileAtLatLon(newLat, newLon);
            });
        }

        /// <summary>
        /// Update noise parameters. Triggers ownership rebuild.
        /// </summary>
        public void UpdateConfig(float scale, float strength)
        {
            bool needsRebuild = Math.Abs(_noiseScale - scale) > 0.0001f ||
                                Math.Abs(_warpStrength - strength) > 0.0001f;
            _noiseScale = scale;
            _warpStrength = strength;
            if (needsRebuild) RebuildOwnership();
        }

        /// <summary>
        /// Access to the underlying subtile topology for rendering subtile borders.
        /// </summary>
        public HexSphereTopology MicroTopology => _microTopology;

        /// <summary>
        /// Gets the distinct Subtile ID (Micro-Cell ID) for a given position.
        /// Fast - just a topology lookup.
        /// </summary>
        public int GetSubtileId(Vector3 spherePosition)
        {
            var (lat, lon) = UseCartesianToLatLon(Vector3.Normalize(spherePosition));
            return _microTopology.GetTileAtLatLon(lat, lon);
        }

        /// <summary>
        /// Gets the parent tile that owns the subtile at this position.
        /// FAST - uses precomputed lookup, no noise sampling.
        /// </summary>
        public int GetSubtileOwner(Vector3 spherePosition)
        {
            int subtileId = GetSubtileId(spherePosition);
            return _subtileToParent[subtileId];
        }

        /// <summary>
        /// Gets the parent tile for a known subtile ID.
        /// Direct array lookup - maximum performance.
        /// </summary>
        public int GetParentTile(int subtileId)
        {
            return _subtileToParent[subtileId];
        }

        /// <summary>
        /// Gets the raw noise perturbation values at a position.
        /// Samples in angular space for debug visualization.
        /// </summary>
        public Vector3 GetNoiseVector(Vector3 spherePosition)
        {
            var (lat, lon) = UseCartesianToLatLon(spherePosition);

            float nx = _warpNoise.GetNoise(lon * _noiseScale + 10f, lat * _noiseScale, 0);
            float ny = _warpNoise.GetNoise(lon * _noiseScale, lat * _noiseScale + 10f, 0);
            float nz = _warpNoise.GetNoise(lon * _noiseScale, lat * _noiseScale, 10f);

            return new Vector3(nx, ny, nz);
        }

        /// <summary>
        /// Gets the blended elevation for a point on the sphere.
        /// Uses precomputed ownership for the base tile, then blends.
        /// </summary>
        public float GetBlendedElevation(Vector3 spherePosition)
        {
            // Get the owning parent tile (FAST via precomputed lookup)
            int bestId = GetSubtileOwner(spherePosition);

            // Get warped position for distance calculations
            var (lat, lon) = UseCartesianToLatLon(spherePosition);
            float nx = _warpNoise.GetNoise(lon * _noiseScale + 10f, lat * _noiseScale, 0);
            float ny = _warpNoise.GetNoise(lon * _noiseScale, lat * _noiseScale + 10f, 0);
            float angularWarp = _warpStrength * 10f;
            float newLat = Math.Clamp(lat + ny * angularWarp, -89.9f, 89.9f);
            float newLon = lon + nx * angularWarp;
            Vector3 perturbedPos = LatLonToCartesian(newLat, newLon);

            // Find 2nd and 3rd closest from the best tile's neighbors
            var neighbors = _map.Topology.GetNeighbors(bestId);

            int secondId = -1;
            int thirdId = -1;
            float dist1 = Vector3.DistanceSquared(perturbedPos, _map.Topology.GetTilePosition(bestId));
            float dist2 = float.MaxValue;
            float dist3 = float.MaxValue;

            foreach (int nId in neighbors)
            {
                float d = Vector3.DistanceSquared(perturbedPos, _map.Topology.GetTilePosition(nId));
                if (d < dist2)
                {
                    dist3 = dist2; thirdId = secondId;
                    dist2 = d; secondId = nId;
                }
                else if (d < dist3)
                {
                    dist3 = d; thirdId = nId;
                }
            }

            // Fallback if not enough neighbors
            if (secondId == -1) secondId = bestId;
            if (thirdId == -1) thirdId = secondId;

            // Inverse Distance Weighting (IDW)
            float w1 = 1f / Math.Max(dist1, 1e-6f);
            float w2 = 1f / Math.Max(dist2, 1e-6f);
            float w3 = 1f / Math.Max(dist3, 1e-6f);
            float totalW = w1 + w2 + w3;

            w1 /= totalW;
            w2 /= totalW;
            w3 /= totalW;

            // Blend Fields
            float h1 = _map.Tiles[bestId].Elevation;
            float h2 = _map.Tiles[secondId].Elevation;
            float h3 = _map.Tiles[thirdId].Elevation;

            float blendedHeight = h1 * w1 + h2 * w2 + h3 * w3;

            // Add Subtile Detail Noise
            float detail = _warpNoise.GetNoise(newLon * _noiseScale * 2f, newLat * _noiseScale * 2f, 0);

            return blendedHeight + detail * 50f;
        }

        private static (float lat, float lon) UseCartesianToLatLon(Vector3 pos)
        {
            float lat = MathF.Asin(pos.Y);
            float lon = MathF.Atan2(pos.Z, pos.X);
            return (lat * 180f / MathF.PI, lon * 180f / MathF.PI);
        }

        private static Vector3 LatLonToCartesian(float lat, float lon)
        {
            float latRad = lat * MathF.PI / 180f;
            float lonRad = lon * MathF.PI / 180f;
            return new Vector3(
                MathF.Cos(latRad) * MathF.Cos(lonRad),
                MathF.Sin(latRad),
                MathF.Cos(latRad) * MathF.Sin(lonRad)
            );
        }
    }
}

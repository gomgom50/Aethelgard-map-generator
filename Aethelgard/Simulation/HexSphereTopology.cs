using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace Aethelgard.Simulation
{
    /// <summary>
    /// Pre-computed Goldberg polyhedron topology for hex-based neighbor lookups.
    /// Based on an icosahedron (20 faces, 12 vertices, 30 edges) subdivided into hexagons.
    /// Each tile has a (face, u, v) coordinate within one of the 20 triangular faces.
    /// 12 tiles are pentagons (at icosahedron vertices), all others are hexagons.
    /// </summary>
    public class HexSphereTopology
    {
        // Icosahedron geometry constants
        private static readonly float Phi = (1f + MathF.Sqrt(5f)) / 2f; // Golden ratio ≈ 1.618

        // The 12 vertices of a unit icosahedron (normalized)
        // Standard icosahedron vertices at (0, ±1, ±φ), (±1, ±φ, 0), (±φ, 0, ±1)
        private static readonly Vector3[] IcoVertices = new Vector3[12];

        // The 20 triangular faces, each defined by 3 vertex indices
        // Correct icosahedron face topology (vertices must be consistently wound)
        private static readonly int[,] IcoFaces = new int[20, 3]
        {
            // 5 faces around vertex 0 (top)
            {0, 11, 5}, {0, 5, 1}, {0, 1, 7}, {0, 7, 10}, {0, 10, 11},
            // 5 adjacent faces
            {1, 5, 9}, {5, 11, 4}, {11, 10, 2}, {10, 7, 6}, {7, 1, 8},
            // 5 faces around vertex 3 (bottom)
            {3, 9, 4}, {3, 4, 2}, {3, 2, 6}, {3, 6, 8}, {3, 8, 9},
            // 5 adjacent faces
            {4, 9, 5}, {2, 4, 11}, {6, 2, 10}, {8, 6, 7}, {9, 8, 1}
        };

        private readonly int _resolution;
        private readonly HexTile[] _tiles;
        private readonly int[][] _neighbors;

        // Spatial grid for fast lat/lon -> tile lookup
        private const int SpatialGridSize = 180; // 2° per cell
        private readonly List<int>[,] _spatialGrid;

        /// <summary>Total number of tiles: 10*n² + 2 (Goldberg formula)</summary>
        public int TileCount { get; }

        /// <summary>Grid resolution (subdivisions per icosahedron edge)</summary>
        public int Resolution => _resolution;

        static HexSphereTopology()
        {
            // Face-Centered Pole Orientation
            // We construct standard Golden Rectangle vertices, then rotate the mesh so that
            // the CENTROID of Face 0 (v0, v11, v5) is aligned to the North Pole (0,1,0).
            // This ensures a single face spans the pole, matching the user's observation of Gleba.

            float phi = (1f + MathF.Sqrt(5f)) / 2f;

            var standardVerts = new Vector3[12];
            standardVerts[0] = Vector3.Normalize(new Vector3(-1, phi, 0));
            standardVerts[1] = Vector3.Normalize(new Vector3(1, phi, 0));
            standardVerts[2] = Vector3.Normalize(new Vector3(-1, -phi, 0));
            standardVerts[3] = Vector3.Normalize(new Vector3(1, -phi, 0));
            standardVerts[4] = Vector3.Normalize(new Vector3(0, -1, phi));
            standardVerts[5] = Vector3.Normalize(new Vector3(0, 1, phi));
            standardVerts[6] = Vector3.Normalize(new Vector3(0, -1, -phi));
            standardVerts[7] = Vector3.Normalize(new Vector3(0, 1, -phi));
            standardVerts[8] = Vector3.Normalize(new Vector3(phi, 0, -1));
            standardVerts[9] = Vector3.Normalize(new Vector3(phi, 0, 1));
            standardVerts[10] = Vector3.Normalize(new Vector3(-phi, 0, -1));
            standardVerts[11] = Vector3.Normalize(new Vector3(-phi, 0, 1));

            // Calculate Centroid of Face 0 (defined by indices 0, 11, 5)
            // Indices from IcoFaces: {0, 11, 5} -> Vertices 0, 11, 5 in standard array
            Vector3 v0 = standardVerts[0];
            Vector3 v11 = standardVerts[11];
            Vector3 v5 = standardVerts[5];
            Vector3 faceCentroid = Vector3.Normalize(v0 + v11 + v5);

            // Rotate Centroid to Up (0,1,0)
            Vector3 from = faceCentroid;
            Vector3 to = Vector3.UnitY;
            Vector3 axis = Vector3.Normalize(Vector3.Cross(from, to));
            float angle = MathF.Acos(Vector3.Dot(from, to));
            Quaternion rot = Quaternion.CreateFromAxisAngle(axis, angle);

            for (int i = 0; i < 12; i++)
            {
                IcoVertices[i] = Vector3.Transform(standardVerts[i], rot);
            }
        }

        /// <summary>
        /// Constructs a hex sphere topology with the given resolution.
        /// </summary>
        /// <param name="resolution">Subdivisions per icosahedron edge (1-128 typical)</param>
        public HexSphereTopology(int resolution)
        {
            if (resolution < 1)
                throw new ArgumentException("Resolution must be at least 1", nameof(resolution));

            _resolution = resolution;
            TileCount = 10 * resolution * resolution + 2;

            _tiles = new HexTile[TileCount];
            _neighbors = new int[TileCount][];
            _spatialGrid = new List<int>[SpatialGridSize, SpatialGridSize];

            // Initialize spatial grid
            for (int i = 0; i < SpatialGridSize; i++)
                for (int j = 0; j < SpatialGridSize; j++)
                    _spatialGrid[i, j] = new List<int>();

            BuildTopology();

            // Relax the grid to make tiles more uniform (Voronoi relaxation)
            // This removes the "face shape" influence by distributing tiles evenly.
            RelaxTopology(10);

            BuildSpatialGrid();
            BuildNeighbors(); // Initial neighbor find

            // Slerp topology is naturally uniform.
        }

        /// <summary>
        /// Applies Lloyd's relaxation to the grid to uniformize tile sizes and shapes.
        /// Iteratively moves each tile center to the centroid of its neighbors.
        /// </summary>
        private void RelaxTopology(int iterations)
        {
            // We need neighbor data for relaxation, but we haven't built the full neighbor list yet.
            // Build temporary neighbor associations.
            BuildSpatialGrid();
            BuildNeighbors();

            for (int iter = 0; iter < iterations; iter++)
            {
                var newPositions = new Vector3[TileCount];

                Parallel.For(0, TileCount, i =>
                {
                    if (_tiles[i].Position == Vector3.Zero) return;

                    var neighbors = _neighbors[i];
                    if (neighbors == null || neighbors.Length == 0)
                    {
                        newPositions[i] = _tiles[i].Position;
                        return;
                    }

                    Vector3 centroid = Vector3.Zero;
                    foreach (int nId in neighbors)
                    {
                        centroid += _tiles[nId].Position;
                    }

                    // Simple centroid of neighbors
                    newPositions[i] = Vector3.Normalize(centroid);
                });

                // Apply new positions
                for (int i = 0; i < TileCount; i++)
                {
                    if (_tiles[i].Position != Vector3.Zero)
                    {
                        _tiles[i].Position = newPositions[i];
                        // Re-calculate Lat/Lon
                        var (lat, lon) = CartesianToLatLon(_tiles[i].Position);
                        _tiles[i].Lat = lat;
                        _tiles[i].Lon = lon;
                    }
                }
            }
            // Rebuild spatial structures after movement
            // _spatialGrid is stale now, but we rebuild it immediately after return.
        }

        /// <summary>Returns the neighbor tile IDs for a given tile.</summary>
        public ReadOnlySpan<int> GetNeighbors(int tileId)
        {
            if (tileId < 0 || tileId >= TileCount)
                throw new ArgumentOutOfRangeException(nameof(tileId));
            return _neighbors[tileId];
        }

        /// <summary>Get the number of neighbors (5 for pentagons, 6 for hexagons)</summary>
        public int GetNeighborCount(int tileId)
        {
            return _neighbors[tileId]?.Length ?? 0;
        }

        /// <summary>Returns the center lat/lon of a tile in degrees.</summary>
        public (float lat, float lon) GetTileCenter(int tileId)
        {
            if (tileId < 0 || tileId >= TileCount)
                throw new ArgumentOutOfRangeException(nameof(tileId));
            return (_tiles[tileId].Lat, _tiles[tileId].Lon);
        }

        /// <summary>Returns true if this tile is a pentagon (12 total at icosahedron vertices).</summary>
        public bool IsPentagon(int tileId)
        {
            if (tileId < 0 || tileId >= TileCount)
                throw new ArgumentOutOfRangeException(nameof(tileId));
            return _tiles[tileId].IsPentagon;
        }

        /// <summary>Returns the icosahedron face (0-19) this tile belongs to.</summary>
        public int GetTileFace(int tileId)
        {
            if (tileId < 0 || tileId >= TileCount)
                throw new ArgumentOutOfRangeException(nameof(tileId));
            return _tiles[tileId].Face;
        }

        /// <summary>
        /// Returns the vertices of a tile's polygon in lat/lon coordinates.
        /// Vertices are computed as midpoints between this tile and each neighbor.
        /// </summary>
        public (float lat, float lon)[] GetTileVertices(int tileId)
        {
            if (tileId < 0 || tileId >= TileCount)
                throw new ArgumentOutOfRangeException(nameof(tileId));

            var neighbors = _neighbors[tileId];
            if (neighbors == null || neighbors.Length == 0)
                return Array.Empty<(float, float)>();

            var tilePos = _tiles[tileId].Position;

            // Collect vertex positions in 3D
            var vertices3D = new Vector3[neighbors.Length];
            for (int i = 0; i < neighbors.Length; i++)
            {
                int neighborId = neighbors[i];
                var neighborPos = _tiles[neighborId].Position;
                // Midpoint on unit sphere
                vertices3D[i] = Vector3.Normalize(tilePos + neighborPos);
            }

            // Sort vertices by angle around the tile center in 3D
            // Create a local coordinate system on the tangent plane
            var up = tilePos; // Normal to sphere at tile center

            // Find an arbitrary tangent vector
            Vector3 tangent;
            if (MathF.Abs(up.Y) < 0.99f)
                tangent = Vector3.Normalize(Vector3.Cross(up, Vector3.UnitY));
            else
                tangent = Vector3.Normalize(Vector3.Cross(up, Vector3.UnitX));

            var bitangent = Vector3.Cross(up, tangent);

            // Sort by angle in tangent plane
            var sortedIndices = new int[neighbors.Length];
            for (int i = 0; i < neighbors.Length; i++) sortedIndices[i] = i;

            Array.Sort(sortedIndices, (a, b) =>
            {
                var va = vertices3D[a] - tilePos;
                var vb = vertices3D[b] - tilePos;
                float angleA = MathF.Atan2(Vector3.Dot(va, bitangent), Vector3.Dot(va, tangent));
                float angleB = MathF.Atan2(Vector3.Dot(vb, bitangent), Vector3.Dot(vb, tangent));
                return angleA.CompareTo(angleB);
            });

            // Convert sorted vertices to lat/lon
            var result = new (float lat, float lon)[neighbors.Length];
            for (int i = 0; i < sortedIndices.Length; i++)
            {
                result[i] = CartesianToLatLon(vertices3D[sortedIndices[i]]);
            }

            return result;
        }

        /// <summary>
        /// Returns the 3D position of a tile on the unit sphere.
        /// </summary>
        public Vector3 GetTilePosition(int tileId)
        {
            if (tileId < 0 || tileId >= TileCount)
                throw new ArgumentOutOfRangeException(nameof(tileId));
            return _tiles[tileId].Position;
        }

        /// <summary>
        /// Find the tile ID that contains the given lat/lon coordinates.
        /// Uses spatial grid for O(1) average lookup.
        /// </summary>
        public int GetTileAtLatLon(float lat, float lon)
        {
            var (gridX, gridY) = LatLonToGridCell(lat, lon);
            var target = LatLonToCartesian(lat, lon);

            int closest = 0;
            float minDist = float.MaxValue;

            // Near poles, longitude becomes meaningless - expand horizontal search
            // At equator: cos(0) = 1.0 -> search 1 cell each way
            // At 80° lat: cos(80) = 0.17 -> search ~6 cells each way
            // At 89° lat: cos(89) = 0.017 -> search entire row
            float cosLat = MathF.Cos(lat * MathF.PI / 180f);
            int horizontalRange = cosLat < 0.1f
                ? SpatialGridSize / 2  // Near poles: search half the grid
                : (int)Math.Ceiling(1.0f / Math.Max(cosLat, 0.2f));
            horizontalRange = Math.Clamp(horizontalRange, 1, SpatialGridSize / 2);

            // Search expanded horizontal range, normal vertical range
            // If no tiles found, expand vertical range
            for (int vRange = 1; vRange <= 4; vRange++)
            {
                for (int dx = -horizontalRange; dx <= horizontalRange; dx++)
                {
                    // Only search new dy values on expanded passes, but simple loop is fine for O(N)
                    // For simplicity, just search full block.
                    // Optimization: We could track visited cells, but grid is small enough.

                    for (int dy = -vRange; dy <= vRange; dy++)
                    {
                        int gx = (gridX + dx + SpatialGridSize) % SpatialGridSize;
                        int gy = Math.Clamp(gridY + dy, 0, SpatialGridSize - 1);

                        foreach (int tileId in _spatialGrid[gx, gy])
                        {
                            float dist = Vector3.DistanceSquared(target, _tiles[tileId].Position);
                            if (dist < minDist)
                            {
                                minDist = dist;
                                closest = tileId;
                            }
                        }
                    }
                }

                // If we found ANY candidate, we can stop expanding
                // (closest is guaranteed to be in this range or inner ranges)
                if (minDist < float.MaxValue) break;
            }

            return closest;
        }

        /// <summary>Builds the spatial grid for fast lookups.</summary>
        private void BuildSpatialGrid()
        {
            for (int i = 0; i < TileCount; i++)
            {
                if (_tiles[i].Position == Vector3.Zero) continue;
                var (gx, gy) = LatLonToGridCell(_tiles[i].Lat, _tiles[i].Lon);
                _spatialGrid[gx, gy].Add(i);
            }
        }

        /// <summary>Converts lat/lon to spatial grid cell indices.</summary>
        private static (int x, int y) LatLonToGridCell(float lat, float lon)
        {
            // lat: -90 to 90 -> 0 to SpatialGridSize-1
            // lon: -180 to 180 -> 0 to SpatialGridSize-1
            int y = (int)((lat + 90f) / 180f * (SpatialGridSize - 1));
            int x = (int)((lon + 180f) / 360f * SpatialGridSize) % SpatialGridSize;
            return (Math.Clamp(x, 0, SpatialGridSize - 1), Math.Clamp(y, 0, SpatialGridSize - 1));
        }

        /// <summary>
        /// Builds topology using Normalized Barycentric Interpolation.
        /// This creates a Class I Geodesic Grid with 3-fold face symmetry, matching Gleba's topology.
        /// </summary>
        private void BuildTopology()
        {
            var positionToId = new Dictionary<Vector3Key, int>();
            int tileIndex = 0;

            for (int face = 0; face < 20; face++)
            {
                Vector3 v0 = IcoVertices[IcoFaces[face, 0]];
                Vector3 v1 = IcoVertices[IcoFaces[face, 1]];
                Vector3 v2 = IcoVertices[IcoFaces[face, 2]];

                // Iterate barycentric coordinates for a triangle grid
                // We want discrete steps.
                // Row i goes from 0 to Resolution
                // Col j goes from 0 to i

                for (int i = 0; i <= _resolution; i++)
                {
                    for (int j = 0; j <= i; j++)
                    {
                        // Convert grid indices to Barycentric (u,v,w)
                        // v1 weight: d1 = j / R
                        // v2 weight: d2 = (i - j) / R ?? No.
                        // Standard barycentric parameterization for this loop:

                        // Let's use vector edges for clarity:
                        // P = V0 + (V1-V0) * s + (V2-V1) * t ??

                        // Easier:
                        // coord 1 (V0): (Resolution - i) / Resolution
                        // coord 2 (V1): j / Resolution
                        // coord 3 (V2): (i - j) / Resolution
                        // Sum = (R-i + j + i-j)/R = R/R = 1. Correct.

                        float w0 = (float)(_resolution - i) / _resolution;
                        float w1 = (float)j / _resolution;
                        float w2 = (float)(i - j) / _resolution;

                        // Linear combination on the face plane (chord)
                        Vector3 posPoints = v0 * w0 + v1 * w1 + v2 * w2;

                        // Project to sphere (Normalize)
                        // This centers tiles on the face (largest tiles at centroid) 
                        // and creates perfect 3-fold symmetry.
                        Vector3 pos = Vector3.Normalize(posPoints);

                        // Use tile vars to track u,v
                        AddTile(pos, face, i, j, ref tileIndex, positionToId);
                        if (tileIndex >= TileCount) break;
                    }
                    if (tileIndex >= TileCount) break;
                }
                if (tileIndex >= TileCount) break;
            }
        }

        private void AddTile(Vector3 pos, int face, int u, int v, ref int tileIndex, Dictionary<Vector3Key, int> positionToId)
        {
            var key = new Vector3Key(pos);
            if (!positionToId.ContainsKey(key))
            {
                var (lat, lon) = CartesianToLatLon(pos);
                bool isPentagon = IsIcosahedronVertex(pos);

                _tiles[tileIndex] = new HexTile
                {
                    Id = tileIndex,
                    Face = face,
                    U = u,
                    V = v,
                    Lat = lat,
                    Lon = lon,
                    Position = pos,
                    IsPentagon = isPentagon
                };
                positionToId[key] = tileIndex;
                tileIndex++;
            }
        }

        /// <summary>
        /// Check if a position is at one of the 12 icosahedron vertices.
        /// </summary>
        private static bool IsIcosahedronVertex(Vector3 pos)
        {
            const float threshold = 0.001f;
            foreach (var vertex in IcoVertices)
            {
                if (Vector3.DistanceSquared(pos, vertex) < threshold)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Builds neighbor relationships based on spatial proximity.
        /// Optimized using spatial grid.
        /// </summary>
        private void BuildNeighbors()
        {
            Parallel.For(0, TileCount, i =>
            {
                if (_tiles[i].Position == Vector3.Zero) return;

                var candidates = new List<(int id, float dist)>();
                var (gridX, gridY) = LatLonToGridCell(_tiles[i].Lat, _tiles[i].Lon);
                float latRad = _tiles[i].Lat * MathF.PI / 180f;
                float cosLat = MathF.Abs(MathF.Cos(latRad));

                // Adaptive search range
                int baseRange = (int)MathF.Ceiling((120f / _resolution) / 2f);
                if (baseRange < 2) baseRange = 2;

                int horizontalRange = baseRange;
                if (cosLat < 0.6f) horizontalRange = baseRange * 2;
                if (cosLat < 0.3f) horizontalRange = baseRange * 4;
                if (cosLat < 0.15f) horizontalRange = SpatialGridSize / 2;

                int verticalRange = baseRange;

                for (int dx = -horizontalRange; dx <= horizontalRange; dx++)
                {
                    for (int dy = -verticalRange; dy <= verticalRange; dy++)
                    {
                        int gx = (gridX + dx + SpatialGridSize) % SpatialGridSize;
                        int gy = Math.Clamp(gridY + dy, 0, SpatialGridSize - 1);

                        foreach (int j in _spatialGrid[gx, gy])
                        {
                            if (i == j) continue;
                            float dist = Vector3.DistanceSquared(_tiles[i].Position, _tiles[j].Position);
                            candidates.Add((j, dist));
                        }
                    }
                }

                candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
                int targetCount = _tiles[i].IsPentagon ? 5 : 6;
                var neighborList = new List<int>();

                if (candidates.Count > 0)
                {
                    float refinedClosestDist = MathF.Sqrt(candidates[0].dist);
                    float thresholdSq = (refinedClosestDist * 1.5f) * (refinedClosestDist * 1.5f);

                    foreach (var c in candidates)
                    {
                        if (c.dist <= thresholdSq && neighborList.Count < targetCount)
                            neighborList.Add(c.id);
                        else if (c.dist > thresholdSq)
                            break;
                    }
                }

                _neighbors[i] = neighborList.ToArray();
            });
        }

        /// <summary>Count of pentagons (should always be 12 for valid topology).</summary>
        public int PentagonCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < TileCount && _tiles[i].Position != Vector3.Zero; i++)
                    if (_tiles[i].IsPentagon) count++;
                return count;
            }
        }

        /// <summary>Converts lat/lon (degrees) to unit sphere cartesian coordinates.</summary>
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

        /// <summary>Converts unit sphere cartesian to lat/lon (degrees).</summary>
        private static (float lat, float lon) CartesianToLatLon(Vector3 pos)
        {
            float lat = MathF.Asin(pos.Y) * 180f / MathF.PI;
            float lon = MathF.Atan2(pos.Z, pos.X) * 180f / MathF.PI;
            return (lat, lon);
        }

        /// <summary>Represents a single hex tile on the sphere.</summary>
        private struct HexTile
        {
            public int Id;
            public int Face;      // Which of the 20 icosahedron faces (0-19)
            public int U, V;      // Barycentric grid coordinates within face
            public float Lat, Lon;
            public Vector3 Position;
            public bool IsPentagon;
        }

        /// <summary>Key for dictionary lookup with floating point tolerance.</summary>
        private readonly struct Vector3Key : IEquatable<Vector3Key>
        {
            private readonly int _x, _y, _z;
            private const float Scale = 10000f; // Precision for deduplication

            public Vector3Key(Vector3 v)
            {
                _x = (int)(v.X * Scale);
                _y = (int)(v.Y * Scale);
                _z = (int)(v.Z * Scale);
            }

            public bool Equals(Vector3Key other) => _x == other._x && _y == other._y && _z == other._z;
            public override bool Equals(object? obj) => obj is Vector3Key k && Equals(k);
            public override int GetHashCode() => HashCode.Combine(_x, _y, _z);
        }
    }
}

using System;

namespace Aethelgard.Simulation
{
    /// <summary>
    /// Helper class for spherical map calculations.
    /// Handles conversion between pixel coordinates and lat/lon,
    /// and calculates proper spherical (great-circle) distances.
    /// </summary>
    public static class SphericalMath
    {
        private const float DEG_TO_RAD = (float)(Math.PI / 180.0);
        private const float RAD_TO_DEG = (float)(180.0 / Math.PI);

        /// <summary>
        /// Generates a deterministic 3D offset for a given plate ID.
        /// Decorrelates noise sampling positions to avoid directional bias.
        /// </summary>
        public static System.Numerics.Vector3 GetDecorrelatedPlateOffset(int plateId)
        {
            // Simple hash-based offset (don't need crypto quality, just dispersion)
            // Use local Random with invariant seed based on ID
            var rng = new Random(plateId * 73856093 ^ 19349663);
            float x = (float)rng.NextDouble() * 1000f; // Large offsets to avoid repeating
            float y = (float)rng.NextDouble() * 1000f;
            float z = (float)rng.NextDouble() * 1000f;
            return new System.Numerics.Vector3(x, y, z);
        }

        /// <summary>
        /// Convert pixel coordinates to latitude/longitude.
        /// X maps to longitude (-180 to 180), Y maps to latitude (90 to -90).
        /// </summary>
        public static (float lon, float lat) PixelToLatLon(int x, int y, int width, int height)
        {
            // X: 0 = -180°, width = 180° (longitude wraps)
            float lon = (x / (float)width) * 360f - 180f;

            // Y: 0 = 90° (north pole), height = -90° (south pole)
            float lat = 90f - (y / (float)height) * 180f;

            return (lon, lat);
        }

        /// <summary>
        /// Convert latitude/longitude to pixel coordinates.
        /// </summary>
        public static (int x, int y) LatLonToPixel(float lon, float lat, int width, int height)
        {
            // Normalize longitude to -180 to 180
            while (lon < -180) lon += 360;
            while (lon > 180) lon -= 360;

            int x = (int)((lon + 180f) / 360f * width);
            int y = (int)((90f - lat) / 180f * height);

            // Clamp
            x = Math.Clamp(x, 0, width - 1);
            y = Math.Clamp(y, 0, height - 1);

            return (x, y);
        }

        /// <summary>
        /// Calculate the great-circle distance between two points using Haversine formula.
        /// Returns normalized distance (0 to PI, where PI = half the globe).
        /// </summary>
        public static float SphericalDistance(float lon1, float lat1, float lon2, float lat2)
        {
            float lat1Rad = lat1 * DEG_TO_RAD;
            float lat2Rad = lat2 * DEG_TO_RAD;
            float dLat = (lat2 - lat1) * DEG_TO_RAD;
            float dLon = (lon2 - lon1) * DEG_TO_RAD;

            // Handle longitude wrapping
            if (dLon > Math.PI) dLon -= 2 * (float)Math.PI;
            if (dLon < -Math.PI) dLon += 2 * (float)Math.PI;

            float a = (float)(Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                              Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                              Math.Sin(dLon / 2) * Math.Sin(dLon / 2));

            float c = 2 * (float)Math.Asin(Math.Sqrt(Math.Min(1.0, a)));

            return c; // Normalized: 0 = same point, PI = opposite side of globe
        }

        /// <summary>
        /// Calculate spherical distance directly from pixel coordinates.
        /// </summary>
        public static float SphericalDistancePixels(int x1, int y1, int x2, int y2, int width, int height)
        {
            var (lon1, lat1) = PixelToLatLon(x1, y1, width, height);
            var (lon2, lat2) = PixelToLatLon(x2, y2, width, height);
            return SphericalDistance(lon1, lat1, lon2, lat2);
        }

        /// <summary>
        /// Get the latitude factor for a given Y position.
        /// Returns cos(latitude): 1.0 at equator, 0.0 at poles.
        /// Used to scale horizontal distances/noise to compensate for projection stretching.
        /// </summary>
        public static float LatitudeFactor(int y, int height)
        {
            // Y = 0 is north pole (lat=90), Y = height is south pole (lat=-90)
            // Equator is at Y = height/2 (lat=0)
            float lat = 90f - (y / (float)height) * 180f;
            return (float)Math.Cos(lat * DEG_TO_RAD);
        }

        /// <summary>
        /// Get latitude in degrees for a given Y position.
        /// </summary>
        public static float GetLatitude(int y, int height)
        {
            return 90f - (y / (float)height) * 180f;
        }

        /// <summary>
        /// Get longitude in degrees for a given X position.
        /// </summary>
        public static float GetLongitude(int x, int width)
        {
            return (x / (float)width) * 360f - 180f;
        }
        /// <summary>
        /// Convert Lat/Lon to a 3D point on the unit sphere.
        /// Useful for vector math (dot products, direction checks).
        /// </summary>
        public static System.Numerics.Vector3 LatLonToHypersphere(float lon, float lat)
        {
            float lonRad = lon * DEG_TO_RAD;
            float latRad = lat * DEG_TO_RAD;

            // Standard spherical coords (Y-up)
            float x = (float)(Math.Cos(latRad) * Math.Cos(lonRad));
            float y = (float)Math.Sin(latRad);
            float z = (float)(Math.Cos(latRad) * Math.Sin(lonRad));

            return new System.Numerics.Vector3(x, y, z);
        }
    }
}

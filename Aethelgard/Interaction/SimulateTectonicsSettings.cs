namespace Aethelgard.Interaction
{
    public class SimulateTectonicsSettings
    {
        // =============================================================
        // BASE TERRAIN
        // =============================================================
        public float CoastSlopeStrength = 0.15f;      // How much interior is higher than coast
        public float RegionalNoiseStrength = 0.5f;    // Large-scale highland/lowland variation
        public float LocalNoiseStrength = 0.12f;      // Medium-scale hills/valleys
        public float BaseElevation = 0.15f;           // Minimum continental elevation

        // Continental coverage
        public float LandCoverageThreshold = 0.0f;    // Higher = less land per continental plate (-0.5 to 0.5)
        public float LatitudeBias = 0.0f;             // Positive = more equatorial land, Negative = more polar land

        // =============================================================
        // ACTIVE BOUNDARIES (Mountains at plate edges)
        // =============================================================
        public float BoundaryMountainStrength = 1.5f; // Height multiplier for boundary mountains
        public float BoundarySpreadPasses = 20;       // How far inland mountains spread
        public float BoundarySpreadDecay = 0.88f;     // Decay rate for inland spread

        // =============================================================
        // FOSSIL BOUNDARIES (Ancient seam lines - secondary Voronoi)
        // =============================================================
        public int FossilSubRegionsMin = 3;           // Min sub-regions per continental plate
        public int FossilSubRegionsMax = 6;           // Max sub-regions per continental plate
        public float FossilNoiseOffset = 30f;         // Noise offset for organic boundaries
        public float FossilMountainStrength = 0.4f;   // Height of fossil mountains (eroded)
        public int FossilSpreadPasses = 6;            // Spread distance for fossils
        public float FossilSpreadDecay = 0.75f;       // Decay rate

        // =============================================================
        // HOTSPOTS (Intraplate volcanism)
        // =============================================================
        public int HotspotsMin = 5;                   // Minimum hotspots per map
        public int HotspotsMax = 15;                  // Maximum hotspots per map
        public float HotspotPeakMin = 0.8f;           // Minimum peak height
        public float HotspotPeakMax = 1.6f;           // Maximum peak height
        public int HotspotClusterSizeMin = 3;         // Min peaks per cluster
        public int HotspotClusterSizeMax = 8;         // Max peaks per cluster

        // =============================================================
        // BIOGENIC FEATURES (Coral reefs, limestone)
        // =============================================================
        public int BiogenicZonesMin = 8;              // Min biogenic zones
        public int BiogenicZonesMax = 15;             // Max biogenic zones
        public int BiogenicZoneSizeMin = 20;          // Min zone size
        public int BiogenicZoneSizeMax = 60;          // Max zone size
        public float BiogenicElevation = 0.08f;       // Target elevation (very low, flat)

        // =============================================================
        // EROSION
        // =============================================================
        public float ErosionRate = 0.05f;             // Erosion strength per pass
        public int ErosionPasses = 5;                 // Number of erosion iterations

        // =============================================================
        // LEGACY / DRIFT (for UI compatibility)
        // =============================================================
        public int DriftSteps = 20;                   // Steps per drift command
        public float DriftSpeed = 1.0f;               // Plate movement speed
        public float UpliftStrength = 0.5f;           // (Legacy) used by UI
        public float CollisionWidth = 3.0f;           // (Legacy) used by UI
    }
}

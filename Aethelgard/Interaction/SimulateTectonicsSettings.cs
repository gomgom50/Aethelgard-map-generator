namespace Aethelgard.Interaction
{
    /// <summary>
    /// Settings for physics-based terrain generation using isostasy and plate tectonics.
    /// </summary>
    public class SimulateTectonicsSettings
    {
        // =============================================================
        // ISOSTASY (Crust thickness determines elevation)
        // =============================================================
        public float MantleEquilibrium = 30f;      // Reference thickness (km) - this floats at sea level
        public float BuoyancyFactor = 0.05f;       // Elevation per km of thickness above equilibrium
        public float ContinentalThickness = 35f;   // Base continental crust thickness (km)
        public float OceanicThickness = 7f;        // Base oceanic crust thickness (km)
        public float ThicknessVariation = 8f;      // Random variation in thickness per plate

        // =============================================================
        // BOUNDARY EFFECTS (Collisions thicken, rifts thin)
        // =============================================================
        public float ConvergentThickening = 30f;   // Extra km at convergent boundaries (mountain building)
        public float DivergentThinning = 15f;      // Reduction km at divergent boundaries (rifts)
        public float BoundaryWidth = 15f;          // Pixel width of boundary effect zone
        public float BoundaryDecay = 0.85f;        // How quickly effect fades from boundary

        // =============================================================
        // ARC/OROGEN PLACEMENT & SHAPE
        // =============================================================
        public float ArcOffset = 12f;              // Distance inland where mountain crest peaks (pixels)
        public float ArcWidth = 10f;               // Core width of the mountain belt
        public float PlateauWidth = 30f;           // Width of plateau for C-C collisions
        public float BandScale = 6f;               // Scale of parallel ridges (banding)
        public float ForearcSubsidence = 4f;       // Slight depression between coast and arc
        public float CoastBuffer = 15f;            // Attenuate uplift within this distance of coast

        // =============================================================
        // MARGIN TYPES (Passive vs Active coasts)
        // =============================================================
        public float PassiveMarginSinking = 0.4f;  // How much passive margins sink (continental shelf)
        public float PassiveMarginWidth = 60f;     // Width of passive margin zone
        public float ActiveMarginUplift = 0.2f;    // Extra uplift at active margins

        // =============================================================
        // TECTONIC TILT (Plates tilt toward subduction)
        // =============================================================
        public float TiltStrength = 0.15f;         // Gradient strength across continent
        public bool EnableTilt = true;             // Toggle tilting on/off

        // =============================================================
        // MANTLE DYNAMICS (Random basins from mantle lows)
        // =============================================================
        public int MantleLowsMin = 3;              // Minimum mantle depression points
        public int MantleLowsMax = 8;              // Maximum mantle depression points
        public float MantleLowStrength = 10f;      // How much they reduce thickness (km)
        public float MantleLowRadius = 80f;        // Radius of effect (pixels)

        // =============================================================
        // HOTSPOTS (Volcanic uplift points)
        // =============================================================
        public int HotspotsMin = 5;
        public int HotspotsMax = 15;
        public float HotspotThickening = 8f;       // Extra thickness at hotspots
        public float HotspotRadius = 25f;          // Radius of hotspot effect

        // =============================================================
        // FOSSIL BOUNDARIES (Ancient mountain ranges)
        // =============================================================
        public int FossilSubRegionsMin = 3;
        public int FossilSubRegionsMax = 6;
        public float FossilNoiseOffset = 30f;
        public float FossilThickening = 12f;       // Extra thickness at fossil sutures

        // =============================================================
        // POLAR AND COVERAGE
        // =============================================================
        public float PolarOceanZone = 0.05f;       // Fraction of map that's polar ocean (0.05 = 5%)
        public float LandCoverageThreshold = 0.0f; // Higher = less land per plate
        public float LatitudeBias = 0.0f;          // Positive = more equatorial land
        public bool UseSphericalNoise = true;      // Apply latitude compensation to noise

        // =============================================================
        // REFINEMENTS & NATURALISM (New)
        // =============================================================
        public float HotspotWiggle = 1.5f;         // How much tracks deviate from straight lines
        public float HotspotChainChance = 0.7f;    // Chance of a hotspot being a chain vs single
        public float FossilGrainStrength = 0.6f;   // How distinct the "wood grain" is
        public float RiftWidth = 6.0f;             // Width of continental rifts
        public float GlacialStrength = 0.8f;       // Strength of glacial carving (fjords)
        public float SedimentDeposition = 0.005f;  // Amount of sediment added to plains

        // =============================================================
        // EROSION (Smoothing & Flow)
        // =============================================================
        public float ErosionStrength = 0.3f;       // How much erosion smooths terrain
        public int ErosionPasses = 3;              // Number of erosion iterations
        public float FlowErosionStrength = 0.4f;   // Strength of channel carving

        // =============================================================
        // LEGACY (UI compatibility)
        // =============================================================
        public int DriftSteps = 20;
        public float DriftSpeed = 1.0f;
        public float UpliftStrength = 0.5f;
        public float CollisionWidth = 3.0f;
    }
}

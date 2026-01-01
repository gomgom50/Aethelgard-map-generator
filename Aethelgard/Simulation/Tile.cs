using System.Runtime.InteropServices;

namespace Aethelgard.Simulation
{
    /// <summary>
    /// Core tile data structure for the world map.
    /// Contains ALL fields required by the WorldGenDeveloperGuide phases.
    /// Organized by generation phase for clarity.
    /// 
    /// Memory layout optimized for cache efficiency with related fields grouped.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Tile
    {
        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 0: FOUNDATION
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Height above/below sea level in meters. Sea level = 0.</summary>
        public float Elevation;

        /// <summary>True if this tile is land, false if water.</summary>
        public bool IsLand;

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 1: TECTONICS
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>ID of the major tectonic plate owning this tile. -1 = unassigned.</summary>
        public int PlateId;

        /// <summary>ID of the microplate/craton within the major plate. -1 = none.</summary>
        public int MicroplateId;

        /// <summary>Type of crust: 0=Oceanic, 1=Continental, 2=Transitional.</summary>
        public byte CrustType;

        /// <summary>Age of oceanic crust (normalized 0-1, 0=new at rift, 1=oldest).</summary>
        public float CrustAge;

        /// <summary>Thickness of crust in km. Used for isostasy calculations.</summary>
        public float CrustThickness;

        /// <summary>Type of plate boundary at this tile. See BoundaryType enum.</summary>
        public BoundaryType BoundaryType;

        /// <summary>TRUE if this tile is on a microplate/fossil boundary.</summary>
        public bool IsMicroBoundary;

        /// <summary>Primary bedrock type. See RockType enum.</summary>
        public RockType RockType;

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 2: TERRAIN FEATURES
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>ID of the orogeny (mountain range) affecting this tile. -1 = none.</summary>
        public int OrogenyId;

        /// <summary>Strength/intensity of orogeny at this tile (0-1).</summary>
        public float OrogenySeverity;

        /// <summary>ID of the hotspot track affecting this tile. -1 = none.</summary>
        public int HotspotId;

        /// <summary>ID of volcano at this tile. -1 = none. Uses SlotMap for details.</summary>
        public int VolcanoId;

        /// <summary>Feature type classification for this tile. See FeatureType enum.</summary>
        public FeatureType FeatureType;

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 3: OCEANOGRAPHY
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Continental shelf zone (0=deep ocean, 1=outer shelf, 2=inner shelf, etc.)</summary>
        public byte ShelfZone;

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 4: HYDROLOGY
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>ID of the waterbody (lake/sea) this tile belongs to. -1 = none.</summary>
        public int WaterbodyId;

        /// <summary>Accumulated waterflow through this tile. Higher = more drainage.</summary>
        public float WaterflowAccumulation;

        /// <summary>Driver value for lake generation (size hint).</summary>
        public float LakeDriver;

        /// <summary>Thickness of ice/glacier at this tile in meters. 0 = no ice.</summary>
        public float IceThickness;

        /// <summary>TRUE if this tile has a river passing through.</summary>
        public bool HasRiver;

        /// <summary>River flow direction (index into neighbor array, or -1 for sink).</summary>
        public sbyte RiverFlowDirection;

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 5: EROSION & SOIL
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Soil composition: Clay fraction (0-1).</summary>
        public float SoilCite;

        /// <summary>Soil composition: Silt fraction (0-1).</summary>
        public float SoilSilt;

        /// <summary>Soil composition: Sand fraction (0-1).</summary>
        public float SoilSand;

        /// <summary>Soil composition: Organic matter fraction (0-1).</summary>
        public float SoilOrganic;

        /// <summary>Depth/thickness of soil layer in meters.</summary>
        public float SoilDepth;

        /// <summary>Accumulated sediment from erosion transport.</summary>
        public float SedimentAccumulation;

        // ═══════════════════════════════════════════════════════════════════════
        // PHASE 6: CLIMATE & BIOMES
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>January temperature in Celsius.</summary>
        public float TemperatureJan;

        /// <summary>July temperature in Celsius.</summary>
        public float TemperatureJul;

        /// <summary>January rainfall in mm.</summary>
        public float RainfallJan;

        /// <summary>July rainfall in mm.</summary>
        public float RainfallJul;

        /// <summary>Primary biome classification. See BiomeId enum.</summary>
        public byte BiomeId;

        /// <summary>Biome variant (sub-classification within biome).</summary>
        public byte BiomeVariant;

        /// <summary>Köppen climate classification code.</summary>
        public byte KoppenCode;

        /// <summary>Flora/vegetation weight: Forest coverage (0-1).</summary>
        public float FloraForest;

        /// <summary>Flora/vegetation weight: Grass coverage (0-1).</summary>
        public float FloraGrass;

        /// <summary>Flora/vegetation weight: Shrub coverage (0-1).</summary>
        public float FloraShrub;

        /// <summary>Flora/vegetation weight: Desert/barren coverage (0-1).</summary>
        public float FloraDesert;

        // ═══════════════════════════════════════════════════════════════════════
        // FLAGS & EXTENSIBILITY
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Bitfield for various tile states. See TileFlags enum.</summary>
        public TileFlags Flags;

        /// <summary>Debug/visualization value (raw float).</summary>
        public float DebugValue;

        /// <summary>Debug/visualization marker (3 bytes RGB).</summary>
        public uint DebugMarker;

        // ═══════════════════════════════════════════════════════════════════════
        // CONSTRUCTORS & HELPERS
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Creates a default tile with all fields zeroed/unassigned.</summary>
        public static Tile Default => new Tile
        {
            Elevation = 0f,
            IsLand = false,
            PlateId = -1,
            MicroplateId = -1,
            CrustType = 0,
            CrustAge = 0f,
            CrustThickness = 7f, // Default oceanic crust thickness
            BoundaryType = BoundaryType.None,
            IsMicroBoundary = false,
            RockType = RockType.None,
            OrogenyId = -1,
            OrogenySeverity = 0f,
            HotspotId = -1,
            VolcanoId = -1,
            FeatureType = FeatureType.None,
            ShelfZone = 0,
            WaterbodyId = -1,
            WaterflowAccumulation = 0f,
            LakeDriver = 0f,
            IceThickness = 0f,
            HasRiver = false,
            RiverFlowDirection = -1,
            SoilCite = 0f,
            SoilSilt = 0f,
            SoilSand = 0f,
            SoilOrganic = 0f,
            SoilDepth = 0f,
            SedimentAccumulation = 0f,
            TemperatureJan = 0f,
            TemperatureJul = 0f,
            RainfallJan = 0f,
            RainfallJul = 0f,
            BiomeId = 0,
            BiomeVariant = 0,
            KoppenCode = 0,
            FloraForest = 0f,
            FloraGrass = 0f,
            FloraShrub = 0f,
            FloraDesert = 0f,
            Flags = TileFlags.None,
            DebugMarker = 0
        };

        /// <summary>Check if tile has a specific flag.</summary>
        public bool HasFlag(TileFlags flag) => (Flags & flag) != 0;

        /// <summary>Set a specific flag.</summary>
        public void SetFlag(TileFlags flag) => Flags |= flag;

        /// <summary>Clear a specific flag.</summary>
        public void ClearFlag(TileFlags flag) => Flags &= ~flag;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SUPPORTING ENUMS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Type of plate boundary interaction.</summary>
    public enum BoundaryType : byte
    {
        None = 0,
        Convergent = 1,   // Plates moving together (mountains, trenches)
        Divergent = 2,    // Plates moving apart (rifts, ridges)
        Transform = 3     // Plates sliding past each other (faults)
    }

    /// <summary>Geological feature type for visualization and classification.</summary>
    public enum FeatureType : byte
    {
        None = 0,
        Ocean = 1,
        Craton = 2,           // Stable continental interior
        ActiveBoundary = 3,   // Current plate boundary feature
        FossilBoundary = 4,   // Ancient/inactive boundary
        Hotspot = 5,          // Mantle plume volcanic area
        ActiveOrogeny = 6,    // Current mountain building
        AncientOrogeny = 7,   // Old eroded mountains
        Rift = 8,             // Continental rift zone
        Hill = 9,             // Uplifted terrain
        Volcano = 10,         // Volcanic feature
        ContinentalShelf = 11 // Shallow ocean near coast
    }

    /// <summary>
    /// Bedrock/rock type classification.
    /// Organized by formation process: Sedimentary, Igneous (Intrusive/Extrusive), Metamorphic.
    /// </summary>
    public enum RockType : byte
    {
        None = 0,

        // SEDIMENTARY - Clastic (from weathered rock)
        Conglomerate = 1,   // Coarse rounded gravel
        Breccia = 2,        // Coarse angular fragments
        Sandstone = 3,      // Sand-sized particles
        Siltstone = 4,      // Silt-sized particles
        Mudstone = 5,       // Mud-sized particles
        Claystone = 6,      // Clay-sized particles
        Shale = 7,          // Fissile mudstone

        // SEDIMENTARY - Chemical/Biogenic
        Limestone = 10,     // CaCO3 from organisms
        Dolostone = 11,     // CaMg(CO3)2
        Chalk = 12,         // Soft limestone
        Chert = 13,         // Siliceous
        Halite = 14,        // Rock salt (evaporite)

        // IGNEOUS - Intrusive (slow cooling, coarse)
        Granite = 20,       // Felsic, quartz + feldspar
        Granodiorite = 21,  // Intermediate
        Diorite = 22,       // Intermediate
        Gabbro = 23,        // Mafic
        Peridotite = 24,    // Ultramafic (mantle)

        // IGNEOUS - Extrusive (fast cooling, fine)
        Rhyolite = 30,      // Felsic volcanic
        Dacite = 31,        // Intermediate volcanic
        Andesite = 32,      // Intermediate volcanic
        Basalt = 33,        // Mafic volcanic
        Picrobasalt = 34,   // Ultramafic volcanic

        // METAMORPHIC - Low to High Grade
        Slate = 40,         // Low grade (from shale)
        Phyllite = 41,      // Low-medium grade
        Schist = 42,        // Medium grade
        Gneiss = 43,        // High grade
        Migmatite = 44,     // Partial melt
        Marble = 45,        // From limestone
        Quartzite = 46,     // From sandstone
        Hornfel = 47,       // Contact metamorphism
        Skarn = 48,         // Contact with carbonate

        // METAMORPHIC - Specialty
        Blueschist = 50,    // High pressure, low temp
        Eclogite = 51,      // Very high pressure
        Greenstone = 52,    // Altered basalt
        Greenschist = 53,   // Low grade facies
        Amphibolite = 54,   // Medium grade facies
        Granulite = 55,     // High grade facies
        Serpentinite = 56   // Hydrated peridotite
    }

    /// <summary>Bit flags for various tile states.</summary>
    [System.Flags]
    public enum TileFlags : uint
    {
        None = 0,

        // Phase 1: Tectonics
        IsBoundary = 1 << 0,
        IsConvergent = 1 << 1,
        IsDivergent = 1 << 2,
        IsTransform = 1 << 3,
        IsContinental = 1 << 4,
        IsOceanic = 1 << 5,

        // Phase 2: Features
        HasVolcano = 1 << 8,
        HasHotspot = 1 << 9,
        HasOrogeny = 1 << 10,
        IsUplift = 1 << 11,
        IsFossilMountain = 1 << 12,

        // Phase 4: Hydrology
        HasRiver = 1 << 16,
        HasLake = 1 << 17,
        HasGlacier = 1 << 18,
        IsFjord = 1 << 19,
        IsCoastal = 1 << 20,

        // Phase 5: Erosion
        IsEroded = 1 << 24,
        HasSediment = 1 << 25,


    }
}

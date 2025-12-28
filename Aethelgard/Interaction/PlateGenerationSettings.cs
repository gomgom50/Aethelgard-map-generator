namespace Aethelgard.Interaction
{
    public enum GenerationMode
    {
        Random,
        FromElevation, // Inverse Generation (Terrain -> Plates)
        Supercontinent // Pangea Start (For Drift Simulation)
    }

    public class PlateGenerationSettings
    {
        public GenerationMode Mode = GenerationMode.Random;
        public int TargetPlateCount = 20;
        public int MicroPlateFactor = 5; // How many micros per target (agglomeration source)
        public float WeightVariance = 0.5f; // 0.0 to 1.0 (Uniform to Varied)
        public float DistortionScale = 0.005f; // Noise Freq
        public float DistortionStrength = 40.0f; // Noise Amp
        public bool UseSmoothing = true;

        // Seeding
        public int Seed = 0;
        public bool UseRandomSeed = true;

        // Crust
        public float ContinentalRatio = 0.4f; // 40% of plates are continental
        public float OceanicLevel = -1.0f; // Base Elevation for Oceanic
        public float ContinentalLevel = 0.1f; // Base Elevation for Continental
    }
}

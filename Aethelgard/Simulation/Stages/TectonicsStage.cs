using System;
using Aethelgard.Simulation.Core;
using Aethelgard.Simulation.Generators;

namespace Aethelgard.Simulation.Stages
{
    /// <summary>
    /// Phase 1: Tectonics Generation Stage.
    /// Wraps the PlateGenerator logic into the pipeline architecture.
    /// Values in this class can be bound to UI sliders.
    /// </summary>
    public class TectonicsStage : IGenerationStage
    {
        public string Name => "Tectonics";
        public string Status => _status;
        public float Progress => _progress;

        private string _status = "Waiting";
        private float _progress = 0f;

        // Configuration Parameters (Bound to UI)
        public int PlateCount { get; set; } = 12;
        public float ContinentalRatio { get; set; } = 0.4f;
        public float TargetLandFraction { get; set; } = 0.3f;
        public float NoiseStrength { get; set; } = 0.5f;

        // Noise Stack A (Base)
        public float NoiseAScale { get; set; } = 0.8f;
        public float NoiseAPersistence { get; set; } = 0.5f;
        public float NoiseALacunarity { get; set; } = 2.0f;
        public float NoiseAWeight { get; set; } = 1.0f;

        // Noise Stack B (Detail)
        public float NoiseBScale { get; set; } = 5.0f;
        public float NoiseBPersistence { get; set; } = 0.5f;
        public float NoiseBLacunarity { get; set; } = 2.0f;
        public float NoiseBWeight { get; set; } = 0.15f;

        public float DistancePenalty { get; set; } = 0.4f;
        public float NoiseWarping { get; set; } = 2.0f;
        public int MicroplatesPerPlate { get; set; } = 3;

        public void Execute(WorldMap map, ConstraintManager constraints)
        {
            _status = "Initializing";
            _progress = 0.0f;

            // Create the generator
            // Note: In future, passing 'constraints' into PlateGenerator will allow it to respect locks.
            // For now, we are just wrapping the existing logic.
            var generator = new PlateGenerator(map, map.Seed);

            // Apply configuration
            generator.PlateCount = PlateCount;
            generator.ContinentalRatio = ContinentalRatio;
            generator.TargetLandFraction = TargetLandFraction;
            generator.NoiseStrength = NoiseStrength;

            generator.NoiseAScale = NoiseAScale;
            generator.NoiseAPersistence = NoiseAPersistence;
            generator.NoiseALacunarity = NoiseALacunarity;
            generator.NoiseAWeight = NoiseAWeight;

            generator.NoiseBScale = NoiseBScale;
            generator.NoiseBPersistence = NoiseBPersistence;
            generator.NoiseBLacunarity = NoiseBLacunarity;
            generator.NoiseBWeight = NoiseBWeight;

            generator.DistancePenalty = DistancePenalty;
            generator.NoiseWarping = NoiseWarping;

            // Run Generation
            _status = "Generating Plates (Seeding & Flooding)";
            _progress = 0.1f;
            generator.GeneratePlates(); // Validates, Floods...

            _status = "Assigning Velocities";
            _progress = 0.3f;
            generator.AssignVelocitiesAndCrust();

            _status = "Classifying Boundaries";
            _progress = 0.4f;
            generator.ClassifyBoundaries();

            _status = "Generating Landmass (Gleba Fractal Fill)";
            _progress = 0.5f;
            generator.GenerateLandmass();

            _status = "Determining Crust Age";
            _progress = 0.7f;
            generator.DetermineCrustAge();

            _status = "Assigning Rock Types";
            _progress = 0.8f;
            generator.AssignRockTypes();


            _status = "Generating Microplates";
            _progress = 0.8f;
            generator.GenerateMicroplates(MicroplatesPerPlate);

            _status = "Complete";
            _progress = 1.0f;
        }
    }
}

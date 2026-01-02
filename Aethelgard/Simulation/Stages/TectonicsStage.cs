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


        // Advanced Config
        public float BoundaryVotingThreshold { get; set; } = 0.525f;
        public float CrustAgeSpread { get; set; } = 2.5f;

        public float CoastalBoostRange { get; set; } = 7500f;
        public float CoastalBoostHeight { get; set; } = 250f;

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

            // Advanced Config
            generator.BoundaryVotingThreshold = BoundaryVotingThreshold;
            generator.CrustAgeSpread = CrustAgeSpread;

            generator.CoastalBoostRange = CoastalBoostRange;
            generator.CoastalBoostHeight = CoastalBoostHeight;

            // Run Generation (Manual Staged Pipeline as per Design Doc)

            // Phase 1: Major Plates
            _status = "Generating Major Plates";
            _progress = 0.1f;
            generator.GeneratePlates();

            // Phase 2: Terranes
            _status = "Generating Microplates";
            _progress = 0.3f;
            generator.GenerateMicroplates(0); // param ignored

            // Phase 3: Velocities & Boundaries
            _status = "Assigning Velocities & Heads";
            _progress = 0.5f;
            generator.AssignVelocitiesAndDirections();
            generator.ClassifyBoundaries();
            generator.DetermineCrustAge();

            // Phase 4: Crust Type
            _status = "Initializing Crust Types";
            _progress = 0.7f;
            generator.InitializeTileCrust();

            // Phase 5: Land Elevation
            _status = "Generating Land Elevation";
            _progress = 0.9f;
            generator.InitializeLandElevation();

            _status = "Complete";
            _progress = 1.0f;
        }
    }
}

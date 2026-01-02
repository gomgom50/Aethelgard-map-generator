namespace Aethelgard.Simulation.Core
{
    /// <summary>
    /// Interface for a distinct stage in the world generation pipeline.
    /// Examples: TectonicsStage, ErosionStage, BiomeStage.
    /// </summary>
    public interface IGenerationStage
    {
        /// <summary>
        /// Display name of the stage (e.g., "Tectonics").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the current status message of the stage.
        /// </summary>
        string Status { get; }

        /// <summary>
        /// Gets the progress of the stage (0.0 to 1.0).
        /// </summary>
        float Progress { get; }

        /// <summary>
        /// Executes the stage.
        /// </summary>
        /// <param name="map">The world map to modify.</param>
        /// <param name="constraints">The constraint manager containing user locks.</param>
        void Execute(WorldMap map, ConstraintManager constraints);
    }
}

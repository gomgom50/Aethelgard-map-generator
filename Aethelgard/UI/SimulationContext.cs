using System;
using Aethelgard.Rendering;
using Aethelgard.Simulation;
using Aethelgard.Simulation.Core;

namespace Aethelgard.UI
{
    /// <summary>
    /// Holds the shared state of the simulation application.
    /// Allows UI panels to access and modify the WorldMap, Renderer, and Pipeline.
    /// </summary>
    public class SimulationContext
    {
        public WorldMap Map { get; set; } = null!;
        public MapRenderer Renderer { get; set; } = null!;
        public PipelineOrchestrator Orchestrator { get; set; } = null!;
        public ConstraintManager Constraints { get; set; } = null!;

        public int Seed { get; set; } = 12345;
        public int Resolution { get; set; } = 50;

        // Settings that persist across regenerations
        public int RenderWidth { get; set; } = 10800;
        public int RenderHeight { get; set; } = 5600;

        public void RegenerateWorld()
        {
            // Dispose old resources
            Renderer?.Dispose();

            // Create new Map
            Map = new WorldMap(Resolution, Seed);
            Constraints = new ConstraintManager(Map.Topology.TileCount);

            // Re-initialize Renderer
            Renderer = new MapRenderer();
            Renderer.Initialize(RenderWidth, RenderHeight, Map);

            // Re-initialize Pipeline
            // Ideally PipelineOrchestrator should just update its reference, 
            // but for now we can create a new one or update it if we add a SetMap method.
            // Let's assume we update the Orchestrator's map reference.
            Orchestrator.UpdateMap(Map, Constraints);
        }
    }
}

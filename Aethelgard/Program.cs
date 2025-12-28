using System.Numerics;
using Raylib_cs;
using ImGuiNET;
using rlImGui_cs;
using Aethelgard.Simulation;
using Aethelgard.Rendering;
using Aethelgard.Core;
using Aethelgard.Interaction;

namespace Aethelgard;

class Program
{
    private static Interaction.PlateGenerationSettings? _plateSettings;
    private static Interaction.SimulateTectonicsSettings? _simSettings;
    private static bool _isDrifting = false;
    private static bool _enablePhysics = false;

    static void Main(string[] args)
    {
        // 1. Initialization
        const int screenWidth = 1280;
        const int screenHeight = 720;
        const int mapSize = 512; // Increased for Phase 2

        Raylib.InitWindow(screenWidth, screenHeight, "Aethelgard World Engine");
        Raylib.SetTargetFPS(60);

        // 2. Setup ImGui
        rlImGui.Setup(true);

        // 3. Simulation & Systems
        WorldMap worldMap = new WorldMap(mapSize, mapSize);
        MapRenderer renderer = new MapRenderer();
        renderer.Initialize(mapSize, mapSize);

        // Map Viewport Logic
        Rectangle mapViewport = new Rectangle(50, 50, 600, 600);

        // Tools
        float brushStrength = 0.05f;
        int brushRadius = 5;

        // Main Loop
        while (!Raylib.WindowShouldClose())
        {
            // --- INPUT HANDLING ---
            // Note: ImGui handles its own input. blocking logic usually happens if !ImGui.GetIO().WantCaptureMouse
            if (!ImGui.GetIO().WantCaptureMouse)
            {
                if (Raylib.IsMouseButtonDown(MouseButton.Left))
                {
                    Vector2 mousePos = Raylib.GetMousePosition();

                    // Check intersection with map viewport
                    if (Raylib.CheckCollisionPointRec(mousePos, mapViewport))
                    {
                        // Convert screen space to map space
                        // Normalize 0..1 then map to 0..mapSize
                        float normX = (mousePos.X - mapViewport.X) / mapViewport.Width;
                        float normY = (mousePos.Y - mapViewport.Y) / mapViewport.Height;

                        int mapX = (int)(normX * worldMap.Width);
                        int mapY = (int)(normY * worldMap.Height);

                        var cmd = new HeightmapBrushCommand(worldMap, mapX, mapY, brushStrength, brushRadius);
                        CommandManager.Instance.ExecuteCommand(cmd);

                        // Mark renderer as dirty? For now just update every frame
                    }
                }
            }

            // Undo
            if (Raylib.IsKeyPressed(KeyboardKey.Z)) // Simple shortcut
            {
                CommandManager.Instance.Undo();
            }

            // --- RENDER UPDATE ---
            renderer.Update(worldMap);

            // --- DRAWING ---
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.DarkGray);

            // Draw Map
            renderer.Draw((int)mapViewport.X, (int)mapViewport.Y, (int)mapViewport.Width, (int)mapViewport.Height);
            Raylib.DrawRectangleLinesEx(mapViewport, 2, Color.White); // Border

            // 5. UI Layer (ImGui)
            rlImGui.Begin();

            ImGui.Begin("Simulation Controller");
            ImGui.Text($"Map Size: {worldMap.Width}x{worldMap.Height}");

            ImGui.Separator();
            ImGui.Text("Brush Settings");
            ImGui.SliderFloat("Strength", ref brushStrength, 0.001f, 0.5f);
            ImGui.SliderInt("Radius", ref brushRadius, 1, 50);

            if (ImGui.Button("Reset Map"))
            {
                // Helper to clear map, maybe add a Command for this later
                worldMap.Elevation.Fill(0.0f);
            }

            ImGui.Separator();
            if (ImGui.Button("Undo (Z)"))
            {
                CommandManager.Instance.Undo();
            }

            ImGui.End();

            ImGui.Begin("Tectonic Controls");

            if (_plateSettings == null) _plateSettings = new PlateGenerationSettings();

            ImGui.Text("Generation Parameters");

            // Mode Selector
            string[] modes = Enum.GetNames(typeof(GenerationMode));
            int currentMode = (int)_plateSettings.Mode;
            if (ImGui.Combo("Mode", ref currentMode, modes, modes.Length))
            {
                _plateSettings.Mode = (GenerationMode)currentMode;
            }

            ImGui.SliderInt("Target Plates", ref _plateSettings.TargetPlateCount, 5, 200);
            ImGui.SliderInt("Micro Factor", ref _plateSettings.MicroPlateFactor, 1, 20);
            ImGui.SliderFloat("Weight Var", ref _plateSettings.WeightVariance, 0.0f, 2.0f);

            ImGui.Separator();
            ImGui.Text("Distortion (FBM)");
            ImGui.SliderFloat("Scale", ref _plateSettings.DistortionScale, 0.001f, 0.05f);
            ImGui.SliderFloat("Strength", ref _plateSettings.DistortionStrength, 0.0f, 100.0f);

            ImGui.Separator();
            ImGui.Checkbox("Random Seed", ref _plateSettings.UseRandomSeed);
            if (!_plateSettings.UseRandomSeed)
            {
                ImGui.InputInt("Seed", ref _plateSettings.Seed);
            }
            else
            {
                ImGui.Text($"Last Seed: {_plateSettings.Seed}");
            }

            if (ImGui.Button("Generate Plates"))
            {
                var cmd = new GeneratePlatesCommand(worldMap, _plateSettings);
                CommandManager.Instance.ExecuteCommand(cmd);
                renderer.CurrentMode = MapRenderer.RenderMode.Elevation; // Auto-switch to see continents
            }

            ImGui.Separator();
            ImGui.Text("Crust Settings");
            ImGui.SliderFloat("Percent Land", ref _plateSettings.ContinentalRatio, 0.1f, 0.9f);
            ImGui.SliderFloat("Ocean Depth", ref _plateSettings.OceanicLevel, -2.0f, -0.1f);

            ImGui.Separator();
            ImGui.Text("View Mode");
            if (ImGui.RadioButton("Elevation", renderer.CurrentMode == MapRenderer.RenderMode.Elevation))
                renderer.CurrentMode = MapRenderer.RenderMode.Elevation;

            if (ImGui.RadioButton("Plates", renderer.CurrentMode == MapRenderer.RenderMode.Plates))
                renderer.CurrentMode = MapRenderer.RenderMode.Plates;

            if (ImGui.RadioButton("Feature Types (Debug)", renderer.CurrentMode == MapRenderer.RenderMode.FeatureTypes))
                renderer.CurrentMode = MapRenderer.RenderMode.FeatureTypes;

            ImGui.End();

            // ImGui.End(); // Removed duplicate

            // --- Tectonics & Dynamics Panel ---
            ImGui.Begin("Plate Dynamics");

            if (_simSettings == null) _simSettings = new Interaction.SimulateTectonicsSettings();

            ImGui.Separator();
            ImGui.Separator();
            ImGui.Text("Kinematic Simulation (Drift)");
            ImGui.SliderFloat("Drift Speed", ref _simSettings.DriftSpeed, 0.1f, 5.0f);

            // Realtime Toggle
            if (_isDrifting)
            {
                if (ImGui.Button("Stop Drift")) _isDrifting = false;
            }
            else
            {
                if (ImGui.Button("Play Drift")) _isDrifting = true;
            }

            ImGui.Checkbox("Enable Tectonics (Realtime Physics)", ref _enablePhysics);

            // Manual Step
            if (ImGui.Button("Step (1 Frame)"))
            {
                var cmdDrift = new DriftPlatesCommand(worldMap, 1, _simSettings.DriftSpeed);
                cmdDrift.Execute();

                if (_enablePhysics)
                {
                    var cmdTec = new SimulateTectonicsCommand(worldMap, _simSettings);
                    cmdTec.Execute();
                }
                renderer.CurrentMode = MapRenderer.RenderMode.Plates;
            }

            // Logic needed in Main Loop
            if (_isDrifting)
            {
                // 1. Move
                var cmdDrift = new DriftPlatesCommand(worldMap, 1, _simSettings.DriftSpeed);
                cmdDrift.Execute(); // Direct execution

                // 2. Orogeny (Physics)
                if (_enablePhysics)
                {
                    var cmdTec = new SimulateTectonicsCommand(worldMap, _simSettings);
                    cmdTec.Execute(); // Direct execution
                }

                // renderer.CurrentMode = MapRenderer.RenderMode.Plates; // Optional: Force view
            }

            ImGui.Separator();
            ImGui.Text("Plate Dynamics (Vertical)");
            ImGui.Text("Simulation Parameters");
            ImGui.SliderFloat("Uplift", ref _simSettings.UpliftStrength, 0.01f, 1.0f);
            ImGui.SliderFloat("Width (Blur)", ref _simSettings.CollisionWidth, 1.0f, 20.0f);

            if (ImGui.Button("Simulate Tectonics"))
            {
                var cmd = new SimulateTectonicsCommand(worldMap, _simSettings);
                CommandManager.Instance.ExecuteCommand(cmd);
                renderer.CurrentMode = MapRenderer.RenderMode.Elevation; // Switch view to see results
            }

            ImGui.Separator();
            ImGui.Text("Generation Settings");

            if (ImGui.CollapsingHeader("Base Terrain"))
            {
                ImGui.SliderFloat("Coast Slope", ref _simSettings.CoastSlopeStrength, 0.0f, 0.5f);
                ImGui.SliderFloat("Regional Noise", ref _simSettings.RegionalNoiseStrength, 0.1f, 1.0f);
                ImGui.SliderFloat("Local Noise", ref _simSettings.LocalNoiseStrength, 0.0f, 0.3f);
            }

            if (ImGui.CollapsingHeader("Fossil Boundaries"))
            {
                ImGui.SliderInt("Sub-Regions Min", ref _simSettings.FossilSubRegionsMin, 1, 10);
                ImGui.SliderInt("Sub-Regions Max", ref _simSettings.FossilSubRegionsMax, 2, 15);
                ImGui.SliderFloat("Noise Offset", ref _simSettings.FossilNoiseOffset, 5f, 80f);
                ImGui.SliderFloat("Mountain Strength", ref _simSettings.FossilMountainStrength, 0.1f, 1.0f);
            }

            if (ImGui.CollapsingHeader("Hotspots"))
            {
                ImGui.SliderInt("Hotspots Min", ref _simSettings.HotspotsMin, 0, 20);
                ImGui.SliderInt("Hotspots Max", ref _simSettings.HotspotsMax, 1, 30);
                ImGui.SliderFloat("Peak Min", ref _simSettings.HotspotPeakMin, 0.2f, 1.5f);
                ImGui.SliderFloat("Peak Max", ref _simSettings.HotspotPeakMax, 0.5f, 3.0f);
            }

            if (ImGui.CollapsingHeader("Biogenic Features"))
            {
                ImGui.SliderInt("Zones Min", ref _simSettings.BiogenicZonesMin, 0, 20);
                ImGui.SliderInt("Zones Max", ref _simSettings.BiogenicZonesMax, 1, 30);
                ImGui.SliderFloat("Target Elevation", ref _simSettings.BiogenicElevation, 0.02f, 0.2f);
            }

            // Helper to visualize velocities?
            // if (ImGui.Button("Randomize Velocities")) ... (Already done in generation)

            ImGui.End();

            rlImGui.End();

            Raylib.EndDrawing();
        }

        // Cleanup
        renderer.Dispose();
        rlImGui.Shutdown();
        Raylib.CloseWindow();
    }
}
using Raylib_cs;
using ImGuiNET;
using rlImGui_cs; // Wherever you put rlImGui

namespace Aethelgard;

class Program
{
    static void Main(string[] args)
    {
        // 1. Initialization
        const int screenWidth = 1280;
        const int screenHeight = 720;

        Raylib.InitWindow(screenWidth, screenHeight, "Aethelgard World Engine");
        Raylib.SetTargetFPS(60);

        // 2. Setup ImGui
        rlImGui.Setup(true); 

        // 3. Simulation Variables (The "Data")
        float seaLevel = 0.5f;
        bool runErosion = false;

        // Main Loop
        while (!Raylib.WindowShouldClose())
        {
            // Update Logic here
            // e.g., if (runErosion) MyErosionSystem.Update();

            // 4. Rendering
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.DarkGray);

            // Draw a placeholder map (A simple rectangle for now)
            Raylib.DrawRectangle(100, 100, 1080, 520, Color.Blue);
            Raylib.DrawText("MAP VIEWPORT", 500, 300, 20, Color.White);

            // 5. UI Layer (ImGui)
            rlImGui.Begin();
            
            ImGui.Begin("Simulation Controller");
            ImGui.Text("World Parameters");
            ImGui.SliderFloat("Sea Level", ref seaLevel, 0.0f, 1.0f);
            ImGui.Checkbox("Run Erosion", ref runErosion);
            
            if (ImGui.Button("Generate New Plates"))
            {
                // Trigger your Tectonic logic
            }
            ImGui.End();

            rlImGui.End();

            Raylib.EndDrawing();
        }

        // Cleanup
        rlImGui.Shutdown();
        Raylib.CloseWindow();
    }
}
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Aethelgard.Simulation.Core;
using Aethelgard.UI.Panels;

namespace Aethelgard.UI
{
    /// <summary>
    /// Manages the main UI loop and windowing.
    /// Delegates specific UI sections to panels.
    /// </summary>
    public class UIManager
    {
        private readonly SimulationContext _context;

        // Panels
        private readonly PipelinePanel _pipelinePanel;
        private readonly GenerationPanel _generationPanel;
        private readonly GeneralSettingsPanel _generalSettingsPanel;
        private readonly SubtileSettingsPanel _subtileSettingsPanel;
        private readonly VisualizationPanel _visualizationPanel;

        public UIManager(SimulationContext context, Simulation.Stages.TectonicsStage tectonicsStage)
        {
            _context = context;

            // Initialize Panels
            _pipelinePanel = new PipelinePanel(context.Orchestrator);
            _generationPanel = new GenerationPanel(tectonicsStage);
            _generalSettingsPanel = new GeneralSettingsPanel(context);
            _subtileSettingsPanel = new SubtileSettingsPanel(context);
            _visualizationPanel = new VisualizationPanel(context);
        }

        // Public accessor for Program.cs logic that might need it (e.g. border rendering)
        public bool ShowSubtileBorders => _subtileSettingsPanel.ShowBorders;

        public void DrawUI(float zoom, Vector2 panOffset, string statusMessage)
        {
            rlImGui.Begin();

            // Main Window
            ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(350, 700), ImGuiCond.FirstUseEver);

            ImGui.Begin("Aethelgard World Engine");

            ImGui.TextColored(new Vector4(0.4f, 1f, 0.6f, 1), statusMessage);
            ImGui.Separator();

            // Background updates
            _subtileSettingsPanel.Update();

            // 1. General Settings (Seed, Res)
            if (ImGui.CollapsingHeader("General Settings", ImGuiTreeNodeFlags.DefaultOpen))
            {
                _generalSettingsPanel.Draw();
            }

            // 2. Visualization
            if (ImGui.CollapsingHeader("Visualization", ImGuiTreeNodeFlags.DefaultOpen))
            {
                _visualizationPanel.Draw();
            }

            // 3. Pipeline Control
            if (ImGui.CollapsingHeader("Pipeline Status", ImGuiTreeNodeFlags.DefaultOpen))
            {
                _pipelinePanel.Draw();
            }

            // 4. Generation Config (Tectonics)
            if (ImGui.CollapsingHeader("Tectonics Config", ImGuiTreeNodeFlags.DefaultOpen))
            {
                _generationPanel.Draw();
            }

            // 5. Subtile System
            if (ImGui.CollapsingHeader("Subtile System"))
            {
                _subtileSettingsPanel.Draw(zoom);
            }

            // Camera Info
            ImGui.Separator();
            ImGui.Text("Camera");
            ImGui.Text($"Zoom: {zoom:F1}x");
            ImGui.Text($"Pan: {panOffset.X:F0}, {panOffset.Y:F0}");

            ImGui.End();

            rlImGui.End();
        }
    }
}

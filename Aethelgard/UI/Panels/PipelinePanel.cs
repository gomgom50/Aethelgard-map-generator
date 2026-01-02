using System.Numerics;
using ImGuiNET;
using Aethelgard.Simulation.Core;

namespace Aethelgard.UI.Panels
{
    public class PipelinePanel
    {
        private readonly PipelineOrchestrator _orchestrator;

        public PipelinePanel(PipelineOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        public void Draw()
        {
            ImGui.Text($"Status: {_orchestrator.GlobalStatus}");

            // Controls
            // Check running state ONCE at start of frame to ensure consistency for Begin/End Disabled
            bool isRunning = _orchestrator.IsRunning;

            if (isRunning)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Reset"))
            {
                _orchestrator.Reset();
            }
            ImGui.SameLine();

            if (ImGui.Button("Step >"))
            {
                _ = _orchestrator.Step();
            }
            ImGui.SameLine();

            if (ImGui.Button("Run All >>"))
            {
                _ = _orchestrator.RunAll();
            }

            if (isRunning)
            {
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.Text("Running...");
            }

            ImGui.Separator();
            ImGui.Text("Stages:");

            // Draw Stage List
            for (int i = 0; i < _orchestrator.Stages.Count; i++)
            {
                var stage = _orchestrator.Stages[i];
                bool isCurrent = i == _orchestrator.CurrentStageIndex;
                bool isPast = i < _orchestrator.CurrentStageIndex;

                // Color coding
                Vector4 color;
                if (isPast) color = new Vector4(0.4f, 1.0f, 0.4f, 1.0f); // Green (Done)
                else if (isCurrent) color = new Vector4(1.0f, 1.0f, 0.4f, 1.0f); // Yellow (Current)
                else color = new Vector4(0.6f, 0.6f, 0.6f, 1.0f); // Grey (Pending)

                ImGui.PushStyleColor(ImGuiCol.Text, color);

                // Selectable item to show focus?
                string prefix = isCurrent ? "-> " : isPast ? "[Ok] " : "[  ] ";

                if (ImGui.Selectable($"{prefix}{stage.Name}", isCurrent))
                {
                    // Potential click interaction
                }

                ImGui.PopStyleColor();

                // Detailed status if current or recently run
                if (isCurrent || isPast)
                {
                    ImGui.Indent();
                    ImGui.ProgressBar(stage.Progress, new Vector2(ImGui.GetContentRegionAvail().X, 0), $"{stage.Progress * 100:F0}%");
                    ImGui.TextDisabled(stage.Status);
                    ImGui.Unindent();
                }
            }
        }
    }
}

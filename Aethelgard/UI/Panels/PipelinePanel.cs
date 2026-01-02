using System.Numerics;
using ImGuiNET;
using Aethelgard.Simulation.Core;
using Aethelgard.Simulation.Stages;

namespace Aethelgard.UI.Panels
{
    public class PipelinePanel
    {
        private readonly PipelineOrchestrator _orchestrator;
        private int _selectedStageIndex = 0;

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
                bool isSelected = (i == _selectedStageIndex);
                string prefix = isCurrent ? "-> " : isPast ? "[Ok] " : "[  ] ";

                if (ImGui.Selectable($"{prefix}{stage.Name}", isSelected))
                {
                    _selectedStageIndex = i;
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

            ImGui.Separator();
            DrawStageConfig();
        }

        private void DrawStageConfig()
        {
            if (_selectedStageIndex < 0 || _selectedStageIndex >= _orchestrator.Stages.Count) return;

            var stage = _orchestrator.Stages[_selectedStageIndex];
            ImGui.Text($"Configuration: {stage.Name}");
            ImGui.SameLine();
            ImGui.TextDisabled("(Settings match Gleba spec by default)");
            ImGui.Separator();

            if (stage is TectonicsStage ts)
            {
                // Basic
                int pc = ts.PlateCount;
                if (ImGui.SliderInt("Plate Count", ref pc, 2, 50)) ts.PlateCount = pc;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Number of major tectonic plates.");

                float cr = ts.ContinentalRatio;
                if (ImGui.SliderFloat("Continental Ratio", ref cr, 0.0f, 1.0f)) ts.ContinentalRatio = cr;



                int mpp = 3; // Fixed
                ImGui.Text($"Microplates: {mpp}");

                ImGui.Spacing();
                ImGui.Text("Noise Settings");
                float ns = ts.NoiseStrength;
                if (ImGui.SliderFloat("Noise Strength (Global)", ref ns, 0.0f, 2.0f)) ts.NoiseStrength = ns;

                float nas = ts.NoiseAScale;
                if (ImGui.SliderFloat("Noise A Scale", ref nas, 0.1f, 10.0f)) ts.NoiseAScale = nas;

                float nbs = ts.NoiseBScale;
                if (ImGui.SliderFloat("Noise B Scale", ref nbs, 0.1f, 20.0f)) ts.NoiseBScale = nbs;

                float nw = ts.NoiseWarping;
                if (ImGui.SliderFloat("Noise Warping", ref nw, 0.0f, 5.0f)) ts.NoiseWarping = nw;

                ImGui.Spacing();
                ImGui.Text("Advanced Simulation Parameters");

                float bvt = ts.BoundaryVotingThreshold;
                if (ImGui.SliderFloat("Voting Threshold", ref bvt, 0.1f, 0.9f)) ts.BoundaryVotingThreshold = bvt;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Percentage of neighbor votes needed to classify a boundary (Default: 0.525).");

                float cas = ts.CrustAgeSpread;
                if (ImGui.SliderFloat("Crust Age Spread", ref cas, 0.1f, 10.0f)) ts.CrustAgeSpread = cas;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("How fast crust ages with distance from divergent boundary.");



                float cbr = ts.CoastalBoostRange;
                if (ImGui.DragFloat("Coastal Boost Range", ref cbr, 10f, 100f, 20000f)) ts.CoastalBoostRange = cbr;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Distance inland affected by coastal elevation boost.");

                float cbh = ts.CoastalBoostHeight;
                if (ImGui.DragFloat("Coastal Boost Height", ref cbh, 1f, 0f, 2000f)) ts.CoastalBoostHeight = cbh;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Maximum elevation added at coastlines.");
            }
        }
    }
}

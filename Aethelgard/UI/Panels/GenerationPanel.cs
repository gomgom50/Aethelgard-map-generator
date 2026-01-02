using ImGuiNET;
using Aethelgard.Simulation.Stages;

namespace Aethelgard.UI.Panels
{
    public class GenerationPanel
    {
        private readonly TectonicsStage _tectonicsStage;

        public GenerationPanel(TectonicsStage tectonicsStage)
        {
            _tectonicsStage = tectonicsStage;
        }

        public void Draw()
        {
            ImGui.Text("Tectonics (Phase 1)");

            int plateCount = _tectonicsStage.PlateCount;
            if (ImGui.SliderInt("Plate Count", ref plateCount, 4, 30)) _tectonicsStage.PlateCount = plateCount;

            float contRatio = _tectonicsStage.ContinentalRatio;
            if (ImGui.SliderFloat("Continental Ratio", ref contRatio, 0.1f, 0.9f, "%.2f")) _tectonicsStage.ContinentalRatio = contRatio;



            float noiseStr = _tectonicsStage.NoiseStrength;
            if (ImGui.SliderFloat("Noise Strength", ref noiseStr, 0.1f, 50.0f, "%.1f")) _tectonicsStage.NoiseStrength = noiseStr;

            ImGui.Indent();
            ImGui.Text("Noise Stack A (Base)");
            float nAsc = _tectonicsStage.NoiseAScale;
            if (ImGui.SliderFloat("Scale A", ref nAsc, 0.001f, 10.0f, "%.4f")) _tectonicsStage.NoiseAScale = nAsc;

            float nAp = _tectonicsStage.NoiseAPersistence;
            if (ImGui.SliderFloat("Pers A", ref nAp, 0.1f, 20.0f, "%.2f")) _tectonicsStage.NoiseAPersistence = nAp;

            float nAl = _tectonicsStage.NoiseALacunarity;
            if (ImGui.SliderFloat("Lac A", ref nAl, 1.0f, 50.0f, "%.2f")) _tectonicsStage.NoiseALacunarity = nAl;

            ImGui.Separator();

            ImGui.Text("Noise Stack B (Detail)");
            float nBsc = _tectonicsStage.NoiseBScale;
            if (ImGui.SliderFloat("Scale B", ref nBsc, 0.001f, 20.0f, "%.4f")) _tectonicsStage.NoiseBScale = nBsc;

            float nBp = _tectonicsStage.NoiseBPersistence;
            if (ImGui.SliderFloat("Pers B", ref nBp, 0.1f, 20.0f, "%.2f")) _tectonicsStage.NoiseBPersistence = nBp;

            float nBl = _tectonicsStage.NoiseBLacunarity;
            if (ImGui.SliderFloat("Lac B", ref nBl, 1.0f, 50.0f, "%.2f")) _tectonicsStage.NoiseBLacunarity = nBl;

            float nBw = _tectonicsStage.NoiseBWeight;
            if (ImGui.SliderFloat("Weight B", ref nBw, 0.0f, 1.0f, "%.2f")) _tectonicsStage.NoiseBWeight = nBw;
            ImGui.Unindent();

            ImGui.Separator();

            float microStr = 3f; // Fixed
            int mp = (int)microStr;
            ImGui.Text($"Microplates: {mp} (Auto)");
        }
    }
}

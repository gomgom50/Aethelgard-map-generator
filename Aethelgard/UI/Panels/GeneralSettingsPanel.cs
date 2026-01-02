using ImGuiNET;

namespace Aethelgard.UI.Panels
{
    public class GeneralSettingsPanel
    {
        private readonly SimulationContext _context;

        public GeneralSettingsPanel(SimulationContext context)
        {
            _context = context;
        }

        public void Draw()
        {
            ImGui.Text("World Configuration");

            bool paramsChanged = false;

            int res = _context.Resolution;
            if (ImGui.SliderInt("Resolution", ref res, 5, 50)) _context.Resolution = res;
            if (ImGui.IsItemDeactivatedAfterEdit()) paramsChanged = true;

            int seed = _context.Seed;
            if (ImGui.InputInt("Seed", ref seed)) _context.Seed = seed;
            if (ImGui.IsItemDeactivatedAfterEdit()) paramsChanged = true;

            if (ImGui.Button("Regenerate World") || paramsChanged)
            {
                _context.RegenerateWorld();
            }

            ImGui.Separator();
            if (_context.Map != null)
            {
                ImGui.Text($"Tiles: {_context.Map.Topology.TileCount:N0}");
            }
        }
    }
}

using ImGuiNET;
using Aethelgard.Rendering;
using Aethelgard.Simulation;
using System.Numerics;

namespace Aethelgard.UI.Panels
{
    public class SubtileSettingsPanel
    {
        private readonly SimulationContext _context;

        // Local state for sliders (to match Program.cs behavior)
        private float _warpStrength = 0.0020f;
        private float _noiseScale = 100.0f;
        private float _detailStrength = 100.0f;
        private int _resolution = 4;
        private bool _showBorders = false;

        // Track the map we last synced with to handle regeneration automatically
        private WorldMap? _lastSyncedMap;

        public bool ShowBorders => _showBorders; // Exposed for UIManager/Main loop if needed

        public SubtileSettingsPanel(SimulationContext context)
        {
            _context = context;
        }

        public void Draw(float currentZoom)
        {
            ImGui.Text("Subtile System (Organic Borders)");

            if (_context.Map?.Subtiles == null)
            {
                ImGui.TextDisabled("Subtile system not initialized.");
                return;
            }

            // Sync check moved to Update()

            bool changed = false;

            if (ImGui.SliderFloat("Warp Strength", ref _warpStrength, 0.0f, 0.05f, "%.4f")) { }
            if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;

            if (ImGui.SliderFloat("Noise Frequency", ref _noiseScale, 0.1f, 200.0f, "%.3f")) { }
            if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;

            if (ImGui.SliderFloat("Detail Strength", ref _detailStrength, 0.0f, 200.0f, "%.1f")) { }
            if (ImGui.IsItemDeactivatedAfterEdit()) changed = true;

            if (ImGui.SliderInt("Resolution Multiplier", ref _resolution, 2, 8)) { }
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                _context.Map.Subtiles.ResolutionMultiplier = _resolution;
                _context.Renderer.IsDirty = true;
                _context.Renderer.GeometryDirty = true;
            }

            if (changed)
            {
                _context.Map.Subtiles.UpdateConfig(_noiseScale, _warpStrength, _detailStrength);
                _context.Renderer.IsDirty = true;
                _context.Renderer.GeometryDirty = true;
            }

            ImGui.Checkbox("Show Subtile Borders", ref _showBorders);
            if (_showBorders && currentZoom < 4.0f)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "(Zoom 4x+ to see)");
            }
        }

        /// <summary>
        /// Call this every frame to ensure config is applied even if panel is hidden.
        /// Also handles re-applying config when Map is regenerated.
        /// </summary>
        public void Update()
        {
            if (_context.Map != null && _context.Map != _lastSyncedMap)
            {
                // New map detected! Instead of overwriting it with our old UI state,
                // we should READ the fresh system state into our UI.
                // This prevents redundant rebuilds on startup.
                if (_context.Map.Subtiles != null)
                {
                    var sys = _context.Map.Subtiles;
                    _noiseScale = sys.NoiseScale;
                    _warpStrength = sys.WarpStrength;
                    _detailStrength = sys.DetailNoiseStrength;
                    _resolution = sys.ResolutionMultiplier;
                }
                _lastSyncedMap = _context.Map;
            }
        }
    }
}

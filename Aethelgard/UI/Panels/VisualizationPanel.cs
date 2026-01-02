using System;
using ImGuiNET;
using Aethelgard.Rendering;

namespace Aethelgard.UI.Panels
{
    public class VisualizationPanel
    {
        private readonly SimulationContext _context;
        private string[] _modeNames;
        private int _currentModeIndex = 0;

        public VisualizationPanel(SimulationContext context)
        {
            _context = context;
            _modeNames = Enum.GetNames<MapRenderer.RenderMode>();
        }

        public void Draw()
        {
            ImGui.Text("Visualization");

            if (ImGui.Combo("Render Mode", ref _currentModeIndex, _modeNames, _modeNames.Length))
            {
                if (_context.Renderer != null)
                {
                    _context.Renderer.CurrentMode = (MapRenderer.RenderMode)_currentModeIndex;
                    _context.Renderer.IsDirty = true;
                }
            }
        }
    }
}

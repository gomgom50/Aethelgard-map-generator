using System;
using System.Collections.Generic;
using Aethelgard.Core;
using Aethelgard.Simulation;

namespace Aethelgard.Interaction
{
    /// <summary>
    /// Command to apply a circular brush to the heightmap.
    /// Stores the previous state of modified pixels for Undo.
    /// </summary>
    public class HeightmapBrushCommand : ICommand
    {
        private readonly WorldMap _map;
        private readonly int _centerX;
        private readonly int _centerY;
        private readonly float _strength;
        private readonly int _radius;

        // Undo History: Store (Index, OldValue)
        private struct PixelSnapshot
        {
            public int X;
            public int Y;
            public float OldValue;
        }
        private readonly List<PixelSnapshot> _history = new List<PixelSnapshot>();

        public HeightmapBrushCommand(WorldMap map, int x, int y, float strength, int radius)
        {
            _map = map;
            _centerX = x;
            _centerY = y;
            _strength = strength;
            _radius = radius;
        }

        public void Execute()
        {
            int r2 = _radius * _radius;

            for (int y = -_radius; y <= _radius; y++)
            {
                for (int x = -_radius; x <= _radius; x++)
                {
                    if (x * x + y * y <= r2)
                    {
                        int targetX = _centerX + x;
                        int targetY = _centerY + y;

                        if (_map.Elevation.IsValid(targetX, targetY))
                        {
                            float currentVal = _map.Elevation.Get(targetX, targetY);

                            // Save state for undo
                            _history.Add(new PixelSnapshot { X = targetX, Y = targetY, OldValue = currentVal });

                            // Apply simplistic additive brush (smooth falloff could be added later)
                            float newVal = Math.Clamp(currentVal + _strength, 0.0f, 1.0f);
                            _map.Elevation.Set(targetX, targetY, newVal);
                        }
                    }
                }
            }
        }

        public void Undo()
        {
            // Restore backwards to prevent overwrite issues (though irrelevant for simple set)
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                var snap = _history[i];
                _map.Elevation.Set(snap.X, snap.Y, snap.OldValue);
            }
        }
    }
}

using Aethelgard.Simulation.Core;

namespace Aethelgard.Simulation.Systems.Stampers
{
    public interface IStamper
    {
        void Apply(WorldMap map, int centerTileId);
    }
}

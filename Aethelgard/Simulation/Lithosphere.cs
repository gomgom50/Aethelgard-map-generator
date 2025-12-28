using System.Collections.Generic;

namespace Aethelgard.Simulation
{
    /// <summary>
    /// Represents the Tectonic Layer of the world.
    /// Stores which plate belongs to which pixel and the plate definitions.
    /// </summary>
    public class Lithosphere
    {
        public DataGrid<int> PlateIdMap { get; private set; }
        public Dictionary<int, Plate> Plates { get; private set; }

        public Lithosphere(int width, int height)
        {
            PlateIdMap = new DataGrid<int>(width, height);
            Plates = new Dictionary<int, Plate>();

            // Initialize with ID 0 (Void/No Plate)
            PlateIdMap.Fill(0);
        }

        public void RegisterPlate(Plate plate)
        {
            if (!Plates.ContainsKey(plate.Id))
            {
                Plates[plate.Id] = plate;
            }
        }

        public Plate? GetPlate(int id)
        {
            if (Plates.TryGetValue(id, out var plate))
            {
                return plate;
            }
            return null;
        }
    }
}

namespace Aethelgard.Simulation
{
    /// <summary>
    /// The container for all world data layers.
    /// Acts as the central "Database" for the simulation.
    /// </summary>
    public class WorldMap
    {
        public int Width { get; private set; }
        public int Height { get; private set; }

        // Layers
        public DataGrid<float> Elevation { get; private set; }
        public DataGrid<float> CrustThickness { get; private set; }
        public Lithosphere Lithosphere { get; private set; }

        /// <summary>
        /// Debug layer: Feature type per pixel.
        /// 0=Ocean, 1=Craton, 2=ActiveBoundary, 3=FossilBoundary, 4=Hotspot, 5=Biogenic
        /// </summary>
        public DataGrid<int> FeatureType { get; private set; }

        public WorldMap(int width, int height)
        {
            Width = width;
            Height = height;

            // Initialize Layers
            Elevation = new DataGrid<float>(width, height);
            CrustThickness = new DataGrid<float>(width, height);
            Lithosphere = new Lithosphere(width, height);
            FeatureType = new DataGrid<int>(width, height);

            // Default initialization
            Elevation.Fill(-1.0f);
            CrustThickness.Fill(1.0f);
            FeatureType.Fill(0);
        }
    }
}

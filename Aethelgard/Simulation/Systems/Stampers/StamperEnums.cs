namespace Aethelgard.Simulation.Systems.Stampers
{
    public enum StamperAction
    {
        Set,
        Add,
        Subtract,
        Max,
        Min,
        Lerp
    }

    public enum StamperTarget
    {
        Elevation,
        LandFlag,
        Moisture,
        // Add other targets as needed
    }

    public enum StamperMode
    {
        Linear, // 1 - t/r
        Power,  // (1 - t/r)^p
    }
}

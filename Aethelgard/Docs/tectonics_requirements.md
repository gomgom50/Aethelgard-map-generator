# Tectonics System Requirements
**Version 1.0**

## 1. Core Philosophy: The Unified Sandbox
The system must treat **Manual Input** and **Procedural Generation** as equal citizens. The Simulation Engine must operate on the *current state of the map*, regardless of how that state was created.

## 2. Fundamental Data Structures
### 2.1. The Plate
*   **Identity**: Unique ID and Color.
*   **Type**: `Continental` (Land) vs `Oceanic` (Water).
*   **State**:
    *   `IsLocked`: Boolean. If true, the plate is an immovable "Anchor" (Craton). It does not move during drift simulation unless explicitly unlocked.
    *   `Velocity`: Vector2. Determines speed and direction of drift.

## 3. Workflows
The system must support the following discrete workflows without code changes:
*   **Bottom-Up (Procedural)**: User clicks "Generate", system creates random plates -> User simulates.
*   **Top-Down (Manual)**: User paints specific terrain shapes -> System generates plates to match -> User simulates.
*   **Hybrid**: User paints *some* plates, locks them, and lets the system generate the rest around them.
*   **User created plates**: User paints all or some plates, locks them, and lets the system generate the rest around them. then the user can simulate with partial locked, all locked, or all unlocked.

## 4. Feature Requirements

### 4.1. Manual Control (Painting)
*   **Paint Terrain**: User can paint Elevation (Land/Sea).
*   **Paint Plates**: User can paint specific Plate IDs directly onto the map.
*   **Pinning**: User can toggle the `IsLocked` state of any plate. This should be a per-plate setting, not a global setting. locked plates should have cross-hatching in the UI to indicate they are locked.

### 4.2. Generation Generators
*   **Random Mode**: Standard Voronoi/Noise generation.
*   **From Elevation Mode (Inverse)**:
    *   Input: Current Elevation Map.
    *   Process: Identify connected "Land" components.
    *   Output: Create a unique `Continental Plate` for each land component. Create `Oceanic Plates` to fill the remaining voids.

### 4.3. Kinematic Simulation (Drift)
*   **Advection**: Move `UnLocked` plates according to their velocity.
*   **Collision**: Detect when plates overlap or encroach.
*   **Rifting (Magma Generation)**: If plates move apart, dynamically spawn new `Oceanic` crust (seeds) to fill the gap.
*   **Subduction (Destruction)**: If plates overlap significantly, destroy the weaker (Oceanic) crust.

### 4.4. Vertical Simulation (Orogeny)
*   **Edge-Scanning**: Detect Plate Boundaries.
*   **Physics**: Calculate Pressure (Convergence = Uplift, Divergence = Rift).
*   **Modification**: Apply these height changes to the global Elevation Map.

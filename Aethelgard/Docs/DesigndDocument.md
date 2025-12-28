# Design Document: Project "Aethelgard"
**High-Fidelity Procedural World Synthesis Engine**

---

## 1. Executive Summary
**Project Aethelgard** is a world-building tool designed to bridge the gap between artistic fantasy mapping and scientific planetary simulation. Unlike traditional noise-based generators, Aethelgard utilizes a layered simulation approach—Tectonics, Atmospheric Circulation, and Hydraulic Erosion—to create realistic, high-fidelity 2D maps.

The goal is to provide a "God-tool" where users can paint tectonic plates and wind patterns as easily as they paint terrain, allowing the simulation to handle the complex interplay of rain shadows, river basins, and biome distribution.

---

## 2. Core Philosophy
*   **Process over Noise:** Realism comes from the *history* of the land, not just Perlin noise.
*   **Art-Directable Science:** Users should be able to override any layer (e.g., "I want a mountain here") and let the simulation adapt (e.g., "Then the land behind it becomes a desert").
*   **Iterative Workflow:** The simulation is not a one-click button but an iterative process where the user "bakes" layers of the world sequentially.

---

## 3. Technical Stack
*   **Language:** C# (.NET 8.0+)
*   **Rendering/Windowing:** **Raylib-cs** (OpenGL abstraction)
*   **User Interface:** **ImGui.NET** (Immediate mode UI for sliders, layering, and debugging)
*   **Data Structure:** Flattened 1D arrays (for cache-friendly performance) mapped to 2D coordinate systems.
*   **Performance:** Heavy use of **Parallel.For** for CPU-based simulation and **GLSL Shaders** for real-time visual stylization.

---

## 4. System Architecture: The Layered Data Model
The world is stored as a series of specialized data grids (textures/arrays). Every pixel on the map contains multiple data points:

1.  **Lithospheric Layer:** Plate ID, Crust Density, Crust Thickness.
2.  **Topographic Layer:** Elevation, Slope, Flow Accumulation.
3.  **Atmospheric Layer:** Air Pressure, Wind Vector (X, Y), Humidity.
4.  **Hydrosphere Layer:** Water Depth, Salinity, Current Vector.
5.  **Biosphere Layer:** Temperature, Annual Precipitation, Biome Type.

---

## 5. Module Breakdown

### Phase I: The Geosphere (Tectonics & Orogeny)
Instead of simulating millions of years of drift, Aethelgard uses **Kinematic Plate Tectonics**.
*   **Plate Generation:** The map is divided into Voronoi cells representing tectonic plates.
*   **Plate Properties:** Each plate is assigned a **Linear Velocity Vector** and an **Angular Velocity**.
*   **Boundary Interactions:** At every pixel where two plates meet, the simulation calculates the relative motion:
    *   **Convergent (Head-on):** Increases elevation (Mountain building).
    *   **Divergent (Pulling apart):** Decreases elevation (Rifting/Oceans).
    *   **Transform (Sliding):** Adds "noise" and minor ridges (Fault lines).
*   **Tooling:** A "Plate Brush" allows users to draw plates, assign "Crustal Density" (Oceanic vs. Continental), and manually set drift directions.

### Phase II: The Atmosphere (Wind & Heat)
Aethelgard uses a simplified **Three-Cell Model** (Hadley, Ferrel, and Polar cells).
*   **Insolation Map:** Calculates heat based on latitude and the planet’s axial tilt.
*   **Pressure Gradients:** High pressure at poles and 30° latitude; Low pressure at equator and 60°.
*   **Coriolis Effect:** Deflects wind vectors based on the planet's rotation direction.
*   **The Flux Algorithm:** Wind "picks up" moisture when over Ocean pixels (based on water temperature) and "drops" it when moving to higher elevations (Orographic Lift) or when air masses cool.

### Phase III: The Hydrosphere (Erosion & Rivers)
This is the "Fidelity Layer" that makes maps look realistic.
*   **Hydraulic Erosion:** A "Particle Droplet" simulation. Thousands of droplets "rain" on the heightmap.
    1.  Droplet picks up sediment based on speed and slope.
    2.  Droplet carves the terrain (lowering elevation).
    3.  Droplet deposits sediment when it slows down (filling pits).
*   **River Synthesis:** Rivers are not drawn; they emerge from **Flow Accumulation**. Any pixel that receives "runoff" from many uphill pixels becomes a river.
*   **Lake Filling:** An "A* Water-Level" algorithm fills depressions (sinks) until they reach a "spill point," connecting them to the sea.

### Phase IV: The Biosphere (Biomes)
Using the **Whittaker Classification System**, the program overlays the Temperature Map and the Precipitation Map.
*   **Dynamic Biomes:** If the user moves a mountain range, the rain shadow moves, and the forest automatically turns into a steppe or desert in the UI.
*   **Climate Logic:** Includes variables for seasonality (summer/winter shifts based on tilt).

---

## 6. Stylization & Rendering Engine
The goal is to export a map that looks like an illustration, not a data heightmap.
*   **Hillshading (Bump Mapping):** A GLSL shader calculates shadows based on the heightmap and a "Sun Position" slider.
*   **The "Paper" Filter:** Multi-layered textures providing parchment grain, coffee stains, and aged edges.
*   **Icon Instancing:** Instead of "Green Pixels," the engine uses Raylib to draw thousands of small, hand-drawn-style tree icons in areas designated as "Forest" by the simulation.
*   **Vector Coastlines:** An edge-detection pass (Marching Squares) creates clean, sharp lines for coasts and borders.

---

## 7. User Interaction Design
*   **The Iterative Cycle:**
    1.  **Define:** Set Planet Radius, Tilt, and Rotation Speed.
    2.  **Sketch:** Paint the Plate layout and set their vectors.
    3.  **Simulate:** Run the "Tectonic Bake" to get the base mountains.
    4.  **Refine:** Use the "Height Brush" to fix specific landmasses.
    5.  **Weather:** Run the "Climate Bake" to generate wind and rain.
    6.  **Erode:** Run the "Hydraulic Pass" to carve the rivers and valleys.
    7.  **Style:** Choose a visual theme (Fantasy, Atlas, Satellite, Political).

---

## 8. Development Roadmap

### Phase 1: The Foundation (Month 1-2)
*   Implement Raylib-cs windowing and ImGui integration.
*   Create the 2D Data Grid system and the basic "Heightmap Painter."
*   Build a basic shader for Topographical Shading (Hypsometric Tints).

### Phase 2: Tectonic Logic (Month 3-4)
*   Implement Voronoi-based plate generation.
*   Create the Plate Interaction math (Convergent/Divergent logic).
*   Allow "Real-time" plate vector adjustment with visual feedback.

### Phase 3: Weather & Erosion (Month 5-6)
*   Implement the Hadley Cell wind simulation.
*   Develop the Hydraulic Erosion "Droplet" system (Threaded for performance).
*   Create the River Flow Accumulation logic.

### Phase 4: Aesthetics & Export (Month 7+)
*   Implement the Biome classification.
*   Build the Stylization Engine (Parchment shaders, symbol instancing).
*   High-resolution PNG/SVG export.

---

## 9. Key Technical Challenges
*   **Spherical Distortion:** Since the map is a 2D grid, logic at the "poles" must account for the fact that the top row of pixels is actually a single point.
*   **Performance:** Hydraulic erosion on a 4K map is computationally expensive. Solution: Move the erosion simulation to a **Compute Shader** if CPU Parallelism is insufficient.
*   **Stability:** Ensuring rivers don't get stuck in infinite loops in flat areas (requires a "Pit Filling" algorithm).
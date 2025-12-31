# Procedural World Generator - Technical Design Document

## Executive Summary

This document outlines the design for a **hybrid procedural-manual world generation system** that combines algorithmic generation with artist control. The core philosophy is **"lock, unlock, and regenerate"** - allowing creators to paint constraints at any level of detail, then let the generator fill in the rest while respecting those constraints.

**Key Innovation**: Unlike traditional generators that produce fixed outputs, this system treats generation as an **iterative, visual, constraint-based process** where human creativity and algorithmic generation work together seamlessly.

---

## Table of Contents

1. [Core Design Philosophy](#core-design-philosophy)
2. [Lock System Architecture](#lock-system-architecture)
3. [Pipeline & Visualization](#pipeline-visualization)
4. [Constraint Modes by Feature](#constraint-modes-by-feature)
5. [Technical Architecture](#technical-architecture)
6. [Feature Extension System](#feature-extension-system)
7. [Map Visualization Modes](#map-visualization-modes)
8. [Future Integration: Canvas of Kings](#future-canvas-of-kings)
9. [Implementation Roadmap](#implementation-roadmap)

---

## Core Design Philosophy

### The Three Pillars

#### 1. **Constraint-Based Generation**
The generator always respects user-defined constraints:
- Painted plates become immutable seeds for flood fill algorithms
- Painted rivers define required flow paths that terrain must accommodate
- Painted elevations create boundary conditions for terrain generation
- Constraints propagate through dependent stages (e.g., locked plate affects boundary features)

#### 2. **Lock Levels**
Every feature supports three lock states:

| Lock State | Description | Regeneration Behavior |
|------------|-------------|----------------------|
| **Fully Locked** | User-painted, exact values | Never modified by generator, becomes hard constraint |
| **Partially Locked** | User-painted with flexibility | Generator adds natural variation within bounds |
| **Fully Generated** | No user input | Generator creates from scratch using algorithms |

#### 3. **Visual, Iterative Process**
Generation is not a black box:
- Real-time visualization of algorithm progress (tile-by-tile updates)
- Step-by-step execution with pause/resume capability
- Undo/redo any stage independently
- Selectively regenerate individual stages without affecting others
- Speed control from real-time observation to maximum performance

---

## Lock System Architecture

### Tile-Level Lock Flags

Each tile maintains lock state for different properties:

**Property Categories**:
- **Plate Assignment** - Which tectonic plate owns this tile
- **Elevation** - Height above/below sea level
- **River Data** - Source points, flow paths, accumulation
- **Features** - Mountains, volcanoes, canyons, etc.
- **Biome** - Climate classification and vegetation
- **Geology** - Rock types and soil composition

**Lock Granularity**:
- Individual tiles can be locked
- Regions can be locked as groups
- Features can be locked (affects all tiles in feature)
- Stages can be locked (skip entire stage if satisfied)

### Lock Inheritance and Propagation

**Dependency Chain**:
When locks exist, they propagate through dependent stages:

```
Locked Plate → Affects plate boundaries → Affects orogenies → Affects elevation
Locked Elevation → Affects waterflow → Affects rivers → Affects biomes
Locked River Source → Affects river path → Affects erosion → Affects soil
```

**Conflict Resolution**:
When locks create impossible conditions:
- System detects conflicts (e.g., river can't flow uphill with locked elevations)
- Presents options: Adjust locks, carve terrain, or skip generation
- Visual indicators show conflict locations
- Allows manual intervention before continuing

---

## Pipeline & Visualization

### Interactive Pipeline Interface

**Real-Time Progress Display**:
```
═══════════════════════════════════════════════════════════════
  WORLD GENERATION PIPELINE
═══════════════════════════════════════════════════════════════
  Status: Running Stage 8 of 15
  Current: [Calculate Plate Boundaries]
  [████████████████████░░░] 83% (22,410 / 27,000 tiles)
  
  [ ▶ Step ]  [ ⏩ Run All ]  [ ⏸ Pause ]  [ ↩ Redo Stage ]  [ ⏹ Cancel ]
  
  Speed: [■■■□□] 1.0x (Real-Time)   |   Visualization: ON
  
  ───────────────────────────────────────────────────────────────
  STAGE PIPELINE:
  
  ☑ PHASE 0: FOUNDATION
    [✓] 1. Reset World Data                      (0.02s)
    [✓] 2. Initialize Topology                   (0.15s)
    [✓] 3. Apply User Constraints                (0.01s)
  
  ☑ PHASE 1: TECTONICS
    [✓] 4. Seed Microplates                      (0.05s) [2 locked]
    [✓] 5. Microplate Flood Fill                 (1.2s)
    [✓] 6. Build Adjacency Graph                 (0.3s)
    [✓] 7. Agglomerate Major Plates              (0.8s) [1 locked]
    [▶] 8. Calculate Plate Boundaries   ← CURRENT (0.4s...)
    [ ] 9. Classify Boundary Types
    [ ] 10. Assign Plate Velocities              [3 locked]
    [ ] 11. Generate Crust Age
  
  □ PHASE 2: TERRAIN FEATURES
    [ ] 12. Boundary Features (Mountains/Rifts)  [12 peaks locked]
    [ ] 13. Generate Hotspots
    [ ] 14. Generate Volcanism                   [5 volcanoes locked]
    [ ] 15. Ancient Features
  
  □ PHASE 3: HYDROLOGY  [Collapsed - Click to Expand]
  □ PHASE 4: CLIMATE    [Collapsed - Click to Expand]
  □ PHASE 5: BIOMES     [Collapsed - Click to Expand]
  
  ───────────────────────────────────────────────────────────────
  LOCKED CONSTRAINTS:
    • 2 Plates (fully painted)
    • 1 Plate (partially locked - will noisify)
    • 3 Plate velocities (directional constraints)
    • 12 Mountain peaks
    • 5 Volcano locations
    • 8 River sources
    • 3 River paths (complete)
  
  ───────────────────────────────────────────────────────────────
  CURRENT STAGE DETAILS:
  
  Stage 8: Calculate Plate Boundaries
  ├─ Finding boundary tiles... [Done]
  ├─ Classifying boundary types... [83%]
  │  ├─ Convergent: 1,247 tiles
  │  ├─ Divergent: 892 tiles
  │  └─ Transform: 341 tiles
  └─ Building boundary segments... [Pending]
  
  Estimated Time Remaining: 0.3s
  
═══════════════════════════════════════════════════════════════
```

### Visualization Control System

**Update Frequency Options**:
- **Every Tile** - See algorithm work tile-by-tile (slow, maximum detail)
- **Every N Tiles** - Update display periodically (configurable N)
- **Every Second** - Time-based updates regardless of tiles processed
- **Stage Complete** - Only update when stage finishes (maximum speed)

**Visual Overlays**:
- **Locked Tiles** - Highlighted border showing user constraints
- **Partial Locks** - Different color for flexible constraints
- **Active Processing** - Animated indicator on currently processing tile
- **Algorithm Frontier** - Show expanding flood fill or search frontier
- **Score Values** - Display internal algorithm scores for debugging
- **Tile Indices** - Show tile IDs for technical debugging

**Speed Control Modes**:
- **Real-Time** (1.0x) - Human-watchable speed, update every frame
- **Fast** (10x) - Quick but still visible progress
- **Very Fast** (100x) - Blur of activity, minimal updates
- **Maximum** - No visualization, pure computation speed
- **Step-by-Step** - Manual advance, press button for each tile

---

## Constraint Modes by Feature

### 1. Tectonic Plates

**Purpose**: Allow artists to define continental layouts before generation

#### **Fully Locked Plate**
**User Action**: Paint a contiguous region with plate brush, mark as locked

**Generator Behavior**:
- Treats painted region as immutable seed in flood fill algorithm
- Generates additional plates to fill remaining space
- Respects locked boundaries exactly (no tiles reassigned)
- Uses locked plate as starting point for plate velocity assignment

**Use Case**: Creating a specific continent shape, ensuring landmass location

#### **Partially Locked Plate**
**User Action**: Paint plate region, then apply "noisify" operation

**Generator Behavior**:
- Maintains core interior tiles as locked
- Applies fractal noise to boundary tiles for natural appearance
- Adjusts boundary within tolerance range (e.g., ±5 tiles)
- Preserves overall shape while adding realism

**Use Case**: Rough continent placement that should look natural

#### **Plate Velocity Constraints**
**User Action**: Draw arrow on plate indicating desired motion direction

**Generator Behavior**:
- Locks velocity vector for that plate
- Adjusts neighboring plates' velocities to maintain reasonable physics
- Boundary types determined by locked velocities

**Use Case**: Ensuring specific plate boundaries (convergent mountain range location)

**UI Components**:
- Plate brush with size control
- Plate selector (create new or extend existing)
- Lock/unlock toggle
- Noisify boundary function
- Velocity arrow tool
- Statistics display (locked/partial/generated plate counts)

---

### 2. Elevation & Terrain

**Purpose**: Allow precise control over landmark elevations and terrain shapes

#### **Fully Locked Elevation**
**User Action**: Paint tiles with exact elevation values using brush

**Generator Behavior**:
- These tiles become fixed landmarks
- Interpolation algorithms work between locked points
- Erosion systems respect as hard constraints
- Surrounding tiles adjust to create natural transitions

**Use Case**: Placing specific mountain peaks, valley floors, ocean trenches

#### **Partially Locked Elevation**
**User Action**: Paint elevation range (min/max) or paint then apply "add noise"

**Generator Behavior**:
- Maintains elevation within specified bounds (e.g., ±10%)
- Adds fractal noise for natural detail
- Smooths transitions to unlocked neighbors
- Erosion can modify within bounds

**Use Case**: General plateau height without micromanaging every tile

#### **Elevation Interpolation**
**User Action**: Mark several elevation points, request "generate between"

**Generator Behavior**:
- Interpolates smooth surface connecting locked points
- Uses algorithms like Delaunay triangulation or radial basis functions
- Optionally adds noise after interpolation

**Use Case**: Sculpting large terrain shapes with minimal manual work

#### **Terrain Modification Operations**
Available tools:
- **Smooth Selection** - Average with neighbors to reduce harshness
- **Add Noise** - Apply fractal noise to add natural detail
- **Erode Selection** - Simulate erosion on selected region
- **Carve Valley** - Lower elevation along painted path
- **Raise Ridge** - Increase elevation along painted path

**UI Components**:
- Elevation brush with height picker
- Brush size and strength sliders
- Mode selector (exact/range/smooth/noise)
- Preset tools (mountain peak, valley, plateau, trench)
- "Generate between points" function
- Terrain modification tool palette

---

### 3. Rivers & Hydrology

**Purpose**: Control water systems for narrative or aesthetic reasons

#### **River Source Points**
**User Action**: Click to place river source marker

**Generator Behavior**:
- Waterflow algorithm must initiate from this tile
- River pathfinding starts here and follows downhill
- If elevation is locked, source works as-is
- If elevation is unlocked, may raise elevation slightly to ensure flow

**Use Case**: Placing springs, glacier melt sources, lake outlets

#### **River Path (Full Lock)**
**User Action**: Paint complete river course from source to ocean/lake

**Generator Behavior**:
- River must flow exactly along painted path
- System carves elevation if necessary to make water flow downhill
- Surrounding terrain adjusted to create natural valley
- Other rivers avoid this path (watershed separation)

**Use Case**: Creating specific river for narrative (follows trade route, city location)

#### **River Path (Waypoints)**
**User Action**: Mark key points river must pass through, not complete path

**Generator Behavior**:
- Pathfinding algorithm finds route between waypoints
- Follows natural downhill flow where possible
- Can adjust terrain slightly to connect waypoints logically

**Use Case**: River must pass certain locations but exact path flexible

#### **"Make Natural" Operation**
**User Action**: Select painted river, apply "make natural"

**Generator Behavior**:
- Analyzes painted path for impossible flows (uphills)
- Adjusts elevation to fix flow issues
- Adds meanders and natural variation
- Maintains general course but improves realism

**River Constraint Interactions**:
- Rivers with locked sources respect locked elevations (or flag conflict)
- Locked river paths affect erosion patterns
- Lake generation must respect river inputs/outputs
- Climate system uses river locations for moisture distribution

**UI Components**:
- River source placement tool
- River path painting brush
- Waypoint marker tool
- River properties panel (flow rate, width, lock state)
- "Make natural" function
- "Carve terrain" function (adjust elevations to fit river)
- River network visualizer

---

### 4. Features (Mountains, Volcanoes, Custom)

**Purpose**: Place distinctive geological features with full control

#### **Mountain Peaks**
**User Action**: Click location, specify mountain type and size

**Generator Behavior**:
- Creates mountain using stamper system centered on clicked tile
- Generates foothills, uplift zones automatically around peak
- Respects microplate boundaries (mountain stays within microplate)
- Adjusts rock types to igneous/metamorphic as appropriate

**Parameters**:
- Peak elevation (absolute or relative to surroundings)
- Mountain radius (affects foothill extent)
- Steepness (affects slope profile)
- Style (sharp peak, dome, ridge)

#### **Volcanoes**
**User Action**: Place volcano marker, select type (shield/strato/cinder)

**Generator Behavior**:
- Creates volcanic terrain using type-specific profile
- Adjusts bedrock to volcanic rock types
- Excludes this location from random volcano generation
- May create volcanic field (multiple small vents) if specified

**Parameters**:
- Volcano type (affects shape and size)
- Activity status (active/dormant/extinct)
- Cone size
- Caldera presence

#### **Custom Features**
**User Action**: Select from feature library (canyons, archipelagos, etc.)

**Generator Behavior**:
- Executes feature-specific generation algorithm
- Uses stampers, selectors, and other subsystems
- Respects microplate and plate constraints
- May affect surrounding tiles (e.g., canyon creates river)

**Feature Library**:
- Mountains (peaks, ranges, massifs)
- Volcanoes (types, fields, calderas)
- Canyons (carved valleys)
- Archipelagos (island chains)
- Peninsulas (shaped coastlines)
- Impact craters (circular features)
- Extensible via plugin system

**UI Components**:
- Feature browser (categorized, searchable)
- Feature placement tool with preview
- Parameter adjustment panel
- Feature library showing icons and descriptions
- "Place at suitable location" auto-finder
- Feature editing (move, resize, delete, adjust parameters)

---

## Technical Architecture

### Core Systems

#### **Constraint Manager**
**Responsibilities**:
- Stores all lock flags per tile
- Maintains global constraints (plate velocities, climate parameters)
- Validates constraint consistency (detects conflicts)
- Injects constraints into algorithm stages
- Enforces hard constraints after each stage

**Key Functions**:
- Apply constraints before stage execution
- Restore locked values after stage execution
- Check for conflicts and report to user
- Serialize/deserialize constraint data for save files

#### **Pipeline Orchestrator**
**Responsibilities**:
- Manages stage execution order
- Handles stage dependencies
- Supports stage skipping (if already satisfied)
- Provides pause/resume capability
- Tracks stage timing and statistics

**Stage States**:
- Not Started
- Running
- Paused
- Completed
- Skipped (constraints already satisfied)
- Failed (validation error)

#### **Visualization Engine**
**Responsibilities**:
- Renders world data in real-time
- Supports multiple visualization modes simultaneously
- Overlays constraint indicators
- Shows algorithm progress (frontier, active tiles)
- Manages update frequency based on speed setting

**Performance Considerations**:
- Only render visible tiles (camera frustum culling)
- Use dirty flags to minimize redraws
- Batch similar render operations
- Support LOD for large worlds

#### **Feature System**
**Responsibilities**:
- Maintains registry of available features
- Loads plugins from external assemblies
- Provides UI generation for feature parameters
- Executes feature generation with constraints
- Supports feature auto-placement scoring

**Plugin Interface**:
- Feature metadata (name, category, icon)
- Placement validation (can place at location?)
- Generation function (create feature at location)
- Parameter definition (what's configurable?)
- Suitability scoring (for auto-generation)

---

### Data Flow Architecture

**Generation Flow**:
```
User Input (Painting/Constraints)
    ↓
Constraint Manager (Store locks)
    ↓
Pipeline Orchestrator (Execute stages)
    ↓
Algorithm Stage (Generate with constraints)
    ↓
Constraint Enforcement (Restore locked values)
    ↓
Validation (Check for conflicts/errors)
    ↓
Visualization Update (Show results)
    ↓
User Review (Approve/Adjust/Redo)
```

**Constraint Propagation**:
```
Locked Plate
    ↓ (affects)
Plate Boundaries
    ↓ (affects)
Orogeny Placement
    ↓ (affects)
Elevation
    ↓ (affects)
Waterflow
    ↓ (affects)
Rivers & Lakes
    ↓ (affects)
Climate & Biomes
```

**Lock State Management**:
Each tile maintains:
- Lock flags (bitfield for each property)
- Locked values (stored separately from generated values)
- Lock source (user-painted, algorithm-locked, inherited)
- Lock timestamp (for undo/redo)

---

### Stage Execution Model

**Stage Interface Requirements**:
Every generation stage must provide:
- **Dependencies** - Which stages must complete first
- **Constraint Types** - Which locks does it respect
- **Validation** - How to check if output is valid
- **Progress Reporting** - How to report tile-by-tile progress
- **Undo Support** - How to revert this stage

**Stage Execution Lifecycle**:
1. **Pre-Stage**: Load constraints, validate preconditions
2. **Setup**: Allocate temporary buffers, prepare data structures
3. **Execute**: Run algorithm with periodic progress updates
4. **Post-Process**: Enforce constraints, clean up artifacts
5. **Validate**: Check output meets requirements
6. **Commit**: Save results, update world state

**Stage Retry Mechanism**:
If validation fails:
- Preserve world state before stage
- Present failure reason to user
- Offer options: retry with different parameters, adjust constraints, skip stage
- Allow manual fixes before continuing

---

## Feature Extension System

### Plugin Architecture

**Design Goals**:
- Third-party developers can add custom features
- No recompilation of core generator required
- Features integrate seamlessly with UI and pipeline
- Full access to world data and generation subsystems
- defered untill after first release

**Feature Definition Requirements**:

Every custom feature must specify:
- **Metadata**: Name, category, description, icon
- **Placement Rules**: Where can this feature exist (validation function)
- **Generation Logic**: How to create the feature (execution function)
- **Parameters**: What's configurable (UI auto-generated from this)
- **Suitability Scoring**: For auto-generation, how suitable is each location

**Parameter System**:

Features expose parameters with metadata:
- Type (float, int, bool, enum, color, etc.)
- Range/constraints (min/max, valid options)
- Default value
- Display name and tooltip
- Grouping (for UI organization)

UI is automatically generated from parameter definitions

**Feature Categories**:
Suggested organization:
- Tectonic Features (orogenies, rifts, faults)
- Volcanic Features (volcanoes, calderas, lava fields)
- Erosional Features (canyons, badlands, karst)
- Coastal Features (bays, fjords, archipelagos)
- Impact Features (craters, ejecta)
- Custom/Other

**Subsystem Access**:

Features have access to:
- World data (read/write tiles)
- Stamper system (for terrain modification)
- Selector system (for tile selection)
- Noise generators (for procedural variation)
- Constraint manager (respect locks)
- Visualization (show progress)

### Auto-Generation Support

**Suitability Scoring**:

For automatic feature placement, features provide scoring function:
- refer to gleba implementations

**Auto-Placement Algorithm**:
- refer to gleba implementations

**User Control**:
- Specify feature count or density
- Set minimum suitability threshold
- Define spacing constraints
- Preview scores as heatmap before committing

---

## Map Visualization Modes

### Essential Visualization Categories

#### **Tectonic Visualizations**
- **Plate Assignment** - Solid color per plate, shows plate coverage
- **Microplates** - Finer subdivision, shows geological provinces
- **Plate Boundaries** - Color-coded: divergent (blue), convergent (red), transform (yellow)
- **Plate Velocities** - Arrow overlay showing motion direction and speed
- **Crust Age** - Gradient showing age from divergent boundaries (young=bright, old=dark)
- **Crust Type** - Continental vs oceanic differentiation

#### **Terrain Visualizations**
- **Elevation** - Heatmap or color gradient (blue=low, green=mid, brown=high, white=peaks)
- **Elevation Contours** - Topographic contour lines at regular intervals
- **Slope** - Steepness visualization (flat=green, steep=red)
- **Local Peaks & Sinks** - Highlight local elevation extrema
- **Aspect** - Which direction slopes face (useful for climate)
- **Terrain Roughness** - Variation in local elevation

#### **Feature Visualizations**
- **Orogeny Strength** - Color per orogeny ID, shows mountain building events
- **Volcanism** - Show volcano locations, types, and activity status
- **Hotspots** - Volcanic chains from mantle plumes
- **Ancient Features** - Old eroded mountains and hills
- **Custom Features** - Show all placed custom features with icons

#### **Hydrology Visualizations**
- **Waterflow Accumulation** - Brightness indicates flow volume (rivers visible)
- **Drainage Basins** - Color per watershed/basin
- **Rivers** - Highlight river courses, width by flow
- **Lakes & Waterbodies** - Show waterbody extents and IDs
- **Glaciers** - Ice coverage and thickness
- **Groundwater** - Aquifer locations (if simulated)

#### **Climate Visualizations**
- **Temperature** - Heatmap for annual/seasonal temperature
- **Rainfall** - Annual precipitation amounts
- **Köppen Classification** - Standard climate zones (Af, BWh, Cfb, etc.)
- **Continentality** - Ocean influence vs inland continental
- **Seasonality** - Temperature or rainfall variation through year
- **Wind Patterns** - Prevailing wind direction (future)

#### **Geology Visualizations**
- **Rock Type** - Color per rock type (igneous, sedimentary, metamorphic)
- **Soil Type** - Soil classification (sand, clay, silt composition)
- **Soil Depth** - Thickness of soil layer
- **Mineral Richness** - Resource distribution (for gameplay)

#### **Biome Visualizations**
- **Biome Map** - Standard biome classification with colors
- **Vegetation Cover** - Tree, grass, shrub coverage percentages
- **Flora Composition** - Specific plant species distribution
- **Biodiversity** - Species richness (future integration)

#### **Debug & Technical Visualizations**
- **Lock Overlay** - Highlight fully locked tiles (yellow border)
- **Partial Lock Overlay** - Highlight partially locked tiles (orange border)
- **Algorithm Frontier** - Show active processing frontier (animated cyan dots)
- **Tile Indices** - Display tile IDs as text
- **Neighbor Graph** - Show connectivity lines between tiles
- **Debug Scores** - Display internal algorithm scoring values
- **Performance Heatmap** - Show computation time per tile

### Visualization Controls

**Multi-Layer Support**:
- Primary visualization mode (base layer)
- Multiple overlay modes simultaneously
- Adjustable opacity per layer
- Toggle layers on/off quickly

**Visual Customization**:
- Custom color schemes for each mode
- Adjustable contrast and brightness
- Optional hillshading for terrain relief
- Contour line thickness and spacing
- Label density and size

**View Presets**:
- Save favorite visualization combinations
- Quick-switch between presets
- Share presets between projects
- Default presets for common tasks

**Export Options**:
- Export current view as image
- Export specific mode as data layer
- Export multiple modes as layers for GIS tools
- Export animation of generation process

---

## Future Integration: Canvas of Kings

### Long-Term Vision (Post-Generator)

Once world generation is feature-complete, integration with narrative and worldbuilding tools:

#### **Settlement Generation**
**Concept**: Algorithmically place cities, towns, villages based on geographic suitability

**Scoring Factors**:
- Proximity to rivers (water access, trade, transportation)
- Fertile regions (agriculture potential)
- Coastal locations (maritime trade)
- Moderate elevation (not mountains or valleys)
- Climate suitability (avoid extremes)
- Distance from other settlements (spacing)
- Strategic positions (defensible, chokepoints)

**Generation Process**:
- Score all tiles for settlement suitability
- Place major cities at highest-scoring locations
- Place towns radiating from cities
- Place villages filling gaps
- Generate road networks connecting settlements
- Create farmland around settlements in suitable terrain
- Establish trade routes (land and maritime)

#### **Narrative Tools**
**Character Tracking**:
- Track multiple characters' positions on map
- Visualize character movement over time
- Show character history (path traveled)
- Group characters (parties, armies, caravans)

**Travel Calculation**:
- Compute travel time between points considering:
  - Terrain difficulty (mountains slow, roads fast)
  - Rivers (boats vs fording)
  - Weather/climate (seasonal adjustments)
  - Character attributes (mounted, on foot, etc.)
- Multiple route options (fastest, safest, scenic)
- Display route on map with time estimates

**Environment Descriptions**:
- Auto-generate prose descriptions of locations
- Based on: biome, terrain, nearby features, climate, season
- Customizable writing style and detail level
- Include sensory details (sights, sounds, smells)

**Event Mapping**:
- Place story events on specific locations
- Timeline view showing when events occur
- Connect events with character movements
- Export chronology for reference

**Faction & Territory**:
- Draw territorial boundaries
- Color-code faction control
- Show influence gradients
- Track territorial changes over time

#### **Paper Map Rendering**
**Artistic Style Features**:
- Parchment/vellum texture background
- Hand-drawn aesthetic for features
- Mountain symbols (pictorial, not realistic)
- Illustrated forests (tree symbols)
- Decorative elements:
  - Compass rose (cardinal directions)
  - Scale bar (distance reference)
  - Title cartouche (ornamental frame)
  - Sea monsters and ships (decorative)
  - Wind roses or weather indicators

**Technical Rendering**:
- Layered rendering system (base → symbols → labels → decorations)
- Vector symbols for scalability
- Custom font support for labels
- Aging effects (tea stains, torn edges, fading)
- Vignette edges for aged look
- Optional grid overlay (latitude/longitude or arbitrary)

**Customization**:
- Multiple art styles (medieval, fantasy, modern, etc.)
- Adjustable detail density
- Selective layer visibility
- Export at various resolutions
- Print-ready output formats

**Camera Controls**:
- Free camera movement (pan, tilt, zoom, rotate)
- Smooth interpolation between positions


**Deliverables**:
- Complete generation pipeline (tectonics through biomes)
- Lock flag system operational
- Basic painting tools (plates, elevation)
- Pipeline visualization UI
- Essential visualization modes

**Success Criteria**:
- Generate geologically plausible worlds
- Locked plates respected correctly
- Artists can paint basic constraints
- Real-time observation of generation

---

### Phase 2: Artist Tools & Polish
**Duration**: 3-4 months
**Goal**: Full suite of painting and constraint tools

**Deliverables**:
- Complete painting tool set (rivers, features)
- "Noisify" and "make natural" operations
- Feature placement system
- Undo/redo for all stages
- All visualization modes
- Export capabilities

**Success Criteria**:
- Artists can constrain any aspect of generation
- Partial locks work correctly (noisification)
- UI is intuitive and responsive
- Visualization supports debugging

---

### Phase 3: Feature Extension System
**Duration**: 2-3 months
**Goal**: Plugin architecture for custom features

**Deliverables**:
- Feature plugin API
- Example custom features (canyon, archipelago, crater)
- Feature library browser
- Auto-placement system
- Documentation for plugin developers

**Success Criteria**:
- Third parties can create features without core modification
- Parameters auto-generate UI
- Features integrate with all systems
- Auto-placement produces sensible results

---

### Phase 4: Optimization & Large Worlds
**Duration**: 2-3 months
**Goal**: Support massive worlds with good performance

**Deliverables**:
- Performance profiling and optimization
- Support for 100k+ tile worlds
- Memory optimization
- Multi-threading for generation stages
- Progressive generation for UI responsiveness

**Success Criteria**:
- 100k tile world generates in <5 minutes
- UI remains responsive during generation
- Memory usage reasonable for large worlds
- Visualization performs well at all scales

---

### Phase 5: Canvas of Kings Integration
**Duration**: 4-6 months
**Goal**: Narrative and worldbuilding tools

**Deliverables**:
- Settlement generation system
- Character tracking
- Travel time calculation
- Route planning
- Environment description generator
- Paper map style renderer
- Google Earth style renderer (basic)

**Success Criteria**:
- Cities placed in sensible locations
- Travel times realistic and terrain-aware
- Paper maps look professionally illustrated
- 3D view navigable and attractive

---

### Phase 6: Polish & Release
**Duration**: 2-3 months
**Goal**: Production-ready software

**Deliverables**:
- Complete documentation (user and developer)
- Tutorial system
- Example worlds and presets
- Performance optimization final pass
- Bug fixing and QA
- Localization support

**Success Criteria**:
- Software stable and reliable
- Documentation comprehensive
- Users can learn system without external help
- Performance meets targets across hardware range

---

## Design Principles Summary

### Core Tenets

1. **Constraint-Driven**: User constraints are absolute, generator works around them
2. **Visual Process**: Generation is observable, understandable, and controllable
3. **Iterative Refinement**: Generate, observe, adjust, regenerate - rapid iteration
4. **Extensible**: Easy to add new features, visualizations, and tools
5. **Artist-Friendly**: Powerful but approachable, emphasizes visual feedback
6. **Technically Sound**: Based on realistic geological and physical processes
7. **Performance-Conscious**: Large worlds supported, but doesn't sacrifice features
8. **Integration-Ready**: Designed for future Canvas of Kings narrative tools

### Success Metrics

**For Artists**:
- Can create desired world shape in <30 minutes
- Constraints work as expected 100% of time
- UI is intuitive (minimal training needed)
- Iteration is fast (change constraint, see result quickly)

**For Technical Users**:
- Custom features can be added in <4 hours
- Plugin API is well-documented and complete
- System is debuggable (visualization modes comprehensive)
- Performance is predictable and acceptable

**For Narrative Users** (Phase 5):
- Cities placed logically
- Travel times feel realistic
- Map styles match aesthetic expectations
- Character tracking enhances storytelling

---


**For performance**:
- Use parallelization where possible
- Use caching where possible
- Use profiling to find bottlenecks
- Use profiling to find bottlenecks

## Conclusion

This world generator represents a new paradigm: **collaborative generation** where human creativity and algorithmic power work together. Unlike traditional "generate and accept" tools, this system treats generation as an iterative, visual conversation between artist and algorithm.

The lock system provides unprecedented control without sacrificing the power of procedural generation. Artists paint the important elements—specific continents, key rivers, landmark mountains—and the generator fills in realistic detail around those constraints.

The visual pipeline transforms generation from a mysterious black box into an understandable, observable process. Artists learn how the system works by watching it, making them better at constraining it effectively.

The feature extension system ensures the generator never becomes limited. As new ideas emerge—canyons, archipelagos, crater fields—they can be added without disrupting the core system.

This foundation, combined with future Canvas of Kings integration, creates a complete worldbuilding and narrative environment where geography, story, and creative vision unite seamlessly.
# Gleba World Generator - Documentation Index

## Overview
This index provides a roadmap to finding specific information across the decompiled Gleba world generation system documentation. Files are organized by topic and functionality.

---

## Core Data Structures & Utilities

### Tile Management
- **TileList.md** - O(1) random selection container for tile sets with HashMap backing
- **TileData.md** - Tile accessor methods: climate sampling (bilinear interpolation), population aggregation, orogeny severity

### Geometry & Coordinates
- **tile.md** - Best-weight index selection, three-tile barycentric interpolation for lat/lon queries
- **region.md** - Random 3D point sampling within cube-sphere regions

### Plate & Orogeny Data
- **TectonicPlateData.md** - Plate movement helper: selects neighbor tile in direction of plate velocity
- **OrogenyData.md** - Adds spine records to orogeny data structures
- **BedrockData.md** - Rock type selector using multi-constraint rule-based classification
- **RockTypeDistribution.md** - Normalizes rock type weight distributions
- **RockTypeID.md** - Resolves rock type IDs to data pointers

---

## Generation Pipeline & Stages

### Pipeline Control
- **get_default_stages.md** - Defines default world-gen pipeline as list of stage closures
- **WorldGenStage.md** - Constructor for stage metadata (name + parameters)
- **WorldGenCoroutine.md** - Background thread wrapper for world generation with progress polling
- **world_gen_stage.md** - Breaks terrain symmetry by perturbing equal-elevation neighbors

### Configuration
- **static_parameters.md** - Loads static parameters from `common/static.ron`
- **model_parameters.md** - Loads model parameters from `common/model.ron`

---

## Tectonics Systems

### Core Tectonics
- **tectonics.md** - Master orchestration: plate creation, flood filling, boundary classification, validation with retry loop
- **generate_plates.md** - Creates plates by seeding and weighted flood-fill expansion with fractal noise
- **elevation.md** - Initializes plates and crust using image-based seeding or procedural generation with retry validation

### Plate Features
- **boundary_features.md** - Generates continental rifts using multi-layer OrogenyStamper
- **microplate_features.md** - Post-processes microplate boundaries with probabilistic stamping
- **old_features.md** - Adds ancient mountains, hills, and uplift belts as late-stage overprint

### Specialized Features
- **hotspots.md** - Generates mantle hotspot tracks following plate motion directions
- **generate_hotspots.md** - Closure that writes hotspot IDs to visited tiles
- **volcanism.md** - Places volcanic features using weighted categorical distributions
- **continental_shelves.md** - Generates continental shelves at ocean-land boundaries
- **rock_types.md** - Assigns bedrock types using Perlin noise and brush stamping
- **noise_elevation.md** - Adds procedural noise variation to land and ocean elevation

### Oceanography
- **oceanography.md** - Generates, smooths, and refines ocean/coastal elevation

---

## Flood Fill Algorithms

### Core Flood Fills
- **flood_fills.md** - Collection of reusable flood-fill algorithms:
  - `simple_flood_fill` - Basic BFS expansion under constraints
  - `fractal_flood_fill` - Noise-based "fractal blob" growth with distance penalty
  - `weighted_flood_fill` - Best-first heap expansion with Perlin scoring
  - `flood_fill_on_chunks` - Chunk-level expansion on cube-sphere
  - `flood_fill_on_cube_sphere` - Tile-level with diagonals

---

## Hydrology & Water Systems

### Rivers & Flow
- **waterflow.md** - Downhill flow accumulation using sorted land tiles
- **WaterflowContext.md** - Helper for allocating/managing transient waterflow buffers
- **rivers.md** - River carving via A* pathfinding with elevation interpolation

### Lakes & Water Bodies
- **lakes.md** - Lake generation using fractal flood fill from seed tiles
- **waterbodies.md** - Manages waterbody objects: creation, flood-fill, perimeter tracking
- **fjords.md** - Generates fjords by tracing paths and carving elevation profiles
- **glacier.md** - Assigns ice/glacier regions using weighted flood fill with noise

---

## Erosion & Terrain Modification

- **erosion.md** - Sorted erosion (waterflow-based) and thermal erosion (slope relaxation)
- **soil.md** - Soil initialization with noise, aeolian deposits, blurring, mountain thinning, sediment transport

---

## Biomes & Climate

- **ecology.md** - Flora initialization from Holdridge moisture/temperature indices or noise
- **biome_gen.md** - Assigns biomes using climate + terrain attributes with range constraints
- **verification.md** - Post-generation validation of tectonics (plate boundaries, ownership)

---

## Selector & Stamper Systems

### Core Selectors
- **selectors.md** - Utility functions:
  - `split_spines` - Splits ordered tile lists into variable chunks
  - `select_random_tile_at_distance` - BFS-based distance window selection
  - `find_*_most_distant_pair` - Finds maximally separated tiles

### Stamper Components
- **Stamper.md** - Core masked BFS flood-fill stamper with noise-expression predicates
- **OrogenyStamper.md** - Bridge between stamper, world orogeny records, and per-tile feature maps
- **stamp_on_tiles.md** - Closure that writes tile flags and applies actions during stamping

### Selection Strategies
- **AreaSelector.md** - Connected-region grower with land/water constraints and noise masking
- **DistanceSelector.md** - Distance-window tile picker with randomized step sizes
- **FillDefinition.md** - Generic "apply operation across selected tiles" helper

### Path & Point Definitions
- **PathDefinition.md** - Dispatch wrapper for path-building strategies (ridges, rifts, chains)
- **PointDefinition.md** - Dispatch for point sampling strategies (random, anchored, along-path)
- **SpanF32.md** - Float range utility for deterministic RNG sampling

### Actions
- **OrogenyAction.md** - Formatting/string dispatch for orogeny action variants

---

## Brush & Stamping Primitives

- **brushes.md** - Random distance-based flood-fill with per-tile mutation (elevation, normals, scalars)

---

## Noise & Expression System

### Noise Generators
- **Noise.md** - Noise object backed by fractal Perlin sources with normalization
- **NoiseExpressionType.md** - Dynamic dispatch for noise expression variants (init + evaluate)

---

## Tile Field Reference

Based on observed usage across files:

### Common Tile Offsets
| Offset | Type | Purpose | Files |
|--------|------|---------|-------|
| `+0x18, +0x1c` | int/int | Plate assignment markers | tectonics, generate_plates, hotspots |
| `+0x78` | u64 | Tile flags (various bits) | stamp_on_tiles, old_features, volcanism |
| `+0xa8, +0xac` | u32/i32 | Waterbody ID + discriminator | waterbodies |
| `+0xb0` | ptr | Feature payload pointer | OrogenyStamper |
| `+0xf0, +0xf4` | u32/i32 | Volcanic/geologic feature refs | hotspots, volcanism |
| `+0xf8, +0xfc` | u64/extra | Hotspot ID + extra field | hotspots, generate_hotspots |
| `+0x108-0x118` | floats | Soil channels (multiple) | soil, ecology, biome_gen |
| `+0x120` | f32 | Elevation | Most modules |
| `+0x128` | f32 | Ice thickness/indicator | lakes, glacier, biome_gen |
| `+0x130` | f32 | Lake size driver | lakes |
| `+0x13c, +0x158, +0x15c` | floats | Brush effect accumulators | brushes |
| `+0x150` | u64 | Packed 2D direction/normal | brushes |
| `+0x164` | u32 | Brush region tag/ID | brushes |
| `+0x174, +0x178, +0x180, +0x184` | floats | Ecology/flora outputs | ecology |
| `+0x188, +0x18c` | u32/int | Biome variant + primary ID | biome_gen |
| `+0x194` | f32 | Waterflow accumulator | waterflow |
| `+0x198` | u8 | Land/water discriminator (0=ocean, 1=land) | Most modules |
| `+0x199-0x19b` | bytes | Debug/region marker RGB | AreaSelector, DistanceSelector |

### World Structure Offsets
| Offset | Purpose | Files |
|--------|---------|-------|
| `+0x08` | Region/entry table base | boundary_features, continental_shelves |
| `+0x10` | Entry count | boundary_features, continental_shelves |
| `+0x28, +0x30` | Record storage pointers | boundary_features, continental_shelves |
| `+0x40` | Volcano SlotMap | volcanism |
| `+0x48-0x5c` | Hotspot sub-feature storage | hotspots |
| `+0x60` | Hotspot SlotMap | hotspots |
| `+0xa0-0xa8` | Waterbody SlotMap | waterbodies |
| `+0xf10` | HexGridTopology | Most modules |
| `+0xf20` | Tile count (alternate view) | Many modules |
| `+0x1140` | TileList storage | rock_types |
| `+0x1148` | Tile array base pointer | Most modules |
| `+0x1150` | Tile count (primary) | Most modules |
| `+0x1170-0x1180` | Transient buffer pool | soil, erosion, WaterflowContext |
| `+0x11a0-0x11b0` | 0xB0-byte config pool | lakes, glacier |
| `+0x11d0-0x11e0` | Hash backing pool | elevation, fjords, lakes, many selectors |
| `+0x1248` | Climate lookup context | ecology, soil, biome_gen |
| `+0x1340` | Subtile topology pointer | WorldGenCoroutine |
| `+0x1348` | Arc-like refcount pointer | biome_gen |
| `+0x1350` | World seed base | Most generation modules |
| `+0x1358` | Planet radius | WorldGenCoroutine, hotspots |
| `+0x1368` | RNG generation counter | Most generation modules |
| `+0x1370` | Climate accessor handle | ecology, soil |
| `+0x1378` | Region count parameter (u32) | WorldGenCoroutine |

---

## Common Patterns & Algorithms

### RNG Mixing Patterns
Most modules use deterministic RNG with these patterns:
- **64-bit state stepping**: `state = state * 0x5851f42d4c957f2d + increment`
- **XOR-rotate extraction**: `u32 = rotr32((state >> 45) ^ (state >> 27), state >> 59)`
- **Hash mixing**: `hash = hi(x*y) ^ lo(x*y)` where multiplication is 128-bit
- Seen in: tectonics, hotspots, fjords, glacier, rock_types, volcanism, many others

### Distance Metrics
- **Great-circle distance**: Used by `selectors.md` for finding distant pairs
- **Euclidean in sampling space**: Used by flood fills for fractal scoring
- **Hop count**: Used by waterflow and some BFS expansions

### Noise Usage
- **Perlin/Fractal sources**: Built with 4-12 octaves typically
- **Geometric normalization**: `scale = C / sum(p^i)` for amplitude control
- **Multi-layer combination**: Several independent stacks with seed offsets
- Core modules: Noise.md, NoiseExpressionType.md, noise_elevation.md

### Memory Management
- **Transient buffer pools**: Reusable per-tile float arrays (soil, erosion, WaterflowContext)
- **SlotMap allocation**: Used for plates, hotspots, volcanoes, waterbodies, orogeny records
- **Hash backing pools**: Reusable HashMap/HashSet backing storage (many selector modules)

---

## Finding Specific Information

### "How are plates created?"
→ Start with **tectonics.md** (orchestration), then **generate_plates.md** (implementation), then **flood_fills.md** (`weighted_flood_fill`)

### "How does river carving work?"
→ **rivers.md** (main algorithm), **waterflow.md** (flow accumulation), **WaterflowContext.md** (buffer management)

### "What determines biome assignment?"
→ **biome_gen.md** (range-based selection), **ecology.md** (Holdridge inputs), **TileData.md** (climate sampling)

### "How are mountains/features stamped?"
→ **Stamper.md** (core engine), **OrogenyStamper.md** (execution), **stamp_on_tiles.md** (tile writes), **DistanceSelector.md** (tile selection)

### "What creates the 'fractal' shapes?"
→ **flood_fills.md** (`fractal_flood_fill` function), **Noise.md** (source generation), **brushes.md** (localized stamping)

### "How is elevation modified?"
→ **elevation.md** (initial setup), **noise_elevation.md** (noise augmentation), **erosion.md** (thermal/sorted), **rivers.md** (carving), **oceanography.md** (ocean smoothing)

### "Where are RNG seeds derived?"
→ Look for `world + 0x1368` (counter) and `world + 0x1350` (base seed) - used in almost every generation module

### "What do tile flags mean?"
→ Check **Tile Field Reference** section above, cross-reference with specific module (e.g., `+0x198` in waterbodies.md, lakes.md, fjords.md)

### "How are validation/retries handled?"
→ **tectonics.md** (plate retry loop), **elevation.md** (placement validation), **verification.md** (post-gen checks)

---

## Module Dependencies (Simplified)
```
Configuration (static/model_parameters)
    ↓
Pipeline (get_default_stages, WorldGenCoroutine)
    ↓
Tectonics (tectonics, generate_plates, elevation)
    ├→ Boundaries (boundary_features, continental_shelves)
    ├→ Features (hotspots, volcanism, old_features, microplate_features)
    └→ Rock Types (rock_types)
    ↓
Oceanography (oceanography, noise_elevation)
    ↓
Hydrology (waterflow, lakes, fjords, glacier, waterbodies)
    ↓
Erosion (erosion, rivers, soil)
    ↓
Biomes (ecology, biome_gen)
    ↓
Verification (verification, world_gen_stage)
```

### Cross-Cutting Systems
- **Flood Fills** (flood_fills.md) - Used by tectonics, lakes, glacier, waterbodies
- **Selectors/Stampers** (Stamper.md, OrogenyStamper.md, AreaSelector.md, DistanceSelector.md) - Used by boundary_features, hotspots, old_features, continental_shelves
- **Brushes** (brushes.md) - Used by rock_types and any localized feature stamping
- **Noise** (Noise.md, NoiseExpressionType.md) - Used throughout for procedural variation

---

## File Count Summary
- **Core utilities**: 11 files
- **Pipeline & configuration**: 5 files  
- **Tectonics**: 14 files
- **Hydrology & water**: 7 files
- **Erosion & terrain**: 2 files
- **Biomes & climate**: 3 files
- **Selectors & stampers**: 11 files
- **Noise systems**: 2 files

**Total**: 55 documentation files
# World Generation System - Developer Reference Guide

## Document Purpose

This guide provides a conceptual framework for implementing a complete world generation system. It focuses on:
- **System requirements and dependencies**
- **Mathematical concepts and algorithms**
- **Data flow and architectural patterns**
- **Testing and validation strategies**

Use this as a reference architecture while designing your C# generator.

---

## System Architecture Overview

### Generation Philosophy

The world generator follows a **multi-pass pipeline** where each stage builds upon previous results. The entire process must be:
- **Deterministic**: Same seed produces identical worlds
- **Reproducible**: Can checkpoint and resume at any stage
- **Validatable**: Each stage verifies its output meets constraints
- **Iterative**: Some stages may retry with different parameters if validation fails

### Dependency Graph

```
Phase 0: Foundation (RNG, Topology, Utilities)
    ↓
Phase 1: Tectonics (Plates, Boundaries, Base Elevation)
    ↓
Phase 2: Terrain Features (Mountains, Rifts, Volcanoes, Hotspots)
    ↓
Phase 3: Oceanography & Initial Hydrology
    ↓
Phase 4: Waterflow, Lakes, Rivers, Glaciers
    ↓
Phase 5: Erosion & Soil Development
    ↓
Phase 6: Climate & Biome Assignment
    ↓
Phase 7: Verification & Cleanup
```

---

## Phase 0: Foundation Systems

**Priority**: **CRITICAL** - Must implement before any generation

### 0.1 Deterministic Random Number Generation

**Purpose**: Provide reproducible randomness for all procedural content

**Requirements**:
- Must support serialization (save/load state)
- Must generate uniform distributions for floats and integers
- Should avoid modulo bias when generating bounded integers
- Must be thread-safe if used in parallel contexts

**Mathematical Concepts**:

**State Evolution** (LCG-style):
The reference uses a 64-bit multiplicative generator:
```
state[n+1] = (state[n] × multiplier) + increment
```
Where `multiplier` and `increment` are carefully chosen constants with good statistical properties.

**Output Extraction** (XOR-Rotate mixing):
Raw state is mixed before output to eliminate patterns:
```
1. Extract two shifted copies of state and XOR them
2. Use another part of state to determine rotation amount
3. Rotate the XOR result
4. Convert to desired output type (float, int, etc.)
```

**Float Conversion** to [0, 1):
```
1. Take upper bits of mixed value (lower bits have less entropy)
2. Multiply by (1.0 / 2^precision) where precision is number of mantissa bits
```

**Modulo-Bias-Free Integer** in [0, max):
```
1. Compute threshold = (2^64 - max) mod max
2. Generate random 64-bit value
3. If value < threshold, reject and retry (ensures uniform distribution)
4. Return value mod max
```

**Key Design Decisions**:
- Use 64-bit state for long period before repetition
- Use cryptographic-quality mixing (not for security, but for statistical quality)
- Never expose raw state directly
- Provide convenience methods: `NextFloat()`, `NextInt(max)`, `NextRange(min, max)`

**Testing Requirements**:
- [ ] Period length is sufficiently long (>2^60 values)
- [ ] Distribution passes chi-squared test for uniformity
- [ ] Same seed produces identical sequences across platforms
- [ ] State serialization is lossless

---

### 0.2 World Topology (Hex-Sphere)

**Purpose**: Define tile positions and neighbor relationships

**Requirements**:
- Support 1,000 to 100,000+ tiles
- Pre-compute all neighbor relationships
- Provide coordinate conversions (cartesian ↔ spherical ↔ local)
- Support spatial queries (nearest tile to lat/lon)

**Mathematical Concepts**:

**Hex-Sphere Construction**:
Choose one method:
1. **Icosphere subdivision**: Start with icosahedron, subdivide triangular faces, project to sphere
2. **Cube-sphere projection**: Create cube, subdivide faces, project to sphere
3. **Fibonacci sphere**: Use golden ratio spiral to place points uniformly

**Neighbor Relationships**:
- Most tiles have 5-6 neighbors (depends on construction method)
- Store as flat array with offsets (memory efficient)
- Guarantee symmetry: if A neighbors B, then B neighbors A

**Coordinate Systems**:

**Cartesian** (x, y, z):
```
x² + y² + z² = r²  (on sphere of radius r)
```

**Spherical** (latitude, longitude):
```
x = r × cos(lat) × cos(lon)
y = r × cos(lat) × sin(lon)
z = r × sin(lat)
```

**Sampling Coordinates**:
For noise sampling, may need additional transforms to avoid distortion at poles.

**Spatial Queries**:
- **Nearest tile**: Project query point to sphere, find closest tile center
- **Three-tile interpolation**: Find containing "triangle", compute barycentric weights

**Key Design Decisions**:
- Choose construction method based on desired tile distribution
- Pre-compute and cache all geometric data
- Store positions in multiple coordinate systems for efficiency
- Consider LOD (level of detail) if supporting variable resolution

**Testing Requirements**:
- [ ] All tiles have 5-6 neighbors (check for exceptions at poles)
- [ ] Neighbor relationships are symmetric
- [ ] Lat/lon ↔ Cartesian conversions are accurate to floating-point precision
- [ ] No duplicate or missing neighbors

---

### 0.3 Tile Data Management

**Purpose**: Store and access per-tile information efficiently

**Requirements**:
- Support all data needed by generation pipeline (see field requirements below)
- Minimize memory footprint while maintaining readability
- Support parallel access patterns (read-heavy with occasional writes)
- Enable fast neighbor queries (cache-friendly memory layout)

**Field Requirements by Phase**:

**Phase 1 (Tectonics)**:
- Plate assignment (which plate owns this tile)
- Plate sub-classification (for complex boundaries)
- Crust type (continental vs oceanic, possibly fractional)
- Crust age (distance from divergent boundary, affects ocean depth)
- Base elevation (initial height before terrain features)
- Land/water classification

**Phase 2 (Features)**:
- Volcanic feature references (hotspots, standalone volcanoes)
- Orogeny/mountain feature references
- Boundary feature markers (rift/mountain/fault)

**Phase 3-4 (Hydrology)**:
- Waterbody assignment (which lake/ocean/sea)
- Waterflow accumulation (for river routing)
- Ice thickness (glaciers)
- Final elevation (after all modifications)

**Phase 5 (Soil)**:
- Soil composition (multiple channels/weights)
- Sediment accumulation
- Erosion factors

**Phase 6 (Biomes)**:
- Temperature (monthly or average)
- Rainfall (monthly or average)
- Flora weights (vegetation composition)
- Biome classification (primary and variant)

**Internal/Debug**:
- Tile flags (bitfield for various boolean states)
- Debug visualization markers
- Feature payload (extensible object for complex data)

**Memory Layout Strategies**:

**Struct-of-Arrays** (SoA):
```
float[] elevations
int[] plateIds
byte[] landWaterFlags
...
```
Pros: Cache-friendly for operations on single field across many tiles
Cons: More complex access patterns

**Array-of-Structs** (AoS):
```
struct Tile { float elevation; int plateId; byte landWaterFlag; ... }
Tile[] tiles
```
Pros: Simple access, all tile data together
Cons: May waste cache lines if operations only need few fields

**Hybrid**:
Core fields in main struct, rare fields in separate arrays or dictionary.

**Key Design Decisions**:
- Choose memory layout based on access patterns
- Use appropriate types (byte vs int) to save memory
- Consider alignment/padding for cache efficiency
- Plan for extensibility (additional features in future)

**Testing Requirements**:
- [ ] Memory usage scales linearly with tile count
- [ ] Parallel access doesn't cause data races
- [ ] Serialization preserves all data accurately

---

### 0.4 Core Utility Structures

**Purpose**: Provide reusable data structures for generation algorithms

#### TileList (O(1) Random Selection)

**Purpose**: Maintain a set of tiles with efficient random access

**Requirements**:
- Add tile in O(1)
- Remove tile in O(1)
- Get random tile in O(1)
- Check contains in O(1)

**Algorithm Concept**:
- Maintain vector of tile IDs (for random access by index)
- Maintain hash map: tile ID → index in vector (for O(1) lookup)
- On removal: swap-remove (move last element to removed position, update map)

**Use Cases**:
- Available tiles during plate expansion
- Candidate sites for features (lakes, volcanoes)
- Any set that needs frequent random sampling

#### Transient Buffer Pool

**Purpose**: Reuse memory allocations between generation stages

**Requirements**:
- Rent/return buffers of fixed size (typically tile count)
- Zero/clear buffers on return
- Thread-safe if used in parallel

**Algorithm Concept**:
- Maintain stack of available buffers
- Rent: pop from stack (or allocate if empty)
- Return: clear and push to stack
- Optionally track "high water mark" for debugging

**Use Cases**:
- Temporary per-tile floats (waterflow, erosion)
- Neighbor elevation caches
- Score/weight buffers for selection algorithms

#### Hash Table Pool

**Purpose**: Reuse hash table backing storage between passes

**Requirements**:
- Support visited sets for flood fills
- Quick clear (may use "generation counter" instead of full clear)
- Multiple pools for nested algorithms

**Algorithm Concept**:
- Pre-allocate hash tables with good initial capacity
- Poison/reset between uses (mark all slots as empty)
- Borrow/return pattern similar to buffer pool

**Use Cases**:
- Visited sets in BFS/DFS algorithms
- Temporary tile → data maps
- Boundary detection

---

## Phase 1: Tectonics System

**Priority**: **CRITICAL** - Defines fundamental world structure

**Dependencies**: Phase 0 (all foundation systems)

### Overview

Tectonics creates the base structure of the world by:
1. Dividing sphere into tectonic plates
2. Assigning movement vectors to plates
3. Classifying plate boundaries
4. Setting initial elevation based on crust type
5. Computing crust age (for ocean depth)

### 1.1 Plate Generation

**Purpose**: Partition the world into tectonic plates

**Requirements**:
- Support 2-50 plates (configurable)
- Each plate must be contiguous (no disconnected regions)
- Plates should have "natural" irregular borders
- Plate sizes should roughly match configured weights

**Algorithm**: Weighted Flood Fill Expansion

**Conceptual Steps**:
1. **Seed Selection**: Choose starting tiles (random or from image)
2. **Initial Assignment**: Create plate records, assign seed tiles
3. **Frontier Building**: Find all neighbors of assigned tiles that are unassigned
4. **Priority Scoring**: For each frontier tile, compute score based on:
   - Multi-octave fractal noise (creates irregular shapes)
   - Distance from plate center (prevents over-expansion)
   - Plate weight (larger plates get boost)
5. **Expansion**: Repeatedly take highest-scoring frontier tile, assign to best plate
6. **Termination**: Continue until all tiles assigned

**Mathematical Concepts**:

**Fractal Noise Scoring**:
```
score_noise = Σ(i=0 to octaves-1) amplitude[i] × Perlin(position × frequency[i])

Where:
  amplitude[i] = base_amplitude × persistence^i
  frequency[i] = base_frequency × lacunarity^i
```

Common parameters:
- Octaves: 6-8
- Persistence: 0.5 (each octave half amplitude of previous)
- Lacunarity: 2.0 (each octave double frequency of previous)

**Distance Penalty**:
```
distance_penalty = ||position_tile - position_plate_center||
score_distance = -penalty_weight × distance_penalty
```

**Combined Score**:
```
score_total = score_noise × plate_weight + score_distance
```

**Plate Weight Normalization**:
To ensure plates match configured size ratios:
```
target_size[plate] = total_tiles × (weight[plate] / Σ weights)
```

**Configuration Parameters**:
- Plate count
- Per-plate weights (relative sizes)
- Per-plate type hints (continental vs oceanic)
- Noise parameters (octaves, persistence, lacunarity)
- Distance penalty weight

**Validation Requirements**:
- [ ] Every tile assigned to exactly one plate
- [ ] Each plate is contiguous
- [ ] Plate size ratios approximately match weights (within tolerance)
- [ ] No single-tile or very small plates (unless intentional)

**Failure Conditions & Retry**:
- If validation fails, may need to retry with:
  - Different random seed
  - Adjusted weights
  - Different seed tile positions

---

### 1.2 Plate Velocity Assignment

**Purpose**: Define how each plate moves on the sphere

**Requirements**:
- Each plate has 2D velocity vector (magnitude and direction)
- Velocities should be geologically plausible
- Can be random or configured

**Mathematical Concepts**:

**2D Velocity on Sphere**:
For simplicity, can treat as 2D vector in local tangent space:
```
velocity = (vx, vy)  // meters per million years, or abstract units
magnitude = √(vx² + vy²)
direction = atan2(vy, vx)
```

**Random Generation**:
```
angle = random(0, 2π)
speed = random(min_speed, max_speed)
vx = speed × cos(angle)
vy = speed × sin(angle)
```

**Configuration**:
- Min/max speed ranges
- Optional: constrain directions (e.g., prefer certain patterns)
- Optional: load velocities from configuration file

**Use in Later Stages**:
- Boundary classification (convergent vs divergent vs transform)
- Hotspot track direction
- Microplate motion

---

### 1.3 Boundary Classification

**Purpose**: Identify and classify plate boundary interactions

**Requirements**:
- Detect all tiles where plates meet
- Classify each boundary as: Divergent, Convergent, or Transform
- Store boundary relationships for feature generation

**Algorithm**: Edge Detection + Velocity Analysis

**Conceptual Steps**:
1. **Edge Detection**: For each tile, check if any neighbor is on different plate
2. **Velocity Projection**: Project both plates' velocities onto boundary normal
3. **Classification**: Compare relative motion:
   - Moving apart → Divergent
   - Moving together → Convergent
   - Sliding parallel → Transform

**Mathematical Concepts**:

**Boundary Normal**:
```
Given two tiles A and B on different plates:
edge_vector = normalize(position_B - position_A)
boundary_normal = perpendicular(edge_vector)
```

In 2D: `normal = (-edge_y, edge_x)` (90° rotation)

**Velocity Projection**:
```
dot_A = velocity_A · boundary_normal
dot_B = velocity_B · boundary_normal
relative_motion = dot_A - dot_B
```

**Classification**:
```
if |relative_motion| < threshold:
    → Transform (parallel motion)
elif relative_motion > 0:
    → Divergent (moving apart)
else:
    → Convergent (moving together)
```

**Configuration Parameters**:
- Transform threshold (how parallel must velocities be)
- Boundary width (single tile or band)

**Data Storage**:
Per boundary segment:
- Tile IDs on both sides
- Plate IDs on both sides
- Boundary type
- Optional: relative motion magnitude

**Validation Requirements**:
- [ ] All edge tiles correctly identified
- [ ] Boundary classification is consistent along segments
- [ ] No gaps in boundary detection

---

### 1.4 Base Elevation Assignment

**Purpose**: Set initial elevation based on crust type

**Requirements**:
- Continental crust above sea level (land)
- Oceanic crust below sea level (water)
- Smooth variation within each crust type
- Deterministic from seed

**Algorithm**: Rule-Based Assignment with Noise

**Conceptual Steps**:
1. **Base Assignment**: Set elevation based on crust type
2. **Random Variation**: Add noise for natural variation
3. **Land/Water Classification**: Mark tiles based on elevation vs sea level

**Mathematical Concepts**:

**Base Elevation**:
```
if crust_type == Continental:
    base = CONTINENTAL_BASE  // e.g., +500m to +1000m
else:
    base = OCEANIC_BASE      // e.g., -3000m to -5000m
```

**Random Variation**:
```
variation = random(-variance, +variance)
elevation = base + variation
```

**Land/Water Flag**:
```
is_land = (elevation > SEA_LEVEL)  // SEA_LEVEL = 0m
```

**Configuration Parameters**:
- Continental base elevation and variance
- Oceanic base elevation and variance
- Sea level reference

**Note**: This is *initial* elevation. Later stages will modify it significantly.

---

### 1.5 Crust Age Determination

**Purpose**: Assign age to oceanic crust based on distance from rifts

**Requirements**:
- Age is 0 at divergent boundaries (where new crust forms)
- Age increases monotonically away from boundaries
- Age only propagates within same plate
- Continental crust may have very high age (or separate classification)

**Algorithm**: Breadth-First Search from Rifts

**Conceptual Steps**:
1. **Find Sources**: Collect all divergent boundary tiles (age = 0)
2. **BFS Expansion**: Expand outward, incrementing age at each step
3. **Plate Constraint**: Don't cross plate boundaries
4. **Continental Handling**: May skip continents or assign default old age

**Mathematical Concepts**:

**Age Propagation**:
```
age[tile] = age[parent] + distance_increment

Where distance_increment can be:
  - 1 (hop count)
  - actual_distance(parent, tile)
  - time-based (using plate velocity)
```

**Usage in Later Stages**:
- Ocean depth increases with age (cooling/contraction)
- Sediment thickness increases with age
- Different volcanic/tectonic activity by age

**Configuration Parameters**:
- Age scaling factor (hops → million years)
- Continental age handling (skip or default value)

---

### 1.6 Boundary Features

**Purpose**: Create terrain features at plate boundaries

**Requirements**:
- Divergent boundaries: rifts, mid-ocean ridges
- Convergent boundaries: mountains, trenches, subduction zones
- Transform boundaries: fault lines
- Features should have multiple layers (core + foothills + outer zones)

This system is complex enough that it's deferred to the **Stamping System** (Phase 2).

**Key Concepts**:
- Use distance-based selection to find tiles near boundaries
- Apply elevation modifications in concentric zones
- Use fractal noise to mask/modulate effects
- Different parameters for different boundary types

**See**: Phase 2 for detailed stamping system requirements.

---

### 1.7 Ancient/Old Features

**Purpose**: Add old, eroded mountains and hills not related to current plates

**Requirements**:
- Scattered randomly across continents
- Lower elevation than active mountains
- Can overlap with other features
- Should look weathered/rounded

**Algorithm**: Random Placement with Stamping

**Conceptual Steps**:
1. **Count Determination**: Compute number based on world size and density
2. **Site Selection**: Pick random continental tiles meeting elevation criteria
3. **Feature Stamping**: Apply multi-layer elevation modifications
4. **Flag Setting**: Mark tiles for later erosion/weathering

**Mathematical Concepts**:

**Count Formula**:
```
area_scaled = (planet_radius² × density_parameter) / normalization_constant
min_count = base_min + area_scaled
max_count = base_max + area_scaled
actual_count = ceil(random(min_count, max_count))
```

**Site Criteria**:
- Must be land (elevation > sea level)
- Elevation below threshold (not already on mountain)
- Random acceptance based on per-tile scalar

**Feature Types**:
1. **Old Orogeny** (ancient mountain ranges)
   - Multi-layer stamper (main belt, foothills, uplift zones)
   - Moderate elevation increase
2. **Hills** (smaller scattered features)
   - Simpler stamper
   - Small elevation increase
3. **Simple Uplift** (direct elevation modification)
   - No stamper, just add random value to elevation
   - Set tile flag for identification

**Configuration Parameters**:
- Density multipliers (per feature type)
- Elevation thresholds
- Stamper radii and intensities

---

### 1.8 Hotspots (Mantle Plumes)

**Purpose**: Create volcanic island chains from stationary mantle plumes

**Requirements**:
- Chains follow plate motion direction
- Oldest volcanoes at one end, newest at other
- Should stay within originating plate
- Length scales with plate velocity

**Algorithm**: Directional Path Tracing

**Conceptual Steps**:
1. **Count Determination**: Based on world size and density
2. **Start Tile Selection**: Pick oceanic tile with low elevation
3. **Direction**: Use plate velocity vector
4. **Path Building**: Walk neighbors in direction of plate motion
5. **Volcano Stamping**: Place volcanoes along path with decreasing intensity
6. **Record Creation**: Store hotspot as object with tile chain

**Mathematical Concepts**:

**Hotspot Count**:
```
density_scaled = (planet_radius² × density_param) / scale_const
count = random(min + density_scaled, max + density_scaled)
```

**Chain Length**:
```
base_length = random(min_length, max_length)
scaled_length = base_length × world_scale_factor × random_variation
```

**Direction Selection** (best matching neighbor):
For each neighbor of current tile:
```
neighbor_direction = normalize(neighbor_pos - current_pos)
dot_product = neighbor_direction · plate_velocity_normalized
→ Choose neighbor with highest dot product
```

**Volcanic Intensity Along Chain**:
```
intensity[i] = (1 - progress[i]) × base_intensity
where progress[i] = i / chain_length
```
(Oldest = weakest, newest = strongest)

**Path Constraints**:
- Must stay within same plate
- Reject if neighbor is on different plate
- Stop if no valid neighbors remain

**Configuration Parameters**:
- Hotspot density
- Min/max chain lengths
- Volcanic intensity function
- Start tile selection criteria

---

### 1.9 Volcanism (General)

**Purpose**: Place standalone volcanoes at geologically appropriate locations

**Requirements**:
- Different volcano types (stratovolcano, shield, cinder cone, etc.)
- Type depends on location context (convergent, rift, hotspot)
- Probabilistic spawning (not every candidate gets volcano)
- Weighted selection of types

**Algorithm**: Candidate Detection + Probabilistic Spawning

**Conceptual Steps**:
1. **Candidate Detection**: Build list of tiles that *could* have volcano
2. **Tile Filtering**: Skip tiles already with features
3. **Distribution Selection**: Choose volcano distribution based on tile flags
4. **Spawn Roll**: Random check against spawn threshold
5. **Type Selection**: Weighted categorical selection of volcano type
6. **Parameter Sampling**: Sample continuous parameters (size, explosivity, etc.)
7. **Record Creation**: Create volcano object and store reference on tile

**Mathematical Concepts**:

**Tile Flags** (bitfield determines eligibility):
```
is_candidate = (flags & CONVERGENT_FLAG) ||
               (flags & HOTSPOT_FLAG) ||
               (flags & RIFT_FLAG) ||
               (flags & OLD_OROGENY_FLAG)
```

**Spawn Threshold**:
```
spawn = (random_value >= distribution.spawn_threshold)
```

**Weighted Categorical Selection**:
```
1. Compute cumulative weights: W[i] = Σ(j=0 to i) weight[j]
2. Generate random in [0, W[n])
3. Find first i where random < W[i]
4. Return type[i]
```

**Parameter Sampling** (per type):
```
Each volcano type defines ranges for parameters:
- Size: random(size_min, size_max)
- Explosivity: random(expl_min, expl_max)
- Boolean flags: random() < flag_probability
```

**Volcano Distributions**:
Different contexts have different type distributions:
- **Convergent**: More stratovolcanoes, higher explosivity
- **Hotspot**: More shield volcanoes, lower explosivity
- **Rift**: Fissure vents, low explosivity
- **Old Mountains**: Extinct, weathered volcanoes

**Configuration Parameters**:
- Per-context distributions (spawn thresholds, type weights)
- Per-type parameter ranges
- Volcano density scaling

**Data Storage**:
Per volcano:
- Type ID
- Parameters (size, explosivity, age, etc.)
- Location (tile ID)
- Optional: visual/gameplay parameters

---

### 1.10 Rock Type Assignment

**Purpose**: Assign bedrock geology to tiles

**Requirements**:
- Multiple rock types (igneous, sedimentary, metamorphic variants)
- Type depends on: plate history, elevation, proximity to features
- Uses fractal noise for natural variation
- Can use "brush" stamping for localized geology

**Algorithm**: Multi-Pass Assignment with Stamping

**Conceptual Steps**:
1. **Initial Assignment**: Parallel pass using noise + climate
2. **Peak Seeding**: Stamp localized rock types from mountain peaks
3. **Random Seeding**: Additional stamps from maintained tile list
4. **Cleanup**: Final pass to ensure consistency

**Mathematical Concepts**:

**Rule-Based Selection**:
Each rock type has a set of constraints:
```
Rule for RockType_X:
  - elevation in [min_elev, max_elev]
  - slope in [min_slope, max_slope]
  - rainfall in [min_rain, max_rain]
  - temperature in [min_temp, max_temp]
  - crust_age in [min_age, max_age]
  - allowed_region_types ⊆ {continental, oceanic, volcanic, ...}
  - noise_threshold < sampled_noise
```

Select first matching rule (or last, depending on priority).

**Noise Influence**:
Multiple Perlin layers provide spatial variation:
```
rock_noise = Σ(i=0 to layers) Perlin_i(position × scale_i) × weight_i
```

**Brush Stamping** (localized clusters):
From a seed tile, expand using distance-based flood fill:
```
For each tile in cluster:
  - Assign specific rock type (or modify existing)
  - Can write additional geological properties
```

**Configuration Parameters**:
- Rock type rule definitions (constraints per type)
- Noise layer parameters (scales, weights)
- Brush stamping parameters (radii, intensities)

**See**: flood_fills.md for brush stamping algorithm details

---

### 1.11 Microplates

**Purpose**: Add smaller plates in complex boundary regions

**Requirements**:
- Generated at triple junctions or complex boundaries
- Smaller than main plates
- Have own motion vectors
- Can have own features

**Algorithm**: Localized Plate Generation

**Conceptual Steps**:
1. **Identify Regions**: Find complex boundary areas (3+ plates meet)
2. **Size Determination**: Compute target microplate size
3. **Local Flood Fill**: Use same weighted expansion as main plates
4. **Feature Generation**: Apply boundary features at smaller scale

**Mathematical Concepts**:
Same as main plate generation, but:
- Smaller target sizes
- Limited expansion region
- May use different noise parameters

**Configuration Parameters**:
- Microplate size range
- Generation threshold (how complex must boundary be)
- Whether to enable this feature

---

## Phase 2: Oceanography & Initial Hydrology

**Priority**: **HIGH** - Refines elevation and water distribution

**Dependencies**: Phase 1 (Tectonics complete)

### Overview

This phase refines ocean depth, shapes coastlines, and begins water classification. It prepares the world for detailed hydrological simulation.

### 2.1 Ocean Elevation Generation

**Purpose**: Set detailed ocean floor topography

**Requirements**:
- Deeper ocean away from continents
- Shallower near rifts (younger, hotter crust)
- Natural variation (not flat)
- Respect crust age (older = deeper)

**Algorithm**: Parallel Per-Tile with Noise

**Conceptual Steps**:
1. **Base Depth**: Use crust age and type
2. **Noise Addition**: Add multi-octave fractal noise
3. **Constraint Application**: Respect min/max bounds

**Mathematical Concepts**:

**Age-Based Depth**:
```
base_depth = depth_offset + age_factor × crust_age^age_exponent
```

Typically:
- Younger crust (near rifts): -2000m to -3000m
- Older crust (far from rifts): -4000m to -6000m
- Exponent: 0.3 to 0.5 (depth increases with square/cube root of age)

**Noise Addition**:
```
depth_variation = Σ(i=0 to octaves) Perlin_i(position) × amplitude_i
final_depth = base_depth + depth_variation
```

**Configuration Parameters**:
- Age-depth relationship parameters
- Noise octaves and scales
- Min/max ocean depth clamps

**Parallelization**:
This is embarrassingly parallel (per-tile, no dependencies).

---

### 2.2 Ocean Smoothing

**Purpose**: Eliminate unrealistic sharp elevation changes in ocean

**Requirements**:
- Only affects ocean tiles
- Only affects tiles within depth band (e.g., -500m to -5000m)
- Iterative neighbor averaging
- Preserves overall depth patterns

**Algorithm**: Iterative Neighbor-Based Relaxation

**Conceptual Steps**:
1. **Iterate**: Typically 2-3 passes
2. **For Each Ocean Tile**:
   - Compute neighbor statistics (min, max, average elevation)
   - Check if tile is "outlier" (difference from neighbors exceeds tolerance)
   - If outlier: blend toward neighbor average
   - Otherwise: leave unchanged

**Mathematical Concepts**:

**Outlier Detection**:
```
neighbor_avg = (Σ neighbor_elevations) / neighbor_count
neighbor_min = min(neighbor_elevations)
neighbor_max = max(neighbor_elevations)

tolerance = abs(tile_elevation) × tolerance_factor

is_outlier = (tile_elevation + tolerance < neighbor_max) ||
             (neighbor_min < tile_elevation - tolerance)
```

**Blending**:
```
new_elevation = blend_weight × neighbor_avg +
                (1 - blend_weight) × tile_elevation
```

Typically `blend_weight ≈ 0.5` (average of self and neighbors)

**Configuration Parameters**:
- Number of iterations
- Depth band (only smooth tiles in range)
- Tolerance factor
- Blend weight

---

### 2.3 Coastal Lowering

**Purpose**: Erode coastal land tiles to create realistic coastlines

**Requirements**:
- Only affects land tiles adjacent to deep water
- Random variation in amount
- Creates beaches, cliffs, continental margins

**Algorithm**: Per-Tile Random Decrement

**Conceptual Steps**:
1. **Identify Coastal Tiles**: Land tiles with ocean neighbors
2. **Check Ocean Depth**: Find minimum neighbor elevation
3. **Apply Lowering**: If deep enough, reduce land elevation randomly

**Mathematical Concepts**:

**Coastal Detection**:
```
is_coastal = (tile.is_land) &&
             (any neighbor is ocean)
```

**Depth Check**:
```
ocean_neighbor_min = min(neighbor.elevation for neighbor in ocean_neighbors)
should_lower = (ocean_neighbor_min < depth_threshold)
```

**Random Lowering**:
```
lowering_amount = random(0, max_lowering) × scale_factor × base_amount
new_elevation = old_elevation - lowering_amount
```

**Configuration Parameters**:
- Ocean depth threshold (how deep must neighbor be)
- Max lowering amount
- Scale factors

---

### 2.4 Continental Shelves

**Purpose**: Create shallow ocean near continental coasts

**Requirements**:
- Shelf tiles are ocean adjacent to land (on same plate)
- Shallower than deep ocean
- Gradual slope from coast to deep ocean
- Multiple shelf layers (near, far, etc.)

**Algorithm**: Boundary Detection + Multi-Layer Stamping

**Conceptual Steps**:
1. **Find Candidates**: Ocean tiles adjacent to land on same plate
2. **Group by Plate/Boundary**: Each plate-boundary gets own shelf
3. **Multi-Layer Stamping**:
   - Inner shelf (very shallow)
   - Outer shelf (shallow)
   - Slope (transition to deep)

**Mathematical Concepts**:

**Coastline Detection**:
```
is_shelf_candidate = (tile.is_ocean) &&
                     (has land neighbor) &&
                     (neighbor.plate_id == tile.plate_id)
```

**Multi-Layer Zones**:
Using distance-based selection:
```
Zone 1 (Near Shelf):   distance in [0, r1]    → elevation ~ -100m
Zone 2 (Shelf):        distance in [r1, r2]   → elevation ~ -200m
Zone 3 (Slope):        distance in [r2, r3]   → elevation ~ -500m
(Beyond r3: deep ocean unchanged)
```

**Noise Masking**:
Use fractal noise to create irregular shelf edge:
```
effective_distance = actual_distance × noise_factor
```

**Configuration Parameters**:
- Shelf zone radii (r1, r2, r3)
- Target elevations per zone
- Noise parameters for masking
- Per-plate shelf parameters (different coastlines can have different shelves)

**See**: Stamping System (Phase 6) for detailed selection and application

---

### 2.5 Noise Augmentation (Ocean)

**Purpose**: Add final detailed variation to ocean floor

**Requirements**:
- Multiple passes (different scales)
- Deep ocean gets different treatment than shallow
- Creates ridges, plains, trenches

**Algorithm**: Multi-Pass Noise Addition

**Conceptual Steps**:
1. **Deep Ocean Pass**: Large-scale features (abyssal plains, ridges)
2. **Shallow Ocean Pass**: Smaller-scale features (sandbars, etc.)
3. **Each Pass**: Sample fractal noise, add to elevation

**Mathematical Concepts**:

**Multi-Scale Noise**:
```
Pass 1 (large scale):
  - Octaves: 6, low frequency
  - Amplitude: moderate
  
Pass 2 (fine detail):
  - Octaves: 12, high frequency
  - Amplitude: small
```

**Depth-Dependent Application**:
```
if depth < shallow_threshold:
    apply shallow_noise
else:
    apply deep_noise
```

**Noise Formula**:
```
augmentation = Σ(i=0 to octaves) amplitude[i] × Perlin_i(position × frequency[i])
new_elevation = old_elevation + augmentation × depth_scale
```

**Configuration Parameters**:
- Pass count and noise parameters per pass
- Depth thresholds for different treatments
- Amplitude and frequency scales

---

## Phase 3: Detailed Hydrology

**Priority**: **HIGH** - Creates rivers, lakes, and water systems

**Dependencies**: Phase 2 (Oceanography complete)

### Overview

This phase simulates water flow, creates lakes and rivers, and assigns ice coverage. These features significantly impact terrain appearance and later biome classification.

### 3.1 Waterflow Simulation

**Purpose**: Compute accumulated flow for each tile (used for river placement)

**Requirements**:
- Every land tile has flow value
- Flow accumulates downhill
- Must handle flat regions and depressions
- Deterministic ordering for consistency

**Algorithm**: Sorted Downhill Accumulation

**Conceptual Steps**:
1. **Sort Tiles**: Order by elevation (high to low)
2. **Initialize**: All tiles start with base flow value
3. **Process in Order**: For each tile:
   - Add increment to tile's flow
   - Find lowest neighbor
   - If neighbor is lower, transfer flow to neighbor

**Mathematical Concepts**:

**Sorting**: 
Sort land tiles by elevation (descending). This ensures we process uphill tiles before downhill tiles.

**Flow Accumulation**:
```
For tile in sorted_tiles:
    flow[tile] += flow_increment  // e.g., 1.0
    
    best_neighbor = argmin(elevation[neighbor] for neighbor in neighbors)
    
    if elevation[best_neighbor] < elevation[tile]:
        flow[best_neighbor] = flow[tile]  // Transfer (not add)
```

Note: Reference uses assignment, not addition. This creates discrete drainage paths.

**Flat Region Handling**:
Tiles with no lower neighbors keep their flow locally. May need special handling for closed basins.

**Configuration Parameters**:
- Flow increment per tile
- Whether to use parallel merge sort

**Output**:
Per-tile flow accumulation value (used by river carving).

---

### 3.2 Lake Generation

**Purpose**: Create inland lakes in appropriate depressions

**Requirements**:
- Lakes must start on land
- Lakes cannot start on ice
- Lake size determined by per-tile property
- Lake shape is irregular (fractal-ish)

**Algorithm**: Fractal Flood Fill from Seeds

**Conceptual Steps**:
1. **Seed Selection**: Identify tiles that should be lake centers
2. **Size Computation**: Calculate target lake size based on tile property
3. **Fractal Expansion**: Use noise-weighted flood fill to grow lake
4. **Tile Conversion**: Mark filled tiles as water

**Mathematical Concepts**:

**Seed Criteria**:
```
is_valid_seed = (tile.is_land) &&
                (tile.ice_thickness == 0) &&
                (tile has positive "lake driver" value)
```

**Target Size**:
```
lake_size = ceil((tile.lake_driver / scale_A) / scale_B)
```

**Fractal Flood Fill**:
Same algorithm as plate expansion, but:
- Starts from single seed
- Uses fractal noise for irregular borders
- Stops when target size reached
- Only claims eligible neighbors (land, no ice, correct plate)

**Tile Conversion**:
```
For each tile in lake:
    tile.is_water = true
    tile.waterbody_id = new_lake_id
```

**Configuration Parameters**:
- Lake size scaling factors
- Fractal noise parameters
- Eligibility constraints

**See**: flood_fills.md for fractal_flood_fill algorithm

---

### 3.3 River Carving

**Purpose**: Create river channels from high flow accumulation

**Requirements**:
- Rivers follow waterflow paths
- Rivers lower elevation (carve channel)
- Rivers connect uplands to coast/lakes
- River width/depth scales with flow

**Algorithm**: Pathfinding + Elevation Interpolation

**Conceptual Steps**:
1. **Candidate Selection**: Find high-flow tiles
2. **Pathfinding**: Use A* to find downhill path to coast/lake
3. **Elevation Carving**: Interpolate elevation along path to ensure downhill gradient
4. **Marking**: Set river flags on affected tiles

**Mathematical Concepts**:

**Candidate Threshold**:
```
is_river_candidate = (tile.flow_accumulation > threshold) &&
                     (tile meets terrain criteria)
```

**A* Downhill Pathfinding**:
```
Goal: Reach coast or existing water body
Cost function: Prefer downhill, avoid elevation gain
Heuristic: Distance to nearest water

Path = A_star(start, goal, cost, heuristic)
```

**Elevation Carving**:
Along path of length N:
```
For i in 0..N-1:
    progress = i / (N-1)
    target_elevation = start_elevation × (1 - progress) +
                      end_elevation × progress
    
    if tile[i].elevation > target_elevation:
        tile[i].elevation = target_elevation  // Lower to create channel
```

**River Width/Depth**:
Optional: Can widen river based on flow:
```
river_width_tiles = floor(flow_accumulation / width_threshold)
→ Apply carving to neighbors as well
```

**Configuration Parameters**:
- Flow threshold for river creation
- Carving amount (how much to lower elevation)
- Width scaling
- Pathfinding costs

**See**: rivers.md for detailed algorithm

---

### 3.4 Fjord Generation

**Purpose**: Create glacially-carved coastal inlets

**Requirements**:
- Fjords start at coastlines (ocean adjacent to land)
- Fjords cut inland following elevation
- Fjords convert land to water
- Fjords have characteristic deep, narrow profile

**Algorithm**: Stepwise Path Tracing + Carving

**Conceptual Steps**:
1. **Coastline Detection**: Find ocean-land boundaries
2. **Candidate Selection**: Random subset of boundaries
3. **Inland Tracing**: Walk inland (following elevation), building path
4. **Elevation Profile**: Apply U-shaped valley profile along path
5. **Water Conversion**: Convert path tiles to water (fjord)

**Mathematical Concepts**:

**Candidate Criteria**:
```
is_fjord_candidate = (tile.is_coast) &&
                     (tile.some_scalar > threshold) &&
                     (random_chance)
```

**Path Building**:
```
Start at coastal tile
For each step:
    candidates = neighbors that are:
        - Higher elevation (going inland)
        - Below elevation threshold
        - On same plate
    
    Choose candidate with highest elevation
    Add to path
    
    Stop when: max_length reached or no valid candidates
```

**Elevation Carving**:
```
For position i along path:
    progress = i / path_length
    
    base_depth = max_depth × (1 - progress)  // Shallow at head
    random_variation = random() × variation
    
    new_elevation = base_depth + random_variation
    tile[i].elevation = new_elevation
    tile[i].is_water = true
```

**Configuration Parameters**:
- Fjord density
- Maximum length
- Depth profile (deepest point, taper)
- Selection thresholds

---

### 3.5 Glacier/Ice Assignment

**Purpose**: Place glaciers and ice sheets

**Requirements**:
- Ice at high elevations and high latitudes
- Ice forms contiguous regions
- Different ice types (polar ice cap, mountain glacier, etc.)
- Thickness varies

**Algorithm**: Multi-Pass Weighted Flood Fill

**Conceptual Steps**:
1. **Pass 1**: Permanent ice (polar, very high elevation)
2. **Pass 2**: Seasonal/mountain ice
3. **Pass 3**: Additional latitude bands
4. **Pass 4**: Final coverage
Each pass uses different threshold and noise parameters.

**Mathematical Concepts**:

**Seed Selection**:
```
is_ice_seed = (latitude > lat_threshold) ||
              (elevation > elev_threshold) ||
              (combination_function(lat, elev, climate) > threshold)
```

**Weighted Expansion**:
Similar to plate expansion:
```
score = noise_value × weight - distance_penalty
```

But constrained by:
- Elevation (ice doesn't easily cross warm lowlands)
- Latitude (more ice toward poles)
- Temperature (from climate data if available)

**Ice Thickness**:
Can vary by:
- Distance from seed (thickest at center)
- Elevation (thickest at high elevations)
- Random variation

**Multiple Passes**:
Each pass has different parameters:
```
Pass 1: threshold = very_strict, weight = high
  → Only most suitable tiles
  
Pass 2: threshold = moderate, weight = medium
  → Expand to moderately suitable tiles
  
Pass 3, 4: gradually looser criteria
  → Fill in gaps, create transitions
```

**Configuration Parameters**:
- Per-pass thresholds and weights
- Latitude/elevation functions
- Noise parameters
- Ice thickness formulas

**See**: glacier.md for implementation details

---

### 3.6 Waterbody Management

**Purpose**: Track lakes, seas, and oceans as distinct objects

**Requirements**:
- Each contiguous water region is separate object
- Object tracks: perimeter, tiles, drain points
- Can query which waterbody a tile belongs to
- Supports water level changes (for later simulation)

**Algorithm**: Flood Fill + Perimeter Detection

**Conceptual Steps**:
1. **Identification**: Find all water regions via flood fill
2. **Object Creation**: Create waterbody record for each region
3. **Tile Assignment**: Mark each tile with waterbody ID
4. **Perimeter Computation**: Find tiles on edge of waterbody

**Mathematical Concepts**:

**Region Detection**:
```
For each unassigned water tile:
    tiles = flood_fill(start = tile, condition = is_water)
    waterbody_id = create_waterbody(tiles)
    
    For tile in tiles:
        tile.waterbody_id = waterbody_id
```

**Perimeter Detection**:
```
For each tile in waterbody:
    For each neighbor:
        if neighbor.waterbody_id != this_waterbody_id:
            → tile is on perimeter
```

**Drain Tile**:
For landlocked lakes, may identify lowest point on perimeter as potential drain/overflow point.

**Data Storage**:
Per waterbody:
- Tile set (or list of tile IDs)
- Perimeter set
- Water level (elevation)
- Type (ocean, sea, lake)
- Optional: drain tile reference

---

## Phase 4: Erosion & Soil Development

**Priority**: **MEDIUM** - Refines terrain and creates surface layer

**Dependencies**: Phase 3 (Hydrology complete)

### Overview

This phase simulates geological erosion processes and develops soil composition. These systems blend sharp features, create realistic terrain profiles, and prepare data for vegetation.

### 4.1 Thermal Erosion

**Purpose**: Smooth steep slopes through gravitational collapse

**Requirements**:
- Only affects land tiles
- Triggered by slope exceeding angle threshold
- Iterative process (multiple passes)
- Preserves overall terrain structure while smoothing extremes

**Algorithm**: Iterative Neighbor-Based Relaxation

**Conceptual Steps**:
1. **Iterate**: Typically 10 passes
2. **For Each Land Tile**:
   - Compute neighbor mean elevation
   - Compute steepness (angle from horizontal)
   - If too steep: blend toward neighbor mean
   - Else: no change

**Mathematical Concepts**:

**Slope Angle**:
```
For each neighbor:
    height_difference = abs(tile.elevation - neighbor.elevation)
    distance = tile_spacing  // or great-circle distance
    angle = atan2(height_difference, distance)

max_angle = max(angle for all neighbors)
```

**Relaxation**:
```
if max_angle > threshold_angle:
    neighbor_mean = average(neighbor.elevation)
    
    blend_factor = relaxation_weight
    new_elevation = (1 - blend_factor) × tile.elevation +
                    blend_factor × neighbor_mean
    
    // Enforce minimum elevation
    new_elevation = max(new_elevation, minimum_elevation)
```

**Configuration Parameters**:
- Number of iterations (typically 5-10)
- Threshold angle (in degrees or radians)
- Relaxation weight (0-1, typically 0.3-0.5)
- Minimum elevation clamp

---

### 4.2 Sorted Erosion (Waterflow-Based)

**Purpose**: Erode terrain based on waterflow patterns

**Requirements**:
- Uses accumulated flow from waterflow simulation
- Higher flow = more erosion
- Iterative process
- Can create valleys and gorges

**Algorithm**: Repeated Waterflow + Erosion Application

**Conceptual Steps**:
1. **Run Waterflow**: Compute or refresh flow accumulation
2. **Erosion Application**: For high-flow tiles, lower elevation
3. **Repeat**: Multiple iterations to create cumulative effect

**Mathematical Concepts**:

**Erosion Amount**:
```
erosion = erosion_coefficient × flow_accumulation^power

Where:
  - power typically 0.5 to 1.5 (non-linear relationship)
  - erosion_coefficient: tunable constant
```

**Elevation Update**:
```
new_elevation = old_elevation - erosion
new_elevation = max(new_elevation, sea_level - max_depth)
```

**Iteration**:
After each erosion pass, may need to recompute waterflow (as elevation changed).

**Configuration Parameters**:
- Number of iterations (typically 10-20)
- Erosion coefficient
- Flow-erosion power relationship
- Minimum elevation clamp

**Note**: This is computationally expensive due to repeated waterflow simulation.

---

### 4.3 Soil Initialization

**Purpose**: Create soil composition based on geology, climate, and terrain

**Requirements**:
- Multiple soil channels (clay, silt, sand, organic, etc.)
- Influenced by: bedrock, elevation, slope, rainfall, temperature
- Uses fractal noise for variation
- Weights sum to 1 (normalized distribution)

**Algorithm**: Parallel Multi-Noise Sampling

**Conceptual Steps**:
1. **Noise Generation**: Create multiple fractal noise fields
2. **Per-Tile Computation**: Sample noise + read climate + apply rules
3. **Normalization**: Ensure weights sum to 1
4. **Write Results**: Store in tile soil channels

**Mathematical Concepts**:

**Multi-Noise Sampling**:
```
For soil_channel i:
    base_value[i] = Σ(octaves) amplitude × Perlin_i(position × frequency)
```

**Climate Influence**:
```
moisture_factor = (january_rainfall + july_rainfall) / 2
temperature_factor = (january_temp + july_temp) / 2

soil_weights[clay] × (1 + moisture_factor × clay_wetness_affinity)
soil_weights[sand] × (1 + (1 - moisture_factor) × sand_dryness_affinity)
...
```

**Piecewise-Linear Interpolation** (Holdridge-style):
For parameter value `x`, find bucket:
```
if x < threshold[0]: weight = value[0]
elif x < threshold[1]: 
    t = (x - threshold[0]) / (threshold[1] - threshold[0])
    weight = lerp(value[0], value[1], t)
elif x < threshold[2]:
    ...
```

**Normalization**:
```
sum = Σ soil_weights[i]
for i in channels:
    soil_weights[i] /= sum
```

**Configuration Parameters**:
- Number of soil channels
- Noise parameters per channel
- Climate influence factors
- Threshold/value tables for interpolation

---

### 4.4 Aeolian Deposits (Wind-Driven Soil)

**Purpose**: Model wind-blown soil accumulation in dry regions

**Requirements**:
- Only affects very dry, low-elevation areas
- Rebalances soil composition toward sand/silt
- Uses rainfall as primary control

**Algorithm**: Per-Tile Conditional Adjustment

**Conceptual Steps**:
1. **Identify Candidates**: Low elevation, low rainfall
2. **Compute Dryness Factor**: Based on annual rainfall
3. **Rebalance Soil**: Shift composition toward wind-deposited types

**Mathematical Concepts**:

**Candidate Criteria**:
```
annual_rainfall = (jan_rainfall + jul_rainfall) × scale_factor
elevation_ok = (elevation < threshold)
rainfall_ok = (annual_rainfall < rainfall_threshold)

is_candidate = elevation_ok && rainfall_ok
```

**Dryness Factor**:
```
dryness = annual_rainfall / rainfall_threshold  // in [0, 1]
```

**Soil Rebalancing**:
```
wind_borne = soil_weights[silt] × dryness +
             soil_weights[sand] × dryness

total = soil_weights[clay] + wind_borne
total = max(total, minimum_total)  // prevent divide by zero

// Renormalize
soil_weights[clay] /= total
soil_weights[silt] = wind_borne / total
...
```

**Configuration Parameters**:
- Elevation threshold
- Rainfall threshold
- Which channels are wind-affected
- Minimum total clamp

---

### 4.5 Soil Blurring

**Purpose**: Smooth soil composition across neighbors

**Requirements**:
- Reduces sharp soil transitions
- Iterative process (typically 10 passes)
- Respects land/water boundaries
- Weighted average with neighbors

**Algorithm**: Iterative Neighbor Averaging

**Conceptual Steps**:
1. **Allocate Buffers**: For each soil channel
2. **Iterate** (e.g., 10 times):
   - For each land tile: compute weighted average with neighbors
   - Write averages to buffers
   - Copy buffers back to tiles
3. **Free Buffers**

**Mathematical Concepts**:

**Weighted Average**:
```
For each soil channel c:
    sum = tile.soil[c] × self_weight
    count = self_weight
    
    for neighbor in neighbors:
        if neighbor.is_land:
            sum += neighbor.soil[c] × neighbor_weight
            count += neighbor_weight
    
    buffer[c] = sum / max(count, minimum_count)
```

**Configuration Parameters**:
- Number of iterations
- Self weight vs neighbor weight
- Minimum count (prevents division by very small values)

---

### 4.6 Soil Thinning (Mountain Soils)

**Purpose**: Reduce soil thickness on steep slopes

**Requirements**:
- Mountains have thinner, rockier soil
- Based on slope or elevation
- Modulates existing soil values (doesn't add/remove channels)

**Algorithm**: Per-Tile Scaling Based on Terrain

**Conceptual Steps**:
1. **Compute Thinning Factor**: Based on slope/elevation
2. **Scale Soil Values**: Multiply channels by factor

**Mathematical Concepts**:

**Thinning Factor**:
```
slope = compute_slope(tile, neighbors)  // or use elevation directly

if slope > threshold:
    factor = 1 - ((slope - threshold) / (max_slope - threshold))
    factor = clamp(factor, min_factor, 1)
else:
    factor = 1  // no thinning
```

**Scaling**:
```
for channel in soil_channels:
    tile.soil[channel] *= factor
```

May need to renormalize after scaling.

**Configuration Parameters**:
- Slope thresholds
- Min/max thinning factors
- Whether to use slope or elevation

---

### 4.7 Sediment Transport

**Purpose**: Move soil downhill via waterflow

**Requirements**:
- Uses waterflow accumulation
- Erodes uphill areas, deposits downhill
- Creates alluvial fans, deltas
- Modifies soil composition

**Algorithm**: Waterflow-Driven Transfer

**Conceptual Steps**:
1. **Run Waterflow**: Get flow accumulation per tile
2. **Erosion Phase**: Remove soil from high-flow uplands
3. **Deposition Phase**: Add soil to low-flow lowlands
4. **Balance**: Total soil should be conserved (or adjust to budget)

**Mathematical Concepts**:

**Erosion** (high flow areas):
```
if flow_accumulation > erosion_threshold:
    erosion_amount = (flow_accumulation - threshold) × erosion_rate
    
    for channel in soil_channels:
        eroded[channel] = tile.soil[channel] × erosion_amount
        tile.soil[channel] -= eroded[channel]
```

**Deposition** (low flow areas, floodplains):
```
if elevation < deposition_threshold:
    deposition_amount = sediment_budget × local_factor
    
    for channel in soil_channels:
        tile.soil[channel] += deposited[channel]
```

**Configuration Parameters**:
- Erosion/deposition thresholds
- Transfer rates
- Sediment budget

**Note**: Implementation may use transient buffers to store eroded material before deposition.

---

## Phase 5: Climate & Biome Assignment

**Priority**: **HIGH** - Determines vegetation and appearance

**Dependencies**: Phase 4 (Soil complete), optionally Phase 3 (for water influence)

### Overview

This phase computes climate variables (temperature, rainfall) and uses them with terrain data to assign biomes and vegetation composition.

### 5.1 Climate Simulation (Grid-Based System)

**Purpose**: Generate temperature and rainfall patterns using a coarse 2D climate grid

**Requirements**:
- Compute climate on coarse grid (e.g., 32×32 or 64×64 cells)
- Each grid cell covers many world tiles
- Use bilinear interpolation when sampling for individual tiles
- Multi-pass computation with intermediate derived fields
- Support seasonal variation (at minimum: January and July)

**Architecture**: The reference implementation uses a **two-tier system**:
1. **Climate Grid**: Coarse 2D grid of 100-byte cells storing computed climate
2. **Per-Tile Sampling**: World tiles interpolate from surrounding grid cells

---

#### Climate Grid Structure

**Grid Dimensions**:
```
grid_width = configurable (e.g., 32, 64, 128)
total_cells = grid_width × grid_width
cell_size = 100 bytes
```

**Per-Cell Data Layout** (100 bytes):
```
Offset 0x00: u32 counter_A        // Ocean/non-land tile count
Offset 0x04: u32 counter_B        // Land tile count
Offset 0x24: f32 distance_to_sea  // Computed by distance propagation
Offset 0x30: f32 elevation_sum    // Sum of tile elevations in cell
Offset 0x34: f32 saldo_positive   // Seasonal radiation balance (summer)
Offset 0x38: f32 saldo_negative   // Seasonal radiation balance (winter)
Offset 0x3C: f32 continentality_A // Inlandness measure (direction 1)
Offset 0x40: f32 continentality_B // Inlandness measure (complement)
Offset 0x44: f32 continentality_C // Inlandness measure (direction 2)
Offset 0x48: f32 continentality_D // Inlandness measure (complement)
Offset 0x4C: f32 temperature_jan  // January temperature (final)
Offset 0x50: f32 temperature_jul  // July temperature (final)
... additional fields for rainfall, derived values, etc.
```

---

#### Tile-to-Grid Mapping (Bilinear Interpolation)

**Purpose**: Convert tile's lat/lon to 4 surrounding grid cells with weights

**Algorithm**: `get_cell_lerp_factors`

**Input**: Tile position (lat, lon), grid_width
**Output**: 4 cell indices + 4 weights

**Mathematical Concepts**:

**Coordinate Transformation**:
```
// Map lat/lon to grid space [0, grid_width)
grid_x = (lon + offset) × scale_factor
grid_y = (lat + offset) × scale_factor

// Get integer and fractional parts
x_base = floor(grid_x)
y_base = floor(grid_y)
x_frac = grid_x - x_base
y_frac = grid_y - y_base

// Handle wrapping/clamping at edges
x_base = clamp(x_base, 0, grid_width - 1)
y_base = clamp(y_base, 0, grid_width - 1)
x_next = (x_base + 1) % grid_width  // May wrap
y_next = (y_base + 1) % grid_width
```

**Four Surrounding Cells**:
```
cell_00 = (x_base, y_base)
cell_10 = (x_next, y_base)
cell_01 = (x_base, y_next)
cell_11 = (x_next, y_next)
```

**Bilinear Weights**:
```
w_00 = (1 - x_frac) × (1 - y_frac)
w_10 = x_frac × (1 - y_frac)
w_01 = (1 - x_frac) × y_frac
w_11 = x_frac × y_frac

// Weights sum to 1.0
```

**Sampling Climate Value**:
```
For any climate field (temperature, rainfall, etc.):
value = w_00 × cell_00.field +
        w_10 × cell_10.field +
        w_01 × cell_01.field +
        w_11 × cell_11.field
```

---

#### Climate Pipeline Stages

The climate system runs a **fixed sequence of stages**, some executed **twice** for stabilization:

**Stage Order**:
1. **Grid Elevation** - Bin tile elevations into grid cells
2. **Distance to Sea** - Compute ocean proximity via flood fill
3. **Saldo** - Compute seasonal radiation balance
4. **Saldo-Based Zones** - Classify cells into climate zones
5. **Continentality** - Compute inland influence via directional sweeps
6. **Post-Process** - Apply noise, finalize values, write to tiles

**Full Pipeline** (as executed):
```
1. Initialize/clear grid cells
2. Run all stages (first pass)
3. Build fractal noise sources
4. Run all stages again (second pass, with noise)
5. Apply noise modulation to grid
6. Cleanup
```

---

#### Stage 1: Calculate Grid Elevation

**Purpose**: Aggregate tile data into grid cells

**Algorithm**: Per-Tile Binning

**For each world tile**:
```
1. Get tile lat/lon from topology
2. Convert to grid coordinates (x, y)
3. Flatten to cell index: idx = y × grid_width + x
4. Accumulate into cell:
   - If tile.is_water: cell.counter_A++
   - If tile.is_land:  cell.counter_B++
   - cell.elevation_sum += tile.elevation
```

**Output**: Each cell knows:
- How many ocean tiles it contains
- How many land tiles it contains
- Sum of elevations (for computing average)

**Land/Water Fraction**:
```
land_fraction = counter_B / (counter_A + counter_B)
// Used by subsequent stages to identify land vs ocean cells
```

---

#### Stage 2: Calculate Distance to Sea

**Purpose**: Compute how far inland each cell is from ocean

**Algorithm**: Multi-Source BFS/Flood Fill

**Conceptual Steps**:
1. **Identify Ocean Cells**:
   ```
   For each cell:
       land_fraction = counter_B / (counter_A + counter_B)
       if land_fraction < threshold:  // Mostly ocean
           mark as ocean source
           distance_to_sea = 0
   ```

2. **Flood Fill Outward**:
   ```
   queue = [all ocean cells]
   visited = set(ocean cells)
   
   while queue not empty:
       cell = queue.pop()
       
       for neighbor in cell.neighbors:
           if neighbor not in visited:
               neighbor.distance_to_sea = cell.distance_to_sea + increment
               neighbor.distance_to_sea = min(neighbor.distance_to_sea, max_cap)
               visited.add(neighbor)
               queue.push(neighbor)
   ```

**Mathematical Concepts**:

**Distance Increment**:
```
new_distance = current_distance + step_size
new_distance = clamp(new_distance, 0, max_distance)
```

**Output**: `cell.distance_to_sea` (float at offset 0x24)
- 0.0 at ocean cells
- Increases inland
- Clamped at maximum

**Used By**: Later stages for ocean influence, rainfall patterns

---

#### Stage 3: Calculate Saldo (Seasonal Radiation Balance)

**Purpose**: Compute latitudinal/seasonal energy balance

**Algorithm**: Column-Based Integration

**For each column (fixed x)**:
```
pos_sum = 0
neg_sum = 0

For each row y in column:
    cell = grid[x, y]
    land_fraction = cell.counter_B / (cell.counter_A + cell.counter_B)
    
    // Compute latitude-like angle
    normalized_y = (y + offset) / grid_width + bias
    angle = normalized_y × scale_factor  // Maps to latitude range
    
    // Seasonal term
    seasonal_factor = sin(angle)
    seasonal_factor = mask_bits(seasonal_factor)  // Sign manipulation
    
    // Weight by land fraction
    contribution = (seasonal_factor × land_fraction) / grid_width
    
    // Accumulate by hemisphere/season
    if angle > 0:
        pos_sum += contribution  // Summer-like
    else:
        neg_sum += contribution  // Winter-like

// Write back to all cells in column
For each cell in column:
    cell.saldo_positive = pos_sum
    cell.saldo_negative = neg_sum
```

**Mathematical Concepts**:

**Seasonal Factor**:
```
latitude_normalized = (y / grid_width) + latitude_offset
angle = latitude_normalized × 2π  // Or similar scaling
seasonal_signal = sin(angle)
```

**Physical Interpretation**:
- Positive saldo: Summer hemisphere, more incoming radiation
- Negative saldo: Winter hemisphere, less radiation
- Magnitude: Depends on land fraction (land heats/cools more than ocean)

**Output**: 
- `cell.saldo_positive` (offset 0x34)
- `cell.saldo_negative` (offset 0x38)

---

#### Stage 4: Calculate Saldo-Based Zones

**Purpose**: Classify cells into climate zones using saldo values

**Algorithm**: Multi-Pass Threshold Classification

**Conceptual Steps** (via parallel callbacks with different thresholds):
```
Pass 1: Threshold = +6.0
    For each cell:
        if meets_criteria(cell.saldo, threshold):
            cell.zone_flags |= ZONE_A

Pass 2: Threshold = +3.0
    For each cell:
        if meets_criteria(cell.saldo, threshold):
            cell.zone_flags |= ZONE_B

Pass 3: Threshold = -3.0
    For each cell:
        if meets_criteria(cell.saldo, threshold):
            cell.zone_flags |= ZONE_C

... additional passes with thresholds like ±4.0, ±10.0, ±24.0
```

**Mathematical Concepts**:

**Zone Classification**:
```
// Tropical zone (high positive saldo year-round)
if saldo_positive > 24.0 && saldo_negative > 10.0:
    → Tropical

// Temperate zones (moderate seasonal variation)
elif saldo_positive > 10.0 && saldo_negative < -3.0:
    → Temperate with distinct seasons

// Arid zones (low saldo values)
elif abs(saldo_positive) < 3.0 && abs(saldo_negative) < 3.0:
    → Desert belt
```

**Output**: Zone flags/weights stored in cell (used for climate modulation)

---

#### Stage 5: Calculate Continentality

**Purpose**: Compute inland influence via directional sweeps

**Algorithm**: Two Opposing 1D Sweeps per Row

**Conceptual Steps**:
```
For each row (fixed y):
    // Forward sweep (west to east)
    accumulator_fwd = 0
    elevation_prev = 0
    
    For x from 0 to grid_width-1:
        cell = grid[x, y]
        land_fraction = cell.counter_B / (cell.counter_A + cell.counter_B)
        
        if land_fraction >= threshold:
            // Over land: accumulate continentality
            accumulator_fwd = min(cap, accumulator_fwd + growth_rate)
            
            // Elevation effect (mountains block maritime influence)
            elevation_avg = cell.elevation_sum / (cell.counter_A + cell.counter_B)
            elevation_delta = max(0, elevation_avg - elevation_prev)
            elevation_factor = elevation_delta / scale_constant
            
            accumulator_fwd += elevation_factor
            accumulator_fwd = clamp(accumulator_fwd, 0, max_value)
        else:
            // Over ocean: decay continentality
            accumulator_fwd = max(0, accumulator_fwd - decay_rate)
        
        cell.continentality_A = accumulator_fwd
        elevation_prev = elevation_avg
    
    // Backward sweep (east to west)
    // Same algorithm but reversed direction
    accumulator_bwd = 0
    For x from grid_width-1 down to 0:
        ... similar logic ...
        cell.continentality_B = accumulator_bwd
```

**Mathematical Concepts**:

**Growth Over Land**:
```
continentality = min(max_continental, continentality + step)
```

**Decay Over Ocean**:
```
continentality = max(0, continentality - decay_constant)
```

**Elevation Blocking**:
```
if elevation_current > elevation_previous:
    elevation_barrier = (elevation_current - elevation_previous) / scaling
    continentality += elevation_barrier
```

**Two-Direction Combination**:
```
// Forward sweep captures influence from western ocean
// Backward sweep captures influence from eastern ocean
// Combined they give "distance from any ocean" metric
```

**Output**: Multiple continentality fields per cell:
- `continentality_A, continentality_B` (one direction pair)
- `continentality_C, continentality_D` (perpendicular direction pair)

**Physical Interpretation**:
- Low continentality: Near ocean, maritime climate
- High continentality: Deep inland, continental climate
- Used to modulate: temperature extremes, rainfall patterns

---

#### Stage 6: Post-Process Tile Data

**Purpose**: Apply noise, combine fields, write final climate to tiles

**Algorithm**: Multi-Buffer Parallel Processing

**Conceptual Steps**:
1. **Build Noise Fields**:
   ```
   For multiple seeds:
       noise_sources = build_fractal_sources(seed, octaves=6)
   ```

2. **Apply to Grid** (parallel over cells):
   ```
   For each cell:
       // Sample all noise fields at cell position
       noise_temp_jan = sample_noise(position, sources_A)
       noise_temp_jul = sample_noise(position, sources_B)
       noise_rain_jan = sample_noise(position, sources_C)
       noise_rain_jul = sample_noise(position, sources_D)
       
       // Combine with computed fields
       base_temp_jan = latitude_temperature_curve(cell.latitude)
       base_temp_jul = base_temp_jan + seasonal_variation
       
       // Apply elevation lapse rate
       avg_elevation = cell.elevation_sum / (cell.counter_A + cell.counter_B)
       temp_adjustment = -lapse_rate × avg_elevation
       
       // Apply ocean/continent influence
       ocean_factor = exp(-cell.distance_to_sea / falloff)
       continent_factor = (cell.continentality_A + cell.continentality_B) / 2
       
       // Final temperature
       cell.temperature_jan = base_temp_jan + temp_adjustment + 
                              noise_temp_jan × (1 - ocean_factor) +
                              continent_factor × seasonal_amplitude
       
       // Similar for July, rainfall, etc.
   ```

3. **Write to Tiles** (parallel over tiles):
   ```
   For each tile:
       // Get surrounding 4 cells + weights
       (cells, weights) = get_cell_lerp_factors(tile.lat, tile.lon)
       
       // Bilinear interpolation
       tile.temperature_jan = Σ(weights[i] × cells[i].temperature_jan)
       tile.temperature_jul = Σ(weights[i] × cells[i].temperature_jul)
       tile.rainfall_jan = Σ(weights[i] × cells[i].rainfall_jan)
       tile.rainfall_jul = Σ(weights[i] × cells[i].rainfall_jul)
   ```

**Noise Parameters**:
- Multiple seed offsets (e.g., +0xEFE, +0x360E, +0x58D3E, +0x3A03B6)
- 6 octaves per noise field
- Different frequency/amplitude for temperature vs rainfall

---

#### Climate Grid Pipeline Summary

**Data Flow**:
```
World Tiles
    ↓ (bin into grid)
Grid: elevation_sum, land/water counts
    ↓ (flood fill)
Grid: distance_to_sea
    ↓ (integrate by latitude)
Grid: saldo_positive, saldo_negative
    ↓ (classify)
Grid: zone_flags
    ↓ (directional sweeps)
Grid: continentality fields
    ↓ (apply noise, combine)
Grid: final temperature, rainfall
    ↓ (bilinear interpolation)
World Tiles: climate values
```

**Key Advantages of Grid System**:
1. **Performance**: Compute expensive operations on coarse grid, not every tile
2. **Smooth Variation**: Bilinear interpolation ensures no sharp discontinuities
3. **Physical Realism**: Distance propagation, directional sweeps simulate real processes
4. **Tunable**: Grid resolution trades accuracy vs performance

---

#### Köppen Classification

**Purpose**: Classify climate into Köppen-style categories

**Algorithm**: Rule-Based Decision Tree

**Input** (per tile or grid cell):
- `temperature_jan`
- `temperature_jul`
- `rainfall_jan`
- `rainfall_jul`
- `is_land` flag

**Derived Quantities**:
```
annual_precip = rainfall_jan + rainfall_jul
temp_max = max(temperature_jan, temperature_jul)
temp_min = min(temperature_jan, temperature_jul)
temp_mean = (temperature_jan + temperature_jul) / 2

// Seasonal precipitation
if temp_jan > temp_jul:
    summer_precip = rainfall_jan
    winter_precip = rainfall_jul
else:
    summer_precip = rainfall_jul
    winter_precip = rainfall_jan

precip_seasonality = summer_precip / annual_precip
```

**Classification Steps**:

**Step 1: Arid Climates (B)**
```
// Compute aridity threshold
if precip_seasonality > 0.7:
    threshold = 20 × temp_mean + 280  // Summer rainfall
elif precip_seasonality < 0.3:
    threshold = 20 × temp_mean         // Winter rainfall
else:
    threshold = 20 × temp_mean + 140  // Evenly distributed

if annual_precip < threshold / 2:
    return DESERT (BW)
elif annual_precip < threshold:
    return STEPPE (BS)
```

**Step 2: Tropical Climates (A)**
```
if temp_min >= 18°C:
    if driest_month_precip >= 60mm:
        return TROPICAL_RAINFOREST (Af)
    elif driest_month_precip >= 100 - annual_precip/25:
        return TROPICAL_MONSOON (Am)
    else:
        return TROPICAL_SAVANNA (Aw/As)
```

**Step 3: Temperate/Continental (C/D)**
```
if temp_min > -3°C:  // Temperate (C)
    if driest_summer < 40mm && driest_summer < wettest_winter/3:
        return MEDITERRANEAN (Cs)
    elif driest_winter < wettest_summer/10:
        return TEMPERATE_DRY_WINTER (Cw)
    else:
        return TEMPERATE_HUMID (Cf)

elif temp_max > 10°C:  // Continental (D)
    // Similar subdivisions as C
    ...
```

**Step 4: Polar (E)**
```
if temp_max < 10°C:
    if temp_max > 0°C:
        return TUNDRA (ET)
    else:
        return ICE_CAP (EF)
```

**Output**: Single byte code representing Köppen category
- Used for visualization, biome hints, validation

---

#### Configuration Parameters

**Grid Resolution**:
- `grid_width`: 32-128 (higher = more accurate but slower)
- Trade-off: 64×64 = 4K cells is reasonable

**Physical Constants**:
- Lapse rate: ~6-7°C per 1000m elevation
- Ocean influence falloff: exponential with characteristic distance
- Continentality growth/decay rates
- Seasonal amplitude factors

**Noise Parameters**:
- Temperature noise: Lower amplitude, lower frequency
- Rainfall noise: Higher amplitude, higher frequency
- Seed offsets for independence

**Thresholds**:
- Ocean/land fraction threshold (for distance_to_sea)
- Saldo zone classification thresholds
- Köppen classification thresholds

---

#### Testing Requirements

**Grid System**:
- [ ] All tiles map to valid grid cells
- [ ] Bilinear weights sum to 1.0
- [ ] Grid edges handle wrapping correctly

**Pipeline Stages**:
- [ ] Distance_to_sea is 0 at ocean, increases inland
- [ ] Saldo values follow expected latitudinal pattern
- [ ] Continentality is low near coasts, high inland

**Final Climate**:
- [ ] Temperature decreases with latitude
- [ ] Temperature decreases with elevation (lapse rate)
- [ ] Rainfall higher near oceans, lower inland
- [ ] Köppen classifications are geographically reasonable

**Performance**:
- [ ] Grid computation scales with grid_width²
- [ ] Tile sampling scales with tile_count
- [ ] Total climate generation < 10% of total generation time

---

### 5.2 Potential Evapotranspiration (PET)

**Purpose**: Compute water demand (for Holdridge classification)

**Requirements**:
- Based on temperature
- Annual and/or monthly values
- Used with rainfall to determine moisture availability

**Mathematical Concepts**:

**Thornthwaite PET** (simplified):
```
For each month:
    temp_adjusted = max(0, temperature[month])
    heat_index += (temp_adjusted / 5)^1.514

annual_heat_index = Σ monthly_heat_indices

For each month:
    if temp_adjusted > 0:
        unadjusted_PET = 16 × (10 × temp_adjusted / heat_index)^exponent
        exponent = f(heat_index)  // polynomial
        PET[month] = unadjusted_PET × daylight_factor
    else:
        PET[month] = 0

annual_PET = Σ PET[month]
```

**PET/Rainfall Ratio**:
```
moisture_index = annual_rainfall / annual_PET

- moisture_index > 1: Surplus (wet)
- moisture_index < 1: Deficit (dry)
```

**Configuration Parameters**:
- Heat index calculation method
- Daylight adjustment factors
- Minimum temperature threshold

---

### 5.3 Ecology/Flora Initialization (Holdridge)

**Purpose**: Assign vegetation composition based on climate

**Requirements**:
- Uses annual_rainfall, annual_PET, temperature
- Produces vegetation weights (forest, grassland, desert, etc.)
- Smooth transitions between types

**Algorithm**: Holdridge Life Zones

**Conceptual Steps**:
1. **Compute Indices**:
   - Biotemperature (annual average, clamped to [0°C, 30°C])
   - Annual precipitation
   - PET ratio
2. **Classify into Holdridge Zone**: Based on thresholds
3. **Map to Vegetation Weights**: Each zone has characteristic composition

**Mathematical Concepts**:

**Biotemperature**:
```
biotemperature = average(monthly_temp for month where temp in [0, 30])
```

**Holdridge Classification** (simplified):
```
Use (biotemperature, annual_precip, PET_ratio) to index into 2D/3D table:
- Polar: biotemperature < 1.5°C
- Boreal: biotemperature 1.5-3°C
- Cool Temperate: 3-6°C
- Warm Temperate: 6-12°C
- Subtropical: 12-18°C
- Tropical: > 18°C

Cross with humidity provinces:
- Superarid: precip < 125mm
- Perarid: 125-250mm
- Arid: 250-500mm
- Semiarid: 500-1000mm
- Subhumid: 1000-2000mm
- Humid: 2000-4000mm
- Perhumid: 4000-8000mm
- Superhumid: > 8000mm
```

**Vegetation Weights**:
```
For each Holdridge zone, define weights:
Zone "Tropical Rainforest": {forest: 0.9, shrub: 0.05, grass: 0.05}
Zone "Desert": {desert: 0.8, shrub: 0.15, grass: 0.05}
...

Assign weights based on tile's zone classification.
```

**Noise Variation**:
Add spatial variation using fractal noise:
```
noise_factor = Perlin(position) × variation_amount
weights[i] = base_weights[i] × (1 + noise_factor)
normalize(weights)
```

**Configuration Parameters**:
- Holdridge threshold tables
- Vegetation weight definitions per zone
- Noise variation amount

---

### 5.4 Biome Assignment

**Purpose**: Classify each tile into discrete biome

**Requirements**:
- Uses climate (temp, rainfall), elevation, slope, soil
- Rule-based with priority order
- Each tile gets primary biome + optional variant

**Algorithm**: Sequential Rule Matching

**Conceptual Steps**:
1. **Compute Derived Values**: slope, avg_temp, avg_rain, etc.
2. **Iterate Biomes by Priority**: Check each biome's requirements
3. **First Match Wins**: Assign first biome that matches all rules
4. **Variant Assignment**: Optional secondary classification

**Mathematical Concepts**:

**Rule Evaluation**:
```
For biome in biomes_sorted_by_priority:
    if all_conditions_met(tile, biome.rules):
        tile.biome = biome.id
        break
```

**Condition Types**:
- Range checks: `value in [min, max]`
- Boolean checks: `is_land`, `has_ice`, etc.
- Computed checks: `slope > threshold`, `PET_ratio < threshold`

**Example Rules**:
```
Biome "Tropical Rainforest":
    - is_land == true
    - ice_thickness == 0
    - elevation in [-100, 2000]
    - min_temp > 18°C
    - annual_rain > 2000mm
    - slope < 30°

Biome "Tundra":
    - is_land == true
    - biotemperature < 3°C
    OR elevation > 3500m
    - annual_rain > 200mm
```

**Variant Mapping**:
Some biomes may have sub-variants based on additional criteria:
```
If biome == "Forest":
    if rainfall > 1500mm: variant = "Rainforest"
    elif rainfall > 1000mm: variant = "Wet Forest"
    else: variant = "Dry Forest"
```

**Configuration Parameters**:
- Biome definitions (rules, priorities)
- Threshold values
- Variant mappings

**See**: biome_gen.md for detailed rule structure

---

### 5.5 Slopeiness Calculation

**Purpose**: Helper for biome rules (average slope magnitude)

**Requirements**:
- Simple metric of terrain steepness
- Average over neighbors
- Used in biome matching

**Mathematical Concepts**:

**Slope to Each Neighbor**:
```
for neighbor in neighbors:
    elevation_diff = abs(tile.elevation - neighbor.elevation)
    slope_contribution = elevation_diff / distance
    
sum_slopes = Σ slope_contribution
avg_slope = sum_slopes / neighbor_count
```

Alternatively, can use bit-masked absolute value:
```
elevation_diff_masked = (tile.elevation - neighbor.elevation) & MASK
```
(Reference uses this pattern for sign-agnostic absolute value)

**Configuration Parameters**:
- Distance normalization factor

---

## Phase 6: Selector & Stamper Systems

**Priority**: **MEDIUM-HIGH** - Required for terrain features

**Dependencies**: Phase 0 (Foundation), used by Phases 1-2

### Overview

The selector/stamper system is a reusable framework for placing terrain features (mountains, rifts, volcanic fields, old mountains, etc.). It consists of:
- **Selectors**: Choose which tiles to affect
- **Stampers**: Apply elevation/terrain modifications to selected tiles
- **Masks**: Optional noise-based filtering of selections

This is used extensively in Phases 1-2 but deserves its own section due to complexity.

### 6.1 Distance Selector

**Purpose**: Select tiles within distance band from seed tiles

**Requirements**:
- Start from seed tile(s)
- Expand using BFS/flood fill
- Track "distance" (hops or accumulated randomized distance)
- Select tiles in [min_distance, max_distance] range
- Optional noise masking

**Algorithm**: BFS with Randomized Distance

**Conceptual Steps**:
1. **Initialize**: Seeds start with distance = 0
2. **Expand**: For each frontier tile, add neighbors with incremented distance
3. **Randomization**: Distance increment is randomized (not strict Euclidean)
4. **Filtering**: Collect tiles whose distance falls in target range
5. **Masking**: Optionally reject tiles based on noise expression

**Mathematical Concepts**:

**Distance Increment**:
```
base_step = random(0, 1) × 2 + bias
scaled_step = base_step × scale_factor
clamped_step = clamp(scaled_step, min_step, max_step)
new_distance = parent_distance + (clamped_step + offset) × multiplier
```

This creates irregular, organic-looking distance bands (not perfect circles).

**Collection**:
```
if min_distance <= tile_distance <= max_distance:
    add to selection
```

**Noise Masking**:
```
noise_value = evaluate_noise_expression(tile_position)
if noise_value >= threshold:
    include tile
else:
    reject tile
```

**Configuration Parameters**:
- Min/max distance
- Step randomization parameters
- Noise expression and threshold
- Land/water gating flags

**Use Cases**:
- Foothill zones around mountains (distance 3-5 from peak)
- Rift belts (distance 1-3 from rift line)
- Coastal shelves (distance 0-2 from coastline)

---

### 6.2 Area Selector

**Purpose**: Select connected region growing from seed

**Requirements**:
- Starts from seed tile(s)
- Expands through valid neighbors
- Uses randomized scoring for irregular shapes
- Respects land/water boundaries
- Optional noise masking

**Algorithm**: Best-First Expansion (Priority Queue)

**Conceptual Steps**:
1. **Initialize**: Seeds added to frontier with score = 0
2. **Expand**: Repeatedly take best (highest score) tile from frontier
3. **Scoring**: Compute score for each neighbor (noise + penalties)
4. **Gating**: Only accept neighbors meeting criteria
5. **Termination**: Stop when frontier empty or count reached

**Mathematical Concepts**:

**Score Calculation**:
```
position = get_tile_cartesian(tile)
noise = sample_fractal_noise(position)
distance = ||position - seed_position||

score = noise × weight - distance × distance_penalty
```

**Frontier Management**:
Use priority queue (heap) or shuffle and sort each iteration:
```
frontier = [(tile_id, score), ...]
frontier.sort_by_score(descending)
next_tile = frontier.pop()
```

**Gating**:
```
is_valid = meets_land_water_criteria(tile) &&
           passes_noise_mask(tile) &&
           same_region_type(tile, seed)
```

**Configuration Parameters**:
- Weight (importance of noise vs distance)
- Distance penalty factor
- Max selection count
- Noise parameters
- Gating criteria

**Use Cases**:
- Microplate territories
- Localized features (large volcanic fields)
- Region selection for specialized effects

---

### 6.3 Stamper Core

**Purpose**: Apply elevation/terrain modifications to selected tiles

**Requirements**:
- Takes selected tiles (from Distance or Area Selector)
- Applies "action" (raise, lower, set, smooth, etc.)
- Supports multiple concentric layers
- Can use falloff function (distance-based intensity)
- Optional per-tile flags

**Algorithm**: Multi-Layer Application

**Conceptual Steps**:
1. **For Each Layer**:
   - Run selector to get tiles
   - Compute per-tile intensity (falloff)
   - Apply action with intensity scaling
2. **Layer Order Matters**: Inner layers override outer

**Mathematical Concepts**:

**Falloff**:
```
For tiles at normalized distance t in [0, 1]:
  
Linear falloff:
    intensity = 1 - t

Power falloff:
    intensity = (1 - t)^exponent

Where t = actual_distance / max_radius
```

**Action Application**:
```
Action types:
- Add: elevation += amount × intensity
- Set: elevation = target × intensity + elevation × (1 - intensity)
- Max: elevation = max(elevation, target × intensity)
- Min: elevation = min(elevation, target × intensity)
```

**Tile Flags**:
```
if intensity > threshold_A:
    tile.flags |= FLAG_A

if intensity > threshold_B:
    tile.flags |= FLAG_B
```

**Configuration Parameters**:
- Layer definitions (selector params, action, falloff)
- Action-specific parameters (amount, target, etc.)
- Flag thresholds

**Use Cases**:
- Mountains: inner peak (high elevation), foothills (moderate), uplift (low)
- Rifts: inner rift (deep cut), valley (moderate), uplift belt (slight raise)
- Volcanoes: cone (raise to peak), slopes (falloff), base (slight)

---

### 6.4 Orogeny Stamper

**Purpose**: Specialized stamper for mountain ranges / orogenies

**Requirements**:
- Supports path-based features (mountain ridge along boundary)
- Creates multiple named layers (main belt, foothills, etc.)
- Records orogeny as object (for later reference)
- Integrates with plate boundary system

**Algorithm**: Path Tracing + Multi-Layer Stamping

**Conceptual Steps**:
1. **Path Generation**: Build spine of mountain range (along boundary)
2. **For Each Spine Segment**:
   - Define layers (belt1, belt2, foothills, uplift)
   - Use Distance Selector for each layer
   - Apply elevation actions
3. **Record Creation**: Create orogeny object tracking all affected tiles
4. **Tile Marking**: Write orogeny reference to tiles

**Mathematical Concepts**:

**Path from Boundary**:
```
Start at boundary tile
For each step:
    direction = boundary_tangent + random_deviation
    next_tile = neighbor_in_direction(current, direction)
    path.append(next_tile)
    
Stop when: reached end of boundary or max length
```

**Layer Definition**:
```
Layer "Main Belt":
    distance_range = [0, 2]
    action = raise_elevation(+800m)
    falloff = power(2.0)

Layer "Foothills 1":
    distance_range = [2, 5]
    action = raise_elevation(+300m)
    falloff = linear

Layer "Foothills 2":
    distance_range = [5, 8]
    action = raise_elevation(+100m)
    falloff = linear

Layer "Uplift":
    distance_range = [8, 12]
    action = raise_elevation(+50m)
    falloff = linear
```

**Configuration Parameters**:
- Path generation (deviation, step length)
- Layer definitions
- Elevation amounts
- Noise masking

**See**: OrogenyStamper.md, boundary_features.md

---

### 6.5 Brush Stamping

**Purpose**: Paint localized clusters (geological features, rock types)

**Requirements**:
- Start from seed tile
- Expand to nearby tiles (distance-limited)
- Mutate per-tile fields (not just elevation)
- Creates irregular shapes via noise
- Can write multiple tile channels

**Algorithm**: Distance-Based Flood Fill with Mutation

**Conceptual Steps**:
1. **Initialize**: Seed with distance = 0
2. **Expand**: BFS/best-first with randomized distance
3. **Per-Tile Mutation**:
   - Increment scalar accumulators
   - Write direction vectors (normals)
   - Set region markers
4. **Termination**: Stop at max distance

**Mathematical Concepts**:

**Distance Computation**:
Same as Distance Selector (randomized steps).

**Tile Writes**:
```
tile.scalar_A += base_value + distance_factor
tile.scalar_B += computed_value_from_noise
tile.direction_vector = compute_normal(position, seed)
tile.region_id = brush_id
```

**Use Cases**:
- Rock type clusters (geological provinces)
- Mineralization zones
- Feature markers (for gameplay)

**See**: brushes.md

---

### 6.6 Path Definition

**Purpose**: Generate sequences of tiles (ridges, rifts, hotspot tracks)

**Requirements**:
- Different strategies (random walk, directed, along boundary)
- Returns ordered list of tile IDs
- Can split into segments
- Used by Orogeny Stamper and other systems

**Strategies**:

**Random Walk**:
```
Start at seed
For N steps:
    choose random neighbor
    append to path
```

**Directed Walk**:
```
Start at seed
direction = target_direction
For N steps:
    choose neighbor closest to direction
    append to path
```

**Boundary Trace**:
```
Start at boundary tile
For N steps:
    choose neighbor that stays on boundary
    append to path
```

**Splitting**:
```
Given path of length L, split into K segments:
    segment_length = L / K (with randomization)
    
    segments = []
    current = 0
    for i in range(K):
        length = random(min_seg, max_seg)
        segments.append(path[current : current+length])
        current += length + random_skip
```

**Configuration Parameters**:
- Strategy type
- Direction vectors (for directed)
- Segment length ranges (for splitting)

**See**: PathDefinition.md, selectors.md (split_spines)

---

### 6.7 Point Definition

**Purpose**: Select single tile (for volcano, feature center, etc.)

**Requirements**:
- Different strategies (random, along path, at distance)
- Returns single tile ID
- Can have eligibility constraints

**Strategies**:

**Random in Area**:
```
candidates = tiles meeting criteria
return random(candidates)
```

**Along Path**:
```
path = generate_path(...)
progress = random(0, 1)
index = floor(progress × path.length)
return path[index]
```

**At Distance from Seed**:
```
selection = distance_selector.select(seed, target_distance, tolerance)
return random(selection)
```

**Configuration Parameters**:
- Strategy type
- Constraints (elevation, land/water, etc.)

**See**: PointDefinition.md

---

### 6.8 Noise Expression System

**Purpose**: Create complex noise-based masks and conditions

**Requirements**:
- Supports multiple noise types (Perlin, Simplex, etc.)
- Can combine expressions (add, multiply, threshold)
- Evaluates at tile positions
- Used for masking selections

**Expression Types**:

**Base Noise**:
```
Perlin(position, frequency, octaves)
```

**Operators**:
```
Add(expr_A, expr_B)
Multiply(expr_A, expr_B)
Threshold(expr, threshold)
Invert(expr)
```

**Evaluation**:
```
value = expression.evaluate(tile_position)
→ Returns float, typically in [-1, 1] or [0, 1]
```

**Use in Masking**:
```
selector.mask_expression = Threshold(
    Add(
        Perlin(pos, freq=0.1, octaves=6),
        Multiply(
            Perlin(pos, freq=0.5, octaves=4),
            0.3
        )
    ),
    threshold=0.5
)

For each tile:
    if selector.mask_expression.evaluate(tile.position) >= 0.5:
        include tile
```

**Configuration Parameters**:
- Per-expression parameters (frequency, octaves, etc.)
- Combination rules
- Threshold values

**See**: NoiseExpressionType.md, Noise.md

---

## Phase 7: Verification & Cleanup

**Priority**: **LOW** - Quality assurance

**Dependencies**: All previous phases

### Overview

This phase validates the generated world meets requirements and fixes any issues found.

### 7.1 Tectonics Verification

**Purpose**: Ensure plate system is consistent

**Requirements**:
- All tiles assigned to exactly one plate
- Plate sizes meet minimums
- Boundary tiles correctly classified
- No orphaned or disconnected regions

**Checks**:

**Plate Ownership**:
```
For each tile:
    assert tile.plate_id in valid_plate_ids
    assert world.plates[tile.plate_id].tiles.contains(tile)
```

**Plate Size**:
```
For each plate:
    assert plate.tile_count >= MINIMUM_PLATE_SIZE
```

**Boundary Consistency**:
```
For each boundary:
    tile_A = boundary.tile_A
    tile_B = boundary.tile_B
    
    assert tile_A.plate_id != tile_B.plate_id
    assert tiles are neighbors
    assert boundary.type in {Divergent, Convergent, Transform}
```

**Action on Failure**:
- Log error details
- Optionally: attempt automatic fix
- Return failure code (triggers retry in generation loop)

**See**: verification.md

---

### 7.2 Symmetry Breaking

**Purpose**: Eliminate artificial-looking exact-equality elevations

**Requirements**:
- Finds tiles with neighbors at exactly same elevation
- Adds small random perturbation
- Iterative until no exact matches remain

**Algorithm**: Iterative Per-Tile Perturbation

**Conceptual Steps**:
1. **Scan**: Find tiles with any neighbor at exact same elevation
2. **Perturb**: Add small random value to tile elevation
3. **Repeat**: Continue until scan finds no exact matches

**Mathematical Concepts**:

**Exact Match Detection**:
```
for neighbor in neighbors:
    if tile.elevation == neighbor.elevation:  // exact float equality
        → mark for perturbation
```

**Perturbation**:
```
delta = random(-max_delta, +max_delta)
tile.elevation += delta
```

Where `max_delta` is small (e.g., 0.01m to 1.0m)

**Termination**:
- After N iterations (safety limit)
- Or when scan finds no exact matches

**Configuration Parameters**:
- Max perturbation amount
- Max iterations

**See**: world_gen_stage.md (break_terrain_symmetry)

---

### 7.3 Tile Type Fixing

**Purpose**: Ensure land/water flags match elevation

**Requirements**:
- If elevation > sea_level, should be land
- If elevation <= sea_level, should be water
- Exceptions for ice (land on floating ice)

**Algorithm**: Simple Per-Tile Check and Fix

**Conceptual Steps**:
```
For each tile:
    expected_type = (tile.elevation > SEA_LEVEL) ? LAND : WATER
    
    if tile.land_water_flag != expected_type:
        tile.land_water_flag = expected_type
```

**Parallelizable**: Yes (no dependencies between tiles)

---

### 7.4 Final Validation

**Purpose**: Check world meets gameplay/design requirements

**Requirements**:
- Min/max land percentage
- Presence of certain biomes
- Connectivity (no isolated continents too small to play)
- Feature counts (volcanoes, mountains, etc.)

**Checks**:

**Land Percentage**:
```
land_count = count(tile for tile if tile.is_land)
land_percentage = land_count / total_tiles

assert MIN_LAND_PERCENT <= land_percentage <= MAX_LAND_PERCENT
```

**Biome Presence**:
```
For each required_biome:
    count = count(tile for tile if tile.biome == required_biome)
    assert count >= minimum_count[required_biome]
```

**Connectivity**:
```
largest_continent = find_largest_connected_land_mass()
assert largest_continent.size >= MIN_PLAYABLE_SIZE
```

**Action on Failure**:
- Return failure code
- Entire generation may retry with different seed

---

## Testing & Validation Strategy

### Per-Phase Testing

Each phase should have unit tests:

**Phase 0 (Foundation)**:
- RNG produces expected sequences
- Topology neighbor relationships are symmetric
- Tile data structures serialize correctly

**Phase 1 (Tectonics)**:
- Every tile assigned to plate
- Plate sizes roughly match weights
- Boundaries correctly classified

**Phase 3 (Hydrology)**:
- Waterflow only flows downhill
- Lakes don't overlap
- Rivers connect to coast/lakes

**Phase 5 (Climate/Biomes)**:
- Biomes match climate rules
- No impossible combinations (ice in tropics, etc.)

### Integration Testing

**Full Pipeline**:
- Run complete generation multiple times with same seed
- Verify identical output each time
- Verify different seeds produce different worlds

**Performance**:
- Profile each phase
- Identify bottlenecks (usually waterflow, flood fills)
- Ensure scalability (test with 10K, 100K tiles)

### Visual Validation

**Render Tests**:
- Elevation heatmap (check for artifacts)
- Plate boundaries (check coverage)
- Biome map (check transitions)
- Waterflow accumulation (check drainage patterns)

**Manual Review**:
- Generate multiple worlds, visually inspect
- Look for unrealistic patterns, sharp transitions
- Verify features look "natural"

---

## Performance Considerations

### Critical Optimizations

**Parallelization**:
- Per-tile operations: Parallel.For
- Flood fills: Harder to parallelize, focus on serial optimization
- Sorting: Use parallel sort for large tile counts

**Memory Management**:
- Pool transient buffers (avoid repeated allocations)
- Use appropriate data structures (hash tables for membership, arrays for iteration)
- Consider struct vs class for tiles (value vs reference types)

**Algorithmic**:
- Pre-compute neighbor relationships (don't recompute each time)
- Cache frequently-accessed values (tile positions, lat/lon)
- Use spatial partitioning for nearest-neighbor queries

**Profiling Hotspots**:
- Waterflow simulation (repeated sorting)
- Flood fills (many neighbor queries)
- Climate interpolation (trigonometric functions)
- Noise sampling (can be expensive with many octaves)

### Scalability

**Small Worlds** (1K-5K tiles):
- All algorithms fast enough
- Can use simpler data structures

**Medium Worlds** (10K-50K tiles):
- Need efficient flood fills
- Parallel processing beneficial
- Hash tables for large sets

**Large Worlds** (100K+ tiles):
- Must parallelize aggressively
- Consider chunking/spatial partitioning
- May need LOD or progressive generation

---

## Configuration & Tuning

### Parameter Categories

**World Size**:
- Tile count (determines resolution)
- Planet radius (affects distances, climate)

**Tectonics**:
- Plate count and weights
- Velocity ranges
- Boundary feature intensities

**Hydrology**:
- Lake density
- River threshold (flow to create river)
- Glacier extent

**Climate**:
- Temperature curve (equator to pole)
- Rainfall distribution
- Seasonality

**Biomes**:
- Threshold values for classification
- Priority order

### Tuning Workflow

1. **Start with Reference Values**: Use parameters from documentation
2. **Generate Test Worlds**: Create multiple worlds, vary one parameter at a time
3. **Visual Inspection**: Look for desired characteristics
4. **Iterate**: Adjust parameters, regenerate
5. **Document**: Record final parameters and rationale

### Common Issues & Fixes

**Too Many Small Plates**:
- Increase plate weights
- Decrease plate count
- Increase distance penalty in plate expansion

**Unrealistic Mountains**:
- Adjust convergent boundary elevation amounts
- Tune layer radii (foothills, uplift zones)
- Add more noise to masking

**Dry/Wet World**:
- Adjust rainfall latitudinal distribution
- Modify ocean influence falloff
- Change PET calculation parameters

**Boring Terrain**:
- Increase noise octaves/amplitude
- Add more old features
- Increase erosion effects

---

## Conclusion

This guide provides a conceptual framework for implementing a complete procedural world generation system. Key takeaways:

1. **Phase-Based Approach**: Each phase builds on previous results
2. **Deterministic Generation**: Same seed always produces same world
3. **Validation & Retry**: Some stages may fail and need retry with different parameters
4. **Reusable Systems**: Flood fills, selectors, stampers used throughout
5. **Mathematical Foundations**: Understand the algorithms (fractal noise, flood fills, interpolation)
6. **Tunable Parameters**: Extensive configuration for desired world characteristics
7. **Testing is Critical**: Verify each phase independently and integrated

Use this guide as a reference while implementing your C# generator. Focus on getting each phase working correctly before moving to the next. Test frequently and tune parameters iteratively.

Good luck with your implementation!

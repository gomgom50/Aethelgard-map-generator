# World Generation Pipeline Reference

*Complete implementation requirements from WorldGenDeveloperGuide.md*

---

## Phase 0: Foundation
- Initialize deterministic RNG
- Build HexSphereTopology
- Allocate Tile[] with all fields

## Phase 1: Tectonics
- Seed/expand microplates (fractal flood fill)
- Agglomerate into major plates
- Classify boundaries (Convergent/Divergent/Transform)
- Assign plate velocities
- Generate crust age (BFS from rifts)
- Assign rock types (35 geological types)

## Phase 2: Terrain Features
- Orogeny Stamper (mountain ranges)
- Continental shelves (multi-layer)
- Hotspots (mantle plume tracks)
- Volcanism
- Ancient features (old mountains, hills)

## Phase 3: Oceanography & Noise
- Ocean elevation (age-based + noise)
- Ocean smoothing (iterative relaxation)
- **Noise Augmentation** - Applied AFTER base elevation

## Phase 4: Hydrology
- Waterflow simulation (sorted downhill)
- Lake generation (fractal flood fill)
- River carving (A* + interpolation)
- Fjords, glaciers

## Phase 5: Erosion & Soil
- Thermal erosion (10+ passes)
- Sorted erosion (waterflow-based)
- Soil initialization (4 channels: clay, silt, sand, organic)
- Soil blurring (neighbor averaging)
- Sediment transport

## Phase 6: Climate & Biomes
- Climate grid (32-128 cells, bilinear interpolation)
- Distance to sea (multi-source BFS)
- Saldo/Continentality sweeps
- Köppen classification
- Holdridge zones
- Biome assignment

## Phase 7: Verification
- Tectonics consistency
- Symmetry breaking
- Tile type fixing
- Final validation

---

## Selector & Stamper System

| Component | Purpose |
|-----------|---------|
| Distance Selector | Tiles within distance band |
| Area Selector | Connected region expansion |
| Orogeny Stamper | Path-based mountain ranges |
| Brush Stamping | Rock type clusters |
| Noise Expression | Complex noise masks |

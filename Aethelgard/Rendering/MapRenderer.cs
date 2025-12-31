using System;
using System.Numerics;
using System.Threading.Tasks;
using Aethelgard.Simulation;
using Raylib_cs;

namespace Aethelgard.Rendering
{
    /// <summary>
    /// Renders WorldMap tiles to an equirectangular projection texture.
    /// Each pixel samples the underlying hex tile and colors based on tile data.
    /// </summary>
    public class MapRenderer : IDisposable
    {
        private Texture2D _texture;
        private Image _image;
        private bool _initialized;
        private WorldMap? _map;

        /// <summary>Width of the render texture in pixels.</summary>
        public int Width { get; private set; }

        /// <summary>Height of the render texture in pixels.</summary>
        public int Height { get; private set; }

        // Color configuration
        private static readonly Color ColorDeepWater = new Color(10, 30, 80, 255);
        private static readonly Color ColorWater = new Color(30, 80, 180, 255);
        private static readonly Color ColorShallowWater = new Color(50, 120, 200, 255);
        private static readonly Color ColorSand = new Color(210, 200, 150, 255);
        private static readonly Color ColorGrass = new Color(50, 150, 50, 255);
        private static readonly Color ColorForest = new Color(30, 100, 30, 255);
        private static readonly Color ColorRock = new Color(120, 100, 80, 255);
        private static readonly Color ColorSnow = new Color(240, 245, 255, 255);

        /// <summary>Available visualization modes.</summary>
        public enum RenderMode
        {
            Elevation,      // Color by tile elevation
            TileId,         // Color by tile index (debug)
            Face,           // Color by icosahedron face
            LandWater,      // Simple land/water mask
            PlateId,        // Color by plate assignment
            BoundaryType,   // Color by boundary classification
            CrustAge,       // Gradient by crust age
            CrustType,      // Continental vs Oceanic
            RockType,       // Color by bedrock type
            Subtiles,       // distinct random colors for each tile (stained glass look)
            SubtileNoise,   // Debug: Subtile System Noise Pattern
        }

        /// <summary>Current visualization mode.</summary>
        public RenderMode CurrentMode { get; set; } = RenderMode.Elevation;

        /// <summary>Flag indicating the texture needs regeneration.</summary>
        public bool IsDirty { get; set; } = true;

        /// <summary>Flag indicating tile geometry needs recalculation (subtile params changed).</summary>
        public bool GeometryDirty { get; set; } = true;

        /// <summary>When true, draws subtle grey borders between subtiles.</summary>
        public bool ShowSubtileBorders { get; set; } = false;

        // Cached tile ID per pixel - expensive to compute, reused across mode changes
        private int[]? _cachedTileIds;
        private int[]? _cachedSubtileIds;
        private float[]? _cachedElevations;  // Blended elevation per pixel

        // Border color for subtile edges
        private static readonly Color SubtileBorderColor = new Color(80, 80, 80, 120);

        /// <summary>
        /// Initializes the renderer with specified resolution and WorldMap.
        /// </summary>
        public void Initialize(int width, int height, WorldMap map)
        {
            Width = width;
            Height = height;
            _map = map;

            _image = Raylib.GenImageColor(width, height, Color.Black);
            _texture = Raylib.LoadTextureFromImage(_image);
            _initialized = true;
            IsDirty = true;
        }

        /// <summary>
        /// Updates the render texture from current tile data.
        /// Uses caching: geometry changes rebuild tile IDs, mode changes just remap colors.
        /// </summary>
        public void Update()
        {
            if (!_initialized || _map == null || !IsDirty) return;

            int pixelCount = Width * Height;

            // Rebuild tile ID cache if geometry changed (subtile params, topology, etc)
            if (GeometryDirty || _cachedTileIds == null || _cachedSubtileIds == null || _cachedElevations == null)
            {
                _cachedTileIds = new int[pixelCount];
                _cachedSubtileIds = new int[pixelCount];
                _cachedElevations = new float[pixelCount];

                // This is the expensive part - only done when geometry changes
                Parallel.For(0, Height, py =>
                {
                    for (int px = 0; px < Width; px++)
                    {
                        int idx = py * Width + px;
                        Vector3 point = GetPointAtPixel(px, py);
                        _cachedSubtileIds[idx] = _map.Subtiles.GetSubtileId(point);
                        _cachedTileIds[idx] = _map.Subtiles.GetParentTile(_cachedSubtileIds[idx]);
                        _cachedElevations[idx] = _map.Subtiles.GetBlendedElevation(point);
                    }
                });
                GeometryDirty = false;
            }

            // Now render colors using cached IDs - this is FAST
            unsafe
            {
                Color* pixels = (Color*)_image.Data;

                // Pre-compute tile colors for tile-based modes
                var tileColors = new Color[_map.Topology.TileCount];
                if (CurrentMode != RenderMode.SubtileNoise && CurrentMode != RenderMode.Subtiles && CurrentMode != RenderMode.Elevation)
                {
                    Parallel.For(0, _map.Topology.TileCount, tileId =>
                    {
                        ref readonly Tile tile = ref _map.Tiles[tileId];
                        tileColors[tileId] = CurrentMode switch
                        {
                            RenderMode.TileId => GetColorForTileId(tileId),
                            RenderMode.Face => GetColorForFace(_map.Topology.GetTileFace(tileId)),
                            RenderMode.LandWater => tile.IsLand
                                ? new Color(80, 160, 80, 255)
                                : new Color(30, 60, 150, 255),
                            RenderMode.PlateId => GetColorForPlateId(tile.PlateId),
                            RenderMode.BoundaryType => GetColorForBoundaryType(tile.BoundaryType),
                            RenderMode.CrustAge => GetColorForCrustAge(tile.CrustAge),
                            RenderMode.CrustType => tile.CrustType == 1
                                ? new Color(180, 140, 100, 255)
                                : new Color(40, 80, 140, 255),
                            RenderMode.RockType => GetColorForRockType(tile.RockType),
                            _ => Color.Magenta
                        };
                    });
                }

                // Map cached IDs to colors (FAST - just array lookups)
                Parallel.For(0, Height, py =>
                {
                    for (int px = 0; px < Width; px++)
                    {
                        int idx = py * Width + px;

                        if (CurrentMode == RenderMode.SubtileNoise)
                        {
                            // Still need to compute noise per-pixel for this debug mode
                            Vector3 point = GetPointAtPixel(px, py);
                            Vector3 noise = _map.Subtiles.GetNoiseVector(point);
                            byte r = (byte)((noise.X * 0.5f + 0.5f) * 255);
                            byte g = (byte)((noise.Y * 0.5f + 0.5f) * 255);
                            byte b = (byte)((noise.Z * 0.5f + 0.5f) * 255);
                            pixels[idx] = new Color(r, g, b, (byte)255);
                        }
                        else if (CurrentMode == RenderMode.Subtiles)
                        {
                            // Use cached subtile ID
                            pixels[idx] = GetColorForSubtiles(_cachedSubtileIds[idx]);
                        }
                        else if (CurrentMode == RenderMode.Elevation)
                        {
                            // Use cached blended elevation
                            pixels[idx] = GetColorForElevation(_cachedElevations[idx]);
                        }
                        else
                        {
                            // Use cached parent tile ID
                            pixels[idx] = tileColors[_cachedTileIds[idx]];
                        }
                    }
                });

                // Overlay subtile borders if enabled (second pass for edge detection)
                if (ShowSubtileBorders)
                {
                    // Cache subtile IDs for border detection
                    int[] subtileIds = new int[Width * Height];
                    Parallel.For(0, Height, py =>
                    {
                        for (int px = 0; px < Width; px++)
                        {
                            Vector3 point = GetPointAtPixel(px, py);
                            subtileIds[py * Width + px] = _map.Subtiles.GetSubtileId(point);
                        }
                    });

                    // Detect borders (check right and down neighbors)
                    Parallel.For(0, Height - 1, py =>
                    {
                        for (int px = 0; px < Width - 1; px++)
                        {
                            int idx = py * Width + px;
                            int current = subtileIds[idx];
                            int right = subtileIds[idx + 1];
                            int down = subtileIds[idx + Width];

                            if (current != right || current != down)
                            {
                                // Blend border color with existing pixel
                                Color existing = pixels[idx];
                                pixels[idx] = BlendColor(existing, SubtileBorderColor);
                            }
                        }
                    });
                }

                Raylib.UpdateTexture(_texture, _image.Data);
            }

            IsDirty = false;
        }

        private static Color BlendColor(Color baseColor, Color overlay)
        {
            float alpha = overlay.A / 255f;
            return new Color(
                (int)(baseColor.R * (1 - alpha) + overlay.R * alpha),
                (int)(baseColor.G * (1 - alpha) + overlay.G * alpha),
                (int)(baseColor.B * (1 - alpha) + overlay.B * alpha),
                255
            );
        }

        /// <summary>
        /// Gets the 3D point on the unit sphere for a given pixel coordinate.
        /// Uses Equirectangular projection (linear lat/lon mapping).
        /// </summary>
        public Vector3 GetPointAtPixel(int px, int py)
        {
            // Equirectangular Projection: Linear mapping of pixel to lat/lon
            float px_norm = px / (float)(Width - 1);
            float py_norm = py / (float)(Height - 1);

            // Longitude: -180° (left) to +180° (right)
            float lon = px_norm * 360f - 180f;

            // Latitude: +90° (top) to -90° (bottom) - Equirectangular is linear
            float lat = 90f - py_norm * 180f;

            // Convert spherical coordinate to 3D point (Y is Up)
            float phi = (90f - lat) * MathF.PI / 180f;
            float theta = lon * MathF.PI / 180f;

            float x = MathF.Sin(phi) * MathF.Cos(theta);
            float y = MathF.Cos(phi);
            float z = MathF.Sin(phi) * MathF.Sin(theta);

            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Gets the tile ID that owns the specified pixel coordinate.
        /// Uses SubtileSystem to provide consistent organic borders matching the render.
        /// </summary>
        public int GetTileAtPixel(int px, int py)
        {
            if (_map == null) return 0;
            Vector3 point = GetPointAtPixel(px, py);
            return _map.Subtiles.GetSubtileOwner(point);
        }

        /// <summary>
        /// Draws the map texture to the specified viewport.
        /// </summary>
        public void Draw(int x, int y, int w, int h)
        {
            if (!_initialized) return;

            Raylib.DrawTexturePro(
                _texture,
                new Rectangle(0, 0, Width, Height),
                new Rectangle(x, y, w, h),
                new System.Numerics.Vector2(0, 0),
                0.0f,
                Color.White
            );
        }

        public void DrawPro(Rectangle sourceRect, Rectangle destRect)
        {
            if (!_initialized) return;

            Raylib.DrawTexturePro(
                _texture,
                sourceRect,
                destRect,
                new System.Numerics.Vector2(0, 0),
                0.0f,
                Color.White
            );
        }

        /// <summary>
        /// Maps elevation (meters) to color with smooth gradients.
        /// </summary>
        private static Color GetColorForElevation(float elevation)
        {
            // Elevation in meters: -5000 to +5000 typical range

            if (elevation < -3000)
            {
                // Deep ocean
                return ColorDeepWater;
            }
            else if (elevation < -500)
            {
                // Ocean gradient
                float t = (elevation + 3000) / 2500f;
                return LerpColor(ColorDeepWater, ColorWater, t);
            }
            else if (elevation < 0)
            {
                // Shallow water
                float t = (elevation + 500) / 500f;
                return LerpColor(ColorWater, ColorShallowWater, t);
            }
            else if (elevation < 200)
            {
                // Coastal lowland
                float t = elevation / 200f;
                return LerpColor(ColorSand, ColorGrass, t);
            }
            else if (elevation < 1000)
            {
                // Grass to forest
                float t = (elevation - 200) / 800f;
                return LerpColor(ColorGrass, ColorForest, t);
            }
            else if (elevation < 2500)
            {
                // Forest to rock
                float t = (elevation - 1000) / 1500f;
                return LerpColor(ColorForest, ColorRock, t);
            }
            else if (elevation < 4000)
            {
                // Rock to snow
                float t = (elevation - 2500) / 1500f;
                return LerpColor(ColorRock, ColorSnow, t);
            }
            else
            {
                return ColorSnow;
            }
        }

        /// <summary>
        /// Generate color for tile ID using golden ratio hue cycling.
        /// </summary>
        private static Color GetColorForTileId(int tileId)
        {
            float hue = (tileId * 0.618033988749895f) % 1.0f;
            return HslToRgb(hue, 0.7f, 0.5f);
        }

        private static Color GetColorForSubtiles(int tileId)
        {
            // Pseudo-random hash for high contrast
            uint h = (uint)tileId;
            h = ((h >> 16) ^ h) * 0x45d9f3b;
            h = ((h >> 16) ^ h) * 0x45d9f3b;
            h = (h >> 16) ^ h;

            byte r = (byte)(h & 0xFF);
            byte g = (byte)((h >> 8) & 0xFF);
            byte b = (byte)((h >> 16) & 0xFF);
            return new Color(r, g, b, (byte)255);
        }

        /// <summary>
        /// Get distinct color for each icosahedron face (0-19).
        /// </summary>
        private static Color GetColorForFace(int faceId)
        {
            Color[] faceColors = new Color[]
            {
                new Color(255, 100, 100, 255), new Color(255, 180, 100, 255),
                new Color(255, 255, 100, 255), new Color(180, 255, 100, 255),
                new Color(100, 255, 100, 255), new Color(100, 255, 180, 255),
                new Color(100, 255, 255, 255), new Color(100, 180, 255, 255),
                new Color(100, 100, 255, 255), new Color(180, 100, 255, 255),
                new Color(255, 100, 255, 255), new Color(255, 100, 180, 255),
                new Color(200, 80, 80, 255),   new Color(200, 140, 80, 255),
                new Color(200, 200, 80, 255),  new Color(80, 200, 80, 255),
                new Color(80, 200, 200, 255),  new Color(80, 80, 200, 255),
                new Color(140, 80, 200, 255),  new Color(200, 80, 140, 255),
            };
            return faceId >= 0 && faceId < 20 ? faceColors[faceId] : Color.Gray;
        }

        /// <summary>
        /// Color for plate ID (-1 = unassigned = gray).
        /// </summary>
        private static Color GetColorForPlateId(int plateId)
        {
            if (plateId < 0) return new Color(50, 50, 50, 255);
            return GetColorForTileId(plateId * 17); // Spread for visibility
        }

        /// <summary>
        /// Color for boundary type.
        /// </summary>
        private static Color GetColorForBoundaryType(BoundaryType bt)
        {
            return bt switch
            {
                BoundaryType.Convergent => new Color(220, 60, 60, 255),   // Red
                BoundaryType.Divergent => new Color(60, 100, 220, 255),   // Blue
                BoundaryType.Transform => new Color(220, 200, 60, 255),   // Yellow
                _ => new Color(80, 80, 80, 255)                           // Gray (no boundary)
            };
        }

        /// <summary>
        /// Color for crust age (0=young/bright, 1=old/dark).
        /// </summary>
        private static Color GetColorForCrustAge(float age)
        {
            age = Math.Clamp(age, 0f, 1f);
            // Young = bright orange, Old = dark red-brown
            return LerpColor(
                new Color(255, 200, 100, 255),  // Young (bright)
                new Color(60, 30, 20, 255),     // Old (dark)
                age
            );
        }

        /// <summary>
        /// Color for rock type.
        /// </summary>
        private static Color GetColorForRockType(RockType rt)
        {
            return rt switch
            {
                // Igneous - Intrusive (reds/pinks)
                RockType.Granite => new Color(220, 180, 180, 255),
                RockType.Gabbro => new Color(80, 80, 100, 255),
                // Igneous - Extrusive (dark grays/blacks)
                RockType.Basalt => new Color(50, 50, 60, 255),
                RockType.Rhyolite => new Color(200, 180, 160, 255),
                // Sedimentary (tans/browns)
                RockType.Sandstone => new Color(210, 180, 140, 255),
                RockType.Limestone => new Color(220, 220, 200, 255),
                RockType.Shale => new Color(120, 100, 80, 255),
                // Metamorphic (grays/blues)
                RockType.Gneiss => new Color(150, 150, 170, 255),
                RockType.Schist => new Color(130, 140, 120, 255),
                RockType.Marble => new Color(240, 240, 245, 255),
                RockType.Quartzite => new Color(230, 220, 210, 255),
                _ => new Color(100, 100, 100, 255)
            };
        }

        private static Color LerpColor(Color a, Color b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return new Color(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t),
                255
            );
        }

        private static Color HslToRgb(float h, float s, float l)
        {
            float c = (1 - Math.Abs(2 * l - 1)) * s;
            float x = c * (1 - Math.Abs((h * 6) % 2 - 1));
            float m = l - c / 2;

            float r, g, b;
            int sector = (int)(h * 6);

            switch (sector % 6)
            {
                case 0: r = c; g = x; b = 0; break;
                case 1: r = x; g = c; b = 0; break;
                case 2: r = 0; g = c; b = x; break;
                case 3: r = 0; g = x; b = c; break;
                case 4: r = x; g = 0; b = c; break;
                default: r = c; g = 0; b = x; break;
            }

            return new Color(
                (int)((r + m) * 255),
                (int)((g + m) * 255),
                (int)((b + m) * 255),
                255
            );
        }

        public void Dispose()
        {
            if (_initialized)
            {
                Raylib.UnloadTexture(_texture);
                Raylib.UnloadImage(_image);
            }
        }
    }
}

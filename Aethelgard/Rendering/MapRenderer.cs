using System;
using System.Numerics;
using System.Threading.Tasks;
using Aethelgard.Simulation;
using Raylib_cs;

namespace Aethelgard.Rendering
{
    /// <summary>
    /// Renders WorldMap tiles using a GPU-accelerated Subtile LUT approach.
    /// 1. LUT Texture (Static/Rare Update): Pixels -> SubtileID (RGB Encoded)
    /// 2. Value Texture (Dynamic Update): SubtileID -> Color/Value
    /// 3. Shader: Decodes pixel's subtile ID, fetches value, outputs color.
    /// </summary>
    public class MapRenderer : IDisposable
    {
        private Texture2D _lutTexture;
        private Image _lutImage;

        private Texture2D _valueTexture;
        private Image _valueImage;

        private Shader _shader;
        private int _locLutTexture;
        private int _locValueTexture;
        private int _locSubtileCount;
        private int _locValueTextureSize;
        private int _locUseRamp;

        private bool _initialized;
        private WorldMap? _map;

        // Cache to detect mode changes
        private RenderMode _lastRenderMode;

        /// <summary>Width of the render texture in pixels.</summary>
        public int Width { get; private set; }

        /// <summary>Height of the render texture in pixels.</summary>
        public int Height { get; private set; }

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
            SubtileNoise,   // Debug: Subtile System Noise Pattern (Special case, heavy calc)
        }

        /// <summary>Current visualization mode.</summary>
        public RenderMode CurrentMode { get; set; } = RenderMode.Elevation;

        /// <summary>Flag indicating the value texture needs regeneration.</summary>
        public bool IsDirty { get; set; } = true;

        /// <summary>Flag indicating the LUT needs regeneration (geometry/resolution changed).</summary>
        public bool GeometryDirty { get; set; } = true;

        /// <summary>When true, draws subtle borders in shader (TODO).</summary>
        public bool ShowSubtileBorders { get; set; } = false;

        // Color configuration
        private static readonly Color ColorDeepWater = new Color(10, 30, 80, 255);
        private static readonly Color ColorWater = new Color(30, 80, 180, 255);
        private static readonly Color ColorShallowWater = new Color(50, 120, 200, 255);
        private static readonly Color ColorSand = new Color(210, 200, 150, 255);
        private static readonly Color ColorGrass = new Color(50, 150, 50, 255);
        private static readonly Color ColorForest = new Color(30, 100, 30, 255);
        private static readonly Color ColorRock = new Color(120, 100, 80, 255);
        private static readonly Color ColorSnow = new Color(240, 245, 255, 255);

        /// <summary>
        /// Initializes the renderer with specified resolution and WorldMap.
        /// </summary>
        public void Initialize(int width, int height, WorldMap map)
        {
            Width = width;
            Height = height;
            _map = map;

            // Load Shader
            try
            {
                // Assuming standard path, might need adjustment based on project structure
                _shader = Raylib.LoadShader("Content/Shaders/map_shader.vs", "Content/Shaders/map_shader.fs");

                // Get Uniform Locations
                _locLutTexture = Raylib.GetShaderLocation(_shader, "lutTexture");
                _locValueTexture = Raylib.GetShaderLocation(_shader, "valueTexture");
                _locSubtileCount = Raylib.GetShaderLocation(_shader, "subtileCount");
                _locValueTextureSize = Raylib.GetShaderLocation(_shader, "valueTextureSize");
                _locUseRamp = Raylib.GetShaderLocation(_shader, "useRamp");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to load map shader: {e.Message}");
            }

            // Init images (empty for now)
            _lutImage = Raylib.GenImageColor(width, height, Color.Black);
            _lutTexture = Raylib.LoadTextureFromImage(_lutImage);
            Raylib.SetTextureFilter(_lutTexture, TextureFilter.Point); // CRITICAL: No interpolation for IDs

            // Init value texture (placeholder size, will resize in UpdateValues)
            _valueImage = Raylib.GenImageColor(1, 1, Color.Magenta);
            _valueTexture = Raylib.LoadTextureFromImage(_valueImage);
            Raylib.SetTextureFilter(_valueTexture, TextureFilter.Point);

            _initialized = true;
            IsDirty = true;
            GeometryDirty = true;
        }

        /// <summary>
        /// Main update loop.
        /// 1. Rebuilds LUT if Geometry changed (Very Expensive, Rare)
        /// 2. Rebuilds Value Texture if Mode changed (Cheap, Frequent)
        /// </summary>
        public void Update()
        {
            if (!_initialized || _map == null) return;

            // 1. Geometry / Topology Change -> Rebuild LUT
            if (GeometryDirty)
            {
                UpdateLut();
                GeometryDirty = false;
                IsDirty = true; // Values need check too (e.g. subtile count changed)
            }

            // 2. Data / Mode Change -> Rebuild Value Texture
            if (IsDirty || CurrentMode != _lastRenderMode)
            {
                UpdateValues();
                IsDirty = false;
                _lastRenderMode = CurrentMode;
            }
        }

        /// <summary>
        /// Rebuilds the LUT (Pixels -> Subtile ID).
        /// O(Width * Height). Expensive. Do only on resize/topology change.
        /// </summary>
        private void UpdateLut()
        {
            // Resize if needed
            if (_lutImage.Width != Width || _lutImage.Height != Height)
            {
                Raylib.UnloadTexture(_lutTexture);
                Raylib.UnloadImage(_lutImage);
                _lutImage = Raylib.GenImageColor(Width, Height, Color.Black);
                _lutTexture = Raylib.LoadTextureFromImage(_lutImage);
                Raylib.SetTextureFilter(_lutTexture, TextureFilter.Point);
            }

            unsafe
            {
                Color* pixels = (Color*)_lutImage.Data;

                // Parallel loop to calculate Subtile ID for every pixel
                Parallel.For(0, Height, py =>
                {
                    for (int px = 0; px < Width; px++)
                    {
                        int idx = py * Width + px;
                        Vector3 point = GetPointAtPixel(px, py);

                        // Get Deterministic ID (CPU-side logic in SubtileSystem)
                        int subtileId = _map!.Subtiles.GetSubtileId(point);

                        // Encode ID into RGB
                        // R = ID & 0xFF
                        // G = (ID >> 8) & 0xFF
                        // B = (ID >> 16) & 0xFF
                        pixels[idx] = new Color(
                            (byte)(subtileId & 0xFF),
                            (byte)((subtileId >> 8) & 0xFF),
                            (byte)((subtileId >> 16) & 0xFF),
                            (byte)255 // Alpha 255
                        );
                    }
                });

                // Upload to GPU
                Raylib.UpdateTexture(_lutTexture, _lutImage.Data);
            }
        }

        /// <summary>
        /// Rebuilds the Value Texture (SubtileID -> Color).
        /// O(SubtileCount). Fast. Done on mapmode switch.
        /// </summary>
        private void UpdateValues()
        {
            int subtileCount = _map!.Subtiles.SubtileCount;
            int texSize = (int)Math.Ceiling(Math.Sqrt(subtileCount));

            // Resize if needed
            if (_valueImage.Width != texSize || _valueImage.Height != texSize)
            {
                Raylib.UnloadTexture(_valueTexture);
                Raylib.UnloadImage(_valueImage);
                _valueImage = Raylib.GenImageColor(texSize, texSize, Color.Magenta); // Magenta background debug
                _valueTexture = Raylib.LoadTextureFromImage(_valueImage);
                Raylib.SetTextureFilter(_valueTexture, TextureFilter.Point);
            }

            // Pre-calculate tile colors if needed (for Modes that use Parent Tile data)
            Color[]? tileColors = null;
            bool useParentTile = CurrentMode != RenderMode.Elevation && CurrentMode != RenderMode.Subtiles && CurrentMode != RenderMode.SubtileNoise;

            if (useParentTile)
            {
                tileColors = new Color[_map.Topology.TileCount];
                Parallel.For(0, _map.Topology.TileCount, tId =>
                {
                    tileColors[tId] = GetColorForTile(tId, CurrentMode);
                });
            }

            unsafe
            {
                Color* pixels = (Color*)_valueImage.Data;

                // Parallel loop over SUBTILES (not pixels)
                Parallel.For(0, subtileCount, sId =>
                {
                    int x = sId % texSize;
                    int y = sId / texSize;
                    int idx = y * texSize + x;

                    Color color;

                    if (CurrentMode == RenderMode.Elevation)
                    {
                        // Blended Elevation
                        float elev = _map.Subtiles.GetSubtileElevation(sId);
                        color = GetColorForElevation(elev);
                    }
                    else if (CurrentMode == RenderMode.Subtiles)
                    {
                        // Distinct Subtile Color
                        color = GetColorForSubtiles(sId);
                    }
                    else if (CurrentMode == RenderMode.SubtileNoise)
                    {
                        // Noise debug - Requires position
                        // Slightly slower but acceptable for debug mode
                        Vector3 center = _map.Subtiles.GetSubtileCenter(sId);
                        Vector3 noise = _map.Subtiles.GetNoiseVector(center);
                        color = new Color(
                            (byte)((noise.X * 0.5f + 0.5f) * 255),
                            (byte)((noise.Y * 0.5f + 0.5f) * 255),
                            (byte)((noise.Z * 0.5f + 0.5f) * 255),
                            (byte)255
                        );
                    }
                    else
                    {
                        // Parent Tile based modes
                        int pId = _map.Subtiles.GetParentTile(sId);
                        if (pId >= 0 && tileColors != null && pId < tileColors.Length)
                            color = tileColors[pId];
                        else
                            color = Color.Magenta;
                    }

                    pixels[idx] = color;
                });

                Raylib.UpdateTexture(_valueTexture, _valueImage.Data);
            }
        }

        /// <summary>
        /// Draws the map using the Shader.
        /// </summary>
        public void Draw(int x, int y, int w, int h)
        {
            if (!_initialized) return;

            // Set Shader Uniforms
            Raylib.BeginShaderMode(_shader);

            // Texture Unit 0 is standard texturing
            // We need to bind Value Texture to another unit?
            // Actually Raylib's BeginShaderMode implies the NEXT draw call uses the shader.
            // We can set sampler uniforms if we manage units, but simpler to rely on default behavior
            // if we can pass textures. Raylib handling of multiple textures involves `rlActiveTextureSlot`.
            // But simpler approach:

            // Raylib Shader binding for multiple textures:
            // 1. Activate Slot 0, Bind LUT.
            // 2. Activate Slot 1, Bind Value.
            // 3. Set Uniforms for locLut=0, locValue=1. (This is GL style).
            // In Raylib_cs, simpler to:

            int lutSlot = 0;
            int valSlot = 1;

            Raylib.SetShaderValueTexture(_shader, _locLutTexture, _lutTexture);
            Raylib.SetShaderValueTexture(_shader, _locValueTexture, _valueTexture);

            int sCount = _map!.Subtiles.SubtileCount;
            int sSize = _valueImage.Width;
            int useRamp = 0; // 0 = Direct Color

            Raylib.SetShaderValue(_shader, _locSubtileCount, sCount, ShaderUniformDataType.Int);
            Raylib.SetShaderValue(_shader, _locValueTextureSize, sSize, ShaderUniformDataType.Int);
            Raylib.SetShaderValue(_shader, _locUseRamp, useRamp, ShaderUniformDataType.Int);

            Raylib.DrawTexturePro(
                _lutTexture,
                new Rectangle(0, 0, Width, Height),
                new Rectangle(x, y, w, h),
                Vector2.Zero,
                0.0f,
                Color.White
            );

            Raylib.EndShaderMode();
        }

        public void DrawPro(Rectangle sourceRect, Rectangle destRect)
        {
            if (!_initialized) return;

            // Set Shader Uniforms
            Raylib.BeginShaderMode(_shader);

            Raylib.SetShaderValueTexture(_shader, _locLutTexture, _lutTexture);
            Raylib.SetShaderValueTexture(_shader, _locValueTexture, _valueTexture);

            int sCount = _map!.Subtiles.SubtileCount;
            int sSize = _valueImage.Width;
            int useRamp = 0; // 0 = Direct Color

            Raylib.SetShaderValue(_shader, _locSubtileCount, sCount, ShaderUniformDataType.Int);
            Raylib.SetShaderValue(_shader, _locValueTextureSize, sSize, ShaderUniformDataType.Int);
            Raylib.SetShaderValue(_shader, _locUseRamp, useRamp, ShaderUniformDataType.Int);

            Raylib.DrawTexturePro(
                _lutTexture,
                sourceRect,
                destRect,
                Vector2.Zero,
                0.0f,
                Color.White
            );

            Raylib.EndShaderMode();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // COLOR HELPERS
        // ═══════════════════════════════════════════════════════════════════════

        public Vector3 GetPointAtPixel(int px, int py)
        {
            float px_norm = px / (float)(Width - 1);
            float py_norm = py / (float)(Height - 1);
            float lon = px_norm * 360f - 180f;
            float lat = 90f - py_norm * 180f;
            float phi = (90f - lat) * MathF.PI / 180f;
            float theta = lon * MathF.PI / 180f;
            float x = MathF.Sin(phi) * MathF.Cos(theta);
            float y = MathF.Cos(phi);
            float z = MathF.Sin(phi) * MathF.Sin(theta);
            return new Vector3(x, y, z);
        }

        public int GetTileAtPixel(int px, int py)
        {
            if (_map == null) return -1;
            Vector3 point = GetPointAtPixel(px, py);
            return _map.Subtiles.GetSubtileOwner(point);
        }

        private Color GetColorForTile(int tileId, RenderMode mode)
        {
            ref readonly Tile tile = ref _map!.Tiles[tileId];
            return mode switch
            {
                RenderMode.TileId => GetColorForTileId(tileId),
                RenderMode.Face => GetColorForFace(_map.Topology.GetTileFace(tileId)),
                RenderMode.LandWater => tile.IsLand ? new Color(80, 160, 80, 255) : new Color(30, 60, 150, 255),
                RenderMode.PlateId => GetColorForPlateId(tile.PlateId),
                RenderMode.BoundaryType => GetColorForBoundaryType(tile.BoundaryType),
                RenderMode.CrustAge => GetColorForCrustAge(tile.CrustAge),
                RenderMode.CrustType => tile.CrustType == 1 ? new Color(180, 140, 100, 255) : new Color(40, 80, 140, 255),
                RenderMode.RockType => GetColorForRockType(tile.RockType),
                _ => Color.Magenta
            };
        }

        private static Color GetColorForElevation(float elevation)
        {
            if (elevation < -3000) return ColorDeepWater;
            else if (elevation < -500) return LerpColor(ColorDeepWater, ColorWater, (elevation + 3000) / 2500f);
            else if (elevation < 0) return LerpColor(ColorWater, ColorShallowWater, (elevation + 500) / 500f);
            else if (elevation < 200) return LerpColor(ColorSand, ColorGrass, elevation / 200f);
            else if (elevation < 1000) return LerpColor(ColorGrass, ColorForest, (elevation - 200) / 800f);
            else if (elevation < 2500) return LerpColor(ColorForest, ColorRock, (elevation - 1000) / 1500f);
            else if (elevation < 4000) return LerpColor(ColorRock, ColorSnow, (elevation - 2500) / 1500f);
            else return ColorSnow;
        }

        private static Color GetColorForTileId(int tileId)
        {
            float hue = (tileId * 0.618033988749895f) % 1.0f;
            return HslToRgb(hue, 0.7f, 0.5f);
        }

        private static Color GetColorForSubtiles(int tileId)
        {
            uint h = (uint)tileId;
            h = ((h >> 16) ^ h) * 0x45d9f3b;
            h = ((h >> 16) ^ h) * 0x45d9f3b;
            h = (h >> 16) ^ h;
            return new Color((byte)(h & 0xFF), (byte)((h >> 8) & 0xFF), (byte)((h >> 16) & 0xFF), (byte)255);
        }

        private static Color GetColorForFace(int faceId)
        {
            // Simple fallback palette
            if (faceId < 0 || faceId >= 20) return Color.Gray;
            int r = (faceId * 37) % 255;
            int g = (faceId * 151) % 255;
            int b = (faceId * 67) % 255;
            return new Color((byte)r, (byte)g, (byte)b, (byte)255);
        }

        private static Color GetColorForPlateId(int plateId) => plateId < 0 ? new Color(50, 50, 50, 255) : GetColorForTileId(plateId * 17);

        private static Color GetColorForBoundaryType(BoundaryType bt) => bt switch
        {
            BoundaryType.Convergent => new Color(220, 60, 60, 255),
            BoundaryType.Divergent => new Color(60, 100, 220, 255),
            BoundaryType.Transform => new Color(220, 200, 60, 255),
            _ => new Color(80, 80, 80, 255)
        };

        private static Color GetColorForCrustAge(float age) => LerpColor(new Color(255, 200, 100, 255), new Color(60, 30, 20, 255), Math.Clamp(age, 0f, 1f));

        private static Color GetColorForRockType(RockType rt) => rt switch
        {
            RockType.Granite => new Color(220, 180, 180, 255),
            RockType.Basalt => new Color(50, 50, 60, 255),
            RockType.Limestone => new Color(220, 220, 200, 255),
            _ => new Color(100, 100, 100, 255) // Valid fallback
        };

        private static Color LerpColor(Color a, Color b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return new Color((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t), (byte)255);
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
            return new Color((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255), (byte)255);
        }

        public void Dispose()
        {
            if (_initialized)
            {
                Raylib.UnloadTexture(_lutTexture);
                Raylib.UnloadImage(_lutImage);
                Raylib.UnloadTexture(_valueTexture);
                Raylib.UnloadImage(_valueImage);
                Raylib.UnloadShader(_shader);
            }
        }
    }
}

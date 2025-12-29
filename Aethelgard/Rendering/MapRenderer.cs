using System;
using System.Threading.Tasks;
using Aethelgard.Simulation;
using Raylib_cs;

namespace Aethelgard.Rendering
{
    public class MapRenderer : IDisposable
    {
        private Texture2D _texture;
        private Image _image;
        private bool _initialized;

        // Configuration
        private Color _colorDeepWater = new Color(10, 10, 80, 255);
        private Color _colorWater = new Color(30, 40, 150, 255);
        private Color _colorSand = new Color(210, 200, 150, 255);
        private Color _colorGrass = new Color(50, 150, 50, 255);
        private Color _colorRock = new Color(100, 100, 100, 255);
        private Color _colorSnow = new Color(240, 240, 240, 255);

        public void Initialize(int width, int height)
        {
            // Create an empty image and load it as a texture
            _image = Raylib.GenImageColor(width, height, Color.Black);
            _texture = Raylib.LoadTextureFromImage(_image);
            _initialized = true;
        }

        public enum RenderMode
        {
            Elevation,
            Plates,
            FeatureTypes,
            HexTiles,
            HexFaces,
            BoundaryTypes,   // Convergent/Divergent/Transform
            DebugNoise,      // Show flood fill distortion noise
            PlateVelocity,   // Color based on plate movement direction
            Continental,     // Continental vs Oceanic crust
            Microplates,     // Ancient craton subdivisions
            CrustAge         // Oceanic crust age (0=new, 1=old)
        }
        public RenderMode CurrentMode { get; set; } = RenderMode.Elevation;
        public float NoiseScale { get; set; } = 0.0125f;

        public void Update(WorldMap map)
        {
            if (!_initialized) return;

            unsafe
            {
                // Note: For actual pixel manipulation in "unsafe", we should use a pointer to the data.
                // However, Raylib_cs Image.Data is void*. We'd need to cast.
                // For simplicity/safety in this Phase, we'll keep using ImageDrawPixel but wrap the update in unsafe.
                // To optimize, we really should assume 32-bit RGBA.

                Color* pixels = (Color*)_image.Data;

                Parallel.For(0, map.Height, y =>
                {
                    for (int x = 0; x < map.Width; x++)
                    {
                        Color c = Color.Black;

                        if (CurrentMode == RenderMode.Elevation)
                        {
                            float h = map.Elevation.Get(x, y);
                            c = GetColorForHeight(h);
                        }
                        else if (CurrentMode == RenderMode.Plates)
                        {
                            int id = map.Lithosphere.PlateIdMap.Get(x, y);
                            if (id > 0 && map.Lithosphere.GetPlate(id) is Plate p)
                            {
                                c = p.Color;
                            }
                        }
                        else if (CurrentMode == RenderMode.FeatureTypes)
                        {
                            int ft = map.FeatureType.Get(x, y);
                            c = GetColorForFeatureType(ft);
                        }
                        else if (CurrentMode == RenderMode.HexTiles)
                        {
                            // Show each hex tile as a unique color based on tile ID
                            int tileId = map.GetHexTileAt(x, y);
                            bool isPentagon = map.Topology.IsPentagon(tileId);

                            if (isPentagon)
                            {
                                // Highlight pentagons in bright magenta
                                c = new Color(255, 0, 255, 255);
                            }
                            else
                            {
                                // Color by tile ID (cycling through hues)
                                c = GetColorForTileId(tileId);
                            }
                        }
                        else if (CurrentMode == RenderMode.HexFaces)
                        {
                            // Show each of the 20 icosahedron faces as a distinct color
                            int tileId = map.GetHexTileAt(x, y);
                            int faceId = map.Topology.GetTileFace(tileId);
                            c = GetColorForFace(faceId);

                            // Mark pentagons with a dot
                            if (map.Topology.IsPentagon(tileId))
                            {
                                c = new Color(255, 255, 255, 255); // White
                            }
                        }
                        else if (CurrentMode == RenderMode.BoundaryTypes)
                        {
                            // Show boundary type: Convergent=Red, Divergent=Blue, Transform=Yellow
                            int tileId = map.GetHexTileAt(x, y);
                            var boundary = map.TileBoundary[tileId];
                            c = boundary switch
                            {
                                BoundaryType.Convergent => new Color(255, 60, 60, 255),   // Red - mountains
                                BoundaryType.Divergent => new Color(60, 100, 255, 255),  // Blue - rifts
                                BoundaryType.Transform => new Color(255, 220, 50, 255),  // Yellow - faults
                                _ => new Color(40, 40, 40, 255)                          // Dark gray - interior
                            };
                        }
                        else if (CurrentMode == RenderMode.DebugNoise)
                        {
                            // Show the distortion noise used for flood fill
                            var (lon, lat) = SphericalMath.PixelToLatLon(x, y, map.Width, map.Height);
                            // Use Domain Warping parameters matching generation
                            float noise = SimpleNoise.GetDomainWarpedNoise(lon * NoiseScale, lat * NoiseScale, 3, 4.0f);
                            byte v = (byte)(Math.Clamp((noise * 0.5f + 0.5f) * 255, 0, 255));
                            c = new Color(v, v, v, (byte)255);
                        }
                        else if (CurrentMode == RenderMode.PlateVelocity)
                        {
                            // Color based on plate velocity direction (hue) and speed (brightness)
                            int plateId = map.Lithosphere.PlateIdMap.Get(x, y);
                            if (plateId > 0 && map.Lithosphere.GetPlate(plateId) is Plate p)
                            {
                                float angle = MathF.Atan2(p.Velocity.Y, p.Velocity.X);
                                float hue = (angle + MathF.PI) / (2 * MathF.PI); // 0-1
                                float speed = p.Velocity.Length();
                                float sat = Math.Clamp(speed, 0.2f, 1f);
                                c = HslToRgb(hue, sat, 0.5f);
                            }
                        }
                        else if (CurrentMode == RenderMode.Continental)
                        {
                            // Continental = Green/Brown, Oceanic = Blue
                            int plateId = map.Lithosphere.PlateIdMap.Get(x, y);
                            if (plateId > 0 && map.Lithosphere.GetPlate(plateId) is Plate p)
                            {
                                c = p.Type == PlateType.Continental
                                    ? new Color(120, 180, 80, 255)   // Green
                                    : new Color(30, 60, 150, 255);   // Blue
                            }
                        }
                        else if (CurrentMode == RenderMode.Microplates)
                        {
                            // Show microplates within continental plates
                            int tileId = map.GetHexTileAt(x, y);
                            int microId = map.TileMicroplateId[tileId];

                            if (map.TileMicroBoundary[tileId])
                            {
                                // Ancient orogeny boundary - white/gray
                                c = new Color(220, 220, 220, 255);
                            }
                            else if (microId >= 0)
                            {
                                // Color by microplate ID (hue cycling)
                                c = GetColorForTileId(microId * 37); // Multiply for better spread
                            }
                            else
                            {
                                // Oceanic = dark blue
                                c = new Color(20, 40, 80, 255);
                            }
                        }
                        else if (CurrentMode == RenderMode.CrustAge)
                        {
                            // Oceanic crust age
                            int tileId = map.GetHexTileAt(x, y);
                            int plateId = map.TilePlateId[tileId];

                            // Check if oceanic
                            bool isOceanic = false;
                            if (plateId != -1 && map.Lithosphere.Plates.TryGetValue(plateId, out var p))
                            {
                                isOceanic = p.Type == PlateType.Oceanic;
                            }

                            if (isOceanic)
                            {
                                float age = map.TileAge[tileId]; // 0.0 (New) -> 1.0 (Old)

                                // Gradient: Red (Hot/New) -> Yellow -> Blue (Cold/Old)
                                if (age < 0.5f)
                                {
                                    // Red to Yellow
                                    float t = age * 2.0f;
                                    c = new Color((byte)255, (byte)(t * 255), (byte)0, (byte)255);
                                }
                                else
                                {
                                    // Yellow to Blue
                                    float t = (age - 0.5f) * 2.0f;
                                    c = new Color((byte)(255 * (1 - t)), (byte)(255 * (1 - t)), (byte)(t * 255), (byte)255);
                                }
                            }
                            else
                            {
                                // Continental = Gray
                                c = new Color(100, 100, 100, 255);
                            }
                        }

                        // Direct pointer access for speed (Unsafe)
                        pixels[y * map.Width + x] = c;
                    }
                });

                Raylib.UpdateTexture(_texture, _image.Data);
            }
        }

        public void Draw(int x, int y, int w, int h)
        {
            if (!_initialized) return;

            // Draw the texture scaled to the viewport
            Raylib.DrawTexturePro(
                _texture,
                new Rectangle(0, 0, _texture.Width, _texture.Height), // Source
                new Rectangle(x, y, w, h),                            // Dest
                new System.Numerics.Vector2(0, 0),
                0.0f,
                Color.White
            );
        }

        private Color GetColorForHeight(float h)
        {
            // Smooth gradient interpolation for natural terrain visualization

            // Define height zones and their colors
            // Zone: deep ocean (-2 to -0.5), shallow ocean (-0.5 to 0), 
            //       beach (0 to 0.1), grass (0.1 to 0.6), rock (0.6 to 1.5), snow (1.5+)

            if (h < -0.5f)
            {
                // Deep to shallow ocean gradient
                float t = Math.Clamp((h + 2.0f) / 1.5f, 0f, 1f);
                return LerpColor(_colorDeepWater, _colorWater, t);
            }
            else if (h < 0.0f)
            {
                // Shallow ocean to coast
                float t = (h + 0.5f) / 0.5f;
                return LerpColor(_colorWater, _colorSand, t * 0.5f); // Only blend halfway
            }
            else if (h < 0.1f)
            {
                // Beach/lowland
                float t = h / 0.1f;
                return LerpColor(_colorSand, _colorGrass, t);
            }
            else if (h < 0.6f)
            {
                // Grass to rock transition
                float t = (h - 0.1f) / 0.5f;
                return LerpColor(_colorGrass, _colorRock, t);
            }
            else if (h < 1.5f)
            {
                // Rock to snow
                float t = (h - 0.6f) / 0.9f;
                return LerpColor(_colorRock, _colorSnow, t);
            }
            else
            {
                return _colorSnow;
            }
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

        /// <summary>
        /// Color-coded debug view for geological features.
        /// 0=Ocean (Blue), 1=Craton (Green), 2=ActiveBoundary (Yellow),
        /// 3=FossilBoundary (Orange), 4=Hotspot (Red), 5=Biogenic (Cyan)
        /// </summary>
        private Color GetColorForFeatureType(int featureType)
        {
            return featureType switch
            {
                0 => new Color(30, 60, 180, 255),   // Ocean - Blue
                1 => new Color(60, 150, 60, 255),  // Craton - Green
                2 => new Color(230, 200, 50, 255), // Active Boundary - Yellow
                3 => new Color(220, 140, 50, 255), // Fossil Boundary - Orange
                4 => new Color(220, 50, 50, 255),  // Hotspot - Red
                5 => new Color(80, 200, 220, 255), // Biogenic - Cyan
                _ => Color.Magenta                  // Unknown - Magenta
            };
        }

        /// <summary>
        /// Generate a unique color for each hex tile using HSL cycling.
        /// </summary>
        private Color GetColorForTileId(int tileId)
        {
            // Use golden ratio to spread colors nicely
            float hue = (tileId * 0.618033988749895f) % 1.0f;
            return HslToRgb(hue, 0.7f, 0.5f);
        }

        /// <summary>
        /// Get a distinct color for each of the 20 icosahedron faces.
        /// </summary>
        private static Color GetColorForFace(int faceId)
        {
            // 20 distinct colors for the 20 faces
            return faceId switch
            {
                0 => new Color(255, 100, 100, 255),   // Red
                1 => new Color(255, 180, 100, 255),   // Orange
                2 => new Color(255, 255, 100, 255),   // Yellow
                3 => new Color(180, 255, 100, 255),   // Lime
                4 => new Color(100, 255, 100, 255),   // Green
                5 => new Color(100, 255, 180, 255),   // Mint
                6 => new Color(100, 255, 255, 255),   // Cyan
                7 => new Color(100, 180, 255, 255),   // Sky
                8 => new Color(100, 100, 255, 255),   // Blue
                9 => new Color(180, 100, 255, 255),   // Purple
                10 => new Color(255, 100, 255, 255),  // Magenta
                11 => new Color(255, 100, 180, 255),  // Pink
                12 => new Color(200, 80, 80, 255),    // Dark Red
                13 => new Color(200, 140, 80, 255),   // Brown
                14 => new Color(200, 200, 80, 255),   // Olive
                15 => new Color(80, 200, 80, 255),    // Forest
                16 => new Color(80, 200, 200, 255),   // Teal
                17 => new Color(80, 80, 200, 255),    // Navy
                18 => new Color(140, 80, 200, 255),   // Violet
                19 => new Color(200, 80, 140, 255),   // Rose
                _ => Color.Gray
            };
        }

        /// <summary>
        /// Approximate face detection based on lat/lon (simplified).
        /// </summary>
        private static int GetIcoFaceFromLatLon(float lat, float lon)
        {
            // Divide sphere into latitude bands and longitude sectors
            // This is a simplified approximation
            int latBand = (int)((90 - lat) / 36); // 5 bands (0-4)
            int lonSector = (int)((lon + 180) / 72); // 5 sectors (0-4)
            if (lonSector >= 5) lonSector = 4;
            if (latBand >= 5) latBand = 4;

            return latBand * 4 + (lonSector % 4);
        }

        /// <summary>
        /// Convert HSL to RGB color.
        /// </summary>
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

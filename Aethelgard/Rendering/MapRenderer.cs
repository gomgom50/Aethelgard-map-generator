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

        public enum RenderMode { Elevation, Plates, FeatureTypes }
        public RenderMode CurrentMode { get; set; } = RenderMode.Elevation;

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

using Raylib_cs;
using ImGuiNET;
using rlImGui_cs;
using Aethelgard.Simulation;
using Aethelgard.Rendering;
using System;
using System.Numerics;

namespace Aethelgard;

/// <summary>
/// Main entry point for the Aethelgard World Generator.
/// Provides tile-centric world generation with interactive ImGui controls.
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        // ═══════════════════════════════════════════════════════════════
        // Window Initialization
        // ═══════════════════════════════════════════════════════════════
        const int screenWidth = 1280;
        const int screenHeight = 720;

        Raylib.InitWindow(screenWidth, screenHeight, "Aethelgard World Engine");
        Raylib.SetTargetFPS(60);
        rlImGui.Setup(true);

        // ═══════════════════════════════════════════════════════════════
        // World Generation Parameters (UI-controlled)
        // ═══════════════════════════════════════════════════════════════
        int resolution = 50;
        int seed = 12345;
        int renderWidth = 10800;   // Very high res for large subtiles
        int renderHeight = 5400;

        // Current render mode
        int currentModeIndex = 0;
        string[] modeNames = Enum.GetNames<MapRenderer.RenderMode>();

        // Tectonics parameters
        int plateCount = 12;
        float continentalRatio = 0.4f;
        float noiseStrength = 1.0f;

        // Subtile parameters (persistent for slider state)
        float subtileWarpStrength = 0.15f;
        float subtileNoiseScale = 0.05f;
        int subtileResolution = 4;
        bool showSubtileBorders = false;
        const float SubtileBorderMinZoom = 4.0f; // Only show borders at this zoom or higher

        // ═══════════════════════════════════════════════════════════════
        // Camera State
        // ═══════════════════════════════════════════════════════════════
        float zoom = 1.0f;
        float zoomMin = 0.25f;   // Allow zooming out more
        float zoomMax = 32.0f;   // Allow high zoom
        float zoomSpeed = 0.1f;

        Vector2 panOffset = Vector2.Zero;  // Pan in texture coordinates
        float panSpeed = 300f;             // Pixels per second for WASD
        bool isDragging = false;
        Vector2 dragStart = Vector2.Zero;
        Vector2 panAtDragStart = Vector2.Zero;

        // ═══════════════════════════════════════════════════════════════
        // Create Initial World
        // ═══════════════════════════════════════════════════════════════
        WorldMap map = new WorldMap(resolution, seed);
        MapRenderer renderer = new MapRenderer();
        renderer.Initialize(renderWidth, renderHeight, map);

        // Map viewport (screen rectangle where map is drawn)
        Rectangle mapViewport = new Rectangle(50, 50, 900, 450);

        string statusMessage = $"World created: {map.Topology.TileCount:N0} tiles";

        // ═══════════════════════════════════════════════════════════════
        // Main Loop
        // ═══════════════════════════════════════════════════════════════
        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();
            Vector2 mousePos = Raylib.GetMousePosition();
            bool mouseInViewport = Raylib.CheckCollisionPointRec(mousePos, mapViewport);
            bool imguiWantsMouse = ImGui.GetIO().WantCaptureMouse;
            bool imguiWantsKeyboard = ImGui.GetIO().WantCaptureKeyboard;

            // ═══════════════════════════════════════════════════════════
            // Camera Input (only when not interacting with ImGui)
            // ═══════════════════════════════════════════════════════════

            // Zoom with scroll wheel
            if (mouseInViewport && !imguiWantsMouse)
            {
                float wheel = Raylib.GetMouseWheelMove();
                if (wheel != 0)
                {
                    zoom += wheel * zoomSpeed * zoom;  // Proportional zoom
                    zoom = Math.Clamp(zoom, zoomMin, zoomMax);
                }
            }

            // Pan with mouse drag
            if (!imguiWantsMouse)
            {
                if (Raylib.IsMouseButtonPressed(MouseButton.Left) && mouseInViewport)
                {
                    isDragging = true;
                    dragStart = mousePos;
                    panAtDragStart = panOffset;
                }

                if (isDragging && Raylib.IsMouseButtonDown(MouseButton.Left))
                {
                    Vector2 delta = mousePos - dragStart;
                    // Convert screen delta to texture delta (accounting for zoom)
                    panOffset = panAtDragStart - delta / zoom;
                }

                if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                {
                    isDragging = false;
                }
            }

            // Pan with WASD
            if (!imguiWantsKeyboard)
            {
                float moveAmount = panSpeed * dt / zoom;
                if (Raylib.IsKeyDown(KeyboardKey.W)) panOffset.Y -= moveAmount;
                if (Raylib.IsKeyDown(KeyboardKey.S)) panOffset.Y += moveAmount;
                if (Raylib.IsKeyDown(KeyboardKey.A)) panOffset.X -= moveAmount;
                if (Raylib.IsKeyDown(KeyboardKey.D)) panOffset.X += moveAmount;

                // Reset view with R
                if (Raylib.IsKeyPressed(KeyboardKey.R))
                {
                    zoom = 1.0f;
                    panOffset = Vector2.Zero;
                }
            }

            // Clamp pan to prevent going too far off the map
            // When zoom < 1, we don't need to allow panning (whole map fits in view)
            float maxPanX = Math.Max(0, renderWidth * (zoom - 1) / (2 * zoom));
            float maxPanY = Math.Max(0, renderHeight * (zoom - 1) / (2 * zoom));
            panOffset.X = Math.Clamp(panOffset.X, -maxPanX, maxPanX);
            panOffset.Y = Math.Clamp(panOffset.Y, -maxPanY, maxPanY);

            // --- UPDATE ---
            renderer.Update();

            // --- DRAWING ---
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(30, 30, 35, 255));

            // Calculate source rectangle (the portion of texture to show)
            float sourceW = renderWidth / zoom;
            float sourceH = renderHeight / zoom;
            float sourceX = (renderWidth - sourceW) / 2 + panOffset.X;
            float sourceY = (renderHeight - sourceH) / 2 + panOffset.Y;

            Rectangle sourceRect = new Rectangle(sourceX, sourceY, sourceW, sourceH);
            Rectangle destRect = mapViewport;

            // Draw map with zoom/pan
            renderer.DrawPro(sourceRect, destRect);

            // Draw viewport border
            Raylib.DrawRectangleLinesEx(mapViewport, 2, Color.White);

            // -----------------------------------------------------------------------
            // Interaction: Tile Highlighting & Wireframe
            // -----------------------------------------------------------------------
            // Retrieve current mouse position (already calculated above as mousePos, but valid to access here)
            // Or just use the existing variable.

            if (Raylib.CheckCollisionPointRec(mousePos, mapViewport))
            {
                // Convert Screen -> Map Texture Coordinates
                // mapX = sourceX + (mouseX - destX) / destW * sourceW
                float relativeX = (mousePos.X - mapViewport.Position.X) / mapViewport.Size.X;
                float relativeY = (mousePos.Y - mapViewport.Position.Y) / mapViewport.Size.Y;

                int mapPixelX = (int)(sourceRect.Position.X + relativeX * sourceRect.Size.X);
                int mapPixelY = (int)(sourceRect.Position.Y + relativeY * sourceRect.Size.Y);

                // Wrap X (Longitude)
                if (mapPixelX < 0) mapPixelX = (mapPixelX % renderer.Width) + renderer.Width;
                if (mapPixelX >= renderer.Width) mapPixelX = mapPixelX % renderer.Width;

                // Clamp Y (Latitude)
                mapPixelY = Math.Clamp(mapPixelY, 0, renderer.Height - 1);

                // Get Tile ID using Organic Lookup
                int hoverTileId = renderer.GetTileAtPixel(mapPixelX, mapPixelY);
                if (map?.Tiles != null && hoverTileId >= 0 && hoverTileId < map.Tiles.Length)
                {
                    ref var tile = ref map.Tiles[hoverTileId];

                    // Show Tooltip
                    string info = $"Tile {hoverTileId}\nPlate: {tile.PlateId}\nType: {(tile.IsLand ? "Land" : "Water")}";
                    Raylib.DrawText(info, (int)mousePos.X + 15, (int)mousePos.Y + 15, 20, Color.Yellow);

                    // Hover: Draw Wireframe (Default behavior as requested)
                    {
                        var vertices = map.Topology.GetTileVertices(hoverTileId);

                        // Project vertices to screen
                        for (int i = 0; i < vertices.Length; i++)
                        {
                            var (vLat, vLon) = vertices[i];
                            var (nextLat, nextLon) = vertices[(i + 1) % vertices.Length];

                            // Project Lat/Lon -> Map Pixel -> Screen Pixel
                            Vector2 Project(float lat, float lon)
                            {
                                float px = (lon + 180f) / 360f * (renderer.Width - 1);
                                float py = (90f - lat) / 180f * (renderer.Height - 1);

                                // Handle wrapping for drawing? Simple clamp for now or check dist
                                // Adjust for sourceRect offset
                                float sx = (px - sourceRect.Position.X) / sourceRect.Size.X * mapViewport.Size.X + mapViewport.Position.X;
                                float sy = (py - sourceRect.Position.Y) / sourceRect.Size.Y * mapViewport.Size.Y + mapViewport.Position.Y;

                                return new Vector2(sx, sy);
                            }

                            Vector2 p1 = Project(vLat, vLon);
                            Vector2 p2 = Project(nextLat, nextLon);

                            // Don't draw cross-screen weirdness for wrapped polygons
                            if (Vector2.Distance(p1, p2) < mapViewport.Size.X / 2)
                                Raylib.DrawLineEx(p1, p2, 2f, Color.Yellow);
                        }
                    }
                }
            }

            // Subtile Border Overlay (only at high zoom)
            // Uses edge detection - draws where adjacent sample points have different IDs
            // Sample grid is aligned to world coordinates to prevent flickering when panning
            // -----------------------------------------------------------------------
            if (showSubtileBorders && zoom >= SubtileBorderMinZoom && map?.Subtiles != null)
            {
                Color borderColor = new Color(60, 60, 60, 180);

                // Calculate visible lat/lon range
                float minLon = (sourceRect.Position.X / (renderer.Width - 1)) * 360f - 180f;
                float maxLon = ((sourceRect.Position.X + sourceRect.Size.X) / (renderer.Width - 1)) * 360f - 180f;
                float minLat = 90f - ((sourceRect.Position.Y + sourceRect.Size.Y) / (renderer.Height - 1)) * 180f;
                float maxLat = 90f - (sourceRect.Position.Y / (renderer.Height - 1)) * 180f;

                // Fixed world-space sampling interval (degrees) - aligned to prevent flicker
                float sampleInterval = 0.5f; // Sample every 0.5 degrees

                // Align sample grid to world coordinates
                float startLon = MathF.Floor(minLon / sampleInterval) * sampleInterval;
                float startLat = MathF.Floor(minLat / sampleInterval) * sampleInterval;
                float endLon = MathF.Ceiling(maxLon / sampleInterval) * sampleInterval;
                float endLat = MathF.Ceiling(maxLat / sampleInterval) * sampleInterval;

                int gridCountX = (int)((endLon - startLon) / sampleInterval) + 1;
                int gridCountY = (int)((endLat - startLat) / sampleInterval) + 1;

                // Safety limit
                gridCountX = Math.Min(gridCountX, 200);
                gridCountY = Math.Min(gridCountY, 200);

                // Cache subtile IDs at world-aligned sample points
                int[,] gridIds = new int[gridCountX, gridCountY];

                for (int gy = 0; gy < gridCountY; gy++)
                {
                    for (int gx = 0; gx < gridCountX; gx++)
                    {
                        float lat = startLat + gy * sampleInterval;
                        float lon = startLon + gx * sampleInterval;
                        var point = LatLonToCartesian(lat, lon);
                        gridIds[gx, gy] = map.Subtiles.GetSubtileId(point);
                    }
                }

                // Helper to convert lat/lon to screen position
                Vector2 LatLonToScreen(float lat, float lon)
                {
                    float px = (lon + 180f) / 360f * (renderer.Width - 1);
                    float py = (90f - lat) / 180f * (renderer.Height - 1);
                    float sx = (px - sourceRect.Position.X) / sourceRect.Size.X * mapViewport.Size.X + mapViewport.Position.X;
                    float sy = (py - sourceRect.Position.Y) / sourceRect.Size.Y * mapViewport.Size.Y + mapViewport.Position.Y;
                    return new Vector2(sx, sy);
                }

                // Draw borders where adjacent cells have different IDs
                for (int gy = 0; gy < gridCountY - 1; gy++)
                {
                    for (int gx = 0; gx < gridCountX - 1; gx++)
                    {
                        int current = gridIds[gx, gy];
                        int right = gridIds[gx + 1, gy];
                        int down = gridIds[gx, gy + 1];

                        float lat = startLat + gy * sampleInterval;
                        float lon = startLon + gx * sampleInterval;

                        // Draw vertical edge if right neighbor differs
                        if (current != right)
                        {
                            Vector2 p1 = LatLonToScreen(lat, lon + sampleInterval);
                            Vector2 p2 = LatLonToScreen(lat + sampleInterval, lon + sampleInterval);
                            if (p1.X >= mapViewport.Position.X && p1.X <= mapViewport.Position.X + mapViewport.Size.X)
                                Raylib.DrawLineEx(p1, p2, 1f, borderColor);
                        }

                        // Draw horizontal edge if bottom neighbor differs
                        if (current != down)
                        {
                            Vector2 p1 = LatLonToScreen(lat + sampleInterval, lon);
                            Vector2 p2 = LatLonToScreen(lat + sampleInterval, lon + sampleInterval);
                            if (p1.Y >= mapViewport.Position.Y && p1.Y <= mapViewport.Position.Y + mapViewport.Size.Y)
                                Raylib.DrawLineEx(p1, p2, 1f, borderColor);
                        }
                    }
                }
            }

            // Helper function for lat/lon to cartesian
            static Vector3 LatLonToCartesian(float lat, float lon)
            {
                float latRad = lat * MathF.PI / 180f;
                float lonRad = lon * MathF.PI / 180f;
                return new Vector3(
                    MathF.Cos(latRad) * MathF.Cos(lonRad),
                    MathF.Sin(latRad),
                    MathF.Cos(latRad) * MathF.Sin(lonRad)
                );
            }

            // ═══════════════════════════════════════════════════════════
            // ImGui UI
            // ═══════════════════════════════════════════════════════════
            rlImGui.Begin();

            ImGui.Begin("World Generation");

            ImGui.TextColored(new Vector4(0.4f, 1f, 0.6f, 1), statusMessage);
            ImGui.Separator();

            // Generation Controls
            ImGui.Text("Generation Parameters");

            bool paramsChanged = false;
            ImGui.SliderInt("Resolution", ref resolution, 5, 50);
            if (ImGui.IsItemDeactivatedAfterEdit()) paramsChanged = true;

            ImGui.InputInt("Seed", ref seed);
            if (ImGui.IsItemDeactivatedAfterEdit()) paramsChanged = true;

            if (ImGui.Button("Regenerate World") || paramsChanged)
            {
                map = new WorldMap(resolution, seed);
                renderer.Dispose();
                renderer = new MapRenderer();
                renderer.Initialize(renderWidth, renderHeight, map);
                statusMessage = $"World regenerated: {map.Topology.TileCount:N0} tiles";
            }

            ImGui.Separator();

            // Visualization Controls
            ImGui.Text("Visualization");
            if (ImGui.Combo("Mode", ref currentModeIndex, modeNames, modeNames.Length))
            {
                renderer.CurrentMode = (MapRenderer.RenderMode)currentModeIndex;
                renderer.IsDirty = true;
            }

            ImGui.Separator();

            // Tectonics Controls
            ImGui.Text("Tectonics (Phase 1)");

            ImGui.SliderInt("Plate Count", ref plateCount, 4, 20);
            ImGui.SliderFloat("Continental Ratio", ref continentalRatio, 0.2f, 0.7f, "%.2f");
            ImGui.SliderFloat("Noise Strength", ref noiseStrength, 0.2f, 2.0f, "%.1f");

            if (ImGui.Button("Generate Tectonics"))
            {
                map?.GenerateTectonics(plateCount, continentalRatio, noiseStrength);
                renderer.IsDirty = true;
                statusMessage = $"Tectonics generated: {map?.Plates?.Length ?? 0} plates";
            }

            ImGui.Separator();
            ImGui.Text("Subtile System (Organic Borders)");

            bool subtileChanged = false;

            if (ImGui.SliderFloat("Warp Strength", ref subtileWarpStrength, 0.0f, 0.5f, "%.2f"))
            {
                // Preview change but don't apply yet
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
                subtileChanged = true;

            // Scale is now in angular space - 0.01 = very smooth, 0.2 = detailed
            if (ImGui.SliderFloat("Noise Frequency", ref subtileNoiseScale, 0.01f, 0.2f, "%.3f"))
            {
                // Preview change but don't apply yet
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
                subtileChanged = true;

            if (ImGui.SliderInt("Subtile Resolution", ref subtileResolution, 2, 8, "%dx"))
            {
                // Preview change but don't apply yet
            }
            if (ImGui.IsItemDeactivatedAfterEdit() && map?.Subtiles != null)
            {
                map.Subtiles.ResolutionMultiplier = subtileResolution;
                renderer.IsDirty = true;
            }

            if (subtileChanged && map?.Subtiles != null)
            {
                map.Subtiles.UpdateConfig(subtileNoiseScale, subtileWarpStrength);
                renderer.IsDirty = true;
            }

            // Subtile border overlay toggle
            ImGui.Checkbox("Show Subtile Borders", ref showSubtileBorders);
            if (showSubtileBorders && zoom < SubtileBorderMinZoom)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), $"(Zoom {SubtileBorderMinZoom}x+ to see)");
            }

            ImGui.Separator();

            // Camera Controls
            ImGui.Text("Camera");
            ImGui.SliderFloat("Zoom", ref zoom, zoomMin, zoomMax, "%.1fx");
            if (ImGui.Button("Reset View (R)"))
            {
                zoom = 1.0f;
                panOffset = Vector2.Zero;
            }
            ImGui.TextDisabled("Scroll=Zoom, Drag=Pan, WASD=Move");

            ImGui.Separator();

            // Statistics
            ImGui.Text("World Statistics");
            ImGui.BulletText($"Tiles: {map.Topology.TileCount:N0}");
            int landCount = 0;
            if (map?.Tiles != null)
            {
                for (int i = 0; i < map.Tiles.Length; i++)
                    if (map.Tiles[i].IsLand) landCount++;
            }
            float totalTiles = map?.Tiles?.Length ?? 1; // Avoid div/0
            ImGui.BulletText($"Land: {(landCount * 100f) / totalTiles:F1}%");

            ImGui.End();

            rlImGui.End();
            Raylib.EndDrawing();
        }

        // ═══════════════════════════════════════════════════════════════
        // Cleanup
        // ═══════════════════════════════════════════════════════════════
        renderer.Dispose();
        rlImGui.Shutdown();
        Raylib.CloseWindow();
    }
}
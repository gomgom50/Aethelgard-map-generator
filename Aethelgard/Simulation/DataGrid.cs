using System;

namespace Aethelgard.Simulation
{
    /// <summary>
    /// A generic 2D grid wrapper around a 1D array.
    /// Optimized for cache locality and easy serialization.
    /// </summary>
    /// <typeparam name="T">The type of data to store (e.g., float, byte, int).</typeparam>
    public class DataGrid<T>
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public T[] RawData { get; private set; }

        public DataGrid(int width, int height)
        {
            Width = width;
            Height = height;
            RawData = new T[width * height];
        }

        public T Get(int x, int y)
        {
            if (!IsValid(x, y)) return default!;
            return RawData[y * Width + x];
        }

        public void Set(int x, int y, T value)
        {
            if (!IsValid(x, y)) return;
            RawData[y * Width + x] = value;
        }

        public bool IsValid(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        /// <summary>
        /// Clears the grid to a default value.
        /// </summary>
        public void Fill(T value)
        {
            Array.Fill(RawData, value);
        }
    }
}

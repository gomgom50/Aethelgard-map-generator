using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Aethelgard.Simulation.Core
{
    /// <summary>
    /// A thread-safe pool for reusing arrays to minimize Garbage Collection pressure
    /// during heavy generation tasks (e.g. per-tile buffers).
    /// </summary>
    /// <typeparam name="T">The type of array element.</typeparam>
    public static class TransientBufferPool<T>
    {
        // Buckets by size. Since mostly we request World.TileCount size, this will often have just 1 active bucket.
        // We bucket by exact requested size for simplicity/safety, assuming the caller requests consistent sizes (e.g. TileCount).
        private static readonly ConcurrentDictionary<int, ConcurrentBag<T[]>> _buckets = new();

        /// <summary>
        /// Rents a buffer of at least the specified size.
        /// Guaranteed to be at least 'size', but may be larger if pooled.
        /// NOT CLEARED by default - contains garbage data.
        /// </summary>
        public static T[] Get(int size)
        {
            var bucket = _buckets.GetOrAdd(size, _ => new ConcurrentBag<T[]>());

            if (bucket.TryTake(out var buffer))
            {
                // Ensure sanity (though bucket key should guarantee this)
                if (buffer.Length >= size) return buffer;
            }

            // Allocate new if pool empty
            return new T[size];
        }

        /// <summary>
        /// Returns a buffer to the pool.
        /// </summary>
        public static void Return(T[] buffer)
        {
            if (buffer == null) return;

            // We bucket strictly by Length. If Get(N) returned an N+X buffer, we return it to bucket N+X.
            // Future Get(N) calls might trigger a new allocation if N+X bucket isn't checked, 
            // but Get(N+X) will find it.
            // For this project, sizes are highly uniform (TileCount), so this is fine.
            var bucket = _buckets.GetOrAdd(buffer.Length, _ => new ConcurrentBag<T[]>());
            bucket.Add(buffer);
        }

        /// <summary>
        /// Clears the pool, allowing GC to collect the arrays.
        /// </summary>
        public static void Clear()
        {
            _buckets.Clear();
        }
    }
}

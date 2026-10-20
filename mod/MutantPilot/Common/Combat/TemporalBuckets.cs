#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace MutantPilot.Combat
{
    // Broad phase only: every returned candidate still uses its original exact
    // collision query. Bounds include the player's size before insertion.
    public sealed class TemporalBuckets
    {
        const int Cell = 256;
        readonly Dictionary<(int X, int Y), List<int>>[] layers;
        // Each time layer owns its pool. Pure forecast builders may populate different
        // layers concurrently, then join before querying; a layer still has one writer.
        readonly Stack<List<int>>[] pools;
        public TemporalBuckets(int horizon)
        {
            layers = new Dictionary<(int, int), List<int>>[horizon + 1];
            pools = new Stack<List<int>>[horizon + 1];
            for (int t = 0; t < layers.Length; t++) { layers[t] = new(); pools[t] = new(); }
        }
        public void Clear()
        {
            for (int tick = 0; tick < layers.Length; tick++)
            {
                var layer = layers[tick]; var pool = pools[tick];
                foreach (var list in layer.Values) { list.Clear(); pool.Push(list); }
                layer.Clear();
            }
        }
        public void Add(int id, int tick, Vector2 minimum, Vector2 maximum)
        {
            int left = (int)MathF.Floor(minimum.X / Cell), right = (int)MathF.Floor(maximum.X / Cell);
            int top = (int)MathF.Floor(minimum.Y / Cell), bottom = (int)MathF.Floor(maximum.Y / Cell);
            var layer = layers[tick];
            var pool = pools[tick];
            for (int x = left; x <= right; x++)
                for (int y = top; y <= bottom; y++)
                {
                    if (!layer.TryGetValue((x, y), out var list))
                        layer[(x, y)] = list = pool.Count > 0 ? pool.Pop() : new(8);
                    list.Add(id);
                }
        }
        public List<int>? At(Vector2 point, int tick) => layers[tick].GetValueOrDefault(
            ((int)MathF.Floor(point.X / Cell), (int)MathF.Floor(point.Y / Cell)));
    }
}

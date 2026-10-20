using System;
using System.Collections.Generic;
using System.Numerics;

namespace MutantPilot.Combat
{
    // Pooled immutable branches of the native projectile update. No entity is
    // moved: every branch pursues its own candidate player's next position.
    public sealed class WormResponseForecast
    {
        sealed class World
        {
            public Vector2[] Position, Velocity;
            public float[] Rotation;
            public int Parent;
            public World(int count) { Position = new Vector2[count]; Velocity = new Vector2[count]; Rotation = new float[count]; }
        }
        readonly List<World> pool = new();
        readonly Dictionary<(int Parent, Vector2 Target, Vector2 PlayerVelocity, int Tick), int> cache = new(4096);
        readonly Dictionary<int, int> indices = new();
        readonly List<WormNode> nodes = new();
        readonly List<int> heads = new();
        int count;
        public int Count => count;
        public float Parent(float reference) => pool[(int)reference].Parent;
        World Get(int index)
        {
            if (index == pool.Count) pool.Add(new(nodes.Count));
            if (pool[index].Position.Length < nodes.Count) pool[index] = new(nodes.Count);
            return pool[index];
        }
        public void Reset(IReadOnlyList<WormNode> ordered)
        {
            nodes.Clear(); nodes.AddRange(ordered); indices.Clear(); heads.Clear(); cache.Clear(); count = 1;
            World initial = Get(0); initial.Parent = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i]; indices.Add(node.Slot, i); if (node.Head) heads.Add(i);
                initial.Position[i] = node.Position; initial.Velocity[i] = node.Velocity; initial.Rotation[i] = node.Rotation;
            }
        }
        public bool Contains(int slot) => indices.ContainsKey(slot);
        public Vector2 Position(float reference, int slot) => pool[(int)reference].Position[indices[slot]];
        public float Advance(float reference, MotionState player, int tick)
        {
            var key = ((int)reference, player.Position, player.Velocity, tick);
            if (cache.TryGetValue(key, out int found)) return found;
            World before = pool[(int)reference], next = Get(count);
            next.Parent = (int)reference;
            Array.Copy(before.Position, next.Position, nodes.Count);
            Array.Copy(before.Velocity, next.Velocity, nodes.Count);
            Array.Copy(before.Rotation, next.Rotation, nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i]; Vector2 position = next.Position[i];
                if (node.Head)
                {
                    Vector2 velocity = next.Velocity[i];
                    next.Rotation[i] = MathF.Atan2(velocity.Y, velocity.X) + MathF.PI / 2;
                    if (node.Age + tick > 60)
                    {
                        Vector2 delta = player.Position - position;
                        float speed = 10 * node.Speed, lerp = node.Speed / 25;
                        if (delta.LengthSquared() > 700 * 700) { speed *= 2; lerp *= 2; }
                        Vector2 direction = delta.LengthSquared() > .000001f ? Vector2.Normalize(delta) : Vector2.UnitY;
                        velocity = Vector2.Lerp(velocity, direction * speed, lerp);
                    }
                    foreach (int other in heads)
                    {
                        if (other == i || Vector2.DistanceSquared(next.Position[other], position) >= node.Width * node.Width) continue;
                        Vector2 at = next.Position[other];
                        velocity += new Vector2(position.X < at.X ? -.05f : .05f, position.Y < at.Y ? -.05f : .05f);
                        next.Velocity[other] += new Vector2(at.X < position.X ? -.05f : .05f, at.Y < position.Y ? -.05f : .05f);
                    }
                    next.Velocity[i] = velocity; next.Position[i] = position + velocity;
                }
                else if (indices.TryGetValue(node.ParentSlot, out int parent))
                {
                    Vector2 at = next.Position[parent], delta = at - position;
                    float turn = next.Rotation[parent] - next.Rotation[i];
                    turn = MathF.Atan2(MathF.Sin(turn), MathF.Cos(turn)) * .1f;
                    float c = MathF.Cos(turn), s = MathF.Sin(turn);
                    delta = new(delta.X * c - delta.Y * s, delta.X * s + delta.Y * c);
                    next.Rotation[i] = MathF.Atan2(delta.Y, delta.X) + MathF.PI / 2;
                    if (delta.LengthSquared() > .000001f) next.Position[i] = at - Vector2.Normalize(delta) * 36;
                    next.Velocity[i] = Vector2.Zero;
                }
                else next.Position[i] = position + next.Velocity[i];
            }
            int result = count++; cache.Add(key, result); return result;
        }
    }
}

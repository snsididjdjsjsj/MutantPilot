using System;
using System.Collections.Generic;
using System.Numerics;

namespace MutantPilot.Combat
{
    public readonly record struct WormNode(int Slot, int ParentSlot, Vector2 Position, Vector2 Velocity,
        float Rotation, bool Head, float Age, float Speed, float Width);

    public sealed class WormForecast
    {
        readonly Vector2[][] paths;
        readonly float[][] rotations;
        readonly Vector2[] velocities;
        readonly List<WormNode> heads = new();
        public WormForecast(int capacity)
        {
            paths = new Vector2[capacity][];
            rotations = new float[capacity][];
            velocities = new Vector2[capacity];
        }
        public Vector2[] Path(int slot) => paths[slot];
        public void Update(IReadOnlyList<WormNode> ordered, Vector2 player, Vector2 playerVelocity)
        {
            heads.Clear();
            foreach (WormNode node in ordered)
            {
                if (node.Head) heads.Add(node);
                paths[node.Slot] ??= new Vector2[CombatPlanner.ForecastTicks + 1];
                rotations[node.Slot] ??= new float[CombatPlanner.ForecastTicks + 1];
                paths[node.Slot][0] = node.Position;
                rotations[node.Slot][0] = node.Rotation;
                velocities[node.Slot] = node.Velocity;
            }
            for (int tick = 1; tick <= CombatPlanner.ForecastTicks; tick++)
                foreach (WormNode node in ordered)
                {
                    int slot = node.Slot;
                    Vector2 position = paths[slot][tick - 1];
                    if (node.Head)
                    {
                        Vector2 velocity = velocities[slot];
                        rotations[slot][tick] = MathF.Atan2(velocity.Y, velocity.X) + MathF.PI / 2;
                        if (node.Age + tick > 60)
                        {
                            Vector2 target = player + playerVelocity * Math.Min(tick, 12);
                            float speed = 10 * node.Speed, lerp = node.Speed / 25;
                            Vector2 delta = target - position;
                            if (delta.LengthSquared() > 700 * 700) { speed *= 2; lerp *= 2; }
                            Vector2 direction = delta.LengthSquared() > .000001f ? Vector2.Normalize(delta) : Vector2.UnitY;
                            velocity = Vector2.Lerp(velocity, direction * speed, lerp);
                        }
                        foreach (WormNode other in heads)
                        {
                            if (!other.Head || other.Slot == slot) continue;
                            Vector2 at = paths[other.Slot][other.Slot < slot ? tick : tick - 1];
                            if (Vector2.DistanceSquared(at, position) >= node.Width * node.Width) continue;
                            Vector2 push = new(position.X < at.X ? -.05f : .05f, position.Y < at.Y ? -.05f : .05f);
                            velocity += push;
                            velocities[other.Slot] += new Vector2(at.X < position.X ? -.05f : .05f, at.Y < position.Y ? -.05f : .05f);
                        }
                        velocities[slot] = velocity;
                        paths[slot][tick] = position + velocity;
                    }
                    else if (node.ParentSlot >= 0)
                    {
                        // Projectile slots update in order. A later parent has not
                        // moved yet when this segment reads its center and rotation.
                        int parentTick = node.ParentSlot < slot ? tick : tick - 1;
                        Vector2 parent = paths[node.ParentSlot][parentTick];
                        Vector2 delta = parent - position;
                        float turn = rotations[node.ParentSlot][parentTick] - rotations[slot][tick - 1];
                        turn = MathF.Atan2(MathF.Sin(turn), MathF.Cos(turn)) * .1f;
                        float c = MathF.Cos(turn), s = MathF.Sin(turn);
                        delta = new(delta.X * c - delta.Y * s, delta.X * s + delta.Y * c);
                        rotations[slot][tick] = MathF.Atan2(delta.Y, delta.X) + MathF.PI / 2;
                        paths[slot][tick] = delta.LengthSquared() > .000001f ? parent - Vector2.Normalize(delta) * 36 : position;
                    }
                    else
                    {
                        paths[slot][tick] = position + node.Velocity;
                        rotations[slot][tick] = node.Rotation;
                    }
                }
        }
    }
}

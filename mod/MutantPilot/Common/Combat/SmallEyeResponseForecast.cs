using System;
using System.Collections.Generic;
using System.Numerics;

namespace MutantPilot.Combat
{
    public readonly record struct EyeBolt(Vector2 Origin, Vector2 Velocity, int Born)
    {
        public bool Touches(Vector2 point, Vector2 half, int tick)
        {
            if (tick < Born) return false;
            // Native type 462 moves and checks damage four times per game tick.
            // A free child slot can run on its birth frame or on the next frame.
            int age = tick - Born;
            return SweptBox.Intersects(Origin + Velocity * Math.Max(0, age - 1) * 4,
                Origin + Velocity * (age + 1) * 4, point, half + new Vector2(4));
        }
    }
    public readonly record struct SmallEye(int Slot, Vector2 Position, Vector2 Velocity,
        int State, float Age, float Side, int Remaining, EyeBolt? A = null, EyeBolt? B = null);

    // Each candidate keeps its own future aim and parent motion. No native entity is written.
    public sealed class SmallEyeResponseForecast
    {
        readonly List<SmallEye[]> pool = new(8192);
        readonly Dictionary<(int Parent, MotionState Player, int Tick), int> cache = new(8192);
        int count, eyes;
        public int Count => count;
        SmallEye[] Allocate(int at)
        {
            if (at == pool.Count) pool.Add(new SmallEye[eyes]);
            else if (pool[at].Length != eyes) pool[at] = new SmallEye[eyes];
            return pool[at];
        }
        public void Reset(IReadOnlyList<SmallEye> initial)
        {
            eyes = initial.Count; count = 1; cache.Clear();
            var world = Allocate(0);
            for (int i = 0; i < eyes; i++) world[i] = initial[i];
        }
        public float Advance(float reference, MotionState player, int tick)
        {
            var key = ((int)reference, player, tick);
            if (cache.TryGetValue(key, out int found)) return found;
            int id = count++; var before = pool[(int)reference]; var next = Allocate(id);
            for (int i = 0; i < eyes; i++) next[i] = Step(before[i], player, tick);
            cache.Add(key, id); return id;
        }
        public SmallEye At(float reference, int index) => pool[(int)reference][index];
        public static SmallEye Step(SmallEye eye, MotionState player, int tick)
        {
            if (eye.Remaining <= 0 || eye.State > 2) return eye with { Remaining = 0 };
            float age = eye.Age + 1; int state = eye.State;
            Vector2 position = eye.Position, velocity = eye.Velocity;
            if (state == 0)
            {
                Vector2 delta = player.Position - position + new Vector2(-200 * eye.Side, -200);
                if (delta != Vector2.Zero) velocity = (velocity * 29 + Vector2.Normalize(delta) * 24) / 30;
                if (Vector2.DistanceSquared(position, player.Position) < 150 * 150)
                    velocity += new Vector2(position.X < player.Position.X ? -.25f : .25f,
                        position.Y < player.Position.Y ? -.25f : .25f);
                if (age > 60) { age = 0; state = 1; }
            }
            else if (state == 1)
            {
                velocity *= .9f;
                if (velocity.LengthSquared() < 1) { velocity = Vector2.Zero; age = 0; state = 2; }
            }
            else
            {
                if (age == 7 || age == 14)
                {
                    Vector2 origin = position - new Vector2(0, 6);
                    Vector2 delta = player.Position + player.Velocity * 15 - origin;
                    if (delta != Vector2.Zero)
                    {
                        var bolt = new EyeBolt(origin, Vector2.Normalize(delta) * 8, tick);
                        eye = age == 7 ? eye with { A = bolt } : eye with { B = bolt };
                    }
                }
                if (age > 21) { age = 0; state = 3; }
            }
            return eye with { Position = position + velocity, Velocity = velocity,
                State = state, Age = age, Remaining = eye.Remaining - 1 };
        }
        public bool Touches(float reference, Vector2 player, Vector2 half, int tick)
        {
            foreach (var eye in pool[(int)reference])
                if (eye.A is EyeBolt a && a.Touches(player, half, tick)
                    || eye.B is EyeBolt b && b.Touches(player, half, tick)) return true;
            return false;
        }
    }
}

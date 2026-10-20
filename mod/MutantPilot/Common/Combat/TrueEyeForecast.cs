using System;
using System.Numerics;

namespace MutantPilot.Combat
{
    public readonly record struct PendingEyeRay(Vector2 Origin, float Angle, float Turn, int Born)
    {
        public bool Intersects(Vector2 point, Vector2 half, int tick)
        {
            int age = tick - Born;
            if (age < 0 || age >= 90) return false;
            // A free child slot can be before or after its parent. Cover both
            // birth-frame update orders; subsequent native rays replace this model.
            for (int offset = 0; offset <= 1; offset++)
            {
                if (age + offset >= 90) continue;
                float angle = Angle + Turn * (age + offset);
                if (new TimedBeam(Origin, new(MathF.Cos(angle), MathF.Sin(angle)), Born, Born + 89, 9)
                    .Intersects(point, half, tick)) return true;
            }
            return Math.Abs(point.X - Origin.X) <= half.X + 24
                && Math.Abs(point.Y - Origin.Y) <= half.Y + 24;
        }
    }

    public static class LeftEyeForecast
    {
        // Installed MutantTrueEyeL. The pupil angle is read from its private
        // observation field, never written. Future player motion remains an
        // approximation until the eye freezes its pupil on entering state 2.
        public static PendingEyeRay? Pending(Vector2 position, Vector2 velocity, int state, float age,
            float pupil, Vector2 player, Vector2 playerVelocity, int horizon)
        {
            if (state > 2 || (state == 2 && age > 0) || !float.IsFinite(pupil)) return null;
            for (int t = 1; t <= horizon; t++)
            {
                Vector2 target = player + playerVelocity * Math.Min(t, 12);
                age++;
                if (state == 0)
                {
                    Vector2 delta = target - position + new Vector2(0, -300);
                    if (delta != Vector2.Zero) velocity = (velocity * 29 + Vector2.Normalize(delta) * 24) / 30;
                    if (Vector2.Distance(position, target) < 150)
                        velocity += new Vector2(position.X < target.X ? -.25f : .25f,
                            position.Y < target.Y ? -.25f : .25f);
                    if (age > 120) { age = 0; state = 1; }
                }
                else if (state == 1)
                {
                    velocity *= .95f;
                    if (velocity.Length() < 1) { velocity = Vector2.Zero; age = 0; state = 2; }
                }
                else if (age == 1)
                {
                    float turn = MathF.PI / 135 * (position.X < target.X ? -1 : 1);
                    return new(position - new Vector2(0, 6), pupil - turn * 60 + MathF.PI, turn, t);
                }
                if (state != 2)
                {
                    float old = (float)((double)pupil % 6.28318548202515 - 3.14159274101257);
                    Vector2 blended = new Vector2(MathF.Cos(old), MathF.Sin(old)) * .9f;
                    Vector2 delta = target - position;
                    float desired = MathF.Atan2(delta.Y, delta.X);
                    blended += new Vector2(MathF.Cos(desired), MathF.Sin(desired)) * .1f;
                    pupil = (float)(Math.Atan2(blended.Y, blended.X) + 5 * Math.PI);
                }
                position += velocity;
            }
            return null;
        }
    }

    // Read-only forecast of installed MutantTrueEyeR and its existing spheres.
    // State and velocity are kept at each projectile update boundary because a
    // child can occupy a lower slot and therefore read yesterday's parent state.
    public sealed class TrueEyeForecast
    {
        public readonly Vector2[] Positions = new Vector2[CombatPlanner.ForecastTicks + 1];
        public readonly Vector2[] Velocities = new Vector2[CombatPlanner.ForecastTicks + 1];
        public readonly int[] States = new int[CombatPlanner.ForecastTicks + 1];
        public readonly bool[] Alive = new bool[CombatPlanner.ForecastTicks + 1];

        public void Update(Vector2 position, Vector2 velocity, int state, float age, float side,
            Vector2 player, Vector2 playerVelocity)
        {
            bool alive = true;
            Positions[0] = position; Velocities[0] = velocity; States[0] = state; Alive[0] = true;
            for (int t = 1; t < Positions.Length; t++)
            {
                Vector2 target = player + playerVelocity * Math.Min(t, 12);
                age++;
                if (alive)
                {
                    switch (state)
                    {
                        case 0:
                            Vector2 delta = target - position + new Vector2(300 * side, -300);
                            if (delta != Vector2.Zero) velocity = (velocity * 29 + Vector2.Normalize(delta) * 24) / 30;
                            if (Vector2.Distance(position, target) < 150)
                                velocity += new Vector2(position.X < target.X ? -.25f : .25f,
                                    position.Y < target.Y ? -.25f : .25f);
                            if (age > 90) { age = 0; state = 1; }
                            break;
                        case 1:
                            velocity *= .95f;
                            if (age > 60) { velocity = Vector2.Zero; age = 0; state = 2; }
                            break;
                        case 2:
                            if (age == 1)
                            {
                                velocity = target - position;
                                if (velocity != Vector2.Zero) velocity = Vector2.Normalize(velocity) * 24;
                            }
                            else if (age > 10) { age = 0; state = 3; }
                            break;
                        default: alive = false; break;
                    }
                    if (alive) position += velocity;
                }
                Positions[t] = position; Velocities[t] = velocity; States[t] = state; Alive[t] = alive;
            }
        }

        public static void Sphere(Vector2[] result, Vector2 position, Vector2 velocity, int timeLeft,
            TrueEyeForecast parent, bool parentUpdatesFirst)
        {
            result[0] = position;
            bool attached = parent != null;
            for (int t = 1; t < result.Length; t++)
            {
                int parentTick = parentUpdatesFirst ? t : t - 1;
                if (attached && parent != null && timeLeft > 295 && parent.Alive[parentTick])
                {
                    if (parent.States[parentTick] == 0) { velocity = Vector2.Zero; attached = false; }
                    else velocity = parent.Velocities[parentTick];
                }
                position += velocity;
                result[t] = position;
                timeLeft--;
            }
        }
    }
}

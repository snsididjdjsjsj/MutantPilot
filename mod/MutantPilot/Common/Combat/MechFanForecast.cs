using System;
using System.Collections.Generic;
using System.Numerics;

namespace MutantPilot.Combat
{
    public readonly record struct FanRayBirth(int Born, Vector2 Origin, float Angle, float Turn, int Lifetime);
    public readonly record struct FanRayPose(Vector2 Origin, Vector2 Direction, float Scale, float Length);
    public sealed class FanRayPath
    {
        public FanRayBirth Birth;
        public readonly FanRayPose[] Poses = new FanRayPose[CombatPlanner.ForecastTicks + 1];
    }

    // Native state 27 fires every eleven updates. These rays damage on their
    // first AI update, so waiting until a projectile exists loses that update.
    public sealed class MechFanForecast
    {
        readonly List<FanRayPath> pool = new(20);
        public readonly List<FanRayPath> Paths = new(20);
        public void Update(int state, float timer, float orientation, float age, Vector2 center, bool masochist,int legendaryHyper=-1)
        {
            Paths.Clear();
            // Its random orientation is observed, never sampled or guessed.
            if (state != 27 || orientation == 0) return;
            for (int tick = 1; tick <= CombatPlanner.ForecastTicks; tick++)
            {
              int updates=EncounterClock.Next(ref legendaryHyper);
              for(int update=0;update<updates;update++,age++)
            {
                if (age > (masochist ? 45 : 60) && age < 240 && ++timer > 10)
                {
                    timer = 0;
                    float turn = 245 * MathF.PI / 180 * orientation / 80;
                    int lifetime = 390 - (int)age;
                    Add(tick, center, 8 * MathF.PI / 180 * orientation, turn, lifetime, masochist);
                    Add(tick, center, -8 * MathF.PI / 180 * orientation + MathF.PI, -turn, lifetime, masochist);
                    if (masochist)
                    {
                        Vector2 other = center - new Vector2(0, orientation * 1200);
                        Add(tick, other, 8 * MathF.PI / 180 * orientation + MathF.PI, turn, lifetime, true);
                        Add(tick, other, -8 * MathF.PI / 180 * orientation, -turn, lifetime, true);
                    }
                }
            }
            }
        }
        void Add(int born, Vector2 origin, float angle, float turn, int lifetime, bool masochist)
        {
            int index = Paths.Count;
            if (index == pool.Count) pool.Add(new());
            FanRayPath path = pool[index]; Paths.Add(path);
            path.Birth = new(born, origin, angle, turn, lifetime);
            Array.Clear(path.Poses);
            Vector2 initial = new(MathF.Cos(angle), MathF.Sin(angle));
            float length = 0, displayTime = Math.Min(270, lifetime + 2);
            for (int tick = born, age = 1; tick <= CombatPlanner.ForecastTicks && age < 270 && age <= lifetime; tick++, age++)
            {
                if (age > 45 && age < 150) turn *= masochist ? .9716f : .9712f;
                angle += turn;
                Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
                length = (length + 3000) * .5f;
                float scale = Math.Min(1, MathF.Sin(age * MathF.PI / displayTime) * 6);
                // AI subtracts the old direction, then normal movement adds
                // the new one. Across updates the displacement telescopes.
                path.Poses[tick] = new(origin + direction - initial, direction, scale, length);
            }
        }
    }
}

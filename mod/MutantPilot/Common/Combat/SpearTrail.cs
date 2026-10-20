using System;
using System.Collections.Generic;
using System.Numerics;

namespace MutantPilot.Combat
{
    public readonly record struct TrailOrb(Vector2 Start, Vector2 Velocity, int Born, float Padding = 0)
    {
        public int Explodes => Born + 14;
        public Vector2 CenterAt(int tick) => Start + Velocity * (tick - Born + 1);
    }

    public static class SpearTrail
    {
        public static void ForecastDash(List<TrailOrb> result, Vector2 center, Vector2 bossVelocity,
            float countdown, bool fast, int remaining)
        {
            if (bossVelocity.LengthSquared() < .001f) return;
            Vector2 forward = Vector2.Normalize(bossVelocity);
            Vector2 normal = new Vector2(-forward.Y, forward.X) * (fast ? 16 : 8);
            for (int tick = 1; tick <= Math.Min(remaining, CombatPlanner.ForecastTicks); tick++)
                if (--countdown < 0)
                {
                    countdown = fast ? 1 : 2;
                    Vector2 origin = center + bossVelocity * tick - forward;
                    result.Add(new(origin, normal, tick));
                    result.Add(new(origin, -normal, tick));
                }
        }
        // Installed Masochist spear: two AI updates per tick, a pair every four
        // AI updates, perpendicular speed 8, and 15 orb updates before exploding.
        public static void Forecast(List<TrailOrb> result, Vector2 center, Vector2 velocity,
            float countdown, int updatesPerTick, int horizon, int bornOffset = 0, float padding = 0)
        {
            if (velocity.LengthSquared() < .001f) return;
            Vector2 normal = Vector2.Normalize(new Vector2(-velocity.Y, velocity.X)) * 8;
            for (int tick = 1; tick <= horizon; tick++)
                for (int update = 0; update < updatesPerTick; update++)
                {
                    if (--countdown < 0)
                    {
                        countdown = 3;
                        result.Add(new(center, normal, tick + bornOffset, padding));
                        result.Add(new(center, -normal, tick + bornOffset, padding));
                    }
                    center += velocity;
                }
        }
    }
}

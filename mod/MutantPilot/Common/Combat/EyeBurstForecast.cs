using System;
using System.Collections.Generic;
using System.Numerics;

namespace MutantPilot.Combat
{
    // P1 boundary bullet hell: deterministic births from visible NPC counters.
    public sealed class EyeBurstForecast
    {
        public readonly List<MarkBirth> Events = new(80);
        Vector2 center;
        public void Update(int state, float timer, float angle, float age, float mode,
            float direction, Vector2 origin, bool masochist,int legendaryHyper=-1)
        {
            Events.Clear(); center = origin;
            if (state != 9 || mode != 0 || age <= 0) return;
            int count = masochist ? 5 : 4;
            for (int tick = 1; tick <= CombatPlanner.ForecastTicks; tick++)
            {
              int updates=EncounterClock.Next(ref legendaryHyper);
              for(int update=0;update<updates;update++)
            {
                if (++timer > 2)
                {
                    timer = 0;
                    angle += masochist ? .0008181231f * (age - 300) * direction : MathF.PI / 77;
                    for (int i = 0; i < count; i++)
                    {
                        float a = angle + MathF.Tau / count * i;
                        Events.Add(new(tick, new Vector2(MathF.Sin(a), -MathF.Cos(a)) * 7));
                    }
                }
                if (++age > (masochist ? 360 : 240)) return;
            }
            }
        }
        public bool Touches(Vector2 point, Vector2 half, int tick)
        {
            foreach (var birth in Events)
            {
                if (tick < birth.Born - 1) continue;
                Vector2 at = center + birth.Velocity * Math.Max(0, tick - birth.Born + 1);
                if (Math.Abs(point.X - at.X) <= half.X + 13 && Math.Abs(point.Y - at.Y) <= half.Y + 13) return true;
            }
            return false;
        }
    }
}

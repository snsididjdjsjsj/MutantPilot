using System;
using System.Collections.Generic;
using System.Numerics;

namespace MutantPilot.Combat
{
    public readonly record struct MarkBirth(int Born, Vector2 Velocity);

    // P1/P3 void-ray markers damage while travelling from the center. The inner
    // region is inside the eventual rays, but is not empty of moving markers.
    public sealed class MarkBurstForecast
    {
        public readonly List<MarkBirth> Events = new(24);
        Vector2 center;
        public void Update(int state, float countdown, float angle, float step,
            float emitted, Vector2 origin, bool masochist,int legendaryHyper=-1)
        {
            Events.Clear(); center = origin;
            if (state != -2 && state != 8) return;
            bool firstPhase = state == 8;
            for (int tick = 1; tick <= CombatPlanner.ForecastTicks; tick++)
            {
              int updates=EncounterClock.Next(ref legendaryHyper);
              for(int update=0;update<updates;update++)
            {
                if (--countdown >= 0) continue;
                float speed = !firstPhase && masochist && emitted <= 40 ? 4 : 2;
                Events.Add(new(tick, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed));
                countdown = firstPhase ? masochist ? 3 : 5 : 1; angle += step;
                float old = emitted++;
                if (firstPhase ? old == 20 || emitted == 40 : old == 40 || emitted == 80 || emitted == 120)
                    angle -= step / (masochist ? 3 : 2);
                else if (emitted >= (firstPhase ? masochist ? 60 : 40 : masochist ? 160 : 120)) return;
            }
            }
        }
        public bool Touches(Vector2 point, Vector2 playerHalf, int tick)
        {
            foreach (var mark in Events)
            {
                if (tick < mark.Born - 1) continue;
                Vector2 at = center + mark.Velocity * Math.Max(0, tick - mark.Born + 1);
                // Include either birth-slot update order, without extending this
                // reserve to the marker's distant future deathray.
                // Fargo's spawn hook has already scaled the 46px defaults to
                // the native 23px box (scale .5) before the first damage query.
                Vector2 half = playerHalf + new Vector2(11.5f + mark.Velocity.Length());
                if (Math.Abs(point.X - at.X) <= half.X && Math.Abs(point.Y - at.Y) <= half.Y) return true;
            }
            return false;
        }
    }
}

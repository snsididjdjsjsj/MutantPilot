using System;
using System.Collections.Generic;
using System.Numerics;

namespace MutantPilot.Combat
{
    public readonly record struct SphereRingEvent(int Born, int Count, float Speed, float Spin, float Offset);

    // Exact visible NPC counters for installed P1 and P3. No RNG is read:
    // the random Okuu direction/offset must already be present in the snapshot.
    public sealed class SphereBurstForecast
    {
        sealed class Path
        {
            public readonly Vector2[] Centers = new Vector2[CombatPlanner.ForecastTicks + 1];
            public int Born;
            public float Padding;
        }
        readonly List<Path> paths = new(128);
        public readonly List<SphereRingEvent> Events = new(10);
        public int Count { get; private set; }

        public void Update(int state, float ai1, float ai2, float ai3, float localAI1,
            int spriteDirection, Vector2 center, bool masochist, bool ending = false, bool buildPaths = true,int legendaryHyper=-1)
        {
            Count = 0; Events.Clear();
            if (state != 1 && state != -3 && state != -5) return;
            if (state == -3 && ai2 == 0) return; // random initialization not yet observed
            for (int tick = 1; tick <= CombatPlanner.ForecastTicks; tick++)
            {
              int updates=EncounterClock.Next(ref legendaryHyper);
              for(int update=0;update<updates;update++)
            {
                if (state == 1)
                {
                    if (--ai1 < 0)
                    {
                        ai1 = masochist ? 30 : 90;
                        ++ai2;
                        if (ai2 > (masochist ? 18 : 4)) return;
                        if (ai2 <= (masochist ? 12 : 4))
                        {
                            float sign = !masochist || (int)ai2 % 2 == 0 ? 1 : -1;
                            Emit(tick, masochist ? 9 : 6, masochist ? 10 : 9, sign, 0);
                            Emit(tick, masochist ? 9 : 6, masochist ? 10 : 9, -.5f * sign, 0);
                        }
                    }
                }
                else if (state == -3)
                {
                    int duration = masochist ? 840 : 480;
                    if (++ai1 > 10 && ai3 > 60 && ai3 < duration - 120)
                    {
                        ai1 = 0;
                        float offset = MathF.PI / 4 * (ai3 - 60) / 240 * ai2;
                        Emit(tick, masochist ? 11 : 10, masochist ? 11 : 10, -.75f, offset);
                        Emit(tick, masochist ? 11 : 10, masochist ? 11 : 10, .75f, offset);
                    }
                    if (++ai3 > duration) return;
                }
                else
                {
                    int delay = masochist && ai2 >= 330 ? 100 : 120;
                    if (++ai1 > delay)
                    {
                        ai1 = 0;
                        Emit(tick, 10, 6, .5f, 0);
                        Emit(tick, 10, 6, -.5f, 0);
                    }
                    if (ai2 == 330 && localAI1 == 0) { localAI1 = 1; ai2 -= 780; }
                    if (++ai2 > (masochist ? 1200 : 1020) && ending) return;
                }
            }
            }

            void Emit(int born, int number, float speed, float spin, float offset)
            {
                Events.Add(new(born, number, speed, spin, offset));
                if (!buildPaths) return;
                for (int i = 0; i < number; i++)
                {
                    if (Count == paths.Count) paths.Add(new());
                    Path path = paths[Count++]; path.Born = born; path.Padding = 20 + speed;
                    Vector2 position = center;
                    float angle = MathF.PI / 2 + MathF.Tau / number * i + offset;
                    Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
                    path.Centers[born - 1] = position;
                    for (int future = born; future <= CombatPlanner.ForecastTicks; future++)
                    {
                        Advance(ref position, ref velocity, speed, spin * (spriteDirection == 0 ? 1 : spriteDirection), future - born + 1);
                        path.Centers[future] = position;
                    }
                }
            }
        }

        public static void Advance(ref Vector2 position, ref Vector2 velocity, float originalSpeed, float modifier, int age)
        {
            double turn = originalSpeed / (Math.PI * 2 * modifier * age);
            Vector2 unit = Vector2.Normalize(velocity);
            velocity = new((float)(unit.X * Math.Cos(turn) - unit.Y * Math.Sin(turn)) * originalSpeed,
                (float)(unit.X * Math.Sin(turn) + unit.Y * Math.Cos(turn)) * originalSpeed);
            position += velocity;
        }

        public bool Touches(Vector2 point, Vector2 playerHalf, int tick)
        {
            for (int i = 0; i < Count; i++)
            {
                Path path = paths[i];
                if (tick < path.Born - 1) continue;
                // A full-size box plus one movement update encloses birth-slot
                // ordering and alpha scaling; live entities take over after birth.
                Vector2 center = path.Centers[Math.Max(path.Born - 1, tick)];
                if (Math.Abs(point.X - center.X) <= playerHalf.X + path.Padding
                    && Math.Abs(point.Y - center.Y) <= playerHalf.Y + path.Padding) return true;
            }
            return false;
        }
        public void AppendGuides(List<GuideTrack> guides)
        {
            for (int i = 0; i < Count; i++)
                guides.Add(new(paths[i].Centers, Math.Max(1, paths[i].Born - 1), CombatPlanner.ForecastTicks, paths[i].Padding));
        }
    }
}

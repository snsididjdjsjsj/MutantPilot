using System;
using System.Collections.Generic;
using System.Numerics;

namespace MutantPilot.Combat
{
    public readonly record struct LinearMark(Vector2 Position, Vector2 Velocity, int StartsAt, int EndsAt, float Padding = 0, float HalfSize = 11.5f);

    // Read-only local route guidance. Existing/native collisions remain authoritative.
    public sealed class VoidInnerRouting
    {
        public const float Radius = 85;
        bool active, havePhase, wasFlexible;
        long lastSearch = -100, lastUpdate;
        Vector2 center;
        float baseAngle, angularSpeed, phase;
        float radius = Radius;
        public int ForecastOverlaps { get; private set; }
        public int OpenPhases { get; private set; }
        public float MinimumClearance { get; private set; }
        public static bool Retain(int attack, float mode, long now, long until, Vector2 boss, Vector2 oldCenter)
            => (attack == 1 || attack == 9 && mode == 0) && now < until && Vector2.DistanceSquared(boss, oldCenter) < 16;
        public void Update(bool enabled, long tick, Vector2 player, Vector2 origin, float nextAngle,
            float step, Vector2 playerHalf, IReadOnlyList<LinearMark> marks, float radius = Radius, int period = 2,
            IReadOnlyList<GuideTrack> curved = null, bool flexible = false, Vector2 velocity = default)
        {
            active = enabled;
            if (!active) { havePhase = false; wasFlexible = false; return; }
            center = origin;
            float nominal = step / Math.Max(1, period);
            if (flexible)
            {
                if (!havePhase || !wasFlexible)
                {
                    baseAngle = MathF.Atan2(player.Y-center.Y, player.X-center.X); phase = 0;
                    this.radius = Math.Clamp(radius, 90, 130); angularSpeed = nominal; lastSearch = -100;
                }
                else baseAngle += angularSpeed * (tick-lastUpdate);
            }
            else
            {
                if (wasFlexible) havePhase = false;
                baseAngle = nextAngle; angularSpeed = nominal; this.radius = Math.Clamp(radius, 80, 125);
            }
            lastUpdate = tick; wasFlexible = flexible;
            if (havePhase && tick - lastSearch < 6) return;
            lastSearch = tick;
            float bestScore = float.PositiveInfinity, selected = phase, selectedSpeed = angularSpeed, selectedRadius = this.radius;
            OpenPhases = 0;
            float[] speeds = flexible ? new[] { nominal, -nominal, -.12f, -.04f, 0, .04f, .12f } : new[] { angularSpeed };
            float[] radii = flexible ? new[] { 90f, 100f, 110f, 120f, 130f } : new[] { this.radius };
            foreach (float candidateSpeed in speeds)
            foreach (float candidateRadius in radii)
            for (int candidate = 0; candidate < 48; candidate++)
            {
                float offset = MathF.Tau * candidate / 48;
                float currentAngle = baseAngle + offset;
                Vector2 current = center + new Vector2(MathF.Cos(currentAngle), MathF.Sin(currentAngle)) * candidateRadius;
                if (flexible && Vector2.DistanceSquared(current, player) > 72*72) continue;
                int overlaps = 0;
                float leastClearance = 10000;
                for (int t = 1; t <= CombatPlanner.ForecastTicks; t++)
                {
                    float a = baseAngle + offset + candidateSpeed * t;
                    Vector2 at = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * candidateRadius;
                    foreach (var mark in marks)
                    {
                        if (t < mark.StartsAt || t > mark.EndsAt) continue;
                        Vector2 position = mark.Position + mark.Velocity * (t - mark.StartsAt);
                        Vector2 half = playerHalf + new Vector2(mark.HalfSize + mark.Padding);
                        float separation = Math.Max(Math.Abs(at.X - position.X) - half.X,
                            Math.Abs(at.Y - position.Y) - half.Y);
                        leastClearance = Math.Min(leastClearance, separation);
                        if (separation <= 0)
                        { overlaps++; break; }
                    }
                    if (curved != null)
                        foreach (var track in curved)
                        {
                            if (t < track.StartsAt || t > track.EndsAt) continue;
                            Vector2 delta = Vector2.Abs(at - track.Positions[t]) - playerHalf - new Vector2(track.HalfSize);
                            float separation = Math.Max(delta.X, delta.Y);
                            leastClearance = Math.Min(leastClearance, separation);
                            if (separation <= 0) { overlaps++; break; }
                        }
                    // All remaining terms are nonnegative. Stop evaluating a
                    // guide already dominated by an inspected safe guide.
                    if (overlaps * 1000000f > bestScore) break;
                }
                if (overlaps == 0) OpenPhases++;
                float change = havePhase ? FinalSparkForecast.Wrap(offset - phase) : 0;
                float edge = Math.Max(0, 24 - leastClearance);
                float score = overlaps * 1000000 + edge * edge * 20 + change * change * 100
                    + Vector2.DistanceSquared(current, player) / 100;
                if (flexible)
                {
                    Vector2 desired = new(-MathF.Sin(currentAngle), MathF.Cos(currentAngle));
                    desired *= candidateRadius * candidateSpeed;
                    score += Vector2.DistanceSquared(desired, velocity) * 5;
                }
                if (score >= bestScore) continue;
                bestScore = score; selected = offset; selectedSpeed = candidateSpeed; selectedRadius = candidateRadius;
                ForecastOverlaps = overlaps; MinimumClearance = leastClearance;
            }
            phase = selected; angularSpeed = selectedSpeed; this.radius = selectedRadius; havePhase = true;
        }
        public Vector2 GoalAt(int tick)
        {
            float angle = baseAngle + phase + angularSpeed * tick;
            return center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
        public float Cost(Vector2 player, int tick) => active
            ? Math.Min(50000, Vector2.DistanceSquared(player, GoalAt(tick)) / 400) : 0;
        public object Snapshot() => new { active, flexible = wasFlexible, radius, phase, angularSpeed, ForecastOverlaps, OpenPhases, MinimumClearance };
    }
}

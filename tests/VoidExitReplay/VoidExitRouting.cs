using System;
using System.Numerics;

namespace MutantPilot.Combat
{
    public readonly record struct VoidExitChoice(Vector2 Center, float StartRadius, float StartAngle,
        float AngularSpeed, float RadialSpeed, int Delay, int FirstHit, float EndRadius, int Queries)
    {
        public Vector2 Goal => GoalAt(48);
        public Vector2 GoalAt(int tick)
        {
            float radius = Math.Clamp(StartRadius + RadialSpeed * Math.Max(0, tick - Delay), StartRadius, 420);
            float angle = StartAngle + AngularSpeed * Math.Min(tick, Delay)
                + AngularSpeed * StartRadius / RadialSpeed * MathF.Log(radius / StartRadius);
            return Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
    }
    public static class VoidExitRouting
    {
        // Search executable normal-input approaches through the remaining ray
        // field before the following center-born sphere rings begin.
        public static VoidExitChoice Choose(MotionState player, MotionModel motion, Vector2 center, float angularSpeed,
            Func<MotionState, int, float> danger)
        {
            Vector2 offset = player.Position - center;
            float start = MathF.Atan2(offset.Y, offset.X);
            float bestScore = float.PositiveInfinity;
            float initialRadius = Math.Clamp(offset.Length(), 80, 125);
            VoidExitChoice best = new(center, initialRadius, start, angularSpeed, 8, 0, 1, offset.Length(), 0);
            int queries = 0;
            foreach (float speed in new[] { 8f, 10f, 6f, 12f, 16f, 20f })
            foreach (int delay in new[] { 0, 3, 6, 9, 12, 18 })
            foreach (float spin in new[] { 1f, 1.5f, 2f, 3f, 4f, .5f, 0f, -.5f, -1f })
            {
                var proposal = new VoidExitChoice(center, initialRadius, start, angularSpeed * spin, speed, delay, 0, 0, 0);
                MotionState state = player; int firstHit = 0; float cost = 0;
                for (int tick = 1; tick <= CombatPlanner.ForecastTicks; tick++)
                {
                    Vector2 goal = proposal.GoalAt(tick);
                    state = Step(state, motion, goal, proposal.GoalAt(tick + 1) - goal);
                    float risk = danger(state, tick); queries++;
                    if (risk >= 1000000) { firstHit = tick; break; }
                    cost += risk;
                    if (queries >= 4096) return best with { Queries = queries };
                }
                float radius = Vector2.Distance(state.Position, center);
                float score = firstHit > 0 ? 100000000 - firstHit * 1000000
                    : cost + Math.Max(0, 420 - radius) * 10;
                if (score >= bestScore) continue;
                bestScore = score; best = proposal with { FirstHit = firstHit, EndRadius = radius };
                if (firstHit == 0 && radius >= 300) return best with { Queries = queries };
            }
            return best with { Queries = queries };
        }
        public static MotionState Step(MotionState state, MotionModel motion, Vector2 goal, Vector2 goalVelocity = default)
        {
            Vector2 delta = (goal - state.Position) * .12f + goalVelocity;
            Vector2 desired = new(Math.Clamp(delta.X, -motion.MaxX, motion.MaxX),
                Math.Clamp(delta.Y, -(motion.FlightMastery ? motion.JumpSpeed * 1.75f : motion.MaxRise), motion.MaxFall));
            float best = float.PositiveInfinity; MotionState selected = state;
            for (int id = 0; id < 12; id++)
            {
                MotionState next = motion.Step(state, Move.FromId(id));
                float score = Vector2.DistanceSquared(next.Position, goal) + 16 * Vector2.DistanceSquared(next.Velocity, desired);
                if (score >= best) continue;
                best = score; selected = next;
            }
            return selected;
        }
    }
}

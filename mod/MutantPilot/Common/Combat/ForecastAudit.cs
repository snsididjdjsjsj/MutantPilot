using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MutantPilot.Combat
{
    public readonly record struct ProjectileObservation(long Identity, int Type, Vector2 Position, float Angle);
    public readonly record struct ForecastMeasurement(int Type, int Horizon, long Samples, long Missing, long Invalid,
        double MeanPx, float MaxPx, long AngleSamples, double MeanAngleDegrees, float MaxAngleDegrees);

    // Compare old predictions with later real observations. Entity identity prevents
    // a reused projectile slot from being mistaken for a large trajectory error.
    public sealed class ForecastAudit
    {
        readonly record struct Sample(ulong At, int Slot, long Identity, int Type, int Horizon,
            Vector2 Position, float? Angle);
        sealed class Totals
        {
            public long Samples, Missing, Invalid, AngleSamples;
            public double PositionSum, AngleSum;
            public float PositionMax, AngleMax;
        }
        readonly List<Sample>[] pending = Enumerable.Range(0, CombatPlanner.ForecastTicks + 1)
            .Select(_ => new List<Sample>()).ToArray();
        readonly Dictionary<(int Type, int Horizon), Totals> totals = new();
        Totals Get(int type, int horizon)
        {
            var key = (type, horizon);
            if (!totals.TryGetValue(key, out var value)) totals[key] = value = new();
            return value;
        }
        public void Expect(ulong now, int slot, long identity, int type, int horizon, Vector2 position, float? angle = null)
        {
            if (horizon < 1 || horizon > CombatPlanner.ForecastTicks) throw new ArgumentOutOfRangeException(nameof(horizon));
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || (angle.HasValue && !float.IsFinite(angle.Value)))
            { Get(type, horizon).Invalid++; return; }
            ulong at = now + (ulong)horizon;
            pending[(int)(at % (ulong)pending.Length)].Add(new(at, slot, identity, type, horizon, position, angle));
        }
        public void Observe(ulong now, Func<int, ProjectileObservation?> read)
        {
            List<Sample> due = pending[(int)(now % (ulong)pending.Length)];
            foreach (Sample sample in due)
            {
                Totals value = Get(sample.Type, sample.Horizon);
                ProjectileObservation? observed = sample.At == now ? read(sample.Slot) : null;
                if (observed is not { } actual || actual.Identity != sample.Identity || actual.Type != sample.Type)
                { value.Missing++; continue; }
                float error = Vector2.Distance(actual.Position, sample.Position);
                if (!float.IsFinite(error) || (sample.Angle.HasValue && !float.IsFinite(actual.Angle)))
                { value.Invalid++; continue; }
                value.Samples++; value.PositionSum += error; value.PositionMax = Math.Max(value.PositionMax, error);
                if (sample.Angle is { } angle)
                {
                    float difference = actual.Angle - angle;
                    float degrees = Math.Abs(MathF.Atan2(MathF.Sin(difference), MathF.Cos(difference))) * 180 / MathF.PI;
                    value.AngleSamples++; value.AngleSum += degrees; value.AngleMax = Math.Max(value.AngleMax, degrees);
                }
            }
            due.Clear();
        }
        public ForecastMeasurement[] Snapshot() => totals.OrderBy(p => p.Key.Type).ThenBy(p => p.Key.Horizon)
            .Select(p => new ForecastMeasurement(p.Key.Type, p.Key.Horizon, p.Value.Samples, p.Value.Missing, p.Value.Invalid,
                p.Value.PositionSum / Math.Max(1, p.Value.Samples), p.Value.PositionMax,
                p.Value.AngleSamples, p.Value.AngleSum / Math.Max(1, p.Value.AngleSamples), p.Value.AngleMax)).ToArray();
    }
}

using System;
using System.Numerics;

namespace MutantPilot.Combat
{
    public readonly record struct PendingSpear(Vector2 Origin, Vector2 Velocity, int Born, float Padding)
    {
        public bool Touches(Vector2 point, Vector2 half, int tick)
        {
            if (tick < Born) return false;
            Vector2 direction = Vector2.Normalize(Velocity);
            // Union of the shaft and entity box across both native sub-updates.
            Vector2 start = Origin + Velocity * (2 * (tick - Born)) - direction * 108;
            return FlatBeam.Intersects(start, direction, 216 + Velocity.Length() * 2, 16,
                point, half + new Vector2(Padding));
        }
    }
    public static class LockedTossForecast
    {
        public static PendingSpear? Read(int state, float timer, float emitted, float limit,
            float angle, Vector2 center, Vector2 velocity)
        {
            // P1 tracks player+velocity*30 only while ai1<145. ai1 is already
            // incremented when that comparison executes, so the snapshot at 144
            // contains the final aim. The next throw is at 151, unless it exits.
            if (state != 0 || timer < 144 || timer > 150 || emitted + 1 > limit) return null;
            int lead = 151 - (int)timer;
            // NPC AI spawns before that update's NPC displacement.
            // Masochist Movement(.5) can reverse each velocity axis by five
            // per update. Bound its accumulated displacement before birth.
            float uncertainty = 2.5f * (lead - 1) * lead + 2;
            return new PendingSpear(center + velocity * (lead - 1),
                new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 25, lead, uncertainty);
        }
    }
}

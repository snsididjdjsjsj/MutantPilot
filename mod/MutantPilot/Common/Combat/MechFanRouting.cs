using System;
using System.Numerics;

namespace MutantPilot.Combat
{
    public static class MechFanRouting
    {
        static readonly float[] gapTangents = MakeTangents();
        public static Vector2 Goal(Vector2 boss, float orientation) => boss - new Vector2(0, Math.Sign(orientation) * 600);
        public static float GapCost(Vector2 point, Vector2 boss, float orientation, float age, int future)
        {
            if (orientation == 0) return 0;
            float between = -(point.Y - boss.Y) * Math.Sign(orientation);
            if (between <= 0 || between >= 1200) return 0;
            int oldestAge = Math.Clamp((int)(age + future) - 56, 0, 269);
            float tangent = gapTangents[oldestAge];
            if (!float.IsFinite(tangent)) return 0;
            float half = Math.Max(20, Math.Min(between, 1200 - between) * tangent - 30);
            float outside = Math.Max(0, Math.Abs(point.X - boss.X) - half);
            return Math.Min(50000, outside * outside / 400);
        }
        static float[] MakeTangents()
        {
            var result = new float[270];
            float turn = 245 * MathF.PI / 180 / 80, angle = 8 * MathF.PI / 180;
            result[0] = float.PositiveInfinity;
            for (int age = 1; age < result.Length; age++)
            {
                if (age > 45 && age < 150) turn *= .9716f;
                angle += turn;
                result[age] = angle <= MathF.PI ? float.PositiveInfinity : MathF.Tan(3 * MathF.PI / 2 - angle);
            }
            return result;
        }
    }
    public static class SpearOrbitRouting
    {
        public static Vector2 Tangent(Vector2 player, Vector2 boss, int side)
        {
            Vector2 offset = player - boss;
            if (offset.LengthSquared() < 1) offset = Vector2.UnitX;
            offset = Vector2.Normalize(offset);
            return new Vector2(-offset.Y, offset.X) * (side < 0 ? -1 : 1);
        }
    }
}

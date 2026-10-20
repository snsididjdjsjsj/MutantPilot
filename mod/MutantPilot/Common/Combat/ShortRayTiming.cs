using System;
using System.Numerics;

namespace MutantPilot.Combat
{
    public readonly record struct PendingVoidRay(Vector2 Origin, Vector2 Direction, int ArmsAt, int EndsAt);
    public static class ShortRayTiming
    {
        public static float Scale(int kind, float age)
        {
            float duration = kind == 1 ? 60 : 180;
            if (age <= 0 || age >= duration) return 0;
            float wave = (float)Math.Sin(age * MathF.PI / duration);
            return kind == 1 ? Math.Min(1, wave * 4) : Math.Min(.7f, wave * 2.5f * .7f);
        }
        public static bool Damages(int kind, float scale) => scale >= (kind == 1 ? 1 : .7f);
    }
}

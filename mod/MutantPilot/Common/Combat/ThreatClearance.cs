using System;
using System.Numerics;

namespace MutantPilot.Combat
{
    public static class ThreatClearance
    {
        public static float Box(Vector2 point, Vector2 center, Vector2 combinedHalf, float reserve)
        {
            if (!Finite(point) || !Finite(center) || !Finite(combinedHalf) || !float.IsFinite(reserve)) return 0;
            Vector2 gap = Vector2.Max(Vector2.Zero, Vector2.Abs(point - center) - combinedHalf);
            if (gap.X >= reserve || gap.Y >= reserve) return 0;
            return Cost(MathF.Sqrt(gap.LengthSquared()), reserve);
        }
        public static float Circle(Vector2 point, Vector2 center, Vector2 playerHalf, float radius, float reserve)
        {
            if (!Finite(point) || !Finite(center) || !Finite(playerHalf)
                || !float.IsFinite(radius) || !float.IsFinite(reserve)) return 0;
            Vector2 gap = Vector2.Max(Vector2.Zero, Vector2.Abs(point - center) - playerHalf);
            if (gap.X >= radius + reserve || gap.Y >= radius + reserve) return 0;
            return Cost(Math.Max(0, gap.Length() - radius), reserve);
        }
        static bool Finite(Vector2 point) => float.IsFinite(point.X) && float.IsFinite(point.Y);
        public static float Beam(Vector2 point,Vector2 origin,Vector2 direction,float length,float width,Vector2 playerHalf,float reserve)
        {
            if(!Finite(point)||!Finite(origin)||!Finite(direction)||!Finite(playerHalf)
                ||!float.IsFinite(length)||!float.IsFinite(width)||!float.IsFinite(reserve)||direction.LengthSquared()<.000001f)return 0;
            direction=Vector2.Normalize(direction);
            Vector2 normal=new(-direction.Y,direction.X),delta=point-origin;
            float along=Vector2.Dot(delta,direction),across=Math.Abs(Vector2.Dot(delta,normal));
            float alongHalf=Vector2.Dot(Vector2.Abs(direction),playerHalf),acrossHalf=Vector2.Dot(Vector2.Abs(normal),playerHalf);
            float x=Math.Max(0,Math.Max(-along-alongHalf,along-length-alongHalf));
            float y=Math.Max(0,across-width/2-acrossHalf);
            return Cost(MathF.Sqrt(x*x+y*y),reserve);
        }
        static float Cost(float clearance, float reserve)
        {
            if (reserve <= 0 || clearance >= reserve) return 0;
            float intrusion = 1 - clearance / reserve;
            // Keep a traversable narrow route worth taking to resume damage.
            // This preference is tiny beside a collision's one-million cost.
            return 8 * intrusion * intrusion;
        }
    }
}

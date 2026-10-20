using System;
using System.Numerics;

namespace MutantPilot.Combat
{
    // A deliberately loose necessary-condition test. It only rejects distant
    // queries; every remaining query still uses Terraria's native collision.
    public readonly struct BeamBroadPhase
    {
        readonly Vector2 origin, direction, originHalf;
        readonly float length, halo;
        readonly bool valid;
        public BeamBroadPhase(Vector2 origin, Vector2 velocity, float length, float scale, Vector2 originHalf)
        {
            this.origin = origin; this.originHalf = originHalf + new Vector2(2);
            float speed = velocity.Length();
            direction = speed > 0 ? velocity / speed : Vector2.Zero;
            this.length = length * speed;
            halo = 96 * Math.Max(1, Math.Abs(scale)) + 4;
            valid = float.IsFinite(origin.X) && float.IsFinite(origin.Y) && float.IsFinite(direction.X)
                && float.IsFinite(direction.Y) && float.IsFinite(this.length) && float.IsFinite(halo)
                && speed > .000001f && this.length >= 0;
        }
        public bool MayTouch(Vector2 point, Vector2 half)
        {
            if (!valid || !float.IsFinite(point.X) || !float.IsFinite(point.Y)
                || !float.IsFinite(half.X) || !float.IsFinite(half.Y)) return true;
            Vector2 delta = point - origin;
            if (Math.Abs(delta.X) <= half.X + originHalf.X && Math.Abs(delta.Y) <= half.Y + originHalf.Y) return true;
            float along = Vector2.Dot(delta, direction);
            float longitudinal = Math.Abs(direction.X) * half.X + Math.Abs(direction.Y) * half.Y + halo;
            if (along < -longitudinal || along > length + longitudinal) return false;
            float across = delta.Y * direction.X - delta.X * direction.Y;
            float transverse = Math.Abs(direction.Y) * half.X + Math.Abs(direction.X) * half.Y + halo;
            return Math.Abs(across) <= transverse;
        }
    }
}

using System;
using System.Numerics;

namespace MutantPilot.Combat
{
    // The initial blade sweeps the front half-circle. Cross its back during
    // the 90-tick wind-up, then continue horizontally as the boss lunges.
    public static class SwordRouting
    {
        public static Vector2 Preparation(Vector2 boss,float face,float step)
        {
            int side=face<0?-1:1,vertical=Math.Sign(step)*side;
            return boss+new Vector2(-side,vertical)*(500/MathF.Sqrt(2));
        }
        public static Vector2 Escape(Vector2 player,float face)=>player+new Vector2(face<0?700:-700,0);
    }
}

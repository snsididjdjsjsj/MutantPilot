using System.Numerics;

namespace MutantPilot.Combat
{
    public static class RitualForecast
    {
        public static Vector2 Step(Vector2 arena,Vector2 boss,int state,float timer,Vector2 player)
        {
            if(state==19 || state==49&&timer>=30)return arena;
            if(state==49)return arena+(player-arena)/10;
            float divisor=state==36?20:state is 22 or 23 or 25?40:60;
            return arena+(boss-arena)/divisor;
        }
    }
}

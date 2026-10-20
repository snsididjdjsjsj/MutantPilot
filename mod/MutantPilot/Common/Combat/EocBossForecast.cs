using System;
using System.Numerics;

namespace MutantPilot.Combat
{
    // State 20 stops following the player at timer 120. Its saved target then
    // makes NPC motion deterministic until ChooseNextAttack at timer 451.
    public static class EocBossForecast
    {
        public static int Forecast(Vector2[] path,Vector2 position,Vector2 velocity,Vector2 locked,float timer)
        {
            path[0]=position;int known=0;
            if(timer<120||timer>450)return 0;
            for(int tick=1;tick<path.Length && timer<=450;tick++,timer++)
            {
                Vector2 away=position-locked;
                if(away.LengthSquared()<1e-6f)break;
                away=Vector2.Normalize(away);
                double angle=-Math.PI/36;
                Vector2 radial=new((float)(away.X*Math.Cos(angle)-away.Y*Math.Sin(angle)),
                    (float)(away.X*Math.Sin(angle)+away.Y*Math.Cos(angle)));
                Vector2 target=locked+radial*450;
                if(Vector2.DistanceSquared(position,target)>50*50)
                {
                    const float speed=.5f;
                    if(Math.Abs(position.X-target.X)>10)
                    {
                        int sign=position.X<target.X?1:-1;velocity.X+=sign*speed;
                        if(velocity.X*sign<0)velocity.X+=sign*speed*4;
                    }
                    int vertical=position.Y<target.Y?1:-1;velocity.Y+=vertical*speed;
                    if(velocity.Y*vertical<0)velocity.Y+=vertical*speed*4;
                    velocity=Vector2.Clamp(velocity,new(-36),new(36));
                }
                path[tick]=position+=velocity;known=tick;
            }
            return known;
        }
    }
}

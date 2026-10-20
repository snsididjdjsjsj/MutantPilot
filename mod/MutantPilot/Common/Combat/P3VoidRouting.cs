using System;
using System.Numerics;

namespace MutantPilot.Combat
{
    // The 1200px ritual cannot contain a static gap for all three 3-degree
    // phase groups. Their damaging 55-tick windows do not all overlap.
    public sealed class P3VoidRouting
    {
        Vector2 center;
        float cell,step,elapsed;
        long observed;
        bool active;
        public bool Active=>active;
        public static float Offset(float time,float step)=>step/3*(1-Math.Clamp((time-232)/12,0,1)-Math.Clamp((time-310)/12,0,1));
        public static Vector2 Point(Vector2 center,float cell,float step,float elapsed)
        {
            float angle=cell+Offset(elapsed,step);
            return center+new Vector2(MathF.Cos(angle),MathF.Sin(angle))*1000;
        }
        public void Update(int attack,bool masochist,Vector2 player,Vector2 boss,float markerAngle,
            float markerStep,float emitted,float countdown,long now)
        {
            if(!masochist || attack is not (-2 or -3)){active=false;return;}
            if(attack==-2)
            {
                if(!active)
                {
                    step=markerStep;
                    int shifts=(emitted>=41?1:0)+(emitted>=80?1:0)+(emitted>=120?1:0);
                    float start=markerAngle-emitted*step+shifts*step/3;
                    float bearing=MathF.Atan2(player.Y-boss.Y,player.X-boss.X);
                    if(Math.Abs(step)<.001f){active=false;return;}
                    cell=start+MathF.Round(FinalSparkForecast.Wrap(bearing-start-step/3)/step)*step;
                    center=boss;
                }
                elapsed=emitted<=0?0:2*(emitted-1)+(countdown<.5f?1:0);
                observed=now;active=true;
            }
            else
            {
                if(!active)return;
                elapsed+=now-observed;observed=now;
                if(elapsed>472 || Vector2.DistanceSquared(center,boss)>2500)active=false;
            }
        }
        public Vector2 Goal()=>Point(center,cell,step,elapsed);
        public float Cost(Vector2 point,int tick)
        {
            if(!active)return 0;
            float angle=cell+Offset(elapsed+tick,step);
            Vector2 delta=point-center;
            float across=-MathF.Sin(angle)*delta.X+MathF.Cos(angle)*delta.Y;
            return Math.Min(50000,across*across/200);
        }
        public object Snapshot()=>new{active,elapsed,cell,step,radius=1000,x=center.X,y=center.Y};
    }
}

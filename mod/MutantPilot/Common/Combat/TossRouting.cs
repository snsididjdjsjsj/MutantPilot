using System;
using System.Numerics;

namespace MutantPilot.Combat
{
    public static class TossRouting
    {
        // Match the observed boss orbit and retain an outward component. A pure
        // circular tangent revisits the opposite half of a paired ray after about
        // 129 FTW game ticks, before its native 156-tick lifetime has ended.
        public const float OutwardRatio=.85f;
        public static Vector2 Goal(Vector2 player,Vector2 boss,float orbitSide,float distance=700)
        {
            Vector2 delta=player-boss;
            Vector2 away=delta.LengthSquared()>1e-6f?Vector2.Normalize(delta):Vector2.UnitX;
            Vector2 tangent=new(-away.Y,away.X);
            Vector2 direction=away*OutwardRatio+tangent*(orbitSide<0?-1:1);
            return player+Vector2.Normalize(direction)*distance;
        }

        // A fixed goal turns a long orbit into a brake-and-return proposal. Build
        // a moving preference with ordinary controls and the observed NPC clock.
        // This is only a proposal: the planner still certifies every real hazard.
        public static void Track(Vector2[] goals,MotionState player,MotionModel motion,
            TossWorld boss,float offset,float side,float limit,int hyper)
        {
            Vector2 heading=Vector2.Normalize(Goal(player.Position,boss.Position,side)-player.Position);
            for(int tick=0;tick<goals.Length;tick++)
            {
                if(!boss.Closed)
                    heading=Vector2.Normalize(Goal(player.Position,boss.Position,side)-player.Position);
                goals[tick]=player.Position+heading*120;
                if(tick==goals.Length-1)break;
                Vector2 target=player.Position+heading*240;
                Vector2 velocity=new(Math.Clamp(heading.X*24,-motion.MaxX,motion.MaxX),
                    Math.Clamp(heading.Y*24,-(motion.FlightMastery?motion.JumpSpeed*1.75f:motion.MaxRise),motion.MaxFall));
                float best=float.PositiveInfinity;MotionState chosen=player;
                for(int id=0;id<12;id++)
                {
                    MotionState next=motion.Step(player,Move.FromId(id));
                    float score=Vector2.DistanceSquared(next.Position,target)+16*Vector2.DistanceSquared(next.Velocity,velocity);
                    if(score>=best)continue;
                    best=score;chosen=next;
                }
                player=chosen;
                boss=TossResponseForecast.GameFrame(boss,player,tick+1,offset,side,limit,false,
                    EncounterClock.Next(ref hyper));
            }
        }
    }
}

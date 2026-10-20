using System.Numerics;
using MutantPilot.Combat;

static class SwordRoutingChecks
{
    public static void Run()
    {
        foreach(int side in new[]{-1,1})foreach(int vertical in new[]{-1,1})
        {
            float step=side*vertical*MathF.PI/20;
            float axis=-MathF.PI/2-side*MathF.PI/4-(vertical<0?side*MathF.PI/2:0);
            var parts=new List<SwordPart>();
            for(int i=1;i<12;i++)parts.Add(new(i,80*i,axis,109));
            for(int j=-1;j<=1;j+=2)
            {
                parts.Add(new(parts.Count,180,axis+j*26.5f*MathF.PI/180,109));
                parts.Add(new(parts.Count,240,axis+j*40f*MathF.PI/180,109));
            }
            var world=new SwordResponseForecast();
            world.Reset(new(Vector2.Zero,Vector2.Zero,1,0,true),step,parts);
            // The observed .84 wind-up start, plus horizontal and vertical
            // variants. This fixture contains no concurrent foreign hazards.
            var player=new MotionState(new(side*345.934f,vertical*183.664f),
                new(-side*14.262514f,-vertical*22.119987f),new(1,1,0,15,side>0,side<0));
            var motion=new MotionModel{FlightMastery=true,MaxX=18,WalkSpeed=8.669999f,Acceleration=1.3125f,
                Braking=1,StopBraking=8,Gravity=.6f,MaxFall=10.01f,JumpSpeed=13.91f};
            float reference=0;
            for(int tick=1;tick<=145;tick++)
            {
                bool preparing=world.At(reference).Preparing;
                Vector2 target=preparing?SwordRouting.Preparation(Vector2.Zero,side,step):SwordRouting.Escape(player.Position,side);
                Vector2 delta=(target-player.Position)*.12f;
                Vector2 desired=new(Math.Clamp(delta.X,-18,18),Math.Clamp(delta.Y,-13.91f*1.75f,10.01f));
                float best=float.PositiveInfinity;MotionState selected=player;
                for(int id=0;id<12;id++)
                {
                    var next=motion.Step(player,Move.FromId(id));
                    float score=Vector2.DistanceSquared(next.Position,target)+16*Vector2.DistanceSquared(next.Velocity,desired);
                    if(score<best){best=score;selected=next;}
                }
                player=selected;reference=world.Advance(reference,player,tick);
                var pose=world.At(reference);
                if(world.Touches(reference,player.Position,new(13),tick)
                    ||pose.ForcedDeath<0&&Vector2.Distance(player.Position,pose.Position)<73)
                    throw new Exception($"Sword back route touches isolated geometry: side={side} vertical={vertical} tick={tick}");
            }
        }
        Console.WriteLine("PASS normal-input sword back route clears wind-up, sweep, boss contact and explosions in four orientations (isolated geometry)");
    }
}

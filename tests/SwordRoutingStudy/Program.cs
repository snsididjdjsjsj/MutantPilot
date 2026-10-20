using System.Numerics;
using System.Text.Json;
using MutantPilot.Combat;

var parts=new List<SwordPart>();
float axis=-3*MathF.PI/4;
for(int i=1;i<12;i++)parts.Add(new(i,80*i,axis,109));
for(int side=-1;side<=1;side+=2)
{
    parts.Add(new(parts.Count,180,axis+26.5f*side*MathF.PI/180,109));
    parts.Add(new(parts.Count,240,axis+40f*side*MathF.PI/180,109));
}
var motion=new MotionModel {FlightMastery=true,MaxX=18,WalkSpeed=8.669999f,Acceleration=1.3125f,
    Braking=1,StopBraking=8,Gravity=.6f,MaxFall=10.01f,JumpSpeed=13.91f};
var output=new List<object>();
for(int bearing=0;bearing<16;bearing++)
foreach(int radius in new[]{350,500,700})
for(int escape=0;escape<16;escape++)
{
    var forecast=new SwordResponseForecast();
    forecast.Reset(new(Vector2.Zero,Vector2.Zero,1,0,true),MathF.PI/20,parts);
    var player=new MotionState(new(345.934f,183.664f),new(-14.262514f,-22.119987f),new(1,1,0,15,true,false));
    float reference=0;int hits=0,first=0;bool launched=false;Vector2 goal=Unit(bearing)*radius,escapeGoal=default;
    for(int tick=1;tick<=145;tick++)
    {
        var world=forecast.At(reference);
        if(!world.Preparing && !launched){launched=true;escapeGoal=player.Position+Unit(escape)*900;}
        Vector2 target=launched?escapeGoal:goal,delta=(target-player.Position)*.12f;
        Vector2 desired=new(Math.Clamp(delta.X,-18,18),Math.Clamp(delta.Y,-13.91f*1.75f,10.01f));
        float score=float.PositiveInfinity;MotionState selected=player;
        for(int id=0;id<12;id++)
        {
            var next=motion.Step(player,Move.FromId(id));
            float value=Vector2.DistanceSquared(next.Position,target)+16*Vector2.DistanceSquared(next.Velocity,desired);
            if(value<score){score=value;selected=next;}
        }
        player=selected;reference=forecast.Advance(reference,player,tick);
        world=forecast.At(reference);
        bool hit=forecast.Touches(reference,player.Position,new(13),tick)
            || world.ForcedDeath<0&&Vector2.Distance(player.Position,world.Position)<73;
        if(hit){hits++;if(first==0)first=tick;}
    }
    if(hits==0)output.Add(new{bearing,radius,escape});
}
Console.WriteLine(JsonSerializer.Serialize(new{scope="isolated native sword geometry; no parallel hazards; not a game victory",safe=output}));
static Vector2 Unit(int n)=>new(MathF.Cos(n*MathF.PI/8),MathF.Sin(n*MathF.PI/8));

using System.Numerics;
using System.Text.Json;
using MutantPilot.Combat;

static class DirectDashChecks
{
    public static void Run()
    {
        var waiting=new DirectDashWorld(new(100,100),new(10,0),22,4,0,6);
        var player=new MotionState(new(400,500),new(12,-4));
        var wait=DirectDashResponseForecast.Step(waiting,player,1);
        if(wait.State!=22||wait.Timer!=5||wait.A!=null||wait.Position!=new Vector2(109,100))
            throw new Exception("direct dash must brake and wait through timer 5");
        var fire=DirectDashResponseForecast.Step(wait,player,2);
        if(fire.State!=23||fire.Timer!=0||fire.A is not DirectCharge charge
            ||Vector2.Distance(fire.Velocity,Vector2.Normalize(player.Position-wait.Position)*60)>1e-4f
            ||charge.Origin!=wait.Position||charge.Born!=2||charge.EndsAt!=22)
            throw new Exception("direct dash must lock this candidate's position on timer 6 and retain the birth origin");
        var one=DirectDashResponseForecast.Step(fire,player,3);
        var two=DirectDashResponseForecast.Step(one,player,4);
        if(one.Timer!=2||two.Timer!=3)throw new Exception("native state 23 skips multiples of three");
        var end=DirectDashResponseForecast.Step(fire with {Timer=30},player,23);
        if(end.State!=22||end.Timer!=0||end.Velocity!=fire.Velocity)
            throw new Exception("direct dash must leave state 23 before another spear damage update");
        var p1=DirectDashResponseForecast.Step(new(Vector2.Zero,Vector2.Zero,5,8,2,0,Delay:8),player,1);
        if(p1.State!=6||p1.Timer!=0||p1.A is not DirectCharge p1Charge||p1Charge.EndsAt!=31
            ||Vector2.Distance(p1.Velocity,Vector2.Normalize(player.Position+player.Velocity)*45)>1e-4f)
            throw new Exception("P1 direct dash must use the observed delay and native position+velocity lock at speed 45");
        if(DirectDashResponseForecast.Step(p1,player,2).Timer!=1)
            throw new Exception("P1 direct dash must not inherit the P2 timer skip");
        var predictive=new DirectDashWorld(Vector2.Zero,Vector2.Zero,14,54,1,3);
        var locked=DirectDashResponseForecast.Step(predictive,player,1);
        if(locked.State!=14||locked.Timer!=55||Math.Abs(locked.Aim-MathF.Atan2(380,760))>.0001f)
            throw new Exception("predictive dash must update its own candidate position+velocity*30 aim before timer 55");
        for(int t=2;t<=7;t++)locked=DirectDashResponseForecast.Step(locked,player with {Position=new(-800,300)},t);
        if(locked.State!=15||Vector2.Distance(locked.Velocity,Vector2.Normalize(new Vector2(760,380))*45)>.001f)
            throw new Exception("predictive dash must retain the locked direction despite later candidate turns and fire after timer 60");
        var fast=DirectDashResponseForecast.Step(new(Vector2.Zero,Vector2.Zero,14,80,2,3,Aim:0),player,1);
        if(fast.State!=15||fast.A?.TrailSpeed!=16)throw new Exception("last predictive dash must retain the doubled-speed trail");
        var trailTree=new DirectDashResponseForecast();
        trailTree.Reset(new(Vector2.Zero,Vector2.Zero,52,0,0,3,A:new(Vector2.Zero,new(45,0),1,31,16)));
        bool fastTouch=trailTree.Touches(0,new(90,300),new(13),15);
        trailTree.Reset(new(Vector2.Zero,Vector2.Zero,52,0,0,3,A:new(Vector2.Zero,new(45,0),1,31,8)));
        if(!fastTouch||trailTree.Touches(0,new(90,300),new(13),15))
            throw new Exception("fast final-dash trail must reach a native danger region that ordinary-speed trails do not");
        var tree=new DirectDashResponseForecast();tree.Reset(wait);
        float a=tree.Advance(0,player,1),b=tree.Advance(0,player with {Position=player.Position+new Vector2(300,0)},1);
        if(a==b||tree.At(a).Velocity==tree.At(b).Velocity)
            throw new Exception("candidate direct dash locks must keep separate histories");
        tree.Reset(new(new(60813.3f,15866.008f),new(-1.9514023f,59.96826f),23,8,1,6));
        float native=tree.Advance(0,new(new(60782.41f,15931.64f),new(-9.1875f,-10.394991f)),1);
        if(Vector2.Distance(tree.At(native).Position,new(60811.348f,15925.977f))>.02f)
            throw new Exception("direct dash must reproduce 0.3.78 native NPC contact position");
        string root=Directory.GetCurrentDirectory();
        while(!File.Exists(Path.Combine(root,"mod","MutantPilot","build.txt")))root=Directory.GetParent(root)?.FullName??throw new Exception("Project root missing");
        string fixture=Path.Combine(root,"tests","fixtures","direct_dash_native_pairs.json");
        if(File.Exists(fixture))
        {
            var pairs=JsonDocument.Parse(File.ReadAllText(fixture));float max=0;int count=0;
            foreach(var pair in pairs.RootElement.EnumerateArray())
            {
                float F(JsonElement obj,string key)=>obj.GetProperty(key).GetSingle();
                var before=pair.GetProperty("before");var after=pair.GetProperty("after");
                var ai=before.GetProperty("bossAI");
                var world=new DirectDashWorld(new(F(before,"bossX"),F(before,"bossY")),new(F(before,"bossVx"),F(before,"bossVy")),
                    (int)F(before,"attack"),ai[1].GetSingle(),ai[2].GetSingle(),before.GetProperty("bossLocalAI")[1].GetSingle(),Delay:ai[3].GetSingle(),Aim:before.GetProperty("bossLocalAI")[0].GetSingle());
                var predicted=DirectDashResponseForecast.Step(world,new(new(F(after,"playerX"),F(after,"playerY")),new(F(after,"vx"),F(after,"vy"))),1);
                float error=Vector2.Distance(predicted.Position,new(F(after,"bossX"),F(after,"bossY")));
                if(error>.04f)throw new Exception($"native direct-dash movement mismatch at {pair.GetProperty("file")} tick {before.GetProperty("tick")}: {error}");
                max=Math.Max(max,error);count++;
            }
            Console.WriteLine($"PASS direct-dash {count} native movement pairs, maximum position error {max:F6}px");
        }
        Console.WriteLine("PASS direct-dash native timer, candidate lock, birth position and observed NPC contact");
    }
}

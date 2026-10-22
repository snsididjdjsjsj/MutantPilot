using System.Numerics;
using System.Text.Json;
using MutantPilot.Combat;

static class LegendaryClockChecks
{
    public static void Run()
    {
        for(int initial=0;initial<=6;initial++)
        {
            int phase=initial;
            for(int tick=1;tick<=200;tick++)
                if(EncounterClock.Next(ref phase)!=EncounterClock.UpdatesAt(initial,tick))
                    throw new Exception("FTW AI phase mismatch");
        }
        var start=new SwordWorld(new(100,200),new(3,4),19,0,false);
        var charge=DirectDashResponseForecast.ObservedCharge(new(101,200),new(50,0),2,6,28,6,false);
        if(charge.Born!=0||charge.EndsAt!=1||charge.Origin!=new Vector2(0,200))
            throw new Exception("Observed FTW dash uses projectile countdown and game-time expiry instead of AI-time displacement");
        var ballistic=new Vector2[3];
        ProjectileKinematics.Ballistic(ballistic,Vector2.Zero,new(0,4),.5f,6);
        if(ballistic[1]!=new Vector2(0,5)||ballistic[2]!=new Vector2(0,10.5f))
            throw new Exception("FTW nuke applies two gravity updates but one displacement");
        var twice=SwordResponseForecast.GameFrame(start,Vector2.Zero,1,.1f,2);
        if(twice.ForcedDeath!=1||twice.DeathPosition!=start.Position||twice.DeathAngle!=start.Angle
            ||twice.Position!=start.Position+start.Velocity)
            throw new Exception("FTW sword expiry must use the previous projectile pose and one NPC displacement");
        Console.WriteLine("PASS FTW AI clocks and sword expiry preserve native update order");
    }
    public static void Audit(string path)
    {
        var frames=File.ReadLines(path).Select(line=>JsonDocument.Parse(line)).Where(d=>d.RootElement.GetProperty("kind").GetString()=="frame")
            .Select(d=>d.RootElement.Clone()).ToList();
        int pairs=0,bad=0;float maxPosition=0,maxAngle=0;
        foreach(var (f,next) in frames.Zip(frames.Skip(1)))
        {
            if(f.GetProperty("attack").GetSingle()!=9||next.GetProperty("attack").GetSingle()!=9
                ||next.GetProperty("gameTick").GetInt64()!=f.GetProperty("gameTick").GetInt64()+1)continue;
            var ai=f.GetProperty("bossAI");var local=f.GetProperty("bossLocalAI");var nl=next.GetProperty("bossLocalAI");
            if(local[2].GetSingle() is not (1 or 2)||nl[2].GetSingle() is not (1 or 2))continue;
            float F(string key)=>f.GetProperty(key).GetSingle();float N(string key)=>next.GetProperty(key).GetSingle();
            var world=new SwordWorld(new(F("bossX"),F("bossY")),new(F("bossVx"),F("bossVy")),ai[1].GetSingle(),ai[3].GetSingle(),local[2].GetSingle()==1);
            int phase=f.GetProperty("bossHyper").GetInt32();
            var predicted=SwordResponseForecast.GameFrame(world,new(N("playerX"),N("playerY")),1,ai[2].GetSingle(),EncounterClock.Next(ref phase));
            float pe=Vector2.Distance(predicted.Position,new(N("bossX"),N("bossY")));
            float ae=Math.Abs(FinalSparkForecast.Wrap(predicted.Angle-next.GetProperty("bossAI")[3].GetSingle()));
            pairs++;maxPosition=Math.Max(maxPosition,pe);maxAngle=Math.Max(maxAngle,ae);
            if(pe>.1f||ae>.0001f)bad++;
        }
        Console.WriteLine(JsonSerializer.Serialize(new{developmentOnly=true,victoryCertification=false,pairs,bad,maxPosition,maxAngle}));
        if(pairs==0||bad!=0)throw new Exception("Native FTW sword replay differs");
    }
    public static void AuditDash(string path,string spawnPath)
    {
        var births=new Dictionary<long,List<Vector2>>();
        foreach(string line in File.ReadLines(spawnPath))
        {
            if(!line.Contains("\"n\":\"MutantSphereSmall\""))continue;
            using var doc=JsonDocument.Parse(line);var row=doc.RootElement;
            if(row.GetProperty("st").GetInt32() is not (6 or 15 or 23))continue;
            long tick=row.GetProperty("t").GetInt64();
            if(!births.TryGetValue(tick,out var list))births[tick]=list=new();
            list.Add(new(row.GetProperty("x").GetSingle(),row.GetProperty("y").GetSingle()));
        }
        int pairs=0,bad=0;float maxError=0;
        foreach(string line in File.ReadLines(path))
        {
            using var doc=JsonDocument.Parse(line);var f=doc.RootElement;
            if(f.GetProperty("kind").GetString()!="frame"||!f.TryGetProperty("navigation",out var n)
                ||!n.TryGetProperty("activeDash",out var dash)||dash.ValueKind==JsonValueKind.Null)continue;
            int state=(int)f.GetProperty("attack").GetSingle();if(state is not (6 or 15 or 23))continue;
            var velocity=new Vector2(f.GetProperty("bossVx").GetSingle(),f.GetProperty("bossVy").GetSingle());
            float countdown=dash.GetProperty("countdown").GetSingle();int born=(int)countdown+1;
            var charge=DirectDashResponseForecast.ObservedCharge(new(dash.GetProperty("x").GetSingle(),dash.GetProperty("y").GetSingle()),
                velocity,countdown,state,f.GetProperty("bossAI")[1].GetSingle(),f.GetProperty("bossHyper").GetInt32(),dash.GetProperty("fast").GetBoolean());
            if(born>charge.EndsAt)continue;
            long at=f.GetProperty("gameTick").GetInt64()+born;
            Vector2 expected=charge.Origin+velocity*(born-charge.Born+2);
            float error=births.TryGetValue(at,out var actual)?actual.Min(p=>Vector2.Distance(p,expected)):float.PositiveInfinity;
            pairs++;maxError=Math.Max(maxError,error);
            if(error>.1f){if(bad<3)Console.WriteLine(JsonSerializer.Serialize(new{tick=f.GetProperty("tick").GetInt32(),at,error=error.ToString(),expectedX=expected.X,expectedY=expected.Y}));bad++;}
        }
        Console.WriteLine(JsonSerializer.Serialize(new{developmentOnly=true,victoryCertification=false,pairs,bad,maxError=maxError.ToString()}));
        if(pairs==0||bad>0)throw new Exception("Native FTW dash-trail replay differs");
    }
}

using System.Numerics;
using System.Text.Json;
using MutantPilot.Combat;

static class EocBossChecks
{
    public static void Run()
    {
        var path=new Vector2[49];
        if(EocBossForecast.Forecast(path,Vector2.One,Vector2.Zero,Vector2.Zero,119)!=0
            ||EocBossForecast.Forecast(path,new(100,0),Vector2.Zero,Vector2.Zero,450)!=1)
            throw new Exception("fixed boss motion must not claim unknown pre-lock movement or the random next attack");
        string root=Directory.GetCurrentDirectory();
        while(!File.Exists(Path.Combine(root,"mod","MutantPilot","build.txt")))root=Directory.GetParent(root)?.FullName??throw new Exception("Project root missing");
        string fixture=Path.Combine(root,"tests","fixtures","eoc_boss_native_windows.json");
        if(!File.Exists(fixture))throw new Exception("native fixed-boss trajectory fixture is required");
        var rows=JsonDocument.Parse(File.ReadAllText(fixture));int count=0;float max=0;
        foreach(var row in rows.RootElement.EnumerateArray())
        {
            Vector2 V(string name){var v=row.GetProperty(name);return new(v[0].GetSingle(),v[1].GetSingle());}
            int ahead=row.GetProperty("ahead").GetInt32();
            int known=EocBossForecast.Forecast(path,V("position"),V("velocity"),V("locked"),row.GetProperty("timer").GetSingle());
            float error=Vector2.Distance(path[ahead],V("actual"));
            if(known<ahead||error>.15f)throw new Exception($"native fixed boss trajectory mismatch: {row.GetProperty("file")} tick {row.GetProperty("tick")} +{ahead}: {error}px");
            count++;max=Math.Max(max,error);
        }
        Console.WriteLine($"PASS locked EOC boss {count} native future windows, max error {max:F6}px");
        var native=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests","fixtures","eoc_eye_lock_118.json"))).RootElement;
        Vector2 Lock(string name){var v=native.GetProperty(name);return new(v[0].GetSingle(),v[1].GetSingle());}
        Vector2 eye=Lock("eyeLock"),npc=Lock("bossLock");
        if(Vector2.Distance(eye,npc)<160)throw new Exception("native FTW regression must retain distinct NPC and projectile lock points");
        foreach(string key in new[]{"a","b"})
        {
            var scythe=native.GetProperty(key);float angle=scythe.GetProperty("ai")[1].GetSingle();
            Vector2 delta=eye-new Vector2(scythe.GetProperty("x").GetSingle(),scythe.GetProperty("y").GetSingle());
            if(Math.Abs(delta.X*MathF.Sin(angle)-delta.Y*MathF.Cos(angle))>.02f)
                throw new Exception("actual eye lock must lie on both independently observed native radial scythe axes");
        }
        float eyeGap=float.PositiveInfinity,npcGap=float.PositiveInfinity;
        foreach(var scythe in native.GetProperty("scythe1").EnumerateArray())
        {
            float angle=scythe.GetProperty("ai")[1].GetSingle();
            Vector2 p=new(scythe.GetProperty("x").GetSingle(),scythe.GetProperty("y").GetSingle());
            float Gap(Vector2 c)=>Math.Abs((c.X-p.X)*MathF.Sin(angle)-(c.Y-p.Y)*MathF.Cos(angle));
            eyeGap=Math.Min(eyeGap,Gap(eye));npcGap=Math.Min(npcGap,Gap(npc));
        }
        if(eyeGap<80||npcGap>2)
            throw new Exception("native Scythe1 axes must preserve the actual eye centre opening while crossing the old NPC target");
        var routing=new EocRouting();routing.Observe(119,npc);
        if(routing.Center.HasValue)throw new Exception("routing must not use an unlocked eye localAI");
        routing.Observe(120,eye);
        if(routing.Center!=eye)throw new Exception("routing must use the native projectile lock, not the faster NPC clock");
        routing.Reset();
        if(routing.Center.HasValue)throw new Exception("a new eye phase must not reuse a previous eye lock");
        Console.WriteLine($"PASS native FTW eye/NPC locks differ {Vector2.Distance(eye,npc):F2}px; observed Scythe1 line gaps {eyeGap:F2}/{npcGap:F2}px; routing waits for projectile lock");
    }
}

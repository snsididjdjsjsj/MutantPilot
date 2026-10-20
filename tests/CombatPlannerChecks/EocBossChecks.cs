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
    }
}

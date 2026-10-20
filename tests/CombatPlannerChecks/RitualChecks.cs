using System.Numerics;
using System.Text.Json;
using MutantPilot.Combat;

static class RitualChecks
{
    public static void Run()
    {
        string root=Directory.GetCurrentDirectory();
        while(!File.Exists(Path.Combine(root,"mod","MutantPilot","build.txt")))root=Directory.GetParent(root)?.FullName??throw new Exception("Project root missing");
        using var data=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests","fixtures","ritual_native_pairs.json")));
        int count=0;float maximum=0;
        foreach(var item in data.RootElement.EnumerateArray())
        {
            Vector2 V(string key){var a=item.GetProperty(key);return new(a[0].GetSingle(),a[1].GetSingle());}
            Vector2 actual=RitualForecast.Step(V("arena"),V("boss"),item.GetProperty("state").GetInt32(),item.GetProperty("timer").GetSingle(),V("player"));
            maximum=Math.Max(maximum,Vector2.Distance(actual,V("expected")));count++;
        }
        if(count<80||maximum>.02f)throw new Exception($"Native ritual recurrence mismatch {count} pairs max {maximum}");
        var toss=new TossResponseForecast();
        toss.Reset(new(Vector2.Zero,Vector2.Zero,150,90,0,false,Arena:new(-200,300)),.2f,1,8);
        float a=0,b=0;
        for(int t=1;t<=48;t++)
        {
            a=toss.Advance(a,new(new(700+18*t,200),new(18,0)),t);
            b=toss.Advance(b,new(new(-700-18*t,-200),new(-18,0)),t);
        }
        if(toss.At(a).Arena==toss.At(b).Arena)throw new Exception("Candidate toss arena histories must remain distinct");
        var dash=new DirectDashResponseForecast();
        dash.Reset(new(Vector2.Zero,new(60,0),23,0,1,3,Arena:new(-200,300)));
        float d=dash.Advance(0,new(new(500,500),Vector2.Zero),1);var dw=dash.At(d);
        if(dw.Arena!=RitualForecast.Step(new(-200,300),dw.Position,dw.State,dw.Timer,new(500,500)))
            throw new Exception("Direct dash must use its updated NPC state and 40-tick arena divisor");
        var sword=new SwordResponseForecast();
        sword.Reset(new(Vector2.Zero,Vector2.Zero,90,0,true,Arena:new(-200,300)),MathF.PI/20,Array.Empty<SwordPart>());
        float s=sword.Advance(0,new(new(500,500),Vector2.Zero),1);var sw=sword.At(s);
        if(sw.Arena!=RitualForecast.Step(new(-200,300),sw.Position,46,sw.Timer,new(500,500)))
            throw new Exception("Sword launch arena must follow the candidate's updated boss position");
        Console.WriteLine($"PASS ritual recurrence matches {count} native pairs (max {maximum:F6}px), keeps branch histories and follows toss/dash/sword NPC responses");
    }
}

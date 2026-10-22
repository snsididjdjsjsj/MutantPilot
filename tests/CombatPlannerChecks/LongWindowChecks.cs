using System.Numerics;
using MutantPilot.Combat;

static class LongWindowChecks
{
    public static void Run()
    {
        RouteCode encoded=0;
        for(int i=0;i<64;i++)encoded=encoded.Append(i,(i*7)%12);
        for(int i=0;i<64;i++)if(encoded.At(i)!=(i*7)%12)throw new Exception("wide rescue paths lost a turning input past bit 128");
        var tree=new TossResponseForecast();
        tree.Reset(new(new(0,-500),Vector2.Zero,164,0,0,false),-MathF.PI/2,1,30,legendaryHyper:5);
        float reference=0;int phase=5;float timer=164;var births=new List<int>();
        for(int tick=1;tick<=CombatPlanner.ForecastTicks;tick++)
        {
            reference=tree.Advance(reference,new(Vector2.Zero,Vector2.Zero),tick);
            int updates=EncounterClock.Next(ref phase);
            for(int j=0;j<updates;j++)
                if(++timer>180){timer=150;births.Add(tick);}else if(timer==165)births.Add(tick);
        }
        var w=tree.At(reference);
        var actual=new[]{w.A,w.B,w.C,w.D,w.E,w.F,w.G,w.H}.Where(p=>p.HasValue).Select(p=>p!.Value.Born).OrderBy(t=>t).ToArray();
        if(births.Count<=4||!actual.SequenceEqual(births))throw new Exception("long window lost a future spear and its paired rays");
        var dash=new DirectDashResponseForecast();
        dash.Reset(new(new(500,0),new(0,60),23,29,1,8),5);
        reference=0;
        for(int t=1;t<=CombatPlanner.ForecastTicks;t++)reference=dash.Advance(reference,new(Vector2.Zero,Vector2.Zero),t);
        var d=dash.At(reference);
        if(d.D==null)throw new Exception("long dash response lost its fourth charge history");
        Console.WriteLine($"PASS long-window histories: {actual.Length} future throws, fourth direct charge, 64 packed turning inputs (development checks only)");
    }
}

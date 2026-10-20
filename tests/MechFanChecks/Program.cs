using System.Numerics;
using System.Text.Json;
using MutantPilot.Combat;

static List<JsonElement> Read(string path)
{
    using var stream = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
    using var reader = new StreamReader(stream);
    var result = new List<JsonElement>();
    while(reader.ReadLine() is string line)
    {
        try { using var doc=JsonDocument.Parse(line); result.Add(doc.RootElement.Clone()); }
        catch(JsonException) { }
    }
    return result;
}
if(args.Length!=3) throw new ArgumentException("combat spawn output.json");
var records=Read(args[0]); var births=Read(args[1]);
var frames=records.Where(r=>r.TryGetProperty("kind",out var k)&&k.GetString()=="frame").ToArray();
var rays=births.Where(r=>r.TryGetProperty("k",out var k)&&k.GetString()=="s"
    &&r.GetProperty("n").GetString()=="MutantDeathray3").ToArray();
long end=frames.Max(r=>r.GetProperty("gameTick").GetInt64());
// Older SpawnLog versions did not flush on world unload. Never interpret an
// unflushed tail beyond its final observed update as an absent native birth.
long spawnObservedThrough=births.Where(r=>r.TryGetProperty("t",out _)).Max(r=>r.GetProperty("t").GetInt64());
end=Math.Min(end,spawnObservedThrough);
int windows=0,queries=0,matched=0,mismatches=0;float maxOrigin=0,maxAngle=0;
var forecast=new MechFanForecast();
foreach(var frame in frames)
{
    if(frame.GetProperty("attack").GetSingle()!=27)continue;
    long now=frame.GetProperty("gameTick").GetInt64();
    if(now+CombatPlanner.ForecastTicks>end)continue;
    var ai=frame.GetProperty("bossAI");
    if(ai[2].GetSingle()==0)continue;
    forecast.Update(27,ai[1].GetSingle(),ai[2].GetSingle(),ai[3].GetSingle(),
        new(frame.GetProperty("bossX").GetSingle(),frame.GetProperty("bossY").GetSingle()),true);
    windows++;
    for(int lead=1;lead<=CombatPlanner.ForecastTicks;lead++)
    {
        queries++;long tick=now+lead;
        var observed=rays.Where(r=>r.GetProperty("t").GetInt64()==tick).ToList();
        var expected=forecast.Paths.Where(p=>p.Birth.Born==lead).ToArray();
        if(observed.Count!=expected.Length){Console.WriteLine($"COUNT frame={now} age={ai[3]} lead={lead} tick={tick} expected={expected.Length} actual={observed.Count}");mismatches++;continue;}
        foreach(var path in expected)
        {
            var b=path.Birth;Vector2 direction=new(MathF.Cos(b.Angle),MathF.Sin(b.Angle));
            int index=observed.FindIndex(r=>Vector2.Distance(new(r.GetProperty("x").GetSingle(),r.GetProperty("y").GetSingle()),b.Origin)<.02f
                &&Vector2.Distance(new(r.GetProperty("vx").GetSingle(),r.GetProperty("vy").GetSingle()),direction)<.002f);
            if(index<0){mismatches++;continue;}
            var actual=observed[index];observed.RemoveAt(index);matched++;
            maxOrigin=Math.Max(maxOrigin,Vector2.Distance(new(actual.GetProperty("x").GetSingle(),actual.GetProperty("y").GetSingle()),b.Origin));
            float error=MathF.Atan2(actual.GetProperty("vy").GetSingle(),actual.GetProperty("vx").GetSingle())-b.Angle;
            maxAngle=Math.Max(maxAngle,Math.Abs(MathF.Atan2(MathF.Sin(error),MathF.Cos(error))));
            // OnSpawn records defaults before SpawnRay applies its lifetime cap.
        }
    }
}
if(windows==0)throw new Exception("No complete native state-27 prediction windows");
int poseSamples=0;float maxPoseOrigin=0,maxPoseDirection=0,maxScale=0;
foreach(var hit in records.Where(r=>r.TryGetProperty("kind",out var k)&&k.GetString()=="hit"))
{
    long tick=hit.GetProperty("gameTick").GetInt64();
    if(!hit.TryGetProperty("nearby",out var nearby))continue;
    foreach(var actual in nearby.EnumerateArray())
    {
        if(actual.GetProperty("name").GetString()!="MutantDeathray3")continue;
        // Hurt snapshots capture later projectile slots before their first AI.
        // Those age-zero entries have not reached their own damage query yet.
        if(actual.GetProperty("localAI")[0].GetSingle()==0)continue;
        var born=rays.Where(r=>r.GetProperty("i").GetInt32()==actual.GetProperty("id").GetInt32()
            &&r.GetProperty("t").GetInt64()<=tick).OrderByDescending(r=>r.GetProperty("t").GetInt64()).FirstOrDefault();
        if(born.ValueKind==JsonValueKind.Undefined)continue;
        long spawnTick=born.GetProperty("t").GetInt64();
        var start=frames.FirstOrDefault(r=>r.GetProperty("attack").GetSingle()==27
            &&r.GetProperty("gameTick").GetInt64()<spawnTick&&tick-r.GetProperty("gameTick").GetInt64()<=48);
        if(start.ValueKind==JsonValueKind.Undefined)continue;
        var ai=start.GetProperty("bossAI");long now=start.GetProperty("gameTick").GetInt64();
        forecast.Update(27,ai[1].GetSingle(),ai[2].GetSingle(),ai[3].GetSingle(),
            new(start.GetProperty("bossX").GetSingle(),start.GetProperty("bossY").GetSingle()),true);
        var path=forecast.Paths.FirstOrDefault(p=>p.Birth.Born==spawnTick-now
            &&Vector2.Distance(p.Birth.Origin,new(born.GetProperty("x").GetSingle(),born.GetProperty("y").GetSingle()))<.02f
            &&Vector2.Distance(new(MathF.Cos(p.Birth.Angle),MathF.Sin(p.Birth.Angle)),
                new(born.GetProperty("vx").GetSingle(),born.GetProperty("vy").GetSingle()))<.002f);
        if(path==null)throw new Exception("Observed ray was not in native birth forecast");
        FanRayPose pose=path.Poses[(int)(tick-now)];poseSamples++;
        maxPoseOrigin=Math.Max(maxPoseOrigin,Vector2.Distance(pose.Origin,new(actual.GetProperty("x").GetSingle(),actual.GetProperty("y").GetSingle())));
        maxPoseDirection=Math.Max(maxPoseDirection,Vector2.Distance(pose.Direction,new(actual.GetProperty("vx").GetSingle(),actual.GetProperty("vy").GetSingle())));
        maxScale=Math.Max(maxScale,Math.Abs(pose.Scale-actual.GetProperty("scale").GetSingle()));
    }
}
if(maxPoseOrigin>.05f||maxPoseDirection>.001f||maxScale>.001f)mismatches++;
string json=JsonSerializer.Serialize(new {combat=Path.GetFileName(args[0]),windows,queries,matched,mismatches,
    maxOriginPx=maxOrigin,maxAngleDegrees=maxAngle*180/MathF.PI,spawnObservedThrough,
    poseSamples,maxPoseOrigin,maxPoseDirection,maxScale,
    note="Only windows ending within the observed spawn-log interval. Overlapping windows, not independent battles or a victory certificate."},new JsonSerializerOptions{WriteIndented=true});
File.WriteAllText(args[2],json);Console.WriteLine(json);
if(mismatches!=0)Environment.Exit(1);

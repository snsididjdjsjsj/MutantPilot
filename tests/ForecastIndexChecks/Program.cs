using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using MutantPilot.Combat;

var rng = new Random(712094);
int horizon=args.Length>0?int.Parse(args[0]):CombatPlanner.ForecastTicks;
var boxes = new List<TimedBox>(); var shards = new List<DecayingShard>();
var orbs = new List<TrailOrb>(); var explosions = new List<(Vector2 Center,int At)>();
Vector2 origin = new(67123.75f, 17469.125f);
Vector2 RandomPoint(float radius) => origin + new Vector2(rng.NextSingle()*2-1,rng.NextSingle()*2-1)*radius;
for(int i=0;i<96;i++)boxes.Add(new(RandomPoint(2000),new(rng.NextSingle()*180+1),rng.Next(1,35),rng.Next(35,horizon+1)));
for(int i=0;i<384;i++)shards.Add(new(RandomPoint(1800),new(rng.NextSingle()*30-15,rng.NextSingle()*30-15),rng.Next(-10,horizon)));
for(int i=0;i<220;i++)orbs.Add(new(RandomPoint(2200),new(rng.NextSingle()*20-10,rng.NextSingle()*20-10),rng.Next(-20,horizon),rng.NextSingle()*30));
for(int i=0;i<40;i++)explosions.Add((RandomPoint(2000),rng.Next(-10,horizon+1)));
var index = new ForecastHazardIndex(horizon);
Vector2 half = default; float reserve = 0;
bool parallelBuild=false;
void Prepare()
{
    index.Clear(half,reserve);
    if(parallelBuild) { index.Build(boxes,shards,orbs,explosions); return; }
    foreach(var b in boxes)index.Add(b);
    foreach(var s in shards)index.Add(s);
    foreach(var o in orbs)index.Add(o);
    foreach(var b in explosions)index.AddExplosion(b.Center,b.At);
}
float Full(Vector2 point,int tick,float cost)
{
    foreach(var b in boxes)if(b.Intersects(point,half,tick))cost+=1000000;
    foreach(var s in shards)if(s.Intersects(point,half,tick))cost+=1000000;
    foreach(var o in orbs)
    {
        if(tick<o.Born || tick>o.Explodes+21)continue;
        bool exploded=tick>=o.Explodes;
        if(exploded && !SmallBlastTiming.CouldDamage(tick-o.Explodes))continue;
        Vector2 at=o.CenterAt(exploded?o.Explodes:tick);
        float dx=Math.Max(0,Math.Abs(point.X-at.X)-half.X),dy=Math.Max(0,Math.Abs(point.Y-at.Y)-half.Y);
        float radius=(exploded?112:41)+o.Padding;
        if(dx*dx+dy*dy<radius*radius)cost+=1000000;
        cost+=ThreatClearance.Circle(point,at,half,radius,reserve);
    }
    foreach(var b in explosions)
    {
        if(!SmallBlastTiming.CouldDamage(tick-b.At))continue;
        float dx=Math.Max(0,Math.Abs(point.X-b.Center.X)-half.X),dy=Math.Max(0,Math.Abs(point.Y-b.Center.Y)-half.Y);
        if(dx*dx+dy*dy<104*104)cost+=1000000;
        cost+=ThreatClearance.Circle(point,b.Center,half,104,reserve);
    }
    return cost;
}
int checkedCount=0;
void Check(Vector2 point,int tick,float initial)
{
    float expected=Full(point,tick,initial),actual=initial; index.Accumulate(ref actual,point,tick);
    if(BitConverter.SingleToInt32Bits(expected)!=BitConverter.SingleToInt32Bits(actual))
        throw new Exception($"Risk mismatch tick={tick} point={point} expected={expected:R} actual={actual:R}");
    checkedCount++;
}
foreach(bool parallel in new[]{false,true})
foreach(float padding in new[]{0f,3f,15f})foreach(float clearance in new[]{0f,48f,128f})
{
    parallelBuild=parallel;
    half=new Vector2(10,21)+new Vector2(padding); reserve=clearance; Prepare();
    for(int n=0;n<14000;n++)Check(RandomPoint(2600),rng.Next(1,horizon+1),n%2==0 ? .12345f : 1000000.25f);
    // Exact cell edges, shape edges and soft-clearance boundaries at large world coordinates.
    for(int tick=1;tick<=horizon;tick++)foreach(var orb in orbs.Take(12))
    {
        bool exploded=tick>=orb.Explodes; Vector2 at=orb.CenterAt(exploded?orb.Explodes:tick);
        float r=(exploded?112:41)+orb.Padding;
        foreach(float delta in new[]{-.0078125f,0f,.0078125f})
        {
            Check(at+new Vector2(r+half.X+delta,0),tick,0);
            Check(at+new Vector2(r+half.X+reserve+delta,0),tick,0);
            Check(new Vector2(MathF.Floor(at.X/256)*256+delta,at.Y),tick,0);
        }
    }
}
Console.WriteLine($"PASS {checkedCount} bit-identical indexed/full risk queries over {horizon} future ticks, including birth/explosion timing and clearance/cell boundaries");
var queries=Enumerable.Range(0,20000).Select(_=>(Point:RandomPoint(2600),Tick:rng.Next(1,horizon+1))).ToArray();
half=new(13,24); reserve=48; Prepare();
// Warm both paths before timing; report build and query costs separately.
foreach(var q in queries.Take(1000)){float c=0; index.Accumulate(ref c,q.Point,q.Tick); _=Full(q.Point,q.Tick,0);}
double[] Fast(bool indexed)
{
    var times=new double[5];float checksum=0;
    for(int pass=0;pass<5;pass++)
    {
        var timer=Stopwatch.StartNew();
        foreach(var q in queries){float cost=0;if(indexed)index.Accumulate(ref cost,q.Point,q.Tick);else cost=Full(q.Point,q.Tick,0);checksum+=cost;}
        times[pass]=timer.Elapsed.TotalMilliseconds;
    }
    GC.KeepAlive(checksum); Array.Sort(times); return times;
}
double[] referenceTimes=Fast(false),indexTimes=Fast(true);
var buildTimes=new List<double>();for(int i=0;i<30;i++){var w=Stopwatch.StartNew();Prepare();buildTimes.Add(w.Elapsed.TotalMilliseconds);}buildTimes.Sort();
Console.WriteLine(JsonSerializer.Serialize(new {benchmark="synthetic dense future hazards, not game FPS",hazards=boxes.Count+shards.Count+orbs.Count+explosions.Count,
    queries=queries.Length, fullScanMedianMs=referenceTimes[2], indexedMedianMs=indexTimes[2], speedup=referenceTimes[2]/indexTimes[2], buildMedianMs=buildTimes[15]}));
foreach(int workerCount in new[]{1,2,4,8,16}.Where(n=>n<=Environment.ProcessorCount))
{
    index.WorkerCount=workerCount;parallelBuild=true;
    for(int i=0;i<5;i++)Prepare();
    var times=new List<double>();
    for(int i=0;i<40;i++){var w=Stopwatch.StartNew();Prepare();times.Add(w.Elapsed.TotalMilliseconds);}
    foreach(var q in queries.Take(500))Check(q.Point,q.Tick,.12345f);
    times.Sort();Console.WriteLine(JsonSerializer.Serialize(new {workers=workerCount,buildMedianMs=times[20],p95Ms=times[38],bitExact=true}));
}

using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using MutantPilot.Combat;

if(args.Length < 3) throw new ArgumentException("combat.jsonl spawn.jsonl output.json [width=256] [horizon=200]");
int width = args.Length > 3 ? int.Parse(args[3]) : 256;
int horizon = args.Length > 4 ? int.Parse(args[4]) : 200;
float cellSize = args.Length > 5 ? float.Parse(args[5],System.Globalization.CultureInfo.InvariantCulture) : 8;
int backTicks = args.Length > 6 ? int.Parse(args[6]) : 0;
float padding = args.Length > 7 ? float.Parse(args[7],System.Globalization.CultureInfo.InvariantCulture) : 3;
int timingSlack = args.Length > 8 ? int.Parse(args[8]) : 1;
float playerHalf = 10 + padding;
var frames = new List<JsonElement>(); var hits = new List<JsonElement>();
foreach(string line in File.ReadLines(args[0]))
{
    using var doc = JsonDocument.Parse(line); var row = doc.RootElement;
    if(row.GetProperty("kind").GetString()=="frame") frames.Add(row.Clone());
    else if(row.GetProperty("kind").GetString()=="hit") hits.Add(row.Clone());
}
var root = frames.First(f => f.GetProperty("attack").GetSingle()==9 && f.GetProperty("bossAI")[3].GetSingle()>0
    && f.GetProperty("bossLocalAI")[2].GetSingle()==0);
if(backTicks>0)root=frames.Last(f=>f.GetProperty("tick").GetInt32()<=root.GetProperty("tick").GetInt32()-backTicks);
float F(string key)=>root.GetProperty(key).GetSingle();
int I(string key)=>root.GetProperty(key).GetInt32();
bool B(string key)=>root.GetProperty(key).GetBoolean();
long now = root.GetProperty("gameTick").GetInt64();
var center = new Vector2(F("bossX"),F("bossY"));
var start = new MotionState(new(F("playerX"),F("playerY")),new(F("vx"),F("vy")),
    new(I("dashType"),I("dash"),I("dashDelay"),I("dashTime"),B("releaseLeft"),B("releaseRight")));
var motion = JsonSerializer.Deserialize<MotionModel>(root.GetProperty("motion").GetRawText(),new JsonSerializerOptions{IncludeFields=true})!;
motion.WorldMinimum=new(F("worldMinX"),F("worldMinY")); motion.WorldMaximum=new(F("worldMaxX"),F("worldMaxY"));
var spawns = new List<Shot>(); var ignored = new HashSet<string>();
foreach(string line in SharedRead(args[1]))
{
    using var doc = JsonDocument.Parse(line); var r=doc.RootElement;
    if(r.GetProperty("k").GetString()!="s")continue;
    long at=r.GetProperty("t").GetInt64(); if(at<now-300 || at>now+horizon)continue;
    string name=r.GetProperty("n").GetString()!;
    int kind=name=="MutantMark1"?1:name=="MutantEye"?2:name=="MutantDeathray1"?3:0;
    if(kind==0){if(name.StartsWith("Mutant") && !name.EndsWith("Friendly"))ignored.Add(name);continue;}
    spawns.Add(new(kind,(int)(at-now),new(r.GetProperty("x").GetSingle(),r.GetProperty("y").GetSingle()),
        new(r.GetProperty("vx").GetSingle(),r.GetProperty("vy").GetSingle())));
}
var hazards = new List<Shape>[horizon+1];
for(int t=0;t<=horizon;t++)
{
    hazards[t]=new();
    foreach(var shot in spawns)
    {
        int age=t-shot.Born;
        if(shot.Kind==3)
        {
            if(age<5-timingSlack||age>55+timingSlack)continue;
            hazards[t].Add(new(shot.Kind,shot.Position,Vector2.Normalize(shot.Velocity),24));
        }
        else
        {
            int life=shot.Kind==1?90:300;
            if(age<0||age>life)continue;
            for(int offset=-timingSlack;offset<=timingSlack;offset++)
            {
                int a=age+offset;if(a<0||a>life)continue;
                // NPC-spawned marks/eyes update in their birth tick. Rays
                // replacing an expired marker start AI on the next tick.
                var position=shot.Position+shot.Velocity*(a+1);
                if(Vector2.DistanceSquared(position,center)>1600*1600)continue;
                hazards[t].Add(new(shot.Kind,position,Vector2.Zero,shot.Kind==1?11.5f:6));
            }
        }
    }
}
float desiredRadius=100;
string? Touch(Vector2 point,int tick)
{
    Vector2 body=Vector2.Max(Vector2.Zero,Vector2.Abs(point-center)-new Vector2(playerHalf,21+padding));
    if(body.LengthSquared()<42*42)return "npc";
    if(Vector2.DistanceSquared(point,center)>1100*1100)return "arena";
    foreach(var h in hazards[tick])
    {
        Vector2 delta=Vector2.Abs(point-h.Position);
        if(h.Kind==3)
        {
            if(delta.X<24+playerHalf && delta.Y<24+playerHalf || FlatBeam.Intersects(h.Position,h.Velocity,3000,22,point,new(playerHalf)))return "ray";
        }
        else if(delta.X<playerHalf+h.Half && delta.Y<playerHalf+h.Half)return h.Kind==1?"mark":"eye";
    }
    return null;
}
float Risk(MotionState state,int tick)
{
    if(Touch(state.Position,tick)!=null)return 1000000;
    float radius=Vector2.Distance(state.Position,center);
    float target=tick<150?desiredRadius:Math.Min(740,desiredRadius+(tick-150)*10);
    float score=(radius-target)*(radius-target)*.005f;
    foreach(var h in hazards[tick])
    {
        if(h.Kind==3)continue;
        Vector2 d=Vector2.Abs(state.Position-h.Position)-new Vector2(playerHalf+h.Half);
        float gap=Math.Max(d.X,d.Y); float reserve=Math.Max(0,24-gap);
        score+=reserve*reserve*.01f;
    }
    return score;
}
var baseline = new List<object>();
foreach(var frame in frames)
{
    int tick=(int)(frame.GetProperty("gameTick").GetInt64()-now);
    if(tick<0||tick>horizon)continue;
    string? source=Touch(new(frame.GetProperty("playerX").GetSingle(),frame.GetProperty("playerY").GetSingle()),tick);
    if(source!=null)baseline.Add(new{tick,source});
}
var watch=Stopwatch.StartNew();
var path=PhaseReachability.Search(start,motion,horizon,Risk,width,cellSize);
var report=new{developmentOnly=true,victoryCertification=false,recordedStartTick=I("tick"),gameTick=now,width,horizon,padding,timingSlack,backTicks,
    path.Complete,path.Reached,path.Evaluations,seconds=watch.Elapsed.TotalSeconds,
    ignoredTypes=ignored.OrderBy(x=>x),baselineCollisions=baseline.Take(8),
    nativeHits=hits.Select(h=>new{tick=h.GetProperty("gameTick").GetInt64()-now,source=h.GetProperty("sourceName").GetString()}),
    inputs=path.Inputs.Select(m=>m.Id),positions=path.States.Select(s=>new{x=s.Position.X-center.X,y=s.Position.Y-center.Y}),
    limits="Uses recorded future births for a local counterfactual, conservative pure collision, no engine victory, no claim of global feasibility."};
File.WriteAllText(args[2],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{path.Complete,path.Reached,path.Evaluations,seconds=watch.Elapsed.TotalSeconds,
    ignored=ignored.OrderBy(x=>x),baseline=baseline.Take(3),output=args[2]}));
static IEnumerable<string> SharedRead(string path)
{
    using var stream = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
    using var reader = new StreamReader(stream);
    while(reader.ReadLine() is string line) yield return line;
}
readonly record struct Shot(int Kind,int Born,Vector2 Position,Vector2 Velocity);
readonly record struct Shape(int Kind,Vector2 Position,Vector2 Velocity,float Half);

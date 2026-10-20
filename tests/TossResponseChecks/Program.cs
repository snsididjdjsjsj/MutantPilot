using System.Numerics;
using System.Text.Json;
using MutantPilot.Combat;
void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); Console.WriteLine("PASS " + message); }
var predictiveInitial=new TossWorld(Vector2.Zero,Vector2.Zero,60,0,3,false);
var predictiveRight=TossResponseForecast.PredictiveStep(predictiveInitial,new(new(500,0),new(0,10)),1,7);
Check(predictiveRight.A is PendingSpear shot && shot.Born==1 && shot.Origin==Vector2.Zero
    &&Vector2.Distance(shot.Velocity,Vector2.Normalize(new Vector2(500,300))*30)<.0001f,
    "predictive P2 spear locks candidate position plus velocity times thirty before native NPC movement");
Check(TossResponseForecast.PredictiveStep(predictiveInitial with {Timer=59},new(new(500,0),Vector2.Zero),1,7).A==null,
    "predictive P2 throw waits until the native timer exceeds sixty");
var predictiveTree=new TossResponseForecast();predictiveTree.Reset(predictiveInitial,0,0,7,predictive:true);
float velocityA=predictiveTree.Advance(0,new(new(500,0),new(0,10)),1);
float velocityB=predictiveTree.Advance(0,new(new(500,0),new(0,-10)),1);
Check(velocityA!=velocityB&&predictiveTree.At(velocityA).A!.Value.Velocity.Y>0&&predictiveTree.At(velocityB).A!.Value.Velocity.Y<0,
    "identical player positions with different velocities retain distinct predictive spear branches");
var initial = new TossWorld(Vector2.Zero, Vector2.Zero, 164, 0, 0, false);
var right = TossResponseForecast.Step(initial, new(500, 0), 1, 0, 1, 10);
var down = TossResponseForecast.Step(initial, new(0, 500), 1, 0, 1, 10);
Check(right.A is PendingSpear { Velocity.X: 30, Velocity.Y: 0 } && down.A is PendingSpear { Velocity.X: 0, Velocity.Y: 30 }
    && initial.A == null, "each candidate trajectory produces its own aimed spear without changing the initial world");
var exit = TossResponseForecast.Step(initial with { Timer = 180, Emitted = 10 }, new(500, 0), 1, 0, 1, 10);
Check(exit.Closed && exit.A != null && TossResponseForecast.Step(exit, new(0, 500), 2, 0, 1, 10).B == null,
    "the final Masochist throw is retained without inventing attacks after the random phase exit");
var tree = new TossResponseForecast(); tree.Reset(initial, 0, 1, 10);
float a = tree.Advance(0, new(new(500, 0), Vector2.Zero), 1);
float b = tree.Advance(0, new(new(0, 500), Vector2.Zero), 1);
Check(a != b && a == tree.Advance(0, new(new(500, 0), Vector2.Zero), 1) && tree.At(0).A == null,
    "shared prefix references are reused while distinct candidate aims remain separate");
tree.Reset(initial with { Timer = 100, AimAngle = 0 }, 0, 1, 10, 7, 30);
a = tree.Advance(0, new(new(500, 0), Vector2.Zero), 1);
b = tree.Advance(0, new(new(0, 500), Vector2.Zero), 1);
float joinedA = tree.Advance(a, new(new(250, 250), Vector2.Zero), 2);
float joinedB = tree.Advance(b, new(new(250, 250), Vector2.Zero), 2);
Check(joinedA != joinedB && Math.Abs(FinalSparkForecast.Wrap(tree.At(joinedA).AimAngle - tree.At(joinedB).AimAngle)) > .1f,
    "identical current player positions retain distinct NPC aim histories after different approach paths");
Check(TossResponseForecast.TouchesSpear(new(Vector2.Zero, new(30, 0), 1, 4), new(480, 120), new(13, 24), 23)
    && !TossResponseForecast.TouchesSpear(new(Vector2.Zero, new(30, 0), 1, 4), new(480, 400), new(13, 24), 23),
    "future spear trails reserve their delayed side explosion without filling unrelated distant space");
if (args.Length < 2) return;
bool predictiveAudit=args.Contains("--predictive");int auditState=predictiveAudit?25:41;
var frames = new List<JsonElement>();
foreach (string line in File.ReadLines(args[0]))
{
    using var doc = JsonDocument.Parse(line); var r = doc.RootElement;
    if (r.GetProperty("kind").GetString() == "frame") frames.Add(r.Clone());
}
var births = new Dictionary<long, JsonElement>();
foreach (string line in File.ReadLines(args[1]))
{
    if (!line.Contains("\"k\":\"s\"") || !line.Contains("\"n\":\"MutantSpearThrown\"")) continue;
    using var doc = JsonDocument.Parse(line); var r = doc.RootElement;
    if (r.GetProperty("st").GetInt32() == auditState) births.Add(r.GetProperty("t").GetInt64(), r.Clone());
}
int pairs = 0, launches = 0, mismatches = 0; float maxPositionError = 0, maxVelocityError = 0, maxLaunchError = 0;
foreach (var (f, next) in frames.Zip(frames.Skip(1)))
{
    if (f.GetProperty("attack").GetSingle() != auditState || next.GetProperty("attack").GetSingle() != auditState
        || next.GetProperty("gameTick").GetInt64() != f.GetProperty("gameTick").GetInt64() + 1
        || f.GetProperty("hits").GetInt32() != next.GetProperty("hits").GetInt32()) continue;
    float F(string key) => f.GetProperty(key).GetSingle(); float N(string key) => next.GetProperty(key).GetSingle();
    var ai = f.GetProperty("bossAI"); var local = f.GetProperty("bossLocalAI");
    if (!predictiveAudit&&(ai[1].GetSingle() <= 0 || local[1].GetSingle() <= 0 || Math.Abs(local[2].GetSingle()) != 1)) continue;
    var world = new TossWorld(new(F("bossX"), F("bossY")), new(F("bossVx"), F("bossVy")),
        ai[1].GetSingle(), ai[3].GetSingle(), ai[2].GetSingle(), false);
    var predicted = predictiveAudit ? TossResponseForecast.PredictiveStep(world,
        new(new(N("playerX"),N("playerY")),new(N("vx"),N("vy"))),1,local[1].GetSingle())
        : TossResponseForecast.Step(world, new(N("playerX"), N("playerY")), 1,
            local[0].GetSingle(), local[2].GetSingle(), local[1].GetSingle());
    float pe = Vector2.Distance(predicted.Position, new(N("bossX"), N("bossY")));
    float ve = Vector2.Distance(predicted.Velocity, new(N("bossVx"), N("bossVy")));
    pairs++; maxPositionError = Math.Max(maxPositionError, pe); maxVelocityError = Math.Max(maxVelocityError, ve);
    if (pe > .1f || ve > .01f)
    {
        if (mismatches < 3) Console.WriteLine(JsonSerializer.Serialize(new { tick = f.GetProperty("tick").GetInt32(), pe, ve }));
        mismatches++;
    }
    if (predicted.A is PendingSpear spear)
    {
        launches++;
        if (!births.TryGetValue(next.GetProperty("gameTick").GetInt64(), out var actual)) { mismatches++; continue; }
        float error = Vector2.Distance(spear.Velocity, new(actual.GetProperty("vx").GetSingle(), actual.GetProperty("vy").GetSingle()));
        maxLaunchError = Math.Max(maxLaunchError, error);
        if (error > .002f) mismatches++;
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { developmentOnly = true, victoryCertification = false,
    pairs, launches, mismatches, maxPositionError, maxVelocityError, maxLaunchError }));
Check(pairs > 0 && mismatches == 0, "candidate-dependent NPC movement and observed launches match native consecutive snapshots");

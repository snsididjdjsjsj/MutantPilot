using System.Numerics;
using System.Text.Json;
using MutantPilot.Combat;
if (args.Length < 2) throw new ArgumentException("combat.jsonl spawn.jsonl [after=150]");
int after = args.Length > 2 ? int.Parse(args[2]) : 150;
JsonElement f = default;
foreach (string line in File.ReadLines(args[0]))
{
    using var doc = JsonDocument.Parse(line); var r = doc.RootElement;
    if (r.GetProperty("kind").GetString() == "frame" && r.GetProperty("attack").GetSingle() == -2
        && r.GetProperty("bossLocalAI")[0].GetSingle() >= after) { f = r.Clone(); break; }
}
if (f.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("No late void snapshot.");
float F(string key) => f.GetProperty(key).GetSingle(); int I(string key) => f.GetProperty(key).GetInt32();
var center = new Vector2(F("bossX"), F("bossY"));
long now = f.GetProperty("gameTick").GetInt64();
var marks = new List<(long Born, Vector2 Center, Vector2 Velocity)>();
foreach (string line in File.ReadLines(args[1]))
{
    if (!line.Contains("\"k\":\"s\"") || !line.Contains("\"n\":\"MutantMark1\"")) continue;
    using var doc = JsonDocument.Parse(line); var r = doc.RootElement;
    long born = r.GetProperty("t").GetInt64();
    if (r.GetProperty("st").GetInt32() == -2 && born <= now && born >= now - 150)
        marks.Add((born, new(r.GetProperty("x").GetSingle(), r.GetProperty("y").GetSingle()),
            new(r.GetProperty("vx").GetSingle(), r.GetProperty("vy").GetSingle())));
}
var pending = new MarkBurstForecast(); var ai = f.GetProperty("bossAI");
pending.Update(-2, ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle(), f.GetProperty("bossLocalAI")[0].GetSingle(), center, true);
foreach (var m in pending.Events) marks.Add((now + m.Born, center, m.Velocity));
var player = new MotionState(new(F("playerX"), F("playerY")), new(F("vx"), F("vy")),
    new(I("dashType"), I("dash"), I("dashDelay"), I("dashTime"), f.GetProperty("releaseLeft").GetBoolean(), f.GetProperty("releaseRight").GetBoolean()));
var motion = JsonSerializer.Deserialize<MotionModel>(f.GetProperty("motion").GetRawText(), new JsonSerializerOptions { IncludeFields = true })!;
float Risk(MotionState s, int ahead)
{
    if (pending.Touches(s.Position, new(13, 24), ahead)) return 1000000;
    foreach (var m in marks)
    {
        int age = (int)(now + ahead - m.Born + 1);
        if (age < 1 || age > 145) continue;
        Vector2 at = m.Center + m.Velocity * Math.Min(age, 90);
        if (age <= 90 && Math.Abs(s.Position.X - at.X) <= 24.5f && Math.Abs(s.Position.Y - at.Y) <= 35.5f) return 1000000;
        if (age < 94) continue;
        Vector2 direction = Vector2.Normalize(m.Velocity);
        if (Math.Abs(s.Position.X - at.X) <= 41 && Math.Abs(s.Position.Y - at.Y) <= 52
            || FlatBeam.Intersects(at, direction, 3000, 22, s.Position, new(17, 28))) return 1000000;
    }
    return 0;
}
VoidExitChoice choice = VoidExitRouting.Choose(player, motion, center, ai[3].GetSingle() / 2, Risk);
var planner = new CombatPlanner(); var goals = new Vector2[CombatPlanner.ForecastTicks + 1];
Move previous = Move.FromId(I("action")); int firstHit = 0;
// Replan against the same fixed initial forecast at each update. Pending birth
// helpers above are anchored to the initial time; shift their query, not entities.
for (int tick = 1; tick <= 48; tick++)
{
    int clock = tick - 1;
    for (int t = 0; t < goals.Length; t++) goals[t] = choice.GoalAt(clock + t);
    var d = planner.ChooseTracking(new(player, choice.Goal, center, 1600, false), motion,
        (s, t) => Risk(s, clock + t), goals, previous, 4, 16);
    player = d.Next; previous = d.Move;
    if (Risk(player, tick) >= 1000000) { firstHit = tick; break; }
}
Console.WriteLine(JsonSerializer.Serialize(new { developmentOnly = true, victoryCertification = false,
    recordedTick = I("tick"), proposedFirstHit = choice.FirstHit, proposedEndRadius = choice.EndRadius, choice.Queries,
    goalX = choice.Goal.X, goalY = choice.Goal.Y, choice.AngularSpeed, choice.RadialSpeed, choice.Delay,
    firstHit, endRadius = Vector2.Distance(player.Position, center),
    limits = "Isolated late P3 void; omits future sphere births and full native engine. Counterfactual route is not a real fight result." }));
if (choice.FirstHit != 0 || choice.EndRadius < 300 || firstHit != 0) throw new InvalidDataException("No safe executable exit in this isolated fixture.");

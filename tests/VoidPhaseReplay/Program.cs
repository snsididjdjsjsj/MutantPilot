using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using MutantPilot.Combat;

// Isolated counterfactual development tool. No Terraria process or save is edited.
if (args.Length < 2) throw new ArgumentException("combat.jsonl spawn.jsonl [pending=true] [reserve=0] [orbit=false] [inner=false] [state=-2]");
bool predictBirths = args.Length <= 2 || bool.Parse(args[2]);
float reserve = args.Length > 3 ? float.Parse(args[3], CultureInfo.InvariantCulture) : 0;
bool orbit = args.Length > 4 && bool.Parse(args[4]);
bool innerRoute = args.Length > 5 && bool.Parse(args[5]);
int targetState = args.Length > 6 ? int.Parse(args[6], CultureInfo.InvariantCulture) : -2;
if (targetState is not (-2 or 8)) throw new ArgumentException("Only void states -2 and 8 are supported.");
bool firstPhase = targetState == 8;
int limit = firstPhase ? 60 : 160, period = firstPhase ? 4 : 2;
float innerRadius = firstPhase ? 100 : 85;
JsonElement start = default;
foreach (string line in File.ReadLines(args[0]))
{
    using var doc = JsonDocument.Parse(line); var row = doc.RootElement;
    if (row.GetProperty("kind").GetString() == "frame" && row.GetProperty("attack").GetSingle() == targetState)
    { start = row.Clone(); break; }
}
float F(string k) => start.GetProperty(k).GetSingle();
int I(string k) => start.GetProperty(k).GetInt32();
var center = new Vector2(F("bossX"), F("bossY"));
var player = new MotionState(new(F("playerX"), F("playerY")), new(F("vx"), F("vy")),
    new(I("dashType"), I("dash"), I("dashDelay"), I("dashTime"),
        start.GetProperty("releaseLeft").GetBoolean(), start.GetProperty("releaseRight").GetBoolean()));
var motion = JsonSerializer.Deserialize<MotionModel>(start.GetProperty("motion").GetRawText(), new JsonSerializerOptions { IncludeFields = true })!;
float countdown = start.GetProperty("bossAI")[1].GetSingle(), angle = start.GetProperty("bossAI")[2].GetSingle();
float step = start.GetProperty("bossAI")[3].GetSingle(), emitted = start.GetProperty("bossLocalAI")[0].GetSingle();
long startTick = start.GetProperty("gameTick").GetInt64(), now = startTick;
var marks = new List<(long Born, Vector2 Velocity)>();
foreach (string line in File.ReadLines(args[1]))
{
    if (!line.Contains("\"n\":\"MutantMark1\"")) continue;
    using var doc = JsonDocument.Parse(line); var r = doc.RootElement;
    long birth = r.GetProperty("t").GetInt64();
    if (r.GetProperty("st").GetInt32() == targetState && birth <= now && birth >= now - 150)
        marks.Add((birth, new(r.GetProperty("vx").GetSingle(), r.GetProperty("vy").GetSingle())));
}
var forecast = new MarkBurstForecast(); var planner = new CombatPlanner();
var inner = new VoidInnerRouting(); var innerMarks = new List<LinearMark>();
var goals = new Vector2[CombatPlanner.ForecastTicks + 1];
Move previous = Move.FromId(I("action"));
int firstHit = 0; string? source = null; float maxRadius = 0, maxSettledRadius = 0;
var watch = Stopwatch.StartNew();
for (int tick = 1; tick <= 500; tick++)
{
    forecast.Update(emitted < limit ? targetState : -3, countdown, angle, step, emitted, center, true);
    innerMarks.Clear();
    foreach (var mark in marks)
    {
        int age = (int)(now - mark.Born + 1);
        if (age <= 90) innerMarks.Add(new(center + mark.Velocity * age, mark.Velocity, 0, 90 - age));
    }
    foreach (var birth in forecast.Events)
        innerMarks.Add(new(center, birth.Velocity, birth.Born - 1, birth.Born + 89, birth.Velocity.Length()));
    inner.Update(innerRoute && emitted < limit, now, player.Position, center, angle, step, new(13, 24), innerMarks, innerRadius, period);
    for (int t = 0; t < goals.Length; t++) goals[t] = inner.GoalAt(t);
    float Risk(Vector2 p, int ahead)
    {
        float cost = reserve > 0 ? FinalSparkForecast.RadialCost(p, center, reserve) : 0;
        if (firstPhase && Vector2.Distance(p, center) < 73) cost += 1000000;
        cost += inner.Cost(p, ahead);
        if (predictBirths && forecast.Touches(p, new(13, 24), ahead)) cost += 1000000;
        foreach (var mark in marks)
        {
            int age = (int)(now - mark.Born + 1 + ahead);
            if (age <= 90)
            {
                Vector2 at = center + mark.Velocity * age;
                if (Math.Abs(p.X - at.X) < 36 && Math.Abs(p.Y - at.Y) < 47) cost += 1000000;
            }
            else if (age >= 94 && age <= 145
                && FinalSparkForecast.TouchesBeam(center + mark.Velocity * 90 + Vector2.Normalize(mark.Velocity),
                    Vector2.Normalize(mark.Velocity), 1, 3000, p, new(19, 30))) cost += 1000000;
        }
        return cost;
    }
    Vector2 goal = center;
    if (orbit && emitted < limit) goal += new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * innerRadius;
    Vector2 pull = goal - player.Position;
    float reach = 24 * Math.Max(8, player.Velocity.Length());
    if (pull.LengthSquared() > reach * reach) goal = player.Position + Vector2.Normalize(pull) * reach;
    if (innerRoute) goal = goals[8];
    var frame = new PlanFrame(player, goal, center, 1600, false);
    var decision = innerRoute
        ? planner.ChooseTracking(frame, motion, (s, t) => Risk(s.Position, t), goals, previous, 4, 16)
        : planner.Choose(frame, motion, Risk, previous, 4, 16);
    player = decision.Next; previous = decision.Move; now++;
    if (emitted < limit && --countdown < 0)
    {
        float speed = !firstPhase && emitted <= 40 ? 4 : 2;
        marks.Add((now, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed));
        countdown = period - 1; angle += step;
        float old = emitted++;
        if (firstPhase ? old == 20 || emitted == 40 : old == 40 || emitted == 80 || emitted == 120) angle -= step / 3;
    }
    if (firstPhase)
    {
        Vector2 closest = Vector2.Max(Vector2.Zero, Vector2.Abs(player.Position - center) - new Vector2(10, 21));
        if (closest.Length() < 42) source = "boss contact";
    }
    foreach (var mark in marks)
    {
        int age = (int)(now - mark.Born + 1);
        if (age <= 90)
        {
            Vector2 at = center + mark.Velocity * age;
            if (Math.Abs(player.Position.X - at.X) < 33 && Math.Abs(player.Position.Y - at.Y) < 44) source = "marker";
        }
        else if (age >= 95 && age <= 145
            && FinalSparkForecast.TouchesBeam(center + mark.Velocity * 90 + Vector2.Normalize(mark.Velocity),
                Vector2.Normalize(mark.Velocity), 1, 3000, player.Position, new(10, 21))) source = "ray";
    }
    maxRadius = Math.Max(maxRadius, Vector2.Distance(player.Position, center));
    if (tick > 60) maxSettledRadius = Math.Max(maxSettledRadius, Vector2.Distance(player.Position, center));
    if (source != null) { firstHit = tick; break; }
    if (innerRoute && emitted >= limit) break;
}
Console.WriteLine(JsonSerializer.Serialize(new { developmentOnly = true, victoryCertification = false,
    recordedStartTick = I("tick"), targetState, predictBirths, reserve, orbit, innerRoute, firstHit, source, maxRadius, maxSettledRadius,
    seconds = watch.Elapsed.TotalSeconds, limits = "Isolated phase; omits concurrent old hazards, native engine collision, HP-dependent early exit and the following phase." }));

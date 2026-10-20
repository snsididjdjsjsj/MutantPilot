using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using MutantPilot.Combat;

// Development-only counterfactual: actual starting motion and visible NPC counters,
// native deterministic rings, coupled beam chase. This is NOT a game victory test.
if (args.Length < 1) throw new ArgumentException("combat.jsonl [radius=150] [reserve=270] [budgetMs=4] [pendingBirths=true]");
float radius = args.Length > 1 ? float.Parse(args[1], CultureInfo.InvariantCulture) : 150;
float reserve = args.Length > 2 ? float.Parse(args[2], CultureInfo.InvariantCulture) : 270;
double budget = args.Length > 3 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 4;
bool pendingBirths = args.Length <= 4 || bool.Parse(args[4]);
var frames = new List<JsonElement>();
foreach (string line in File.ReadLines(args[0]))
{
    using var document = JsonDocument.Parse(line);
    var row = document.RootElement;
    if (row.GetProperty("kind").GetString() == "frame" && row.GetProperty("attack").GetSingle() == -5)
        frames.Add(row.Clone());
}
var start = frames.First(f => f.GetProperty("bossAI")[2].GetSingle() < 0);
float F(string k) => start.GetProperty(k).GetSingle();
int I(string k) => start.GetProperty(k).GetInt32();
bool B(string k) => start.GetProperty(k).GetBoolean();
var center = new Vector2(F("bossX"), F("bossY"));
var state = new MotionState(new(F("playerX"), F("playerY")), new(F("vx"), F("vy")),
    new(I("dashType"), I("dash"), I("dashDelay"), I("dashTime"), B("releaseLeft"), B("releaseRight")));
var motion = JsonSerializer.Deserialize<MotionModel>(start.GetProperty("motion").GetRawText(),
    new JsonSerializerOptions { IncludeFields = true })!;
float ai1 = start.GetProperty("bossAI")[1].GetSingle(), ai2 = start.GetProperty("bossAI")[2].GetSingle();
float angle = start.GetProperty("bossAI")[3].GetSingle();
int age = (int)ai2 + 450, orbitSide = FinalSparkForecast.OrbitSide(MathF.Atan2(state.Position.Y - center.Y, state.Position.X - center.X), angle);
int phaseElapsed = 330 + age;
var rings = new List<Ring>();
// Both spin signs are always emitted. Changing sprite direction swaps the pair.
for (int born = 121; born <= phaseElapsed; born += 121) Emit(born);
void Emit(int born)
{
    foreach (float spin in new[] { -.5f, .5f })
        for (int i = 0; i < 10; i++) rings.Add(new Ring(born, center, MathF.PI / 2 + MathF.Tau / 10 * i, spin));
}
var planner = new CombatPlanner();
var pending = new SphereBurstForecast();
Move previous = Move.FromId(I("action"));
int firstHit = 0; string? source = null;
float maxRadius = Vector2.Distance(state.Position, center);
var watch = Stopwatch.StartNew();
int simulated = 0;
for (; simulated < 1700 && ai2 <= 1200; simulated++)
{
    int now = phaseElapsed;
    pending.Update(-5, ai1, ai2, 0, 1, 1, center, true, true);
    var paths = rings.Where(r => now >= r.Born && now - r.Born < 480 && r.At(now - r.Born + 1) is Vector2 p
        && Vector2.DistanceSquared(p, center) <= 1200 * 1200).ToArray();
    float Risk(MotionState candidate, int ahead, float rayAngle)
    {
        Vector2 point = candidate.Position;
        float cost = FinalSparkForecast.RadialCost(point, center, reserve);
        if (Vector2.DistanceSquared(point, center) > 1130 * 1130) cost += 1000000;
        if (pendingBirths && pending.Touches(point, new(13, 24), ahead)) cost += 1000000;
        foreach (var ring in paths)
        {
            int a = now - ring.Born + 1 + ahead;
            if (a > 480) continue;
            Vector2 at = ring.At(a);
            if (Vector2.DistanceSquared(at, center) > 1200 * 1200) continue;
            if (Math.Abs(point.X - at.X) < 33 && Math.Abs(point.Y - at.Y) < 44) cost += 1000000;
        }
        if (age > 0 && age + ahead < 780)
        {
            float scale = FinalSparkForecast.Scale(age + ahead, true);
            Vector2 direction = new(MathF.Cos(rayAngle), MathF.Sin(rayAngle));
            if (scale >= 5 && FinalSparkForecast.TouchesBeam(center + direction * (175 * FinalSparkForecast.Scale(age + ahead - 1, true) / 10 + 1),
                direction, scale, 3000, point, new(19, 30))) cost += 1000000;
        }
        return cost;
    }
    Vector2 goal = FinalSparkForecast.OrbitGoal(state.Position, center, orbitSide, radius);
    Vector2 pull = goal - state.Position;
    float reach = 24 * Math.Max(8, state.Velocity.Length());
    if (pull.LengthSquared() > reach * reach) goal = state.Position + Vector2.Normalize(pull) * reach;
    var frame = new PlanFrame(state, goal, center, 1600, false);
    Decision decision = age > 0 ? planner.ChooseCoupled(frame, motion, angle,
        (a, s, t) => FinalSparkForecast.Chase(a, center, s.Position, ai2 + t >= 420), Risk, previous, budget, 16)
        : planner.ChooseWithState(frame, motion, (s, t) => Risk(s, t, angle), previous, budget, 16);
    state = motion.Step(state, decision.Move); previous = decision.Move;
    phaseElapsed++;
    if (++ai1 > (ai2 >= 330 ? 100 : 120)) { ai1 = 0; Emit(phaseElapsed); }
    if (ai2 > 330 && ai2 < 420) angle = MathF.Atan2(center.Y - state.Position.Y, center.X - state.Position.X);
    ai2++;
    if (ai2 == 420) { angle += MathF.PI / 9; age = 0; }
    angle = FinalSparkForecast.Chase(angle, center, state.Position, ai2 >= 420);
    if (age > 0 || ai2 == 420) { age++; if (age >= 780) age = -1; }
    maxRadius = Math.Max(maxRadius, Vector2.Distance(state.Position, center));
    foreach (Ring ring in rings)
    {
        int a = phaseElapsed - ring.Born + 1;
        if (a <= 0 || a > 480) continue;
        Vector2 at = ring.At(a);
        if (Vector2.DistanceSquared(at, center) <= 1200 * 1200
            && Math.Abs(state.Position.X - at.X) < 30 && Math.Abs(state.Position.Y - at.Y) < 41) source = "sphere";
    }
    if (age > 0 && FinalSparkForecast.Scale(age, true) >= 5)
    {
        Vector2 d = new(MathF.Cos(angle), MathF.Sin(angle));
        if (FinalSparkForecast.TouchesBeam(center + d * (175 * FinalSparkForecast.Scale(age - 1, true) / 10 + 1), d,
            FinalSparkForecast.Scale(age, true), 3000, state.Position, new(10, 21))) source = "beam";
    }
    if (source != null) { firstHit = simulated + 1; break; }
}
Console.WriteLine(JsonSerializer.Serialize(new { developmentOnly = true, victoryCertification = false,
    recordedStartTick = I("tick"), radius, reserve, budget, pendingBirths, simulated, firstHit, source, maxRadius,
    elapsedSeconds = watch.Elapsed.TotalSeconds, limits = "No native engine collision, beam jitter, gear changes or full battle; not acceptance evidence." }));

sealed class Ring
{
    public int Born { get; }
    readonly Vector2[] positions = new Vector2[481];
    public Ring(int born, Vector2 center, float angle, float spin)
    {
        Born = born; positions[0] = center;
        Vector2 p = center, velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 6;
        for (int a = 1; a <= 480; a++) { SphereBurstForecast.Advance(ref p, ref velocity, 6, spin, a); positions[a] = p; }
    }
    public Vector2 At(int age) => positions[Math.Clamp(age, 0, 480)];
}

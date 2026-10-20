using System.Numerics;
using System.Text.Json;
using MutantPilot.Combat;
if (args.Length != 2) throw new ArgumentException("combat.jsonl spawn.jsonl");
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
    if (r.GetProperty("st").GetInt32() == 0) births.Add(r.GetProperty("t").GetInt64(), r.Clone());
}
int queries = 0, mismatches = 0, excludedTransitions = 0; float maxVelocityError = 0, maxOriginError = 0, maxOriginExcess = 0;
foreach (var f in frames)
{
    var ai = f.GetProperty("bossAI"); var local = f.GetProperty("bossLocalAI");
    if (local[3].GetSingle() <= 0) continue;
    float F(string name) => f.GetProperty(name).GetSingle();
    var pending = LockedTossForecast.Read((int)F("attack"), ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle(),
        local[0].GetSingle(), new(F("bossX"), F("bossY")), new(F("bossVx"), F("bossVy")));
    if (pending is not PendingSpear p) continue;
    long due = f.GetProperty("gameTick").GetInt64() + p.Born;
    if (!births.TryGetValue(due, out var actual))
    {
        bool transition = frames.Any(r => r.GetProperty("gameTick").GetInt64() >= due - 1
            && r.GetProperty("gameTick").GetInt64() <= due + 6 && r.GetProperty("attack").GetSingle() != 0);
        if (transition) { excludedTransitions++; continue; }
        queries++; mismatches++; continue;
    }
    queries++;
    Vector2 velocity = new(actual.GetProperty("vx").GetSingle(), actual.GetProperty("vy").GetSingle());
    Vector2 origin = new(actual.GetProperty("x").GetSingle(), actual.GetProperty("y").GetSingle());
    float ve = Vector2.Distance(velocity, p.Velocity), oe = Vector2.Distance(origin, p.Origin);
    Vector2 axisError = Vector2.Abs(origin - p.Origin);
    maxVelocityError = Math.Max(maxVelocityError, ve); maxOriginError = Math.Max(maxOriginError, oe);
    maxOriginExcess = Math.Max(maxOriginExcess, Math.Max(axisError.X, axisError.Y) - p.Padding);
    if (ve > .002f || Math.Max(axisError.X, axisError.Y) > p.Padding + .1f)
    {
        if (mismatches < 4) Console.WriteLine(JsonSerializer.Serialize(new {
            tick = f.GetProperty("tick").GetInt32(), due, p.Born, p.Padding, ai1 = ai[1].GetSingle(),
            predictedX = p.Origin.X, predictedY = p.Origin.Y, actualX = origin.X, actualY = origin.Y,
            bossX = F("bossX"), bossY = F("bossY"), bossVx = F("bossVx"), bossVy = F("bossVy") }));
        mismatches++;
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { developmentOnly = true, victoryCertification = false,
    queries, mismatches, excludedTransitions, maxVelocityError, maxOriginError, maxOriginExcess }));
if (queries == 0 || mismatches != 0) throw new InvalidDataException("P1 locked toss differs from native birth timing, velocity or reserved origin envelope.");

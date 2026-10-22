using System.Numerics;
using System.Text.Json;
using MutantPilot.Combat;

static void Check(bool value, string message)
{
    if (!value) throw new InvalidDataException(message);
    Console.WriteLine("PASS " + message);
}
var forecast = new SphereBurstForecast();
forecast.Update(1,1,0,0,0,1,Vector2.Zero,true,legendaryHyper:6);
Check(forecast.Events[0].Born==1,"FTW second AI can emit a sphere ring on the current game tick");
forecast.Update(-3, 10, 1, 60, 0, 1, Vector2.Zero, true);
Check(forecast.Events.Select(e => e.Born).Distinct().SequenceEqual(new[] { 2, 13, 24, 35, 46 }) && forecast.Count == 110,
    "Okuu first spawn waits until age exceeds 60, then emits paired eleven-sphere rings every eleven updates");
forecast.Update(-5, 100, 329, 0, 0, 1, Vector2.Zero, true);
Check(forecast.Events.Count == 2 && forecast.Events.All(e => e.Born == 2) && forecast.Count == 20,
    "FinalSpark predicts the shorter interval at 330 and respects the 780-update timer rewind");
forecast.Update(-3, 10, 0, 0, 0, 1, Vector2.Zero, true);
Check(forecast.Count == 0, "an unobserved random Okuu initialization is not invented");
forecast.Update(-5, 120, 0, 0, 0, 1, Vector2.Zero, true);
Check(forecast.Touches(Vector2.Zero, new(13, 24), 1)
    && !forecast.Touches(new(1000, 0), new(13, 24), 1),
    "a not-yet-spawned central ring warns before an immediate center hit without filling distant space");
forecast.Update(0, 0, 0, 0, 0, 1, Vector2.Zero, true);
Check(forecast.Count == 0 && forecast.Events.Count == 0, "switching attack removes stale pending rings");
// Native fixture: P3 ring birth at game tick 22254, then the same sphere at
// age 89 in the five-Hurt kill. Spawn logging rounds coordinates to 0.001px.
Vector2 recordedPosition = new(56890.648f, 5726.055f), recordedVelocity = new(5.372f, 9.599f);
for (int age = 1; age <= 89; age++)
    SphereBurstForecast.Advance(ref recordedPosition, ref recordedVelocity, 11, -.75f, age);
Check(Vector2.Distance(recordedPosition, new(56504.836f, 5799.1553f)) < .25f,
    "89 updates from a real recorded ring birth reproduce its later native position within logging precision");

if (args.Length != 2) return;
var frames = new List<JsonElement>();
long finalTick = 0;
foreach (string line in File.ReadLines(args[0]))
{
    using var row = JsonDocument.Parse(line);
    if (row.RootElement.GetProperty("kind").GetString() == "frame")
    {
        var frame = row.RootElement.Clone();
        frames.Add(frame); finalTick = Math.Max(finalTick, frame.GetProperty("gameTick").GetInt64());
    }
}
var spawns = new Dictionary<long, int>();
foreach (string line in File.ReadLines(args[1]))
{
    // The older generic spawn logger can write nonfinite cosmetic projectile
    // values. Parse every target sphere row strictly; other entity rows do
    // not participate in this focused scheduling check.
    if (!line.Contains("\"k\":\"s\"") || !line.Contains("\"n\":\"MutantSphereRing\"")) continue;
    using var row = JsonDocument.Parse(line);
    var data = row.RootElement;
    if (data.GetProperty("k").GetString() != "s" || data.GetProperty("n").GetString() != "MutantSphereRing"
        || data.GetProperty("st").GetInt32() is not (-3 or -5)) continue;
    long tick = data.GetProperty("t").GetInt64();
    spawns[tick] = spawns.GetValueOrDefault(tick) + 1;
}
int queries = 0, mismatches = 0, predictedBirths = 0;
foreach (var frame in frames)
{
    int state = (int)frame.GetProperty("attack").GetSingle();
    long now = frame.GetProperty("gameTick").GetInt64();
    if (state is not (-3 or -5) || frame.GetProperty("tick").GetInt32() % 6 != 0
        || now + CombatPlanner.ForecastTicks > finalTick) continue;
    var ai = frame.GetProperty("bossAI");
    // The fight may finish during this window; isolate scheduling while P3 persists.
    if (state == -5 && !frame.TryGetProperty("bossHyper",out _) && ai[2].GetSingle() > 1150) continue;
    forecast.Update(state, ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle(),
        frame.GetProperty("bossLocalAI")[1].GetSingle(), 1, Vector2.Zero, true,
        frame.GetProperty("life").GetInt32() <= 1, false,
        frame.TryGetProperty("bossHyper",out var hyper)?hyper.GetInt32():-1);
    var expected = forecast.Events.GroupBy(e => e.Born).ToDictionary(g => g.Key, g => g.Sum(e => e.Count));
    for (int ahead = 1; ahead <= CombatPlanner.ForecastTicks; ahead++)
    {
        queries++;
        int predicted = expected.GetValueOrDefault(ahead), actual = spawns.GetValueOrDefault(now + ahead);
        predictedBirths += predicted;
        if (predicted != actual)
        {
            if (mismatches < 5) Console.WriteLine($"MISMATCH tick={now} state={state} ahead={ahead} predicted={predicted} actual={actual} ai={ai}");
            mismatches++;
        }
    }
}
Console.WriteLine($"AUDIT queries={queries} predicted_projectile_births={predictedBirths} mismatches={mismatches}");
Check(queries > 0 && predictedBirths > 0 && mismatches == 0,
    "every predicted P3 ring count and exact future update matches the real spawn recording");

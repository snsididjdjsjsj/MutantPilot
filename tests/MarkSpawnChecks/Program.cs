using System.Numerics;
using System.Text.Json;
using MutantPilot.Combat;

void Check(bool ok, string message)
{
    if (!ok) throw new InvalidDataException(message);
    Console.WriteLine("PASS " + message);
}
var forecast = new MarkBurstForecast();
forecast.Update(8,1,0,MathF.PI/10,0,Vector2.Zero,true,legendaryHyper:6);
Check(forecast.Events[0].Born==1,"FTW second AI advances the marker birth without advancing its physics twice");
forecast.Update(-2, 1, 0, MathF.PI / 20, 39, Vector2.Zero, true);
Check(forecast.Events.Count == 24 && forecast.Events[0].Born == 2 && forecast.Events[1].Born == 4
    && Math.Abs(forecast.Events[1].Velocity.Length() - 4) < .001f
    && Math.Abs(forecast.Events[2].Velocity.Length() - 2) < .001f,
    "P3 markers emit every two updates and change speed after the forty-first birth");
forecast.Update(-2, 0, 0, MathF.PI / 20, 159, Vector2.Zero, true);
Check(forecast.Events.Count == 1 && forecast.Events[0].Born == 1,
    "last P3 marker is forecast once, with no invented births after the phase exit");
forecast.Update(-2, 0, 0, MathF.PI / 20, 0, Vector2.Zero, true);
Check(forecast.Touches(Vector2.Zero, new(13, 24), 1) && !forecast.Touches(new(1000, 0), new(13, 24), 1),
    "new damaging markers reserve their moving center before the real entity exists");
Check(forecast.Touches(new(4, 35), new(13, 24), 1) && !forecast.Touches(new(4, 40), new(13, 24), 1),
    "native 23px marker boxes keep the adjacent gap while reserving one movement update of birth uncertainty");
forecast.Update(-3, 0, 0, 0, 0, Vector2.Zero, true);
Check(forecast.Events.Count == 0, "phase changes clear pending marker births");
forecast.Update(8, 3, 0, MathF.PI / 10, 19, Vector2.Zero, true);
Check(forecast.Events.Count == 12 && forecast.Events[0].Born == 4
    && forecast.Events[1].Born == 8 && forecast.Events.All(x => Math.Abs(x.Velocity.Length() - 2) < .001f)
    && Vector2.Distance(forecast.Events[2].Velocity,
        new Vector2(MathF.Cos(MathF.PI / 6), MathF.Sin(MathF.PI / 6)) * 2) < .001f,
    "P1 markers keep speed two, the four-update cadence and the twenty-first-birth angle adjustment");
forecast.Update(8, 0, 0, MathF.PI / 10, 59, Vector2.Zero, true);
Check(forecast.Events.Count == 1 && forecast.Events[0].Born == 1,
    "P1 stops forecasting after its sixtieth marker");
forecast.Update(8, 5, 0, MathF.PI / 10, 39, Vector2.Zero, false);
Check(forecast.Events.Count == 2 && forecast.Events[0].Born == 6 && forecast.Events[1].Born == 12,
    "ordinary P1 preserves the native fortieth-birth angle branch before exiting on birth forty-one");
if (args.Length != 2) return;
var frames = new List<JsonElement>();
foreach (string line in File.ReadLines(args[0]))
{
    using var doc = JsonDocument.Parse(line);
    var row = doc.RootElement;
    if (row.GetProperty("kind").GetString() == "frame") frames.Add(row.Clone());
}
var births = new Dictionary<long, Vector2>();
int sizeChecks = 0;
foreach (string line in File.ReadLines(args[1]))
{
    if (!line.Contains("\"k\":\"s\"") || !line.Contains("\"n\":\"MutantMark1\"")) continue;
    using var doc = JsonDocument.Parse(line);
    var row = doc.RootElement;
    if (row.GetProperty("st").GetInt32() is not (-2 or 8)) continue;
    if (row.GetProperty("w").GetInt32() != 23 || row.GetProperty("h").GetInt32() != 23
        || row.GetProperty("sc").GetSingle() != .5f) throw new InvalidDataException("Native marker size differs from its forecast.");
    sizeChecks++;
    births.Add(row.GetProperty("t").GetInt64(), new(row.GetProperty("vx").GetSingle(), row.GetProperty("vy").GetSingle()));
}
foreach (int state in new[] { 8, -2 })
{
int queries = 0, mismatches = 0, excludedExitWindows = 0; float maxVelocityError = 0;
for (int i = 0; i < frames.Count; i++)
{
    var f = frames[i];
    if (f.GetProperty("attack").GetSingle() != state || f.GetProperty("tick").GetInt32() % 6 != 0) continue;
    long now = f.GetProperty("gameTick").GetInt64(); var ai = f.GetProperty("bossAI");
    // P1 may end early on HP crossing. Only compare horizons whose state is
    // observed to remain P1; HP-dependent transitions are not predicted here.
    if (state == 8)
    {
        int end = i;
        while (end + 1 < frames.Count && frames[end + 1].GetProperty("attack").GetSingle() == state) end++;
        if (now + CombatPlanner.ForecastTicks > frames[end].GetProperty("gameTick").GetInt64())
        { excludedExitWindows++; continue; }
    }
    forecast.Update(state, ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle(),
        f.GetProperty("bossLocalAI")[0].GetSingle(), Vector2.Zero, true);
    var expected = forecast.Events.ToDictionary(x => x.Born, x => x.Velocity);
    for (int t = 1; t <= CombatPlanner.ForecastTicks; t++)
    {
        queries++;
        bool predicted = expected.TryGetValue(t, out var predictedVelocity), actual = births.TryGetValue(now + t, out var velocity);
        if (predicted && actual) maxVelocityError = Math.Max(maxVelocityError, Vector2.Distance(predictedVelocity, velocity));
        if (predicted != actual || predicted && Vector2.Distance(predictedVelocity, velocity) > .002f) mismatches++;
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { state, queries, mismatches, maxVelocityError, excludedExitWindows }));
Check(queries > 0 && mismatches == 0, "visible marker birth times and velocities match the real spawn log in state " + state);
}
Check(sizeChecks > 0, "all target marker births have native 23px bounds and scale one half; samples=" + sizeChecks);
var eyeForecast = new EyeBurstForecast();
var eyeBirths = new Dictionary<long, List<Vector2>>();
foreach (string line in File.ReadLines(args[1]))
{
    if (!line.Contains("\"k\":\"s\"") || !line.Contains("\"n\":\"MutantEye\"")) continue;
    using var doc = JsonDocument.Parse(line); var row = doc.RootElement;
    if (row.GetProperty("st").GetInt32() != 9) continue;
    long tick = row.GetProperty("t").GetInt64();
    if (!eyeBirths.TryGetValue(tick, out var list)) eyeBirths[tick] = list = new();
    list.Add(new(row.GetProperty("vx").GetSingle(), row.GetProperty("vy").GetSingle()));
}
int eyeQueries = 0, eyeMismatches = 0; float maxEyeError = 0;
for (int i = 0; i < frames.Count; i++)
{
    var f = frames[i];
    if (f.GetProperty("attack").GetSingle() != 9 || f.GetProperty("bossLocalAI")[2].GetSingle() != 0
        || f.GetProperty("bossAI")[3].GetSingle() <= 0 || f.GetProperty("tick").GetInt32() % 6 != 0) continue;
    long now = f.GetProperty("gameTick").GetInt64(); int end = i;
    while (end + 1 < frames.Count && frames[end + 1].GetProperty("attack").GetSingle() == 9
        && frames[end + 1].GetProperty("bossLocalAI")[2].GetSingle() == 0) end++;
    if (now + CombatPlanner.ForecastTicks > frames[end].GetProperty("gameTick").GetInt64()) continue;
    var ai = f.GetProperty("bossAI"); var local = f.GetProperty("bossLocalAI");
    eyeForecast.Update(9, ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle(),
        0, local[0].GetSingle(), Vector2.Zero, true);
    for (int t = 1; t <= CombatPlanner.ForecastTicks; t++)
    {
        eyeQueries++;
        var predicted = eyeForecast.Events.Where(e => e.Born == t).Select(e => e.Velocity).ToList();
        var actual = eyeBirths.GetValueOrDefault(now + t) ?? new();
        if (predicted.Count != actual.Count) { eyeMismatches++; continue; }
        foreach (var velocity in predicted)
        {
            float error = actual.Min(v => Vector2.Distance(v, velocity));
            maxEyeError = Math.Max(maxEyeError, error);
            if (error > .002f) eyeMismatches++;
        }
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { eyeQueries, eyeMismatches, maxEyeError }));
Check(eyeQueries > 0 && eyeMismatches == 0, "P1 boundary-eye births match native timing, count and velocity away from HP-dependent exits");

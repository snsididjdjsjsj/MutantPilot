using System.Numerics;
using System.Text.Json;
using MutantPilot.Combat;
void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); Console.WriteLine("PASS " + message); }
Check(Enumerable.Range(0, 61).Where(a => ShortRayTiming.Damages(1, ShortRayTiming.Scale(1, a))).SequenceEqual(Enumerable.Range(5, 51)),
    "short void ray damages at native ages five through fifty-five");
Check(Enumerable.Range(0, 181).Where(a => ShortRayTiming.Damages(2, ShortRayTiming.Scale(2, a))).SequenceEqual(Enumerable.Range(24, 133)),
    "spear deathray damages at native ages twenty-four through one hundred fifty-six");
Vector2 probe = new(500, 40);
Check(!FlatBeam.Intersects(Vector2.Zero, Vector2.UnitX, 3000, 22, probe, new(13, 24))
    && new TimedBeam(Vector2.Zero, Vector2.UnitX, 1, 55).Intersects(probe, new(13, 24), 1),
    "native full width preserves a side gap that the older padded timeline beam closes");
int samples = 0, damageSamples = 0, mismatches = 0, uninitialized = 0; float maxScaleError = 0;
foreach (string file in args)
foreach (string line in File.ReadLines(file))
{
    using var doc = JsonDocument.Parse(line); var row = doc.RootElement;
    if (row.GetProperty("kind").GetString() != "hit" || !row.TryGetProperty("nearby", out var nearby)) continue;
    foreach (var p in nearby.EnumerateArray())
    {
        string? name = p.GetProperty("name").GetString();
        int kind = name == "MutantDeathray1" ? 1 : name == "MutantDeathray2" ? 2 : 0;
        if (kind == 0) continue;
        float age = p.GetProperty("localAI")[0].GetSingle(), scale = p.GetProperty("scale").GetSingle();
        // Freshly allocated rays still expose the constructor scale before
        // their first AI; every future collision query runs after that AI.
        if (age == 0) { uninitialized++; continue; }
        float error = Math.Abs(ShortRayTiming.Scale(kind, age) - scale);
        samples++; maxScaleError = Math.Max(maxScaleError, error);
        if (error > .0001f) mismatches++;
        if (p.TryGetProperty("canDamage", out var can) && can.ValueKind == JsonValueKind.True)
        {
            damageSamples++;
            if (!ShortRayTiming.Damages(kind, ShortRayTiming.Scale(kind, age))) mismatches++;
        }
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { developmentOnly = true, victoryCertification = false,
    samples, damageSamples, mismatches, maxScaleError, uninitialized }));
if (args.Length > 0) Check(samples > 0 && mismatches == 0, "real ray snapshots agree on native scale and every observed damaging age");

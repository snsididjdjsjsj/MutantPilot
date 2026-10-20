using System.Numerics;
using MutantPilot.Combat;
void Check(bool condition, string text) { if (!condition) throw new Exception(text); Console.WriteLine("PASS " + text); }
var forecast = new WormResponseForecast();
WormNode Head(int slot) => new(slot, -1, Vector2.Zero, Vector2.Zero, MathF.PI/2, true, 61, 1, 42);
WormNode Body(int slot, int parent) => new(slot, parent, new(-36, 0), Vector2.Zero, MathF.PI/2, false, 0, 0, 30);
forecast.Reset(new[] { Head(1), Body(2, 1) });
float right = forecast.Advance(0, new(new(100, 0), Vector2.Zero), 1);
float above = forecast.Advance(0, new(new(0, -100), Vector2.Zero), 1);
Check(Vector2.Distance(forecast.Position(right, 1), new(.4f, 0)) < .0001f
    && Vector2.Distance(forecast.Position(above, 1), new(0, -.4f)) < .0001f,
    "each candidate has the native close-range pursuit response to its own new position");
Check(Vector2.Distance(forecast.Position(right, 2), new(-35.6f, 0)) < .0001f,
    "a later body follows its already-updated parent at the native thirty-six-pixel spacing");
Check(forecast.Position(0, 1) == Vector2.Zero && forecast.Position(0, 2) == new Vector2(-36, 0),
    "branching does not modify the root or any sibling");
Check(forecast.Advance(0,new(new(100,0),Vector2.Zero),1)==right,
    "identical parent, target and player velocity reuse a prefix");
float differentVelocity=forecast.Advance(0,new(new(100,0),new(99,12)),1);
Check(differentVelocity!=right&&forecast.Position(differentVelocity,1)==forecast.Position(right,1),
    "equal worm positions preserve distinct player velocities for a simultaneously predictive spear response");
float a = forecast.Advance(right, new(new(100, 100), Vector2.Zero), 2);
float b = forecast.Advance(above, new(new(100, 100), Vector2.Zero), 2);
Check(a != b && forecast.Position(a, 1) != forecast.Position(b, 1),
    "different histories ending at one player position keep different pursuing heads");
float far = forecast.Advance(0, new(new(-1000, 0), Vector2.Zero), 1);
Check(Vector2.Distance(forecast.Position(far, 1), new(-1.6f, 0)) < .0001f,
    "native far-range pursuit doubles both desired speed and interpolation");
forecast.Reset(new[] { Body(1, 2), Head(2) });
float laterParent = forecast.Advance(0, new(new(100, 0), Vector2.Zero), 1);
Check(forecast.Position(laterParent, 1) == new Vector2(-36, 0)
    && Vector2.Distance(forecast.Position(laterParent, 2), new(.4f, 0)) < .0001f,
    "an earlier body reads a later parent's previous position, matching native projectile slot order");
Console.WriteLine("Native end-of-frame prediction audits and a complete zero-Hurt battle are still required.");

using System;
using System.Numerics;

namespace MutantPilot.Combat
{
    public static class EocDashForecast
    {
        // After native age 120 the target is fixed in localAI[0..1]. The eye
        // keeps speed 120, redirects at 121, and resets its phase at 132.
        // A straight-line extrapolation loses each subsequent redirection.
        public static void Forecast(Vector2[] path, Vector2 position, Vector2 velocity,
            float age, Vector2 target)
        {
            path[0] = position;
            for (int tick = 1; tick < path.Length; tick++)
            {
                age++;
                if (age == 121)
                {
                    Vector2 delta = target-position;
                    Vector2 direction = delta.LengthSquared() > .000001f ? Vector2.Normalize(delta) : Vector2.Zero;
                    float angle = MathF.PI/8;
                    velocity = new(direction.X*MathF.Cos(angle)-direction.Y*MathF.Sin(angle),
                        direction.X*MathF.Sin(angle)+direction.Y*MathF.Cos(angle));
                    velocity *= 120;
                }
                else if (age >= 131.66667f) age = 120;
                position += velocity;
                path[tick] = position;
            }
        }
    }
}

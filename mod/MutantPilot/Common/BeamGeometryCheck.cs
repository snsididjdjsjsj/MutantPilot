using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using MutantPilot.Combat;
using NVector = System.Numerics.Vector2;

namespace MutantPilot.Common
{
    // Read-only comparison with the installed engine, outside combat. The local
    // System.Random is independent of Main.rand and cannot alter game randomness.
    public static class BeamGeometryCheck
    {
        public sealed record Report(int Samples, int FalsePositives, int FalseNegatives, object[] Examples);
        public static Report Run()
        {
            var random = new Random(264561);
            int positives = 0, negatives = 0;
            var examples = new List<object>();
            const int samples = 100000;
            for (int i = 0; i < samples; i++)
            {
                float angle = (float)(random.NextDouble() * Math.PI * 2);
                Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
                Vector2 normal = new(-direction.Y, direction.X);
                Vector2 origin = new(random.Next(1000, 40000), random.Next(1000, 14000));
                float length = random.Next(100, 3001), width = random.Next(11, 221);
                float along = (float)(random.NextDouble() * (length + 160) - 80);
                float across = (float)(random.NextDouble() * (width + 160) - (width + 160) / 2);
                Vector2 point = origin + direction * along + normal * across;
                int halfX = random.Next(9, 41), halfY = random.Next(9, 41);
                var box = new Rectangle((int)point.X - halfX, (int)point.Y - halfY, halfX * 2, halfY * 2);
                float at = 0;
                bool native = Collision.CheckAABBvLineCollision(box.TopLeft(), box.Size(), origin,
                    origin + direction * length, width, ref at);
                bool model = FlatBeam.Intersects(new(origin.X, origin.Y), new(direction.X, direction.Y),
                    length, width, new(box.Center.X, box.Center.Y), new(halfX, halfY));
                if (native == model) continue;
                if (model) positives++; else negatives++;
                if (examples.Count < 4) examples.Add(new { native, model, angle, length, width,
                    originX = origin.X, originY = origin.Y, box.X, box.Y, box.Width, box.Height });
            }
            return new(samples, positives, negatives, examples.ToArray());
        }
    }
}

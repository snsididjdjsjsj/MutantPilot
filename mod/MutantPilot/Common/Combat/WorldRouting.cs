using System;
using System.Numerics;

namespace MutantPilot.Combat
{
    // Long-term room to manoeuvre, separate from the moving boss/ritual arena.
    // All outputs are goals or soft costs; this never changes live motion.
    public sealed class WorldRouting
    {
        bool initialized;
        Vector2 minimum, maximum;
        float reserveX, reserveY;
        bool useHeightBand;
        public float AnchorY { get; private set; }
        public float UpperY { get; private set; }
        public float LowerY { get; private set; }
        public int Direction { get; private set; } = -1;

        public void Observe(Vector2 position, Vector2 worldMinimum, Vector2 airMaximum,
            float boundaryReserve, float halfBand)
        {
            minimum = worldMinimum;
            maximum = Vector2.Max(worldMinimum + new Vector2(2), airMaximum);
            float requested = Math.Clamp(boundaryReserve, 0, 6000);
            reserveX = Math.Min(requested, (maximum.X - minimum.X) * .2f);
            reserveY = Math.Min(requested, (maximum.Y - minimum.Y) * .2f);
            if (!initialized)
            {
                AnchorY = Math.Clamp(position.Y, minimum.Y + reserveY, maximum.Y - reserveY);
                initialized = true;
            }
            float band = Math.Clamp(halfBand, 200, 4000);
            useHeightBand = halfBand > 0;
            UpperY = Math.Max(minimum.Y + reserveY, AnchorY - band);
            LowerY = Math.Min(maximum.Y - reserveY, AnchorY + band);
            if (position.Y <= UpperY) Direction = 1;
            else if (position.Y >= LowerY) Direction = -1;
        }

        public float BoundaryCost(Vector2 position)
        {
            // A gradual preference begins several forecast horizons before the
            // physical edge. It stays far below a real collision's 1e6 cost.
            static float Intrusion(float distance, float reserve)
            {
                if (reserve <= 0) return 0;
                float fraction = Math.Clamp(1 - distance / reserve, 0, 1);
                return 2000 * fraction * fraction;
            }
            return Intrusion(position.X - minimum.X, reserveX)
                + Intrusion(maximum.X - position.X, reserveX)
                + Intrusion(position.Y - minimum.Y, reserveY)
                + Intrusion(maximum.Y - position.Y, reserveY);
        }

        public float HeightCost(Vector2 position)
        {
            if (!useHeightBand) return 0;
            float outside = Math.Max(0, Math.Max(UpperY - position.Y, position.Y - LowerY));
            return Math.Min(50000, outside * outside / 10000);
        }
    }
}

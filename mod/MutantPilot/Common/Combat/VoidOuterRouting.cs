using System;
using System.Numerics;

namespace MutantPilot.Combat
{
    // P1 uses an 18-degree step and two 6-degree phase adjustments.
    // Their union is a fixed 6-degree grid. The midpoint of a grid cell
    // widens with distance, unlike the congested inner marker region.
    public sealed class VoidOuterRouting
    {
        bool active;
        float bearing;
        Vector2 center;
        float voidRadius=850,residualRadius=1100;
        public bool Active => active;
        public static float GapBearing(float markerAngle, float markerStep, float playerBearing, bool masochist)
        {
            float grid = Math.Abs(markerStep) / (masochist ? 3 : 2);
            if (grid < .001f) return playerBearing;
            float offset = FinalSparkForecast.Wrap(playerBearing-markerAngle);
            return markerAngle + (MathF.Round(offset/grid-.5f)+.5f)*grid;
        }
        public void Update(bool enabled, bool inVoid, Vector2 player, Vector2 boss, float markerAngle,
            float markerStep, bool masochist,float inVoidRadius=850,float afterVoidRadius=1100)
        {
            voidRadius=inVoidRadius;residualRadius=afterVoidRadius;
            if (!enabled) { active = false; return; }
            if (!active || inVoid && Vector2.DistanceSquared(boss, center)>16)
            {
                center = boss;
                bearing = GapBearing(markerAngle, markerStep, MathF.Atan2(player.Y-boss.Y,player.X-boss.X),masochist);
            }
            active = true;
        }
        public Vector2 Goal(bool inVoid) => center + new Vector2(MathF.Cos(bearing),MathF.Sin(bearing)) * (inVoid ? voidRadius : residualRadius);
        public float Cost(Vector2 point)
        {
            if (!active) return 0;
            Vector2 unit = new(MathF.Cos(bearing),MathF.Sin(bearing));
            Vector2 delta = point-center;
            float across = delta.X*-unit.Y+delta.Y*unit.X;
            return Math.Min(50000, across*across/200);
        }
        public object Snapshot() => new { active,bearing,x=center.X,y=center.Y };
    }
}

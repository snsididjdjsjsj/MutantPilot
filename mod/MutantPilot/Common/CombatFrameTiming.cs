using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework.Graphics;
using Terraria.ModLoader;

namespace MutantPilot.Common
{
    // Observation only. Draw cadence includes skipped renders and any stalls; entity update time
    // covers PreUpdateEntities through PostUpdateEverything, not GPU execution or presentation.
    public sealed class CombatFrameTiming : ModSystem
    {
        static readonly List<double> updates = new(40000), controllers = new(40000),
            telemetry = new(40000), drawIntervals = new(40000);
        static bool active;
        static long started, updateStart, lastDraw, windowStart;
        static int draws, windowDraws, windowUpdates, gc0, gc1, gc2;
        static double windowUpdateSum, windowUpdateMax, windowControllerMax;
        public static object LastWindow { get; private set; }
        static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        public static void Begin()
        {
            updates.Clear(); controllers.Clear(); telemetry.Clear(); drawIntervals.Clear();
            started = windowStart = Stopwatch.GetTimestamp(); lastDraw = updateStart = 0;
            draws = windowDraws = windowUpdates = 0;
            windowUpdateSum = windowUpdateMax = windowControllerMax = 0;
            gc0 = GC.CollectionCount(0); gc1 = GC.CollectionCount(1); gc2 = GC.CollectionCount(2);
            LastWindow = null; active = true;
        }
        public override void PreUpdateEntities()
        {
            if (active) updateStart = Stopwatch.GetTimestamp();
        }
        public override void PostUpdateEverything()
        {
            if (!active || updateStart == 0) return;
            long now = Stopwatch.GetTimestamp();
            double ms = Ms(now - updateStart); updates.Add(ms);
            windowUpdates++; windowUpdateSum += ms; windowUpdateMax = Math.Max(windowUpdateMax, ms);
            double seconds = Ms(now - windowStart) / 1000;
            if (seconds < 5) return;
            LastWindow = new { seconds, updatesPerSecond = windowUpdates / seconds,
                drawsPerSecond = windowDraws / seconds,
                entityUpdateMeanMs = windowUpdateSum / Math.Max(1, windowUpdates),
                entityUpdateMaxMs = windowUpdateMax, controllerMaxMs = windowControllerMax };
            windowStart = now; windowDraws = windowUpdates = 0;
            windowUpdateSum = windowUpdateMax = windowControllerMax = 0;
        }
        public override void PostDrawInterface(SpriteBatch spriteBatch)
        {
            if (!active) return;
            long now = Stopwatch.GetTimestamp();
            if (lastDraw != 0) drawIntervals.Add(Ms(now - lastDraw));
            lastDraw = now; draws++; windowDraws++;
        }
        public static void Controller(double milliseconds)
        {
            if (!active) return;
            controllers.Add(milliseconds);
            windowControllerMax = Math.Max(windowControllerMax, milliseconds);
        }
        public static void Telemetry(double milliseconds) { if (active) telemetry.Add(milliseconds); }
        static object Stats(List<double> values)
        {
            if (values.Count == 0) return null;
            var ordered = values.ToArray(); Array.Sort(ordered);
            double sum = 0; int over16 = 0, over33 = 0;
            foreach (double value in ordered) { sum += value; if (value > 1000.0/60) over16++; if (value > 1000.0/30) over33++; }
            double P(double p) => ordered[(int)((ordered.Length - 1)*p)];
            return new { count = ordered.Length, meanMs = sum/ordered.Length, p50Ms = P(.5),
                p95Ms = P(.95), p99Ms = P(.99), maxMs = ordered[^1], over16_67 = over16, over33_33 = over33 };
        }
        public static object Finish()
        {
            if (!active) return null;
            active = false;
            double seconds = Ms(Stopwatch.GetTimestamp() - started)/1000;
            return new { wallSeconds = seconds, entityUpdates = updates.Count, draws,
                updatesPerSecond = updates.Count/seconds, drawsPerSecond = draws/seconds,
                entityUpdate = Stats(updates), controller = Stats(controllers), telemetry = Stats(telemetry),
                drawInterval = Stats(drawIntervals),
                garbageCollections = new { gen0 = GC.CollectionCount(0)-gc0, gen1 = GC.CollectionCount(1)-gc1, gen2 = GC.CollectionCount(2)-gc2 } };
        }
        // PilotHarness closes the encounter during world unload. Do not discard the measurements
        // before its OnWorldUnload runs; Finish owns that transition.
        public override void Unload() { active = false; }
    }
}

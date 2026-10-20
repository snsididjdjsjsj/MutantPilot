using System;
using System.Globalization;
using System.IO;
using System.Text;
using Terraria;
using Terraria.ModLoader;

namespace MutantPilot.Common
{
    /// <summary>
    /// Read-only observation log for the attack-timeline work.
    ///
    /// Writes spawn_*.jsonl next to combat_*.jsonl, with two record kinds:
    ///   {"k":"state","t":gameTick,"st":ai0,"from":prev,"ai":[...],"lai":[...],"bx":..,"by":..}
    ///   {"k":"s","t":gameTick,"st":ai0,"n":"TypeName","i":identity,"g":generation,
    ///    "x":..,"y":..,"vx":..,"vy":..,"w":..,"h":..,"sc":..,"tl":..,"d":damage,"own":..}
    ///
    /// Purpose: let offline analysis compare the frames a state timeline predicts a projectile
    /// to appear with the frame it actually appeared. It never writes game state and is never
    /// read by the controller, so it cannot influence a battle.
    /// </summary>
    public sealed class SpawnLog : ModSystem
    {
        static StreamWriter writer;
        static readonly StringBuilder line = new(320);
        static int lastState = int.MinValue;
        static int absentTicks;
        static int recordCount;
        static bool failed;

        static Mod owner;
        static void Warn(string message) => owner?.Logger.Warn(message);

        public override void Load() => owner = Mod;

        static string Num(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

        static void Begin()
        {
            try
            {
                string directory = Path.Combine(Main.SavePath, "MutantPilot");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"spawn_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jsonl");
                writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = false };
                recordCount = 0;
                lastState = int.MinValue;
                line.Clear();
                line.Append("{\"k\":\"start\",\"v\":1,\"world\":\"").Append(Main.worldName)
                    .Append("\",\"masochist\":").Append(PilotWorld.IsMasochist ? "true" : "false")
                    .Append(",\"utc\":\"").Append(DateTime.UtcNow.ToString("O")).Append("\"}");
                writer.WriteLine(line.ToString());
            }
            catch (Exception ex)
            {
                failed = true;
                Warn("Spawn log unavailable: " + ex.Message);
            }
        }

        static void Close(string reason)
        {
            if (writer == null) return;
            try
            {
                line.Clear();
                line.Append("{\"k\":\"end\",\"reason\":\"").Append(reason)
                    .Append("\",\"records\":").Append(recordCount)
                    .Append(",\"dropped\":").Append(dropped).Append('}');
                writer.WriteLine(line.ToString());
                writer.Flush();
                writer.Dispose();
            }
            catch (IOException) { }
            writer = null;
            lastState = int.MinValue;
        }

        static bool? logEnabled;

        /// <summary>
        /// Opt-in. The log costs about a millisecond of the frame budget, so it stays off
        /// unless the marker file exists; delete the marker to restore the decision-only path.
        /// </summary>
        static bool LogEnabled()
        {
            if (logEnabled.HasValue) return logEnabled.Value;
            bool on = false;
            try { on = File.Exists(Path.Combine(Main.SavePath, "MutantPilot", "spawnlog.on")); }
            catch (IOException) { }
            logEnabled = on;
            return on;
        }

        public override void PostUpdateEverything()
        {
            if (failed || Main.dedServ || Main.gameMenu || !LogEnabled()) return;
            NPC boss = PilotSense.FindBoss();
            if (boss == null)
            {
                if (writer != null && ++absentTicks > 600) Close("boss_gone");
                return;
            }
            absentTicks = 0;
            if (writer == null) Begin();
            if (writer == null) return;

            int state = (int)boss.ai[0];
            if (state != lastState)
            {
                Write(state, boss, lastState);
                lastState = state;
            }
            if (recordCount > 0 && Main.GameUpdateCount % 60 == 0)
            {
                try { writer.Flush(); } catch (IOException) { Close("io"); }
            }
        }

        static void Write(int state, NPC boss, int previous)
        {
            try
            {
                line.Clear();
                line.Append("{\"k\":\"state\",\"t\":").Append(Main.GameUpdateCount)
                    .Append(",\"st\":").Append(state)
                    .Append(",\"from\":").Append(previous == int.MinValue ? -999 : previous)
                    .Append(",\"ai\":[").Append(Num(boss.ai[0])).Append(',').Append(Num(boss.ai[1])).Append(',')
                    .Append(Num(boss.ai[2])).Append(',').Append(Num(boss.ai[3])).Append(']')
                    .Append(",\"lai\":[").Append(Num(boss.localAI[0])).Append(',').Append(Num(boss.localAI[1])).Append(',')
                    .Append(Num(boss.localAI[2])).Append(',').Append(Num(boss.localAI[3])).Append(']')
                    .Append(",\"bx\":").Append(Num(boss.Center.X))
                    .Append(",\"by\":").Append(Num(boss.Center.Y))
                    .Append(",\"life\":").Append(boss.life)
                    .Append(",\"lifeMax\":").Append(boss.lifeMax)
                    .Append('}');
                writer.WriteLine(line.ToString());
                recordCount++;
            }
            catch (Exception ex)
            {
                failed = true;
                Warn("Spawn log failed: " + ex.Message);
                Close("error");
            }
        }

        public static void Spawn(Projectile p, int state)
        {
            if (failed || writer == null) return;
            try
            {
                string name = p.ModProjectile?.Name ?? ("vanilla:" + p.type);
                line.Clear();
                line.Append("{\"k\":\"s\",\"t\":").Append(Main.GameUpdateCount)
                    .Append(",\"st\":").Append(state)
                    .Append(",\"n\":\"").Append(name).Append('"')
                    .Append(",\"i\":").Append(p.identity)
                    .Append(",\"g\":").Append(ProjectileBirth.Of(p))
                    .Append(",\"x\":").Append(Num(p.Center.X))
                    .Append(",\"y\":").Append(Num(p.Center.Y))
                    .Append(",\"vx\":").Append(Num(p.velocity.X))
                    .Append(",\"vy\":").Append(Num(p.velocity.Y))
                    .Append(",\"w\":").Append(p.width)
                    .Append(",\"h\":").Append(p.height)
                    .Append(",\"sc\":").Append(Num(p.scale))
                    .Append(",\"tl\":").Append(p.timeLeft)
                    .Append(",\"d\":").Append(p.damage)
                    .Append(",\"own\":").Append(p.owner)
                    .Append('}');
                writer.WriteLine(line.ToString());
                recordCount++;
            }
            catch (Exception ex)
            {
                failed = true;
                Warn("Spawn log failed: " + ex.Message);
                Close("error");
            }
        }

        static int dropped;
        public static void Drop() => dropped++;

        public override void OnWorldUnload() => Close("world_unload");

        public override void Unload()
        {
            Close("unload");
            failed = false;
            logEnabled = null;
        }
    }

    /// <summary>Forwards every projectile birth to <see cref="SpawnLog"/> while a boss is present.</summary>
    public sealed class SpawnLogHook : GlobalProjectile
    {
        public override void OnSpawn(Projectile projectile, Terraria.DataStructures.IEntitySource source)
        {
            if (Main.dedServ || Main.gameMenu) return;
            // Player weapon projectiles (Zenith alone spawns hundreds) are noise here; the
            // timeline only predicts hostile and mod-owned spawns.
            if (!projectile.hostile && projectile.ModProjectile == null) return;
            NPC boss = PilotSense.FindBoss();
            if (boss == null) return;
            SpawnLog.Spawn(projectile, (int)boss.ai[0]);
        }
    }
}

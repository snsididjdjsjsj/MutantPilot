using System;
using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using MutantPilot.Combat;

namespace MutantPilot.Common
{
    public sealed class BeamBroadPhaseCheck : ModSystem
    {
        public static bool Enabled { get; private set; }
        public static int Samples { get; private set; }
        public static int Rejected { get; private set; }
        public static int FalseNegatives { get; private set; }
        public override void PostSetupContent()
        {
            if (Main.dedServ) return;
            var random = new Random(589134);
            Samples = Rejected = FalseNegatives = 0; Enabled = false;
            for (int i = 0; i < 100000; i++)
            {
                float angle = (float)(random.NextDouble() * Math.PI * 2);
                Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
                Vector2 origin = new(random.Next(1000, 130000), random.Next(1000, 35000));
                float length = random.Next(0, 3001), scale = (float)random.NextDouble() * 10;
                float along = (float)random.NextDouble() * (length + 600) - 300;
                float across = (float)random.NextDouble() * 1000 - 500;
                Vector2 point = origin + direction * along + new Vector2(-direction.Y, direction.X) * across;
                int hx = random.Next(9, 101), hy = random.Next(9, 101);
                var query = new Rectangle((int)point.X - hx, (int)point.Y - hy, hx * 2, hy * 2);
                var originBox = new Rectangle((int)(origin.X - 24), (int)(origin.Y - 24), 48, 48);
                float at = 0;
                bool native = originBox.Intersects(query) || Collision.CheckAABBvLineCollision(query.TopLeft(), query.Size(),
                    origin, origin + direction * length, 22 * scale, ref at);
                var broad = new BeamBroadPhase(new(origin.X, origin.Y), new(direction.X, direction.Y), length, scale, new(24));
                bool maybe = broad.MayTouch(new(query.Center.X, query.Center.Y), new(hx, hy));
                Samples++;
                if (!maybe) Rejected++;
                if (native && !maybe) FalseNegatives++;
            }
            Enabled = FalseNegatives == 0 && Samples == 100000;
            try
            {
                string directory = Path.Combine(Main.SavePath, "MutantPilot"); Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "beam_broad_phase_check.json"), JsonSerializer.Serialize(new {
                    utc = DateTime.UtcNow, version = Mod.Version.ToString(), check = Snapshot() }));
            }
            catch (IOException ex) { Mod.Logger.Warn("Beam broad phase report: " + ex.Message); }
            if (!Enabled) Mod.Logger.Warn("Beam broad phase disabled: native self-check failed.");
        }
        public static object Snapshot() => new { Enabled, Samples, Rejected, FalseNegatives };
        public override void Unload() { Enabled = false; Samples = Rejected = FalseNegatives = 0; }
    }
}

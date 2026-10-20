using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;

namespace MutantPilot.Common
{
    // Read-only sensing. Movement constraints are enforced by choosing inputs.
    public static class PilotArena
    {
        public static bool TryCircle(out Vector2 center, out float radius, out Projectile ritual)
        {
            center = Vector2.Zero; radius = 0; ritual = null;
            foreach (Projectile p in Main.ActiveProjectiles)
            {
                string name = p.ModProjectile?.Name ?? "";
                // Ritual2/3/4/5 are decorative or separate effects. In particular,
                // Ritual5 follows the boss while the damaging Ritual can stay still.
                if (name != "MutantRitual") continue;
                float candidate = 1200;
                for (Type type = p.ModProjectile.GetType(); type != null; type = type.BaseType)
                {
                    FieldInfo field = type.GetField("threshold", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field?.GetValue(p.ModProjectile) is float threshold && threshold > 100)
                    { candidate = threshold; break; }
                }
                if (radius == 0 || candidate < radius) { radius = candidate; center = p.Center; ritual = p; }
            }
            if (radius > 0) return true;
            NPC boss = PilotSense.FindBoss();
            if (boss == null || boss.localAI[3] != 1) return false;
            center = boss.Center; radius = 2000;
            var flags = BindingFlags.Instance | BindingFlags.Public;
            if (boss.ModNPC?.GetType().GetField("AuraCenter", flags)?.GetValue(boss.ModNPC) is Vector2 aura) center = aura;
            if (boss.ModNPC?.GetType().GetField("AuraScale", flags)?.GetValue(boss.ModNPC) is float scale) radius *= scale;
            return true;
        }
    }
}

using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using MutantPilot.Combat;

namespace MutantPilot.Common
{
    public static class PilotControls
    {
        public const int Hover = 10;
        public static void Apply(Player player, Move move)
        {
            player.controlLeft = move.Horizontal < 0;
            player.controlRight = move.Horizontal > 0;
            player.controlUp = move.Vertical == 2;
            player.controlDown = move.Vertical == 3;
            player.controlJump = move.Vertical != 0;
            player.controlHook = false;
            player.controlUseTile = false;
            player.controlMount = false;
            player.controlThrow = false;
            if (!player.controlJump) player.releaseJump = true;
        }
        public static void SelectWeapon(Player player)
        {
            for(int i=0;i<player.inventory.Length;i++)
                if(player.inventory[i].ModItem?.FullName==PilotTuning.Current.PrimaryWeapon){player.selectedItem=i;return;}
        }
        static float WeaponScore(Item item, Player player)
        {
            if (item.IsAir || item.damage <= 0 || item.pick > 0 || item.axe > 0 || item.hammer > 0
                || item.accessory || item.ammo > 0 || item.useTime <= 0) return 0;
            if (item.useAmmo > 0 && !player.HasAmmo(item)) return 0;
            if (item.mana > 0 && player.statMana < item.mana * player.manaCost) return 0;
            float score = item.damage * 60f / item.useTime;
            if (item.shoot > ProjectileID.None) score *= 1.5f;
            if (item.type == ItemID.Zenith) score *= 12;
            return score;
        }
        public static float AttackRange(Item item, Player player = null, NPC target = null)
        {
            if (item.type == ItemID.Zenith)
            {
                // Installed Player.LimitPointToPlayerReachableArea limits aim
                // to +/-960 x +/-600. Secondary Zenith blades acquire a
                // chaseable NPC within 400px of that normal cursor point.
                // Keep 150px of that acquisition radius as a moving-target reserve.
                Vector2 direction = player != null && target != null
                    ? (target.Center - player.Center).SafeNormalize(Vector2.UnitX) : Vector2.UnitX;
                float x = Math.Min(960, Math.Max(100, Main.screenWidth / Math.Max(.1f, Main.GameViewMatrix.Zoom.X) / 2 - 24));
                float y = Math.Min(600, Math.Max(100, Main.screenHeight / Math.Max(.1f, Main.GameViewMatrix.Zoom.Y) / 2 - 24));
                float reach = Math.Min(x / Math.Max(.0001f, Math.Abs(direction.X)), y / Math.Max(.0001f, Math.Abs(direction.Y)));
                return reach + 250;
            }
            if (item.shoot > ProjectileID.None) return 750;
            return Math.Max(90, Math.Max(item.width, item.height) * item.scale * 2);
        }
        public static bool AimAndAttack(Player player, NPC boss)
        {
            Item weapon = player.HeldItem;
            if(weapon.ModItem?.FullName!=PilotTuning.Current.PrimaryWeapon){player.controlUseItem=false;return false;}
            if (WeaponScore(weapon, player) <= 0) { player.controlUseItem = false; return false; }
            Vector2 target = boss.Center;
            if (weapon.shootSpeed > 1 && weapon.type != ItemID.Zenith && weapon.ModItem?.Name!="TheDestroyer")
            {
                float travel = Math.Min(15, Vector2.Distance(player.Center, target) / weapon.shootSpeed);
                target += boss.velocity * travel;
            }
            AimAt(target);
            if (player.itemTime == 0 && player.itemAnimation == 0) player.releaseUseItem = true;
            player.controlUseItem = true;
            return true;
        }
        public static void AimAt(Vector2 target)
        {
            Vector2 center = new(Main.screenWidth / 2f, Main.screenHeight / 2f);
            Vector2 mouse = (target - Main.screenPosition - center) * Main.GameViewMatrix.Zoom + center;
            Main.mouseX = (int)Math.Clamp(mouse.X, 0, Main.screenWidth - 1);
            Main.mouseY = (int)Math.Clamp(mouse.Y, 0, Main.screenHeight - 1);
        }
    }
}

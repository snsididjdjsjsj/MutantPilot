using System;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace MutantPilot.Common
{
    // Observation only. Never enables an effect, changes a hitbox or invokes
    // an ability. The native precision rectangle is read after equipment updates.
    public static class FargoCapabilities
    {
        static bool initialized, failed;
        static MethodInfo hasPrecision, precisionBox;
        static Type fargoPlayerType;
        static FieldInfo styxSet, styxMeter, styxReady, betsyDashing, specialDashCd, goldShell;
        static Player captured;
        static int width, height;
        static int timeFrozenType=-1;
        static int betsyTicks;
        static bool previousDash;
        // BetsyDash: Projectile.timeLeft = 15 and BetsyDashing is set on every tick the dash
        // lives, while FargoSoulsPlayer.ImmuneToDamage => BetsyDashing makes
        // CanBeHitByPlayerProjectile return false. So the whole dash skips the Hurt event.
        public const int DashLength = 15;
        public static bool PrecisionActive { get; private set; }
        public static bool StyxSet { get; private set; }
        public static int StyxMeter { get; private set; }
        public static int StyxReady { get; private set; }
        public static int TimeFrozenTicks { get; private set; }
        public static bool DashActive { get; private set; }
        public static int DashImmuneRemaining { get; private set; }
        // SpecialDashKey refuses to act while SpecialDashCD > 0 and then sets a 5 second (300 tick)
        // cooldown, so a caller that ignores this field mostly presses into nothing.
        public static int SpecialDashCooldown { get; private set; }
        // Golden Stasis: GoldKeyEffect grants GoldenStasisBuff for 300 ticks and the buff keeps
        // GoldShell true, which routes through ImmuneToDamage -> no Hurt event at all. It also zeroes
        // every movement key, so it is 5 seconds of guaranteed safety at the price of standing still.
        public static bool GoldShellActive { get; private set; }
        // Dash starts actually observed, i.e. proof the press turned into a dash, as opposed to
        // counting how many times we called the effect.
        public static int DashStarts { get; private set; }
        public static ulong CapturedAt { get; private set; }
        // Two hooks read this snapshot every frame: one inside the player update (PostUpdateRunSpeeds)
        // and one at the end of the frame. Only the end-of-frame call sees the dash projectile's
        // BetsyDashing flag, because FargoSoulsPlayer.ResetEffects clears it at the start of the next
        // tick. The dash bookkeeping therefore runs only for frameEnd callers - counting both made the
        // 15-tick dash look like fifteen separate starts and pinned remaining immunity at 14.
        public static void Capture(Player player, bool frameEnd = false)
        {
            captured = player; width = player.width; height = player.height;
            PrecisionActive = false; StyxSet = false; StyxMeter = StyxReady = TimeFrozenTicks = 0; CapturedAt = Main.GameUpdateCount;
            if (frameEnd) { DashActive = false; DashImmuneRemaining = 0; }
            if (failed) return;
            try
            {
                if (!initialized)
                {
                    if (!ModLoader.TryGetMod("FargowiltasSouls", out Mod fargo)) return;
                    if(fargo.TryFind("TimeFrozenBuff",out ModBuff frozenBuff))timeFrozenType=frozenBuff.Type;
                    fargoPlayerType = fargo.Code.GetType("FargowiltasSouls.Core.ModPlayers.FargoSoulsPlayer", true);
                    Type effect = fargo.Code.GetType("FargowiltasSouls.Content.Items.Accessories.Masomode.PrecisionSealHurtbox", true);
                    Type loader = fargo.Code.GetType("FargowiltasSouls.Core.AccessoryEffectSystem.AccessoryEffectLoader", true);
                    hasPrecision = loader.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Single(m => m.Name == "HasEffect" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
                        .MakeGenericMethod(effect);
                    precisionBox = fargoPlayerType.GetMethod("GetPrecisionHurtbox");
                    styxSet = fargoPlayerType.GetField("StyxSet"); styxMeter = fargoPlayerType.GetField("StyxMeter");
                    styxReady = fargoPlayerType.GetField("StyxAttackReadyTimer");
                    betsyDashing = fargoPlayerType.GetField("BetsyDashing");
                    specialDashCd = fargoPlayerType.GetField("SpecialDashCD");
                    goldShell = fargoPlayerType.GetField("GoldShell"); initialized = true;
                }
                object mp = null;
                int frozenIndex=timeFrozenType<0?-1:player.FindBuffIndex(timeFrozenType);
                if(frozenIndex>=0)TimeFrozenTicks=player.buffTime[frozenIndex];
                foreach (ModPlayer candidate in player.ModPlayers)
                    if (candidate.GetType() == fargoPlayerType) { mp = candidate; break; }
                if (mp == null) return;
                StyxSet = styxSet?.GetValue(mp) is true;
                StyxMeter = styxMeter?.GetValue(mp) is int meter ? meter : 0;
                StyxReady = styxReady?.GetValue(mp) is int ready ? ready : 0;
                // The dash lasts a fixed 15 ticks, so counting consecutive active ticks is enough
                // to know how many planned ticks are still immune.
                if (frameEnd)
                {
                    bool dashing = betsyDashing?.GetValue(mp) is true;
                    betsyTicks = dashing ? Math.Min(DashLength, betsyTicks + 1) : 0;
                    if (dashing && !previousDash) DashStarts++;
                    previousDash = dashing;
                    DashActive = dashing;
                    DashImmuneRemaining = dashing ? Math.Max(0, DashLength - betsyTicks) : 0;
                }
                SpecialDashCooldown = specialDashCd?.GetValue(mp) is int cooldown ? cooldown : 0;
                GoldShellActive = goldShell?.GetValue(mp) is true;
                if (hasPrecision.Invoke(null, new object[] { player }) is not true) return;
                if (precisionBox?.Invoke(mp, null) is Rectangle rectangle && rectangle.Width > 0 && rectangle.Height > 0
                    && rectangle.Width <= player.width && rectangle.Height <= player.height
                    && rectangle.Center == player.Hitbox.Center)
                { width = rectangle.Width; height = rectangle.Height; PrecisionActive = true; }
            }
            catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or ArgumentException
                or TypeLoadException or MissingMemberException or AmbiguousMatchException)
            {
                failed = true; PrecisionActive = false; width = player.width; height = player.height;
                ModContent.GetInstance<MutantPilot>().Logger.Warn("Native equipment observation unavailable; full player box retained: " + ex.Message);
            }
        }
        public static System.Numerics.Vector2 ProjectileHalf(Player player) => ReferenceEquals(captured, player) && PrecisionActive
            && PilotTuning.Current.UseNativeHurtbox ? new(width / 2f, height / 2f) : new(player.width / 2f, player.height / 2f);
        public static object Snapshot(Player player) => new { available = initialized && !failed,
            precisionActive = ReferenceEquals(captured, player) && PrecisionActive,
            projectileWidth = ProjectileHalf(player).X * 2, projectileHeight = ProjectileHalf(player).Y * 2,
            physicalWidth = player.width, physicalHeight = player.height,
            StyxSet, StyxMeter, StyxReady, TimeFrozenTicks, CapturedAt,
            dashActive = ReferenceEquals(captured, player) && DashActive,
            dashImmuneRemaining = ReferenceEquals(captured, player) ? DashImmuneRemaining : 0,
            specialDashCooldown = SpecialDashCooldown, dashStarts = DashStarts,
            goldShellActive = GoldShellActive };
    }
}

using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;

namespace MutantPilot.Common
{
    // Golden Stasis: the only guaranteed-hit answer in this kit. GoldKeyEffect.ActiveSkillJustPressed
    // calls GoldKey, which adds GoldenStasisBuff for 300 ticks (600 with Gold Enchant) and a 3600 tick
    // cooldown buff. While that buff lives, GoldenStasisBuff.Update sets GoldShell = true, and
    // FargoSoulsPlayer.ImmuneToDamage includes GoldShell, so CanBeHitByProjectile returns false and no
    // Hurt event fires at all. The buff also zeroes every movement key, i.e. the player stands still.
    //
    // Pressing while the shell is already up CANCELS it, so that is the one case this must never do.
    // Pressing during the cooldown is a harmless no-op, so no buff-type reflection is needed.
    public static class FargoGoldShell
    {
        static bool ready, failed;
        static string failure = "not initialised";
        static MethodInfo equipped, active, justPressed;
        static FieldInfo noUsingItems, spectreGhost;
        static Type fargoPlayerType, effectPlayerType, effectType;
        static object effect;
        public static int Presses { get; private set; }
        public static int LastPressTick { get; private set; } = -1;
        public static bool Available => ready && !failed;

        static Type Resolve(Mod fargo, string name)
        {
            Type type = fargo.Code.GetType(name);
            if (type == null) failure = "type not found: " + name;
            return type;
        }

        static MethodInfo FindMethod(Type type, string name, Type parameter)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo exact = current.GetMethod(name, BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { parameter }, null);
                if (exact != null) return exact;
                MethodInfo loose = current.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType.IsAssignableFrom(parameter));
                if (loose != null) return loose;
            }
            return null;
        }

        static bool Prepare()
        {
            if (ready) return true;
            if (failed) return false;
            try
            {
                if (!ModLoader.TryGetMod("FargowiltasSouls", out Mod fargo)) { failure = "mod missing"; return false; }
                effectType = Resolve(fargo, "FargowiltasSouls.Core.AccessoryEffectSystem.AccessoryEffect");
                if (effectType == null) { failed = true; return false; }
                Type loaderType = Resolve(fargo, "FargowiltasSouls.Core.AccessoryEffectSystem.AccessoryEffectLoader");
                if (loaderType == null) { failed = true; return false; }
                Type goldType = Resolve(fargo, "FargowiltasSouls.Content.Items.Accessories.Enchantments.GoldKeyEffect");
                if (goldType == null) { failed = true; return false; }
                fargoPlayerType = Resolve(fargo, "FargowiltasSouls.Core.ModPlayers.FargoSoulsPlayer");
                effectPlayerType = Resolve(fargo, "FargowiltasSouls.Core.AccessoryEffectSystem.AccessoryEffectPlayer");
                if (fargoPlayerType == null || effectPlayerType == null) { failed = true; return false; }

                FieldInfo registry = loaderType.GetField("AccessoryEffects", BindingFlags.Public | BindingFlags.Static);
                if (registry?.GetValue(null) is IEnumerable list)
                    foreach (object candidate in list)
                        if (candidate != null && candidate.GetType() == goldType) { effect = candidate; break; }
                if (effect == null)
                {
                    MethodInfo generic = loaderType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "GetEffect" && m.IsGenericMethodDefinition
                            && m.GetParameters().Length == 0);
                    if (generic != null) effect = generic.MakeGenericMethod(goldType).Invoke(null, null);
                }
                if (effect == null) { failed = true; failure = "no GoldKeyEffect instance"; return false; }

                equipped = FindMethod(effectPlayerType, "Equipped", effectType);
                active = FindMethod(effectPlayerType, "Active", effectType);
                justPressed = FindMethod(effectType, "ActiveSkillJustPressed", typeof(Player));
                // The real signature is (Player, bool stunned), so fall back to any two-argument
                // overload before giving up.
                if (justPressed == null)
                    foreach (MethodInfo m in effectType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        if (m.Name == "ActiveSkillJustPressed" && m.GetParameters().Length == 2) { justPressed = m; break; }
                if (equipped == null) { failed = true; failure = "Equipped not found"; return false; }
                if (active == null) { failed = true; failure = "Active not found"; return false; }
                if (justPressed == null) { failed = true; failure = "ActiveSkillJustPressed not found"; return false; }
                noUsingItems = fargoPlayerType.GetField("NoUsingItems");
                spectreGhost = fargoPlayerType.GetField("SpectreGhostTime");
                ready = true;
                return true;
            }
            catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or ArgumentException
                or TypeLoadException or MissingMemberException or AmbiguousMatchException)
            {
                failed = true; failure = ex.GetType().Name + ": " + ex.Message;
                ModContent.GetInstance<MutantPilot>().Logger.Warn("Golden Stasis unavailable: " + failure);
                return false;
            }
        }

        static object ModPlayerOf(Player player, Type type)
        {
            foreach (ModPlayer candidate in player.ModPlayers)
                if (candidate.GetType() == type) return candidate;
            return null;
        }

        public static bool Trigger(Player player)
        {
            if (!Prepare())
            {
                if (!failed) { failed = true; ModContent.GetInstance<MutantPilot>().Logger.Warn("Golden Stasis unavailable: " + failure); }
                return false;
            }
            // Already encased: pressing cancels the shell, so never do it.
            if (FargoCapabilities.GoldShellActive) return false;
            try
            {
                object mp = ModPlayerOf(player, fargoPlayerType);
                object aep = ModPlayerOf(player, effectPlayerType);
                if (mp == null || aep == null) return false;
                if (equipped.Invoke(aep, new[] { effect }) is not true) return false;
                if (active.Invoke(aep, new[] { effect }) is not true) return false;
                bool stunned = player.CCed
                    || (noUsingItems?.GetValue(mp) is int noUse && noUse > 0)
                    || (spectreGhost?.GetValue(mp) is int ghost && ghost > 0);
                if (stunned) return false;
                justPressed.Invoke(effect, new object[] { player, false });
                Presses++;
                LastPressTick = (int)Main.GameUpdateCount;
                return true;
            }
            catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException
                or ArgumentException or MissingMemberException)
            {
                failed = true; failure = ex.GetType().Name + ": " + ex.Message;
                ModContent.GetInstance<MutantPilot>().Logger.Warn("Golden Stasis press failed: " + failure);
                return false;
            }
        }

        public static object Snapshot() => new { available = Available, failed, failure,
            presses = Presses, lastTick = LastPressTick,
            shellActive = FargoCapabilities.GoldShellActive };
    }
}

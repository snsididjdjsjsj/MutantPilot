using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;

namespace MutantPilot.Common
{
    // Action, not observation: fires the exact entry point the game calls when the player presses
    // the effect's active-skill key, behind the same guards (Equipped, Active, not stunned), so it
    // can never do more than a key press could. It does not touch health, immunity timers, the boss
    // or the RNG, and every fire is counted so a run can prove what happened.
    public static class FargoActiveSkill
    {
        static bool ready, failed;
        static string failure = "not initialised";
        static MethodInfo equipped, active, justPressed;
        static FieldInfo goldShell, spectreGhost, noUsingItems;
        static Type fargoPlayerType, effectPlayerType, effectType;
        static object effect;
        static int preparedTick = -1;
        public static int Fired { get; private set; }
        public static int LastFiredTick { get; private set; } = -1;
        public static bool Available => ready && !failed;
        public static string Failure => failure;
        // Diagnostics for the one check inside SpecialDashKey that can swallow the press without
        // leaving a trace: HasEffect<BetsyDashEffect>() must be true, and the player must be local.
        public static bool? HasEffectDash { get; private set; }
        public static int WhoAmI { get; private set; } = -1;
        public static int MainMyPlayer { get; private set; } = -1;
        static MethodInfo hasEffectDash;

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
                Type dashType = Resolve(fargo, "FargowiltasSouls.Content.Items.Accessories.Masomode.BetsyDashEffect");
                if (dashType == null) { failed = true; return false; }
                fargoPlayerType = Resolve(fargo, "FargowiltasSouls.Core.ModPlayers.FargoSoulsPlayer");
                effectPlayerType = Resolve(fargo, "FargowiltasSouls.Core.AccessoryEffectSystem.AccessoryEffectPlayer");
                if (fargoPlayerType == null || effectPlayerType == null) { failed = true; return false; }

                // Finding the live instance is more reliable through the loader's registry than
                // through GetEffect<T>(), which routes via ModContent and can come back null.
                FieldInfo registry = loaderType.GetField("AccessoryEffects", BindingFlags.Public | BindingFlags.Static);
                if (registry?.GetValue(null) is IEnumerable list)
                    foreach (object candidate in list)
                        if (candidate != null && candidate.GetType() == dashType) { effect = candidate; break; }
                if (effect == null)
                {
                    MethodInfo generic = loaderType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "GetEffect" && m.IsGenericMethodDefinition
                            && m.GetParameters().Length == 0);
                    if (generic != null) effect = generic.MakeGenericMethod(dashType).Invoke(null, null);
                }
                if (effect == null) { failed = true; failure = "no BetsyDashEffect instance"; return false; }

                equipped = FindMethod(effectPlayerType, "Equipped", effectType);
                active = FindMethod(effectPlayerType, "Active", effectType);
                justPressed = FindMethod(effectType, "ActiveSkillJustPressed", typeof(Player));
                if (justPressed == null)
                    foreach (MethodInfo m in effectType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        if (m.Name == "ActiveSkillJustPressed" && m.GetParameters().Length == 2) { justPressed = m; break; }
                if (equipped == null) { failed = true; failure = "Equipped not found"; return false; }
                if (active == null) { failed = true; failure = "Active not found"; return false; }
                if (justPressed == null) { failed = true; failure = "ActiveSkillJustPressed not found"; return false; }

                goldShell = fargoPlayerType.GetField("GoldShell");
                spectreGhost = fargoPlayerType.GetField("SpectreGhostTime");
                noUsingItems = fargoPlayerType.GetField("NoUsingItems");
                hasEffectDash = loaderType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "HasEffect" && m.IsGenericMethodDefinition
                        && m.GetParameters().Length == 1)?.MakeGenericMethod(dashType);
                ready = true; preparedTick = (int)Main.GameUpdateCount;
                return true;
            }
            catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or ArgumentException
                or TypeLoadException or MissingMemberException or AmbiguousMatchException)
            {
                failed = true; failure = ex.GetType().Name + ": " + ex.Message;
                ModContent.GetInstance<MutantPilot>().Logger.Warn("Betsy dash trigger unavailable: " + failure);
                return false;
            }
        }

        static object ModPlayerOf(Player player, Type type)
        {
            foreach (ModPlayer candidate in player.ModPlayers)
                if (candidate.GetType() == type) return candidate;
            return null;
        }

        // Mirrors FargoSoulsPlayer.ProcessTriggers: the key handler refuses to run while the player
        // is stunned, so this does too.
        public static bool Trigger(Player player)
        {
            if (!Prepare())
            {
                if (!failed) { failed = true; ModContent.GetInstance<MutantPilot>().Logger.Warn("Betsy dash trigger unavailable: " + failure); }
                return false;
            }
            try
            {
                object mp = ModPlayerOf(player, fargoPlayerType);
                object aep = ModPlayerOf(player, effectPlayerType);
                if (mp == null || aep == null) return false;
                if (equipped.Invoke(aep, new[] { effect }) is not true) return false;
                if (active.Invoke(aep, new[] { effect }) is not true) return false;
                bool stunned = player.CCed
                    || (noUsingItems?.GetValue(mp) is int noUse && noUse > 0)
                    || (goldShell?.GetValue(mp) is bool gold && gold)
                    || (spectreGhost?.GetValue(mp) is int ghost && ghost > 0);
                if (stunned) return false;
                WhoAmI = player.whoAmI; MainMyPlayer = Main.myPlayer;
                if (hasEffectDash != null) HasEffectDash = hasEffectDash.Invoke(null, new object[] { player }) is true;
                // SpecialDashKey returns immediately while its own 5 second cooldown is running, so
                // pressing then would only burn the caller's opportunity and change nothing.
                if (FargoCapabilities.SpecialDashCooldown > 0) return false;
                justPressed.Invoke(effect, new object[] { player, false });
                Fired++;
                LastFiredTick = (int)Main.GameUpdateCount;
                return true;
            }
            catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException
                or ArgumentException or MissingMemberException)
            {
                failed = true; failure = ex.GetType().Name + ": " + ex.Message;
                ModContent.GetInstance<MutantPilot>().Logger.Warn("Betsy dash trigger failed: " + failure);
                return false;
            }
        }

        // fired = presses issued, starts = dash bodies actually observed. Comparing the two is how a
        // run proves the press turned into a dash instead of being swallowed by the internal cooldown.
        public static object Snapshot() => new { available = Available, failed, failure, fired = Fired,
            lastTick = LastFiredTick, preparedTick,
            starts = FargoCapabilities.DashStarts, cooldown = FargoCapabilities.SpecialDashCooldown,
            hasEffect = HasEffectDash, whoAmI = WhoAmI, mainMyPlayer = MainMyPlayer };
    }
}

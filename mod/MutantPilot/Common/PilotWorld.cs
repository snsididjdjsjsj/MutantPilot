using System;
using System.Reflection;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace MutantPilot.Common
{
    public static class PilotWorld
    {
        public static bool IsLegendary => Main.masterMode && Main.getGoodWorld;
        public static object DifficultySnapshot() => new { gameMode=Main.GameMode, expert=Main.expertMode,
            master=Main.masterMode, forTheWorthy=Main.getGoodWorld, zenith=Main.zenithWorld, legendary=IsLegendary };
        public static int BossHyper(NPC boss) => boss?.ModNPC?.GetType().GetField("hyper")?.GetValue(boss.ModNPC) is int value
            ? value : -1;
        static Player inspectedPlayer;
        static PropertyInfo heroGod;
        static readonly List<(object Target, FieldInfo Field)> assistanceFields = new();
        static void Inspect(Player player)
        {
            if (ReferenceEquals(inspectedPlayer, player)) return;
            inspectedPlayer = player; assistanceFields.Clear();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            heroGod = ModLoader.TryGetMod("HEROsMod", out Mod hero)
                ? hero.Code?.GetType("HEROsMod.HEROsModServices.GodModeService")?.GetProperty("Enabled", flags) : null;
            if (ModLoader.TryGetMod("MutantRecorder", out Mod recorder))
            {
                var god = recorder.Code?.GetType("MutantRecorder.Common.RecorderPlayer")?.GetField("God", flags);
                if (god?.FieldType == typeof(bool)) assistanceFields.Add((null, god));
            }
            foreach (var mp in player.ModPlayers)
                foreach (string name in new[] { "godMode", "GodMode", "godmode", "GodModeEnabled", "noClip", "NoClip" })
                {
                    var field = mp.GetType().GetField(name, flags);
                    if (field?.FieldType == typeof(bool)) assistanceFields.Add((mp, field));
                }
        }
        public static bool IsMasochist
        {
            get
            {
                if (!ModLoader.TryGetMod("FargowiltasSouls", out Mod fargo)) return false;
                Type world = fargo.Code?.GetType("FargowiltasSouls.Core.Systems.WorldSavingSystem");
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                return world?.GetField("MasochistModeReal", flags)?.GetValue(null) is true
                    || world?.GetProperty("MasochistModeReal", flags)?.GetValue(null) is true;
            }
        }
        public static bool HasAssistance(Player player)
        {
            if (player.creativeGodMode) return true;
            Inspect(player);
            if (heroGod?.GetValue(null) is true) return true;
            foreach (var entry in assistanceFields)
                if (entry.Field.GetValue(entry.Target) is true) return true;
            return false;
        }
        public static string[] DescribeAssistance(Player player)
        {
            Inspect(player);
            var result = new List<string>();
            if (heroGod?.GetValue(null) is true) result.Add("HEROsMod.GodModeService.Enabled");
            if (player.creativeGodMode) result.Add("Terraria.Player.creativeGodMode");
            foreach (var entry in assistanceFields)
                if (entry.Field.GetValue(entry.Target) is true)
                    result.Add(entry.Field.DeclaringType.FullName + "." + entry.Field.Name);
            return result.ToArray();
        }
        public static void DisableAssistance(Player player)
        {
            Inspect(player);
            player.creativeGodMode = false;
            heroGod?.SetValue(null, false);
            foreach (var entry in assistanceFields)
                if (!entry.Field.IsInitOnly) entry.Field.SetValue(entry.Target, false);
        }
    }
}

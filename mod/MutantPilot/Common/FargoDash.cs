using System;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;

namespace MutantPilot.Common
{
	/// <summary>弱引用 Fargo's Souls。未安装时 Betsy 冲刺自动跳过。</summary>
	public static class FargoDash
	{
		public static int BetsyDashType = -1;
		public static int MutantBossType = -1;
		static Type playerType;
		static MethodInfo specialDashKey;

		public static void Init()
		{
			BetsyDashType = -1;
			MutantBossType = -1;
			playerType = null;
			specialDashKey = null;
			if (!ModLoader.TryGetMod("FargowiltasSouls", out var mod))
				return;
			if (mod.TryFind<ModProjectile>("BetsyDash", out var dash))
				BetsyDashType = dash.Type;
			if (mod.TryFind<ModNPC>("MutantBoss", out var boss))
				MutantBossType = boss.Type;
			playerType = mod.Code?.GetType("FargowiltasSouls.Core.ModPlayers.FargoSoulsPlayer");
			specialDashKey = playerType?.GetMethod("SpecialDashKey", new[] { typeof(int) });
		}

		public static void Unload()
		{
			playerType = null;
			specialDashKey = null;
			BetsyDashType = -1;
			MutantBossType = -1;
		}

		public static bool MutantBossAlive()
		{
			if (MutantBossType < 0)
				return false;
			for (int i = 0; i < Main.maxNPCs; i++)
			{
				NPC npc = Main.npc[i];
				if (npc.active && npc.type == MutantBossType)
					return true;
			}
			return false;
		}

		public static void ReadBetsy(Player player, out bool dashing, out int cd)
		{
			dashing = false;
			cd = 0;
			foreach (var mp in player.ModPlayers)
			{
				var type = mp.GetType();
				var dash = type.GetField("BetsyDashing");
				var cool = type.GetField("SpecialDashCD");
				if (dash == null && cool == null)
					continue;
				if (dash?.GetValue(mp) is bool on)
					dashing = on;
				if (cool?.GetValue(mp) is int left)
					cd = left;
				return;
			}
		}

		public static bool TrySpecialDash(Player player)
		{
			if (playerType == null || specialDashKey == null)
				return false;
			foreach (var mp in player.ModPlayers)
			{
				if (mp.GetType() != playerType)
					continue;
				specialDashKey.Invoke(mp, new object[] { 2 });
				return true;
			}
			return false;
		}
	}
}

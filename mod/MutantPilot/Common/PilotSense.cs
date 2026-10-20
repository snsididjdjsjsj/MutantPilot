using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace MutantPilot.Common
{
	/// <summary>
	/// 把当前帧收成客户端能还原成 758 维观测的原始数。坐标减去开打时的玩家位置，和模拟器的原点对齐。
	/// </summary>
	public static class PilotSense
	{
		public const int Header = 40;
		public const int Stride = 10;
		public const int MaxProj = 96;

		static readonly Dictionary<int, uint> born = new();
		static readonly (string name, int kind)[] names =
		{
			("MutantSpearThrown", 1), ("MutantSpearMagical", 1),
			("MutantSphereSmall", 2), ("MutantBombSmall", 3), ("MutantSphereRing", 4),
			("MutantMark1", 5),
			("MutantDeathray1", 6), ("MutantDeathrayAim", 6), ("MutantDeathraySmall", 6),
			("MutantGlowything", 7),
			("MutantEye", 8), ("MutantEyeWavy", 8), ("MutantEyeHoming", 9),
			("MutantSpearDash", 10), ("MutantSpearAim", 11), ("MutantSpearSpin", 12),
			("MutantSword", 13),
			("MutantTrueEyeL", 14), ("MutantTrueEyeS", 15), ("MutantTrueEyeR", 16),
			("MutantTrueEyeSphere", 17), ("MutantTrueEyeDeathray", 18),
			("PhantasmalBolt", 19), ("PhantasmalBolt2", 19), ("PhantasmalBoltTrueEye", 19),
			("PhantasmalBlast", 20),
			("MutantDeathray2", 21), ("MutantDeathray3", 22), ("MutantGiantDeathray2", 23),
			("MutantPillar", 24), ("MutantFragment", 25), ("MutantEyeOfCthulhu", 26),
			("MutantScythe1", 27), ("MutantScythe2", 27),
			("MutantDestroyerHead", 28), ("MutantDestroyerBody", 28), ("MutantDestroyerTail", 28),
			("MutantGuardian", 29), ("MutantFishron", 30), ("MutantShadowHand", 30),
			("MutantNuke", 31), ("MutantFishronRitual", 32), ("MutantBomb", 33),
			("MutantSlimeBall", 34), ("MutantSlimeRain", 34), ("MutantSlimeSpike", 34),
			("MutantReticle", 35), ("MutantReticle2", 35), ("MutantMark2", 36),
			("MutantCrystalLeaf", 37), ("MutantRetirang", 38), ("MutantSpazmarang", 38),
		};

		static readonly Dictionary<int, int> kindOf = new();
		static int ritualType = -1;
		static readonly HashSet<int> rays = new() { 6, 18, 21, 22, 23 };

		public static void Init()
		{
			kindOf.Clear();
			ritualType = -1;
			if (!ModLoader.TryGetMod("FargowiltasSouls", out var mod))
				return;
			foreach (var (name, kind) in names)
			{
				if (mod.TryFind<ModProjectile>(name, out var proj))
					kindOf[proj.Type] = kind;
			}
			if (mod.TryFind<ModProjectile>("MutantRitual", out var ritual))
				ritualType = ritual.Type;
		}

		public static float[] Build(Player player, Vector2 origin, int fightTick)
		{
			var buf = new float[Header + MaxProj * Stride];
			Vector2 pos = player.Center - origin;
			buf[0] = pos.X;
			buf[1] = pos.Y;
			buf[2] = player.velocity.X;
			buf[3] = player.velocity.Y;
			buf[4] = player.dashDelay;
			buf[5] = player.dashDelay > 0 && System.Math.Abs(player.velocity.X) > 12f ? 1 : 0;
			buf[6] = player.immuneTime;
			buf[7] = player.wingTime > 0 ? 1 : 0;
			FargoDash.ReadBetsy(player, out bool betsyOn, out int betsyCd);
			buf[8] = betsyOn ? 1 : 0;
			buf[9] = betsyCd;
			buf[10] = player.direction;

			NPC boss = FindBoss();
			if (boss != null)
			{
				Vector2 b = boss.Center - origin;
				buf[11] = 1;
				buf[12] = b.X;
				buf[13] = b.Y;
				buf[14] = boss.velocity.X;
				buf[15] = boss.velocity.Y;
				buf[16] = boss.ai[0];
				buf[17] = boss.ai[1];
				buf[18] = boss.ai[2];
				buf[19] = boss.ai[3];
				buf[20] = boss.localAI[0];
				buf[21] = boss.localAI[1];
				buf[22] = boss.localAI[2];
				buf[23] = boss.localAI[3];
				buf[24] = boss.life;
				buf[25] = boss.lifeMax;
				if (boss.localAI[3] == 1f)
				{
					buf[26] = 1;
					ReadAura(boss, origin, out Vector2 aura, out float scale);
					buf[27] = aura.X;
					buf[28] = aura.Y;
					buf[29] = scale;
				}
			}
			if (TryRitual(origin, out Vector2 ritual))
			{
				buf[30] = 1;
				buf[31] = ritual.X;
				buf[32] = ritual.Y;
			}
			buf[34] = fightTick;

			int n = WriteProjectiles(player, origin, buf);
			buf[33] = n;
			if (n == MaxProj)
				return buf;
			var used = new float[Header + n * Stride];
			System.Array.Copy(buf, used, used.Length);
			return used;
		}

		static int WriteProjectiles(Player player, Vector2 origin, float[] buf)
		{
			uint now = (uint)Main.GameUpdateCount;
			var keep = new HashSet<int>();
			int n = 0;
			var rest = new List<(float dist, Projectile proj, int kind, float age)>();
			for (int i = 0; i < Main.maxProjectiles; i++)
			{
				Projectile proj = Main.projectile[i];
				if (!proj.active || proj.type == ritualType)
					continue;
				bool known = kindOf.TryGetValue(proj.type, out int kind);
				if (!known)
					kind = 0;
				if (proj.friendly && !proj.hostile)
					continue;
				if (!proj.hostile && !known)
					continue;
				keep.Add(proj.identity);
				if (!born.ContainsKey(proj.identity))
					born[proj.identity] = now;
				float age = now - born[proj.identity];
				if (kind == 24)
				{
					if (n < 8)
						WriteOne(buf, n++, proj, origin, kind, age);
				}
				else
					rest.Add((Vector2.Distance(proj.Center, player.Center), proj, kind, age));
			}
			rest.Sort((a, b) => a.dist.CompareTo(b.dist));
			for (int i = 0; i < rest.Count && n < MaxProj; i++)
				WriteOne(buf, n++, rest[i].proj, origin, rest[i].kind, rest[i].age);
			foreach (int id in new List<int>(born.Keys))
			{
				if (!keep.Contains(id))
					born.Remove(id);
			}
			return n;
		}

		static void WriteOne(float[] buf, int slot, Projectile proj, Vector2 origin, int kind, float age)
		{
			int s = Header + slot * Stride;
			Vector2 c = proj.Center - origin;
			buf[s] = c.X;
			buf[s + 1] = c.Y;
			buf[s + 2] = proj.velocity.X;
			buf[s + 3] = proj.velocity.Y;
			buf[s + 4] = proj.width * 0.5f;
			buf[s + 5] = proj.height * 0.5f;
			buf[s + 6] = proj.damage > 0 ? 1 : 0;
			buf[s + 7] = kind;
			buf[s + 8] = age;
			buf[s + 9] = rays.Contains(kind) ? System.Math.Max(proj.width, proj.height) : 0;
		}

		public static NPC FindBoss()
		{
			if (FargoDash.MutantBossType < 0)
				return null;
			for (int i = 0; i < Main.maxNPCs; i++)
			{
				NPC npc = Main.npc[i];
				if (npc.active && npc.type == FargoDash.MutantBossType)
					return npc;
			}
			return null;
		}

		static bool TryRitual(Vector2 origin, out Vector2 center)
		{
			center = Vector2.Zero;
			if (ritualType < 0)
				return false;
			for (int i = 0; i < Main.maxProjectiles; i++)
			{
				Projectile proj = Main.projectile[i];
				if (proj.active && proj.type == ritualType)
				{
					center = proj.Center - origin;
					return true;
				}
			}
			return false;
		}

		public static bool TryArena(out Vector2 center, out float radius)
		{
			center = Vector2.Zero;
			radius = 0;
			float best = float.MaxValue;
			for (int i = 0; i < Main.maxProjectiles; i++)
			{
				Projectile proj = Main.projectile[i];
				if (!proj.active || proj.ModProjectile == null)
					continue;
				if (proj.ModProjectile.Name != "MutantRitual")
					continue;
				float r = proj.localAI[0] > 100f ? proj.localAI[0] : 1200f;
				if (r < best)
				{
					best = r;
					center = proj.Center;
					radius = r;
				}
			}
			NPC boss = FindBoss();
			if (boss != null && boss.localAI[3] == 1f)
			{
				ReadAura(boss, Vector2.Zero, out Vector2 aura, out float scale);
				if (aura == Vector2.Zero)
					aura = boss.Center;
				float r = 2000f * scale;
				if (r < best)
				{
					center = aura;
					radius = r;
				}
			}
			return radius > 100f;
		}

		static void ReadAura(NPC boss, Vector2 origin, out Vector2 center, out float scale)
		{
			center = boss.Center - origin;
			scale = 1f;
			object modnpc = boss.ModNPC;
			if (modnpc == null)
				return;
			var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
			var type = modnpc.GetType();
			var fCenter = type.GetField("AuraCenter", flags);
			var fScale = type.GetField("AuraScale", flags);
			if (fCenter?.GetValue(modnpc) is Vector2 aura)
				center = aura - origin;
			if (fScale?.GetValue(modnpc) is float sc && sc > 0)
				scale = sc;
		}
	}
}

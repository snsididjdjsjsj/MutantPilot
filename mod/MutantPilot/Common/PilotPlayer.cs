using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using MutantPilot.Combat;

namespace MutantPilot.Common
{
    public class PilotPlayer : ModPlayer
    {
        public static bool God => false;
        LiveCombat combat;
        LiveMotion observedMotion = new();
        CombatSession session;
        bool bossWasAlive, manualOff, encounterInterrupted;
        int originalSlot = -1;
        object lastResult;
        int prepareTicks;
        int attackStuck;
        int lastTimelineState = int.MinValue;
        bool prepareReached;
        float prepareY;
        public string PrepareArena()
        {
            if (Player.dead) return "Player is dead";
            if (PilotSense.FindBoss() != null) return "Cannot prepare during a fight";
            float minimum = Main.topWorld + 656 + Player.height / 2f;
            float airBottom = System.Math.Max(minimum + 240, (float)Main.worldSurface * 16 - 1000);
            prepareY = (minimum + airBottom) * .5f;
            prepareTicks = 10800; // Permit ordinary regeneration after a failed low-life trial.
            prepareReached = System.Math.Abs(Player.Center.Y - prepareY) <= 24;
            TrialSummons.Begin(Player);
            return null;
        }
        public object Snapshot() => new {
            tick = session?.Ticks ?? 0, enabled = ModContent.GetInstance<PilotSystem>().Enabled,
            dead = Player.dead, life = Player.statLife, x = Player.Center.X, y = Player.Center.Y,
            vx = Player.velocity.X, vy = Player.velocity.Y, maxRun = Player.maxRunSpeed, accRun = Player.accRunSpeed,
            acceleration = Player.runAcceleration, wings = Player.wingsLogic, wingTime = Player.wingTime,
            weapon = Player.HeldItem.Name, actualDamage = session?.Metrics.DamageDealt ?? 0,
            attackTelemetry = CombatSession.AttackSnapshot(Player),
            hits = session?.Metrics.Hits ?? 0, assisted = session?.Metrics.Assisted ?? false,
            controllerMs = combat?.TotalMilliseconds ?? 0,
            modelError = combat?.PredictionError ?? 0, lastResult,
            prepareTicks, prepareY, prepareReached, attackStuck,summonPreparation=TrialSummons.Snapshot(Player),
            motion = observedMotion.Parameters,
            hotbar = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(
                System.Linq.Enumerable.Range(0, 10), i => new { slot = i, name = Player.inventory[i].Name,
                    type = Player.inventory[i].type, damage = Player.inventory[i].damage }))
        };
        public void SetPilot(bool enabled)
        {
            ModContent.GetInstance<PilotSystem>().Enabled = enabled;
            manualOff = !enabled;
            if (!enabled) { prepareTicks = 0; encounterInterrupted = true; End("manual_release"); }
        }
        public override void OnEnterWorld()
        {
            observedMotion = new();
            bossWasAlive = false; manualOff = false; encounterInterrupted = false;
            Main.NewText("[MutantPilot] 实战规划器已加载，无需 Python。F6 接管/交还；记录真实伤害与受击。", Color.CadetBlue);
        }
        public override void PostUpdateRunSpeeds()
        {
            if (Player.whoAmI == Main.myPlayer && !Main.gameMenu)
            { observedMotion.Capture(Player); FargoCapabilities.Capture(Player); }
        }
        public override void ProcessTriggers(Terraria.GameInput.TriggersSet triggersSet)
        {
            if (Player.whoAmI != Main.myPlayer) return;
            var sys = ModContent.GetInstance<PilotSystem>();
            if (sys.TogglePilot?.JustPressed != true) return;
            SetPilot(!sys.Enabled);
            Main.NewText(sys.Enabled ? "[MutantPilot] 实战规划接管开启" : "[MutantPilot] 操作交还", Color.CadetBlue);
        }
        public override void SetControls()
        {
            if (Player.whoAmI != Main.myPlayer || Main.gameMenu) return;
            var sys = ModContent.GetInstance<PilotSystem>();
            NPC boss = PilotSense.FindBoss();
            bool alive = boss != null;
            if (alive && !bossWasAlive) { manualOff = false; encounterInterrupted = false; sys.Enabled = true; }
            if (!alive && bossWasAlive) { End("boss_disappeared"); sys.Enabled = false; }
            bossWasAlive = alive;
            if (Player.dead) { End("player_dead"); sys.Enabled = false; manualOff = true; return; }
            if (!alive && prepareTicks > 0)
            {
                prepareTicks--;
                prepareReached |= System.Math.Abs(Player.Center.Y - prepareY) <= 24;
                int vertical = prepareReached ? 3 : Player.Center.Y > prepareY ? 2 : 0;
                PilotControls.Apply(Player, new Move(0, vertical));
                TrialSummons.Step(Player);
                return;
            }
            if (alive) prepareTicks = 0;
            if (!sys.Enabled || manualOff || !alive) return;
            if (session == null)
            {
                originalSlot = Player.selectedItem;
                PilotControls.SelectWeapon(Player);
                session = new CombatSession(Mod, Player, boss); combat = new LiveCombat();
                if (encounterInterrupted) session.Metrics.Invalidate();
                Main.NewText("[MutantPilot] 持续攻击＋实战避险规划已接管。", Color.LightGreen);
            }
            ReportTimeline(boss);
            if (session.Ticks % 60 == 0) PilotControls.SelectWeapon(Player);
            var move = combat.Decide(Player, boss, session.Ticks, session.Ticks - session.LastDamageTick, observedMotion);
            CombatFrameTiming.Controller(combat.TotalMilliseconds);
            PilotControls.Apply(Player, move);
            bool attacking = PilotControls.AimAndAttack(Player, boss);
            if(FargoArmorSkill.TryUse(Player,boss,combat.LastDecision.ForecastFirstHit==0))attacking=Player.controlUseItem;
            DetectBlockedAttack(attacking);
            long telemetryStart = System.Diagnostics.Stopwatch.GetTimestamp();
            session.Tick(Player, boss, combat, attacking);
            CombatFrameTiming.Telemetry((System.Diagnostics.Stopwatch.GetTimestamp()-telemetryStart)*1000.0/System.Diagnostics.Stopwatch.Frequency);
            if (session.Ticks % 300 == 0 && !boss.dontTakeDamage && session.Ticks - session.LastDamageTick > 180)
                Main.NewText("[MutantPilot] 最近未命中：检查武器/弹药/魔力；正在缩短攻击距离。", Color.Orange);
        }
        /// <summary>
        /// Observation only: on every boss-state change, record the counters the offline attack
        /// timeline needs. The planner does not read this yet.
        /// </summary>
        void ReportTimeline(NPC boss)
        {
            int state = (int)boss.ai[0];
            if (state == lastTimelineState || session == null) return;
            lastTimelineState = state;
            session.Note("state_enter", new {
                state, ai1 = boss.ai[1], ai2 = boss.ai[2], ai3 = boss.ai[3],
                localAI1 = boss.localAI[1], localAI2 = boss.localAI[2],
                bossX = boss.Center.X, bossY = boss.Center.Y,
            });
        }
        /// <summary>
        /// The use-item input can be accepted and still never start a swing (ItemCheck_Inner
        /// refuses while the player is crowd controlled or item use is suppressed). Detect that
        /// state, record what was true at the time, and try the two cheapest recoveries.
        /// </summary>
        void DetectBlockedAttack(bool attacking)
        {
            bool idle = Player.itemAnimation == 0 && Player.itemTime == 0;
            attackStuck = (attacking && idle) ? attackStuck + 1 : 0;
            if (attackStuck < 30 || (attackStuck != 30 && attackStuck % 300 != 0)) return;
            if (session == null) return;
            var buffs = new System.Collections.Generic.List<string>();
            for (int i = 0; i < Player.buffType.Length; i++)
                if (Player.buffType[i] > 0)
                    buffs.Add(Terraria.Lang.GetBuffName(Player.buffType[i]) + ":" + Player.buffTime[i]);
            session.Note("attack_blocked", new {
                stuckFrames = attackStuck,
                state = CombatSession.AttackSnapshot(Player),
                buffs,
            });
            // Recovery 1: hand the game a release edge, then press again next frame.
            Player.controlUseItem = false;
            Player.releaseUseItem = true;
            // Recovery 2: re-pick the weapon slot (a stale selection is another silent refusal).
            PilotControls.SelectWeapon(Player);
        }
        public override void OnHitNPCWithProj(Projectile proj,NPC target, NPC.HitInfo hit, int damageDone)
        {
            if (target.type == FargoDash.MutantBossType) session?.Damage(damageDone,DamageOrigin.Snapshot(proj),hit.Crit);
        }
        public override void OnHitNPCWithItem(Item item,NPC target,NPC.HitInfo hit,int damageDone)
        {
            if(target.type==FargoDash.MutantBossType)session?.Damage(damageDone,new {kind="item",name=item.ModItem?.FullName??$"Terraria/{item.type}",item.prefix},hit.Crit);
        }
        public override void OnHurt(Player.HurtInfo info) => session?.Hurt(Player, info, combat);
        public override void Kill(double damage, int hitDirection, bool pvp, Terraria.DataStructures.PlayerDeathReason damageSource)
        {
            End("player_dead");
            ModContent.GetInstance<PilotSystem>().Enabled = false;
        }
        public void RecordKill(NPC boss)
        {
            if (session == null || session.BossIndex != boss.whoAmI) return;
            session.Killed();
            lastResult = new { reason = "boss_killed", damage = session.Metrics.DamageDealt,
                hits = session.Metrics.Hits, noHitVictory = session.NoHitVictory };
            Main.NewText(session.NoHitVictory ? "[MutantPilot] 已确认零受击击杀！"
                : "[MutantPilot] 已确认击杀；本局未通过零受击验收，详情见战斗记录。", Color.LightGreen);
        }
        void End(string reason)
        {
            if (session != null) lastResult = new { reason = session.Metrics.ResultReason(reason), damage = session.Metrics.DamageDealt,
                hits = session.Metrics.Hits, noHitVictory = session.NoHitVictory };
            session?.Finish(reason); session = null; combat = null;
            if (originalSlot >= 0) { Player.selectedItem = originalSlot; originalSlot = -1; }
        }
        public override void PlayerDisconnect() => End("disconnect");
    }
}

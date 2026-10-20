using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Terraria;
using Terraria.ModLoader;
using MutantPilot.Combat;

namespace MutantPilot.Common
{
    public sealed class CombatSession : IDisposable
    {
        public readonly BattleMetrics Metrics = new();
        public int LastDamageTick { get; private set; }
        public int Ticks { get; private set; }
        public int BossIndex { get; private set; }
        StreamWriter writer;
        readonly Mod mod;
        bool closed;
        int rejectedRecords;
        int goldShellTicks;
        int dashImmuneTicks;
        static readonly JsonSerializerOptions diagnosticOptions = new() {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        readonly int? developmentSeed = PilotHarness.DevelopmentSeed;
        // A real no-hit run never needs an immunity crutch. Golden Stasis AND the Betsy dash both
        // route through FargoSoulsPlayer.ImmuneToDamage, which makes CanBeHitByProjectile return false,
        // so both skip the Hurt event instead of dodging it. Counting only GoldShell would leave the
        // dash as a loophole that certifies the same fake result.
        public bool NoHitVictory => Metrics.NoHitVictory && !developmentSeed.HasValue
            && goldShellTicks == 0 && dashImmuneTicks == 0;
        static System.Reflection.FieldInfo noUsingField;
        static string noUsingOwner;

        /// <summary>
        /// Fargo's Souls blocks item use through its own counter. Read it by reflection so this
        /// mod keeps working without a hard reference, and so a stuck attack can be attributed.
        /// </summary>
        static int ReadNoUsing(Player player)
        {
            var players = player.ModPlayers;
            if (noUsingField == null)
            {
                foreach (var mp in players)
                {
                    if (mp == null || mp.GetType().Name != "FargoSoulsPlayer") continue;
                    noUsingField = mp.GetType().GetField("NoUsingItems");
                    noUsingOwner = mp.GetType().FullName;
                    break;
                }
            }
            if (noUsingField == null) return -1;
            foreach (var mp in players)
                if (mp != null && mp.GetType().Name == "FargoSoulsPlayer")
                    return noUsingField.GetValue(mp) is int value ? value : 0;
            return -1;
        }

        public static object AttackSnapshot(Player player) => new {
            effectiveDamage = player.GetWeaponDamage(player.HeldItem),
            attackSpeed = player.GetTotalAttackSpeed(player.HeldItem.DamageType),
            player.itemTime, player.itemAnimation, player.itemAnimationMax,
            player.mouseInterface, inventoryOpen = Main.playerInventory,
            // Why an attack can be silently refused; see ItemCheck_Inner's first guard.
            controlUseItem = player.controlUseItem, player.releaseUseItem,
            cced = player.CCed, player.frozen, player.webbed, player.stoned,
            player.cursed, player.noItems, player.silence,
            fargoNoUsingItems = ReadNoUsing(player),
            selectedItem = player.selectedItem, weapon = player.HeldItem.Name,
        };

        /// <summary>Free-form diagnostic record; the analyser ignores kinds it does not know.</summary>
        public void Note(string kind, object payload)
        {
            if (closed) return;
            Write(new { kind, tick = Ticks, gameTick = Main.GameUpdateCount, payload });
        }
        public CombatSession(Mod owner, Player player, NPC boss)
        {
            mod = owner; BossIndex = boss.whoAmI;
            CombatFrameTiming.Begin();
            LogicFrameRecorder.BeginBattle();
            Metrics.VerifyTargetMode(PilotWorld.IsMasochist);
            try
            {
                string directory = Path.Combine(Main.SavePath, "MutantPilot");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"combat_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jsonl");
                writer = new StreamWriter(path, false, new UTF8Encoding(false));
                Write(new { kind = "start", version = 2, controller = "live-mpc", controllerVersion = owner.Version.ToString(), bossLifeMax = boss.lifeMax,
                    weapon = player.HeldItem.Name, mods = Array.ConvertAll(ModLoader.Mods, m => m.Name + "@" + m.Version),
                    objective = "zero-hit verified Masochist boss kill without Golden Stasis", goldenStasisAllowed = false,
                    masochist = Metrics.TargetModeVerified,
                    developmentSeed, background = BackgroundCombat.Snapshot(), collisionBroadPhase = BeamBroadPhaseCheck.Snapshot(),
                    recording = LogicFrameRecorder.Snapshot(),
                    equipmentObservation = FargoCapabilities.Snapshot(player),
                    world = new { name = Main.worldName, width = Main.maxTilesX, height = Main.maxTilesY, surface = Main.worldSurface },
                    assistance = PilotWorld.DescribeAssistance(player),
                    attackTelemetry = AttackSnapshot(player),
                    buffs = player.buffType.Select((type, slot) => new { type, ticks = player.buffTime[slot],
                        name = type > 0 ? Terraria.Lang.GetBuffName(type) : "" }).Where(buff => buff.type > 0).ToArray(),
                    settings = PilotTuning.Current,
                    equipment = player.armor.Select((item, slot) => new { slot, item.type, item.Name, item.prefix })
                        .Where(item => item.type > 0).ToArray(),
                    projectileTypes = ModContent.GetContent<ModProjectile>()
                        .Where(p => p.FullName.Contains("Mutant", StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(p => p.Type, p => p.FullName),
                    expert = Main.expertMode, master = Main.masterMode, utc = DateTime.UtcNow });
            }
            catch (IOException ex) { mod.Logger.Warn("Combat recording unavailable: " + ex.Message); }
        }
        public void Tick(Player player, NPC boss, LiveCombat combat, bool attacking)
        {
            Ticks++;
            if (FargoCapabilities.GoldShellActive) { goldShellTicks++; Metrics.Invalidate(); }
            // Same rule for the dash: it is the same ImmuneToDamage bypass, so a run that used it is
            // not a real no-hit either.
            if (FargoCapabilities.DashActive) { dashImmuneTicks++; Metrics.Invalidate(); }
            Metrics.Tick(PilotWorld.HasAssistance(player));
            if (combat.SpatialAuditMismatches > 0) Metrics.Invalidate();
            if (Ticks % 60 == 0 && !PilotWorld.IsMasochist) Metrics.Invalidate();
            var d = combat.LastDecision;
            if (Ticks > 600 && Ticks % 6 != 0 && d.Cost < 500000 && combat.PredictionError < 1) return;
            Write(new { kind = "frame", tick = Ticks, gameTick = Main.GameUpdateCount, playerX = player.Center.X, playerY = player.Center.Y,
                vx = player.velocity.X, vy = player.velocity.Y, bossX = boss.Center.X, bossY = boss.Center.Y,
                attack = boss.ai[0], life = boss.life, lifeMax = boss.lifeMax, invulnerable = boss.dontTakeDamage,
                bossAI = boss.ai, bossLocalAI = boss.localAI,
                weapon = player.HeldItem.Name, attacking, range = player.Distance(boss.Center),
                attackTelemetry = AttackSnapshot(player),
                goalX = combat.Goal.X, goalY = combat.Goal.Y, action = d.Move.Id,
                arenaX = combat.ArenaCenter.X, arenaY = combat.ArenaCenter.Y, arenaRadius = combat.ArenaRadius,
                expectedX = d.Next.Position.X, expectedY = d.Next.Position.Y,
                expectedVx = d.Next.Velocity.X, expectedVy = d.Next.Velocity.Y, flightMastery = combat.FlightMastery,
                motion = combat.MotionStats,
                worldMinX = combat.WorldMinimum.X, worldMinY = combat.WorldMinimum.Y,
                worldMaxX = combat.WorldMaximum.X, worldMaxY = combat.WorldMaximum.Y,
                dash = player.dash, dashType = player.dashType, dashTime = player.dashTime,
                dashDelay = player.dashDelay, player.releaseLeft, player.releaseRight,
                expectedDash = d.Next.Dash,
                plannerMs = d.Milliseconds, horizon = d.Horizon, evaluations = d.Evaluations, riskCost = d.Cost,
                controllerMs = combat.TotalMilliseconds, forecastMs = combat.ForecastMilliseconds,
                budgetMs = combat.EffectiveBudgetMs, rescueBudgetMs = combat.EffectiveRescueBudgetMs,
                predictedFirstHit = d.FirstHit,
                predictedBeamAngle = d.NextAngle,
                navigation = combat.NavigationReport,
                forecastFirstHit = d.ForecastFirstHit,
                dangerQueries = d.DangerQueries, cachedQueries = d.CachedQueries,
                threatCandidates = combat.ThreatCandidates, threatFullScanCandidates = combat.ThreatFullScanCandidates,
                spatialAuditChecks = combat.SpatialAuditChecks, spatialAuditMismatches = combat.SpatialAuditMismatches,
                futureAuditChecks = combat.FutureAuditChecks, futureAuditMismatches = combat.FutureAuditMismatches,
                spatialIndexedMeanUs = combat.SpatialIndexedMeanUs, spatialFullScanMeanUs = combat.SpatialFullScanMeanUs,
                bossVx = boss.velocity.X, bossVy = boss.velocity.Y,
                rescueDepth = d.RescueDepth,
                predictionError = combat.PredictionError, threats = combat.ThreatCount,
                actualDamage = Metrics.DamageDealt, hits = Metrics.Hits, assisted = Metrics.Assisted });
            if (Ticks % 300 == 0) Write(new { kind = "forecast_audit", tick = Ticks, measurements = combat.ForecastReport });
            if (Ticks % 120 == 0) writer?.Flush();
        }
        public void Damage(int amount)
        {
            if (closed) return;
            Metrics.Damage(amount); LastDamageTick = Ticks;
            Write(new { kind = "damage", tick = Ticks, gameTick = Main.GameUpdateCount, amount, total = Metrics.DamageDealt });
        }
        public void Hurt(Player player, Player.HurtInfo info, LiveCombat combat)
        {
            if (closed) return;
            Metrics.Hurt();
            int npcIndex = info.DamageSource.SourceNPCIndex;
            NPC sourceNpc = npcIndex >= 0 && npcIndex < Main.maxNPCs ? Main.npc[npcIndex] : null;
            Write(new { kind = "hit", tick = Ticks, gameTick = Main.GameUpdateCount, damage = info.Damage,
                source = info.DamageSource.SourceProjectileType, npc = info.DamageSource.SourceNPCIndex,
                sourceName = ModContent.GetModProjectile(info.DamageSource.SourceProjectileType)?.FullName,
                sourceIndex = info.DamageSource.SourceProjectileLocalIndex,
                forecast = combat?.TraceThreat(info.DamageSource.SourceProjectileLocalIndex),
                x = player.Center.X, y = player.Center.Y, vx = player.velocity.X, vy = player.velocity.Y,
                npcState = sourceNpc == null ? null : new { sourceNpc.type, x = sourceNpc.Center.X, y = sourceNpc.Center.Y,
                    vx = sourceNpc.velocity.X, vy = sourceNpc.velocity.Y, sourceNpc.ai, sourceNpc.localAI },
                nearby = Main.projectile.Where(p => p.active && p.hostile && p.damage > 0 &&
                    (p.Distance(player.Center) < 500 || p.whoAmI == info.DamageSource.SourceProjectileLocalIndex))
                    .Select(p => new { id = p.whoAmI, name = p.ModProjectile?.Name, x = p.Center.X, y = p.Center.Y,
                        vx = p.velocity.X, vy = p.velocity.Y, p.width, p.height, p.scale, p.ai, p.localAI,
                        p.rotation, p.frame, p.frameCounter, p.timeLeft,
                        canDamage = ProjectileLoader.CanDamage(p) }).ToArray() });
            writer?.Flush();
        }
        public void Killed() { if (!PilotWorld.IsMasochist) Metrics.Invalidate(); Metrics.Kill(); Finish("boss_killed"); }
        public void Finish(string reason)
        {
            if (closed) return;
            LogicFrameRecorder.EndBattle(new {reason,ticks=Ticks,damage=Metrics.DamageDealt,hits=Metrics.Hits,
                killed=Metrics.Killed,noHitVictory=NoHitVictory});
            Write(new { kind = "performance", measurement = CombatFrameTiming.Finish() });
            Write(new { kind = "result", reason, ticks = Ticks, damage = Metrics.DamageDealt,
                hits = Metrics.Hits, assisted = Metrics.Assisted, killed = Metrics.Killed,
                masochist = Metrics.TargetModeVerified, developmentSeed, rejectedRecords,
                goldShellTicks, dashImmuneTicks, noHitVictory = NoHitVictory });
            closed = true; writer?.Dispose(); writer = null;
        }
        void Write(object record)
        {
            if (writer == null) return;
            try { writer.WriteLine(JsonSerializer.Serialize(record)); }
            catch (ArgumentException ex)
            {
                // Diagnostic data must never unwind a normal game-control hook.
                // The encounter also loses certification if a record is rejected.
                Metrics.Invalidate();
                rejectedRecords++;
                if (rejectedRecords <= 3)
                {
                    mod.Logger.Warn("Combat record rejected; encounter invalidated: " + ex.Message);
                    // Keep bounded diagnostic evidence without silently accepting
                    // nonfinite values as valid combat telemetry.
                    try { writer.WriteLine(JsonSerializer.Serialize(new { kind = "rejected_record", tick = Ticks,
                        gameTick = Main.GameUpdateCount, diagnostic = JsonSerializer.Serialize(record, diagnosticOptions) })); }
                    catch (IOException) { }
                }
            }
            catch (IOException ex)
            {
                mod.Logger.Warn("Combat recording stopped: " + ex.Message);
                writer.Dispose(); writer = null;
            }
        }
        public void Dispose() => Finish("interrupted");
    }
    public sealed class PilotKillAudit : GlobalNPC
    {
        public override void OnKill(NPC npc)
        {
            if (Main.dedServ || npc.type != FargoDash.MutantBossType) return;
            Main.LocalPlayer.GetModPlayer<PilotPlayer>().RecordKill(npc);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using NVector = System.Numerics.Vector2;
using MutantPilot.Combat;

namespace MutantPilot.Common
{
    public sealed class LiveCombat
    {
        readonly CombatPlanner planner = new();
        readonly WorldRouting worldRouting = new();
        readonly SphereBurstForecast sphereBirths = new();
        readonly MarkBurstForecast markBirths = new();
        readonly TossResponseForecast tossResponse = new();
        readonly ForecastAudit tossBossAudit = new();
        readonly ForecastAudit ritualResponseAudit = new();
        bool coupledToss;
        int legendaryHyper=-1;
        Projectile activeDashProjectile;
        readonly int[] bossAiUpdates=new int[CombatPlanner.ForecastTicks+1];
        readonly int[] bossAiElapsed=new int[CombatPlanner.ForecastTicks+1];
        readonly EyeBurstForecast eyeBirths = new();
        readonly MechFanForecast fanBirths = new();
        readonly SwordResponseForecast swordResponse = new();
        readonly ForecastAudit swordResponseAudit = new();
        readonly List<SwordPart> swordParts = new(16);
        readonly List<float> wormSwordReferences = new(4096);
        bool coupledSword;
        int queriedSword = -1;
        readonly SmallEyeResponseForecast smallEyeResponse = new();
        readonly ForecastAudit smallEyeResponseAudit = new();
        readonly List<SmallEye> smallEyes = new(6);
        readonly List<float> wormEyeReferences = new(4096);
        readonly List<float> eyeTossReferences = new(4096);
        readonly List<float> eyeSwordReferences = new(4096);
        bool coupledSmallEyes;
        readonly DirectDashResponseForecast directDashResponse = new();
        readonly ForecastAudit directDashAudit = new();
        readonly List<float> wormDashReferences = new(4096);
        bool coupledDirectDash;
        int queriedDirectDash = -1;
        readonly NVector[] fixedBossPath = new NVector[CombatPlanner.ForecastTicks+1];
        readonly ForecastAudit fixedBossAudit = new();
        int fixedBossKnown;
        readonly VoidInnerRouting voidRouting = new();
        readonly VoidOuterRouting outerVoid = new();
        readonly P3VoidRouting p3VoidRouting = new();
        readonly EocRouting eocRouting = new();
        readonly List<LinearMark> voidMarks = new(80);
        readonly List<GuideTrack> voidCurved = new(80);
        readonly NVector[] voidGoals = new NVector[CombatPlanner.ForecastTicks + 1];
        readonly NVector[] tossGoals = new NVector[CombatPlanner.ForecastTicks + 1];
        readonly MotionModel motion = new();
        readonly ForecastAudit forecastAudit = new();
        readonly ForecastAudit eyeBirthAudit = new();
        readonly List<PendingEyeRay> futureEyeRays = new(4);
        readonly Dictionary<Type, FieldInfo> pupilFields = new();
        readonly WormForecast wormForecast = new(Main.maxProjectiles);
        readonly WormResponseForecast wormResponse = new();
        readonly ForecastAudit wormResponseAudit = new();
        readonly List<float> wormTossReferences = new(4096);
        bool coupledWorms;
        int queriedWorm = -1;
        readonly List<Projectile> wormParts = new(256);
        readonly List<WormNode> wormNodes = new(256);
        readonly Dictionary<(int Owner, int Identity), Projectile> wormIdentities = new();
        static readonly int[] auditHorizons = { 1, 6, 24, 48 };
        readonly List<Threat> threats = new(512);
        readonly ForecastHazardIndex futureIndex = new(CombatPlanner.ForecastTicks);
        readonly List<(NVector Center,int At)> forecastExplosions = new(64);
        public long FutureAuditChecks { get; private set; }
        public long FutureAuditMismatches { get; private set; }
        readonly TemporalBuckets threatBuckets = new(CombatPlanner.ForecastTicks);
        readonly List<int> globalThreats = new(32);
        readonly Dictionary<int, Track> tracks = new();
        readonly Dictionary<Type, bool> customShapes = new();
        readonly Dictionary<int, Vector2[]> forecasts = new();
        readonly Dictionary<int, float[]> rayAngles = new();
        readonly Dictionary<int, float[]> shortRayScales = new();
        readonly List<PendingVoidRay> futureMarkRays = new(24);
        readonly List<(Vector2 Center, int At)> explosions = new();
        readonly List<TrailOrb> futureOrbs = new(48);
        readonly List<TimedBeam> futureBeams = new(40);
        readonly List<TimedBox> futureBoxes = new(128);
        readonly List<DecayingShard> futureShards = new(384);
        readonly List<Projectile> warningPillars = new(8);
        readonly TimelineHazard timeline = new();
        readonly Dictionary<int, int> beamSources = new();
        readonly Dictionary<int, (int Type, TimedBox Box)> boxSources = new();
        readonly Dictionary<(int Owner, int Identity), (Projectile Parent, NVector[] Centers)> leafParents = new();
        readonly Dictionary<int, TrueEyeForecast> trueEyeBuffers = new();
        readonly Dictionary<(int Owner, int Identity), (Projectile Parent, TrueEyeForecast Path)> trueEyeParents = new();
        readonly bool[] bombOwners = new bool[256];
        readonly record struct Track(long Generation, int Type, Vector2 Position, float Angle, ulong Tick);
        readonly record struct Threat(Projectile Projectile, int ObservedType, long Generation, Vector2 Velocity, float RotationSpeed,
            bool LongShape, int RemainingTicks, int ArmsIn, bool WarmingRay, bool SmallBomb, bool Sword, Vector2[] Centers, float[] Angles,
            BeamBroadPhase? Broad = null, int ShortRayKind = 0, float[] Scales = null);
        MotionState before, expected;
        Move previous = new(0, 3);
        bool havePrevious;
        int side = 1, sideUntil;
        int tossTravelHoldUntil;
        // Latched axis/side for the state-41 wind-up lateral escape; see TossEarlyLateral.
        float tossEarlyAngle;
        int tossEarlySide = 1;
        // Default stance keeps circling the boss; see the goal assignment in Decide.
        float standAngle;
        int standSign = 1;
        bool standInitialised;
        int kiteDirection = -1, lastAttack = int.MinValue;
        float orbitAngle;
        float p1EyeOrbitAngle;
        bool trackingEyeOrbit;
        Vector2 arenaCenter, goal;
        float arenaRadius, inflation;
        float clearanceReserve;
        NVector projectileHalf;
        // Ticks of the current plotting horizon still covered by Betsy dash immunity (0 = none).
        int dashImmune;
        // 0, not int.MinValue: "tick - last" with int.MinValue overflows negative in unchecked
        // arithmetic and silently disables the trigger forever.
        int lastDashTriggerTick;
        // Evidence of accessory-skill use: presses accepted, and what the dash was aimed at.
        int dashFires;
        Vector2 dashAim;
        int dashFiredAtThreatTicks;
        // Tick until which the dash direction stays the goal; see DashRunTicks in PilotTuning.
        int dashRunUntil;
        Vector2[] arenaPath;
        Projectile giantRay;
        readonly List<Projectile> giantRays = new(2);
        bool stateDependentThreats;
        int finalOrbitSide = -1;
        long lastGiantGeneration = -1;
        bool haveLockedDashSide;
        int lockedDashSide = 1;
        Vector2 voidRayCenter;
        int voidRayHoldUntil = -1;
        long voidLastTick;
        float voidLastAngle, voidLastStep;
        bool trackingVoid;
        int fanSide = 1;
        int tossOrbitSide = 1;
        int tossTravelSide = 1;
        PendingSpear? pendingLockedSpear;
        int p1TossHoldUntil = -1, p1TossSide = 1;
        float p1TossAngle;
        long beamQueries, beamRejected;
        Player player;
        NPC boss;
        public Decision LastDecision { get; private set; }
        public object NavigationReport => new { cost = planner.NavigationCost, firstHit = planner.NavigationFirstHit,
            futureIndexParallel = futureIndex.LastBuildParallel,
            forecastWorkers=futureIndex.WorkerCount,
            reactiveProposals = planner.ReactiveProposals,
            rescueFrontierPeak=planner.RescueFrontierPeak,
            giantRayCount=giantRays.Count,
            activeDash=activeDashProjectile==null?null:new {x=activeDashProjectile.Center.X,y=activeDashProjectile.Center.Y,
                countdown=activeDashProjectile.localAI[0],fast=activeDashProjectile.ai[1]==-2},
            detourProposals = planner.DetourProposals,
            retainedForecastFirstHit = planner.RetainedForecastFirstHit,
            selectedRoute = LastDecision.RescueDepth>0 || LastDecision.ForecastFirstHit>0 ? planner.SelectedRoute : null,
            fixedBossKnown, fixedBossResponseAudit = fixedBossAudit.Snapshot(),
            eocRoute=eocRouting.Center is NVector eocCenter?new {x=eocCenter.X,y=eocCenter.Y}:null,
            coupledSmallEyes, smallEyeForecastStates = coupledSmallEyes ? smallEyeResponse.Count : 0,
            smallEyeResponseAudit = smallEyeResponseAudit.Snapshot(),
            coupledDirectDash, directDashForecastStates = coupledDirectDash ? directDashResponse.Count : 0,
            directDashResponseAudit = directDashAudit.Snapshot(),
            forecastFirstHit = planner.NavigationForecastFirstHit, x = planner.NavigationEnd.X,
            y = planner.NavigationEnd.Y, chosen = planner.NavigationChosen, timeline = timeline.Snapshot(),
            world = new { anchorY = worldRouting.AnchorY, upperY = worldRouting.UpperY,
                lowerY = worldRouting.LowerY, direction = worldRouting.Direction,
                heightCost = boss.ai[0] >= 0 && boss.ai[0] != 33 && boss.ai[0] != 34
                    ? worldRouting.HeightCost(N(player.Center)) : 0,
                topClearance = player.Center.Y - motion.WorldMinimum.Y },
            pendingSpherePaths = sphereBirths.Count, pendingSphereRings = sphereBirths.Events.Count,
            pendingMarks = markBirths.Events.Count, pendingEyes = eyeBirths.Events.Count, pendingFanRays = fanBirths.Paths.Count,
            nativeEquipment = FargoCapabilities.Snapshot(player),
            betsyDash = FargoActiveSkill.Snapshot(),
            accessorySkills = new { allowed = PilotTuning.Current.AllowAccessorySkills,
                trigger = PilotTuning.Current.TriggerBetsyDash, fires = dashFires,
                aimX = dashAim.X, aimY = dashAim.Y, firedAtThreatTicks = dashFiredAtThreatTicks,
                starts = FargoCapabilities.DashStarts, allowedGoldenStasis = PilotTuning.Current.AllowGoldenStasis },
            goldShell = FargoGoldShell.Snapshot(),
            dashImmunity = new { remaining = dashImmune, applied = PilotTuning.Current.UseDashImmunity },
            clearanceReserve, voidRoute = voidRouting.Snapshot(), outerVoidRoute = outerVoid.Snapshot(), p3VoidRoute=p3VoidRouting.Snapshot(), pendingLockedSpear = pendingLockedSpear is PendingSpear spear
                ? new { spear.Born, spear.Padding, x = spear.Origin.X, y = spear.Origin.Y, vx = spear.Velocity.X, vy = spear.Velocity.Y } : null,
            beamBroadPhase = new { beamQueries, beamRejected }, coupledToss,
            tossForecastStates = coupledToss ? tossResponse.Count : 0,
            tossBossResponseAudit = tossBossAudit.Snapshot(),
            ritualResponseAudit=ritualResponseAudit.Snapshot(),
            coupledSword, swordForecastStates = coupledSword ? swordResponse.Count : 0,
            swordResponseAudit = swordResponseAudit.Snapshot(),
            coupledWorms, wormForecastStates = coupledWorms ? wormResponse.Count : 0,
            wormResponseAudit = wormResponseAudit.Snapshot() };
        public float PredictionError => motion.Error;
        public Vector2 Goal => goal;
        public Vector2 ArenaCenter => arenaCenter;
        public float ArenaRadius => arenaRadius;
        public int ThreatCount => threats.Count;
        public bool FlightMastery => motion.FlightMastery;
        public MotionParameters MotionStats { get; private set; }
        public NVector WorldMinimum => motion.WorldMinimum;
        public NVector WorldMaximum => motion.WorldMaximum;
        public double TotalMilliseconds { get; private set; }
        public double ForecastMilliseconds { get; private set; }
        public double EffectiveBudgetMs { get; private set; }
        public double EffectiveRescueBudgetMs { get; private set; }
        public long ThreatCandidates { get; private set; }
        public long ThreatFullScanCandidates { get; private set; }
        long spatialQuerySerial;
        public long SpatialAuditChecks { get; private set; }
        public long SpatialAuditMismatches { get; private set; }
        long spatialIndexedTicks, spatialFullScanTicks;
        public double SpatialIndexedMeanUs => SpatialAuditChecks == 0 ? 0
            : spatialIndexedTicks * 1000000.0 / Stopwatch.Frequency / SpatialAuditChecks;
        public double SpatialFullScanMeanUs => SpatialAuditChecks == 0 ? 0
            : spatialFullScanTicks * 1000000.0 / Stopwatch.Frequency / SpatialAuditChecks;
        public object ForecastReport => forecastAudit.Snapshot().Select(row => new {
            name = ModContent.GetModProjectile(row.Type)?.FullName ?? row.Type.ToString(), measurement = row })
            .Concat(eyeBirthAudit.Snapshot().Select(row => new {
                name = "Pending/" + (ModContent.GetModProjectile(row.Type)?.FullName ?? row.Type.ToString()), measurement = row })).ToArray();
        public object TraceThreat(int index)
        {
            foreach (Threat t in threats)
                if (t.Projectile.whoAmI == index && t.Projectile.type == t.ObservedType && ProjectileBirth.Of(t.Projectile) == t.Generation)
                {
                    NVector next = coupledWorms && wormResponse.Contains(index)
                        ? wormResponse.Position(LastDecision.NextAngle, index) : N(t.Centers[1]);
                    return new { observedX = t.Centers[0].X, observedY = t.Centers[0].Y,
                        predictedX = next.X, predictedY = next.Y,
                        t.ArmsIn, t.RemainingTicks, virtualBeams = futureBeams.Count + futureMarkRays.Count, virtualOrbs = futureOrbs.Count,
                        virtualShards = futureShards.Count, virtualExplosions = explosions.Count };
                }
            if (boxSources.TryGetValue(index, out var source) && Main.projectile[index].type == source.Type)
                return new { timedBox = true, observedX = source.Box.Center.X, observedY = source.Box.Center.Y,
                    ArmsIn = source.Box.ArmsAt, RemainingTicks = source.Box.EndsAt, virtualBoxes = futureBoxes.Count };
            if (beamSources.TryGetValue(index, out int beamType) && Main.projectile[index].type == beamType)
                return new { forecastBeam = true, persistentLifetime = true, virtualBeams = futureBeams.Count + futureMarkRays.Count };
            return new { unseenThisTick = true, virtualSpherePaths = sphereBirths.Count,
                virtualBeams = futureBeams.Count + futureMarkRays.Count, virtualOrbs = futureOrbs.Count,
                virtualBoxes = futureBoxes.Count, virtualShards = futureShards.Count, virtualExplosions = explosions.Count };
        }

        static NVector N(Vector2 v) => new(v.X, v.Y);
        static Vector2 X(NVector v) => new(v.X, v.Y);

        public Move Decide(Player local, NPC target, int tick, int ticksWithoutDamage, LiveMotion observed)
        {
            var totalWatch = Stopwatch.StartNew();
            player = local; boss = target;
            if(boss.ai[0]==20&&lastAttack!=20)eocRouting.Reset();
            legendaryHyper=PilotWorld.IsMasochist&&Main.getGoodWorld?PilotWorld.BossHyper(boss):-1;
            int clockPhase=legendaryHyper;
            for(int future=1;future<bossAiUpdates.Length;future++)
            {
                bossAiUpdates[future]=EncounterClock.Next(ref clockPhase);
                bossAiElapsed[future]=bossAiElapsed[future-1]+bossAiUpdates[future];
            }
            clearanceReserve = boss.ai[0] >= 0 && (boss.dontTakeDamage || ticksWithoutDamage < 180)
                ? Math.Clamp(PilotTuning.Current.ThreatClearance, 0, 128) : 0;
            var actual = new MotionState(N(player.Center), N(player.velocity),
                new(player.dashType, player.dash, player.dashDelay, player.dashTime,
                    player.releaseLeft, player.releaseRight));
            if (havePrevious) motion.Observe(before, actual, previous, expected);
            observed.Apply(motion);
            // Player.BordersMovement uses a fixed 640px margin, in addition to
            // 16/32px and half the player's size. Main.topWorld alone is not reachable.
            motion.WorldMinimum = new(Main.leftWorld + 656 + player.width / 2f, Main.topWorld + 656 + player.height / 2f);
            motion.WorldMaximum = new(Main.rightWorld - 672 - player.width / 2f, Main.bottomWorld - 672 - player.height / 2f);
            MotionStats = observed.Parameters;
            inflation = 3 + Math.Min(12, motion.Error);
            projectileHalf = FargoCapabilities.ProjectileHalf(player);
            dashImmune = PilotTuning.Current.UseDashImmunity ? FargoCapabilities.DashImmuneRemaining : 0;
            double forecastStart = totalWatch.Elapsed.TotalMilliseconds;
            coupledSword = boss.ai[2] != 0 && (boss.ai[0] is 45 or 46 || boss.ai[0] == 9 && boss.localAI[2] is 1 or 2);
            fixedBossKnown=PilotWorld.IsMasochist && boss.ai[0]==20
                ? EocBossForecast.Forecast(fixedBossPath,N(boss.Center),N(boss.velocity),new(boss.ai[2],boss.ai[3]),boss.ai[1]) : 0;
            BuildThreats();
            coupledSmallEyes = smallEyes.Count > 0 && giantRay == null;
            if (coupledSmallEyes) smallEyeResponse.Reset(smallEyes);
            ForecastMilliseconds = totalWatch.Elapsed.TotalMilliseconds - forecastStart;
            ThreatCandidates = 0; ThreatFullScanCandidates = 0;
            beamQueries = beamRejected = 0;
            bool circle = PilotArena.TryCircle(out arenaCenter, out arenaRadius, out Projectile ritual);
            arenaPath = ritual == null ? null : forecasts.GetValueOrDefault(ritual.whoAmI);
            if (!circle) { arenaCenter = boss.Center; arenaRadius = 1800; }
            if (coupledDirectDash)
            {
                DirectCharge? observedCharge=legendaryHyper>=0&&activeDashProjectile!=null&&boss.ai[0] is 6 or 15 or 23
                    ?DirectDashResponseForecast.ObservedCharge(N(activeDashProjectile.Center),N(boss.velocity),activeDashProjectile.localAI[0],
                        (int)boss.ai[0],boss.ai[1],legendaryHyper,activeDashProjectile.ai[1]==-2):null;
                directDashResponse.Reset(new(N(boss.Center),N(boss.velocity),(int)boss.ai[0],
                    boss.ai[1],boss.ai[2],boss.localAI[1],Delay:boss.ai[3],Aim:boss.localAI[0],Arena:circle?N(arenaCenter):null,
                    PrepareSpan:(boss.height+player.height)/2f),legendaryHyper,observedCharge);
            }
            float range = PilotControls.AttackRange(player.HeldItem, player, boss);
            // The old 650px estimate and no-damage timeout pulled Zenith into
            // close attacks. Use the installed weapon's normal aiming bounds.
            float desired = Math.Min(range * Math.Clamp(PilotTuning.Current.StandoffFactor, 0.3f, 1.2f),
                Math.Clamp(PilotTuning.Current.Standoff, 200f, 1200f));
            // Lack of damage can come from boss transitions or transient aim;
            // it must not drive a Zenith user into the boss's close attacks.
            if (player.HeldItem.type != ItemID.Zenith && ticksWithoutDamage > 180 && !boss.dontTakeDamage) desired *= .85f;
            if (tick >= sideUntil && Math.Abs(player.Center.X - boss.Center.X) > 120)
            {
                side = Math.Sign(player.Center.X - boss.Center.X);
                sideUntil = tick + 90;
            }
            // Default stance: circle the boss instead of parking on one flank. A fixed flank makes
            // every aimed pattern converge on the same bearing, which is exactly what human no-hit
            // play avoids - they keep changing the bearing so the boss's aim and its fans always lag
            // behind. Side is still tracked because several attack-specific branches use it.
            if (PilotTuning.Current.StandOrbit)
            {
                if (!standInitialised)
                {
                    Vector2 fromBoss = player.Center - boss.Center;
                    standAngle = fromBoss.LengthSquared() > 1f ? fromBoss.ToRotation() : 0f;
                    standInitialised = true;
                }
                float orbitSpeed = Math.Clamp(PilotTuning.Current.StandOrbitSpeed, 0f, .05f);
                standAngle += standSign * orbitSpeed;
                Vector2 candidate = boss.Center + standAngle.ToRotationVector2() * desired;
                // Reverse rather than let the arena clamp flatten the circle into a stop.
                if (Vector2.Distance(candidate, arenaCenter) > Math.Max(200f, arenaRadius - 320f))
                {
                    standSign = -standSign;
                    standAngle += 2f * standSign * orbitSpeed;
                    candidate = boss.Center + standAngle.ToRotationVector2() * desired;
                }
                goal = candidate;
            }
            else goal = boss.Center + new Vector2(side * desired, 0);
            int attack = (int)boss.ai[0];
            var tuning = PilotTuning.Current;
            // Dash states: the boss closes by itself, so orbiting gets cut off. A state-14 spear
            // arrived with the player at 186px, which is 4 ticks at 45px/tick - no lateral move can
            // beat that, and the spear only ever travels the boss-to-player line. Retreat radially and
            // hold a minimum range instead of circling. The locked-aim refinement below still runs.
            if (attack is 5 or 6 or 14 or 15 or 23)
            {
                Vector2 away = player.Center - boss.Center;
                float hold = Math.Max(desired, Math.Clamp(tuning.MinDashRange, 300f, 1200f));
                if (away.LengthSquared() > 1f) goal = boss.Center + Vector2.Normalize(away) * hold;
            }
            coupledToss = PilotWorld.IsMasochist && (Main.getGoodWorld&&attack==0&&boss.localAI[3]>0&&boss.ai[3]>0 || attack == 25 || attack == 41 && boss.ai[1] > 0
                && boss.localAI[1] > 0 && Math.Abs(boss.localAI[2]) == 1);
            if (coupledToss)
            {
                Projectile aim = null; int count = 0;
                foreach (var threat in threats)
                    if (threat.Projectile.ModProjectile?.Name == "MutantSpearAim" && threat.Projectile.ai[0] == boss.whoAmI
                        && (attack==0?threat.Projectile.ai[1]==3:attack==25 ? threat.Projectile.ai[1]==2 : threat.Projectile.ai[1]<=1)) { aim = threat.Projectile; count++; }
                if (count != 1) aim = null;
                tossResponse.Reset(new(N(boss.Center), N(boss.velocity), boss.ai[1], boss.ai[3], boss.ai[2], false,
                        AimAngle: aim != null && aim.localAI[0] != 0 ? aim.rotation : float.NaN,Arena:circle?N(arenaCenter):null,
                        LockedAim:boss.localAI[0]),
                    boss.localAI[0], boss.localAI[2], attack==0?boss.ai[3]:boss.localAI[1], aim?.whoAmI ?? -1, aim?.timeLeft ?? 0, attack==25,
                    legendaryHyper,firstPhase:attack==0);
            }
            wormTossReferences.Clear(); wormTossReferences.Add(0);
            wormSwordReferences.Clear(); wormSwordReferences.Add(0);
            wormEyeReferences.Clear(); wormEyeReferences.Add(0);
            wormDashReferences.Clear(); wormDashReferences.Add(0);
            eyeTossReferences.Clear(); eyeTossReferences.Add(0);
            eyeSwordReferences.Clear(); eyeSwordReferences.Add(0);
            if (coupledSword)
            {
                swordParts.Clear();
                foreach (Threat threat in threats)
                    if (threat.Sword)
                    {
                        Projectile p=threat.Projectile;
                        swordParts.Add(new(p.whoAmI,p.ai[1],p.localAI[0]==0 ? (p.Center-boss.Center).ToRotation() : p.localAI[1],p.timeLeft));
                    }
                swordResponse.Reset(new(N(boss.Center),N(boss.velocity),boss.ai[1],boss.ai[3],
                    boss.ai[0]==45 || boss.ai[0]==9 && boss.localAI[2]==1,Arena:circle?N(arenaCenter):null),boss.ai[2],swordParts,legendaryHyper);
            }
            worldRouting.Observe(N(player.Center), motion.WorldMinimum,
                new(motion.WorldMaximum.X, (float)Main.worldSurface * 16 - 200),
                tuning.WorldBoundaryReserve, tuning.KiteBand);
            // The encounter's height anchor is fixed, not the moving boss or ritual.
            // Boundary cost also applies outside spear states, so another attack
            // cannot silently spend all the room reserved for the next manoeuvre.
            if (tuning.KiteBand > 0) kiteDirection = worldRouting.Direction;
            else
            {
                if (player.Center.Y < 2400) kiteDirection = 1;
                if (player.Center.Y > Main.worldSurface * 16 - 2000) kiteDirection = -1;
            }
            if (tuning.RouteSpears && attack is 0 or 25 or 41)
                goal.Y = player.Center.Y + kiteDirection * Math.Clamp(tuning.KiteOffset, 0, 1200);
            if (tuning.RouteSpears && attack is 5 or 6)
            {
                if (lastAttack is not (5 or 6))
                {
                    NVector tangent = SpearOrbitRouting.Tangent(N(player.Center), N(boss.Center), 1);
                    float forward = NVector.Dot(tangent, N(player.velocity));
                    tossOrbitSide = Math.Abs(forward) > 4 ? Math.Sign(forward) : side;
                }
                goal = player.Center + X(SpearOrbitRouting.Tangent(N(player.Center), N(boss.Center), tossOrbitSide)) * 700;
            }
            // State 21 ends in a six-update wind-up. Build lateral speed while
            // its aim still follows us, including at a height-band edge.
            if (tuning.RouteSpears && attack is 4 or 5 or 6 or 13 or 21 or 22)
            {
                if(lastAttack is not (4 or 5 or 6 or 13 or 21 or 22 or 23))
                {
                    NVector tangent=SpearOrbitRouting.Tangent(N(player.Center),N(boss.Center),1);
                    float forward=NVector.Dot(tangent,N(player.velocity));
                    tossOrbitSide=Math.Abs(forward)>4?Math.Sign(forward):side;
                }
                NVector tangentGoal=SpearOrbitRouting.Tangent(N(player.Center),N(boss.Center),tossOrbitSide);
                goal=player.Center+X(tangentGoal)*700;
            }
            if (tuning.RouteSpears && tuning.TossOrbit && attack == 41)
            {
                if (lastAttack != 41)
                {
                    tossTravelSide = Math.Abs(player.velocity.X)>4 ? Math.Sign(player.velocity.X)
                        : player.Center.X < (motion.WorldMinimum.X+motion.WorldMaximum.X)/2 ? 1 : -1;
                }
                // Keep making horizontal progress through this long throw sequence.
                // A tight orbit folds back across rays that remain live for 156 ticks.
                if(player.Center.X<motion.WorldMinimum.X+6000)tossTravelSide=1;
                if(player.Center.X>motion.WorldMaximum.X-6000)tossTravelSide=-1;
                float travel=Math.Clamp(tuning.KiteOffset,200,1200);
                goal=player.Center+new Vector2(tossTravelSide*travel,kiteDirection*travel*(tuning.ContinuousTossTravel?1f:.3f));
                if(tuning.SpiralTossTravel && Math.Abs(boss.localAI[2])==1)
                    goal=X(TossRouting.Goal(N(player.Center),N(boss.Center),boss.localAI[2],travel));
            }
            if(tuning.LegendaryWormTravel && Main.getGoodWorld && attack is 0 or 24 or 25)
            {
                if(lastAttack is not (0 or 24 or 25))
                    tossTravelSide=Math.Abs(player.velocity.X)>4?Math.Sign(player.velocity.X)
                        :player.Center.X<(motion.WorldMinimum.X+motion.WorldMaximum.X)/2?1:-1;
                if(player.Center.X<motion.WorldMinimum.X+6000)tossTravelSide=1;
                if(player.Center.X>motion.WorldMaximum.X-6000)tossTravelSide=-1;
                // Spread pursuing chains along a moving path from their telegraph
                // onward. Every candidate still checks all native worm collisions.
                float travel=Math.Clamp(tuning.KiteOffset,400,1200);
                goal=player.Center+new Vector2(tossTravelSide*travel,kiteDirection*travel*.6f);
            }
            // State 41 locks its throw aim at ai[1] 151 and then drags two 156-tick MutantDeathray2
            // rays along that axis while small bombs fall on the trail; the 2026-10-20 hit records
            // cluster exactly there. A lateral 600px displacement taken during the wind-up makes the
            // throw and both rays miss, which is the only cheap answer to a pattern this wide.
            if (tuning.RouteSpears && tuning.TossEarlyLateral && attack == 41 && boss.ai[1] < tuning.TossEarlyTicks)
            {
                if (lastAttack != 41)
                {
                    Vector2 fromBoss = player.Center - boss.Center;
                    tossEarlyAngle = fromBoss.LengthSquared() > 1f ? fromBoss.ToRotation() : 0f;
                    Vector2 normal = new(-MathF.Sin(tossEarlyAngle), MathF.Cos(tossEarlyAngle));
                    // Keep the side the player is already on: crossing the axis re-enters the throw.
                    tossEarlySide = Vector2.Dot(fromBoss, normal) < 0 ? -1 : 1;
                }
                goal = X(ProjectileKinematics.LockedSpearGoal(N(player.Center), N(boss.Center), tossEarlyAngle, tossEarlySide));
            }
            if(Main.getGoodWorld&&attack is 25 or 41)tossTravelHoldUntil=tick+156;
            if (tuning.OrbitSpheres && attack is 1 or 40)
            {
                if (lastAttack != attack) orbitAngle = (player.Center - boss.Center).ToRotation();
                orbitAngle += Math.Clamp(tuning.OrbitRadiansPerTick, -.05f, .05f);
                goal = boss.Center + orbitAngle.ToRotationVector2() * Math.Clamp(tuning.OrbitRadius, 150, range * .95f);
            }
            bool wasEyeOrbit = trackingEyeOrbit;
            trackingEyeOrbit = tuning.P1EyeOrbit && attack == 9 && boss.localAI[2] == 0 && !trackingVoid && !outerVoid.Active;
            if (trackingEyeOrbit)
            {
                if (lastAttack != 9 || !wasEyeOrbit) p1EyeOrbitAngle = (player.Center - boss.Center).ToRotation();
                p1EyeOrbitAngle += .016f;
                float radius = Math.Min(480, range * .8f);
                for (int t = 0; t < voidGoals.Length; t++)
                {
                    float angle = p1EyeOrbitAngle + .016f * t;
                    voidGoals[t] = N(boss.Center) + new NVector(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                }
                goal = X(voidGoals[8]);
            }
            if (tuning.HoldAttackPause && attack == 52 && Vector2.Distance(player.Center, boss.Center) <= range * .9f
                && ticksWithoutDamage < 120) goal = player.Center;
            if(tuning.LegendaryWormTravel&&Main.getGoodWorld&&tick<tossTravelHoldUntil&&attack is 52 or 31)
            {
                float travel=Math.Clamp(tuning.KiteOffset,400,1200);
                goal=player.Center+new Vector2(tossTravelSide*travel,kiteDirection*travel*.6f);
            }
            // Stay near the eye's actual lock from its age 120 onward. In FTW,
            // the NPC locks earlier at a different position. Radial Scythe2 and
            // all other hazards still override this preferred central opening.
            if(attack==20&&eocRouting.Center is NVector eyeCenter)goal=X(eyeCenter);
            if (attack == 33) goal = X(ProjectileKinematics.NukePreparation(N(player.Center), motion.WorldMinimum.Y));
            if (attack == 34) goal = boss.Center - new Vector2(0, 100);
            if(coupledSword)
                goal=boss.ai[0]==45 || boss.ai[0]==9&&boss.localAI[2]==1
                    ? X(SwordRouting.Preparation(N(boss.Center),boss.localAI[1],boss.ai[2]))
                    : X(SwordRouting.Escape(N(player.Center),boss.localAI[1]));
            // Predictive P2 freezes its aim at age 55 and launches at 60, or
            // 80 for the final dash. Leave that known axis during the wind-up,
            // before the spear and its paired trail projectiles exist.
            bool lockedDash = attack == 14 && boss.ai[1] >= 55 && boss.ai[2] > 0
                && boss.ai[2] < boss.localAI[1];
            if(attack==14 && boss.ai[2]>0 && boss.ai[1]<55)goal=player.Center;
            if (lockedDash)
            {
                if (!haveLockedDashSide)
                {
                    Vector2 normal = new(-MathF.Sin(boss.localAI[0]), MathF.Cos(boss.localAI[0]));
                    lockedDashSide = Vector2.Dot(player.Center - boss.Center, normal) < 0 ? -1 : 1;
                }
                goal = X(ProjectileKinematics.LockedSpearGoal(N(player.Center), N(boss.Center), boss.localAI[0], lockedDashSide));
            }
            haveLockedDashSide = lockedDash;
            // Travel dash states (6/15/23) charge at up to 60px/tick against the player's 18, so backing
            // off along the boss-to-player line is hopeless: a recorded 60px/tick charge collapsed the
            // range 405 -> 44 in six ticks while the planner kept "holding distance". Only lateral
            // displacement beats a head-on charge, which is what the state-14 locked-aim branch already
            // does. Reuse the same geometry with the live charge direction, no latch needed.
            if (tuning.DashLateralEscape && attack is 6 or 15 or 23 && boss.velocity.LengthSquared() > 400f)
            {
                float chargeAngle = boss.velocity.ToRotation();
                Vector2 chargeNormal = new(-MathF.Sin(chargeAngle), MathF.Cos(chargeAngle));
                int escapeSide = attack==15 && boss.ai[2]>1 ? lockedDashSide
                    : Vector2.Dot(player.Center - boss.Center, chargeNormal) < 0 ? -1 : 1;
                goal = X(ProjectileKinematics.LockedSpearGoal(N(player.Center), N(boss.Center), chargeAngle, escapeSide));
            }
            if (pendingLockedSpear is PendingSpear lockedSpear)
            {
                Vector2 normal = new(-MathF.Sin(boss.localAI[0]), MathF.Cos(boss.localAI[0]));
                if (tick >= p1TossHoldUntil)
                    p1TossSide = Vector2.Dot(player.Center - boss.Center, normal) < 0 ? -1 : 1;
                p1TossAngle = boss.localAI[0]; p1TossHoldUntil = tick + lockedSpear.Born + 20;
            }
            if (attack == 0 && tick < p1TossHoldUntil)
                goal = X(ProjectileKinematics.LockedSpearGoal(N(player.Center), N(boss.Center), p1TossAngle, p1TossSide));
            // P1 void rays start at the end of a 90-tick, speed-2 marker,
            // then extend outward. Approach the inner region before they fire.
            // Keep its fixed center through the last marker and damaging ray.
            // P3 has no boss contact damage. Its marks and deathrays travel
            // outward from 180/360px, leaving the central region open.
            if (trackingVoid) goal = X(voidGoals[8]);
            if (outerVoid.Active) goal = X(outerVoid.Goal(attack is 8 or -2));
            if (p3VoidRouting.Active) goal=X(p3VoidRouting.Goal());
            if (attack is 26 or 27)
            {
                if (attack != lastAttack && Math.Abs(player.Center.X - boss.Center.X) > 1)
                    fanSide = Math.Sign(player.Center.X - boss.Center.X);
                goal = boss.Center + new Vector2(fanSide * 650, 0);
                if (attack == 27 && tuning.MechFanGap && boss.ai[2] != 0)
                    goal = X(MechFanRouting.Goal(N(boss.Center), boss.ai[2]));
            }
            if ((attack is -3 or -4) && !outerVoid.Active && !p3VoidRouting.Active) goal = arenaCenter + new Vector2(side * 350, 150);
            if (attack is -6 or -7) goal = boss.Center + new Vector2(side * 350, 200);
            if (attack == -5)
            {
                if (lastAttack != -5) { finalOrbitSide = -1; lastGiantGeneration = -1; }
                if (giantRay != null && ProjectileBirth.Of(giantRay) != lastGiantGeneration)
                {
                    finalOrbitSide = FinalSparkForecast.OrbitSide((player.Center - boss.Center).ToRotation(), giantRay.velocity.ToRotation());
                    lastGiantGeneration = ProjectileBirth.Of(giantRay);
                }
                else if (giantRay == null && boss.localAI[1] != 0 && boss.ai[2] >= 330) finalOrbitSide = 1;
                goal = X(FinalSparkForecast.OrbitGoal(N(player.Center), N(boss.Center), finalOrbitSide,
                    Math.Clamp(tuning.FinalSparkRadius, 100, 500)));
            }
            // A live MutantDeathray2 is a 3000px line, so distance from its origin says nothing: what
            // matters is the perpendicular offset from its bearing, and that offset has to keep
            // growing. The 2026-10-20 kill logs show the ray sweeping into a player who was flying
            // along the beam instead of across it. Step perpendicular to the nearest armed ray.
            float rayGap = float.MaxValue;
            Vector2 rayNormal = Vector2.Zero;
            float rayStep = tuning.RaySidestep;
            // Residual rays remain in the collision search. Their nearest normal
            // must not replace a wind-up's strategic route: in native 119 it pulled
            // away from the sword's back until a collision was only 19 ticks away.
            bool dedicatedRoute=coupledSword||attack==20&&eocRouting.Center.HasValue
                ||tuning.ContinuousTossTravel&&attack==41;
            if (rayStep > 0f && !dedicatedRoute)
            {
                foreach (Threat threat in threats)
                {
                    if (threat.ShortRayKind != 2 || threat.RemainingTicks <= 0 || threat.Projectile.scale < .3f) continue;
                    Vector2 direction = threat.Projectile.velocity;
                    if (direction.LengthSquared() < .001f) continue;
                    direction = Vector2.Normalize(direction);
                    Vector2 toPlayer = player.Center - threat.Projectile.Center;
                    if (Vector2.Dot(toPlayer, direction) < -200f) continue;
                    Vector2 normal = new(-direction.Y, direction.X);
                    float lateral = Vector2.Dot(toPlayer, normal);
                    // Armed rays (scale >= .7 can damage) keep the tighter threshold; a ray that is
                    // still arming is worth leaving early, which is the only way to be clear in time.
                    float limit = threat.Projectile.scale >= .7f ? Math.Min(rayStep, 340f) : 340f;
                    if (Math.Abs(lateral) > limit) continue;
                    if (Math.Abs(lateral) < rayGap)
                    {
                        rayGap = Math.Abs(lateral);
                        rayNormal = lateral >= 0 ? normal : -normal;
                    }
                }
            }
            if (rayNormal != Vector2.Zero) goal = player.Center + rayNormal * rayStep;
            // A dash press already committed 15 ticks of immunity and ~375px of displacement along one
            // bearing. Finish that escape instead of re-centring on the stance target the moment the
            // immunity ends: measured on the 2026-10-20 trials, 8 of 20 hits landed 1-10 ticks after
            // the immunity expired. The hazard search stays authoritative and can still deviate.
            if (tick < dashRunUntil) goal = player.Center + dashAim * Math.Max(200f, tuning.DashRunDistance);
            lastAttack = attack;
            Vector2 offset = goal - arenaCenter;
            if (offset.Length() > arenaRadius - 200)
                goal = arenaCenter + Vector2.Normalize(offset) * Math.Max(80, arenaRadius - 200);
            goal.X = Math.Clamp(goal.X, motion.WorldMinimum.X + 120, motion.WorldMaximum.X - 120);
            // The installed Mutant suspends ordinary attacks while the player is underground.
            goal.Y = Math.Clamp(goal.Y, motion.WorldMinimum.Y + 120, (float)Main.worldSurface * 16 - 200);
            // Every stance target above is an attractor, and the risk search only looks
            // 48 ticks ahead. Recorded fights spent the last 96 ticks before a hit chasing
            // a goal 500-700px away whose arrival time was 38-121 ticks, so the goal was
            // never a plan the planner could execute - just a direction it committed to
            // while the hazard arrived. Keep the direction, clamp the distance to what the
            // current speed can actually deliver inside the horizon.
            Vector2 pull = goal - player.Center;
            float reach = tuning.GoalReachTicks * Math.Max(tuning.GoalReachFloorSpeed, player.velocity.Length());
            if (pull.LengthSquared() > reach * reach) goal = player.Center + Vector2.Normalize(pull) * reach;
            var frame = new PlanFrame(actual, N(goal), N(boss.Center), range, !boss.dontTakeDamage);
            double budget = Math.Clamp(tuning.BudgetMs, 1, 16), rescueBudget = Math.Clamp(tuning.RescueBudgetMs, 0, 256);
            if (giantRay != null)
            {
                budget = Math.Max(budget, Math.Clamp(tuning.FinalBeamBudgetMs, 1, 8));
                rescueBudget = Math.Max(rescueBudget, Math.Clamp(tuning.FinalBeamRescueBudgetMs, 0, 256));
            }
            EffectiveBudgetMs = budget; EffectiveRescueBudgetMs = rescueBudget;
            planner.GoalPullDivisor = tuning.GoalPullDivisor;
            planner.ProposalBudget = tuning.ProposalBudget;
            planner.RescueBeamWidth=tuning.RescueBeamWidth;
            planner.RescueSegmentTicks=tuning.RescueSegmentTicks;
            planner.DiverseRescue=tuning.DiverseRescue&&Main.getGoodWorld;
            NVector[] routeGoals=trackingVoid||trackingEyeOrbit?voidGoals:null;
            if(routeGoals==null&&coupledToss&&attack==41&&tuning.SpiralTossTravel)
            {
                TossRouting.Track(tossGoals,actual,motion,tossResponse.At(0),boss.localAI[0],
                    boss.localAI[2],boss.localAI[1],legendaryHyper);
                routeGoals=tossGoals;
                goal=X(tossGoals[0]);
                frame=frame with {Goal=tossGoals[0]};
            }
            LastDecision = coupledWorms
                ? planner.ChooseCoupled(frame, motion, 0, AdvanceWormResponse, DangerWormResponse, previous, budget, rescueBudget,
                    routeGoals)
                : coupledDirectDash
                ? planner.ChooseCoupled(frame, motion, 0, directDashResponse.Advance, DangerDirectDash, previous, budget, rescueBudget)
                : coupledSmallEyes
                ? planner.ChooseCoupled(frame, motion, 0, AdvanceSmallEyesResponse, DangerSmallEyesResponse, previous, budget, rescueBudget,
                    routeGoals)
                : trackingVoid || trackingEyeOrbit
                ? planner.ChooseTracking(frame, motion, DangerState, voidGoals, previous, budget, rescueBudget)
                : coupledToss
                ? planner.ChooseCoupled(frame, motion, 0, tossResponse.Advance, DangerToss, previous, budget, rescueBudget,routeGoals)
                : coupledSword
                ? planner.ChooseCoupled(frame, motion, 0, swordResponse.Advance, DangerSword, previous, budget, rescueBudget)
                : giantRay != null
                ? planner.ChooseCoupled(frame, motion, boss.ai[3], AdvanceRay, Danger, previous, budget, rescueBudget)
                : stateDependentThreats ? planner.ChooseWithState(frame, motion, DangerState, previous, budget, rescueBudget)
                : planner.Choose(frame, motion, DangerPosition, previous, budget, rescueBudget);
            // Accessory skills are permitted by the operator, except Golden Stasis. The Betsy dash is
            // the one that changes survivability: 15 ticks of FargoSoulsPlayer.ImmuneToDamage while
            // travelling 25px/tick toward the cursor, on a 300 tick native cooldown. Nothing presses
            // its key unless the controller does, so every use is deliberate and counted.
            // DashTriggerFirstHit > 0 waits until the chosen line is already doomed inside that many
            // ticks. 0 therefore means "preemptive": dash whenever the cooldown is free and anything
            // is predicted in the horizon.
            int threatTicks = tuning.DashTriggerFirstHit <= 0
                ? LastDecision.ForecastFirstHit : LastDecision.FirstHit;
            bool wantDash = TrialLoadout.DashesAllowed && tuning.AllowAccessorySkills && tuning.TriggerBetsyDash && threatTicks != 0
                && (tuning.DashTriggerFirstHit <= 0 || threatTicks <= tuning.DashTriggerFirstHit);
            if (wantDash && tick >= lastDashTriggerTick + Math.Max(1, tuning.DashTriggerCooldown))
            {
                // SpecialDashKey throws the dash along SafeDirectionTo(player, Main.MouseWorld), so
                // the cursor has to be aimed first, and the press cannot be recalled. Aim at the
                // bearing with the most room at the end of the dash rather than merely away from the
                // boss: the immunity lasts 15 ticks, so the player has to finish outside whatever is
                // still arriving when it expires.
                Vector2 away = DashEscapeDirection();
                if (away.LengthSquared() > 1f)
                {
                    Vector2 screenCenter = new(Main.screenWidth / 2f, Main.screenHeight / 2f);
                    Vector2 aim = (player.Center + Vector2.Normalize(away) * 400f
                        - Main.screenPosition - screenCenter) * Main.GameViewMatrix.Zoom + screenCenter;
                    Main.mouseX = (int)Math.Clamp(aim.X, 0, Main.screenWidth - 1);
                    Main.mouseY = (int)Math.Clamp(aim.Y, 0, Main.screenHeight - 1);
                }
                if (FargoActiveSkill.Trigger(player))
                {
                    lastDashTriggerTick = tick;
                    dashFires++;
                    dashAim = away;
                    dashFiredAtThreatTicks = threatTicks;
                    dashRunUntil = tick + Math.Clamp(tuning.DashRunTicks, 0, 60);
                }
            }
            // Golden Stasis. Unlike the dash this costs nothing in control, because the shell zeroes
            // every movement key anyway: it is 300 ticks of guaranteed immunity. So it is reserved for
            // the case the search itself calls lost, i.e. the chosen line already hits this soon.
            // Golden Stasis is never triggered by this build, even under stale tuning.
            if (giantRay != null && Main.GameUpdateCount % 6 == 0 && giantRay.localAI[0] + 1 < (boss.ai[0]==-7?255:PilotWorld.IsMasochist ? 780 : 600))
            {
                NVector direction = new(MathF.Cos(LastDecision.NextAngle), MathF.Sin(LastDecision.NextAngle));
                NVector center = N(boss.Center + boss.velocity) + direction * ((boss.ai[0]==-7?100:175) * giantRay.scale / 10 + 1);
                forecastAudit.Expect(Main.GameUpdateCount, giantRay.whoAmI, ProjectileBirth.Of(giantRay), giantRay.type, 1,
                    center, LastDecision.NextAngle);
            }
            if (Main.GameUpdateCount % 6 == 0)
                foreach (Threat threat in threats)
                    if (threat.Projectile.ModProjectile?.Name == "MutantSpearAim" && threat.RemainingTicks >= 1)
                    {
                        Projectile p = threat.Projectile;
                        float aim = ProjectileKinematics.SpearAimAngle(N(threat.Centers[1]), LastDecision.Next.Position,
                            LastDecision.Next.Velocity, p.ai[1], p.rotation, p.localAI[0] != 0, 1);
                        NVector at = N(threat.Centers[1]);
                        if (coupledToss && p.whoAmI == tossResponse.AimSlot)
                        {
                            var view = tossResponse.At(TossReference(LastDecision.NextAngle));
                            if (view.Closed) continue;
                            at = view.Position; aim = view.AimAngle;
                        }
                        forecastAudit.Expect(Main.GameUpdateCount, p.whoAmI, threat.Generation, p.type, 1,
                            at, aim);
                    }
            if(coupledSword && Main.GameUpdateCount%6==0)
            {
                float reference=SwordReference(LastDecision.NextAngle);
                foreach(SwordPart part in swordParts)
                {
                    Projectile p=Main.projectile[part.Slot];
                    if(part.Remaining>1&&swordResponse.At(reference).ForcedDeath<0)
                        swordResponseAudit.Expect(Main.GameUpdateCount,p.whoAmI,ProjectileBirth.Of(p),p.type,1,swordResponse.Position(reference,part));
                }
            }
            if(coupledToss && Main.GameUpdateCount%6==0)
            {
                TossWorld view=tossResponse.At(TossReference(LastDecision.NextAngle));
                if(!view.Closed)tossBossAudit.Expect(Main.GameUpdateCount,boss.whoAmI,boss.whoAmI,boss.type,1,view.Position);
            }
            if(coupledSmallEyes && Main.GameUpdateCount%6==0)
                for(int i=0;i<smallEyes.Count;i++)
                {
                    SmallEye next=smallEyeResponse.At(EyeReference(LastDecision.NextAngle),i);
                    Projectile p=Main.projectile[smallEyes[i].Slot];
                    if(next.State<=2 && next.Remaining>0)
                        smallEyeResponseAudit.Expect(Main.GameUpdateCount,p.whoAmI,ProjectileBirth.Of(p),p.type,1,next.Position);
                }
            if(coupledDirectDash && Main.GameUpdateCount%6==0)
                directDashAudit.Expect(Main.GameUpdateCount,boss.whoAmI,boss.whoAmI,boss.type,1,
                    directDashResponse.At(DashReference(LastDecision.NextAngle)).Position);
            if(ritual!=null && Main.GameUpdateCount%6==0)
            {
                NVector? nextArena=coupledToss?tossResponse.At(TossReference(LastDecision.NextAngle)).Arena
                    :coupledSword?swordResponse.At(SwordReference(LastDecision.NextAngle)).Arena
                    :coupledDirectDash?directDashResponse.At(DashReference(LastDecision.NextAngle)).Arena:null;
                if(nextArena is NVector center)ritualResponseAudit.Expect(Main.GameUpdateCount,ritual.whoAmI,
                    ProjectileBirth.Of(ritual),ritual.type,1,center);
            }
            if(fixedBossKnown>0 && Main.GameUpdateCount%6==0)
                foreach(int horizon in auditHorizons)
                    if(horizon<=fixedBossKnown)fixedBossAudit.Expect(Main.GameUpdateCount,boss.whoAmI,boss.whoAmI,boss.type,horizon,fixedBossPath[horizon]);
            if (coupledWorms && Main.GameUpdateCount % 6 == 0)
                foreach (Projectile p in wormParts)
                    wormResponseAudit.Expect(Main.GameUpdateCount, p.whoAmI, ProjectileBirth.Of(p), p.type, 1,
                        wormResponse.Position(LastDecision.NextAngle, p.whoAmI));
            before = actual; expected = LastDecision.Next; previous = LastDecision.Move;
            havePrevious = true;
            TotalMilliseconds = totalWatch.Elapsed.TotalMilliseconds;
            return previous;
        }

        void BuildThreats()
        {
            ritualResponseAudit.Observe(Main.GameUpdateCount,index=>{
                Projectile p=Main.projectile[index];
                return p.active?new ProjectileObservation(ProjectileBirth.Of(p),p.type,N(p.Center),0):null;
            });
            fixedBossAudit.Observe(Main.GameUpdateCount,index=>{
                NPC p=Main.npc[index];
                return p.active?new ProjectileObservation(p.whoAmI,p.type,N(p.Center),0):null;
            });
            directDashAudit.Observe(Main.GameUpdateCount,index=>{
                NPC p=Main.npc[index];
                return p.active?new ProjectileObservation(p.whoAmI,p.type,N(p.Center),0):null;
            });
            smallEyeResponseAudit.Observe(Main.GameUpdateCount,index=>{
                Projectile p=Main.projectile[index];
                return p.active ? new ProjectileObservation(ProjectileBirth.Of(p),p.type,N(p.Center),0) : null;
            });
            tossBossAudit.Observe(Main.GameUpdateCount,index=>{
                NPC npc=Main.npc[index];
                return npc.active?new ProjectileObservation(npc.whoAmI,npc.type,N(npc.Center),npc.rotation):null;
            });
            swordResponseAudit.Observe(Main.GameUpdateCount,index=>{
                Projectile p=Main.projectile[index];
                return p.active?new ProjectileObservation(ProjectileBirth.Of(p),p.type,N(p.Center),p.rotation):null;
            });
            wormResponseAudit.Observe(Main.GameUpdateCount, index => {
                Projectile p = Main.projectile[index];
                return p.active ? new ProjectileObservation(ProjectileBirth.Of(p), p.type, N(p.Center), 0) : null;
            });
            eyeBirthAudit.Observe(Main.GameUpdateCount, parentIndex => {
                Projectile parent = Main.projectile[parentIndex];
                if (!parent.active) return null;
                foreach (Projectile ray in Main.ActiveProjectiles)
                    if (ray.ModProjectile?.Name == "MutantTrueEyeDeathray" && ray.ai[1] == parentIndex
                        && ray.localAI[0] <= 1)
                        return new ProjectileObservation(ProjectileBirth.Of(parent), ray.type, N(ray.Center),
                            ray.velocity.ToRotation() - ray.ai[0] * ray.localAI[0]);
                return null;
            });
            forecastAudit.Observe(Main.GameUpdateCount, index => {
                Projectile p = Main.projectile[index];
                return p.active ? new ProjectileObservation(ProjectileBirth.Of(p), p.type, N(p.Center),
                    p.ModProjectile?.Name == "MutantSpearAim" ? p.rotation : p.velocity.ToRotation()) : null;
            });
            threats.Clear(); explosions.Clear(); futureOrbs.Clear(); futureBeams.Clear(); futureMarkRays.Clear(); futureBoxes.Clear(); futureShards.Clear();
            futureEyeRays.Clear(); warningPillars.Clear();
            boxSources.Clear(); beamSources.Clear(); leafParents.Clear(); trueEyeParents.Clear(); giantRay = null;giantRays.Clear();activeDashProjectile=null;
            stateDependentThreats = false;
            Array.Clear(bombOwners);
            wormParts.Clear(); wormNodes.Clear(); wormIdentities.Clear();
            smallEyes.Clear();
            // Build the identity lookup once, rather than scan the projectile pool for every leaf and rollout.
            foreach (Projectile p in Main.ActiveProjectiles)
            {
                if(p.ModProjectile?.Name=="MutantTrueEyeS" && p.ai[0]==player.whoAmI && p.ai[1]<=2)
                    smallEyes.Add(new(p.whoAmI,N(p.Center),N(p.velocity),(int)p.ai[1],p.localAI[0],p.localAI[1],p.timeLeft));
                if (p.ModProjectile?.Name == "MutantTrueEyeL")
                {
                    Type type = p.ModProjectile.GetType();
                    if (!pupilFields.TryGetValue(type, out FieldInfo field))
                        pupilFields[type] = field = type.GetField("localAI0", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field?.GetValue(p.ModProjectile) is float pupil)
                    {
                        Player target = p.ai[0] >= 0 && p.ai[0] < Main.maxPlayers ? Main.player[(int)p.ai[0]] : player;
                        PendingEyeRay? pending = LeftEyeForecast.Pending(N(p.Center), N(p.velocity), (int)p.ai[1],
                            p.localAI[0], pupil, N(target.Center), N(target.velocity), CombatPlanner.ForecastTicks);
                        if (pending is PendingEyeRay ray)
                        {
                            futureEyeRays.Add(ray);
                            if (Main.GameUpdateCount % 6 == 0
                                && ModContent.TryFind<ModProjectile>("FargowiltasSouls/MutantTrueEyeDeathray", out var child))
                                eyeBirthAudit.Expect(Main.GameUpdateCount, p.whoAmI, ProjectileBirth.Of(p), child.Type,
                                    ray.Born, ray.Origin, ray.Angle);
                        }
                    }
                }
                if (p.ModProjectile?.Name == "MutantTrueEyeR")
                {
                    if (!trueEyeBuffers.TryGetValue(p.whoAmI, out TrueEyeForecast path))
                        trueEyeBuffers[p.whoAmI] = path = new();
                    Player target = p.ai[0] >= 0 && p.ai[0] < Main.maxPlayers ? Main.player[(int)p.ai[0]] : player;
                    path.Update(N(p.Center), N(p.velocity), (int)p.ai[1], p.localAI[0], p.localAI[1],
                        N(target.Center), N(target.velocity));
                    trueEyeParents[(p.owner, p.identity)] = (p, path);
                }
                if(boss.ai[0]==20&&p.ModProjectile?.Name=="MutantEyeOfCthulhu"&&p.ai[0]==player.whoAmI)
                    eocRouting.Observe(p.ai[1],new(p.localAI[0],p.localAI[1]));
                if (p.ModProjectile?.Name is "MutantDestroyerHead" or "MutantDestroyerBody" or "MutantDestroyerTail")
                {
                    wormParts.Add(p); wormIdentities[(p.owner, p.identity)] = p;
                }
                if (p.ModProjectile?.Name == "MutantBomb" && p.owner >= 0 && p.owner < bombOwners.Length)
                    bombOwners[p.owner] = true;
                if (p.ModProjectile?.Name == "MutantMark2")
                {
                    Vector2[] forecast = Forecast(p, p.velocity, "MutantMark2");
                    NVector[] parent = GetParentBuffer(p.whoAmI);
                    for (int i = 0; i < parent.Length; i++) parent[i] = N(forecast[i]);
                    leafParents[(p.owner, p.identity)] = (p, parent);
                }
            }
            coupledDirectDash=PilotWorld.IsMasochist && (Main.getGoodWorld&&boss.ai[0]==4 || boss.ai[0] is 13 or 14 or 15 or 21 or 22 or 23
                ||boss.ai[0] is 5 or 6 && boss.ai[3]>0) && smallEyes.Count==0 && giantRay==null;
            wormParts.Sort((a, b) => a.whoAmI.CompareTo(b.whoAmI));
            foreach (Projectile p in wormParts)
            {
                bool head = p.ModProjectile.Name == "MutantDestroyerHead";
                int parent = !head && wormIdentities.TryGetValue((p.owner, (int)p.ai[0]), out Projectile predecessor)
                    ? predecessor.whoAmI : -1;
                wormNodes.Add(new(p.whoAmI, parent, N(p.Center), N(p.velocity), p.rotation, head, p.localAI[1], p.ai[1], p.width));
            }
            if (wormNodes.Count > 0) wormForecast.Update(wormNodes, N(player.Center), N(player.velocity));
            ulong now = Main.GameUpdateCount;
            foreach (Projectile p in Main.ActiveProjectiles)
            {
                if (!p.hostile || p.damage <= 0) continue;
                string name = p.ModProjectile?.Name ?? "";
                if(name=="MutantSpearDash"&&p.ai[0]==boss.whoAmI)activeDashProjectile=p;
                // A dropping pillar is still alpha-fading, so CanDamage rejects it below
                // and it never enters threats. Keep it for the timeline layer instead.
                if (name == "MutantPillar" && p.alpha > 0) warningPillars.Add(p);
                if (name == "MutantGiantDeathray2")
                {
                    giantRay = p; giantRays.Add(p); beamSources[p.whoAmI] = p.type;
                    continue;
                }
                if (name is "MoonLordMoonBlast" or "MoonLordSunBlast")
                {
                    float reach = 850 + 40 * CombatPlanner.ForecastTicks;
                    if (Vector2.DistanceSquared(p.Center, player.Center) < reach * reach)
                    {
                        NVector half = p.localAI[1] == 0 ? new(140) : new(p.width / 2f, p.height / 2f);
                        int age = p.frame * 2 + p.frameCounter;
                        boxSources[p.whoAmI] = (p.type, new(N(p.Center), half, Math.Max(1, 6 - age), 9 - age));
                        MoonBlast.Forecast(futureBoxes, N(p.Center), half, p.ai[0], (int)p.ai[1],
                            age, (int)p.localAI[0], CombatPlanner.ForecastTicks);
                    }
                    continue;
                }
                if (name == "MutantMark1" && p.timeLeft + 4 <= CombatPlanner.ForecastTicks
                    && p.velocity.LengthSquared() > .001f)
                    futureMarkRays.Add(new(N(p.Center + p.velocity * p.timeLeft), N(Vector2.Normalize(p.velocity)),
                        p.timeLeft + 4, p.timeLeft + 55));
                if (name == "MutantSpearThrown" && PilotWorld.IsMasochist)
                    SpearTrail.Forecast(futureOrbs, N(p.Center), N(p.velocity), p.localAI[0],
                        p.extraUpdates + 1, Math.Min(CombatPlanner.ForecastTicks, p.timeLeft / (p.extraUpdates + 1)));
                if (!coupledDirectDash && name == "MutantSpearDash" && PilotWorld.IsMasochist && boss.ai[0] is 6 or 15 or 23)
                    SpearTrail.ForecastDash(futureOrbs, N(p.Center), N(boss.velocity), p.localAI[0], p.ai[1] == -2,
                        Math.Max(0, 30 - (int)boss.ai[1]));
                bool ray = name.Contains("Deathray", StringComparison.OrdinalIgnoreCase)
                    || p.aiStyle == ProjAIStyleID.ThickLaser;
                bool custom = false;
                if (p.ModProjectile != null)
                {
                    Type type = p.ModProjectile.GetType();
                    if (!customShapes.TryGetValue(type, out custom))
                    {
                        custom = type.GetMethod("Colliding")?.DeclaringType != typeof(ModProjectile);
                        customShapes[type] = custom;
                    }
                }
                bool lance = p.type == ProjectileID.FairyQueenLance;
                // Vanilla 919 uses a +/-40px line in Projectile.Colliding, despite
                // its 8x8 entity bounds. Do not reject that line by the tiny box.
                bool longShape = ray || lance || (custom && name is not "MutantBomb" and not "MutantBombSmall"
                    and not "MutantScythe1" and not "MutantScythe2")
                    || name.Contains("Ritual", StringComparison.OrdinalIgnoreCase);
                Vector2 speed = ray ? Vector2.Zero : p.velocity * (p.extraUpdates + 1);
                float turn = 0, angle = p.velocity.ToRotation();
                long generation = ProjectileBirth.Of(p);
                if (tracks.TryGetValue(p.whoAmI, out Track track) && track.Generation == generation
                    && track.Type == p.type && now > track.Tick && now - track.Tick <= 3)
                {
                    float dt = now - track.Tick;
                    Vector2 measured = (p.Center - track.Position) / dt;
                    if (measured.LengthSquared() < 40000) speed = measured;
                    if (ray) turn = Math.Clamp(MathHelper.WrapAngle(angle - track.Angle) / dt, -.06f, .06f);
                }
                tracks[p.whoAmI] = new(generation, p.type, p.Center, angle, now);
                if (name == "MutantTrueEyeDeathray") turn = p.ai[0];
                float[] angles = null;
                if (name == "MutantDeathray3")
                {
                    if (!rayAngles.TryGetValue(p.whoAmI, out angles))
                        rayAngles[p.whoAmI] = angles = new float[CombatPlanner.ForecastTicks + 1];
                    ProjectileKinematics.DecayingRayAngles(angles, angle, p.ai[0], p.localAI[0], PilotWorld.IsMasochist);
                }
                // The P1 ray arms after its scale reaches .7. Waiting for CanDamage=true
                // leaves no reaction time: forecast the existing warning ray before it arms.
                int armAge = name == "MutantDeathray2" ? 24 : name == "MutantDeathray1" ? 5 : 0;
                bool warmingRay = armAge > 0 && p.localAI[0] < armAge;
                bool smallBomb = name == "MutantBombSmall";
                bool ritual = name == "MutantRitual";
                bool sanctuary = name == "MutantFishronRitual" && boss.ai[0] == 34;
                if (ProjectileLoader.CanDamage(p) == false && !warmingRay && !smallBomb && !ritual && !lance && !sanctuary) continue;
                float maximumSpeed = name is "MutantScythe1" or "MutantScythe2"
                    ? Math.Max(1, p.velocity.Length()) * MathF.Pow(1 + Math.Max(0, p.ai[0]), CombatPlanner.ForecastTicks)
                    : lance ? 40 : speed.Length();
                if ((!longShape || lance) && Vector2.Distance(p.Center, player.Center)
                    > 850 + maximumSpeed * CombatPlanner.ForecastTicks + Math.Max(p.width, p.height)) continue;
                Vector2[] centers = Forecast(p, speed, name);
                int remaining = Math.Max(1, (p.timeLeft + p.extraUpdates) / (p.extraUpdates + 1));
                if (name == "MutantSpearDash") remaining = Math.Min(remaining, Math.Max(0, 30 - (int)boss.ai[1]));
                // Its AI refreshes timeLeft to 2 while the parent is alive.
                if (ritual) remaining = CombatPlanner.ForecastTicks;
                if (name == "MutantPillar" && p.alpha == 0 && remaining <= CombatPlanner.ForecastTicks)
                    PillarBurst.Forecast(futureShards, N(centers[remaining]), p.velocity.ToRotation(), remaining, PilotWorld.IsMasochist);
                if (name == "MutantDeathray2") remaining = Math.Min(remaining, Math.Max(0, 156 - (int)p.localAI[0]));
                if (name == "MutantDeathray1") remaining = Math.Min(remaining, Math.Max(0, 55 - (int)p.localAI[0]));
                if (name == "MutantTrueEyeDeathray") remaining = Math.Min(remaining, Math.Max(0, 89 - (int)p.localAI[0]));
                if (lance) remaining = Math.Min(remaining, Math.Max(0, 359 - (int)p.localAI[0]));
                if (name is "MutantRetirang" or "MutantSpazmarang")
                    remaining = Math.Min(remaining, Math.Max(0, (int)(p.ai[1] - p.localAI[0])));
                if (smallBomb) remaining = Math.Min(remaining,
                    (Main.projFrames[p.type] - p.frame) * 3 - p.frameCounter);
                bool sword = name == "MutantSword";
                int shortKind = name == "MutantDeathray1" ? 1 : name == "MutantDeathray2" ? 2 : 0;
                float[] scales = null;
                if (shortKind != 0)
                {
                    if (!shortRayScales.TryGetValue(p.whoAmI, out scales))
                        shortRayScales[p.whoAmI] = scales = new float[CombatPlanner.ForecastTicks + 1];
                    for (int t = 0; t < scales.Length; t++) scales[t] = ShortRayTiming.Scale(shortKind, p.localAI[0] + t);
                }
                if (sword && p.ai[0] >= 0 && p.ai[0] < Main.maxNPCs)
                {
                    NPC parent = Main.npc[(int)p.ai[0]];
                    int state = (int)parent.ai[0];
                    bool preparing = state == 45 || (state == 9 && parent.localAI[2] == 1);
                    int untilKill = preparing ? Math.Max(0, 91 - (int)parent.ai[1]) + 21
                        : (state == 46 || (state == 9 && parent.localAI[2] == 2)) ? 21 - (int)parent.ai[1] : 1;
                    remaining = Math.Min(remaining, Math.Max(1, untilKill));
                }
                threats.Add(new(p, p.type, generation, speed, turn, longShape, remaining,
                    warmingRay ? Math.Max(1, armAge - (int)p.localAI[0]) : lance ? ProjectileKinematics.LanceArms(p.localAI[0])
                    : sanctuary ? ProjectileKinematics.SanctuaryArms(p.alpha, boss.ai[1],
                        p.owner >= 0 && p.owner < bombOwners.Length && bombOwners[p.owner]) : 0,
                    warmingRay, smallBomb, sword, centers, angles,
                    BeamBroadPhaseCheck.Enabled && name is ("MutantDeathray1" or "MutantDeathray2" or "MutantDeathray3" or "MutantTrueEyeDeathray")
                        ? new BeamBroadPhase(N(p.Center), N(p.velocity), shortKind != 0 ? 3000 : p.localAI[1], p.scale, new(p.width / 2f, p.height / 2f)) : null,
                    shortKind, scales));
                stateDependentThreats |= name == "MutantSpearAim";
                if (now % 6 == 0)
                    foreach (int horizon in auditHorizons)
                        if (horizon <= remaining && (name != "MutantSpearAim" || horizon != 1))
                            forecastAudit.Expect(now, p.whoAmI, generation, p.type, horizon, N(centers[horizon]),
                                ray ? angles == null ? angle + turn * horizon : angles[horizon] : null);
                if (name == "MutantSphereSmall" && remaining <= CombatPlanner.ForecastTicks)
                    explosions.Add((centers[remaining], remaining));
                if (sword && !coupledSword && remaining <= CombatPlanner.ForecastTicks)
                    explosions.Add((centers[remaining], remaining));
            }
            // Timeline injection. These three attacks telegraph for longer than the
            // 48-tick search horizon, yet the projectile is only readable once it may
            // already damage. Emitting them here puts them in the same futureBeams layer
            // the missile forecasts use, so Danger() treats them identically.
            int timelineState = (int)boss.ai[0];
            if (!PilotTuning.Current.RouteSpears || !PilotTuning.Current.TossOrbit)
                timeline.EmitSpearToss(futureBeams, CombatPlanner.ForecastTicks, timelineState,
                    boss.ai[1], boss.ai[2], N(boss.Center), N(player.Center));
            timeline.EmitEyeDash(futureBeams, CombatPlanner.ForecastTicks, timelineState,
                boss.ai[1], N(boss.Center), N(player.Center));
            foreach (Projectile pillar in warningPillars)
                timeline.EmitPillarDash(futureBeams, CombatPlanner.ForecastTicks, N(pillar.Center),
                    pillar.alpha, N(player.Center), pillar.whoAmI, ProjectileBirth.Of(pillar));
            pendingLockedSpear = boss.localAI[3] > 0 && !(Main.getGoodWorld&&boss.ai[0]==0)
                ? LockedTossForecast.Read((int)boss.ai[0], boss.ai[1], boss.ai[2], boss.ai[3],
                    boss.localAI[0], N(boss.Center), N(boss.velocity)) : null;
            if (pendingLockedSpear is PendingSpear pendingToss && PilotWorld.IsMasochist)
            {
                SpearTrail.Forecast(futureOrbs, pendingToss.Origin, pendingToss.Velocity, 0, 2,
                    CombatPlanner.ForecastTicks - pendingToss.Born + 1, pendingToss.Born - 1, pendingToss.Padding);
                NVector axis = NVector.Normalize(pendingToss.Velocity);
                futureBeams.Add(new(pendingToss.Origin, axis, pendingToss.Born + 23, pendingToss.Born + 155, 30 + pendingToss.Padding));
                futureBeams.Add(new(pendingToss.Origin, -axis, pendingToss.Born + 23, pendingToss.Born + 155, 30 + pendingToss.Padding));
            }
            sphereBirths.Update((int)boss.ai[0], boss.ai[1], boss.ai[2], boss.ai[3], boss.localAI[1],
                boss.spriteDirection, N(boss.Center), PilotWorld.IsMasochist, boss.life <= 1,legendaryHyper:legendaryHyper);
            markBirths.Update((int)boss.ai[0], boss.ai[1], boss.ai[2], boss.ai[3], boss.localAI[0],
                N(boss.Center), PilotWorld.IsMasochist,legendaryHyper);
            eyeBirths.Update((int)boss.ai[0], boss.ai[1], boss.ai[2], boss.ai[3], boss.localAI[2],
                boss.localAI[0], N(boss.Center), PilotWorld.IsMasochist,legendaryHyper);
            fanBirths.Update((int)boss.ai[0], boss.ai[1], boss.ai[2], boss.ai[3], N(boss.Center), PilotWorld.IsMasochist,legendaryHyper);
            if (boss.ai[0] == 8)
            {
                voidRayCenter = boss.Center; voidLastTick = (long)Main.GameUpdateCount;
                voidLastAngle = boss.ai[2]; voidLastStep = boss.ai[3];
                voidRayHoldUntil = (int)Main.GameUpdateCount + 152;
            }
            bool residualVoid = VoidInnerRouting.Retain((int)boss.ai[0], boss.localAI[2],
                (long)Main.GameUpdateCount, voidRayHoldUntil, N(boss.Center), N(voidRayCenter));
            p3VoidRouting.Update((int)boss.ai[0],PilotWorld.IsMasochist,N(player.Center),N(boss.Center),
                boss.ai[2],boss.ai[3],boss.localAI[0],boss.ai[1],(long)Main.GameUpdateCount);
            bool useOuter = PilotTuning.Current.OuterP1Void && (boss.ai[0] == 8 || residualVoid);
            outerVoid.Update(useOuter, boss.ai[0] == 8, N(player.Center), N(boss.Center), boss.ai[2], boss.ai[3], PilotWorld.IsMasochist);
            trackingVoid = !p3VoidRouting.Active && (boss.ai[0] == -2 || !useOuter && (boss.ai[0] == 8 || residualVoid));
            voidMarks.Clear(); voidCurved.Clear();
            if (trackingVoid)
            {
                foreach (var threat in threats)
                    if (threat.Projectile.ModProjectile?.Name == "MutantMark1")
                        voidMarks.Add(new(N(threat.Projectile.Center), N(threat.Projectile.velocity), 0, threat.RemainingTicks, 0, threat.Projectile.width / 2f));
                    else if (residualVoid && threat.Projectile.ModProjectile?.Name == "MutantEye")
                        voidMarks.Add(new(N(threat.Projectile.Center), N(threat.Projectile.velocity), 0, threat.RemainingTicks, 0, threat.Projectile.width / 2f));
                    else if (residualVoid && threat.Projectile.ModProjectile?.Name == "MutantSphereRing")
                    {
                        NVector[] path = GetParentBuffer(threat.Projectile.whoAmI);
                        for (int t = 0; t < path.Length; t++) path[t] = N(threat.Centers[t]);
                        voidCurved.Add(new(path, 1, Math.Min(CombatPlanner.ForecastTicks, threat.RemainingTicks), threat.Projectile.width / 2f));
                    }
                foreach (var birth in markBirths.Events)
                    voidMarks.Add(new(N(boss.Center), birth.Velocity, birth.Born - 1, birth.Born + 89, birth.Velocity.Length()));
                if (residualVoid)
                {
                    sphereBirths.AppendGuides(voidCurved);
                    foreach (var birth in eyeBirths.Events)
                        voidMarks.Add(new(N(boss.Center), birth.Velocity, birth.Born - 1, birth.Born + 299, 7, 6));
                }
            }
            int voidPeriod = boss.ai[0] == -2 ? 2 : PilotWorld.IsMasochist ? 4 : 6;
            float voidAngle = residualVoid ? voidLastAngle + voidLastStep / voidPeriod * ((long)Main.GameUpdateCount - voidLastTick) : boss.ai[2];
            voidRouting.Update(trackingVoid, (long)Main.GameUpdateCount, N(player.Center), N(boss.Center),
                voidAngle, residualVoid ? voidLastStep : boss.ai[3], projectileHalf + new NVector(inflation), voidMarks,
                boss.ai[0] == -2 ? 85 : 100, voidPeriod, voidCurved, residualVoid, N(player.velocity));
            if (trackingVoid)
                for (int t = 0; t < voidGoals.Length; t++) voidGoals[t] = voidRouting.GoalAt(t);
            coupledWorms = PilotTuning.Current.CoupledWorms && wormNodes.Count > 0 && giantRay == null;
            if (coupledWorms) wormResponse.Reset(wormNodes);
            BuildThreatBuckets();
            NVector queryHalf = new((int)Math.Ceiling(projectileHalf.X)+(int)inflation,
                (int)Math.Ceiling(projectileHalf.Y)+(int)inflation);
            futureIndex.Clear(queryHalf, clearanceReserve);
            forecastExplosions.Clear();
            foreach (var blast in explosions) forecastExplosions.Add((N(blast.Center), blast.At));
            futureIndex.WorkerCount=PilotTuning.Current.ForecastWorkers;
            futureIndex.Build(futureBoxes, futureShards, futureOrbs, forecastExplosions);
            if (tracks.Count > Main.maxProjectiles * 2) tracks.Clear();
        }

        void BuildThreatBuckets()
        {
            threatBuckets.Clear(); globalThreats.Clear();
            NVector playerHalf = projectileHalf + new NVector((int)inflation + 4);
            for (int index = 0; index < threats.Count; index++)
            {
                Threat threat = threats[index]; Projectile p = threat.Projectile;
                if(p.ModProjectile?.Name=="MutantSpearSpin"&&p.ai[0]==boss.whoAmI){globalThreats.Add(index);continue;}
                if (coupledWorms && wormResponse.Contains(p.whoAmI)) { globalThreats.Add(index); continue; }
                bool lance = p.type == ProjectileID.FairyQueenLance;
                if (threat.LongShape && !lance) { globalThreats.Add(index); continue; }
                NVector extent = playerHalf + (lance ? new NVector(44) : new NVector(p.width / 2f, p.height / 2f));
                if (!threat.LongShape) extent += new NVector(clearanceReserve);
                // Preserve the existing soft clearance cost around worm heads too.
                if (p.ModProjectile?.Name == "MutantDestroyerHead")
                    extent = NVector.Max(extent, new NVector(PilotTuning.Current.HeadClearance));
                int first = Math.Max(1, threat.ArmsIn), last = Math.Min(CombatPlanner.ForecastTicks, threat.RemainingTicks);
                bool finite = true;
                for (int t = first; t <= last; t++)
                {
                    NVector a = N(threat.Centers[t - 1]), b = N(threat.Centers[t]);
                    if (!float.IsFinite(a.X) || !float.IsFinite(a.Y) || !float.IsFinite(b.X) || !float.IsFinite(b.Y))
                    { finite = false; break; }
                    NVector span = NVector.Abs(b - a) + extent * 2;
                    if (span.X > 4096 || span.Y > 4096) { finite = false; break; }
                }
                // Unknown/nonfinite shapes keep the previous unfiltered path.
                if (!finite) { globalThreats.Add(index); continue; }
                for (int t = first; t <= last; t++)
                {
                    NVector a = N(threat.Centers[t - 1]), b = N(threat.Centers[t]);
                    threatBuckets.Add(index, t, NVector.Min(a, b) - extent, NVector.Max(a, b) + extent);
                }
            }
        }

        Vector2[] Forecast(Projectile p, Vector2 measured, string name)
        {
            if (!forecasts.TryGetValue(p.whoAmI, out Vector2[] result))
                forecasts[p.whoAmI] = result = new Vector2[CombatPlanner.ForecastTicks + 1];
            result[0] = p.Center;
            if (name == "MutantTrueEyeSphere")
            {
                trueEyeParents.TryGetValue((p.owner, (int)p.ai[0]), out var parent);
                NVector[] path = GetParentBuffer(p.whoAmI);
                TrueEyeForecast.Sphere(path, N(p.Center), N(p.velocity), p.timeLeft,
                    parent.Path, parent.Parent != null && parent.Parent.whoAmI < p.whoAmI);
                for (int t = 1; t < result.Length; t++) result[t] = X(path[t]);
                return result;
            }
            if (name == "MutantEyeHoming")
            {
                NVector[] path = GetParentBuffer(p.whoAmI);
                ProjectileKinematics.HomingEye(path, N(p.Center), N(p.velocity), p.ai[1], (int)boss.ai[0],
                    PilotWorld.IsMasochist, Main.getGoodWorld, N(player.Center), N(player.velocity));
                for (int t = 1; t < result.Length; t++) result[t] = X(path[t]);
                return result;
            }
            if (name == "MutantEyeOfCthulhu" && p.ai[1] >= 120)
            {
                NVector[] path = GetParentBuffer(p.whoAmI);
                EocDashForecast.Forecast(path, N(p.Center), N(p.velocity), p.ai[1], new(p.localAI[0],p.localAI[1]));
                for (int t = 1; t < result.Length; t++) result[t] = X(path[t]);
                return result;
            }
            if (name is "MutantDestroyerHead" or "MutantDestroyerBody" or "MutantDestroyerTail")
            {
                NVector[] path = wormForecast.Path(p.whoAmI);
                for (int t = 1; t < result.Length; t++) result[t] = X(path[t]);
                return result;
            }
            if (name is "MutantSpearAim" or "MutantSpearDash")
            {
                Vector2 offset = name == "MutantSpearDash" ? boss.velocity + boss.velocity.SafeNormalize(Vector2.UnitY) : Vector2.Zero;
                for (int t = 1; t < result.Length; t++) result[t] = boss.Center + boss.velocity * t + offset;
                return result;
            }
            if (name is "MutantRetirang" or "MutantSpazmarang")
            {
                NVector[] path = GetParentBuffer(p.whoAmI);
                ProjectileKinematics.Boomerang(path, N(p.Center), N(p.velocity), p.localAI[1], p.ai[0]);
                for (int t = 1; t < result.Length; t++) result[t] = X(path[t]);
                return result;
            }
            if (name == "MutantFishronRitual")
            {
                for (int t = 1; t < result.Length; t++)
                    result[t] = boss.Center + boss.velocity * t - new Vector2(0, 100);
                return result;
            }
            if (name == "MutantNuke")
            {
                NVector[] path = GetParentBuffer(p.whoAmI);
                ProjectileKinematics.Ballistic(path, N(p.Center), N(p.velocity), p.ai[0],
                    legendaryHyper>=0?(int)p.localAI[2]:-1);
                for (int t = 1; t < result.Length; t++) result[t] = X(path[t]);
                return result;
            }
            if (p.type == ProjectileID.FairyQueenLance)
            {
                NVector[] path = GetParentBuffer(p.whoAmI);
                ProjectileKinematics.Lance(path, N(p.Center), N(p.velocity), p.localAI[0], p.ai[0]);
                for (int t = 1; t < result.Length; t++) result[t] = X(path[t]);
                return result;
            }
            if (name is "MutantFragment" or "MutantScythe1" or "MutantScythe2")
            {
                NVector[] path = GetParentBuffer(p.whoAmI);
                if (name == "MutantFragment") ProjectileKinematics.Decay(path, N(p.Center), N(p.velocity), .985f);
                else ProjectileKinematics.Scythe(path, N(p.Center), N(p.velocity), p.timeLeft, p.ai[0], p.ai[1]);
                for (int t = 1; t < result.Length; t++) result[t] = X(path[t]);
                return result;
            }
            if (name == "MutantRitual")
            {
                int state = (int)boss.ai[0];
                for (int t = 1; t < result.Length; t++)
                {
                    Vector2 center = result[t - 1];
                    if (state == 19 || (state == 49 && boss.ai[1] + t >= 30)) { result[t] = center; continue; }
                    Vector2 target = state == 49 ? player.Center + player.velocity * Math.Min(t, 12)
                        : t<=fixedBossKnown ? X(fixedBossPath[t]) : boss.Center + boss.velocity * t;
                    float divisor = state == 49 ? 10 : state == 36 ? 20 : state is 22 or 23 or 25 ? 40 : 60;
                    result[t] = center + (target - center) / divisor;
                }
                return result;
            }
            if (name == "MutantMark2")
            {
                Vector2 markerVelocity = p.velocity;
                float stop = p.ai[0], launch = p.ai[1];
                for (int t = 1; t <= CombatPlanner.ForecastTicks; t++)
                {
                    Vector2 position = result[t - 1];
                    if (--stop == 0) markerVelocity = Vector2.Zero;
                    if (--launch == 0)
                        markerVelocity = (player.Center + player.velocity * Math.Min(t, 12) - position)
                            .SafeNormalize(Vector2.UnitY) * 15;
                    result[t] = position + markerVelocity;
                }
                return result;
            }
            if (name == "MutantCrystalLeaf")
            {
                if (leafParents.TryGetValue((p.owner, (int)p.ai[0]), out var parent))
                {
                    NVector[] orbit = GetParentBuffer(p.whoAmI);
                    orbit[0] = N(p.Center);
                    LeafOrbit.Forecast(orbit, parent.Centers, p.ai[1], parent.Parent.ai[1]);
                    for (int t = 1; t < result.Length; t++) result[t] = X(orbit[t]);
                }
                else for (int t = 1; t < result.Length; t++) result[t] = p.Center + p.velocity * t;
                return result;
            }
            if (name == "MutantSword" && p.ai[0] >= 0 && p.ai[0] < Main.maxNPCs)
            {
                NPC parent = Main.npc[(int)p.ai[0]];
                int state = (int)parent.ai[0];
                bool preparing = state == 45 || (state == 9 && parent.localAI[2] == 1);
                bool swinging = state == 46 || (state == 9 && parent.localAI[2] == 2);
                int wait = preparing ? Math.Max(0, 91 - (int)parent.ai[1]) : 0;
                float startAngle = p.localAI[0] == 0 ? (p.Center - parent.Center).ToRotation() : p.localAI[1];
                Vector2 launch = (player.Center + player.velocity * wait - new Vector2(300 * parent.ai[2], 0)
                    - parent.Center) / 20;
                for (int t = 1; t <= CombatPlanner.ForecastTicks; t++)
                {
                    if (!preparing && !swinging) { result[t] = p.Center; continue; }
                    int spinTicks = Math.Max(0, t - wait);
                    Vector2 center = parent.Center;
                    if (preparing && t >= wait) center += launch;
                    result[t] = X(SwordOrbit.At(N(center), N(preparing ? launch : parent.velocity), p.ai[1],
                        startAngle + parent.ai[3], parent.ai[2], spinTicks));
                }
                return result;
            }
            Vector2 velocity = name is "MutantDestroyerHead" or "MutantSphereRing" or "MutantSphereSmall"
                ? p.velocity * (p.extraUpdates + 1) : measured;
            for (int t = 1; t <= CombatPlanner.ForecastTicks; t++)
            {
                Vector2 position = result[t - 1];
                Vector2 target = player.Center + player.velocity * Math.Min(t, 12);
                if (name == "MutantDestroyerHead" && p.localAI[1] + t > 60)
                {
                    float speed = 10 * p.ai[1], lerp = p.ai[1] / 25;
                    if (Vector2.Distance(position, target) > 700) { speed *= 2; lerp *= 2; }
                    velocity = Vector2.Lerp(velocity, (target - position).SafeNormalize(Vector2.UnitY) * speed, lerp);
                }
                else if (name == "MutantSphereRing" && Math.Abs(p.ai[0]) > .001f)
                    velocity = velocity.RotatedBy(p.ai[1] / (MathHelper.TwoPi * p.ai[0] * Math.Max(1, p.localAI[0] + t)));
                else if (name == "MutantSphereSmall" && p.ai[0] >= 0 && p.ai[1] + t > 20)
                    velocity = Vector2.Lerp(velocity, (target - position).SafeNormalize(Vector2.UnitY) * 5, .05f);
                result[t] = position + velocity;
            }
            return result;
        }

        readonly Dictionary<int, NVector[]> parentBuffers = new();
        NVector[] GetParentBuffer(int id)
        {
            if (!parentBuffers.TryGetValue(id, out NVector[] buffer))
                parentBuffers[id] = buffer = new NVector[CombatPlanner.ForecastTicks + 1];
            return buffer;
        }

        float AdvanceRay(float angle, MotionState state, int tick)
        {
            if(boss.ai[0]!=-5)return angle;
            for(int update=0;update<bossAiUpdates[tick];update++)
                angle=FinalSparkForecast.Chase(angle,N(boss.Center+boss.velocity*tick),state.Position,
                    PilotWorld.IsMasochist&&boss.ai[2]+bossAiElapsed[tick-1]+update+1>=420);
            return angle;
        }
        float TossReference(float reference) => coupledWorms ? wormTossReferences[(int)reference]
            : coupledSmallEyes ? eyeTossReferences[(int)reference] : reference;
        float SwordReference(float reference) => coupledWorms ? wormSwordReferences[(int)reference]
            : coupledSmallEyes ? eyeSwordReferences[(int)reference] : reference;
        float EyeReference(float reference) => coupledWorms ? wormEyeReferences[(int)reference] : reference;
        float DashReference(float reference) => coupledWorms ? wormDashReferences[(int)reference] : reference;
        float DangerDirectDash(MotionState state,int tick,float reference)
        {
            queriedDirectDash=(int)reference;
            try{return DangerCore(state,tick,float.NaN,null);}
            finally{queriedDirectDash=-1;}
        }
        float AdvanceSmallEyesResponse(float reference,MotionState state,int tick)
        {
            float next=smallEyeResponse.Advance(reference,state,tick);
            if((int)next==eyeTossReferences.Count)
            {
                eyeTossReferences.Add(coupledToss?tossResponse.Advance(TossReference(reference),state,tick):0);
                eyeSwordReferences.Add(coupledSword?swordResponse.Advance(SwordReference(reference),state,tick):0);
            }
            return next;
        }
        float DangerSmallEyesResponse(MotionState state,int tick,float reference)
        {
            queriedSword=coupledSword?(int)SwordReference(reference):-1;
            try
            {
                float cost=coupledToss?DangerToss(state,tick,TossReference(reference)):DangerCore(state,tick,float.NaN,null);
                if(smallEyeResponse.Touches(reference,state.Position,projectileHalf+new NVector(inflation),tick))cost+=1000000;
                return cost;
            }
            finally {queriedSword=-1;}
        }
        float AdvanceWormResponse(float reference, MotionState state, int tick)
        {
            float next = wormResponse.Advance(reference, state, tick);
            if ((int)next == wormTossReferences.Count)
            {
                wormTossReferences.Add(coupledToss ? tossResponse.Advance(TossReference(reference), state, tick) : 0);
                wormSwordReferences.Add(coupledSword ? swordResponse.Advance(wormSwordReferences[(int)reference],state,tick) : 0);
                wormEyeReferences.Add(coupledSmallEyes ? smallEyeResponse.Advance(EyeReference(reference),state,tick) : 0);
                wormDashReferences.Add(coupledDirectDash ? directDashResponse.Advance(DashReference(reference),state,tick) : 0);
            }
            return next;
        }
        float DangerWormResponse(MotionState state, int tick, float reference)
        {
            queriedWorm = (int)reference;
            queriedSword = coupledSword ? (int)wormSwordReferences[(int)reference] : -1;
            queriedDirectDash = coupledDirectDash ? (int)DashReference(reference) : -1;
            try
            {
                TossWorld? view = coupledToss ? tossResponse.At(TossReference(reference)) : null;
                float cost = DangerCore(state, tick, float.NaN, view is TossWorld toss && !toss.Closed ? toss : null);
                if (coupledToss && tossResponse.Touches(TossReference(reference), state.Position,
                    projectileHalf + new NVector(inflation), tick)) cost += 1000000;
                if(coupledSmallEyes && smallEyeResponse.Touches(EyeReference(reference),state.Position,
                    projectileHalf+new NVector(inflation),tick))cost+=1000000;
                return cost;
            }
            finally { queriedWorm = -1; queriedSword = -1; queriedDirectDash = -1; }
        }
        float DangerSword(MotionState state,int tick,float reference)
        {
            queriedSword=(int)reference;
            try {return DangerCore(state,tick,float.NaN,null);}
            finally {queriedSword=-1;}
        }
        float DangerPosition(NVector point, int tick) => Danger(new(point, NVector.Zero), tick, float.NaN);
        float DangerState(MotionState state, int tick) => Danger(state, tick, float.NaN);
        float DangerToss(MotionState state, int tick, float reference)
        {
            TossWorld view = tossResponse.At(reference);
            return DangerCore(state, tick, float.NaN, view.Closed ? null : view)
                + (tossResponse.Touches(reference, state.Position, projectileHalf + new NVector(inflation), tick) ? 1000000 : 0);
        }
        float Danger(MotionState state, int tick, float rayAngle) => DangerCore(state, tick, rayAngle, null);
        NVector? ResponseArena(TossWorld? tossView)
        {
            if(queriedDirectDash>=0)return directDashResponse.At(queriedDirectDash).Arena;
            if(queriedSword>=0)return swordResponse.At(queriedSword).Arena;
            return tossView?.Arena;
        }
        Vector2 ResponseBoss(int tick,TossWorld? tossView)
        {
            if(queriedDirectDash>=0)return X(directDashResponse.At(queriedDirectDash).Position);
            if(queriedSword>=0)return X(swordResponse.At(queriedSword).Position);
            if(tossView is TossWorld toss)return X(toss.Position);
            if(tick<=fixedBossKnown)return X(fixedBossPath[tick]);
            return boss.Center+boss.velocity*Math.Min(tick,10);
        }
        float DangerCore(MotionState state, int tick, float rayAngle, TossWorld? tossView)
        {
            NVector future = state.Position;
            Vector2 point = X(future);
            float cost = 0;
            if(queriedSword>=0 && swordResponse.Touches(queriedSword,future,projectileHalf+new NVector(inflation),tick))cost+=1000000;
            cost += worldRouting.BoundaryCost(future);
            int routingAttack = (int)boss.ai[0];
            if(PilotTuning.Current.ReserveSpearTransition && routingAttack==1)
            {
                // Okuu can finish at state 4 with no wind-up tick in between. This is
                // a bounded spacing preference, not an invented damaging hitbox.
                float intrusion=Math.Max(0,220-Vector2.Distance(point,boss.Center));
                cost+=Math.Min(50000,intrusion*intrusion*2);
            }
            if (routingAttack >= 0 && routingAttack != 33 && routingAttack != 34)
                cost += worldRouting.HeightCost(future);
            if (routingAttack is 3 or 32)
            {
                float distance = Vector2.Distance(point, boss.Center + boss.velocity * Math.Min(tick, 10));
                float diveReserve = Math.Max(0, Math.Clamp(PilotTuning.Current.DiveClearance, 0, 240) - distance);
                cost += Math.Min(50000, diveReserve * diveReserve / 4);
            }
            cost += voidRouting.Cost(future, tick);
            cost += outerVoid.Cost(future);
            cost += p3VoidRouting.Cost(future,tick);
            if (point.X < motion.WorldMinimum.X + 24 || point.X > motion.WorldMaximum.X - 24
                || point.Y < motion.WorldMinimum.Y + 24 || point.Y > Main.worldSurface * 16 - 160) cost += 1000000;
            NVector? responseArena=ResponseArena(tossView);
            Vector2 arenaAt = responseArena is NVector candidateArena?X(candidateArena):arenaPath == null ? arenaCenter : arenaPath[tick];
            float margin = arenaRadius - Vector2.Distance(point, arenaAt);
            if (margin < 70) cost += 1000000;
            else if (margin < 200) cost += (200 - margin) * 2;
            float reserve = Math.Clamp(PilotTuning.Current.BoundaryReserve, 200, Math.Min(800, arenaRadius * .6f));
            if (margin < reserve) cost += (reserve - margin) * (reserve - margin) / 10000;
            // Everything from here to the tile test below is damage this player can take. During a
            // Betsy dash FargoSoulsPlayer.ImmuneToDamage is true, so none of it can produce a Hurt
            // event; snapshot the cost and drop the whole hazard section on an immune tick. World
            // bounds, the arena margin and the tile test stay, because those still apply.
            float costBeforeHazards = cost;
            Vector2 bossAt = tossView is TossWorld view ? X(view.Position) : boss.Center + boss.velocity * Math.Min(tick, 10);
            if(tick<=fixedBossKnown)bossAt=X(fixedBossPath[tick]);
            if(queriedSword>=0)bossAt=X(swordResponse.At(queriedSword).Position);
            if(queriedDirectDash>=0)bossAt=X(directDashResponse.At(queriedDirectDash).Position);
            if (boss.damage > 0 && boss.ai[0] > -1 && Vector2.Distance(point, bossAt) < 70 + inflation) cost += 1000000;
            else if (boss.damage > 0 && tossView is TossWorld beforeMove
                && Vector2.Distance(point, X(beforeMove.Position - beforeMove.Velocity)) < 70 + inflation) cost += 1000000;
            if (boss.ai[0] == -5) cost += FinalSparkForecast.RadialCost(future, N(boss.Center),
                Math.Clamp(PilotTuning.Current.FinalSparkRadius + 120, 220, 600));
            // Random guardians converge on a known central vertical lane.
            // Reserve room before they arrive; this is a soft route preference,
            // not a claim that every point inside the cone contains a projectile.
            if (boss.ai[0] == 27 && boss.ai[2] != 0)
                cost += PilotTuning.Current.MechFanGap
                    ? MechFanRouting.GapCost(future, N(boss.Center), boss.ai[2], boss.ai[3], tick)
                    : ProjectileKinematics.GuardianLaneCost(future, N(boss.Center), boss.ai[2]);

            int halfX = (int)Math.Ceiling(projectileHalf.X) + (int)inflation;
            int halfY = (int)Math.Ceiling(projectileHalf.Y) + (int)inflation;
            if(boss.ai[0]==-6 && FinalSparkForecast.PendingDeathTouches(boss.ai[1],tick,N(boss.Center),
                future,new(halfX,halfY),PilotWorld.IsMasochist))cost+=1000000;
            if(queriedDirectDash>=0 && directDashResponse.Touches(queriedDirectDash,future,new(halfX,halfY),tick))cost+=1000000;
            if (sphereBirths.Touches(future, new NVector(halfX, halfY), tick)) cost += 1000000;
            if (markBirths.Touches(future, new NVector(halfX, halfY), tick)) cost += 1000000;
            if (eyeBirths.Touches(future, new NVector(halfX, halfY), tick)) cost += 1000000;
            foreach (FanRayPath path in fanBirths.Paths)
            {
                FanRayPose pose = path.Poses[tick];
                if (pose.Scale > 0 && NativeRayTouches(point, halfX, halfY, X(pose.Origin), X(pose.Direction), pose.Scale, pose.Length))
                    cost += 1000000;
            }
            if (pendingLockedSpear is PendingSpear pending && pending.Touches(future, new(halfX, halfY), tick)) cost += 1000000;
            if (giantRay != null && float.IsFinite(rayAngle))
            foreach (Projectile activeRay in giantRays)
            {
                int duration=boss.ai[0]==-7?255:0;
                float age=activeRay.localAI[0]+tick,previousAge=age-1;
                if(Main.getGoodWorld&&boss.ai[0]==-5&&boss.life>1)
                {
                    age=Math.Min(age,771);previousAge=Math.Min(previousAge,771);
                }
                float scale = FinalSparkForecast.Scale(age, PilotWorld.IsMasochist,duration);
                if (scale >= 5)
                {
                    NVector direction = new(MathF.Cos(rayAngle), MathF.Sin(rayAngle));
                    float previousScale = tick == 1 ? activeRay.scale
                        : FinalSparkForecast.Scale(previousAge, PilotWorld.IsMasochist,duration);
                    NVector origin = N(boss.Center + boss.velocity * tick) + direction * ((boss.ai[0]==-7?100:175) * previousScale / 10 + 1);
                    float length = 3000 + (activeRay.localAI[1] - 3000) * MathF.Pow(.5f, tick);
                    // Native lineWidth is full width with flat endpoints. Its
                    // separate 48px projectile box still damages at the origin.
                    // Six pixels per axis cover the real 5px origin jitter and rounding.
                    Vector2 start = X(origin), end = X(origin + direction * length);
                    var query = new Rectangle((int)point.X - halfX - 6, (int)point.Y - halfY - 6,
                        (halfX + 6) * 2, (halfY + 6) * 2);
                    var originBox = new Rectangle((int)(origin.X - 24), (int)(origin.Y - 24), 48, 48);
                    float collisionAt = 0;
                    if (originBox.Intersects(query) || Collision.CheckAABBvLineCollision(query.TopLeft(), query.Size(),
                        start, end, 22 * scale, ref collisionAt)) cost += 1000000;
                }
            }
            foreach (TimedBeam beam in futureBeams)
                if (beam.Intersects(future, new NVector(halfX, halfY), tick)) cost += 1000000;
            foreach (PendingVoidRay ray in futureMarkRays)
                if (tick >= ray.ArmsAt && tick <= ray.EndsAt
                    && NativeRayTouches(point, halfX, halfY, X(ray.Origin), X(ray.Direction), 1)) cost += 1000000;
            foreach (PendingEyeRay ray in futureEyeRays)
                if (ray.Intersects(future, new NVector(halfX, halfY), tick)) cost += 1000000;
            float beforeFuture = cost;
            futureIndex.Accumulate(ref cost, future, tick);
            if ((spatialQuerySerial + 1) % 512 == 0)
            {
                float reference = beforeFuture;
                AccumulateFutureFull(ref reference, future, halfX, halfY, tick);
                FutureAuditChecks++;
                if (Math.Abs(cost-reference) > .5f) { FutureAuditMismatches++; SpatialAuditMismatches++; }
            }
            bool auditQuery = ++spatialQuerySerial % 512 == 0;
            long sampleStart = auditQuery ? Stopwatch.GetTimestamp() : 0;
            float indexedRisk = ProjectileRisk(state, tick, false, tossView);
            cost += indexedRisk;
            // Audit real native collision calls at a bounded sampling rate.
            // A discrepancy disqualifies the encounter and retains diagnostics.
            if (auditQuery)
            {
                long sampleMiddle = Stopwatch.GetTimestamp();
                float fullRisk = ProjectileRisk(state, tick, true, tossView);
                long sampleEnd = Stopwatch.GetTimestamp();
                SpatialAuditChecks++;
                spatialIndexedTicks += sampleMiddle - sampleStart;
                spatialFullScanTicks += sampleEnd - sampleMiddle;
                if (Math.Abs(indexedRisk - fullRisk) > .5f) SpatialAuditMismatches++;
            }
            // An immune tick (Betsy dash, see the snapshot above) cannot produce a Hurt event, so
            // the whole hazard section is discarded. The audit above already ran on the real values,
            // so this only changes what the search sees, never the measurement.
            if (dashImmune >= tick) cost = costBeforeHazards;
            Vector2 topLeft = point - new Vector2(player.width / 2f, player.height / 2f);
            if (Collision.SolidCollision(topLeft, player.width, player.height)) cost += 1000000;
            return cost;
        }

        void AccumulateFutureFull(ref float cost, NVector future, int halfX, int halfY, int tick)
        {
            Vector2 point = X(future);
            foreach (TimedBox box in futureBoxes)
                if (box.Intersects(future, new NVector(halfX, halfY), tick)) cost += 1000000;
            foreach (DecayingShard shard in futureShards)
                if (shard.Intersects(future, new NVector(halfX, halfY), tick)) cost += 1000000;
            foreach (TrailOrb orb in futureOrbs)
            {
                if (tick < orb.Born || tick > orb.Explodes + 21) continue;
                bool exploded = tick >= orb.Explodes;
                if (exploded && !SmallBlastTiming.CouldDamage(tick - orb.Explodes)) continue;
                NVector center = orb.CenterAt(exploded ? orb.Explodes : tick);
                float dx = Math.Max(0, Math.Abs(future.X - center.X) - halfX);
                float dy = Math.Max(0, Math.Abs(future.Y - center.Y) - halfY);
                // One orb update of padding covers creation-order differences in the
                // engine's projectile pool. Forecasts never create or edit game entities.
                float radius = (exploded ? 112 : 41) + orb.Padding;
                if (dx * dx + dy * dy < radius * radius) cost += 1000000;
                cost += ThreatClearance.Circle(future, center, new(halfX, halfY), radius, clearanceReserve);
            }
            foreach (var blast in explosions)
            {
                if (!SmallBlastTiming.CouldDamage(tick - blast.At)) continue;
                float dx = Math.Max(0, Math.Abs(point.X - blast.Center.X) - halfX);
                float dy = Math.Max(0, Math.Abs(point.Y - blast.Center.Y) - halfY);
                if (dx * dx + dy * dy < 104 * 104) cost += 1000000;
                cost += ThreatClearance.Circle(future, N(blast.Center), new(halfX, halfY), 104, clearanceReserve);
            }
        }

        // SpecialDashKey throws the dash along SafeDirectionTo(player, Main.MouseWorld): the aim decides
        // everything and the press cannot be recalled. The 2026-10-20 Legendary trial proved that
        // scoring bearings by distance to threat centres is useless against a 3000px rotating ray - it
        // aimed straight back into the beam and the hit landed 17 ticks after the immunity expired.
        // Score candidate landing points with the same hazard model that drives control: the dash puts
        // the player 15 x 25px away, and immunity covers exactly those 15 ticks, so what matters is how
        // dangerous that landing point is on the tick immunity ends and shortly after.
        static readonly int[] dashProbeTicks = { 16, 24, 32 };
        Vector2 DashEscapeDirection()
        {
            Vector2 origin = player.Center;
            Vector2 planned = X(planner.NavigationEnd) - origin;
            Vector2 best = Vector2.Zero;
            float bestScore = float.MaxValue;
            if (planned.LengthSquared() > 900f)
            {
                best = Vector2.Normalize(planned);
                bestScore = DashLandingScore(origin + best * 375f);
            }
            for (int i = 0; i < 8; i++)
            {
                Vector2 direction = (i * MathHelper.TwoPi / 8f).ToRotationVector2();
                Vector2 landing = origin + direction * 375f;
                if (landing.X < motion.WorldMinimum.X + 140f || landing.X > motion.WorldMaximum.X - 140f
                    || landing.Y < motion.WorldMinimum.Y + 140f || landing.Y > motion.WorldMaximum.Y - 140f) continue;
                float score = DashLandingScore(landing);
                if (score < bestScore) { bestScore = score; best = direction; }
            }
            if (best != Vector2.Zero) return best;
            Vector2 next = X(LastDecision.Next.Position) - origin;
            if (next.LengthSquared() > 100f) return Vector2.Normalize(next);
            Vector2 away = origin - boss.Center;
            return away.LengthSquared() > 1f ? Vector2.Normalize(away) : new Vector2(1f, 0f);
        }
        // Read-only use of the live hazard model at a hypothetical point. Costs a handful of native
        // collision queries on the frames a dash is pressed, never on ordinary frames.
        float DashLandingScore(Vector2 landing)
        {
            NVector point = N(landing);
            float score = 0;
            foreach (int tick in dashProbeTicks) score += Math.Min(DangerPosition(point, tick), 1000000f);
            return score;
        }

        // Worm head proximity. The soft term alone was worth ~9 cost at 31px against a 1000000 hit, so
        // the planner let the Destroyer head close to 31px and then the space was saturated by its body,
        // 66 predicted spear-trail orbs and 10 explosions: every candidate was doomed for 9 ticks.
        // Reserving a hard inner ring keeps the worm from arriving in the first place.
        float HeadCost(NVector head, NVector point)
        {
            float distance = NVector.Distance(point, head);
            float hard = Math.Clamp(PilotTuning.Current.HeadHardClearance, 40f, 300f);
            if (distance < hard) return 1000000;
            float clearance = Math.Max(0, PilotTuning.Current.HeadClearance - distance);
            return clearance * clearance / 4000;
        }

        static bool BladeContact(string name) => name is "MutantSpearAim" or "MutantSpearDash"
            or "MutantSpearSpin" or "MutantSpearThrown" or "MutantSword";
        // Bounded: at zero gap it is worth 30000, far below a hit's 1000000, so a genuinely closed
        // corridor can still be crossed, but ordinary routes keep their distance from blades.
        static float BladeContactCost(Vector2 point, Vector2 center, int halfX, int halfY, float reserve)
        {
            float dx = Math.Max(0, Math.Abs(point.X - center.X) - halfX);
            float dy = Math.Max(0, Math.Abs(point.Y - center.Y) - halfY);
            float gap = MathF.Sqrt(dx * dx + dy * dy);
            if (gap >= reserve) return 0;
            float intrusion = 1 - gap / reserve;
            return 30000 * intrusion * intrusion;
        }
        float ProjectileRisk(MotionState state, int tick, bool fullScan, TossWorld? tossView)
        {
            NVector future = state.Position;
            Vector2 point = X(future);
            double cost = 0;
            int halfX = (int)Math.Ceiling(projectileHalf.X) + (int)inflation, halfY = (int)Math.Ceiling(projectileHalf.Y) + (int)inflation;
            var nearbyThreats = fullScan ? null : threatBuckets.At(future, tick);
            int nearbyCount = nearbyThreats?.Count ?? 0;
            int candidateCount = fullScan ? threats.Count : nearbyCount + globalThreats.Count;
            if (!fullScan)
            {
                ThreatFullScanCandidates += threats.Count;
                ThreatCandidates += candidateCount;
            }
            for (int candidate = 0; candidate < candidateCount; candidate++)
            {
                Threat threat = threats[fullScan ? candidate
                    : candidate < nearbyCount ? nearbyThreats[candidate] : globalThreats[candidate - nearbyCount]];
                if (tick > threat.RemainingTicks || tick < threat.ArmsIn) continue;
                Projectile p = threat.Projectile;
                if(queriedDirectDash>=0 && p.ModProjectile?.Name=="MutantSpearDash")continue;
                if(threat.Sword && queriedSword>=0)continue;
                if (queriedWorm >= 0 && wormResponse.Contains(p.whoAmI))
                {
                    NVector center = wormResponse.Position(queriedWorm, p.whoAmI);
                    if (Math.Abs(future.X - center.X) < p.width / 2f + halfX
                        && Math.Abs(future.Y - center.Y) < p.height / 2f + halfY) cost += 1000000;
                    cost += ThreatClearance.Box(future, center,
                        new(p.width / 2f + halfX, p.height / 2f + halfY), clearanceReserve);
                    if (p.ModProjectile?.Name == "MutantDestroyerHead")
                        cost += HeadCost(center, future);
                    continue;
                }
                bool followsToss = tossView is TossWorld && p.whoAmI == tossResponse.AimSlot
                    && tick <= tossResponse.AimRemaining && float.IsFinite(tossView.Value.AimAngle);
                bool followsSpin=p.ModProjectile?.Name=="MutantSpearSpin"&&p.ai[0]==boss.whoAmI;
                Vector2 spinCenter=followsSpin?ResponseBoss(tick,tossView):Vector2.Zero;
                if (threat.Sword)
                {
                    Vector2 a = threat.Centers[tick], b = threat.Centers[tick - 1], mid = (a + b) * .5f;
                    bool Touch(Vector2 c) => Math.Abs(point.X - c.X) < p.width / 2f + halfX
                        && Math.Abs(point.Y - c.Y) < p.height / 2f + halfY;
                    if (Touch(a) || Touch(b) || Touch(mid)) cost += 1000000;
                    continue;
                }
                if (p.ModProjectile?.Name == "MutantDestroyerHead")
                    cost += HeadCost(N(threat.Centers[tick]), future);
                if (threat.SmallBomb)
                {
                    if (!SmallBlastTiming.Damages(p.frame * 3 + p.frameCounter + tick)) continue;
                }
                if (!threat.LongShape && clearanceReserve > 0)
                    cost += threat.SmallBomb
                        ? ThreatClearance.Circle(future, N(threat.Centers[tick]), new(halfX, halfY), p.width / 2f, clearanceReserve)
                        : ThreatClearance.Box(future, N(followsSpin?spinCenter:threat.Centers[tick]),
                            new(p.width / 2f + halfX, p.height / 2f + halfY), clearanceReserve);
                // Touching a blade is already a failure even when its own hit deals 0: its
                // OnHitPlayer drops a MutantBombSmall on the player, which is what actually hurts.
                if (!threat.LongShape && PilotTuning.Current.BladeContactBand > 0
                    && BladeContact(p.ModProjectile?.Name))
                    cost += BladeContactCost(point, threat.Centers[tick], halfX, halfY, PilotTuning.Current.BladeContactBand);
                if (p.extraUpdates > 0 && !threat.LongShape)
                {
                    // Native fast bolts check damage at every sub-update. Checking only
                    // the last position let a bolt cross a player between planner samples.
                    if (SweptBox.Intersects(N(threat.Centers[tick - 1]), N(threat.Centers[tick]), future,
                        new NVector(p.width / 2f + halfX, p.height / 2f + halfY))) cost += 1000000;
                    continue;
                }
                if (threat.ShortRayKind != 0)
                {
                    float scale = threat.Scales[tick];
                    if (!ShortRayTiming.Damages(threat.ShortRayKind, scale)) continue;
                    if(Main.getGoodWorld && clearanceReserve>0)
                        cost+=ThreatClearance.Beam(future,N(threat.Centers[tick]),N(p.velocity),3000,22*scale,
                            new(halfX,halfY),clearanceReserve);
                    if (!fullScan && threat.Broad is BeamBroadPhase broad)
                    {
                        beamQueries++;
                        NVector relative = N(point - (threat.Centers[tick] - p.Center));
                        if (!broad.MayTouch(relative, new(halfX, halfY))) { beamRejected++; continue; }
                    }
                    if (NativeRayTouches(point, halfX, halfY, threat.Centers[tick], p.velocity, scale)) cost += 1000000;
                    continue;
                }
                // Translate/rotate the query, never the live projectile or player.
                int updates = p.extraUpdates + 1;
                // FTW final rings check TimeFrozen inside AI, before their movement.
                // Hurt is checked after movement. Both positions must stay clear.
                bool timeStopRing=Main.getGoodWorld&&boss.ai[0]==-5&&p.ModProjectile?.Name=="MutantSphereRing";
                for (int sub = updates > 1 || timeStopRing ? 0 : 1; sub <= updates; sub++)
                {
                    Vector2 center = Vector2.Lerp(threat.Centers[tick - 1], threat.Centers[tick], (float)sub / updates);
                    if(followsSpin)center=spinCenter;
                    if (followsToss) center = X(tossView.Value.Position);
                    if(p.ModProjectile?.Name=="MutantRitual" && ResponseArena(tossView) is NVector candidateArena)
                        center=X(candidateArena);
                    Vector2 relative = point - (center - p.Center);
                    float rotation = threat.Angles == null ? threat.RotationSpeed * tick : threat.Angles[tick] - threat.Angles[0];
                    if (followsToss) rotation = tossView.Value.AimAngle - p.rotation;
                    else if (p.ModProjectile?.Name == "MutantSpearAim")
                        rotation = ProjectileKinematics.SpearAimAngle(N(center), future, state.Velocity,
                            p.ai[1], p.rotation, p.localAI[0] != 0, tick) - p.rotation;
                    if (rotation != 0)
                        relative = p.Center + (relative - p.Center).RotatedBy(-rotation);
                    if (p.type == ProjectileID.FairyQueenLance
                        && (Math.Abs(relative.X - p.Center.X) > 44 + halfX
                            || Math.Abs(relative.Y - p.Center.Y) > 44 + halfY)) continue;
                    if (!threat.LongShape && (Math.Abs(relative.X - p.Center.X) > p.width / 2 + halfX + 4
                        || Math.Abs(relative.Y - p.Center.Y) > p.height / 2 + halfY + 4)) continue;
                    int extra = threat.WarmingRay ? 24 : 0;
                    NVector queryHalf = new(halfX + extra, halfY + extra);
                    if (rotation != 0) queryHalf = ProjectileKinematics.RotatedHalfSize(queryHalf, rotation);
                    int queryX = (int)Math.Ceiling(queryHalf.X), queryY = (int)Math.Ceiling(queryHalf.Y);
                    var hitbox = new Rectangle((int)relative.X - queryX, (int)relative.Y - queryY, queryX * 2, queryY * 2);
                    if (!fullScan && threat.Broad is BeamBroadPhase broad)
                    {
                        beamQueries++;
                        if (!broad.MayTouch(new(hitbox.Center.X, hitbox.Center.Y), new(queryX, queryY)))
                        { beamRejected++; continue; }
                    }
                    if (p.Colliding(p.Hitbox, hitbox)) { cost += 1000000; break; }
                }
            }
            return (float)cost;
        }
        static bool NativeRayTouches(Vector2 point, int halfX, int halfY, Vector2 origin, Vector2 direction, float scale, float length = 3000)
        {
            var query = new Rectangle((int)point.X - halfX, (int)point.Y - halfY, halfX * 2, halfY * 2);
            var source = new Rectangle((int)(origin.X - 24), (int)(origin.Y - 24), 48, 48);
            if (source.Intersects(query)) return true;
            float at = 0;
            // Full native width and flat endpoints; no warming-ray query expansion.
            // Active native rays have already grown beyond the reachable arena;
            // 3000 also conservatively bounds their length during warm-up.
            return Collision.CheckAABBvLineCollision(query.TopLeft(), query.Size(), origin,
                origin + direction * length, 22 * scale, ref at);
        }
    }
}

namespace MutantPilot.Common
{
    // Live routing parameters can be changed between trials without rebuilding the mod.
    public sealed class PilotTuning
    {
        public string PrimaryWeapon { get; set; } = "FargowiltasSouls/TheDestroyer";
        public bool ReserveSpearTransition { get; set; } = true;
        public bool ContinuousTossTravel { get; set; } = true;
        public bool SpiralTossTravel { get; set; } = true;
        public bool TriggerArmorSkill { get; set; } = true;
        public int ForecastWorkers { get; set; } = System.Math.Min(8,System.Environment.ProcessorCount);
        public static PilotTuning Current { get; set; } = new();
        public bool RouteSpears { get; set; } = true;
        public bool OrbitSpheres { get; set; } = true;
        public float KiteOffset { get; set; } = 700;
        public float OrbitRadius { get; set; } = 740;
        public float OrbitRadiansPerTick { get; set; } = .018f;
        public float HeadClearance { get; set; } = 220;
        public double BudgetMs { get; set; } = 4;
        public double RescueBudgetMs { get; set; } = 16;
        public int RescueBeamWidth { get; set; } = 12;
        public int RescueSegmentTicks { get; set; } = 3;
        public bool DiverseRescue { get; set; } = true;
        public double FinalBeamBudgetMs { get; set; } = 4;
        public double FinalBeamRescueBudgetMs { get; set; } = 16;
        public float BoundaryReserve { get; set; } = 600;
        // The stance goal is an attractor, not a waypoint: recorded fights spent their last
        // 96 ticks chasing a target whose arrival time was 38-121 ticks, well outside the
        // 48-tick search. Clamp the pull to what the current speed can deliver in
        // GoalReachTicks, flooring the speed so a parked player still gets a usable target.
        public float GoalReachTicks { get; set; } = 24;
        public float GoalReachFloorSpeed { get; set; } = 8;
        // Fixed encounter-height half-band for spear goals. Dodging may leave it.
        public float KiteBand { get; set; } = 1500;
        public float WorldBoundaryReserve { get; set; } = 3000;
        public float FinalSparkRadius { get; set; } = 150;
        public float ThreatClearance { get; set; } = 48;
        public bool TossOrbit { get; set; } = true;
        // State 41 locks its throw aim at ai[1] 151 and then drags two 156-tick MutantDeathray2 rays
        // along that axis, with small bombs falling on the trail. Leave the axis during the wind-up
        // (ai[1] < TossEarlyTicks) with a 600px perpendicular displacement instead of after the throw.
        public bool TossEarlyLateral { get; set; } = false;
        public int TossEarlyTicks { get; set; } = 150;
        public bool MechFanGap { get; set; } = true;
        public bool P1EyeOrbit { get; set; } = true;
        public bool HoldAttackPause { get; set; } = true;
        public float DiveClearance { get; set; } = 160;
        public bool UseNativeHurtbox { get; set; } = true;
        public bool CoupledWorms { get; set; } = true;
        public bool LegendaryWormTravel { get; set; } = true;
        public bool OuterP1Void { get; set; } = true;
        // Operator decision 2026-10-21: stationary equipment skills are allowed; Golden Stasis
        // and every dash are forbidden. The harness and action entry points enforce this policy.
        public bool AllowAccessorySkills { get; set; } = true;
        // Betsy dash grants FargoSoulsPlayer.ImmuneToDamage for its 15 ticks, which makes
        // CanBeHitByPlayerProjectile return false: no Hurt event at all. With this on, ticks still
        // inside the dash are not charged hazard cost, so the planner stops fleeing something that
        // cannot hurt it. Runtime switchable so the A/B runs one binary.
        public bool UseDashImmunity { get; set; } = false;
        // Press the Betsy active-skill key when the chosen line is already doomed inside this many
        // ticks (Decision.FirstHit != 0 && <= value). 0 means preemptive. Gated by
        // AllowAccessorySkills, which is the operator's switch for the whole accessory toolkit.
        public bool TriggerBetsyDash { get; set; } = false;
        public int DashTriggerFirstHit { get; set; } = 6;
        public int DashTriggerCooldown { get; set; } = 20;
        // After a dash press, the player still has to finish the escape it paid for. 2026-10-20 trials:
        // 8 of 20 hits landed 1-10 ticks after the immunity expired because the planner re-centred on
        // its stance target. Keep the dash direction as the attractor for this many ticks afterwards.
        public int DashRunTicks { get; set; } = 25;
        public float DashRunDistance { get; set; } = 700;
        // Perpendicular step away from an armed MutantDeathray2 (a 3000px line). Distance from the
        // ray's origin is meaningless; the offset from its bearing is what kills.
        public float RaySidestep { get; set; } = 600;
        // Golden Stasis (the gold-shell): 300 ticks of real immunity, 3600 tick cooldown, but it zeroes
        // every movement key. That makes it the one tool in this kit that can answer a genuinely
        // unavoidable hit, so it is pressed only when the chosen line is already doomed this soon.
        public bool TriggerGoldShell { get; set; } = false;
        public int GoldShellFirstHit { get; set; } = 2;
        // Golden Stasis is the one skill the operator excluded, so it stays unreachable: the harness
        // rejects any tuning that arms it, and this flag is the second lock for arithmetic in code.
        public bool AllowGoldenStasis { get; set; } = false;
        // Stand-off distance. Zenith's own projectiles reach far past 650px, so the stance no longer
        // needs to sit inside the boss's close-range attacks. Factor scales the weapon's declared
        // range; Standoff is the hard cap.
        public float StandoffFactor { get; set; } = 1f;
        public float Standoff { get; set; } = 850;
        // Circle the boss instead of holding one flank. 0.012 rad/tick is a full lap in ~525 ticks
        // (~9s), close to how a human orbits without falling behind the fight.
        public bool StandOrbit { get; set; } = true;
        public float StandOrbitSpeed { get; set; } = 0.012f;
        // Inner ring around a Destroyer head that counts as a hit. HeadClearance stays the soft
        // preference; this is what stops the worm from closing to 31px before the trap closes.
        public float HeadHardClearance { get; set; } = 110;
        // During a dash state the boss closes on its own, so the stance stops orbiting and retreats
        // radially to at least this range. 600px leaves 13 ticks against a 45px/tick spear.
        public float MinDashRange { get; set; } = 600;
        // Travel dash states (6/15/23) charge at up to 60px/tick, faster than the player's 18px/tick,
        // so the only answer is perpendicular displacement. This reuses the state-14 locked-aim escape
        // geometry with the live charge direction.
        public bool DashLateralEscape { get; set; } = true;
        // Divisor on the squared goal distance. 250000f made a 192px stance offset worth 0.147, which
        // is the same order as the 0.12 move-change penalty, so the planner idled. 40000f makes the
        // same offset worth 0.92 while a full 650px stance offset stays ~10.6, far below any real hit.
        public float GoalPullDivisor { get; set; } = 250000;
        // Compatibility field; true is rejected until all native collision inputs can be frozen.
        public bool AsyncDecision { get; set; } = false;
        // Optional additional cutoff, not a deterministic replay guarantee: the time limit still applies.
        public int ProposalBudget { get; set; } = 0;
        // Touching a spear or sword is already a failure even when the hit itself deals 0 damage: its
        // OnHitPlayer drops a MutantBombSmall on the player (target.Center plus up to 100px) that deals
        // the real 500-700 damage. All seven MutantBombSmall hits recorded on 2026-10-21 were frame-0
        // bombs spawned 20-107px from the player, i.e. blade contacts one tick earlier.
        public float BladeContactBand { get; set; } = 220;
    }
}

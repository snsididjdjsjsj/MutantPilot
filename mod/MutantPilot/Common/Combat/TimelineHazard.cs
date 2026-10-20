using System;
using System.Collections.Generic;
using System.Numerics;

namespace MutantPilot.Combat
{
    /// <summary>
    /// Timeline-derived hazards for the three Mutant attacks whose per-frame schedule the
    /// installed 1.7.3.9 handlers pin down exactly: 19 PillarDunk, 20 EOCStarSickles and
    /// 41 SpearTossDirectP2.
    ///
    /// Every rule answers one question - "how many ticks until this thing may damage me,
    /// and roughly where will it be then" - and is re-evaluated on every tick, so the
    /// event walks into CombatPlanner.ForecastTicks on its own. The answer is emitted as
    /// a TimedBeam, i.e. into the same container the missile forecasts already fill, so
    /// the existing native Colliding test, the first-hit ordering and the spatial pruning
    /// all apply unchanged. Nothing here creates, edits or reads back a game entity.
    ///
    /// The estimate of *where* is frozen on the first tick the event enters the horizon.
    /// The handler aims at the player position it samples on the fire tick, which cannot
    /// be known in advance; re-aiming every tick instead would make the predicted line
    /// chase the planner, and then no candidate escape would score better than any other.
    /// </summary>
    public sealed class TimelineHazard
    {
        // MutantPillar declares alpha 255 and decrements 2 per tick, and CanDamage() is
        // exactly alpha == 0: the pillar is inert for 128 ticks and then lunges at 32 px
        // per tick with timeLeft = distance / 32.
        public const int PillarArmTicks = 128;
        // EOCStarSickles creates the eye on the entry frame; Masochist seeds the eye's
        // ai[1] at 30 and MutantEyeOfCthulhu.CanDamage() is ai[1] >= 120.
        public const int EyeArmTicks = 90;
        // The eye snaps onto a 700px ring around its target on the arming frame and then
        // dashes back through it at 120 px per tick, trailing scythes.
        public const float EyeRing = 700f;
        // SpearTossDirectP2 fires on ai[1] == 165 (Masochist extra) and again when ai[1]
        // passes 180, where ai[1] is rewound to 150 so the cycle is 31 ticks long.
        public const int TossEarlyTick = 165;
        public const int TossLateTick = 180;

        int lockKey = int.MinValue;
        Vector2 lockTarget;
        int lastLead = -1;
        float lastEyeAi1 = -1f;
        int eyeEpoch;

        /// <summary>Ticks of warning the most recent emission bought; -1 when nothing fired.</summary>
        public int LastLead => lastLead;
        /// <summary>Ticks on which at least one timeline hazard was armed inside the horizon.</summary>
        public long ArmedTicks { get; private set; }
        /// <summary>Total timeline beams handed to the planner so far.</summary>
        public long Emitted { get; private set; }

        public object Snapshot() => new { lead = lastLead, armedTicks = ArmedTicks, emitted = Emitted };

        public void Reset()
        {
            lockKey = int.MinValue;
            lastLead = -1;
        }

        Vector2 Lock(int key, Vector2 playerCenter)
        {
            if (key != lockKey)
            {
                lockKey = key;
                lockTarget = playerCenter;
            }
            return lockTarget;
        }

        bool Inside(int lead, int horizon)
        {
            if (lead < 1 || lead > horizon) return false;
            ArmedTicks++;
            lastLead = lead;
            return true;
        }

        /// <summary>
        /// State 41 SpearTossDirectP2: a spear plus a pair of MutantDeathray2 leaves the
        /// boss on ai[1] == 165 and again as ai[1] crosses 180. The aim marker only
        /// appears on ai[1] == 151, so reading ai[1] buys up to 34 extra ticks, and the
        /// paired rays arm 24 ticks after they spawn on top of that.
        /// </summary>
        public void EmitSpearToss(List<TimedBeam> beams, int horizon, int state, float ai1, float ai2,
            Vector2 bossCenter, Vector2 playerCenter)
        {
            if (state != 41) return;
            int lead = ai1 < TossEarlyTick ? (int)MathF.Ceiling(TossEarlyTick - ai1)
                : ai1 < TossLateTick ? (int)MathF.Ceiling(TossLateTick - ai1) : 1;
            if (!Inside(lead, horizon)) return;
            Vector2 target = Lock(0x0041_0000 ^ (int)ai2, playerCenter);
            Vector2 delta = target - bossCenter;
            if (delta.LengthSquared() < 1f) return;
            beams.Add(new TimedBeam(bossCenter, Vector2.Normalize(delta), lead,
                Math.Min(horizon, lead + 34), 30f));
            Emitted++;
        }

        /// <summary>
        /// State 20 EOCStarSickles: the eye is created on the entry frame at the boss
        /// center and needs 90 ticks before it may damage, at which point it teleports to
        /// a 700px ring around its target and lunges through it. The ring point is the
        /// axis we predict, because that is the corridor the lunges and their scythes
        /// sweep.
        /// </summary>
        public void EmitEyeDash(List<TimedBeam> beams, int horizon, int state, float ai1,
            Vector2 bossCenter, Vector2 playerCenter)
        {
            if (state != 20 || ai1 > EyeArmTicks) return;
            // ai[1] only ever climbs inside one entry, so a decrease is a new EOCStarSickles.
            if (ai1 < lastEyeAi1) eyeEpoch++;
            lastEyeAi1 = ai1;
            int lead = Math.Max(1, (int)MathF.Ceiling(EyeArmTicks - ai1));
            if (!Inside(lead, horizon)) return;
            Vector2 target = Lock(0x0020_0000 ^ eyeEpoch, playerCenter);
            Vector2 axis = bossCenter - target;
            if (axis.LengthSquared() < 1f) return;
            Vector2 arm = target + Vector2.Normalize(axis) * EyeRing;
            beams.Add(new TimedBeam(arm, Vector2.Normalize(target - arm), lead,
                Math.Min(horizon, lead + 60), 48f));
            Emitted++;
        }

        /// <summary>
        /// State 19 PillarDunk: the first pillar is dropped at ai[1] == 60 and the
        /// Masochist extra at ai[1] == 300, both at the boss center. The pillar spends
        /// 128 ticks invisible to the planner (CanDamage is false) while it tracks the
        /// boss, then lunges at the player at 32 px per tick, so the countdown has to be
        /// read off the projectile itself rather than from ai[1].
        /// </summary>
        public void EmitPillarDash(List<TimedBeam> beams, int horizon, Vector2 center, float alpha,
            Vector2 playerCenter, int index, long generation)
        {
            if (alpha <= 0f) return;
            int lead = Math.Max(1, (int)MathF.Ceiling(alpha * .5f));
            if (!Inside(lead, horizon)) return;
            Vector2 target = Lock(0x0019_0000 ^ (index << 8) ^ (int)(generation & 0xFF), playerCenter);
            Vector2 delta = target - center;
            if (delta.LengthSquared() < 1f) return;
            beams.Add(new TimedBeam(center, Vector2.Normalize(delta), lead,
                Math.Min(horizon, lead + 40), 34f));
            Emitted++;
        }
    }
}

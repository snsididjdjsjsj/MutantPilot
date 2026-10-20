using System;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;
using MutantPilot.Combat;

namespace MutantPilot.Common
{
    public readonly record struct MotionParameters(float MaxX, float WalkSpeed, float Acceleration,
        float Braking, float StopBraking, float Gravity, float MaxFall, float JumpSpeed, bool FlightMastery);

    // Player.Update resets run/jump/gravity fields before SetControls. Observe them after
    // equipment and wing modifiers, then use the observation on the next input frame.
    // Reading these hooks never changes live movement or accessory effects.
    public sealed class LiveMotion
    {
        ModPlayer fargoPlayer;
        FieldInfo noMomentum, darkenedHeart;
        PropertyInfo stillHolding;
        Player inspectedPlayer;
        public MotionParameters Parameters { get; private set; }
        public bool HasSample { get; private set; }

        public void Capture(Player player)
        {
            if (!ReferenceEquals(inspectedPlayer, player))
            {
                inspectedPlayer = player;
                foreach (ModPlayer mp in player.ModPlayers)
                    if (mp.GetType().FullName == "FargowiltasSouls.Core.ModPlayers.FargoSoulsPlayer")
                    {
                        fargoPlayer = mp;
                        noMomentum = mp.GetType().GetField("NoMomentum");
                        darkenedHeart = mp.GetType().GetField("DarkenedHeartItem");
                        stillHolding = mp.GetType().GetProperty("IsStillHoldingInSameDirectionAsMovement");
                        break;
                    }
            }
            float extraBraking = 0;
            if (!player.mount.Active)
            {
                if (noMomentum?.GetValue(fargoPlayer) is true) extraBraking += 7;
                if (darkenedHeart?.GetValue(fargoPlayer) != null) extraBraking += .2f;
            }
            bool holding = stillHolding?.GetValue(fargoPlayer) is true;
            // Fargo adds stopping drag conditionally; retain both branches for candidate inputs.
            float forwardBraking = Math.Max(.01f, player.runSlowdown - (holding ? 0 : extraBraking));
            bool mastery = false;
            for (Type type = player.equippedWings?.ModItem?.GetType(); type != null; type = type.BaseType)
                if (type.Name == "FlightMasteryWings") { mastery = true; break; }
            Parameters = new(Math.Max(player.maxRunSpeed, player.accRunSpeed), player.maxRunSpeed,
                player.runAcceleration, forwardBraking, forwardBraking + extraBraking,
                player.gravity, player.maxFallSpeed, Player.jumpSpeed, mastery);
            HasSample = true;
        }

        public void Apply(MotionModel motion)
        {
            if (!HasSample) return;
            var p = Parameters;
            motion.MaxX = p.MaxX;
            motion.WalkSpeed = p.WalkSpeed;
            motion.Acceleration = p.Acceleration;
            motion.Braking = p.Braking;
            motion.StopBraking = p.StopBraking;
            motion.Gravity = p.Gravity;
            motion.MaxFall = p.MaxFall;
            motion.JumpSpeed = p.JumpSpeed;
            motion.FlightMastery = p.FlightMastery;
        }
    }
}

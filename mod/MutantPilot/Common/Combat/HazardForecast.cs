using System;
using System.Collections.Generic;
using System.Numerics;

namespace MutantPilot.Combat
{
    public static class FlatBeam
    {
        // AABB versus the finite oriented rectangle used by Terraria's thick
        // line collision. lineWidth is full width; neither endpoint is extended.
        public static bool Intersects(Vector2 origin, Vector2 direction, float length, float lineWidth,
            Vector2 point, Vector2 half)
        {
            Vector2 normal = new(-direction.Y, direction.X);
            float along = length * .5f, across = lineWidth * .5f;
            Vector2 delta = point - (origin + direction * along);
            Vector2 absDirection = Vector2.Abs(direction), absNormal = Vector2.Abs(normal);
            return Math.Abs(delta.X) <= half.X + absDirection.X * along + absNormal.X * across
                && Math.Abs(delta.Y) <= half.Y + absDirection.Y * along + absNormal.Y * across
                && Math.Abs(Vector2.Dot(delta, direction)) <= along + Vector2.Dot(half, absDirection)
                && Math.Abs(Vector2.Dot(delta, normal)) <= across + Vector2.Dot(half, absNormal);
        }
    }

    public readonly record struct TimedBox(Vector2 Center, Vector2 HalfSize, int ArmsAt, int EndsAt)
    {
        public bool Intersects(Vector2 point, Vector2 playerHalf, int tick) => tick >= ArmsAt && tick <= EndsAt
            && Math.Abs(point.X - Center.X) <= HalfSize.X + playerHalf.X
            && Math.Abs(point.Y - Center.Y) <= HalfSize.Y + playerHalf.Y;
    }

    public static class MoonBlast
    {
        // Installed Moon/Sun blast: frames 3 and 4 damage, two ticks per frame.
        // At age 6 it makes a stationary copy and/or a forward chain segment.
        // Random size, 20px copy jitter and +/-15 degree chain jitter are bounded,
        // never sampled from the game's RNG or applied to live entities.
        public static void Forecast(List<TimedBox> result, Vector2 center, Vector2 halfSize,
            float angle, int remaining, int age, int mode, int horizon)
        {
            if (age <= 9) result.Add(new(center, halfSize, Math.Max(1, 6 - age), 9 - age));
            if (age >= 6 || remaining <= 0) return;
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            int firstBirth = 6 - age;
            for (int generation = 1; generation <= remaining; generation++)
            {
                // A child in a later projectile slot can update on its birth tick.
                int arms = firstBirth + (generation - 1) * 5 + 5;
                if (arms > horizon) break;
                int ends = firstBirth + (generation - 1) * 6 + 9;
                for (int forward = 0; forward <= generation; forward++)
                {
                    if ((mode == 1 && forward != 0) || (mode == 2 && forward != generation)) continue;
                    int copies = generation - forward;
                    if (copies > 6) continue; // Stationary children inherit at most five further copies.
                    float uncertainty = 27 * forward + 20 * copies;
                    result.Add(new(center + direction * (100 * forward), new Vector2(140 + uncertainty), arms, ends));
                }
            }
        }
    }

    public readonly record struct TimedBeam(Vector2 Origin, Vector2 Direction, int ArmsAt, int EndsAt, float Padding = 24)
    {
        public bool Intersects(Vector2 point, Vector2 halfSize, int tick)
        {
            if (tick < ArmsAt || tick > EndsAt) return false;
            return SweptBox.Intersects(Origin, Origin + Direction * 3000, point, halfSize + new Vector2(Padding));
        }
    }

    public static class ProjectileKinematics
    {
        public static float GuardianLaneCost(Vector2 player, Vector2 boss, float fanDirection)
        {
            float centerY = boss.Y - Math.Sign(fanDirection) * 600;
            float lane = Math.Abs(player.Y - centerY) * MathF.Tan(MathF.PI / 12) + 150;
            float intrusion = Math.Max(0, lane - Math.Abs(player.X - boss.X));
            return intrusion * intrusion / 4000;
        }
        public static Vector2 VoidRayGoal(Vector2 player, Vector2 center, int side)
        {
            Vector2 offset = player - center;
            if (offset.LengthSquared() < .001f) offset = new(side < 0 ? -1 : 1, 0);
            return center + Vector2.Normalize(offset) * 115;
        }
        public static float SpearAimAngle(Vector2 origin, Vector2 player, Vector2 velocity,
            float mode, float current, bool initialized, int ticks)
        {
            Vector2 target = player + (mode > 1 ? velocity * 30 : Vector2.Zero) - origin;
            float desired = MathF.Atan2(target.Y, target.X);
            if (mode > 1 || !initialized) return desired;
            return current + FinalSparkForecast.Wrap(desired - current) * (1 - MathF.Pow(.8f, ticks));
        }
        public static Vector2 RotatedHalfSize(Vector2 half, float angle)
        {
            float c = Math.Abs(MathF.Cos(angle)), s = Math.Abs(MathF.Sin(angle));
            return new(c * half.X + s * half.Y, s * half.X + c * half.Y);
        }
        public static void DecayingRayAngles(float[] result, float angle, float turn, float age, bool masochist)
        {
            result[0] = angle;
            for (int t = 1; t < result.Length; t++)
            {
                age++;
                if (age > 45 && age < 150) turn *= masochist ? .9716f : .9712f;
                result[t] = result[t - 1] + turn;
            }
        }
        public static Vector2 LockedSpearGoal(Vector2 player, Vector2 boss, float angle, int side)
        {
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            Vector2 normal = new(-direction.Y, direction.X);
            return boss + direction * Math.Clamp(Vector2.Dot(player - boss, direction), 100, 700)
                + normal * (side < 0 ? -600 : 600);
        }
        public static void HomingEye(Vector2[] result, Vector2 start, Vector2 velocity, float countdown,
            int bossState, bool masochist, bool goodWorld, Vector2 player, Vector2 playerVelocity)
        {
            result[0] = start;
            float baseSpeed = masochist ? 15 : 10;
            bool dash = bossState is 4 or 5 or 6 or 13 or 14 or 15 or 21 or 22 or 23;
            bool fleeing = !dash && (!masochist || bossState <= 10) && !goodWorld;
            Vector2 RotateTowards(Vector2 v, Vector2 direction, float amount)
            {
                double difference = (float)Math.Atan2(direction.Y, direction.X) - (float)Math.Atan2(v.Y, v.X);
                if (difference > Math.PI) difference -= Math.PI * 2;
                if (difference < -Math.PI) difference += Math.PI * 2;
                double angle = difference * amount;
                return new((float)(v.X * Math.Cos(angle) - v.Y * Math.Sin(angle)),
                    (float)(v.X * Math.Sin(angle) + v.Y * Math.Cos(angle)));
            }
            for (int t = 1; t < result.Length; t++)
            {
                Vector2 position = result[t - 1], target = player + playerVelocity * Math.Min(t, 12);
                float distance = Vector2.Distance(position, target);
                if (fleeing) countdown = -600;
                countdown--;
                if (fleeing || (countdown > 0 && distance < 240))
                    velocity = RotateTowards(velocity, position - target, .05f);
                else if (countdown < 0 && countdown > -600)
                {
                    float cap = baseSpeed * (bossState is 21 or 22 or 23 ? 2 : 1);
                    if (velocity.Length() < cap) velocity *= 1.02f;
                    if (distance > (masochist ? 360 : 480))
                        velocity = RotateTowards(velocity, target - position, .1f);
                    else countdown = -600;
                }
                if (countdown < -600 && !goodWorld && velocity.Length() > baseSpeed) velocity *= .96f;
                result[t] = position + velocity;
            }
        }
        public static Vector2 NukePreparation(Vector2 player, float top) => new(player.X, Math.Max(player.Y + 700, top + 1500));
        public static int SanctuaryArms(int alpha, float bossAge, bool hasBomb) =>
            Math.Max(1, Math.Max((alpha + 6) / 7, hasBomb ? 1 : 122 - (int)bossAge));
        public static void Boomerang(Vector2[] result, Vector2 start, Vector2 velocity, float speed, float curve)
        {
            result[0] = start;
            if (speed <= 0) speed = velocity.Length();
            for (int t = 1; t < result.Length; t++)
            {
                Vector2 direction = velocity.LengthSquared() > .000001f ? Vector2.Normalize(velocity) : Vector2.UnitX;
                velocity = direction * speed + new Vector2(-direction.Y, direction.X) * curve;
                result[t] = result[t - 1] + velocity;
            }
        }
        public static void Ballistic(Vector2[] result, Vector2 start, Vector2 velocity, float gravity,int legendaryHyper=-1)
        {
            result[0] = start;
            for (int t = 1; t < result.Length; t++)
            {
                velocity.Y += gravity*EncounterClock.Next(ref legendaryHyper);
                result[t] = result[t - 1] + velocity;
            }
        }
        public static int LanceArms(float age) => Math.Max(0, 61 - (int)age);
        public static void Lance(Vector2[] result, Vector2 start, Vector2 velocity, float age, float angle)
        {
            result[0] = start;
            for (int t = 1; t < result.Length; t++)
            {
                // AI moves at age 60; vanilla Damage rejects it until age 61.
                if (age + t >= 60) velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 40;
                result[t] = result[t - 1] + velocity;
            }
        }
        public static void Decay(Vector2[] result, Vector2 start, Vector2 velocity, float factor)
        {
            result[0] = start;
            for (int t = 1; t < result.Length; t++)
            { velocity *= factor; result[t] = result[t - 1] + velocity; }
        }
        public static void Scythe(Vector2[] result, Vector2 start, Vector2 velocity, int timeLeft, float acceleration, float angle)
        {
            result[0] = start;
            for (int t = 1; t < result.Length; t++)
            {
                if (timeLeft - t + 1 < 180)
                {
                    if (velocity == Vector2.Zero) velocity = new(MathF.Cos(angle), MathF.Sin(angle));
                    velocity *= 1 + acceleration;
                }
                result[t] = result[t - 1] + velocity;
            }
        }
    }

    public readonly record struct DecayingShard(Vector2 Start, Vector2 Velocity, int Born)
    {
        static readonly float[] Travel = MakeTravel();
        static float[] MakeTravel()
        {
            var result = new float[CombatPlanner.ForecastTicks + 2];
            float speed = 1;
            for (int t = 1; t < result.Length; t++) { speed *= .985f; result[t] = result[t - 1] + speed; }
            return result;
        }
        public Vector2 At(int tick) => Start + Velocity * Travel[Math.Clamp(tick - Born + 1, 0, Travel.Length - 1)];
        public bool Intersects(Vector2 point, Vector2 halfSize, int tick) => tick >= Born
            // Projectile.Update moves the pillar before decrementing timeLeft. Its
            // death center is exact; the segment covers either child update order.
            && SweptBox.Intersects(At(tick - 1), At(tick), point, halfSize + new Vector2(7));
    }

    public static class PillarBurst
    {
        public static void Forecast(List<DecayingShard> result, Vector2 center, float rotation, int born, bool masochist)
        {
            float speed = masochist ? 5.5f : 5;
            for (int ring = 0; ring < 4; ring++)
                for (int k = 0; k < 24; k++)
                {
                    float angle = rotation + MathF.PI / 2 + MathF.PI / 12 * k;
                    result.Add(new(center, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed * (ring + .5f), born));
                }
        }
    }

    public static class FinalSparkForecast
    {
        // DyingDramaticPause changes -6 to -7 when ++timer > 120.
        // The new fixed upward ray updates on its birth frame; age two already
        // damages in Masochist. Predict it before there is a native projectile.
        public static bool PendingDeathTouches(float timer, int tick, Vector2 center,
            Vector2 player, Vector2 half, bool masochist)
        {
            if(!masochist)return false;
            float age=timer+tick-120;
            float scale=Scale(age,true,255);
            if(scale<5)return false;
            float previousScale=age<=1?1:Scale(age-1,true,255);
            Vector2 origin=center-Vector2.UnitY*(100*previousScale/10+1);
            return TouchesBeam(origin,-Vector2.UnitY,scale,3000,player,half+new Vector2(6));
        }
        public static bool TouchesBeam(Vector2 origin, Vector2 direction, float scale, float length,
            Vector2 player, Vector2 half)
        {
            return (Math.Abs(player.X - origin.X) < 24 + half.X && Math.Abs(player.Y - origin.Y) < 24 + half.Y)
                || FlatBeam.Intersects(origin, direction, length, 22 * scale, player, half);
        }
        public static float Scale(float age, bool masochist,int durationOverride=0)
        {
            float duration = durationOverride>0?durationOverride:masochist ? 780 : 600;
            if (age <= 0 || age >= duration) return 0;
            return Math.Min(10, MathF.Sin(age * MathF.PI / duration) * (masochist ? 350 : 70));
        }
        public static float Chase(float angle, Vector2 origin, Vector2 target, bool fast)
        {
            Vector2 offset = target - origin;
            float error = Wrap(MathF.Atan2(offset.Y, offset.X) - angle);
            float step = Math.Min(MathF.PI / 180 * (fast ? 1.1f : 1), Math.Abs(error)) * Math.Sign(error);
            if (fast)
            {
                step *= 1.1f;
                if (Math.Abs(error * .015f) > Math.Abs(step)) step = error * .015f;
            }
            return angle + step;
        }
        public static float Wrap(float angle) => MathF.Atan2(MathF.Sin(angle), MathF.Cos(angle));
        public static int OrbitSide(float playerAngle, float rayAngle) => Wrap(playerAngle - rayAngle) > 0 ? 1 : -1;
        public static float RadialCost(Vector2 player, Vector2 center, float reserveRadius = 600)
        {
            float outside = Math.Max(0, Vector2.Distance(player, center) - reserveRadius);
            return outside * outside / 100;
        }
        public static Vector2 OrbitGoal(Vector2 player, Vector2 center, int side, float radius = 350)
        {
            Vector2 offset = player - center;
            float angle = MathF.Atan2(offset.Y, offset.X) + side * .6f;
            // A smaller orbit gives the slower downward leg more angular room.
            // Real beam and sphere collision still determine the chosen input.
            return center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
    }

    public static class SweptBox
    {
        public static bool Intersects(Vector2 from, Vector2 to, Vector2 point, Vector2 extent)
        {
            Vector2 min = point - extent, max = point + extent, delta = to - from;
            float enter = 0, leave = 1;
            return Clip(from.X, delta.X, min.X, max.X, ref enter, ref leave)
                && Clip(from.Y, delta.Y, min.Y, max.Y, ref enter, ref leave);
        }
        static bool Clip(float origin, float delta, float min, float max, ref float enter, ref float leave)
        {
            if (Math.Abs(delta) < .00001f) return origin >= min && origin <= max;
            float a = (min - origin) / delta, b = (max - origin) / delta;
            enter = Math.Max(enter, Math.Min(a, b)); leave = Math.Min(leave, Math.Max(a, b));
            return enter <= leave;
        }
    }

    public static class SwordOrbit
    {
        public static Vector2 At(Vector2 center, Vector2 velocity, float radius, float angle,
            float angularSpeed, int tick)
        {
            float a = angle + angularSpeed * tick;
            return center + velocity * tick + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
        }
    }

    public static class LeafOrbit
    {
        public static void Forecast(Vector2[] result, Vector2[] parent, float angle, float launchCountdown)
        {
            for (int tick = 1; tick < result.Length; tick++)
            {
                launchCountdown--;
                result[tick] = parent[tick] + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 100;
                // The installed leaf increments its angle before clamping the visual factor.
                angle += .15f * Math.Max(0, 150 - launchCountdown) / 150;
            }
        }
    }
}

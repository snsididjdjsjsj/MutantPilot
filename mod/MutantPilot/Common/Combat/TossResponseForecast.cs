using System;
using System.Collections.Generic;
using System.Numerics;

namespace MutantPilot.Combat
{
    public readonly record struct TossWorld(Vector2 Position, Vector2 Velocity, float Timer, float OrbitTick,
        float Emitted, bool Closed, PendingSpear? A = null, PendingSpear? B = null, PendingSpear? C = null, PendingSpear? D = null,
        float AimAngle = float.NaN,Vector2? Arena=null,float LockedAim=0,PendingSpear? E=null,PendingSpear? F=null,PendingSpear? G=null,PendingSpear? H=null);

    // An immutable forecast tree, local to one planning call. The float used by
    // ChooseCoupled is an exact small integer reference, never a game field.
    public sealed class TossResponseForecast
    {
        readonly List<TossWorld> worlds = new(8192);
        readonly Dictionary<(int Parent, Vector2 Player, Vector2 Velocity, int Tick), int> cache = new(8192);
        float orbitOffset, orbitSide, throwLimit;
        bool predictive;
        bool firstPhase;
        readonly int[] aiUpdates = new int[CombatPlanner.ForecastTicks + 1];
        public int AimSlot { get; private set; } = -1;
        public int AimRemaining { get; private set; }
        public int Count => worlds.Count;
        public void Reset(TossWorld initial, float offset, float side, float limit, int aimSlot = -1, int aimRemaining = 0, bool predictive = false,
            int legendaryHyper = -1,bool firstPhase=false)
        {
            worlds.Clear(); cache.Clear(); worlds.Add(initial);
            orbitOffset = offset; orbitSide = side; throwLimit = limit;
            AimSlot = aimSlot; AimRemaining = aimRemaining;
            this.predictive=predictive;
            this.firstPhase=firstPhase;
            int phase=legendaryHyper;
            for(int tick=1;tick<aiUpdates.Length;tick++)aiUpdates[tick]=phase<0?1:AdvanceHyper(ref phase);
        }
        // The installed Boss recursively calls AI when ++hyper > 6. The recursive
        // call itself increments hyper to one. NPC displacement still occurs once.
        public static int AdvanceHyper(ref int phase)
        {
            return EncounterClock.Next(ref phase);
        }
        public static TossWorld GameFrame(TossWorld world,MotionState player,int tick,float offset,float side,float limit,
            bool predictive,int updates,bool firstPhase=false)
        {
            Vector2 origin=world.Position;
            for(int update=0;update<updates;update++)
            {
                world=world with {Position=origin};
                world=firstPhase?FirstPhaseStep(world,player,tick,limit):predictive?PredictiveStep(world,player,tick,limit):Step(world,player.Position,tick,offset,side,limit);
            }
            return world with {Position=origin+world.Velocity};
        }
        public TossWorld At(float reference) => worlds[(int)reference];
        public float Advance(float reference, MotionState player, int tick)
        {
            int parent = (int)reference;
            var key = (parent, player.Position, player.Velocity, tick);
            if (cache.TryGetValue(key, out int found)) return found;
            TossWorld next = GameFrame(worlds[parent],player,tick,orbitOffset,orbitSide,throwLimit,predictive,aiUpdates[tick],firstPhase);
            if (AimSlot >= 0 && tick <= AimRemaining && !next.Closed)
            {
                Vector2 delta = player.Position + (predictive||firstPhase ? player.Velocity*30 : Vector2.Zero) - next.Position;
                float desired = MathF.Atan2(delta.Y, delta.X), current = worlds[parent].AimAngle;
                next = next with { AimAngle = predictive||firstPhase ? desired : float.IsFinite(current)
                    ? current + FinalSparkForecast.Wrap(desired - current) * .2f : desired };
            }
            if(worlds[parent].Arena is Vector2 arena)
                next=next with {Arena=firstPhase?Vector2.Lerp(arena,worlds[parent].Position,1-MathF.Pow(.7f,aiUpdates[tick]))
                    :RitualForecast.Step(arena,next.Position,next.Closed?52:predictive?25:41,next.Timer,player.Position)};
            int id = worlds.Count; worlds.Add(next); cache.Add(key, id); return id;
        }
        public static TossWorld FirstPhaseStep(TossWorld world,MotionState player,int tick,float limit)
        {
            if(world.Closed)return world with {Position=world.Position+world.Velocity};
            Vector2 target=player.Position+new Vector2(world.Position.X<player.Position.X?-500:500,0);
            Vector2 velocity=world.Velocity;
            if(Vector2.DistanceSquared(world.Position,target)>50*50)
            {
                if(Math.Abs(world.Position.X-target.X)>10)
                {
                    int sign=world.Position.X<target.X?1:-1;
                    velocity.X+=sign;
                    if(velocity.X*sign<0)velocity.X+=sign*4;
                }
                int vertical=world.Position.Y<target.Y?1:-1;
                velocity.Y+=vertical;
                if(velocity.Y*vertical<0)velocity.Y+=vertical*4;
                velocity=Vector2.Clamp(velocity,new(-36),new(36));
            }
            float timer=world.Timer+1,emitted=world.Emitted,aim=world.LockedAim;bool closed=false;
            if(timer<145)
            {
                Vector2 delta=player.Position+player.Velocity*30-world.Position;
                aim=MathF.Atan2(delta.Y,delta.X);
            }
            if(timer>150)
            {
                timer=60;emitted++;closed=emitted>limit;
                if(closed)
                {
                    Vector2 delta=player.Position-world.Position;
                    velocity=(delta.LengthSquared()>.000001f?Vector2.Normalize(delta):Vector2.UnitY)*2;
                }
                else
                {
                    var spear=new PendingSpear(world.Position,new Vector2(MathF.Cos(aim),MathF.Sin(aim))*25,tick,4);
                    if(world.A==null)world=world with {A=spear};
                    else if(world.B==null)world=world with {B=spear};
                    else if(world.C==null)world=world with {C=spear};
                    else world=world with {D=spear};
                }
                aim=0;
            }
            return world with {Position=world.Position+velocity,Velocity=velocity,Timer=timer,Emitted=emitted,Closed=closed,LockedAim=aim};
        }
        public static TossWorld PredictiveStep(TossWorld world,MotionState player,int tick,float limit)
        {
            if(world.Closed)return world with {Position=world.Position+world.Velocity};
            Vector2 target=player.Position+new Vector2(world.Position.X<player.Position.X?-500:500,0);
            Vector2 velocity=world.Velocity;
            if(Vector2.DistanceSquared(world.Position,target)>25*25)
            {
                const float speed=1.6f;
                if(Math.Abs(world.Position.X-target.X)>10)
                {
                    int sign=world.Position.X<target.X?1:-1;
                    velocity.X+=sign*speed;
                    if(velocity.X*sign<0)velocity.X+=sign*speed*4;
                }
                int vertical=world.Position.Y<target.Y?1:-1;
                velocity.Y+=vertical*speed;
                if(velocity.Y*vertical<0)velocity.Y+=vertical*speed*4;
                velocity=Vector2.Clamp(velocity,new(-36),new(36));
            }
            float timer=world.Timer+1,emitted=world.Emitted;bool closed=false;
            if(timer>60)
            {
                timer=0;emitted++;closed=emitted>limit;
                Vector2 delta=player.Position+player.Velocity*30-world.Position;
                Vector2 direction=delta.LengthSquared()>.000001f?Vector2.Normalize(delta):Vector2.UnitY;
                var spear=new PendingSpear(world.Position,direction*30,tick,4);
                if(world.A==null)world=world with {A=spear};
                else if(world.B==null)world=world with {B=spear};
                else if(world.C==null)world=world with {C=spear};
                else world=world with {D=spear};
            }
            return world with {Position=world.Position+velocity,Velocity=velocity,Timer=timer,Emitted=emitted,Closed=closed};
        }
        public static TossWorld Step(TossWorld world, Vector2 player, int tick, float offset, float side, float limit)
        {
            if (world.Closed) return world with { Position = world.Position + world.Velocity };
            float angle = MathF.PI / 150 * world.OrbitTick * side + offset;
            Vector2 target = player + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 500;
            Vector2 velocity = world.Velocity;
            if (Vector2.DistanceSquared(world.Position, target) > 25 * 25)
            {
                const float speed = 1.2f; // installed Masochist Movement(.6)
                if (Math.Abs(world.Position.X - target.X) > 10)
                {
                    int sign = world.Position.X < target.X ? 1 : -1;
                    velocity.X += sign * speed;
                    if (velocity.X * sign < 0) velocity.X += sign * speed * 4;
                }
                int vertical = world.Position.Y < target.Y ? 1 : -1;
                velocity.Y += vertical * speed;
                if (velocity.Y * vertical < 0) velocity.Y += vertical * speed * 4;
                velocity = Vector2.Clamp(velocity, new(-36), new(36));
            }
            float timer = world.Timer + 1, emitted = world.Emitted;
            bool closed = false, fire = timer == 165;
            if (timer > 180)
            {
                timer = 150; emitted++; closed = emitted > limit;
                fire = true; // Masochist also fires on its last, exiting throw.
            }
            if (fire)
            {
                Vector2 direction = player - world.Position;
                direction = direction.LengthSquared() > .000001f ? Vector2.Normalize(direction) : Vector2.UnitY;
                var spear = new PendingSpear(world.Position, direction * 30, tick, 4);
                if (world.A == null) world = world with { A = spear };
                else if (world.B == null) world = world with { B = spear };
                else if (world.C == null) world = world with { C = spear };
                else if(world.D==null)world=world with {D=spear};
                else if(world.E==null)world=world with {E=spear};
                else if(world.F==null)world=world with {F=spear};
                else if(world.G==null)world=world with {G=spear};
                else world=world with {H=spear};
            }
            return world with { Position = world.Position + velocity, Velocity = velocity,
                Timer = timer, OrbitTick = world.OrbitTick + 1, Emitted = emitted, Closed = closed };
        }
        public bool Touches(float reference, Vector2 player, Vector2 half, int tick)
        {
            TossWorld world = At(reference);
            return Touch(world.A) || Touch(world.B) || Touch(world.C) || Touch(world.D) || Touch(world.E) || Touch(world.F) || Touch(world.G) || Touch(world.H);
            bool Touch(PendingSpear? value) => value is PendingSpear spear && TouchesSpear(spear, player, half, tick);
        }
        public static bool TouchesSpear(PendingSpear spear, Vector2 player, Vector2 half, int tick)
        {
            if (tick < spear.Born) return false;
            if (spear.Touches(player, half, tick)) return true;
            Vector2 direction = Vector2.Normalize(spear.Velocity);
            int age = tick - spear.Born + 1;
            if (age >= 24 && age <= 156)
            {
                Vector2 delta = Vector2.Abs(player - spear.Origin);
                Vector2 rayHalf = half + new Vector2(spear.Padding);
                if (delta.X <= rayHalf.X + 24 && delta.Y <= rayHalf.Y + 24
                    || FlatBeam.Intersects(spear.Origin, direction, 3000, 16, player, rayHalf)
                    || FlatBeam.Intersects(spear.Origin, -direction, 3000, 16, player, rayHalf)) return true;
            }
            float along = Vector2.Dot(player - spear.Origin, direction);
            float spacing=spear.Velocity.Length()*4;
            int nearest = (int)MathF.Round(along / spacing);
            Vector2 normal = new(-direction.Y, direction.X);
            for (int j = Math.Max(0, nearest - 2); j <= nearest + 2; j++)
            {
                int born = spear.Born + 2 * j;
                int orbAge = tick - born;
                if (orbAge < -1 || orbAge > 35) continue;
                bool exploded = orbAge >= 14;
                if (exploded && !SmallBlastTiming.CouldDamage(orbAge - 14)) continue;
                float distance = Math.Clamp(orbAge + 1, 0, 15) * 8;
                float radius = (exploded ? 112 : 41) + spear.Padding;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 center = spear.Origin + direction * (spacing * j) + normal * (distance * side);
                    Vector2 delta = Vector2.Max(Vector2.Zero, Vector2.Abs(player - center) - half);
                    if (delta.LengthSquared() < radius * radius) return true;
                }
            }
            return false;
        }
    }
}

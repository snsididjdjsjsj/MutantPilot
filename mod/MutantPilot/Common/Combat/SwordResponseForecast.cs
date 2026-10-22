using System;
using System.Collections.Generic;
using System.Numerics;

namespace MutantPilot.Combat
{
    public readonly record struct SwordPart(int Slot, float Radius, float BaseAngle, int Remaining);
    public readonly record struct SwordWorld(Vector2 Position, Vector2 Velocity, float Timer, float Angle, bool Preparing,
        Vector2 PreviousPosition = default, float PreviousAngle = 0, int Tick = 0, int Parent = 0,
        int ForcedDeath = -1, Vector2 DeathPosition = default, float DeathAngle = 0,Vector2? Arena=null);

    public sealed class SwordResponseForecast
    {
        readonly List<SwordWorld> worlds = new(8192);
        readonly Dictionary<(int Parent, Vector2 Player, int Tick), int> cache = new(8192);
        readonly List<SwordPart> parts = new(16);
        float angularStep;
        int hyperPhase;
        public int Count => worlds.Count;
        public void Reset(SwordWorld initial, float step, IEnumerable<SwordPart> swords,int legendaryHyper=-1)
        {
            worlds.Clear(); cache.Clear(); worlds.Add(initial);
            parts.Clear(); parts.AddRange(swords); angularStep=step;
            hyperPhase=legendaryHyper;
        }
        public SwordWorld At(float reference) => worlds[(int)reference];
        public float Advance(float reference, MotionState player, int tick)
        {
            int parent=(int)reference;var key=(parent,player.Position,tick);
            if(cache.TryGetValue(key,out int found))return found;
            SwordWorld next=GameFrame(worlds[parent],player.Position,tick,angularStep,EncounterClock.UpdatesAt(hyperPhase,tick)) with {Parent=parent};
            if(worlds[parent].Arena is Vector2 arena)
                next=next with {Arena=next.ForcedDeath<0?RitualForecast.Step(arena,next.Position,45,next.Timer,player.Position):null};
            int id=worlds.Count;worlds.Add(next);cache.Add(key,id);return id;
        }
        public static SwordWorld GameFrame(SwordWorld world,Vector2 player,int tick,float step,int updates)
        {
            SwordWorld before=world;
            for(int update=0;update<updates;update++)world=Step(world with {Position=before.Position},player,tick,step);
            if(before.ForcedDeath<0&&world.ForcedDeath>=0)
                world=world with {DeathPosition=before.Position,DeathAngle=before.Angle};
            return world with {Position=before.Position+world.Velocity,
                PreviousPosition=before.Position,PreviousAngle=before.Angle};
        }
        public static SwordWorld Step(SwordWorld world, Vector2 player, int tick, float step)
        {
            Vector2 velocity=world.Velocity;float timer=world.Timer,angle=world.Angle;
            bool preparing=world.Preparing;int death=world.ForcedDeath;
            Vector2 deathPosition=world.DeathPosition;float deathAngle=world.DeathAngle;
            if(death<0)
            {
                if(preparing)
                {
                    velocity=Vector2.Zero;
                    if(++timer>90)
                    {
                        preparing=false;timer=0;
                        velocity=(player-new Vector2(300*step,0)-world.Position)/20;
                    }
                }
                else
                {
                    angle+=step;
                    if(++timer>20){death=tick;deathPosition=world.Position;deathAngle=world.Angle;}
                }
            }
            return world with {Position=world.Position+velocity,Velocity=velocity,Timer=timer,Angle=angle,Preparing=preparing,
                PreviousPosition=world.Position,PreviousAngle=world.Angle,Tick=tick,ForcedDeath=death,
                DeathPosition=deathPosition,DeathAngle=deathAngle};
        }
        static Vector2 PartAt(Vector2 center,float angle,SwordPart part) => center+
            new Vector2(MathF.Cos(angle+part.BaseAngle),MathF.Sin(angle+part.BaseAngle))*part.Radius;
        public Vector2 Position(float reference, SwordPart part) {var w=At(reference);return PartAt(w.Position,w.Angle,part);}
        public bool Touches(float reference,Vector2 player,Vector2 half,int tick)
        {
            SwordWorld world=At(reference);
            foreach(var part in parts)
            {
                int death=world.ForcedDeath>0?Math.Min(part.Remaining,world.ForcedDeath):part.Remaining;
                if(tick<=part.Remaining&&world.ForcedDeath<0)
                {
                    Vector2 a=PartAt(world.Position,world.Angle,part),b=PartAt(world.PreviousPosition,world.PreviousAngle,part);
                    Vector2 extent=half+new Vector2(32);
                    bool Box(Vector2 p)=>Math.Abs(player.X-p.X)<extent.X&&Math.Abs(player.Y-p.Y)<extent.Y;
                    if(Box(a)||Box(b)||Box((a+b)*.5f))return true;
                }
                if(tick<death||!SmallBlastTiming.CouldDamage(tick-death))continue;
                Vector2 blast;
                if(world.ForcedDeath>0&&world.ForcedDeath<=part.Remaining)
                    blast=PartAt(world.DeathPosition,world.DeathAngle,part);
                else
                {
                    SwordWorld past=world;
                    while(past.Tick>death)past=worlds[past.Parent];
                    blast=PartAt(past.Position,past.Angle,part);
                }
                Vector2 delta=Vector2.Max(Vector2.Zero,Vector2.Abs(player-blast)-half);
                if(delta.LengthSquared()<=104*104)return true;
            }
            return false;
        }
    }
}

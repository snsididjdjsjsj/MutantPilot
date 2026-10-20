using System;
using System.Collections.Generic;
using System.Numerics;

namespace MutantPilot.Combat
{
    public readonly record struct DirectCharge(Vector2 Origin, Vector2 Velocity, int Born, int EndsAt,float TrailSpeed=8);
    public readonly record struct DirectDashWorld(Vector2 Position, Vector2 Velocity, int State, float Timer,
        float Charges, float Limit, DirectCharge? A=null, DirectCharge? B=null, DirectCharge? C=null,float Delay=0,float Aim=0,Vector2? Arena=null);
    public sealed class DirectDashResponseForecast
    {
        readonly List<DirectDashWorld> worlds=new(8192);
        readonly Dictionary<(int Parent,MotionState Player,int Tick),int> cache=new(8192);
        public int Count=>worlds.Count;
        public void Reset(DirectDashWorld initial)
        {
            worlds.Clear();cache.Clear();
            if(initial.State is 6 or 15 or 23)
            {
                bool ordinary=initial.State!=23;
                int elapsed=ordinary?(int)initial.Timer:(int)(initial.Timer*2/3),remaining=ordinary?31-(int)initial.Timer:Remaining(initial.Timer);
                initial=initial with {A=new(initial.Position-initial.Velocity*(elapsed+1),initial.Velocity,-elapsed,remaining-1,
                    initial.State==15&&initial.Charges==initial.Limit?16:8)};
            }
            worlds.Add(initial);
        }
        public DirectDashWorld At(float reference)=>worlds[(int)reference];
        public float Advance(float reference,MotionState player,int tick)
        {
            var key=((int)reference,player,tick);
            if(cache.TryGetValue(key,out int found))return found;
            var before=worlds[(int)reference];var next=Step(before,player,tick);
            if(before.Arena is Vector2 arena)
                next=next with {Arena=RitualForecast.Step(arena,next.Position,next.State,next.Timer,player.Position)};
            int id=worlds.Count;worlds.Add(next);cache.Add(key,id);return id;
        }
        static float AdvanceTimer(float timer)=>timer+(timer%3==0?2:1);
        static int Remaining(float timer)
        {
            int result=0;
            do {timer=AdvanceTimer(timer);result++;}while(timer<=30);
            return result;
        }
        static Vector2 Move(Vector2 position,Vector2 velocity,Vector2 target,float speed,bool fastX)
        {
            if(Math.Abs(position.X-target.X)>10)
            {
                int direction=position.X<target.X?1:-1;velocity.X+=speed*direction;
                if(velocity.X*direction<0)velocity.X+=speed*direction*(fastX?4:2);
            }
            int vertical=position.Y<target.Y?1:-1;velocity.Y+=speed*vertical;
            if(velocity.Y*vertical<0)velocity.Y+=speed*vertical*4;
            return Vector2.Clamp(velocity,new(-36),new(36));
        }
        public static DirectDashWorld Step(DirectDashWorld world,MotionState player,int tick)
        {
            int state=world.State;float timer=world.Timer,charges=world.Charges;
            Vector2 position=world.Position,velocity=world.Velocity;
            if(state==14)
            {
                float limit=world.Limit>0?world.Limit:3;
                if(timer==0 && charges==limit-1)
                {
                    if(Vector2.DistanceSquared(position,player.Position)>450*450)
                    {
                        velocity=Move(position,velocity,player.Position,1.2f,true);
                        return world with {Position=position+velocity,Velocity=velocity};
                    }
                    velocity*=.75f;
                }
                velocity*=.9f;
                float aim=world.Aim;
                if(timer<55)
                {
                    Vector2 delta=player.Position+player.Velocity*30-position;
                    aim=MathF.Atan2(delta.Y,delta.X);
                }
                int wait=charges==limit-1?80:60;
                if(charges==0||charges>=limit)wait=0;
                if(++timer>wait)
                {
                    timer=0;charges++;
                    if(charges>limit)state=52;
                    else
                    {
                        state=15;velocity=new Vector2(MathF.Cos(aim),MathF.Sin(aim))*45;
                        var charge=new DirectCharge(position,velocity,tick,tick+30,charges==limit?16:8);
                        if(world.A==null)world=world with {A=charge};
                        else if(world.B==null)world=world with {B=charge};
                        else world=world with {C=charge};
                    }
                    aim=0;
                }
                world=world with {Aim=aim};
            }
            else if(state==21)
            {
                if(++timer>180){state=22;timer=0;}
                Vector2 target=player.Position+new Vector2(0,450*Math.Sign(position.Y-player.Position.Y));
                velocity=Move(position,velocity,target,1.4f,false);
                if(Vector2.DistanceSquared(position,player.Position)<200*200)
                {
                    Vector2 away=position-player.Position;
                    if(away!=Vector2.Zero)velocity=Move(position,velocity,position+Vector2.Normalize(away),2.8f,true);
                }
            }
            else if(state is 5 or 22)
            {
                bool p1=state==5;
                velocity*=.9f;
                if(++timer>(p1?world.Delay:5))
                {
                    timer=0;charges++;
                    // The first random count is not guessed. Its minimum is three,
                    // and no unknown random next attack is emitted after that limit.
                    if(charges>(p1?5:world.Limit>0?world.Limit:3))state=p1?-100:52;
                    else
                    {
                        state=p1?6:23;Vector2 delta=player.Position+(p1?player.Velocity:Vector2.Zero)-position;
                        velocity=(delta.LengthSquared()>1e-6f?Vector2.Normalize(delta):Vector2.UnitY)*(p1?45:60);
                        var charge=new DirectCharge(position,velocity,tick,tick+(p1?31:Remaining(0))-1);
                        if(world.A==null)world=world with {A=charge};
                        else if(world.B==null)world=world with {B=charge};
                        else world=world with {C=charge};
                    }
                }
            }
            else if(state is 6 or 15 or 23)
            {
                int old=state;timer=state==23?AdvanceTimer(timer):timer+1;
                if(timer>30)
                {
                    state=old==6?5:old==15?14:22;timer=0;
                    if(old==15 && charges==world.Limit-1 && Vector2.DistanceSquared(position,player.Position)>450*450)
                        velocity=Vector2.Normalize(player.Position-position)*16;
                }
            }
            else if(state==52)
            {
                Vector2 away=position-player.Position;
                Vector2 target=player.Position+(away.LengthSquared()>1e-6f?Vector2.Normalize(away):Vector2.UnitY)*400;
                velocity=Move(position,velocity,target,.6f,true);
                if(Vector2.DistanceSquared(position,target)>200*200)velocity=Move(position,velocity,target,.6f,true);
                if(++timer>60 || Vector2.DistanceSquared(position,target)<200*200&&timer>15)state=-100;
            }
            else velocity*=.9f;
            return world with {Position=position+velocity,Velocity=velocity,State=state,Timer=timer,Charges=charges};
        }
        public bool Touches(float reference,Vector2 player,Vector2 half,int tick)
        {
            var world=At(reference);
            return Touch(world.A)||Touch(world.B)||Touch(world.C);
            bool Touch(DirectCharge? value)
            {
                if(value is not DirectCharge charge||tick<charge.Born)return false;
                Vector2 direction=Vector2.Normalize(charge.Velocity),normal=new(-direction.Y,direction.X);
                int age=tick-charge.Born;
                if(tick<=charge.EndsAt)
                {
                    Vector2 tip=charge.Origin+charge.Velocity*(age+2)+direction;
                    Vector2 delta=Vector2.Abs(player-tip);
                    if(delta.X<8+half.X && delta.Y<8+half.Y)return true;
                    if(FlatBeam.Intersects(tip-direction*100,direction,200,8,player,half))return true;
                }
                // Existing rays are already tracked natively; only future births
                // in this response tree add the paired fixed-origin beams.
                if(charge.Born>0)
                {
                    float scale=ShortRayTiming.Scale(2,age+1);
                    if(ShortRayTiming.Damages(2,scale))
                    {
                        Vector2 delta=Vector2.Abs(player-charge.Origin);
                        if(delta.X<=24+half.X&&delta.Y<=24+half.Y
                            ||FlatBeam.Intersects(charge.Origin,direction,3000,22*scale,player,half)
                            ||FlatBeam.Intersects(charge.Origin,-direction,3000,22*scale,player,half))return true;
                    }
                }
                int interval=charge.TrailSpeed>8?2:3;
                for(int born=Math.Max(charge.Born,charge.Born+interval*((1-charge.Born+interval-1)/interval));born<=Math.Min(tick,charge.EndsAt);born+=interval)
                {
                    if(born<1)continue;
                    int orbAge=tick-born;bool exploded=orbAge>=14;
                    if(exploded&&!SmallBlastTiming.CouldDamage(orbAge-14))continue;
                    Vector2 origin=charge.Origin+charge.Velocity*(born-charge.Born+2);
                    float travel=Math.Min(15,orbAge+1)*charge.TrailSpeed,radius=exploded?112:41;
                    for(int side=-1;side<=1;side+=2)
                    {
                        Vector2 delta=Vector2.Max(Vector2.Zero,Vector2.Abs(player-(origin+normal*(side*travel)))-half);
                        if(delta.LengthSquared()<radius*radius)return true;
                    }
                }
                return false;
            }
        }
    }
}

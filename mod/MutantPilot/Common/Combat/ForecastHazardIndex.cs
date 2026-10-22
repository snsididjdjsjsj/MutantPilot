using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace MutantPilot.Combat
{
    // Freeze state-independent future geometry once per game tick. A spatial lookup only removes
    // zero-cost entries; exact tests, soft clearance, multiplicity and accumulation order are retained.
    public sealed class ForecastHazardIndex
    {
        enum Shape { Box, Segment, Circle }
        readonly record struct Primitive(Shape Kind, Vector2 A, Vector2 B, float Radius);
        readonly TemporalBuckets buckets;
        readonly List<Primitive>[] layers;
        readonly List<int>[] globals;
        Vector2 half;
        float reserve;
        readonly ParallelOptions workers = new() { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) };
        public int WorkerCount
        {
            get=>workers.MaxDegreeOfParallelism;
            set=>workers.MaxDegreeOfParallelism=Math.Clamp(value,1,Math.Max(1,Math.Min(16,Environment.ProcessorCount)));
        }
        public bool LastBuildParallel { get; private set; }
        public ForecastHazardIndex(int horizon)
        {
            buckets = new(horizon); layers = new List<Primitive>[horizon+1]; globals = new List<int>[horizon+1];
            for (int t=0; t<layers.Length; t++) { layers[t] = new(128); globals[t] = new(); }
        }
        public void Clear(Vector2 playerHalf, float clearance)
        {
            half = playerHalf; reserve = clearance; buckets.Clear();
            for (int t=0; t<layers.Length; t++) { layers[t].Clear(); globals[t].Clear(); }
        }
        static bool Finite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
        void Add(int tick, Primitive p, Vector2 minimum, Vector2 maximum)
        {
            int id = layers[tick].Count; layers[tick].Add(p);
            if (!Finite(minimum) || !Finite(maximum) || maximum.X-minimum.X>8192 || maximum.Y-minimum.Y>8192)
                globals[tick].Add(id);
            else buckets.Add(id,tick,minimum-new Vector2(1),maximum+new Vector2(1));
        }
        public void Add(TimedBox box)
        {
            Vector2 extent=half+box.HalfSize;
            for(int t=Math.Max(1,box.ArmsAt);t<=Math.Min(layers.Length-1,box.EndsAt);t++)
                Add(t,new(Shape.Box,box.Center,box.HalfSize,0),box.Center-extent,box.Center+extent);
        }
        public void Add(DecayingShard shard)
        {
            Vector2 extent=half+new Vector2(7);
            for(int t=Math.Max(1,shard.Born);t<layers.Length;t++)
            {
                Vector2 a=shard.At(t-1),b=shard.At(t);
                Add(t,new(Shape.Segment,a,b,0),Vector2.Min(a,b)-extent,Vector2.Max(a,b)+extent);
            }
        }
        public void Add(TrailOrb orb)
        {
            for(int t=Math.Max(1,orb.Born);t<=Math.Min(layers.Length-1,orb.Explodes+21);t++)
            {
                bool exploded=t>=orb.Explodes;
                if(exploded && !SmallBlastTiming.CouldDamage(t-orb.Explodes))continue;
                AddCircle(t,orb.CenterAt(exploded?orb.Explodes:t),(exploded?112:41)+orb.Padding);
            }
        }
        public void AddExplosion(Vector2 center,int at)
        {
            for(int t=1;t<layers.Length;t++)
                if(SmallBlastTiming.CouldDamage(t-at))AddCircle(t,center,104);
        }
        public void Build(IReadOnlyList<TimedBox> boxes, IReadOnlyList<DecayingShard> shards,
            IReadOnlyList<TrailOrb> orbs, IReadOnlyList<(Vector2 Center,int At)> explosions, bool allowParallel = true)
        {
            // Inputs are value-only forecast data. The game thread waits for this bounded build;
            // workers never read Terraria entities, invoke native collisions or publish stale plans.
            void Layer(int tick)
            {
                for(int i=0;i<boxes.Count;i++)
                {
                    var box=boxes[i];
                    if(tick<box.ArmsAt || tick>box.EndsAt)continue;
                    Vector2 extent=half+box.HalfSize;
                    Add(tick,new(Shape.Box,box.Center,box.HalfSize,0),box.Center-extent,box.Center+extent);
                }
                for(int i=0;i<shards.Count;i++)
                {
                    var shard=shards[i];
                    if(tick<shard.Born)continue;
                    Vector2 a=shard.At(tick-1),b=shard.At(tick),extent=half+new Vector2(7);
                    Add(tick,new(Shape.Segment,a,b,0),Vector2.Min(a,b)-extent,Vector2.Max(a,b)+extent);
                }
                for(int i=0;i<orbs.Count;i++)
                {
                    var orb=orbs[i];
                    if(tick<orb.Born || tick>orb.Explodes+21)continue;
                    bool exploded=tick>=orb.Explodes;
                    if(exploded && !SmallBlastTiming.CouldDamage(tick-orb.Explodes))continue;
                    AddCircle(tick,orb.CenterAt(exploded?orb.Explodes:tick),(exploded?112:41)+orb.Padding);
                }
                for(int i=0;i<explosions.Count;i++)
                {
                    var blast=explosions[i];
                    if(SmallBlastTiming.CouldDamage(tick-blast.At))AddCircle(tick,blast.Center,104);
                }
            }
            LastBuildParallel=allowParallel && workers.MaxDegreeOfParallelism>1 && boxes.Count+shards.Count+orbs.Count+explosions.Count>=128;
            if(LastBuildParallel)Parallel.For(1,layers.Length,workers,Layer);
            else for(int t=1;t<layers.Length;t++)Layer(t);
        }
        void AddCircle(int tick,Vector2 center,float radius)
        {
            Vector2 extent=half+new Vector2(radius+Math.Max(0,reserve));
            Add(tick,new(Shape.Circle,center,default,radius),center-extent,center+extent);
        }
        void Apply(ref float cost,Primitive p,Vector2 point)
        {
            switch(p.Kind)
            {
                case Shape.Box:
                    if(Math.Abs(point.X-p.A.X)<=p.B.X+half.X && Math.Abs(point.Y-p.A.Y)<=p.B.Y+half.Y)cost+=1000000;
                    break;
                case Shape.Segment:
                    if(SweptBox.Intersects(p.A,p.B,point,half+new Vector2(7)))cost+=1000000;
                    break;
                case Shape.Circle:
                    float dx=Math.Max(0,Math.Abs(point.X-p.A.X)-half.X),dy=Math.Max(0,Math.Abs(point.Y-p.A.Y)-half.Y);
                    if(dx*dx+dy*dy<p.Radius*p.Radius)cost+=1000000;
                    cost+=ThreatClearance.Circle(point,p.A,half,p.Radius,reserve);
                    break;
            }
        }
        public void Accumulate(ref float cost,Vector2 point,int tick)
        {
            if(!Finite(point)) { foreach(var p in layers[tick])Apply(ref cost,p,point); return; }
            var nearby=buckets.At(point,tick); var fallback=globals[tick]; var layer=layers[tick];
            int a=0,b=0,n=nearby?.Count??0;
            // Both lists contain original insertion ids in ascending order.
            while(a<n || b<fallback.Count)
            {
                int id=b==fallback.Count || a<n && nearby![a]<fallback[b] ? nearby![a++] : fallback[b++];
                Apply(ref cost,layer[id],point);
            }
        }
    }
}

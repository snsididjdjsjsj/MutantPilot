using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace MutantPilot.Combat
{
    public static class EncounterClock
    {
        public static int UpdatesAt(int phase,int tick)
        {
            int first=7-phase;
            return phase>=0&&tick>=first&&(tick-first)%6==0?2:1;
        }
        public static int Next(ref int phase)
        {
            if(phase<0)return 1;
            if(++phase<=6)return 1;
            phase=1;return 2;
        }
    }
    // This core has no Terraria dependency: replay and regression checks run without the game.
    public readonly record struct Move(int Horizontal, int Vertical)
    {
        // Vertical: 0 release/fall, 1 fly, 2 fast fly (up+jump), 3 hover (down+jump).
        public int Id => Horizontal + 1 + 3 * Vertical;
        public static Move FromId(int id) => new(id % 3 - 1, id / 3);
    }

    public readonly record struct DashState(int Type, int Active, int Delay, int Tap,
        bool LeftReleased, bool RightReleased);
    public readonly record struct MotionState(Vector2 Position, Vector2 Velocity, DashState Dash = default);
    public readonly record struct PlanFrame(MotionState Player, Vector2 Goal, Vector2 Boss,
        float AttackRange, bool Damageable);
    public readonly record struct GuideTrack(Vector2[] Positions, int StartsAt, int EndsAt, float HalfSize);
    public readonly record struct Decision(Move Move, MotionState Next, int Evaluations,
        int Horizon, double Milliseconds, float Cost, int FirstHit, int RescueDepth, int ForecastFirstHit,
        int DangerQueries, int CachedQueries, float NextAngle = 0);

    public readonly record struct RouteCode(UInt128 Low, UInt128 High)
    {
        public static implicit operator RouteCode(int value)=>new((UInt128)(uint)value,0);
        public RouteCode Append(int segment,int input)=>segment<32
            ?this with {Low=Low|((UInt128)(uint)input<<(segment*4))}
            :this with {High=High|((UInt128)(uint)input<<((segment-32)*4))};
        public int At(int segment)=>(int)(((segment<32?Low>>(segment*4):High>>((segment-32)*4)))&15);
    }
    public sealed class MotionModel
    {
        public float MaxX = 18, Acceleration = .75f, Braking = 1;
        public float StopBraking = float.NaN;
        public float Gravity = .4f, MaxFall = 10, MaxRise = 13, RiseAcceleration = .6f;
        public bool FlightMastery;
        public float WalkSpeed = 8.52f, JumpSpeed = 13.91f;
        public Vector2 WorldMinimum = new(float.NegativeInfinity), WorldMaximum = new(float.PositiveInfinity);
        public float Error { get; private set; } = 2;
        readonly float[] verticalDelta = new float[4];
        readonly int[] samples = new int[4];
        float hoverVelocity;
        int hoverSamples;

        public void Observe(MotionState before, MotionState actual, Move move, MotionState predicted)
        {
            float error = Vector2.Distance(predicted.Position, actual.Position);
            // Teleports, collisions and equipment changes must not train the free-flight model.
            Error = .9f * Error + .1f * Math.Min(error, 24);
            if (Vector2.Distance(before.Position, actual.Position) > Math.Max(128, actual.Velocity.Length() * 3)) return;
            float dv = actual.Velocity.Y - before.Velocity.Y;
            if (move.Vertical == 3)
            {
                hoverVelocity = hoverSamples == 0 ? actual.Velocity.Y : .8f * hoverVelocity + .2f * actual.Velocity.Y;
                hoverSamples++;
            }
            if (move.Vertical is 1 or 2 && Math.Abs(dv) < 64 && Math.Abs(dv) > .01f)
            {
                int v = move.Vertical;
                if (dv < 0)
                {
                    verticalDelta[v] = samples[v] == 0 ? -dv : .9f * verticalDelta[v] + .1f * -dv;
                    samples[v]++;
                }
            }
            if (!FlightMastery && actual.Velocity.Y < -.5f && move.Vertical is 1 or 2)
                MaxRise = Math.Max(MaxRise * .999f, -actual.Velocity.Y);
        }

        public MotionState Step(MotionState state, Move move)
        {
            Vector2 v = state.Velocity;
            DashState dash = state.Dash;
            if (FlightMastery)
            {
                // Terraria's wing flight has different acceleration above walking speed,
                // extra braking on reversal, and half braking on release.
                int h = move.Horizontal;
                bool dashing = dash.Active == 1 && dash.Delay < 0;
                float braking = dashing || h * v.X > 0 || float.IsNaN(StopBraking) ? Braking : StopBraking;
                if (h != 0 && h * v.X < WalkSpeed)
                {
                    if (h * v.X < -braking) v.X += h * braking;
                    v.X += h * Acceleration;
                }
                else if (h != 0 && h * v.X < MaxX && !dashing) v.X += h * Acceleration * .4f;
                else v.X = Approach(v.X, 0, braking * .5f);
                if (dash.Type == 1 || dash.Active == 1) (v.X, dash) = TabiDash(v.X, dash, h);
                float offsetY = 0;
                if (move.Vertical == 3)
                {
                    // FlightMasterySoul.AddEffects cancels the incoming displacement before
                    // vanilla hover applies. This models the effect; no live motion is edited.
                    offsetY = -v.Y;
                    v.Y = .00001f;
                }
                else if (move.Vertical is 1 or 2)
                {
                    float multiplier = move.Vertical == 2 ? 6 : 1;
                    v.Y -= .135f * multiplier;
                    if (v.Y > 0) v.Y -= .85f * (Gravity > .5f ? 1.5f : 1) * multiplier;
                    else if (v.Y > -JumpSpeed) v.Y -= .25f * multiplier;
                    v.Y = Math.Max(-JumpSpeed * 1.75f, v.Y);
                }
                else v.Y = Math.Min(MaxFall, v.Y + Gravity);
                return Borders(new(state.Position + v + new Vector2(0, offsetY), v, dash));
            }
            float targetX = move.Horizontal * MaxX;
            float ax = move.Horizontal == 0 || move.Horizontal * v.X < 0 ? Braking : Acceleration;
            v.X = Approach(v.X, targetX, Math.Max(.05f, ax));
            if (move.Vertical == 3) v.Y = hoverSamples > 3 ? hoverVelocity : 0;
            else if (move.Vertical is 1 or 2)
            {
                float a = samples[move.Vertical] > 0 ? verticalDelta[move.Vertical]
                    : RiseAcceleration * (move.Vertical == 2 ? 6 : 1);
                v.Y = Math.Max(-MaxRise, v.Y - Math.Max(.05f, a));
            }
            else v.Y = Math.Min(MaxFall, v.Y + Gravity);
            return Borders(new(state.Position + v, v));
        }

        MotionState Borders(MotionState state)
        {
            Vector2 p = state.Position, v = state.Velocity;
            if (p.X < WorldMinimum.X) { p.X = WorldMinimum.X; v.X = 0; }
            if (p.X > WorldMaximum.X) { p.X = WorldMaximum.X; v.X = 0; }
            if (p.Y < WorldMinimum.Y) { p.Y = WorldMinimum.Y; if (v.Y < .11f) v.Y = .11f; }
            if (p.Y > WorldMaximum.Y) { p.Y = WorldMaximum.Y; v.Y = 0; }
            return state with { Position = p, Velocity = v };
        }

        (float, DashState) TabiDash(float vx, DashState state, int horizontal)
        {
            // Pure forecast of normal double-tap input. Live dash timers/velocity are never written.
            int active = state.Delay == 0 ? state.Type : state.Active;
            int delay = state.Delay, tap = state.Tap;
            if (active == 0) { delay = 0; tap = 0; }
            else if (delay > 0) delay--;
            else if (delay < 0)
            {
                if (Math.Abs(vx) > 12) vx *= .992f;
                else if (Math.Abs(vx) > MaxX) vx *= .96f;
                else { delay = 20; vx = Math.Sign(vx) * MaxX; }
            }
            else
            {
                tap -= Math.Sign(tap);
                if (horizontal > 0 && state.RightReleased)
                {
                    if (tap > 0) { vx = 16.9f; delay = -1; tap = 0; }
                    else tap = 15;
                }
                else if (horizontal < 0 && state.LeftReleased)
                {
                    if (tap < 0) { vx = -16.9f; delay = -1; tap = 0; }
                    else tap = -15;
                }
            }
            return (vx, new(state.Type, active, delay, tap, horizontal >= 0, horizontal <= 0));
        }

        static float Approach(float value, float target, float amount) =>
            value < target ? Math.Min(value + amount, target) : Math.Max(value - amount, target);
    }

    public sealed class CombatPlanner
    {
        public const int ForecastTicks = 72;
        // Hazards past SafetyTicks are discounted to 1% of their value (max 500k), i.e. below the hit
        // threshold, so THIS is the real avoidance horizon, not ForecastTicks. Measured behaviour:
        // every recorded trap closed 21 to 34 ticks before impact, right at this edge.
        // Raising it to 36 does what it says (predictedFirstHit then reports out to 36 ticks, 91 frames
        // in one fight where that was previously impossible) but the one measured fight scored 8 hits,
        // inside the 4-8 noise band, at p95 10.1ms. Unproven, so it stays at 24.
        public const int SafetyTicks = 24;
        static float TimedRisk(float risk, int tick) => tick <= SafetyTicks ? risk : Math.Min(500000, risk * .01f);
        readonly record struct Node(MotionState State, Move First, Move Last, float Cost, int FirstHit, int ForecastFirstHit);
        readonly record struct BeamNode(MotionState State, Move First, Move Last, float Cost,
            int FirstHit, int ForecastFirstHit, RouteCode Code, float Score, float Angle);
        readonly List<Node> candidates = new(12);
        readonly List<BeamNode> beam = new(12);
        readonly List<BeamNode> branches = new(144);
        readonly Dictionary<(MotionState State, float Angle, int Tick), float> riskCache = new(8192);
        readonly Dictionary<(Vector2 Position, int Tick), float> positionRiskCache = new(8192);
        readonly Move[] solution = new Move[ForecastTicks];
        readonly Move[] trial = new Move[ForecastTicks];
        readonly Move[] bestSequence = new Move[ForecastTicks];
        readonly Move[] navigationSequence = new Move[ForecastTicks];
        bool haveSolution;
        public float NavigationCost { get; private set; }
        public int NavigationFirstHit { get; private set; }
        public int NavigationForecastFirstHit { get; private set; }
        public Vector2 NavigationEnd { get; private set; }
        public bool NavigationChosen { get; private set; }
        public int ReactiveProposals { get; private set; }
        public int DetourProposals { get; private set; }
        public int RetainedForecastFirstHit { get; private set; }
        public int[] SelectedRoute => Array.ConvertAll(solution,move=>move.Id);
        public float GoalPullDivisor { get; set; } = 250000;
        public int ProposalBudget { get; set; }
        public int RescueBeamWidth { get; set; } = 12;
        public int RescueSegmentTicks { get; set; } = 3;
        public bool DiverseRescue { get; set; }
        public int RescueFrontierPeak { get; private set; }

        static int Compare(Node a, Node b)
        {
            int safeA = a.FirstHit == 0 ? int.MaxValue : a.FirstHit;
            int safeB = b.FirstHit == 0 ? int.MaxValue : b.FirstHit;
            int safety = safeB.CompareTo(safeA);
            if (safety != 0) return safety;
            int forecastA = a.ForecastFirstHit == 0 ? int.MaxValue : a.ForecastFirstHit;
            int forecastB = b.ForecastFirstHit == 0 ? int.MaxValue : b.ForecastFirstHit;
            int futureSafety = forecastB.CompareTo(forecastA);
            return futureSafety != 0 ? futureSafety : a.Cost.CompareTo(b.Cost);
        }

        public Decision Choose(PlanFrame frame, MotionModel motion,
            Func<Vector2, int, float> danger, Move previous, double budgetMs = 4, double rescueBudgetMs = 16)
            => ChooseCore(frame, motion, (state, tick, _) => danger(state.Position, tick), false, 0, null,
                previous, budgetMs, rescueBudgetMs);

        public Decision ChooseWithState(PlanFrame frame, MotionModel motion,
            Func<MotionState, int, float> danger, Move previous, double budgetMs = 4, double rescueBudgetMs = 16)
            => ChooseCore(frame, motion, (state, tick, _) => danger(state, tick), true, 0, null,
                previous, budgetMs, rescueBudgetMs);

        public Decision ChooseCoupled(PlanFrame frame, MotionModel motion, float initialAngle,
            Func<float, MotionState, int, float> advance, Func<MotionState, int, float, float> danger,
            Move previous, double budgetMs = 4, double rescueBudgetMs = 16, Vector2[] goals = null!)
            => ChooseCore(frame, motion, danger, true, initialAngle, advance, previous, budgetMs, rescueBudgetMs, goals);

        public Decision ChooseTracking(PlanFrame frame, MotionModel motion,
            Func<MotionState, int, float> danger, Vector2[] goals, Move previous,
            double budgetMs = 4, double rescueBudgetMs = 16)
        {
            if (goals.Length != ForecastTicks + 1) throw new ArgumentException("Tracking needs a goal for every forecast tick.");
            return ChooseCore(frame, motion, (state, tick, _) => danger(state, tick), true, 0, null!,
                previous, budgetMs, rescueBudgetMs, goals);
        }

        Decision ChooseCore(PlanFrame frame, MotionModel motion, Func<MotionState, int, float, float> danger,
            bool coupled, float initialAngle, Func<float, MotionState, int, float> advance,
            Move previous, double budgetMs, double rescueBudgetMs, Vector2[] goalPath = null!)
        {
            RescueFrontierPeak=0;
            Vector2 GoalAt(int tick) => goalPath == null ? frame.Goal : goalPath[Math.Min(tick, ForecastTicks)];
            var watch = Stopwatch.StartNew();
            ReactiveProposals = 0;
            DetourProposals = 0;
            RetainedForecastFirstHit = -1;
            int evaluations = 0;
            int cachedQueries = 0;
            if (coupled) riskCache.Clear(); else positionRiskCache.Clear();
            // The observed world is fixed during one Choose call. Shared prefixes
            // produce exactly identical floats; no grid approximation or stale frame
            // results are used. Reuse only the expensive collision query, not scores.
            float RiskAt(MotionState state, int tick, float angle)
            {
                // Response-dependent hazards can distinguish identical positions
                // reached with different velocities or prior beam trajectories.
                float risk;
                if (coupled)
                {
                    var key = (state, angle, tick);
                    if (riskCache.TryGetValue(key, out risk)) { cachedQueries++; return risk; }
                    risk = danger(state, tick, angle); riskCache.Add(key, risk);
                }
                else
                {
                    var key = (state.Position, tick);
                    if (positionRiskCache.TryGetValue(key, out risk)) { cachedQueries++; return risk; }
                    risk = danger(state, tick, angle); positionRiskCache.Add(key, risk);
                }
                return risk;
            }
            Node best = default;
            bool haveBest = false;
            Node Rollout(Move first, Move second, int changeAt, bool sequence = false)
            {
                MotionState state = frame.Player;
                float angle = initialAngle;
                float cost = first != previous ? .12f : 0;
                int firstHit = 0;
                int forecastFirstHit = 0;
                Move last = first;
                for (int future = 1; future <= ForecastTicks; future++)
                {
                    Move move = sequence ? trial[future - 1] : future <= changeAt ? first : second;
                    if (move != last) cost += .12f;
                    last = move;
                    state = motion.Step(state, move);
                    if (advance != null) angle = advance(angle, state, future);
                    float rawRisk = RiskAt(state, future, angle);
                    if (forecastFirstHit == 0 && rawRisk >= 1000000) forecastFirstHit = future;
                    float risk = TimedRisk(rawRisk, future);
                    if (firstHit == 0 && risk >= 1000000) firstHit = future;
                    cost += risk / (1 + .02f * future);
                    // Goal pull must outbid the flat 0.12 move-change penalty, otherwise standing
                    // still is rational: at the 192px clamp the old /250000f was worth 0.147, i.e. the
                    // same order, and the player idled on 15.5% of ticks (longest stall 150 ticks).
                    cost += Vector2.DistanceSquared(state.Position, GoalAt(future))
                        / Math.Max(10000f, GoalPullDivisor);
                    if (frame.Damageable)
                    {
                        float outside = Math.Max(0, Vector2.Distance(state.Position, frame.Boss) - frame.AttackRange);
                        cost += outside * outside / 40000f;
                    }
                    evaluations++;
                    // A collision cannot be undone later in this rollout. Strictly
                    // dominated candidates need no more expensive collision queries.
                    if (firstHit != 0 && haveBest && (best.FirstHit == 0 || firstHit < best.FirstHit)) break;
                    if (firstHit == 0 && forecastFirstHit != 0 && haveBest && best.FirstHit == 0
                        && (best.ForecastFirstHit == 0 || forecastFirstHit < best.ForecastFirstHit)) break;
                }
                return new(state, first, second, cost, firstHit, forecastFirstHit);
            }
            void Consider(Node node, int changeAt, bool sequence = false)
            {
                if (haveBest && Compare(node, best) >= 0) return;
                best = node; haveBest = true;
                if (sequence) Array.Copy(trial, bestSequence, ForecastTicks);
                else for (int t = 0; t < ForecastTicks; t++) bestSequence[t] = t < changeAt ? node.First : node.Last;
            }
            void NavigateGoal(Vector2? waypoint=null,int until=ForecastTicks)
            {
                MotionState state = frame.Player;
                for (int t = 0; t < ForecastTicks; t++)
                {
                    bool detouring=waypoint.HasValue && t<until;
                    Vector2 target = detouring ? waypoint.Value : GoalAt(t + 1);
                    Vector2 delta = (target - state.Position) * .12f;
                    if (goalPath != null && !detouring) delta += GoalAt(t + 2) - target;
                    Vector2 desired = new(Math.Clamp(delta.X, -motion.MaxX, motion.MaxX),
                        Math.Clamp(delta.Y, -(motion.FlightMastery ? motion.JumpSpeed * 1.75f : motion.MaxRise), motion.MaxFall));
                    float bestSteering = float.PositiveInfinity;
                    MotionState selected = state;
                    Move input = default;
                    for (int id = 0; id < 12; id++)
                    {
                        Move candidate = Move.FromId(id);
                        MotionState next = motion.Step(state, candidate);
                        float score = Vector2.DistanceSquared(next.Position, target)
                            + 16 * Vector2.DistanceSquared(next.Velocity, desired);
                        if (score >= bestSteering) continue;
                        bestSteering = score; selected = next; input = candidate;
                    }
                    trial[t] = input; state = selected;
                }
                // Propose braking and re-acceleration together, then apply the
                // same full danger evaluation as every other candidate route.
                Node navigation = Rollout(trial[0], trial[^1], 0, true);
                if(!waypoint.HasValue)
                {
                    Array.Copy(trial, navigationSequence, ForecastTicks);
                    NavigationCost = navigation.Cost; NavigationFirstHit = navigation.FirstHit;
                    NavigationForecastFirstHit = navigation.ForecastFirstHit; NavigationEnd = navigation.State.Position;
                }
                Consider(navigation, 0, true);
            }
            bool NavigateHazards(int lookAhead,double deadline)
            {
                ReactiveProposals++;
                MotionState state=frame.Player;float angle=initialAngle;
                for(int t=1;t<=ForecastTicks;t++)
                {
                    if(watch.Elapsed.TotalMilliseconds>=deadline)return false;
                    Vector2 target=GoalAt(t),delta=(target-state.Position)*.12f;
                    if(goalPath!=null)delta+=GoalAt(t+1)-target;
                    Vector2 desired=new(Math.Clamp(delta.X,-motion.MaxX,motion.MaxX),
                        Math.Clamp(delta.Y,-(motion.FlightMastery?motion.JumpSpeed*1.75f:motion.MaxRise),motion.MaxFall));
                    int safest=-1;float lowest=float.PositiveInfinity;
                    Move selected=default;MotionState selectedState=state;float selectedAngle=angle;
                    for(int id=0;id<12;id++)
                    {
                        Move move=Move.FromId(id);MotionState next=motion.Step(state,move);
                        float nextAngle=advance==null?angle:advance(angle,next,t);
                        MotionState peek=next;float peekAngle=nextAngle,penalty=0;int firstBad=int.MaxValue;
                        for(int lead=0;lead<lookAhead && t+lead<=ForecastTicks;lead++)
                        {
                            if(lead>0){peek=motion.Step(peek,move);if(advance!=null)peekAngle=advance(peekAngle,peek,t+lead);}
                            float raw=RiskAt(peek,t+lead,peekAngle);
                            evaluations++;
                            if(raw>=1000000 && firstBad==int.MaxValue)firstBad=lead;
                            penalty+=Math.Min(500000,raw);
                        }
                        float score=(Vector2.DistanceSquared(next.Position,target)+16*Vector2.DistanceSquared(next.Velocity,desired))/250000+penalty;
                        if(firstBad<safest || firstBad==safest && score>=lowest)continue;
                        safest=firstBad;lowest=score;selected=move;selectedState=next;selectedAngle=nextAngle;
                    }
                    trial[t-1]=selected;state=selectedState;angle=selectedAngle;
                }
                // Local feedback only proposes a sequence. Certify the whole
                // 48-update trajectory with exactly the usual coupled risk.
                Consider(Rollout(trial[0],trial[^1],0,true),0,true);
                return true;
            }
            // Shift the previously verified route by one frame. Its remaining turn may
            // now be five ticks away, which a fresh 6/12/18-tick switch cannot represent.
            if (haveSolution)
            {
                Array.Copy(solution, 1, trial, 0, ForecastTicks - 1);
                trial[^1] = solution[^1];
                Node retained=Rollout(trial[0],trial[^1],0,true);
                RetainedForecastFirstHit=retained.ForecastFirstHit;
                Consider(retained,0,true);
            }
            NavigateGoal();
            // Every immediate input still reaches the full horizon unless it is already
            // proved worse than the incumbent at its first collision.
            candidates.Clear();
            for (int id = 0; id < 12; id++)
            {
                Move move = Move.FromId(id);
                Node node = Rollout(move, move, ForecastTicks);
                candidates.Add(node); Consider(node, ForecastTicks);
            }
            candidates.Sort(Compare);
            int mutations = 0;
            int proposals = 0;
            for (int changeAt = 6; changeAt < ForecastTicks; changeAt += 6)
                for (int root = 0; root < 6; root++)
                    for (int id = 0; id < 12; id++)
                    {
                        if ((ProposalBudget > 0 && proposals++ >= ProposalBudget) || watch.Elapsed.TotalMilliseconds >= budgetMs) goto Done;
                        Move first = candidates[root].First, second = Move.FromId(id);
                        if (first != second) Consider(Rollout(first, second, changeAt), changeAt);
                        if (watch.Elapsed.TotalMilliseconds >= budgetMs) goto Done;
                        // Alternate two-segment proposals with local changes to the full
                        // retained route, so the search can keep multiple timed turns.
                        Array.Copy(bestSequence, trial, ForecastTicks);
                        int start = (mutations++ / 12 % (ForecastTicks / 6)) * 6;
                        Array.Fill(trial, second, start, 6);
                        Consider(Rollout(trial[0], trial[^1], 0, true), 0, true);
                    }
            Done:
            int rescueDepth = 0;
            // Share one rescue deadline across complete detours, feedback and
            // the beam tree. Previously each layer added its own time allowance.
            double rescueDeadline=watch.Elapsed.TotalMilliseconds+rescueBudgetMs;
            if(best.FirstHit!=1 && best.ForecastFirstHit>0 && rescueBudgetMs>0)
            {
                Vector2 forward=frame.Player.Velocity.LengthSquared()>1?frame.Player.Velocity:frame.Goal-frame.Player.Position;
                float bearing=MathF.Atan2(forward.Y,forward.X);
                int[] offsets={2,-2,4,1,-1,3,-3,0};
                for(int pass=0;pass<2 && best.ForecastFirstHit>0;pass++)
                    foreach(int offset in offsets)
                    {
                        if(watch.Elapsed.TotalMilliseconds>=rescueDeadline)goto RescueDone;
                        float angle=bearing+offset*MathF.PI/4;
                        Vector2 waypoint=frame.Player.Position+new Vector2(MathF.Cos(angle),MathF.Sin(angle))*(pass==0?600:300);
                        DetourProposals++;
                        NavigateGoal(waypoint,pass==0?ForecastTicks:24);rescueDepth=ForecastTicks;
                        if(best.ForecastFirstHit==0)break;
                    }
            }
            // Build a complete braking/waiting/turning proposal before spending
            // the rescue budget on a tree which can stop after only two layers.
            // This extra work is confined to already unsafe forecast windows.
            if(best.FirstHit!=1 && best.ForecastFirstHit>0 && rescueBudgetMs>0)
            {
                if(NavigateHazards(3,rescueDeadline))rescueDepth=ForecastTicks;
                if(best.ForecastFirstHit>0 && NavigateHazards(1,rescueDeadline))rescueDepth=ForecastTicks;
            }
            // Distant danger also needs multi-turn proposals before it becomes an
            // urgent collision. Keep this early search smaller than an urgent rescue.
            // A first-tick collision already tested every available immediate input.
            if (best.FirstHit != 1 && best.ForecastFirstHit > 0 && rescueBudgetMs > 0)
            {
                double deadline = rescueDeadline;
                int segmentTicks=Math.Clamp(RescueSegmentTicks,2,3);
                int segments=ForecastTicks/segmentTicks;
                beam.Clear(); beam.Add(new(frame.Player, previous, previous, 0, 0, 0, 0, 0, initialAngle));
                for (int segment = 0; segment < segments; segment++)
                {
                    branches.Clear();
                    foreach (BeamNode parent in beam)
                        for (int id = 0; id < 12; id++)
                        {
                            if (watch.Elapsed.TotalMilliseconds >= deadline) goto RescueDone;
                            Move move = Move.FromId(id);
                            MotionState state = parent.State;
                            float angle = parent.Angle;
                            float cost = parent.Cost + (move != parent.Last ? .12f : 0);
                            int firstHit = parent.FirstHit;
                            int forecastFirstHit = parent.ForecastFirstHit;
                            bool dominated = false;
                            for (int step = 1; step <= segmentTicks; step++)
                            {
                                int future = segment * segmentTicks + step;
                                state = motion.Step(state, move);
                                if (advance != null) angle = advance(angle, state, future);
                                float rawRisk = RiskAt(state, future, angle);
                                if (forecastFirstHit == 0 && rawRisk >= 1000000) forecastFirstHit = future;
                                float risk = TimedRisk(rawRisk, future);
                                if (firstHit == 0 && risk >= 1000000) firstHit = future;
                                cost += risk / (1 + .02f * future);
                                cost += Vector2.DistanceSquared(state.Position, GoalAt(future))
                                    / Math.Max(10000f, GoalPullDivisor);
                                if (frame.Damageable)
                                {
                                    float outside = Math.Max(0, Vector2.Distance(state.Position, frame.Boss) - frame.AttackRange);
                                    cost += outside * outside / 40000f;
                                }
                                evaluations++;
                                if (firstHit > 0 && (best.FirstHit == 0 || firstHit < best.FirstHit))
                                { dominated = true; break; }
                                if (firstHit == 0 && forecastFirstHit > 0 && best.FirstHit == 0
                                    && (best.ForecastFirstHit == 0 || forecastFirstHit < best.ForecastFirstHit))
                                { dominated = true; break; }
                            }
                            if (dominated) continue;
                            rescueDepth = Math.Max(rescueDepth, (segment + 1) * segmentTicks);
                            Move first = segment == 0 ? move : parent.First;
                            RouteCode code = parent.Code.Append(segment,id);
                            if (segment == segments - 1)
                            {
                                Node node = new(state, first, move, cost, firstHit, forecastFirstHit);
                                if (Compare(node, best) < 0)
                                {
                                    for (int t = 0; t < ForecastTicks; t++) trial[t] = Move.FromId(code.At(t / segmentTicks));
                                    Consider(node, 0, true);
                                }
                            }
                            else branches.Add(new(state, first, move, cost, firstHit, forecastFirstHit, code,
                                cost + Vector2.DistanceSquared(state.Position, GoalAt(ForecastTicks)) / 250000f * (ForecastTicks - (segment + 1) * segmentTicks), angle));
                        }
                    rescueDepth = (segment + 1) * segmentTicks;
                    if (segment == segments - 1 || branches.Count == 0) break;
                    branches.Sort((a, b) =>
                    {
                        int safeA = a.FirstHit == 0 ? int.MaxValue : a.FirstHit;
                        int safeB = b.FirstHit == 0 ? int.MaxValue : b.FirstHit;
                        int safety = safeB.CompareTo(safeA);
                        if (safety != 0) return safety;
                        int farA = a.ForecastFirstHit == 0 ? int.MaxValue : a.ForecastFirstHit;
                        int farB = b.ForecastFirstHit == 0 ? int.MaxValue : b.ForecastFirstHit;
                        int futureSafety = farB.CompareTo(farA);
                        return futureSafety != 0 ? futureSafety : a.Score.CompareTo(b.Score);
                    });
                    // An interrupted search can still improve the route: complete the
                    // best prefix with the incumbent tail and verify the whole horizon.
                    if (watch.Elapsed.TotalMilliseconds < deadline)
                    {
                        Array.Copy(bestSequence, trial, ForecastTicks);
                        RouteCode prefix = branches[0].Code;
                        for (int t = 0; t < (segment + 1) * segmentTicks; t++) trial[t] = Move.FromId(prefix.At(t / segmentTicks));
                        Consider(Rollout(trial[0], trial[^1], 0, true), 0, true);
                        if (best.ForecastFirstHit == 0) break;
                    }
                    beam.Clear();
                    int width=Math.Clamp(RescueBeamWidth,12,48);
                    if(DiverseRescue)
                    {
                        int firstMoves=0;
                        foreach(BeamNode node in branches)
                        {
                            int bit=1<<node.First.Id;
                            if((firstMoves&bit)!=0)continue;
                            beam.Add(node);firstMoves|=bit;
                            if(beam.Count==width)break;
                        }
                    }
                    foreach (BeamNode node in branches)
                    {
                        if(beam.Count>=width)break;
                        bool duplicate = false;
                        foreach (BeamNode selected in beam)
                            if (node.Angle == selected.Angle && node.State.Dash == selected.State.Dash
                                && Vector2.DistanceSquared(node.State.Position, selected.State.Position) < .04f
                                && Vector2.DistanceSquared(node.State.Velocity, selected.State.Velocity) < .04f)
                            { duplicate = true; break; }
                        if (!duplicate) beam.Add(node);
                        if (beam.Count == width) break;
                    }
                    RescueFrontierPeak=Math.Max(RescueFrontierPeak,beam.Count);
                }
            }
            RescueDone:
            NavigationChosen = true;
            for (int t = 0; t < ForecastTicks; t++)
                if (bestSequence[t] != navigationSequence[t]) { NavigationChosen = false; break; }
            Array.Copy(bestSequence, solution, ForecastTicks); haveSolution = true;
            MotionState next = motion.Step(frame.Player, best.First);
            return new(best.First, next, evaluations,
                ForecastTicks, watch.Elapsed.TotalMilliseconds, best.Cost, best.FirstHit, rescueDepth, best.ForecastFirstHit,
                coupled ? riskCache.Count : positionRiskCache.Count, cachedQueries,
                advance == null ? initialAngle : advance(initialAngle, next, 1));
        }
    }

    public sealed class BattleMetrics
    {
        public long DamageDealt { get; private set; }
        public int Hits { get; private set; }
        public bool Assisted { get; private set; }
        public bool Killed { get; private set; }
        public int ControlledTicks { get; private set; }
        public bool TargetModeVerified { get; private set; }
        public bool NoHitVictory => TargetModeVerified && Killed && Hits == 0 && !Assisted && ControlledTicks > 0 && DamageDealt > 0;
        public string ResultReason(string fallback) => Killed ? "boss_killed" : fallback;
        public void VerifyTargetMode(bool enabled) => TargetModeVerified = enabled;
        public void Tick(bool assisted) { ControlledTicks++; Assisted |= assisted; }
        public void Damage(int damage) { DamageDealt += Math.Max(0, damage); }
        public void Hurt() => Hits++;
        public void Invalidate() => Assisted = true;
        public void Kill() => Killed = true;
    }
}

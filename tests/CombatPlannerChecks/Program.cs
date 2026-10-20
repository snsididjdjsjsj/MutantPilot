using System.Numerics;
using System.Text.Json;
using MutantPilot.Combat;

SmallEyeChecks.Run();
DirectDashChecks.Run();
EocBossChecks.Run();
SwordRoutingChecks.Run();
P3VoidRoutingChecks.Run();
RitualChecks.Run();

var swordTree=new SwordResponseForecast();
float swordStep=-MathF.PI/20;
var preparingSword=new SwordWorld(Vector2.Zero,Vector2.Zero,89,0,true);
var waitSword=SwordResponseForecast.Step(preparingSword,new(100,200),1,swordStep);
if(!waitSword.Preparing||waitSword.Position!=Vector2.Zero)throw new Exception("sword wind-up must wait until native timer exceeds 90");
var launchSword=SwordResponseForecast.Step(waitSword,new(100,200),2,swordStep);
if(launchSword.Preparing||launchSword.Timer!=0||Vector2.Distance(launchSword.Velocity,new((100-300*swordStep)/20,10))>.001f)
    throw new Exception("sword launch must lock this candidate's actual player position and native angular offset");
var nativeSword=new SwordWorld(new(55914.13f,15908.837f),new(-53.671875f,13.192383f),14,-2.1991153f,false);
var nativePart=new SwordPart(207,160,-MathF.PI/4,4);
swordTree.Reset(nativeSword,swordStep,new[]{nativePart});
float swordRef=swordTree.Advance(0,new MotionState(Vector2.Zero,Vector2.Zero),1);
if(Vector2.Distance(swordTree.Position(swordRef,nativePart),new(55700.457f,15922.029f))>.02f
    ||!swordTree.Touches(swordRef,new(55711.047f,15901.632f),new(13),1))
    throw new Exception("candidate sword response must reproduce the native 0.3.68 Hurt body");
swordTree.Reset(preparingSword,swordStep,new[]{nativePart});
float shared=swordTree.Advance(0,new MotionState(new(100,200),Vector2.Zero),1);
if(shared!=swordTree.Advance(0,new MotionState(new(100,200),Vector2.Zero),1))
    throw new Exception("identical sword input prefixes must share exact state");
float branchA=swordTree.Advance(shared,new MotionState(new(100,200),Vector2.Zero),2);
float branchB=swordTree.Advance(shared,new MotionState(new(-200,200),Vector2.Zero),2);
if(branchA==branchB||swordTree.At(branchA).Velocity==swordTree.At(branchB).Velocity)
    throw new Exception("sword candidates must retain distinct lock positions and dash histories");
Console.WriteLine("PASS sword response uses candidate lock positions, preserves distinct histories and matches the actual Hurt body");

var eocPath = new Vector2[49];
Vector2 eocTarget = new(77018.734f,16591.82f);
EocDashForecast.Forecast(eocPath,new(77695.9f,16690.281f),new(-98.75141f,-68.17741f),121,eocTarget);
if (Vector2.Distance(eocPath[1],new(77597.15f,16622.104f))>.02f)
    throw new Exception("locked Cthulhu eye first update must match the native Hurt snapshot");
Vector2 linearEye = new Vector2(77695.9f,16690.281f)+new Vector2(-98.75141f,-68.17741f)*12;
if (Vector2.Distance(eocPath[12],linearEye)<100)
    throw new Exception("locked Cthulhu eye must redirect after native phase reset instead of continuing linearly");
EocDashForecast.Forecast(eocPath,new(700,0),Vector2.Zero,120,Vector2.Zero);
if (Math.Abs(Vector2.Distance(eocPath[0],eocPath[1])-120)>.001f
    || Math.Abs(eocPath[1].Y+120*MathF.Sin(MathF.PI/8))>.001f)
    throw new Exception("locked Cthulhu first dash must use native speed and 22.5 degree offset");
Console.WriteLine("PASS locked Cthulhu eye redirects every native cycle and reproduces actual first-Hurt movement");

var fan = new MechFanForecast();
fan.Update(27, 9, 1, 77, Vector2.Zero, true);
if (fan.Paths.Count != 20 || fan.Paths[0].Birth.Born != 2 || fan.Paths[4].Birth.Born != 13)
    throw new Exception("mechanical rays must forecast all four native eleven-tick births before any projectile exists");
FanRayPose newborn = fan.Paths[0].Poses[2];
if (fan.Paths[0].Poses[1].Scale != 0 || Math.Abs(newborn.Scale - .0698116f) > .00001f
    || Math.Abs(newborn.Length - 1500) > .001f || Math.Abs(newborn.Direction.Y - .19187967f) > .00001f)
    throw new Exception("mechanical rays must use native first-update turn, scale and length");
Vector2 fatalPoint = new(49938.35f-49673.84f,15994.246f-15950.167f);
if (!FlatBeam.Intersects(newborn.Origin,newborn.Direction,newborn.Length,22*newborn.Scale,fatalPoint,new(13)))
    throw new Exception("birth forecast must cover the native 0.3.66 first-Hurt beam");
fan.Update(27, 10, -1, 239, Vector2.Zero, true);
if (fan.Paths.Count != 4 || fan.Paths[0].Birth.Born != 1 || fan.Paths[2].Birth.Origin.Y != 1200)
    throw new Exception("last valid native emission and opposite orientation must be retained");
fan.Update(27, 10, 1, 240, Vector2.Zero, true);
if (fan.Paths.Count != 0) throw new Exception("mechanical fan cannot emit at age 240");
fan.Update(27, 10, 0, 100, Vector2.Zero, true);
if (fan.Paths.Count != 0) throw new Exception("unobserved random fan orientation cannot be guessed");
Console.WriteLine("PASS future mechanical rays include immediate native damage, eleven-tick timing and both origins without guessing RNG");

foreach(float seedAngle in new[] {-2.9f,-1.4622837f,0f,.7f,2.8f})
for(int sector=0;sector<60;sector++)
{
    float bearing=VoidOuterRouting.GapBearing(seedAngle,MathF.PI/10,MathF.Tau*sector/60,true);
    Vector2 point = new Vector2(MathF.Cos(bearing),MathF.Sin(bearing))*850;
    for(int ray=0;ray<60;ray++)
    {
        float angle=seedAngle+MathF.PI/30*ray; Vector2 direction=new(MathF.Cos(angle),MathF.Sin(angle));
        if(FlatBeam.Intersects(direction*180,direction,3000,22,point,new(13,24)))
            throw new Exception("P1 outer grid midpoint must clear all three native ray phase groups even with a full inflated player box");
    }
}
Console.WriteLine("PASS P1 outer midpoint clears all sixty native directions across five initial angles and every approach sector");

if (!VoidInnerRouting.Retain(9, 0, 100, 145, Vector2.Zero, Vector2.Zero)
    || !VoidInnerRouting.Retain(1, 0, 100, 145, Vector2.Zero, Vector2.Zero)
    || VoidInnerRouting.Retain(9, 1, 100, 145, Vector2.Zero, Vector2.Zero)
    || VoidInnerRouting.Retain(9, 0, 145, 145, Vector2.Zero, Vector2.Zero)
    || VoidInnerRouting.Retain(1, 0, 100, 145, new(5, 0), Vector2.Zero))
    throw new Exception("P1 residual route must survive compatible stationary transitions, then release on expiry or boss movement");
Console.WriteLine("PASS P1 residual route survives compatible transitions and releases on expiry or movement");
var guideRoute = new VoidInnerRouting();
guideRoute.Update(true, 0, new(100, 0), Vector2.Zero, 0, 0, new(13), Array.Empty<LinearMark>(), 100, 4,
    new[] { new GuideTrack(Enumerable.Repeat(new Vector2(100, 0), 49).ToArray(), 1, 48, 20) });
Vector2 guided = guideRoute.GoalAt(1);
if (guideRoute.ForecastOverlaps != 0 || Math.Max(Math.Abs(guided.X-100), Math.Abs(guided.Y)) <= 33)
    throw new Exception("P1 residual phase selection must also avoid the curved sphere track");
Console.WriteLine("PASS residual phase selection keeps an open phase around an additional curved projectile");
var flexibleRoute = new VoidInnerRouting();
flexibleRoute.Update(true, 0, new(100, 0), Vector2.Zero, 0, -.31415927f, new(13), Array.Empty<LinearMark>(), 100, 4,
    new[] { new GuideTrack(Enumerable.Repeat(new Vector2(-100, 0), 49).ToArray(), 1, 48, 20) }, true, Vector2.Zero);
Vector2 held = flexibleRoute.GoalAt(0);
if (Vector2.Distance(held, flexibleRoute.GoalAt(30)) > .01f)
    throw new Exception("residual steering must permit a stationary safe route instead of compulsory rotation");
flexibleRoute.Update(true, 1, new(100, 0), Vector2.Zero, -.07853982f, -.31415927f, new(13), Array.Empty<LinearMark>(),
    100, 4, null, true, Vector2.Zero);
if (Vector2.Distance(held, flexibleRoute.GoalAt(0)) > .01f)
    throw new Exception("a retained adaptive route must keep its own time base rather than resume the old attack's rotation");
Console.WriteLine("PASS adaptive residual guidance can hold position and retain its independent motion epoch");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message);
}
var fullWindowMotion = new MotionModel { MaxX = 5, Acceleration = 1, Braking = 1, Gravity = 0, MaxFall = 0 };
var fullWindowFrame = new PlanFrame(new(Vector2.Zero, Vector2.Zero), Vector2.Zero, Vector2.Zero, 2000, false);
var fullWindowRoute = new CombatPlanner().Choose(fullWindowFrame, fullWindowMotion,
    (p, t) => t >= 36 && p.X >= -160 ? 1000000 : p.X < -10 ? 50000 : 0, new(0, 3), 0, 0);
Check(fullWindowRoute.FirstHit == 0 && fullWindowRoute.ForecastFirstHit == 0 && fullWindowRoute.Move.Horizontal < 0,
    "a whole-window safe escape outranks a cheap delayed collision even when the safe route has a large soft cost");

var planner = new CombatPlanner();
var motion = new MotionModel();
var lockedToss = LockedTossForecast.Read(0, 144, 4, 7, 0, new(500, 0), new(2, 0));
Check(lockedToss is PendingSpear { Born: 7, Padding: 107 } && lockedToss.Value.Origin == new Vector2(512, 0)
    && lockedToss.Value.Velocity == new Vector2(25, 0),
    "P1 pending spear starts from the final visible aim seven updates before the throw");
Check(LockedTossForecast.Read(0, 143, 4, 7, 0, Vector2.Zero, Vector2.Zero) == null
    && LockedTossForecast.Read(0, 144, 7, 7, 0, Vector2.Zero, Vector2.Zero) == null,
    "P1 pending spear excludes an aim that still tracks and the counter that exits without firing");
var delayedOrbs = new List<TrailOrb>();
SpearTrail.Forecast(delayedOrbs, Vector2.Zero, new(25, 0), 0, 2, 42, 6, 56);
Check(delayedOrbs.Count == 42 && delayedOrbs[0].Born == 7 && delayedOrbs[2].Born == 9
    && delayedOrbs[0].Explodes == 21 && delayedOrbs.All(x => x.Padding == 56),
    "pending P1 spear shifts the full native two-sub-update trail and explosion schedule to its future birth");
var broadBeam = new BeamBroadPhase(Vector2.Zero, Vector2.UnitX, 3000, 1, new(24));
Check(broadBeam.MayTouch(new(-24, 0), new(10, 21)) && broadBeam.MayTouch(new(200, 10), new(10, 21)),
    "beam broad phase preserves both the projectile origin box and the damaging line");
Check(!broadBeam.MayTouch(new(200, 400), new(10, 21)) && !broadBeam.MayTouch(new(3500, 0), new(10, 21)),
    "beam broad phase rejects distant transverse and endpoint queries before native collision work");
Check(new BeamBroadPhase(Vector2.Zero, new(float.NaN, 0), 3000, 1, new(24)).MayTouch(new(200, 400), new(10, 21))
    && broadBeam.MayTouch(new(float.NaN, 0), new(10, 21)),
    "nonfinite beam geometry and queries keep the full native collision path");
bool fanGoalHit = false, oldFanSideHit = false;
foreach (int orientation in new[] { -1, 1 })
foreach (int originIndex in new[] { 0, 1 })
foreach (int rayIndex in new[] { 0, 1 })
{
    Vector2 origin = new(0, originIndex * -1200 * orientation);
    float initial = (rayIndex == 0 ? 8 * orientation : -8 * orientation + 180) * MathF.PI / 180;
    if (originIndex == 1) initial += MathF.PI;
    float turn = 245 * MathF.PI / 180 * orientation / 80 * (rayIndex == 0 ? 1 : -1);
    float[] angles = new float[270];
    ProjectileKinematics.DecayingRayAngles(angles, initial, turn, 0, true);
    Vector2 firstDirection = new(MathF.Cos(initial), MathF.Sin(initial));
    for (int age = 1; age < angles.Length; age++)
    {
        Vector2 direction = new(MathF.Cos(angles[age]), MathF.Sin(angles[age]));
        Vector2 at = origin + direction - firstDirection;
        bool Touch(Vector2 p) => FlatBeam.Intersects(at, direction, 3000, 22, p, new(13, 24))
            || Math.Abs(p.X - at.X) < 37 && Math.Abs(p.Y - at.Y) < 48;
        fanGoalHit |= Touch(MechFanRouting.Goal(Vector2.Zero, orientation));
        oldFanSideHit |= Touch(new(650, 0)) || Touch(new(-650, 0));
    }
}
Check(!fanGoalHit && oldFanSideHit,
    "middle fan goal stays between both real emitter groups throughout every damaging ray age; the old horizontal side goal is swept");
bool tangentHit = false;
for (int angleIndex = 0; angleIndex < 12; angleIndex++)
{
    var orbitPlanner = new CombatPlanner();
    float a = MathF.Tau * angleIndex / 12;
    MotionState orbitPlayer = new(new Vector2(MathF.Cos(a), MathF.Sin(a)) * 500, Vector2.Zero);
    var orbitMotion = new MotionModel { FlightMastery = true, MaxX = 18, WalkSpeed = 8.67f,
        Acceleration = 1.3125f, Braking = 1, StopBraking = 8, Gravity = .6f, MaxFall = 10.01f, JumpSpeed = 13.91f };
    Move orbitPrevious = new(0, 3);
    Vector2 spear = Vector2.Zero, spearVelocity = Vector2.Zero;
    for (int tick = 0; tick < 70; tick++)
    {
        if (tick == 30) spearVelocity = Vector2.Normalize(orbitPlayer.Position) * 60;
        var goal = orbitPlayer.Position + SpearOrbitRouting.Tangent(orbitPlayer.Position, Vector2.Zero, 1) * 300;
        var route = orbitPlanner.Choose(new(orbitPlayer, goal, Vector2.Zero, 650, true), orbitMotion, (_, _) => 0,
            orbitPrevious, 0, 0);
        orbitPlayer = route.Next; orbitPrevious = route.Move;
        if (tick >= 30)
        {
            Vector2 next = spear + spearVelocity;
            Vector2 direction = Vector2.Normalize(spearVelocity);
            tangentHit |= FlatBeam.Intersects(spear - direction * 100, direction, 260, 16,
                orbitPlayer.Position, new(13, 24));
            spear = next;
        }
    }
}
Check(!tangentHit,
    "continuous normal tangential flight clears a newly aimed 200px spear shaft at 60px per tick from twelve approach angles before reactive search");
var nonfiniteMargin = new CombatPlanner().Choose(new(new(Vector2.Zero, Vector2.Zero), new(100, 0), new(100, 0), 650, true),
    motion, (p, t) => ThreatClearance.Box(p, new(float.NaN, 0), new(20), 48)
        + ThreatClearance.Circle(p, Vector2.Zero, new(13, 24), float.NaN, 48), new(0, 3), 0, 0);
Check(float.IsFinite(nonfiniteMargin.Cost) && JsonSerializer.Serialize(nonfiniteMargin).Length > 0,
    "a nonfinite external projectile cannot poison the comfort preference, selected route, or strict JSON telemetry");
var trackingMotion = new MotionModel { FlightMastery = true, MaxX = 18, WalkSpeed = 8.67f,
    Acceleration = 1.3125f, Braking = 1, StopBraking = 8, Gravity = .6f, MaxFall = 10.01f, JumpSpeed = 13.91f };
var trackingPlanner = new CombatPlanner();
MotionState trackingPlayer = new(new(85, 0), Vector2.Zero);
Move trackingPrevious = new(0, 3);
var trackingGoals = new Vector2[CombatPlanner.ForecastTicks + 1];
float maxTrackingError = 0;
for (int now = 0; now < 360; now++)
{
    for (int t = 0; t < trackingGoals.Length; t++)
    {
        float angle = (now + t) * MathF.PI / 40;
        trackingGoals[t] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 85;
    }
    var route = trackingPlanner.ChooseTracking(new(trackingPlayer, trackingGoals[8], Vector2.Zero, 650, false),
        trackingMotion, (s, t) => Vector2.DistanceSquared(s.Position, trackingGoals[t]) / 400,
        trackingGoals, trackingPrevious, 0, 0);
    trackingPlayer = route.Next; trackingPrevious = route.Move;
    maxTrackingError = Math.Max(maxTrackingError, Vector2.Distance(trackingPlayer.Position, trackingGoals[1]));
}
Console.WriteLine($"Moving goal tracking maxError={maxTrackingError}");
Check(maxTrackingError < 40,
    "normal flight follows a moving inner-circle target for 360 ticks using the tracking proposal even without refinement budget");
foreach (float comfort in new[] { 0f, 48f })
{
    var marginPlanner = new CombatPlanner();
    MotionState corridorPlayer = new(Vector2.Zero, Vector2.Zero);
    Move corridorPrevious = new(0, 3);
    bool touched = false;
    for (int t = 0; t < 240; t++)
    {
        float CorridorRisk(Vector2 p, int ahead)
        {
            float cost = Math.Abs(p.Y) > 14 ? 1000000 : 0;
            foreach (float y in new[] { -50f, 50f })
                cost += ThreatClearance.Box(p, new(350, y), new(80, 36), comfort);
            return cost;
        }
        var route = marginPlanner.Choose(new(corridorPlayer, new(700, 0), new(700, 0), 80, true),
            motion, CorridorRisk, corridorPrevious, 0, 0);
        corridorPlayer = route.Next; corridorPrevious = route.Move;
        touched |= Math.Abs(corridorPlayer.Position.Y) > 14;
    }
    Console.WriteLine($"Clearance corridor reserve={comfort} final={corridorPlayer.Position}");
    Check(!touched && Vector2.Distance(corridorPlayer.Position, new(700, 0)) < 80,
        "soft projectile clearance keeps a necessary narrow attack corridor usable through normal controls");
}
var measuredMargins = new List<float>();
foreach (float comfort in new[] { 0f, 48f })
{
    var marginPlanner = new CombatPlanner();
    MotionState marginPlayer = new(Vector2.Zero, Vector2.Zero);
    Move marginPrevious = new(0, 3);
    float leastGap = float.PositiveInfinity;
    bool hit = false;
    for (int t = 0; t < 180; t++)
    {
        Vector2 obstacle = new(350, 35), half = new(35, 25);
        float MarginRisk(Vector2 p, int ahead) =>
            (Math.Abs(p.X - obstacle.X) < half.X && Math.Abs(p.Y - obstacle.Y) < half.Y ? 1000000 : 0)
            + ThreatClearance.Box(p, obstacle, half, comfort);
        var route = marginPlanner.Choose(new(marginPlayer, new(700, 0), new(700, 0), 80, true),
            motion, MarginRisk, marginPrevious, 2, 0);
        marginPlayer = route.Next; marginPrevious = route.Move;
        hit |= MarginRisk(marginPlayer.Position, 1) >= 1000000;
        leastGap = Math.Min(leastGap, Vector2.Max(Vector2.Zero, Vector2.Abs(marginPlayer.Position - obstacle) - half).Length());
    }
    measuredMargins.Add(leastGap);
    Console.WriteLine($"Clearance approach reserve={comfort} min={leastGap} final={marginPlayer.Position}");
    Check(!hit && Vector2.Distance(marginPlayer.Position, new(700, 0)) < 80,
        "projectile comfort margin preserves sustained progress into actual attack range");
}
Check(measuredMargins[1] > measuredMargins[0] + 5,
    "closed-loop planning leaves more room beside a passable projectile instead of grazing the binary collision edge");
Check(Enumerable.Range(0, 21).Where(SmallBlastTiming.Damages).SequenceEqual(
    Enumerable.Range(0, 9).Concat(Enumerable.Range(15, 6)))
    && !SmallBlastTiming.Damages(21),
    "native small explosion damages on frames zero to two and five to six, then disappears");
Check(Enumerable.Range(10, 4).All(age => !SmallBlastTiming.CouldDamage(age))
    && SmallBlastTiming.CouldDamage(-1) && SmallBlastTiming.CouldDamage(9)
    && SmallBlastTiming.CouldDamage(14) && SmallBlastTiming.CouldDamage(21)
    && !SmallBlastTiming.CouldDamage(22),
    "one-update birth uncertainty preserves the four-tick safe gap and includes the last damaging update");
var worldRoute = new WorldRouting();
worldRoute.Observe(new(67200, 17600), new(666, 677), new(133718, 35000), 3000, 1500);
float fixedHeight = worldRoute.AnchorY;
worldRoute.Observe(new(68000, 8000), new(666, 677), new(133718, 35000), 3000, 1500);
Check(worldRoute.AnchorY == fixedHeight && worldRoute.Direction == 1,
    "height anchor stays at the encounter start when the fight moves upward, and spear routing turns down");
Check(worldRoute.BoundaryCost(new(67200, 17600)) == 0
    && worldRoute.BoundaryCost(new(67200, 1000)) > worldRoute.BoundaryCost(new(67200, 3000))
    && worldRoute.BoundaryCost(new(67200, 677)) < 1000000,
    "physical ceiling receives increasing advance cost without being declared a projectile collision");
var borderMotion = new MotionModel { FlightMastery = true, MaxX = 18, WalkSpeed = 8.67f,
    Acceleration = 1.3125f, Braking = 1, StopBraking = 8, Gravity = .6f, MaxFall = 15.015f,
    JumpSpeed = 13.91f, WorldMinimum = new(666, 677), WorldMaximum = new(133718, 37707) };
MotionState borderRising = new(new(67200, 3700), new(0, -24));
var borderPlanner = new CombatPlanner();
Move borderPrevious = new(0, 2);
float leastClearance = float.PositiveInfinity;
for (int t = 0; t < 360; t++)
{
    worldRoute.Observe(borderRising.Position, borderMotion.WorldMinimum, new(133718, 35000), 3000, 1500);
    var frame = new PlanFrame(borderRising, borderRising.Position - new Vector2(0, 250), borderRising.Position, 650, false);
    Decision step = borderPlanner.Choose(frame, borderMotion, (p, _) => worldRoute.BoundaryCost(p), borderPrevious, 0, 0);
    borderRising = step.Next; borderPrevious = step.Move;
    leastClearance = Math.Min(leastClearance, borderRising.Position.Y - 677);
}
Check(leastClearance > 2000,
    "normal flight planner rejects a persistent upward attractor before reaching the ceiling reserve");
var restoringRoute = new WorldRouting();
var restoringPlanner = new CombatPlanner();
MotionState altitudeRising = new(new(67200, 17600), new(0, -24));
Move restoringPrevious = new(0, 2);
float restoringMinimum = altitudeRising.Position.Y;
for (int t = 0; t < 360; t++)
{
    restoringRoute.Observe(altitudeRising.Position, borderMotion.WorldMinimum, new(133718, 35000), 3000, 1500);
    var frame = new PlanFrame(altitudeRising, altitudeRising.Position - new Vector2(0, 250), altitudeRising.Position, 650, false);
    Decision step = restoringPlanner.Choose(frame, borderMotion,
        (p, _) => restoringRoute.BoundaryCost(p) + restoringRoute.HeightCost(p), restoringPrevious, 0, 0);
    altitudeRising = step.Next; restoringPrevious = step.Move;
    restoringMinimum = Math.Min(restoringMinimum, altitudeRising.Position.Y);
}
Check(restoringMinimum >= 15800 && altitudeRising.Position.Y >= 15800,
    "height recovery stops sustained upward drift near the encounter band while still far from the world ceiling");
Decision emergencyUp = new CombatPlanner().Choose(
    new(new(new(67200, 1600), Vector2.Zero), new(67200, 1600), new(67200, 1600), 650, false),
    borderMotion, (p, _) => worldRoute.BoundaryCost(p) + worldRoute.HeightCost(p)
        + (p.Y >= 1600 ? 1000000 : 0), new(0, 3), 0, 0);
Check(emergencyUp.Move.Vertical is 1 or 2 && emergencyUp.FirstHit == 0,
    "real impending damage still outranks the height reserve and allows an upward escape");
var cycleRoute = new WorldRouting();
var cyclePlanner = new CombatPlanner();
MotionState cycling = new(new(67200, 17600), Vector2.Zero);
Move cyclePrevious = new(0, 3);
float lowestCycleY = cycling.Position.Y, highestCycleY = cycling.Position.Y;
int turns = 0, lastDirection = -1;
for (int t = 0; t < 1800; t++)
{
    cycleRoute.Observe(cycling.Position, borderMotion.WorldMinimum, new(133718, 35000), 3000, 1500);
    if (cycleRoute.Direction != lastDirection) turns++;
    lastDirection = cycleRoute.Direction;
    var frame = new PlanFrame(cycling, cycling.Position + new Vector2(0, lastDirection * 250), cycling.Position, 650, false);
    Decision step = cyclePlanner.Choose(frame, borderMotion, (p, _) => cycleRoute.BoundaryCost(p), cyclePrevious, 0, 0);
    cycling = step.Next; cyclePrevious = step.Move;
    lowestCycleY = Math.Min(lowestCycleY, cycling.Position.Y); highestCycleY = Math.Max(highestCycleY, cycling.Position.Y);
}
Check(turns >= 3 && lowestCycleY >= 15900 && highestCycleY <= 19300,
    "repeated spear-height goals cycle around the fixed anchor instead of climbing through the tall world");
var entryMotion = new MotionModel { FlightMastery = true, MaxX = 18, WalkSpeed = 8.519999f,
    Acceleration = 1.3125f, Braking = 1, StopBraking = 8, Gravity = .6f, MaxFall = 10.01f, JumpSpeed = 13.710001f };
var entryFrame = new PlanFrame(new(new(34560.105f, 7581.1875f), new(-14.012499f, -20.474995f),
    new(1, 1, 3, 0, false, true)), new(34409.926f, 7654.073f), new(34306.465f, 7704.284f), 650, true);
Decision entry = new CombatPlanner().Choose(entryFrame, entryMotion, (_, _) => 0, new(0, 1), 0, 0);
Check(entry.Move.Vertical == 3 && entry.Cost < 8,
    "recorded fast ascent proposes hover braking followed by normal descent toward the attack goal even before refinement has time");
Check(!FlatBeam.Intersects(new(175, 0), Vector2.UnitX, 3000, 220, Vector2.Zero, new(13, 24))
    && new TimedBeam(new(175, 0), Vector2.UnitX, 1, 1, 260).Intersects(Vector2.Zero, new(13, 24), 1),
    "native flat beam leaves the area behind its origin open instead of adding the old oversized backward cap");
Check(!FlatBeam.Intersects(Vector2.Zero, Vector2.UnitX, 3000, 220, new(600, 145), new(13, 24))
    && FlatBeam.Intersects(Vector2.Zero, Vector2.UnitX, 3000, 220, new(600, 130), new(13, 24))
    && FinalSparkForecast.TouchesBeam(new(175, 0), Vector2.UnitX, 10, 3000, new(160, 0), new(1, 1)),
    "giant beam uses half the supplied full width and also retains its real 48px origin projectile box");
Check(Enumerable.Range(0, 360).All(degrees => {
    float angle = degrees * MathF.PI / 180; Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
    return !FinalSparkForecast.TouchesBeam(direction * 176, direction, 10, 3000, Vector2.Zero, new(19, 30));
}), "full-scale giant beam does not invent an obstacle at the harmless P3 boss center across all beam directions; spawned spheres remain separate hazards");
bool reservedGuardianLane = true;
foreach (int sign in new[] { -1, 1 })
    for (int degrees = -15; degrees <= 15; degrees++)
        for (int along = -1800; along <= 1800; along += 32)
        {
            float angle = degrees * MathF.PI / 180;
            Vector2 center = new(0, -sign * 600);
            Vector2 guardian = center + new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * along;
            // These offsets still overlap a 70x70 guardian and 26x48 player.
            Vector2 edgeVictim = guardian + new Vector2(45, 58);
            reservedGuardianLane &= ProjectileKinematics.GuardianLaneCost(edgeVictim, Vector2.Zero, sign) > 0;
        }
Check(reservedGuardianLane && ProjectileKinematics.GuardianLaneCost(new(650, 0), Vector2.Zero, -1) == 0,
    "guardian route reserve covers the installed +/-15 degree convergence paths and player overlap edges while leaving the offensive side position unpenalized");
Vector2 voidOrigin = new(33431.008f, 6498.076f), voidDirection = new(-.26697662f, .963703f);
Vector2 voidCenter = voidOrigin - voidDirection * 180;
Vector2 voidGoal = ProjectileKinematics.VoidRayGoal(new(33415.254f, 6613.9434f), voidCenter, 1);
Check(!new TimedBeam(voidOrigin, voidDirection, 1, 55).Intersects(voidGoal, new(13, 24), 10)
    && new TimedBeam(voidOrigin, voidDirection, 1, 55).Intersects(new(33415.254f, 6613.9434f), new(13, 24), 10)
    && Vector2.Distance(voidGoal, voidCenter) > 100,
    "void-ray inner goal lies outside the recorded first-hit beam while retaining clearance from boss contact");
Check(Enumerable.Range(0, 360).All(degrees => {
    float angle = degrees * MathF.PI / 180; Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
    return !new TimedBeam(voidCenter + direction * 180, direction, 1, 55)
        .Intersects(voidGoal, new(13, 24), 10);
}), "the inner goal fits inside outward ray origins across a full circle including nonsquare player bounds; moving markers still need planning");
var rayAngles = new float[49];
ProjectileKinematics.DecayingRayAngles(rayAngles, 0, .05f, 44, true);
Vector2 actualRayVictim = new Vector2(MathF.Cos(rayAngles[24]), MathF.Sin(rayAngles[24])) * 1000;
var correctFan = new TimedBeam(Vector2.Zero, Vector2.Normalize(actualRayVictim), 24, 24);
var constantFan = new TimedBeam(Vector2.Zero, new(MathF.Cos(.05f * 24), MathF.Sin(.05f * 24)), 24, 24);
Check(correctFan.Intersects(actualRayVictim, new(13, 24), 24)
    && !constantFan.Intersects(actualRayVictim, new(13, 24), 24),
    "decelerating fan predicts a real beam crossing that the old constant angular velocity incorrectly marks safe");
ProjectileKinematics.DecayingRayAngles(rayAngles, 0, .05f, 148, true);
Check(Math.Abs(rayAngles[1] - .05f * .9716f) < .000001f
    && Math.Abs((rayAngles[24] - rayAngles[23]) - .05f * .9716f) < .000001f,
    "fan ray stops reducing angular speed at age 150 instead of decaying throughout its remaining life");
var buckets = new TemporalBuckets(48);
var randomField = new Random(48321);
var indexedShapes = Enumerable.Range(0, 300).Select(_ =>
    (Start: new Vector2(randomField.Next(-1800, 1800), randomField.Next(-1800, 1800)),
     Velocity: new Vector2(randomField.Next(-50, 50), randomField.Next(-50, 50)),
     Half: new Vector2(randomField.Next(15, 100), randomField.Next(25, 100)))).ToArray();
for (int t = 1; t <= 48; t++)
    for (int i = 0; i < indexedShapes.Length; i++)
    {
        var shape = indexedShapes[i];
        Vector2 a = shape.Start + shape.Velocity * (t - 1), b = a + shape.Velocity;
        buckets.Add(i, t, Vector2.Min(a, b) - shape.Half, Vector2.Max(a, b) + shape.Half);
    }
bool equivalent = true;
long indexedCount = 0;
for (int query = 0; query < 10000; query++)
{
    int t = query % 48 + 1;
    Vector2 point = query % 2 == 0
        ? indexedShapes[query % 300].Start + indexedShapes[query % 300].Velocity * t
        : new(randomField.Next(-2500, 2500), randomField.Next(-2500, 2500));
    bool Hits(int id)
    {
        var s = indexedShapes[id];
        return SweptBox.Intersects(s.Start + s.Velocity * (t - 1), s.Start + s.Velocity * t, point, s.Half);
    }
    var candidates = buckets.At(point, t);
    int indexedHits = candidates?.Count(Hits) ?? 0;
    indexedCount += candidates?.Count ?? 0;
    equivalent &= indexedHits == Enumerable.Range(0, indexedShapes.Length).Count(Hits);
}
Check(equivalent && indexedCount < 300L * 10000 / 10,
    "time/spatial broad phase preserves exhaustive swept collision counts across 10000 queries including negative coordinates and reduces candidates by over 90%");
buckets.Clear(); buckets.Add(777, 1, new(-256, -256), Vector2.Zero);
Check(buckets.At(new(-256, -256), 1)?.SequenceEqual(new[] { 777 }) == true
    && buckets.At(Vector2.Zero, 1)?.SequenceEqual(new[] { 777 }) == true
    && buckets.At(Vector2.Zero, 2) == null,
    "broad-phase reset removes stale hazards and includes exact cell edges at the correct future tick");
var trueEye = new TrueEyeForecast();
var pendingLeft = LeftEyeForecast.Pending(new(100, 200), new(1.04f, 0), 1, 12, 0,
    new(1000, 500), Vector2.Zero, 48);
Check(pendingLeft is { Born: 2 } && pendingLeft.Value.Origin == new Vector2(100, 194)
    && Math.Abs(FinalSparkForecast.Wrap(pendingLeft.Value.Angle - (MathF.PI + MathF.PI * 60 / 135))) < .00001f,
    "left eye stops below speed one then fires on the following tick without updating its frozen pupil");
Check(LeftEyeForecast.Pending(Vector2.Zero, Vector2.Zero, 2, 1, 0, new(100, 0), Vector2.Zero, 48) == null,
    "an already fired left eye does not duplicate its existing native laser as another future ray");
float recordedEyeAngle = MathF.Atan2(.9274551f, .37393442f), recordedEyeTurn = .023271058f;
float recordedPupil = recordedEyeAngle + recordedEyeTurn * (60 - 19) - MathF.PI;
var recordedEyeRay = LeftEyeForecast.Pending(new(34086.12f, 5515.4487f), Vector2.Zero, 2, 0,
    recordedPupil, new(33900, 5800), Vector2.Zero, 48)!.Value;
Check(recordedEyeRay.Intersects(new(34226.38f, 5894.994f), new(10, 21), 20)
    && !recordedEyeRay.Intersects(new(34226.38f, 5894.994f), new(10, 21), 0),
    "an unspawned left-eye ray includes the recorded first-hit crossing after birth without marking the warning period harmful");
var spherePath = new Vector2[49];
trueEye.Update(Vector2.Zero, new(10, 0), 1, 58, 1, new(1000, 0), Vector2.Zero);
TrueEyeForecast.Sphere(spherePath, new(100, 0), new(10, 0), 302, trueEye, true);
Check(Math.Abs(spherePath[1].X - 109.5f) < .001f && Math.Abs(spherePath[3].X - 118.525f) < .001f
    && Math.Abs(spherePath[4].X - 142.525f) < .001f && Math.Abs(spherePath[24].X - 622.525f) < .001f,
    "true-eye spheres decelerate with their parent, stop at its phase change, then inherit its 24px launch before coasting");
TrueEyeForecast.Sphere(spherePath, new(100, 0), new(10, 0), 302, trueEye, false);
Check(Math.Abs(spherePath[1].X - 110) < .001f && Math.Abs(spherePath[4].X - 128.525f) < .001f
    && Math.Abs(spherePath[5].X - 152.525f) < .001f,
    "a sphere in an earlier slot reads the parent's previous update rather than launching one tick early");
TrueEyeForecast.Sphere(spherePath, Vector2.Zero, new(3, 4), 295, trueEye, true);
Check(spherePath[24] == new Vector2(72, 96),
    "old true-eye spheres detach at timeLeft 295 and preserve their own velocity");
var worm = new WormForecast(10);
var wormNodes = new[] { new WormNode(1, -1, Vector2.Zero, new(10, 0), MathF.PI / 2, true, 0, 1, 42),
    new WormNode(2, 1, new(-36, 0), Vector2.Zero, MathF.PI / 2, false, 0, 0, 30),
    new WormNode(3, 2, new(-72, 0), Vector2.Zero, MathF.PI / 2, false, 0, 0, 30) };
worm.Update(wormNodes, new(1000, 0), Vector2.Zero);
Check(Vector2.Distance(worm.Path(3)[24], new(168, 0)) < .001f,
    "worm tail follows the moving chain even though each live body velocity is zero");
worm.Update(new[] { wormNodes[1] with { Slot = 0 }, wormNodes[0] }, new(1000, 0), Vector2.Zero);
Check(Vector2.Distance(worm.Path(0)[1], new(-36, 0)) < .001f,
    "a worm segment reads the old parent position when its projectile slot updates first");
var dashOrbs = new List<TrailOrb>();
SpearTrail.ForecastDash(dashOrbs, new(100, 0), new(45, 0), 0, false, 7);
Check(dashOrbs.Count == 6 && dashOrbs[0].Born == 1 && dashOrbs[4].Born == 7
    && Vector2.Distance(dashOrbs[0].Start, new(144, 0)) < .001f,
    "dashing spear predicts its own paired trail every three ticks before those orbs spawn");
var forecastAudit = new ForecastAudit();
forecastAudit.Expect(100, 7, 30, 1289, 24, new(20, 0), MathF.PI - .01f);
forecastAudit.Observe(123, _ => new(30, 1289, new(999, 999), 0));
Check(forecastAudit.Snapshot().Length == 0, "projectile audit waits for the predicted game tick");
forecastAudit.Observe(124, _ => new(30, 1289, new(23, 4), -MathF.PI + .01f));
var forecastRow = forecastAudit.Snapshot().Single();
Check(forecastRow.Samples == 1 && Math.Abs(forecastRow.MeanPx - 5) < .001
    && forecastRow.MaxAngleDegrees < 1.2f,
    "projectile audit measures later real displacement and wraps angles across pi");
forecastAudit.Expect(125, 7, 30, 1289, 6, Vector2.Zero);
forecastAudit.Observe(131, _ => new(31, 1289, new(999, 999), 0));
Check(forecastAudit.Snapshot().Single(r => r.Horizon == 6) is { Samples: 0, Missing: 1 },
    "reused projectile slots are recorded as missing observations rather than false model error");
forecastAudit.Expect(200, 7, 30, 1289, 1, new(float.NaN, 0));
forecastAudit.Expect(200, 7, 30, 1289, 1, Vector2.Zero, 0);
forecastAudit.Observe(201, _ => new(30, 1289, Vector2.Zero, float.PositiveInfinity));
Check(forecastAudit.Snapshot().Single(r => r.Horizon == 1) is { Samples: 0, Invalid: 2 }
    && !JsonSerializer.Serialize(forecastAudit.Snapshot()).Contains("NaN"),
    "nonfinite forecasts and observations cannot poison telemetry or throw during JSON recording");
var flight = new MotionModel { FlightMastery = true, Gravity = .6f, JumpSpeed = 13.91f };
var windupMotion = new MotionModel { FlightMastery = true, Gravity = .6f, JumpSpeed = 13.91f,
    WalkSpeed = 8.67f, MaxX = 18, Acceleration = 1.3125f, Braking = 1, StopBraking = 8 };
Vector2 aimOrigin = new(20087.863f, 4795.371f);
var aimState = new MotionState(new(20176.348f, 4774.416f), new(0, -2.30999f));
var aimUnsafe = windupMotion.Step(aimState, new(0, 2));
float aimAngle = ProjectileKinematics.SpearAimAngle(aimOrigin, aimUnsafe.Position, aimUnsafe.Velocity,
    4, -.79622644f, true, 1);
float MovingAimRisk(MotionState candidate, int tick)
{
    float angle = ProjectileKinematics.SpearAimAngle(aimOrigin, candidate.Position, candidate.Velocity,
        4, -.79622644f, true, tick) - MathF.PI * .75f;
    Vector2 arm = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 100;
    return SweptBox.Intersects(aimOrigin - arm, aimOrigin + arm, candidate.Position, new(17, 28))
        || Vector2.Distance(candidate.Position, aimOrigin) < 70 ? 1_000_000 : 0;
}
Check(Vector2.Distance(aimUnsafe.Position, new(20176.348f, 4769.796f)) < .005f
    && Math.Abs(FinalSparkForecast.Wrap(aimAngle + 1.0764651f)) < .001f && MovingAimRisk(aimUnsafe, 1) > 0,
    "recorded spear-aim hit is reproduced by its rotation toward the newly selected player velocity, not a frozen observed direction");
var aimEscape = new CombatPlanner().ChooseWithState(new(aimState, aimOrigin + new Vector2(455, 0), aimOrigin, 650, true),
    windupMotion, MovingAimRisk, new(0, 2), 5, 20);
Check(MovingAimRisk(aimEscape.Next, 1) == 0,
    "state-aware planning rejects the recorded unsafe input and chooses a first step outside the resulting spear and boss contact");
Vector2 rotatingVictim = new(500, 27), originalHalf = new(13, 24), inverseVictim = new(27, -500);
Vector2 rotatedHalf = ProjectileKinematics.RotatedHalfSize(originalHalf, MathF.PI / 2);
Check(SweptBox.Intersects(new(-1000, 0), new(1000, 0), rotatingVictim, originalHalf + new Vector2(4))
    && !SweptBox.Intersects(new(0, -1000), new(0, 1000), inverseVictim, originalHalf + new Vector2(4))
    && SweptBox.Intersects(new(0, -1000), new(0, 1000), inverseVictim, rotatedHalf + new Vector2(4)),
    "rotating a native collision query also encloses the rotated nonsquare player box, preserving a previously missed beam edge");
var windupPlanner = new CombatPlanner();
var windupState = new MotionState(new(28587.389f, 7041.3403f), new(1.3125f, -2.1525f));
Vector2 windupBoss = new(28471.523f, 6751.5f), windupNormal = new(-MathF.Sin(1.1735028f), MathF.Cos(1.1735028f));
Move windupPrevious = new(1, 1);
for (int t = 0; t < 20; t++)
{
    Vector2 windupGoal = ProjectileKinematics.LockedSpearGoal(windupState.Position, windupBoss, 1.1735028f, 1);
    var choice = windupPlanner.Choose(new(windupState, windupGoal, windupBoss, 650, true), windupMotion,
        (_, _) => 0, windupPrevious, 2);
    windupState = choice.Next; windupPrevious = choice.Move;
}
Check(Vector2.Dot(windupState.Position - windupBoss, windupNormal) > 200
    && Vector2.Distance(windupState.Position, windupBoss) < 850,
    "locked predictive-spear windup leaves the recorded incoming dash axis before launch while retaining an offensive position");
var boundedFlight = new MotionModel { FlightMastery = true, Gravity = .6f, JumpSpeed = 13.91f,
    WorldMinimum = new(666, 677), WorldMaximum = new(66518, 18507) };
var atCeiling = boundedFlight.Step(new(new(1000, 677), new(0, -.1f)), new(0, 2));
Check(atCeiling.Position.Y == 677 && Math.Abs(atCeiling.Velocity.Y - .11f) < .0001f,
    "world ceiling reproduces the recorded Y677 clamp and vanilla downward velocity rather than forecasting unreachable sky");
var atSide = boundedFlight.Step(new(new(666, 1000), new(-18, 0)), new(-1, 3));
Check(atSide.Position.X == 666 && atSide.Velocity.X == 0,
    "world side limit includes the 640px vanilla border and player half-width");
var ceilingEscape = new CombatPlanner().Choose(new(new(new(1000, 677), new(0, .11f)), new(1000, 797),
    new(1400, 1000), 650, true), boundedFlight, (p, _) => p.Y < 701 ? 1_000_000 : 0, new(0, 2), 100);
Check(ceilingEscape.Move.Vertical == 0 && ceilingEscape.Next.Position.Y > 677,
    "leaves the real ceiling through normal falling instead of perpetually flying into a phantom goal");
Check(ProjectileKinematics.SanctuaryArms(0, 118, false) == 4
    && ProjectileKinematics.SanctuaryArms(255, 118, true) == 37,
    "nuke sanctuary warns before the first random bomb activates it, while respecting its fade-in");
var nukePath = new Vector2[4];
ProjectileKinematics.Ballistic(nukePath, Vector2.Zero, new(2, -1), .2f);
Check(Vector2.Distance(nukePath[3], new(6, -1.8f)) < .001f,
    "nuke forecast integrates its installed gravity before each movement rather than drifting linearly");
var curvedPath = new Vector2[25];
var homingPath = new Vector2[25];
ProjectileKinematics.HomingEye(homingPath, new(600, 0), new(0, 10), -10, 14, true, false, Vector2.Zero, Vector2.Zero);
Check(homingPath[1].X < -1 + 600 && Vector2.Distance(homingPath[24], new(600, 240)) > 100,
    "pursuing eye accelerates and turns toward the real target instead of keeping a long linear extrapolation");
ProjectileKinematics.HomingEye(homingPath, new(100, 0), new(15, 0), -10, 0, true, false, Vector2.Zero, Vector2.Zero);
Check(Vector2.Distance(homingPath[24], new(460, 0)) < .001f,
    "fleeing eye already facing away remains straight at its installed speed");
ProjectileKinematics.Boomerang(curvedPath, new(27845.316f, 3149.3948f), new(-10.496f, 3.2872f), 10.995575f, -.2302908f);
Check(Vector2.Distance(curvedPath[1], new(27834.893f, 3152.901f)) < .01f
    && Vector2.Distance(curvedPath[24], new Vector2(27845.316f, 3149.3948f) + new Vector2(-10.496f, 3.2872f) * 24) > 40,
    "curved boomerang reproduces the recorded hit-source movement and does not retain its large linear drift");
var hover = flight.Step(new(new Vector2(0, 100), new Vector2(0, 20)), new(0, 3));
Check(Math.Abs(hover.Position.Y - 80) < .01f && Math.Abs(hover.Velocity.Y) < .01f,
    "Flight Mastery hover cancels incoming displacement as the installed accessory does");
var rising = new MotionState(Vector2.Zero, new Vector2(0, -14));
for (int t = 0; t < 100; t++) rising = flight.Step(rising, new(0, 1));
Check(Math.Abs(rising.Velocity.Y + 24.3425f) < .01f,
    "Flight Mastery ascent reaches the equipped jump-speed cap without fitting a fictitious lower cap");
var measuredFlight = new MotionModel { FlightMastery = true, Gravity = .6f, JumpSpeed = 13.71f,
    WalkSpeed = 8.52f, MaxX = 18, Acceleration = 1.3125f, Braking = 1, StopBraking = 8 };
var measuredReverse = measuredFlight.Step(new(Vector2.Zero, new Vector2(6.5625f, -11.549991f)), new(-1, 2));
Check(Vector2.Distance(measuredReverse.Velocity, new(5.25f, -13.859991f)) < .001f,
    "recorded frame 6 uses equipped acceleration and jump threshold rather than reset defaults");
var measuredRelease = measuredFlight.Step(new(Vector2.Zero, new Vector2(7.4875f, -21.862497f)), new(0, 1));
Check(Vector2.Distance(measuredRelease.Velocity, new(3.4875f, -21.997498f)) < .001f,
    "Fargo NoMomentum adds stopping drag for neutral input without changing forward drag");
var measuredFast = measuredFlight.Step(new(Vector2.Zero, new Vector2(9.1875f, -22.769987f)), new(1, 2));
Check(Vector2.Distance(measuredFast.Velocity, new(9.7125f, -23.579987f)) < .001f,
    "recorded wing flight above walking speed uses the effective 0.4 acceleration branch");
var tapStart = measuredFlight.Step(new(Vector2.Zero, new(12.112511f, -23.9925f),
    new(1, 1, 0, 4, true, true)), new(1, 1));
Check(Math.Abs(tapStart.Velocity.X - 16.9f) < .001f && tapStart.Dash.Delay == -1,
    "recorded double-tap starts a real Tabi dash inside candidate motion instead of being an unexplained jump");
var dashCoast = measuredFlight.Step(new(Vector2.Zero, new(16.9f, -23.9925f),
    new(1, 1, -1, 0, true, false)), new(0, 1));
Check(Math.Abs(dashCoast.Velocity.X - 16.268799f) < .001f && dashCoast.Dash.Delay == -1,
    "active Tabi dash disables NoMomentum stopping drag and applies vanilla dash decay");
var dashEnd = measuredFlight.Step(new(Vector2.Zero, new(11.989544f, -23.9925f),
    new(1, 1, -1, 0, true, true)), new(0, 1));
Check(Math.Abs(dashEnd.Velocity.X - 18) < .001f && dashEnd.Dash.Delay == 20,
    "recorded Tabi dash completion snaps to equipped run speed and enters cooldown in the forecast");
var state = new MotionState(new Vector2(-1400, 0), Vector2.Zero);
Move previous = new(0, 3);
for (int tick = 0; tick < 180; tick++)
{
    var frame = new PlanFrame(state, new Vector2(-400, 100), Vector2.Zero, 650, true);
    Decision d = planner.Choose(frame, motion, (_, _) => 0, previous, 100);
    state = motion.Step(state, d.Move); previous = d.Move;
}
Check(Vector2.Distance(state.Position, Vector2.Zero) < 650, "returns to attack range instead of escaping indefinitely");
Check(Math.Abs(state.Position.Y - 100) < 100, "holds combat altitude without perpetual ascent");

state = new(Vector2.Zero, Vector2.Zero);
Decision incoming = planner.Choose(new(state, new Vector2(300, 0), new Vector2(600, 0), 650, true), motion,
    (p, t) => Vector2.Distance(p, new Vector2(90 - 8 * t, 0)) < 30 ? 1_000_000 : 0,
    new(0, 3), 100);
var dodgeState = state;
bool dodgeHit = false;
for (int t = 1; t <= 8; t++)
{
    dodgeState = motion.Step(dodgeState, incoming.Move);
    dodgeHit |= Vector2.Distance(dodgeState.Position, new Vector2(90 - 8 * t, 0)) < 30;
}
Check(!dodgeHit, "incoming projectile changes immediate movement to avoid collision");

Decision delayed = planner.Choose(new(new(Vector2.Zero, Vector2.Zero), Vector2.Zero,
    new Vector2(500, 0), 650, true), motion,
    (p, t) => t >= 18 && p.Length() < 100 ? 1_000_000 : 0, new(0, 3), 0);
Check(delayed.Horizon == CombatPlanner.ForecastTicks && delayed.Move != new Move(0, 3),
    "every initial action sees a delayed explosion even when refinement has no time budget");

var trail = new List<TrailOrb>();
SpearTrail.Forecast(trail, new(0, 0), new(25, 0), 0, 2, 24);
var firstBlast = trail[0].CenterAt(trail[0].Explodes);
state = new(firstBlast, Vector2.Zero);
Decision beforeSpawn = planner.Choose(new(state, firstBlast, new Vector2(500, 120), 650, true), motion,
    (p, t) => trail.Any(orb => t >= orb.Explodes &&
        Vector2.Distance(p, orb.CenterAt(orb.Explodes)) < 104) ? 1_000_000 : 0,
    new(0, 3), 100);
Check(beforeSpawn.Move != new Move(0, 3),
    "a moving spear triggers avoidance before its child orb exists, using the child's future explosion");
var markerBeam = new TimedBeam(Vector2.Zero, Vector2.UnitX, 14, 65);
state = new(new(260, 10), Vector2.Zero);
Decision beforeBeam = planner.Choose(new(state, state.Position, new(600, 10), 650, true), motion,
    (p, t) => markerBeam.Intersects(p, new(13, 24), t) ? 1_000_000 : 0, new(0, 3), 100);
Check(beforeBeam.Move.Vertical != 3,
    "an expiring marker prompts vertical escape before its delayed deathray exists");
Vector2 swordRecorded = SwordOrbit.At(new(37375.26f, 5194.3994f), new(16.613f, 2.2705f),
    240, -1.658065f + 1.2566371f, .15707964f, 3);
Check(Vector2.Distance(swordRecorded, new(37664.516f, 5217.952f)) < .1f,
    "sword orbit reproduces the recorded hit source three ticks after the preceding boss snapshot");
Vector2 boltFrom = new(29260.037f, 3151.699f), boltTo = new(29282.045f, 3174.9304f);
Vector2 boltVictim = new(29264.094f, 3181.8933f), combinedHalf = new(17, 28);
Check(!SweptBox.Intersects(boltTo, boltTo, boltVictim, combinedHalf)
    && SweptBox.Intersects(boltFrom, boltTo, boltVictim, combinedHalf),
    "recorded four-update PhantasmalBolt collision is found along its path even though the final point misses");
var parentPath = new Vector2[CombatPlanner.ForecastTicks * 2 + 1];
var fragmentPath = new Vector2[25];
var lancePath = new Vector2[25];
ProjectileKinematics.Lance(lancePath, Vector2.Zero, Vector2.Zero, 59, 0);
Check(lancePath[1] == new Vector2(40, 0) && lancePath[2] == new Vector2(80, 0)
    && ProjectileKinematics.LanceArms(59) == 2 && ProjectileKinematics.LanceArms(61) == 0,
    "vanilla fairy lance warning forecasts its 40px launch and the first damaging age 61");
ProjectileKinematics.Decay(fragmentPath, new(20277.107f, 4493.057f), new Vector2(14.040654f, 10.136801f) / .985f, .985f);
Check(Vector2.Distance(fragmentPath[1], new(20291.148f, 4503.194f)) < .005f
    && Vector2.Distance(fragmentPath[24], fragmentPath[0] + new Vector2(14.040654f, 10.136801f) / .985f * 24) > 50,
    "fragment decay reproduces the recorded first-hit source and avoids a large 24-tick linear drift");
var scythePath = new Vector2[25];
ProjectileKinematics.Scythe(scythePath, Vector2.Zero, Vector2.Zero, 181, .03f, 0);
Check(scythePath[2] == Vector2.Zero && Math.Abs(scythePath[3].X - 1.03f) < .001f && scythePath[24].X > 20,
    "stationary scythes are forecast to launch and accelerate below timeLeft 180");
var shards = new List<DecayingShard>();
PillarBurst.Forecast(shards, Vector2.Zero, 0, 10, true);
Check(shards.Count == 96 && !shards[0].Intersects(Vector2.Zero, new(13, 24), 9)
    && shards[0].Intersects(shards[0].At(12), new(13, 24), 12),
    "a launched pillar forecasts all four rings of future decaying fragments before its death");
var straightShard = new DecayingShard(Vector2.Zero, new(0, 19.25f), 10);
Check(!straightShard.Intersects(straightShard.At(12) + new Vector2(40, 0), new(13, 24), 12),
    "pillar forecast preserves an open lane beside a fragment instead of inflating it by a whole pillar step");
Check(FinalSparkForecast.Scale(3, true) < 5 && FinalSparkForecast.Scale(4, true) >= 5
    && FinalSparkForecast.Scale(776, true) >= 5 && FinalSparkForecast.Scale(777, true) < 5,
    "Masochist final beam scale predicts its real damaging ages rather than its refreshed two-tick lifetime");
Check(FinalSparkForecast.TouchesBeam(new(0,-101),-Vector2.UnitY,10,3000,new(120,-145),new(19))
    && !FinalSparkForecast.TouchesBeam(new(0,-176),-Vector2.UnitY,10,3000,new(120,-145),new(19))
    && FinalSparkForecast.Scale(212,true,255)==10 && FinalSparkForecast.Scale(254,true,255)<5
    && FinalSparkForecast.Scale(255,true,255)==0 && FinalSparkForecast.Scale(254,true)==10,
    "death-animation beam reproduces the recorded hidden hit with native 100px origin and 255-tick lifetime");
Check(!FinalSparkForecast.PendingDeathTouches(119,2,Vector2.Zero,new(10,-583),new(13),true)
    && FinalSparkForecast.PendingDeathTouches(119,3,Vector2.Zero,new(10,-583),new(13),true)
    && !FinalSparkForecast.PendingDeathTouches(119,3,Vector2.Zero,new(350,200),new(13),true)
    && !FinalSparkForecast.PendingDeathTouches(119,3,Vector2.Zero,new(10,-583),new(13),false),
    "death beam predicts the recorded age-two hit before birth, with a safe lower flank and Masochist-only damage");
float recordedBeamAngle = FinalSparkForecast.Chase(5.9294024f, new(17637.371f, 2428.6477f),
    new(18621.494f, 2199.7603f), true);
Check(Math.Abs(FinalSparkForecast.Wrap(recordedBeamAngle - MathF.Atan2(-.32656243f, .94517565f))) < .00001f,
    "coupled one-step beam chase reproduces the actual first giant-beam hit direction from the prior live trial");
Check(FinalSparkForecast.OrbitSide(0, MathF.PI - .35f) == -1
    && FinalSparkForecast.OrbitSide(0, MathF.PI + .35f) == 1
    && FinalSparkForecast.OrbitGoal(new(500, 0), Vector2.Zero, -1).Y < 0,
    "final beam route follows the chasing beam direction and changes side for its reversed second sweep");
var sparkMotion = new MotionModel { FlightMastery = true, MaxX = 18, WalkSpeed = 8.669999f,
    Acceleration = 1.3125f, Braking = 1, StopBraking = 8, Gravity = .6f,
    MaxFall = 10.01f, JumpSpeed = 13.91f };
bool sparkHit = false;
var sparkStarts = new[] {
    (Side: -1, Position: new Vector2(500, 0), Velocity: Vector2.Zero, Angle: MathF.PI - .35f, LeftReleased: true),
    (Side: 1, Position: new Vector2(500, 0), Velocity: Vector2.Zero, Angle: MathF.PI + .35f, LeftReleased: true),
    (Side: 1, Position: new Vector2(-141.478f, 530.193f), Velocity: new Vector2(-16.012495f, -4.045001f), Angle: .9978909f, LeftReleased: false) };
foreach (var sparkStart in sparkStarts)
foreach (float sparkRadius in new[] { 150f, 350f })
{
int sparkSide = sparkStart.Side;
var sparkPlanner = new CombatPlanner();
var sparkState = new MotionState(sparkStart.Position, sparkStart.Velocity, new(1, 1, 0, 0, sparkStart.LeftReleased, true));
float sparkAngle = sparkStart.Angle;
Move sparkPrevious = new(0, 3);
for (int clock = 0; clock < 600; clock++)
{
    var route = sparkPlanner.ChooseCoupled(new(sparkState, FinalSparkForecast.OrbitGoal(sparkState.Position, Vector2.Zero, sparkSide, sparkRadius),
        Vector2.Zero, 650, false), sparkMotion, sparkAngle,
        (angle, state, _) => FinalSparkForecast.Chase(angle, Vector2.Zero, state.Position, true),
        (state, t, angle) => {
            Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
            return (FinalSparkForecast.TouchesBeam(direction * 176, direction, 10, 3000, state.Position, new(19, 30))
                || state.Position.Length() > 1100 ? 1_000_000 : 0)
                    + FinalSparkForecast.RadialCost(state.Position, Vector2.Zero);
        }, sparkPrevious, 0, 0);
    sparkState = sparkMotion.Step(sparkState, route.Move); sparkPrevious = route.Move;
    // The actual pursuit follows the newly moved player, not the constant-velocity forecast.
    sparkAngle = FinalSparkForecast.Chase(sparkAngle, Vector2.Zero, sparkState.Position, true);
    Vector2 actualDirection = new(MathF.Cos(sparkAngle), MathF.Sin(sparkAngle));
    sparkHit |= FinalSparkForecast.TouchesBeam(actualDirection * 176, actualDirection, 10, 3000,
        sparkState.Position, new(19, 30));
}
}
Check(!sparkHit, "coupled planning follows both beam orbit directions and the recorded approach for 600 ticks at both 150px and 350px targets");
var moonBoxes = new List<TimedBox>();
MoonBlast.Forecast(moonBoxes, new(27574.2f, 2641.5781f), new(131), -2.1661634f, 4, 5, 1, 24);
Vector2 moonVictim = new(27612.592f, 2501.3682f);
Check(moonBoxes[0].Intersects(moonVictim, new(13, 24), 1)
    && !moonBoxes[0].Intersects(moonVictim, new(13, 24), 5),
    "recorded moon blast is predicted while still warming and stops damaging after frame four");
var chainBoxes = new List<TimedBox>();
MoonBlast.Forecast(chainBoxes, Vector2.Zero, new(100), 0, 48, 1, 0, 24);
Check(chainBoxes.Any(b => b.ArmsAt <= 10 && b.Intersects(new(100, 26), new(13, 24), 10))
    && chainBoxes.Any(b => b.Center.X == 0 && b.HalfSize.X >= 160),
    "moon chain forecasts forward children and stationary repeats with random-position bounds before they exist");
var spentBoxes = new List<TimedBox>();
MoonBlast.Forecast(spentBoxes, Vector2.Zero, new(100), 0, 48, 10, 0, 24);
Check(spentBoxes.Count == 0, "spent moon blast neither damages nor generates already-spawned children again");
var leafPath = new Vector2[CombatPlanner.ForecastTicks * 2 + 1];
LeafOrbit.Forecast(leafPath, parentPath, 0, 0);
state = new(new(-100, 0), Vector2.Zero);
var leafPlanner = new CombatPlanner();
Move leafPrevious = new(0, 3);
bool leafHit = false;
for (int clock = 0; clock < 24; clock++)
{
    Decision orbitEscape = leafPlanner.Choose(new(state, new(-100, 0), new(400, 0), 650, true), motion,
        (p, t) => Vector2.Distance(p, leafPath[t + clock]) < 45 ? 1_000_000 : 0, leafPrevious, 100);
    state = motion.Step(state, orbitEscape.Move); leafPrevious = orbitEscape.Move;
    leafHit |= Vector2.Distance(state.Position, leafPath[clock + 1]) < 45;
}
Check(!leafHit, "avoids the returning crystal leaf over a complete orbit rather than requiring one particular first input");

Decision top = planner.Choose(new(new(new Vector2(0, 20), new Vector2(0, -8)), new Vector2(0, -500),
    new Vector2(300, 200), 650, true), motion,
    (p, _) => p.Y < 0 ? 1_000_000 : 0, new(0, 2), 100);
Check(top.Move.Vertical is 0 or 3, "world ceiling overrides the offensive target through normal input");
Decision inevitable = planner.Choose(new(new(Vector2.Zero, Vector2.Zero), Vector2.Zero,
    new Vector2(600, 0), 650, true), motion,
    (p, t) => p.X < -.01f ? (t == 2 ? 1_000_000 : 0)
        : p.X > .01f ? (t >= 10 ? 1_000_000 : 0) : (t >= 3 ? 1_000_000 : 0), new(0, 3), 100);
Check(inevitable.Move.Horizontal == 1, "when every path fails, delay the first hit instead of accepting an early cheaper hit");

var escaped = new BattleMetrics(); escaped.VerifyTargetMode(true); escaped.Tick(false); escaped.Damage(1000);
var corridorPlanner = new CombatPlanner();
var corridorMotion = new MotionModel { MaxX = 1, Acceleration = 1, Braking = 1,
    Gravity = 0, MaxFall = 0, MaxRise = 1, RiseAcceleration = 1 };
MotionState corridorState = new(Vector2.Zero, Vector2.Zero);
int corridorClock = 0;
float Corridor(Vector2 p, int t)
{
    int at = corridorClock + t;
    Vector2 required = at <= 6 ? new(at, 0) : new(6, 6 - at);
    return Vector2.DistanceSquared(p, required) > .01f ? 1_000_000 : 0;
}
var elbow = corridorPlanner.Choose(new(corridorState, new(6, -18), new(6, -18), 650, true),
    corridorMotion, Corridor, new(0, 3), 100);
Check(elbow.FirstHit == 0, "finds a feasible route through a timed right-angle corridor");
corridorState = corridorMotion.Step(corridorState, elbow.Move); corridorClock++;
var retained = corridorPlanner.Choose(new(corridorState, new(6, -18), new(6, -18), 650, true),
    corridorMotion, Corridor, elbow.Move, 0);
Check(retained.FirstHit == 0,
    "retains the known-safe turn now five ticks away even with no refinement budget");
Check(retained.Evaluations < 13 * CombatPlanner.ForecastTicks,
    "stops strictly dominated colliding candidates before wasting the rest of their horizon");
float MultiTurn(Vector2 p, int t)
{
    Vector2 required = t <= 3 ? new(t, 0) : t <= 6 ? new(3, 3 - t) : new(t - 3, -3);
    return Vector2.DistanceSquared(p, required) > .01f ? 1_000_000 : 0;
}
var turnFrame = new PlanFrame(new(Vector2.Zero, Vector2.Zero), new(21, -3), new(21, -3), 650, true);
var cheapTurn = new CombatPlanner().Choose(turnFrame, corridorMotion, MultiTurn, new(0, 3), 0, 0);
var rescuedTurn = new CombatPlanner().Choose(turnFrame, corridorMotion, MultiTurn, new(0, 3), 0, 100);
Check(cheapTurn.FirstHit > 0 && rescuedTurn.FirstHit == 0 && rescuedTurn.Horizon == CombatPlanner.ForecastTicks && rescuedTurn.RescueDepth >= 6,
    "bounded beam rescue finds two timed turns that constant inputs and a single turn cannot traverse");
Check(!escaped.NoHitVictory, "damage plus survival or disappearance is not a victory");
escaped.Kill();
Check(escaped.NoHitVictory, "verified kill with zero hits passes");
Check(escaped.ResultReason("boss_disappeared") == "boss_killed",
    "confirmed kill remains the reported outcome when the NPC disappears on the following frame");
var trapMotion = new MotionModel { MaxX = 5, Acceleration = 1, Braking = 1, Gravity = 0, MaxFall = 0 };
var earlyEscape = new CombatPlanner().Choose(new(new(Vector2.Zero, Vector2.Zero), new(20, 0), new(20, 0), 650, false),
    trapMotion, (p, t) => Math.Abs(p.Y) > .01f || (t >= 36 && p.X >= -160) ? 1_000_000 : 0,
    new(0, 3), 0, 0);
Check(earlyEscape.Move.Horizontal == -1 && earlyEscape.FirstHit == 0,
    "soft strategic horizon commits to escape before a trap beyond the near-term safety horizon becomes unavoidable");
float DistantTurns(Vector2 p, int t)
{
    if (t < 30) return 0;
    return Vector2.DistanceSquared(p, new(t - 3, -3)) > .01f ? 1_000_000 : 0;
}
var distantFrame = new PlanFrame(new(Vector2.Zero, Vector2.Zero), new(45, -3), new(45, -3), 650, false);
var distantCheap = new CombatPlanner().Choose(distantFrame, corridorMotion, DistantTurns, new(0, 3), 0, 0);
var distantRescue = new CombatPlanner().Choose(distantFrame, corridorMotion, DistantTurns, new(0, 3), 0, 100);
// Check the full-window collision field directly. A safe feedback route can
// brake several times and have a larger input-change cost than the old route.
Check(distantCheap.FirstHit == 0 && distantCheap.ForecastFirstHit > CombatPlanner.SafetyTicks
    && distantRescue.ForecastFirstHit == 0 && distantRescue.RescueDepth > 0,
    "searches multiple turns for a distant closing passage before it enters the near-term safety horizon");
var cachePlanner = new CombatPlanner();
int collisionQueries = 0;
float CountedCorridor(Vector2 p, int t) { collisionQueries++; return MultiTurn(p, t); }
var cachedTurn = cachePlanner.Choose(turnFrame, corridorMotion, CountedCorridor, new(0, 3), 0, 100);
Check(cachedTurn.FirstHit == 0 && cachedTurn.CachedQueries > 0
    && collisionQueries == cachedTurn.DangerQueries
    && collisionQueries + cachedTurn.CachedQueries == cachedTurn.Evaluations,
    "shared-prefix collision queries are reused while the complete multi-turn escape remains safe");
collisionQueries = 0;
var changedWorld = cachePlanner.Choose(turnFrame, corridorMotion, (p, t) => { collisionQueries++; return 1_000_000; }, new(0, 3), 0, 0);
Check(changedWorld.FirstHit == 1 && collisionQueries > 0,
    "collision cache is discarded on the next observed game frame rather than retaining stale safety");
var hitRun = new BattleMetrics(); hitRun.Tick(false); hitRun.Damage(1000); hitRun.Hurt(); hitRun.Kill();
Check(!hitRun.NoHitVictory, "any recorded hit fails the no-hit objective");
var godRun = new BattleMetrics(); godRun.Tick(true); godRun.Tick(false); godRun.Damage(1000); godRun.Kill();
Check(!godRun.NoHitVictory, "god mode invalidates the entire run even after it is disabled");
var idleKill = new BattleMetrics(); idleKill.Tick(false); idleKill.Kill();
Check(!idleKill.NoHitVictory, "no actual dealt damage cannot pass as an agent kill");
var resumed = new BattleMetrics(); resumed.Invalidate(); resumed.Tick(false); resumed.Damage(1000); resumed.Kill();
Check(!resumed.NoHitVictory, "manual release and resume cannot reset a failed encounter into a no-hit victory");
var wrongMode = new BattleMetrics(); wrongMode.Tick(false); wrongMode.Damage(1000); wrongMode.Kill();
Check(!wrongMode.NoHitVictory, "unverified difficulty cannot pass as Masochist success");

var costs = new List<double>();
for (int i = 0; i < 100; i++)
{
    Decision d = planner.Choose(new(new(Vector2.Zero, Vector2.Zero), new Vector2(300, 100),
        new Vector2(650, 0), 650, true), motion, (_, _) => 0, new(0, 3));
    costs.Add(d.Milliseconds);
}
costs.Sort();
Console.WriteLine($"Core benchmark: median={costs[50]:F3}ms p95={costs[95]:F3}ms (synthetic field; not game performance)");

if (args.Length == 2 && args[0] == "--replay")
{
    var frames = new List<JsonElement>();
    foreach (string line in File.ReadLines(args[1]))
    {
        try
        {
            using var record = JsonDocument.Parse(line);
            if (record.RootElement.GetProperty("kind").GetString() == "frame")
                frames.Add(record.RootElement.Clone());
        }
        catch (JsonException) { /* An active recording can have an unfinished final line. */ }
    }
    var residuals = new List<float>();
    foreach (var pair in frames.Zip(frames.Skip(1)))
    {
        var a = pair.First; var b = pair.Second;
        if (b.GetProperty("tick").GetInt32() != a.GetProperty("tick").GetInt32() + 1
            || b.GetProperty("hits").GetInt32() != 0 || !a.TryGetProperty("motion", out var p)) continue;
        float F(string key) => p.GetProperty(key).GetSingle();
        var calibrated = new MotionModel { FlightMastery = p.GetProperty("FlightMastery").GetBoolean(),
            MaxX = F("MaxX"), WalkSpeed = F("WalkSpeed"), Acceleration = F("Acceleration"),
            Braking = F("Braking"), StopBraking = F("StopBraking"), Gravity = F("Gravity"),
            MaxFall = F("MaxFall"), JumpSpeed = F("JumpSpeed") };
        if (a.TryGetProperty("worldMinX", out var minX))
        {
            calibrated.WorldMinimum = new(minX.GetSingle(), a.GetProperty("worldMinY").GetSingle());
            calibrated.WorldMaximum = new(a.GetProperty("worldMaxX").GetSingle(), a.GetProperty("worldMaxY").GetSingle());
        }
        int I(string key) => a.GetProperty(key).GetInt32();
        var initial = new MotionState(new(a.GetProperty("playerX").GetSingle(), a.GetProperty("playerY").GetSingle()),
            new(a.GetProperty("vx").GetSingle(), a.GetProperty("vy").GetSingle()),
            new(I("dashType"), I("dash"), I("dashDelay"), I("dashTime"),
                a.GetProperty("releaseLeft").GetBoolean(), a.GetProperty("releaseRight").GetBoolean()));
        MotionState predicted = calibrated.Step(initial, Move.FromId(I("action")));
        float error = Vector2.Distance(predicted.Position,
            new(b.GetProperty("playerX").GetSingle(), b.GetProperty("playerY").GetSingle()));
        residuals.Add(error);
        if (error > 1) Console.WriteLine($"REPLAY outlier tick={I("tick")} error={error:F4}px");
    }
    residuals.Sort();
    if (residuals.Count > 0)
        Console.WriteLine($"REPLAY pairs={residuals.Count} mean={residuals.Average():F4}px " +
            $"median={residuals[residuals.Count / 2]:F4}px p95={residuals[(int)(residuals.Count * .95)]:F4}px max={residuals[^1]:F4}px");
}

using System.Numerics;
using MutantPilot.Combat;

static class TossRoutingChecks
{
    public static void Run()
    {
        // Analytic orbit fixture, independently integrate the paired radial ray
        // crossing. This proves geometry of the route preference, not an engine win.
        float omega=MathF.PI/150*7/6;
        float previous=MathF.PI/omega;
        float next=(MathF.PI+2*MathF.Atan(TossRouting.OutwardRatio))/omega;
        if(previous>=156||next<=170)throw new Exception("outward orbit must postpone the second radial-ray crossing beyond native beam expiry");
        foreach(int side in new[]{-1,1})
        {
            Vector2 player=new(500,0),boss=Vector2.Zero;
            Vector2 direction=Vector2.Normalize(TossRouting.Goal(player,boss,side)-player);
            if(direction.X<=0||Math.Sign(direction.Y)!=side)
                throw new Exception("toss steering must follow the actual orbital sign while retaining outward travel");
            Vector2 p0=new(-1,TossRouting.OutwardRatio*side);
            for(int tick=24;tick<=156;tick++)
            {
                float angle=side*omega*tick;
                Vector2 p=new(p0.X*MathF.Cos(angle)-p0.Y*MathF.Sin(angle),p0.X*MathF.Sin(angle)+p0.Y*MathF.Cos(angle));
                if(Math.Abs(p.Y-p0.Y)<.025f)throw new Exception("paired birth ray must stay separated from the ideal offset orbit while armed");
            }
        }
        // Native 117 pre-trap pose. Over 72 frames the preferred orbit bends
        // substantially; a fixed goal at its initial bearing cannot express it.
        var motion=new MotionModel {FlightMastery=true,MaxX=18,WalkSpeed=8.67f,
            Acceleration=1.3125f,Braking=1,StopBraking=8,Gravity=.6f,MaxFall=10.01f};
        MotionState observed=new(new(78023.664f,13945.78f),new(17.687504f,-5.7749915f));
        TossWorld native=new(new(78164.51f,13480.392f),new(27.450018f,-4.7999983f),152,245,3,false);
        Vector2[] goals=new Vector2[73],translated=new Vector2[73];
        TossRouting.Track(goals,observed,motion,native,-.00304498f,1,12,5);
        Vector2 shift=new(-70000,-10000);
        TossRouting.Track(translated,observed with {Position=observed.Position+shift},motion,
            native with {Position=native.Position+shift},-.00304498f,1,12,5);
        if(Vector2.Distance(goals[0],goals[^1])<300)
            throw new Exception("72-frame toss preference must remain a moving trajectory, not a fixed brake target");
        for(int t=0;t<goals.Length;t++)
        {
            if(!float.IsFinite(goals[t].X)||!float.IsFinite(goals[t].Y)||Vector2.Distance(goals[t]+shift,translated[t])>1)
                throw new Exception("toss trajectory must be finite and independent of world origin");
            if(t>0&&Vector2.Distance(goals[t-1],goals[t])>60)
                throw new Exception("toss attractor must advance smoothly rather than jump between flanks");
        }
        Console.WriteLine("PASS native toss pre-trap pose produces a smooth moving 72-frame control preference");
        Console.WriteLine($"PASS toss orbit geometry: pure tangent recross {previous:F1} ticks; outward preference {next:F1}, native ray expires at 156 (development geometry only)");
    }
}

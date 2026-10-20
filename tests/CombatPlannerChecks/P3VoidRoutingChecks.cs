using System.Numerics;
using MutantPilot.Combat;

static class P3VoidRoutingChecks
{
    public static void Run()
    {
        foreach(float initial in new[]{-2.9f,-1.4622837f,0f,.7f,2.8f})
        foreach(int sign in new[]{-1,1})
        {
            float step=sign*MathF.PI/20,angle=initial;int emitted=0;
            var marks=new List<(int Born,Vector2 Direction,int Speed)>();
            for(int n=0;n<160;n++)
            {
                marks.Add((n*2,new(MathF.Cos(angle),MathF.Sin(angle)),n<=40?4:2));
                angle+=step;int old=emitted++;
                if(old==40||emitted==80||emitted==120)angle-=step/3;
            }
            for(int sector=0;sector<40;sector++)for(int time=0;time<=472;time++)
            {
                Vector2 point=P3VoidRouting.Point(Vector2.Zero,initial+sector*step,step,time);
                if(point.Length()>1000.01f || 1200-point.Length()<199.9f)
                    throw new Exception("P3 void stance must fit the actual 1200px arena with the goal's 200px reserve");
                foreach(var mark in marks)
                {
                    int age=time-mark.Born;
                    // Deliberately wider than native arm/fade timing: include
                    // both birth-slot orders and the entire final fading tail.
                    if(age<88||age>150)continue;
                    if(FlatBeam.Intersects(mark.Direction*(90*mark.Speed),mark.Direction,3000,22,point,new(13,24)))
                        throw new Exception($"P3 phase route touches native radial schedule: t={time} born={mark.Born}");
                }
            }
        }
        var route=new P3VoidRouting();
        route.Update(-2,true,new(0,218),Vector2.Zero,-MathF.PI/2,MathF.PI/20,0,0,100);
        if(!route.Active || Math.Abs(route.Goal().Length()-1000)>.01f)
            throw new Exception("P3 phase route must initialize from visible native counters");
        route.Update(-4,true,Vector2.Zero,Vector2.Zero,0,0,0,0,500);
        if(route.Active)throw new Exception("P3 phase route must release after its residual state");
        Console.WriteLine("PASS P3 phase route stays inside the actual arena and clears the complete 160-marker radial schedule, both spin directions and inflated player box (standing/phase geometry; arrival not certified)");
    }
}

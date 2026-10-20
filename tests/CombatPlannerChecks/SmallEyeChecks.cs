using System.Numerics;
using MutantPilot.Combat;

static class SmallEyeChecks
{
    public static void Run()
    {
        var eye = new SmallEye(10,new(100,200),Vector2.Zero,2,6,1,300);
        var player = new MotionState(new(300,400),new(8,-3));
        var shot = SmallEyeResponseForecast.Step(eye,player,1);
        if(shot.A is not EyeBolt bolt || bolt.Origin!=new Vector2(100,194)
            ||Vector2.Distance(bolt.Velocity,Vector2.Normalize(player.Position+player.Velocity*15-bolt.Origin)*8)>1e-5f)
            throw new Exception("small eye must fire at age 7 with this candidate's native velocity*15 aim");
        var brake=SmallEyeResponseForecast.Step(eye with {State=1,Age=40,Velocity=new(1.05f,0)},player,1);
        if(brake.State!=2||brake.Age!=0||brake.Velocity!=Vector2.Zero||brake.Position!=eye.Position||brake.A!=null)
            throw new Exception("small eye must enter shooting with zero velocity and reset the timer");
        var second=SmallEyeResponseForecast.Step(shot with {Age=13},player,8);
        if(second.B==null || second.A!=shot.A)throw new Exception("small eye must retain both independent native shots");
        var tree=new SmallEyeResponseForecast();tree.Reset(new[]{eye});
        float a=tree.Advance(0,player,1);
        if(a!=tree.Advance(0,player,1))throw new Exception("identical small-eye prefixes must share");
        float b=tree.Advance(0,player with {Velocity=-player.Velocity},1);
        if(a==b||tree.At(a,0).A?.Velocity==tree.At(b,0).A?.Velocity)
            throw new Exception("same position with different velocities must not merge future bolt aims");
        float c=tree.Advance(a,player,2),d=tree.Advance(b,player,2);
        if(c==d)throw new Exception("different shot histories must stay distinct after player paths meet");
        var nativeBolt=new EyeBolt(new(64006.336f,30524.656f),new(6.9656157f,-3.9344888f),3);
        if(nativeBolt.Touches(new(64011.156f,30507.13f),new(13),2)
            ||!nativeBolt.Touches(new(64011.156f,30507.13f),new(13),3))
            throw new Exception("fast bolt must cover native 0.3.76 first-subupdate Hurt but never before its birth");
        Console.WriteLine("PASS small-eye native shooting thresholds, candidate aim, shared history and sub-update sweep");
    }
}

using System.Numerics;

namespace MutantPilot.Combat
{
    public sealed class EocRouting
    {
        public Vector2? Center {get;private set;}
        public void Reset()=>Center=null;
        public void Observe(float eyeAge,Vector2 eyeLock)
        {
            // FTW accelerates the NPC's clock, not this projectile's clock. The
            // eye's own localAI is the centre of its chords and subsequent fans.
            if(eyeAge>=120&&float.IsFinite(eyeLock.X)&&float.IsFinite(eyeLock.Y))Center=eyeLock;
        }
    }
}

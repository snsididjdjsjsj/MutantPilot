namespace MutantPilot.Combat
{
    // Extra draws of the same world state are ignored. Missing world updates are an error,
    // never repaired by interpolation or duplicated frames.
    public sealed class LogicFrameSequence
    {
        public uint First { get; private set; }
        public uint Last { get; private set; }
        public int Frames { get; private set; }
        public uint Missing { get; private set; }
        public bool Accept(uint tick)
        {
            if(Frames>0)
            {
                uint delta=unchecked(tick-Last);
                if(delta==0)return false;
                if(delta!=1) { Missing+=delta-1; throw new System.InvalidOperationException($"Missing {delta-1} game frames before tick {tick}"); }
            }
            else First=tick;
            Last=tick; Frames++; return true;
        }
    }
}

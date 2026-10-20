using MutantPilot.Combat;
var frames=new LogicFrameSequence();
for(uint t=100;t<280;t++)
{
    if(!frames.Accept(t)||frames.Accept(t))throw new Exception("Unique ticks must appear once");
}
if(frames.Frames!=180||frames.First!=100||frames.Last!=279||frames.Missing!=0)throw new Exception("Incorrect frame span");
bool rejected=false;try{frames.Accept(282);}catch(InvalidOperationException){rejected=true;}
if(!rejected||frames.Missing!=2)throw new Exception("Missing logic frames must fail, never be interpolated");
var rollover=new LogicFrameSequence();rollover.Accept(uint.MaxValue);rollover.Accept(0);
if(rollover.Frames!=2||rollover.Missing!=0)throw new Exception("Unsigned tick rollover must stay contiguous");
Console.WriteLine("PASS unique game ticks, duplicate-draw suppression, missing-frame rejection and tick rollover");
if(args.Length<2)return;
string path=System.IO.Path.GetFullPath(args[1]);
var video=new LogicVideoWriter(System.IO.Path.GetFullPath(args[0]),path,321,181);
for(uint t=0;t<60;t++)
{
    byte[] pixels=video.AcquireBuffer();
    for(int p=0;p<pixels.Length;p+=4){pixels[p]=(byte)(t*4);pixels[p+1]=80;pixels[p+2]=200;pixels[p+3]=255;}
    video.Submit(pixels,1234+t);
}
video.Complete(new {reason="encoder_smoke",missingGameFrames=0});
var deadline=DateTime.UtcNow.AddSeconds(30);
while(!video.Completed && DateTime.UtcNow<deadline)Thread.Sleep(20);
if(!video.Completed||video.Error!=null||video.FramesWritten!=60)throw new Exception("Encoding failed: "+video.Error);
Console.WriteLine("PASS bounded RGBA writer encoded all 60 frames and finalized the video and tick manifest");

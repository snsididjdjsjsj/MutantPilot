#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Security.Cryptography;

namespace MutantPilot.Combat
{
    // Only copied RGBA pixels and tick numbers cross this boundary. A bounded queue applies
    // backpressure instead of dropping frames; neither Terraria objects nor game APIs enter the worker.
    public sealed class LogicVideoWriter
    {
        // Wall is the capture instant in Environment.TickCount64 milliseconds. The game runs slower than
        // real time when the search stalls, so the audio track (recorded in real time) can only be
        // aligned to the video timeline with this per-frame stamp.
        readonly record struct Frame(byte[] Pixels,uint Tick,long Wall);
        readonly BlockingCollection<byte[]> free = new(4);
        readonly BlockingCollection<Frame> pending = new(4);
        readonly CancellationTokenSource failed = new();
        readonly Process encoder;
        readonly StreamWriter errors;
        readonly Thread worker;
        readonly int width,height;
        readonly Stopwatch wall = Stopwatch.StartNew();
        string metadata="null";
        volatile bool completing;
        int written;
        public volatile bool Completed;
        public string? Error { get; private set; }
        public string Path { get; }
        public int FramesWritten => Volatile.Read(ref written);
        public LogicVideoWriter(string ffmpeg,string path,int width,int height)
        {
            this.width=width;this.height=height;Path=path;
            if(File.Exists(path))throw new IOException("Recording output already exists");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? throw new ArgumentException("An absolute video path is required"));
            errors=new StreamWriter(path+".encoder.log");
            var info=new ProcessStartInfo(ffmpeg) {UseShellExecute=false,CreateNoWindow=true,
                RedirectStandardInput=true,RedirectStandardError=true};
            foreach(string arg in new[]{"-hide_banner","-loglevel","warning","-n","-f","rawvideo","-pixel_format","rgba",
                "-video_size",$"{width}x{height}","-framerate","60","-i","pipe:0","-an",
                "-c:v","ffv1","-level","3","-coder","1","-context","1","-g","1",
                "-slices","16","-slicecrc","1","-threads","4","-pix_fmt","bgra",
                "-fps_mode","passthrough","-f","matroska",path})info.ArgumentList.Add(arg);
            encoder=new Process {StartInfo=info};
            encoder.ErrorDataReceived+=(_,e)=>{
                if(e.Data==null)return;
                try {lock(errors){errors.WriteLine(e.Data);errors.Flush();}}catch(ObjectDisposedException) { }
            };
            try {encoder.Start();encoder.BeginErrorReadLine();
                try {encoder.PriorityClass=ProcessPriorityClass.BelowNormal;}catch { }}
            catch {errors.Dispose();encoder.Dispose();throw;}
            for(int i=0;i<4;i++)free.Add(new byte[checked(width*height*4)]);
            worker=new Thread(WriteLoop) {IsBackground=true,Name="MutantPilotVideoEncoder"};worker.Start();
        }
        public byte[] AcquireBuffer()=>free.Take(failed.Token);
        public void Submit(byte[] pixels,uint tick)=>pending.Add(new(pixels,tick,Environment.TickCount64),failed.Token);
        public void Complete(object outcome)
        {
            if(completing)return;
            metadata=JsonSerializer.Serialize(outcome);completing=true;pending.CompleteAdding();
        }
        void WriteLoop()
        {
            uint first=0,last=0;
            try
            {
                using(var ticks=new StreamWriter(Path+".frames.jsonl"))
                    foreach(Frame frame in pending.GetConsumingEnumerable())
                    {
                        encoder.StandardInput.BaseStream.Write(frame.Pixels,0,frame.Pixels.Length);
                        int number=Interlocked.Increment(ref written);
                        if(number==1)first=frame.Tick;last=frame.Tick;
                        ticks.WriteLine(JsonSerializer.Serialize(new {frame=number-1,gameTick=frame.Tick,wall=frame.Wall,
                            rgbaSha256=Convert.ToHexString(SHA256.HashData(frame.Pixels)).ToLowerInvariant()}));
                        free.Add(frame.Pixels);
                    }
                encoder.StandardInput.Close();
                if(!encoder.WaitForExit(30000))throw new IOException("Encoder did not finalize in 30 seconds");
                encoder.WaitForExit();
                if(encoder.ExitCode!=0)throw new IOException("Encoder exited with code "+encoder.ExitCode);
            }
            catch(Exception ex) {Error=ex.Message;failed.Cancel();}
            finally
            {
                if(Error!=null && !completing)SpinWait.SpinUntil(()=>completing,2000);
                wall.Stop();
                try
                {
                    File.WriteAllText(Path+".capture.json",JsonSerializer.Serialize(new {
                        format="native rendered logic frames",fps=60,width,height,frames=FramesWritten,
                        firstGameTick=first,lastGameTick=last,wallSeconds=wall.Elapsed.TotalSeconds,
                        videoSeconds=FramesWritten/60.0,audio=false,encoder="ffv1",lossless=true,sourcePixelFormat="rgba",storedPixelFormat="bgra",
                        unscaled=true,perFrameRgbaSha256=true,
                        error=Error,outcome=JsonSerializer.Deserialize<JsonElement>(metadata)}));
                }
                catch(Exception ex) {Error??=ex.Message;}
                // A failed encoder must unblock the renderer's queue waits as well.
                if(Error!=null)failed.Cancel();
                lock(errors)errors.Dispose();encoder.Dispose();Completed=true;
            }
        }
    }
}

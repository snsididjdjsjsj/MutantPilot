using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.Enums;
using Terraria.ModLoader;
using MutantPilot.Combat;

namespace MutantPilot.Common
{
    public sealed class LogicFrameRecorder : ModSystem
    {
        static LogicVideoWriter writer;
        static LogicFrameSequence sequence;
        static Color[] pixels;
        static int width,height,previewLimit;
        static bool armed,active,pacing;
        static uint? finishAt;
        static FrameSkipMode previousSkip;
        static bool previousFixed;
        static object outcome;
        static string failure,version;
        static RenderTarget2D frameTarget;
        static RenderTargetBinding[] frameBinding;
        static SpriteBatch presenter;
        static bool redirect;
        static long memoryAtStart,peakMemory;
        Hook targetHook;
        delegate void SetTargets(GraphicsDevice device, RenderTargetBinding[] targets);
        public override void Load()
        {
            if(Main.dedServ)return;
            targetHook=new Hook(typeof(GraphicsDevice).GetMethod(nameof(GraphicsDevice.SetRenderTargets),new[]{typeof(RenderTargetBinding[])}),
                (Action<SetTargets,GraphicsDevice,RenderTargetBinding[]>)RedirectTargets);
            Main.OnPreDraw+=BeforeDraw;Main.OnPostDraw+=AfterDraw;
        }
        static void RedirectTargets(SetTargets original,GraphicsDevice device,RenderTargetBinding[] targets)
        {
            // A persistent render target owns/reuses its staging texture. Repeated backbuffer
            // GetData calls in the installed D3D11 path retain roughly one image allocation each.
            original(device,redirect && (targets==null || targets.Length==0)?frameBinding:targets);
        }
        static void BeforeDraw(GameTime time)
        {
            if(!pacing || frameTarget==null)return;
            redirect=true;Main.instance.GraphicsDevice.SetRenderTarget(frameTarget);
        }
        public override void Unload()
        {
            if(!Main.dedServ){Main.OnPreDraw-=BeforeDraw;Main.OnPostDraw-=AfterDraw;}
            Stop("mod_unload");
            targetHook?.Dispose();targetHook=null;
        }
        public override void OnWorldUnload()=>Stop("world_exit");
        public static void Arm(string ffmpeg,string directory,int previewFrames=0)
        {
            if(PilotSense.FindBoss()!=null)throw new InvalidOperationException("Arm recording between fights");
            if(writer!=null && !writer.Completed)throw new InvalidOperationException("Previous recording is still encoding");
            if(!System.IO.Path.IsPathFullyQualified(ffmpeg) || !File.Exists(ffmpeg))throw new IOException("An absolute installed ffmpeg path is required");
            directory=System.IO.Path.GetFullPath(directory);Directory.CreateDirectory(directory);
            if(new DriveInfo(System.IO.Path.GetPathRoot(directory)).AvailableFreeSpace<1024L*1024*1024)
                throw new IOException("Less than 1 GiB free for the recording");
            var presentation=Main.instance.GraphicsDevice.PresentationParameters;
            width=presentation.BackBufferWidth;height=presentation.BackBufferHeight;
            frameTarget=new RenderTarget2D(Main.instance.GraphicsDevice,width,height,false,presentation.BackBufferFormat,
                presentation.DepthStencilFormat,0,RenderTargetUsage.PreserveContents);
            frameBinding=new[]{new RenderTargetBinding(frameTarget)};
            presenter=new SpriteBatch(Main.instance.GraphicsDevice);
            string path=System.IO.Path.Combine(directory,$"{(previewFrames>0?"preview":"battle")}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.mp4");
            writer=new LogicVideoWriter(ffmpeg,path,width,height);
            version=ModContent.GetInstance<LogicFrameRecorder>().Mod.Version.ToString();
            pixels=new Color[checked(width*height)];sequence=new();
            using(var process=Process.GetCurrentProcess())memoryAtStart=peakMemory=process.PrivateMemorySize64;
            previewLimit=Math.Clamp(previewFrames,0,36000);finishAt=null;outcome=null;failure=null;
            previousSkip=Main.FrameSkipMode;previousFixed=Main.instance.IsFixedTimeStep;
            pacing=true;Main.FrameSkipMode=FrameSkipMode.Off;Main.instance.IsFixedTimeStep=false;
            armed=previewLimit==0;active=previewLimit>0;
        }
        public static void BeginBattle()
        {
            if(!armed)return;
            armed=false;active=true;sequence=new();finishAt=null;
        }
        public static void EndBattle(object result)
        {
            if(!active || previewLimit>0)return;
            outcome=result;
            finishAt=unchecked(Main.GameUpdateCount+120); // Two seconds of real game frames after the result.
        }
        static void AfterDraw(GameTime time)
        {
            if(!pacing)return;
            if(redirect)
            {
                redirect=false;
                Main.instance.GraphicsDevice.SetRenderTarget(null);
                presenter.Begin(SpriteSortMode.Immediate,BlendState.Opaque,SamplerState.PointClamp,
                    DepthStencilState.None,RasterizerState.CullNone);
                presenter.Draw(frameTarget,new Rectangle(0,0,width,height),Color.White);presenter.End();
            }
            // FrameSkip Off is the native foreground mode. Keep FNA's outer loop in its one-update
            // branch after drawing, including while unfocused. Main still supplies its own original
            // 1/60 world update; no GameTime, AI timers, RNG or entity state is changed here.
            Main.FrameSkipMode=FrameSkipMode.Off;
            Main.instance.IsFixedTimeStep=false;
            if(!active)return;
            try
            {
                if(Main.gameMenu) {Stop("world_exit");return;}
                var current=Main.instance.GraphicsDevice.PresentationParameters;
                if(current.BackBufferWidth!=width || current.BackBufferHeight!=height)
                    throw new InvalidOperationException("Resolution changed while recording");
                if(!sequence.Accept(Main.GameUpdateCount))return;
                if(sequence.Frames%300==0)
                {
                    using var process=Process.GetCurrentProcess();long bytes=process.PrivateMemorySize64;
                    peakMemory=Math.Max(peakMemory,bytes);
                    if(bytes>6L*1024*1024*1024 || bytes>4L*1024*1024*1024 && bytes-memoryAtStart>1536L*1024*1024)
                        throw new InvalidOperationException("Recording stopped by memory growth guard");
                }
                byte[] buffer=writer.AcquireBuffer();
                frameTarget.GetData(pixels);
                MemoryMarshal.AsBytes(pixels.AsSpan()).CopyTo(buffer);
                writer.Submit(buffer,Main.GameUpdateCount);
                if(previewLimit>0 && sequence.Frames>=previewLimit || finishAt.HasValue && Main.GameUpdateCount>=finishAt.Value)
                    Stop(previewLimit>0?"preview_complete":"battle_complete");
            }
            catch(Exception ex) {failure=writer?.Error??ex.Message;Stop("capture_error");}
        }
        public static void Stop(string reason)
        {
            if(!pacing)return;
            armed=active=pacing=redirect=false;
            Main.FrameSkipMode=previousSkip;Main.instance.IsFixedTimeStep=previousFixed;
            writer?.Complete(new {reason,error=failure,frames=sequence?.Frames??0,
                firstGameTick=sequence?.First,lastGameTick=sequence?.Last,missingGameFrames=sequence?.Missing??0,
                memoryAtStartBytes=memoryAtStart,peakMemoryBytes=peakMemory,
                source="uninterpolated native game render",timebase="one unique game world tick per video frame, 60 fps",
                controllerVersion=version,battle=outcome});
            frameTarget?.Dispose();frameTarget=null;frameBinding=null;
            presenter?.Dispose();presenter=null;
            pixels=null;
        }
        public static object Snapshot()=>new {armed,active,pacing,output=writer?.Path,
            frames=sequence?.Frames??0,written=writer?.FramesWritten??0,completed=writer?.Completed??false,
            missingGameFrames=sequence?.Missing??0,error=failure??writer?.Error,audio=false,
            memoryAtStartBytes=memoryAtStart,peakMemoryBytes=peakMemory};
    }
}

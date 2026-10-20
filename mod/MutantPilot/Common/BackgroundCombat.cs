using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace MutantPilot.Common
{
    // Opt-in validation setting: keep the native single-player update running
    // when the window loses focus. It never changes GameTime or gameplay state.
    public sealed class BackgroundCombat : ModSystem
    {
        ILHook hook;
        public static bool Requested { get; private set; }
        public static bool Installed { get; private set; }
        public static long BackgroundTicks { get; private set; }
        static string Marker => Path.Combine(Main.SavePath, "MutantPilot", "background-pilot.on");
        public override void Load()
        {
            if (Main.dedServ) return;
            Requested = File.Exists(Marker);
            try
            {
                MethodInfo update = typeof(Main).GetMethod("DoUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException("Main.DoUpdate");
                hook = new ILHook(update, il => {
                    var cursor = new ILCursor(il);
                    if (!cursor.TryGotoNext(MoveType.Before, instruction => instruction.MatchStsfld<Main>("hasFocus")))
                        throw new InvalidOperationException("Native focus assignment was not found");
                    // Transform only the focus check's boolean; the rest of the
                    // native update, collision, damage and timing run normally.
                    cursor.EmitDelegate<Func<bool, bool>>(KeepUpdating);
                    // Preserve the desktop cursor while the game is actually
                    // unfocused, despite keeping its world update active.
                    while (cursor.TryGotoNext(MoveType.Before, instruction =>
                        instruction.MatchCallvirt<Game>("set_IsMouseVisible") || instruction.MatchCall<Game>("set_IsMouseVisible")))
                    {
                        cursor.EmitDelegate<Func<bool, bool>>(PreserveCursor);
                        cursor.Index++;
                    }
                });
                Installed = true;
            }
            catch (Exception ex)
            {
                hook?.Dispose(); hook = null; Installed = false;
                Mod.Logger.Warn("Background validation unavailable; native pause retained: " + ex.Message);
            }
        }
        static bool KeepUpdating(bool focused)
        {
            if (focused || !Requested || Main.gameMenu || Main.netMode != NetmodeID.SinglePlayer) return focused;
            BackgroundTicks++;
            return true;
        }
        static bool PreserveCursor(bool visible) => visible || Requested && !Main.gameMenu
            && Main.netMode == NetmodeID.SinglePlayer && !Main.instance.IsActive;
        public static void Configure(bool enabled)
        {
            if (enabled && !Installed) throw new InvalidOperationException("Background update hook is unavailable");
            Directory.CreateDirectory(Path.GetDirectoryName(Marker));
            if (enabled) File.WriteAllText(Marker, "Native-speed background validation enabled. Disable with live.py background --enabled 0.");
            else if (File.Exists(Marker)) File.Delete(Marker);
            Requested = enabled;
        }
        public static object Snapshot() => new { requested = Requested, installed = Installed,
            backgroundTicks = BackgroundTicks, nativeWindowActive = Main.instance?.IsActive ?? false };
        public override void Unload()
        {
            hook?.Dispose(); hook = null;
            Requested = false; Installed = false; BackgroundTicks = 0;
        }
    }
}

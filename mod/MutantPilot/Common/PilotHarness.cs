using System;
using System.IO;
using System.Text.Json;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace MutantPilot.Common
{
    // Local validation control. Commands never edit health, motion, damage, immunity or boss AI.
    // HERO-style fixture creation starts the installed NPC with its ordinary stats and AI.
    public sealed class PilotHarness : ModSystem
    {
        string directory, lastId, commandError;
        bool quitting;
        public static int? DevelopmentSeed { get; private set; }
        public override void OnWorldLoad()
        {
            directory = Path.Combine(Main.SavePath, "MutantPilot");
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "command.json");
                if (File.Exists(path))
                {
                    using var previous = JsonDocument.Parse(File.ReadAllText(path));
                    lastId = previous.RootElement.GetProperty("id").GetString();
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
            { Mod.Logger.Warn("Pilot harness initialization: " + ex.Message); }
        }
        public override void PostUpdateEverything()
        {
            if (Main.dedServ || Main.gameMenu || Main.netMode != NetmodeID.SinglePlayer) return;
            Player player = Main.LocalPlayer;
            // FargoSoulsPlayer.ResetEffects clears BetsyDashing at the start of every tick and the
            // dash projectile's AI sets it afterwards, so the player-update hook always reads a stale
            // false. End-of-frame is the only place that sees the real value, and it has to run every
            // frame: the dash lasts 15 ticks and the planner needs the remaining-immunity count per
            // tick, not per 15. Command polling keeps its own throttle below.
            if (player != null && player.active && !player.dead) FargoCapabilities.Capture(player);
            if (directory == null || Main.GameUpdateCount % 15 != 0) return;
            try
            {
                string path = Path.Combine(directory, "command.json");
                if (File.Exists(path))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(path));
                    var command = document.RootElement;
                    string id = command.GetProperty("id").GetString();
                    if (id != lastId)
                    {
                        lastId = id; commandError = null;
                        if (command.GetProperty("op").GetString() == "tune")
                        {
                            if (PilotSense.FindBoss() != null) commandError = "Tune between fights for reproducible trials";
                            else
                            {
                                var proposed = JsonSerializer.Deserialize<PilotTuning>(command.GetProperty("settings").GetRawText()) ?? new();
                                if (proposed.AsyncDecision) commandError = "AsyncDecision is unavailable: native collision state must stay on the game thread";
                                else if (proposed.TriggerGoldShell) commandError = "Golden Stasis is forbidden in this controller build";
                                else PilotTuning.Current = proposed;
                            }
                        }
                        else Execute(command.GetProperty("op").GetString(), player, command);
                    }
                }
                NPC boss = PilotSense.FindBoss();
                var status = new { utc = DateTime.UtcNow, version = Mod.Version.ToString(), commandId = lastId, commandError, seed = DevelopmentSeed,
                    world = new { name = Main.worldName, width = Main.maxTilesX, height = Main.maxTilesY, surface = Main.worldSurface },
                    masochist = PilotWorld.IsMasochist, assistance = PilotWorld.DescribeAssistance(player), settings = PilotTuning.Current,
                    player = player.GetModPlayer<PilotPlayer>().Snapshot(), background = BackgroundCombat.Snapshot(),
                    performance = CombatFrameTiming.LastWindow,
                    recording = LogicFrameRecorder.Snapshot(),
                    collisionBroadPhase = BeamBroadPhaseCheck.Snapshot(),
                    equipmentObservation = FargoCapabilities.Snapshot(player),
                    boss = boss == null ? null : new { id = boss.whoAmI, life = boss.life, lifeMax = boss.lifeMax,
                        state = boss.ai[0], x = boss.Center.X, y = boss.Center.Y, invulnerable = boss.dontTakeDamage } };
                string temp = Path.Combine(directory, "status.json.tmp");
                File.WriteAllText(temp, JsonSerializer.Serialize(status));
                File.Move(temp, Path.Combine(directory, "status.json"), true);
                if (quitting)
                {
                    quitting = false;
                    SystemLoader.PreSaveAndQuit();
                    WorldGen.SaveAndQuit();
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
            { commandError = ex.Message; }
        }
        void Execute(string operation, Player player, JsonElement command)
        {
            switch (operation)
            {
                case "record":
                    try
                    {
                        string mode=command.GetProperty("mode").GetString();
                        if(mode=="stop")LogicFrameRecorder.Stop("manual_stop");
                        else if(PilotSense.FindBoss()!=null)commandError="Arm recording between fights";
                        else if(mode=="arm" || mode=="preview")
                            LogicFrameRecorder.Arm(command.GetProperty("ffmpeg").GetString(),command.GetProperty("directory").GetString(),
                            mode=="preview"?(command.TryGetProperty("frames",out var count)?Math.Clamp(count.GetInt32(),1,36000):180):0);
                        else commandError="Unknown recording mode";
                    }
                    catch(Exception ex) {commandError="Recording: "+ex.Message;}
                    break;
                case "inspect":
                    if (PilotSense.FindBoss() != null) { commandError = "Inspect loadout between fights"; break; }
                    File.WriteAllText(Path.Combine(directory, "loadout.json"), JsonSerializer.Serialize(new {
                        utc = DateTime.UtcNow, version = Mod.Version.ToString(),
                        inventory = player.inventory.Select((item, slot) => new { slot, item.type, item.Name,
                            modType = item.ModItem?.FullName, item.stack, item.damage, item.useTime, item.useAnimation,
                            item.shoot, item.shootSpeed, item.pick, item.axe, item.hammer, item.mana, item.useAmmo,
                            effectiveDamage = item.damage > 0 ? player.GetWeaponDamage(item) : 0 }).Where(i => i.type > 0).ToArray(),
                        armor = player.armor.Select((item, slot) => new { slot, item.type, item.Name,
                            modType = item.ModItem?.FullName }).Where(i => i.type > 0).ToArray() }));
                    break;
                case "summon":
                    if (!PilotWorld.IsMasochist) { commandError = "Masochist mode is not active"; break; }
                    if (player.dead) { commandError = "Player is dead"; break; }
                    if (FargoDash.MutantBossType < 0) { commandError = "Fargo Mutant not loaded"; break; }
                    if (PilotSense.FindBoss() != null) { commandError = "Mutant is already alive"; break; }
                    int index = NPC.NewNPC(player.GetSource_Misc("MutantPilotValidation"),
                        (int)player.Center.X + 500, (int)player.Center.Y - 200,
                        FargoDash.MutantBossType, Target: player.whoAmI);
                    if (index < 0 || index >= Main.maxNPCs || !Main.npc[index].active)
                        commandError = "The engine could not create Mutant";
                    break;
                case "prepare": commandError = player.GetModPlayer<PilotPlayer>().PrepareArena(); break;
                case "background":
                    if (PilotSense.FindBoss() != null) { commandError = "Configure background updates between fights"; break; }
                    BackgroundCombat.Configure(command.GetProperty("enabled").GetBoolean());
                    break;
                case "anchor":
                    // summon places the boss at player.Center + (500, -200), so a fight is only
                    // reproducible when the player stands on the same world x every time. Pin x and
                    // clear the velocity; the hover keeps y on prepareY by itself.
                    if (PilotSense.FindBoss() != null) { commandError = "Anchor before summon"; break; }
                    if (player.dead) { commandError = "Player is dead"; break; }
                    if (!command.TryGetProperty("x", out var anchorField)) { commandError = "anchor requires x"; break; }
                    float anchorX = anchorField.GetSingle();
                    // Default y is the same line PrepareArena hovers on: the highest point above
                    // the surface, i.e. the furthest the arena can sit from the world ceiling.
                    float anchorY = Math.Max(480f, (float)Main.worldSurface * 16 - 1000);
                    if (command.TryGetProperty("y", out var anchorYField)) anchorY = anchorYField.GetSingle();
                    player.Center = new Microsoft.Xna.Framework.Vector2(anchorX, anchorY);
                    player.velocity = Microsoft.Xna.Framework.Vector2.Zero;
                    break;
                case "checkbeams":
                    if (PilotSense.FindBoss() != null) { commandError = "Check beam geometry between fights"; break; }
                    var check = BeamGeometryCheck.Run();
                    File.WriteAllText(Path.Combine(directory, "beam_geometry_check.json"), JsonSerializer.Serialize(new {
                        utc = DateTime.UtcNow, version = Mod.Version.ToString(), check }));
                    if (check.FalsePositives + check.FalseNegatives > 0)
                        commandError = "Offline beam geometry differs from the installed engine; inspect beam_geometry_check.json";
                    break;
                case "clean":
                    PilotWorld.DisableAssistance(player);
                    // Leftovers from a previous encounter consume Main.rand in a different order, which
                    // shifts the seeded sequence from the first tick: two runs with the same seed diverged
                    // before tick 6386 because of this. Clearing them makes a seeded run repeatable.
                    for (int i = 0; i < Main.maxProjectiles; i++)
                        if (Main.projectile[i].active) Main.projectile[i].active = false;
                    for (int i = 0; i < Main.maxItems; i++)
                        if (Main.item[i].active) Main.item[i].active = false;
                    for (int i = 0; i < Main.maxNPCs; i++)
                    {
                        NPC leftover = Main.npc[i];
                        if (leftover.active && !leftover.townNPC && !leftover.friendly && leftover.type != NPCID.TargetDummy)
                            leftover.active = false;
                    }
                    break;
                case "seed":
                    // Development aid for repeatable fights: reseed the game RNG between fights
                    // so two versions can be compared on the same attack sequence. It does not
                    // alter the player, the boss, or the AI; the accepted run must stay unseeded.
                    if (PilotSense.FindBoss() != null) { commandError = "Seed before summon"; break; }
                    if (!command.TryGetProperty("seed", out var seedField)) { commandError = "seed requires an integer"; break; }
                    try
                    {
                        int seed = seedField.GetInt32();
                        Terraria.Utilities.UnifiedRandom seeded = new(seed);
                        Main.rand = seeded;
                        DevelopmentSeed = seed;
                    }
                    catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
                    { commandError = "Seed rejected: " + ex.Message; }
                    break;
                case "savequit": quitting = true; break;
                case "stop": player.GetModPlayer<PilotPlayer>().SetPilot(false); break;
                case "pilot": player.GetModPlayer<PilotPlayer>().SetPilot(true); break;
                case "status": break;
                default: commandError = "Unknown operation"; break;
            }
        }
        public override void OnWorldUnload()
        {
            if (!Main.dedServ && Main.LocalPlayer?.active == true)
                Main.LocalPlayer.GetModPlayer<PilotPlayer>().SetPilot(false);
            directory = null;
        }
    }
}

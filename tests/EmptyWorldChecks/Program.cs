using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--for-the-worthy"))
    throw new ArgumentException("World path, tModLoader directory, and optional --for-the-worthy required");
bool forTheWorthy = args.Length == 3;
string path = Path.GetFullPath(args[0]);
string game = Path.GetFullPath(args[1]);

void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}

using var input = new BinaryReader(File.OpenRead(path));
Require(input.ReadInt32() == 279, "World version");
Require(System.Text.Encoding.ASCII.GetString(input.ReadBytes(8)) == "relogic\x02", "World signature");
input.BaseStream.Position = 24;
int count = input.ReadInt16();
Require(count == 11, "Section count");
int[] offsets = Enumerable.Range(0, count).Select(_ => input.ReadInt32()).ToArray();
Require(offsets.SequenceEqual(offsets.OrderBy(x => x)), "Section order");
input.BaseStream.Position = offsets[0];
string name = input.ReadString();
input.ReadString();
input.ReadUInt64();
Guid id = new(input.ReadBytes(16));
int worldId = input.ReadInt32();
int left = input.ReadInt32(), right = input.ReadInt32(), top = input.ReadInt32(), bottom = input.ReadInt32();
int height = input.ReadInt32(), width = input.ReadInt32(), mode = input.ReadInt32();
Require(width == 8400 && height == 2400 && mode == 2, "Large Master world dimensions");
Require(left == 0 && top == 0 && right == width * 16 && bottom == height * 16, "World bounds");
byte[] specialSeeds = input.ReadBytes(8);
Require(specialSeeds.SequenceEqual(new byte[] { 0, forTheWorthy ? (byte)1 : (byte)0, 0, 0, 0, 0, 0, 0 }),
    "Unexpected special seed");
DateTime created = DateTime.FromBinary(input.ReadInt64());
input.ReadByte();
input.BaseStream.Position += 17 * 4;
int spawnX = input.ReadInt32(), spawnY = input.ReadInt32();
double surface = input.ReadDouble(), rock = input.ReadDouble();
Require(spawnX == 4200 && spawnY == 1100 && surface == 2200 && rock == 2250, "Spawn and layer layout");
input.BaseStream.Position = offsets[1];
long emptyCells = 0;
for (int x = 0; x < width; x++)
{
    int y = 0;
    while (y < height)
    {
        byte flags = input.ReadByte();
        Require((flags & 63) == 0, "Found tile, wall, liquid or additional tile flags");
        int repeats = (flags >> 6) switch { 0 => 0, 1 => input.ReadByte(), _ => input.ReadInt16() };
        Require(repeats >= 0 && y + repeats < height, "Invalid vertical RLE");
        y += repeats + 1;
        emptyCells += repeats + 1;
    }
}
Require(input.BaseStream.Position == offsets[2], "Tile section end");
Require(input.ReadInt16() == 0 && input.ReadInt16() == 40, "Chest section");
Require(input.BaseStream.Position == offsets[3] && input.ReadInt16() == 0, "Sign section");
Require(input.BaseStream.Position == offsets[4], "NPC section offset");
Require(input.ReadInt32() == 0 && !input.ReadBoolean() && !input.ReadBoolean(), "NPC section must be empty");
for (int i = 5; i <= 7; i++)
{
    Require(input.BaseStream.Position == offsets[i] && input.ReadInt32() == 0, "World object section");
}
Require(input.BaseStream.Position == offsets[8], "Bestiary section offset");
input.BaseStream.Position = offsets[10];
Require(input.ReadBoolean() && input.ReadString() == name && input.ReadInt32() == worldId, "Footer identity");
Require(input.BaseStream.Position == input.BaseStream.Length, "Unexpected trailing data");
input.Dispose(); // WorldFile.GetFileMetadata opens the file with exclusive sharing.

// Load only the installed engine's file/NBT serializers. Do not initialize Main,
// allocate a live world, launch a server, load mods or touch the running client.
AssemblyLoadContext.Default.Resolving += (_, requested) =>
{
    string? found = Directory.EnumerateFiles(Path.Combine(game, "Libraries"), requested.Name + ".dll", SearchOption.AllDirectories)
        .FirstOrDefault(p => !p.Contains(Path.DirectorySeparatorChar + "Native" + Path.DirectorySeparatorChar)
                            && !p.Contains(Path.DirectorySeparatorChar + "ref" + Path.DirectorySeparatorChar));
    return found == null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(found);
};
Assembly engine = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "tModLoader.dll"));
Type worldFile = engine.GetType("Terraria.IO.WorldFile", true)!;
object? metadata = worldFile.GetMethod("GetFileMetadata")!.Invoke(null, new object[] { path, false });
Require(metadata != null, "Installed engine rejected file metadata");
Type tagIO = engine.GetType("Terraria.ModLoader.IO.TagIO", true)!;
MethodInfo read = tagIO.GetMethods().Single(m => m.Name == "FromStream" && m.GetParameters().Length == 2);
using var sidecar = File.OpenRead(Path.ChangeExtension(path, ".twld"));
object root = read.Invoke(null, new object[] { sidecar, true })!;
object Get(object compound, string key) => compound.GetType().GetProperty("Item")!.GetValue(compound, new object[] { key })!;
object Compound(object compound, string key) => compound.GetType().GetMethod("GetCompound")!.Invoke(compound, new object[] { key })!;
var tiles = (IEnumerable)Compound(root, "tiles");
Require(!tiles.Cast<object>().Any(), "Mod tile/wall data must be empty");
bool maso = false;
foreach (object row in (IEnumerable)Get(root, "modData"))
{
    if ((string)Get(row, "mod") != "FargowiltasSouls" || (string)Get(row, "name") != "WorldSavingSystem") continue;
    string[] flags = ((IEnumerable)Get(Compound(row, "data"), "downed")).Cast<string>().ToArray();
    maso = new[] { "eternity", "CanPlayMaso", "getReal" }.All(flags.Contains);
}
Require(maso, "Masochist world data missing");
object icon = Compound(Compound(Compound(root, "0header"), "modHeaders"), "FargowiltasSouls/EternityWorldIconManager");
Require(Convert.ToInt32(Get(icon, "EternityWorld")) == 1 && Convert.ToInt32(Get(icon, "MasochistWorld")) == 1,
    "World-selection difficulty flags");
Console.WriteLine(JsonSerializer.Serialize(new { name, width, height, mode, surface, rock, spawnX, spawnY,
    emptyCells, worldId, id, created, forTheWorthy, legendary = mode == 2 && forTheWorthy,
    nativeFileMetadataAccepted = true, nativeTagIOAccepted = true,
    masochistFlagsPreserved = maso, note = "Offline file validation; no game world was opened." }));

using Mono.Cecil;
using Mono.Cecil.Cil;

string path = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("TMODLOADER_DIR") is string dir && dir.Length > 0
    ? Path.Combine(dir, "tModLoader.dll")
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "tModLoader", "tModLoader.dll");
using var module = ModuleDefinition.ReadModule(path);
var main = module.Types.Single(t => t.FullName == "Terraria.Main");
var update = main.Methods.Single(m => m.Name == "DoUpdate" && !m.IsStatic && m.Parameters.Count == 1);
var matches = update.Body.Instructions.Where(i => i.OpCode.Code == Code.Stsfld
    && i.Operand is FieldReference f && f.Name == "hasFocus" && f.DeclaringType.FullName == "Terraria.Main").ToArray();
if (matches.Length != 1 || matches[0].Previous.Operand is not MethodReference getter
    || getter.Name != "get_IsActive" || getter.ReturnType.MetadataType != MetadataType.Boolean)
    throw new InvalidDataException("Installed native update does not have the expected unique boolean focus assignment.");
Console.WriteLine("PASS installed Main.DoUpdate contains one native IsActive -> hasFocus assignment; hook leaves timing and gameplay instructions intact.");
Console.WriteLine("Runtime load and unfocused tick-rate checks are still required; this inspection does not certify a battle.");

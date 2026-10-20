using Terraria.ModLoader;

namespace MutantPilot.Common
{
	public class PilotSystem : ModSystem
	{
		public ModKeybind TogglePilot;
		public bool Enabled;

		public override void Load()
		{
			TogglePilot = KeybindLoader.RegisterKeybind(Mod, "TogglePilot", "F6");
		}

		public override void PostSetupContent()
		{
			FargoDash.Init();
			PilotSense.Init();
		}

		public override void Unload()
		{
			FargoDash.Unload();
			TogglePilot = null;
		}
	}
}

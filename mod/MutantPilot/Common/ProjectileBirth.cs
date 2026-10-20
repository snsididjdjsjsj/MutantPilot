using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace MutantPilot.Common
{
    // Single-player Projectile.identity can equal its reused pool slot. Keep a
    // private observation stamp; never change the game's identity or actor state.
    public sealed class ProjectileBirth : GlobalProjectile
    {
        static long nextGeneration;
        public long Generation { get; private set; }
        public override bool InstancePerEntity => true;
        public override void OnSpawn(Projectile projectile, IEntitySource source) => Generation = ++nextGeneration;
        public static long Of(Projectile projectile) => projectile.GetGlobalProjectile<ProjectileBirth>().Generation;
    }
}

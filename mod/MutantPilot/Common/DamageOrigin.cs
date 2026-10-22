using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace MutantPilot.Common
{
    // Attribution metadata only; never changes a projectile or damage calculation.
    public sealed class DamageOrigin : GlobalProjectile
    {
        public override bool InstancePerEntity=>true;
        string rootItem,parentName,context;
        bool accessory;
        public override void SetDefaults(Projectile projectile){rootItem=parentName=context=null;accessory=false;}
        public override void OnSpawn(Projectile projectile,IEntitySource source)
        {
            context=source?.Context;
            if(source is IEntitySource_WithStatsFromItem itemSource)
            {rootItem=itemSource.Item.ModItem?.FullName??$"Terraria/{itemSource.Item.type}";accessory=itemSource.Item.accessory;}
            else if(source is EntitySource_Parent parent&&parent.Entity is Projectile p)
            {
                parentName=p.ModProjectile?.FullName??$"Terraria/{p.type}";
                var origin=p.GetGlobalProjectile<DamageOrigin>();rootItem=origin.rootItem;accessory=origin.accessory;
            }
        }
        public static object Snapshot(Projectile projectile)
        {
            var origin=projectile.GetGlobalProjectile<DamageOrigin>();
            return new {kind="projectile",name=projectile.ModProjectile?.FullName??$"Terraria/{projectile.type}",projectile.type,
                projectile.identity,generation=ProjectileBirth.Of(projectile),projectile.owner,projectile.minion,projectile.sentry,
                damageClass=projectile.DamageType.Name,origin.rootItem,origin.accessory,origin.parentName,origin.context};
        }
    }
}

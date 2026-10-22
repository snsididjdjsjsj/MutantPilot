using System.Linq;
using Terraria;

namespace MutantPilot.Common
{
    // Pre-fight item inputs only: the engine spends mana, applies buffs and creates minions.
    public static class TrialSummons
    {
        static bool active,ready=true;
        static int phase,elapsed;
        public static void Begin(Player player)
        {
            active=PilotTuning.Current.PrimaryWeapon=="FargowiltasSouls/TheDestroyer";
            ready=!active;phase=elapsed=0;
        }
        public static void Step(Player player)
        {
            player.controlUseItem=false;
            if(!active)return;
            elapsed++;
            if(player.dead||PilotSense.FindBoss()!=null||elapsed>2100){active=false;return;}
            if(player.itemAnimation>0||player.itemTime>0)return;
            while(phase<3)
            {
                Item staff=player.inventory[4+phase];
                if(staff.IsAir||staff.mana<=0){active=false;return;}
                bool done=phase<2?player.ownedProjectileCounts[staff.shoot]>0:player.slotsMinions+1>player.maxMinions;
                if(done){phase++;continue;}
                player.selectedItem=4+phase;
                PilotControls.AimAt(player.Center+new Microsoft.Xna.Framework.Vector2(140,-60));
                player.releaseUseItem=true;
                player.controlUseItem=true;
                return;
            }
            active=false;ready=true;PilotControls.SelectWeapon(player);
        }
        public static object Snapshot(Player player)=>new {active,ready,phase,elapsed,player.maxMinions,player.slotsMinions,
            minions=Main.projectile.Where(p=>p.active&&p.owner==player.whoAmI&&(p.minion||p.sentry))
                .GroupBy(p=>p.ModProjectile?.FullName??$"Terraria/{p.type}")
                .Select(g=>new {name=g.Key,count=g.Count(),slots=g.Sum(p=>p.minionSlots)}).ToArray()};
    }
}

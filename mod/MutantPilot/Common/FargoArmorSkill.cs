using System;
using Terraria;
using Terraria.ModLoader;

namespace MutantPilot.Common
{
    public static class FargoArmorSkill
    {
        static int rayType=-1,lastPress=-1000;
        static long lastAttempt=-1;
        public static int Presses { get; private set; }
        public static int Spawned { get; private set; }
        public static bool TryUse(Player player,NPC boss,bool routeSafe)
        {
            if(!PilotTuning.Current.AllowAccessorySkills||!PilotTuning.Current.TriggerArmorSkill||!routeSafe
                ||boss.dontTakeDamage||player.Distance(boss.Center)>900||!FargoCapabilities.StyxSet||FargoCapabilities.StyxReady<=0
                ||player.CCed||player.noItems||player.cursed||player.dead||FargoCapabilities.TimeFrozenTicks>0)return false;
            if(rayType<0)rayType=ModContent.Find<ModProjectile>("FargowiltasSouls/StyxGazerArmor").Type;
            if(player.ownedProjectileCounts[rayType]>0||(int)Main.GameUpdateCount-lastPress<90||lastAttempt==(long)Main.GameUpdateCount)return false;
            lastAttempt=(long)Main.GameUpdateCount;
            // Invoke the installed double-tap armor-set input, including Fargo's normal key hook.
            // The native Styx handler checks the actual set and consumes its existing scythes.
            player.KeyDoubleTap(Main.ReversedUpDownArmorSetBonuses?1:0);
            Presses++;lastPress=(int)Main.GameUpdateCount;
            foreach(Projectile p in Main.ActiveProjectiles)
                if(p.type==rayType&&p.owner==player.whoAmI){Spawned++;return true;}
            return false;
        }
        public static object Snapshot()=>new {skill="Styx armor double-tap",presses=Presses,spawned=Spawned,lastPress};
    }
}

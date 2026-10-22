using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace MutantPilot.Common
{
    // Operator-authorized equipment setup, between encounters only. Items retain their installed
    // defaults and legal prefixes; ammunition is finite and consumed by the ordinary item logic.
    public static class TrialLoadout
    {
        public static bool DashesAllowed => false;
        public static bool IsAllowedWeapon(string name) => name is "FargowiltasSouls/DragonBreath2" or "FargowiltasSouls/NukeFishron" or "FargowiltasSouls/TheDestroyer";
        public static object Applied { get; private set; }
        public static object ItemSnapshot(Item item) => new { item.type, item.Name, item.prefix,
            fullName=item.ModItem?.FullName??$"Terraria/{item.type}", item.stack, item.damage, item.useTime, item.useAmmo };

        static void Preserve(Player player,int slot,bool armor)
        {
            Item old=armor?player.armor[slot]:player.inventory[slot];
            if(old.IsAir)return;
            int free=Enumerable.Range(10,40).FirstOrDefault(i=>player.inventory[i].IsAir,-1);
            if(free>=0){player.inventory[free]=old;return;}
            string directory=Path.Combine(Main.SavePath,"MutantPilot","equipment_backups");
            Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,$"{DateTime.UtcNow:yyyyMMdd_HHmmss_fffffff}_{(armor?"armor":"inventory")}_{slot}_{old.type}.tag");
            TagIO.ToFile(new TagCompound{{"item",ItemIO.Save(old)},{"originalSlot",slot},{"armor",armor}},path);
        }
        static Item Create(int type,int prefix)
        {
            Item item=new();item.SetDefaults(type);
            if(!item.Prefix(prefix)||item.prefix!=prefix)throw new InvalidOperationException("Native item refused the requested prefix");
            return item;
        }
        public static void Apply(Player player,string weapon)
        {
            if(PilotSense.FindBoss()!=null||player.dead)throw new InvalidOperationException("Configure equipment between fights while alive");
            if(!IsAllowedWeapon(weapon))throw new InvalidOperationException("Weapon is outside the audited pre-Mutant, no-displacement list");
            if(!player.IsItemSlotUnlockedAndUsable(9))throw new InvalidOperationException("Native Master accessory slot is unavailable");
            bool summon=weapon=="FargowiltasSouls/TheDestroyer";
            ModItem weaponType=ModContent.Find<ModItem>(weapon),accessoryType=ModContent.Find<ModItem>(summon?"FargowiltasSouls/ConjuristsSoul":"FargowiltasSouls/SnipersSoul");
            Item primary=Create(weaponType.Type,summon?PrefixID.Legendary:PrefixID.Unreal),accessory=Create(accessoryType.Type,PrefixID.Menacing);
            int ammoType=weapon.EndsWith("DragonBreath2",StringComparison.Ordinal)?ItemID.Gel:ItemID.RocketI;
            object before=new {weapon=ItemSnapshot(player.inventory[3]),accessory=ItemSnapshot(player.armor[9]),ammo=ItemSnapshot(player.inventory[54])};
            // These are the same Fargo effect toggles exposed in its equipment menu. Disable the
            // native double-tap dash as well as the active dash, rather than rewriting dash physics.
            var disabled=DisableDashToggles(player);
            if(summon)ConfigureSummonToggles(player);
            if(player.inventory[3].type!=primary.type||player.inventory[3].prefix!=primary.prefix){Preserve(player,3,false);player.inventory[3]=primary;}
            if(player.armor[9].type!=accessory.type||player.armor[9].prefix!=accessory.prefix){Preserve(player,9,true);player.armor[9]=accessory;}
            if(!summon)
            {
                if(player.inventory[54].type!=ammoType){Preserve(player,54,false);player.inventory[54]=new Item(ammoType);}
                player.inventory[54].stack=Math.Min(player.inventory[54].maxStack,9999);
            }
            if(summon)
            {
                EquipStaff(player,4,ModContent.Find<ModItem>("FargowiltasSouls/OpticStaffEX").Type);
                EquipStaff(player,5,ItemID.StardustDragonStaff);
                EquipStaff(player,6,ItemID.EmpressBlade);
            }
            player.selectedItem=3;
            PilotTuning.Current.PrimaryWeapon=weapon;
            Applied=new {utc=DateTime.UtcNow,weapon=ItemSnapshot(player.inventory[3]),accessory=ItemSnapshot(player.armor[9]),
                ammo=ItemSnapshot(player.inventory[54]),player.extraAccessory,masterSlotUsable=player.IsItemSlotUnlockedAndUsable(9),
                disabledDashToggles=disabled,summonSetup=summon,staves=summon?new[]{ItemSnapshot(player.inventory[4]),ItemSnapshot(player.inventory[5]),ItemSnapshot(player.inventory[6])}:null,
                before,policy="Pre-Mutant equipment; native stats/prefixes/ammo; no Golden Stasis or dashes"};
            string directory=Path.Combine(Main.SavePath,"MutantPilot");
            File.WriteAllText(Path.Combine(directory,$"loadout_change_{DateTime.Now:yyyyMMdd_HHmmss_fff}.json"),JsonSerializer.Serialize(Applied));
        }
        static void EquipStaff(Player player,int slot,int type)
        {
            Item staff=Create(type,PrefixID.Ruthless);
            if(player.inventory[slot].type==type&&player.inventory[slot].prefix==staff.prefix)return;
            Preserve(player,slot,false);player.inventory[slot]=staff;
        }
        static void ConfigureSummonToggles(Player player)
        {
            SetEffectToggle(player,"MinionsDeactivatedEffect",false);
            SetEffectToggle(player,"AbomWandCrit",true);
            SetEffectToggle(player,"UfoMinionEffect",true);
        }
        public static void SetEffectToggle(Player player,string name,bool enabled)
        {
            Mod fargo=ModLoader.GetMod("FargowiltasSouls");
            Type loader=fargo.Code.GetType("FargowiltasSouls.Core.AccessoryEffectSystem.AccessoryEffectLoader",true);
            var registry=(IEnumerable)loader.GetField("AccessoryEffects",BindingFlags.Public|BindingFlags.Static).GetValue(null);
            object effect=registry.Cast<object>().Single(e=>e.GetType().Name==name);
            MethodInfo setter=fargo.Code.GetTypes().SelectMany(t=>t.GetMethods(BindingFlags.Public|BindingFlags.Static))
                .First(m=>m.Name=="SetToggleValue"&&!m.IsGenericMethod&&m.GetParameters().Length==3&&m.GetParameters()[0].ParameterType==typeof(Player));
            setter.Invoke(null,new object[]{player,effect,enabled});
        }
        static string[] DisableDashToggles(Player player)
        {
            Mod fargo=ModLoader.GetMod("FargowiltasSouls");
            Type loader=fargo.Code.GetType("FargowiltasSouls.Core.AccessoryEffectSystem.AccessoryEffectLoader",true);
            var registry=(IEnumerable)loader.GetField("AccessoryEffects",BindingFlags.Public|BindingFlags.Static).GetValue(null);
            MethodInfo setter=fargo.Code.GetTypes().SelectMany(t=>t.GetMethods(BindingFlags.Public|BindingFlags.Static))
                .First(m=>m.Name=="SetToggleValue"&&!m.IsGenericMethod&&m.GetParameters().Length==3
                    &&m.GetParameters()[0].ParameterType==typeof(Player));
            var names=new HashSet<string>{"SupersonicTabi","CthulhuShield","BetsyDashEffect","SpecialDashEffect","MonkDashEffect","ShinobiDashEffect"};
            var changed=new List<string>();
            foreach(object effect in registry)
                if(names.Contains(effect.GetType().Name)){setter.Invoke(null,new object[]{player,effect,false});changed.Add(effect.GetType().Name);}
            if(!changed.Contains("SupersonicTabi")||!changed.Contains("CthulhuShield"))
                throw new InvalidOperationException("Native dash toggles were not found");
            return changed.ToArray();
        }
    }
}

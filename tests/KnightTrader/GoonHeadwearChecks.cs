using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using pitTeam.GoingRogue.Server;
using pitTeam.Server.Services;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Helpers.Bot;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

internal static class GoonHeadwearChecks
{
    private static Func<GenerateEquipmentProperties,bool>? generation;
    private static bool Prefix(GenerateEquipmentProperties settings, ref bool __result)
    { __result=generation!(settings);return false; }

    internal static void Run(string runtime, JsonUtil json, GlobalTable globals, Action<bool,string> check)
    {
        var roles=new[]{"bossknight","followerbigpipe","followerbirdeye"};
        var bots=new BotTable {Base=new(),Core=null!,Types=roles.ToDictionary(role=>role,role=>json.Deserialize<BotType>(File.ReadAllText(Path.Combine(runtime,"SPT_Data/database/bots/types",role+".json"))))};
        var config=json.Deserialize<BotConfig>(File.ReadAllText(Path.Combine(runtime,"SPT_Data/configs/bot.json")))!;
        var itemHelper=(ItemHelper)RuntimeHelpers.GetUninitializedObject(typeof(ItemHelper));
        var botHelper=(BotHelper)RuntimeHelpers.GetUninitializedObject(typeof(BotHelper));
        var generator=(BotInventoryGenerator)RuntimeHelpers.GetUninitializedObject(typeof(BotInventoryGenerator));
        var policy=new BossHeadwearPolicy(bots,config,generator,botHelper,itemHelper);
        var method=typeof(BotInventoryGenerator).GetMethod(nameof(BotInventoryGenerator.GenerateEquipment),[typeof(GenerateEquipmentProperties)])!;
        var harmony=new Harmony("fixture.goon-headwear");
        harmony.Patch(method,prefix:new HarmonyMethod(typeof(GoonHeadwearChecks),nameof(Prefix)));
        BotBase Profile(string role)
        {
            var root=new MongoId();var pumpkin=new MongoId();
            return new() {Id=new MongoId(),Info=new(){Level=60,Settings=new(){Role=role}},Inventory=new(){Equipment=root,Items=[
                new(){Id=root,Template=ItemTpl.INVENTORY_DEFAULT},
                new(){Id=pumpkin,Template=ItemTpl.HEADWEAR_JACKOLANTERN_TACTICAL_PUMPKIN_HELMET,ParentId=root.ToString(),SlotId="Headwear"},
                new(){Id=new MongoId(),Template=ItemTpl.INVENTORY_DEFAULT,ParentId=pumpkin.ToString(),SlotId="test-child"},
                new(){Id=new MongoId(),Template=ItemTpl.INVENTORY_DEFAULT,ParentId=root.ToString(),SlotId="TacticalVest"},
                new(){Id=new MongoId(),Template=ItemTpl.INVENTORY_DEFAULT,ParentId=root.ToString(),SlotId="FirstPrimaryWeapon"},
                new(){Id=new MongoId(),Template=ItemTpl.INVENTORY_DEFAULT,ParentId=root.ToString(),SlotId="test-ammo"},
                new(){Id=new MongoId(),Template=ItemTpl.INVENTORY_DEFAULT,ParentId=root.ToString(),SlotId="test-medical"},
                new(){Id=new MongoId(),Template=ItemTpl.HEADWEAR_JACKOLANTERN_TACTICAL_PUMPKIN_HELMET,ParentId=root.ToString(),SlotId="test-cargo"}
            ]}};
        }
        var requests=new List<GenerateEquipmentProperties>();
        bool Generate(GenerateEquipmentProperties settings)
        {
            requests.Add(settings);
            var chance=settings.SpawnChances!.EquipmentChances[settings.RootEquipmentSlot.ToString()];
            if(chance<=0 || settings.RootEquipmentPool!.Count==0)return false;
            settings.Inventory!.Items!.Add(new(){Id=new MongoId(),Template=settings.RootEquipmentPool.First().Key,
                ParentId=settings.Inventory.Equipment!.Value.ToString(),SlotId=settings.RootEquipmentSlot.ToString()});
            return true;
        }
        generation=Generate;
        try
        {
            foreach(var role in roles)
            {
                var profile=Profile(role);var before=json.Serialize(bots.Types[role]);
                var inventory=profile.Inventory!;var keep=inventory.Items!.Where(item=>item.SlotId is "TacticalVest" or "test-cargo" or "FirstPrimaryWeapon" or "test-ammo" or "test-medical").Select(item=>json.Serialize(item)).ToArray();
                var pumpkin=inventory.Items!.Single(item=>item.SlotId=="Headwear").Id;
                requests.Clear();policy.Ensure(profile);
                check(!ReferenceEquals(inventory,profile.Inventory) && inventory.Items!.Any(item=>item.Id==pumpkin),"head correction commits an isolated inventory only: "+role);
                check(!profile.Inventory!.Items!.Any(item=>item.SlotId=="Headwear" && item.Template==ItemTpl.HEADWEAR_JACKOLANTERN_TACTICAL_PUMPKIN_HELMET),"equipped pumpkin removed: "+role);
                check(!profile.Inventory.Items!.Any(item=>item.Id==pumpkin || item.ParentId==pumpkin.ToString()),"native item traversal removes pumpkin child tree: "+role);
                check(keep.All(item=>profile.Inventory.Items!.Any(after=>json.Serialize(after)==item)),"weapons, armor, supplies and carried pumpkin loot preserved: "+role);
                check(requests.Select(r=>r.RootEquipmentSlot).SequenceEqual(new[]{EquipmentSlots.FaceCover,EquipmentSlots.Headwear,EquipmentSlots.Earpiece,EquipmentSlots.Eyewear}),"native head-slot order and scope: "+role);
                check(requests.All(r=>!r.RootEquipmentPool!.ContainsKey(ItemTpl.HEADWEAR_JACKOLANTERN_TACTICAL_PUMPKIN_HELMET) && r.BotData!.Role==role),"native role and filtered local pools reach real generator boundary: "+role);
                check(json.Serialize(bots.Types[role])==before,"shared native pools/chances unchanged: "+role);
                var restored=profile.Inventory;requests.Clear();policy.Ensure(profile);
                check(ReferenceEquals(restored,profile.Inventory) && requests.Count==0,"normal kit is not rerolled on repeated preparation: "+role);
                if(role=="bossknight")check(profile.Inventory.Items!.Any(item=>item.SlotId=="FaceCover" && item.Template==new MongoId("62963c18dbc8ab5f0d382d0b")) && !profile.Inventory.Items!.Any(item=>item.SlotId=="Headwear"),"Knight's native skull mask and no-helmet choice restored");
            }
            foreach(var role in new[]{"pmcUSEC","assault","exUsec","bossSanitar"})
            {var profile=Profile(role);var inv=profile.Inventory;requests.Clear();policy.Ensure(profile);check(ReferenceEquals(inv,profile.Inventory) && requests.Count==0,"other bots keep Halloween outfit: "+role);}
            var missing=Profile("bossKnight");missing.Inventory=null;policy.Ensure(missing);check(missing.Inventory==null,"missing inventory untouched");
            var normal=Profile("bossKnight");normal.Inventory!.Items!.Single(item=>item.SlotId=="Headwear").Template=new MongoId("5a16bb52fcdbcb001a3b00dc");
            var unchanged=normal.Inventory;requests.Clear();policy.Ensure(normal);check(ReferenceEquals(unchanged,normal.Inventory) && requests.Count==0,"carried pumpkin cannot trigger correction without a worn pumpkin");
            var retained=Profile("followerBigPipe");var mask=new Item{Id=new MongoId(),Template=new MongoId("62a61bbf8ec41a51b34758d2"),ParentId=retained.Inventory!.Equipment!.Value.ToString(),SlotId="FaceCover"};retained.Inventory.Items!.Add(mask);
            requests.Clear();policy.Ensure(retained);check(!requests.Any(r=>r.RootEquipmentSlot==EquipmentSlots.FaceCover) && retained.Inventory.Items!.Any(item=>item.Id==mask.Id),"existing non-pumpkin head equipment is retained");
            var failed=Profile("bossKnight");var original=failed.Inventory;var snapshot=json.Serialize(original);var poolSnapshot=json.Serialize(bots.Types["bossknight"]);
            generation=settings=>{settings.RootEquipmentPool!.Clear();throw new InvalidOperationException("fixture native equipment failure");};
            try{policy.Ensure(failed);throw new Exception("expected generation failure");}catch(InvalidOperationException){check(ReferenceEquals(original,failed.Inventory) && json.Serialize(failed.Inventory)==snapshot && json.Serialize(bots.Types["bossknight"])==poolSnapshot,"generation failure preserves original inventory and shared pools");}
            generation=Generate;
            new GoingRogueServerPlugin(globals,bots,itemHelper,(InventoryHelper)RuntimeHelpers.GetUninitializedObject(typeof(InventoryHelper)),config,generator,botHelper).OnLoadAsync(default).GetAwaiter().GetResult();
            foreach(var role in roles)
            {
                var profile=Profile(role);profile.Inventory!.Items!.RemoveAll(item=>item.SlotId=="FirstPrimaryWeapon");requests.Clear();
                FriendlyGameplayModeAddons.PolicyForRole("GoingRogue",role)!.PrepareRaidEquipment!(profile);
                check(requests.Count==4 && !BossHeadwearPolicy.NeedsRestore(profile),"registered raid-equipment callback corrects headgear before magazine provisioning: "+role);
            }
            check(FriendlyGameplayModeAddons.PolicyForRole("GoingRogue","exUsec")!.PrepareRaidEquipment==null,"ordinary exUsec policy receives no headwear exception");
        }
        finally{harmony.UnpatchSelf();generation=null;}
    }
}

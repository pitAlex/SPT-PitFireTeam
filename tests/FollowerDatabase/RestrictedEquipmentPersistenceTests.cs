using System.Reflection;
using System.Text.Json;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Services.Items;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;

internal static class RestrictedEquipmentPersistenceTests
{
    internal static void Run(FriendlyTeammateStorage storage, FriendlyServerSettingsService settings,
        JsonUtil json, MongoId session, Action<bool, string> check)
    {
        var template = new MongoId();
        // Native ItemHelper uses the native base-class cache. This fixture has no magazines;
        // magazine loss/placement is exercised separately by RestrictedMagazineRecovery.
        var baseClasses = Construct<ItemBaseClassService>(_ => null);
        typeof(ItemBaseClassService).GetField("_itemBaseClassesCache", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(baseClasses, new Dictionary<MongoId, HashSet<MongoId>> { [template] = [] });
        var itemHelper = Construct<ItemHelper>(name => name == "itemBaseClassService" ? baseClasses : null);
        var cloner = new FastCloner();
        var service = Construct<FriendlyTeammateService>(name => name switch
        {
            "storage" => storage, "settingsService" => settings, "jsonUtil" => json,
            "itemHelper" => itemHelper, "cloner" => cloner,
            "logger" => DispatchProxy.Create<ISptLogger<FriendlyTeammateService>, TestLogger>(), _ => null
        });
        Item New(string? parent = null, string? slot = null) => new()
        {
            Id = new MongoId(), Template = template, ParentId = parent, SlotId = slot,
            Upd = new Upd { StackObjectsCount = 1 }
        };

        foreach (string legacyValue in new[] { "false", "true", "null" })
        {
            settings.SaveAndApply(JsonSerializer.Deserialize<FriendlyServerSettingsRequest>(
                "{\"loadoutManagementMode\":\"Restricted\",\"restrictedGearMaintenance\":" + legacyValue + "}")!);
            check(!JsonSerializer.Serialize(settings.LoadSettings()).Contains("restrictedGearMaintenance"),
                "obsolete upkeep field is ignored and omitted on save: " + legacyValue);
            foreach (bool escaped in new[] { true, false })
            {
                Item root = New(), armor = New(root.Id.ToString(), "ArmorVest"), pockets = New(root.Id.ToString(), "Pockets");
                armor.Upd!.Repairable = new UpdRepairable { Durability = 60, MaxDurability = 60 };
                Item meds = New(pockets.Id.ToString(), "pocket1"), ammo = New(pockets.Id.ToString(), "pocket2");
                meds.Upd!.MedKit = new UpdMedKit { HpResource = 100 };
                ammo.Upd!.StackObjectsCount = 30;
                var teammate = new BotBase { Aid = 880, Inventory = new BotBaseInventory
                {
                    Equipment = root.Id, Items = [root, armor, pockets, meds, ammo]
                } };
                var live = cloner.Clone(teammate.Inventory.Items)!;
                live.Single(item => item.Id == armor.Id).Upd!.Repairable!.Durability = 25;
                live.Single(item => item.Id == meds.Id).Upd!.MedKit!.HpResource = 35;
                live.Single(item => item.Id == ammo.Id).Upd!.StackObjectsCount = 4;
                Item acquired = New(root.Id.ToString(), "FirstPrimaryWeapon"), attachment = New(acquired.Id.ToString(), "mod_scope");
                Item cargo = New(pockets.Id.ToString(), "pocket3"), cargoChild = New(cargo.Id.ToString(), "mod_test");
                Item otherGear = New(root.Id.ToString(), "Headwear"), otherChild = New(otherGear.Id.ToString(), "mod_test");
                Item secure = New(root.Id.ToString(), "SecuredContainer"), secureMed = New(secure.Id.ToString(), "main");
                live.AddRange([acquired, attachment, cargo, cargoChild, otherGear, otherChild, secure, secureMed]);
                var entry = new FriendlyTeammateDeathEscapeEntry
                {
                    Aid = "880", Escaped = escaped, EquipmentItems = live, TrackedItemIds = [cargo.Id.ToString()]
                };
                var protectedIds = new Dictionary<string, HashSet<string>> { ["881"] = [otherGear.Id.ToString()] };
                Invoke(service, "ApplyImmersiveEscapedDefaultEquipmentState", session, teammate, entry, protectedIds);
                Invoke(service, "ApplyRestrictedGearMaintenanceDeathEquipmentState", session, teammate, entry, protectedIds);
                Invoke(service, "ApplyImmersiveDefaultGearLoss", session, teammate, entry);
                var saved = storage.Read<List<Item>>(session, "880-equipment.json")!;
                string scenario = $"Restricted escaped={escaped}, legacy upkeep={legacyValue}";
                check(saved.Single(item => item.Id == armor.Id).Upd!.Repairable!.Durability == 25, scenario + " persists durability");
                check(saved.Single(item => item.Id == meds.Id).Upd!.MedKit!.HpResource == 35, scenario + " persists spent medicine");
                check(saved.Single(item => item.Id == ammo.Id).Upd!.StackObjectsCount == 4, scenario + " persists spent ammunition");
                check(saved.Any(item => item.Id == acquired.Id) && saved.Any(item => item.Id == attachment.Id), scenario + " retains acquired equipment");
                check(!saved.Any(item => new[] { cargo.Id, cargoChild.Id, otherGear.Id, otherChild.Id, secure.Id, secureMed.Id }.Contains(item.Id)),
                    scenario + " filters tracked cargo, other teammate gear and managed secure supplies");
                check(teammate.Inventory.Items.Any(item => item.Id == armor.Id), scenario + " never applies Immersive death loss");
                check(armor.Upd.Repairable.Durability == 60 && live.Single(item => item.Id == armor.Id).Upd!.Repairable!.Durability == 25,
                    scenario + " leaves baseline and input snapshots detached");
            }
        }
        settings.SaveAndApply(new());
    }

    private static T Construct<T>(Func<string?, object?> argument)
    {
        var constructor = typeof(T).GetConstructors().Single();
        return (T)constructor.Invoke(constructor.GetParameters().Select(parameter => argument(parameter.Name)).ToArray());
    }

    private static void Invoke(object service, string method, params object[] arguments) =>
        typeof(FriendlyTeammateService).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, arguments);
}

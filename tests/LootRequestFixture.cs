using System;
using EFT.InventoryLogic;
using pitTeam.Modules;

internal static class LootRequestFixture
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }

    public static int Main()
    {
        Item[] items = { new FoodDrink(), new Meds(), new Item(), new Weapon(), new Mod(),
            new Ammo(), new Magazine(), new ThrowWeap(), new Armor(), new Vest(), new Headwear(),
            new ArmorPlate(), new ArmoredEquipment() };
        int[] categories = { 0, 1, 2, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4 };
        for (int settings = 0; settings < 32; settings++)
        {
            pitTeam.pitFireTeam.Set(settings);
            foreach (FollowerLootMode mode in Enum.GetValues(typeof(FollowerLootMode)))
            {
                var request = new FollowerLootRequest(mode);
                bool weapon = mode == FollowerLootMode.GetWeapon || mode == FollowerLootMode.LootAndGetWeapon;
                bool gear = mode == FollowerLootMode.GetGear || mode == FollowerLootMode.LootAndGetGear;
                bool priority = mode == FollowerLootMode.LootAndGetWeapon || mode == FollowerLootMode.LootAndGetGear;
                bool only = mode == FollowerLootMode.GetWeapon || mode == FollowerLootMode.GetGear;
                var otherFollower = new FollowerLootRequest();
                for (int phase = 0; phase < 2; phase++)
                {
                    bool restricted = only || (priority && phase == 0);
                    Check(request.CategoryOnly == restricted, "category phase");
                    Check(request.AllowsWeaponWork == (!restricted || weapon), "weapon maintenance gate");
                    Check(request.AllowsGearWork == (!restricted || gear), "gear planner gate");
                    for (int i = 0; i < items.Length; i++)
                    {
                        bool selected = (weapon && categories[i] == 3) || (gear && categories[i] == 4);
                        bool configured = (settings & (1 << categories[i])) != 0;
                        bool expected = restricted ? selected : selected || configured;
                        Check(FollowerLootCategoryService.PassesCategoryFilter(items[i], request) == expected,
                            $"mode={mode} settings={settings} phase={phase} item={items[i].GetType().Name}");
                        Check(FollowerLootCategoryService.PassesCategoryFilter(items[i], otherFollower) == configured,
                            "override leaked to another request");
                    }
                    Check(!FollowerLootCategoryService.PassesCategoryFilter(null, request), "null item accepted");
                    Check(request.CompletePriority() == (priority && phase == 0), "priority transition");
                    Check(pitTeam.pitFireTeam.Mask() == settings, "saved settings mutated");
                }
                var nextRequest = new FollowerLootRequest();
                Check(nextRequest.Mode == FollowerLootMode.Normal && !nextRequest.CategoryOnly,
                    "new normal request inherited override");
            }
        }
        Console.WriteLine($"Loot request fixture passed: {checks} assertions.");
        return 0;
    }
}

namespace pitTeam
{
    internal sealed class Toggle { public bool Value; }
    internal static class pitFireTeam
    {
        public static Toggle lootFilterFood = new Toggle(), lootFilterMeds = new Toggle(),
            lootFilterValuables = new Toggle(), lootFilterWeapons = new Toggle(), lootFilterGear = new Toggle();
        public static void Set(int mask)
        {
            lootFilterFood.Value = (mask & 1) != 0; lootFilterMeds.Value = (mask & 2) != 0;
            lootFilterValuables.Value = (mask & 4) != 0; lootFilterWeapons.Value = (mask & 8) != 0;
            lootFilterGear.Value = (mask & 16) != 0;
        }
        public static int Mask() => (lootFilterFood.Value ? 1 : 0) | (lootFilterMeds.Value ? 2 : 0) |
            (lootFilterValuables.Value ? 4 : 0) | (lootFilterWeapons.Value ? 8 : 0) | (lootFilterGear.Value ? 16 : 0);
    }
}

namespace EFT.InventoryLogic
{
    public class Item { }
    public class FoodDrink : Item { }
    public class Meds : Item { }
    public interface IWeapon { }
    public class Weapon : Item, IWeapon { }
    public class Mod : Item { }
    public class Ammo : Item { }
    public class Magazine : Item { }
    public class ThrowWeap : Item { }
    public class Armor : Item { }
    public class Vest : Item { }
    public class Headwear : Item { }
    public class ArmorPlate : Item { }
    public class ArmoredEquipment : Item { }
}

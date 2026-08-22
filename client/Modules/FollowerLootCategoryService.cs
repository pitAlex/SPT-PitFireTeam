using EFT.InventoryLogic;

namespace pitTeam.Modules
{
    internal static class FollowerLootCategoryService
    {
        public static bool PassesCategoryFilter(Item item)
        {
            if (item == null)
            {
                return false;
            }

            switch (Classify(item))
            {
                case FollowerLootCategory.Food:
                    return pitFireTeam.lootFilterFood?.Value ?? true;
                case FollowerLootCategory.Meds:
                    return pitFireTeam.lootFilterMeds?.Value ?? true;
                case FollowerLootCategory.Weapons:
                    return pitFireTeam.lootFilterWeapons?.Value ?? true;
                case FollowerLootCategory.Gear:
                    return pitFireTeam.lootFilterGear?.Value ?? true;
                default:
                    return pitFireTeam.lootFilterValuables?.Value ?? true;
            }
        }

        private static FollowerLootCategory Classify(Item item)
        {
            if (item is EFT.InventoryLogic.FoodDrink)
            {
                return FollowerLootCategory.Food;
            }

            if (item is EFT.InventoryLogic.Meds)
            {
                return FollowerLootCategory.Meds;
            }

            if (IsWearableGear(item))
            {
                return FollowerLootCategory.Gear;
            }

            if (IsWeaponLoot(item))
            {
                return FollowerLootCategory.Weapons;
            }

            return FollowerLootCategory.Valuables;
        }

        public static bool IsWholeWearableTree(Item item)
        {
            return item is EFT.InventoryLogic.Armor ||
                   item is EFT.InventoryLogic.Vest ||
                   item is EFT.InventoryLogic.Headwear;
        }

        private static bool IsWearableGear(Item item)
        {
            return IsWholeWearableTree(item) ||
                   item is EFT.InventoryLogic.ArmorPlate ||
                   item is EFT.InventoryLogic.ArmoredEquipment;
        }

        private static bool IsWeaponLoot(Item item)
        {
            return item is Weapon ||
                   item is EFT.InventoryLogic.IWeapon ||
                   item is EFT.InventoryLogic.Mod ||
                   item is EFT.InventoryLogic.Ammo ||
                   item is EFT.InventoryLogic.Magazine ||
                   item is EFT.InventoryLogic.ThrowWeap;
        }

        private enum FollowerLootCategory
        {
            Food,
            Meds,
            Valuables,
            Weapons,
            Gear
        }
    }
}

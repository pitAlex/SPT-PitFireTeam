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
            if (item is FoodDrinkItemClass)
            {
                return FollowerLootCategory.Food;
            }

            if (item is MedsItemClass)
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
            return item is ArmorItemClass ||
                   item is VestItemClass ||
                   item is HeadwearItemClass;
        }

        private static bool IsWearableGear(Item item)
        {
            return IsWholeWearableTree(item) ||
                   item is ArmorPlateItemClass ||
                   item is ArmoredEquipmentItemClass;
        }

        private static bool IsWeaponLoot(Item item)
        {
            return item is Weapon ||
                   item is IWeapon ||
                   item is EFT.InventoryLogic.Mod ||
                   item is AmmoItemClass ||
                   item is MagazineItemClass ||
                   item is ThrowWeapItemClass;
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

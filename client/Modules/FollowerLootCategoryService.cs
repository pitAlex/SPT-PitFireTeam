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

            if (IsGear(item))
            {
                return FollowerLootCategory.Gear;
            }

            return FollowerLootCategory.Valuables;
        }

        private static bool IsGear(Item item)
        {
            return item is Weapon ||
                   item is IWeapon ||
                   item is EFT.InventoryLogic.Mod ||
                   item is AmmoItemClass ||
                   item is MagazineItemClass ||
                   item is ThrowWeapItemClass ||
                   item is ArmoredEquipmentItemClass ||
                   item is EquipmentItemClass;
        }

        private enum FollowerLootCategory
        {
            Food,
            Meds,
            Valuables,
            Gear
        }
    }
}

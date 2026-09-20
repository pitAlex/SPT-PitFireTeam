using EFT.InventoryLogic;
using System;
using System.Linq;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private static bool PreservesEquippedMagazineReloadSpace(
            InventoryEquipment equipment,
            Item item,
            ItemAddress address)
        {
            if (address is not GridItemAddress destination || equipment == null || item == null)
            {
                return true;
            }

            var roots = new[]
            {
                equipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem as SearchableItem,
                equipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem as SearchableItem
            };
            int destinationRoot = Array.FindIndex(roots, root =>
                root != null && ReferenceEquals(root, destination.Grid.ParentItem));
            // Nested cargo does not occupy any additional cells in the outer fast-access grids.
            if (destinationRoot < 0)
            {
                return true;
            }

            try
            {
                var reserves = new[]
                {
                    EquipmentSlot.FirstPrimaryWeapon,
                    EquipmentSlot.SecondPrimaryWeapon,
                    EquipmentSlot.Holster
                }
                    .Select(slot => equipment.GetSlot(slot)?.ContainedItem as Weapon)
                    .OfType<Weapon>()
                    .Where(weapon => weapon.ReloadMode == Weapon.EReloadMode.ExternalMagazine)
                    .Select(GetCurrentMagazineSafely)
                    .Where(magazine => magazine != null && magazine.Id != item.Id && HasReloadSpaceInRoots(roots, magazine))
                    .ToList();
                if (reserves.Count == 0)
                {
                    // Do not block looting merely because an existing kit already cannot stow its magazine.
                    return true;
                }

                // Normal CloneItem generates new IDs. Keep IDs only in these isolated copies so a
                // relocation can find and remove the copied source item before testing its new cells.
                var trialRoots = roots.Select(root => root?.CloneItemWithSameId()).ToArray();
                if (roots.Where((root, index) => root != null && trialRoots[index] == null).Any())
                {
                    return false;
                }

                // A relocation frees its old cells; never remove or add anything in the live inventory.
                foreach (var root in trialRoots.Where(root => root?.Grids != null))
                {
                    root.CurrentAddress = null;
                    foreach (var grid in root.Grids)
                    {
                        var existing = grid.Items.FirstOrDefault(candidate => candidate.Id == item.Id);
                        if (existing != null && grid.Remove(existing, false).Failed)
                        {
                            return false;
                        }
                    }
                }

                var trialGrid = trialRoots[destinationRoot]?.Grids?
                    .FirstOrDefault(grid => grid.ID == destination.Grid.ID);
                var trialItem = ClonePlanningItem(item);
                if (trialGrid == null || trialItem == null ||
                    trialGrid.Add(trialItem, destination.LocationInGrid, false).Failed)
                {
                    return false;
                }

                // Check each shape independently against the same free layout. Reloads are sequential:
                // one opening can serve several weapons, but area alone cannot compare 1x4 and 2x2.
                return reserves.All(magazine => HasReloadSpaceInRoots(trialRoots, magazine));
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[LootCommand] Reload landing-space validation failed: {ex.Message}");
                return false;
            }
        }

        private static bool HasReloadSpaceInRoots(SearchableItem[] roots, Item magazine)
        {
            return roots.Any(root => root?.Grids != null && root.Grids.Any(grid =>
                grid != null && grid.TryFindLocationForItem(magazine, out _)));
        }
    }
}

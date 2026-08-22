using EFT.InventoryLogic;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace pitTeam.Patches
{
    internal sealed class LoadoutEditorUnloadAmmoPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(EFT.InventoryLogic.InventoryEquipmentExtension).GetMethod(nameof(EFT.InventoryLogic.InventoryEquipmentExtension.GetPrioritizedGridsForUnloadedObject));
        }

        [PatchPostfix]
        private static void PatchPostfix(InventoryEquipment equipment, ref IEnumerable<EFT.InventoryLogic.Grid> __result)
        {
            if (OtherPlayerProfileScreenPatch.LoadoutEditorOverlayRoot == null
                || equipment == null
                || !ReferenceEquals(equipment, OtherPlayerProfileScreenPatch.LoadoutEditorProfile?.Inventory?.Equipment))
            {
                return;
            }

            List<EFT.InventoryLogic.Grid> prioritizedGrids = __result?.ToList() ?? [];
            HashSet<string> seenGridIds = prioritizedGrids
                .Where(grid => grid != null)
                .Select(grid => grid.ID)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet();

            AppendUniqueGrids(prioritizedGrids, seenGridIds, GetSlotGrids(equipment, EquipmentSlot.Backpack));
            AppendUniqueGrids(prioritizedGrids, seenGridIds, OtherPlayerProfileScreenPatch.LoadoutEditorProfile?.Inventory?.Stash?.Grids);
            __result = prioritizedGrids;
        }

        private static IEnumerable<EFT.InventoryLogic.Grid> GetSlotGrids(InventoryEquipment equipment, EquipmentSlot slot)
        {
            return (equipment.GetSlot(slot)?.ContainedItem as CompoundItem)?.Grids ?? [];
        }

        private static void AppendUniqueGrids(
            ICollection<EFT.InventoryLogic.Grid> destination,
            ISet<string> seenGridIds,
            IEnumerable<EFT.InventoryLogic.Grid> grids)
        {
            if (grids == null)
            {
                return;
            }

            foreach (EFT.InventoryLogic.Grid grid in grids)
            {
                if (grid == null)
                {
                    continue;
                }

                string gridId = grid.ID;
                if (!string.IsNullOrWhiteSpace(gridId) && !seenGridIds.Add(gridId))
                {
                    continue;
                }

                destination.Add(grid);
            }
        }
    }
}

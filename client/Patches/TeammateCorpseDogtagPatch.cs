using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace pitTeam.Patches
{
    internal static class TeammateCorpseDogtagGuard
    {
        public static bool IsTeammateCorpseDogtag(Item item)
        {
            try
            {
                if (item == null || item.GetItemComponent<DogtagComponent>() == null)
                {
                    return false;
                }

                Slot sourceSlot = item.CurrentAddress?.Container as Slot;
                if (sourceSlot == null ||
                    sourceSlot.ParentItem is not InventoryEquipment corpseEquipment ||
                    !ReferenceEquals(sourceSlot.ContainedItem, item) ||
                    !string.Equals(sourceSlot.ID, EquipmentSlot.Dogtag.ToString(), StringComparison.Ordinal))
                {
                    return false;
                }

                return TeammateCorpseIdentity.IsTeammateCorpseEquipment(corpseEquipment);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log?.LogError("[Loot] Failed to check teammate corpse dogtag.");
                pitFireTeam.Log?.LogError(ex);
                return false;
            }
        }
    }

    internal sealed class TeammateCorpseDogtagMovePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.InventoryLogic.ItemController), nameof(EFT.InventoryLogic.ItemController.CanMoveDogtag));
        }

        [PatchPrefix]
        private static bool PatchPrefix(Item item, ref bool __result)
        {
            if (!TeammateCorpseDogtagGuard.IsTeammateCorpseDogtag(item))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    internal sealed class TeammateCorpseDogtagRemovePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(EFT.InventoryLogic.ItemManipulator),
                nameof(EFT.InventoryLogic.ItemManipulator.Remove),
                new[] { typeof(Item), typeof(EFT.InventoryLogic.ItemController), typeof(bool) });
        }

        [PatchPrefix]
        private static bool PatchPrefix(Item item, bool simulate, ref Diz.LanguageExtensions.OperationResult<EFT.InventoryLogic.RemoveResult> __result)
        {
            if (!simulate || !TeammateCorpseDogtagGuard.IsTeammateCorpseDogtag(item))
            {
                return true;
            }

            __result = new EFT.InventoryLogic.ItemManipulator.ItemManuallyLockedError(item);
            return false;
        }
    }
}

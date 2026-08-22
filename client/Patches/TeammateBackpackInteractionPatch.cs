using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;
using System.Reflection;
using TMPro;

namespace pitTeam.Patches
{
    internal class TeammateBackpackInspectionUpdatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GamePlayerOwner), nameof(GamePlayerOwner.LateUpdate));
        }

        [PatchPostfix]
        private static void PatchPostfix(GamePlayerOwner __instance)
        {
            TeammateBackpackInspection.Update(__instance);
        }
    }

    internal class TeammateBackpackChangedContainerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.SearchController), nameof(EFT.SearchController.TryFindChangedContainer));
        }

        [PatchPostfix]
        private static void PatchPostfix(ItemAddress address, ref EFT.ItemInfo changedContainer, ref bool __result)
        {
            // Stock transfer validation rejects moves into or out of a parent searchable container that is not
            // searched. Teammate backpacks and fallen teammate corpse equipment are presented as already-searched.
            if (!__result ||
                (!TeammateBackpackInspection.ShouldTreatAddressSearched(address) &&
                 !TeammateCorpseLootGuard.ShouldTreatAddressSearched(address)))
            {
                return;
            }

            changedContainer = null;
            __result = false;
        }
    }

    internal class TeammateBackpackObserverStatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.SearchController), nameof(EFT.SearchController.GetObserverItemState));
        }

        [PatchPostfix]
        private static void PatchPostfix(Item item, ItemAddress address, ref EObserverItemState __result)
        {
            // Grid item views and CanModifyItem use observer state in addition to searchable-container state.
            // Without this, visible backpack items can still behave like unknown search results.
            if (__result == EObserverItemState.Known ||
                (!TeammateBackpackInspection.ShouldTreatObservedItemKnown(item, address) &&
                 !TeammateCorpseLootGuard.ShouldTreatObservedItemKnown(item, address)))
            {
                return;
            }

            __result = EObserverItemState.Known;
        }
    }

    internal class TeammateBackpackExaminedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(InventoryController), nameof(InventoryController.Examined), new[] { typeof(Item) });
        }

        [PatchPostfix]
        private static void PatchPostfix(InventoryController __instance, Item item, ref bool __result)
        {
            // Moving an item into a normal grid requires template examination. This is a temporary UI/session
            // answer only; it intentionally does not mutate the player's encyclopedia.
            if (__result ||
                (!TeammateBackpackInspection.ShouldTreatItemExamined(__instance, item) &&
                 !TeammateCorpseLootGuard.ShouldTreatItemExamined(__instance, item)))
            {
                return;
            }

            __result = true;
        }
    }

    internal class TeammateBackpackKnownItemPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.ActiveSearchController), nameof(EFT.ActiveSearchController.IsItemKnown));
        }

        [PatchPostfix]
        private static void PatchPostfix(EFT.ActiveSearchController __instance, Item item, ItemAddress itemAddress, ref bool __result)
        {
            if (__result ||
                (!TeammateBackpackInspection.ShouldTreatObservedItemKnown(item, itemAddress) &&
                 !TeammateCorpseLootGuard.ShouldTreatItemKnown(__instance, item)))
            {
                return;
            }

            __result = true;
        }
    }

    internal class TeammateBackpackSearchedItemPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.PlayerSearchController), nameof(EFT.PlayerSearchController.IsSearched));
        }

        [PatchPostfix]
        private static void PatchPostfix(EFT.PlayerSearchController __instance, EFT.InventoryLogic.SearchableItem item, ref bool __result)
        {
            if (__result ||
                (!TeammateBackpackInspection.IsActiveBackpack(item) &&
                 !TeammateCorpseLootGuard.ShouldTreatSearchableSearched(__instance, item)))
            {
                return;
            }

            __result = true;
        }
    }

    internal class TeammateBackpackUnknownContentsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.PlayerSearchController), nameof(EFT.PlayerSearchController.ContainsUnknownItems));
        }

        [PatchPostfix]
        private static void PatchPostfix(EFT.PlayerSearchController __instance, EFT.InventoryLogic.SearchableItem item, ref bool __result)
        {
            if (!__result ||
                (!TeammateBackpackInspection.IsActiveBackpack(item) &&
                 !TeammateCorpseLootGuard.ShouldTreatSearchableContentsKnown(__instance, item)))
            {
                return;
            }

            __result = false;
        }
    }

    internal class TeammateBackpackSimpleStashLabelPatch : ModulePatch
    {
        private static readonly FieldInfo SimpleGridNameField = AccessTools.Field(typeof(SimpleStashPanel), "_simpleGridName");
        private static readonly FieldInfo ContainerNameField = AccessTools.Field(typeof(SimpleStashPanel), "_containerName");
        private static readonly FieldInfo ContainerNamePanelField = AccessTools.Field(typeof(SimpleStashPanel), "_containerNamePanel");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(SimpleStashPanel), nameof(SimpleStashPanel.Show));
        }

        [PatchPostfix]
        private static void PatchPostfix(SimpleStashPanel __instance, CompoundItem item)
        {
            // SimpleStashPanel writes "LOOT" after Show(). Rewrite only the active teammate backpack panel so
            // the player sees whose backpack is open.
            if (!TeammateBackpackInspection.IsActiveBackpack(item))
            {
                return;
            }

            string title = TeammateBackpackInspection.GetActiveBackpackTitle();
            if (SimpleGridNameField.GetValue(__instance) is TMP_Text gridName)
            {
                gridName.text = title;
            }

            if (ContainerNameField.GetValue(__instance) is TMP_Text containerName)
            {
                containerName.text = title;
            }

            if (ContainerNamePanelField.GetValue(__instance) is UnityEngine.GameObject containerNamePanel)
            {
                containerNamePanel.SetActive(true);
            }
        }
    }
}

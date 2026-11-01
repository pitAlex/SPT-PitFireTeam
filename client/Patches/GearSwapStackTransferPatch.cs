using System.Reflection;
using Diz.LanguageExtensions;
using EFT.InventoryLogic;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    /// <summary>Reject inadmissible stack combinations during native preview as well as execution.</summary>
    internal sealed class GearSwapStackTransferPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ItemManipulator),
            nameof(ItemManipulator.TransferMaxStackCount),
            new[] { typeof(Item), typeof(Item), typeof(int), typeof(ItemController), typeof(bool) });

        [PatchPrefix]
        private static bool Prefix(Item source, Item target, ItemController itemController,
            ref OperationResult<TransferResult> __result)
        {
            if (!(itemController is GearSwapInventoryController draft) ||
                draft.Session?.CanCombineDraftStacks(source, target) == true) return true;

            // Merge, partial transfer and magazine top-off share this native boundary.
            // GridView repeats successful partial-transfer previews, even if execution fails.
            // A late Stage-only refusal leaves counts unchanged and can loop on the UI thread.
            __result = new DraftStackError();
            return false;
        }

        private sealed class DraftStackError : InventoryError
        {
            public override string ToString() => pitFireTeam.GetSocialUiText("SwapGearMixedStacks");
        }
    }
}

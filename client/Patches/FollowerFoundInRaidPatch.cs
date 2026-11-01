using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    /// <summary>Saved/carried follower inventories already have their own item provenance.</summary>
    internal sealed class FollowerFoundInRaidPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(
            AccessTools.TypeByName("SPT.Custom.CustomAI.PmcFoundInRaidEquipment"),
            "ConfigurePMCFindInRaidStatus", new[] { typeof(BotOwner) });

        [PatchPrefix]
        private static bool Prefix(BotOwner __0)
        {
            // ShallBeFollower is set before native activation, before the follower component
            // exists. Transit uses that same admission and must keep its carried loot flags.
            if (__0?.Profile?.Inventory?.Equipment == null || !BossPlayers.IsFollower(__0)) return true;

            // SPT normally rewrites worn equipment and container contents to imitate a PMC
            // who has looted this raid. Preserve our incoming flags, including nested cargo.
            // Keep SPT's independent dogtag rule without changing the inventory tree.
            var dogtag = __0.Profile.Inventory.Equipment.GetSlot(EquipmentSlot.Dogtag)?.ContainedItem;
            if (dogtag != null) dogtag.SpawnedInSession = true;
            return false;
        }
    }
}

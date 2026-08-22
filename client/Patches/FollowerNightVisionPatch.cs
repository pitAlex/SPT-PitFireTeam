using EFT.InventoryLogic;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Patches
{
    internal class FollowerNightVisionActivatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotNightVisionData), nameof(BotNightVisionData.Activate));
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotNightVisionData __instance)
        {
            if (__instance?._owner == null || !BossPlayers.IsFollower(__instance._owner))
            {
                return true;
            }

            try
            {
                __instance._slotHeadwear = __instance._owner.GetPlayer?.InventoryController?.Inventory?.Equipment?.GetSlot(EquipmentSlot.Headwear);
                if (__instance._slotHeadwear?.ContainedItem is not CompoundItem headwear)
                {
                    return false;
                }

                NightVisionComponent nightVision = headwear.GetItemComponentsInChildren<NightVisionComponent>(true).FirstOrDefault();
                if (nightVision == null)
                {
                    return false;
                }

                __instance.HaveNightVision = true;
                __instance.NightVisionItem = nightVision;
                __instance.TradableItem = nightVision.Item;
                __instance._nightVisionAtPocket = false;
                __instance._stopTryingMove = false;
                __instance._nextTimeCheck = Time.time + 10f;
                __instance.CheckWhatIWant();
            }
            catch (Exception ex)
            {
                Logger.LogError("[NVG] Failed to initialize follower night vision without stow behavior.");
                Logger.LogError(ex);
            }

            return false;
        }
    }

    internal class FollowerNightVisionOffPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotNightVisionData), nameof(BotNightVisionData.MoveToHeadPocket));
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotNightVisionData __instance)
        {
            if (__instance?._owner == null || !BossPlayers.IsFollower(__instance._owner))
            {
                return true;
            }

            try
            {
                TogglableComponent togglable = __instance.NightVisionItem?.Togglable;
                if (togglable?.On == true)
                {
                    togglable.Set(false, false, false);
                }

                __instance.UsingNow = false;
                __instance._nightVisionAtPocket = false;
                __instance._stopTryingMove = false;
            }
            catch (Exception ex)
            {
                Logger.LogError("[NVG] Failed to toggle follower night vision off.");
                Logger.LogError(ex);
            }

            return false;
        }
    }

    internal class FollowerNightVisionOnPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotNightVisionData), nameof(BotNightVisionData.MoveToHeadAndToggleOn));
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotNightVisionData __instance)
        {
            if (__instance?._owner == null || !BossPlayers.IsFollower(__instance._owner))
            {
                return true;
            }

            try
            {
                TogglableComponent togglable = __instance.NightVisionItem?.Togglable;
                if (togglable?.On == false)
                {
                    togglable.Set(true, false, false);
                }

                __instance.UsingNow = true;
                __instance._nightVisionAtPocket = false;
                __instance._stopTryingMove = false;
            }
            catch (Exception ex)
            {
                Logger.LogError("[NVG] Failed to toggle follower night vision on.");
                Logger.LogError(ex);
            }

            return false;
        }
    }
}

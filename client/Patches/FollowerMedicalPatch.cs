using System;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    internal class FollowerMedicalCancelPatch : ModulePatch
    {
        private static bool reported;

        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(BotAbstractMeds), nameof(BotAbstractMeds.CancelCurrent));

        [PatchPrefix]
        private static void PatchPrefix(BotAbstractMeds __instance)
        {
            // Prepare the operation before native CancelCurrent marks cancellation and
            // takes the previous weapon. Native HideWeapon then delivers its use callback.
            // Do not clear Using here: the native callback owns medical completion.
            if (!__instance._using || __instance._lastIsCanceled ||
                !BossPlayers.IsFollower(__instance._owner)) return;

            try
            {
                Meds item = __instance switch
                {
                    BotAbstractMedsToPart treatment => treatment.CurUsingMeds,
                    BotStimulators stimulators => stimulators._stimulator,
                    _ => null
                };
                if (item == null ||
                    !(__instance._owner.GetPlayer?.HandsController is Player.MedsController hands) ||
                    !ReferenceEquals(hands.Item, item) || hands.CurrentOperation == null ||
                    hands.CurrentOperation.State == Player.EOperationState.Finished) return;

                hands._failedToApply = true;
                hands.CurrentOperation.State = Player.EOperationState.Finished;
            }
            catch (Exception ex)
            {
                if (reported) return;
                reported = true;
                Logger.LogError($"[Medical] Could not finish cancelled follower medicine; retaining native cancellation and recovery. {ex}");
            }
        }
    }

    internal class FollowerFirstAidMinimumHealthPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.PropertyGetter(typeof(BotFirstAid), nameof(BotFirstAid.min_percent));

        [PatchPostfix]
        private static void PatchPostfix(BotFirstAid __instance, ref float __result)
        {
            // FindDamagedPart uses a strict > comparison: zero-health destroyed parts
            // remain ineligible, while living parts below 5% can receive first aid.
            if (BossPlayers.IsFollower(__instance._owner)) __result = 0f;
        }
    }

    internal class FollowerFirstAidFracturePatch : ModulePatch
    {
        private static bool reported;

        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(BotFirstAid), nameof(BotFirstAid.FindDamagedPart));

        [PatchPostfix]
        private static void PatchPostfix(BotFirstAid __instance)
        {
            if (!BossPlayers.IsFollower(__instance._owner)) return;
            try
            {
                Utils.FollowerFirstAidRetry.ValidateSelection(__instance);
                Utils.FollowerMedical.SelectFractureFirstAid(__instance);
            }
            catch (Exception ex)
            {
                if (reported) return;
                reported = true;
                Logger.LogError($"[Medical] Could not select follower fracture treatment; retaining native selection. {ex}");
            }
        }
    }

    internal class FollowerFirstAidAttemptPatch : ModulePatch
    {
        private static bool reported;
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(BotFirstAid), nameof(BotFirstAid.ApplyToSelf));

        [PatchPrefix]
        private static void PatchPrefix(BotFirstAid __instance)
        {
            if (!BossPlayers.IsFollower(__instance._owner)) return;
            try { Utils.FollowerFirstAidRetry.Begin(__instance); }
            catch (Exception ex)
            {
                if (reported) return;
                reported = true;
                Logger.LogError($"[Medical] Could not observe first-aid start: {ex}");
            }
        }
    }

    internal class FollowerSurgeryTargetPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(BotSurgicalKit), nameof(BotSurgicalKit.FindDamagedPart));

        [PatchPostfix]
        private static void PatchPostfix(BotSurgicalKit __instance)
        {
            if (!BossPlayers.IsFollower(__instance._owner) ||
                (__instance._bodyPartToHeal != EBodyPart.Head && __instance._bodyPartToHeal != EBodyPart.Chest) ||
                __instance._health == null || !__instance._owner.HealthController.IsAlive ||
                !__instance._owner.Settings.FileSettings.Mind.CAN_USE_MEDS) return;

            // Native selection has no side effects beyond these fields. Keep its part
            // ordering and item/cooldown checks without modifying the bot's parts array.
            __instance._bodyPartToHeal = null;
            __instance.Damaged = false;
            if (__instance.parts == null) return;
            foreach (EBodyPart part in __instance.parts)
            {
                if (part != EBodyPart.Stomach && part != EBodyPart.LeftArm &&
                    part != EBodyPart.RightArm && part != EBodyPart.LeftLeg && part != EBodyPart.RightLeg) continue;
                if (!__instance._health.IsBodyPartDestroyed(part)) continue;
                __instance._bodyPartToHeal = part;
                __instance.Damaged = true;
                break;
            }
        }
    }
}

using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using pitTeam.Modules;
using pitTeam.Utils;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Patches
{
    internal class FollowerGrenadeCooldownPatch : ModulePatch
    {
        private static readonly FieldInfo BotOwnerField = AccessTools.Field(typeof(BotGrenadeController), "BotOwner_0");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotGrenadeController), nameof(BotGrenadeController.DoThrow));
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotGrenadeController __instance, ref bool __result)
        {
            BotOwner bot = BotOwnerField?.GetValue(__instance) as BotOwner;
            if (bot == null)
            {
                return true;
            }

            if (!BossPlayers.IsFollower(bot))
            {
                return true;
            }

            if (!FollowerGrenadeRuntimeGate.IsThrowAllowed(bot))
            {
                FollowerGrenadeRuntimeGate.ShouldBlockThrowAttempt(bot, out _);
                __result = false;
                return false;
            }

            if (FollowerGrenadeCooldowns.CanProceedToThrow(bot))
            {
                return true;
            }

            __result = false;
            return false;
        }

        [PatchPostfix]
        private static void PatchPostfix(BotGrenadeController __instance)
        {
            BotOwner bot = BotOwnerField?.GetValue(__instance) as BotOwner;
            if (bot == null || !BossPlayers.IsFollower(bot))
            {
                return;
            }

            if (FollowerGrenadeRuntimeGate.IsThrowAllowed(bot))
            {
                bot.SetPose(1f);
            }
        }

    }

    internal class FollowerGrenadeThrowFinishPatch : ModulePatch
    {
        private static readonly FieldInfo BotOwnerField = AccessTools.Field(typeof(BotGrenadeController), "BotOwner_0");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotGrenadeController), "method_6", new[] { typeof(ThrowWeapItemClass) });
        }

        [PatchPostfix]
        private static void PatchPostfix(BotGrenadeController __instance, ThrowWeapItemClass grenade)
        {
            BotOwner bot = BotOwnerField?.GetValue(__instance) as BotOwner;
            if (bot == null || !BossPlayers.IsFollower(bot))
            {
                return;
            }

            bool completed = FollowerGrenadeRuntimeGate.ConsumeThrowReleased(bot);

            FollowerGrenadeRuntimeGate.FinishExplicitThrow(bot, completed);
        }
    }

    internal class FollowerGrenadeReleasePatch : ModulePatch
    {
        private static readonly FieldInfo BotOwnerField = AccessTools.Field(typeof(BotGrenadeController), "BotOwner_0");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotGrenadeController), "method_11");
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotGrenadeController __instance, Result<IHandsThrowController> throwResult)
        {
            BotOwner bot = BotOwnerField?.GetValue(__instance) as BotOwner;
            if (bot == null || !BossPlayers.IsFollower(bot))
            {
                return true;
            }

            if (throwResult.Succeed && throwResult.Value != null)
            {
                if (__instance.AIGreanageThrowData != null &&
                    FollowerShotSafety.IsRegularGrenadeTrajectoryUnsafeForThrower(
                        bot,
                        __instance,
                        __instance.AIGreanageThrowData,
                        out string trajectoryRejectReason))
                {
                    BattleRecorder.RecordGrenadeEvent(
                        bot,
                        "releaseBlocked",
                        trajectoryRejectReason,
                        target: __instance.AIGreanageThrowData?.Target);
                    __instance.method_6(null);
                    return false;
                }

                Vector3? target = __instance.AIGreanageThrowData?.Target;
                if (target.HasValue &&
                    FollowerShotSafety.IsFriendlyNearGrenadeImpact(
                        bot,
                        target.Value,
                        FollowerShotSafety.RegularGrenadeUnsafeRadius,
                        includeMovementPrediction: false,
                        out string impactRejectReason))
                {
                    BattleRecorder.RecordGrenadeEvent(
                        bot,
                        "releaseBlocked",
                        $"friendlyImpact:{impactRejectReason}",
                        target: target.Value);
                    __instance.method_6(null);
                    return false;
                }

                bool firstRelease = FollowerGrenadeRuntimeGate.MarkThrowReleased(bot);
                if (firstRelease)
                {
                    BattleRecorder.RecordGrenadeEvent(bot, "release", "cooldownStart");
                }
            }

            return true;
        }
    }
}

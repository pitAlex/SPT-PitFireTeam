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
        private static readonly FieldInfo BotOwnerField = AccessTools.Field(typeof(BotGrenadeController), "_owner");

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
        private static readonly FieldInfo BotOwnerField = AccessTools.Field(typeof(BotGrenadeController), "_owner");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotGrenadeController), nameof(BotGrenadeController.EndAll), new[] { typeof(EFT.InventoryLogic.ThrowWeap) });
        }

        [PatchPostfix]
        private static void PatchPostfix(BotGrenadeController __instance, EFT.InventoryLogic.ThrowWeap grenade)
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
        protected override MethodBase GetTargetMethod()
        {
            // CG_method_11 is the on-used callback: the grenade already exists then.
            return AccessTools.Method(typeof(Player.BaseGrenadeHandsController),
                nameof(Player.BaseGrenadeHandsController.ThrowGrenade),
                new[] { typeof(float), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(bool) });
        }

        [PatchPrefix]
        private static bool PatchPrefix(Player.BaseGrenadeHandsController __instance, Player ____player)
        {
            BotOwner bot = ____player?.AIData?.BotOwner;
            if (bot == null || !BossPlayers.IsFollower(bot))
            {
                return true;
            }

            BotGrenadeController grenades = bot.WeaponManager?.Grenades;
            AIGreanageThrowData data = grenades?.AIGreanageThrowData;
            string reason;
            if (!ReferenceEquals(____player.HandsController, __instance))
            {
                reason = "staleHandsController";
            }
            else if (FollowerGrenadeRuntimeGate.HasReleasedThrow(bot))
            {
                reason = "alreadyReleased";
            }
            else if (!FollowerGrenadeRuntimeGate.IsThrowAllowed(bot))
            {
                reason = "throwPermissionRevoked";
            }
            else if (data == null)
            {
                reason = "trajectoryMissing";
            }
            else if (FollowerShotSafety.IsFriendlyNearGrenadeImpact(bot, data.Target,
                FollowerShotSafety.RegularGrenadeUnsafeRadius, includeMovementPrediction: false,
                out string impactRejectReason))
            {
                reason = $"friendlyImpact:{impactRejectReason}";
            }
            else if (!FollowerShotSafety.IsRegularGrenadeTrajectoryUnsafeForThrower(bot, grenades, data, out reason))
            {
                return true;
            }

            BattleRecorder.RecordGrenadeEvent(bot, "releaseBlocked", reason, target: data?.Target);
            if (ReferenceEquals(____player.HandsController, __instance) &&
                !FollowerGrenadeRuntimeGate.HasReleasedThrow(bot))
                FollowerBlockedGrenadeRecovery.Record(bot, __instance);
            return false;
        }
    }

    internal class FollowerGrenadeSpawnPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player.BaseGrenadeHandsController),
                nameof(Player.BaseGrenadeHandsController.CreateAndRegisterGrenade),
                new[] { typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(float) });
        }

        [PatchPostfix]
        private static void PatchPostfix(Player ____player, Grenade __result)
        {
            BotOwner bot = ____player?.AIData?.BotOwner;
            if (__result == null || bot == null || !BossPlayers.IsFollower(bot)) return;
            if (FollowerGrenadeRuntimeGate.MarkThrowReleased(bot))
                BattleRecorder.RecordGrenadeEvent(bot, "release", "cooldownStart",
                    target: bot.WeaponManager?.Grenades?.AIGreanageThrowData?.Target);
        }
    }
}

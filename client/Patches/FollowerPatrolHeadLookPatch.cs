using System.Reflection;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    // Native head animation is independent of body/weapon steering. Keep its enemy
    // transitions, but let native rotation smoothly centre an idle follower's head.
    internal class FollowerPatrolHeadLookPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(BotHeadData), nameof(BotHeadData.UpdatePatrolState));

        [PatchPrefix]
        internal static bool PatchPrefix(BotHeadData __instance)
        {
            var owner = __instance?._owner;
            if (owner == null || owner.IsDead || owner.BotState != EBotState.Active ||
                owner.BotFollower?.HaveBoss != true || owner.Memory?.IsPeace != true ||
                __instance.State != BotHeadData.EHeadState.Patrol)
                return true;
            var follower = BossPlayers.Instance?.GetFollower(owner);
            if (!ReferenceEquals(follower?.GetBot(), owner)) return true;

            var look = owner.Settings?.FileSettings?.Look;
            if (look == null || owner.EnemiesController == null) return true;
            var observed = owner.EnemiesController.BestObservedEnemy;
            if (observed != null && observed.VisibilityLevel >= look.VISIBILITY_LEVEL_TO_TURN_HEAD)
                return true;

            __instance._targetHorizontalRotation = 0f;
            return false;
        }
    }
}

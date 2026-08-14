using EFT;
using HarmonyLib;
using pitTeam.BigBrain;
using pitTeam.Modules;
using pitTeam.Utils;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace pitTeam.Patches
{
    /// <summary>
    /// Stops stale vanilla action/reload state from starting a new burst after core combat has
    /// released ownership. Live core combat and the optional SAIN follower-combat path are not
    /// changed by this guard.
    /// </summary>
    internal sealed class FollowerShootDataSafetyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ShootData), nameof(ShootData.Shoot), Type.EmptyTypes);
        }

        [PatchPrefix]
        private static bool PatchPrefix(ShootData __instance, ref bool __result)
        {
            try
            {
                BotOwner? botOwner = __instance?.Owner;
                if (botOwner == null ||
                    !BossPlayers.IsFollower(botOwner) ||
                    pitFireTeam.UseSainFollowerCombat ||
                    FollowerCombatLayer.IsFollowerCombatLayerActive(botOwner) ||
                    FollowerCombatLayer.HasLiveGoalEnemyForFire(botOwner))
                {
                    return true;
                }

                FollowerRecovery.StopShooting(botOwner);
                BattleRecorder.RecordFollowerWeaponSafetyEvent(botOwner, "shootBlockedNoCombatEnemy");
                __result = false;
                return false;
            }
            catch
            {
                // Fail open so a compatibility problem cannot disable normal bot shooting.
                return true;
            }
        }
    }
}

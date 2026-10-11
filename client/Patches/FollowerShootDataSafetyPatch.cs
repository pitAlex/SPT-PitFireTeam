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
    /// released ownership, and enforces the shared muzzle-lane gate for every follower brain.
    /// </summary>
    internal sealed class FollowerShootDataSafetyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ShootData), nameof(ShootData.Shoot), Type.EmptyTypes);
        }

        [PatchPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool PatchPrefix(ShootData __instance, ref bool __result, out bool __state)
        {
            __state = false;
            try
            {
                BotOwner? botOwner = __instance?._owner;
                if (FollowerFireSafety.IsUnsafe(botOwner))
                {
                    __state = true;
                    FollowerFireSafety.Stop(botOwner);
                    __result = false;
                    return false;
                }

                if (botOwner == null ||
                    !BossPlayers.IsFollower(botOwner) ||
                    pitFireTeam.UseSainFollowerCombat(botOwner) ||
                    FollowerCombatLayer.IsFollowerCombatLayerActive(botOwner) ||
                    FollowerCombatLayer.HasLiveGoalEnemyForFire(botOwner))
                {
                    return true;
                }

                __state = true;
                FollowerRecovery.StopShooting(botOwner);
                BattleRecorder.RecordFollowerWeaponSafetyEvent(botOwner, "shootBlockedNoCombatEnemy");
                __result = false;
                return false;
            }
            catch
            {
                // Unknown layouts fail open; a confirmed veto survives cleanup faults.
                return !__state;
            }
        }

        [PatchPostfix]
        private static void PatchPostfix(ShootData __instance, bool __state, ref bool __result)
        {
            if (!__state) return;
            // HarmonyX permits other prefixes to run after a veto. SAIN may
            // still publish Shooting/result=true; reconcile after all prefixes.
            __result = false;
            try { FollowerFireSafety.Stop(__instance?._owner); }
            catch { /* A confirmed veto must retain its false result. */ }
        }
    }
}

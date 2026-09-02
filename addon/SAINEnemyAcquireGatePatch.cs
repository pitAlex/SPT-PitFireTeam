using EFT;
using HarmonyLib;
using SAIN.SAINComponent.Classes.EnemyClasses;
using pitTeam.Components;
using pitTeam.Modules;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace pitTeam.SAINAddon
{
    internal static class SAINEnemyAcquireGatePatch
    {
        public static void Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(typeof(SAINEnemyController), nameof(SAINEnemyController.CheckAddEnemy));
            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find SAINEnemyController.CheckAddEnemy for acquire gate patch.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(SAINEnemyAcquireGatePatch), nameof(Prefix_CheckAddEnemy)));
            Modules.Logger.LogInfo("[Init] SAIN enemy acquire gate patch applied.");
        }

        private static bool Prefix_CheckAddEnemy(SAINEnemyController __instance, IPlayer IPlayer, ref Enemy __result)
        {

            BotOwner? owner = __instance?.BotOwner;
            if (owner == null || !BossPlayers.IsFollower(owner))
            {
                return true;
            }

            if (owner.BotFollower.HaveBoss)
            {
                bool isTeammate = false;
                owner.BotFollower.BossToFollow.Followers.ForEach(f =>
                {
                    if (f.ProfileId == IPlayer.ProfileId)
                    {
                        isTeammate = true;
                    }
                });
                if (isTeammate)
                {
                    return false;
                }
            }

            if (!SAINFollowerEnemyRetentionService.ShouldAllowAcquire(owner, IPlayer, out _))
            {
                return false;
            }

            if (!SAINFollowerEnemyRetentionService.ShouldAllowRelationshipAcquire(owner, IPlayer, out _))
            {
                return false;
            }

            return true;
        }

    }
}

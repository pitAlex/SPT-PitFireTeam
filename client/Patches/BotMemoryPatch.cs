using EFT;

using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

using pitTeam.Modules;
using pitTeam.Utils;
using pitTeam.BigBrain;

namespace pitTeam.Patches
{
    /**
     * Patch to yell friendly fire from teamates
     */
    internal class BotMemoryDamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotMemoryClass), "method_8");
        }
        [PatchPrefix]
        private static void PatchPrefix(BotMemoryClass __instance, DamageInfoStruct damageInfo)
        {
            try
            {
                var botOwner_0 = AccessTools.Field(typeof(BotMemoryClass), "BotOwner_0").GetValue(__instance) as BotOwner;

                if (damageInfo.Player == null) return;

                bool isfollower = BossPlayers.IsFollower(botOwner_0);
                if (!isfollower) return;

                FollowerAwareness.FollowerHit(botOwner_0, damageInfo);

                bool isBossEnemy = BossPlayers.IsPlayerBoss(damageInfo.Player.iPlayer.ProfileId);

                bool isTeamate = false;

                if (botOwner_0.BotFollower.BossToFollow == null) return;

                botOwner_0.BotFollower.BossToFollow.Followers.ForEach(bt =>
                {
                    if (bt.ProfileId == damageInfo.Player.iPlayer.ProfileId) isTeamate = true;
                });

                if (!(isBossEnemy || isTeamate)) return;

                botOwner_0.BotTalk.TrySay(EPhraseTrigger.FriendlyFire, false);
            }
            catch (System.Exception e)
            {
                Modules.Logger.LogError(e);
            }
        }
    }

    internal sealed class FollowerGoalEnemyClearRetentionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.PropertySetter(typeof(BotMemoryClass), nameof(BotMemoryClass.GoalEnemy));
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotMemoryClass __instance, EnemyInfo value)
        {
            try
            {
                BotOwner botOwner = AccessTools.Field(typeof(BotMemoryClass), "BotOwner_0").GetValue(__instance) as BotOwner;
                EnemyInfo previous = __instance.GoalEnemy;
                string reason = FollowerGoalEnemyTracker.CurrentReason;

                if (value != null)
                {
                    if (ShouldBlockUnscopedMemoryOnlyGoal(botOwner, value, reason, out string? acquisitionBlockedReason))
                    {
                        FollowerGoalEnemyTracker.RecordSetter(
                            botOwner,
                            previous,
                            value,
                            allowed: false,
                            blockedReason: acquisitionBlockedReason);
                        return false;
                    }

                    bool allowed = FollowerCombatTargetCommitments.ShouldAllowGoalEnemySet(
                        botOwner,
                        previous,
                        value,
                        reason,
                        out string? blockedReason);
                    if (allowed && botOwner != null && BossPlayers.IsFollower(botOwner))
                    {
                        allowed = FollowerContactEnemyRetention.ShouldAllowGoalEnemySet(
                            botOwner,
                            previous,
                            value,
                            reason,
                            out blockedReason);
                    }

                    FollowerGoalEnemyTracker.RecordSetter(
                        botOwner,
                        previous,
                        value,
                        allowed,
                        blockedReason);
                    return allowed;
                }

                if (botOwner == null || !BossPlayers.IsFollower(botOwner))
                {
                    return true;
                }

                bool shouldBlockClear = FollowerContactEnemyRetention.ShouldBlockGoalEnemyClear(botOwner, previous);
                string? clearBlockedReason = shouldBlockClear ? "retentionBlockedClear" : null;
                if (!shouldBlockClear &&
                    previous != null &&
                    string.Equals(reason, "unscopedSetter", System.StringComparison.Ordinal) &&
                    SainGoalEnemyBridge.TryGetRetainedSameGoalEnemy(
                        botOwner,
                        previous,
                        out _))
                {
                    shouldBlockClear = true;
                    clearBlockedReason = "sainRetainedSameTarget";
                }

                FollowerGoalEnemyTracker.RecordSetter(
                    botOwner,
                    previous,
                    null,
                    allowed: !shouldBlockClear,
                    blockedReason: clearBlockedReason);
                return !shouldBlockClear;
            }
            catch (System.Exception e)
            {
                Modules.Logger.LogError(e);
                return true;
            }
        }

        private static bool ShouldBlockUnscopedMemoryOnlyGoal(
            BotOwner botOwner,
            EnemyInfo value,
            string reason,
            out string? blockedReason)
        {
            blockedReason = null;
            if (botOwner == null ||
                !BossPlayers.IsFollower(botOwner) ||
                !string.Equals(reason, "unscopedSetter", System.StringComparison.Ordinal))
            {
                return false;
            }

            // Vanilla SetVisible(true) recalculates GoalEnemy before CheckLookEnemy finishes
            // writing VisibleType and PersonalLastSeenTime. Defer soft/setup acquisition until
            // the normal post-look recalculation can use the completed, corrected sensor state.
            if (!pitFireTeam.IsSAINInstalled &&
                FollowerEnemyInfoCorrection.IsInsideLookCheck &&
                Enemy.RequiresAcquisitionAwarenessGate(value.GroupInfo?.Cause))
            {
                blockedReason = "lookCheckGoalEnemyDeferred";
                return true;
            }

            if (!Enemy.IsMemoryOnlyAcquisitionWithoutPersonalContact(value))
            {
                return false;
            }

            blockedReason = "memoryOnlyGoalEnemyBlocked";
            return true;
        }
    }
}

using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.Models.Enums;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace pitTeam.SAINAddon
{
    internal static class SAINFollowerSquadDecisionCalculator
    {
        private const float RadioCommsMaxDistanceSq = 1200f;
        private const float MyEnemySeenRecentTime = 10f;
        private const float SuppressFriendlyDistStart = 30f;
        private const float SuppressFriendlyDistEnd = 50f;
        private const float StartHelpFriendDist = 30f;
        private const float EndHelpFriendDist = 45f;
        private const float EndHelpFriendsEnemySeenRecentTime = 8f;
        private const float SearchEnemySeenRecentTime = 20f;
        private const float RegroupEnemySeenRecentTime = 60f;
        private const float PushSuppressedEnemyMaxPathDistance = 75f;
        private const float PushSuppressedEnemyMaxPathDistanceSprint = 100f;
        private const float PushSuppressedEnemyLowAmmoRatio = 0.5f;

        public static bool TryGetDecision(BotOwner owner, BotComponent bot, out ESquadDecision decision)
        {
            decision = ESquadDecision.None;
            if (owner == null || bot == null || owner.IsDead || !BossPlayers.IsFollower(owner))
            {
                return false;
            }

            if (owner.BotFollower?.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                return false;
            }

            Enemy myEnemy = bot.GoalEnemy;
            if (ShallPushSuppressedEnemy(bot, myEnemy))
            {
                decision = ESquadDecision.PushSuppressedEnemy;
                return true;
            }

            if (ShallGroupSearch(bot, owner, boss))
            {
                decision = ESquadDecision.GroupSearch;
                return true;
            }

            foreach (BotComponent member in EnumerateFollowerMembers(owner, boss))
            {
                if (!HasRadioComms(bot) && (bot.Transform.Position - member.Transform.Position).sqrMagnitude > RadioCommsMaxDistanceSq)
                {
                    continue;
                }

                if (myEnemy != null && member.HasEnemy && member.GoalEnemy != null &&
                    myEnemy.EnemyPlayer == member.GoalEnemy.EnemyPlayer)
                {
                    if (ShallSuppressEnemy(bot, member))
                    {
                        decision = ESquadDecision.Suppress;
                        return true;
                    }

                    if (ShallHelp(bot, member))
                    {
                        decision = ESquadDecision.Help;
                        return true;
                    }
                }
            }

            if (ShallSearch(bot, myEnemy))
            {
                decision = ESquadDecision.Search;
                return true;
            }

            if (ShallRegroup(owner, bot, boss, myEnemy))
            {
                decision = ESquadDecision.Regroup;
                return true;
            }

            return false;
        }

        private static bool ShallPushSuppressedEnemy(BotComponent bot, Enemy enemy)
        {
            if (enemy == null ||
                bot.Decision.SelfActionDecisions.LowOnAmmo(PushSuppressedEnemyLowAmmoRatio))
            {
                return false;
            }

            bool inRange = false;
            float modifier = enemy.Status.VulnerableAction == EEnemyAction.UsingSurgery ? 1.25f : 1f;
            if (enemy.Path.PathLength < PushSuppressedEnemyMaxPathDistanceSprint * modifier && bot.BotOwner?.CanSprintPlayer == true)
            {
                inRange = true;
            }
            else if (enemy.Path.PathLength < PushSuppressedEnemyMaxPathDistance * modifier)
            {
                inRange = true;
            }

            if (!inRange)
            {
                return false;
            }

            ETagStatus status = bot.Memory.Health.HealthStatus;
            if (status != ETagStatus.Healthy && status != ETagStatus.Injured)
            {
                return false;
            }

            var squadInfo = bot.Squad?.SquadInfo;
            if (squadInfo == null || !squadInfo.SquadIsSuppressEnemy(enemy.EnemyPlayer.ProfileId, out var suppressingMember))
            {
                return false;
            }

            if (suppressingMember == bot)
            {
                return false;
            }

            if (enemy.Status.VulnerableAction != EEnemyAction.None)
            {
                return true;
            }

            ETagStatus enemyHealth = enemy.EnemyPlayer.HealthStatus;
            return enemyHealth == ETagStatus.Dying ||
                   enemyHealth == ETagStatus.BadlyInjured ||
                   enemy.EnemyPlayer.IsInPronePose;
        }

        private static bool ShallSearch(BotComponent bot, Enemy enemy)
        {
            if (bot == null || enemy == null)
            {
                return false;
            }

            if (enemy.IsVisible || !enemy.Seen)
            {
                return false;
            }

            if (enemy.TimeSinceSeen > SearchEnemySeenRecentTime)
            {
                return false;
            }

            return true;
        }

        private static bool HasRadioComms(BotComponent bot)
        {
            return bot?.PlayerComponent?.Equipment?.GearInfo?.HasEarPiece == true;
        }

        private static bool ShallSuppressEnemy(BotComponent bot, BotComponent member)
        {
            if (bot.GoalEnemy?.SuppressionTarget == null || bot.GoalEnemy.IsVisible)
            {
                return false;
            }

            if (bot.BotOwner != null && SAINFollowerSuppressionSafety.IsFriendlyInSuppressionLane(bot.BotOwner, bot.GoalEnemy.EnemyPosition))
            {
                return false;
            }

            if (member.Decision.CurrentCombatDecision != ECombatDecision.Retreat)
            {
                return false;
            }

            float memberDistance = (member.Transform.Position - bot.BotOwner.Position).magnitude;
            float ammo = bot.Decision.SelfActionDecisions.AmmoRatio;
            if (bot.Decision.CurrentSquadDecision == ESquadDecision.Suppress)
            {
                return memberDistance <= SuppressFriendlyDistEnd && ammo >= 0.1f;
            }

            return memberDistance <= SuppressFriendlyDistStart && ammo >= 0.5f;
        }

        private static bool ShallGroupSearch(BotComponent bot, BotOwner owner, pitAIBossPlayer boss)
        {
            if (bot.GoalEnemy == null ||
                bot.GoalEnemy.IsVisible ||
                !bot.GoalEnemy.Seen ||
                bot.GoalEnemy.TimeSinceSeen > SearchEnemySeenRecentTime)
            {
                return false;
            }

            string enemyProfileId = bot.GoalEnemy.EnemyPlayer?.ProfileId;
            if (string.IsNullOrEmpty(enemyProfileId) || !SAINFollowerRuntimeBridge.HasSearchPartyLeader(boss, enemyProfileId))
            {
                return false;
            }

            foreach (BotComponent member in EnumerateFollowerMembers(owner, boss))
            {
                if (member.Decision.CurrentCombatDecision == ECombatDecision.Search && DoesMemberShareEnemy(bot, member))
                {
                    return SAINFollowerRuntimeBridge.TryLockSearchPartyLeader(boss, enemyProfileId, owner.ProfileId);
                }
            }

            return false;
        }

        private static bool DoesMemberShareEnemy(BotComponent bot, BotComponent member)
        {
            if (member == null || member.ProfileId == bot.ProfileId || member.BotOwner?.IsDead == true)
            {
                return false;
            }

            return member.GoalEnemy != null &&
                   bot.GoalEnemy != null &&
                   member.GoalEnemy.EnemyPlayer.ProfileId == bot.GoalEnemy.EnemyPlayer.ProfileId;
        }

        private static bool ShallHelp(BotComponent bot, BotComponent member)
        {
            if (member?.GoalEnemy == null)
            {
                return false;
            }

            float distance = member.GoalEnemy.Path.PathLength;
            bool visible = member.GoalEnemy.IsVisible;

            if (bot.Decision.CurrentSquadDecision == ESquadDecision.Help && member.GoalEnemy.Seen)
            {
                return distance < EndHelpFriendDist && member.GoalEnemy.TimeSinceSeen < EndHelpFriendsEnemySeenRecentTime;
            }

            return distance < StartHelpFriendDist && visible;
        }

        private static bool ShallRegroup(BotOwner owner, BotComponent bot, pitAIBossPlayer boss, Enemy enemy)
        {
            // Follower combat layer should not claim out-of-combat regroup by distance only.
            // Keep no-enemy regroup on vanilla/patrol/request routes and reserve this path for combat context.
            if (enemy == null)
            {
                return false;
            }

            Vector3 bossPos = FollowerCombatAnchor.GetAnchorPosition(owner);
            float regroupDistance = GetAutoRegroupDistance(owner);

            if (enemy.IsVisible || (enemy.Seen && enemy.TimeSinceSeen < RegroupEnemySeenRecentTime))
            {
                return false;
            }

            Vector3 botPos = owner.Position;
            Vector3 directionToBoss = bossPos - botPos;
            float bossDistance = directionToBoss.magnitude;
            Vector3 directionToEnemy = enemy.EnemyPosition - botPos;
            float enemyDistance = directionToEnemy.magnitude;
            if (enemyDistance < bossDistance && enemyDistance < 30f && Vector3.Dot(directionToEnemy.normalized, directionToBoss.normalized) > 0.25f)
            {
                return false;
            }

            return bossDistance > regroupDistance;
        }

        private static float GetAutoRegroupDistance(BotOwner owner)
        {
            float baseDistance = pitFireTeam.regroupRadius?.Value ?? 18f;
            return Mathf.Clamp(baseDistance, 10f, 38f);
        }

        private static System.Collections.Generic.IEnumerable<BotComponent> EnumerateFollowerMembers(BotOwner owner, pitAIBossPlayer boss)
        {
            var followers = boss.Followers;
            if (followers == null || followers.Count == 0)
            {
                yield break;
            }

            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i];
                if (follower == null || follower == owner || follower.IsDead)
                {
                    continue;
                }

                if (BotManagerComponent.Instance != null &&
                    BotManagerComponent.Instance.GetSAIN(follower, out BotComponent followerComponent) &&
                    followerComponent != null &&
                    followerComponent.BotActive)
                {
                    yield return followerComponent;
                }
            }
        }
    }
}

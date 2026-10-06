using System;
using EFT;
using pitTeam.Components;
using pitTeam.Patches;
using pitTeam.Utils;

namespace pitTeam.Modules
{
    // Relationship policy only. Never assigns goals, sight, positions, or firing permission.
    internal static class FollowerGroupHostility
    {
        [ThreadStatic] private static bool sharing;
        internal static bool IsSharing => sharing;

        internal static bool IsExplicitHostility(EBotEnemyCause cause) =>
            !Enemy.RequiresAcquisitionAwarenessGate(cause) ||
            cause == EBotEnemyCause.AddEnemyToAllGroupsInBotZone ||
            cause == EBotEnemyCause.AddEnemyToAllGroups;

        internal static pitAIBossPlayer? FindBoss(IPlayer person) =>
            BossPlayers.GetBoss(person.ProfileId) ??
            BossPlayers.GetFollowerByProfileId(person.ProfileId)?.GetBoss() ??
            person.AIData?.BotOwner?.BotFollower?.BossToFollow as pitAIBossPlayer;

        internal static bool IsHostileToPlayer(BotsGroup group, pitAIBossPlayer boss)
        {
            if (group == null || boss?.realPlayer == null || group == boss.bossGroup) return false;
            if (group.IsEnemy(boss.realPlayer)) return true;
            if (group.IsAlly(boss.realPlayer) || group.Neutrals.ContainsKey(boss.realPlayer)) return false;

            // Mirror the same initial protections as BotGroupAddEnemyPatch before asking
            // native policy. In particular, an AI's own side is not its player's side.
            if (IsProtected(group.InitialBotType)) return false;
            if ((group.InitialBotType == WildSpawnType.exUsec || Props.BossFollowersType.Contains(group.InitialBotType)) &&
                Utils.Utils.PlayerHasKnightQuest(boss.realPlayer.Profile)) return false;
            if (group.Side == boss.realPlayer.Side &&
                (group.Side == EPlayerSide.Usec || group.Side == EPlayerSide.Bear) &&
                Utils.Utils.FlagGet("pitFireTeam") && !Utils.Utils.FlagGet("isBadGuy")) return false;
            return group.IsPlayerEnemy(boss.realPlayer);
        }

        internal static bool ShouldBlockAmbientAddition(BotsGroup group, IPlayer person, EBotEnemyCause cause)
        {
            if (IsExplicitHostility(cause)) return false;
            if (group is BotsGroupPlayer playerGroup)
                return ShouldBlockCandidate(playerGroup.Boss, person);

            // Native setup/sight scans must classify a follower using the human leader.
            // Direct aggression and explicit Contact are handled separately above.
            var boss = FindBoss(person);
            return person.IsAI && boss != null && !IsHostileToPlayer(group, boss);
        }

        internal static bool ShouldBlockCandidate(pitAIBossPlayer? boss, IPlayer candidate)
        {
            var group = candidate?.AIData?.BotOwner?.BotsGroup;
            return boss?.realPlayer != null && candidate.IsAI && group != null &&
                group != boss.bossGroup && !IsHostileToPlayer(group, boss);
        }

        internal static void DeclareContact(BotOwner follower, Player enemy)
        {
            var boss = follower?.BotFollower?.BossToFollow as pitAIBossPlayer;
            var group = enemy?.AIData?.BotOwner?.BotsGroup;
            if (boss == null || group == null || !enemy.IsAI ||
                BossPlayers.IsPlayerBoss(enemy.ProfileId) || BossPlayers.IsFollowerProfileId(enemy.ProfileId) ||
                IsProtected(enemy.Profile.Info.Settings.Role)) return;
            ShareHostility(group, boss, EBotEnemyCause.addPlayer);
        }

        internal static void OnBossDamage(pitAIBossPlayer boss, IPlayer attacker)
        {
            if (boss?.realPlayer == null || attacker?.IsAI != true || FindBoss(attacker) != null ||
                IsProtected(attacker.Profile.Info.Settings.Role)) return;
            ShareHostility(attacker.AIData?.BotOwner?.BotsGroup, boss, EBotEnemyCause.followGetHit);
        }

        internal static void OnDamage(BotOwner victim, IPlayer attacker)
        {
            if (victim?.GetPlayer == null || attacker == null) return;
            var victimBoss = FindBoss(victim.GetPlayer);
            var attackerBoss = FindBoss(attacker);
            // Friendly fire inside the player's squad must not declare hostility.
            if (victimBoss != null && attackerBoss != null) return;
            if (victimBoss != null && attacker.IsAI)
            {
                if (IsProtected(attacker.Profile.Info.Settings.Role)) return;
                ShareHostility(attacker.AIData?.BotOwner?.BotsGroup, victimBoss, EBotEnemyCause.followGetHit);
            }
            else if (attackerBoss != null && !IsProtected(victim.Profile.Info.Settings.Role))
                ShareHostility(victim.BotsGroup, attackerBoss, EBotEnemyCause.followGetHit);
        }

        internal static void ShareHostility(BotsGroup group, pitAIBossPlayer boss, EBotEnemyCause cause)
        {
            if (sharing || group == null || group is BotsGroupPlayer || boss?.realPlayer == null ||
                group == boss.bossGroup || group.MembersCount == 0 || IsProtected(group.InitialBotType)) return;
            AllegiancePmcFriendship.RevokeGroup(group, cause);
            sharing = true;
            try
            {
                // The explicit origin authorizes the relationship; recipient memories remain
                // relation-only. Native AddEnemy validation is authoritative even if it fails.
                AddMissing(group, boss.realPlayer, EBotEnemyCause.addPlayerToBoss);
                var followers = boss.Followers;
                if (followers != null)
                    for (int i = 0; i < followers.Count; i++)
                    {
                        var member = followers[i];
                        if (member != null && !member.IsDead && member.GetPlayer != null)
                            AddMissing(group, member.GetPlayer, EBotEnemyCause.addPlayerToBoss);
                    }

                // Explicit Contact/aggression must also reach the follower group. Mere
                // setup sharing keeps its existing Scav-intent and awareness gates.
                if (boss.bossGroup != null)
                    for (int i = 0; i < group.MembersCount; i++)
                    {
                        var member = group.Member(i);
                        if (member != null && !member.IsDead && member.GetPlayer != null)
                            AddMissing(boss.bossGroup, member.GetPlayer,
                                cause == EBotEnemyCause.AddNewMember ? cause : EBotEnemyCause.addPlayerToBoss);
                    }
            }
            catch (Exception ex)
            {
                Logger.LogError("[FollowerHostility] Failed to share squad relationship: " + ex);
            }
            finally { sharing = false; }
        }

        private static void AddMissing(BotsGroup group, IPlayer person, EBotEnemyCause cause)
        {
            if (person.HealthController?.IsAlive != true) return;
            if (!group.IsEnemy(person)) group.AddEnemy(person, cause);
            if (group.IsEnemy(person))
            {
                group.Allies.Remove(person);
                group.Neutrals.Remove(person);
            }
        }

        private static bool IsProtected(WildSpawnType role) => Props.friendlyBotTypes.Contains(role);
    }
}

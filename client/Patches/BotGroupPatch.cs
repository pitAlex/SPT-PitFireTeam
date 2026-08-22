using Comfort.Common;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Patches
{
    internal class BotGroupUsecEnemyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotsGroup), nameof(BotsGroup.IsPlayerBadUsec));

        }
        [PatchPrefix]
        private static bool PatchPrefix(BotsGroup __instance, ref bool __result, IPlayer player)
        {
            // fix Usecs turning hostile because of UsecRaidRemainKills
            if (player?.Profile?.Info?.Side == EPlayerSide.Usec)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    internal class BotGroupAddEnemyPatch : ModulePatch
    {
        private const float FollowerGroupEnemyAcquireMaxDistanceSqr = 90f * 90f;

        private static bool IsInitialCause(EBotEnemyCause cause)
        {
            return cause == EBotEnemyCause.initial ||
                   cause == EBotEnemyCause.AddNewMember ||
                   cause == EBotEnemyCause.warn ||
                   cause == EBotEnemyCause.addBotNoGroup;
        }

        private static bool IsRogueFriendlyType(WildSpawnType role)
        {
            return role == WildSpawnType.exUsec || Utils.Props.BossFollowersType.Contains(role);
        }

        private static bool RequiresAwarenessGate(EBotEnemyCause cause)
        {
            switch (cause)
            {
                // Direct/aggressive causes: let these pass immediately.
                case EBotEnemyCause.byKill:
                case EBotEnemyCause.followGetHit:
                case EBotEnemyCause.addPlayer:
                case EBotEnemyCause.callBot:
                case EBotEnemyCause.gifterKill:
                case EBotEnemyCause.bossKillArena:
                case EBotEnemyCause.KillaSyncTagilla:
                case EBotEnemyCause.tagillaFindENemy:
                case EBotEnemyCause.fuckGestus:
                case EBotEnemyCause.pmcBossKill:
                case EBotEnemyCause.christmas:
                case EBotEnemyCause.synWithKilla:
                case EBotEnemyCause.ravangeZryachiy:
                case EBotEnemyCause.partisanBadKarma:
                case EBotEnemyCause.attackBTR:
                case EBotEnemyCause.tagillaAlarm:
                case EBotEnemyCause.MarkOfUnknowsDist:
                case EBotEnemyCause.zryachiyLogic:
                case EBotEnemyCause.pairLogic:
                    return false;
            }

            // Soft/ambient/group propagation causes must pass awareness check.
            return true;
        }

        private static bool HasGroupEnemyContact(BotsGroup group, IPlayer enemy)
        {
            if (group == null || enemy == null) return false;

            for (int i = 0; i < group.MembersCount; i++)
            {
                BotOwner member = group.Member(i);
                if (member == null) continue;

                if (member.EnemiesController != null &&
                    member.EnemiesController.EnemyInfos != null &&
                    member.EnemiesController.EnemyInfos.TryGetValue(enemy, out EnemyInfo info) &&
                    info != null)
                {
                    if (info.IsVisible ||
                        info.CanShoot ||
                        info.HaveSeenPersonal ||
                        (info.PersonalLastSeenTime > 0f && Time.time - info.PersonalLastSeenTime < 3f))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsEnemyNearGroup(BotsGroup group, IPlayer enemy)
        {
            if (group == null || enemy == null) return false;

            Vector3 enemyPos = enemy.Position;
            for (int i = 0; i < group.MembersCount; i++)
            {
                BotOwner member = group.Member(i);
                if (member == null) continue;

                if ((member.Position - enemyPos).sqrMagnitude <= FollowerGroupEnemyAcquireMaxDistanceSqr)
                {
                    return true;
                }
            }

            return false;
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotsGroup), "AddEnemy");

        }
        [PatchPrefix]
        private static bool PatchPrefix(BotsGroup __instance, ref bool __result, IPlayer person, EBotEnemyCause cause)
        {
            if (person == null || (person.IsAI && person.AIData?.BotOwner?.GetPlayer == null)) return true;
            if (person.Profile?.Info == null) return true;
            if (cause == EBotEnemyCause.addPlayerToBoss) return true;


            bool isBossPlayerGroup = __instance is BotsGroupPlayer;

            pitAIBossPlayer? plBoss = BossPlayers.GetBoss(person.ProfileId);

            BotsGroup? bossGroup = plBoss != null ? plBoss.bossGroup : null;

            bool isInitialCause = IsInitialCause(cause);

            bool badGuy = Utils.Utils.FlagGet("isBadGuy");

            bool isPMCGroup = __instance.Side == EPlayerSide.Bear || __instance.Side == EPlayerSide.Usec;

            bool isFriendlyPMC = isPMCGroup && Utils.Utils.FlagGet("pitFireTeam");

            // prevent Rogues from adding the player and his followers as enemies if they are friends with the Goons
            WildSpawnType groupRole = __instance.InitialBotType;

            if (!isBossPlayerGroup && isInitialCause && __instance.MembersCount > 0)
            {
                if (plBoss == null)
                {
                    var followerOfBoss = BossPlayers.GetFollowerByProfileId(person.ProfileId);
                    if (followerOfBoss != null) plBoss = followerOfBoss.GetBoss();
                }

                if (plBoss != null && Utils.Utils.PlayerHasKnightQuest(plBoss.realPlayer.Profile))
                {
                    bool isRogue = IsRogueFriendlyType(groupRole);
                    if (isRogue)
                    {
                        __result = false;
                        return false;
                    }
                }
            }

            // bad guy flag will exclude the player and his followers from the friendly PMCs
            if (
                badGuy &&
                !isBossPlayerGroup &&
                person.Profile.Info.Side == __instance.Side &&
                isPMCGroup &&
                (
                    plBoss != null ||
                    BossPlayers.IsFollowerProfileId(person.ProfileId)
                )
            )
            {
                return true;

            }

            // if friendly PMC side is on, prevent groups from adding same side players as enemies
            if (
                isInitialCause && person.Profile.Info.Side == __instance.Side && isFriendlyPMC
            )
            {
                __result = false;
                return false;
            }

            // prevent BTR from adding the player's followers as enemies (bug in 0.16 it seems)
            if (
                __instance.InitialBotType == WildSpawnType.shooterBTR &&
                BossPlayers.IsFollowerProfileId(person.ProfileId)
            )
            {
                __result = false;
                return false;
            }

            // from this point if this is not the player boss group, allow adding enemies
            if (!isBossPlayerGroup) return true;

            // Followers must ignore BTR targets/causes.
            if (cause == EBotEnemyCause.attackBTR || cause == EBotEnemyCause.serviceBTR)
            {
                __result = false;
                return false;
            }

            WildSpawnType? personRole = person.Profile?.Info?.Settings?.Role;
            if (personRole == WildSpawnType.shooterBTR)
            {
                __result = false;
                return false;
            }

            // For follower groups, prevent "omniscient" enemy acquisition:
            // apply this only to soft/propagated causes. High-confidence/direct causes are allowed.
            bool shouldGateByAwareness = RequiresAwarenessGate(cause);
            if (shouldGateByAwareness && !HasGroupEnemyContact(__instance, person) && !IsEnemyNearGroup(__instance, person))
            {
                __result = false;
                return false;
            }

            // prevent followers from adding teammates
            bool isGroupMember = false;
            for (int i = 0; i < __instance.MembersCount; i++)
            {
                BotOwner member = __instance.Member(i);
                if (member != null && member.ProfileId == person.ProfileId)
                {
                    isGroupMember = true;
                    break;
                }
            }

            if (isGroupMember)
            {
                __result = false;
                return false;
            }
            // prevent boss player from being added as an enemy
            else if ((__instance as BotsGroupPlayer).Boss != null && (__instance as BotsGroupPlayer).Boss.realPlayer.ProfileId == person.ProfileId)
            {
                __result = false;
                return false;
            }

            // prevent followers group from adding friendly bots as enemies
            if (
                personRole.HasValue &&
                (
                    (isInitialCause || personRole == WildSpawnType.shooterBTR || personRole == WildSpawnType.gifter) &&
                    Utils.Props.friendlyBotTypes.Contains(personRole.Value)
                )
            )
            {
                __result = false;
                return false;
            }

            // prevent Rogues from being added as enemies if they are friends with the player
            pitAIBossPlayer bossOfGroup = BossPlayers.GetBossByGroup(__instance.Id);

            if (isInitialCause && personRole != null && bossOfGroup != null)
            {
                Player bossPlayer = bossOfGroup.realPlayer;
                if (Utils.Utils.PlayerHasKnightQuest(bossPlayer.Profile))
                {
                    if (IsRogueFriendlyType(personRole.Value))
                    {
                        __result = false;
                        return false;
                    }
                }
            }

            return true;
        }

        /**
         * Whoever makes the player or his followers an enemy will become the enemy of the player's boss group (BTR is the exception)
         */
        [PatchPostfix]
        private static void PatchPostfix(BotsGroup __instance, IPlayer person, EBotEnemyCause cause, bool __result)
        {
            if (__result && __instance is BotsGroupPlayer)
            {
                Utils.Enemy.ForceIgnoreUntilAggressionOff(__instance);
            }

            if (
                person == null ||
                (person.IsAI && person.AIData?.BotOwner?.GetPlayer == null) ||
                cause == EBotEnemyCause.warn ||
                __instance is BotsGroupPlayer
            )
            {
                return;
            }

            bool isFriend = false;

            for (int i = 0; i < __instance.MembersCount; i++)
            {
                var mem = __instance.Member(i);
                // ignore BTR 
                if (
                    mem?.Profile?.Info?.Settings?.Role == WildSpawnType.shooterBTR
                )
                {
                    isFriend = true;
                    break;
                }
            }

            if (isFriend) return;

            var plBoss = BossPlayers.GetBoss(person.ProfileId);

            if (plBoss == null && person.IsAI && person.AIData?.BotOwner != null && BossPlayers.IsFollower(person.AIData.BotOwner))
            {
                plBoss = BossPlayers.GetBossByGroup(person.AIData.BotOwner.BotsGroup.Id);
            }

            if (plBoss == null) return;


            // - skip if the group is of the Rouges and they are just spawning
            if (cause == EBotEnemyCause.AddNewMember)
            {
                bool isFriendly = false;

                for (int i = 0; i < __instance.MembersCount; i++)
                {
                    var mem = __instance.Member(i);
                    WildSpawnType? memberRole = mem?.Profile?.Info?.Settings?.Role;
                    if (memberRole.HasValue && IsRogueFriendlyType(memberRole.Value))
                    {
                        isFriendly = true;
                        break;
                    }
                }

                if (isFriendly && Utils.Utils.PlayerHasKnightQuest(plBoss.realPlayer.Profile))
                {
                    return;
                }
            }

            try
            {
                BotsGroup bossGroup = plBoss.bossGroup;

                if (bossGroup != null)
                    for (int i = 0; i < __instance.MembersCount; i++)
                    {
                        var item = __instance.Member(i);
                        if (cause == EBotEnemyCause.AddNewMember)
                        {
                            bossGroup.AddEnemy(item, cause);
                        }
                        else
                            bossGroup.AddEnemy(item, EBotEnemyCause.addPlayerToBoss);
                    }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Failed to make a group an enemy");
                Modules.Logger.LogError(ex);
            }
        }

    }

    internal class BotGroupReportEnemyPatch : ModulePatch
    {
        private static readonly Dictionary<string, float> _syncDedupeUntil = new();

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotsGroup), "ReportAboutEnemy");
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotsGroup __instance, IPlayer enemy, EEnemyPartVisibleType isVisibleOnlyBySence, BotOwner reporter)
        {
            if (__instance is not BotsGroupPlayer) return true;
            if (enemy == null || reporter == null) return true;
            if (!BossPlayers.IsFollower(reporter)) return true;

            // Followers must never report the player boss or another follower as enemy.
            if (BossPlayers.IsPlayerBoss(enemy.ProfileId)) return false;
            if (enemy.IsAI && enemy.AIData?.BotOwner != null && BossPlayers.IsFollower(enemy.AIData.BotOwner)) return false;

            return true;
        }
    }

    /**
     * Patch to make friendly bots who just turned on the player to be marked as an enemy by the player's followers
     */
    /*internal class BotsGroupCheckAddPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotsGroup), "CheckAndAddEnemy");

        }
        [PatchPostfix]
        private static void PatchPostfix(BotsGroup __instance, IPlayer player, bool ignoreAI = false)
        {
            if (player == null || !player.IsAI) return;
            pitAIBossPlayer boss = BossPlayers.GetBoss(player.ProfileId);

            if (boss == null) return;
            if (boss.bossGroup == null) return;

            if (__instance.Id == boss.bossGroup.Id) return;

            for (int i = 0; i < __instance.MembersCount; i++)
            {
                boss.bossGroup.AddEnemy(__instance.Member(i), EBotEnemyCause.addPlayerToBoss);
            }

        }
    }*/

    internal class BotsGroupPlayer : BotsGroup
    {
        private pitAIBossPlayer boss;
        public pitAIBossPlayer Boss => boss;

        public BotsGroupPlayer(BotZone zone, IBotGame botGame, BotOwner initialBot, List<BotOwner> enemies, DeadBodiesController deadBodiesController, List<Player> allPlayers, pitAIBossPlayer player) : base(zone, botGame, initialBot, enemies, deadBodiesController, allPlayers, false)
        {
            RemoveEnemy(player.Player());
            AddAlly(player.realPlayer);
            Side = player.realPlayer.Side;

            boss = player;

            foreach (var item in Enemies)
            {
                WildSpawnType? Role = item.Value.Player?.Profile?.Info?.Settings?.Role;
                if (
                    Role.HasValue &&
                    Utils.Props.friendlyBotTypes.Contains(Role.Value)
                )
                {
                    RemoveEnemy(item.Value.Player, item.Value.Cause);
                    break;
                }
            }

            initialBot.Settings.GetFriendlyBotTypes().AddRange(Utils.Props.friendlyBotTypes);

            initialBot.Settings.GetEnemyBotTypes().RemoveAll(x => Utils.Props.friendlyBotTypes.Contains(x));

            if (Utils.Utils.FlagGet("isBadGuy"))
            {
                initialBot.Settings.FileSettings.Mind.DEFAULT_USEC_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;
                initialBot.Settings.FileSettings.Mind.DEFAULT_BEAR_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;

                if (Side == EPlayerSide.Bear) initialBot.Settings.GetEnemyBotTypes().Add(WildSpawnType.pmcBEAR);
                else if (Side == EPlayerSide.Usec) initialBot.Settings.GetEnemyBotTypes().Add(WildSpawnType.pmcUSEC);

                initialBot.Settings.GetWarnBotTypes().Remove(WildSpawnType.pmcBEAR);
                initialBot.Settings.GetWarnBotTypes().Remove(WildSpawnType.pmcUSEC);

            }
        }
    }

    // Guard vanilla propagation path from invalid players/zones, which can happen after out-of-band debug spawns.
    internal class BotControllerEnemyPropagationSafetyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(BotsController),
                "AddEnemyToAllGroupsInBotZone",
                new[] { typeof(IPlayer), typeof(IPlayer), typeof(IPlayer) });
        }

        [PatchPrefix]
        private static bool PatchPrefix(IPlayer aggressor, IPlayer groupOwner, IPlayer target)
        {
            if (!IsValidPlayerRef(aggressor) || !IsValidPlayerRef(groupOwner) || !IsValidPlayerRef(target))
            {
                return false;
            }

            return true;
        }

        private static bool IsValidPlayerRef(IPlayer player)
        {
            if (player == null) return false;

            try
            {
                var _ = player.Position;
            }
            catch
            {
                return false;
            }

            if (player.IsAI && player.AIData?.BotOwner?.GetPlayer == null)
            {
                return false;
            }

            return true;
        }
    }

    internal class PmcFriendlyFireRetaliationBridgePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(BotsController),
                nameof(BotsController.OnBeingHit),
                new[] { typeof(EFT.Ballistics.DamageInfo), typeof(Player) });
        }

        [PatchPrefix]
        [HarmonyPriority(Priority.First)]
        private static void PatchPrefix(EFT.Ballistics.DamageInfo damageInfo, Player target)
        {
            try
            {
                // Capture the relation before EFT's OnBeingHit handler adds the player as an enemy.
                PlayerKilledPatch.TryRememberFriendlyPmcBeforePlayerDamage(damageInfo, target);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[RetaliationBridge] failed to capture pre-damage PMC relationship.");
                Modules.Logger.LogError(ex);
            }
        }

        [PatchPostfix]
        private static void PatchPostfix(EFT.Ballistics.DamageInfo damageInfo, Player target)
        {
            try
            {
                // Same-side friendly systems can prevent normal group retaliation from spreading.
                // When the player boss or one of our followers directly damages a non-follower PMC,
                // restore the expected relation: the damaged PMC's group is now hostile to that attacker.
                IPlayer attacker = damageInfo.Player?.iPlayer;
                if (attacker == null || target == null || !target.IsAI)
                {
                    return;
                }

                bool attackerIsBoss = BossPlayers.IsPlayerBoss(attacker.ProfileId);
                bool attackerIsFollower = BossPlayers.IsFollowerProfileId(attacker.ProfileId);
                if (!attackerIsBoss && !attackerIsFollower)
                {
                    return;
                }

                BotOwner targetBot = target.AIData?.BotOwner;
                BotsGroup targetGroup = targetBot?.BotsGroup;
                if (targetBot == null || targetGroup == null || BossPlayers.IsFollower(targetBot))
                {
                    return;
                }

                if (!IsPmc(target.Side) || targetGroup is BotsGroupPlayer)
                {
                    return;
                }

                targetGroup.AddEnemy(attacker, EBotEnemyCause.AddEnemyToAllGroupsInBotZone);
                PromoteGroupMembersToAttacker(targetGroup, attacker);

                pitAIBossPlayer boss = attackerIsBoss
                    ? BossPlayers.GetBoss(attacker.ProfileId)
                    : BossPlayers.GetFollowerByProfileId(attacker.ProfileId)?.GetBoss();

                if (attackerIsFollower && boss?.realPlayer != null)
                {
                    targetGroup.AddEnemy(boss.realPlayer, EBotEnemyCause.AddEnemyToAllGroupsInBotZone);
                    PromoteGroupMembersToAttacker(targetGroup, boss.realPlayer);
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[RetaliationBridge] direct damage retaliation bridge failed.");
                Modules.Logger.LogError(ex);
            }
        }

        private static bool IsPmc(EPlayerSide side)
        {
            return side == EPlayerSide.Bear || side == EPlayerSide.Usec;
        }

        private static void PromoteGroupMembersToAttacker(BotsGroup group, IPlayer attacker)
        {
            if (group == null || attacker == null)
            {
                return;
            }

            for (int i = 0; i < group.MembersCount; i++)
            {
                BotOwner member = group.Member(i);
                if (member == null ||
                    member.IsDead ||
                    member.GetPlayer == null ||
                    member.Memory == null ||
                    member.EnemiesController == null)
                {
                    continue;
                }

                if (BossPlayers.IsFollower(member))
                {
                    continue;
                }

                LootingBotsInterop.PreventBotFromLooting(member, 180f);

                // AddEnemy updates group relations, but some members can remain in peaceful memory/layer
                // state until they personally see or are hit by the attacker. Give each group member the
                // already-registered enemy info so combat systems have a real GoalEnemy to evaluate.
                if (member.Memory?.HaveEnemy == true && member.Memory.GoalEnemy != null)
                {
                    continue;
                }

                if (!TryGetOrCreateEnemyInfo(member, group, attacker, out EnemyInfo info))
                {
                    continue;
                }

                member.Memory.IsPeace = false;
                info.IgnoreUntilAggression = false;
                using (FollowerGoalEnemyTracker.Begin("BotGroupPatch.PropagateAttackerToGroup", "groupAttackerShare"))
                {
                    member.Memory.GoalEnemy = info;
                }
            }
        }

        private static bool TryGetOrCreateEnemyInfo(BotOwner member, BotsGroup group, IPlayer attacker, out EnemyInfo info)
        {
            info = null;
            if (member == null || group == null || attacker == null)
            {
                return false;
            }

            if (member.EnemiesController?.EnemyInfos != null &&
                member.EnemiesController.EnemyInfos.TryGetValue(attacker, out info) &&
                info != null)
            {
                return true;
            }

            if (group.Enemies == null ||
                !group.Enemies.TryGetValue(attacker, out BotGroupEnemyInfo settings) ||
                settings == null ||
                member.Memory == null)
            {
                return false;
            }

            member.Memory?.AddEnemy(attacker, settings, false);

            if (member.EnemiesController?.EnemyInfos != null &&
                member.EnemiesController.EnemyInfos.TryGetValue(attacker, out info) &&
                info != null)
            {
                return true;
            }

            if (member.Memory?.GoalEnemy?.ProfileId == attacker.ProfileId)
            {
                info = member.Memory.GoalEnemy;
                return info != null;
            }

            return false;
        }
    }
}

using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using pitTeam.Patches;

namespace pitTeam.Modules
{
    // Raid-local, individual relationships. Never changes shared bot settings or factions.
    internal static class AllegiancePmcFriendship
    {
        internal const int RaidLimit = 3;
        internal const float SameSideChance = 0.30f;
        internal const float OppositeSideChance = 0.15f;
        private const string GreetingSubscriber = "AllegianceFriendlyGreeting";
        private sealed class Decision
        {
            internal BotOwner Bot;
            internal BotsGroup Group;
            internal bool Friendly;
            internal Action<BotOwner> MemberAdded;
            internal readonly AllegianceFriendlyGreeting Greeting = new AllegianceFriendlyGreeting();
        }

        private static readonly Dictionary<string, Decision> Decisions = new Dictionary<string, Decision>(StringComparer.Ordinal);
        private static int selectedCount;
        private static Player Human => Singleton<GameWorld>.Instance?.MainPlayer;

        private static float GetSelectionChance(EPlayerSide botSide, EPlayerSide playerSide)
        {
            int multiplier = Math.Max(1, Math.Min(5, pitFireTeam.friendlyChanceMultiplier?.Value ?? 1));
            float baseChance = botSide == playerSide ? SameSideChance : OppositeSideChance;
            float chance = baseChance + (1f - baseChance) * ((multiplier - 1) / 4f);
            return Shared.FriendlyEncounterPenaltyPolicy.Apply(chance, FriendlyEncounterPenaltyRuntime.GetPoints());
        }

        internal static void Reset()
        {
            BotOwnerUpdateHub.Unregister(GreetingSubscriber);
            foreach (var decision in Decisions.Values)
                if (decision.MemberAdded != null) decision.Group.OnMemberAdd -= decision.MemberAdded;
            Decisions.Clear();
            selectedCount = 0;
        }

        private static bool IsPmc(BotOwner bot) =>
            bot?.Profile?.Info?.Settings?.Role == WildSpawnType.pmcUSEC ||
            bot?.Profile?.Info?.Settings?.Role == WildSpawnType.pmcBEAR;

        private static bool IsGrouped(BotOwner bot)
        {
            var group = bot.BotsGroup;
            var original = bot.SpawnProfileData?.SpawnParams?.ShallBeGroup;
            return group == null || group is BotsGroupPlayer || group.MembersCount > 1 ||
                   group.TargetMembersCount > 1 || (original != null && original.Group && original.StartCount > 1);
        }

        internal static void Apply(BotOwner bot)
        {
            var human = Human;
            if (!GameplayModeRuntime.IsAllegiance || human == null || human.Side == EPlayerSide.Savage ||
                !IsPmc(bot) || bot.IsDead || bot.GetPlayer?.HealthController?.IsAlive != true ||
                BossPlayers.IsFollower(bot) || string.IsNullOrEmpty(bot.ProfileId)) return;

            if (!Decisions.TryGetValue(bot.ProfileId, out var decision))
            {
                decision = new Decision { Bot = bot, Group = bot.BotsGroup };
                Decisions.Add(bot.ProfileId, decision);
                if (IsGrouped(bot))
                {
                    Logger.LogInfo($"[Allegiance] candidate={bot.ProfileId} decision=excluded-group");
                    return;
                }
                if (selectedCount >= RaidLimit)
                {
                    Logger.LogInfo($"[Allegiance] candidate={bot.ProfileId} decision=raid-cap");
                    return;
                }
                float roll = UnityEngine.Random.value;
                float chance = GetSelectionChance(bot.Side, human.Side);
                decision.Friendly = chance >= 1f || roll < chance;
                if (decision.Friendly)
                {
                    selectedCount++;
                    BotOwnerUpdateHub.Register(GreetingSubscriber, UpdateGreeting);
                    decision.MemberAdded = member =>
                    {
                        if (BossPlayers.IsFollower(decision.Bot) || decision.Bot.BotsGroup != decision.Group)
                        {
                            decision.Group.OnMemberAdd -= decision.MemberAdded;
                            decision.MemberAdded = null;
                            return;
                        }
                        if (decision.Group.MembersCount > 1 && decision.Friendly)
                        {
                            Revoke(decision, "joined-group");
                            var boss = BossPlayers.GetBoss(Human?.ProfileId);
                            if (boss != null) FollowerGroupHostility.ShareHostility(decision.Group, boss, EBotEnemyCause.AddNewMember);
                        }
                    };
                    decision.Group.OnMemberAdd += decision.MemberAdded;
                }
                Logger.LogInfo($"[Allegiance] candidate={bot.ProfileId} side={bot.Side} playerSide={human.Side} roll={roll:F3} chance={chance:F2} friendly={decision.Friendly} selected={selectedCount}/{RaidLimit}");
            }
            if (!IsFriendly(bot)) return;

            Neutralize(bot, human);
            PmcKarmaRuntime.NoteFriendly(bot);
            foreach (var follower in BossPlayers.GetFollowersByBoss(human.ProfileId))
            {
                var member = follower?.GetBot();
                if (member != null && !member.IsDead && member.GetPlayer != null)
                {
                    Neutralize(bot, member.GetPlayer);
                    FactionHostility.EnsureNeutral(member.BotsGroup, bot.GetPlayer);
                    member.Memory?.DeleteInfoAboutEnemy(bot.GetPlayer);
                }
            }
        }

        private static void UpdateGreeting(BotOwner bot)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId) || BossPlayers.IsFollower(bot) ||
                !Decisions.TryGetValue(bot.ProfileId, out var decision) || decision.Bot != bot ||
                !IsFriendly(bot)) return;
            decision.Greeting.Update(bot, Human);
        }

        internal static void OnFollowerAdded(BotOwner follower, IPlayer leader)
        {
            var human = Human;
            if (!GameplayModeRuntime.IsAllegiance || human == null || human.Side == EPlayerSide.Savage ||
                leader?.ProfileId != human.ProfileId || follower == null || follower.IsDead ||
                follower.BotsGroup == null || follower.GetPlayer?.HealthController?.IsAlive != true ||
                !BossPlayers.IsFollower(follower)) return;

            // Selection may predate this follower. Ambient AddEnemy guards cannot remove
            // the pair's existing hostility, including SAIN's separately cached contact.
            foreach (var decision in Decisions.Values)
            {
                var candidate = decision.Bot;
                if (!decision.Friendly || candidate == null || candidate.IsDead ||
                    candidate.GetPlayer?.HealthController?.IsAlive != true ||
                    BossPlayers.IsFollower(candidate) || !IsFriendly(candidate)) continue;

                Neutralize(candidate, follower.GetPlayer);
                Neutralize(follower, candidate.GetPlayer);
                Logger.LogInfo($"[Allegiance] follower={follower.ProfileId} candidate={candidate.ProfileId} friendship-refreshed=both-directions");
            }
        }

        private static void Neutralize(BotOwner bot, IPlayer person)
        {
            FactionHostility.EnsureNeutral(bot.BotsGroup, person);
            bot.Memory?.DeleteInfoAboutEnemy(person);
        }

        private static bool IsFriendly(BotOwner bot)
        {
            if (bot == null || !Decisions.TryGetValue(bot.ProfileId, out var decision) || !decision.Friendly) return false;
            if (!BossPlayers.IsFollower(bot) && IsGrouped(bot)) Revoke(decision, "grouped");
            return decision.Friendly;
        }

        internal static bool CanRecruit(BotOwner bot, IPlayer player) =>
            !GameplayModeRuntime.IsAllegiance || player?.Side == EPlayerSide.Savage ||
            (player != null && player.ProfileId == Human?.ProfileId && IsPmc(bot) && IsFriendly(bot));

        internal static bool ShouldBlockEnemyAddition(BotsGroup group, IPlayer person, EBotEnemyCause cause)
        {
            if (!GameplayModeRuntime.IsAllegiance || group == null || person == null) return false;
            var human = Human;
            if (human == null) return false;
            Decision matched = null;
            foreach (var decision in Decisions.Values)
            {
                if (!decision.Friendly || BossPlayers.IsFollower(decision.Bot)) continue;
                bool toSquad = group == decision.Bot.BotsGroup &&
                    (person.ProfileId == human.ProfileId || FollowerGroupHostility.FindBoss(person)?.realPlayer?.ProfileId == human.ProfileId);
                bool fromSquad = group is BotsGroupPlayer playerGroup &&
                    playerGroup.Boss?.realPlayer?.ProfileId == human.ProfileId && person.ProfileId == decision.Bot.ProfileId;
                if (toSquad || fromSquad) { matched = decision; break; }
            }
            if (matched == null || !IsFriendly(matched.Bot)) return false;
            if (FollowerGroupHostility.IsExplicitHostility(cause))
            {
                Revoke(matched, cause.ToString());
                return false;
            }
            return true;
        }

        internal static void RevokeGroup(BotsGroup group, EBotEnemyCause cause)
        {
            if (!GameplayModeRuntime.IsAllegiance || group == null || !FollowerGroupHostility.IsExplicitHostility(cause)) return;
            foreach (var decision in Decisions.Values)
                if (decision.Friendly && !BossPlayers.IsFollower(decision.Bot) && decision.Bot.BotsGroup == group)
                    Revoke(decision, cause.ToString());
            // Contact/damage can precede activation of a standby bot. Never clear that
            // real hostility when its first activation finally reaches the selection hook.
            var human = Human;
            if (human == null || human.Side == EPlayerSide.Savage) return;
            for (int i = 0; i < group.MembersCount; i++)
            {
                var bot = group.Member(i);
                if (!IsPmc(bot) || BossPlayers.IsFollower(bot) ||
                    string.IsNullOrEmpty(bot.ProfileId) || Decisions.ContainsKey(bot.ProfileId)) continue;
                Decisions.Add(bot.ProfileId, new Decision { Bot = bot, Group = group });
                Logger.LogInfo($"[Allegiance] candidate={bot.ProfileId} decision=hostile-before-selection cause={cause}");
            }
        }

        private static void Revoke(Decision decision, string reason)
        {
            decision.Friendly = false;
            // The lifetime selection remains spent; reactivation and repeat requests cannot reroll it.
            Logger.LogInfo($"[Allegiance] candidate={decision.Bot.ProfileId} friendship-revoked={reason} selected={selectedCount}/{RaidLimit}");
        }
    }
}

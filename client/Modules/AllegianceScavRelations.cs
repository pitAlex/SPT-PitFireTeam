using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using pitTeam.Components;
using pitTeam.Patches;

namespace pitTeam.Modules
{
    // Raid-local diplomacy. Native group membership and enemy publication remain authoritative.
    internal static class AllegianceScavRelations
    {
        private sealed class Companion
        {
            internal BotOwner Bot;
            internal pitAIBossPlayer Boss;
            internal BotsGroup Group;
            internal Action<EDamageType> Died;
            internal readonly Dictionary<string, IPlayer> SharedEnemies = new Dictionary<string, IPlayer>();
            internal readonly HashSet<string> DirectAggressors = new HashSet<string>();
        }
        private sealed class Encounter
        {
            internal float Roll;
            internal bool Revoked;
            internal readonly List<BorrowedNeutral> Borrowed = new List<BorrowedNeutral>();
        }
        private sealed class BorrowedNeutral
        {
            internal BotsGroup Group;
            internal IPlayer Player;
            internal bool WasEnemy;
        }
        private sealed class Intent
        {
            internal bool Eligible, Recruited, CountsForTrust;
        }
        private static readonly Dictionary<string, Companion> Companions = new Dictionary<string, Companion>();
        private static readonly Dictionary<BotsGroup, Encounter> Encounters = new Dictionary<BotsGroup, Encounter>();
        private static readonly Dictionary<string, Intent> Intentions = new Dictionary<string, Intent>();
        private static readonly HashSet<string> Kills = new HashSet<string>();
        private static readonly HashSet<BotsGroup> AggressiveGroups = new HashSet<BotsGroup>();
        private static bool recruited, sharing;
        private static int neutralKills;
        private static Player Human => Singleton<GameWorld>.Instance?.MainPlayer;
        private static bool Enabled => GameplayModeRuntime.IsAllegiance && Human?.Side != EPlayerSide.Savage && Human != null;
        private static float NeutralChance => Math.Max(0f, 0.75f - neutralKills * 0.25f);
        internal static bool TrustBroken => neutralKills >= 3;
        private static bool HasLivingCompanion => Companions.Values.Any(c =>
            !c.Bot.IsDead && c.Bot.GetPlayer?.HealthController?.IsAlive == true);

        internal static void Reset()
        {
            foreach (var companion in Companions.Values)
                if (companion.Bot.GetPlayer?.HealthController != null)
                    companion.Bot.GetPlayer.HealthController.DiedEvent -= companion.Died;
            Companions.Clear(); Encounters.Clear(); Intentions.Clear(); Kills.Clear();
            AggressiveGroups.Clear();
            recruited = sharing = false; neutralKills = 0;
        }

        internal static bool KeepSeparateGroup(BotOwner bot, IPlayer leader, bool squadMate) =>
            Enabled && !squadMate && leader?.ProfileId == Human.ProfileId &&
            AllegiancePmcFriendship.IsRegularScav(bot) && AllegiancePmcFriendship.IsSelectedFriendly(bot, leader);

        internal static bool RefusesRecruitment(BotOwner bot) => Enabled && TrustBroken && AllegiancePmcFriendship.IsRegularScav(bot);

        internal static bool OwnsGroup(BotOwner bot, pitAIBossPlayer boss) => bot != null &&
            Companions.TryGetValue(bot.ProfileId, out var companion) && companion.Boss == boss && companion.Group == bot.BotsGroup;

        internal static bool IsCompanionGroup(BotsGroup group) => Companions.Values.Any(c => c.Group == group);

        internal static void OnFollowerAdded(BotOwner bot, pitAIBossPlayer boss, bool squadMate)
        {
            if (!Enabled || boss?.realPlayer?.ProfileId != Human.ProfileId || bot == null) return;
            if (KeepSeparateGroup(bot, boss.realPlayer, squadMate))
            {
                if (!Companions.ContainsKey(bot.ProfileId))
                {
                    var companion = new Companion { Bot = bot, Boss = boss, Group = bot.BotsGroup };
                    companion.Died = _ =>
                    {
                        try { OnRemoved(bot); }
                        catch (Exception ex) { Logger.LogError("[ScavRelations] Companion death cleanup failed: " + ex); }
                    };
                    Companions.Add(bot.ProfileId, companion);
                    bot.GetPlayer.HealthController.DiedEvent += companion.Died;
                }
                recruited = true;
                Logger.LogInfo($"[ScavRelations] companion={bot.ProfileId} group={bot.BotsGroup.Id} pmcGroup={boss.bossGroup?.Id}");
            }
            foreach (var companion in Companions.Values) LinkAllies(companion);
            foreach (var player in Singleton<GameWorld>.Instance.AllAlivePlayersList.ToArray()) Apply(player?.AIData?.BotOwner);
        }

        private static void Ally(BotsGroup group, Player player)
        {
            if (group == null || player == null) return;
            FactionHostility.EnsureNeutral(group, player);
            group.Neutrals.Remove(player);
            group.AddAlly(player);
        }

        private static void LinkAllies(Companion companion)
        {
            Ally(companion.Group, companion.Boss.realPlayer);
            var pmcGroup = companion.Boss.bossGroup;
            Ally(pmcGroup, companion.Bot.GetPlayer);
            foreach (var member in companion.Boss.Followers)
                if (member != null && member != companion.Bot && !member.IsDead) Ally(companion.Group, member.GetPlayer);
            if (pmcGroup != null)
                foreach (var enemy in pmcGroup.Enemies.Keys.ToArray()) MirrorEnemy(companion, enemy);
        }

        private static bool OrdinaryGroup(BotsGroup group)
        {
            if (group == null || group is BotsGroupPlayer || group.InitialBotType != WildSpawnType.assault || group.MembersCount == 0) return false;
            for (int i = 0; i < group.MembersCount; i++)
                if (!AllegiancePmcFriendship.IsRegularScav(group.Member(i)) || BossPlayers.IsFollower(group.Member(i))) return false;
            return true;
        }

        private static bool IsNeutral(BotsGroup group) => HasLivingCompanion && group != null && OrdinaryGroup(group) &&
            Encounters.TryGetValue(group, out var encounter) && !encounter.Revoked && encounter.Roll < NeutralChance;

        private static void MarkAggressive(BotsGroup group)
        {
            if (group == null) return;
            AggressiveGroups.Add(group);
            if (Encounters.TryGetValue(group, out var encounter)) encounter.Revoked = true;
        }

        internal static void OnBossDamage(pitAIBossPlayer boss, IPlayer attacker)
        {
            if (Enabled && boss?.realPlayer?.ProfileId == Human.ProfileId && attacker != null &&
                FollowerGroupHostility.FindBoss(attacker) == null) MarkAggressive(attacker.AIData?.BotOwner?.BotsGroup);
        }

        internal static void Apply(BotOwner bot)
        {
            if (!Enabled || !recruited || bot == null || bot.IsDead || !OrdinaryGroup(bot.BotsGroup)) return;
            var group = bot.BotsGroup;
            if (TrustBroken)
            {
                MakeHostile(group);
                return;
            }
            if (!HasLivingCompanion) return;
            if (!Encounters.ContainsKey(group))
            {
                Encounters.Add(group, new Encounter { Roll = UnityEngine.Random.value, Revoked = AggressiveGroups.Contains(group) });
                Logger.LogInfo($"[ScavRelations] group={group.Id} neutral={IsNeutral(group)} kills={neutralKills}");
            }
            if (!IsNeutral(group)) return;
            var encounter = Encounters[group];
            var boss = BossPlayers.GetBoss(Human.ProfileId);
            BorrowNeutral(encounter, group, Human);
            if (boss == null) return;
            foreach (var follower in boss.Followers)
                if (follower != null && !follower.IsDead)
                {
                    BorrowNeutral(encounter, group, follower.GetPlayer);
                    BorrowNeutral(encounter, follower.BotsGroup, bot.GetPlayer);
                }
            BorrowNeutral(encounter, boss.bossGroup, bot.GetPlayer);
        }

        private static void BorrowNeutral(Encounter encounter, BotsGroup group, IPlayer player)
        {
            if (group == null || player == null) return;
            if (!group.IsAlly(player) && !group.Neutrals.Keys.Any(p => p.ProfileId == player.ProfileId) &&
                !encounter.Borrowed.Any(r => r.Group == group && r.Player.ProfileId == player.ProfileId))
                encounter.Borrowed.Add(new BorrowedNeutral { Group = group, Player = player,
                    WasEnemy = group.Enemies.Keys.Any(p => p.ProfileId == player.ProfileId) });
            FactionHostility.EnsureNeutral(group, player);
        }

        private static void ReleaseBorrowedNeutrality()
        {
            // Keep the raid's rolls and trust history. Only undo neutrality owned by a living companion.
            foreach (var encounter in Encounters.Values.ToArray())
            {
                var borrowed = encounter.Borrowed.ToArray();
                encounter.Borrowed.Clear();
                foreach (var relation in borrowed)
                {
                    var group = relation.Group;
                    var player = relation.Player;
                    if (player.HealthController?.IsAlive != true || group.IsEnemy(player) || group.IsAlly(player) ||
                        AllegiancePmcFriendship.ShouldBlockEnemyAddition(group, player, EBotEnemyCause.initial)) continue;
                    group.Neutrals.Remove(player);
                    // Publish through native admission; do not select goals or fabricate visibility.
                    if (relation.WasEnemy || group.IsPlayerEnemy(player)) group.AddEnemy(player, EBotEnemyCause.initial);
                }
            }
            Logger.LogInfo("[ScavRelations] last-companion-removed neutrality-released");
        }

        internal static bool ShouldBlockEnemyAddition(BotsGroup group, IPlayer person, EBotEnemyCause cause)
        {
            if (!Enabled || group == null || person == null || sharing) return false;
            bool explicitCause = FollowerGroupHostility.IsExplicitHostility(cause);
            foreach (var companion in Companions.Values)
            {
                if (companion.Bot.IsDead || companion.Bot.GetPlayer?.HealthController?.IsAlive != true) continue;
                bool pair = (group == companion.Group && (person.ProfileId == Human.ProfileId ||
                    FollowerGroupHostility.FindBoss(person)?.realPlayer?.ProfileId == Human.ProfileId)) ||
                    (group is BotsGroupPlayer playerGroup && playerGroup.Boss == companion.Boss && person.ProfileId == companion.Bot.ProfileId);
                if (pair && !explicitCause) return true;
            }
            BotsGroup other = null;
            if (IsNeutral(group) && (person.ProfileId == Human.ProfileId || FollowerGroupHostility.FindBoss(person)?.realPlayer?.ProfileId == Human.ProfileId)) other = group;
            else if (person.AIData?.BotOwner?.BotsGroup is BotsGroup candidate && IsNeutral(candidate) &&
                ((group is BotsGroupPlayer ownedGroup && ownedGroup.Boss?.realPlayer?.ProfileId == Human.ProfileId) ||
                 IsCompanionGroup(group))) other = candidate;
            if (other == null) return false;
            if (!explicitCause) return true;
            MarkAggressive(other);
            Logger.LogInfo($"[ScavRelations] group={other.Id} neutrality-revoked cause={cause}");
            return false;
        }

        internal static void OnEnemyAdded(BotsGroup group, IPlayer person, EBotEnemyCause cause)
        {
            if (!Enabled || sharing || group == null || person == null) return;
            if (FollowerGroupHostility.IsExplicitHostility(cause))
            {
                if (group is BotsGroupPlayer ownedGroup && ownedGroup.Boss?.realPlayer?.ProfileId == Human.ProfileId &&
                    person.AIData?.BotOwner?.BotsGroup is BotsGroup targetGroup) MarkAggressive(targetGroup);
                else if (!IsCompanionGroup(group) && (person.ProfileId == Human.ProfileId ||
                    FollowerGroupHostility.FindBoss(person)?.realPlayer?.ProfileId == Human.ProfileId)) MarkAggressive(group);
            }
            foreach (var companion in Companions.Values.ToArray())
            {
                if (companion.Bot.IsDead) continue;
                if (group == companion.Boss.bossGroup) MirrorEnemy(companion, person);
                else if (group == companion.Group)
                {
                    if (person.AIData?.BotOwner is BotOwner enemy && AllegiancePmcFriendship.IsRegularScav(enemy))
                        FollowerGroupHostility.ShareHostility(enemy.BotsGroup, companion.Boss, EBotEnemyCause.addPlayer);
                }
                else if (person.ProfileId == Human.ProfileId || FollowerGroupHostility.FindBoss(person)?.realPlayer?.ProfileId == Human.ProfileId)
                    for (int i = 0; i < group.MembersCount; i++) MirrorEnemy(companion, group.Member(i)?.GetPlayer);
            }
        }

        private static void MirrorEnemy(Companion companion, IPlayer enemy)
        {
            if (enemy == null || enemy.HealthController?.IsAlive != true || enemy.ProfileId == Human.ProfileId ||
                BossPlayers.IsFollowerProfileId(enemy.ProfileId) || companion.Group.IsEnemy(enemy)) return;
            sharing = true;
            try
            {
                companion.Group.AddEnemy(enemy, EBotEnemyCause.addPlayerToBoss);
                if (companion.Group.IsEnemy(enemy)) companion.SharedEnemies[enemy.ProfileId] = enemy;
            }
            finally { sharing = false; }
        }

        internal static void OnDamage(BotOwner victim, IPlayer attacker)
        {
            if (!Enabled || victim == null || attacker == null) return;
            if (SquadKiller(attacker) && !BossPlayers.IsFollower(victim))
            {
                MarkAggressive(victim.BotsGroup);
            }
            if (FollowerGroupHostility.FindBoss(victim.GetPlayer)?.realPlayer?.ProfileId == Human.ProfileId)
                MarkAggressive(attacker.AIData?.BotOwner?.BotsGroup);
            if (victim != null && attacker != null && Companions.TryGetValue(victim.ProfileId, out var companion) &&
                FollowerGroupHostility.FindBoss(attacker) == null) companion.DirectAggressors.Add(attacker.ProfileId);
        }

        private static bool SquadKiller(IPlayer player) => player != null &&
            (player.ProfileId == Human?.ProfileId || ((player.Side == EPlayerSide.Usec || player.Side == EPlayerSide.Bear) &&
             FollowerGroupHostility.FindBoss(player)?.realPlayer?.ProfileId == Human?.ProfileId));

        internal static void RememberBeforeDamage(EFT.Ballistics.DamageInfo damage, Player target)
        {
            if (damage.Damage <= 0f || float.IsNaN(damage.Damage) || float.IsInfinity(damage.Damage) ||
                !SquadKiller(damage.Player?.iPlayer)) return;
            CaptureIntent(target, damage.Player?.iPlayer?.ProfileId == Human?.ProfileId);
        }

        internal static void RememberContact(pitAIBossPlayer boss, Player target)
        {
            if (boss?.realPlayer?.ProfileId == Human?.ProfileId) CaptureIntent(target, true);
        }

        private static void CaptureIntent(Player target, bool playerInitiated)
        {
            if (!Enabled || target?.HealthController?.IsAlive != true ||
                target.AIData?.BotOwner is not BotOwner bot || !AllegiancePmcFriendship.IsRegularScav(bot) ||
                bot.BotsGroup == null || Intentions.ContainsKey(target.ProfileId)) return;
            var boss = BossPlayers.GetBoss(Human.ProfileId);
            bool companion = Companions.ContainsKey(bot.ProfileId);
            bool neutral = !bot.BotsGroup.IsEnemy(Human) && boss?.bossGroup?.IsEnemy(target) != true &&
                boss?.Followers?.Any(f => f != null && (f.Side == EPlayerSide.Usec || f.Side == EPlayerSide.Bear) && bot.BotsGroup.IsEnemy(f.GetPlayer)) != true &&
                (bot.BotsGroup.IsAlly(Human) || bot.BotsGroup.Neutrals.ContainsKey(Human));
            // Freeze the first interaction, including uncommanded PMC damage. A later
            // human shot must not convert an AI-started fight into player betrayal.
            var intent = new Intent { Eligible = playerInitiated && (companion || neutral),
                Recruited = companion, CountsForTrust = recruited };
            Intentions.Add(target.ProfileId, intent);
            Logger.LogInfo($"[ScavRelations] intent victim={target.ProfileId} eligible={intent.Eligible} companion={companion} playerInitiated={playerInitiated}");
        }

        internal static void RecordKill(Player victim, IPlayer killer)
        {
            if (!Enabled || victim == null || !SquadKiller(killer) ||
                !Intentions.TryGetValue(victim.ProfileId, out var intent) || !intent.Eligible || !Kills.Add(victim.ProfileId)) return;
            ScavFenceKarma.RecordKill(Human, intent.Recruited);
            if (!intent.CountsForTrust || TrustBroken) return;
            neutralKills++;
            Logger.LogInfo($"[ScavRelations] neutral-kill victim={victim.ProfileId} killer={killer.ProfileId} total={neutralKills} neutralChance={NeutralChance}");
            foreach (var pair in Encounters.ToArray())
                if (!IsNeutral(pair.Key) && !pair.Value.Revoked)
                {
                    pair.Value.Revoked = true;
                    var boss = BossPlayers.GetBoss(Human.ProfileId);
                    if (boss != null) FollowerGroupHostility.ShareHostility(pair.Key, boss, EBotEnemyCause.addPlayer);
                }
            if (!TrustBroken) return;
            foreach (var player in Singleton<GameWorld>.Instance.AllAlivePlayersList.ToArray()) Apply(player?.AIData?.BotOwner);
            // Do not change ownership while the native death dispatcher is enumerating listeners.
            var departures = Companions.Values.Where(c => !c.Bot.IsDead && c.Bot.GetPlayer.HealthController?.IsAlive == true)
                .Select(c => new { Companion = c, Follower = BossPlayers.GetFollowerByProfileId(c.Bot.ProfileId) })
                .Where(c => c.Follower != null).ToArray();
            if (departures.Length == 0) return;
            Utils.Utils.SetTimeout(() =>
            {
                var removed = new List<Companion>();
                foreach (var departure in departures)
                {
                    var companion = departure.Companion;
                    if (Companions.TryGetValue(companion.Bot.ProfileId, out var current) && current == companion &&
                        !companion.Bot.IsDead && BossPlayers.GetFollowerByProfileId(companion.Bot.ProfileId) == departure.Follower)
                    {
                        departure.Follower.Dismiss(false);
                        BossPlayers.RemoveFollower(companion.Bot, companion.Boss);
                        removed.Add(companion);
                    }
                }
                // Drop all departing Scavs from squad ownership before publishing hostility,
                // so they do not inherit a fight against each other as former squadmates.
                foreach (var companion in removed) MakeHostile(companion.Group);
            }, 0);
        }

        private static void MakeHostile(BotsGroup group)
        {
            MarkAggressive(group);
            var boss = BossPlayers.GetBoss(Human.ProfileId);
            if (boss != null) FollowerGroupHostility.ShareHostility(group, boss, EBotEnemyCause.addPlayer);
            else
            {
                AllegiancePmcFriendship.RevokeGroup(group, EBotEnemyCause.addPlayer);
                group.AddEnemy(Human, EBotEnemyCause.addPlayerToBoss);
            }
        }

        internal static BotsGroup DepartureGroup(BotOwner bot) => bot != null &&
            Companions.TryGetValue(bot.ProfileId, out var companion) ? companion.Group : null;

        private static void MakeDepartureNeutral(Companion companion)
        {
            companion.Group.Allies.Remove(companion.Boss.realPlayer);
            FactionHostility.EnsureNeutral(companion.Group, companion.Boss.realPlayer);
            FactionHostility.EnsureNeutral(companion.Boss.bossGroup, companion.Bot.GetPlayer);
            foreach (var member in companion.Boss.Followers)
                if (member != companion.Bot && member?.GetPlayer != null)
                {
                    companion.Group.Allies.Remove(member.GetPlayer);
                    FactionHostility.EnsureNeutral(companion.Group, member.GetPlayer);
                    FactionHostility.EnsureNeutral(member.BotsGroup, companion.Bot.GetPlayer);
                }
        }

        internal static void OnRemoved(BotOwner bot)
        {
            if (bot == null || !Companions.TryGetValue(bot.ProfileId, out var companion)) return;
            Companions.Remove(bot.ProfileId);
            if (companion.Bot.GetPlayer?.HealthController != null)
                companion.Bot.GetPlayer.HealthController.DiedEvent -= companion.Died;
            companion.Boss.bossGroup?.Allies.Remove(bot.GetPlayer);
            foreach (var enemy in companion.SharedEnemies.Values)
                if (enemy.AIData?.BotOwner is BotOwner scav && AllegiancePmcFriendship.IsRegularScav(scav) &&
                    !companion.DirectAggressors.Contains(enemy.ProfileId))
                {
                    // These were borrowed from the PMC alliance, not the Scav's own conflicts.
                    FactionHostility.EnsureNeutral(companion.Group, enemy);
                    FactionHostility.EnsureNeutral(scav.BotsGroup, bot.GetPlayer);
                }
            // Betrayal hostility is published by the deferred departure callback,
            // after BossPlayers has removed every follower-ownership alias.
            if (!TrustBroken) MakeDepartureNeutral(companion);
            if (!HasLivingCompanion) ReleaseBorrowedNeutrality();
        }
    }
}

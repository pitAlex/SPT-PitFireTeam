using Comfort.Common;
using EFT;
using pitTeam.Components;
using pitTeam.Utils;
using System.Collections.Generic;

namespace pitTeam.Modules
{
    internal static class FactionHostility
    {
        private enum Faction
        {
            None,
            Bear,
            Usec,
            Scav,
            Cultist,
            Raider,
            Rogue,
        }

        private static readonly HashSet<WildSpawnType> OrdinaryScavRoles = new HashSet<WildSpawnType>
        {
            WildSpawnType.assault,
            WildSpawnType.marksman,
            WildSpawnType.cursedAssault,
            WildSpawnType.assaultGroup,
            WildSpawnType.crazyAssaultEvent,
        };

        internal static void Apply(BotOwner bot)
        {
            if (pitFireTeam.factionHostilities?.Value != true ||
                pitFireTeam.IsSeparateHostilityInstalled ||
                bot == null ||
                bot.IsDead ||
                bot.GetPlayer == null ||
                bot.BotsGroup == null)
            {
                return;
            }

            Faction botFaction = GetFaction(bot.GetPlayer);
            if (botFaction == Faction.None)
            {
                return;
            }

            GameWorld gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld?.AllAlivePlayersList == null)
            {
                return;
            }

            var players = new List<Player>(gameWorld.AllAlivePlayersList);
            var reciprocalHostileGroups = new HashSet<BotsGroup>();
            var reciprocalNeutralGroups = new HashSet<BotsGroup>();

            foreach (Player other in players)
            {
                if (other == null ||
                    other == bot.GetPlayer ||
                    other.HealthController == null ||
                    !other.HealthController.IsAlive ||
                    BossPlayers.IsFollowerProfileId(other.ProfileId))
                {
                    continue;
                }

                Faction otherFaction = GetFaction(other);
                bool shouldBeNeutral = ShouldBeNeutral(botFaction, otherFaction);
                bool shouldBeHostile = ShouldBeHostile(botFaction, otherFaction);
                if (!shouldBeNeutral && !shouldBeHostile)
                {
                    continue;
                }

                BotsGroup otherGroup = other.AIData?.BotOwner?.BotsGroup;
                if (otherGroup == bot.BotsGroup)
                {
                    continue;
                }

                if (shouldBeNeutral)
                {
                    if (ShouldPreservePlayerScavKarmaHostility(other))
                    {
                        continue;
                    }

                    EnsureNeutral(bot.BotsGroup, other);
                    ConfigureWarningIfOwned(bot.BotsGroup, botFaction, otherFaction, other.Profile.Info.Settings.Role);

                    if (otherGroup != null && reciprocalNeutralGroups.Add(otherGroup))
                    {
                        EnsureNeutral(otherGroup, bot.GetPlayer);
                        ConfigureWarningIfOwned(otherGroup, otherFaction, botFaction, bot.Profile.Info.Settings.Role);
                    }

                    continue;
                }

                if (!EnsureEnemy(bot.BotsGroup, other))
                {
                    continue;
                }

                if (otherGroup != null && reciprocalHostileGroups.Add(otherGroup))
                {
                    EnsureEnemy(otherGroup, bot.GetPlayer);
                }
            }
        }

        private static Faction GetFaction(IPlayer player)
        {
            if (player?.Profile?.Info == null)
            {
                return Faction.None;
            }

            EPlayerSide side = player.Profile.Info.Side;
            WildSpawnType? role = player.Profile.Info.Settings?.Role;

            if (player.IsAI && role.HasValue)
            {
                if (role.Value.IsSectant())
                {
                    return Faction.Cultist;
                }

                if (role.Value == WildSpawnType.pmcBot)
                {
                    return Faction.Raider;
                }

                if (role.Value.IsExUsec())
                {
                    return Faction.Rogue;
                }
            }

            if (side == EPlayerSide.Bear && (!player.IsAI || role == WildSpawnType.pmcBEAR))
            {
                return Faction.Bear;
            }

            if (side == EPlayerSide.Usec && (!player.IsAI || role == WildSpawnType.pmcUSEC))
            {
                return Faction.Usec;
            }

            if (side == EPlayerSide.Savage && role.HasValue && IsScavFactionRole(role.Value))
            {
                return Faction.Scav;
            }

            return Faction.None;
        }

        internal static bool IsScavFaction(IPlayer player)
        {
            return GetFaction(player) == Faction.Scav;
        }

        private static bool IsScavFactionRole(WildSpawnType role)
        {
            if (OrdinaryScavRoles.Contains(role))
            {
                return true;
            }

            // EFT classifies Lighthouse Rogues and the Goons together through IsExUsec(),
            // but Partisan has dedicated karma/zone/proximity hostility logic under his own
            // boss role. Keep all of them, plus existing non-combat/quest-protected roles,
            // outside the Scav faction hostility policy.
            if (role == WildSpawnType.bossPartisan ||
                role.IsExUsec() ||
                role.IsSectant() ||
                Props.friendlyBotTypes.Contains(role))
            {
                return false;
            }

            return role.IsBossOrFollower();
        }

        private static bool ShouldBeHostile(Faction left, Faction right)
        {
            if (left == Faction.None || right == Faction.None || left == right)
            {
                return false;
            }

            bool opposingPmcs =
                (left == Faction.Bear && right == Faction.Usec) ||
                (left == Faction.Usec && right == Faction.Bear);

            bool pmcAgainstScav =
                (left == Faction.Scav && (right == Faction.Bear || right == Faction.Usec)) ||
                (right == Faction.Scav && (left == Faction.Bear || left == Faction.Usec));

            return opposingPmcs || pmcAgainstScav;
        }

        private static bool ShouldBeNeutral(Faction left, Faction right)
        {
            return (left == Faction.Scav && IsScavWarningFaction(right)) ||
                   (right == Faction.Scav && IsScavWarningFaction(left));
        }

        private static bool IsScavWarningFaction(Faction faction)
        {
            return faction == Faction.Cultist ||
                   faction == Faction.Raider ||
                   faction == Faction.Rogue;
        }

        private static bool ShouldPreservePlayerScavKarmaHostility(IPlayer player)
        {
            return player != null &&
                   !player.IsAI &&
                   player.Profile?.Info?.Side == EPlayerSide.Savage &&
                   player.Loyalty != null &&
                   (player.Loyalty.HostileScavs || player.Loyalty.CanBeFreeKilled);
        }

        private static void ConfigureWarningIfOwned(
            BotsGroup group,
            Faction ownerFaction,
            Faction targetFaction,
            WildSpawnType targetRole)
        {
            bool ownsWarning = targetFaction == Faction.Scav &&
                               IsScavWarningFaction(ownerFaction);

            if (!ownsWarning || group == null)
            {
                return;
            }

            for (int i = 0; i < group.MembersCount; i++)
            {
                BotOwner member = group.Member(i);
                if (member?.Settings?.FileSettings?.Mind == null || member.Settings.FileSettings.Boss == null)
                {
                    continue;
                }

                List<WildSpawnType> warnTypes = member.Settings.GetWarnBotTypes();
                if (!warnTypes.Contains(targetRole))
                {
                    warnTypes.Add(targetRole);
                }

                var fileWarnTypes = new List<WildSpawnType>(
                    member.Settings.FileSettings.Mind.WARN_BOT_TYPES ?? new WildSpawnType[0]);
                if (!fileWarnTypes.Contains(targetRole))
                {
                    fileWarnTypes.Add(targetRole);
                    member.Settings.FileSettings.Mind.WARN_BOT_TYPES = fileWarnTypes.ToArray();
                }

                member.Settings.FileSettings.Mind.DEFAULT_SAVAGE_BEHAVIOUR = EWarnBehaviour.Warn;
                member.Settings.FileSettings.Boss.SHALL_WARN = true;
            }
        }

        private static void EnsureNeutral(BotsGroup group, IPlayer neutral)
        {
            if (group == null || neutral == null)
            {
                return;
            }

            if (group.Enemies?.ContainsKey(neutral) == true)
            {
                group.RemoveEnemy(neutral);
            }

            if (!string.IsNullOrEmpty(neutral.GroupId))
            {
                group._enemyPlayerGroups?.Remove(neutral.GroupId);
            }

            if (group.Neutrals?.ContainsKey(neutral) != true)
            {
                group.AddNeutral(neutral);
            }
        }

        private static bool EnsureEnemy(BotsGroup group, IPlayer enemy)
        {
            if (group == null || enemy == null)
            {
                return false;
            }

            if (group.Enemies?.ContainsKey(enemy) == true)
            {
                return true;
            }

            bool added = group.AddEnemy(enemy, EBotEnemyCause.initial);
            return added || group.Enemies?.ContainsKey(enemy) == true;
        }
    }
}

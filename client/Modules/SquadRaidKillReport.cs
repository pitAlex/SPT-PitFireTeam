using EFT;
using System;
using System.Collections.Generic;

namespace pitTeam.Modules
{
    internal sealed class SquadRaidKillGroup
    {
        internal SquadRaidKillGroup(string teammateNickname, IReadOnlyList<VictimStats> victims)
        {
            TeammateNickname = teammateNickname;
            Victims = victims;
        }

        internal string TeammateNickname { get; }
        internal IReadOnlyList<VictimStats> Victims { get; }
    }

    internal static class SquadRaidKillReport
    {
        private sealed class MutableKillGroup
        {
            internal MutableKillGroup(string nickname)
            {
                Nickname = nickname;
            }

            internal string Nickname { get; set; }
            internal List<VictimStats> Victims { get; } = new List<VictimStats>();
            internal HashSet<string> VictimKeys { get; } = new HashSet<string>(StringComparer.Ordinal);
        }

        private static readonly object SyncRoot = new object();
        private static readonly List<MutableKillGroup> OrderedGroups = new List<MutableKillGroup>();
        private static readonly Dictionary<string, MutableKillGroup> GroupsByProfileId =
            new Dictionary<string, MutableKillGroup>(StringComparer.Ordinal);
        private static string _playerNickname = string.Empty;

        internal static void BeginRaid(bool isTransitContinuation)
        {
            if (isTransitContinuation)
            {
                return;
            }

            lock (SyncRoot)
            {
                _playerNickname = string.Empty;
                OrderedGroups.Clear();
                GroupsByProfileId.Clear();
            }
        }

        internal static void SetPlayerNickname(string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname))
            {
                return;
            }

            lock (SyncRoot)
            {
                _playerNickname = nickname;
            }
        }

        internal static string GetPlayerNickname()
        {
            lock (SyncRoot)
            {
                return _playerNickname;
            }
        }

        internal static void RegisterTeammate(string profileId, string nickname)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return;
            }

            lock (SyncRoot)
            {
                GetOrCreateGroup(profileId, nickname);
            }
        }

        internal static void RecordTeammateKill(Player teammate, Player victim)
        {
            if (teammate?.Profile?.EftStats?.Victims == null ||
                victim == null ||
                string.IsNullOrWhiteSpace(teammate.ProfileId))
            {
                return;
            }

            VictimStats recordedVictim = FindRecordedVictim(teammate, victim.ProfileId);
            if (recordedVictim == null)
            {
                pitFireTeam.Log.LogWarning(
                    $"[RaidKillReport] EFT did not expose the recorded victim for teammate {teammate.Profile?.Nickname ?? teammate.ProfileId}; skipping the result-row copy.");
                return;
            }

            VictimStats snapshot = CloneVictim(recordedVictim);
            string victimKey = BuildVictimKey(snapshot);

            lock (SyncRoot)
            {
                MutableKillGroup group = GetOrCreateGroup(teammate.ProfileId, teammate.Profile.Nickname);
                if (group.VictimKeys.Add(victimKey))
                {
                    group.Victims.Add(snapshot);
                }
            }
        }

        internal static IReadOnlyList<SquadRaidKillGroup> CreateResultSnapshot()
        {
            lock (SyncRoot)
            {
                List<SquadRaidKillGroup> result = new List<SquadRaidKillGroup>();
                foreach (MutableKillGroup group in OrderedGroups)
                {
                    if (group.Victims.Count == 0)
                    {
                        continue;
                    }

                    List<VictimStats> victims = new List<VictimStats>(group.Victims.Count);
                    foreach (VictimStats victim in group.Victims)
                    {
                        victims.Add(CloneVictim(victim));
                    }

                    result.Add(new SquadRaidKillGroup(group.Nickname, victims));
                }

                return result;
            }
        }

        private static MutableKillGroup GetOrCreateGroup(string profileId, string nickname)
        {
            if (GroupsByProfileId.TryGetValue(profileId, out MutableKillGroup group))
            {
                if (!string.IsNullOrWhiteSpace(nickname))
                {
                    group.Nickname = nickname;
                }

                return group;
            }

            group = new MutableKillGroup(nickname ?? string.Empty);
            GroupsByProfileId.Add(profileId, group);
            OrderedGroups.Add(group);
            return group;
        }

        private static VictimStats FindRecordedVictim(Player teammate, string victimProfileId)
        {
            var victims = teammate.Profile.EftStats.Victims;
            for (int index = victims.Count - 1; index >= 0; index--)
            {
                VictimStats candidate = victims[index];
                if (candidate != null &&
                    string.Equals(candidate.ProfileId, victimProfileId, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string BuildVictimKey(VictimStats victim)
        {
            if (!string.IsNullOrWhiteSpace(victim.ProfileId))
            {
                return "profile:" + victim.ProfileId;
            }

            if (!string.IsNullOrWhiteSpace(victim.AccountId))
            {
                return "account:" + victim.AccountId;
            }

            return $"fallback:{victim.Name}:{victim.Time.Ticks}:{victim.BodyPart}:{victim.Weapon}";
        }

        private static VictimStats CloneVictim(VictimStats source)
        {
            return new VictimStats
            {
                AccountId = source.AccountId,
                ProfileId = source.ProfileId,
                Name = source.Name,
                Side = source.Side,
                Time = source.Time,
                Level = source.Level,
                PrestigeLevel = source.PrestigeLevel,
                BodyPart = source.BodyPart,
                Weapon = source.Weapon,
                Distance = source.Distance,
                Role = source.Role,
                Location = source.Location
            };
        }
    }
}

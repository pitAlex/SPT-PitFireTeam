using EFT;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.Modules
{
    // HP-only wire snapshot. SpawnMaximum is the original raid maximum, so surgery's
    // reduced maximum cannot turn a partly restored limb into free post-raid healing.
    internal sealed class FollowerRaidBodyPartHealth
    {
        public float Current { get; set; }
        public float SpawnMaximum { get; set; }
    }

    internal sealed class FollowerRaidHealth
    {
        public Dictionary<string, FollowerRaidBodyPartHealth> BodyParts { get; set; }
    }

    internal static class FollowerRaidHealthRetention
    {
        private static readonly HashSet<string> SavedProfiles = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Dictionary<EBodyPart, float>> SpawnMaximums = new Dictionary<string, Dictionary<EBodyPart, float>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, FollowerRaidHealth> Fallen = new Dictionary<string, FollowerRaidHealth>(StringComparer.Ordinal);

        internal static void RegisterSavedProfile(Profile profile)
        {
            if (profile != null && !string.IsNullOrWhiteSpace(profile.Id))
                lock (SavedProfiles) SavedProfiles.Add(profile.Id);
        }

        internal static bool Begin(BotOwner bot)
        {
            if (bot?.ProfileId == null) return false;
            lock (SavedProfiles) if (!SavedProfiles.Contains(bot.ProfileId)) return false;
            if (!SpawnMaximums.ContainsKey(bot.ProfileId) && bot.GetPlayer?.ActiveHealthController != null)
            {
                SpawnMaximums[bot.ProfileId] = EFT.HealthSystem.HealthHelper.RealBodyParts.ToDictionary(part => part,
                    part => bot.GetPlayer.ActiveHealthController.GetBodyPartHealth(part, false).Maximum);
                var total = bot.GetPlayer.ActiveHealthController.GetBodyPartHealth(EBodyPart.Common, false);
                Logger.LogInfo($"[TeammateHealth] Retained saved spawn HP: aid={bot.Profile?.AccountId} hp={total.Current:0.##}/{total.Maximum:0.##}");
            }
            return true;
        }

        internal static FollowerRaidHealth Capture(Player player)
        {
            if (player?.ActiveHealthController == null) return null;
            try
            {
                SpawnMaximums.TryGetValue(player.ProfileId, out var original);
                return new FollowerRaidHealth
                {
                    BodyParts = EFT.HealthSystem.HealthHelper.RealBodyParts.ToDictionary(part => part.ToString(), part =>
                    {
                        var value = player.ActiveHealthController.GetBodyPartHealth(part, false);
                        return new FollowerRaidBodyPartHealth
                        {
                            Current = value.Current,
                            SpawnMaximum = original != null && original.TryGetValue(part, out float maximum) ? maximum : value.Maximum
                        };
                    })
                };
            }
            catch (Exception ex)
            {
                Logger.LogError($"[TeammateHealth] Could not capture HP for '{player.ProfileId}': {ex}");
                return null;
            }
        }

        internal static void RecordFallen(Player player)
        {
            var health = Capture(player);
            if (health == null) return;
            if (!string.IsNullOrWhiteSpace(player.Profile?.AccountId)) Fallen[player.Profile.AccountId] = health;
            if (!string.IsNullOrWhiteSpace(player.ProfileId)) Fallen[player.ProfileId] = health;
        }

        internal static FollowerRaidHealth GetFallen(string aid, string profileId)
        {
            if (aid != null && Fallen.TryGetValue(aid, out var health)) return health;
            return profileId != null && Fallen.TryGetValue(profileId, out health) ? health : null;
        }

        // Transit continues the same raid and keeps the original maxima/effects. Clear only
        // after final extraction/teardown, after immutable outcome payloads were captured.
        internal static void Clear()
        {
            lock (SavedProfiles) SavedProfiles.Clear();
            SpawnMaximums.Clear();
            Fallen.Clear();
        }
    }
}

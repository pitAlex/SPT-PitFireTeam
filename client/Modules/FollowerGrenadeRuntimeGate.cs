using EFT;
using System.Collections.Generic;
using UnityEngine;

namespace pitTeam.Modules
{
    internal static class FollowerGrenadeRuntimeGate
    {
        private const float BlockedRetryCooldownSeconds = 7f;

        private static readonly HashSet<string> AlwaysDisabledByProfileId = new();
        private static readonly HashSet<string> ExplicitThrowEnabledByProfileId = new();
        private static readonly Dictionary<string, string> ExplicitThrowOwnerByGroupKey = new();
        private static readonly Dictionary<string, float> BlockedRetryUntilByProfileId = new();
        private static readonly HashSet<string> ReleasedThrowByProfileId = new();

        public static void EnforceDisabled(BotOwner bot)
        {
            if (bot == null)
            {
                return;
            }

            string? groupKey = GetGroupKey(bot);
            if (!string.IsNullOrEmpty(groupKey) &&
                ExplicitThrowOwnerByGroupKey.TryGetValue(groupKey, out string ownerProfileId) &&
                ownerProfileId == bot.ProfileId)
            {
                ExplicitThrowOwnerByGroupKey.Remove(groupKey);
            }

            SetThrowState(bot, enabled: false, disabled: true);
            RefreshFollowerGroup(bot);
        }

        public static void EnableExplicitThrow(BotOwner bot)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return;
            }

            if (!pitFireTeam.botGrenades.Value)
            {
                EnforceDisabled(bot);
                return;
            }

            string? groupKey = GetGroupKey(bot);
            if (string.IsNullOrEmpty(groupKey))
            {
                BlockedRetryUntilByProfileId.Remove(bot.ProfileId);
                SetThrowState(bot, enabled: true, disabled: false);
                return;
            }

            if (!ExplicitThrowOwnerByGroupKey.TryGetValue(groupKey, out string ownerProfileId) ||
                string.IsNullOrEmpty(ownerProfileId) ||
                ownerProfileId == bot.ProfileId)
            {
                ExplicitThrowOwnerByGroupKey[groupKey] = bot.ProfileId;
            }

            bool ownsThrowWindow = ExplicitThrowOwnerByGroupKey.TryGetValue(groupKey, out ownerProfileId) &&
                                   ownerProfileId == bot.ProfileId;
            if (ownsThrowWindow)
            {
                BlockedRetryUntilByProfileId.Remove(bot.ProfileId);
            }

            SetThrowState(bot, enabled: ownsThrowWindow, disabled: !ownsThrowWindow);
            RefreshFollowerGroup(bot);
        }

        public static void FinishExplicitThrow(BotOwner bot, bool completed)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return;
            }

            ReleasedThrowByProfileId.Remove(bot.ProfileId);

            string? groupKey = GetGroupKey(bot);
            if (!string.IsNullOrEmpty(groupKey) &&
                ExplicitThrowOwnerByGroupKey.TryGetValue(groupKey, out string ownerProfileId) &&
                ownerProfileId == bot.ProfileId)
            {
                ExplicitThrowOwnerByGroupKey.Remove(groupKey);
            }

            SetThrowState(bot, enabled: false, disabled: true);
            // MarkThrowReleased already starts cooldowns at physical creation.
            // The later animation callback must not restart those timers.
            if (!completed)
            {
                FollowerGrenadeCooldowns.CancelPending(bot);
            }

            RefreshFollowerGroup(bot);
        }

        public static bool MarkThrowReleased(BotOwner bot)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return false;
            }

            bool firstRelease = ReleasedThrowByProfileId.Add(bot.ProfileId);
            if (firstRelease)
            {
                FollowerGrenadeCooldowns.RecordThrow(bot);
            }

            return firstRelease;
        }

        public static bool ConsumeThrowReleased(BotOwner bot)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return false;
            }

            return ReleasedThrowByProfileId.Remove(bot.ProfileId);
        }

        public static bool HasReleasedThrow(BotOwner bot)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return false;
            }

            return ReleasedThrowByProfileId.Contains(bot.ProfileId);
        }

        public static bool ShouldBlockThrowAttempt(BotOwner bot, out string reason)
        {
            reason = string.Empty;
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return false;
            }

            float now = Time.time;
            if (BlockedRetryUntilByProfileId.TryGetValue(bot.ProfileId, out float retryUntil))
            {
                if (now < retryUntil)
                {
                    reason = $"runtimeGateCooldown:{Mathf.CeilToInt(retryUntil - now)}s";
                    return true;
                }

                BlockedRetryUntilByProfileId.Remove(bot.ProfileId);
            }

            string? groupKey = GetGroupKey(bot);
            if (string.IsNullOrEmpty(groupKey) ||
                !ExplicitThrowOwnerByGroupKey.TryGetValue(groupKey, out string ownerProfileId) ||
                string.IsNullOrEmpty(ownerProfileId) ||
                ownerProfileId == bot.ProfileId)
            {
                return false;
            }

            BlockedRetryUntilByProfileId[bot.ProfileId] = now + BlockedRetryCooldownSeconds;
            reason = "runtimeGateBlocked";
            return true;
        }

        public static bool IsThrowAllowed(BotOwner bot)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return false;
            }

            if (!pitFireTeam.botGrenades.Value)
            {
                return false;
            }

            string? groupKey = GetGroupKey(bot);
            if (!string.IsNullOrEmpty(groupKey) &&
                ExplicitThrowOwnerByGroupKey.TryGetValue(groupKey, out string ownerProfileId) &&
                ownerProfileId != bot.ProfileId)
            {
                return false;
            }

            if (AlwaysDisabledByProfileId.Contains(bot.ProfileId))
            {
                return false;
            }

            return ExplicitThrowEnabledByProfileId.Contains(bot.ProfileId);
        }

        public static void ClearAll()
        {
            AlwaysDisabledByProfileId.Clear();
            ExplicitThrowEnabledByProfileId.Clear();
            ExplicitThrowOwnerByGroupKey.Clear();
            BlockedRetryUntilByProfileId.Clear();
            ReleasedThrowByProfileId.Clear();
        }

        private static void SetThrowState(BotOwner bot, bool enabled, bool disabled)
        {
            SetAlwaysDisabled(bot, disabled);
            SetExplicitThrowEnabled(bot, enabled);
        }

        private static void SetAlwaysDisabled(BotOwner bot, bool disabled)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return;
            }

            if (disabled)
            {
                AlwaysDisabledByProfileId.Add(bot.ProfileId);
            }
            else
            {
                AlwaysDisabledByProfileId.Remove(bot.ProfileId);
            }

            RefreshCoreCanGrenade(bot);
        }

        private static void SetExplicitThrowEnabled(BotOwner bot, bool enabled)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return;
            }

            if (enabled)
            {
                ExplicitThrowEnabledByProfileId.Add(bot.ProfileId);
            }
            else
            {
                ExplicitThrowEnabledByProfileId.Remove(bot.ProfileId);
            }

            RefreshCoreCanGrenade(bot);
        }

        private static void RefreshCoreCanGrenade(BotOwner bot)
        {
            if (bot?.Settings?.FileSettings?.Core == null)
            {
                return;
            }

            bot.Settings.FileSettings.Core.CanGrenade = IsThrowAllowed(bot);
        }

        private static string? GetGroupKey(BotOwner bot)
        {
            string bossProfileId = bot?.BotFollower?.BossToFollow?.Player()?.ProfileId;
            if (!string.IsNullOrEmpty(bossProfileId))
            {
                return $"boss:{bossProfileId}";
            }

            if (bot?.BotsGroup != null)
            {
                return $"group:{bot.BotsGroup.Id}";
            }

            return null;
        }

        private static void RefreshFollowerGroup(BotOwner bot)
        {
            IBossToFollow boss = bot?.BotFollower?.BossToFollow;
            if (boss?.Followers == null)
            {
                RefreshCoreCanGrenade(bot);
                return;
            }

            var followers = boss.Followers;
            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i];
                if (follower == null)
                {
                    continue;
                }

                RefreshCoreCanGrenade(follower);
            }
        }

    }
}

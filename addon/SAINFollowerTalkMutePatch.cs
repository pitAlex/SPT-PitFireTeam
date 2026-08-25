using EFT;
using HarmonyLib;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.Classes.Talk;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Patches;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace pitTeam.SAINAddon
{
    internal static class SAINFollowerTalkMutePatch
    {
        private const bool EnableFollowerTalkMuteDebugLogs = false;
        private const float LogThrottleSeconds = 2f;
        private static readonly HashSet<EPhraseTrigger> MutedFollowerTriggers = new HashSet<EPhraseTrigger>
        {
            EPhraseTrigger.LostVisual,
            EPhraseTrigger.OnLostVisual,
            EPhraseTrigger.Clear
        };

        private static readonly Dictionary<string, float> NextLogAtByKey = new Dictionary<string, float>(64, StringComparer.Ordinal);
        private static readonly FieldInfo? EnemyKnownPlacesEnemyField =
            AccessTools.Field(typeof(EnemyKnownPlaces), "Enemy");
        private static readonly Type? PlayerComponentType = Type.GetType("SAIN.Components.PlayerComponentSpace.PlayerComponent, SAIN");
        private static readonly PropertyInfo? PlayerComponentPlayerProperty = PlayerComponentType?.GetProperty("Player");

        public static void Apply(Harmony harmony)
        {
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(GroupTalk), "CheckEnemyContact", new[] { typeof(Enemy) }),
                nameof(Prefix_CheckEnemyContact),
                "[Init] Failed to find SAIN GroupTalk.CheckEnemyContact for follower talk mute patch.");

            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(GroupTalk), "ShallReportLostVisual", new[] { typeof(Enemy) }),
                nameof(Prefix_ShallReportLostVisual),
                "[Init] Failed to find SAIN GroupTalk.ShallReportLostVisual for follower talk mute patch.");

            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(EnemyKnownPlaces), "tryTalk", Type.EmptyTypes),
                nameof(Prefix_EnemyKnownPlacesTryTalk),
                "[Init] Failed to find SAIN EnemyKnownPlaces.tryTalk for follower talk mute patch.");

            PatchPrefix(
                harmony,
                PlayerComponentType != null
                    ? AccessTools.Method(PlayerComponentType, "PlayVoiceLine", new[] { typeof(EPhraseTrigger), typeof(ETagStatus), typeof(bool) })
                    : null,
                nameof(Prefix_PlayVoiceLine),
                "[Init] Failed to find SAIN PlayerComponent.PlayVoiceLine for follower talk mute patch.");

            Modules.Logger.LogInfo("[Init] SAIN follower contact/lost-visual talk mute patch applied.");
        }

        private static void PatchPrefix(Harmony harmony, MethodInfo? target, string prefixName, string errorMessage)
        {
            if (target == null)
            {
                Modules.Logger.LogError(errorMessage);
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(SAINFollowerTalkMutePatch), prefixName));
        }

        private static bool Prefix_CheckEnemyContact(GroupTalk __instance, Enemy enemy, ref bool __result)
        {
            if (!TryGetFollowerOwner(__instance?.BotOwner, out BotOwner? owner))
            {
                return true;
            }

            return true;
        }

        private static bool Prefix_ShallReportLostVisual(GroupTalk __instance, Enemy enemy, ref bool __result)
        {
            if (!TryGetFollowerOwner(__instance?.BotOwner, out BotOwner? owner))
            {
                return true;
            }

            __result = false;
            LogBlockedPhrase(owner!, enemy, "GroupTalk.ShallReportLostVisual", "LostVisual/Rat");
            return false;
        }

        private static bool Prefix_EnemyKnownPlacesTryTalk(EnemyKnownPlaces __instance)
        {
            Enemy? enemy = EnemyKnownPlacesEnemyField?.GetValue(__instance) as Enemy;
            if (!TryGetFollowerOwner(enemy, out BotOwner? owner))
            {
                return true;
            }

            LogBlockedPhrase(owner!, enemy, "EnemyKnownPlaces.tryTalk", "Clear/LostVisual");
            return false;
        }

        private static bool Prefix_PlayVoiceLine(object __instance, EPhraseTrigger phrase, ETagStatus mask, bool aggressive, ref bool __result)
        {
            Player? player = PlayerComponentPlayerProperty?.GetValue(__instance) as Player;
            if (!TryGetFollowerOwner(player?.AIData?.BotOwner, out BotOwner? owner))
            {
                return true;
            }

            // Preserve the core squad-linger announcement contract if the addon is enabled later.
            // SAIN's automatic Clear/LostVisual generation remains muted everywhere else.
            if (FollowerPostCombatClearPhraseGate.IsAllowed(owner!, phrase))
            {
                return true;
            }

            if (FollowerContactPhraseGate.IsContactPhrase(phrase) && !FollowerContactPhraseGate.ShouldAllow(owner!))
            {
                __result = false;
                LogBlockedPhrase(
                    owner!,
                    enemy: null,
                    "PlayerComponent.PlayVoiceLine",
                    phrase.ToString(),
                    $"reason=contactGate mask={mask} aggressive={aggressive}");
                return false;
            }

            if (FollowerTalkFrequencyGate.ShouldBlockCombatTalk(owner!, phrase))
            {
                __result = false;
                LogBlockedPhrase(
                    owner!,
                    enemy: null,
                    "PlayerComponent.PlayVoiceLine",
                    phrase.ToString(),
                    $"reason=botTalkThrottle value={pitFireTeam.botTalk.Value} mask={mask} aggressive={aggressive}");
                return false;
            }

            if (!MutedFollowerTriggers.Contains(phrase))
            {
                return true;
            }

            __result = false;
            LogBlockedPhrase(
                owner!,
                enemy: null,
                "PlayerComponent.PlayVoiceLine",
                phrase.ToString(),
                $"mask={mask} aggressive={aggressive}");
            return false;
        }

        private static bool TryGetFollowerOwner(Enemy? enemy, out BotOwner? owner)
        {
            owner = enemy?.Bot?.BotOwner;
            return TryGetFollowerOwner(owner, out owner);
        }

        private static bool TryGetFollowerOwner(BotOwner? owner, out BotOwner? followerOwner)
        {
            followerOwner = owner;
            return followerOwner != null && BossPlayers.IsFollower(followerOwner);
        }

        private static void LogBlockedPhrase(BotOwner owner, Enemy? enemy, string source, string phraseSummary, string? extra = null)
        {
            if (!EnableFollowerTalkMuteDebugLogs || owner == null)
            {
                return;
            }

            string profileId = owner.ProfileId ?? "<null>";
            string key = profileId + "|" + source + "|" + phraseSummary;
            float now = Time.time;
            if (NextLogAtByKey.TryGetValue(key, out float nextLogAt) && nextLogAt > now)
            {
                return;
            }

            NextLogAtByKey[key] = now + LogThrottleSeconds;

            string followerName = owner.Profile?.Nickname ?? owner.name ?? "<unknown>";
            string memoryGoalEnemy = owner.Memory?.GoalEnemy?.ProfileId ?? "<null>";
            bool memoryHaveEnemy = owner.Memory?.HaveEnemy == true;
            bool memoryGoalVisible = owner.Memory?.GoalEnemy?.IsVisible == true;
            string enemyProfileId = enemy?.EnemyPlayer?.ProfileId ?? "<null>";
            bool enemySeen = enemy?.Seen == true;
            bool enemyVisible = enemy?.IsVisible == true;
            float timeSinceSeen = enemy != null ? enemy.TimeSinceSeen : -1f;
            string seenText = enemy != null ? timeSinceSeen.ToString("0.00") : "n/a";
            string suffix = string.IsNullOrEmpty(extra) ? string.Empty : " " + extra;

            Modules.Logger.LogInfo(
                $"[SAINTalkMute] source={source} phrase={phraseSummary} follower={followerName}[{profileId}] " +
                $"memoryHaveEnemy={memoryHaveEnemy} memoryGoalEnemy={memoryGoalEnemy} memoryGoalVisible={memoryGoalVisible} " +
                $"sainEnemy={enemyProfileId} enemySeen={enemySeen} enemyVisible={enemyVisible} timeSinceSeen={seenText}{suffix}");
        }
    }
}

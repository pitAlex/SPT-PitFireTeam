using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using SAIN;
using SAIN.Components;
using SAIN.Models.Enums;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;
using HarmonyLib;

namespace pitTeam.SAINAddon
{
    internal static class SAINFollowerRuntimeBridge
    {
        private const float SoloCombatReleaseGraceSeconds = 1.5f;
        private const float StaleSearchReleaseSeconds = 3f;
        private const float StaleSeekCoverReleaseSeconds = 3f;
        private const float StaleRetreatReleaseSeconds = 4f;
        private const float StaleShiftCoverReleaseSeconds = 3.5f;
        private const float StaleSoloLayerNoDecisionReleaseSeconds = 2.5f;
        private const float CooldownCrouchPose = 0.1f;
        private const float CooldownCrouchSetIntervalSeconds = 0.3f;
        private static readonly Dictionary<string, float> LastSoloCombatSeenAtByBot = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private static readonly Dictionary<string, float> StaleDecisionStartedAtByBot = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private static readonly Dictionary<string, ECombatDecision> StaleDecisionTypeByBot = new Dictionary<string, ECombatDecision>(System.StringComparer.Ordinal);
        private static readonly Dictionary<string, float> SoloLayerNoDecisionStartedAtByBot = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private static readonly Dictionary<string, float> NextCooldownCrouchSetAtByBot = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private static readonly Dictionary<string, Dictionary<string, BotOwner>> SearchPartyLeaderByBossAndEnemy =
            new Dictionary<string, Dictionary<string, BotOwner>>(System.StringComparer.Ordinal);
        private static readonly Dictionary<string, Dictionary<string, HashSet<string>>> SearchPartyLeaderLocksByBossAndEnemy =
            new Dictionary<string, Dictionary<string, HashSet<string>>>(System.StringComparer.Ordinal);
        private static readonly FieldInfo TimeLastKnownUpdatedField = typeof(SAIN.SAINComponent.Classes.EnemyClasses.EnemyKnownPlaces)
            .GetField("<TimeLastKnownUpdated>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo SetGoalEnemyMethod =
            AccessTools.Method(typeof(SAINEnemyController), "setGoalEnemy", new[] { typeof(EnemyInfo) });
        private static readonly MethodInfo GoalEnemySetter =
            AccessTools.PropertySetter(typeof(SAINEnemyController), "GoalEnemy");

        public static void OnBossGroupStaticUpdate(pitAIBossPlayer boss)
        {
            if (boss?.realPlayer == null || string.IsNullOrEmpty(boss.realPlayer.ProfileId))
            {
                return;
            }

            string bossProfileId = boss.realPlayer.ProfileId;
            if (!boss.realPlayer.HealthController.IsAlive)
            {
                SearchPartyLeaderByBossAndEnemy.Remove(bossProfileId);
                SearchPartyLeaderLocksByBossAndEnemy.Remove(bossProfileId);
                return;
            }

            try
            {
                Dictionary<string, (BotOwner leader, float sqrDistance)> bestLeaderByEnemy =
                    new Dictionary<string, (BotOwner leader, float sqrDistance)>(System.StringComparer.Ordinal);
                HashSet<string> activeEnemyIds = new HashSet<string>(System.StringComparer.Ordinal);
                HashSet<string> activeFollowerIds = new HashSet<string>(System.StringComparer.Ordinal);

                foreach (BotFollowerPlayer followerData in BossPlayers.GetFollowersByBoss(bossProfileId))
                {
                    BotOwner owner = followerData?.GetBot();
                    if (owner == null || owner.IsDead)
                    {
                        continue;
                    }

                    activeFollowerIds.Add(owner.ProfileId);

                    if (!TryGetFollowerEnemyForGroupSearch(owner, out Player enemyPlayer, out string enemyProfileId))
                    {
                        continue;
                    }

                    if (enemyPlayer == null || string.IsNullOrEmpty(enemyProfileId))
                    {
                        continue;
                    }

                    activeEnemyIds.Add(enemyProfileId);

                    float sqrDistance = (owner.Position - enemyPlayer.Position).sqrMagnitude;
                    if (!bestLeaderByEnemy.TryGetValue(enemyProfileId, out var existing) || sqrDistance < existing.sqrDistance)
                    {
                        bestLeaderByEnemy[enemyProfileId] = (owner, sqrDistance);
                    }
                }

                if (!SearchPartyLeaderByBossAndEnemy.TryGetValue(bossProfileId, out Dictionary<string, BotOwner> leaders) || leaders == null)
                {
                    leaders = new Dictionary<string, BotOwner>(System.StringComparer.Ordinal);
                    SearchPartyLeaderByBossAndEnemy[bossProfileId] = leaders;
                }

                if (!SearchPartyLeaderLocksByBossAndEnemy.TryGetValue(bossProfileId, out Dictionary<string, HashSet<string>> locksByEnemy) || locksByEnemy == null)
                {
                    locksByEnemy = new Dictionary<string, HashSet<string>>(System.StringComparer.Ordinal);
                    SearchPartyLeaderLocksByBossAndEnemy[bossProfileId] = locksByEnemy;
                }

                foreach (string enemyId in leaders.Keys.ToList())
                {
                    bool hasLocks = false;
                    if (locksByEnemy.TryGetValue(enemyId, out HashSet<string> lockOwners) && lockOwners != null)
                    {
                        lockOwners.RemoveWhere(id => !activeFollowerIds.Contains(id));
                        hasLocks = lockOwners.Count > 0;
                    }

                    if (lockOwners != null && lockOwners.Count == 0)
                    {
                        locksByEnemy.Remove(enemyId);
                        hasLocks = false;
                    }

                    BotOwner leader = leaders[enemyId];
                    bool validLeader = leader != null &&
                                       !leader.IsDead &&
                                       activeFollowerIds.Contains(leader.ProfileId);

                    if (!validLeader)
                    {
                        leaders.Remove(enemyId);
                        if (!hasLocks)
                        {
                            locksByEnemy.Remove(enemyId);
                        }
                        continue;
                    }

                    if (!hasLocks && !activeEnemyIds.Contains(enemyId))
                    {
                        leaders.Remove(enemyId);
                        locksByEnemy.Remove(enemyId);
                    }
                }

                foreach (KeyValuePair<string, (BotOwner leader, float sqrDistance)> kvp in bestLeaderByEnemy)
                {
                    if (!leaders.ContainsKey(kvp.Key))
                    {
                        leaders[kvp.Key] = kvp.Value.leader;
                    }
                }

                if (leaders.Count == 0)
                {
                    SearchPartyLeaderByBossAndEnemy.Remove(bossProfileId);
                }

                if (locksByEnemy.Count == 0)
                {
                    SearchPartyLeaderLocksByBossAndEnemy.Remove(bossProfileId);
                }
            }
            catch (System.Exception ex)
            {
                Modules.Logger.LogError($"[SAIN] Boss group static update failed for {boss.realPlayer.Profile?.Nickname ?? boss.realPlayer.name}");
                Modules.Logger.LogError(ex);
            }
        }

        public static bool HasSearchPartyLeader(pitAIBossPlayer boss, string enemyProfileId)
        {
            return TryGetSearchPartyLeader(boss, enemyProfileId, out _);
        }

        public static bool TryLockSearchPartyLeader(pitAIBossPlayer boss, string enemyProfileId, string followerProfileId)
        {
            if (!TryGetSearchPartyLeader(boss, enemyProfileId, out _) ||
                boss?.realPlayer == null ||
                string.IsNullOrEmpty(boss.realPlayer.ProfileId) ||
                string.IsNullOrEmpty(enemyProfileId) ||
                string.IsNullOrEmpty(followerProfileId))
            {
                return false;
            }

            string bossProfileId = boss.realPlayer.ProfileId;
            if (!SearchPartyLeaderLocksByBossAndEnemy.TryGetValue(bossProfileId, out Dictionary<string, HashSet<string>> locksByEnemy) || locksByEnemy == null)
            {
                locksByEnemy = new Dictionary<string, HashSet<string>>(System.StringComparer.Ordinal);
                SearchPartyLeaderLocksByBossAndEnemy[bossProfileId] = locksByEnemy;
            }

            if (!locksByEnemy.TryGetValue(enemyProfileId, out HashSet<string> lockOwners) || lockOwners == null)
            {
                lockOwners = new HashSet<string>(System.StringComparer.Ordinal);
                locksByEnemy[enemyProfileId] = lockOwners;
            }

            lockOwners.Add(followerProfileId);
            return true;
        }

        public static void UnlockSearchPartyLeader(pitAIBossPlayer boss, string enemyProfileId, string followerProfileId)
        {
            if (boss?.realPlayer == null ||
                string.IsNullOrEmpty(boss.realPlayer.ProfileId) ||
                string.IsNullOrEmpty(enemyProfileId) ||
                string.IsNullOrEmpty(followerProfileId))
            {
                return;
            }

            string bossProfileId = boss.realPlayer.ProfileId;
            if (!SearchPartyLeaderLocksByBossAndEnemy.TryGetValue(bossProfileId, out Dictionary<string, HashSet<string>> locksByEnemy) ||
                locksByEnemy == null ||
                !locksByEnemy.TryGetValue(enemyProfileId, out HashSet<string> lockOwners) ||
                lockOwners == null)
            {
                return;
            }

            lockOwners.Remove(followerProfileId);
            if (lockOwners.Count == 0)
            {
                locksByEnemy.Remove(enemyProfileId);

                if (SearchPartyLeaderByBossAndEnemy.TryGetValue(bossProfileId, out Dictionary<string, BotOwner> leaders) &&
                    leaders != null)
                {
                    leaders.Remove(enemyProfileId);
                    if (leaders.Count == 0)
                    {
                        SearchPartyLeaderByBossAndEnemy.Remove(bossProfileId);
                    }
                }
            }

            if (locksByEnemy.Count == 0)
            {
                SearchPartyLeaderLocksByBossAndEnemy.Remove(bossProfileId);
            }
        }

        public static bool IsSearchPartyLeader(pitAIBossPlayer boss, BotOwner bot, string enemyProfileId)
        {
            return boss != null &&
                   bot != null &&
                   !string.IsNullOrEmpty(enemyProfileId) &&
                   TryGetSearchPartyLeader(boss, enemyProfileId, out BotOwner leader) &&
                   leader != null &&
                   !leader.IsDead &&
                   leader.ProfileId == bot.ProfileId;
        }

        public static bool TryGetSearchPartyLeaderPosition(pitAIBossPlayer boss, string enemyProfileId, string? followerProfileId, out Vector3 position)
        {
            position = default;
            if (!TryGetSearchPartyLeader(boss, enemyProfileId, out BotOwner leader) || leader == null || leader.IsDead)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(followerProfileId) && string.Equals(leader.ProfileId, followerProfileId, System.StringComparison.Ordinal))
            {
                return false;
            }

            position = leader.Position;
            return true;
        }

        private static bool TryGetSearchPartyLeader(pitAIBossPlayer boss, string enemyProfileId, out BotOwner leader)
        {
            leader = null!;
            if (boss?.realPlayer == null || string.IsNullOrEmpty(boss.realPlayer.ProfileId) || string.IsNullOrEmpty(enemyProfileId))
            {
                return false;
            }

            return SearchPartyLeaderByBossAndEnemy.TryGetValue(boss.realPlayer.ProfileId, out Dictionary<string, BotOwner> leaders) &&
                   leaders != null &&
                   leaders.TryGetValue(enemyProfileId, out leader) &&
                   leader != null;
        }

        internal static void OnFollowerLifecycleEvent(BotOwner bot, FollowerLifecycleEvent eventType)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return;
            }

            if (eventType != FollowerLifecycleEvent.OnDismiss && eventType != FollowerLifecycleEvent.OnRaidEnd)
            {
                return;
            }

            RemoveFollowerFromSearchPartyCache(bot.ProfileId);
        }

        private static void RemoveFollowerFromSearchPartyCache(string followerProfileId)
        {
            foreach (string bossId in SearchPartyLeaderByBossAndEnemy.Keys.ToList())
            {
                if (SearchPartyLeaderByBossAndEnemy.TryGetValue(bossId, out Dictionary<string, BotOwner> leaders) && leaders != null)
                {
                    foreach (string enemyId in leaders.Keys.ToList())
                    {
                        BotOwner leader = leaders[enemyId];
                        if (leader != null && string.Equals(leader.ProfileId, followerProfileId, System.StringComparison.Ordinal))
                        {
                            leaders.Remove(enemyId);
                        }
                    }

                    if (leaders.Count == 0)
                    {
                        SearchPartyLeaderByBossAndEnemy.Remove(bossId);
                    }
                }

                if (SearchPartyLeaderLocksByBossAndEnemy.TryGetValue(bossId, out Dictionary<string, HashSet<string>> locksByEnemy) && locksByEnemy != null)
                {
                    foreach (string enemyId in locksByEnemy.Keys.ToList())
                    {
                        HashSet<string> owners = locksByEnemy[enemyId];
                        owners?.Remove(followerProfileId);
                        if (owners == null || owners.Count == 0)
                        {
                            locksByEnemy.Remove(enemyId);
                        }
                    }

                    if (locksByEnemy.Count == 0)
                    {
                        SearchPartyLeaderLocksByBossAndEnemy.Remove(bossId);
                    }
                }
            }
        }

        private static bool TryGetFollowerEnemyForGroupSearch(BotOwner owner, out Player enemyPlayer, out string enemyProfileId)
        {
            enemyPlayer = null!;
            enemyProfileId = string.Empty;
            if (owner == null || owner.IsDead)
            {
                return false;
            }

            if (SAINEnableClass.GetSAIN(owner.ProfileId, out BotComponent sainBot) &&
                sainBot?.GoalEnemy?.EnemyPlayer is Player sainEnemyPlayer &&
                !string.IsNullOrEmpty(sainEnemyPlayer.ProfileId))
            {
                enemyPlayer = sainEnemyPlayer;
                enemyProfileId = sainEnemyPlayer.ProfileId;
                return true;
            }

            if (owner.Memory?.GoalEnemy?.Person is Player memoryEnemyPlayer &&
                !string.IsNullOrEmpty(owner.Memory.GoalEnemy.ProfileId))
            {
                enemyPlayer = memoryEnemyPlayer;
                enemyProfileId = owner.Memory.GoalEnemy.ProfileId;
                return true;
            }

            return false;
        }

        // Core plugin calls this bridge when SAIN is installed so SAIN-specific runtime gating stays addon-owned.
        public static bool IsReadyForPatrolAfterCombat(BotOwner owner)
        {
            if (owner == null || owner.IsDead || owner.BotState != EBotState.Active)
            {
                return false;
            }

            if (!SAINEnableClass.GetSAIN(owner.ProfileId, out BotComponent bot) || bot == null)
            {
                return false;
            }

            if (SAINFollowerCombatLayer.IsFollowerCombatLayerActive(owner))
            {
                return false;
            }

            string profileId = owner.ProfileId;
            bool botInCombat = bot.BotActivation.BotInCombat;
            bool soloCombatLayerActive = bot.ActiveLayer == ESAINLayer.Combat;
            var decisions = bot.Decision;
            int knownEnemyCount = bot.EnemyController?.KnownEnemies?.Count ?? 0;
            ECombatDecision combatDecision = decisions?.CurrentCombatDecision ?? ECombatDecision.None;
            ESelfActionType selfDecision = decisions?.CurrentSelfDecision ?? ESelfActionType.None;
            bool soloSelfActionSeekCover = decisions != null
                && combatDecision == ECombatDecision.SeekCover
                && selfDecision != ESelfActionType.None;

            bool hasNoEnemyContext = owner.Memory?.HaveEnemy != true && knownEnemyCount == 0;
            bool staleDecisionCandidate = TryGetStaleReleaseTimeout(combatDecision, selfDecision, hasNoEnemyContext, out float staleTimeoutSeconds);
            bool staleSoloLayerNoDecisionCandidate =
                soloCombatLayerActive &&
                combatDecision == ECombatDecision.None &&
                selfDecision == ESelfActionType.None &&
                hasNoEnemyContext;

            if (staleDecisionCandidate)
            {
                bool hasTimer = StaleDecisionStartedAtByBot.TryGetValue(profileId, out float startedAt);
                bool sameDecision = StaleDecisionTypeByBot.TryGetValue(profileId, out ECombatDecision trackedDecision) && trackedDecision == combatDecision;
                if (!hasTimer || !sameDecision)
                {
                    StaleDecisionStartedAtByBot[profileId] = Time.time;
                    StaleDecisionTypeByBot[profileId] = combatDecision;
                }
                else if (Time.time - startedAt >= staleTimeoutSeconds)
                {
                    Modules.Logger.LogInfo(
                        $"[SAIN] Stale {combatDecision} release for follower={owner.Profile?.Nickname ?? owner.name}[{profileId}]");
                    ForceReleaseFollowerCombatState(owner);
                    StaleDecisionStartedAtByBot.Remove(profileId);
                    StaleDecisionTypeByBot.Remove(profileId);
                    SoloLayerNoDecisionStartedAtByBot.Remove(profileId);
                    LastSoloCombatSeenAtByBot.Remove(profileId);
                    NextCooldownCrouchSetAtByBot.Remove(profileId);
                    return true;
                }
            }
            else
            {
                StaleDecisionStartedAtByBot.Remove(profileId);
                StaleDecisionTypeByBot.Remove(profileId);
            }

            if (staleSoloLayerNoDecisionCandidate)
            {
                if (!SoloLayerNoDecisionStartedAtByBot.TryGetValue(profileId, out float startedAt))
                {
                    SoloLayerNoDecisionStartedAtByBot[profileId] = Time.time;
                }
                else if (Time.time - startedAt >= StaleSoloLayerNoDecisionReleaseSeconds)
                {
                    Modules.Logger.LogInfo(
                        $"[SAIN] Stale solo-layer release for follower={owner.Profile?.Nickname ?? owner.name}[{profileId}] " +
                        $"combat={combatDecision} self={selfDecision} knownEnemies={knownEnemyCount}");
                    ForceReleaseFollowerCombatState(owner);
                    SoloLayerNoDecisionStartedAtByBot.Remove(profileId);
                    StaleDecisionStartedAtByBot.Remove(profileId);
                    StaleDecisionTypeByBot.Remove(profileId);
                    LastSoloCombatSeenAtByBot.Remove(profileId);
                    NextCooldownCrouchSetAtByBot.Remove(profileId);
                    return true;
                }
            }
            else
            {
                SoloLayerNoDecisionStartedAtByBot.Remove(profileId);
            }

            if (botInCombat || soloCombatLayerActive || soloSelfActionSeekCover)
            {
                LastSoloCombatSeenAtByBot[profileId] = Time.time;
            }

            if (soloCombatLayerActive || soloSelfActionSeekCover || staleDecisionCandidate)
            {
                TryApplyCooldownCrouch(owner, bot, profileId);
                return false;
            }

            if (LastSoloCombatSeenAtByBot.TryGetValue(profileId, out float lastSeenAt))
            {
                if (Time.time - lastSeenAt < SoloCombatReleaseGraceSeconds)
                {
                    TryApplyCooldownCrouch(owner, bot, profileId);
                    return false;
                }

                LastSoloCombatSeenAtByBot.Remove(profileId);
            }

            return true;
        }

        private static bool TryGetStaleReleaseTimeout(
            ECombatDecision combatDecision,
            ESelfActionType selfDecision,
            bool hasNoEnemyContext,
            out float timeoutSeconds)
        {
            timeoutSeconds = 0f;
            if (!hasNoEnemyContext)
            {
                return false;
            }

            switch (combatDecision)
            {
                case ECombatDecision.Search:
                    timeoutSeconds = StaleSearchReleaseSeconds;
                    return true;
                case ECombatDecision.SeekCover:
                    if (selfDecision != ESelfActionType.None)
                    {
                        return false;
                    }
                    timeoutSeconds = StaleSeekCoverReleaseSeconds;
                    return true;
                case ECombatDecision.Retreat:
                    timeoutSeconds = StaleRetreatReleaseSeconds;
                    return true;
                case ECombatDecision.ShiftCover:
                    timeoutSeconds = StaleShiftCoverReleaseSeconds;
                    return true;
                default:
                    return false;
            }
        }

        private static void TryApplyCooldownCrouch(BotOwner owner, BotComponent bot, string profileId)
        {
            if (owner == null || bot == null || string.IsNullOrEmpty(profileId))
            {
                return;
            }

            if (NextCooldownCrouchSetAtByBot.TryGetValue(profileId, out float nextAt) && Time.time < nextAt)
            {
                return;
            }

            NextCooldownCrouchSetAtByBot[profileId] = Time.time + CooldownCrouchSetIntervalSeconds;

            float currentPose = owner.GetPlayer?.MovementContext?.PoseLevel ?? 1f;
            if (currentPose <= 0.2f)
            {
                return;
            }

            try
            {
                bot.Mover?.SetTargetPose(CooldownCrouchPose);
                owner.Mover?.SetPose(CooldownCrouchPose);
            }
            catch
            {
                // Keep readiness checks resilient; crouch nudge is best-effort only.
            }
        }

        public static void ForceReleaseFollowerCombatState(BotOwner owner)
        {
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return;
            }

            try
            {
                if (!SAINEnableClass.GetSAIN(owner.ProfileId, out BotComponent bot) || bot == null)
                {
                    return;
                }

                ClearFollowerSearchState(bot);
                ExpireKnownEnemyTimers(bot);

                var enemyController = bot.EnemyController;
                if (enemyController != null)
                {
                    enemyController.ClearEnemy();
                }

                var decisions = bot.Decision;
                if (decisions != null)
                {
                    decisions.ResetDecisions(false);
                }

                // Hard-release SAIN layer ownership so attention/reset cannot leave a bot in
                // combat-layer control with no active combat decision.
                bot.ActiveLayer = ESAINLayer.None;
                bot.BotActivation?.SetCurrentAction(null);

                if (owner.BotRequestController?.CurRequest != null)
                {
                    owner.BotRequestController.CurRequest.Complete();
                    owner.BotRequestController.CurRequest = null;
                }

                owner.StopMove();
                owner.GoToSomePointData?.SetPoint(owner.Position);
                owner.GoToSomePointData?.UpdateToGo(false);

                if (owner.Mover != null)
                {
                    owner.Mover.Pause = false;
                    if (owner.Mover.Sprinting)
                    {
                        owner.Mover.Sprint(false, false);
                    }
                    owner.Mover.Stop();
                }
            }
            catch
            {
                // Keep release resilient in raid even if movement internals throw.
            }
        }

        private static void ExpireKnownEnemyTimers(BotComponent bot)
        {
            if (bot?.EnemyController?.EnemiesArray == null || TimeLastKnownUpdatedField == null)
            {
                return;
            }

            float forgetEnemyTime = Mathf.Max(bot.Info?.ForgetEnemyTime ?? 0f, 0.1f);
            float expiredAt = Time.time - forgetEnemyTime - 0.01f;

            foreach (var enemy in bot.EnemyController.EnemiesArray)
            {
                var knownPlaces = enemy?.KnownPlaces;
                if (knownPlaces?.LastKnownPlace == null)
                {
                    continue;
                }

                TimeLastKnownUpdatedField.SetValue(knownPlaces, expiredAt);
            }
        }

        public static bool TrySyncFollowerEnemyState(BotOwner owner, Player enemyPlayer, bool prioritizeAsGoal)
        {
            if (owner == null || enemyPlayer == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return false;
            }

            if (!SAINEnableClass.GetSAIN(owner.ProfileId, out BotComponent bot) || bot == null)
            {
                return false;
            }

            var enemyController = bot.EnemyController;
            if (enemyController == null)
            {
                return false;
            }

            var sainEnemy = enemyController.CheckAddEnemy(enemyPlayer);
            if (sainEnemy == null)
            {
                return false;
            }

            sainEnemy.UpdateLastSeenPosition(enemyPlayer.Position, Time.time);
            bool shouldForceGoal = prioritizeAsGoal || enemyController.GoalEnemy == null;
            if (shouldForceGoal)
            {
                SetGoalEnemy(enemyController, sainEnemy);
            }
            else
            {
                enemyController.ChooseEnemy();
                if (enemyController.GoalEnemy == null)
                {
                    SetGoalEnemy(enemyController, sainEnemy);
                }
            }

            if (!bot.IsInCombat)
            {
                bot.BotActivation.SetInCombat(true);
            }

            return true;
        }

        private static void SetGoalEnemy(SAINEnemyController enemyController, Enemy sainEnemy)
        {
            GoalEnemySetter?.Invoke(enemyController, new object[] { sainEnemy });
            SetGoalEnemyMethod?.Invoke(enemyController, new object[] { sainEnemy.EnemyInfo });
        }

        public static bool TryResetFollowerDecisionState(BotOwner owner)
        {
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return false;
            }

            if (!SAINEnableClass.GetSAIN(owner.ProfileId, out BotComponent bot) || bot == null)
            {
                return false;
            }

            ClearFollowerSearchState(bot);
            var decisions = bot.Decision;
            if (decisions == null)
            {
                return false;
            }

            decisions.ResetDecisions(false);
            return true;
        }

        private static void ClearFollowerSearchState(BotComponent bot)
        {
            if (bot == null)
            {
                return;
            }

            try
            {
                var search = bot.Search;
                if (search != null)
                {
                    search.ToggleSearch(false, null);
                    search.Reset();
                }
            }
            catch
            {
                // Keep release resilient if SAIN search internals change.
            }

            try
            {
                var enemyController = bot.EnemyController;
                if (enemyController?.EnemiesArray == null)
                {
                    return;
                }

                foreach (var enemy in enemyController.EnemiesArray)
                {
                    enemy?.KnownPlaces?.OnEnemyKnownChanged(false, enemy);
                }
            }
            catch
            {
                // Best-effort known-place cleanup only.
            }
        }

    }
}

using Comfort.Common;
using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace pitTeam.Modules
{
    internal enum FollowerCombatTargetMissionKind
    {
        OrderedPush,
        AutoPush
    }

    internal static class FollowerCombatTargetCommitments
    {
        private const float TemporaryTargetLostGraceSeconds = 1.25f;
        private const float TemporaryTargetRecentDamageGraceSeconds = 2f;

        private static readonly Dictionary<string, TargetState> States =
            new Dictionary<string, TargetState>(StringComparer.Ordinal);

        [ThreadStatic]
        private static int authoritativeGoalSetDepth;

        public static GoalSetScope BeginAuthoritativeGoalSet()
        {
            authoritativeGoalSetDepth++;
            return new GoalSetScope();
        }

        public static bool HasMission(BotOwner? follower)
        {
            return TryGetState(follower, out TargetState? state) &&
                   state!.MissionActive &&
                   IsMissionAlive(state);
        }

        public static bool IsMissionTarget(BotOwner? follower, EnemyInfo? enemy)
        {
            return enemy != null &&
                   TryGetState(follower, out TargetState? state) &&
                   state!.MissionActive &&
                   string.Equals(enemy.ProfileId, state.MissionEnemyProfileId, StringComparison.Ordinal);
        }

        public static bool IsMissionTarget(BotOwner? follower, string? enemyProfileId)
        {
            return !string.IsNullOrEmpty(enemyProfileId) &&
                   TryGetState(follower, out TargetState? state) &&
                   state!.MissionActive &&
                   string.Equals(enemyProfileId, state.MissionEnemyProfileId, StringComparison.Ordinal);
        }

        public static void SetMission(
            BotOwner? follower,
            EnemyInfo? target,
            FollowerCombatTargetMissionKind kind,
            string reason)
        {
            if (!TryGetFollowerKey(follower, out string followerId) ||
                target == null ||
                string.IsNullOrEmpty(target.ProfileId))
            {
                return;
            }

            if (kind == FollowerCombatTargetMissionKind.AutoPush &&
                Enemy.IsMemoryOnlyAcquisitionWithoutPersonalContact(target))
            {
                return;
            }

            TargetState state = GetOrCreateState(followerId);
            if (state.MissionActive &&
                state.MissionKind == FollowerCombatTargetMissionKind.OrderedPush &&
                kind == FollowerCombatTargetMissionKind.AutoPush &&
                !string.Equals(state.MissionEnemyProfileId, target.ProfileId, StringComparison.Ordinal))
            {
                return;
            }

            if (state.MissionActive &&
                state.MissionKind == kind &&
                !string.Equals(state.MissionEnemyProfileId, target.ProfileId, StringComparison.Ordinal) &&
                IsMissionAlive(state))
            {
                return;
            }

            state.MissionActive = true;
            state.MissionKind = kind;
            state.MissionEnemyProfileId = target.ProfileId;
            state.MissionLastKnownPosition = GetBestEnemyPosition(target);
            state.MissionSetAt = Time.time;
            state.MissionLastRefreshAt = Time.time;
            state.TemporaryTargets.Clear();
            state.ActiveTemporaryEnemyProfileId = null;
        }

        public static void RefreshMission(
            BotOwner? follower,
            EnemyInfo? target,
            FollowerCombatTargetMissionKind kind,
            string reason)
        {
            if (!TryGetState(follower, out TargetState? state) ||
                target == null ||
                string.IsNullOrEmpty(target.ProfileId) ||
                !state!.MissionActive ||
                state.MissionKind != kind ||
                !string.Equals(state.MissionEnemyProfileId, target.ProfileId, StringComparison.Ordinal))
            {
                return;
            }

            Vector3 position = GetBestEnemyPosition(target);
            if (IsFinite(position) && position.sqrMagnitude > 0.01f)
            {
                state.MissionLastKnownPosition = position;
            }

            state.MissionLastRefreshAt = Time.time;
        }

        public static void RefreshMission(
            BotOwner? follower,
            Player? target,
            FollowerCombatTargetMissionKind kind,
            string reason)
        {
            if (!TryGetState(follower, out TargetState? state) ||
                target == null ||
                string.IsNullOrEmpty(target.ProfileId) ||
                !state!.MissionActive ||
                state.MissionKind != kind ||
                !string.Equals(state.MissionEnemyProfileId, target.ProfileId, StringComparison.Ordinal))
            {
                return;
            }

            if (FollowerEnemyTracking.Mode == EnemyTrackingMode.Simple && IsFinite(target.Position) && target.Position.sqrMagnitude > 0.01f)
            {
                state.MissionLastKnownPosition = target.Position;
            }

            state.MissionLastRefreshAt = Time.time;
        }

        public static void ClearMission(
            BotOwner? follower,
            FollowerCombatTargetMissionKind? kind,
            string reason)
        {
            if (!TryGetFollowerKey(follower, out string followerId) ||
                !States.TryGetValue(followerId, out TargetState state))
            {
                return;
            }

            if (kind.HasValue &&
                (!state.MissionActive || state.MissionKind != kind.Value))
            {
                return;
            }

            state.ClearMission();
            if (state.IsEmpty)
            {
                States.Remove(followerId);
            }
        }

        public static bool ShouldAllowGoalEnemySet(
            BotOwner? follower,
            EnemyInfo? previous,
            EnemyInfo? next,
            string reason,
            out string? blockedReason)
        {
            blockedReason = null;
            if (next == null ||
                follower == null ||
                authoritativeGoalSetDepth > 0 ||
                !BossPlayers.IsFollower(follower) ||
                !TryGetState(follower, out TargetState? state) ||
                !state!.MissionActive)
            {
                return true;
            }

            if (!IsMissionAlive(state))
            {
                ClearMission(follower, state.MissionKind, "missionDeadBeforeGoalSet");
                return true;
            }

            if (string.Equals(next.ProfileId, state.MissionEnemyProfileId, StringComparison.Ordinal))
            {
                RefreshMission(follower, next, state.MissionKind, "goalSetMissionTarget");
                state.ActiveTemporaryEnemyProfileId = null;
                return true;
            }

            if (string.IsNullOrEmpty(next.ProfileId))
            {
                blockedReason = "targetCommitmentBlocked:missingCandidateId";
                return false;
            }

            if (TryGetTemporary(state, next.ProfileId, out TemporaryTarget? temporary) &&
                IsTemporaryTargetStillValid(follower, next, temporary!))
            {
                state.ActiveTemporaryEnemyProfileId = next.ProfileId;
                return true;
            }

            if (TryRegisterTemporaryTarget(
                    follower,
                    next,
                    reason,
                    out string temporaryReason))
            {
                return true;
            }

            blockedReason = "targetCommitmentBlocked:" + temporaryReason;
            return false;
        }

        public static bool TryRegisterTemporaryTarget(
            BotOwner? follower,
            EnemyInfo? candidate,
            string reason,
            out string resultReason)
        {
            resultReason = "noMission";
            if (candidate == null ||
                follower == null ||
                !TryGetState(follower, out TargetState? state) ||
                !state!.MissionActive)
            {
                return false;
            }

            if (!IsMissionAlive(state))
            {
                ClearMission(follower, state.MissionKind, "missionDeadBeforeTemporary");
                resultReason = "missionDead";
                return false;
            }

            if (string.IsNullOrEmpty(candidate.ProfileId))
            {
                resultReason = "missingCandidateId";
                return false;
            }

            if (string.Equals(candidate.ProfileId, state.MissionEnemyProfileId, StringComparison.Ordinal))
            {
                resultReason = "missionTarget";
                return false;
            }

            if (!TryClassifyTemporaryTarget(follower, candidate, reason, out resultReason))
            {
                return false;
            }

            TemporaryTarget temporary = GetOrCreateTemporary(state, candidate.ProfileId);
            temporary.Reason = resultReason;
            temporary.FirstSeenAt = temporary.FirstSeenAt <= 0f ? Time.time : temporary.FirstSeenAt;
            temporary.LastValidAt = Time.time;
            temporary.LastSeenPosition = GetBestEnemyPosition(candidate);
            temporary.WasVisible = candidate.IsVisible;
            temporary.WasShootable = candidate.CanShoot;
            state.ActiveTemporaryEnemyProfileId = candidate.ProfileId;

            return true;
        }

        public static bool IsActiveTemporaryTarget(BotOwner? follower, EnemyInfo? enemy)
        {
            if (enemy == null ||
                string.IsNullOrEmpty(enemy.ProfileId) ||
                !TryGetState(follower, out TargetState? state) ||
                !state!.MissionActive ||
                !string.Equals(state.ActiveTemporaryEnemyProfileId, enemy.ProfileId, StringComparison.Ordinal) ||
                !TryGetTemporary(state, enemy.ProfileId, out TemporaryTarget? temporary))
            {
                return false;
            }

            return IsTemporaryTargetStillValid(follower!, enemy, temporary!);
        }

        public static bool TryRestoreMissionIfTemporaryExpired(
            BotOwner? follower,
            string reason,
            out EnemyInfo? restored)
        {
            restored = null;
            if (follower?.Memory == null ||
                !TryGetState(follower, out TargetState? state) ||
                !state!.MissionActive)
            {
                return false;
            }

            if (!IsMissionAlive(state))
            {
                ClearMission(follower, state.MissionKind, "missionDeadBeforeRestore");
                return false;
            }

            EnemyInfo? current = follower.Memory.GoalEnemy;
            if (current != null &&
                string.Equals(current.ProfileId, state.MissionEnemyProfileId, StringComparison.Ordinal))
            {
                RefreshMission(follower, current, state.MissionKind, "missionAlreadyGoal");
                restored = current;
                return true;
            }

            if (current != null &&
                TryGetTemporary(state, current.ProfileId, out TemporaryTarget? temporary) &&
                IsTemporaryTargetStillValid(follower, current, temporary!))
            {
                state.ActiveTemporaryEnemyProfileId = current.ProfileId;
                return false;
            }

            Player? missionTarget = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(state.MissionEnemyProfileId);
            if (!FollowerEnemyTracking.CanRestore(follower, state.MissionEnemyProfileId))
            {
                ClearMission(follower, state.MissionKind, "trackingExpired");
                return false;
            }
            if (missionTarget?.HealthController?.IsAlive != true)
            {
                ClearMission(follower, state.MissionKind, "missionTargetDead");
                return false;
            }

            EnemyInfo? missionEnemy = Enemy.MakeEnemy(
                follower,
                missionTarget,
                EBotEnemyCause.checkAddTODO,
                countSharedSeenAsPersonal: false);
            if (missionEnemy == null)
            {
                return false;
            }

            if (!FollowerEnemyTracking.IsEligible(missionEnemy))
            {
                ClearMission(follower, state.MissionKind, "trackingExpired");
                return false;
            }

            missionEnemy.PriorityIndex = 0;
            missionEnemy.IgnoreUntilAggression = false;
            missionEnemy.SetVisible(missionEnemy.IsVisible);
            Vector3 rememberedPosition = IsFinite(state.MissionLastKnownPosition) && state.MissionLastKnownPosition.sqrMagnitude > 0.01f
                ? state.MissionLastKnownPosition
                : missionTarget.Position;
            if (IsFinite(rememberedPosition) && rememberedPosition.sqrMagnitude > 0.01f)
            {
                missionEnemy.PersonalLastPos = rememberedPosition;
                if (missionEnemy.GroupInfo != null)
                {
                    missionEnemy.GroupInfo.EnemyLastPosition = rememberedPosition;
                }
            }

            follower.Memory.IsPeace = false;
            using (BeginAuthoritativeGoalSet())
            using (FollowerGoalEnemyTracker.Begin("FollowerCombatTargetCommitments.TryRestoreMission", reason))
            {
                follower.Memory.GoalEnemy = missionEnemy;
            }

            state.ActiveTemporaryEnemyProfileId = null;
            state.MissionLastKnownPosition = rememberedPosition;
            state.MissionLastRefreshAt = Time.time;
            FollowerContactEnemyRetention.Register(follower, missionTarget, missionEnemy.IsVisible || missionEnemy.CanShoot, prioritized: true);

            restored = missionEnemy;
            return true;
        }

        public static bool IsCurrentGoalTemporaryTarget(BotOwner? follower)
        {
            return IsActiveTemporaryTarget(follower, follower?.Memory?.GoalEnemy);
        }

        private static bool TryClassifyTemporaryTarget(
            BotOwner follower,
            EnemyInfo candidate,
            string reason,
            out string resultReason)
        {
            resultReason = "notEngageable";
            if (candidate.Person?.HealthController?.IsAlive != true)
            {
                resultReason = "candidateDead";
                return false;
            }

            if (FollowerImmediateFirePolicy.IsLocalSelfDefenseThreat(candidate))
            {
                resultReason = "localSelfDefense";
                return true;
            }

            if (candidate.IsVisible && candidate.CanShoot)
            {
                resultReason = "visibleShootable";
                return true;
            }

            if (candidate.IsVisible &&
                candidate.Distance <= CombatDistanceConfiguration.Instance.GetCloseThreatAutoAcquireDistance())
            {
                resultReason = "closeVisible";
                return true;
            }

            if (reason.IndexOf("directHit", StringComparison.OrdinalIgnoreCase) >= 0 &&
                follower.Memory != null &&
                Time.time - follower.Memory.LastTimeHit <= TemporaryTargetRecentDamageGraceSeconds)
            {
                resultReason = "directHit";
                return true;
            }

            return false;
        }

        private static bool IsTemporaryTargetStillValid(
            BotOwner follower,
            EnemyInfo candidate,
            TemporaryTarget temporary)
        {
            if (candidate.Person?.HealthController?.IsAlive != true)
            {
                return false;
            }

            if (TryClassifyTemporaryTarget(follower, candidate, temporary.Reason, out string reason))
            {
                temporary.LastValidAt = Time.time;
                temporary.LastSeenPosition = GetBestEnemyPosition(candidate);
                temporary.Reason = reason;
                temporary.WasVisible = candidate.IsVisible;
                temporary.WasShootable = candidate.CanShoot;
                return true;
            }

            return Time.time - temporary.LastValidAt <= TemporaryTargetLostGraceSeconds;
        }

        private static bool IsMissionAlive(TargetState state)
        {
            if (!state.MissionActive || string.IsNullOrEmpty(state.MissionEnemyProfileId))
            {
                return false;
            }

            Player? missionTarget = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(state.MissionEnemyProfileId);
            return missionTarget?.HealthController?.IsAlive == true;
        }

        private static bool TryGetFollowerKey(BotOwner? follower, out string followerId)
        {
            followerId = follower?.ProfileId ?? string.Empty;
            return !string.IsNullOrEmpty(followerId);
        }

        private static bool TryGetState(BotOwner? follower, out TargetState? state)
        {
            state = null;
            return TryGetFollowerKey(follower, out string followerId) &&
                   States.TryGetValue(followerId, out state);
        }

        private static TargetState GetOrCreateState(string followerId)
        {
            if (!States.TryGetValue(followerId, out TargetState state))
            {
                state = new TargetState();
                States[followerId] = state;
            }

            return state;
        }

        private static bool TryGetTemporary(
            TargetState state,
            string? profileId,
            out TemporaryTarget? temporary)
        {
            temporary = null;
            return !string.IsNullOrEmpty(profileId) &&
                   state.TemporaryTargets.TryGetValue(profileId, out temporary);
        }

        private static TemporaryTarget GetOrCreateTemporary(TargetState state, string profileId)
        {
            if (!state.TemporaryTargets.TryGetValue(profileId, out TemporaryTarget temporary))
            {
                temporary = new TemporaryTarget();
                state.TemporaryTargets[profileId] = temporary;
            }

            return temporary;
        }

        private static Vector3 GetBestEnemyPosition(EnemyInfo enemy)
        {
            if (FollowerEnemyTracking.IsRealistic(enemy)) return FollowerEnemyTracking.Position(enemy);
            if (IsFinite(enemy.CurrPosition) && enemy.CurrPosition.sqrMagnitude > 0.01f)
            {
                return enemy.CurrPosition;
            }

            if (IsFinite(enemy.EnemyLastPositionReal) && enemy.EnemyLastPositionReal.sqrMagnitude > 0.01f)
            {
                return enemy.EnemyLastPositionReal;
            }

            if (IsFinite(enemy.PersonalLastPos) && enemy.PersonalLastPos.sqrMagnitude > 0.01f)
            {
                return enemy.PersonalLastPos;
            }

            return enemy.Person?.Position ?? Vector3.zero;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public readonly struct GoalSetScope : IDisposable
        {
            public void Dispose()
            {
                authoritativeGoalSetDepth = Math.Max(0, authoritativeGoalSetDepth - 1);
            }
        }

        private sealed class TargetState
        {
            public bool MissionActive;
            public FollowerCombatTargetMissionKind MissionKind;
            public string MissionEnemyProfileId = string.Empty;
            public Vector3 MissionLastKnownPosition;
            public float MissionSetAt;
            public float MissionLastRefreshAt;
            public string? ActiveTemporaryEnemyProfileId;
            public readonly Dictionary<string, TemporaryTarget> TemporaryTargets =
                new Dictionary<string, TemporaryTarget>(StringComparer.Ordinal);

            public bool IsEmpty => !MissionActive && TemporaryTargets.Count == 0;

            public void ClearMission()
            {
                MissionActive = false;
                MissionEnemyProfileId = string.Empty;
                MissionLastKnownPosition = Vector3.zero;
                MissionSetAt = 0f;
                MissionLastRefreshAt = 0f;
                ActiveTemporaryEnemyProfileId = null;
                TemporaryTargets.Clear();
            }
        }

        private sealed class TemporaryTarget
        {
            public string Reason = string.Empty;
            public float FirstSeenAt;
            public float LastValidAt;
            public Vector3 LastSeenPosition;
            public bool WasVisible;
            public bool WasShootable;
        }
    }
}

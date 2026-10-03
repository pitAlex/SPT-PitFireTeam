using System;
using System.Collections.Generic;
using EFT;
using pitTeam.BigBrain;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.Modules
{
    internal static partial class BattleRecorder
    {
        internal static bool IsRecordingEnabled => IsRecording();

        // Reads existing state only. In particular, never call tracking Observe/IsEligible,
        // patrol readiness, native decision providers, or medical selection to record them.
        private static void RecordPatrolWait(BotOwner owner, RecorderFollowerState state)
        {
            if (Time.time < state.NextPatrolWaitProbe) return;
            state.NextPatrolWaitProbe = Time.time + 0.5f;
            bool waiting = owner.Brain?.BaseBrain?.CurLayerInfo?.Name() == "pitTeam.FollowerPatrol" &&
                owner.Brain?.Agent?.LastResult().Reason == pitTeam.BigBrain.FollowerPatrolLayer.CombatReadinessWaitReason;
            if (!waiting && !state.PatrolWaiting) return;
            string block = BossPlayers.Instance?.GetFollower(owner)?.DescribePatrolCombatBlock() ?? "unavailable";
            bool changed = waiting != state.PatrolWaiting || block != state.PatrolWaitBlock;
            if (!changed && Time.time < state.NextPatrolWaitRecord) return;
            string phase = !waiting ? "exit" : !state.PatrolWaiting ? "enter" : changed ? "blockChanged" : "heartbeat";
            if (waiting && !state.PatrolWaiting) state.PatrolWaitStarted = Time.time;
            state.PatrolWaiting = waiting;
            state.PatrolWaitBlock = block;
            state.NextPatrolWaitRecord = Time.time + 5f;
            WriteEventInternal("patrolCombatWait", owner, new
            {
                phase, waiting, block, duration = Time.time - state.PatrolWaitStarted,
                trackingMode = FollowerEnemyTracking.Mode.ToString(),
                rememberSeconds = FollowerEnemyTracking.RememberSeconds,
                eftForgetSeconds = SanitizeFloat(owner.Settings?.FileSettings?.Mind?.TIME_TO_FORGOR_ABOUT_ENEMY_SEC ?? float.NaN),
                group = PatrolGroupEvidence(owner),
                nativeGoal = SainGoalEnemyBridge.DescribeGoalEnemy(owner),
                recovery = FollowerMedical.DescribePostCombatRecovery(owner),
                snapshot = CreateBotSnapshot(owner, state)
            });
        }

        private static object PatrolGroupEvidence(BotOwner owner)
        {
            var group = owner.BotsGroup;
            var contacts = new List<object>();
            if (group?.Enemies != null)
                foreach (var pair in group.Enemies)
                {
                    if (contacts.Count >= 32) break;
                    EnemyInfo personal = null;
                    owner.EnemiesController?.EnemyInfos?.TryGetValue(pair.Key, out personal);
                    var info = pair.Value;
                    contacts.Add(new
                    {
                        profileId = pair.Key?.ProfileId, alive = pair.Key?.HealthController?.IsAlive,
                        cause = info?.Cause.ToString(), haveSeen = info?.IsHaveSeen,
                        seenAt = SanitizeFloat(info?.EnemyLastSeenTimeReal ?? float.NaN),
                        sensedAt = SanitizeFloat(info?.EnemyLastSeenTimeSense ?? float.NaN),
                        personalVisible = personal?.IsVisible, personalCanShoot = personal?.CanShoot,
                        personalSeenAt = SanitizeFloat(personal?.PersonalLastSeenTime ?? float.NaN),
                        tracking = personal == null ? null : FollowerEnemyTracking.Snapshot(owner, personal)
                    });
                }
            return new
            {
                seenAt = SanitizeFloat(group?.EnemyLastSeenTimeReal ?? float.NaN),
                seenAge = SanitizeFloat(Time.time - (group?.EnemyLastSeenTimeReal ?? float.NaN)),
                rawSeenAt = (group?.EnemyLastSeenTimeReal ?? float.NaN).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                totalEnemies = group?.Enemies?.Count ?? 0, contacts,
                truncated = (group?.Enemies?.Count ?? 0) > contacts.Count
            };
        }

        [System.Diagnostics.Conditional("DEBUG")]
        internal static void RecordGroupSightWrite(BotOwner owner, string enemyId, string reporterId,
            float before, float after, string stack, int suppressed)
        {
            if (!CanRecordBot(owner)) return;
            WriteEventInternal("groupSightTimestamp", owner, new
            {
                enemyId, reporterId, before = SanitizeFloat(before), after = SanitizeFloat(after),
                rawAfter = after.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                age = SanitizeFloat(Time.time - after), suppressed, stack,
                group = PatrolGroupEvidence(owner)
            });
        }
    }
}

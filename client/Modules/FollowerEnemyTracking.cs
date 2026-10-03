using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace pitTeam.Modules
{
    public enum EnemyTrackingMode { Simple, Realistic }

    // Tactical knowledge is independent of EnemyInfo's real sensor/shot geometry.
    public static class FollowerEnemyTracking
    {
        public const float SearchUpperLimit = 400f;
        public static EnemyTrackingMode Mode { get; private set; } = EnemyTrackingMode.Realistic;
        public static float RememberSeconds { get; private set; } = 20f;
        private static readonly Dictionary<(string, string), Contact> Contacts = new();

        private sealed class Contact
        {
            public Vector3 Position;
            public float ObservedAt = -1f;
            public bool SearchActive;
            public float SearchedAt = -1f;
            public string Source = string.Empty;
            public int SampleFrame = -1;
        }

        public static void BeginRaid()
        {
            Contacts.Clear();
            Mode = pitFireTeam.enemyTracking?.Value ?? EnemyTrackingMode.Realistic;
            RememberSeconds = pitFireTeam.enemyRemember?.Value ?? 20;
        }

        public static void EndRaid() => Contacts.Clear();

        public static bool IsRealistic(EnemyInfo? enemy) => enemy?.Owner != null &&
            BossPlayers.IsFollower(enemy.Owner) && Mode == EnemyTrackingMode.Realistic;

        public static void Clear(BotOwner owner)
        {
            if (owner == null) return;
            var keys = new List<(string, string)>();
            foreach (var key in Contacts.Keys) if (key.Item1 == owner.ProfileId) keys.Add(key);
            foreach (var key in keys) Contacts.Remove(key);
        }

        public static bool IsFinite(Vector3 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) &&
            !float.IsNaN(p.y) && !float.IsInfinity(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.z);

        private static Contact Get(EnemyInfo enemy)
        {
            var key = (enemy.Owner.ProfileId, enemy.ProfileId);
            if (!Contacts.TryGetValue(key, out var contact)) Contacts[key] = contact = new Contact();
            return contact;
        }

        public static void Report(EnemyInfo enemy, Vector3 position, float observedAt, string source)
        {
            if (enemy?.Owner == null || !BossPlayers.IsFollower(enemy.Owner) || !IsFinite(position) ||
                float.IsNaN(observedAt) || float.IsInfinity(observedAt) || observedAt < 0f) return;
            var contact = Get(enemy);
            if (observedAt <= contact.ObservedAt) return;
            contact.Position = position;
            contact.ObservedAt = observedAt;
            contact.Source = source;
        }

        private static Contact Observe(EnemyInfo enemy)
        {
            var contact = Get(enemy);
            if (contact.SampleFrame == Time.frameCount) return contact;
            contact.SampleFrame = Time.frameCount;
            if (SainGoalEnemyBridge.TryGetTrackingReport(enemy.Owner, enemy, out var nativePosition, out float nativeTime))
            {
                Report(enemy, nativePosition, nativeTime, "nativeReport");
                // Native hearing carries dispersion and provenance. An EFT mirror must not
                // overwrite it with a bookkeeping timestamp and a hidden transform.
                return contact;
            }
            if (enemy.IsVisible)
                Report(enemy, enemy.CurrPosition, Time.time, "personalSight");
            else
            {
                // Each coordinate travels with its own observation timestamp. Reads and
                // retention/mission repairs never count as another report.
                if (enemy.PersonalLastSeenTime > 0f)
                    Report(enemy, enemy.PersonalLastPos, enemy.PersonalLastSeenTime, "personalReport");
                var group = enemy.GroupInfo;
                if (group != null)
                {
                    if (group.EnemyLastSeenTimeReal > 0f)
                        Report(enemy, enemy.EnemyLastPositionReal, group.EnemyLastSeenTimeReal, "squadSight");
                    if (group.EnemyLastSeenTimeSense > 0f)
                        Report(enemy, enemy.EnemyLastPosition, group.EnemyLastSeenTimeSense, "sensedReport");
                }
            }
            return contact;
        }

        public static bool TryGetKnownPosition(EnemyInfo enemy, out Vector3 position, out float observedAt)
        {
            position = default; observedAt = -1f;
            if (enemy?.Owner == null || !BossPlayers.IsFollower(enemy.Owner)) return false;
            var contact = Observe(enemy);
            if (contact.ObservedAt < 0f) return false;
            position = contact.Position; observedAt = contact.ObservedAt;
            return true;
        }

        public static Vector3 Position(EnemyInfo enemy)
        {
            if (enemy == null) return default;
            if (!IsRealistic(enemy)) return enemy.CurrPosition;
            return TryGetKnownPosition(enemy, out var position, out _) ? position : enemy.Owner.Position;
        }

        public static float Distance(EnemyInfo enemy) => IsRealistic(enemy)
            ? TryGetKnownPosition(enemy, out var point, out _) ? (point - enemy.Owner.Position).magnitude : float.MaxValue
            : enemy?.Distance ?? float.MaxValue;

        public static bool IsEligible(EnemyInfo enemy)
        {
            if (enemy?.Owner == null || !BossPlayers.IsFollower(enemy.Owner)) return true;
            var contact = Observe(enemy);
            float age = Time.time - contact.ObservedAt;
            if (Mode == EnemyTrackingMode.Simple) return contact.ObservedAt >= 0f && age <= RememberSeconds;
            if (!pitFireTeam.UseSainFollowerCombat(enemy.Owner) && contact.ObservedAt >= 0f &&
                contact.SearchedAt >= contact.ObservedAt && !enemy.IsVisible) return false;
            if (pitFireTeam.UseSainFollowerCombat(enemy.Owner) && age <= SearchUpperLimit &&
                SainGoalEnemyBridge.TryGetRetainedSameGoalEnemy(enemy.Owner, enemy, out _)) return true;
            return contact.ObservedAt >= 0f && age <= SearchUpperLimit &&
                (age <= RememberSeconds || (contact.SearchActive &&
                    contact.SearchedAt < contact.ObservedAt));
        }

        public static bool IsSearched(EnemyInfo enemy) => IsRealistic(enemy) &&
            Observe(enemy).ObservedAt >= 0f && Get(enemy).SearchedAt >= Get(enemy).ObservedAt;

        public static bool ShouldRetainSearch(EnemyInfo enemy) => IsRealistic(enemy) &&
            Get(enemy).SearchActive && !IsSearched(enemy) && IsEligible(enemy);

        // Passive recording must never sample sensors or refresh evidence.
        public static object Snapshot(BotOwner owner, EnemyInfo enemy)
        {
            if (owner == null || enemy == null || !Contacts.TryGetValue((owner.ProfileId, enemy.ProfileId), out var contact)) return null;
            return new
            {
                mode = Mode.ToString(), source = contact.Source,
                position = new { x = contact.Position.x, y = contact.Position.y, z = contact.Position.z },
                observedAt = contact.ObservedAt, searchedAt = contact.SearchedAt, searchActive = contact.SearchActive,
                normalExpiry = contact.ObservedAt + RememberSeconds,
                searchUpperExpiry = contact.ObservedAt + SearchUpperLimit
            };
        }

        public static void SearchTick(EnemyInfo enemy)
        {
            if (IsRealistic(enemy)) Observe(enemy).SearchActive = true;
        }

        public static void EndSearch(BotOwner owner, string enemyId)
        {
            if (owner != null && Contacts.TryGetValue((owner.ProfileId, enemyId), out var contact)) contact.SearchActive = false;
        }

        public static bool HasArrived(BotOwner owner, Vector3 point)
        {
            return (owner.Position - point).sqrMagnitude <= 4f && Math.Abs(owner.Position.y - point.y) <= 1.75f &&
                Utils.Utils.TryGetCompletePathDistance(owner.Position, point, out float route) && route <= 2f;
        }

        public static ShootToPoint? CoverTarget(BotOwner owner)
        {
            var enemy = owner?.Memory?.GoalEnemy;
            if (IsRealistic(enemy) && !enemy.IsVisible)
                return TryGetKnownPosition(enemy, out var point, out _) ? new ShootToPoint(point + Vector3.up * 1.1f, 1f) : null;
            return owner?.CurrentEnemyTargetPosition(true);
        }

        public static void CompleteSearch(EnemyInfo enemy, float reportTime)
        {
            if (IsRealistic(enemy)) Get(enemy).SearchedAt = Math.Max(Get(enemy).SearchedAt, reportTime);
        }

        public static bool CanRestore(BotOwner owner, string enemyId)
        {
            if (owner?.EnemiesController?.EnemyInfos != null)
                foreach (var info in owner.EnemiesController.EnemyInfos.Values)
                    if (info?.ProfileId == enemyId) return IsEligible(info);
            if (owner == null || !Contacts.TryGetValue((owner.ProfileId, enemyId), out var contact)) return false;
            float age = Time.time - contact.ObservedAt;
            if (Mode == EnemyTrackingMode.Simple) return contact.ObservedAt >= 0f && age <= RememberSeconds;
            if (!pitFireTeam.UseSainFollowerCombat(owner) && contact.SearchedAt >= contact.ObservedAt) return false;
            return contact.ObservedAt >= 0f && age <= SearchUpperLimit &&
                (age <= RememberSeconds || (contact.SearchActive && contact.SearchedAt < contact.ObservedAt));
        }

        public static void Update(BotOwner owner)
        {
            var enemy = owner?.Memory?.GoalEnemy;
            if (enemy == null || !BossPlayers.IsFollower(owner) || pitFireTeam.UseSainFollowerCombat(owner) || IsEligible(enemy)) return;
            if (FollowerContactEnemyRetention.TryGetActiveRetainedEnemy(owner, out var retained, out _) && retained?.ProfileId == enemy.ProfileId)
                FollowerContactEnemyRetention.ClearAndAllowNextGoalClear(owner);
            if (FollowerCombatTargetCommitments.IsMissionTarget(owner, enemy))
            {
                FollowerCombatTargetCommitments.ClearMission(owner, null, "trackingExpired");
                BossPlayers.Instance?.GetFollower(owner)?.ClearOrderedPushTargetLock("trackingExpired");
            }
            using (FollowerGoalEnemyTracker.Begin("FollowerEnemyTracking.Update", "trackingExpired"))
                owner.Memory.GoalEnemy = null;
            // Keep relationship/memory records for later genuine reacquisition.
        }
    }
}

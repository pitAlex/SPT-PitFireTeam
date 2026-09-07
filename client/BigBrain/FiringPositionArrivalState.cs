using System;
using UnityEngine;

namespace pitTeam.BigBrain
{
    // One arrival owns one search, at most one adjustment, and one non-renewable wait.
    internal sealed class FiringPositionArrivalState
    {
        private enum Phase { None, Checking, Adjusting, Waiting, Released }
        private Phase phase;

        public Vector3 Origin { get; private set; }
        public Vector3 AdjustmentPoint { get; private set; }
        public string EnemyProfileId { get; private set; } = string.Empty;
        public string SourceReason { get; private set; } = string.Empty;
        public string ReasonPrefix { get; private set; } = string.Empty;
        public float WaitUntil { get; private set; }
        public bool IsActive => phase != Phase.None && phase != Phase.Released;
        public bool IsAdjusting => phase == Phase.Adjusting;
        public bool IsWaiting => phase == Phase.Waiting;
        public bool IsReleased => phase == Phase.Released;
        public string MoveReason => ReasonPrefix + ".adjust";
        public string WaitReason => ReasonPrefix + ".wait";
        public string ShotReason => ReasonPrefix + ".shot";

        public bool Begin(Vector3 origin, string enemyProfileId, string sourceReason, string reasonPrefix)
        {
            if (phase != Phase.None) return false;
            Origin = origin;
            EnemyProfileId = enemyProfileId;
            SourceReason = sourceReason;
            ReasonPrefix = reasonPrefix;
            phase = Phase.Checking;
            return true;
        }

        public bool TryAdjust(Vector3 point)
        {
            if (phase != Phase.Checking) return false;
            AdjustmentPoint = point;
            phase = Phase.Adjusting;
            return true;
        }

        public void BeginWait(float now, float duration)
        {
            if (phase != Phase.Checking && phase != Phase.Adjusting) return;
            WaitUntil = now + Mathf.Clamp(duration, 1.5f, 2.5f);
            phase = Phase.Waiting;
        }

        public bool TryRelease(float now)
        {
            if (!IsWaiting || now < WaitUntil) return false;
            phase = Phase.Released;
            return true;
        }

        public bool MatchesEnemy(string? enemyProfileId) =>
            phase != Phase.None && string.Equals(EnemyProfileId, enemyProfileId, StringComparison.Ordinal);

        public bool Owns(string? reason) => (IsAdjusting || IsWaiting) &&
            string.Equals(reason, IsAdjusting ? MoveReason : WaitReason, StringComparison.Ordinal);

        public void Reset()
        {
            phase = Phase.None;
            Origin = AdjustmentPoint = Vector3.zero;
            EnemyProfileId = SourceReason = ReasonPrefix = string.Empty;
            WaitUntil = 0f;
        }
    }
}

using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using SAIN.Layers;
using SAIN.SAINComponent.Classes.EnemyClasses;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.SAINAddon
{
    internal class SAINFollowerCombatRegroupAction : BotAction
    {
        private const float MoveIssueInterval = 0.75f;
        private const float TargetRefreshInterval = 0.8f;
        private const float BossReanchorDistance = 2.5f;
        private const float DesiredBossBuffer = 4f;
        private const float MinBossBuffer = 3f;
        private const float MaxBossBuffer = 7f;
        private const float CloseBossDistance = 10f;
        private const float BossPointRadius = 5f;
        private const float BossOffsetMin = 0.5f;
        private const float BossOffsetMax = 2.5f;
        private const float HoldInCoverSeconds = 4f;
        private const float FollowerSpacing = 3f;
        private const float ArriveDistance = 1.5f;
        private const float TightRegroupNavDistance = 2.5f;
        private const float SameLevelTolerance = 1.75f;
        private const float AttackMoveEnemyFrontDot = 0.1f;
        private const float DestinationClaimStaleSeconds = 4f;
        private static readonly Dictionary<string, Dictionary<string, DestinationClaim>> DestinationClaimsByBossId =
            new Dictionary<string, Dictionary<string, DestinationClaim>>(StringComparer.Ordinal);

        private struct DestinationClaim
        {
            public Vector3 Position;
            public float UpdatedAt;
        }

        private float _nextMoveIssueTime;
        private float _nextRefreshTargetTime;
        private bool _haveTarget;
        private bool _retreatRunLocked;
        private Vector3 _targetPosition;
        private Vector3 _lastBossPosition;
        private float _lastFallbackPointEndedAt;
        private string? _claimBossId;
        private readonly NavMeshPath _path = new NavMeshPath();
        private readonly List<BotOwner> _aliveFollowers = new List<BotOwner>(8);

        protected virtual bool UseVanillaBossFallbackMode => false;

        public SAINFollowerCombatRegroupAction(BotOwner botOwner)
            : base(botOwner, nameof(SAINFollowerCombatRegroupAction))
        {
        }

        public override void Start()
        {
            base.Start();
            _nextMoveIssueTime = 0f;
            _nextRefreshTargetTime = 0f;
            _haveTarget = false;
            _retreatRunLocked = false;
            _targetPosition = Vector3.zero;
            _lastBossPosition = Vector3.zero;
            _lastFallbackPointEndedAt = 0f;
            _claimBossId = null;
        }

        public override void Stop()
        {
            BossPlayers.Instance?.GetFollower(BotOwner)?.SetCombatRegroupBossAnchor(false);
            if (!string.IsNullOrEmpty(_claimBossId))
            {
                RemoveDestinationClaim(_claimBossId, BotOwner?.ProfileId);
                _claimBossId = null;
            }

            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (!TryGetBoss(out pitAIBossPlayer boss, out Vector3 bossPosition))
            {
                return;
            }

            if (!UseVanillaBossFallbackMode && IsAlreadyInRegroupRange(bossPosition))
            {
                Bot.Mover.Stop();
                _retreatRunLocked = false;
                return;
            }

            if (NeedRetarget(bossPosition) || !HasCompletePath(_targetPosition))
            {
                if (_haveTarget)
                {
                    _lastFallbackPointEndedAt = Time.time;
                }

                Vector3 target;
                bool holdInCover;
                bool foundTarget = IsTightRegroupRequested()
                    ? TrySelectTightRegroupTarget(boss, bossPosition, out target, out holdInCover)
                    : UseVanillaBossFallbackMode
                    ? TrySelectDefaultBossTarget(boss, bossPosition, out target, out holdInCover)
                    : TrySelectRegroupTarget(boss, bossPosition, out target, out holdInCover);

                if (foundTarget)
                {
                    if (holdInCover)
                    {
                        _targetPosition = bossPosition;
                        _haveTarget = true;
                        RegisterDestinationClaim(boss, _targetPosition);
                    }
                    else
                    {
                        _targetPosition = target;
                        _haveTarget = true;
                        RegisterDestinationClaim(boss, _targetPosition);
                    }
                }
                else
                {
                    _targetPosition = bossPosition;
                    _haveTarget = true;
                    RegisterDestinationClaim(boss, _targetPosition);
                }

                _lastBossPosition = bossPosition;
                _nextRefreshTargetTime = Time.time + TargetRefreshInterval;
            }

            if (!_haveTarget)
            {
                Bot.Mover.Stop();
                _retreatRunLocked = false;
                return;
            }

            Enemy enemy = Bot.GoalEnemy;
            float leadDist = (_targetPosition - BotOwner.Position).magnitude;
            bool canAttackMove = CanAttackMoveWhileRetreating(enemy);
            bool enemyInFront = IsEnemyInFrontOfRetreat(enemy);

            if (_retreatRunLocked)
            {
                if (leadDist <= ArriveDistance || (canAttackMove && enemyInFront))
                {
                    _retreatRunLocked = false;
                }
            }

            bool sprint = true; // leadDist > 20f && (_retreatRunLocked || !canAttackMove); // always sprint on regroup for now, can tweak later if needed
            if (sprint)
            {
                _retreatRunLocked = true;
            }

            if (_nextMoveIssueTime <= Time.time)
            {
                _nextMoveIssueTime = Time.time + MoveIssueInterval;
                if (sprint)
                {
                    Bot.Mover.RunToPoint(_targetPosition);
                }
                else
                {
                    if ((bossPosition - BotOwner.Position).magnitude <= MinBossBuffer && leadDist <= ArriveDistance)
                    {
                        Bot.Mover.Stop();
                    }
                    else
                    {
                        Bot.Mover.WalkToPoint(_targetPosition);
                    }
                }
            }

            Bot.Mover.SetTargetPose(1f);
            Bot.Mover.SetTargetMoveSpeed(1f);
        }

        public override void OnSteeringTicked()
        {
            Enemy enemy = Bot.GoalEnemy;
            bool canAttackMove = CanAttackMoveWhileRetreating(enemy);
            bool enemyInFront = IsEnemyInFrontOfRetreat(enemy);
            bool keepRunning = _retreatRunLocked && (!canAttackMove || !enemyInFront);

            if (keepRunning)
            {
                Bot.Steering.LookToMovingDirection();
                return;
            }

            if (!Shoot.ShootAnyVisibleEnemies(enemy))
            {
                Bot.Suppression.TrySuppressAnyEnemy(enemy, Bot.EnemyController.KnownEnemies);
            }

            if (!Bot.Steering.SteerByPriority(enemy))
            {
                Bot.Steering.LookToMovingDirection();
            }
        }

        private bool NeedRetarget(Vector3 bossPosition)
        {
            if (!_haveTarget || Time.time >= _nextRefreshTargetTime)
            {
                return true;
            }

            if ((_lastBossPosition - bossPosition).sqrMagnitude >= BossReanchorDistance * BossReanchorDistance)
            {
                return true;
            }

            return (_targetPosition - BotOwner.Position).sqrMagnitude <= ArriveDistance * ArriveDistance;
        }

        private bool CanAttackMoveWhileRetreating(Enemy enemy)
        {
            return enemy != null &&
                   enemy.InLineOfSight &&
                   enemy.EnemyPlayer != null &&
                   enemy.EnemyPlayer.HealthController.IsAlive;
        }

        private bool IsEnemyInFrontOfRetreat(Enemy enemy)
        {
            if (enemy == null)
            {
                return false;
            }

            Vector3 moveDirection = _targetPosition - BotOwner.Position;
            moveDirection.y = 0f;
            if (moveDirection.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            Vector3 enemyDirection = enemy.EnemyPosition - BotOwner.Position;
            enemyDirection.y = 0f;
            if (enemyDirection.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            return Vector3.Dot(moveDirection.normalized, enemyDirection.normalized) > AttackMoveEnemyFrontDot;
        }

        private bool IsAlreadyInRegroupRange(Vector3 bossPosition)
        {
            if (IsTightRegroupRequested())
            {
                return Mathf.Abs(bossPosition.y - BotOwner.Position.y) <= SameLevelTolerance &&
                       TryGetPathLength(bossPosition, out float tightPathDistance) &&
                       tightPathDistance <= TightRegroupNavDistance;
            }

            float bossDistance = (bossPosition - BotOwner.Position).magnitude;
            return bossDistance <= ArriveDistance;
        }

        private bool TryGetBoss(out pitAIBossPlayer boss, out Vector3 position)
        {
            boss = default!;
            position = default;
            if (BotOwner?.BotFollower?.BossToFollow is not pitAIBossPlayer playerBoss || playerBoss.realPlayer == null)
            {
                return false;
            }

            boss = playerBoss;
            position = FollowerCombatAnchor.GetAnchorPosition(BotOwner);
            return true;
        }

        private bool TrySelectRegroupTarget(pitAIBossPlayer boss, Vector3 bossPosition, out Vector3 target, out bool holdInCover)
        {
            target = default;
            holdInCover = false;
            if (TrySelectClosestCoverTarget(boss, bossPosition, MinBossBuffer, MaxBossBuffer + 1.5f, out target))
            {
                return true;
            }

            BuildAliveFollowerSnapshot(boss);
            int slotIndex = GetFollowerSlotIndex();
            int aliveCount = _aliveFollowers.Count;
            float angleStep = aliveCount > 0 ? 360f / aliveCount : 45f;

            float bestScore = float.MaxValue;
            bool found = false;

            for (int i = 0; i < Mathf.Max(aliveCount, 8); i++)
            {
                int slot = aliveCount > 0 ? (slotIndex + i) % aliveCount : i;
                float angleDeg = slot * angleStep;
                if (TryEvaluateCandidate(boss, bossPosition, angleDeg, DesiredBossBuffer, ref bestScore, ref target))
                {
                    found = true;
                }
                if (TryEvaluateCandidate(boss, bossPosition, angleDeg + (angleStep * 0.5f), MinBossBuffer + 0.5f, ref bestScore, ref target))
                {
                    found = true;
                }
            }

            for (int i = 0; i < 12; i++)
            {
                float angle = UnityEngine.Random.Range(0f, 360f);
                float radius = UnityEngine.Random.Range(MinBossBuffer + 0.25f, MaxBossBuffer);
                if (TryEvaluateCandidate(boss, bossPosition, angle, radius, ref bestScore, ref target))
                {
                    found = true;
                }
            }

            return found;
        }

        private bool TrySelectTightRegroupTarget(
            pitAIBossPlayer boss,
            Vector3 bossPosition,
            out Vector3 target,
            out bool holdInCover)
        {
            holdInCover = false;
            BuildAliveFollowerSnapshot(boss);
            int aliveCount = Mathf.Max(1, _aliveFollowers.Count);
            int slotIndex = GetFollowerSlotIndex();
            float angle = slotIndex * (360f / aliveCount);
            target = bossPosition + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 1.5f;
            if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 1.5f, NavMesh.AllAreas) ||
                !TryGetPathLength(hit.position, out _))
            {
                target = default;
                return false;
            }

            target = hit.position;
            return true;
        }

        private bool IsTightRegroupRequested()
        {
            return BossPlayers.Instance?.GetFollower(BotOwner)?.TightRegroupRequested == true;
        }

        private bool TrySelectDefaultBossTarget(pitAIBossPlayer boss, Vector3 bossPosition, out Vector3 target, out bool holdInCover)
        {
            target = default;
            holdInCover = false;

            if (TrySelectClosestCoverTarget(boss, bossPosition, 0f, CloseBossDistance, out target))
            {
                return true;
            }

            if (TrySelectBossOffsetCoverTarget(boss, bossPosition, out target))
            {
                return true;
            }

            if (TrySelectBossOffsetPoint(boss, bossPosition, out target))
            {
                return true;
            }

            if (BotOwner.Memory?.IsInCover == true &&
                TryGetPathLength(bossPosition, out float bossPathLength) &&
                bossPathLength <= ArriveDistance)
            {
                holdInCover = true;
                return true;
            }

            return false;
        }

        private bool TrySelectClosestCoverTarget(pitAIBossPlayer boss, Vector3 bossPosition, float minBossDistance, float maxBossDistance, out Vector3 target)
        {
            target = default;
            CustomNavigationPoint point = global::pitTeam.Utils.Covers.GetClosestCoverPoint(
                BotOwner,
                bossPosition,
                maxBossDistance,
                cover =>
                {
                    if (cover == null || cover.IsSpotted)
                    {
                        return false;
                    }

                    float bossDistance = (cover.Position - bossPosition).magnitude;
                    if (bossDistance < minBossDistance || bossDistance > maxBossDistance)
                    {
                        return false;
                    }

                    if (IsCrowded(boss, cover.Position))
                    {
                        return false;
                    }

                    return TryGetPathLength(cover.Position, out _);
                });

            if (point == null)
            {
                return false;
            }

            target = point.Position;
            return true;
        }

        private bool TrySelectBossOffsetCoverTarget(pitAIBossPlayer boss, Vector3 bossPosition, out Vector3 target)
        {
            target = default;
            Vector3 sampledBossOffset = SampleBossOffsetPoint(bossPosition);
            CustomNavigationPoint closestPoint = BotOwner.Covers?.GetClosestPoint(sampledBossOffset, null, false, 1000);
            if (closestPoint == null || closestPoint.IsSpotted)
            {
                return false;
            }

            if ((closestPoint.Position - bossPosition).sqrMagnitude > CloseBossDistance * CloseBossDistance)
            {
                return false;
            }

            if (IsCrowded(boss, closestPoint.Position) || !TryGetPathLength(closestPoint.Position, out _))
            {
                return false;
            }

            target = closestPoint.Position;
            return true;
        }

        private bool TrySelectBossOffsetPoint(pitAIBossPlayer boss, Vector3 bossPosition, out Vector3 target)
        {
            target = default;
            if (Time.time - _lastFallbackPointEndedAt <= 10f)
            {
                return false;
            }

            Vector3 sampledBossOffset = SampleBossOffsetPoint(bossPosition);
            if (!NavMesh.SamplePosition(sampledBossOffset, out NavMeshHit navMeshHit, BossPointRadius, NavMesh.AllAreas))
            {
                return false;
            }

            if (IsCrowded(boss, navMeshHit.position) || !TryGetPathLength(navMeshHit.position, out _))
            {
                return false;
            }

            target = navMeshHit.position;
            return true;
        }

        private static Vector3 SampleBossOffsetPoint(Vector3 bossPosition)
        {
            float x = UnityEngine.Random.Range(BossOffsetMin, BossOffsetMax) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
            float z = UnityEngine.Random.Range(BossOffsetMin, BossOffsetMax) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
            return bossPosition + new Vector3(x, 0f, z);
        }

        private bool TryEvaluateCandidate(pitAIBossPlayer boss, Vector3 bossPosition, float angleDeg, float radius, ref float bestScore, ref Vector3 bestTarget)
        {
            Vector3 dir = Quaternion.Euler(0f, angleDeg, 0f) * Vector3.forward;
            Vector3 raw = bossPosition + (dir.normalized * Mathf.Clamp(radius, MinBossBuffer, MaxBossBuffer));

            if (!NavMesh.SamplePosition(raw, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
            {
                return false;
            }

            if ((hit.position - bossPosition).magnitude < MinBossBuffer)
            {
                return false;
            }

            if (IsCrowded(boss, hit.position))
            {
                return false;
            }

            if (!TryGetPathLength(hit.position, out float pathLength))
            {
                return false;
            }

            float score = Mathf.Abs((hit.position - bossPosition).magnitude - DesiredBossBuffer) * 2f + pathLength;
            if (score >= bestScore)
            {
                return false;
            }

            bestScore = score;
            bestTarget = hit.position;
            return true;
        }

        private void BuildAliveFollowerSnapshot(pitAIBossPlayer boss)
        {
            _aliveFollowers.Clear();
            var followers = boss?.Followers;
            if (followers == null || followers.Count == 0)
            {
                return;
            }

            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i];
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active)
                {
                    continue;
                }

                _aliveFollowers.Add(follower);
            }
        }

        private int GetFollowerSlotIndex()
        {
            if (_aliveFollowers.Count == 0)
            {
                return 0;
            }

            for (int i = 0; i < _aliveFollowers.Count; i++)
            {
                if (_aliveFollowers[i] == BotOwner)
                {
                    return i;
                }
            }

            return 0;
        }

        private bool IsCrowded(pitAIBossPlayer boss, Vector3 candidate)
        {
            Vector3 anchorPosition = FollowerCombatAnchor.GetAnchorPosition(BotOwner);
            if ((candidate - anchorPosition).magnitude < MinBossBuffer)
            {
                return true;
            }

            var followers = boss?.Followers;
            if (followers == null || followers.Count == 0)
            {
                return HasDestinationClaimConflict(boss, candidate, BotOwner?.ProfileId);
            }

            float spacingSqr = FollowerSpacing * FollowerSpacing;
            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i];
                if (follower == null || follower == BotOwner || follower.IsDead || follower.BotState != EBotState.Active)
                {
                    continue;
                }

                if ((follower.Position - candidate).sqrMagnitude < spacingSqr)
                {
                    return true;
                }
            }

            return HasDestinationClaimConflict(boss, candidate, BotOwner?.ProfileId);
        }

        private void RegisterDestinationClaim(pitAIBossPlayer boss, Vector3 target)
        {
            string? bossId = boss?.realPlayer?.ProfileId;
            string? followerId = BotOwner?.ProfileId;
            if (string.IsNullOrEmpty(bossId) || string.IsNullOrEmpty(followerId))
            {
                return;
            }

            _claimBossId = bossId;
            if (!DestinationClaimsByBossId.TryGetValue(bossId, out Dictionary<string, DestinationClaim>? claims))
            {
                claims = new Dictionary<string, DestinationClaim>(StringComparer.Ordinal);
                DestinationClaimsByBossId[bossId] = claims;
            }

            PruneStaleClaims(claims);
            claims[followerId] = new DestinationClaim
            {
                Position = target,
                UpdatedAt = Time.time
            };
        }

        private static bool HasDestinationClaimConflict(pitAIBossPlayer boss, Vector3 candidate, string? selfId)
        {
            string? bossId = boss?.realPlayer?.ProfileId;
            if (string.IsNullOrEmpty(bossId))
            {
                return false;
            }

            if (!DestinationClaimsByBossId.TryGetValue(bossId, out Dictionary<string, DestinationClaim>? claims) || claims.Count == 0)
            {
                return false;
            }

            PruneStaleClaims(claims);

            float spacingSqr = FollowerSpacing * FollowerSpacing;
            foreach (KeyValuePair<string, DestinationClaim> kv in claims)
            {
                if (!string.IsNullOrEmpty(selfId) && string.Equals(kv.Key, selfId, StringComparison.Ordinal))
                {
                    continue;
                }

                if ((kv.Value.Position - candidate).sqrMagnitude < spacingSqr)
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveDestinationClaim(string bossId, string? followerId)
        {
            if (string.IsNullOrEmpty(bossId) || string.IsNullOrEmpty(followerId))
            {
                return;
            }

            if (!DestinationClaimsByBossId.TryGetValue(bossId, out Dictionary<string, DestinationClaim>? claims))
            {
                return;
            }

            claims.Remove(followerId);
            if (claims.Count == 0)
            {
                DestinationClaimsByBossId.Remove(bossId);
            }
        }

        private static void PruneStaleClaims(Dictionary<string, DestinationClaim> claims)
        {
            if (claims.Count == 0)
            {
                return;
            }

            float now = Time.time;
            List<string>? staleKeys = null;
            foreach (KeyValuePair<string, DestinationClaim> kv in claims)
            {
                if (now - kv.Value.UpdatedAt <= DestinationClaimStaleSeconds)
                {
                    continue;
                }

                staleKeys ??= new List<string>(4);
                staleKeys.Add(kv.Key);
            }

            if (staleKeys == null)
            {
                return;
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                claims.Remove(staleKeys[i]);
            }
        }

        private bool HasCompletePath(Vector3 target)
        {
            return TryGetPathLength(target, out _);
        }

        private bool TryGetPathLength(Vector3 target, out float length)
        {
            length = 0f;
            if (!NavMesh.CalculatePath(BotOwner.Position, target, NavMesh.AllAreas, _path) || _path.status != NavMeshPathStatus.PathComplete)
            {
                return false;
            }

            var corners = _path.corners;
            if (corners == null || corners.Length == 0)
            {
                return false;
            }

            for (int i = 1; i < corners.Length; i++)
            {
                length += Vector3.Distance(corners[i - 1], corners[i]);
            }

            return true;
        }
    }
}

using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System.Collections;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Combat hold action used for committed cover holds, boss holds, and no-action holds.
    /// It keeps the bot stationary while selecting a useful look direction from current enemy data,
    /// recent contact, command overrides, ally pressure, or boss context.
    /// </summary>
    internal sealed class CombatHoldPositionAction : FollowerCombatActionBase
    {
        private const float UnsafeVanillaReloadDeferralSeconds = 0.25f;
        private const float PushSearchArrivalStandingSettleSeconds = 0.75f;

        private readonly HoldPosition baseLogic;
        private readonly FollowerCombatFireOverlay fireOverlay;
        private float holdStartedAt;

        public CombatHoldPositionAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new EnemyFacingHoldLogic(botOwner);
            fireOverlay = new FollowerCombatFireOverlay(botOwner);
        }

        public override void Start()
        {
            base.Start();
            holdStartedAt = Time.time;
            StopStationaryCombatMovement();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            string? reason = GetReason(data) ?? BotOwner.Brain?.Agent?.LastResult().Reason;
            bool recoveryNoCover = FollowerCombatCommon.IsRecoveryNoCoverReason(reason);
            if (recoveryNoCover)
            {
                BotOwner.SetPose(1f);
            }

            if (StopUnownedGrenadeLauncherFire(
                    reason,
                    BotOwner.Memory?.GoalEnemy,
                    blockWhenWaiting: false))
            {
                return;
            }

            DeferUnsafeVanillaHoldReload(reason);
            baseLogic.UpdateNodeByBrain(GetData<ShootHoldResultParams>(data));

            // Vanilla hold nodes can lower pose after the decision-level crouch policy has already
            // run. Reassert the shared close-threat rule before the hold fire overlay pulls the
            // trigger so regroup/boss/cover holds cannot bypass the 50m standing contract.
            EnforceCloseThreatStandingPose("holdPosition", reason);
            EnforceDistantCombatMovementStandingPose(
                "holdPosition",
                reason,
                IsPushSearchArrivalHold(reason) &&
                Time.time - holdStartedAt <= PushSearchArrivalStandingSettleSeconds);

            if (recoveryNoCover)
            {
                BotOwner.SetPose(1f);
            }

            TryPreferMarksmanPrimaryAtRange(BotOwner.Memory?.GoalEnemy, reason);
            FollowerCombatCommon.TryRaiseForStandingCoverShot(BotOwner, out _);
            fireOverlay.Update(
                BotOwner.Memory?.GoalEnemy,
                reason,
                allowThreatSuppression: recoveryNoCover || BotOwner.Memory?.IsUnderFire == true,
                forceThreatLook: true,
                out _);
        }

        private static bool IsPushSearchArrivalHold(string? reason)
        {
            return !string.IsNullOrEmpty(reason) &&
                   reason.StartsWith(
                       "committedCoverHold.push.search",
                       System.StringComparison.Ordinal);
        }

        private void DeferUnsafeVanillaHoldReload(string? reason)
        {
            if (BotOwner.WeaponManager?.Reload?.Reloading == true ||
                BotOwner.Memory?.IsInCover == true ||
                FollowerCombatCommon.IsReloadHoldReason(reason) ||
                FollowerCombatCommon.IsWeaponPreparationHoldReason(reason))
            {
                return;
            }

            bool exposedPressure = BotOwner.Memory?.IsUnderFire == true ||
                                   FollowerAwareness.WasRecentlyHit(BotOwner);
            if (!exposedPressure)
            {
                return;
            }

            // HoldPosition otherwise starts its own below-half-magazine reload based only on stale
            // real-sight time. Keep that hidden policy out of exposed pressure holds so the
            // cover-first combat reload router remains the sole owner of unsafe reload starts.
            baseLogic._nextCheckReload = Mathf.Max(
                baseLogic._nextCheckReload,
                Time.time + UnsafeVanillaReloadDeferralSeconds);
        }

        public override void Stop()
        {
            fireOverlay.Stop();
            base.Stop();
        }
    }

    /// <summary>
    /// Hold-position look controller. Vanilla hold logic is retained, but follower code decides
    /// what the bot should watch so a hold does not turn into passive staring away from the fight.
    /// </summary>
    internal sealed class EnemyFacingHoldLogic : HoldPosition
    {
        private const float SignificantCornerSwitchAngle = 20f;
        private const float CornerSwitchLockDuration = 0.45f;
        private const float CornerSwitchPersistDuration = 0.2f;
        private const float CornerSwitchScoreMargin = 0.18f;
        private const float MinCornerAlignmentScore = 0.05f;
        private const float WallFacingProbeDistance = 1.25f;
        private const float MaxCornerLookDistance = 3f;
        private const float MaxCornerEnemyLineDistance = 2f;
        private const float MinCornerEnemySectorDot = 0.35f;
        private const float PointLookLockDuration = 0.75f;
        private const float PointLookSectorSwitchPersistDuration = 0.2f;

        private EnemyInfo? currentEnemy;
        private LookTargetMode currentLookMode;
        private Vector3 currentLookPoint;
        private Vector3 currentEnemyDirection;
        private Vector3 currentCornerLookDirection;
        private int currentCornerSide;
        private int currentCoverPointId = -1;
        private bool currentEnemyVisible;
        private EnemyLookSector currentEnemySector;
        private float currentCornerAssignedAt;
        private int pendingSwitchSide;
        private float pendingSwitchSince;
        private float currentPointAssignedAt;
        private EnemyLookSector pendingPointSector;
        private float pendingPointSectorSwitchSince;

        private enum EnemyLookSector
        {
            None,
            Top,
            TopRight,
            Right,
            BottomRight,
            Bottom,
            BottomLeft,
            Left,
            TopLeft,
        }

        public EnemyFacingHoldLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Look()
        {
            if (BotFollowerPlayer.TryApplyCommandLookOverride(_owner))
            {
                return;
            }

            if (FollowerAwareness.TryGetTargetHandoffLookPoint(_owner, out Vector3 handoffLookPoint))
            {
                _owner.Steering.LookToPoint(handoffLookPoint);
                return;
            }

            if (TryLookTowardCloseUnseenThreat())
            {
                return;
            }

            if (TryLookTowardSuppressionThreat())
            {
                return;
            }

            if (TryLookTowardBossRangedThreat())
            {
                return;
            }

            if (TryLookTowardEnemy())
            {
                return;
            }

            if (FollowerAwareness.TryGetRecentThreatLookPoint(_owner, out Vector3 threatLookPoint))
            {
                _owner.Steering.LookToPoint(threatLookPoint);
                return;
            }

            if (TryGetClosestAllyLookPoint(out Vector3 allyLookPoint))
            {
                _owner.Steering.LookToPoint(allyLookPoint);
                return;
            }

            _owner.LookData.SetLookPointByHearing(null);
        }

        private bool TryLookTowardCloseUnseenThreat()
        {
            EnemyInfo? goalEnemy = _owner?.Memory?.GoalEnemy;
            if (goalEnemy?.IsVisible == true && goalEnemy.CanShoot)
            {
                return false;
            }

            if (!FollowerAwareness.TryGetRecentCloseThreatLookPoint(
                    _owner,
                    CombatDistanceConfiguration.Instance.GetTooCloseDistance(),
                    out Vector3 threatLookPoint))
            {
                return false;
            }

            _owner.Steering.LookToPoint(threatLookPoint);
            return true;
        }

        private bool TryLookTowardSuppressionThreat()
        {
            EnemyInfo? goalEnemy = _owner?.Memory?.GoalEnemy;
            if (goalEnemy?.IsVisible == true && goalEnemy.CanShoot)
            {
                return false;
            }

            if (_owner?.Memory?.IsInCover != true ||
                _owner.Memory.IsUnderFire != true)
            {
                return false;
            }

            if (!FollowerAwareness.TryGetRecentThreatLookPoint(_owner, out Vector3 threatLookPoint))
            {
                return false;
            }

            _owner.Steering.LookToPoint(threatLookPoint);
            return true;
        }

        private bool TryLookTowardBossRangedThreat()
        {
            EnemyInfo? enemy = _owner?.Memory?.GoalEnemy ?? _owner?.Memory?.LastEnemy;
            if (!FollowerAwareness.TryGetBossRangedThreatLookPoint(
                    _owner,
                    enemy,
                    out Vector3 threatLookPoint))
            {
                return false;
            }

            _owner.Steering.LookToPoint(threatLookPoint);
            return true;
        }

        private bool TryGetClosestAllyLookPoint(out Vector3 lookPoint)
        {
            lookPoint = Vector3.zero;

            BotsGroup group = _owner?.BotsGroup;
            if (group == null)
            {
                return false;
            }

            float bestDistanceSqr = float.MaxValue;
            bool found = false;

            // Required order: check own group members first.
            for (int i = 0; i < group.MembersCount; i++)
            {
                TryUpdateClosestAlly(group.Member(i), ref found, ref bestDistanceSqr, ref lookPoint);
            }

            // Then check allied groups/entries.
            if (!found)
            {
                TryCollectClosestAllyFromEnumerable(group.Allies as IEnumerable, ref found, ref bestDistanceSqr, ref lookPoint);
            }

            return found;
        }

        private void TryCollectClosestAllyFromEnumerable(
            IEnumerable source,
            ref bool found,
            ref float bestDistanceSqr,
            ref Vector3 lookPoint)
        {
            if (source == null)
            {
                return;
            }

            foreach (object item in source)
            {
                if (item is BotOwner allyOwner)
                {
                    TryUpdateClosestAlly(allyOwner, ref found, ref bestDistanceSqr, ref lookPoint);
                    continue;
                }

                if (item is BotsGroup allyGroup)
                {
                    for (int i = 0; i < allyGroup.MembersCount; i++)
                    {
                        TryUpdateClosestAlly(allyGroup.Member(i), ref found, ref bestDistanceSqr, ref lookPoint);
                    }
                }
            }
        }

        private void TryUpdateClosestAlly(
            BotOwner ally,
            ref bool found,
            ref float bestDistanceSqr,
            ref Vector3 lookPoint)
        {
            if (ally == null || ally == _owner || ally.IsDead)
            {
                return;
            }

            Vector3 targetPoint = ally.Position;
            float distanceSqr = (targetPoint - _owner.Position).sqrMagnitude;
            if (distanceSqr >= bestDistanceSqr)
            {
                return;
            }

            bestDistanceSqr = distanceSqr;
            lookPoint = targetPoint;
            found = true;
        }

        private bool TryLookTowardEnemy()
        {
            EnemyInfo enemy = _owner.Memory.GoalEnemy ?? _owner.Memory.LastEnemy;
            if (enemy == null)
            {
                ClearCurrentLook();
                return false;
            }

            if (CanKeepCurrentLook(enemy))
            {
                ApplyCurrentLook();
                return true;
            }

            if (TryAcquireNewLook(enemy))
            {
                ApplyCurrentLook();
                return true;
            }

            ClearCurrentLook();
            return false;
        }

        private bool CanKeepCurrentLook(EnemyInfo enemy)
        {
            if (!ReferenceEquals(currentEnemy, enemy))
            {
                return false;
            }

            if (currentLookMode == LookTargetMode.Corner)
            {
                return CanKeepCornerLook(enemy);
            }

            return CanKeepPointLook(enemy);
        }

        private bool CanKeepPointLook(EnemyInfo enemy)
        {
            Vector3 enemyLookPoint = GetEnemyLookPoint(enemy);
            if (enemyLookPoint == Vector3.zero)
            {
                return false;
            }

            if (enemy.IsVisible != currentEnemyVisible)
            {
                return false;
            }

            if (Time.time - currentPointAssignedAt < PointLookLockDuration)
            {
                return true;
            }

            if (!TryGetEnemyLookSector(enemyLookPoint, out EnemyLookSector enemySector))
            {
                return false;
            }

            if (!ShouldSwitchPointLookSector(enemySector))
            {
                return true;
            }

            return false;
        }

        private bool ShouldSwitchPointLookSector(EnemyLookSector enemySector)
        {
            if (enemySector == EnemyLookSector.None || enemySector == currentEnemySector)
            {
                pendingPointSector = EnemyLookSector.None;
                pendingPointSectorSwitchSince = 0f;
                return false;
            }

            if (pendingPointSector != enemySector)
            {
                pendingPointSector = enemySector;
                pendingPointSectorSwitchSince = Time.time;
                return false;
            }

            return Time.time - pendingPointSectorSwitchSince >= PointLookSectorSwitchPersistDuration;
        }

        private bool CanKeepCornerLook(EnemyInfo enemy)
        {
            if (enemy.IsVisible)
            {
                return false;
            }

            CustomNavigationPoint coverPoint = _owner.Memory.CurCustomCoverPoint;
            if (coverPoint == null || coverPoint.Id != currentCoverPointId)
            {
                return false;
            }

            if (!IsCornerUsable(coverPoint, currentCornerSide))
            {
                return false;
            }

            if (!TryGetEnemyDirection(enemy, coverPoint, out Vector3 enemyDirection))
            {
                return false;
            }

            Vector3 enemyLookPoint = GetEnemyLookPoint(enemy);
            if (!CanUseCornerLook(coverPoint, enemyLookPoint, currentCornerSide))
            {
                return false;
            }

            if (GetCornerSideScore(coverPoint, enemyDirection, currentCornerSide) < MinCornerAlignmentScore)
            {
                return false;
            }

            int preferredSide = GetPreferredCornerSide(coverPoint, enemyDirection);
            if (preferredSide == currentCornerSide)
            {
                currentEnemyDirection = enemyDirection;
                pendingSwitchSide = 0;
                return true;
            }

            if (!CanSwitchCornerSide(coverPoint, enemyDirection, preferredSide))
            {
                currentEnemyDirection = enemyDirection;
                return true;
            }

            return Vector3.Angle(currentEnemyDirection, enemyDirection) < SignificantCornerSwitchAngle;
        }

        private bool CanSwitchCornerSide(CustomNavigationPoint coverPoint, Vector3 enemyDirection, int preferredSide)
        {
            if (Time.time - currentCornerAssignedAt < CornerSwitchLockDuration)
            {
                return false;
            }

            float currentScore = GetCornerSideScore(coverPoint, enemyDirection, currentCornerSide);
            float preferredScore = GetCornerSideScore(coverPoint, enemyDirection, preferredSide);
            if (preferredScore <= currentScore + CornerSwitchScoreMargin)
            {
                pendingSwitchSide = 0;
                return false;
            }

            if (pendingSwitchSide != preferredSide)
            {
                pendingSwitchSide = preferredSide;
                pendingSwitchSince = Time.time;
                return false;
            }

            return Time.time - pendingSwitchSince >= CornerSwitchPersistDuration;
        }

        private static float GetCornerSideScore(CustomNavigationPoint coverPoint, Vector3 enemyDirection, int side)
        {
            Vector3 sideDirection;
            if (coverPoint.BordersLightHave)
            {
                sideDirection = side > 0 ? coverPoint.LeftBorderLight : coverPoint.RightBorderLight;
            }
            else
            {
                sideDirection = global::Utils.Rotate90(coverPoint.ToWallVector, side > 0 ? global::Utils.SideTurn.left : global::Utils.SideTurn.right);
            }

            sideDirection.y = 0f;
            if (sideDirection.sqrMagnitude <= 0.001f)
            {
                return -1f;
            }

            sideDirection = global::Utils.NormalizeFastSelf(sideDirection);
            return Vector3.Dot(sideDirection, enemyDirection);
        }

        private bool TryAcquireNewLook(EnemyInfo enemy)
        {
            Vector3 enemyLookPoint = GetEnemyLookPoint(enemy);
            bool hasReliableEnemyLookPoint = enemyLookPoint != Vector3.zero;
            Vector3 fallbackEnemyDirectionPoint = enemy.CurrPosition + Vector3.up * 0.8f;

            if (enemy.IsVisible)
            {
                SetPointLook(enemy, enemyLookPoint, true);
                return true;
            }

            if (hasReliableEnemyLookPoint && !WouldLookTowardPointHitWall(enemyLookPoint))
            {
                SetPointLook(enemy, enemyLookPoint, false);
                return true;
            }

            CustomNavigationPoint coverPoint = _owner.Memory.CurCustomCoverPoint;
            if (coverPoint != null && hasReliableEnemyLookPoint)
            {
                Vector3 cornerTargetPoint = hasReliableEnemyLookPoint ? enemyLookPoint : fallbackEnemyDirectionPoint;
                if (TryGetEnemyDirectionFromPoint(cornerTargetPoint, coverPoint, out Vector3 enemyDirection))
                {
                    int preferredSide = GetPreferredCornerSide(coverPoint, enemyDirection);
                    if (TrySetCornerLook(enemy, coverPoint, enemyDirection, preferredSide))
                    {
                        return true;
                    }

                    if (TrySetCornerLook(enemy, coverPoint, enemyDirection, -preferredSide))
                    {
                        return true;
                    }
                }
            }

            if (hasReliableEnemyLookPoint)
            {
                SetPointLook(enemy, enemyLookPoint, false);
                return true;
            }

            return false;
        }

        private static bool PreferLeftCorner(CustomNavigationPoint coverPoint, Vector3 enemyDirection)
        {
            if (coverPoint.BordersLightHave)
            {
                return Vector3.Angle(coverPoint.LeftBorderLight, enemyDirection) <=
                       Vector3.Angle(coverPoint.RightBorderLight, enemyDirection);
            }

            Vector3 leftDirection = global::Utils.Rotate90(coverPoint.ToWallVector, global::Utils.SideTurn.left);
            Vector3 rightDirection = global::Utils.Rotate90(coverPoint.ToWallVector, global::Utils.SideTurn.right);
            return Vector3.Dot(leftDirection, enemyDirection) >= Vector3.Dot(rightDirection, enemyDirection);
        }

        private static int GetPreferredCornerSide(CustomNavigationPoint coverPoint, Vector3 enemyDirection)
        {
            return PreferLeftCorner(coverPoint, enemyDirection) ? 1 : -1;
        }

        private bool TrySetCornerLook(EnemyInfo enemy, CustomNavigationPoint coverPoint, Vector3 enemyDirection, int side)
        {
            Vector3 enemyLookPoint = GetEnemyLookPoint(enemy);
            if (!CanUseCornerLook(coverPoint, enemyLookPoint, side))
            {
                return false;
            }

            if (!IsCornerUsable(coverPoint, side))
            {
                return false;
            }

            if (GetCornerSideScore(coverPoint, enemyDirection, side) < MinCornerAlignmentScore)
            {
                return false;
            }

            currentEnemy = enemy;
            currentLookMode = LookTargetMode.Corner;
            currentEnemyDirection = enemyDirection;
            currentCornerLookDirection = GetCornerLookDirection(coverPoint, side);
            currentCornerSide = side;
            currentCoverPointId = coverPoint.Id;
            currentEnemyVisible = false;
            currentEnemySector = GetEnemyLookSectorOrNone(enemy);
            currentCornerAssignedAt = Time.time;
            pendingSwitchSide = 0;
            pendingSwitchSince = 0f;
            return true;
        }

        private void SetPointLook(EnemyInfo enemy, Vector3 enemyLookPoint, bool enemyVisible)
        {
            currentEnemy = enemy;
            currentLookMode = LookTargetMode.Point;
            currentLookPoint = enemyLookPoint;
            currentEnemyVisible = enemyVisible;
            currentEnemySector = GetEnemyLookSectorOrNone(enemyLookPoint);
            currentCoverPointId = -1;
            currentCornerSide = 0;
            currentEnemyDirection = Vector3.zero;
            currentCornerLookDirection = Vector3.zero;
            currentCornerAssignedAt = 0f;
            pendingSwitchSide = 0;
            pendingSwitchSince = 0f;
            currentPointAssignedAt = Time.time;
            pendingPointSector = EnemyLookSector.None;
            pendingPointSectorSwitchSince = 0f;
        }

        private void ApplyCurrentLook()
        {
            if (currentLookMode == LookTargetMode.Corner)
            {
                CustomNavigationPoint coverPoint = _owner.Memory.CurCustomCoverPoint;
                if (coverPoint != null && coverPoint.Id == currentCoverPointId && IsCornerUsable(coverPoint, currentCornerSide))
                {
                    if (currentEnemy != null && TryGetEnemyDirection(currentEnemy, coverPoint, out Vector3 enemyDirection))
                    {
                        if (GetCornerSideScore(coverPoint, enemyDirection, currentCornerSide) < MinCornerAlignmentScore)
                        {
                            _owner.Steering.LookToPoint(GetEnemyLookPoint(currentEnemy));
                            return;
                        }

                        if (!CanUseCornerLook(coverPoint, GetEnemyLookPoint(currentEnemy), currentCornerSide))
                        {
                            _owner.Steering.LookToPoint(GetEnemyLookPoint(currentEnemy));
                            return;
                        }
                    }

                    if (currentCornerLookDirection.sqrMagnitude <= 0.001f)
                    {
                        currentCornerLookDirection = GetCornerLookDirection(coverPoint, currentCornerSide);
                    }

                    _owner.Steering.LookToDirection(currentCornerLookDirection);
                    return;
                }
            }

            if (currentLookMode == LookTargetMode.Point)
            {
                _owner.Steering.LookToPoint(currentLookPoint);
            }
        }

        private void ClearCurrentLook()
        {
            currentEnemy = null;
            currentLookMode = LookTargetMode.None;
            currentLookPoint = Vector3.zero;
            currentEnemyDirection = Vector3.zero;
            currentCornerLookDirection = Vector3.zero;
            currentCornerSide = 0;
            currentCoverPointId = -1;
            currentEnemyVisible = false;
            currentEnemySector = EnemyLookSector.None;
            currentCornerAssignedAt = 0f;
            pendingSwitchSide = 0;
            pendingSwitchSince = 0f;
            currentPointAssignedAt = 0f;
            pendingPointSector = EnemyLookSector.None;
            pendingPointSectorSwitchSince = 0f;
        }

        private Vector3 GetEnemyLookPoint(EnemyInfo enemy)
        {
            if (enemy.IsVisible)
            {
                return enemy.GetBodyPartPosition();
            }

            Vector3 enemyPoint = enemy.EnemyLastPositionReal;
            if (!IsUsableDirectionPoint(enemyPoint, _owner.Position))
            {
                enemyPoint = enemy.CurrPosition;
            }

            if (!IsUsableDirectionPoint(enemyPoint, _owner.Position))
            {
                return Vector3.zero;
            }

            Vector3 lookPoint = enemyPoint + Vector3.up * 0.8f;
            if (!IsUsableDirectionPoint(lookPoint, _owner.Position))
            {
                return Vector3.zero;
            }

            return lookPoint;
        }

        private bool TryGetEnemyDirection(EnemyInfo enemy, CustomNavigationPoint coverPoint, out Vector3 enemyDirection)
        {
            Vector3 enemyLookPoint = GetEnemyLookPoint(enemy);
            enemyDirection = enemyLookPoint - coverPoint.Position;
            enemyDirection.y = 0f;
            if (enemyDirection.sqrMagnitude <= 0.001f)
            {
                enemyDirection = enemyLookPoint - _owner.Position;
                enemyDirection.y = 0f;
            }

            if (enemyDirection.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            enemyDirection = global::Utils.NormalizeFastSelf(enemyDirection);
            return true;
        }

        private bool TryGetEnemyDirectionFromPoint(Vector3 enemyLookPoint, CustomNavigationPoint coverPoint, out Vector3 enemyDirection)
        {
            enemyDirection = enemyLookPoint - coverPoint.Position;
            enemyDirection.y = 0f;
            if (enemyDirection.sqrMagnitude <= 0.001f)
            {
                enemyDirection = enemyLookPoint - _owner.Position;
                enemyDirection.y = 0f;
            }

            if (enemyDirection.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            enemyDirection = global::Utils.NormalizeFastSelf(enemyDirection);
            return true;
        }

        private static bool IsUsableDirectionPoint(Vector3 point, Vector3 origin)
        {
            if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z))
            {
                return false;
            }

            if (float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z))
            {
                return false;
            }

            // EFT uses Vector3.zero as an unavailable enemy-position sentinel. Comparing it only
            // to the bot position makes world origin look valid on every normal map location.
            if (point.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            return (point - origin).sqrMagnitude > 0.01f;
        }

        private EnemyLookSector GetEnemyLookSectorOrNone(EnemyInfo enemy)
        {
            return GetEnemyLookSectorOrNone(GetEnemyLookPoint(enemy));
        }

        private EnemyLookSector GetEnemyLookSectorOrNone(Vector3 enemyLookPoint)
        {
            return TryGetEnemyLookSector(enemyLookPoint, out EnemyLookSector sector)
                ? sector
                : EnemyLookSector.None;
        }

        private bool TryGetEnemyLookSector(Vector3 enemyLookPoint, out EnemyLookSector sector)
        {
            sector = EnemyLookSector.None;

            Vector3 direction = enemyLookPoint - _owner.Position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            direction = global::Utils.NormalizeFastSelf(direction);

            Vector3 forward = _owner.Transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            forward = global::Utils.NormalizeFastSelf(forward);
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            float angle = Mathf.Atan2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward)) * Mathf.Rad2Deg;
            if (angle < 0f)
            {
                angle += 360f;
            }

            if (Enemy.Distance(_owner.Memory.GoalEnemy) <= Enemy.EnemyDistance.VeryClose)
            {
                sector = angle switch
                {
                    >= 337.5f or < 22.5f => EnemyLookSector.Top,
                    >= 22.5f and < 67.5f => EnemyLookSector.TopRight,
                    >= 67.5f and < 112.5f => EnemyLookSector.Right,
                    >= 112.5f and < 157.5f => EnemyLookSector.BottomRight,
                    >= 157.5f and < 202.5f => EnemyLookSector.Bottom,
                    >= 202.5f and < 247.5f => EnemyLookSector.BottomLeft,
                    >= 247.5f and < 292.5f => EnemyLookSector.Left,
                    _ => EnemyLookSector.TopLeft,
                };
            }
            else
            {
                sector = angle switch
                {
                    >= 315f or < 45f => EnemyLookSector.Top,
                    >= 45f and < 135f => EnemyLookSector.Right,
                    >= 135f and < 225f => EnemyLookSector.Bottom,
                    _ => EnemyLookSector.Left,
                };
            }

            return true;
        }

        private bool WouldLookTowardPointHitWall(Vector3 lookPoint)
        {
            Vector3 origin = _owner.MyHead != null ? _owner.MyHead.position : _owner.Position + Vector3.up * 1.4f;
            Vector3 direction = lookPoint - origin;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            direction = global::Utils.NormalizeFastSelf(direction);
            return Physics.Raycast(new Ray(origin, direction), WallFacingProbeDistance, LayersMaskController.HighPolyWithTerrainMask);
        }

        private bool CanUseCornerLook(CustomNavigationPoint coverPoint, Vector3 enemyLookPoint, int side)
        {
            if (coverPoint == null || enemyLookPoint == Vector3.zero || !IsCornerUsable(coverPoint, side))
            {
                return false;
            }

            Vector3 botPosition = _owner.Position;
            Vector3 botToCorner = coverPoint.Position - botPosition;
            botToCorner.y = 0f;
            if (botToCorner.sqrMagnitude > MaxCornerLookDistance * MaxCornerLookDistance)
            {
                return false;
            }

            Vector3 botToEnemy = enemyLookPoint - botPosition;
            botToEnemy.y = 0f;
            if (botToEnemy.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            float enemyDistance = botToEnemy.magnitude;
            Vector3 enemyDirectionFromBot = botToEnemy / enemyDistance;
            float alongEnemyLine = Vector3.Dot(botToCorner, enemyDirectionFromBot);
            if (alongEnemyLine <= 0f || alongEnemyLine >= enemyDistance)
            {
                return false;
            }

            Vector3 offEnemyLine = botToCorner - enemyDirectionFromBot * alongEnemyLine;
            if (offEnemyLine.sqrMagnitude > MaxCornerEnemyLineDistance * MaxCornerEnemyLineDistance)
            {
                return false;
            }

            Vector3 cornerLookDirection = GetCornerLookDirection(coverPoint, side);
            if (cornerLookDirection.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            return Vector3.Dot(cornerLookDirection, enemyDirectionFromBot) >= MinCornerEnemySectorDot;
        }

        private static bool IsCornerUsable(CustomNavigationPoint coverPoint, int side)
        {
            return side > 0 ? coverPoint.CanLookLeft : coverPoint.CanLookRight;
        }

        private Vector3 GetCornerLookDirection(CustomNavigationPoint coverPoint, int side)
        {
            Vector3 lookDirection = _owner.LookData.RotateWallBySide(coverPoint, side);
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                return global::Utils.NormalizeFastSelf(lookDirection);
            }

            Vector3 fallbackDirection = side > 0 ? coverPoint.LeftBorderLight : coverPoint.RightBorderLight;
            fallbackDirection.y = 0f;
            if (fallbackDirection.sqrMagnitude > 0.001f)
            {
                return global::Utils.NormalizeFastSelf(fallbackDirection);
            }

            return Vector3.zero;
        }

        private enum LookTargetMode
        {
            None,
            Point,
            Corner
        }
    }
}

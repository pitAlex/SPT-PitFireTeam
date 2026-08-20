using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Modules;
using pitTeam.BigBrain;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Search action for enemy-last-known areas. It wraps EFT search behavior but keeps follower
    /// movement/look state stable so a search does not immediately collapse back into passive hold.
    /// </summary>
    internal sealed class CombatSearchAction : FollowerCombatActionBase
    {
        private readonly GClass235 baseLogic;

        private const float MaxCornerThreatAngleDegrees = 35f;
        private const float MemorySearchArrivalDistanceSqr = 4f;
        private const float MemorySearchRefreshDistanceSqr = 4f;

        private bool memorySearchInitialized;
        private string memorySearchEnemyProfileId = string.Empty;
        private Vector3 memorySearchPoint;
        private float memorySearchRealReportTime;

        public CombatSearchAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass235(botOwner);
        }

        public override void Start()
        {
            base.Start();
            memorySearchInitialized = false;
            memorySearchEnemyProfileId = string.Empty;
            memorySearchPoint = Vector3.zero;
            memorySearchRealReportTime = 0f;
        }

        public override void Update(CustomLayer.ActionData data)
        {
            string? reason = GetReason(data);
            if (TryStopForPointBlankContact(reason))
            {
                return;
            }

            if (FollowerCombatPush.IsMemoryOnlySearchReason(reason))
            {
                UpdateMemoryOnlySearch(reason!);
                return;
            }

            baseLogic.UpdateNodeByBrain(GetRawData(data));
            EnforceCloseThreatStandingPose("search", reason);
            EnsureSearchMove();
            LookSimple();
        }

        private void UpdateMemoryOnlySearch(string reason)
        {
            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (goalEnemy == null ||
                (memorySearchInitialized &&
                 !string.Equals(goalEnemy.ProfileId, memorySearchEnemyProfileId, System.StringComparison.Ordinal)))
            {
                ClearSearchPointAndStop();
                return;
            }

            if (!memorySearchInitialized)
            {
                BotSearchPoint? selectedPoint = BotOwner.SearchData?.SearchPoint;
                if (selectedPoint == null || !IsFinite(selectedPoint.Position))
                {
                    ClearSearchPointAndStop();
                    return;
                }

                memorySearchInitialized = true;
                memorySearchEnemyProfileId = goalEnemy.ProfileId;
                memorySearchPoint = selectedPoint.Position;
                memorySearchRealReportTime = goalEnemy.GroupInfo?.EnemyLastSeenTimeReal ?? 0f;
                BattleRecorder.RecordCommitmentEvent(
                    BotOwner,
                    "memorySearch",
                    "commit",
                    reason,
                    target: memorySearchPoint);
            }

            TryRefreshMemorySearchPoint(goalEnemy, reason);

            Vector3 toSearchPoint = memorySearchPoint - BotOwner.Position;
            if (toSearchPoint.y < BotOwner.Settings.FileSettings.Move.Y_APPROXIMATION)
            {
                toSearchPoint.y = 0f;
            }

            if (toSearchPoint.sqrMagnitude < MemorySearchArrivalDistanceSqr)
            {
                ClearSearchPointAndStop();
                BattleRecorder.RecordCommitmentEvent(
                    BotOwner,
                    "memorySearch",
                    "arrive",
                    reason,
                    target: memorySearchPoint);
                return;
            }

            StopCombatShooting();
            EnforceCloseThreatStandingPose("memorySearch", reason);
            EnsureSearchMove();
            LookSimple(memorySearchPoint);
        }

        private void TryRefreshMemorySearchPoint(EnemyInfo goalEnemy, string reason)
        {
            float reportTime = goalEnemy.GroupInfo?.EnemyLastSeenTimeReal ?? 0f;
            Vector3 reportedPoint = goalEnemy.EnemyLastPositionReal;
            if (reportTime <= memorySearchRealReportTime ||
                !IsFinite(reportedPoint) ||
                reportedPoint.sqrMagnitude <= 0.01f ||
                (reportedPoint - memorySearchPoint).sqrMagnitude < MemorySearchRefreshDistanceSqr ||
                !NavMesh.SamplePosition(reportedPoint, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            {
                return;
            }

            memorySearchRealReportTime = reportTime;
            memorySearchPoint = hit.position;
            BotOwner.SearchData.SearchPoint = new BotSearchPoint(memorySearchPoint, EBotSearchPoint.playerPosition);
            BotOwner.SearchData.LastSearchPoint = null;
            BotOwner.SearchData.NextPosibleCheckTime = Time.time + 10f;
            BotOwner.SearchData.NextPosibleGoRefresh = 0f;
            BotOwner.SearchData.Going = false;
            BotOwner.Mover.Stop();
            BattleRecorder.RecordCommitmentEvent(
                BotOwner,
                "memorySearch",
                "refresh",
                reason,
                target: memorySearchPoint);
        }

        private void ClearSearchPointAndStop()
        {
            if (BotOwner.SearchData != null)
            {
                BotOwner.SearchData.SearchPoint = null;
                BotOwner.SearchData.Going = false;
            }

            BotOwner.Mover.Stop();
            SetCombatSprint(false);
            BotOwner.SetTargetMoveSpeed(1f);
            StopCombatShooting();
        }

        private bool TryStopForPointBlankContact(string? reason)
        {
            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (!FollowerCombatCommon.IsPointBlankContactWithoutHardSeparation(BotOwner, goalEnemy))
            {
                return false;
            }

            BotOwner.Mover.Stop();
            SetCombatSprint(false);
            BotOwner.SetTargetMoveSpeed(1f);
            EnforceCloseThreatStandingPose("search", reason, goalEnemy);
            BotOwner.SetPose(1f);
            BotOwner.Steering.LookToPoint(goalEnemy!.GetBodyPartPosition());
            StopCombatShooting();
            return true;
        }

        private void EnsureSearchMove()
        {
            BotSearchPoint? searchPoint = BotOwner.SearchData?.SearchPoint;
            if (searchPoint == null)
            {
                return;
            }

            Vector3 toSearchPoint = searchPoint.Position - BotOwner.Position;
            if (toSearchPoint.y < BotOwner.Settings.FileSettings.Move.Y_APPROXIMATION)
            {
                toSearchPoint.y = 0f;
            }

            if (toSearchPoint.sqrMagnitude < 4f || BotOwner.HasPathAndNotComplete)
            {
                return;
            }

            BotOwner.Mover.Sprint(false, true);
            BotOwner.SetTargetMoveSpeed(1f);
            NavMeshPathStatus status = BotOwner.GoToPoint(searchPoint.Position, false, -1f, true, false, true, false, false);
            BotOwner.SearchData.IsReachableLast = status == NavMeshPathStatus.PathComplete;
        }

        public void LookSimple(Vector3? committedDestination = null)
        {
            if (TryLookTowardCloseUnseenThreat(CombatDistanceConfiguration.Instance.GetTooCloseDistance()))
            {
                return;
            }

            Vector3 dest = committedDestination ??
                           (BotOwner.Memory.HaveEnemy ? BotOwner.Memory.GoalEnemy.CurrPosition : BotOwner.Position);
            Vector3 botPos = BotOwner.GetPlayer.Transform.position;
            Vector3 corner = BotOwner.Mover.CurrentCornerPoint;

            if (Utils.Covers.IsPointBetween(corner, botPos, dest))
            {
                Vector3 cornerDirection = corner - botPos;
                if (IsCornerLookAlignedWithThreat(cornerDirection, dest - botPos))
                {
                    baseLogic.BotObserveDataClass.SetVectorToLook(cornerDirection);
                }
                else
                {
                    baseLogic.BotObserveDataClass.SetVectorToLook(dest - botPos);
                }
            }
            else
            {
                baseLogic.BotObserveDataClass.SetVectorToLook(dest - botPos);
            }
            baseLogic.BotObserveDataClass.Update();
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsCornerLookAlignedWithThreat(Vector3 cornerDirection, Vector3 threatDirection)
        {
            Vector3 look = cornerDirection;
            look.y = 0f;
            Vector3 threat = threatDirection;
            threat.y = 0f;

            if (look.sqrMagnitude < 0.0001f || threat.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            look.Normalize();
            threat.Normalize();
            return Vector3.Angle(look, threat) <= MaxCornerThreatAngleDegrees;
        }
    }
}

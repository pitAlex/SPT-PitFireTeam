using EFT;
using EFT.InventoryLogic;
using pitTeam.Components;
using pitTeam.Modules;
using System;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatGrenadierObjective : FollowerCombatObjectiveBase
    {
        internal const float OpportunityWindowSeconds = 5f;
        internal const string ReasonPrefix = "objectiveGrenadier";
        private const string OrderedReasonPrefix = ReasonPrefix + ".ordered";
        private const string AutonomousReasonPrefix = ReasonPrefix + ".auto";
        private const string RetryHoldReason = "objectiveGrenadier.retry";
        private const string AutonomousActivationReason = "objectiveGrenadier.activateAuto";
        private const float RetryScanSeconds = 0.25f;
        private const float PhysicsAbortMinActiveSeconds = 0.5f;
        private const float PhysicsAbortNavSampleRadius = 3f;
        private const float PhysicsAbortNavDrop = 3f;
        private const float PhysicsAbortVerticalDrop = 6f;
        private const float PhysicsAbortStationaryHorizontal = 4f;
        private const float PhysicsAbortBossDistance = 60f;
        private const float PhysicsAbortBossDistanceJump = 30f;
        private const float PhysicsAbortBossVerticalDrop = 10f;

        private bool complete;
        private bool active;
        private bool ordered;
        private bool negativeSaid;
        private bool cooldownRecorded;
        private bool launcherReady;
        private float activeUntil;
        private float physicsTrackStartedAt;
        private float highestObservedY;
        private float lastBossDistance;
        private float retryScanUntil;
        private Vector3 highestObservedPosition;
        private FollowerCombatCommon.GrenadeLauncherFirePlan? launcherPlan;

        public FollowerCombatGrenadierObjective(BotOwner botOwner, FollowerCombatCommon combatCommon)
            : base(botOwner, combatCommon)
        {
        }

        public override bool IsComplete => complete;

        public override void Reset()
        {
            complete = false;
            active = false;
            ordered = false;
            negativeSaid = false;
            cooldownRecorded = false;
            launcherReady = false;
            activeUntil = 0f;
            physicsTrackStartedAt = 0f;
            highestObservedY = float.NegativeInfinity;
            lastBossDistance = 0f;
            retryScanUntil = 0f;
            highestObservedPosition = Vector3.zero;
            launcherPlan = null;
        }

        public override void Activate()
        {
            Activate(ordered: false);
        }

        public void Activate(bool ordered)
        {
            Reset();
            active = true;
            this.ordered = ordered;
            ResetPhysicsTracking();
            ClearObjectiveCommitments();
        }

        public override void Deactivate()
        {
            DeactivateForObjectiveSwitch("deactivate");
        }

        public void DeactivateForObjectiveSwitch(string reason)
        {
            if (!active)
            {
                return;
            }

            RecordAttemptCooldown(reason);
            active = false;
            CombatCommon.PrepareLauncherSuppressWeaponFallback();
            ClearObjectiveCommitments();
            complete = false;
            launcherPlan = null;
        }

        public override void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            CombatCommon.HandleSharedDecisionChanged(nextDecision);
            if (IsLauncherMoveReason(nextDecision.Reason))
            {
                return;
            }

            if (IsLauncherPreparationReason(nextDecision.Reason))
            {
                return;
            }

            CombatCommon.HandleFollowerSuppressDecisionChanged(nextDecision);
        }

        public override void StartDecision()
        {
        }

        public override AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy)
        {
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return FailObjective("noEnemy");
            }

            if (TryGetEmergencyDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> emergencyDecision))
            {
                // A first-primary launcher must not remain in hand while the shared combat stack
                // retreats, heals, reloads, or enters a point-blank fight. The support launcher
                // path is unaffected because this helper only accepts the first-primary launcher.
                string emergencyReason = emergencyDecision.Reason ?? emergencyDecision.Action.ToString();
                CombatCommon.RequestFirstPrimaryLauncherHolsterFallback(
                    $"grenadierEmergency.{emergencyReason}");
                if (CombatCommon.TryCreatePendingFirstPrimaryLauncherHolsterFallbackDecision(
                        out AICoreActionResultStruct<BotLogicDecision, GClass26> holsterFallbackDecision))
                {
                    emergencyDecision = holsterFallbackDecision;
                }

                complete = true;
                RecordAttemptCooldown($"emergency.{emergencyReason}");
                CombatCommon.PrepareLauncherSuppressWeaponFallback();
                ClearObjectiveCommitments();
                return emergencyDecision;
            }

            if (!launcherReady)
            {
                if (!CombatCommon.TryPrepareGrenadeLauncherWeaponForSuppress(
                        GetModeReasonPrefix(),
                        out AICoreActionResultStruct<BotLogicDecision, GClass26> prepareDecision,
                        out bool ready,
                        out string failReason))
                {
                    return FailObjective(failReason);
                }

                if (!ready)
                {
                    return prepareDecision;
                }

                launcherReady = true;
                activeUntil = Time.time + OpportunityWindowSeconds;
            }

            if (Time.time >= activeUntil)
            {
                return FailObjective("windowExpired");
            }

            if (TryGetLauncherDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> launcherDecision))
            {
                return launcherDecision;
            }

            return RetryOrFail("noOpportunity");
        }

        public override AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            if (!IsGrenadierReason(currentDecision.Reason))
            {
                return CombatCommon.ShallEndCurrentDecision(currentDecision);
            }

            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                complete = true;
                ClearObjectiveCommitments();
                return new AICoreActionEndStruct("grenadierEnemyMissing", true);
            }

            if (currentDecision.Action == BotLogicDecision.shootFromPlace)
            {
                return EndLauncherFire(currentDecision.Reason);
            }

            if (currentDecision.Action == BotLogicDecision.goToPoint &&
                IsLauncherMoveReason(currentDecision.Reason))
            {
                return EndLauncherMove();
            }

            if (currentDecision.Action == BotLogicDecision.holdPosition &&
                IsRetryHoldReason(currentDecision.Reason))
            {
                return EndRetryHold();
            }

            if (currentDecision.Action == BotLogicDecision.holdPosition)
            {
                return new AICoreActionEndStruct("grenadierHoldComplete", true);
            }

            return CombatCommon.ShallEndCurrentDecision(currentDecision);
        }

        internal static bool IsAutonomousActivationReason(string? reason)
        {
            return string.Equals(reason, AutonomousActivationReason, StringComparison.Ordinal);
        }

        internal static AICoreActionResultStruct<BotLogicDecision, GClass26> CreateAutonomousActivationDecision()
        {
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                AutonomousActivationReason);
        }

        private bool TryGetEmergencyDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (TryGetPhysicsAbortDecision(out decision))
            {
                return true;
            }

            if (CombatCommon.TryGetDogFightDecision() is { } dogFightDecision)
            {
                decision = dogFightDecision;
                return true;
            }

            if (CombatCommon.TryGetNeedHealDecision() is { } healDecision)
            {
                decision = healDecision;
                return true;
            }

            if (CombatCommon.TryGetReloadRetreatDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> reloadRetreatDecision))
            {
                decision = reloadRetreatDecision;
                return true;
            }

            return false;
        }

        private void ResetPhysicsTracking()
        {
            Vector3 position = BotOwner.Position;
            physicsTrackStartedAt = Time.time;
            highestObservedY = position.y;
            highestObservedPosition = position;
            Vector3 bossPosition = CombatCommon.GetBossPosition();
            lastBossDistance = FollowerCombatCommon.IsFinite(bossPosition)
                ? Vector3.Distance(position, bossPosition)
                : 0f;
        }

        private bool TryGetPhysicsAbortDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            Vector3 position = BotOwner.Position;
            if (!FollowerCombatCommon.IsFinite(position))
            {
                return CreatePhysicsAbortDecision("invalidPosition", out decision);
            }

            if (physicsTrackStartedAt <= 0f)
            {
                ResetPhysicsTracking();
                return false;
            }

            if (position.y > highestObservedY)
            {
                highestObservedY = position.y;
                highestObservedPosition = position;
            }

            float activeSeconds = Time.time - physicsTrackStartedAt;
            if (activeSeconds < PhysicsAbortMinActiveSeconds)
            {
                UpdateLastBossDistance(position);
                return false;
            }

            float verticalDrop = highestObservedY - position.y;
            float horizontalFromHigh = DistanceXZ(position, highestObservedPosition);
            bool noNearbyNavMesh =
                verticalDrop >= PhysicsAbortNavDrop &&
                !NavMesh.SamplePosition(position, out _, PhysicsAbortNavSampleRadius, NavMesh.AllAreas);
            if (noNearbyNavMesh)
            {
                return CreatePhysicsAbortDecision($"noNearbyNavMesh:drop={verticalDrop:0.0}", out decision);
            }

            if (verticalDrop >= PhysicsAbortVerticalDrop &&
                horizontalFromHigh <= PhysicsAbortStationaryHorizontal)
            {
                return CreatePhysicsAbortDecision($"stationaryVerticalDrop:drop={verticalDrop:0.0}", out decision);
            }

            Vector3 bossPosition = CombatCommon.GetBossPosition();
            if (FollowerCombatCommon.IsFinite(bossPosition))
            {
                float bossDistance = Vector3.Distance(position, bossPosition);
                float bossVerticalDrop = bossPosition.y - position.y;
                if (bossVerticalDrop >= PhysicsAbortBossVerticalDrop &&
                    bossDistance >= PhysicsAbortBossDistance &&
                    (lastBossDistance <= 0f || bossDistance - lastBossDistance >= PhysicsAbortBossDistanceJump))
                {
                    return CreatePhysicsAbortDecision(
                        $"bossDistanceJump:distance={bossDistance:0.0}:vertical={bossVerticalDrop:0.0}",
                        out decision);
                }

                lastBossDistance = bossDistance;
            }

            return false;
        }

        private void UpdateLastBossDistance(Vector3 position)
        {
            Vector3 bossPosition = CombatCommon.GetBossPosition();
            if (FollowerCombatCommon.IsFinite(bossPosition))
            {
                lastBossDistance = Vector3.Distance(position, bossPosition);
            }
        }

        private bool CreatePhysicsAbortDecision(
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            BattleRecorder.RecordObjectiveDiagnostic(
                BotOwner,
                nameof(FollowerCombatGrenadierObjective),
                "physicsAbort",
                reason);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                FollowerCombatRegroupObjective.ActivateRegroupReason);
            return true;
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private bool TryGetLauncherDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (launcherPlan != null && TryUseLauncherPlan(goalEnemy, launcherPlan, out decision))
            {
                return true;
            }

            launcherPlan = null;
            if (!CombatCommon.TryPrepareGrenadeLauncherFirePlan(
                    goalEnemy,
                    GetModeReasonPrefix(),
                    ordered,
                    out FollowerCombatCommon.GrenadeLauncherFirePlan? preparedPlan) ||
                preparedPlan == null)
            {
                return false;
            }

            launcherPlan = preparedPlan;
            return TryUseLauncherPlan(goalEnemy, preparedPlan, out decision);
        }

        private bool TryUseLauncherPlan(
            EnemyInfo goalEnemy,
            FollowerCombatCommon.GrenadeLauncherFirePlan plan,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (plan.HasSuppressFrom &&
                !CombatCommon.IsAtGrenadeLauncherFirePosition(plan))
            {
                decision = CombatCommon.CreateGrenadeLauncherMoveDecision(plan);
                return true;
            }

            if (CombatCommon.TryStartGrenadeLauncherFireDecision(goalEnemy, plan, out decision))
            {
                return true;
            }

            launcherPlan = null;
            return false;
        }

        private AICoreActionEndStruct EndLauncherFire(string? reason)
        {
            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                complete = true;
                launcherPlan = null;
                ClearObjectiveCommitments();
                return new AICoreActionEndStruct("grenadierEnemyMissing", true);
            }

            Weapon? launcher = FollowerCombatCommon.GetActiveOrEquippedGrenadeLauncher(BotOwner);
            if (FollowerCombatCommon.CountLoadedRounds(launcher) <= 0)
            {
                if (FollowerCombatCommon.IsSingleUseLauncherWeapon(launcher))
                {
                    complete = true;
                    RecordAttemptCooldown("launcherSingleUseSpent");
                    CombatCommon.PrepareLauncherSuppressWeaponFallback();
                    CombatCommon.RequestFirstPrimaryLauncherHolsterFallback("launcherSingleUseSpent");
                    ClearObjectiveCommitments();
                    return new AICoreActionEndStruct("launcherSingleUseSpent", true);
                }

                // Leave the ordinary shooting node as soon as the cylinder/barrel is empty. The
                // objective preparation step owns the bounded loose-ammo reload and then returns to
                // normal fire without completing a one-shot suppression task.
                launcherReady = false;
                launcherPlan = null;
                activeUntil = Time.time + OpportunityWindowSeconds;
                return new AICoreActionEndStruct("launcherNeedsReload", true);
            }

            if (!FollowerCombatCommon.TryCanUseGrenadeLauncherNormalFire(
                    BotOwner,
                    goalEnemy,
                    ordered,
                    out _,
                    out string fireRejectReason))
            {
                bool continueCommittedPrimaryShot =
                    string.Equals(fireRejectReason, "enemyNotVisible", StringComparison.Ordinal) &&
                    FollowerCombatCommon.TryContinueFirstPrimaryGrenadeLauncherNormalFire(
                        BotOwner,
                        goalEnemy,
                        ordered,
                        out _,
                        out _);
                if (continueCommittedPrimaryShot)
                {
                    return default;
                }

                launcherPlan = null;
                return new AICoreActionEndStruct($"launcherNormalFireRejected:{fireRejectReason}", true);
            }

            // CombatShootFromPlaceAction runs EFT's normal aim-and-trigger worker directly after
            // launcher arc safety succeeds. Do not delegate to EndShootFromPlace here because that
            // outer rifle end gate rejects the same valid arc whenever GoalEnemy.CanShoot is false.
            return default;
        }

        private AICoreActionEndStruct EndLauncherMove()
        {
            if (launcherPlan == null)
            {
                launcherPlan = null;
                return new AICoreActionEndStruct("grenadierMovePlanMissing", true);
            }

            if (CombatCommon.IsAtGrenadeLauncherFirePosition(launcherPlan))
            {
                return new AICoreActionEndStruct("grenadierMoveArrived", true);
            }

            AICoreActionEndStruct moveEnd = CombatCommon.EndGoToPoint(endWhenEnemyVisibleShootable: false);
            if (moveEnd.Value)
            {
                CombatCommon.ClearCommittedMovement();
            }

            return moveEnd.Value ? moveEnd : default;
        }

        private AICoreActionEndStruct EndRetryHold()
        {
            if (Time.time >= retryScanUntil)
            {
                return new AICoreActionEndStruct("grenadierRetryScan", true);
            }

            CombatCommon.HoldFor(Mathf.Max(0.05f, retryScanUntil - Time.time));
            return default;
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> RetryOrFail(string suffix)
        {
            if (Time.time >= activeUntil)
            {
                return FailObjective(suffix);
            }

            retryScanUntil = Mathf.Min(activeUntil, Time.time + RetryScanSeconds);
            LookTowardEnemy();
            CombatCommon.HoldFor(Mathf.Max(0.05f, retryScanUntil - Time.time));
            BattleRecorder.RecordObjectiveDiagnostic(
                BotOwner,
                nameof(FollowerCombatGrenadierObjective),
                "retry",
                suffix);
            return Hold($"retry.{suffix}");
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> FailObjective(string suffix)
        {
            complete = true;
            RecordAttemptCooldown($"fail.{suffix}");
            CombatCommon.PrepareLauncherSuppressWeaponFallback();
            CombatCommon.RequestFirstPrimaryLauncherHolsterFallback($"grenadierFail.{suffix}");
            ClearObjectiveCommitments();
            BattleRecorder.RecordObjectiveDiagnostic(
                BotOwner,
                nameof(FollowerCombatGrenadierObjective),
                "reject",
                suffix);
            if (ordered)
            {
                SayNegativeOnce();
            }

            return Hold(suffix);
        }

        private void SayNegativeOnce()
        {
            if (negativeSaid)
            {
                return;
            }

            negativeSaid = true;
            BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
            BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
        }

        private void LookTowardEnemy()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return;
            }

            Vector3 enemyPosition = FollowerCombatCommon.GetEnemyCurrentPosition(goalEnemy);
            if (FollowerCombatCommon.IsFinite(enemyPosition))
            {
                BotOwner.Steering.LookToPoint(enemyPosition);
            }
        }

        private void ClearObjectiveCommitments()
        {
            CombatCommon.ClearFollowerSuppressState();
            CombatCommon.ClearCommittedMovement();
            CombatCommon.ClearCommittedPosition();
            CombatCommon.ClearInitialDecision();
        }

        private void RecordAttemptCooldown(string reason)
        {
            if (cooldownRecorded)
            {
                return;
            }

            // A launcher occupying FirstPrimaryWeapon is the follower's ordinary combat weapon.
            // Normal action completion, enemy loss, healing, or an objective switch must not apply
            // the old support-suppression cooldown. Only a genuine failed opportunity uses the
            // cooldown so the loaded-pistol fallback is not immediately undone.
            if (FollowerCombatCommon.HasUsableFirstPrimaryGrenadeLauncher(BotOwner) &&
                !reason.StartsWith("fail.", StringComparison.Ordinal))
            {
                return;
            }

            cooldownRecorded = true;
            CombatCommon.StartGrenadeLauncherSuppressCooldown(ordered, reason);
            if (ordered)
            {
                CombatCommon.StartGrenadeLauncherSuppressCooldown(ordered: false, $"ordered.{reason}");
            }
        }

        internal static bool IsGrenadierReason(string? reason)
        {
            return reason != null && reason.StartsWith(ReasonPrefix, StringComparison.Ordinal);
        }

        internal static bool IsOrderedGrenadierReason(string? reason)
        {
            return reason != null && reason.StartsWith(OrderedReasonPrefix, StringComparison.Ordinal);
        }

        internal static bool IsAutonomousGrenadierReason(string? reason)
        {
            return reason != null && reason.StartsWith(AutonomousReasonPrefix, StringComparison.Ordinal);
        }

        private string GetModeReasonPrefix()
        {
            return ordered ? OrderedReasonPrefix : AutonomousReasonPrefix;
        }

        private static bool IsLauncherMoveReason(string? reason)
        {
            return IsGrenadierReason(reason) &&
                   reason.EndsWith(".launcherMove", StringComparison.Ordinal);
        }

        private static bool IsLauncherPreparationReason(string? reason)
        {
            return IsGrenadierReason(reason) &&
                   (reason.EndsWith(".launcherSwitch", StringComparison.Ordinal) ||
                    reason.EndsWith(".launcherReload", StringComparison.Ordinal));
        }

        private static bool IsRetryHoldReason(string? reason)
        {
            return reason != null && reason.StartsWith(RetryHoldReason, StringComparison.Ordinal);
        }

        private static AICoreActionResultStruct<BotLogicDecision, GClass26> Hold(string suffix)
        {
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                $"{ReasonPrefix}.{suffix}");
        }
    }
}

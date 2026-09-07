using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// BigBrain action payload used by follower combat actions. It carries the selected decision,
    /// human-readable reason, and optional vanilla node data through the custom layer boundary.
    /// </summary>
    internal sealed class FollowerCombatActionData : CustomLayer.ActionData
    {
        public BotLogicDecision Decision { get; }
        public string Reason { get; }
        public CoreActionResultParams? Data { get; }

        public FollowerCombatActionData(BotLogicDecision decision, string reason, CoreActionResultParams? data)
        {
            Decision = decision;
            Reason = reason;
            Data = data;
        }
    }

    /// <summary>
    /// Base class for follower combat actions. It centralizes common interop with vanilla combat
    /// nodes: safe sprint toggling, combat cover tactic setup, typed action data access, shot cleanup,
    /// primary-weapon preference, and short aim-alignment waits.
    /// </summary>
    internal abstract class FollowerCombatActionBase : CustomLogic
    {
        private const float DistantCombatMovementStandingDistance = 25f;

        private float nextUnownedLauncherGuardRecordAt;
        private bool closeThreatStandingRecorded;
        private string? closeThreatStandingRecordReason;
        private bool distantMovementStandingRecorded;
        private string? distantMovementStandingRecordReason;

        protected FollowerCombatActionBase(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            closeThreatStandingRecorded = false;
            closeThreatStandingRecordReason = null;
            distantMovementStandingRecorded = false;
            distantMovementStandingRecordReason = null;
            base.Start();
        }

        protected sealed class FallbackRunRestoreGate
        {
            private const float NoThreatRestoreSeconds = 3f;
            private const float StableRunSeconds = 1.5f;
            private const float StableRunWindowStartSeconds = NoThreatRestoreSeconds - StableRunSeconds;

            private float noThreatSince;
            private float canRunStableSince;

            public void Reset()
            {
                noThreatSince = 0f;
                canRunStableSince = 0f;
            }

            public bool ShouldRestoreToRun(bool canRun, EnemyInfo? goalEnemy)
            {
                if (HasActiveThreatContact(goalEnemy))
                {
                    Reset();
                    return false;
                }

                if (noThreatSince <= 0f)
                {
                    noThreatSince = Time.time;
                    canRunStableSince = 0f;
                    return false;
                }

                if (Time.time - noThreatSince < StableRunWindowStartSeconds)
                {
                    canRunStableSince = 0f;
                    return false;
                }

                if (!canRun)
                {
                    canRunStableSince = 0f;
                    return false;
                }

                if (canRunStableSince <= 0f)
                {
                    canRunStableSince = Time.time;
                    return false;
                }

                return Time.time - noThreatSince >= NoThreatRestoreSeconds &&
                       Time.time - canRunStableSince >= StableRunSeconds;
            }

            private static bool HasActiveThreatContact(EnemyInfo? goalEnemy)
            {
                return goalEnemy?.Person?.HealthController?.IsAlive == true &&
                       (goalEnemy.IsVisible || goalEnemy.CanShoot);
            }
        }

        protected void SetCombatSprint(bool sprint, bool withDebugCallback = false)
        {
            bool moverRequestedSprint = BotOwner.Mover.Sprinting;
            bool playerSprintEngaged = IsActuallySprinting(BotOwner);
            if (sprint && moverRequestedSprint && playerSprintEngaged) return;
            else if (!sprint && !moverRequestedSprint && !playerSprintEngaged) return;
            if (sprint)
            {
                BotOwner.SetPose(1f);
                BotOwner.SetTargetMoveSpeed(1f);
            }

            // Use the mover directly for follower combat run actions. BotOwner.Sprint(true)
            // drops current aiming target every tick, which can fight combat steering and turn
            // a run decision into a walk-looking movement state.
            BotOwner.Mover.Sprint(sprint, withDebugCallback);
        }

        internal static bool IsActuallySprinting(BotOwner? botOwner)
        {
            Player? player = botOwner?.GetPlayer ?? botOwner?.AIData?.Player;
            if (player?.MovementContext != null)
            {
                return player.MovementContext.IsSprintEnabled;
            }

            return botOwner?.Mover?.Sprinting == true;
        }

        internal static bool IsDoorInteractionBlockingSprint(DoorInteractionStatus status)
        {
            int rawStatus = (int)status;
            return status == DoorInteractionStatus.OpeningDoor ||
                   (rawStatus != 0 && status != DoorInteractionStatus.CanRun);
        }

        internal static bool TryGetCurrentShotVector(
            BotOwner? botOwner,
            out Vector3 fireOrigin,
            out Vector3 pointDirection)
        {
            if (TryGetActualFirearmShotVector(botOwner, out fireOrigin, out pointDirection))
            {
                return true;
            }

            Player? player = botOwner?.GetPlayer ?? botOwner?.AIData?.Player;
            fireOrigin = botOwner?.WeaponRoot != null
                ? botOwner.WeaponRoot.position
                : (botOwner?.Position ?? Vector3.zero) + Vector3.up * 1.2f;
            pointDirection = player?.LookDirection ?? botOwner?.LookDirection ?? Vector3.zero;
            if (pointDirection.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            pointDirection.Normalize();
            return true;
        }

        internal static bool TryGetActualFirearmShotVector(
            BotOwner? botOwner,
            out Vector3 fireOrigin,
            out Vector3 pointDirection)
        {
            Player? player = botOwner?.GetPlayer ?? botOwner?.AIData?.Player;
            if (player?.HandsController is Player.FirearmController firearmController &&
                firearmController.CurrentFireport != null)
            {
                fireOrigin = firearmController.CurrentFireport.position;
                pointDirection = firearmController.CurrentFireport.Original.TransformDirection(player.LocalShotDirection);
                firearmController.AdjustShotVectors(ref fireOrigin, ref pointDirection);
                if (pointDirection.sqrMagnitude > 0.0001f)
                {
                    pointDirection.Normalize();
                    return true;
                }
            }

            fireOrigin = Vector3.zero;
            pointDirection = Vector3.zero;
            return false;
        }

        protected CoverSearchType SetAttackCoverSearchType(CoverShootType shootType)
        {
            SetCombatCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            return BotOwner.Tactic.SubTactic.SearchTypeAttack(shootType);
        }

        protected void SetCombatCoverTactic(BotsGroup.BotCurrentTactic tactic)
        {
            if (BotOwner.Tactic._shallReturnToAttack && tactic != BotsGroup.BotCurrentTactic.Ambush)
            {
                BotOwner.Tactic._shallReturnToAttack = false;
                BotOwner.Tactic._returnToAttackTime = 0f;
            }

            BotOwner.Tactic.SetTactic(tactic);
        }

        protected static CoreActionResultParams? GetRawData(CustomLayer.ActionData data)
        {
            return (data as FollowerCombatActionData)?.Data;
        }

        protected static string? GetReason(CustomLayer.ActionData data)
        {
            return (data as FollowerCombatActionData)?.Reason;
        }

        protected static TData? GetData<TData>(CustomLayer.ActionData data) where TData : CoreActionResultParams
        {
            return GetRawData(data) as TData;
        }

        protected void StopCombatShooting()
        {
            FollowerRecovery.StopShooting(BotOwner);
        }

        protected bool EnforceCloseThreatStandingPose(
            string action,
            string? actionReason = null,
            EnemyInfo? goalEnemy = null)
        {
            goalEnemy ??= BotOwner?.Memory?.GoalEnemy;
            if (!FollowerShootPoseSafety.ShouldForceStandingForCloseThreat(
                    BotOwner,
                    goalEnemy,
                    out string policyReason,
                    out float enemyDistance,
                    out Vector3? target))
            {
                closeThreatStandingRecorded = false;
                closeThreatStandingRecordReason = null;
                return false;
            }

            bool lowTargetPose = BotOwner.Mover?.TargetPose < 0.85f;
            bool prone = BotOwner.GetPlayer?.MovementContext?.IsInPronePose == true;
            if (!lowTargetPose && !prone)
            {
                return true;
            }

            if (prone)
            {
                BotOwner.BotLay?.GetUp(false);
            }

            BotOwner.SetPose(1f);

            string recordReason = string.IsNullOrEmpty(actionReason)
                ? policyReason
                : $"{policyReason}:{actionReason}";
            if (!closeThreatStandingRecorded ||
                !string.Equals(
                    closeThreatStandingRecordReason,
                    recordReason,
                    System.StringComparison.Ordinal))
            {
                closeThreatStandingRecorded = true;
                closeThreatStandingRecordReason = recordReason;
                BattleRecorder.RecordCombatPosturePolicy(
                    BotOwner,
                    action,
                    "crouch",
                    false,
                    recordReason,
                    enemyDistance,
                    target);
            }

            return true;
        }

        /// <summary>
        /// Search travel and its short arrival settle should not become distant crouch-walking.
        /// Callers explicitly prove that translation is expected; stationary cover/fire posture is
        /// intentionally left to the existing pose and firing-lane policies.
        /// </summary>
        protected bool EnforceDistantCombatMovementStandingPose(
            string action,
            string? actionReason,
            bool movementExpected,
            EnemyInfo? goalEnemy = null)
        {
            goalEnemy ??= BotOwner?.Memory?.GoalEnemy;
            float enemyDistance = goalEnemy?.Distance ?? 0f;
            if (!movementExpected ||
                goalEnemy?.Person?.HealthController?.IsAlive != true)
            {
                distantMovementStandingRecorded = false;
                distantMovementStandingRecordReason = null;
                return false;
            }

            if (enemyDistance <= 0f)
            {
                enemyDistance = Vector3.Distance(BotOwner.Position, goalEnemy.CurrPosition);
            }

            if (enemyDistance < DistantCombatMovementStandingDistance)
            {
                distantMovementStandingRecorded = false;
                distantMovementStandingRecordReason = null;
                return false;
            }

            bool lowTargetPose = BotOwner.Mover?.TargetPose < 0.85f;
            bool prone = BotOwner.GetPlayer?.MovementContext?.IsInPronePose == true;
            if (!lowTargetPose && !prone)
            {
                return true;
            }

            if (prone)
            {
                BotOwner.BotLay?.GetUp(false);
            }

            BotOwner.SetPose(1f);
            BotOwner.Mover?.SetPose(1f);

            string recordReason = string.IsNullOrEmpty(actionReason)
                ? "distantMovement"
                : $"distantMovement:{actionReason}";
            if (!distantMovementStandingRecorded ||
                !string.Equals(
                    distantMovementStandingRecordReason,
                    recordReason,
                    System.StringComparison.Ordinal))
            {
                distantMovementStandingRecorded = true;
                distantMovementStandingRecordReason = recordReason;
                BattleRecorder.RecordCombatPosturePolicy(
                    BotOwner,
                    action,
                    "crouchMove",
                    false,
                    recordReason,
                    enemyDistance,
                    FollowerCombatCommon.GetEnemyAnchor(goalEnemy));
            }

            return true;
        }

        protected void StopStationaryCombatMovement()
        {
            if (BotOwner == null)
            {
                return;
            }

            BotOwner.GoToSomePointData?.SetPoint(BotOwner.Position);
            BotOwner.GoToSomePointData?.UpdateToGo(false);
            BotOwner.StopMove();

            if (BotOwner.Mover?.Sprinting == true)
            {
                BotOwner.Mover.Sprint(false, false);
            }
        }

        protected bool StopUnownedGrenadeLauncherFire(
            string? reason,
            EnemyInfo? goalEnemy = null,
            bool blockWhenWaiting = true)
        {
            if (FollowerCombatCommon.IsGrenadeLauncherCombatReason(reason))
            {
                return false;
            }

            bool firstPrimaryLauncher =
                FollowerCombatCommon.IsFirstPrimaryGrenadeLauncherSelectedOrActive(BotOwner);
            bool supportLauncher =
                FollowerCombatCommon.IsSupportGrenadeLauncherSelectedOrActive(BotOwner);
            if (!firstPrimaryLauncher && !supportLauncher)
            {
                return false;
            }

            StopCombatShooting();
            if (firstPrimaryLauncher)
            {
                if (Time.time >= nextUnownedLauncherGuardRecordAt)
                {
                    nextUnownedLauncherGuardRecordAt = Time.time + 2f;
                    BattleRecorder.RecordGrenadeEvent(
                        BotOwner,
                        "launcherReject",
                        $"primaryLauncherOutsideGrenadier:{reason ?? "unknown"}",
                        goalEnemy: goalEnemy);
                }

                // A first-primary launcher belongs in the Grenadier objective. That objective
                // validates explosive safety before deliberately returning the ordinary fire node.
                return blockWhenWaiting;
            }

            bool switched = FollowerCombatCommon.TrySwitchSelectedGrenadeLauncherToPrimaryForOpportunity(
                BotOwner,
                goalEnemy,
                reason,
                tacticalIntent: true,
                out string waitReason);

            if (Time.time >= nextUnownedLauncherGuardRecordAt)
            {
                nextUnownedLauncherGuardRecordAt = Time.time + 2f;
                BattleRecorder.RecordGrenadeEvent(
                    BotOwner,
                    "launcherReject",
                    switched
                        ? $"unownedLauncherSelection:{reason ?? "unknown"}:switched"
                        : $"unownedLauncherSelection:{reason ?? "unknown"}:wait={waitReason}",
                    goalEnemy: goalEnemy);
            }

            return switched || blockWhenWaiting;
        }

        protected bool StopIfFriendlyInCurrentFireLane(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null)
            {
                return false;
            }

            ShootToPoint? shootPoint = BotOwner.CurrentEnemyTargetPosition(false);
            Vector3 target = shootPoint?.Point ?? goalEnemy.GetBodyPartPosition();
            return StopIfFriendlyInCurrentFireLane(target);
        }

        protected bool StopIfFriendlyInCurrentFireLane(Vector3 target)
        {
            Vector3 fireOrigin = BotOwner.WeaponRoot != null
                ? BotOwner.WeaponRoot.position
                : BotOwner.Position + Vector3.up * 1.2f;

            if (FollowerShotSafety.IsFriendlyInShotLane(BotOwner, fireOrigin, target))
            {
                StopCombatShooting();
                return true;
            }

            Vector3 aimDirection = BotOwner.LookDirection;
            if (aimDirection.sqrMagnitude <= 0.0001f && BotOwner.Transform != null)
            {
                aimDirection = BotOwner.Transform.forward;
            }

            float distance = Vector3.Distance(fireOrigin, target);
            if (FollowerShotSafety.IsFriendlyInAimLane(BotOwner, fireOrigin, aimDirection, distance))
            {
                StopCombatShooting();
                return true;
            }

            return false;
        }

        protected void TryPreferPrimaryAtRange(EnemyInfo? goalEnemy, string? reason = null)
        {
            if (goalEnemy == null)
            {
                return;
            }

            if (BossPlayers.Instance?.GetFollower(BotOwner)?.CombatTactic == FollowerCombatTactic.Marksman)
            {
                TryPreferMarksmanPrimaryAtRange(goalEnemy, reason);
                return;
            }

            if ((FollowerCombatPush.IsPushReason(reason) ||
                 FollowerCombatPush.IsStartWeakEnemyPushReason(reason)) &&
                FollowerCombatCommon.TrySwitchToPushReadyLongGun(BotOwner))
            {
                return;
            }

            if (ShouldKeepAutomaticSecondaryForPush(reason))
            {
                return;
            }

            BotWeaponSelector? selector = BotOwner?.WeaponManager?.Selector;
            if (selector == null)
            {
                return;
            }

            if (BotOwner.WeaponManager.IsMelee)
            {
                selector.ChangeToMain();
                return;
            }

            if (ShouldRespectVanillaSupportWeaponFallback(selector))
            {
                return;
            }

            selector.TryChangeToMain();
        }

        protected bool HoldPushMovementUntilLongGunReady(string? reason)
        {
            if (!FollowerCombatPush.IsPushReason(reason) &&
                !FollowerCombatPush.IsStartWeakEnemyPushReason(reason))
            {
                return false;
            }

            if (FollowerCombatCommon.IsPushReadyLongGunActive(BotOwner) &&
                BotOwner.WeaponManager?.Selector?.IsChanging != true &&
                BotOwner.WeaponManager?.IsWeaponReady != false)
            {
                return false;
            }

            FollowerCombatCommon.TrySwitchToPushReadyLongGun(BotOwner);
            BotOwner.Mover?.Stop();
            BotOwner.Mover?.Sprint(false, true);
            StopCombatShooting();
            return true;
        }

        protected void TryPreferMarksmanPrimaryAtRange(EnemyInfo? goalEnemy, string? reason = null)
        {
            if (goalEnemy == null ||
                BossPlayers.Instance?.GetFollower(BotOwner)?.CombatTactic != FollowerCombatTactic.Marksman)
            {
                return;
            }

            if (!FollowerCombatCommon.IsUsingAutomaticMarksmanSupportOverNonAutomaticPrimary(BotOwner))
            {
                return;
            }

            if (FollowerCombatSniper.IsAutomaticSupportIntentReason(reason))
            {
                return;
            }

            if (FollowerCombatSniper.CanUseAutomaticSupportForCloseThreat(BotOwner, goalEnemy))
            {
                return;
            }

            BotWeaponSelector? selector = BotOwner?.WeaponManager?.Selector;
            if (selector != null && !selector.IsChanging)
            {
                selector.ChangeToMain();
            }
        }

        private bool ShouldRespectVanillaSupportWeaponFallback(BotWeaponSelector selector)
        {
            if (selector.LastEquipmentSlot == EquipmentSlot.Holster)
            {
                return true;
            }

            if (selector.LastEquipmentSlot == EquipmentSlot.SecondPrimaryWeapon &&
                FollowerCombatCommon.IsGrenadeLauncherWeapon(selector.SecondPrimaryWeaponItem as Weapon))
            {
                return false;
            }

            if (selector.LastEquipmentSlot != selector._supportWeapon)
            {
                return false;
            }

            BotWeaponManager? weaponManager = BotOwner?.WeaponManager;
            if (weaponManager == null)
            {
                return false;
            }

            if (weaponManager.Reload?.Reloading == true)
            {
                return true;
            }

            return weaponManager.MainWeaponInfo?.BulletCount <= 0;
        }

        private bool ShouldKeepAutomaticSecondaryForPush(string? reason)
        {
            return FollowerCombatCommon.IsSelectedSecondPrimaryOverShotgunPrimary(BotOwner) ||
                   FollowerCombatCommon.IsAutomaticSecondaryPushReason(reason) &&
                   FollowerCombatCommon.IsUsingAutomaticSecondaryOverNonAutomaticPrimary(BotOwner);
        }

        protected bool WaitForEnemyAimAlignment(
            ref float startedAt,
            float maxAngle = 32f,
            float timeout = 0.12f,
            float wayOffAngle = 25f)
        {
            EnemyInfo? goalEnemy = BotOwner?.Memory?.GoalEnemy;
            if (goalEnemy?.Person?.HealthController?.IsAlive != true || !goalEnemy.CanShoot)
            {
                startedAt = 0f;
                return false;
            }

            if (startedAt <= 0f)
            {
                startedAt = Time.time;
            }

            Vector3 lookPoint = GetEnemyShootLookPoint(goalEnemy);
            BotOwner.Steering.LookToPoint(lookPoint);

            Vector3 lookOrigin = BotOwner.Transform != null
                ? BotOwner.Transform.position + Vector3.up * 1.2f
                : BotOwner.Position + Vector3.up * 1.2f;
            Vector3 toEnemy = lookPoint - lookOrigin;
            if (toEnemy.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            Vector3 currentLook = BotOwner.LookDirection;
            if (currentLook.sqrMagnitude <= 0.001f && BotOwner.Transform != null)
            {
                currentLook = BotOwner.Transform.forward;
            }

            if (currentLook.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            // Faster than Vector3.Angle for this hot path: compare normalized dot to cosine threshold.
            float denominator = Mathf.Sqrt(currentLook.sqrMagnitude * toEnemy.sqrMagnitude);
            if (denominator <= 0.0001f)
            {
                return false;
            }

            float alignmentDot = Vector3.Dot(currentLook, toEnemy) / denominator;
            float alignedDot = Mathf.Cos(maxAngle * Mathf.Deg2Rad);
            if (alignmentDot >= alignedDot)
            {
                startedAt = 0f;
                return false;
            }

            float elapsed = Time.time - startedAt;
            float wayOffDot = Mathf.Cos(wayOffAngle * Mathf.Deg2Rad);
            bool stillWayOff = alignmentDot < wayOffDot;
            if (elapsed < timeout || stillWayOff)
            {
                StopCombatShooting();
                return true;
            }

            startedAt = 0f;
            return false;
        }

        protected bool TryLookTowardCloseUnseenThreat(float maxSourceDistance)
        {
            EnemyInfo? goalEnemy = BotOwner?.Memory?.GoalEnemy;
            if (goalEnemy?.IsVisible == true && goalEnemy.CanShoot)
            {
                return false;
            }

            if (!FollowerAwareness.TryGetRecentCloseThreatLookPoint(BotOwner, maxSourceDistance, out Vector3 lookPoint))
            {
                return false;
            }

            BotOwner.Steering.LookToPoint(lookPoint);
            return true;
        }

        private Vector3 GetEnemyShootLookPoint(EnemyInfo goalEnemy)
        {
            ShootToPoint? shootPoint = BotOwner.CurrentEnemyTargetPosition(false);
            if (shootPoint != null)
            {
                return shootPoint.Point;
            }

            return goalEnemy.GetBodyPartPosition();
        }
    }
}

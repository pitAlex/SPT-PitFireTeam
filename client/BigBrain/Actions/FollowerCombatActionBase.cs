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
        public GClass26? Data { get; }

        public FollowerCombatActionData(BotLogicDecision decision, string reason, GClass26? data)
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
        private float nextUnownedLauncherGuardRecordAt;

        protected FollowerCombatActionBase(BotOwner botOwner) : base(botOwner)
        {
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
            if (sprint && BotOwner.Mover.Sprinting) return;
            else if (!sprint && !BotOwner.Mover.Sprinting) return;
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

        protected CoverSearchType SetAttackCoverSearchType(CoverShootType shootType)
        {
            SetCombatCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            return BotOwner.Tactic.SubTactic.SearchTypeAttack(shootType);
        }

        protected void SetCombatCoverTactic(BotsGroup.BotCurrentTactic tactic)
        {
            if (BotOwner.Tactic.ShallReturnToAttack && tactic != BotsGroup.BotCurrentTactic.Ambush)
            {
                BotOwner.Tactic.ShallReturnToAttack = false;
                BotOwner.Tactic.ReturnToAttackTime = 0f;
            }

            BotOwner.Tactic.SetTactic(tactic);
        }

        protected static GClass26? GetRawData(CustomLayer.ActionData data)
        {
            return (data as FollowerCombatActionData)?.Data;
        }

        protected static string? GetReason(CustomLayer.ActionData data)
        {
            return (data as FollowerCombatActionData)?.Reason;
        }

        protected static TData? GetData<TData>(CustomLayer.ActionData data) where TData : GClass26
        {
            return GetRawData(data) as TData;
        }

        protected void StopCombatShooting()
        {
            ShootData? shootData = BotOwner?.ShootData;
            shootData?.EndShoot();

            var shootController = BotOwner?.WeaponManager?.ShootController;
            if (shootController != null)
            {
                shootController.SetTriggerPressed(false);
            }
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

            ShootPointClass? shootPoint = BotOwner.CurrentEnemyTargetPosition(false);
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
                TryPreferMarksmanPrimaryAtRange(goalEnemy);
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

        protected void TryPreferMarksmanPrimaryAtRange(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null ||
                BossPlayers.Instance?.GetFollower(BotOwner)?.CombatTactic != FollowerCombatTactic.Marksman)
            {
                return;
            }

            if (!FollowerCombatCommon.IsUsingAutomaticSecondaryOverNonAutomaticPrimary(BotOwner))
            {
                return;
            }

            if (goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetCloseQuarterDistance())
            {
                return;
            }

            BotOwner?.WeaponManager?.Selector?.TryChangeToMain();
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

            if (selector.LastEquipmentSlot != selector.SupportWeapon)
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
            ShootPointClass? shootPoint = BotOwner.CurrentEnemyTargetPosition(false);
            if (shootPoint != null)
            {
                return shootPoint.Point;
            }

            return goalEnemy.GetBodyPartPosition();
        }
    }
}

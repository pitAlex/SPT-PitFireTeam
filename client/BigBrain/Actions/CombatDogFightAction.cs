using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Utils;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Close-quarters fight action. It owns short lateral/backward movement, preserves a settled
    /// firing pose until movement is accepted, blocks friendly shot lanes, and forces threat facing
    /// so dogfight movement does not expose the follower's back.
    /// </summary>
    internal sealed class CombatDogFightAction : FollowerCombatActionBase
    {
        private const float MoveUpdateMinDelay = 0.1f;
        private const float MoveUpdateFallbackDelay = 0.33f;
        private const float RecentSeenThreshold = 1f;
        private const float UnsafeCloseThreatDistance = 8f;
        private const float UnsafeCloseThreatLookAngle = 70f;
        private const float FastFireDistance = 8f;
        private const float FastFireAngle = 35f;
        private const float CloseContactFireDistance = 18f;
        private const float CloseContactFireAngle = 18f;
        private const float RecentContactFireAngle = 15f;
        private const float PointBlankContactFireAngle = 55f;

        private readonly GClass178 shootLogic;
        private readonly GClass274 grenadeLogic;
        private readonly NavMeshPath dogFightPath = new NavMeshPath();

        private DogFightMoveStatus moveStatus;
        private float nextMoveUpdateTime;

        public CombatDogFightAction(BotOwner botOwner) : base(botOwner)
        {
            shootLogic = new GClass183(botOwner);
            grenadeLogic = new GClass274(botOwner);
        }

        public override void Start()
        {
            base.Start();
            StopStationaryCombatMovement();
            moveStatus = DogFightMoveStatus.None;
            nextMoveUpdateTime = 0f;
        }

        public override void Stop()
        {
            StopCombatShooting();
            moveStatus = DogFightMoveStatus.None;
            nextMoveUpdateTime = 0f;

            if (BotOwner?.DogFight != null)
            {
                BotOwner.DogFight.DogFightState = BotDogFightStatus.none;
                BotOwner.DogFight.PursuitInProgress = false;
            }

            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;

            // Dogfight is about weapon control, not travel speed. Keep movement slow and update a
            // short SAIN-like dodge destination without giving up a settled firing pose first.
            BotOwner.Mover.SetTargetMoveSpeed(1f);
            bool unsafeCloseFacing = IsUnsafeCloseThreatFacing(goalEnemy);
            bool hasConfirmedShot = goalEnemy != null && goalEnemy.CanShoot && goalEnemy.IsVisible;
            bool hasPointBlankContactShot = false;
            Vector3 pointBlankContactTarget = Vector3.zero;
            bool hasRecentContactShot = false;
            Vector3 recentContactTarget = Vector3.zero;
            if (!hasConfirmedShot)
            {
                hasPointBlankContactShot = FollowerCombatCommon.TryGetPointBlankContactFireTarget(
                    BotOwner,
                    goalEnemy,
                    out pointBlankContactTarget);
                if (!hasPointBlankContactShot)
                {
                    hasRecentContactShot = FollowerCombatCommon.TryGetCloseRecentContactFireTarget(
                        BotOwner,
                        goalEnemy,
                        out recentContactTarget);
                }
            }
            if (ShouldLockDogFightLook(goalEnemy))
            {
                MaintainThreatFacing(goalEnemy!, GetDogFightThreatLookPoint(goalEnemy!), allowHardTurn: true);
            }

            bool hasAcceptedMovement = UpdateSainLikeMovement(goalEnemy);
            bool hasFireContact = hasConfirmedShot || hasPointBlankContactShot || hasRecentContactShot;

            // Close retreat movement is dangerous if it turns the bot away from the attacker.
            // A nearby door can also make EFT immediately cancel an otherwise accepted dodge.
            // In either case, revoke the mobile posture and retry from the existing firing pose.
            if (hasAcceptedMovement &&
                ((hasFireContact && unsafeCloseFacing && IsUnsafeCloseThreatFacing(goalEnemy)) ||
                 BotOwner.DoorOpener.DogFightHaveNearestDoor()))
            {
                BotOwner.Mover.Stop();
                moveStatus = DogFightMoveStatus.None;
                nextMoveUpdateTime = Time.time + MoveUpdateFallbackDelay;
                hasAcceptedMovement = false;
            }

            ApplyDogFightPose(hasAcceptedMovement, goalEnemy);
            BotOwner.Sprint(false, true);

            if (goalEnemy == null || !hasConfirmedShot && !hasPointBlankContactShot && !hasRecentContactShot)
            {
                // Dogfight movement can step forward, backward, or sideways, but aim should stay
                // locked to the fight target. If visibility flickers without a safe recent-contact
                // lane, stop firing and keep looking rather than letting movement own look.
                if (ShouldLockDogFightLook(goalEnemy))
                {
                    MaintainThreatFacing(goalEnemy!, GetDogFightThreatLookPoint(goalEnemy!), allowHardTurn: true);
                }

                StopCombatShooting();
                BotOwner.LookData.SetLookPointByHearing(null);
                return;
            }

            if (BotOwner.WeaponManager.IsMelee)
            {
                BotOwner.WeaponManager.Selector.ChangeToMain();
                return;
            }

            if (StopUnownedGrenadeLauncherFire(GetReason(data), goalEnemy))
            {
                return;
            }

            if (BotOwner.Settings.FileSettings.Grenade.CAN_THROW_FROM_ANY_PLACE && grenadeLogic.UpdateTryThrow())
            {
                return;
            }

            Vector3 shootPoint = hasPointBlankContactShot && !hasConfirmedShot
                ? pointBlankContactTarget
                : hasRecentContactShot && !hasConfirmedShot
                    ? recentContactTarget
                    : shootLogic.GetTarget() ?? GetDogFightThreatLookPoint(goalEnemy);
            MaintainThreatFacing(goalEnemy, shootPoint, allowHardTurn: true);

            // Dogfight tends to happen inside the squad. Friendly lane safety is the hard stop before
            // either our fast-fire helper or the vanilla shoot node can press the trigger.
            if (StopIfFriendlyInCurrentFireLane(shootPoint))
            {
                return;
            }

            if (hasPointBlankContactShot && !hasConfirmedShot)
            {
                if (GetLookAngleToPoint(shootPoint) <= PointBlankContactFireAngle)
                {
                    BotOwner.ShootData.Shoot();
                }
                else
                {
                    StopCombatShooting();
                }

                return;
            }

            if (hasRecentContactShot && !hasConfirmedShot)
            {
                if (GetLookAngleToPoint(shootPoint) <= RecentContactFireAngle)
                {
                    BotOwner.ShootData.Shoot();
                }
                else
                {
                    StopCombatShooting();
                }

                return;
            }

            if (goalEnemy.Distance <= CloseContactFireDistance &&
                GetLookAngleToPoint(shootPoint) <= CloseContactFireAngle)
            {
                BotOwner.ShootData.Shoot();
            }

            if (goalEnemy.Distance <= FastFireDistance &&
                CombatAttackMoveLook.GetThreatLookAngle(BotOwner, goalEnemy) <= FastFireAngle)
            {
                BotOwner.ShootData.Shoot();
            }

            shootLogic.UpdateNodeByBrain(GetData<GClass27>(data));
        }

        private bool UpdateSainLikeMovement(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null)
            {
                moveStatus = DogFightMoveStatus.None;
                return false;
            }

            if (goalEnemy.IsVisible && moveStatus == DogFightMoveStatus.MovingToEnemy)
            {
                moveStatus = DogFightMoveStatus.Shooting;
                BotOwner.Mover.Stop();
                nextMoveUpdateTime = Time.time + 0.25f * Random.Range(0.66f, 1.33f);
                return false;
            }

            if (nextMoveUpdateTime > Time.time)
            {
                return IsMovementAccepted();
            }

            if (TryBackUpFromEnemy(goalEnemy))
            {
                moveStatus = DogFightMoveStatus.BackingUp;
                float baseTime = goalEnemy.IsVisible ? 0.75f : 1f;
                nextMoveUpdateTime = Time.time + baseTime * Random.Range(0.66f, 1.33f);
                return true;
            }

            if (TryMoveTowardEnemy(goalEnemy))
            {
                moveStatus = DogFightMoveStatus.MovingToEnemy;
                nextMoveUpdateTime = Time.time + Mathf.Clamp(0.25f * Random.Range(0.5f, 1.25f), MoveUpdateMinDelay, 0.66f);
                return true;
            }

            moveStatus = DogFightMoveStatus.None;
            nextMoveUpdateTime = Time.time + MoveUpdateFallbackDelay;
            return false;
        }

        private bool IsMovementAccepted()
        {
            return moveStatus == DogFightMoveStatus.BackingUp ||
                   moveStatus == DogFightMoveStatus.MovingToEnemy;
        }

        private void ApplyDogFightPose(bool hasAcceptedMovement, EnemyInfo? goalEnemy)
        {
            // Only point-blank dogfights preserve a useful settled pose while movement is
            // unavailable. At ordinary dogfight ranges, retain the normal mobile standing posture.
            bool preserveCloseSettledPose = !hasAcceptedMovement &&
                                             goalEnemy != null &&
                                             goalEnemy.Distance <= UnsafeCloseThreatDistance;
            if (!preserveCloseSettledPose)
            {
                BotOwner.SetPose(1f);
            }
        }

        private bool TryMoveTowardEnemy(EnemyInfo goalEnemy)
        {
            Vector3 moveTarget = goalEnemy.IsVisible
                ? goalEnemy.CurrPosition
                : goalEnemy.EnemyLastPositionReal;

            if (!NavMesh.SamplePosition(moveTarget, out NavMeshHit navMeshHit, 1.5f, -1))
            {
                return false;
            }

            return BotOwner.GoToPoint(navMeshHit.position, false, -1f, false, false) == NavMeshPathStatus.PathComplete;
        }

        private bool TryBackUpFromEnemy(EnemyInfo goalEnemy)
        {
            Vector3? target = FindBackUpTarget(goalEnemy);
            if (target == null)
            {
                return false;
            }

            Vector3 botPosition = BotOwner.Position;
            Vector3 targetDirection = target.Value - botPosition;
            targetDirection.y = 0f;
            if (targetDirection.sqrMagnitude < 0.01f)
            {
                return false;
            }

            Vector3 away = -targetDirection.normalized;
            Vector3 preferred = botPosition + away * 3f;

            const int maxIterations = 5;
            for (int i = 0; i < maxIterations; i++)
            {
                Vector3 random = Random.onUnitSphere * 2f;
                random.y = Mathf.Clamp(random.y, -0.5f, 0.5f);
                Vector3 candidate = preferred + random;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit navMeshHit, 2f, -1))
                {
                    continue;
                }

                if ((navMeshHit.position - botPosition).sqrMagnitude <= 1f)
                {
                    continue;
                }

                if (BotOwner.GoToPoint(navMeshHit.position, false, -1f, false, false) == NavMeshPathStatus.PathComplete)
                {
                    return true;
                }
            }

            if (goalEnemy.IsVisible && Time.time - goalEnemy.PersonalSeenTime < RecentSeenThreshold * Random.Range(0.66f, 1.33f))
            {
                Vector3 direction = (botPosition - target.Value).normalized;
                Vector3 random = Random.onUnitSphere * Random.Range(1.25f, 2f);
                random.y = 0f;
                Vector3 point = botPosition + direction * Random.Range(1f, 2f) + random;
                if (NavMesh.Raycast(botPosition, point, out NavMeshHit raycastHit, -1))
                {
                    if (raycastHit.distance <= 0.5f)
                    {
                        dogFightPath.ClearCorners();
                        if (NavMesh.CalculatePath(botPosition, point, -1, dogFightPath) &&
                            dogFightPath.status == NavMeshPathStatus.PathComplete &&
                            dogFightPath.corners.Length > 0)
                        {
                            Vector3 pathEnd = dogFightPath.corners[dogFightPath.corners.Length - 1];
                            return BotOwner.GoToPoint(pathEnd, false, -1f, false, false) == NavMeshPathStatus.PathComplete;
                        }
                    }

                    return BotOwner.GoToPoint(raycastHit.position, false, -1f, false, false) == NavMeshPathStatus.PathComplete;
                }

                return BotOwner.GoToPoint(point, false, -1f, false, false) == NavMeshPathStatus.PathComplete;
            }

            return false;
        }

        private Vector3? FindBackUpTarget(EnemyInfo goalEnemy)
        {
            BotOwner? enemyBot = goalEnemy.Person?.AIData?.BotOwner;
            if (enemyBot != null)
            {
                // A newly spawned enemy can publish AIData before its weapon and reload
                // controllers are ready. Treat that transient state as unknown instead of
                // faulting the follower's dogfight action.
                var enemyWeapons = enemyBot.WeaponManager;
                bool enemyWeaponVulnerable = enemyWeapons != null &&
                                             (enemyWeapons.Reload?.Reloading == true ||
                                              !enemyWeapons.HaveBullets);
                if (enemyWeaponVulnerable ||
                    enemyBot.Medecine?.Using == true ||
                    (goalEnemy.IsVisible && Time.time - goalEnemy.PersonalSeenTime < RecentSeenThreshold))
                {
                    return goalEnemy.CurrPosition;
                }
            }

            if (goalEnemy.IsVisible && Time.time - goalEnemy.PersonalSeenTime < RecentSeenThreshold)
            {
                return goalEnemy.CurrPosition;
            }

            return goalEnemy.EnemyLastPositionReal;
        }

        private bool IsUnsafeCloseThreatFacing(EnemyInfo? goalEnemy)
        {
            return goalEnemy != null &&
                   goalEnemy.Distance <= UnsafeCloseThreatDistance &&
                   CombatAttackMoveLook.GetThreatLookAngle(BotOwner, goalEnemy) > UnsafeCloseThreatLookAngle;
        }

        private static bool ShouldLockDogFightLook(EnemyInfo? goalEnemy)
        {
            return goalEnemy?.Person?.HealthController?.IsAlive == true;
        }

        private void MaintainThreatFacing(EnemyInfo goalEnemy, Vector3 shootPoint, bool allowHardTurn)
        {
            if (!CombatAttackMoveLook.TryLookThreatFacing(BotOwner, goalEnemy, allowHardTurn))
            {
                BotOwner.Steering.LookToPoint(shootPoint);
            }
        }

        private float GetLookAngleToPoint(Vector3 point)
        {
            Vector3 lookDirection = BotOwner.LookDirection;
            if (lookDirection.sqrMagnitude <= 0.001f && BotOwner.Transform != null)
            {
                lookDirection = BotOwner.Transform.forward;
            }

            Vector3 toTarget = point - BotOwner.Position;
            if (lookDirection.sqrMagnitude <= 0.001f || toTarget.sqrMagnitude <= 0.001f)
            {
                return 180f;
            }

            return Vector3.Angle(lookDirection, toTarget);
        }

        private static Vector3 GetDogFightThreatLookPoint(EnemyInfo goalEnemy)
        {
            Vector3 bodyPoint = goalEnemy.GetBodyPartPosition();
            if (FollowerCombatCommon.IsFinite(bodyPoint))
            {
                return bodyPoint;
            }

            Vector3 currentPosition = FollowerCombatCommon.GetEnemyCurrentPosition(goalEnemy) + Vector3.up * 0.8f;
            if (FollowerCombatCommon.IsFinite(currentPosition))
            {
                return currentPosition;
            }

            return goalEnemy.EnemyLastPositionReal + Vector3.up * 0.8f;
        }

        private enum DogFightMoveStatus
        {
            None,
            BackingUp,
            MovingToEnemy,
            Shooting,
        }
    }
}

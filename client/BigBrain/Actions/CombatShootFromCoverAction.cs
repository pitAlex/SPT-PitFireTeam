using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Modules;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Stationary cover-fire action used when the follower already owns a cover position and the
    /// decision tree wants him to fight from that cover instead of relocating. The action delegates
    /// the actual cover shooting behavior to EFT's shoot-from-cover node, but wraps it with follower
    /// safeguards: no opportunistic grenade throws, stance correction for standing cover lanes, aim
    /// alignment before firing, and guaranteed cleanup of modified grenade settings.
    /// </summary>
    internal sealed class CombatShootFromCoverAction : FollowerCombatActionBase
    {
        private readonly GClass277 baseLogic;
        private readonly FollowerEmergencyFireGate emergencyFireGate = new FollowerEmergencyFireGate();
        private float aimAlignStartedAt;
        private bool? lastCrouchAllowed;
        private string? lastCrouchPolicyReason;

        public CombatShootFromCoverAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass277(botOwner);
        }

        public override void Start()
        {
            base.Start();
            BotOwner.SetPose(1f);
            lastCrouchAllowed = null;
            lastCrouchPolicyReason = null;
        }

        public override void Stop()
        {
            StopCombatShooting();
            emergencyFireGate.Reset();
            aimAlignStartedAt = 0f;
            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            // The vanilla cover-shoot node can opportunistically throw grenades from this state.
            // Followers route grenades through explicit decisions, so disable those flags only for
            // this update and restore them in finally to avoid leaking settings into other actions.
            bool oldCanThrowFromAnyPlace = BotOwner.Settings.FileSettings.Grenade.CAN_THROW_FROM_ANY_PLACE;
            bool oldCanThrowStraightContact = BotOwner.Settings.FileSettings.Grenade.CAN_THROW_STRAIGHT_CONTACT;
            BotOwner.Settings.FileSettings.Grenade.CAN_THROW_FROM_ANY_PLACE = false;
            BotOwner.Settings.FileSettings.Grenade.CAN_THROW_STRAIGHT_CONTACT = false;
            try
            {
                EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
                bool allowCrouch = EvaluateCrouchPolicy(
                    goalEnemy,
                    out string crouchPolicyReason,
                    out float crouchEnemyDistance,
                    out Vector3? crouchTarget);
                if (!allowCrouch)
                {
                    BotOwner.SetPose(1f);
                }

                RecordCrouchPolicyIfChanged(
                    allowCrouch,
                    crouchPolicyReason,
                    crouchEnemyDistance,
                    crouchTarget);
                if (StopUnownedGrenadeLauncherFire(GetReason(data), goalEnemy))
                {
                    return;
                }

                // Before the vanilla node runs, try to raise the follower if the current cover only
                // has a standing shot lane. This prevents a crouched follower from "shooting cover"
                // while his muzzle is actually blocked by the cover edge.
                FollowerCombatCommon.TryRaiseForStandingCoverShot(
                    BotOwner,
                    out _,
                    requireShootingCoverIntent: false);

                // Give steering a short chance to align on the enemy before allowing the cover node
                // to press the trigger. This avoids visible off-angle firing when the bot just moved
                // into cover or reacquired a target.
                if (WaitForEnemyAimAlignment(ref aimAlignStartedAt))
                {
                    return;
                }

                if (StopIfFriendlyInCurrentFireLane(goalEnemy))
                {
                    return;
                }

                baseLogic.UpdateNodeByBrain(GetData<GClass28>(data));

                if (goalEnemy != null)
                {
                    ShootPointClass shootPoint = BotOwner.CurrentEnemyTargetPosition(false) ??
                                                 new ShootPointClass(goalEnemy.GetBodyPartPosition(), 1f);
                    emergencyFireGate.TryFire(
                        BotOwner,
                        goalEnemy,
                        shootPoint.Point,
                        "shootFromCover",
                        GetReason(data),
                        suppression: false,
                        out _);
                }

                if (!allowCrouch)
                {
                    BotOwner.SetPose(1f);
                }

                // The cover node may change pose internally during its update. Re-run the standing
                // lane correction afterward so the final pose still matches the usable firing lane.
                FollowerCombatCommon.TryRaiseForStandingCoverShot(
                    BotOwner,
                    out _,
                    requireShootingCoverIntent: false);
            }
            finally
            {
                BotOwner.Settings.FileSettings.Grenade.CAN_THROW_FROM_ANY_PLACE = oldCanThrowFromAnyPlace;
                BotOwner.Settings.FileSettings.Grenade.CAN_THROW_STRAIGHT_CONTACT = oldCanThrowStraightContact;
            }
        }

        private bool EvaluateCrouchPolicy(
            EnemyInfo? goalEnemy,
            out string reason,
            out float enemyDistance,
            out Vector3? target)
        {
            reason = "enemyTargetMissing";
            enemyDistance = goalEnemy?.Distance ?? 0f;
            target = null;
            if (goalEnemy == null)
            {
                return false;
            }

            ShootPointClass shootPoint = BotOwner.CurrentEnemyTargetPosition(false) ??
                                         new ShootPointClass(goalEnemy.GetBodyPartPosition(), 1f);
            target = shootPoint.Point;
            return FollowerShootPoseSafety.CanUseCombatCrouchFire(
                BotOwner,
                shootPoint.Point,
                out reason,
                out enemyDistance);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void RecordCrouchPolicyIfChanged(
            bool allowed,
            string reason,
            float enemyDistance,
            Vector3? target)
        {
            if (!BattleRecorder.IsRecordingFor(BotOwner, requireRecordedCombat: true))
            {
                return;
            }

            if (lastCrouchAllowed == allowed &&
                string.Equals(lastCrouchPolicyReason, reason, System.StringComparison.Ordinal))
            {
                return;
            }

            lastCrouchAllowed = allowed;
            lastCrouchPolicyReason = reason;
            BattleRecorder.RecordCombatPosturePolicy(
                BotOwner,
                "shootFromCover",
                "crouch",
                allowed,
                reason,
                enemyDistance,
                target);
        }
    }
}

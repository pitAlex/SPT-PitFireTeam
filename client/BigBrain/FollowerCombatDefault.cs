using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatDefault
    {
        private const float RecentSeenPressureSeconds = 2f;
        private const string BossHoldReason = "bossHold";
        private const string BossHoldOpenReason = "bossHoldOpen";
        private const float PushSearchHoldSeconds = 1.25f;
        private const float ShootCoverSettleSeconds = 2.5f;
        private const float CommittedHolderEngagementBreakSettleSeconds = 1f;
        private const string ShootCoverHoldReason = "shootCoverHold";
        private const float IntentSettleSeconds = 2.5f;
        private const float IntentScanIntervalSeconds = 0.75f;
        private const float SupportIntentMaxHoldSeconds = 8f;
        private const float ProtectIntentMaxHoldSeconds = 8f;
        private const float IntentRetryCooldownSeconds = 3f;
        private const float BossSupportForcedRetrySeconds = 9f;
        private const float BossSupportGeometryMoveDistance = 4f;
        private const float BossSupportBearingDotThreshold = 0.9659258f;
        private const float BossSupportGeometryCheckIntervalSeconds = 0.5f;
        private const float HoldReactionRetryCooldownSeconds = 3f;
        private const float SupportBreakRetryCooldownSeconds = 1f;
        private const float VisibleAlignedFireMaxDistance = 35f;
        private const float VisibleAlignedFireMaxAngle = 12f;
        private const float AutoSuppressRetryCooldownSeconds = 3.5f;
        private const float AllyPushSupportMaxStraightDistance = 45f;
        private const float AllyPushSupportMaxNavDistance = 65f;
        private const float HardRecoveryCloseThreatDistance = 10f;
        private const float AutomaticSecondaryRecentSeenHoldSeconds = 2.25f;
        private const float HoldPositionLocalSupportMaxNavDistance = 28f;
        private const float HoldPositionLocalSupportRetryCooldownSeconds = 2f;
        private const float ExposedFirePointBlankDistance = 8f;
        private const float ExposedFireNoReturnLeaseSeconds = 0.55f;
        private const float ExposedFireReturnedLeaseSeconds = 0.9f;
        private const float ExposedFireRecoveryRetrySeconds = 0.35f;

        private enum CoverIntentKind
        {
            None,
            Support,
            ProtectBoss,
        }

        private readonly BotOwner botOwner;
        private readonly FollowerCombatCommon combatCommon;
        private readonly FollowerCombatPush combatPush;
        private readonly CommittedCoverPhaseState shootCoverSettlePhase = new CommittedCoverPhaseState();
        private readonly CommittedCoverPhaseState coverIntentPhase = new CommittedCoverPhaseState();

        private CoverIntentKind activeCoverIntent;
        private float supportIntentRetryUntil;
        private float protectIntentRetryUntil;
        private float protectIntentForcedRetryUntil;
        private float nextBossSupportGeometryCheckAt;
        private bool bossSupportBreakPending;
        private bool hasFailedBossSupportSignature;
        private BossSupportRetrySignature failedBossSupportSignature;
        private float autoSuppressRetryUntil;
        private float holdPositionSupportRetryUntil;
        private float holdReactionRetryUntil;
        private float nextHoldReactionGeometryCheckAt;
        private bool hasFailedHoldReactionSignature;
        private HoldReactionRetrySignature failedHoldReactionSignature;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? preparedAllySupportDecision;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? preparedCloseCoverFightDecision;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? preparedRecoveryDecision;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? preparedAdvanceDecision;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? preparedHoldPositionSupportDecision;
        private int holdReactionDamageRevision;
        private bool shotgunAutomaticSecondaryLatched;
        private bool shotgunAutomaticSecondaryDisabledForFight;
        private bool ownsAutomaticSecondarySwitch;
        private string exposedFireEnemyProfileId = string.Empty;
        private string exposedFireDecisionReason = string.Empty;
        private float exposedFireStartedAt;
        private float exposedFireInitialTriggerPressedAt;
        private int exposedFireInitialDamageRevision;
        private float exposedFireRecoveryRetryAt;

        private struct BossSupportRetrySignature
        {
            public string AttackerProfileId;
            public Vector3 FollowerPosition;
            public Vector3 BossPosition;
            public Vector3 AttackerPosition;
            public bool AttackerVisible;
            public bool AttackerCanShoot;
        }

        private struct HoldReactionRetrySignature
        {
            public string EnemyProfileId;
            public Vector3 FollowerPosition;
            public Vector3 EnemyPosition;
            public bool EnemyVisible;
            public bool EnemyCanShoot;
            public bool UnderFire;
        }

        public FollowerCombatDefault(BotOwner botOwner, FollowerCombatCommon combatCommon)
        {
            this.botOwner = botOwner;
            this.combatCommon = combatCommon;
            this.combatPush = new FollowerCombatPush(botOwner, combatCommon);
        }

        /// <summary>
        /// Clears follower-local combat state when the combat layer stops or restarts.
        /// </summary>
        public void Reset()
        {
            combatCommon.ResetCommittedCover();
            combatCommon.ClearCommittedPosition();
            combatPush.Reset();
            combatCommon.ClearCommittedMovement();
            shootCoverSettlePhase.Reset();
            ClearCoverIntent();
            bossSupportBreakPending = false;
            if (!combatCommon.HasActiveCombatEnemy())
            {
                autoSuppressRetryUntil = 0f;
                ClearBossSupportRetryState();
            }

            holdPositionSupportRetryUntil = 0f;
            ClearHoldReactionRetryState();
            preparedAllySupportDecision = null;
            preparedCloseCoverFightDecision = null;
            preparedRecoveryDecision = null;
            preparedAdvanceDecision = null;
            preparedHoldPositionSupportDecision = null;
            holdReactionDamageRevision = FollowerAwareness.GetDamageRevision(botOwner);
            combatCommon.ResetRecoveryNoCoverCommitment();
            ResetExposedFireLease();
            if (!combatCommon.HasActiveCombatEnemy())
            {
                shotgunAutomaticSecondaryLatched = false;
                shotgunAutomaticSecondaryDisabledForFight = false;
                if (ownsAutomaticSecondarySwitch &&
                    combatCommon.TrySwitchBackToPrimaryFromAutomaticSecondary())
                {
                    ownsAutomaticSecondarySwitch = false;
                }
            }
        }

        /// <summary>
        /// Updates local commitment state after the BigBrain decision changes.
        /// </summary>
        public void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            ResolvePendingBossSupportBreak(nextDecision);
            ApplyRiflemanWeaponPolicy(nextDecision);
            combatCommon.HandleSharedDecisionChanged(nextDecision);
            combatCommon.HandleCommittedCoverDecisionChanged(nextDecision);
            combatCommon.HandleFollowerSuppressDecisionChanged(nextDecision);
            UpdateShootCoverSettleState(nextDecision);
            combatPush.HandleDecisionChanged(nextDecision);
            combatCommon.UpdateRecoveryNoCoverCommitment(nextDecision);
            UpdateExposedFireLease(nextDecision);

            if (nextDecision.Action == BotLogicDecision.holdPosition &&
                IsCoverHoldReason(nextDecision.Reason))
            {
                holdReactionDamageRevision = FollowerAwareness.GetDamageRevision(botOwner);
            }

            if (nextDecision.Action == BotLogicDecision.holdPosition &&
                FollowerCombatPush.IsPushReason(nextDecision.Reason))
            {
                combatCommon.HoldFor(PushSearchHoldSeconds);
            }

            if (combatCommon.ShouldCommitMovementDecision(nextDecision, combatPush.IsPushCommittedDecision(nextDecision)))
            {
                combatCommon.CommitMovement(nextDecision);
            }
            else if (!combatCommon.IsSameCommittedMovement(nextDecision))
            {
                combatCommon.ClearCommittedMovement();
            }

            if (activeCoverIntent != CoverIntentKind.None && !IsDecisionCompatibleWithCoverIntent(nextDecision))
            {
                ClearCoverIntent();
            }

            if (!IsDecisionCompatibleWithPreparedAdvance(nextDecision))
            {
                preparedAdvanceDecision = null;
            }

            if (!IsDecisionCompatibleWithPreparedHoldPositionSupport(nextDecision))
            {
                preparedHoldPositionSupportDecision = null;
            }
        }

        private void ApplyRiflemanWeaponPolicy(AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            if ((FollowerCombatPush.IsPushReason(nextDecision.Reason) ||
                 FollowerCombatPush.IsStartWeakEnemyPushReason(nextDecision.Reason)) &&
                combatCommon.TrySwitchToPushReadyLongGun())
            {
                return;
            }

            if (TryApplyShotgunDistanceWeaponPolicy())
            {
                return;
            }

            if (shotgunAutomaticSecondaryDisabledForFight)
            {
                TryReleaseOwnedAutomaticSecondary();
                return;
            }

            if (FollowerCombatCommon.IsAutomaticSecondaryPushReason(nextDecision.Reason))
            {
                if (combatCommon.TrySwitchToAutomaticSecondaryForPush(out bool changedToSecondary) &&
                    changedToSecondary)
                {
                    ownsAutomaticSecondarySwitch = true;
                }

                return;
            }

            if (ShouldSwitchBackToPrimaryAfterAutomaticPush())
            {
                TryReleaseOwnedAutomaticSecondary();
            }
        }

        private bool TryApplyShotgunDistanceWeaponPolicy()
        {
            if (shotgunAutomaticSecondaryLatched &&
                combatCommon.ShouldDisableAutomaticSecondaryForEmptyReload())
            {
                shotgunAutomaticSecondaryLatched = false;
                shotgunAutomaticSecondaryDisabledForFight = true;
                TryReleaseOwnedAutomaticSecondary();
                return true;
            }

            if (shotgunAutomaticSecondaryDisabledForFight)
            {
                return false;
            }

            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            if (goalEnemy == null)
            {
                return false;
            }

            if (!shotgunAutomaticSecondaryLatched &&
                Enemy.Distance(goalEnemy) < Enemy.EnemyDistance.Mid)
            {
                return false;
            }

            if (!shotgunAutomaticSecondaryLatched &&
                !combatCommon.HasShotgunPrimaryWithLoadedAutomaticSecondary())
            {
                return false;
            }

            bool wasLatched = shotgunAutomaticSecondaryLatched;
            if (!combatCommon.TrySwitchToAutomaticSecondaryForShotgunDistance(out bool changedToSecondary))
            {
                return wasLatched;
            }

            if (!changedToSecondary && !ownsAutomaticSecondarySwitch && !wasLatched)
            {
                return false;
            }

            shotgunAutomaticSecondaryLatched = true;
            if (changedToSecondary)
            {
                ownsAutomaticSecondarySwitch = true;
            }

            return true;
        }

        private bool ShouldSwitchBackToPrimaryAfterAutomaticPush()
        {
            if (!ownsAutomaticSecondarySwitch)
            {
                return false;
            }

            if (!combatCommon.IsUsingAutomaticSecondaryOverNonAutomaticPrimary())
            {
                ownsAutomaticSecondarySwitch = false;
                return false;
            }

            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            if (goalEnemy == null)
            {
                return true;
            }

            if (goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetCloseQuarterDistance())
            {
                return false;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            return Time.time - goalEnemy.PersonalSeenTime > AutomaticSecondaryRecentSeenHoldSeconds;
        }

        private void TryReleaseOwnedAutomaticSecondary()
        {
            if (!ownsAutomaticSecondarySwitch)
            {
                return;
            }

            if (!combatCommon.IsUsingAutomaticSecondaryOverNonAutomaticPrimary())
            {
                ownsAutomaticSecondarySwitch = false;
                return;
            }

            if (combatCommon.TrySwitchBackToPrimaryFromAutomaticSecondary())
            {
                ownsAutomaticSecondarySwitch = false;
            }
        }

        /// <summary>
        /// Seeds the one-shot combat opener used immediately after combat activation.
        /// </summary>
        public void PrepareStartDecision()
        {
            combatCommon.PrepareStartDecision(combatCommon.GetAggression01());
        }

        /// <summary>
        /// Main default-combat router. Keep urgent fighting and medical work explicit, then honor
        /// destination/hold commitments before considering support, protection, or fresh pressure.
        /// </summary>
        public AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy)
        {
            combatCommon.RefreshShootCover();

            // Router order is deliberate. Immediate survival/contact can break plans, but normal
            // tactical ideas must pass through commitments so a chosen destination gets reached,
            // held briefly, and then re-evaluated instead of churned every tick.
            combatCommon.ValidateCommittedCover();

            // A close visible contact may end a committed cover run only after its end condition
            // prepares a concrete fire/dogfight replacement. Consume that handoff before low-ammo
            // reload routing can select the same cover run again.
            if (TryGetPreparedCloseCoverFightDecision(
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> preparedCloseCoverFight))
            {
                return preparedCloseCoverFight;
            }

            // Exposed static fire and failed-cover holds only release after their end conditions
            // prepare an actual recovery move. Consume that handoff before ordinary routing can
            // recreate the prior decision.
            if (TryGetPreparedRecoveryDecision(
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> preparedRecovery))
            {
                return preparedRecovery;
            }

            if (combatCommon.TryGetReloadRetreatDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> reloadRetreatDecision))
            {
                combatCommon.ClearInitialDecision();
                return reloadRetreatDecision;
            }

            if (TryGetImmediateFightDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> fightDecision))
            {
                return fightDecision;
            }

            if (combatCommon.TryGetCombatLongGunPreparationDecision(
                    goalEnemy,
                    orderedPush: false,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> longGunPreparation))
            {
                combatCommon.ClearInitialDecision();
                return longGunPreparation;
            }

            if (combatCommon.TryGetCommittedGrenadeDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> committedGrenade))
            {
                return committedGrenade;
            }

            if (combatCommon.HasInitialDecision)
            {
                return combatCommon.ConsumeInitialDecision();
            }

            if (TryGetHealDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> healDecision))
            {
                return healDecision;
            }

            if (!ShouldPrioritizeRecoveryBeforeSupport(goalEnemy) &&
                TryGetAllySupportDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> earlyAllySupportDecision))
            {
                return earlyAllySupportDecision;
            }

            // When exposed, under fire, or hurt, switch to recovery behavior before honoring
            // normal commitments or support routing.
            if (TryGetRecoverDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> recoverDecision))
            {
                return recoverDecision;
            }

            if (TryGetBossDistanceRegroupDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> bossDistanceDecision))
            {
                return bossDistanceDecision;
            }

            if (TryGetPreparedAdvanceDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> preparedAdvance))
            {
                return preparedAdvance;
            }

            // Arrival holders block re-planning after the bot reaches a cover/position. Their end
            // conditions still break for direct contact, under-fire pressure, or settled support needs.
            if (combatCommon.HasCommittedPosition(out AICoreActionResultStruct<BotLogicDecision, GClass26> committedPosition))
            {
                return committedPosition;
            }

            // Once a push has been chosen, keep returning that same push action until a hard interrupt
            // or the action end logic says the push phase is over.
            if (combatPush.TryGetCommittedPushDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> committedPush))
            {
                return committedPush;
            }

            // Non-push movement commitments prevent mid-route replanning. Arrival and urgent
            // fight/survival interrupts clear the latch so completed movement can hand off to hold/shoot.
            if (combatCommon.TryGetCommittedMovementDecision(
                    goalEnemy,
                    HasExplicitRegroupOrder(),
                    HasActivePushOrder(),
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> committedMovement))
            {
                return committedMovement;
            }

            // If a cover point was already committed earlier, keep following through on it before
            // inventing a new plan.
            if (TryGetCommittedCoverDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> committedCoverDecision))
            {
                return committedCoverDecision;
            }

            if (TryGetBossDistanceRegroupDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> bossDistanceAfterCommitment))
            {
                return bossDistanceAfterCommitment;
            }

            if (TryGetOrderedPushDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> orderedPushDecision))
            {
                return orderedPushDecision;
            }

            // Very low aggression followers should treat bossward regroup as their primary objective
            // unless the enemy is already close enough that local engagement is unavoidable.
            if (TryGetLowAggressionRegroupDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> lowAggressionRegroup))
            {
                return lowAggressionRegroup;
            }

            // Combat HoldPosition means "do not advance on the enemy", not "freeze in the open".
            // If the boss line is stable, allow a small behind/lateral firing-position move.
            if (TryGetHoldPositionLocalSupportDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> holdSupportDecision))
            {
                return holdSupportDecision;
            }

            // If the boss was just hit and this follower is not already committed to a stronger
            // personal fight, bias toward protecting the boss and adopting the boss's attacker.
            if (TryGetBossUnderAttackDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> protectBossDecision))
            {
                return protectBossDecision;
            }

            // While passively holding in cover, scan for a credible ally support opportunity before
            // defaulting into generic push/search or idle holding behavior.
            if (TryGetAllySupportDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> allySupportDecision))
            {
                return allySupportDecision;
            }

            if (combatCommon.TryActivateFollowerGrenade(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> grenadeDecision))
            {
                return grenadeDecision;
            }

            if (TryGetAutonomousSuppressDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> autoSuppressDecision))
            {
                return autoSuppressDecision;
            }

            // Resolve visible-but-not-immediate contact: take a firing cover or hand off to pressure.
            if (TryGetVisibleDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> visibleDecision))
            {
                return visibleDecision;
            }

            // If pushing is justified after the safety checks above, ask the engage helper to choose
            // how to pressure the enemy or search forward.
            if (TryGetAdvanceDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> advanceDecision))
            {
                return advanceDecision;
            }

            if (botOwner.Memory.IsInCover)
            {
                combatCommon.HoldCoverForMaxDuration();
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "coverHold");
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "fallbackShoot");
            }

            if (ShouldBlockPassiveBossHold(goalEnemy))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    "postHitNoPassiveHold");
            }

            combatCommon.HoldCoverForMaxDuration();
            string holdReason = botOwner.Memory.IsInCover ? BossHoldReason : BossHoldOpenReason;
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, holdReason);
        }

        private bool TryGetImmediateFightDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            AICoreActionResultStruct<BotLogicDecision, GClass26>? dogFightDecision = combatCommon.TryGetDogFightDecision();
            if (dogFightDecision != null)
            {
                combatCommon.ClearInitialDecision();
                decision = dogFightDecision.Value;
                return true;
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!ShouldDeferExposedImmediateFireForRecovery(goalEnemy))
            {
                AICoreActionResultStruct<BotLogicDecision, GClass26>? inFightDecision = combatCommon.InFightLogic();
                if (inFightDecision != null)
                {
                    combatCommon.ClearInitialDecision();
                    decision = inFightDecision.Value;
                    return true;
                }
            }

            return false;
        }

        private bool ShouldPrioritizeRecoveryBeforeSupport(EnemyInfo goalEnemy)
        {
            return combatCommon.HasRecoveryPressure(1f) ||
                   combatCommon.IsFollowerCriticallyWounded() ||
                   combatCommon.HasUrgentHealWork() ||
                   combatCommon.IsEnemyActivelyThreateningMe(goalEnemy, HardRecoveryCloseThreatDistance, 0.75f);
        }

        private bool ShouldDeferExposedImmediateFireForRecovery(EnemyInfo? goalEnemy)
        {
            if (botOwner.Memory.IsInCover)
            {
                return false;
            }

            bool damagePressure =
                FollowerCombatCommon.WasHitRecently(botOwner, 1f) ||
                FollowerAwareness.WasRecentlyDamaged(botOwner) ||
                combatCommon.IsFollowerCriticallyWounded() ||
                combatCommon.HasUrgentHealWork();
            if (!damagePressure)
            {
                return false;
            }

            // At point blank, breaking aim to route recovery can be worse than fighting through.
            return goalEnemy == null ||
                   !goalEnemy.IsVisible ||
                   !goalEnemy.CanShoot ||
                   Enemy.Distance(goalEnemy) > Enemy.EnemyDistance.VeryClose;
        }

        private bool TryGetHealDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            AICoreActionResultStruct<BotLogicDecision, GClass26>? healDecision = combatCommon.TryGetNeedHealDecision();
            if (healDecision == null)
            {
                return false;
            }

            combatCommon.ClearInitialDecision();
            decision = healDecision.Value;
            return true;
        }

        private bool TryGetBossDistanceRegroupDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (ShouldDeferBossDistanceRegroupForCommitment())
            {
                return false;
            }

            return TryGetBossCombatObjectiveDecision(out decision);
        }

        private bool ShouldDeferBossDistanceRegroupForCommitment()
        {
            if (HasActivePushOrder() || combatPush.HasCommittedPush())
            {
                return true;
            }

            if (activeCoverIntent != CoverIntentKind.None)
            {
                return true;
            }

            if (combatCommon.HasCommittedMovement())
            {
                return true;
            }

            if (combatCommon.HasCommittedCover() && !combatCommon.IsBotInCommittedCover())
            {
                return true;
            }

            return FollowerCombatCommon.IsBossDistanceProtectedCommitmentReason(combatCommon.CommittedPositionReason) ||
                   FollowerCombatCommon.IsBossDistanceProtectedCommitmentReason(combatCommon.CommittedCoverReason);
        }

        private bool TryGetAllySupportDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (preparedAllySupportDecision.HasValue)
            {
                decision = preparedAllySupportDecision.Value;
                preparedAllySupportDecision = null;
                return true;
            }

            return TryBuildAndCommitAllySupportDecision(out decision, false);
        }

        private bool TryBuildAndCommitAllySupportDecision(
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool retryOnFailure)
        {
            decision = default;
            if (IsCoverIntentRetryActive(CoverIntentKind.Support))
            {
                return false;
            }

            if (TryBuildAndCommitPushSupportDecision(out decision))
            {
                CommitCoverIntent(CoverIntentKind.Support);
                return true;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? allySupportDecision =
                combatCommon.TryGetAllyEngagementSupportDecision();
            if (allySupportDecision == null)
            {
                if (retryOnFailure)
                {
                    supportIntentRetryUntil = Mathf.Max(supportIntentRetryUntil, Time.time + SupportBreakRetryCooldownSeconds);
                }

                return false;
            }

            CommitCoverIntent(CoverIntentKind.Support);
            decision = allySupportDecision.Value;
            return true;
        }

        private bool TryBuildAndCommitPushSupportDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            // Rifleman helper path: consume another follower's active autonomous push event and
            // convert it into support. This branch never emits a new push event and never chooses
            // direct go-to-enemy/run-to-enemy as its support objective.
            if (!combatCommon.TryGetNearbyActivePushEventForCurrentEnemy(
                    AllyPushSupportMaxStraightDistance,
                    AllyPushSupportMaxNavDistance,
                    out CombatEvents.PushEvent pushEvent))
            {
                return false;
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return false;
            }

            if (combatCommon.CanShootFromCurrentCover(out _))
            {
                // Best support is doing nothing fancy: stay in cover and shoot if this position
                // already contributes to the pusher's fight.
                combatCommon.ExtendCommittedCover();
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromCover,
                    "allyPushSupport.currentCover");
                return true;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? immediate =
                combatCommon.TryGetImmediateShootDecision("allyPushSupport.immediateShoot");
            if (immediate != null)
            {
                decision = immediate.Value;
                return true;
            }

            if (pushEvent.IsSearchPush &&
                (botOwner.Position - pushEvent.Owner.Position).sqrMagnitude <= 20f * 20f &&
                combatCommon.TryCreateTeamSearchSupportDecision(
                    pushEvent,
                    goalEnemy,
                    "allyPushSupport.teamSearch",
                    out decision))
            {
                // Search pushes use a small flank/backstop point near the pusher, not the enemy.
                return true;
            }

            if (combatCommon.TryCommitPushSupportCover(
                    goalEnemy,
                    pushEvent.Owner.Position,
                    pushEvent.EnemyPosition,
                    pushEvent.Destination,
                    "allyPushSupport.cover",
                    out string coverReason))
            {
                // Normal support push: take a cover that can watch the enemy or pusher destination.
                decision = combatCommon.CreateMoveToCommittedCoverDecision(coverReason);
                return true;
            }

            Vector3 supportAnchor = FollowerCombatCommon.IsFinite(pushEvent.EnemyPosition)
                ? pushEvent.EnemyPosition
                : pushEvent.Destination;
            // Final fallback is still a support firing point, not a direct assault push.
            return combatCommon.TryCreateSupportFiringPositionDecision(
                goalEnemy,
                supportAnchor,
                "allyPushSupport.position",
                out decision,
                preferBackline: false,
                enforceMarksmanPositionPolicy: false);
        }

        private bool TryPrepareAllySupportBreak(
            string reason,
            bool clearCommittedPosition,
            out AICoreActionEndStruct end)
        {
            end = FollowerCombatCommon.Continue();
            if (!TryBuildAndCommitAllySupportDecision(
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> supportDecision,
                    true))
            {
                return false;
            }

            if (clearCommittedPosition)
            {
                combatCommon.ClearCommittedPosition();
            }

            preparedAllySupportDecision = supportDecision;
            end = new AICoreActionEndStruct(reason, true);
            return true;
        }

        private bool TryGetPreparedAdvanceDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!preparedAdvanceDecision.HasValue)
            {
                return false;
            }

            if (combatCommon.HasActiveOrPendingHealWork())
            {
                preparedAdvanceDecision = null;
                return false;
            }

            decision = preparedAdvanceDecision.Value;
            preparedAdvanceDecision = null;
            return true;
        }

        private bool TryPrepareAdvanceBreak(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionEndStruct end)
        {
            end = FollowerCombatCommon.Continue();
            if (IsHoldReactionRetrySuppressed(goalEnemy))
            {
                return false;
            }

            if (!TryGetAdvanceDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> advanceDecision) ||
                advanceDecision.Action == BotLogicDecision.holdPosition)
            {
                ArmHoldReactionRetry(goalEnemy, "advanceNoAction");
                return false;
            }

            ClearHoldReactionRetryState();
            preparedAdvanceDecision = advanceDecision;
            end = new AICoreActionEndStruct(reason, true);
            return true;
        }

        private bool TryGetPreparedCloseCoverFightDecision(
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!preparedCloseCoverFightDecision.HasValue)
            {
                return false;
            }

            if (!combatCommon.HasActiveCombatEnemy())
            {
                preparedCloseCoverFightDecision = null;
                return false;
            }

            decision = preparedCloseCoverFightDecision.Value;
            preparedCloseCoverFightDecision = null;
            return true;
        }

        private bool TryGetPreparedRecoveryDecision(
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!preparedRecoveryDecision.HasValue)
            {
                return false;
            }

            if (!combatCommon.HasActiveCombatEnemy())
            {
                preparedRecoveryDecision = null;
                return false;
            }

            decision = preparedRecoveryDecision.Value;
            preparedRecoveryDecision = null;
            return true;
        }

        private bool TryPrepareVisibleHoldBreak(
            EnemyInfo goalEnemy,
            out AICoreActionEndStruct end)
        {
            end = FollowerCombatCommon.Continue();
            if (IsHoldReactionRetrySuppressed(goalEnemy))
            {
                return false;
            }

            if (!TryGetVisibleDecision(
                    goalEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> visibleDecision) ||
                visibleDecision.Action == BotLogicDecision.holdPosition)
            {
                ArmHoldReactionRetry(goalEnemy, "visibleNoAction");
                return false;
            }

            ClearHoldReactionRetryState();
            preparedAdvanceDecision = visibleDecision;
            end = new AICoreActionEndStruct("visibleEnemyBreakHold", true);
            return true;
        }

        private bool TryPrepareRecentHitHoldBreak(
            EnemyInfo goalEnemy,
            out AICoreActionEndStruct end)
        {
            end = FollowerCombatCommon.Continue();
            int damageRevision = FollowerAwareness.GetDamageRevision(botOwner);
            bool freshDamage = damageRevision != holdReactionDamageRevision;
            if (freshDamage)
            {
                holdReactionDamageRevision = damageRevision;
                // Actual new damage is meaningful evidence even when the previous cover/action
                // scan was deferred. Let it trigger one immediate bounded reevaluation.
                ClearHoldReactionRetryState();
            }

            if (IsHoldReactionRetrySuppressed(goalEnemy))
            {
                return false;
            }

            // A hit is only allowed to end a covered hold after a real fight handoff exists.
            // Otherwise the normal router selects the same coverHold again while the three-second
            // damage-awareness flag remains set, producing a start/end loop without changing tactics.
            if (TryGetImmediateFightDecision(
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> fightDecision) &&
                fightDecision.Action != BotLogicDecision.holdPosition)
            {
                ClearHoldReactionRetryState();
                preparedAdvanceDecision = fightDecision;
                end = new AICoreActionEndStruct("hitBreakHold", true);
                return true;
            }

            // A coverHold reason is not proof that the follower remains physically protected.
            // If a real hit arrives after EFT drops IsInCover, prepare a concrete recovery move
            // before ending. Merely ending here recreates the same coverHold every decision tick.
            if (!botOwner.Memory.IsInCover &&
                TryGetRecoverDecision(
                    goalEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> recoveryDecision,
                    forceRecovery: true,
                    allowPointBlankFightThrough: true) &&
                FollowerCombatCommon.IsMovementDecision(recoveryDecision))
            {
                ClearHoldReactionRetryState();
                preparedRecoveryDecision = recoveryDecision;
                string breakReason = freshDamage
                    ? "freshDamageOutOfCoverRecovery"
                    : "hitOutOfCoverRecoveryRetry";
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "holdSelfPreservation",
                    "prepareRecovery",
                    breakReason,
                    recoveryDecision);
                end = new AICoreActionEndStruct(breakReason, true);
                return true;
            }

            ArmHoldReactionRetry(
                goalEnemy,
                freshDamage ? "hitNoRecoveryMove" : "hitRetryNoRecoveryMove");
            return false;
        }

        private bool TryGetHoldPositionLocalSupportDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (preparedHoldPositionSupportDecision.HasValue)
            {
                decision = preparedHoldPositionSupportDecision.Value;
                preparedHoldPositionSupportDecision = null;
                return true;
            }

            return TryBuildHoldPositionLocalSupportDecision(goalEnemy, out decision);
        }

        private bool TryPrepareHoldPositionLocalSupportBreak(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionEndStruct end)
        {
            end = FollowerCombatCommon.Continue();
            if (!TryBuildHoldPositionLocalSupportDecision(
                    goalEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> supportDecision))
            {
                return false;
            }

            preparedHoldPositionSupportDecision = supportDecision;
            end = new AICoreActionEndStruct(reason, true);
            return true;
        }

        private bool TryBuildHoldPositionLocalSupportDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!combatCommon.IsTemporaryHoldPositionAggressionActive() ||
                Time.time < holdPositionSupportRetryUntil ||
                !combatCommon.HasActiveCombatEnemy(goalEnemy) ||
                (goalEnemy.IsVisible && goalEnemy.CanShoot) ||
                botOwner.Memory.IsUnderFire ||
                FollowerCombatCommon.WasHitRecently(botOwner, 0.75f) ||
                FollowerAwareness.WasRecentlyDamaged(botOwner))
            {
                return false;
            }

            Vector3 enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
            if (!FollowerCombatCommon.IsFinite(enemyAnchor))
            {
                return false;
            }

            bool found = combatCommon.TryCreateSupportFiringPositionDecision(
                goalEnemy,
                enemyAnchor,
                "holdPosition.localSupport",
                out decision,
                preferBackline: true,
                enforceMarksmanPositionPolicy: false,
                allowForwardPositions: false,
                allowBattlefieldPositions: false,
                maxNavDistance: HoldPositionLocalSupportMaxNavDistance);

            if (!found)
            {
                holdPositionSupportRetryUntil = Time.time + HoldPositionLocalSupportRetryCooldownSeconds;
            }

            return found;
        }

        /// <summary>
        /// Keeps decision end conditions local to the simplified combat state machine.
        /// </summary>
        public AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            if (FollowerCombatCommon.IsRecoveryManeuverReason(currentDecision.Reason) &&
                FollowerCombatCommon.IsMovementDecision(currentDecision))
            {
                return EndCoverMoveOrAttackMoving(currentDecision);
            }

            // Fight actions should not be interrupted by regroup/support routing, but they still
            // need their own end checks so stale visible-fire decisions release when LOS is gone.
            if (combatCommon.IsInFight(currentDecision.Action))
            {
                if (currentDecision.Action == BotLogicDecision.shootFromPlace &&
                    TryPrepareExposedFireRecoveryBreak(currentDecision, out AICoreActionEndStruct recoveryBreak))
                {
                    return recoveryBreak;
                }

                return combatCommon.ShallEndCurrentDecision(currentDecision);
            }

            // Explicit regroup commands should interrupt any ongoing action immediately.
            // expect if bot has clearly visible enemy and is enganged
            if (HasExplicitRegroupOrder())
            {
                combatPush.ClearCommittedPush("explicitRegroup");
                combatCommon.ClearCommittedCover();

                combatCommon.ClearCommittedPosition();

                ClearCoverIntent();
                return new AICoreActionEndStruct("defaultExplicitRegroup", true);
            }

            if (currentDecision.Action == BotLogicDecision.holdPosition &&
                FollowerCombatPush.IsPushReason(currentDecision.Reason))
            {
                return EndPushSearchHold(currentDecision.Reason);
            }

            if (FollowerCombatPush.IsPushReason(currentDecision.Reason) ||
                FollowerCombatPush.IsStartWeakEnemyPushReason(currentDecision.Reason))
            {
                return combatPush.EndCommittedPush(currentDecision);
            }

            switch (currentDecision.Action)
            {
                case BotLogicDecision.holdPosition:
                    return EndHoldPosition(currentDecision.Reason);
                case BotLogicDecision.runToCover:
                case BotLogicDecision.attackMoving:
                case BotLogicDecision.attackMovingWithSuppress:
                    return EndCoverMoveOrAttackMoving(currentDecision);
                case BotLogicDecision.shootFromCover:
                    return EndShootFromCover(currentDecision.Reason);
                case BotLogicDecision.suppressFire:
                    return FollowerCombatCommon.IsRecoveryNoCoverReason(currentDecision.Reason)
                        ? combatCommon.EndRecoveryNoCoverSuppress(currentDecision.Reason)
                        : combatCommon.EndSuppressFire(currentDecision.Reason);
                default:
                    return combatCommon.ShallEndCurrentDecision(currentDecision);
            }
        }

        /// <summary>
        /// Prioritizes immediate action on visible enemies: fire now, take cover for fire, or pressure forward.
        /// </summary>
        private bool TryGetVisibleDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!goalEnemy.IsVisible)
            {
                return false;
            }

            // If the previous cover move just armed an arrival hold, do not let visible-cover
            // acquisition immediately reselect the same already-reached cover. The holder itself
            // decides whether contact is urgent enough to break.
            if (combatCommon.HasCommittedPosition(out decision))
            {
                return true;
            }

            // If the enemy is visible and the bot can already fire, resolve the fight immediately
            // instead of falling through to slower blind-search or recovery logic.
            if (goalEnemy.CanShoot)
            {
                // Very-close visible enemies skip cover logic and collapse into point-blank combat.
                if (Enemy.Distance(goalEnemy) <= Enemy.EnemyDistance.VeryClose)
                {
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "visibleDogFight");
                    return true;
                }

                // A fresh immediate-fire window can snap to exposed/flanking enemies, but a bot
                // already in cover must first prove a crouch or standing cover lane exists.
                if (combatCommon.ShouldShootImmediately())
                {
                    if (!botOwner.Memory.IsInCover)
                    {
                        decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "visibleImmediateShoot");
                        return true;
                    }

                    if (TryGetCoveredVisibleFireOrAdvanceDecision(goalEnemy, "visibleImmediateCoverFire", out decision))
                    {
                        return true;
                    }

                    // Covered but no verified crouch/standing lane: do not force shootFromPlace into
                    // the cover wall. Let the normal cover/reposition branches below pick the next move.
                }

                // If the bot is already protected by cover and that cover still supports a real shot,
                // use it immediately instead of re-evaluating movement.
                if (botOwner.Memory.IsInCover &&
                    TryGetCoveredVisibleFireOrAdvanceDecision(goalEnemy, "coverVisibleFire", out decision))
                {
                    return true;
                }

                // If the bot is exposed and current context says "take the fight from cover",
                // commit one shooting cover before trading from the open.
                if (!botOwner.Memory.IsInCover &&
                    combatCommon.ShouldTakeVisibleCover(goalEnemy) &&
                    TryCommitCombatCover(goalEnemy, requireShootLane: true, out string coverReason, avoidBossFireLane: true))
                {
                    decision = combatCommon.CreateMoveToCommittedCoverDecision(coverReason);
                    return true;
                }

                if (botOwner.Memory.IsInCover)
                {
                    return false;
                }

                // Otherwise an exposed bot with corrected visible, shootable contact stands and fires.
                if (goalEnemy.CanShoot)
                {
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "visibleShoot");
                    return true;
                }

                return false;
            }

            // The enemy is seen but cannot be shot from the current spot, so first try to take one
            // committed firing cover that should open the lane.
            // The enemy is visible but not yet shootable from here, so prefer moving to a cover
            // that creates a lane before considering forward pressure.
            if (!botOwner.Memory.IsInCover && ShouldForceVisibleAlignedFire(goalEnemy))
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "visibleAlignedShoot");
                return true;
            }

            if (!botOwner.Memory.IsInCover &&
                TryCommitCombatCover(goalEnemy, requireShootLane: true, out string visibleCoverReason, avoidBossFireLane: true))
            {
                decision = combatCommon.CreateMoveToCommittedCoverDecision(visibleCoverReason);
                return true;
            }

            // If no immediate firing position exists, aggressive followers are allowed to close distance
            // while the enemy is still visible.
            if (!combatCommon.HasActiveOrPendingHealWork() &&
                combatCommon.ShouldAdvance(goalEnemy) &&
                goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetVisiblePushDistance())
            {
                // Once local logic decides a visible enemy should be pushed, hand off to the old-plugin
                // engage helper so it can choose rush/walk/approach behavior from its richer threat checks.
                bool enemyLowThreat = combatCommon.IsEnemyLowThreat(goalEnemy, combatCommon.GetAggression01());
                decision = combatPush.EngageEnemy(FollowerCombatPush.PushActivationSource.Automatic, enemyLowThreat);
                return true;
            }

            // Visible contact still matters, but if there is no immediate fire/cover answer let the
            // engage helper decide whether this should become a push, search, or brief pressure hold.
            return false;
        }

        private bool TryGetCoveredVisibleFireOrAdvanceDecision(
            EnemyInfo goalEnemy,
            string coverFireReason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (combatCommon.CanShootFromCurrentCoverOrStandingIntent(out _))
            {
                combatCommon.ExtendCommittedCover();
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, coverFireReason);
                return true;
            }

            if (combatCommon.HasActiveOrPendingHealWork() ||
                !combatCommon.ShouldAdvance(goalEnemy))
            {
                return false;
            }

            combatCommon.ClearCommittedCover();
            bool enemyLowThreat = combatCommon.IsEnemyLowThreat(goalEnemy, combatCommon.GetAggression01());
            decision = combatPush.EngageEnemy(FollowerCombatPush.PushActivationSource.Automatic, enemyLowThreat);
            return true;
        }

        /// <summary>
        /// Keeps the bot tied to a chosen cover point long enough to actually arrive, settle, and use it.
        /// </summary>
        private bool TryGetCommittedCoverDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            // No committed cover means this branch has nothing to manage, so let the rest of the
            // combat tree choose a fresh action.
            if (!combatCommon.HasCommittedCover())
            {
                shootCoverSettlePhase.Clear();
                ClearCoverIntent();
                return false;
            }

            // If the bot has actually reached the committed point, this method owns the next step:
            // either fight from that cover, briefly settle, or hold it.
            if (combatCommon.IsBotInCommittedCover())
            {
                shootCoverSettlePhase.PromoteToHoldOnArrival();
                UpdateCoverIntentArrival();

                if (HasActivePushOrder())
                {
                    shootCoverSettlePhase.Clear();
                    combatCommon.ClearCommittedCover();
                    ClearCoverIntent();
                    return false;
                }

                if (goalEnemy.IsVisible && combatCommon.ShouldShootImmediately())
                {
                    if (TryGetCoveredVisibleFireOrAdvanceDecision(goalEnemy, "committedImmediateCoverFire", out decision))
                    {
                        return true;
                    }

                    // Reached committed cover but no valid crouch/standing lane exists. Continue into
                    // the visible-cover handling below instead of forcing shootFromPlace into geometry.
                }

                // Once the bot has actually reached committed cover, shooting from that cover always wins.
                if (goalEnemy.IsVisible && TryGetCoveredVisibleFireOrAdvanceDecision(goalEnemy, "committedFire", out decision))
                {
                    return true;
                }

                // Visible enemies should not get trapped in passive cover hold just because the current
                // cover-fire validation failed. Break out into active pressure instead.
                if (goalEnemy.IsVisible)
                {
                    return false;
                }

                if (shootCoverSettlePhase.IsHolding)
                {
                    combatCommon.HoldFor(ShootCoverSettleSeconds);
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, ShootCoverHoldReason);
                    return true;
                }

                // Once the initial cover settle is over, boss-distance regroup should beat passive
                // local cover churn unless there is already a real visible shot to solve here.
                if (ShouldBreakCommittedCoverForBossObjective(goalEnemy))
                {
                    shootCoverSettlePhase.Clear();
                    combatCommon.ClearCommittedCover();
                    ClearCoverIntent();
                    return false;
                }

                if (TryGetCoverIntentRepositionDecision(goalEnemy, out decision))
                {
                    return true;
                }

                if (ShouldReleaseCoverIntentForOpportunity(goalEnemy))
                {
                    shootCoverSettlePhase.Clear();
                    combatCommon.ClearCommittedCover();
                    ClearCoverIntent();
                    return false;
                }

                if (ShouldExpireCoverIntent())
                {
                    shootCoverSettlePhase.Clear();
                    combatCommon.ClearCommittedCover();
                    ApplyCoverIntentRetryCooldown();
                    ClearCoverIntent();
                    return false;
                }

                if (!goalEnemy.IsVisible && combatCommon.ShouldAdvance(goalEnemy))
                {
                    shootCoverSettlePhase.Clear();
                    combatCommon.ClearCommittedCover();
                    ClearCoverIntent();
                    return false;
                }

                if (!goalEnemy.IsVisible && combatCommon.HasReliablePersonalEnemyLocation(goalEnemy))
                {
                    shootCoverSettlePhase.Clear();
                    combatCommon.ClearCommittedCover();
                    ClearCoverIntent();
                    return false;
                }

                // Otherwise remain in the committed cover and wait for the next actionable event.
                combatCommon.HoldCoverForMaxDuration();
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "coverHold");
                return true;
            }

            // The bot has not reached its committed cover yet, so keep feeding the same cover back into EFT
            // until arrival or invalidation.
            combatCommon.AssignCommittedCover();
            decision = combatCommon.CreateCommittedCoverMoveDecision();
            return true;
        }

        /// <summary>
        /// Forces cover-seeking when recent damage, suppression, or poor enemy certainty makes standing unsafe.
        /// </summary>
        private bool TryGetRecoverDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool forceRecovery = false,
            bool allowPointBlankFightThrough = true)
        {
            decision = default;
            // Recovery only matters while exposed. If the bot is already in cover, let the normal
            // visible/committed-cover logic decide whether to shoot or hold.
            if (botOwner.Memory.IsInCover)
            {
                return false;
            }

            bool needCover =
                forceRecovery ||
                combatCommon.HasRecoveryPressure(1f) ||
                combatCommon.IsFollowerCriticallyWounded() ||
                combatCommon.IsEnemyActivelyThreateningMe(goalEnemy, 18f, 0.75f);

            // If the enemy was only just lost and the bot does not have a trustworthy personal location,
            // bias toward safety instead of standing in the open waiting for certainty.
            if (!needCover &&
                !goalEnemy.IsVisible &&
                Time.time - goalEnemy.PersonalLastSeenTime < RecentSeenPressureSeconds &&
                !combatCommon.HasReliablePersonalEnemyLocation(goalEnemy))
            {
                needCover = true;
            }

            if (!needCover)
            {
                return false;
            }

            if (allowPointBlankFightThrough && ShouldFightThroughPointBlankRecovery(goalEnemy))
            {
                if (TryCreatePointBlankRecoveryPressure(goalEnemy, out decision))
                {
                    return true;
                }

                return false;
            }

            if (combatCommon.TryGetCommittedRecoveryDecision(goalEnemy, out decision))
            {
                return true;
            }

            // With no credible cover, keep facing and fighting the threat. Common owns the bounded
            // commitment so every tactic retries recovery without an end-and-reselect loop.
            decision = combatCommon.CreateNoCoverRecoveryDecision(goalEnemy);
            return true;
        }

        private bool ShouldFightThroughPointBlankRecovery(EnemyInfo goalEnemy)
        {
            if (Enemy.Distance(goalEnemy) > Enemy.EnemyDistance.VeryClose)
            {
                return false;
            }

            // At point blank, a healthy rifleman turning away for retreat cover is usually worse
            // than forcing local pressure. Only critical medical pressure is allowed to override it.
            return !combatCommon.IsFollowerCriticallyWounded() &&
                   !combatCommon.HasUrgentHealWork();
        }

        private bool TryCreatePointBlankRecoveryPressure(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromPlace,
                    "pointBlankRecoveryShoot");
                return true;
            }

            // This is only a short continuity burst for a just-lost close threat. Do not create a
            // generic suppress-from-point movement here; when the recent-contact window expires,
            // reselecting that move every frame creates churn instead of useful pressure.
            if (FollowerImmediateFirePolicy.CanUseRecentContactSuppress(goalEnemy))
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    "pointBlankRecoveryRecentSuppress");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Uses the restored old-plugin engage helper only after the current tree has already decided
        /// that a non-visible enemy should still be actively pushed.
        /// </summary>
        private bool TryGetAdvanceDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (combatCommon.HasActiveOrPendingHealWork())
            {
                return false;
            }

            bool pushOrdered = combatCommon.ShouldAdvance(goalEnemy);
            if (!pushOrdered)
            {
                return false;
            }

            if (goalEnemy.IsVisible)
            {
                return false;
            }

            bool enemyLowThreat = combatCommon.IsEnemyLowThreat(goalEnemy, combatCommon.GetAggression01());
            decision = combatPush.EngageEnemy(FollowerCombatPush.PushActivationSource.Automatic, enemyLowThreat);
            return true;
        }

        /// <summary>
        /// Converts boss-distance pressure into a regroup objective switch. Ordered push and immediate
        /// local survival branches run earlier and still win before this objective trigger.
        /// </summary>
        private bool TryGetBossCombatObjectiveDecision(
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            if (!ShouldRegroupForBossDistance())
            {
                return false;
            }

            // Do not try to execute the bossward move here anymore. Default combat only decides that
            // "bossward regroup should now be the primary objective". The router will immediately swap
            // objectives and ask regroup for the real move/fire decision in the same frame.
            decision = combatCommon.CreateRegroupObjectiveDecision();
            return true;
        }

        private bool ShouldBlockPassiveBossHold(EnemyInfo goalEnemy)
        {
            if (botOwner.Memory.IsInCover)
            {
                return false;
            }

            bool recentHitPressure =
                botOwner.Memory.IsUnderFire ||
                FollowerCombatCommon.WasHitRecently(botOwner, 1f) ||
                FollowerAwareness.WasRecentlyDamaged(botOwner);
            if (!recentHitPressure)
            {
                return false;
            }

            if (goalEnemy.CanShoot || Enemy.IsVisible(botOwner, goalEnemy))
            {
                return true;
            }

            return Time.time - goalEnemy.PersonalLastSeenTime < RecentSeenPressureSeconds &&
                   combatCommon.HasReliablePersonalEnemyLocation(goalEnemy);
        }

        private bool ShouldForceVisibleAlignedFire(EnemyInfo goalEnemy)
        {
            Vector3 enemyPosition = goalEnemy.CurrPosition;
            Vector3 toEnemy = enemyPosition - botOwner.Position;
            toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            float distance = toEnemy.magnitude;
            if (distance > VisibleAlignedFireMaxDistance)
            {
                return false;
            }

            Vector3 lookDirection = botOwner.LookDirection;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            float lookAngle = Vector3.Angle(lookDirection.normalized, toEnemy.normalized);
            if (lookAngle > VisibleAlignedFireMaxAngle ||
                Time.time - goalEnemy.PersonalSeenTime > 1.25f)
            {
                return false;
            }

            Vector3 target = enemyPosition + Vector3.up * 0.8f;
            return FollowerImmediateFirePolicy.HasDirectFireLane(botOwner, target);
        }

        /// <summary>
        /// Low aggression can force regroup as the primary combat objective. This is stronger than the
        /// normal boss-line split and exists mainly so defensive followers stop solving the fight from
        /// their current position unless the enemy is already close enough to demand local engagement.
        /// 0% aggression only fights at VeryClose. Up to 30% aggression only fights at Close.
        /// </summary>
        private bool TryGetLowAggressionRegroupDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            float aggression = combatCommon.GetAggression01();
            if (aggression > 0.3f)
            {
                return false;
            }

            Enemy.EnemyDistance maxLocalEngageDistance = aggression <= 0.01f
                ? Enemy.EnemyDistance.VeryClose
                : Enemy.EnemyDistance.Close;

            if (Enemy.Distance(goalEnemy) <= maxLocalEngageDistance)
            {
                return false;
            }

            Vector3 bossPosition = combatCommon.GetBossPosition();
            if (combatCommon.GetBossNavDistance(bossPosition) <=
                CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius())
            {
                return false;
            }

            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.standBy,
                FollowerCombatRegroupObjective.ActivateRegroupReason);
            return true;
        }

        /// <summary>
        /// Boss-under-attack is support escalation, not a push order. After survival gates have
        /// already passed, the follower first tries to contribute from its current position, then
        /// moves only if the current position is tactically useless.
        /// </summary>
        private bool TryGetBossUnderAttackDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (FollowerCombatAnchor.IsCombatIndependent(botOwner))
            {
                return false;
            }

            if (combatCommon.GetBossProtectionWillingness01() < PickupFollowerPersonality.ProtectBossMinWillingness)
            {
                return false;
            }

            if (IsCoverIntentRetryActive(CoverIntentKind.ProtectBoss))
            {
                return false;
            }

            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            AIBossPlayerLogic? bossLogic = boss.GetBossLogic();
            if (bossLogic == null || !bossLogic.IsHitted)
            {
                return false;
            }

            // Immediate visible personal combat still wins over boss-protection routing.
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            float sinceLastSeen = Time.time - goalEnemy.PersonalLastSeenTime;
            if (botOwner.Memory.HaveEnemy && sinceLastSeen > 2.5f)
            {
                return false;
            }

            BotOwner? bossEnemy = boss.ClosestEnemy();
            if (bossEnemy == null || bossEnemy.GetPlayer?.HealthController?.IsAlive != true)
            {
                return false;
            }

            if (!combatCommon.TryUseSupportGoalEnemy(bossEnemy, "bossUnderAttack", out EnemyInfo? prioritizedEnemy) ||
                !combatCommon.HasActiveCombatEnemy(prioritizedEnemy))
            {
                return false;
            }

            Vector3 bossPosition = combatCommon.GetBossPosition();
            FollowerCombatTactic tactic = combatCommon.GetFollowerTactic();
            float nearbySupportRadius = CombatDistanceConfiguration.Instance.GetBossSupportShootCoverRadius();
            float widerSupportRadius = Mathf.Min(
                CombatDistanceConfiguration.Instance.GetCombatCoverMaxDistance(),
                60f);

            if (prioritizedEnemy.CanShoot)
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromPlace,
                    "protectBossFire");
                MarkBossSupportDecisionPrepared();
                return true;
            }

            if (combatCommon.TryCreateSuppressFromPlaceDecision(
                    prioritizedEnemy,
                    "protectBossSuppress",
                    out decision,
                    allowSoftObstructedSuppression: true))
            {
                MarkBossSupportDecisionPrepared();
                return true;
            }

            // Fast nearby support cover keeps the follower useful without destabilizing a valid formation.
            if (combatCommon.TryCommitSupportFiringCover(
                    prioritizedEnemy,
                    "protectBossSupportCover.near",
                    out string nearbyCoverReason,
                    preferBackline: tactic == FollowerCombatTactic.Protector,
                    maxSearchRadius: nearbySupportRadius))
            {
                CommitCoverIntent(CoverIntentKind.ProtectBoss);
                decision = combatCommon.CreateMoveToCommittedCoverDecision(nearbyCoverReason);
                MarkBossSupportDecisionPrepared();
                return true;
            }

            if (ShouldRegroupForBossDistance())
            {
                decision = combatCommon.CreateRegroupObjectiveDecision();
                MarkBossSupportDecisionPrepared();
                return true;
            }

            // Wider support cover is still bot-centered; it improves firing position without collapsing
            // every follower into the same boss-local cover pocket.
            if (combatCommon.TryCommitSupportFiringCover(
                    prioritizedEnemy,
                    "protectBossSupportCover.wide",
                    out string wideCoverReason,
                    preferBackline: tactic == FollowerCombatTactic.Protector,
                    maxSearchRadius: widerSupportRadius))
            {
                CommitCoverIntent(CoverIntentKind.ProtectBoss);
                decision = combatCommon.CreateMoveToCommittedCoverDecision(wideCoverReason);
                MarkBossSupportDecisionPrepared();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Ordered GoForward should commit to a concrete firing position against the enemy's
        /// current body position before falling back to generic pressure/search behavior.
        /// </summary>
        private bool TryGetOrderedPushDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            if (followerData == null ||
                !followerData.TryGetActiveCommand(out FollowerCommandType activeCommand, out _) ||
                activeCommand != FollowerCommandType.PushEnemy)
            {
                return false;
            }

            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                followerData.ClearCommand("PushEnemy:noActiveEnemy");
                return false;
            }

            if (combatCommon.HasActiveOrPendingHealWork())
            {
                return false;
            }

            if (combatPush.TryCreateOrderedPushFiringPosition(goalEnemy, out decision))
            {
                return true;
            }

            decision = MarkOrderedPushDecision(combatPush.EngageEnemy(FollowerCombatPush.PushActivationSource.Ordered));
            return true;
        }

        private static AICoreActionResultStruct<BotLogicDecision, GClass26> MarkOrderedPushDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (decision.Reason == null ||
                decision.Reason.StartsWith("push.ordered", StringComparison.Ordinal))
            {
                return decision;
            }

            if (decision.Reason.StartsWith("push.", StringComparison.Ordinal))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    decision.Action,
                    "push.ordered." + decision.Reason.Substring("push.".Length));
            }

            return decision;
        }

        private bool TryGetAutonomousSuppressDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (Time.time < autoSuppressRetryUntil)
            {
                return false;
            }

            autoSuppressRetryUntil = Time.time + AutoSuppressRetryCooldownSeconds;
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return false;
            }

            // Shared/priority contact can keep an enemy known, but it is not fire authorization.
            // Wait for personal contact before autonomous suppression; explicit suppression orders
            // remain objective-owned and may intentionally use a reported target.
            if (Utils.Enemy.IsMemoryOnlyAcquisitionWithoutPersonalContact(goalEnemy))
            {
                return false;
            }

            if (goalEnemy.IsSuppressed() || !goalEnemy.ShallISuppress())
            {
                return false;
            }

            if (combatCommon.IsGrenadeLauncherSuppressCooldownActive(ordered: false, out _))
            {
                combatCommon.RecordGrenadeLauncherSuppressCooldownSkip(
                    ordered: false,
                    "autoSuppress");
            }
            else if (combatCommon.HasAutonomousGrenadeLauncherTarget(goalEnemy, out _))
            {
                if (combatCommon.HasPendingLauncherPrimaryFallback())
                {
                    return false;
                }

                autoSuppressRetryUntil = Time.time +
                                         FollowerCombatGrenadierObjective.OpportunityWindowSeconds +
                                         AutoSuppressRetryCooldownSeconds;
                decision = FollowerCombatGrenadierObjective.CreateAutonomousActivationDecision();
                return true;
            }

            if (combatCommon.TryCreateSuppressDecision(goalEnemy, "autoSuppress", out decision))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Finds and commits a single cover point for the current threat instead of constantly re-picking.
        /// </summary>
        private bool TryCommitCombatCover(
            EnemyInfo goalEnemy,
            bool requireShootLane,
            out string reason,
            bool avoidBossFireLane = false,
            bool recoveryManeuver = false)
        {
            return combatCommon.TryCommitCombatCover(
                goalEnemy,
                requireShootLane,
                CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius(),
                out reason,
                avoidBossFireLane,
                recoveryManeuver);
        }

        /// <summary>
        /// Ends passive hold when the bot leaves committed cover or should transition back into action.
        /// </summary>
        private AICoreActionEndStruct EndHoldPosition(string reason)
        {
            combatCommon.ValidateCommittedCover();

            if (string.Equals(reason, FollowerCombatCommon.RecoveryNoCoverThreatHoldReason, StringComparison.Ordinal))
            {
                return combatCommon.EndRecoveryNoCoverThreatHold();
            }

            if (combatCommon.IsCommittedHolderReason(reason))
            {
                return EndCommittedHolder(reason);
            }

            if (combatCommon.HasActiveCombatGestureOrder())
            {
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("combatGestureBreakHold", true);
            }

            if (FollowerCombatCommon.IsReloadHoldReason(reason))
            {
                return combatCommon.EndReloadHold(reason);
            }

            if (FollowerCombatCommon.IsWeaponPreparationHoldReason(reason))
            {
                return combatCommon.EndWeaponPreparationHold(reason);
            }

            if (string.Equals(reason, ShootCoverHoldReason, StringComparison.Ordinal))
            {
                return EndShootCoverHoldPosition();
            }

            bool isCoverHoldReason = IsCoverHoldReason(reason);
            bool isHoldingInCover = isCoverHoldReason ||
                                    botOwner.Memory.IsInCover ||
                                    combatCommon.IsBotInCommittedCover();
            bool isRecoveryHold = FollowerCombatCommon.IsRecoveryManeuverReason(reason);
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;

            if (isHoldingInCover)
            {
                if (HasActivePushOrder())
                {
                    return new AICoreActionEndStruct("orderedPushBreakHold", true);
                }

                if (string.Equals(reason, "coverHold", StringComparison.Ordinal) &&
                    combatCommon.HasCommittedCover() &&
                    !combatCommon.IsBotInCommittedCover())
                {
                    return new AICoreActionEndStruct("leftCommittedCover", true);
                }

                if (!isRecoveryHold &&
                    goalEnemy != null &&
                    ShouldBreakForBossUnderAttack(goalEnemy))
                {
                    combatCommon.ClearCommittedCover();

                    return new AICoreActionEndStruct(
                        IsBossHoldReason(reason)
                            ? "bossUnderAttackBreakBossHold"
                            : "bossUnderAttackBreakCoverHold",
                        true);
                }

                if (goalEnemy != null &&
                    (FollowerCombatCommon.WasHitRecently(botOwner, 0.75f) ||
                     FollowerAwareness.WasRecentlyDamaged(botOwner)))
                {
                    if (isRecoveryHold)
                    {
                        combatCommon.HoldCoverForMaxDuration();
                        return FollowerCombatCommon.Continue();
                    }

                    if (TryPrepareRecentHitHoldBreak(goalEnemy, out AICoreActionEndStruct hitBreak))
                    {
                        return hitBreak;
                    }

                    combatCommon.HoldCoverForMaxDuration();
                    return FollowerCombatCommon.Continue();
                }

                if (!isRecoveryHold &&
                    goalEnemy != null &&
                    ShouldBreakCommittedCoverForBossObjective(goalEnemy))
                {
                    combatCommon.ClearCommittedCover();

                    return new AICoreActionEndStruct(
                        IsBossHoldReason(reason)
                            ? "bossObjectiveBreakBossHold"
                            : "bossObjectiveBreakCoverHold",
                        true);
                }

                if (!isRecoveryHold &&
                    goalEnemy != null &&
                    TryPrepareAllySupportBreak(
                        "allyEngagementBreakHold",
                        false,
                        out AICoreActionEndStruct allySupportBreak))
                {
                    return allySupportBreak;
                }

                if (!isRecoveryHold && goalEnemy != null && !goalEnemy.IsVisible)
                {
                    if (TryPrepareAdvanceBreak(
                            goalEnemy,
                            "advanceFromCover",
                            out AICoreActionEndStruct advanceBreak))
                    {
                        return advanceBreak;
                    }

                    // No usable advance was produced. Stay in the current hold while the retry
                    // signature is unchanged instead of ending/reselecting the same no-op hundreds
                    // of times. A meaningful geometry/contact change releases this early.
                    if (IsHoldReactionRetrySuppressed(goalEnemy))
                    {
                        return FollowerCombatCommon.Continue();
                    }
                }

                if (goalEnemy != null && goalEnemy.IsVisible)
                {
                    if (TryPrepareVisibleHoldBreak(goalEnemy, out AICoreActionEndStruct visibleBreak))
                    {
                        return visibleBreak;
                    }

                    if (IsHoldReactionRetrySuppressed(goalEnemy))
                    {
                        return FollowerCombatCommon.Continue();
                    }
                }
            }
            else if (goalEnemy != null &&
                     IsBossHoldReason(reason) &&
                     TryPrepareHoldPositionLocalSupportBreak(
                         goalEnemy,
                         "holdPositionLocalSupportBreak",
                         out AICoreActionEndStruct holdSupportBreak))
            {
                return holdSupportBreak;
            }

            return combatCommon.EndBaseHoldPosition(reason);
        }

        private AICoreActionEndStruct EndPushSearchHold(string reason)
        {
            if (combatCommon.HasActiveOrPendingHealWork())
            {
                return new AICoreActionEndStruct("pushNeedHeal", true);
            }

            return combatCommon.EndBaseHoldPosition(reason);
        }

        private AICoreActionEndStruct EndCommittedHolder(string reason)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            bool isRecoveryHold = FollowerCombatCommon.IsRecoveryManeuverReason(reason);
            if (combatCommon.HasActiveCombatGestureOrder())
            {
                combatCommon.ClearCommittedPosition();
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("combatGestureBreakCommittedHold", true);
            }

            if (HasActivePushOrder())
            {
                combatCommon.ClearCommittedPosition();
                return new AICoreActionEndStruct("orderedPushBreakCommittedHold", true);
            }

            if (botOwner.Memory.IsUnderFire ||
                FollowerCombatCommon.WasHitRecently(botOwner, 0.75f) ||
                FollowerAwareness.WasRecentlyDamaged(botOwner))
            {
                if (isRecoveryHold && botOwner.Memory.IsInCover)
                {
                    combatCommon.HoldCoverForMaxDuration();
                    return FollowerCombatCommon.Continue();
                }

                if (isRecoveryHold &&
                    TryPrepareExposedRecoveryHoldFight(goalEnemy, out AICoreActionEndStruct recoveryFightBreak))
                {
                    return recoveryFightBreak;
                }

                if (isRecoveryHold && combatCommon.IsBotInCommittedCover())
                {
                    // Proximity is enough to complete movement, but it is not proof that the point
                    // protects the follower. Keep only the short settle grace unless a concrete
                    // exposed-fight handoff can be prepared above.
                    combatCommon.HoldCoverForMaxDuration();
                    return FollowerCombatCommon.Continue();
                }

                combatCommon.ClearCommittedPosition();
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("underFireBreakCommittedHold", true);
            }

            bool canBreakForEngagement = combatCommon.HasCommittedHolderSettled(CommittedHolderEngagementBreakSettleSeconds);

            if (canBreakForEngagement &&
                !isRecoveryHold &&
                goalEnemy != null &&
                ShouldBreakForBossUnderAttack(goalEnemy))
            {
                combatCommon.ClearCommittedPosition();
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("bossUnderAttackBreakCommittedHold", true);
            }

            if (canBreakForEngagement &&
                !isRecoveryHold &&
                goalEnemy != null &&
                TryPrepareAllySupportBreak(
                    "allyEngagementBreakCommittedHold",
                    true,
                    out AICoreActionEndStruct allySupportBreak))
            {
                return allySupportBreak;
            }

            if (goalEnemy != null && IsCommittedHoldEnemyContact(goalEnemy))
            {
                combatCommon.ClearCommittedPosition();
                return new AICoreActionEndStruct("enemyContactBreakCommittedHold", true);
            }

            if (!FollowerCombatCommon.IsBossDistanceProtectedCommitmentReason(reason) &&
                ShouldRegroupForBossDistance())
            {
                combatCommon.ClearCommittedPosition();
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("bossDistanceBreakCommittedHold", true);
            }

            if (!combatCommon.IsCommittedHolderTimerActive())
            {
                combatCommon.ClearCommittedPosition();
                return new AICoreActionEndStruct("committedHoldExpired", true);
            }

            return FollowerCombatCommon.Continue();
        }

        private bool TryPrepareExposedRecoveryHoldFight(
            EnemyInfo? goalEnemy,
            out AICoreActionEndStruct end)
        {
            end = FollowerCombatCommon.Continue();
            if (goalEnemy == null ||
                !goalEnemy.IsVisible ||
                !goalEnemy.CanShoot ||
                botOwner.Memory.IsInCover ||
                !combatCommon.HasCommittedHolderSettled(CommittedHolderEngagementBreakSettleSeconds) ||
                !FollowerCombatCommon.WasHitRecently(botOwner, 0.75f))
            {
                return false;
            }

            BotWeaponManager? weaponManager = botOwner.WeaponManager;
            if (weaponManager == null ||
                weaponManager.Reload?.Reloading == true ||
                weaponManager.Selector?.IsChanging == true ||
                !weaponManager.IsWeaponReady ||
                !weaponManager.HaveBullets)
            {
                return false;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26> fightDecision;
            if (Enemy.Distance(goalEnemy) <= Enemy.EnemyDistance.VeryClose)
            {
                AICoreActionResultStruct<BotLogicDecision, GClass26>? dogFightDecision =
                    combatCommon.TryGetDogFightDecision();
                if (!dogFightDecision.HasValue)
                {
                    return false;
                }

                fightDecision = dogFightDecision.Value;
            }
            else
            {
                fightDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromPlace,
                    FollowerCombatCommon.RecoveryNoCoverFightReason);
            }

            preparedCloseCoverFightDecision = fightDecision;
            combatCommon.BlockCommittedRecoveryCover("arrivalStillExposedUnderFire");
            combatCommon.ClearCommittedPosition("recoveryExposedFightPrepared");
            combatCommon.ClearCommittedCover("recoveryExposedFightPrepared");
            ClearCoverIntent();
            end = new AICoreActionEndStruct("recoveryExposedFightPrepared", true);
            return true;
        }

        private void UpdateExposedFireLease(
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            if (nextDecision.Action != BotLogicDecision.shootFromPlace)
            {
                ResetExposedFireLease();
                return;
            }

            exposedFireEnemyProfileId = botOwner.Memory?.GoalEnemy?.ProfileId ?? string.Empty;
            exposedFireDecisionReason = nextDecision.Reason ?? string.Empty;
            exposedFireStartedAt = Time.time;
            exposedFireInitialTriggerPressedAt = botOwner.ShootData?.LastTriggerPressd ?? 0f;
            exposedFireInitialDamageRevision = FollowerAwareness.GetDamageRevision(botOwner);
            exposedFireRecoveryRetryAt = 0f;
        }

        private void ResetExposedFireLease()
        {
            exposedFireEnemyProfileId = string.Empty;
            exposedFireDecisionReason = string.Empty;
            exposedFireStartedAt = 0f;
            exposedFireInitialTriggerPressedAt = 0f;
            exposedFireInitialDamageRevision = 0;
            exposedFireRecoveryRetryAt = 0f;
        }

        private bool TryPrepareExposedFireRecoveryBreak(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision,
            out AICoreActionEndStruct end)
        {
            end = FollowerCombatCommon.Continue();
            if (FollowerCombatCommon.IsRecoveryNoCoverReason(currentDecision.Reason) ||
                FollowerCombatCommon.IsGrenadeLauncherCombatReason(currentDecision.Reason))
            {
                return false;
            }

            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy) ||
                goalEnemy == null ||
                botOwner.Memory.IsInCover ||
                !goalEnemy.IsVisible ||
                !goalEnemy.CanShoot ||
                goalEnemy.Distance <= ExposedFirePointBlankDistance ||
                !botOwner.IsEnemyLookingAtMe(goalEnemy))
            {
                return false;
            }

            string enemyProfileId = goalEnemy.ProfileId ?? string.Empty;
            string decisionReason = currentDecision.Reason ?? string.Empty;
            if (exposedFireStartedAt <= 0f ||
                !string.Equals(exposedFireEnemyProfileId, enemyProfileId, StringComparison.Ordinal) ||
                !string.Equals(exposedFireDecisionReason, decisionReason, StringComparison.Ordinal))
            {
                UpdateExposedFireLease(currentDecision);
                return false;
            }

            bool freshDamage = FollowerAwareness.GetDamageRevision(botOwner) != exposedFireInitialDamageRevision;
            float lastTriggerPressedAt = botOwner.ShootData?.LastTriggerPressd ?? 0f;
            bool returnedFire = lastTriggerPressedAt > exposedFireInitialTriggerPressedAt + 0.001f;
            float leaseSeconds = returnedFire
                ? ExposedFireReturnedLeaseSeconds
                : ExposedFireNoReturnLeaseSeconds;
            if (!freshDamage && Time.time - exposedFireStartedAt < leaseSeconds)
            {
                return false;
            }

            if (Time.time < exposedFireRecoveryRetryAt)
            {
                return false;
            }

            // Cover selection can be comparatively expensive. A failed scan leaves the current fire
            // action intact and retries at a bounded cadence instead of iterating every decision tick.
            exposedFireRecoveryRetryAt = Time.time + ExposedFireRecoveryRetrySeconds;
            if (!TryGetRecoverDecision(
                    goalEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> recoveryDecision,
                    forceRecovery: true,
                    allowPointBlankFightThrough: false) ||
                !FollowerCombatCommon.IsMovementDecision(recoveryDecision))
            {
                return false;
            }

            preparedRecoveryDecision = recoveryDecision;
            string breakReason = freshDamage
                ? "exposedFireFreshDamageRecovery"
                : returnedFire
                    ? "exposedFireLeaseRecovery"
                    : "exposedFireNoReturnRecovery";
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "exposedFire",
                "prepareRecovery",
                breakReason,
                recoveryDecision,
                untilTime: exposedFireStartedAt + leaseSeconds);
            end = new AICoreActionEndStruct(breakReason, true);
            return true;
        }

        private bool IsCommittedHoldEnemyContact(EnemyInfo goalEnemy)
        {
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return true;
            }

            if (goalEnemy.IsVisible && goalEnemy.Distance <= 18f)
            {
                return true;
            }

            return Enemy.IsVisible(botOwner, goalEnemy) &&
                   (goalEnemy.CanShoot || goalEnemy.Distance <= VisibleAlignedFireMaxDistance);
        }

        private static bool IsCoverHoldReason(string? reason)
        {
            return string.Equals(reason, "coverHold", StringComparison.Ordinal) ||
                   IsBossHoldReason(reason);
        }

        private AICoreActionEndStruct EndShootCoverHoldPosition()
        {
            combatCommon.ValidateCommittedCover();

            if (!combatCommon.IsBotInCommittedCover())
            {
                shootCoverSettlePhase.Clear();
                ClearCoverIntent();
                return new AICoreActionEndStruct("leftCommittedCover", true);
            }

            if (HasActivePushOrder())
            {
                shootCoverSettlePhase.Clear();
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("orderedPushBreakShootCoverHold", true);
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy != null && goalEnemy.IsVisible)
            {
                if (TryPrepareVisibleHoldBreak(goalEnemy, out AICoreActionEndStruct visibleBreak))
                {
                    shootCoverSettlePhase.Clear();
                    return new AICoreActionEndStruct("visibleEnemyBreakShootCoverHold", visibleBreak.Value);
                }

                if (IsHoldReactionRetrySuppressed(goalEnemy))
                {
                    return FollowerCombatCommon.Continue();
                }
            }

            if (botOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(botOwner, 0.75f))
            {
                shootCoverSettlePhase.Clear();
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("underFireBreakShootCoverHold", true);
            }

            AICoreActionEndStruct baseEnd = combatCommon.EndBaseHoldPosition(ShootCoverHoldReason);
            if (baseEnd.Value)
            {
                shootCoverSettlePhase.Clear();
            }

            return baseEnd;
        }

        /// <summary>
        /// Cover movement remains owned by its movement end contract. Autonomous regroup distance
        /// is evaluated only after the move finishes; it must not cancel an active route.
        /// </summary>
        private AICoreActionEndStruct EndCoverMoveOrAttackMoving(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            bool isHealMove = FollowerCombatCommon.IsMedicalRetreatMovementReason(currentDecision.Reason);
            bool isRecoveryMove = FollowerCombatCommon.IsRecoveryManeuverReason(currentDecision.Reason);
            bool isBossSupportMove = IsBossSupportDecision(currentDecision);
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!isHealMove &&
                !isRecoveryMove &&
                !isBossSupportMove &&
                goalEnemy != null &&
                ShouldBreakForBossUnderAttack(goalEnemy))
            {
                combatCommon.ClearCommittedMovement();
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("bossUnderAttackBreakCoverMove", true);
            }

            AICoreActionEndStruct result = combatCommon.ShallEndCurrentDecision(currentDecision);
            if (result.Value &&
                string.Equals(result.Reason, "visibleCloseFireBreakCoverMove", StringComparison.Ordinal))
            {
                // Ending first and hoping the normal router chooses combat lets low-ammo reload
                // routing reuse this same committed cover. That creates an end/reselect loop while
                // the follower keeps sprinting and cannot fire. End only with a real close-fight
                // handoff ready; otherwise preserve the current run and retry on a later update.
                if (!TryPrepareCloseCoverFightHandoff(goalEnemy))
                {
                    return FollowerCombatCommon.Continue();
                }

                combatCommon.ClearCommittedMovement();
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return result;
            }

            if (isRecoveryMove &&
                result.Value &&
                ShouldContinueCommittedRecoveryMove(goalEnemy, result.Reason))
            {
                return FollowerCombatCommon.Continue();
            }

            if (result.Value)
            {
                combatCommon.ClearCommittedMovement();
            }

            return result;
        }

        private bool TryPrepareCloseCoverFightHandoff(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null || !goalEnemy.IsVisible || !goalEnemy.CanShoot)
            {
                return false;
            }

            if (!TryGetImmediateFightDecision(
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> fightDecision) ||
                (fightDecision.Action != BotLogicDecision.dogFight &&
                 !combatCommon.IsInFight(fightDecision.Action)))
            {
                return false;
            }

            preparedCloseCoverFightDecision = fightDecision;
            return true;
        }

        private bool ShouldContinueCommittedRecoveryMove(EnemyInfo? goalEnemy, string? endReason)
        {
            if (!combatCommon.HasCommittedCover() || combatCommon.IsBotInCommittedCover())
            {
                return false;
            }

            if (goalEnemy != null && Enemy.Distance(goalEnemy) <= Enemy.EnemyDistance.VeryClose)
            {
                return false;
            }

            return string.Equals(endReason, "visibleCloseFireBreakCoverMove", StringComparison.Ordinal) ||
                   string.Equals(endReason, "stableImmediateFire", StringComparison.Ordinal) ||
                   string.Equals(endReason, "stationary", StringComparison.Ordinal);
        }

        /// <summary>
        /// Shooting from cover remains sticky while its fire contract is valid. Autonomous regroup
        /// distance is evaluated after firing ends and cannot interrupt the shot action itself.
        /// </summary>
        private AICoreActionEndStruct EndShootFromCover(string reason)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy != null && ShouldBreakForBossUnderAttack(goalEnemy))
            {
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("bossUnderAttackBreakShootCover", true);
            }

            return combatCommon.EndShootFromCover();
        }
        private bool HasActivePushOrder()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType activeCommand, out _) &&
                   activeCommand == FollowerCommandType.PushEnemy;
        }

        private void CommitCoverIntent(CoverIntentKind intent)
        {
            activeCoverIntent = intent;
            coverIntentPhase.BeginTravel();
        }

        private void ClearCoverIntent()
        {
            activeCoverIntent = CoverIntentKind.None;
            coverIntentPhase.Clear();
            preparedAllySupportDecision = null;
        }

        private void UpdateCoverIntentArrival()
        {
            if (activeCoverIntent == CoverIntentKind.None)
            {
                return;
            }

            if (coverIntentPhase.PromoteToHoldOnArrival())
            {
                float timeout = activeCoverIntent == CoverIntentKind.ProtectBoss
                    ? ProtectIntentMaxHoldSeconds
                    : SupportIntentMaxHoldSeconds;
                coverIntentPhase.BeginHoldLifecycle(IntentSettleSeconds, timeout);
            }
        }

        private bool CanScanCoverIntent()
        {
            return activeCoverIntent != CoverIntentKind.None && coverIntentPhase.CanScan;
        }

        private void MarkCoverIntentScanned()
        {
            if (activeCoverIntent != CoverIntentKind.None)
            {
                coverIntentPhase.MarkScanned(IntentScanIntervalSeconds);
            }
        }

        private bool ShouldExpireCoverIntent()
        {
            if (activeCoverIntent == CoverIntentKind.None)
            {
                return false;
            }

            return coverIntentPhase.IsHoldExpired;
        }

        private void ApplyCoverIntentRetryCooldown()
        {
            switch (activeCoverIntent)
            {
                case CoverIntentKind.Support:
                    supportIntentRetryUntil = Mathf.Max(supportIntentRetryUntil, Time.time + IntentRetryCooldownSeconds);
                    break;
                case CoverIntentKind.ProtectBoss:
                    protectIntentRetryUntil = Mathf.Max(protectIntentRetryUntil, Time.time + IntentRetryCooldownSeconds);
                    break;
            }
        }

        private bool IsCoverIntentRetryActive(CoverIntentKind intent)
        {
            return intent switch
            {
                CoverIntentKind.Support => Time.time < supportIntentRetryUntil,
                CoverIntentKind.ProtectBoss => IsBossSupportRetrySuppressed(),
                _ => false,
            };
        }

        private void ResolvePendingBossSupportBreak(
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            if (!bossSupportBreakPending)
            {
                return;
            }

            if (IsBossSupportDecision(nextDecision))
            {
                MarkBossSupportDecisionPrepared();
                return;
            }

            bossSupportBreakPending = false;
            protectIntentRetryUntil = Mathf.Max(
                protectIntentRetryUntil,
                Time.time + IntentRetryCooldownSeconds);
            protectIntentForcedRetryUntil = Mathf.Max(
                protectIntentForcedRetryUntil,
                Time.time + BossSupportForcedRetrySeconds);
            nextBossSupportGeometryCheckAt = protectIntentRetryUntil;
            hasFailedBossSupportSignature = TryCaptureBossSupportRetrySignature(
                out failedBossSupportSignature);

            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "bossSupportRetry",
                "deferAfterMiss",
                nextDecision.Reason,
                nextDecision,
                target: hasFailedBossSupportSignature
                    ? failedBossSupportSignature.AttackerPosition
                    : null,
                untilTime: protectIntentRetryUntil);
        }

        private void MarkBossSupportDecisionPrepared()
        {
            bool hadRetryState = bossSupportBreakPending ||
                                 hasFailedBossSupportSignature ||
                                 Time.time < protectIntentRetryUntil;
            if (hadRetryState)
            {
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "bossSupportRetry",
                    "clearPrepared",
                    "bossSupportDecisionPrepared");
            }

            ClearBossSupportRetryState();
        }

        private bool IsBossSupportRetrySuppressed()
        {
            if (Time.time < protectIntentRetryUntil)
            {
                return true;
            }

            if (!hasFailedBossSupportSignature)
            {
                return false;
            }

            if (Time.time >= protectIntentForcedRetryUntil)
            {
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "bossSupportRetry",
                    "retryForced",
                    failedBossSupportSignature.AttackerProfileId,
                    target: failedBossSupportSignature.AttackerPosition);
                ClearBossSupportRetryState();
                return false;
            }

            if (Time.time < nextBossSupportGeometryCheckAt)
            {
                return true;
            }

            nextBossSupportGeometryCheckAt = Time.time + BossSupportGeometryCheckIntervalSeconds;

            if (!TryCaptureBossSupportRetrySignature(out BossSupportRetrySignature currentSignature))
            {
                ClearBossSupportRetryState();
                return false;
            }

            if (HasBossSupportGeometryChanged(failedBossSupportSignature, currentSignature))
            {
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "bossSupportRetry",
                    "retryGeometryChanged",
                    currentSignature.AttackerProfileId,
                    target: currentSignature.AttackerPosition);
                ClearBossSupportRetryState();
                return false;
            }

            return true;
        }

        private bool TryCaptureBossSupportRetrySignature(out BossSupportRetrySignature signature)
        {
            signature = default;
            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            BotOwner? bossEnemy = boss.ClosestEnemy();
            if (bossEnemy?.GetPlayer?.HealthController?.IsAlive != true ||
                string.IsNullOrEmpty(bossEnemy.ProfileId))
            {
                return false;
            }

            EnemyInfo? attackerInfo = botOwner.Memory?.GoalEnemy;
            if (!string.Equals(attackerInfo?.ProfileId, bossEnemy.ProfileId, StringComparison.Ordinal))
            {
                attackerInfo = null;
                if (botOwner.EnemiesController?.EnemyInfos != null)
                {
                    foreach (EnemyInfo candidate in botOwner.EnemiesController.EnemyInfos.Values)
                    {
                        if (string.Equals(candidate?.ProfileId, bossEnemy.ProfileId, StringComparison.Ordinal))
                        {
                            attackerInfo = candidate;
                            break;
                        }
                    }
                }
            }

            Vector3 bossPosition = combatCommon.GetRealBossPosition();
            if (!FollowerCombatCommon.IsFinite(botOwner.Position) ||
                !FollowerCombatCommon.IsFinite(bossPosition) ||
                !FollowerCombatCommon.IsFinite(bossEnemy.Position))
            {
                return false;
            }

            signature = new BossSupportRetrySignature
            {
                AttackerProfileId = bossEnemy.ProfileId,
                FollowerPosition = botOwner.Position,
                BossPosition = bossPosition,
                AttackerPosition = bossEnemy.Position,
                AttackerVisible = attackerInfo?.IsVisible == true,
                AttackerCanShoot = attackerInfo?.CanShoot == true,
            };
            return true;
        }

        private static bool HasBossSupportGeometryChanged(
            BossSupportRetrySignature previous,
            BossSupportRetrySignature current)
        {
            if (!string.Equals(previous.AttackerProfileId, current.AttackerProfileId, StringComparison.Ordinal) ||
                previous.AttackerVisible != current.AttackerVisible ||
                previous.AttackerCanShoot != current.AttackerCanShoot)
            {
                return true;
            }

            float moveDistanceSqr = BossSupportGeometryMoveDistance * BossSupportGeometryMoveDistance;
            if ((previous.FollowerPosition - current.FollowerPosition).sqrMagnitude >= moveDistanceSqr ||
                (previous.BossPosition - current.BossPosition).sqrMagnitude >= moveDistanceSqr ||
                (previous.AttackerPosition - current.AttackerPosition).sqrMagnitude >= moveDistanceSqr)
            {
                return true;
            }

            return HasPlanarBearingChanged(
                       previous.FollowerPosition,
                       previous.AttackerPosition,
                       current.FollowerPosition,
                       current.AttackerPosition) ||
                   HasPlanarBearingChanged(
                       previous.BossPosition,
                       previous.AttackerPosition,
                       current.BossPosition,
                       current.AttackerPosition);
        }

        private static bool HasPlanarBearingChanged(
            Vector3 previousOrigin,
            Vector3 previousTarget,
            Vector3 currentOrigin,
            Vector3 currentTarget)
        {
            Vector3 previousDirection = previousTarget - previousOrigin;
            Vector3 currentDirection = currentTarget - currentOrigin;
            previousDirection.y = 0f;
            currentDirection.y = 0f;
            if (previousDirection.sqrMagnitude <= 0.01f || currentDirection.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            return Vector3.Dot(previousDirection.normalized, currentDirection.normalized) <
                   BossSupportBearingDotThreshold;
        }

        private static bool IsBossSupportDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return decision.Reason?.StartsWith("protectBoss", StringComparison.Ordinal) == true ||
                   FollowerCombatRegroupObjective.IsRegroupActivationReason(decision.Reason);
        }

        private void ClearBossSupportRetryState()
        {
            bossSupportBreakPending = false;
            protectIntentRetryUntil = 0f;
            protectIntentForcedRetryUntil = 0f;
            nextBossSupportGeometryCheckAt = 0f;
            hasFailedBossSupportSignature = false;
            failedBossSupportSignature = default;
        }

        private void ArmHoldReactionRetry(EnemyInfo goalEnemy, string reason)
        {
            holdReactionRetryUntil = Time.time + HoldReactionRetryCooldownSeconds;
            nextHoldReactionGeometryCheckAt = Time.time + BossSupportGeometryCheckIntervalSeconds;
            hasFailedHoldReactionSignature = TryCaptureHoldReactionRetrySignature(
                goalEnemy,
                out failedHoldReactionSignature);

            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "holdReactionRetry",
                "deferAfterMiss",
                reason,
                target: hasFailedHoldReactionSignature
                    ? failedHoldReactionSignature.EnemyPosition
                    : null,
                untilTime: holdReactionRetryUntil);
        }

        private bool IsHoldReactionRetrySuppressed(EnemyInfo goalEnemy)
        {
            if (!hasFailedHoldReactionSignature)
            {
                return Time.time < holdReactionRetryUntil;
            }

            if (Time.time >= holdReactionRetryUntil)
            {
                ClearHoldReactionRetryState();
                return false;
            }

            if (Time.time < nextHoldReactionGeometryCheckAt)
            {
                return true;
            }

            nextHoldReactionGeometryCheckAt = Time.time + BossSupportGeometryCheckIntervalSeconds;
            if (!TryCaptureHoldReactionRetrySignature(goalEnemy, out HoldReactionRetrySignature current) ||
                HasHoldReactionGeometryChanged(failedHoldReactionSignature, current))
            {
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "holdReactionRetry",
                    "retryGeometryChanged",
                    goalEnemy.ProfileId,
                    target: current.EnemyPosition);
                ClearHoldReactionRetryState();
                return false;
            }

            return true;
        }

        private bool TryCaptureHoldReactionRetrySignature(
            EnemyInfo? goalEnemy,
            out HoldReactionRetrySignature signature)
        {
            signature = default;
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return false;
            }

            Vector3 enemyPosition = FollowerCombatCommon.GetEnemyCurrentPosition(goalEnemy!);
            if (!FollowerCombatCommon.IsFinite(botOwner.Position) ||
                !FollowerCombatCommon.IsFinite(enemyPosition))
            {
                return false;
            }

            signature = new HoldReactionRetrySignature
            {
                EnemyProfileId = goalEnemy!.ProfileId ?? string.Empty,
                FollowerPosition = botOwner.Position,
                EnemyPosition = enemyPosition,
                EnemyVisible = goalEnemy.IsVisible,
                EnemyCanShoot = goalEnemy.CanShoot,
                UnderFire = botOwner.Memory?.IsUnderFire == true,
            };
            return true;
        }

        private static bool HasHoldReactionGeometryChanged(
            HoldReactionRetrySignature previous,
            HoldReactionRetrySignature current)
        {
            if (!string.Equals(previous.EnemyProfileId, current.EnemyProfileId, StringComparison.Ordinal) ||
                previous.EnemyVisible != current.EnemyVisible ||
                previous.EnemyCanShoot != current.EnemyCanShoot ||
                previous.UnderFire != current.UnderFire)
            {
                return true;
            }

            float moveDistanceSqr = BossSupportGeometryMoveDistance * BossSupportGeometryMoveDistance;
            return (previous.FollowerPosition - current.FollowerPosition).sqrMagnitude >= moveDistanceSqr ||
                   (previous.EnemyPosition - current.EnemyPosition).sqrMagnitude >= moveDistanceSqr ||
                   HasPlanarBearingChanged(
                       previous.FollowerPosition,
                       previous.EnemyPosition,
                       current.FollowerPosition,
                       current.EnemyPosition);
        }

        private void ClearHoldReactionRetryState()
        {
            holdReactionRetryUntil = 0f;
            nextHoldReactionGeometryCheckAt = 0f;
            hasFailedHoldReactionSignature = false;
            failedHoldReactionSignature = default;
        }

        private bool ShouldReleaseCoverIntentForOpportunity(EnemyInfo goalEnemy)
        {
            if (!CanScanCoverIntent())
            {
                return false;
            }

            MarkCoverIntentScanned();
            return goalEnemy.IsVisible ||
                   combatCommon.ShouldAdvance(goalEnemy) ||
                   (goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetClosePushDistance() &&
                    combatCommon.IsEnemyLowThreat(goalEnemy, combatCommon.GetAggression01()));
        }

        private bool TryGetCoverIntentRepositionDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!CanScanCoverIntent())
            {
                return false;
            }

            int? previousCoverId = combatCommon.CommittedCoverId;
            switch (activeCoverIntent)
            {
                case CoverIntentKind.Support:
                    if (!combatCommon.TryGetAllyEngagementEnemy(out string supportEnemyProfileId, out Vector3 supportEnemyPosition) ||
                        !combatCommon.TrySelectPreferredSupportEnemy(supportEnemyProfileId, supportEnemyPosition, out EnemyInfo? supportEnemy))
                    {
                        return false;
                    }

                    bool preferBackline = combatCommon.GetFollowerTactic() is FollowerCombatTactic.Marksman or FollowerCombatTactic.Protector;
                    if (!combatCommon.TryCommitSupportFiringCover(supportEnemy, "allySupportCover.refresh", out string supportReason, preferBackline) ||
                        combatCommon.CommittedCoverId == previousCoverId)
                    {
                        return false;
                    }

                    CommitCoverIntent(CoverIntentKind.Support);
                    decision = combatCommon.CreateMoveToCommittedCoverDecision(supportReason);
                    return true;

                case CoverIntentKind.ProtectBoss:
                    if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
                    {
                        return false;
                    }

                    BotOwner? bossEnemy = boss.ClosestEnemy();
                    if (bossEnemy == null || bossEnemy.GetPlayer?.HealthController?.IsAlive != true)
                    {
                        return false;
                    }

                    if (!combatCommon.TryUseSupportGoalEnemy(bossEnemy, "protectBossCover.refresh", out EnemyInfo? prioritizedEnemy) ||
                        !combatCommon.HasActiveCombatEnemy(prioritizedEnemy) ||
                        !combatCommon.TryFindBossCover(prioritizedEnemy, combatCommon.GetBossPosition(), CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius(), out CustomNavigationPoint? protectCover) ||
                        !combatCommon.TryCommitSelectedCombatCover(prioritizedEnemy, protectCover, "protectBossCover.refresh") ||
                        combatCommon.CommittedCoverId == previousCoverId)
                    {
                        return false;
                    }

                    CommitCoverIntent(CoverIntentKind.ProtectBoss);
                    decision = combatCommon.CreateCommittedCoverMoveDecision();
                    return true;
            }

            return false;
        }

        private bool IsDecisionCompatibleWithCoverIntent(AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            if (activeCoverIntent == CoverIntentKind.None)
            {
                return false;
            }

            return nextDecision.Action == BotLogicDecision.runToCover ||
                   nextDecision.Action == BotLogicDecision.attackMoving ||
                   nextDecision.Action == BotLogicDecision.attackMovingWithSuppress ||
                   nextDecision.Action == BotLogicDecision.holdPosition ||
                   nextDecision.Action == BotLogicDecision.shootFromCover;
        }

        private bool IsDecisionCompatibleWithPreparedAdvance(AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            return !preparedAdvanceDecision.HasValue ||
                   (preparedAdvanceDecision.Value.Action == nextDecision.Action &&
                    string.Equals(preparedAdvanceDecision.Value.Reason, nextDecision.Reason, StringComparison.Ordinal));
        }

        private bool IsDecisionCompatibleWithPreparedHoldPositionSupport(AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            return !preparedHoldPositionSupportDecision.HasValue ||
                   (preparedHoldPositionSupportDecision.Value.Action == nextDecision.Action &&
                    StringComparer.Ordinal.Equals(preparedHoldPositionSupportDecision.Value.Reason, nextDecision.Reason));
        }

        /// <summary>
        /// Reuses the boss-relative objective gate as the "leave stale cover/hold and rejoin the boss"
        /// signal. This only triggers after the initial cover commit window and only when there is no
        /// stronger local fight to justify staying put.
        /// </summary>
        private bool ShouldBreakCommittedCoverForBossObjective(EnemyInfo goalEnemy, bool allowMovingCommittedCoverBreak = false)
        {
            return combatCommon.ShouldBreakCommittedCoverForBossObjective(
                goalEnemy,
                ShouldRegroupForBossDistance(),
                HasActivePushOrder(),
                goalEnemy.IsVisible && goalEnemy.CanShoot,
                allowMovingCommittedCoverBreak);
        }

        private bool ShouldBreakForBossUnderAttack(EnemyInfo goalEnemy)
        {
            if (IsBossSupportRetrySuppressed())
            {
                return false;
            }

            bool shouldBreak = combatCommon.ShouldBreakForBossUnderAttack(goalEnemy, HasActivePushOrder());
            if (shouldBreak)
            {
                bossSupportBreakPending = true;
            }

            return shouldBreak;
        }

        private void UpdateShootCoverSettleState(AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            if (IsShootCoverMoveDecision(nextDecision))
            {
                shootCoverSettlePhase.BeginTravel();
                return;
            }

            if (!string.Equals(nextDecision.Reason, ShootCoverHoldReason, StringComparison.Ordinal))
            {
                shootCoverSettlePhase.Clear();
            }
        }

        private static bool IsShootCoverMoveDecision(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return IsShootCoverReason(decision.Reason) &&
                   (decision.Action == BotLogicDecision.runToCover ||
                    decision.Action == BotLogicDecision.attackMoving ||
                    decision.Action == BotLogicDecision.attackMovingWithSuppress ||
                    decision.Action == (BotLogicDecision)CustomBotDecisions.attackRetreat);
        }

        private static bool IsShootCoverReason(string? reason)
        {
            return FollowerCombatCommon.IsReasonOrSubreason(reason, "shootCover") ||
                   FollowerCombatCommon.IsReasonOrSubreason(reason, "retreatShootCover");
        }

        private static bool IsBossHoldReason(string? reason)
        {
            return FollowerCombatCommon.IsBossHoldReason(reason);
        }

        /// <summary>
        /// Computes whether the follower should switch to the regroup objective based on live boss
        /// nav distance. Regroup itself decides whether that becomes forward, backward, or lateral movement.
        /// </summary>
        private bool ShouldRegroupForBossDistance()
        {
            Vector3 bossPosition = combatCommon.GetBossPosition();
            if (!IsFinite(bossPosition))
            {
                return false;
            }

            float navDistance = combatCommon.GetBossNavDistance(bossPosition);
            float directDistance = Vector3.Distance(botOwner.Position, bossPosition);
            if (IsUrbanDetourRegroup(directDistance, navDistance) &&
                FollowerCombatRegroupObjective.IsSameBossLevel(botOwner.Position, bossPosition))
            {
                return false;
            }

            float followerBossDistance = FollowerCombatCommon.GetSafeRegroupDistance(navDistance, directDistance);
            float regroupTriggerDistance = CombatDistanceConfiguration.Instance.GetBossRegroupTriggerDistance(botOwner);
            float protectionWillingness = combatCommon.GetBossProtectionWillingness01();
            regroupTriggerDistance *= Mathf.Lerp(
                PickupFollowerPersonality.RegroupMaxTriggerMultiplier,
                1f,
                protectionWillingness);
            if (followerBossDistance <= regroupTriggerDistance)
            {
                return false;
            }

            if (combatCommon.ShouldDeferAutonomousRegroupAfterRecentFight(
                    botOwner.Memory.GoalEnemy,
                    followerBossDistance,
                    regroupTriggerDistance))
            {
                return false;
            }

            return true;
        }

        private static bool IsUrbanDetourRegroup(float directDistance, float navDistance)
        {
            return CombatDistanceConfiguration.Instance.IsUrbanDetourRegroup(directDistance, navDistance);
        }

        /// <summary>
        /// Guards against NaN or infinity vectors from stale game data.
        /// </summary>
        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.z);
        }

        private bool HasExplicitRegroupOrder()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            if (followerData == null)
            {
                return false;
            }

            return followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.RegroupNearBoss;
        }
    }
}

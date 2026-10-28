using System;
using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.GoingRogue.Combat
{
    internal abstract class GoonCombatObjective : FollowerCombatObjectiveBase
    {
        private readonly IGoonCombatSelector vanilla;
        private readonly string prefix;
        private readonly string helpCoverReason;
        private readonly FollowerCombatSupportPlanner support;
        private readonly CommittedCoverPhaseState supportPhase = new CommittedCoverPhaseState();
        private AICoreActionResult<BotLogicDecision, CoreActionResultParams>? preparedSupport;
        private float supportRetryAt;
        private float helpRetryAt;
        private float coverArrivedAt = -1f;
        private float coverBreakRetryAt;
        private string preparedEnemy;

        internal GoonCombatObjective(BotOwner owner, FollowerCombatCommon common, IGoonCombatSelector selector, string actorPrefix) : base(owner, common)
        {
            vanilla = selector;
            prefix = actorPrefix + ".";
            helpCoverReason = "protectBossSupportCover." + actorPrefix;
            support = new FollowerCombatSupportPlanner(owner, common);
        }

        public override void Reset()
        {
            vanilla.Dispose();
            supportPhase.Reset();
            preparedSupport = null;
            preparedEnemy = null;
            supportRetryAt = helpRetryAt = 0f;
            coverArrivedAt = -1f;
            coverBreakRetryAt = 0f;
            knightPositionRetryAt = 0f;
        }

        public override void StartDecision() => vanilla.Begin(CombatCommon);

        public override void Activate()
        {
            preparedSupport = null;
            supportPhase.Clear();
            coverArrivedAt = -1f;
            coverBreakRetryAt = 0f;
            knightPositionRetryAt = 0f;
            vanilla.Begin(CombatCommon);
        }

        public override void Deactivate()
        {
            preparedSupport = null;
            supportPhase.Clear();
            vanilla.InvalidatePosition();
        }

        public override AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision(EnemyInfo enemy)
        {
            CombatCommon.RefreshShootCover();
            CombatCommon.ValidateCommittedCover();
            if (CombatCommon.TryGetReloadRetreatDecision(enemy, out var reload)) return reload;
            var immediate = CombatCommon.TryGetDogFightDecision();
            if (immediate.HasValue) return immediate.Value;
            if (CombatCommon.TryGetCommittedGrenadeDecision(out var grenade)) return grenade;
            var medical = CombatCommon.TryGetNeedHealDecision();
            if (medical.HasValue) return medical.Value;
            if (CombatCommon.ShouldHoldForHealRetry())
                return Hold(FollowerCombatCommon.HealRetryHoldReason);
            if (CombatCommon.TryGetCombatLongGunPreparationDecision(enemy, orderedPush: false, out var weaponPreparation))
                return weaponPreparation;
            if (!CombatCommon.HasCommittedCover())
            {
                supportPhase.Clear();
                coverArrivedAt = -1f;
            }
            if (preparedSupport.HasValue && TrySupport(out var prepared)) return prepared;

            // These commitments include immediate medical arrival; they are evaluated before
            // optional assistance or native cover selection can overwrite the destination.
            if (CombatCommon.HasCommittedPosition(out var position)) return position;
            if (CombatCommon.TryGetCommittedMovementDecision(enemy, false, false, out var movement)) return movement;
            if (CombatCommon.HasCommittedCover())
            {
                if (!CombatCommon.IsBotInCommittedCover()) return CombatCommon.CreateCommittedCoverMoveDecision();
                if (coverArrivedAt < 0f) coverArrivedAt = Time.time;
                if (supportPhase.PromoteToHoldOnArrival()) supportPhase.BeginHoldLifecycle(2.5f, 8f);
                if (CombatCommon.CanShootFromCurrentCover(out _))
                    return Decision(BotLogicDecision.shootFromCover, prefix + "coverFire");
                if (Time.time - coverArrivedAt < 2.5f)
                    return Hold(prefix + "coverArrival");
                if (supportPhase.IsHolding && !supportPhase.IsHoldExpired)
                    return Hold(prefix + "supportHold");
                if (supportPhase.IsHoldExpired)
                {
                    supportRetryAt = Time.time + 3f;
                    supportPhase.Clear();
                }
                CombatCommon.ClearCommittedCover(prefix + "coverReevaluate");
                vanilla.InvalidatePosition();
                coverArrivedAt = -1f;
            }

            if (TryHelpLeader(enemy, out var help)) return help;
            if (TryRejoinLeader(enemy, out var rejoin)) return rejoin;
            if (TryKnightPosition(out var knightPosition)) return knightPosition;
            if (CanSupport() && TryGetRoleSupportDecision(enemy, out var roleSupport)) return roleSupport;
            if (TrySupport(out var assistance)) return assistance;
            if (CombatCommon.TryActivateFollowerGrenade(enemy, out grenade)) return grenade;
            return vanilla.GetDecision();
        }

        protected virtual bool TryGetRoleSupportDecision(EnemyInfo enemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        { decision = default; return false; }

        private bool TryRejoinLeader(EnemyInfo enemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            // This is a fresh decision after committed work, never a distance interrupt.
            // A distant remembered enemy must not renew no-cover recovery indefinitely.
            if (!CanSupport() || FollowerCombatAnchor.IsCombatIndependent(BotOwner) ||
                enemy.IsVisible || enemy.CanShoot || CombatCommon.IsTemporaryHoldPositionAggressionActive() ||
                BotOwner.WeaponManager.Reload?.Reloading == true || !BotOwner.WeaponManager.IsWeaponReady ||
                BotOwner.WeaponManager.Selector?.IsChanging == true ||
                (BotOwner.WeaponManager.ShootController as Player.FirearmController)?.IsInReloadOperation() == true)
                return false;
            var follower = BossPlayers.Instance?.GetFollower(BotOwner);
            if (follower == null || follower.HasOrderedPushTargetLock || follower.TryPeekActiveCommand(out _, out _, out _))
                return false;
            var bossPosition = CombatCommon.GetBossPosition();
            if (!FollowerCombatCommon.IsFinite(bossPosition)) return false;
            float nav = CombatCommon.GetBossNavDistance(bossPosition);
            float direct = Vector3.Distance(BotOwner.Position, bossPosition);
            if (CombatDistanceConfiguration.Instance.IsUrbanDetourRegroup(direct, nav)) return false;
            float distance = FollowerCombatCommon.GetSafeRegroupDistance(nav, direct);
            float trigger = CombatDistanceConfiguration.Instance.GetBossRegroupTriggerDistance(BotOwner);
            if (!(distance > trigger) || CombatCommon.ShouldDeferAutonomousRegroupAfterRecentFight(enemy, distance, trigger))
                return false;
            decision = CombatCommon.CreateRegroupObjectiveDecision();
            BattleRecorder.RecordObjectiveDiagnostic(BotOwner, prefix + "combat", "rejoinLeader", "unproductiveContact",
                () => new { distance, trigger, enemy = enemy.ProfileId });
            return true;
        }
        private bool TryHelpLeader(EnemyInfo enemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            if (Time.time < helpRetryAt || BotOwner.Memory.IsUnderFire ||
                (enemy.IsVisible && enemy.CanShoot) ||
                BotOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss ||
                boss.GetBossLogic()?.IsHitted != true) return false;
            var attacker = boss.ClosestEnemy();
            if (attacker?.GetPlayer?.HealthController?.IsAlive != true ||
                !CombatCommon.TryUseSupportGoalEnemy(attacker, prefix + "needHelp", out var threat) ||
                !CombatCommon.HasActiveCombatEnemy(threat)) return false;
            helpRetryAt = Time.time + 1f;
            var fire = CombatCommon.TryGetImmediateShootDecision(prefix + "needHelpFire");
            if (fire.HasValue) { decision = fire.Value; return true; }
            if (CombatCommon.TryCreateSuppressFromPlaceDecision(threat, "protectBossSuppress", out decision,
                    allowSoftObstructedSuppression: true)) return true;
            if (CombatCommon.TryCommitSupportFiringCover(threat, helpCoverReason, out var reason,
                    preferBackline: false, maxSearchRadius: 60f))
            {
                decision = CombatCommon.CreateMoveToCommittedCoverDecision(reason);
                return true;
            }
            decision = CombatCommon.CreateRegroupObjectiveDecision();
            return true;
        }

        private bool CanSupport()
            => !CombatCommon.HasActiveOrPendingHealWork() && !CombatCommon.HasImmediateExplosiveDanger() &&
               !BotOwner.Memory.IsUnderFire && !FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f) &&
               BotOwner.WeaponManager?.Grenades?.ThrowindNow != true &&
               BotOwner.WeaponManager?.Grenades?.ReadyToThrow != true;

        private float knightPositionRetryAt;
        private bool TryKnightPosition(out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            if (Time.time < knightPositionRetryAt || !CanSupport() ||
                !KnightSquadCoordinator.TryGetMemberPosition(BotOwner, out var point) ||
                (point - BotOwner.Position).sqrMagnitude <= 4f) return false;
            knightPositionRetryAt = Time.time + 1f;
            bool sniper = BotOwner.Profile?.Info?.Settings?.Role == WildSpawnType.followerBirdEye;
            return CombatCommon.TryCreateAssignedFiringPositionDecision(BotOwner.Memory.GoalEnemy, point,
                prefix + "knightPosition", out decision, preferBackline: sniper, enforceMarksmanPositionPolicy: sniper);
        }

        private bool TrySupport(out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            if (!CanSupport()) { preparedSupport = null; return false; }
            if (preparedSupport.HasValue)
            {
                var prepared = preparedSupport.Value;
                preparedSupport = null;
                if (string.Equals(preparedEnemy, BotOwner.Memory.GoalEnemy?.ProfileId, StringComparison.Ordinal))
                { decision = prepared; return true; }
            }
            if (Time.time < supportRetryAt || supportPhase.IsActive) return false;
            if (!support.TryBuild(out decision)) { supportRetryAt = Time.time + 1f; return false; }
            supportPhase.BeginTravel();
            return true;
        }

        public override AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> current)
        {
            if (FollowerCombatCommon.IsReloadHoldReason(current.Reason))
                return CombatCommon.EndReloadHold(current);
            if (FollowerCombatCommon.IsWeaponPreparationHoldReason(current.Reason))
                return CombatCommon.EndWeaponPreparationHold(current.Reason);
            if (current.Reason == FollowerCombatCommon.HealRetryHoldReason)
                return EndMedicalRetryHold(current);
            if (current.Reason == FollowerCombatCommon.RecoveryNoCoverThreatHoldReason)
                return CombatCommon.EndRecoveryNoCoverThreatHold();
            // A search/hold may outlive the emergency pistol draw. Prepare one real
            // successor so the shared driver can consume it before resuming the old action.
            if (!FollowerCombatCommon.IsMedicalDecision(current) && CanSupport() &&
                CombatCommon.TryGetCombatLongGunPreparationDecision(BotOwner.Memory.GoalEnemy,
                    orderedPush: false, out var weaponPreparation) &&
                CombatCommon.TryPrepareDecisionTransition(current, prefix + "prepareLongGun", weaponPreparation))
                return new AICoreActionEnd(prefix + "prepareLongGun", true);
            if (current.Reason == prefix + "noCover" || current.Reason == prefix + "medicalDeferred")
            {
                return new AICoreActionEnd(prefix + "retry", Time.time >= vanilla.RetryUntil ||
                    BotOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f) ||
                    (BotOwner.Memory.GoalEnemy?.IsVisible == true && BotOwner.Memory.GoalEnemy.CanShoot));
            }
            if (current.Action == BotLogicDecision.holdPosition &&
                current.Reason != prefix + "coverArrival" && current.Reason != prefix + "supportHold" &&
                !FollowerCombatCommon.IsMedicalDecision(current) &&
                CombatCommon.HasCommittedHolderSettled(1f) && CanSupport() &&
                (TryKnightPosition(out var next) || TrySupport(out next)))
            {
                preparedSupport = next;
                preparedEnemy = BotOwner.Memory.GoalEnemy?.ProfileId;
                return new AICoreActionEnd(prefix + "preparedAllySupport", true);
            }
            if (current.Reason == prefix + "coverArrival" || current.Reason == prefix + "supportHold")
            {
                if (CombatCommon.HasActiveOrPendingHealWork() ||
                    (current.Reason == prefix + "supportHold" ? supportPhase.IsHoldExpired : Time.time - coverArrivedAt >= 2.5f))
                    return new AICoreActionEnd(prefix + "coverSettled", true);
                var enemy = BotOwner.Memory.GoalEnemy;
                if (Time.time >= coverBreakRetryAt && (BotOwner.Memory.IsUnderFire || enemy?.IsVisible == true))
                {
                    coverBreakRetryAt = Time.time + 0.25f;
                    var fire = CombatCommon.TryGetImmediateShootDecision(prefix + "coverArrivalFire");
                    if (fire.HasValue && CombatCommon.TryPrepareDecisionTransition(current, prefix + "coverFireReady", fire.Value))
                        return new AICoreActionEnd(prefix + "coverFireReady", true);
                    if (BotOwner.Memory.IsUnderFire && enemy != null && CombatCommon.HasActiveCombatEnemy(enemy))
                    {
                        if (!CombatCommon.TryGetCommittedRecoveryDecision(enemy, out var recovery))
                            recovery = CombatCommon.CreateNoCoverRecoveryDecision(enemy);
                        if (CombatCommon.TryPrepareDecisionTransition(current, prefix + "coverRecoveryReady", recovery))
                            return new AICoreActionEnd(prefix + "coverRecoveryReady", true);
                    }
                }
                return FollowerCombatCommon.Continue();
            }
            // Native stationary-fire/hold timing belongs to the role selector; common movement and
            // medical end rules retain arrival, treatment, target and safety ownership.
            if (current.Reason?.StartsWith(prefix + "native.", StringComparison.Ordinal) == true &&
                (current.Action == BotLogicDecision.holdPosition || current.Action == BotLogicDecision.shootFromPlace ||
                 current.Action == BotLogicDecision.shootFromCover || current.Action == BotLogicDecision.lay))
            {
                if (!CombatCommon.HasActiveCombatEnemy()) return new AICoreActionEnd(prefix + "enemyGone", true);
                if (CombatCommon.HasActiveOrPendingHealWork() && !BotOwner.Medecine.Using)
                    return new AICoreActionEnd(prefix + "medical", true);
                return vanilla.ShallEndCurrentDecision(current);
            }
            // The arrival transaction owns its deadline and exact destination. Generic
            // cover flags can lag physical arrival and repeatedly restart the same hold;
            // generic hold extension also must not outlive the committed deadline.
            if (current.Action == BotLogicDecision.holdPosition && CombatCommon.IsCommittedHolderReason(current.Reason))
                return CombatCommon.EndCommittedPositionHold(current);
            return current.Action == BotLogicDecision.holdPosition
                ? CombatCommon.EndBaseHoldPosition(current.Reason ?? string.Empty)
                : CombatCommon.ShallEndCurrentDecision(current);
        }

        private AICoreActionEnd EndMedicalRetryHold(AICoreActionResult<BotLogicDecision, CoreActionResultParams> current)
        {
            var enemy = BotOwner.Memory?.GoalEnemy;
            if (enemy == null || !CombatCommon.HasActiveCombatEnemy(enemy))
                return new AICoreActionEnd("medicalRetryCombatEnded", true);

            bool retryDue = !CombatCommon.IsHealDecisionRetryBlocked;
            if (CombatCommon.IsActivelyUsingMedical || retryDue)
            {
                var medical = CombatCommon.TryGetNeedHealDecision();
                if (medical.HasValue)
                    return CombatCommon.TryPrepareDecisionTransition(current, "medicalRetryReady", medical.Value)
                        ? new AICoreActionEnd("medicalRetryReady", true) : FollowerCombatCommon.Continue();
                if (CombatCommon.IsActivelyUsingMedical) return FollowerCombatCommon.Continue();
            }
            if (!CombatCommon.HasActiveOrPendingHealWork())
                return new AICoreActionEnd("medicalRetryCleared", true);

            // Reaching safe medical cover preserves treatment's retry window. Do not let
            // generic hit/under-fire hold endings recreate this same decision every frame.
            if (CombatCommon.HasReachedSafeHealPosition(enemy)) return FollowerCombatCommon.Continue();

            var fight = CombatCommon.TryGetImmediateShootDecision(prefix + "medicalRetryFire");
            if (fight.HasValue && CombatCommon.TryPrepareDecisionTransition(current, "medicalRetryImmediateFight", fight.Value))
                return new AICoreActionEnd("medicalRetryImmediateFight", true);

            // A failed medical-cover search is not a reason to stand exposed forever.
            // Reuse shared recovery selection and its bounded no-cover commitment; a
            // quiet failed retry also gets a chance to reach a different usable position.
            if (CombatCommon.TryGetMedicalRetryRecoveryDecision(enemy, retryDue, out var recovery))
            {
                if (CombatCommon.TryPrepareDecisionTransition(current, "medicalRetryRecovery", recovery))
                    return new AICoreActionEnd("medicalRetryRecovery", true);
            }
            return FollowerCombatCommon.Continue();
        }

        public override void DecisionChanged(AICoreActionResult<BotLogicDecision, CoreActionResultParams>? previous,
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> next)
        {
            vanilla.LastSelectedDecision = next.Action;
            CombatCommon.HandleSharedDecisionChanged(next);
            CombatCommon.HandleCommittedCoverDecisionChanged(next);
            CombatCommon.HandleFollowerSuppressDecisionChanged(next);
            if (CombatCommon.ShouldCommitMovementDecision(next, false)) CombatCommon.CommitMovement(next);
            else if (!CombatCommon.IsSameCommittedMovement(next)) CombatCommon.ClearCommittedMovement();
            CombatCommon.UpdateRecoveryNoCoverCommitment(next);
        }

        private AICoreActionResult<BotLogicDecision, CoreActionResultParams> Hold(string reason)
        {
            CombatCommon.HoldFor(1f);
            return Decision(BotLogicDecision.holdPosition, reason);
        }
        private static AICoreActionResult<BotLogicDecision, CoreActionResultParams> Decision(BotLogicDecision action, string reason)
            => new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(action, reason);
    }
}

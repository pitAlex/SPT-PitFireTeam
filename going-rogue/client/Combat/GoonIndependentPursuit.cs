using EFT;
using pitTeam.BigBrain;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.GoingRogue.Combat;

// On Your Own releases escort caution, not known-contact, navigation or shot admission.
internal sealed class GoonIndependentPursuit(BotOwner owner, FollowerCombatCommon common, string reason, float stopDistance)
{
    private float nextHoldProbe;
    private string searchEnemy;
    private float holdStarted = -1f;
    private string holdReason;

    internal void Reset() { EndSearch(); nextHoldProbe = 0f; holdStarted = -1f; holdReason = null; }
    private void EndSearch()
    {
        if (searchEnemy != null) FollowerEnemyTracking.EndSearch(owner, searchEnemy);
        searchEnemy = null;
    }
    internal void DecisionChanged(AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
    {
        holdStarted = decision.Action == BotLogicDecision.holdPosition ? Time.time : -1f;
        holdReason = decision.Action == BotLogicDecision.holdPosition ? decision.Reason : null;
        if (decision.Reason?.StartsWith(reason, System.StringComparison.Ordinal) != true) { EndSearch(); return; }
        var enemy = owner.Memory.GoalEnemy;
        if (enemy?.ProfileId != searchEnemy) EndSearch();
        if (enemy != null) { searchEnemy = enemy.ProfileId; FollowerEnemyTracking.SearchTick(enemy); }
    }
    private bool TryPlan(out Vector3 point, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
    {
        point = default; decision = default;
        var enemy = owner.Memory.GoalEnemy;
        if (!FollowerCombatAnchor.IsCombatIndependent(owner) || !common.HasActiveCombatEnemy(enemy) ||
            enemy.IsVisible && enemy.CanShoot || common.HasActiveOrPendingHealWork() || owner.Medecine.Using ||
            common.HasRecoveryPressure(1f) || common.HasImmediateExplosiveDanger() || owner.Memory.IsUnderFire ||
            FollowerCombatCommon.WasHitRecently(owner, 0.75f) || common.IsTemporaryHoldPositionAggressionActive() ||
            owner.WeaponManager.Reload?.Reloading == true || !owner.WeaponManager.IsWeaponReady || !owner.WeaponManager.HaveBullets ||
            owner.WeaponManager.Selector?.IsChanging == true ||
            (owner.WeaponManager.ShootController as Player.FirearmController)?.IsInReloadOperation() == true ||
            owner.WeaponManager.Grenades?.ThrowindNow == true || owner.WeaponManager.Grenades?.ReadyToThrow == true)
            return false;
        var follower = BossPlayers.Instance?.GetFollower(owner);
        if (follower == null || follower.CombatRegroupUsesBossAnchor || follower.HasOrderedPushTargetLock ||
            follower.TryPeekActiveCommand(out _, out _, out _)) return false;
        if (owner.Profile.Info.Settings.Role != WildSpawnType.bossKnight &&
            !GoonPushLeadership.CanAutonomousAdvance(owner, enemy)) return false;
        string moveReason = reason;
        if (FollowerEnemyTracking.Distance(enemy) > 60f && common.CanSprintForCombatMovement() && common.CanRunToEnemyNow())
            moveReason += ".runToPoint";
        return common.TryPlanContactAdvanceDecision(enemy, stopDistance, moveReason, out point, out decision);
    }
    internal bool TryGetDecision(out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
    {
        if (!TryPlan(out var point, out decision)) return false;
        common.CommitContactAdvancePoint(point);
        return true;
    }
    internal bool TryReleaseHold(AICoreActionResult<BotLogicDecision, CoreActionResultParams> current, out AICoreActionEnd end)
    {
        end = default;
        if (current.Action != BotLogicDecision.holdPosition || Time.time < nextHoldProbe ||
            FollowerCombatCommon.IsMedicalDecision(current) || FollowerCombatCommon.IsReloadHoldReason(current.Reason) ||
            FollowerCombatCommon.IsWeaponPreparationHoldReason(current.Reason) ||
            !common.HasCommittedHolderSettled(2.5f) &&
                (holdReason != current.Reason || holdStarted < 0f || Time.time - holdStarted < 2.5f)) return false;
        nextHoldProbe = Time.time + 0.75f;
        if (!TryPlan(out var point, out var next) || !common.TryPrepareDecisionTransition(current, reason, next)) return false;
        // Prepare first: a blocked successor never tears down an existing cover/hold.
        common.ClearCommittedCover(reason);
        common.ClearCommittedPosition(reason);
        common.ClearCommittedMovement();
        common.CommitContactAdvancePoint(point);
        end = new AICoreActionEnd(reason, true);
        return true;
    }
}

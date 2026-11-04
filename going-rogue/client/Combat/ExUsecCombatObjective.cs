using EFT;
using pitTeam.BigBrain;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.GoingRogue.Combat;

internal sealed class ExUsecCombatObjective : FollowerCombatObjectiveBase
{
    private readonly FollowerCombatDefault rifleman;
    private float nextOrderProbe;
    private string followingEnemy;
    private float nextLeadershipProbe, leaderHoldUntil;
    internal ExUsecCombatObjective(BotOwner owner, FollowerCombatCommon common) : base(owner, common)
    {
        rifleman = new FollowerCombatDefault(owner, common, TryCommanderDecision, CanAdvance);
        // This common instance belongs to this Rogue's combat logic. Every Rifleman
        // arbitration (including search and regroup) must observe the same order.
        common.CompanionAdvanceOrder = HasCommanderAdvanceOrder;
    }

    private void EndFollow()
    {
        if (followingEnemy != null) FollowerEnemyTracking.EndSearch(BotOwner, followingEnemy);
        followingEnemy = null;
    }
    public override void Reset() { EndFollow(); rifleman.Reset(); nextOrderProbe = nextLeadershipProbe = leaderHoldUntil = 0f; GoonPushLeadership.Complete(BotOwner); }
    public override void Activate() { EndFollow(); rifleman.Reset(); CombatCommon.ClearInitialDecision(); nextOrderProbe = nextLeadershipProbe = leaderHoldUntil = 0f; }
    public override void Deactivate() => EndFollow();
    public override void StartDecision()
    { if (ExUsecCombatExtension.FindLeader(BotOwner) == null) rifleman.PrepareStartDecision(); }
    public override void DecisionChanged(AICoreActionResult<BotLogicDecision, CoreActionResultParams>? previous,
        AICoreActionResult<BotLogicDecision, CoreActionResultParams> next)
    {
        rifleman.DecisionChanged(previous, next);
        var enemy = BotOwner.Memory.GoalEnemy;
        if (next.Reason?.StartsWith("exUsec.command.follow", System.StringComparison.Ordinal) != true || enemy == null)
        { EndFollow(); return; }
        if (followingEnemy != enemy.ProfileId) EndFollow();
        followingEnemy = enemy.ProfileId;
        FollowerEnemyTracking.SearchTick(enemy);
    }
    public override AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision(EnemyInfo enemy)
        => rifleman.GetDecision(enemy);

    private bool CanAdvance(EnemyInfo enemy) => GoonPushLeadership.CanAutonomousAdvance(BotOwner, enemy);

    private bool HasCommanderAdvanceOrder(EnemyInfo enemy)
        => CanReconsiderAdvance(enemy) &&
            (BossPlayers.Instance?.GetFollower(BotOwner)?.GetBoss()?.GetBossLogic()?.IsHitted != true ||
             FollowerCombatAnchor.IsCombatIndependent(BotOwner)) &&
            GoonPushLeadership.TryGetAdvanceLeader(BotOwner, enemy, out _);

    private AICoreActionResult<BotLogicDecision, CoreActionResultParams>? TryCommanderDecision(EnemyInfo enemy)
    {
        if (Time.time < nextOrderProbe || !CanFollow(enemy)) return null;
        nextOrderProbe = Time.time + 1f;
        var leader = ExUsecCombatExtension.FindLeader(BotOwner);
        if (leader == null) return null;
        if (RoguePositionCoordinator.TryPlan(BotOwner, enemy, CombatCommon, out var point, out var position))
        {
            CombatCommon.CommitContactAdvancePoint(point);
            return position;
        }
        if (GoonPushLeadership.TryGetAdvanceLeader(BotOwner, enemy, out leader) &&
            Vector3.Distance(BotOwner.Position, leader.Position) > 4f &&
            CombatCommon.TryPlanCombatFollowDecision(enemy, leader.Position, "exUsec.command.follow", out point, out var follow) &&
            RoguePositionCoordinator.TryReserve(BotOwner, enemy, point))
        {
            BotOwner.GoToSomePointData.SetPoint(point);
            return follow;
        }
        return null;
    }

    private bool CanFollow(EnemyInfo enemy)
        => CanReconsiderAdvance(enemy) && !enemy.IsVisible && !enemy.CanShoot &&
            (BossPlayers.Instance?.GetFollower(BotOwner)?.GetBoss()?.GetBossLogic()?.IsHitted != true ||
             FollowerCombatAnchor.IsCombatIndependent(BotOwner));

    private bool CanReconsiderAdvance(EnemyInfo enemy)
    {
        var follower = BossPlayers.Instance?.GetFollower(BotOwner);
        return CombatCommon.HasActiveCombatEnemy(enemy) && !(enemy.IsVisible && enemy.CanShoot) && follower != null &&
            !follower.CombatRegroupUsesBossAnchor && !follower.HasOrderedPushTargetLock &&
            !follower.TryPeekActiveCommand(out _, out _, out _) && !CombatCommon.IsTemporaryHoldPositionAggressionActive() &&
            !CombatCommon.HasRecoveryPressure(1f) && !CombatCommon.HasActiveOrPendingHealWork() &&
            !CombatCommon.HasImmediateExplosiveDanger() && !BotOwner.Medecine.Using &&
            BotOwner.GetPlayer?.MovementContext?.IsGrounded == true &&
            BotOwner.WeaponManager.IsWeaponReady && BotOwner.WeaponManager.Reload?.Reloading != true &&
            BotOwner.WeaponManager.Selector?.IsChanging != true &&
            (BotOwner.WeaponManager.ShootController as Player.FirearmController)?.IsInReloadOperation() != true &&
            BotOwner.WeaponManager.Grenades?.ThrowindNow != true && BotOwner.WeaponManager.Grenades?.ReadyToThrow != true;
    }

    public override AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> current)
    {
        if (current.Reason == "exUsec.command.wait")
            return new AICoreActionEnd("exUsec.command.reevaluate", Time.time >= leaderHoldUntil ||
                !CanReconsiderAdvance(BotOwner.Memory.GoalEnemy));
        bool advance = FollowerCombatPush.IsPushReason(current.Reason) ||
            FollowerCombatPush.IsStartWeakEnemyPushReason(current.Reason) ||
            current.Reason?.StartsWith("exUsec.command.follow", System.StringComparison.Ordinal) == true;
        bool movable = FollowerCombatCommon.IsMovementDecision(current) || current.Action == BotLogicDecision.search ||
            current.Action == BotLogicDecision.holdPosition;
        if (advance && movable && Time.time >= nextLeadershipProbe && CanReconsiderAdvance(BotOwner.Memory.GoalEnemy) &&
            BotOwner.GetPlayer.MovementContext.IsGrounded && FollowerCombatCommon.HasSupportedCombatGround(BotOwner.Position))
        {
            nextLeadershipProbe = Time.time + 0.25f;
            if (!CanAdvance(BotOwner.Memory.GoalEnemy) &&
                rifleman.TryPrepareCompanionAdvanceStop(current, "exUsec.command.wait", out var end))
            {
                leaderHoldUntil = Time.time + 1f;
                return end;
            }
        }
        return rifleman.ShallEndCurrentDecision(current);
    }
}

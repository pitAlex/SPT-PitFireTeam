using EFT;
using pitTeam.BigBrain;
using pitTeam.Modules;
using pitTeam.Components;
using UnityEngine;

namespace pitTeam.GoingRogue.Combat;

// BirdEyeAbstractLayer's native 60m preferred / 40m minimum sniper geometry,
// admitted by Core rather than the native finder publishing unchecked movement.
internal sealed class BirdEyeSniperPositioning
{
    internal const float PreferredDistance = 60f, MinimumDistance = 40f;
    private readonly BotOwner owner;
    private readonly FollowerCombatCommon common;
    private float nextProbe;
    private int failedPositions;
    private string positionEnemy;
    private readonly GoonIndependentPursuit independentPursuit;
    internal BirdEyeSniperPositioning(BotOwner owner, FollowerCombatCommon common)
    { this.owner = owner; this.common = common; independentPursuit = new(owner, common, "sniper.birdEye.onYourOwn.pursuit", PreferredDistance); }
    internal void Reset() { nextProbe = 0f; failedPositions = 0; positionEnemy = null; independentPursuit.Reset(); }
    internal bool TryReleasePursuitHold(AICoreActionResult<BotLogicDecision, CoreActionResultParams> current, out AICoreActionEnd end)
        => independentPursuit.TryReleaseHold(current, out end);
    internal void DecisionChanged(AICoreActionResult<BotLogicDecision, CoreActionResultParams> next)
        => independentPursuit.DecisionChanged(next);
    internal bool HasGoonSupport()
        => common.HasActiveCombatEnemy(owner.Memory.GoalEnemy) &&
           TryChooseSupport(owner.Memory.GoalEnemy, out _, out _, out var source) && source != BirdEyeSupportSource.Player;
    internal AICoreActionResult<BotLogicDecision, CoreActionResultParams>? GetDecision(EnemyInfo enemy)
    {
        if (Time.time < nextProbe || !common.HasActiveCombatEnemy(enemy) ||
            common.HasActiveOrPendingHealWork() || common.HasImmediateExplosiveDanger() ||
            common.HasRecoveryPressure(1f) || owner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(owner, 0.75f) ||
            enemy.IsVisible && enemy.CanShoot || owner.WeaponManager.Reload?.Reloading == true ||
            (owner.WeaponManager.ShootController as Player.FirearmController)?.IsInReloadOperation() == true ||
            !owner.WeaponManager.IsWeaponReady || owner.WeaponManager.Selector?.IsChanging == true ||
            owner.WeaponManager.Grenades?.ThrowindNow == true || owner.WeaponManager.Grenades?.ReadyToThrow == true)
            return null;
        var follower = BossPlayers.Instance?.GetFollower(owner);
        if (follower == null || follower.CombatRegroupUsesBossAnchor ||
            follower.GetBoss()?.GetBossLogic()?.IsHitted == true && !FollowerCombatAnchor.IsCombatIndependent(owner) ||
            follower.TryPeekActiveCommand(out _, out _, out _)) return null;
        nextProbe = Time.time + 1f;
        if (KnightSquadCoordinator.TryGetMemberPosition(owner, out var assigned))
        {
            // Refused Knight positions cannot fall through to a competing autonomous move.
            return common.TryCreateAssignedFiringPositionDecision(enemy, assigned, "sniper.FireSupport.position.birdEye.knight",
                out var order, preferBackline: true, enforceMarksmanPositionPolicy: true) ? order : null;
        }
        // Resolve from living same-human Goons and this actor's own admitted reports.
        bool supported = TryChooseSupport(enemy, out var target, out var supportPosition, out var source);
        if (!supported && independentPursuit.TryGetDecision(out var independent)) return independent;
        if (positionEnemy != target.ProfileId) { positionEnemy = target.ProfileId; failedPositions = 0; }
        if (FollowerEnemyTracking.Distance(target) < MinimumDistance ||
            !FollowerEnemyTracking.TryGetKnownPosition(target, out var anchor, out _) ||
            !FollowerCombatCommon.IsFinite(anchor) || !FollowerCombatCommon.IsFinite(owner.Position)) return null;
        var away = (supported ? supportPosition : owner.Position) - anchor;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) return null;
        away = away.normalized;
        var preferred = anchor + away * PreferredDistance;
        // Alternate a small sector after failure, retaining exact-point lane/floor/path gates.
        if (failedPositions > 0) preferred += new Vector3(-away.z, 0f, away.x) * (failedPositions % 2 == 0 ? -10f : 10f);
        var cover = common.GetClosestShootCover(preferred, 20f, maxDistanceFromBot: 90f, avoidBossFireLane: true);
        var point = cover?.Position ?? preferred;
        var separation = point - anchor;
        separation.y = 0f;
        string reason = "sniper.FireSupport.position.birdEye." + (supported ? source.ToString() : "native");
        if (separation.sqrMagnitude >= MinimumDistance * MinimumDistance &&
            common.TryPlanAssignedFiringPositionDecision(target, point, reason, out var planned, out var decision,
                preferBackline: true, enforceMarksmanPositionPolicy: true) &&
            common.TryCommitCompanionSupportPoint(target, planned))
        {
            failedPositions = 0;
            RecordSupport(source, target, supported);
            return decision;
        }
        failedPositions = System.Math.Min(failedPositions + 1, 3);
        // A failed firing position is not proof that the entire contact area is unusable.
        // Reacquire from admitted memory in a <=20m step, retaining the native 40m minimum.
        if (failedPositions >= 2 && !common.HasRecoveryPressure(1f) &&
            common.TryPlanContactAdvanceDecision(target, MinimumDistance, "sniper.birdEye.reacquire", out planned, out decision) &&
            common.TryCommitCompanionSupportPoint(target, planned))
        {
            RecordSupport(source, target, supported);
            return decision;
        }
        return null;
    }

    private void RecordSupport(BirdEyeSupportSource source, EnemyInfo target, bool supported)
        => BattleRecorder.RecordObjectiveDiagnostic(owner, "birdEye.support", "positionCommitted", supported ? source.ToString() : "ownContact",
            () => new { target = target.ProfileId, failedPositions });

    private bool TryChooseSupport(EnemyInfo current, out EnemyInfo target, out Vector3 position, out BirdEyeSupportSource source)
    {
        var boss = BossPlayers.Instance?.GetFollower(owner)?.GetBoss();
        BotOwner knight = null, pipe = null;
        EnemyInfo knightTarget = null, pipeTarget = null;
        foreach (var follower in BossPlayers.GetFollowers())
        {
            var actor = follower.GetBot();
            if (actor == null || actor == owner || actor.IsDead || actor.BotState != EBotState.Active ||
                actor.GetPlayer?.HealthController?.IsAlive != true || !ReferenceEquals(follower.GetBoss(), boss) ||
                follower.CombatRegroupUsesBossAnchor || actor.Medecine.Using ||
                !GoonCombatLayer.IsOwned(actor, actor.Profile.Info.Settings.Role.ToString())) continue;
            var contact = actor.Memory.GoalEnemy;
            if (contact == null || !(follower.IsBotActivelyEngaging(contact.ProfileId) ||
                GoonPushLeadership.HasAutonomousAdvanceIntent(actor, contact.ProfileId))) continue;
            var known = current.ProfileId == contact.ProfileId ? current : common.GetTrackedEnemyByProfileId(contact.ProfileId);
            if (!common.HasActiveCombatEnemy(known) || !FollowerEnemyTracking.TryGetKnownPosition(known, out var report, out _) ||
                !FollowerCombatCommon.IsFinite(report)) continue;
            if (actor.Profile.Info.Settings.Role == WildSpawnType.bossKnight) { knight = actor; knightTarget = known; }
            else if (actor.Profile.Info.Settings.Role == WildSpawnType.followerBigPipe) { pipe = actor; pipeTarget = known; }
        }
        source = BirdEyeSupportPriority.Select(knightTarget?.ProfileId, pipeTarget?.ProfileId);
        if (source != BirdEyeSupportSource.Player)
        {
            target = source == BirdEyeSupportSource.Knight ? knightTarget : pipeTarget;
            position = source == BirdEyeSupportSource.Knight ? knight.Position : pipe.Position;
            return true;
        }
        target = current; position = owner.Position;
        if (boss?.IsPlayerEngaging(out string id, out _) == true)
        {
            var known = current.ProfileId == id ? current : common.GetTrackedEnemyByProfileId(id);
            if (common.HasActiveCombatEnemy(known) && FollowerEnemyTracking.TryGetKnownPosition(known, out _, out _))
            { target = known; position = boss.realPlayer.Position; return true; }
        }
        return false;
    }
}

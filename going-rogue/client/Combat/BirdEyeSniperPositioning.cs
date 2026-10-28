using EFT;
using pitTeam.BigBrain;
using pitTeam.Modules;
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
    internal BirdEyeSniperPositioning(BotOwner owner, FollowerCombatCommon common) { this.owner = owner; this.common = common; }
    internal void Reset() => nextProbe = 0f;
    internal AICoreActionResult<BotLogicDecision, CoreActionResultParams>? GetDecision(EnemyInfo enemy)
    {
        if (Time.time < nextProbe || !common.HasActiveCombatEnemy(enemy) ||
            common.HasActiveOrPendingHealWork() || common.HasImmediateExplosiveDanger() ||
            owner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(owner, 0.75f) ||
            enemy.IsVisible && enemy.CanShoot || owner.WeaponManager.Reload?.Reloading == true ||
            (owner.WeaponManager.ShootController as Player.FirearmController)?.IsInReloadOperation() == true ||
            !owner.WeaponManager.IsWeaponReady || owner.WeaponManager.Selector?.IsChanging == true ||
            owner.WeaponManager.Grenades?.ThrowindNow == true || owner.WeaponManager.Grenades?.ReadyToThrow == true)
            return null;
        var follower = BossPlayers.Instance?.GetFollower(owner);
        if (follower == null || follower.CombatRegroupUsesBossAnchor ||
            follower.GetBoss()?.GetBossLogic()?.IsHitted == true ||
            follower.TryPeekActiveCommand(out _, out _, out _)) return null;
        nextProbe = Time.time + 1f;
        if (KnightSquadCoordinator.TryGetMemberPosition(owner, out var assigned))
        {
            // Refused Knight positions cannot fall through to a competing autonomous move.
            return common.TryCreateAssignedFiringPositionDecision(enemy, assigned, "sniper.FireSupport.position.birdEye.knight",
                out var order, preferBackline: true, enforceMarksmanPositionPolicy: true) ? order : null;
        }
        if (FollowerEnemyTracking.Distance(enemy) < MinimumDistance ||
            !FollowerEnemyTracking.TryGetKnownPosition(enemy, out var anchor, out _) ||
            !FollowerCombatCommon.IsFinite(anchor) || !FollowerCombatCommon.IsFinite(owner.Position)) return null;
        var away = owner.Position - anchor;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) return null;
        var preferred = anchor + away.normalized * PreferredDistance;
        var cover = common.GetClosestShootCover(preferred, 20f, maxDistanceFromBot: 90f, avoidBossFireLane: true);
        var point = cover?.Position ?? preferred;
        var separation = point - anchor;
        separation.y = 0f;
        if (separation.sqrMagnitude < MinimumDistance * MinimumDistance) return null;
        return common.TryCreateAssignedFiringPositionDecision(enemy, point, "sniper.FireSupport.position.birdEye.native",
            out var decision, preferBackline: true, enforceMarksmanPositionPolicy: true) ? decision : null;
    }
}

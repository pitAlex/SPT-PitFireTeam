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
    private readonly GoonIndependentPursuit independentPursuit;
    internal BirdEyeSniperPositioning(BotOwner owner, FollowerCombatCommon common)
    { this.owner = owner; this.common = common; independentPursuit = new(owner, common, "sniper.birdEye.onYourOwn.pursuit", PreferredDistance); }
    internal void Reset() { nextProbe = 0f; independentPursuit.Reset(); }
    internal bool TryReleasePursuitHold(AICoreActionResult<BotLogicDecision, CoreActionResultParams> current, out AICoreActionEnd end)
        => independentPursuit.TryReleaseHold(current, out end);
    internal void DecisionChanged(AICoreActionResult<BotLogicDecision, CoreActionResultParams> next)
        => independentPursuit.DecisionChanged(next);
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
            follower.GetBoss()?.GetBossLogic()?.IsHitted == true && !FollowerCombatAnchor.IsCombatIndependent(owner) ||
            follower.TryPeekActiveCommand(out _, out _, out _)) return null;
        nextProbe = Time.time + 1f;
        if (KnightSquadCoordinator.TryGetMemberPosition(owner, out var assigned))
        {
            // Refused Knight positions cannot fall through to a competing autonomous move.
            return common.TryCreateAssignedFiringPositionDecision(enemy, assigned, "sniper.FireSupport.position.birdEye.knight",
                out var order, preferBackline: true, enforceMarksmanPositionPolicy: true) ? order : null;
        }
        // Native BirdEyeAgro advances while another member sees the same recently
        // reported opponent. Keep the native 30m stopping distance, but advance only
        // through Core's bounded contact-memory movement and shared action/safety gates.
        float reportAge = Time.time - (enemy.GroupInfo?.EnemyLastSeenTimeReal ?? enemy.TimeLastSeenReal);
        if (reportAge < 10f && HasEngagedTeammate(enemy) &&
            common.TryCreateContactAdvanceDecision(enemy, 30f, "sniper.birdEye.teamAdvance", out var advance))
            return advance;
        if (independentPursuit.TryGetDecision(out var pursuit)) return pursuit;
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

    private bool HasEngagedTeammate(EnemyInfo enemy)
    {
        for (int i = 0; i < owner.BotsGroup.MembersCount; i++)
        {
            var member = owner.BotsGroup.Member(i);
            var contact = member?.Memory?.GoalEnemy;
            if (member != owner && member != null && !member.IsDead && contact?.IsVisible == true &&
                string.Equals(contact.ProfileId, enemy.ProfileId, System.StringComparison.Ordinal)) return true;
        }
        return false;
    }
}

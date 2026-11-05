using EFT;
using pitTeam.BigBrain;
using pitTeam.Modules;
using pitTeam.Utils;

namespace pitTeam.GoingRogue.Combat;

// Role initiative changes tactical priority, never perception or permission to fire.
internal static class GoonAssaultPolicy
{
    internal static bool IsAggressor(BotOwner actor) => actor?.Profile?.Info?.Settings?.Role is
        WildSpawnType.bossKnight or WildSpawnType.followerBigPipe;

    internal static bool HasPlayerHelp(BotOwner actor)
    {
        var follower = BossPlayers.Instance?.GetFollower(actor);
        return follower?.TeamSplitCombatActive == true ? follower.TryGetTeamSplitHelpEnemy(out _) :
            follower?.GetBoss()?.GetBossLogic()?.HasManualHelpRequest == true;
    }

    internal static bool CanAssault(BotOwner actor, FollowerCombatCommon common, EnemyInfo enemy)
    {
        var follower = BossPlayers.Instance?.GetFollower(actor);
        return IsAggressor(actor) && follower != null && !follower.CombatRegroupUsesBossAnchor &&
           !follower.HasOrderedPushTargetLock && !follower.TryPeekActiveCommand(out _, out _, out _) &&
           common.HasActiveCombatEnemy(enemy) && !HasPlayerHelp(actor) &&
           !common.IsTemporaryHoldPositionAggressionActive() &&
           GoonCombatThreat.IsLowThreat(actor, enemy, common, allowReportedContact: true);
    }

    internal static bool HasBlockingPressure(BotOwner actor, FollowerCombatCommon common, EnemyInfo enemy)
    {
        if (!common.HasRecoveryPressure(1f) && !actor.Memory.IsUnderFire) return false;
        return !CanIgnoreHarassment(actor, common, enemy);
    }

    internal static bool CanIgnoreHarassment(BotOwner actor, FollowerCombatCommon common, EnemyInfo enemy)
    {
        var role = enemy?.Person?.Profile?.Info?.Settings?.Role;
        bool ordinaryScav = role is WildSpawnType.assault or WildSpawnType.assaultGroup or WildSpawnType.cursedAssault;
        // Harassment from a favourable ordinary Scav is not the same as injury or a dangerous fight.
        return ordinaryScav && !FollowerAwareness.WasRecentlyDamaged(actor) &&
            !FollowerCombatCommon.WasHitRecently(actor, 1.5f) && !common.IsFollowerCriticallyWounded() &&
            CanAssault(actor, common, enemy);
    }
}

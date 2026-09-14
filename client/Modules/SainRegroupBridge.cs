using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.Modules;

// Exposes existing core distances, navigation and squad destination reservations. The
// addon owns regroup policy/state; this bridge does not execute core combat decisions.
public static class SainRegroupBridge
{
    public static float GetTriggerDistance(BotOwner owner) =>
        CombatDistanceConfiguration.Instance.GetBossRegroupTriggerDistance(owner) *
        Mathf.Lerp(PickupFollowerPersonality.RegroupMaxTriggerMultiplier, 1f,
            BossPlayers.Instance?.GetFollower(owner)?.BossProtectionWillingness01 ?? 1f);

    public static float GetCompleteDistance(bool tight) => tight
        ? FollowerCombatRegroupObjective.TightRegroupCompleteDistance
        : FollowerCombatRegroupObjective.GetOrderedRegroupDistance(FollowerCombatTactic.SainMan);

    public static float BossMoveRefreshDistance => CombatDistanceConfiguration.Instance.GetRegroupBossMoveRefreshDistance();
    public static bool SameLevel(Vector3 first, Vector3 second) => FollowerCombatRegroupObjective.IsSameBossLevel(first, second);
    public static bool IsUrbanDetour(float direct, float path) => CombatDistanceConfiguration.Instance.IsUrbanDetourRegroup(direct, path);

    public static bool TryGetDistance(Vector3 from, Vector3 to, out float distance)
    {
        distance = float.PositiveInfinity;
        if (!IsFinite(from) || !IsFinite(to) ||
            !NavMesh.SamplePosition(from, out NavMeshHit start, 2f, NavMesh.AllAreas) ||
            !NavMesh.SamplePosition(to, out NavMeshHit end, 2f, NavMesh.AllAreas) ||
            !SameLevel(start.position, from) || !SameLevel(end.position, to) ||
            !Utils.Utils.TryGetCompletePathDistance(start.position, end.position, out float path) ||
            float.IsNaN(path) || float.IsInfinity(path)) return false;
        distance = FollowerCombatCommon.GetSafeRegroupDistance(path, Vector3.Distance(from, to));
        return true;
    }

    public static bool IsUnderFire(BotOwner owner) => owner.Memory?.IsUnderFire == true || Utils.FollowerAwareness.WasRecentlyDamaged(owner);
    public static float LastShotTime(BotOwner owner) => owner.ShootData?.LastTriggerPressd ?? 0f;

    public static bool TrySpreadDestination(BotOwner owner, Vector3 player, bool tight, out Vector3 target)
    {
        var events = GetEvents(owner);
        if (events != null && events.TryFindBossSpreadDestination(owner, player, 1f, tight ? 2.5f : 6f, 1.75f, 2f, out target))
            return true;
        target = default;
        if (!NavMesh.SamplePosition(player, out NavMeshHit hit, 2f, NavMesh.AllAreas) ||
            !SameLevel(hit.position, player) || !IsDestinationAvailable(owner, hit.position) ||
            !TryGetDistance(owner.Position, hit.position, out _)) return false;
        target = hit.position;
        return true;
    }

    private static bool IsFinite(Vector3 point) =>
        !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
        !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
        !float.IsNaN(point.z) && !float.IsInfinity(point.z);

    public static bool IsDestinationAvailable(BotOwner owner, Vector3 target) =>
        GetEvents(owner)?.HasDestinationClaimConflict(owner, target, 2f) != true;
    public static void Claim(BotOwner owner, Vector3 target) => GetEvents(owner)?.UpsertDestinationClaim(owner, target, 2f);
    public static void Release(BotOwner owner, Vector3 target) => GetEvents(owner)?.TryReleaseDestinationClaim(owner, target, 0.1f);
    private static CombatEvents? GetEvents(BotOwner owner) => (owner.BotFollower?.BossToFollow as pitAIBossPlayer)?.CombatEvents;

    public static void Record(BotOwner owner, string mode, string reason)
    {
        BattleRecorder.RecordObjectiveSwitch(owner, mode == "None" ? "sain.combat" : "sain.regroup." + mode.ToLowerInvariant(), reason);
        Logger.LogInfo($"[SAIN] Regroup: follower={owner.ProfileId} mode={mode} reason={reason}");
    }
}

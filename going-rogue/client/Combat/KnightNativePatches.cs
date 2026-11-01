using System.Collections.Generic;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace pitTeam.GoingRogue.Combat;

[HarmonyPatch(typeof(BotBoss), nameof(BotBoss.Followers), MethodType.Getter)]
internal static class KnightFollowerViewPatch
{
    private static void Postfix(BotBoss __instance, ref List<BotOwner> __result)
    {
        if (KnightSquadCoordinator.TryGet(__instance.Owner, out var coordinator)) __result = coordinator.Members;
    }
}

[HarmonyPatch(typeof(BotBoss), nameof(BotBoss.ManualUpdate))]
internal static class KnightBossTickPatch
{
    private static bool Prefix(BotBoss __instance)
    {
        if (!KnightCombatLayer.IsOwned(__instance.Owner)) return true;
        // BotOwner still owns the native tick, exactly once. Native follower recruitment
        // must not compete with the human's saved follower lifecycle.
        __instance.BossLogic.BossLogicUpdate();
        return false;
    }
}

[HarmonyPatch(typeof(BotBoss), nameof(BotBoss.OfferSelf))]
internal static class KnightRecruitmentPatch
{
    private static bool Prefix(BotBoss __instance, ref bool __result)
    {
        if (!KnightCombatLayer.IsOwned(__instance.Owner)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(BossKnight), nameof(BossKnight.CalcOptimalPositions))]
internal static class KnightSingleCompanionPositionPatch
{
    private static bool Prefix(BossKnight __instance, List<Vector3> positions, List<BotOwner> bots,
        ref Dictionary<BotOwner, Vector3> __result)
    {
        if (!KnightSquadCoordinator.TryGet(__instance._owner, out _) || bots.Count != 1 || positions.Count == 0) return true;
        var closest = positions[0];
        foreach (var position in positions)
            if ((position - bots[0].Position).sqrMagnitude < (closest - bots[0].Position).sqrMagnitude) closest = position;
        __result = new Dictionary<BotOwner, Vector3> { [bots[0]] = closest };
        return false;
    }
}

[HarmonyPatch(typeof(BossKnightLayersStrategy), nameof(BossKnightLayersStrategy.ForceRecalcShootPos))]
internal static class KnightRecalculationPatch
{
    private static bool Prefix(BossKnightLayersStrategy __instance)
    {
        // The original native fight layer is dormant. Recalculation must not let it
        // overwrite a shared cover/medical/command commitment behind BigBrain's back.
        var owner = __instance._fightLayer._owner;
        if (!KnightCombatLayer.IsOwned(owner)) return true;
        KnightCombatLayer.RequestPositionRefresh(owner);
        return false;
    }
}

[HarmonyPatch(typeof(BossKnight), nameof(BossKnight.ForceRecalcShootPos))]
internal static class KnightMemberRecalculationPatch
{
    private static bool Prefix(BossKnight __instance, int shootPosKey)
        => !KnightSquadCoordinator.TryGet(__instance._owner, out var coordinator) ||
           !coordinator.RequestMemberPositionRefresh(shootPosKey);
}

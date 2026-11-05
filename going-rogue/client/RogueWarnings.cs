using System.Collections.Generic;
using Comfort.Common;
using EFT;
using HarmonyLib;
using pitTeam.Modules;

namespace pitTeam.GoingRogue;

[HarmonyPatch(typeof(BotGroupWarnData), nameof(BotGroupWarnData.UpdateWarnTargets))]
internal static class RogueWarningPatch
{
    private static void Postfix(BotGroupWarnData __instance, List<BotGroupEnemyInfo> __result)
    {
        if (GameplayModeAddons.Current != GameplayMode.Allegiance || __result == null ||
            __instance.Boss?.Profile?.Info?.Settings?.Role != WildSpawnType.exUsec ||
            RogueAlliance.Friendship("exUsec") != true) return;
        var player = Singleton<GameWorld>.Instance?.MainPlayer;
        if (player != null) __result.RemoveAll(target => target?.Player?.ProfileId == player.ProfileId);
    }
}

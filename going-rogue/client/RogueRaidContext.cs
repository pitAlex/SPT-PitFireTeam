using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Patches;
using SPT.Common.Http;

namespace pitTeam.GoingRogue;

// Selected saved profiles have generated IDs, so legacy hardcoded boss IDs cannot
// establish raid allegiance. Resolve their existing roster metadata once at setup.
internal static class RogueRaidContext
{
    private static readonly HashSet<BotOwner> Converting = new();
    private static readonly RogueSelectionState Selection = new();
    internal static bool IsPresent => Selection.HasRogues || Converting.Count > 0;
    internal static bool HasGoons => Selection.HasGoons || Converting.Any(bot => RogueRosterSelection.IsGoonRole(bot?.Profile?.Info?.Settings?.Role.ToString()));
    internal static void ResolveSelection(string json) => Selection.Resolve(json);
    internal static void Reset() { Selection.Begin(new string[0]); Converting.Clear(); Combat.GoonPushLeadership.Reset(); Quests.KnightQuestRuntime.Reset(); }

    [HarmonyPatch(typeof(EftClientBackendSession), nameof(EftClientBackendSession.SendRaidSettings))]
    private static class RaidSelectionPatch
    {
        private static void Prefix(RaidSettings settings)
        {
            Reset();
            if (GameplayModeAddons.Current != GameplayMode.GoingRogue || !settings.IsPmc) return;
            var accounts = new HashSet<string>(MainMenuControllerPatch.GroupPlayers.Select(player => player.AccountId));
            Selection.Begin(accounts);
            if (accounts.Count == 0) return;
            try
            {
                string roster = RequestHandler.GetJson("/client/game/bot/followerdetails");
                Selection.Resolve(roster);
            }
            catch (Exception ex) { Logger.LogError($"[GoingRogue] Selected roles unresolved; Goons remain blocked pending spawn preflight: {ex}"); }
        }
    }

    // Also covers debug/recruited companions: Core conversion reads allegiance before
    // adding the follower to its registry. Keep this context only across that call.
    [HarmonyPatch(typeof(BossPlayers), nameof(BossPlayers.AddFollower))]
    private static class FollowerConversionPatch
    {
        private static void Postfix(BotOwner bot, pitAIBossPlayer player, BotFollowerPlayer __result)
        {
            if (__result != null) Quests.KnightQuestRuntime.RegisterKnight(bot, player?.realPlayer);
        }
        private static void Prefix(BotOwner bot, pitAIBossPlayer player, ref bool __state)
        {
            __state = GameplayModeAddons.Current == GameplayMode.GoingRogue && player?.realPlayer?.IsYourPlayer == true &&
                RogueRosterSelection.IsRole(bot?.Profile?.Info?.Settings?.Role.ToString()) && Converting.Add(bot);
        }
        private static void Finalizer(BotOwner bot, bool __state)
        {
            if (__state) Converting.Remove(bot);
        }
    }
}

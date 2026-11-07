using BepInEx;
using pitTeam.Modules;
using EFT;
using System.Linq;
using System.Collections.Generic;
using DrakiaXYZ.BigBrain.Brains;
using HarmonyLib;
using pitTeam.GoingRogue.Combat;
using UnityEngine;

namespace pitTeam.GoingRogue;

[BepInPlugin("xyz.pit.fireteam.goingrogue", "PitFireTeam Going Rogue", "1.0.1")]
[BepInDependency("xyz.pit.fireteam", "1.1.1")]
[BepInDependency("xyz.drakia.bigbrain", BepInDependency.DependencyFlags.HardDependency)]
public sealed class GoingRoguePlugin : BaseUnityPlugin
{
    private Harmony harmony;
    private float nextRefresh;
    internal static bool CombatEnabled { get; private set; }
    private void Awake()
    {
        GameplayModeAddons.Register(GameplayMode.Allegiance, HasRogueCompanions, HasGoonCompanions, exUsecFriendly: false,
            resolveSelection: RogueRaidContext.ResolveSelection,
            companionReady: (id, role) => !(RogueRosterSelection.IsGoonRole(role) || ExUsecCombatExtension.OwnsCombat(role)) || GoonCombatLayer.IsSpawnReady(id, role),
            abortCompanion: GoonCombatLayer.AbortSpawn,
            gearPolicy: GoonGearPolicy.ForRole,
            ownsCombat: role => RogueRosterSelection.IsGoonRole(role) || ExUsecCombatExtension.OwnsCombat(role));
        GameplayModeAddons.RegisterRecruitmentPolicy(GameplayMode.Allegiance,
            RogueAlliance.Friendship, RogueAlliance.Acceptance, RogueAlliance.CanRecruit);
        GameplayModeAddons.RegisterCommandPolicy(GameplayMode.Allegiance,
            role => GoonCommandPolicy.ForRole(role, KnightSquadCoordinator.HasLocalKnight()));
        GameplayModeAddons.RegisterAdvanceOrderPolicy(GameplayMode.Allegiance, GoonPushLeadership.Evaluate);
        GameplayModeAddons.RegisterQuestCompanionProgressPolicy(GameplayMode.Allegiance, RogueAlliance.NeedsCompanion);
        GameplayModeAddons.RegisterSessionRefresh(GameplayMode.Allegiance, Trading.KnightSessionRefresh.RefreshAsync);
        harmony = new Harmony("xyz.pit.fireteam.goingrogue");
        harmony.PatchAll(typeof(GoingRoguePlugin).Assembly);
        BrainManager.AddCustomLayer(typeof(KnightCombatLayer), new List<string> { "Knight" },
            KnightCombatLayer.LayerPriority, new List<WildSpawnType> { WildSpawnType.bossKnight });
        BrainManager.AddCustomLayer(typeof(BigPipeCombatLayer), new List<string> { "BigPipe" },
            BigPipeCombatLayer.LayerPriority, new List<WildSpawnType> { WildSpawnType.followerBigPipe });
        BrainManager.AddCustomLayer(typeof(BirdEyeCombatLayer), new List<string> { "BirdEye" },
            BirdEyeCombatLayer.LayerPriority, new List<WildSpawnType> { WildSpawnType.followerBirdEye });
        BrainManager.AddCustomLayer(typeof(ExUsecCombatLayer), new List<string> { "ExUsec" },
            GoonCombatLayer.LayerPriority, new List<WildSpawnType> { WildSpawnType.exUsec });
        CompanionTeamCommands.Register(GoonTeamSplit.Available, GoonTeamSplit.SetSplit, GoonTeamSplit.Refresh);
        CombatEnabled = true;
    }
    private void Update()
    {
        if (Time.time < nextRefresh) return;
        nextRefresh = Time.time + 0.25f;
        Trading.KnightTraderVisibility.RefreshMode();
        GoonCombatLayer.RefreshAll();
        ExUsecCombatExtension.RefreshAnchors();
        GoonTeamSplit.Refresh();
        RogueAlliance.ApplyHostility();
        RogueEnemyReports.Update();
    }
    private void OnDestroy()
    {
        CombatEnabled = false;
        GoonTeamSplit.Reset();
        CompanionTeamCommands.Unregister();
        Quests.KnightQuestZones.Reset();
        GoonCombatLayer.ReleaseAll();
        ExUsecCombatExtension.Reset();
        harmony?.UnpatchSelf();
        RogueRaidContext.Reset();
        RogueAlliance.Reset();
        Quests.KnightQuestRuntime.Reset();
        GameplayModeAddons.Unregister(GameplayMode.Allegiance);
    }
    private static bool HasRogueCompanions() => RogueRaidContext.IsPresent || GameplayModeAddons.HasSelectedBossCompanions ||
        BossPlayers.GetFollowers().Any(follower => follower.GetBoss()?.realPlayer?.IsYourPlayer == true &&
            follower.GetBot() is BotOwner bot && !bot.IsDead &&
            (bot.Profile.Info.Settings.Role == WildSpawnType.exUsec || pitTeam.Utils.Props.BossFollowersType.Contains(bot.Profile.Info.Settings.Role)));

    private static bool HasGoonCompanions() => RogueRaidContext.HasGoons ||
        BossPlayers.GetFollowers().Any(follower => follower.GetBoss()?.realPlayer?.IsYourPlayer == true &&
            follower.GetBot() is BotOwner bot && !bot.IsDead &&
            pitTeam.Utils.Props.BossFollowersType.Contains(bot.Profile.Info.Settings.Role));
}

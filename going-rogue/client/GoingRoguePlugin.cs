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

[BepInPlugin("xyz.pit.fireteam.goingrogue", "PitFireTeam Going Rogue", "0.1.0")]
[BepInDependency("xyz.pit.fireteam", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("xyz.drakia.bigbrain", BepInDependency.DependencyFlags.HardDependency)]
public sealed class GoingRoguePlugin : BaseUnityPlugin
{
    private Harmony harmony;
    private float nextRefresh;
    internal static bool CombatEnabled { get; private set; }
    private void Awake()
    {
        GameplayModeAddons.Register(GameplayMode.GoingRogue, HasRogueCompanions, HasGoonCompanions, exUsecFriendly: false,
            resolveSelection: RogueRaidContext.ResolveSelection,
            companionReady: (id, role) => !RogueRosterSelection.IsGoonRole(role) || GoonCombatLayer.IsSpawnReady(id, role),
            abortCompanion: GoonCombatLayer.AbortSpawn,
            gearPolicy: GoonGearPolicy.ForRole,
            ownsCombat: RogueRosterSelection.IsGoonRole);
        GameplayModeAddons.RegisterCommandPolicy(GameplayMode.GoingRogue,
            role => GoonCommandPolicy.ForRole(role, KnightSquadCoordinator.HasLocalKnight()));
        GameplayModeAddons.RegisterAdvanceOrderPolicy(GameplayMode.GoingRogue, GoonPushLeadership.Evaluate);
        GameplayModeAddons.RegisterQuestCompanionPolicy(GameplayMode.GoingRogue, Quests.KnightQuestPolicy.UsesMap);
        harmony = new Harmony("xyz.pit.fireteam.goingrogue");
        harmony.PatchAll(typeof(GoingRoguePlugin).Assembly);
        BrainManager.AddCustomLayer(typeof(KnightCombatLayer), new List<string> { "Knight" },
            KnightCombatLayer.LayerPriority, new List<WildSpawnType> { WildSpawnType.bossKnight });
        BrainManager.AddCustomLayer(typeof(BigPipeCombatLayer), new List<string> { "BigPipe" },
            BigPipeCombatLayer.LayerPriority, new List<WildSpawnType> { WildSpawnType.followerBigPipe });
        BrainManager.AddCustomLayer(typeof(BirdEyeCombatLayer), new List<string> { "BirdEye" },
            BirdEyeCombatLayer.LayerPriority, new List<WildSpawnType> { WildSpawnType.followerBirdEye });
        CombatEnabled = true;
    }
    private void Update()
    {
        if (Time.time < nextRefresh) return;
        nextRefresh = Time.time + 0.25f;
        Trading.KnightTraderVisibility.RefreshMode();
        GoonCombatLayer.RefreshAll();
    }
    private void OnDestroy()
    {
        CombatEnabled = false;
        GoonCombatLayer.ReleaseAll();
        harmony?.UnpatchSelf();
        RogueRaidContext.Reset();
        Quests.KnightQuestRuntime.Reset();
        GameplayModeAddons.Unregister(GameplayMode.GoingRogue);
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

using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Quests;
using HarmonyLib;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.GoingRogue.Quests;

// Native counters own progress and saving. This only supplies companion admission and Knight kill input.
internal static class KnightQuestRuntime
{
    [ThreadStatic] private static bool forwardingKnightKill;
    private static WeakReference<GameWorld> raid;
    private static bool knightParticipated;
    private static readonly HashSet<string> creditedDeaths = new HashSet<string>(StringComparer.Ordinal);

    internal static void Reset()
    {
        raid = null;
        creditedDeaths.Clear();
        forwardingKnightKill = false;
        knightParticipated = false;
    }

    private static void ObserveRaid(GameWorld world)
    {
        if (raid != null && raid.TryGetTarget(out var previous) && previous == world) return;
        raid = new WeakReference<GameWorld>(world);
        creditedDeaths.Clear();
        knightParticipated = false;
    }

    internal static void RegisterKnight(BotOwner bot, Player player)
    {
        if (!GameplayModeRuntime.IsGoingRogue || player?.IsYourPlayer != true ||
            bot?.Profile?.Info?.Settings?.Role != WildSpawnType.bossKnight) return;
        ObserveRaid(player.GameWorld);
        knightParticipated = true;
        pitTeam.Modules.Logger.LogInfo("[GoingRogueQuests] Knight joined quest-capable raid profile=" + bot.ProfileId);
    }

    private static Player LocalPlayer => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
    private static BotOwner Knight(Player player) => BossPlayers.GetFollowers()
        .Where(f => f.GetBoss()?.realPlayer == player)
        .Select(f => f.GetBot())
        .FirstOrDefault(bot => bot != null && !bot.IsDead && bot.Profile.Info.Settings.Role == WildSpawnType.bossKnight);

    internal static bool AdmitCounter(TaskConditionCounter counter)
    {
        string id = counter?.Id.ToString();
        // A synthetic Knight event must never credit vanilla or unrelated addon quests.
        if (forwardingKnightKill && id != KnightQuestPolicy.MyLandKills) return false;
        if (!KnightQuestPolicy.OwnsCounter(id)) return true;
        var player = LocalPlayer;
        var owner = player?.QuestController;
        // Never alter AI quest books or non-local profiles that happen to contain these IDs.
        if (owner?.Quests == null || !owner.Quests.Any(q => ReferenceEquals(q, counter.Conditional))) return false;
        ObserveRaid(player.GameWorld);
        var knight = Knight(player);
        string source = owner.Profile?.Id;
        return KnightQuestPolicy.CanCredit(id, GameplayModeRuntime.IsGoingRogue,
            player.HealthController?.IsAlive == true, knight != null,
            knight == null ? float.PositiveInfinity : Vector3.Distance(knight.Position, player.Position),
            !forwardingKnightKill && source == player.ProfileId,
            forwardingKnightKill || source == knight?.ProfileId, knightParticipated);
    }

    internal sealed class DeathInput
    {
        internal Player Player;
        internal Player Victim;
        internal Player Killer;
        internal List<string> Zones;
        internal bool KnightDied;
    }

    internal static DeathInput CaptureDeath(Player victim, IPlayer aggressor)
    {
        if (!GameplayModeRuntime.IsGoingRogue) return null;
        var player = LocalPlayer;
        if (player?.QuestController == null || player.Side == EPlayerSide.Savage || victim == null) return null;
        ObserveRaid(victim.GameWorld);
        // Capture ownership before native death cleanup removes Knight from the follower registry.
        var follower = BossPlayers.GetFollowers().FirstOrDefault(f => f.GetBoss()?.realPlayer == player &&
            f.GetBot()?.Profile.Info.Settings.Role == WildSpawnType.bossKnight &&
            (f.GetBot().ProfileId == victim.ProfileId || f.GetBot().ProfileId == aggressor?.ProfileId));
        if (follower == null) return null;
        var knight = follower.GetBot();
        bool died = knight.ProfileId == victim.ProfileId;
        if (!died && (knight.IsDead || Vector3.Distance(knight.Position, player.Position) > 80f ||
            victim.IsYourPlayer || BossPlayers.GetFollowers().Any(f => f.GetBoss()?.realPlayer == player && f.GetBot()?.ProfileId == victim.ProfileId))) return null;
        return new DeathInput { Player = player, Victim = victim, Killer = knight.GetPlayer,
            KnightDied = died, Zones = new List<string>(victim.TriggerZones ?? new List<string>()) };
    }

    internal static void CompleteDeath(DeathInput input, EFT.Ballistics.DamageInfo damage, EBodyPart bodyPart)
    {
        if (!GameplayModeRuntime.IsGoingRogue || input == null || !creditedDeaths.Add(input.Victim.ProfileId)) return;
        try
        {
            var controller = input.Player.QuestController;
            if (input.KnightDied)
            {
                foreach (var quest in controller.Quests.Where(q => KnightQuestPolicy.Owns(q.Id) &&
                    KnightQuestPolicy.UsesMap(q.Id, input.Player.Location) &&
                    (q.QuestStatus == EQuestStatus.Started || q.QuestStatus == EQuestStatus.AvailableForFinish)).ToArray())
                    quest.SetStatus(EQuestStatus.Fail, true, false);
                return;
            }
            var victim = input.Victim;
            var killer = input.Killer;
            // Submit only the native My Land counter. No profile substitution or global quest forwarding.
            var questMyLand = controller.Quests.FirstOrDefault(q => q.Id == KnightQuestPolicy.MyLand && q.QuestStatus == EQuestStatus.Started);
            if (questMyLand == null) return;
            var hit = controller.CheckHitConditionCounter("Any", victim.Inventory.EquippedInSlotsTemplateIds,
                damage.Weapon, bodyPart, Vector3.Distance(killer.Position, victim.Position),
                victim.Profile.Info.Settings.Role.ToString(), victim.CurrentHour, victim.HealthController?.BodyPartEffects);
            if (hit == null) return;
            forwardingKnightKill = true;
            questMyLand.ConditionCountersManager.Test(1,
                new ConditionCheck(typeof(ConditionKills)).Test(hit),
                new ConditionCheck(typeof(ConditionLocation)).Test(killer.Location),
                new ConditionCheck(typeof(ConditionInZone)).Test(input.Zones));
        }
        catch (Exception ex) { pitTeam.Modules.Logger.LogError("[GoingRogueQuests] Companion death/kill processing failed: " + ex); }
        finally { forwardingKnightKill = false; }
    }
}

[HarmonyPatch(typeof(ConditionCounterManager), nameof(ConditionCounterManager.Test), new[] { typeof(int), typeof(TaskConditionCounter), typeof(ConditionCheck[]) })]
internal static class KnightQuestCounterPatch
{
    private static bool Prefix(TaskConditionCounter counter)
    {
        try { return KnightQuestRuntime.AdmitCounter(counter); }
        catch (Exception ex)
        {
            pitTeam.Modules.Logger.LogError("[GoingRogueQuests] Counter admission failed: " + ex);
            return !KnightQuestPolicy.OwnsCounter(counter?.Id.ToString());
        }
    }
}

[HarmonyPatch(typeof(Player), "OnBeenKilledByAggressor")]
internal static class KnightQuestDeathPatch
{
    private static void Prefix(Player __instance, IPlayer aggressor, out KnightQuestRuntime.DeathInput __state)
    {
        __state = null;
        try { __state = KnightQuestRuntime.CaptureDeath(__instance, aggressor); }
        catch (Exception ex) { pitTeam.Modules.Logger.LogError("[GoingRogueQuests] Death capture failed: " + ex); }
    }
    private static void Postfix(KnightQuestRuntime.DeathInput __state, EFT.Ballistics.DamageInfo damageInfo, EBodyPart bodyPart) =>
        KnightQuestRuntime.CompleteDeath(__state, damageInfo, bodyPart);
}

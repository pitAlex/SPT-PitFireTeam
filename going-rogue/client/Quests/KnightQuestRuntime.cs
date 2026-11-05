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

// Native counters own progress and saving. This only supplies companion admission and Goon kill input.
internal static class KnightQuestRuntime
{
    [ThreadStatic] private static bool forwardingKnightKill;
    [ThreadStatic] private static string forwardedCounter;
    private static WeakReference<GameWorld> raid;
    private static bool knightParticipated;
    private static bool goonParticipated;
    private static bool birdeyeParticipated;
    private static bool pipeParticipated;
    private static readonly HashSet<string> creditedDeaths = new HashSet<string>(StringComparer.Ordinal);

    internal static void Reset()
    {
        raid = null;
        creditedDeaths.Clear();
        forwardingKnightKill = false;
        forwardedCounter = null;
        knightParticipated = false;
        goonParticipated = false;
        birdeyeParticipated = false;
        pipeParticipated = false;
    }

    private static void ObserveRaid(GameWorld world)
    {
        if (raid != null && raid.TryGetTarget(out var previous) && previous == world) return;
        raid = new WeakReference<GameWorld>(world);
        creditedDeaths.Clear();
        knightParticipated = false;
        goonParticipated = false;
        birdeyeParticipated = false;
        pipeParticipated = false;
    }

    internal static void RegisterKnight(BotOwner bot, Player player)
    {
        if (!GameplayModeRuntime.IsAllegiance || player?.IsYourPlayer != true ||
            !IsGoon(bot?.Profile?.Info?.Settings?.Role)) return;
        ObserveRaid(player.GameWorld);
        goonParticipated = true;
        if (bot.Profile.Info.Settings.Role == WildSpawnType.bossKnight) knightParticipated = true;
        if (bot.Profile.Info.Settings.Role == WildSpawnType.followerBirdEye) birdeyeParticipated = true;
        if (bot.Profile.Info.Settings.Role == WildSpawnType.followerBigPipe) pipeParticipated = true;
        pitTeam.Modules.Logger.LogInfo("[GoingRogueQuests] Goon joined quest-capable raid profile=" + bot.ProfileId);
    }

    private static Player LocalPlayer => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
    private static BotOwner Knight(Player player) => BossPlayers.GetFollowers()
        .Where(f => f.GetBoss()?.realPlayer == player)
        .Select(f => f.GetBot())
        .FirstOrDefault(bot => bot != null && !bot.IsDead && bot.Profile.Info.Settings.Role == WildSpawnType.bossKnight);

    private static bool IsGoon(WildSpawnType? role) => role == WildSpawnType.bossKnight ||
        role == WildSpawnType.followerBigPipe || role == WildSpawnType.followerBirdEye;
    private static IEnumerable<BotOwner> Goons(Player player) => BossPlayers.GetFollowers()
        .Where(f => f.GetBoss()?.realPlayer == player).Select(f => f.GetBot())
        .Where(bot => bot != null && !bot.IsDead && IsGoon(bot.Profile.Info.Settings.Role));

    internal static bool AdmitCounter(TaskConditionCounter counter)
    {
        string id = counter?.Id.ToString();
        // A synthetic Knight event must never credit vanilla or unrelated addon quests.
        if (forwardingKnightKill && id != forwardedCounter) return false;
        if (!KnightQuestPolicy.OwnsCounter(id)) return true;
        // Owned companion objectives have their own single death forwarder (including 120m sniper rules).
        if (!forwardingKnightKill && pitTeam.Patches.PlayerKilledPatch.IsForwardingRosterKill) return false;
        var player = LocalPlayer;
        var owner = player?.QuestController;
        // Never alter AI quest books or non-local profiles that happen to contain these IDs.
        if (owner?.Quests == null || !owner.Quests.Any(q => ReferenceEquals(q, counter.Conditional))) return false;
        ObserveRaid(player.GameWorld);
        bool badge = id == KnightQuestPolicy.BadgeBanditKills || id == KnightQuestPolicy.BadgeBanditFailure;
        bool birdeye = id == KnightQuestPolicy.EnemySpottedKills || id == KnightQuestPolicy.EnemySpottedFailure ||
            id == KnightQuestPolicy.AFavorKills || id == KnightQuestPolicy.AFavorFailure;
        bool pier = id == KnightQuestPolicy.OffLimitsKills || id == KnightQuestPolicy.OffLimitsFailure;
        var pair = pier ? Goons(player).Where(bot => bot.Profile.Info.Settings.Role == WildSpawnType.followerBigPipe ||
            bot.Profile.Info.Settings.Role == WildSpawnType.followerBirdEye).ToArray() : Array.Empty<BotOwner>();
        bool hasPair = pair.Any(bot => bot.Profile.Info.Settings.Role == WildSpawnType.followerBigPipe) &&
            pair.Any(bot => bot.Profile.Info.Settings.Role == WildSpawnType.followerBirdEye);
        var knight = pier ? hasPair ? pair.OrderBy(bot => Vector3.Distance(bot.Position, player.Position)).First() : null :
            badge ? Goons(player).OrderBy(bot => Vector3.Distance(bot.Position, player.Position)).FirstOrDefault() :
            birdeye ? Goons(player).FirstOrDefault(bot => bot.Profile.Info.Settings.Role == WildSpawnType.followerBirdEye) : Knight(player);
        string source = owner.Profile?.Id;
        return KnightQuestPolicy.CanCredit(id, GameplayModeRuntime.IsAllegiance,
            player.HealthController?.IsAlive == true, knight != null,
            knight == null ? float.PositiveInfinity : Vector3.Distance(knight.Position, player.Position),
            !forwardingKnightKill && source == player.ProfileId,
            forwardingKnightKill || (badge || pier ? Goons(player).Any(bot => bot.ProfileId == source) : source == knight?.ProfileId),
            pier ? pipeParticipated && birdeyeParticipated : badge ? goonParticipated : birdeye ? birdeyeParticipated : knightParticipated);
    }

    internal sealed class DeathInput
    {
        internal Player Player;
        internal Player Victim;
        internal Player Killer;
        internal List<string> Zones;
        internal bool KnightDied;
        internal bool GoonDied;
        internal bool BirdeyeDied;
        internal bool PipeDied;
    }

    internal static DeathInput CaptureDeath(Player victim, IPlayer aggressor)
    {
        if (!GameplayModeRuntime.IsAllegiance) return null;
        var player = LocalPlayer;
        if (player?.QuestController == null || player.Side == EPlayerSide.Savage || victim == null) return null;
        ObserveRaid(victim.GameWorld);
        // Capture ownership before native death cleanup removes Knight from the follower registry.
        var follower = BossPlayers.GetFollowers().FirstOrDefault(f => f.GetBoss()?.realPlayer == player &&
            IsGoon(f.GetBot()?.Profile.Info.Settings.Role) &&
            (f.GetBot().ProfileId == victim.ProfileId || f.GetBot().ProfileId == aggressor?.ProfileId));
        if (follower == null) return null;
        var knight = follower.GetBot();
        bool died = knight.ProfileId == victim.ProfileId;
        bool closeEnough = Goons(player).Any(bot => Vector3.Distance(bot.Position, player.Position) <= 80f) ||
            knight.Profile.Info.Settings.Role == WildSpawnType.followerBirdEye && Vector3.Distance(knight.Position, player.Position) <= 120f;
        if (!died && (knight.IsDead || !closeEnough ||
            victim.IsYourPlayer || BossPlayers.GetFollowers().Any(f => f.GetBoss()?.realPlayer == player && f.GetBot()?.ProfileId == victim.ProfileId))) return null;
        return new DeathInput { Player = player, Victim = victim, Killer = knight.GetPlayer,
            GoonDied = died, KnightDied = died && knight.Profile.Info.Settings.Role == WildSpawnType.bossKnight,
            BirdeyeDied = died && knight.Profile.Info.Settings.Role == WildSpawnType.followerBirdEye,
            PipeDied = died && knight.Profile.Info.Settings.Role == WildSpawnType.followerBigPipe,
            Zones = new List<string>(victim.TriggerZones ?? new List<string>()) };
    }

    internal static void CompleteDeath(DeathInput input, EFT.Ballistics.DamageInfo damage, EBodyPart bodyPart)
    {
        if (!GameplayModeRuntime.IsAllegiance || input == null || !creditedDeaths.Add(input.Victim.ProfileId)) return;
        try
        {
            var controller = input.Player.QuestController;
            if (input.GoonDied || input.KnightDied)
            {
                foreach (var quest in controller.Quests.Where(q => (q.Id == KnightQuestPolicy.BadgeBandit ||
                    input.KnightDied && (q.Id == KnightQuestPolicy.Competition || q.Id == KnightQuestPolicy.MyLand) ||
                    input.BirdeyeDied && (q.Id == KnightQuestPolicy.EnemySpotted || q.Id == KnightQuestPolicy.AFavor) ||
                    (input.PipeDied || input.BirdeyeDied) && q.Id == KnightQuestPolicy.OffLimits) &&
                    KnightQuestPolicy.UsesMap(q.Id, input.Player.Location) &&
                    (q.QuestStatus == EQuestStatus.Started || q.QuestStatus == EQuestStatus.AvailableForFinish)).ToArray())
                    quest.SetStatus(EQuestStatus.Fail, true, false);
                return;
            }
            var victim = input.Victim;
            var killer = input.Killer;
            var candidates = controller.Quests.Where(q => q.QuestStatus == EQuestStatus.Started &&
                (q.Id == KnightQuestPolicy.BadgeBandit || q.Id == KnightQuestPolicy.OffLimits &&
                    Vector3.Distance(killer.Position, input.Player.Position) <= (killer.Profile.Info.Settings.Role == WildSpawnType.followerBirdEye ? 120f : 80f) ||
                    (q.Id == KnightQuestPolicy.EnemySpotted || q.Id == KnightQuestPolicy.AFavor) && killer.Profile.Info.Settings.Role == WildSpawnType.followerBirdEye || q.Id == KnightQuestPolicy.MyLand &&
                    killer.Profile.Info.Settings.Role == WildSpawnType.bossKnight)).ToArray();
            if (candidates.Length == 0) return;
            foreach (var quest in candidates)
            {
                bool birdeyeQuest = quest.Id == KnightQuestPolicy.EnemySpotted;
                string target = quest.Id == KnightQuestPolicy.MyLand || quest.Id == KnightQuestPolicy.OffLimits ? "Any" :
                    victim.Side == EPlayerSide.Savage ? "Savage" : birdeyeQuest ? "AnyPmc" : "Any";
                var hit = controller.CheckHitConditionCounter(target, victim.Inventory.EquippedInSlotsTemplateIds,
                    damage.Weapon, bodyPart, Vector3.Distance(killer.Position, victim.Position),
                    victim.Profile.Info.Settings.Role.ToString(), victim.CurrentHour, victim.HealthController?.BodyPartEffects);
                if (hit == null) continue;
                forwardingKnightKill = true;
                forwardedCounter = quest.Id == KnightQuestPolicy.AFavor ? KnightQuestPolicy.AFavorKills :
                    quest.Id == KnightQuestPolicy.OffLimits ? KnightQuestPolicy.OffLimitsKills :
                    birdeyeQuest ? KnightQuestPolicy.EnemySpottedKills : quest.Id == KnightQuestPolicy.MyLand ? KnightQuestPolicy.MyLandKills : KnightQuestPolicy.BadgeBanditKills;
                quest.ConditionCountersManager.Test(1,
                    new ConditionCheck(typeof(ConditionKills)).Test(hit),
                    new ConditionCheck(typeof(ConditionLocation)).Test(killer.Location),
                    new ConditionCheck(typeof(ConditionInZone)).Test(input.Zones));
            }
        }
        catch (Exception ex) { pitTeam.Modules.Logger.LogError("[GoingRogueQuests] Companion death/kill processing failed: " + ex); }
        finally { forwardingKnightKill = false; forwardedCounter = null; }
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

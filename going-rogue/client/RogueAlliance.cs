using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Quests;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using pitTeam.Modules;
using SPT.Common.Http;

namespace pitTeam.GoingRogue;

internal static class RogueAlliance
{
    private static WeakReference<GameWorld> raid;
    private static readonly HashSet<string> Deaths = new(StringComparer.Ordinal);
    private static string raidId = Guid.NewGuid().ToString("N");
    private static bool betrayed;
    private static readonly object Reports = new();
    private static Task lastReport = Task.CompletedTask;
    private static int reportRevision;
    private static Profile Profile => Singleton<GameWorld>.Instance?.MainPlayer?.Profile ??
        (Singleton<ClientApplication<IEftSession>>.Instantiated ? Singleton<ClientApplication<IEftSession>>.Instance?.GetClientBackEndSession()?.Profile : null);
    // Read live status/template references: login, acceptance, completion and failure
    // need no separate conflict snapshot or asynchronous refresh.
    internal static bool Conflict => Profile?.QuestsData?.Any(q =>
        (q.Status == EQuestStatus.Started || q.Status == EQuestStatus.AvailableForFinish) &&
        HasHostileObjectives(q.Template)) == true;
    internal static bool HasHostileObjectives(QuestTemplate template) =>
        template?.Conditions?.TryGetValue(EQuestStatus.AvailableForFinish, out var objectives) == true &&
        HasHostileConditions(objectives);
    private static bool HasHostileConditions(IEnumerable<Condition> conditions) => conditions?.Any(condition =>
        condition is ConditionKills kills && RogueAlliancePolicy.HostileKill("Kills", kills.savageRole) ||
        condition is ConditionCounterCreator counter && HasHostileConditions(counter.Conditions)) == true;
    private static int Completed => RogueAlliancePolicy.Completed(id => Profile?.QuestsData?.Any(q => q.Id == id && q.Status == EQuestStatus.Success) == true);
    private static bool Unlocked => Profile?.TryGetTraderInfo(pitTeam.Utils.Props.KnightTrader, out var info) == true && !info.Disabled &&
        Profile.QuestsData?.Any(q => q.Id == RogueAlliancePolicy.Quests[0] && q.Status == EQuestStatus.Success) == true;
    private static double Standing => Profile?.TryGetTraderInfo(pitTeam.Utils.Props.KnightTrader, out var info) == true ? info.Standing : 0d;
    internal static bool? Friendship(string role) => RogueAlliancePolicy.IsExUsec(role) ?
        Unlocked && !Conflict && !betrayed : (bool?)null;
    internal static float? Acceptance(string role)
    {
        if (!RogueAlliancePolicy.IsExUsec(role)) return null;
        float chance = RogueAlliancePolicy.Acceptance(Completed, Standing, HasGoonEscort());
        // The escort is an explicit guarantee, after the quest/standing gates.
        return chance >= 1f ? chance : pitTeam.Shared.FriendlyEncounterPenaltyPolicy.Apply(chance,
            FriendlyEncounterPenaltyRuntime.GetRecruitmentReductionPercent());
    }
    private static bool HasGoonEscort()
    {
        var human = Singleton<GameWorld>.Instance?.MainPlayer;
        return human != null && BossPlayers.GetFollowersByBoss(human.ProfileId).Any(follower =>
            follower?.GetBot() is BotOwner bot && !bot.IsDead && bot.BotState == EBotState.Active &&
            bot.GetPlayer?.HealthController?.IsAlive == true &&
            RogueAlliancePolicy.IsGoon(bot.Profile?.Info?.Settings?.Role.ToString()));
    }
    internal static bool CanRecruit(string role) => !RogueAlliancePolicy.IsExUsec(role) ||
        Completed == RogueAlliancePolicy.Quests.Length && Friendship(role) == true &&
        RogueAlliancePolicy.CanDeploy(role, Standing, Conflict);
    internal static bool NeedsCompanion(string quest, string map, Func<string, bool> completed) =>
        !Conflict && Quests.KnightQuestPolicy.NeedsCompanion(quest, map, completed);

    internal static void Reset()
    {
        raid = null; Deaths.Clear(); betrayed = false; raidId = Guid.NewGuid().ToString("N");
    }
    private static void Observe(GameWorld world)
    {
        if (raid != null && raid.TryGetTarget(out var previous) && ReferenceEquals(previous, world)) return;
        Reset();
        raid = new WeakReference<GameWorld>(world);
    }
    internal static void PlayerKilled(Player victim, IPlayer killer)
    {
        if (GameplayModeAddons.Current != GameplayMode.Allegiance || victim?.IsAI != true || killer?.IsYourPlayer != true ||
            victim.GameWorld?.MainPlayer?.ProfileId != killer.ProfileId ||
            !RogueAlliancePolicy.IsAlly(victim.Profile?.Info?.Settings?.Role.ToString()) || !Unlocked) return;
        Observe(victim.GameWorld);
        var follower = BossPlayers.GetFollowerByProfileId(victim.ProfileId);
        if (follower != null && (follower.HasPersistentProgress || RogueAlliancePolicy.IsGoon(victim.Profile.Info.Settings.Role.ToString()))) return;
        if (!Deaths.Add(victim.ProfileId)) return;
        betrayed = true;
        ApplyHostility();
        // Reuse native client trader publication, but persist/deduplicate on the server.
        string role = victim.Profile.Info.Settings.Role.ToString();
        string report = new JObject { ["raidId"] = raidId, ["victimId"] = victim.ProfileId, ["role"] = role }.ToString();
        var targetProfile = killer.Profile;
        if (targetProfile.TryGetTraderInfo(pitTeam.Utils.Props.KnightTrader, out var local))
            local.SetStanding(RogueAlliancePolicy.AfterPenalty(local.Standing, role));
        var context = SynchronizationContext.Current;
        int revision = ++reportRevision;
        Task previous;
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Reports) { previous = lastReport; lastReport = finished.Task; }
        _ = GameplayModeRuntime.RunRosterRequest(() =>
        {
            try
            {
                previous.GetAwaiter().GetResult();
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        var response = JObject.Parse(RequestHandler.PostJson("/singleplayer/going-rogue/knight/standing", report));
                        if ((int?)response["err"] != 0 || response["data"]?["standing"] == null)
                            throw new InvalidOperationException("Knight standing report was not acknowledged.");
                        double confirmed = (double)response["data"]["standing"];
                        context?.Post(_ => {
                            if (revision == reportRevision && targetProfile.TryGetTraderInfo(pitTeam.Utils.Props.KnightTrader, out var info))
                                info.SetStanding(confirmed);
                        }, null);
                        Logger.LogInfo($"[RogueAlliance] Player kill role={role} penalty={RogueAlliancePolicy.Penalty(role)} standing={confirmed} raidHostile=true");
                        return;
                    }
                    catch when (attempt < 2) { Thread.Sleep(250 * (attempt + 1)); }
                }
            }
            catch (Exception ex) { Logger.LogError($"[RogueAlliance] Knight standing persistence was not acknowledged: {ex}"); }
            finally { finished.TrySetResult(true); }
        });
    }
    internal static void ApplyHostility()
    {
        if (GameplayModeAddons.Current != GameplayMode.Allegiance) return;
        var world = Singleton<GameWorld>.Instance;
        var human = world?.MainPlayer;
        if (human == null || world.AllAlivePlayersList == null) return;
        Observe(world);
        bool hostileTask = Conflict;
        if (!betrayed && !hostileTask) return;
        foreach (var person in world.AllAlivePlayersList)
        {
            var bot = person?.AIData?.BotOwner;
            if (bot == null || bot.IsDead || BossPlayers.IsFollower(bot) || bot.BotsGroup == null ||
                !RogueAlliancePolicy.IsAlly(bot.Profile?.Info?.Settings?.Role.ToString())) continue;
            if (!bot.BotsGroup.IsEnemy((IPlayer)human) && bot.BotsGroup.AddEnemy((IPlayer)human, EBotEnemyCause.addPlayer))
                Logger.LogInfo($"[RogueAlliance] Ambient enemy admitted: bot={person.ProfileId} role={bot.Profile.Info.Settings.Role} player={human.ProfileId} reason={(betrayed ? "betrayal" : "hostileTask")}");
            foreach (var follower in BossPlayers.GetFollowersByBoss(human.ProfileId))
            {
                var member = follower?.GetBot();
                if (member == null || member.IsDead || member.GetPlayer == null) continue;
                if (!bot.BotsGroup.IsEnemy((IPlayer)member.GetPlayer)) bot.BotsGroup.AddEnemy((IPlayer)member.GetPlayer, EBotEnemyCause.addPlayerToBoss);
                if (member.BotsGroup?.IsEnemy((IPlayer)person) == false) member.BotsGroup.AddEnemy((IPlayer)person, EBotEnemyCause.addPlayerToBoss);
            }
        }
    }
}

[HarmonyPatch(typeof(Player), "OnBeenKilledByAggressor")]
internal static class RogueAllianceDeathPatch
{
    private static void Prefix(Player __instance, IPlayer aggressor)
    {
        try { RogueAlliance.PlayerKilled(__instance, aggressor); }
        catch (Exception ex) { Logger.LogError($"[RogueAlliance] Kill handling failed: {ex}"); }
    }
}

[HarmonyPatch(typeof(GameWorld), nameof(GameWorld.OnGameStarted))]
internal static class RogueAllianceRaidPatch
{
    private static void Postfix() => RogueAlliance.Reset();
}

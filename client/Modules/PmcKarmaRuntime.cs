using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.UI;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using pitTeam.Shared;
using SPT.Common.Http;

namespace pitTeam.Modules
{
    internal static class PmcKarmaRuntime
    {
        private static readonly HashSet<string> Damaged = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> FriendlyBeforeDamage = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> Killed = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> Recruits = new HashSet<string>(StringComparer.Ordinal);
        private static readonly object RequestQueue = new object();
        private static Task lastRequest = Task.CompletedTask;
        private static readonly FieldInfo KarmaField = AccessTools.Field(typeof(Profile), nameof(Profile.KarmaValue));
        private static string raidId;
        private static bool hadFriendlies, recruitedAny, ended, allegiance;
        private static SynchronizationContext gameContext;
        private static Player Human => Singleton<GameWorld>.Instance?.MainPlayer;
        private static bool IsPmc(IPlayer player) => player?.Side == EPlayerSide.Usec || player?.Side == EPlayerSide.Bear;

        internal static void BeginRaid(bool transit = false)
        {
            gameContext = SynchronizationContext.Current ?? gameContext;
            if (transit && !string.IsNullOrEmpty(raidId)) { ended = false; return; }
            raidId = Guid.NewGuid().ToString("N");
            Damaged.Clear(); FriendlyBeforeDamage.Clear(); Killed.Clear(); Recruits.Clear();
            hadFriendlies = recruitedAny = ended = false;
            allegiance = GameplayModeRuntime.IsAllegiance;
        }

        internal static void NoteFriendly(BotOwner bot)
        {
            if (!ended && IsPmc(Human) && bot != null) hadFriendlies = true;
        }

        internal static void NoteRecruitment(BotOwner bot, IPlayer leader, bool squadMate)
        {
            if (!ended && !squadMate && IsPmc(leader) && leader.ProfileId == Human?.ProfileId &&
                IsPmc(bot?.GetPlayer) && !string.IsNullOrEmpty(bot.ProfileId))
            {
                recruitedAny = true;
                Recruits.Add(bot.ProfileId);
            }
        }

        private static bool IsFriendlyTarget(Player target, IPlayer player)
        {
            var follower = BossPlayers.GetFollowerByProfileId(target.ProfileId);
            if (follower != null)
                return Recruits.Contains(target.ProfileId) && !follower.IsSquadMate &&
                    follower.GetBoss()?.realPlayer?.ProfileId == player.ProfileId;
            var bot = target.AIData?.BotOwner;
            if (bot?.BotsGroup == null || !IsPmc(target)) return false;
            if (GameplayModeRuntime.IsAllegiance) return AllegiancePmcFriendship.CanRecruit(bot, player);
            return target.Side == player.Side && GameplayModeRuntime.GetEffectiveValue(pitFireTeam.pitFireTeamFLAG) &&
                !GameplayModeRuntime.GetEffectiveValue(pitFireTeam.badGuy) &&
                !bot.BotsGroup.IsEnemy(player) && bot.EnemiesController?.IsEnemy(player) != true &&
                bot.Memory?.GoalEnemy?.Person?.ProfileId != player.ProfileId;
        }

        internal static void RememberBeforePlayerDamage(EFT.Ballistics.DamageInfo damage, Player target)
        {
            var attacker = damage.Player?.iPlayer;
            if (ended || damage.Damage <= 0f || attacker?.IsYourPlayer != true || !IsPmc(attacker) ||
                target?.IsAI != true || string.IsNullOrEmpty(target.ProfileId) || !Damaged.Add(target.ProfileId)) return;
            if (IsFriendlyTarget(target, attacker)) FriendlyBeforeDamage.Add(target.ProfileId);
        }

        internal static void RecordKill(Player victim, IPlayer aggressor)
        {
            try
            {
                if (ended || string.IsNullOrEmpty(raidId) || aggressor?.IsYourPlayer != true || !IsPmc(aggressor) ||
                    victim?.IsAI != true || string.IsNullOrEmpty(victim.ProfileId) ||
                    (!FriendlyBeforeDamage.Contains(victim.ProfileId) && !IsFriendlyTarget(victim, aggressor)) ||
                    !Killed.Add(victim.ProfileId)) return;
                Send(new PmcKarmaReport { RaidId = raidId, Kind = "kill", VictimProfileId = victim.ProfileId }, Human);
            }
            catch (Exception ex) { Logger.LogError("[PmcKarma] Friendly kill capture failed: " + ex); }
        }

        internal static void EndRaid(string profileId, ExitStatus status)
        {
            var human = Human;
            if (ended || string.IsNullOrEmpty(raidId) || human?.ProfileId != profileId || !IsPmc(human)) return;
            ended = true;
            // A transit continues this deployment; reward only its final extraction.
            if (status == ExitStatus.Transit) return;
            bool extracted = (status == ExitStatus.Survived || status == ExitStatus.Runner) &&
                human.HealthController?.IsAlive == true;
            var recruits = extracted ? BossPlayers.GetFollowersByBoss(profileId)
                .Where(f => f != null && !f.IsSquadMate && f.GetBot()?.IsDead != true &&
                    Recruits.Contains(f.GetBot()?.ProfileId ?? string.Empty) && IsPmc(f.GetBot()?.GetPlayer) &&
                    f.GetBot()?.GetPlayer?.HealthController?.IsAlive == true)
                .Select(f => f.GetBot().ProfileId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList()
                : new List<string>();
            // Failed/abandoned connections are not a completed peaceful raid.
            bool completed = status == ExitStatus.Survived || status == ExitStatus.Runner ||
                status == ExitStatus.Killed || status == ExitStatus.MissingInAction;
            Send(new PmcKarmaReport { RaidId = raidId, Kind = "end", Allegiance = allegiance,
                HadFriendlies = hadFriendlies && completed, KilledFriendly = Killed.Count > 0,
                RecruitedAny = recruitedAny, ExtractedRecruitIds = recruits }, human);
        }

        private static void Send(PmcKarmaReport report, Player human)
        {
            var profile = human?.Profile;
            var context = SynchronizationContext.Current ?? gameContext;
            // The shared DTO's property names are also the backend's exact JSON names.
            var json = JsonConvert.SerializeObject(report);
            Task previous;
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (RequestQueue)
            {
                previous = lastRequest;
                lastRequest = finished.Task;
            }
            _ = GameplayModeRuntime.RunRosterRequest(() =>
            {
                try
                {
                    // Preserve capture order: caps make kill/extraction order significant.
                    previous.GetAwaiter().GetResult();
                    for (int attempt = 0; ; attempt++)
                    {
                        try
                        {
                            var body = JObject.Parse(RequestHandler.PostJson("/singleplayer/pitfireteam/pmc-karma", json));
                            if (body["err"]?.Value<int?>() != 0) throw new InvalidOperationException("PMC karma report rejected.");
                            var result = body["data"].ToObject<PmcKarmaResult>();
                            if (result == null || result.KarmaValue < 0d || result.KarmaValue > 1d ||
                                double.IsNaN(result.KarmaValue)) throw new InvalidOperationException("Invalid PMC karma response.");
                            // No Unity access from the HTTP worker. The native GUI sound also works
                            // after GameWorld teardown, unlike KarmaClientController's player-id gate.
                            context?.Post(_ =>
                            {
                                try
                                {
                                    if (profile != null) KarmaField.SetValue(profile, (float)result.KarmaValue);
                                    if (result.Sound != 0) Singleton<GUISounds>.Instance?.PlayKarmaSound(result.Sound > 0);
                                }
                                catch (Exception ex) { Logger.LogError("[PmcKarma] Client feedback failed: " + ex); }
                            }, null);
                            Logger.LogInfo($"[PmcKarma] raid={report.RaidId} kind={report.Kind} karma={result.KarmaValue:F3} sound={result.Sound}");
                            return;
                        }
                        catch when (attempt < 2) { Thread.Sleep(250 * (attempt + 1)); }
                    }
                }
                catch (Exception ex) { Logger.LogError("[PmcKarma] Report failed; karma was not acknowledged: " + ex); }
                finally { finished.TrySetResult(true); }
            });
        }
    }
}

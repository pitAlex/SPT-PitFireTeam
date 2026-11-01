using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using Comfort.Common;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using SPT.Common.Http;

namespace pitTeam.Patches
{
    internal partial class BotsControllerPatch
    {
        private static async Task<Dictionary<string, BotDetails>> LoadSavedSpawnSettings(IEnumerable<string> accounts, Func<bool> valid)
        {
            var required = accounts.Distinct(StringComparer.Ordinal).ToArray();
            Exception failure = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (!valid()) throw new OperationCanceledException("Raid ended during follower preflight");
                try
                {
                    string json = await Task.Run(() => RequestHandler.GetJson("/client/game/bot/followerdetails"));
                    if (!valid()) throw new OperationCanceledException("Raid ended during follower preflight");
                    var rows = FollowerSpawnMetadata.Read(json, required, requireSettings: true);
                    GameplayModeAddons.ConfirmSpawnSelection(json);
                    return rows.ToDictionary(pair => pair.Key, pair => pair.Value.ToObject<BotDetails>()
                        ?? throw new InvalidOperationException("Follower settings deserialization failed"), StringComparer.Ordinal);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failure = ex;
                    Modules.Logger.LogInfo($"[FollowerSpawn] Metadata attempt {attempt + 1}/3 failed: {ex.Message}");
                    if (attempt < 2) await Task.Delay(250);
                }
            }
            throw new InvalidOperationException("Saved follower settings could not be validated", failure);
        }

        private static bool IsRegisteredSpawn(BotOwner bot, pitAIBossPlayer leader, BotFollowerPlayer follower) =>
            bot != null && bot.BotState == EBotState.Active && !bot.IsDead && bot.GetPlayer?.HealthController?.IsAlive == true &&
            follower != null && ReferenceEquals(BossPlayers.Instance?.GetFollower(bot), follower) &&
            ReferenceEquals(bot.BotFollower?.BossToFollow, leader);

        private static bool IsSpawnReady(BotOwner bot, pitAIBossPlayer leader, BotFollowerPlayer follower, string expectedRole) =>
            IsRegisteredSpawn(bot, leader, follower) &&
            string.Equals(bot.Profile?.Info?.Settings?.Role.ToString(), expectedRole, StringComparison.OrdinalIgnoreCase) &&
            GameplayModeAddons.IsSpawnCombatReady(bot.ProfileId, expectedRole);

        private static async Task CompleteSavedFollowerSpawn(Task<BotFollowerPlayer> registration,
            Func<BotOwner> activated, pitAIBossPlayer leader, string expectedRole, Func<bool> valid, Func<bool> started)
        {
            var follower = await FollowerSpawnReadiness.Registration(registration, TimeSpan.FromSeconds(8),
                valid, started, () => Task.Delay(100));
            var bot = activated();
            await FollowerSpawnReadiness.Wait(() => valid() && IsRegisteredSpawn(bot, leader, follower),
                () => IsSpawnReady(bot, leader, follower, expectedRole), () => Task.Delay(100));
            Modules.Logger.LogInfo($"[FollowerSpawn] Registration and combat ready: follower={bot.ProfileId}");
            TrySayFollowerReadyControlled(bot, 2000, () => valid() && IsSpawnReady(bot, leader, follower, expectedRole));
        }

        private static void ReportSpawnFailure(string nickname, Exception error, pitAIBossPlayer leader = null)
        {
            Modules.Logger.LogError($"[FollowerSpawn] Setup failed for {nickname}: {error}");
            if (leader != null && !ReferenceEquals(BossPlayers.GetBoss(leader.realPlayer.ProfileId), leader)) return;
            if (!Singleton<GameWorld>.Instantiated || GamePlayerOwner.MyPlayer?.HealthController?.IsAlive != true) return;
            try { AddTeammateCreationFlow.ShowToast(string.Format(pitFireTeam.GetSocialUiText("FollowerSpawnFailedToast"), nickname)); }
            catch (Exception ex) { Modules.Logger.LogError($"[FollowerSpawn] Could not show setup failure: {ex}"); }
        }
    }
}

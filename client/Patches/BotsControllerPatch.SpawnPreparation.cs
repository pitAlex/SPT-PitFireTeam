using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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

        private static void RestoreSpawnHealth(BotOwner bot)
        {
            if (FollowerTransitStateCache.IsTransitSpawnProfile(bot.ProfileId))
            {
                Modules.Logger.LogInfo($"[Transit] Preserved carried health for '{bot.Profile?.Nickname ?? bot.ProfileId}'.");
                return;
            }

            if (!FollowerRaidHealthRetention.Begin(bot)) bot.GetPlayer.ActiveHealthController.RestoreFullHealth();
        }

        private static async Task SpawnSavedFollower(BotsController controller, BotCreatorClient creator, BotCreationData data,
            Profile profile, PositionNote position, BotZone zone, pitAIBossPlayer leader, EPlayerSide side, bool nativeGroup,
            Func<BotOwner, BotZone, BotsGroup> getGroup, Func<BotOwner, BotFollowerPlayer> attach,
            CancellationToken cancellationToken = default)
        {
            var spawner = controller.BotSpawner;
            if (creator == null || spawner == null)
            {
                ReportSpawnFailure(profile.Nickname, new InvalidOperationException("Native follower bot creator is unavailable"), leader);
                return;
            }
            var world = Singleton<GameWorld>.Instance;
            var game = creator._game;
            string profileId = profile.ProfileId;
            string expectedRole = profile.Info?.Settings?.Role.ToString();
            using var nativeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var nativeToken = nativeCancellation.Token;
            var registration = new TaskCompletionSource<BotFollowerPlayer>(TaskCreationOptions.RunContinuationsAsynchronously);
            BotOwner bot = null;
            string watcherId = null;
            Task creation = null;
            bool pendingCount = false;
            bool nativeTracked = false;
            bool awaitingStartupActivation = false;
            bool SameRaid() => ReferenceEquals(Controller, controller) && Singleton<GameWorld>.Instantiated &&
                ReferenceEquals(Singleton<GameWorld>.Instance, world) &&
                ReferenceEquals(BossPlayers.GetBoss(leader.realPlayer.ProfileId), leader);
            bool RaidValid() => SameRaid() && !nativeToken.IsCancellationRequested && !data.SpawnStopped &&
                leader.realPlayer.HealthController?.IsAlive == true &&
                game.Status != GameStatus.Stopped && game.Status != GameStatus.Stopping && game.Status != GameStatus.SoftStopping;
            bool Started() => game.Status == GameStatus.Started;
            bool ActorValid() => RaidValid() && bot != null && bot.BotState != EBotState.Disposed &&
                !bot.IsDead && bot.GetPlayer?.HealthController?.IsAlive == true &&
                (spawner._bots.BotOwners.Contains(bot) || spawner._bots._botsToAdd.Contains(bot));

            void ObserveState(EBotState state)
            {
                if (state != EBotState.NonActive) awaitingStartupActivation = false;
                if (state == EBotState.ActiveFail)
                    registration.TrySetException(new InvalidOperationException("Native follower activation failed"));
            }

            void Register(BotOwner active)
            {
                if (!ReferenceEquals(bot, active) || registration.Task.IsCompleted) return;
                BotOwnerManualUpdatePatch.BotOwnerUpdate.Remove(watcherId);
                try
                {
                    if (!ActorValid()) throw new OperationCanceledException("Follower disappeared before registration");
                    var follower = attach(active) ?? throw new InvalidOperationException("Follower initialization returned no registered follower");
                    registration.TrySetResult(follower);
                }
                catch (Exception ex) { registration.TrySetException(ex); }
            }

            // This is a diagnostic interval. It never ends a native spawn.
            Action PendingLog(string stage)
            {
                var elapsed = new Stopwatch();
                bool logged = false;
                return () =>
                {
                    if (logged || !Started()) return;
                    if (!elapsed.IsRunning) elapsed.Start();
                    if (elapsed.Elapsed < TimeSpan.FromSeconds(8)) return;
                    logged = true;
                    try
                    {
                        pitFireTeam.Log.LogWarning($"[FollowerSpawn] Still waiting for {stage}: profile={profileId}, role={expectedRole}, " +
                            $"state={bot?.BotState}, weaponReady={bot?.WeaponManager?.IsReady}, game={game.Status}, " +
                            $"position={position.position}, zone={zone?.name}, corePoint={position.CorePointId}");
                    }
                    catch { /* A diagnostic must never abort a spawn. */ }
                };
            }

            try
            {
                // The native single-profile wrapper yields before it starts creating the actor.
                await Task.Yield();
                if (!RaidValid()) return;
                InteractableObjects.StoreEquipment(profile);
                BossPlayers.ShallBeFollower(profileId);
                spawner._inSpawnProcess++;
                pendingCount = true;
                var spawnWatch = Stopwatch.StartNew();

                // The single-profile ActivateBot overload schedules CreateBot without awaiting it.
                // Await CreateBot itself and use the same native pre-activation callback as that overload.
                creation = creator.CreateBot(profile, position, owner =>
                {
                    bot = owner;
                    if (!RaidValid()) throw new OperationCanceledException("Raid ended during native follower creation");
                    creator.method_3(zone, owner, prepared =>
                    {
                        awaitingStartupActivation = !Started() && prepared.BotState == EBotState.NonActive;
                        watcherId = prepared.ProfileId;
                        BotOwnerManualUpdatePatch.BotOwnerUpdate.Add(watcherId, Register);
                        prepared.OnBotStateChange += ObserveState;

                        if (prepared.Side != side) prepared.GetPlayer.Profile.Info.Side = side;
                        int countBeforeCallback = spawner._inSpawnProcess;
                        try
                        {
                            spawner.ActivateBotCallback(prepared, data, null, nativeGroup, spawnWatch);
                        }
                        finally
                        {
                            // Native normally consumes the pending count, including when a spawn has stopped.
                            pendingCount = spawner._inSpawnProcess >= countBeforeCallback;
                            nativeTracked = !pendingCount || spawner._bots._botsToAdd.Contains(prepared);
                        }
                        Modules.Logger.LogInfo($"[FollowerSpawn] Native creation complete: profile={watcherId}, role={expectedRole}, state={prepared.BotState}, game={game.Status}");
                        ObserveState(prepared.BotState);
                    }, getGroup);
                }, true, nativeToken);
                await FollowerSpawnReadiness.AwaitSignal(creation, RaidValid, () => Task.Delay(100),
                    onPending: PendingLog("native creation"));
                if (watcherId == null) throw new InvalidOperationException("Native follower creation completed without an actor callback");

                var logRegistration = PendingLog("activation and registration");
                await FollowerSpawnReadiness.AwaitSignal(registration.Task, ActorValid, () => Task.Delay(100), Started, () =>
                {
                    // Native CheckActivation only visits bots already added to the list.
                    // An actor queued immediately before that pass can miss the startup transition.
                    if (awaitingStartupActivation && Started() && bot.BotState == EBotState.NonActive &&
                        spawner._bots.BotOwners.Contains(bot))
                    {
                        awaitingStartupActivation = false;
                        bot.PostActivate();
                    }
                    ObserveState(bot.BotState);
                    logRegistration();
                });
                var follower = await registration.Task;
                await FollowerSpawnReadiness.Wait(() => ActorValid() && IsRegisteredSpawn(bot, leader, follower),
                    () => IsSpawnReady(bot, leader, follower, expectedRole), () => Task.Delay(100));
                Modules.Logger.LogInfo($"[FollowerSpawn] Registration and combat ready: follower={bot.ProfileId}");
                TrySayFollowerReadyControlled(bot, 2000, () => ActorValid() && IsSpawnReady(bot, leader, follower, expectedRole));
            }
            catch (Exception ex)
            {
                nativeCancellation.Cancel();
                if (creation?.IsFaulted == true) _ = creation.Exception;
                else if (creation != null && !creation.IsCompleted) creation.HandleExceptions();
                if (registration.Task.IsFaulted) _ = registration.Task.Exception;
                if (SameRaid() && bot != null && bot.BotState != EBotState.Disposed)
                {
                    spawner._bots._botsToAdd.Remove(bot);
                    // Native despawn decrements the living count unless the actor was never counted.
                    if (!nativeTracked) bot.IsDead = true;
                }
                if (SameRaid()) BossPlayers.AbortSpawnFollower(profileId, bot, leader);
                if (!(ex is OperationCanceledException))
                    ReportSpawnFailure(profile.Nickname, ex, leader);
            }
            finally
            {
                if (pendingCount) spawner._inSpawnProcess--;
                if (bot != null) bot.OnBotStateChange -= ObserveState;
                if (watcherId != null &&
                    BotOwnerManualUpdatePatch.BotOwnerUpdate.TryGetValue(watcherId, out var current) && current == Register)
                    BotOwnerManualUpdatePatch.BotOwnerUpdate.Remove(watcherId);
            }
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

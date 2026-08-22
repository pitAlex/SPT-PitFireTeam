using EFT;
using EFT.Game.Spawning;
using Comfort.Common;
using EFT.InventoryLogic;
using EFT.UI.Matchmaker;
using pitTeam.Modules;
using pitTeam.Utils;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using SPT.Common.Utils;
using SPT.Reflection.Patching;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using playerGroup = System.Collections.Generic.List<EFT.UI.Matchmaker.RaidPlayer>;
using OtherProfileResult = EFT.OtherPlayerProfileDescriptor;
using ResultProfile = EFT.OtherPlayerProfile;

namespace pitTeam.Patches
{
    internal static class SyntheticTeammateRaidGuard
    {
        private static readonly TimeSpan SyntheticRaidStartErrorWindow = TimeSpan.FromSeconds(30);
        private static DateTime _lastSyntheticRaidStartUtc = DateTime.MinValue;

        private static readonly MethodInfo LocalRaidStartMethod = AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.LocalGameMatching), new Type[]
        {
            typeof(TimeAndWeatherSettings),
            typeof(bool)
        });

        public static bool HasSyntheticTeammates()
        {
            return MainMenuControllerPatch.GroupPlayers != null && MainMenuControllerPatch.GroupPlayers.Count > 0;
        }

        public static bool TryForceLocalRaid(TarkovApplication application, string reason)
        {
            if (application == null || !HasSyntheticTeammates())
            {
                return false;
            }

            RaidSettings raidSettings = AccessTools.Field(typeof(TarkovApplication), "_raidSettings").GetValue(application) as RaidSettings;
            if (raidSettings == null)
            {
                pitFireTeam.Log.LogWarning($"[Raid] Failed to force local raid at {reason}: raid settings missing.");
                return false;
            }

            raidSettings.RaidMode = ERaidMode.Local;
            raidSettings.IsPveOffline = true;
            _lastSyntheticRaidStartUtc = DateTime.UtcNow;
            Modules.Logger.LogInfo($"[Raid] Forced local teammate raid at {reason}. groupPlayers={MainMenuControllerPatch.GroupPlayers.Count}");
            return true;
        }

        public static bool IsRecentSyntheticRaidStart()
        {
            return HasSyntheticTeammates() && DateTime.UtcNow - _lastSyntheticRaidStartUtc <= SyntheticRaidStartErrorWindow;
        }

        public static Task StartLocalRaid(TarkovApplication application)
        {
            RaidSettings raidSettings = AccessTools.Field(typeof(TarkovApplication), "_raidSettings").GetValue(application) as RaidSettings;
            if (raidSettings == null)
            {
                throw new InvalidOperationException("Raid settings missing while starting local teammate raid.");
            }

            return (Task)LocalRaidStartMethod.Invoke(application, new object[]
            {
                raidSettings.TimeAndWeatherSettings,
                false
            });
        }
    }

    // Finalizer-only compatibility patch for raid startup. We still let EFT run
    // HideoutGame.Dispose normally, then suppress only the known null-ref that can
    // happen when hideout/trader-scene unload races our synthetic local raid start.
    internal class HideoutGameDisposeSyntheticRaidGuardPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HideoutGame), "Dispose");
        }

        [PatchFinalizer]
        private static Exception PatchFinalizer(HideoutGame __instance, Exception __exception)
        {
            if (__exception == null)
            {
                return null;
            }

            // Some hideout/trader-scene unload paths can null-ref exactly as the
            // synthetic teammate flow switches the client into a local raid. This
            // finalizer is intentionally narrow: only suppress that null-ref during
            // the short raid-start window, and let every other dispose failure surface.
            if (__exception is NullReferenceException && SyntheticTeammateRaidGuard.IsRecentSyntheticRaidStart())
            {
                pitFireTeam.Log.LogWarning("[Raid] Suppressed HideoutGame.Dispose null-ref during synthetic teammate raid start.");
                pitFireTeam.Log.LogWarning(__exception);

                try
                {
                    if (__instance != null && __instance.gameObject != null)
                    {
                        UnityEngine.Object.DestroyImmediate(__instance.gameObject);
                    }
                }
                catch (Exception cleanupException)
                {
                    pitFireTeam.Log.LogWarning("[Raid] Failed fallback hideout game-object cleanup after suppressed dispose exception.");
                    pitFireTeam.Log.LogWarning(cleanupException);
                }

                return null;
            }

            return __exception;
        }
    }

    internal static class SyntheticTeammateVisualHealth
    {
        public static void Ensure(EFT.UI.Matchmaker.RaidPlayer teammate, Profile.HealthInfo referenceHealth)
        {
            if (teammate == null || referenceHealth == null)
            {
                return;
            }

            Profile.HealthInfo sourceHealth = teammate.PlayerVisualRepresentation?.Info?.Health ?? teammate.Info?.Health;
            Profile.HealthInfo normalizedHealth = Normalize(sourceHealth, referenceHealth);

            if (teammate.PlayerVisualRepresentation?.Info != null)
            {
                teammate.PlayerVisualRepresentation.Info.Health = normalizedHealth;
            }

            if (teammate.Info != null)
            {
                teammate.Info.Health = CloneHealth(normalizedHealth);
            }
        }

        public static Profile.HealthInfo Normalize(Profile.HealthInfo sourceHealth, Profile.HealthInfo referenceHealth)
        {
            if (referenceHealth == null)
            {
                return sourceHealth;
            }

            if (sourceHealth == null)
            {
                return CloneHealth(referenceHealth);
            }

            Profile.HealthInfo normalizedHealth = CloneHealth(referenceHealth);

            normalizedHealth.Energy = CloneValueInfo(sourceHealth.Energy) ?? normalizedHealth.Energy;
            normalizedHealth.Hydration = CloneValueInfo(sourceHealth.Hydration) ?? normalizedHealth.Hydration;
            normalizedHealth.Temperature = CloneValueInfo(sourceHealth.Temperature) ?? normalizedHealth.Temperature;
            normalizedHealth.Poison = CloneValueInfo(sourceHealth.Poison) ?? normalizedHealth.Poison;
            normalizedHealth.UpdateTime = sourceHealth.UpdateTime ?? normalizedHealth.UpdateTime;

            if (sourceHealth.BodyParts != null)
            {
                foreach (KeyValuePair<EBodyPart, Profile.HealthInfo.BodyPartInfo> bodyPart in sourceHealth.BodyParts)
                {
                    Profile.HealthInfo.BodyPartInfo clonedBodyPart = CloneBodyPart(bodyPart.Value);
                    if (clonedBodyPart != null)
                    {
                        normalizedHealth.BodyParts[bodyPart.Key] = clonedBodyPart;
                    }
                }
            }

            return normalizedHealth;
        }

        public static Profile.HealthInfo CloneHealth(Profile.HealthInfo source)
        {
            if (source == null)
            {
                return null;
            }

            Profile.HealthInfo clone = new Profile.HealthInfo
            {
                Energy = CloneValueInfo(source.Energy) ?? new Profile.HealthInfo.ValueInfo(),
                Hydration = CloneValueInfo(source.Hydration) ?? new Profile.HealthInfo.ValueInfo(),
                Temperature = CloneValueInfo(source.Temperature) ?? new Profile.HealthInfo.ValueInfo(),
                Poison = CloneValueInfo(source.Poison) ?? new Profile.HealthInfo.ValueInfo(),
                UpdateTime = source.UpdateTime,
                BodyParts = new Dictionary<EBodyPart, Profile.HealthInfo.BodyPartInfo>()
            };

            if (source.BodyParts != null)
            {
                foreach (KeyValuePair<EBodyPart, Profile.HealthInfo.BodyPartInfo> bodyPart in source.BodyParts)
                {
                    Profile.HealthInfo.BodyPartInfo clonedBodyPart = CloneBodyPart(bodyPart.Value);
                    if (clonedBodyPart != null)
                    {
                        clone.BodyParts[bodyPart.Key] = clonedBodyPart;
                    }
                }
            }

            return clone;
        }

        private static Profile.HealthInfo.ValueInfo CloneValueInfo(Profile.HealthInfo.ValueInfo source)
        {
            if (source == null)
            {
                return null;
            }

            return new Profile.HealthInfo.ValueInfo
            {
                Current = source.Current,
                Minimum = source.Minimum,
                Maximum = source.Maximum,
                OverDamageReceivedMultiplier = source.OverDamageReceivedMultiplier,
                EnvironmentDamageMultiplier = source.EnvironmentDamageMultiplier
            };
        }

        private static Profile.HealthInfo.BodyPartInfo CloneBodyPart(Profile.HealthInfo.BodyPartInfo source)
        {
            if (source == null)
            {
                return null;
            }

            Profile.HealthInfo.BodyPartInfo bodyPart = new Profile.HealthInfo.BodyPartInfo
            {
                Health = CloneValueInfo(source.Health) ?? new Profile.HealthInfo.ValueInfo(),
                Effects = new Dictionary<string, Profile.HealthInfo.EffectInfo>()
            };

            if (source.Effects == null)
            {
                return bodyPart;
            }

            foreach (KeyValuePair<string, Profile.HealthInfo.EffectInfo> effect in source.Effects)
            {
                if (string.IsNullOrWhiteSpace(effect.Key) || effect.Value == null)
                {
                    continue;
                }

                bodyPart.Effects[effect.Key] = new Profile.HealthInfo.EffectInfo
                {
                    Time = effect.Value.Time
                };
            }

            return bodyPart;
        }
    }

    internal static class SyntheticTeammateAutoJoinLoader
    {
        private const string AutoJoinRoute = "/singleplayer/autoteam";
        private const string ProfileRoute = "/singleplayer/pitfireteam/teammate/profile";

        public static void EnsureLoaded(EFT.UI.Matchmaker.MatchmakerPlayersController controller)
        {
            if (controller?.CurrentPlayer?.Info == null)
            {
                return;
            }

            bool addedSyntheticTeammate = false;
            foreach (string accountId in LoadAutoJoinAccountIds())
            {
                EFT.UI.Matchmaker.RaidPlayer teammate = BuildGroupPlayer(accountId, controller.CurrentPlayer);
                if (teammate == null)
                {
                    continue;
                }

                ReplaceOrAddPlayer(MainMenuControllerPatch.GroupPlayers, teammate);

                if (controller.GroupPlayers != null)
                {
                    ReplaceOrAddPlayer(controller.GroupPlayers, teammate);
                }

                addedSyntheticTeammate = true;
            }

            if (addedSyntheticTeammate)
            {
                EnsureLocalGroupOwner(controller);
            }
        }

        public static void RefreshLoadedTeammateVisuals(EFT.UI.Matchmaker.MatchmakerPlayersController controller)
        {
            if (controller?.CurrentPlayer?.Info == null || MainMenuControllerPatch.GroupPlayers == null || MainMenuControllerPatch.GroupPlayers.Count == 0)
            {
                return;
            }

            string currentPlayerAccountId = controller.CurrentPlayer.AccountId;
            List<string> accountIds = new List<string>();
            foreach (EFT.UI.Matchmaker.RaidPlayer player in MainMenuControllerPatch.GroupPlayers)
            {
                string accountId = player?.AccountId;
                if (string.IsNullOrWhiteSpace(accountId)
                    || string.Equals(accountId, currentPlayerAccountId, StringComparison.Ordinal)
                    || accountIds.Contains(accountId))
                {
                    continue;
                }

                accountIds.Add(accountId);
            }

            foreach (string accountId in accountIds)
            {
                EFT.UI.Matchmaker.RaidPlayer refreshed = BuildGroupPlayer(accountId, controller.CurrentPlayer);
                if (refreshed == null)
                {
                    continue;
                }

                // EFT.UI.Matchmaker.RaidPlayer carries a full visual snapshot. If the player edits a teammate's
                // loadout after inviting them, the cached matchmaker entry must be replaced before previews render.
                ReplaceOrAddPlayer(MainMenuControllerPatch.GroupPlayers, refreshed);

                if (controller.GroupPlayers != null)
                {
                    ReplaceOrAddPlayer(controller.GroupPlayers, refreshed);
                }
            }
        }

        private static void ReplaceOrAddPlayer(IList<EFT.UI.Matchmaker.RaidPlayer> players, EFT.UI.Matchmaker.RaidPlayer teammate)
        {
            if (players == null || teammate == null || string.IsNullOrWhiteSpace(teammate.AccountId))
            {
                return;
            }

            int existingIndex = -1;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i]?.AccountId == teammate.AccountId)
                {
                    existingIndex = i;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                players[existingIndex] = teammate;
                return;
            }

            players.Add(teammate);
        }

        private static void EnsureLocalGroupOwner(EFT.UI.Matchmaker.MatchmakerPlayersController controller)
        {
            if (controller?.CurrentPlayer == null || controller.GroupPlayers == null || controller.Group == null)
            {
                return;
            }

            if (controller.GroupPlayers.All(player => player?.AccountId != controller.CurrentPlayer.AccountId))
            {
                controller.GroupPlayers.Insert(0, controller.CurrentPlayer);
            }

            controller.CurrentPlayer.IsLeader = true;
            controller.Group.UpdateOwner(controller.CurrentPlayer);
            EFT.UI.BaseContextInteractions.RequestGlobalRedraw();
        }

        private static IReadOnlyList<string> LoadAutoJoinAccountIds()
        {
            try
            {
                string response = RequestHandler.GetJson(AutoJoinRoute);
                if (string.IsNullOrWhiteSpace(response))
                {
                    return Array.Empty<string>();
                }

                JToken root = JToken.Parse(response);
                JToken dataToken = root.Type == JTokenType.Array ? root : root["data"];
                if (dataToken is not JArray ids)
                {
                    return Array.Empty<string>();
                }

                return TeammateAutoJoinRuntime.FilterInviteCandidates(ids.Values<string>());
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning("[UI] Failed to load persisted auto-join teammate ids.");
                pitFireTeam.Log.LogError(ex);
                return Array.Empty<string>();
            }
        }

        private static EFT.UI.Matchmaker.RaidPlayer BuildGroupPlayer(string accountId, EFT.UI.Matchmaker.RaidPlayer currentPlayer)
        {
            if (string.IsNullOrWhiteSpace(accountId) || currentPlayer?.Info == null)
            {
                return null;
            }

            try
            {
                string responseJson = RequestHandler.PostJson(ProfileRoute, JsonConvert.SerializeObject(new { accountId }));
                FriendlyTeammateBodyResponse<OtherProfileResult> body =
                    JsonConvert.DeserializeObject<FriendlyTeammateBodyResponse<OtherProfileResult>>(responseJson);

                if (body?.err != 0)
                {
                    pitFireTeam.Log.LogWarning($"[UI] Failed to build auto-join teammate '{accountId}': {body?.errmsg}");
                    return null;
                }

                OtherProfileResult profileResult = body?.data;
                if (profileResult == null)
                {
                    return null;
                }

                ResultProfile profile = new ResultProfile(profileResult);
                EFT.PlayerVisualRepresentation playerVisualization = profile.PlayerVisualRepresentation;
                if (playerVisualization?.Info == null)
                {
                    return null;
                }

                Profile.HealthInfo normalizedHealth =
                    SyntheticTeammateVisualHealth.Normalize(playerVisualization.Info.Health, currentPlayer.Info.Health);
                playerVisualization.Info.Health = normalizedHealth;

                JsonType.PlayerInfo previewInfo = new JsonType.PlayerInfo
                {
                    Level = playerVisualization.Info.Level,
                    PrestigeLevel = playerVisualization.Info.PrestigeLevel,
                    MemberCategory = EMemberCategory.Unheard,
                    SelectedMemberCategory = EMemberCategory.Unheard,
                    Nickname = playerVisualization.Info.Nickname ?? accountId,
                    Side = playerVisualization.Info.Side,
                    SavageLockTime = currentPlayer.Info.SavageLockTime,
                    SavageNickname = currentPlayer.Info.Nickname,
                    GameVersion = currentPlayer.Info.GameVersion,
                    HasCoopExtension = currentPlayer.Info.HasCoopExtension,
                    Health = SyntheticTeammateVisualHealth.CloneHealth(normalizedHealth)
                };
                playerVisualization.Info.MemberCategory = EMemberCategory.Unheard;
                playerVisualization.Info.SelectedMemberCategory = EMemberCategory.Unheard;

                return new EFT.UI.Matchmaker.RaidPlayer(new EFT.GroupPlayer
                {
                    AccountId = accountId,
                    Id = accountId,
                    Info = new JsonType.PlayerInfo
                    {
                        Level = previewInfo.Level,
                        PrestigeLevel = previewInfo.PrestigeLevel,
                        MemberCategory = previewInfo.MemberCategory,
                        SelectedMemberCategory = previewInfo.SelectedMemberCategory,
                        Nickname = previewInfo.Nickname,
                        Side = previewInfo.Side,
                        SavageLockTime = currentPlayer.Info.SavageLockTime,
                        SavageNickname = currentPlayer.Info.Nickname,
                        GameVersion = currentPlayer.Info.GameVersion,
                        HasCoopExtension = currentPlayer.Info.HasCoopExtension,
                        Health = previewInfo.Health
                    },
                    PlayerVisualRepresentation = playerVisualization
                });
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning($"[UI] Failed to materialize auto-join teammate '{accountId}' for matchmaker.");
                pitFireTeam.Log.LogError(ex);
                return null;
            }
        }
    }

    internal class MatchMakerPlayerPreviewFollowerUiPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MatchMakerPlayerPreview), nameof(MatchMakerPlayerPreview.Show));
        }

        [PatchPostfix]
        private static void PatchPostfix(MatchMakerPlayerPreview __instance, EFT.UI.Matchmaker.RaidPlayer player)
        {
            try
            {
                if (__instance == null || player == null)
                {
                    return;
                }

                string accountId = player?.AccountId ?? string.Empty;
                bool isTeammate = !string.IsNullOrWhiteSpace(accountId)
                    && MainMenuControllerPatch.GroupPlayers.Any(groupPlayer => groupPlayer?.AccountId == accountId);
                TextMeshProUGUI statusField = AccessTools.Field(typeof(MatchMakerPlayerPreview), "_groupStatusField")?.GetValue(__instance) as TextMeshProUGUI;
                if (statusField != null)
                {
                    statusField.gameObject.SetActive(!isTeammate);
                }

                Transform secureContainerSummary = __instance.transform.Find("FriendlyTeammateSecureContainerPreview");
                if (secureContainerSummary != null)
                {
                    secureContainerSummary.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(secureContainerSummary.gameObject);
                }
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning("[UI] Failed to update follower preview UI.");
                pitFireTeam.Log.LogError(ex);
            }
        }
    }

    /**
     * Patch to set what followers will the player start with (PMC case only)
     */
    internal class RaidStartPatch : ModulePatch
    {
        public static bool HasFika()
        {
            return Type.GetType("Fika.Core.Coop.GameMode.CoopGame, Fika.Core") != null;
        }
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.EftClientBackendSession), "SendRaidSettings");
        }
        [PatchPostfix]
        private static void PatchPostfix(EFT.EftClientBackendSession __instance, RaidSettings settings)
        {
            bool badGuy = pitFireTeam.badGuy.Value;

            Utils.SpawnHelper.spawnMemberIds.Clear();
            Utils.SpawnHelper.spawnMemberIdsScav.Clear();
            Utils.SpawnHelper.spawnMemberIdsBoss.Clear();
            // has members selected for spawn
            if (MainMenuControllerPatch.GroupPlayers != null)
            {
                foreach (var player in MainMenuControllerPatch.GroupPlayers)
                {
                    if (player.Id == "677c4e0cc7a538c4210d4d47")
                    {
                        Utils.SpawnHelper.spawnMemberIdsBoss.Add(WildSpawnType.bossKnight);
                    }
                    else if (player.Id == "677c4e0cc7a538c4210d4d48")
                    {
                        SpawnHelper.spawnMemberIdsBoss.Add(WildSpawnType.followerBigPipe);
                    }
                    else if (player.Id == "677c4e0cc7a538c4210d4d49")
                    {
                        SpawnHelper.spawnMemberIdsBoss.Add(WildSpawnType.followerBirdEye);
                    }
                    else
                    {
                        if (!settings.IsPmc)
                        {
                            if (Utils.SpawnHelper.ScavSquad) Utils.SpawnHelper.spawnMemberIdsScav.AddRange(MainMenuControllerPatch.GroupPlayers.Select(x => x.AccountId));
                        }
                        else
                            Utils.SpawnHelper.spawnMemberIds.AddRange(MainMenuControllerPatch.GroupPlayers.Select(x => x.AccountId));

                        break;
                    }
                }
            }

            // spawning with a Goon will turn bad guy flag on
            if (SpawnHelper.spawnMemberIdsBoss.Count > 0)
            {
                badGuy = true;
                Utils.Utils.FlagSet("isBadGuy", true);
            }

            Profile profile = __instance.GetProfileBySide(ESideType.Pmc);

            // see if user is to spawn with a Goon do to questing
            List<string> questCompanions = new List<string>();

            if (profile.TryGetTraderInfo(Utils.Props.KnightTrader, out var traderInfo) && !traderInfo.Disabled)
            {
                profile.QuestsData.ForEach(quest =>
                {

                    foreach (var item in Utils. Props.Quests)
                    {
                        foreach (var item1 in item.Value)
                        {
                            if (item1 == quest.Id)
                            {
                                if (quest.Status == EFT.Quests.EQuestStatus.Started)
                                {
                                    bool isGoonQuest = true;

                                    if (Utils.Props.QuestsLocations.TryGetValue(quest.Id, out List<string> locations))
                                    {
                                        isGoonQuest = false;
                                        if (locations.Contains(settings.LocationId.ToLower()))
                                        {
                                            isGoonQuest = true;
                                        }
                                    }

                                    if (Utils.SpawnHelper.spawnMemberIds.Count < 1 && isGoonQuest)
                                    {
                                        Utils.Utils.FlagSet("questGoons", true);
                                        // - when doing Goons quests, we reset any other companions
                                        SpawnHelper.spawnMemberIdsBoss.Clear();
                                        // - when doing Goons quests, we are always bad guys
                                        Utils.Utils.FlagSet("isBadGuy", true);
                                        badGuy = true;

                                        if (item.Key == "Knight")
                                        {
                                            if (!questCompanions.Contains("bossKnight"))
                                            {
                                                questCompanions.Add("bossKnight");
                                            }
                                        }
                                        else if (item.Key == "BigPipe")
                                        {
                                            if (!questCompanions.Contains("followerBigPipe"))
                                            {
                                                questCompanions.Add("followerBigPipe");
                                            }
                                        }
                                        else if (item.Key == "BirdEye")
                                        {
                                            if (!questCompanions.Contains("followerBirdEye"))
                                            {
                                                questCompanions.Add("followerBirdEye");
                                            }
                                        }

                                        break;
                                    }
                                }
                            }
                        }
                    }
                });
            }

            if (questCompanions.Count > 0)
            {
                questCompanions.ForEach(companion =>
                {
                    if (companion == "bossKnight")
                    {
                        SpawnHelper.spawnMemberIdsBoss.Add(WildSpawnType.bossKnight);
                    }
                    else if (companion == "followerBigPipe")
                    {
                        SpawnHelper.spawnMemberIdsBoss.Add(WildSpawnType.followerBigPipe);
                    }
                    else if (companion == "followerBirdEye")
                    {
                        SpawnHelper.spawnMemberIdsBoss.Add(WildSpawnType.followerBirdEye);
                    }

                });
            }

            // patch raid settings to that we can change the settings without restarting the game
            var converterClass = typeof(AbstractGame).Assembly.GetTypes()
                .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);

            var _defaultJsonConverters = Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;

            /* string pitConfig = RequestHandler.PostJson("/client/raid/pitconfig", new
            {
                Config = new Dictionary<string, object>
                {
                    { "pitFireTeam", pitFireTeam.pitFireTeamFLAG.Value },
                    { "badGuy", badGuy },
                    { "pmcArmbands", pitFireTeam.pmcArmbands.Value },
                    { "englishBear", pitFireTeam.englishBear.Value },
                    { "location", settings.LocationId }
                }

            }.ToJson(_defaultJsonConverters));

            PitConfig config = Json.Deserialize<PitConfig>(pitConfig);

            Utils.SpawnHelper.ScavSquad = config.ScavSquad;
            Utils.SpawnHelper.ScavSquadSize = config.ScavSquadSize;
            Utils.SpawnHelper.Pickups = config.Pickups;
            Utils.SpawnHelper.Restrictions = config.Restrictions;

            // - when restrictions are enabled, maxium scav squad size will be determined based on fence standing
            if (config.Restrictions)
            {
                double fenceStanding = profile.FenceInfo.Standing;
                int minScavSize = 1;
                int maxScavSize = 10;
                int inputMaxScavSize = Utils.SpawnHelper.ScavSquadSize;
                double minStanding = 1.0;
                double maxStanding = 6.0;

                if (fenceStanding < minStanding) Utils.SpawnHelper.ScavSquadSize = 0;
                else
                {
                    double standingRange = maxStanding - minStanding;
                    double scavSizeRange = maxScavSize - minScavSize;
                    double standingRatio = (fenceStanding - minStanding) / standingRange;
                    double scavSize = minScavSize + (standingRatio * scavSizeRange);

                    int scavSquadSize = (int)Math.Round(scavSize, 0);
                    if (scavSquadSize > inputMaxScavSize)
                    {
                        Utils.SpawnHelper.ScavSquadSize = inputMaxScavSize;
                    }
                    else
                    {
                        Utils.SpawnHelper.ScavSquadSize = inputMaxScavSize;
                    }
                }
            }
 */
            //if (Utils.SpawnHelper.ScavSquadSize < 1) Utils.SpawnHelper.ScavSquad = false;

            if (pitFireTeam.badGuy.Value) Utils.Utils.FlagSet("isBadGuy", true);
            if (pitFireTeam.pitFireTeamFLAG.Value) Utils.Utils.FlagSet("pitFireTeam", true);
        }
    }
    /**
     * Ensure the game does not see player having a group which would switch the game mode to Online - position #1
     */
    internal class MainMenuControllerPatch : ModulePatch
    {
        public static playerGroup GroupPlayers = new playerGroup();
        public static readonly playerGroup TransitPlayers = new playerGroup();

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.MainMenuShowOperation), "method_49");
        }

        [PatchPrefix]
        private static void PatchPrefix(EFT.MainMenuShowOperation __instance)
        {
        }
    }
    /**
     * Stay in sync with what bots the player adds to the raid group
     */
    internal class MatchmakerPlayerControllerClassAddMemberPatch : ModulePatch
    {
        private const string TeammatesRoute = "/singleplayer/pitfireteam/teammates";
        private static readonly HashSet<string> TeammateAccountIds = new HashSet<string>(StringComparer.Ordinal);
        private static float _nextRefreshTime;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.UI.Matchmaker.BaseMatchmakerController<RaidSettings>), nameof(EFT.UI.Matchmaker.BaseMatchmakerController<RaidSettings>.AddPlayerToGroup));
        }

        [PatchPostfix]
        private static void PatchPostfix(EFT.UI.Matchmaker.MatchmakerPlayersController __instance, EFT.UI.Matchmaker.RaidPlayer player)
        {
            NormalizeTeammateIconCategory(player);
            EnsureTeammateVisualHealth(__instance, player);
            TeammateAutoJoinRuntime.ClearSuppression(player?.AccountId);
            if (__instance.CurrentPlayer != player &&
                MainMenuControllerPatch.GroupPlayers.All(x => x.AccountId != player.AccountId))
            {
                MainMenuControllerPatch.GroupPlayers.Add(player);
            }
        }

        private static void NormalizeTeammateIconCategory(EFT.UI.Matchmaker.RaidPlayer player)
        {
            if (player == null || string.IsNullOrWhiteSpace(player.AccountId))
            {
                return;
            }

            RefreshTeammateCacheIfNeeded();
            if (!TeammateAccountIds.Contains(player.AccountId))
            {
                return;
            }

            if (player.Info != null)
            {
                player.Info.MemberCategory = EMemberCategory.Unheard;
                player.Info.SelectedMemberCategory = EMemberCategory.Unheard;
            }

            if (player.PlayerVisualRepresentation?.Info != null)
            {
                player.PlayerVisualRepresentation.Info.MemberCategory = EMemberCategory.Unheard;
                player.PlayerVisualRepresentation.Info.SelectedMemberCategory = EMemberCategory.Unheard;
            }
        }

        private static void EnsureTeammateVisualHealth(EFT.UI.Matchmaker.MatchmakerPlayersController controller, EFT.UI.Matchmaker.RaidPlayer player)
        {
            if (controller?.CurrentPlayer?.Info?.Health == null || player?.PlayerVisualRepresentation?.Info == null)
            {
                return;
            }

            RefreshTeammateCacheIfNeeded();
            if (!TeammateAccountIds.Contains(player.AccountId))
            {
                return;
            }

            SyntheticTeammateVisualHealth.Ensure(player, controller.CurrentPlayer.Info.Health);
        }

        private static void RefreshTeammateCacheIfNeeded()
        {
            if (Time.time < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.time + 5f;

            try
            {
                string response = RequestHandler.GetJson(TeammatesRoute);
                if (string.IsNullOrWhiteSpace(response))
                {
                    return;
                }

                JToken root = JToken.Parse(response);
                JToken dataToken = root.Type == JTokenType.Array ? root : root["data"];
                if (dataToken is not JArray teammates)
                {
                    return;
                }

                TeammateAccountIds.Clear();
                foreach (JToken teammate in teammates)
                {
                    string? accountId = teammate?["Aid"]?.ToString() ?? teammate?["aid"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(accountId))
                    {
                        TeammateAccountIds.Add(accountId);
                    }
                }
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning("[UI] Failed to refresh teammate cache for matchmaker icon normalization.");
                pitFireTeam.Log.LogError(ex);
            }
        }
    }
    /**
     * Clear the raid group when the player disbands the orignal group
     */
    internal class MatchmakerPlayerControllerClassDisbandGroupPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.UI.Matchmaker.BaseMatchmakerController<RaidSettings>), nameof(EFT.UI.Matchmaker.BaseMatchmakerController<RaidSettings>.RemoveGroup));
        }

        [PatchPrefix]
        private static void PatchPrefix(EFT.UI.Matchmaker.MatchmakerPlayersController __instance, bool revertSettings = true)
        {
            MainMenuControllerPatch.GroupPlayers.Clear();
        }
    }

    /**
     * Keep the stock ready-screen gate on the local branch by hiding synthetic teammates
     * from the temporary stock group-count check, then restoring them immediately after.
     */
    internal class MainMenuControllerReadyScreenGatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.MainMenuShowOperation), "method_52");
        }

        [PatchPrefix]
        private static void PatchPrefix(EFT.MainMenuShowOperation __instance)
        {
            if (Modules.SquadSideSelectionFlow.SquadModeActive)
            {
                Modules.SquadSideSelectionFlow.Deactivate("play-ready-screen");
            }

            RaidSettings raidSettings = __instance.raidSettings_0;
            if (raidSettings == null || MainMenuControllerPatch.GroupPlayers.Count < 1)
            {
                return;
            }

            raidSettings.RaidMode = ERaidMode.Local;
        }
    }
    /**
     * Clear the raid group when the player aborts the matchmaking
     */
    internal class MatchmakerPlayerControllerClassAbortPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.UI.Matchmaker.BaseMatchmakerController<RaidSettings>), "MatchingAbort");
        }

        [PatchPrefix]
        private static void PatchPrefix(EFT.UI.Matchmaker.MatchmakerPlayersController __instance)
        {
            MainMenuControllerPatch.GroupPlayers.Clear();
        }
    }
    /**
     * When a player is removed from the original group, remove them from the raid group as well
     */
    internal class ContextInteractionsPlayerRemovePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.UI.Matchmaker.RaidGroupContextInteractions), nameof(EFT.UI.Matchmaker.RaidGroupContextInteractions.RemovePlayer));
        }

        [PatchPrefix]
        private static bool PatchPrefix(EFT.UI.Matchmaker.RaidGroupContextInteractions __instance)
        {
            string id = __instance._selectedPlayer.AccountId;
            if (string.IsNullOrWhiteSpace(id))
            {
                return true;
            }

            bool isFriendlyTeammate = MainMenuControllerPatch.GroupPlayers.Any(player => player?.AccountId == id);
            if (!isFriendlyTeammate)
            {
                return true;
            }

            TeammateAutoJoinRuntime.MarkSuppressed(id);
            MainMenuControllerPatch.GroupPlayers.RemoveFirst(x => x?.AccountId == id);

            EFT.UI.Matchmaker.IMatchmakerController controller = __instance._matchmakerPlayersController;
            if (controller?.GroupPlayers == null || controller.GroupPlayers.All(player => player?.AccountId != id))
            {
                return true;
            }

            controller.GroupPlayers.RemoveFirst(player => player?.AccountId == id);
            if (controller.GroupPlayers.Count <= 1)
            {
                controller.Group?.RemoveOwner();
            }

            EFT.UI.BaseContextInteractions.RequestGlobalRedraw();
            return false;
        }
    }

    /** Ensure raid loading screen reflects the correct number of players based on raid group instead of original group **/
    internal class MatchmakerTimeHasComeShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MatchmakerTimeHasCome), "Show", new Type[]
            {
                typeof(EFT.IEftSession),
                typeof(RaidSettings),
                typeof(EFT.UI.Matchmaker.MatchmakerPlayersController)
            });
        }
        [PatchPrefix]
        private static void PatchPrefix(MatchmakerTimeHasCome __instance, EFT.IEftSession session, RaidSettings raidSettings, EFT.UI.Matchmaker.MatchmakerPlayersController matchmaker)
        {
            if (!raidSettings.IsPmc) MainMenuControllerPatch.GroupPlayers.Clear();
            SyntheticTeammateAutoJoinLoader.RefreshLoadedTeammateVisuals(matchmaker);

            if (matchmaker?.GroupPlayers?.List == null)
            {
                return;
            }

            List<EFT.UI.Matchmaker.RaidPlayer> raidGroup = matchmaker.GroupPlayers.List;
            EFT.UI.Matchmaker.RaidPlayer currentPlayer = matchmaker.CurrentPlayer;
            if (currentPlayer != null)
            {
                int currentIndex = raidGroup.FindIndex(x => x?.AccountId == currentPlayer.AccountId);
                if (currentIndex < 0)
                {
                    raidGroup.Insert(0, currentPlayer);
                }
                else if (currentIndex > 0)
                {
                    raidGroup.RemoveAt(currentIndex);
                    raidGroup.Insert(0, currentPlayer);
                }
            }

            Profile.HealthInfo currentHealth = matchmaker.CurrentPlayer?.Info?.Health;

            try
            {
                foreach (var item in MainMenuControllerPatch.GroupPlayers)
                {
                    if (currentHealth != null)
                    {
                        SyntheticTeammateVisualHealth.Ensure(item, currentHealth);
                    }

                    try
                    {
                        if (raidGroup.All(x => x.AccountId != item.AccountId))
                        {
                            raidGroup.Add(item);
                        }
                    }
                    catch (Exception ex)
                    {
                        pitFireTeam.Log.LogWarning($"[Raid] Failed to add teammate {item?.AccountId} to raid group on MatchmakerTimeHasComeShow");
                        pitFireTeam.Log.LogError(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning("[Raid] Failed to inject teammates into MatchmakerTimeHasCome");
                pitFireTeam.Log.LogError(ex);
                return;
            }

            if (MainMenuControllerPatch.TransitPlayers.Count > 0)
            {
                try
                {
                    foreach (var item in MainMenuControllerPatch.TransitPlayers)
                    {
                        var player = raidGroup.FirstOrDefault(x => x.AccountId == item.AccountId);
                        if (player == null)
                        {
                            if (currentHealth != null)
                            {
                                SyntheticTeammateVisualHealth.Ensure(item, currentHealth);
                            }

                            try
                            {
                                raidGroup.Add(item);
                            }
                            catch (Exception ex)
                            {
                                pitFireTeam.Log.LogWarning($"[Raid] Failed to add transit player {item?.AccountId} to raid group");
                                pitFireTeam.Log.LogError(ex);
                            }
                        }
                    }

                    MainMenuControllerPatch.TransitPlayers.Clear();
                }
                catch (Exception ex)
                {
                    pitFireTeam.Log.LogWarning("[Raid] Failed to process transit players on MatchmakerTimeHasCome");
                    pitFireTeam.Log.LogError(ex);
                }
            }
        }
    }

    internal class PartyInfoPanelEquipmentHealthPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(PartyInfoPanel), nameof(PartyInfoPanel.CG_method_3));
        }

        [PatchPrefix]
        private static void PatchPrefix(PartyInfoPanel __instance, EFT.UI.Matchmaker.RaidPlayer raidPlayer)
        {
            try
            {
                if (raidPlayer == null)
                {
                    return;
                }

                Profile currentProfile = AccessTools.Field(typeof(PartyInfoPanel), "_profile").GetValue(__instance) as Profile;
                Profile.HealthInfo referenceHealth = currentProfile?.Health;
                if (referenceHealth == null)
                {
                    return;
                }

                SyntheticTeammateVisualHealth.Ensure(raidPlayer, referenceHealth);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning("[UI] Failed to normalize teammate health before showing party equipment.");
                pitFireTeam.Log.LogError(ex);
            }
        }
    }

    internal class MatchMakerAcceptScreenPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MatchMakerAcceptScreen), "Show", new Type[] { typeof(EFT.IEftSession), typeof(RaidSettings), typeof(RaidSettings) });

        }

        [PatchPrefix]
        private static void PatchPrefix(MatchMakerAcceptScreen __instance, EFT.IEftSession session, RaidSettings raidSettings, RaidSettings offlineRaidSettings)
        {
            try
            {
                EFT.UI.Matchmaker.MatchmakerPlayersController controller = AccessTools.Field(typeof(MatchMakerAcceptScreen), "MatchmakerPlayersController")?.GetValue(__instance) as EFT.UI.Matchmaker.MatchmakerPlayersController;
                SyntheticTeammateAutoJoinLoader.EnsureLoaded(controller);
                SyntheticTeammateAutoJoinLoader.RefreshLoadedTeammateVisuals(controller);

                if (!SyntheticTeammateRaidGuard.HasSyntheticTeammates())
                {
                    return;
                }

                raidSettings.RaidMode = ERaidMode.Local;

                // CRITICAL: Add teammates to controller BEFORE the game populates previews
                if (controller != null && MainMenuControllerPatch.GroupPlayers.Count > 0)
                {
                    foreach (var teammate in MainMenuControllerPatch.GroupPlayers)
                    {
                        int existingIndex = -1;
                        for (int i = 0; i < controller.GroupPlayers.Count; i++)
                        {
                            if (controller.GroupPlayers[i]?.AccountId == teammate.AccountId)
                            {
                                existingIndex = i;
                                break;
                            }
                        }

                        if (existingIndex >= 0)
                        {
                            controller.GroupPlayers[existingIndex] = teammate;
                            continue;
                        }

                        controller.GroupPlayers.Add(teammate);
                        Modules.Logger.LogInfo($"[UI] Added teammate {teammate.AccountId} to controller before preview population");
                    }
                }
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning("[UI] Failed to inject teammates in MatchMakerAcceptScreenPatch prefix");
                pitFireTeam.Log.LogError(ex);
            }
        }

        [PatchPostfix]
        private static void PatchPostfix(MatchMakerAcceptScreen __instance, EFT.IEftSession session, RaidSettings raidSettings, RaidSettings offlineRaidSettings)
        {
            try
            {
                // Teammates are injected into controller via Prefix.
                // Now rebuild the preview group to use updated controller.GroupPlayers
                EFT.UI.Matchmaker.MatchmakerPlayersController controller = AccessTools.Field(typeof(MatchMakerAcceptScreen), "MatchmakerPlayersController")?.GetValue(__instance) as EFT.UI.Matchmaker.MatchmakerPlayersController;
                MatchMakerGroupPreview groupPreview = AccessTools.Field(typeof(MatchMakerAcceptScreen), "_groupPreview").GetValue(__instance) as MatchMakerGroupPreview;
                RaidSettings raidSettings_0 = AccessTools.Field(typeof(MatchMakerAcceptScreen), "_raidSettings").GetValue(__instance) as RaidSettings;
                string string_2 = AccessTools.Field(typeof(MatchMakerAcceptScreen), "_currentProfileAid").GetValue(__instance) as string;

                if (controller == null || groupPreview == null || raidSettings_0 == null)
                {
                    return;
                }

                // Rebuild the entire group preview with updated controller.GroupPlayers
                groupPreview.Show(string_2, controller, raidSettings_0, new Func<EFT.UI.Matchmaker.RaidPlayer, bool, bool, EFT.UI.Matchmaker.RaidGroupContextInteractions>(controller.GetContextInteractions));

                Modules.Logger.LogInfo($"[UI] Rebuilt group preview with {controller.GroupPlayers.Count} players");
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning("[UI] Failed to rebuild group preview in postfix");
                pitFireTeam.Log.LogError(ex);
            }
        }
    }

    internal class TarkovApplicationOnlineFallbackPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.NetworkGameMatching), new Type[]
            {
                typeof(string),
                typeof(EMatchingType)
            });
        }

        [PatchPrefix]
        private static bool PatchPrefix(TarkovApplication __instance, ref Task __result)
        {
            if (!SyntheticTeammateRaidGuard.TryForceLocalRaid(__instance, "TarkovApplication.method_42"))
            {
                return true;
            }

            __result = SyntheticTeammateRaidGuard.StartLocalRaid(__instance);
            return false;
        }
    }

    internal class SelectSpawnPointPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            InterfaceMapping interfaceMap = typeof(EFT.Game.Spawning.SpawnSystem)
                .GetInterfaceMap(typeof(ISpawnSystem));
            MethodInfo interfaceMethod = AccessTools.Method(
                typeof(ISpawnSystem),
                nameof(ISpawnSystem.SelectSpawnPoint));
            int methodIndex = Array.IndexOf(interfaceMap.InterfaceMethods, interfaceMethod);
            return methodIndex >= 0 ? interfaceMap.TargetMethods[methodIndex] : null;
        }
        [PatchPrefix]
        private static void PatchPrefix(ref ESpawnCategory category, EPlayerSide side, string groupId, string teamId, IPlayer person, string infiltration, string profileId)
        {
            if (!pitFireTeam.spawnPoint.Value || infiltration == "Hideout") return;

            if (!string.IsNullOrEmpty(profileId))
            {
                int transitCount;
                if (EFT.TransitController.IsTransit(profileId, out transitCount))
                {
                    return;
                }
            }

            // switch to coop mode if the player has followers
            if (category == ESpawnCategory.Player && person == null)
            {
                if (SpawnHelper.spawnMemberIds.Count > 0 || SpawnHelper.spawnMemberIdsBoss.Count > 0)
                {
                    category = ESpawnCategory.Coop;
                }
            }
        }
    }
}

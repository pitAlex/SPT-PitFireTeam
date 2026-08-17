using Comfort.Common;
using EFT;
using EFT.Quests;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using SPT.Common.Utils;
using SPT.Reflection.Patching;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using BotCreator = BotCreatorClass;
using IProfileData = BotProfileDataClass;
using ProfileEndPoint = ProfileEndpointFactoryAbstractClass;
using ProfileEndPointHelper = GClass1392;
using ProfileResult = CompleteProfileDescriptorClass;
using spawnPosition = GClass682;

namespace pitTeam.Patches
{
    internal class CancelToken : GInterface22
    {
        CancellationTokenSource cancelSource;
        public CancelToken()
        {
            cancelSource = new CancellationTokenSource();
        }

        public CancellationToken GetCancelToken()
        {
            return cancelSource.Token;
        }

        public void Cancel()
        {
            cancelSource.Cancel();
        }
    }

    internal class BotsControllerPatch : ModulePatch
    {
        public static BotsControllerPatch Instance;

        public static BotsController Controller = null;

        public static List<pitAIBossPlayer> spawnedPlayers = new List<pitAIBossPlayer>();

        public static Dictionary<string, Task<Dictionary<string, Profile>>> followerCreationTask;
        public static Dictionary<string, Task<BotCreationDataClass>> alliesCreationTask;
        public static Dictionary<WildSpawnType, Task<BotCreationDataClass>> bossCreationTask;

        public BotsControllerPatch()
        {
            if (Instance == null) Instance = this;
            followerCreationTask = new Dictionary<string, Task<Dictionary<string, Profile>>>();
            alliesCreationTask = new Dictionary<string, Task<BotCreationDataClass>>();
            bossCreationTask = new Dictionary<WildSpawnType, Task<BotCreationDataClass>>();
        }

        private static List<BotDetails> DeserializeFollowerDetails(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<BotDetails>();
            }

            var token = JToken.Parse(json);
            if (token.Type == JTokenType.Array)
            {
                return token.ToObject<List<BotDetails>>() ?? new List<BotDetails>();
            }

            var response = token.ToObject<FriendlyTeammateBodyResponse<List<BotDetails>>>();
            if (response?.err != 0)
            {
                throw new InvalidOperationException(
                    $"Follower details request failed with err={response?.err}: {response?.errmsg}"
                );
            }

            return response?.data ?? new List<BotDetails>();
        }

        private AICorePoint GetClosestCorePoint(BotsController _botsController, Vector3 position)
        {
            var coversData = _botsController.CoversData;
            var groupPoint = coversData.GetClosest(position);
            return groupPoint.CorePointInGame;
        }

        private BotsGroup GetPlayerGroup(pitAIBossPlayer player, BotOwner bt, BotZone zn, int groupSize = 0)
        {
            if (player.bossGroup != null) return player.bossGroup;

            BotSpawner botSpawnerClass = Controller.BotSpawner;

            var botGame = botSpawnerClass.BotGame;

            var spawnGroups = botSpawnerClass.Groups;
            var deadBodiesController = botSpawnerClass.DeadBodiesController;
            var allPlayers = botSpawnerClass.AllPlayers;

            bool _freeForAll = true;

            WildSpawnType sptBear = WildSpawnType.pmcBEAR;
            WildSpawnType sptUsec = WildSpawnType.pmcUSEC;

            WildSpawnType roleh;
            bool sameSideFriendly = false;

            if (player.realPlayer.Side == EPlayerSide.Bear)
            {
                roleh = sptBear;
            }
            else if (player.realPlayer.Side == EPlayerSide.Usec)
            {
                roleh = sptUsec;
            }
            else
            {
                roleh = WildSpawnType.assault;
            }

            GetSameSideFriendly(roleh, player.realPlayer.Side, out sameSideFriendly);

            EPlayerSide side = player.realPlayer.Side;

            BotsGroup botsGroup;

            List<BotOwner> list = new List<BotOwner>();

            if (side != EPlayerSide.Savage)
            {
                // botsGroup take on the values of the inital bot, attempt to prevent the group from being hostile to the player
                bt.Settings.FileSettings.Mind.ENEMY_BY_GROUPS_PMC_PLAYERS = side != EPlayerSide.Savage ? false : true;
                bt.Settings.FileSettings.Mind.ENEMY_BY_GROUPS_SAVAGE_PLAYERS = side == EPlayerSide.Savage ? false : true;

                var old_reasons = bt.Settings.FileSettings.Mind.VALID_REASONS_TO_ADD_ENEMY;

                bt.Settings.FileSettings.Mind.USE_ADD_TO_ENEMY_VALIDATION = true;
                bt.Settings.FileSettings.Mind.VALID_REASONS_TO_ADD_ENEMY = new EBotEnemyCause[] { };

                bt.Settings.FileSettings.Mind.DEFAULT_SAVAGE_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;

                if (!sameSideFriendly)
                {
                    bt.Settings.FileSettings.Mind.DEFAULT_BEAR_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;
                    bt.Settings.FileSettings.Mind.DEFAULT_USEC_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;
                    var enemyTypes = bt.Settings.GetEnemyBotTypes();
                    if (!enemyTypes.Contains(WildSpawnType.pmcBEAR)) enemyTypes.Add(WildSpawnType.pmcBEAR);
                    if (!enemyTypes.Contains(WildSpawnType.pmcUSEC)) enemyTypes.Add(WildSpawnType.pmcUSEC);
                }
                else
                {
                    bt.Settings.FileSettings.Mind.DEFAULT_BEAR_BEHAVIOUR = bt.Side == EPlayerSide.Bear ? EWarnBehaviour.AlwaysFriends : EWarnBehaviour.AlwaysEnemies;
                    bt.Settings.FileSettings.Mind.DEFAULT_USEC_BEHAVIOUR = bt.Side == EPlayerSide.Usec ? EWarnBehaviour.AlwaysFriends : EWarnBehaviour.AlwaysEnemies;

                    var enemyTypes = bt.Settings.GetEnemyBotTypes();

                    if (bt.Side == EPlayerSide.Bear)
                    {
                        if (enemyTypes.Contains(WildSpawnType.pmcBEAR)) enemyTypes.Remove(WildSpawnType.pmcBEAR);
                        if (!enemyTypes.Contains(WildSpawnType.pmcUSEC)) enemyTypes.Add(WildSpawnType.pmcUSEC);
                    }
                    else
                    {
                        if (enemyTypes.Contains(WildSpawnType.pmcUSEC)) enemyTypes.Remove(WildSpawnType.pmcUSEC);
                        if (!enemyTypes.Contains(WildSpawnType.pmcBEAR)) enemyTypes.Add(WildSpawnType.pmcBEAR);
                    }
                }

                List<WildSpawnType> toremove = new List<WildSpawnType>();

                bt.Settings.GetEnemyBotTypes().ForEach(type =>
                {
                    if (Utils.Props.friendlyBotTypes.Contains(type))
                    {
                        toremove.Add(type);
                    }
                });

                toremove.ForEach(type =>
                {
                    bt.Settings.GetEnemyBotTypes().Remove(type);
                });



                foreach (BotOwner item2 in botSpawnerClass.method_5(bt))
                {
                    if (!Utils.Props.friendlyBotTypes.Contains(item2.Profile.Info.Settings.Role))
                        list.Add(item2);
                }

                botsGroup = new BotsGroupPlayer(zn, botGame, bt, list, deadBodiesController, allPlayers, player);

                if (groupSize != 0) botsGroup.TargetMembersCount = groupSize;

                if (_freeForAll)
                {
                    spawnGroups.AddNoKey(botsGroup, zn);
                }
                else
                {
                    spawnGroups.Add(zn, side, botsGroup, false);
                }

                BossPlayers.AddGroupToBoss(player, botsGroup);

                // revert changes
                bt.Settings.FileSettings.Mind.USE_ADD_TO_ENEMY_VALIDATION = false;
                bt.Settings.FileSettings.Mind.VALID_REASONS_TO_ADD_ENEMY = old_reasons;

            }
            else
            {
                foreach (BotOwner item2 in botSpawnerClass.method_5(bt))
                {
                    list.Add(item2);
                }

                botsGroup = new BotsGroupPlayer(zn, botGame, bt, list, deadBodiesController, allPlayers, player);
                if (groupSize != 0) botsGroup.TargetMembersCount = groupSize;

                if (_freeForAll)
                {
                    spawnGroups.AddNoKey(botsGroup, zn);
                }
                else
                {
                    spawnGroups.Add(zn, side, botsGroup, false);
                }

                BossPlayers.AddGroupToBoss(player, botsGroup);
            }

            return botsGroup;
        }

        private void GetSameSideFriendly(WildSpawnType role, EPlayerSide side, out bool isFriends)
        {
            isFriends = true;

            if (Utils.Utils.FlagGet("isBadGuy"))
            {
                isFriends = false;
                return;
            }
        }

        private async Task<bool> ActivateBotAtPosition(BotCreator botCreator, Profile profile, spawnPosition position, BotZone zone, bool shallBeGroup, Func<BotOwner, BotZone, BotsGroup> GroupAction, Action<BotOwner> OnActivate, CancellationToken token)
        {

            try
            {
                await botCreator.ActivateBot(profile, position, zone, false, GroupAction, OnActivate, token);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Failed to activate bot at debug position");
                Modules.Logger.LogError(ex);

                return false;
            }

            await Task.Yield();

            return true;
        }

        private static int ScoreDebugSpawnProfile(Profile candidate, EPlayerSide expectedSide, WildSpawnType expectedRole)
        {
            if (candidate == null) return int.MinValue;

            int score = 0;
            if (candidate.Info != null)
            {
                if (candidate.Info.Side == expectedSide) score += 1000;
                if (candidate.Info.Settings != null)
                {
                    if (candidate.Info.Settings.Role == expectedRole) score += 1000;
                    if (candidate.Info.Settings.BotDifficulty == BotDifficulty.hard) score += 150;
                    if (candidate.Info.Settings.BotDifficulty == BotDifficulty.normal) score += 75;
                }

                score += Mathf.Clamp(candidate.Info.Level, 0, 100);
            }

            return score;
        }

        private static Profile PickBestDebugSpawnProfile(Profile[] loadedProfiles, EPlayerSide expectedSide, WildSpawnType expectedRole, string logPrefix)
        {
            if (loadedProfiles == null || loadedProfiles.Length == 0)
            {
                return null;
            }

            Profile best = null;
            int bestScore = int.MinValue;
            foreach (Profile p in loadedProfiles)
            {
                if (p == null) continue;
                int score = ScoreDebugSpawnProfile(p, expectedSide, expectedRole);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }

            if (best != null)
            {
                Modules.Logger.LogInfo(
                    $"{logPrefix} selected profile: nick={best.Nickname}, side={best.Info?.Side}, " +
                    $"role={best.Info?.Settings?.Role}, diff={best.Info?.Settings?.BotDifficulty}, lvl={best.Info?.Level}, score={bestScore}");
            }

            return best;
        }

        private static async Task<Profile> GenerateDebugSpawnProfile(
            BotCreator botCreator,
            IProfileData profileData,
            EPlayerSide expectedSide,
            WildSpawnType expectedRole,
            bool multiPickPmc,
            CancelToken token,
            string logPrefix)
        {
            BotCreationDataClass generationData = BotCreationDataClass.CreateWithoutProfile(profileData);

            object profileCreator = AccessTools.Field(typeof(BotCreator), "Ginterface21_0")?.GetValue(botCreator);
            BotsPresets presets = profileCreator as BotsPresets;
            IBackEndSession session = null;
            if (presets?.ISession != null)
            {
                session = presets.ISession as IBackEndSession;
            }

            if (session != null)
            {
                int requestCount = multiPickPmc ? 4 : 1;
                List<WaveInfoClass> waves = profileData.PrepareToLoadBackend(requestCount)?.ToList() ?? new List<WaveInfoClass>();
                if (waves.Count > 0 && presets != null)
                {
                    List<WaveInfoClass> delayed;
                    waves = presets.method_3(waves, out delayed);
                }

                Profile[] loadedProfiles = await session.LoadBots(waves);
                Profile loadedProfile = PickBestDebugSpawnProfile(loadedProfiles, expectedSide, expectedRole, logPrefix);
                if (loadedProfile != null)
                {
                    await Singleton<PoolManagerClass>.Instance.LoadBundlesAndCreatePools(
                        PoolManagerClass.PoolsCategory.Raid,
                        PoolManagerClass.AssemblyType.Local,
                        loadedProfile.GetAllPrefabPaths(false).ToArray<ResourceKey>(),
                        JobPriorityClass.General,
                        null,
                        PoolManagerClass.DefaultCancellationToken
                    );

                    generationData.AddProfile(loadedProfile);
                }
            }
            else
            {
                Profile generatedProfile = await botCreator.GenerateProfile(generationData, token.GetCancelToken(), true);
                if (generatedProfile != null)
                {
                    generationData.AddProfile(generatedProfile);
                }
            }

            return generationData?.Profiles?.FirstOrDefault(p => p != null);
        }

        /** Fetch Follower Profile data along with custom appearance from server */
        private async Task<Profile> FetchMemberProfile(string aid, Profile boss, BotCreator botCreator, EPlayerSide side, WildSpawnType role, BotSpawnParams spawnParams)
        {
            if (FollowerTransitStateCache.TryConsumeProfile(aid, role, out Profile transitProfile))
            {
                await LoadFollowerProfileBundles(transitProfile);
                return transitProfile;
            }

            IProfileData data = new IProfileData(side, role, BotDifficulty.hard, 0f, spawnParams);

            Dictionary<string, dynamic> customization = new Dictionary<string, dynamic>();

            // send health multiplier
            customization["Health"] = pitFireTeam.heatlhMultiplier.Value;
            customization["English"] = pitFireTeam.englishBear.Value;

            var botPresets = AccessTools.Field(typeof(BotCreator), "Ginterface21_0").GetValue(botCreator) as BotsPresets;
            var profileEndpoint = AccessTools.Field(typeof(BotsPresets), "ISession").GetValue(botPresets) as ProfileEndPoint;
            var gclass1200_0 = AccessTools.Field(typeof(ProfileEndPoint), "Gclass1392_0").GetValue(profileEndpoint) as ProfileEndPointHelper;


            List<WaveInfoClass> limit = GClass378.OptimizeBotWaves(data.PrepareToLoadBackend(1).ToList(), out var list3);

            // call backend - follow ProfileEndpointFactoryAbstractClass.LoadBots
            ProfileResult[] result;

            string scavId = null;

            if (side == EPlayerSide.Savage)
            {
                scavId = aid;
                aid = null;
            }

            try
            {
                result = await profileEndpoint.method_3<ProfileResult[]>(new LegacyParamsStruct
                {
                    Url = gclass1200_0.Main + "/client/game/bot/followergenerate",
                    Params = new Dictionary<string, object>
                    {
                        { "Info",  new Class19<List<WaveInfoClass>>(limit) },
                        { "MemberId", aid },
                        { "ScavId", scavId},
                        { "Custom", customization }
                    },
                    Retries = new byte?(LegacyParamsStruct.DefaultRetries)
                });

                Modules.Logger.LogInfo("Follower Profile data received from backend");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Failed to fetch member profile");
                Modules.Logger.LogError(ex);
                return null;
            }

            Profile profile = result.Select(new Func<ProfileResult, Profile>(ProfileEndpointFactoryAbstractClass.Class1550.class1550_0.method_10)).ToList<Profile>().Random();
            // process backend result
            await LoadFollowerProfileBundles(profile);

            Modules.Logger.LogInfo("Generated Follower Profile " + profile.Nickname + " with level " + profile.Info.Level);

            return profile;
        }

        private static Task LoadFollowerProfileBundles(Profile profile)
        {
            return Singleton<PoolManagerClass>.Instance.LoadBundlesAndCreatePools(
                PoolManagerClass.PoolsCategory.Raid,
                PoolManagerClass.AssemblyType.Local,
                profile.GetAllPrefabPaths(false).ToArray<ResourceKey>(),
                JobPriorityClass.General,
                null,
                PoolManagerClass.DefaultCancellationToken);
        }
        /** 
         * Task for creating Follower Profiles along with applying custom equipment (if specified) to them 
         * **/
        private async Task<Dictionary<string, Profile>> CreateProfilesJob(pitAIBossPlayer player)
        {
            ConcurrentDictionary<string, Profile> profiles = new ConcurrentDictionary<string, Profile>();

            var botSpawnerClass = Controller.BotSpawner;
            var botCreator = botSpawnerClass.BotCreator as BotCreator;

            EPlayerSide side = player.realPlayer.Side;
            Vector3 position = player.Position;

            WildSpawnType sptBear = WildSpawnType.pmcBEAR;
            WildSpawnType sptUsec = WildSpawnType.pmcUSEC;

            WildSpawnType type;
            if (side == EPlayerSide.Bear)
            {
                type = sptBear;
            }
            else
            {
                type = sptUsec;
            }

            int memberCount = SpawnHelper.spawnMemberIds.Count;

            BotSpawnParams @params = new BotSpawnParams();
            @params.ShallBeGroup = new ShallBeGroupParams(true, false, memberCount + 1);

            Profile playerProfile = player.realPlayer.Profile;

            List<Task> profileTasks = new List<Task>();

            if (SpawnHelper.spawnMemberIds.Count > 0)
            {
                foreach (var aid in SpawnHelper.spawnMemberIds)
                {
                    string id = aid.ToString();
                    profileTasks.Add(FetchMemberProfile(id, playerProfile, botCreator, side, type, @params).ContinueWith(dt =>
                    {
                        if (dt == null) return;

                        if (dt.Status == TaskStatus.RanToCompletion && dt.Result != null)
                        {
                            profiles.TryAdd(id, dt.Result);
                        }
                        else
                        {
                            Modules.Logger.LogError($"Failed to fetch profile for AID: {id}. Task status: {dt.Status}, Exception: {dt.Exception?.ToString()}");
                        }
                    }));
                }
            }

            await Task.WhenAll(profileTasks);

            // followers should use the same groupID as the player
            foreach (var item in profiles)
            {
                Profile profile = item.Value;
                profile.Info.GroupId = player.realPlayer.GroupId;
                profile.Info.TeamId = player.realPlayer.Profile.Info.TeamId;
            }

            Modules.Logger.LogInfo("Return follower profile data");

            return profiles.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        }
        /** Promise of Follower Profiles that will be cached for faster use **/
        public Task<Dictionary<string, Profile>> CreateFollowerProfiles(pitAIBossPlayer player)
        {
            if (Controller == null) return null;
            if (followerCreationTask.ContainsKey(player.realPlayer.ProfileId))
            {
                return followerCreationTask[player.realPlayer.ProfileId];
            }

            followerCreationTask[player.realPlayer.ProfileId] = CreateProfilesJob(player);


            return followerCreationTask[player.realPlayer.ProfileId];
        }

        public Task<BotCreationDataClass> PreFetchBossProfiles(pitAIBossPlayer player, WildSpawnType? type = null)
        {
            if (Controller == null) return null;

            EPlayerSide side = player.Player().Side;

            var botSpawnerClass = Controller.BotSpawner;

            BotCreator botCreator = botSpawnerClass.BotCreator as BotCreator;

            WildSpawnType[] bosses = new WildSpawnType[] { WildSpawnType.bossKnight, WildSpawnType.followerBigPipe, WildSpawnType.followerBirdEye };

            BotSpawnParams @params = new BotSpawnParams();
            @params.ShallBeGroup = new ShallBeGroupParams(true, false, 4);

            if (type.HasValue)
            {
                IProfileData botData = new IProfileData(side, type.Value, BotDifficulty.normal, 5f, @params);
                BotCreationDataClass botCreation = BotCreationDataClass.CreateWithoutProfile(botData);

                return FetchMemberProfile(null, player.realPlayer.Profile, botCreator, side, type.Value, @params).ContinueWith(t =>
                {
                    botCreation.AddProfile(t.Result);
                    return botCreation;
                });
            }


            foreach (var boss in bosses)
            {
                IProfileData botData = new IProfileData(side, boss, BotDifficulty.normal, 0f, @params);
                BotCreationDataClass botCreation = BotCreationDataClass.CreateWithoutProfile(botData);

                bossCreationTask[boss] = FetchMemberProfile(null, player.realPlayer.Profile, botCreator, side, boss, @params).ContinueWith(t =>
                {
                    botCreation.AddProfile(t.Result);
                    return botCreation;
                });
            }

            return null;

        }

        private Task<BotCreationDataClass> GetBossProfile(pitAIBossPlayer player, WildSpawnType boss)
        {
            if (bossCreationTask.ContainsKey(boss))
            {
                return bossCreationTask[boss];
            }

            return PreFetchBossProfiles(player, boss);
        }

        public void PreFetchScavProfiles(pitAIBossPlayer player)
        {
            if (Controller == null) return;

            var botSpawnerClass = Controller.BotSpawner;
            BotCreator botCreator = botSpawnerClass.BotCreator as BotCreator;

            int memberCount = Utils.SpawnHelper.spawnMemberIdsScav.Count > 0 ? Utils.SpawnHelper.spawnMemberIdsScav.Count : Utils.SpawnHelper.ScavSquadSize;


            BotSpawnParams @params = new BotSpawnParams();
            @params.ShallBeGroup = new ShallBeGroupParams(true, false, memberCount + 1);

            IProfileData data = new IProfileData(EPlayerSide.Savage, WildSpawnType.assault, BotDifficulty.hard, 5f, @params);

            BotCreationDataClass botCreation = BotCreationDataClass.CreateWithoutProfile(data);

            List<Task<Profile>> tasks = new List<Task<Profile>>();

            for (int i = 0; i < memberCount; i++)
            {
                string aid = null;
                try
                {
                    if (Utils.SpawnHelper.spawnMemberIdsScav.Count > 0)
                    {
                        aid = Utils.SpawnHelper.spawnMemberIdsScav[i];
                    }
                }
                catch
                {
                }
                tasks.Add(FetchMemberProfile(aid, player.realPlayer.Profile, botCreator, EPlayerSide.Savage, WildSpawnType.assault, @params));
            }

            alliesCreationTask[player.realPlayer.ProfileId] = Task.WhenAll(tasks).ContinueWith((task) =>
            {
                foreach (var item in task.Result)
                {
                    botCreation.AddProfile(item);
                }

                return botCreation;
            });
        }

        public Task<BotCreationDataClass> GetScavProfiles(pitAIBossPlayer player)
        {
            if (alliesCreationTask.ContainsKey(player.realPlayer.ProfileId))
            {
                return alliesCreationTask[player.realPlayer.ProfileId];
            }
            else
            {
                PreFetchScavProfiles(player);
                return alliesCreationTask[player.realPlayer.ProfileId];
            }
        }

        private static bool HasFika()
        {
            return Type.GetType("Fika.Core.Coop.GameMode.CoopGame, Fika.Core") != null;
        }

        public async Task SpawnBossFollower(pitAIBossPlayer player, WildSpawnType boss = WildSpawnType.bossKnight, CancelToken cancelToken = null)
        {
            try
            {
                float dist;

                CancelToken token = cancelToken != null ? cancelToken : new CancelToken();

                BotSpawner botSpawnerClass = Controller.BotSpawner;
                BotCreator botCreator = botSpawnerClass.BotCreator as BotCreator;

                Vector3 position = player.Position;
                EPlayerSide side = player.Player().Side;


                BotZone zone = botSpawnerClass.GetClosestZone(position, out dist);

                BotSpawnParams @params = new BotSpawnParams();
                @params.ShallBeGroup = new ShallBeGroupParams(true, false, 4);

                IProfileData botData = new IProfileData(side, boss, BotDifficulty.hard, 0f, @params);
                List<IProfileData> bossFollowers = new List<IProfileData> { };

                BotCreationDataClass bossAlly = null;

                foreach (var type in Utils.Props.BossFollowersType)
                {
                    if (type == boss)
                    {
                        var ally = await GetBossProfile(player, type);
                        if (bossAlly == null) bossAlly = ally;
                        else bossAlly.AddProfiles(ally.Profiles);
                        break;
                    }

                }

                if (SpawnHelper.spawnMemberIdsBoss.Contains(WildSpawnType.bossKnight))
                {
                    if (bossAlly.Profiles.Find(prf => prf.Info.Settings.Role == WildSpawnType.bossKnight) == null)
                    {
                        var knight = await GetBossProfile(player, WildSpawnType.bossKnight);
                        if (bossAlly == null) bossAlly = knight;
                        else bossAlly.AddProfiles(knight.Profiles);
                    }
                }

                if (SpawnHelper.spawnMemberIdsBoss.Contains(WildSpawnType.followerBigPipe))
                {
                    if (bossAlly.Profiles.Find(prf => prf.Info.Settings.Role == WildSpawnType.followerBigPipe) == null)
                    {
                        var bigPipe = await GetBossProfile(player, WildSpawnType.followerBigPipe);
                        if (bossAlly == null) bossAlly = bigPipe;
                        else bossAlly.AddProfiles(bigPipe.Profiles);
                    }
                }

                if (SpawnHelper.spawnMemberIdsBoss.Contains(WildSpawnType.followerBirdEye))
                {
                    if (bossAlly.Profiles.Find(prf => prf.Info.Settings.Role == WildSpawnType.followerBirdEye) == null)
                    {
                        var birdEye = await GetBossProfile(player, WildSpawnType.followerBirdEye);
                        if (bossAlly == null) bossAlly = birdEye;
                        else bossAlly.AddProfiles(birdEye.Profiles);
                    }
                }

                if (bossAlly == null) return;

                var closestCorePoint = GetClosestCorePoint(Controller, position);
                bossAlly.AddPosition(position, closestCorePoint.Id);


                bossAlly.Profiles.ForEach(async profile =>
                {

                    InteractableObjects.StoreEquipment(profile);

                    if (!FollowerTransitStateCache.IsTransitSpawnProfile(profile.ProfileId))
                    {
                        // normalize boss followers health
                        foreach (EBodyPart part in Enum.GetValues(typeof(EBodyPart)))
                        {
                            profile.Health.BodyParts.TryGetValue(part, out var bodyPart);
                            if (bodyPart != null)
                            {
                                float adjuster = 1f;
                                switch (part)
                                {
                                    case EBodyPart.Head:
                                        adjuster = 1.5f;
                                        break;
                                    case EBodyPart.RightArm:
                                    case EBodyPart.LeftArm:
                                        adjuster = 1.3f;
                                        break;
                                    case EBodyPart.RightLeg:
                                    case EBodyPart.LeftLeg:
                                        adjuster = 1.7f;
                                        break;

                                    default:
                                        adjuster = 1f;
                                        break;
                                }

                                bodyPart.Health.Minimum = Mathf.Round(adjuster * bodyPart.Health.Minimum);
                                bodyPart.Health.Maximum = Mathf.Round(adjuster * bodyPart.Health.Maximum);
                                bodyPart.Health.Current = Mathf.Round(adjuster * bodyPart.Health.Current);
                            }
                        }
                    }
                    else
                    {
                        Modules.Logger.LogInfo($"[Transit] Preserving carried health for boss follower '{profile.Nickname ?? profile.ProfileId}'.");
                    }

                    WildSpawnType botRole = profile.Info.Settings.Role;

                    profile.Info.Side = side;
                    profile.Info.GroupId = player.realPlayer.GroupId;
                    profile.Info.TeamId = player.Player().Profile.Info.TeamId;

                    Stopwatch stopWatch = new Stopwatch();
                    stopWatch.Start();

                    Action<BotOwner> OnActivate = new Action<BotOwner>((BotOwner owner) =>
                    {
                        Action<BotOwner> OnBotState = new Action<BotOwner>((BotOwner me) =>
                        {
                            BotOwnerManualUpdatePatch.BotOwnerUpdate.Remove(me.ProfileId); // clear watcher

                            try
                            {
                                // prevent attack of player on spawn
                                me.Memory.DeleteInfoAboutEnemy(player.Player());

                                if (!FollowerTransitStateCache.IsTransitSpawnProfile(me.ProfileId))
                                {
                                    me.GetPlayer.ActiveHealthController.RestoreFullHealth(); // ensure bot has full health
                                }
                                else
                                {
                                    Modules.Logger.LogInfo($"[Transit] Preserved carried health for '{me.Profile?.Nickname ?? me.ProfileId}'.");
                                }

                                me.Memory.IsPeace = true;

                                // force player side on the bot
                                if (me.Side != side)
                                {
                                    me.GetPlayer.Profile.Info.Side = side;
                                }

                                if (!me.IsRole(botRole))
                                {
                                    me.GetPlayer.Profile.Info.Settings.Role = botRole;
                                }

                                // our Pipe needs the same boss logic as knight due to their shared fighting logic
                                if (me.IsRole(WildSpawnType.followerBigPipe))
                                {
                                    if (me.Boss != null)
                                    {
                                        if (me.Boss.BossLogic != null)
                                            me.Boss.BossLogic.Dispose();

                                        me.Boss.BossLogic = new GClass440(me, me.Boss);
                                        me.Boss.NeedProtection = false;
                                    }

                                }
                                // make bot boss a follower
                                BossPlayers.AddFollower(me, player, false, botRole);
                                me.BotTalk.SetSilence(0f);
                                Utils.Utils.SetTimeout(() =>
                                {
                                    if (player.Followers.Count > 0)
                                    {
                                        player.Followers.Random().BotTalk.TrySay(EPhraseTrigger.MumblePhrase);
                                    }
                                }, 1500);
                            }
                            catch (Exception ex)
                            {
                                Modules.Logger.LogError("Failed to add " + me.Profile.Nickname + " as ally");
                                Modules.Logger.LogError(ex);
                                //task.SetResult(false);
                            }
                        });

                        BotOwnerManualUpdatePatch.BotOwnerUpdate.Add(owner.ProfileId, OnBotState);

                        // force player side on the bot
                        if (owner.Side != side)
                        {
                            owner.GetPlayer.Profile.Info.Side = side;
                        }

                        botSpawnerClass.method_11(owner, bossAlly, new Action<BotOwner>((BotOwner follower) =>
                        {
                            Modules.Logger.LogInfo("Ally " + follower.Profile.Nickname + " spawned");

                        }), true, stopWatch);

                    });

                    Func<BotOwner, BotZone, BotsGroup> GroupAction = new Func<BotOwner, BotZone, BotsGroup>((BotOwner bt, BotZone zn) =>
                    {
                        return GetPlayerGroup(player, bt, zn, bossAlly.Count);
                    });
                    // activate the boss bot
                    BossPlayers.ShallBeFollower(profile.ProfileId);
                    await ActivateBotAtPosition(
                        botCreator,
                        profile,
                        new spawnPosition(position, closestCorePoint.Id, false),
                        zone, true,
                        GroupAction,
                        OnActivate,
                        token.GetCancelToken()
                    );
                });
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Failed to spawn boss follower");
                Modules.Logger.LogError(ex);
            }
        }

        public async Task SpawnGroupBots(pitAIBossPlayer player)
        {

            CancelToken token = new CancelToken();

            BotSpawner botSpawnerClass = Controller.BotSpawner;
            BotCreator botCreator = botSpawnerClass.BotCreator as BotCreator;

            EPlayerSide side = player.Player().Side;
            Vector3 position = player.Position;
            BotZone zone = botSpawnerClass.GetClosestZone(position, out var dist);

            // get what type the bot will be 
            WildSpawnType sptBear = WildSpawnType.pmcBEAR;
            WildSpawnType sptUsec = WildSpawnType.pmcUSEC;

            WildSpawnType type;
            if (side == EPlayerSide.Bear)
            {
                type = sptBear;
            }
            else if (side == EPlayerSide.Usec)
            {
                type = sptUsec;
            }
            else
            {
                type = WildSpawnType.assault;
            }

            Modules.Logger.LogInfo("Spawn Followers");

            int memberCount = 0;


            BotCreationDataClass botsData;

            // remember what tactic this bot is associated with as the tactic will be set after spawn
            Dictionary<string, string> profileTactic = new Dictionary<string, string>();
            Dictionary<string, float> profileAggression = new Dictionary<string, float>();

            // scav players will have random followers that cannot be customized
            if (side == EPlayerSide.Savage)
            {
                botsData = await GetScavProfiles(player);
                double fenceStanding = player.realPlayer.Profile.FenceInfo.Standing;
                string[] tactis = new string[] { "Default", "Guard", "Marksman", "Pusher", "Holder", "Assist" };
                botsData.Profiles.ForEach(profile =>
                {
                    if (fenceStanding < 3)
                        profileTactic[profile.Id] = tactis[5];
                    else
                        profileTactic[profile.Id] = tactis.Random();
                });
                memberCount = botsData.Profiles.Count;
            }
            else
            {
                memberCount = SpawnHelper.spawnMemberIds.Count;

                BotSpawnParams @params = new BotSpawnParams();
                @params.ShallBeGroup = new ShallBeGroupParams(true, false, memberCount + 1);

                IProfileData data = new IProfileData(side, type, BotDifficulty.hard, side == EPlayerSide.Savage ? 5f : 0f, @params);
                Dictionary<string, Profile> botsProfile;

                try
                {
                    botsProfile = await CreateFollowerProfiles(player);
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogError(ex);
                    return;
                }

                Dictionary<string, string> botsTactic = new Dictionary<string, string>();
                Dictionary<string, float> botsAggression = new Dictionary<string, float>();

                try
                {
                    string tacticsBE = RequestHandler.GetJson("/client/game/bot/followerdetails");

                    List<BotDetails> BETactics = DeserializeFollowerDetails(tacticsBE);
                    foreach (var item in BETactics)
                    {
                        botsTactic[item.Aid] = item.Tactic;
                        botsAggression[item.Aid] = item.Aggression;
                    }
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogError(ex);
                }


                botsData = await BotCreationDataClass.Create(data, botCreator, 0, botSpawnerClass);

                followerCreationTask.Remove(player.realPlayer.ProfileId);

                foreach (var aid in Utils.SpawnHelper.spawnMemberIds)
                {
                    if (botsProfile.TryGetValue(aid, out Profile profile))
                    {
                        botsData.AddProfile(profile);
                        // see what tactic this follower will have
                        if (botsTactic.TryGetValue(aid, out string tactic))
                        {
                            profileTactic[profile.Id] = tactic;
                        }
                        else
                        {
                            profileTactic[profile.Id] = "Default";
                        }

                        if (botsAggression.TryGetValue(aid, out float aggression))
                        {
                            profileAggression[profile.Id] = aggression;
                        }
                        else
                        {
                            profileAggression[profile.Id] = 50f;
                        }
                    }
                }

            }

            Func<BotOwner, BotZone, BotsGroup> GroupAction = new Func<BotOwner, BotZone, BotsGroup>((BotOwner bt, BotZone zn) =>
            {
                return GetPlayerGroup(player, bt, zn, memberCount);
            });

            var closestCorePoint = GetClosestCorePoint(Controller, position);
            botsData.AddPosition(position, closestCorePoint.Id);

            botsData.Profiles.ForEach(async profile =>
            {
                InteractableObjects.StoreEquipment(profile);

                Action<BotOwner> OnActivate = new Action<BotOwner>((BotOwner owner) =>
                {

                    bool shallBeGroup = botsData.SpawnParams?.ShallBeGroup != null;

                    Stopwatch stopWatch = new Stopwatch();
                    stopWatch.Start();

                    Action<BotOwner> OnBotState = new Action<BotOwner>((BotOwner me) =>
                    {
                        BotOwnerManualUpdatePatch.BotOwnerUpdate.Remove(me.ProfileId); // clear watcher
                        try
                        {
                            me.Memory.DeleteInfoAboutEnemy(player.Player()); // prevent attack of player on spawn

                            if (!FollowerTransitStateCache.IsTransitSpawnProfile(me.ProfileId))
                            {
                                me.GetPlayer.ActiveHealthController.RestoreFullHealth(); // ensure bot has full health
                            }
                            else
                            {
                                Modules.Logger.LogInfo($"[Transit] Preserved carried health for '{me.Profile?.Nickname ?? me.ProfileId}'.");
                            }

                            string tactic = null;
                            profileTactic.TryGetValue(me.Profile.ProfileId, out tactic);

                            if (tactic == null) tactic = "Default";
                            profileAggression.TryGetValue(me.Profile.ProfileId, out float aggression);
                            if (aggression <= 0f && !profileAggression.ContainsKey(me.Profile.ProfileId))
                            {
                                aggression = 50f;
                            }

                            WildSpawnType botType = type;

                            if (me.Profile.Info?.Settings?.Role != null)
                            {
                                botType = me.Profile.Info.Settings.Role;
                            }

                            Modules.Logger.LogInfo("Tactic is " + tactic);

                            BossPlayers.AddFollower(me, player, true, botType, tactic, aggression);

                            TrySayFollowerReadyControlled(me, 2000);

                        }
                        catch (Exception ex)
                        {
                            Modules.Logger.LogError("Failed to add " + me.Profile.Nickname + " as follower");
                            Modules.Logger.LogError(ex);
                        }
                    });

                    BotOwnerManualUpdatePatch.BotOwnerUpdate.Add(owner.ProfileId, OnBotState);

                    // force player side on the bot
                    if (owner.Side != side)
                    {
                        owner.GetPlayer.Profile.Info.Side = side;
                    }

                    botSpawnerClass.method_11(owner, botsData, new Action<BotOwner>((BotOwner follower) =>
                    {
                        Modules.Logger.LogInfo("Follower " + follower.Profile.Nickname + " spawned");

                    }), shallBeGroup, stopWatch);

                });

                Modules.Logger.LogInfo("Trying to spawn " + profile.Nickname + " follower");

                int _inSpawnProcess = botSpawnerClass.InSpawnProcess;
                botSpawnerClass.InSpawnProcess = _inSpawnProcess + 1;

                // activate the bot
                BossPlayers.ShallBeFollower(profile.ProfileId);
                try
                {
                    await ActivateBotAtPosition(
                        botCreator,
                        profile,
                        new spawnPosition(position, botsData.GetPosition().CorePointId, false),
                        zone, true,
                        GroupAction,
                        OnActivate,
                        token.GetCancelToken()
                    );
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogError(ex);
                }

            });
        }

        public async Task<bool> SpawnDebugFollower(pitAIBossPlayer player, EPlayerSide spawnSide, Action<string> onFailure = null)
        {
            if (player == null || Controller == null || Controller.BotSpawner == null)
            {
                onFailure?.Invoke("player/controller/bot spawner not ready");
                return false;
            }

            CancelToken token = new CancelToken();
            BotSpawner botSpawnerClass = Controller.BotSpawner;
            BotCreator botCreator = botSpawnerClass.BotCreator as BotCreator;
            if (botCreator == null)
            {
                onFailure?.Invoke("bot creator is null");
                return false;
            }

            Vector3 position = player.Position;
            BotZone zone = botSpawnerClass.GetClosestZone(position, out _);
            if (zone == null)
            {
                onFailure?.Invoke("no bot zone found near player");
                return false;
            }

            WildSpawnType role = spawnSide switch
            {
                EPlayerSide.Bear => WildSpawnType.pmcBEAR,
                EPlayerSide.Usec => WildSpawnType.pmcUSEC,
                _ => WildSpawnType.assault
            };

            BotSpawnParams spawnParams = new BotSpawnParams
            {
                ShallBeGroup = new ShallBeGroupParams(true, false, Mathf.Max(2, player.Followers.Count + 2))
            };

            IProfileData data = new IProfileData(spawnSide, role, BotDifficulty.hard, spawnSide == EPlayerSide.Savage ? 5f : 0f, spawnParams);
            BotCreationDataClass botsData = BotCreationDataClass.CreateWithoutProfile(data);
            Profile profile = null;

            try
            {
                bool multiPickPmc = spawnSide == EPlayerSide.Bear || spawnSide == EPlayerSide.Usec;
                profile = await GenerateDebugSpawnProfile(botCreator, data, spawnSide, role, multiPickPmc, token, "SpawnDebugFollower");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("SpawnDebugFollower profile generation failed for requested side/role");
                Modules.Logger.LogError(ex);
            }

            if (profile == null)
            {
                try
                {
                    IProfileData safeData = new IProfileData(EPlayerSide.Savage, WildSpawnType.assault, BotDifficulty.normal, 0f, spawnParams);
                    profile = await GenerateDebugSpawnProfile(botCreator, safeData, EPlayerSide.Savage, WildSpawnType.assault, false, token, "SpawnDebugFollower");
                    if (profile != null)
                    {
                        Modules.Logger.LogInfo("SpawnDebugFollower used safe fallback profile (Savage/assault)");
                    }
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogError("SpawnDebugFollower fallback profile generation failed");
                    Modules.Logger.LogError(ex);
                }
            }

            if (profile == null)
            {
                onFailure?.Invoke("game profile generation returned no valid profile (primary+fallback)");
                return false;
            }

            profile.Info.GroupId = player.realPlayer.GroupId;
            profile.Info.TeamId = player.realPlayer.Profile.Info.TeamId;
            botsData.AddProfile(profile);

            AICorePoint closestCorePoint = GetClosestCorePoint(Controller, position);
            if (closestCorePoint == null)
            {
                onFailure?.Invoke("no valid AI core point found near player");
                return false;
            }
            botsData.AddPosition(position, closestCorePoint.Id);

            Func<BotOwner, BotZone, BotsGroup> groupAction = (BotOwner bt, BotZone zn) =>
                GetPlayerGroup(player, bt, zn, Mathf.Max(2, player.Followers.Count + 2));

            Action<BotOwner> onActivate = owner =>
            {
                bool shallBeGroup = botsData.SpawnParams?.ShallBeGroup != null;
                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();

                Action<BotOwner> onBotState = me =>
                {
                    BotOwnerManualUpdatePatch.BotOwnerUpdate.Remove(me.ProfileId);
                    try
                    {
                        me.Memory.DeleteInfoAboutEnemy(player.Player());
                        me.GetPlayer.ActiveHealthController.RestoreFullHealth();

                        WildSpawnType botRole = me.Profile.Info?.Settings?.Role ?? role;
                        BossPlayers.AddFollower(me, player, true, botRole, "Default");
                        TrySayFollowerReadyControlled(me, 500);
                    }
                    catch (Exception ex)
                    {
                        Modules.Logger.LogError("Failed to convert debug-spawned bot to follower");
                        Modules.Logger.LogError(ex);
                    }
                };

                BotOwnerManualUpdatePatch.BotOwnerUpdate[owner.ProfileId] = onBotState;

                if (owner.Side != spawnSide)
                {
                    owner.GetPlayer.Profile.Info.Side = spawnSide;
                }

                botSpawnerClass.method_11(owner, botsData, _ => { }, shallBeGroup, stopWatch);
            };


            botSpawnerClass.InSpawnProcess++;
            bool activated = false;
            try
            {
                activated = await ActivateBotAtPosition(
                    botCreator,
                    profile,
                    new spawnPosition(position, botsData.GetPosition().CorePointId, false),
                    zone,
                    true,
                    groupAction,
                    onActivate,
                    token.GetCancelToken()
                );
            }
            catch
            {
                // Keep old behavior of bubbling exception to caller while preserving spawn counter consistency.
                botSpawnerClass.InSpawnProcess = Mathf.Max(0, botSpawnerClass.InSpawnProcess - 1);
                throw;
            }

            if (!activated)
            {
                botSpawnerClass.InSpawnProcess = Mathf.Max(0, botSpawnerClass.InSpawnProcess - 1);
                onFailure?.Invoke("bot activation failed");
                return false;
            }

            return true;
        }

        public async Task<bool> SpawnDebugScavEnemy(Vector3 position, Action<string> onFailure = null)
        {
            if (Controller == null || Controller.BotSpawner == null)
            {
                onFailure?.Invoke("bots controller/spawner not ready");
                return false;
            }

            CancelToken token = new CancelToken();
            BotSpawner botSpawnerClass = Controller.BotSpawner;
            BotCreator botCreator = botSpawnerClass.BotCreator as BotCreator;
            if (botCreator == null)
            {
                onFailure?.Invoke("bot creator is null");
                return false;
            }

            BotZone zone = botSpawnerClass.GetClosestZone(position, out _);
            if (zone == null)
            {
                onFailure?.Invoke("no bot zone found near target");
                return false;
            }

            AICorePoint closestCorePoint = GetClosestCorePoint(Controller, position);
            if (closestCorePoint == null)
            {
                onFailure?.Invoke("no valid AI core point found near target");
                return false;
            }

            BotSpawnParams spawnParams = new BotSpawnParams();
            IProfileData data = new IProfileData(EPlayerSide.Savage, WildSpawnType.assault, BotDifficulty.normal, 0f, spawnParams);
            BotCreationDataClass botsData = BotCreationDataClass.CreateWithoutProfile(data);
            Profile profile = null;

            try
            {
                profile = await GenerateDebugSpawnProfile(botCreator, data, EPlayerSide.Savage, WildSpawnType.assault, false, token, "SpawnDebugScav");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("SpawnDebugScav profile generation failed");
                Modules.Logger.LogError(ex);
            }

            if (profile == null)
            {
                onFailure?.Invoke("game profile generation returned no valid scav profile");
                return false;
            }

            profile.Info.Side = EPlayerSide.Savage;
            if (profile.Info.Settings != null)
            {
                profile.Info.Settings.Role = WildSpawnType.assault;
                profile.Info.Settings.BotDifficulty = BotDifficulty.normal;
            }

            botsData.AddProfile(profile);
            botsData.AddPosition(position, closestCorePoint.Id);

            Func<BotOwner, BotZone, BotsGroup> groupAction = (BotOwner bot, BotZone botZone) =>
                botSpawnerClass.GetGroupAndSetEnemies(bot, botZone);

            Action<BotOwner> onActivate = owner =>
            {
                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();

                if (owner.Side != EPlayerSide.Savage)
                {
                    owner.GetPlayer.Profile.Info.Side = EPlayerSide.Savage;
                }

                botSpawnerClass.method_11(owner, botsData, spawned =>
                {
                    Modules.Logger.LogInfo(
                        $"SpawnDebugScav spawned {spawned.Profile?.Nickname ?? "scav"} at {position}");
                }, false, stopWatch);
            };

            botSpawnerClass.InSpawnProcess++;
            bool activated = false;
            try
            {
                activated = await ActivateBotAtPosition(
                    botCreator,
                    profile,
                    new spawnPosition(position, closestCorePoint.Id, false),
                    zone,
                    false,
                    groupAction,
                    onActivate,
                    token.GetCancelToken()
                );
            }
            catch
            {
                botSpawnerClass.InSpawnProcess = Mathf.Max(0, botSpawnerClass.InSpawnProcess - 1);
                throw;
            }

            if (!activated)
            {
                botSpawnerClass.InSpawnProcess = Mathf.Max(0, botSpawnerClass.InSpawnProcess - 1);
                onFailure?.Invoke("bot activation failed");
                return false;
            }

            return true;
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotsController), "AddActivePLayer");

        }
        [PatchPostfix]
        private static void PatchPostfix(BotsController __instance, Player player)
        {
            try
            {
                if (Controller == null)
                {
                    PlayerKilledPatch.ResetKillMessageRaidState();
                    new BossPlayers();
                    new InteractableObjects();
                    new NpcMessage();
                    PingTeamates.Enable();

                    Props.Reset();

                    BotsEventsControllerSpawnPatch.squadSpawned = false;

                    Controller = __instance;

                    string locationId = Singleton<GameWorld>.Instance.LocationId;
                    CombatDistanceConfiguration.Instance.UpdateForCurrentMap(locationId);

                    if (CombatDistanceConfiguration.UsesFactoryDistanceProfile(locationId))
                    {
                        Props.FactoryMapSett();
                    }

                    Modules.Logger.LogInfo(
                        $"[CombatDistances] map={locationId} " +
                        $"factoryDistanceProfile={CombatDistanceConfiguration.Instance.IsFactoryMode} " +
                        $"urbanDetourProfile={CombatDistanceConfiguration.Instance.IsUrbanDetourMode}");

                    Modules.Logger.LogInfo("Raid Started");
                    BattleRecorder.StartRaid(locationId);
                }


                pitAIBossPlayer playerBoss = BossPlayers.AddPlayerAsBoss(player, __instance);

                spawnedPlayers.Add(playerBoss);

                // prefetch follower profile data
                if (!HasFika() && pitFireTeam.botPrefetch.Value)
                {
                    if (playerBoss.Player().Side != EPlayerSide.Savage)
                    {
                        if (SpawnHelper.spawnMemberIdsBoss.Count > 0)
                        {
                            Instance?.PreFetchBossProfiles(playerBoss);
                        }
                        else if (Utils.SpawnHelper.spawnMemberIds.Count > 0)
                        {
                            Instance?.CreateFollowerProfiles(playerBoss);
                        }
                    }
                    else if (playerBoss.Player().Side == EPlayerSide.Savage && SpawnHelper.ScavSquad)
                    {
                        Instance?.PreFetchScavProfiles(playerBoss);
                    }
                }
            }
            catch (Exception e)
            {
                Modules.Logger.LogError(e);
            }

        }

        private static void TrySayFollowerReadyControlled(BotOwner bot, int delayMs)
        {
            if (bot == null || bot.IsDead || bot.BotState != EBotState.Active)
            {
                return;
            }

            FollowerForcedPhraseGate.Arm(bot, EPhraseTrigger.Ready, 1.5f);
            Utils.Utils.SetTimeout(() =>
            {
                if (bot == null || bot.IsDead || bot.BotState != EBotState.Active)
                {
                    return;
                }

                bot.BotTalk.TrySay(EPhraseTrigger.Ready, false);
                FollowerForcedPhraseGate.Clear(bot);
            }, delayMs);
        }
    }

    internal class BossSpawnWaveManagerClassPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BossSpawnScenario), "Run");

        }
        [PatchPrefix]
        private static bool PatchPrefix(BossSpawnScenario __instance)
        {

            if (!Singleton<AbstractGame>.Instantiated || GamePlayerOwner.MyPlayer == null) return true;

            if (GamePlayerOwner.MyPlayer.HealthController == null || !GamePlayerOwner.MyPlayer.HealthController.IsAlive)
            {
                return true;
            }

            Player player = GamePlayerOwner.MyPlayer;

            if (player.Side == EPlayerSide.Savage)
            {
                return true;
            }

            foreach (var wave in __instance.BossSpawnWaves)
            {
                // do not spawn the Goons if we are running with them
                if (
                    wave.BossType == WildSpawnType.bossKnight &&
                    (SpawnHelper.spawnMemberIdsBoss.Contains(WildSpawnType.followerBigPipe) ||
                     SpawnHelper.spawnMemberIdsBoss.Contains(WildSpawnType.followerBirdEye) ||
                     SpawnHelper.spawnMemberIdsBoss.Contains(WildSpawnType.bossKnight))
                )
                {
                    wave.ShallSpawn = false;
                    wave.ForceSpawn = false;
                }
                // increase the chance of spawning a boss based on player quests
                if (Utils.Utils.FlagGet("questGoons")) player.Profile.QuestsData.ForEach(quest =>
                {
                    foreach (var item in Utils.Props.QuestBosses)
                    {
                        if (item.Key == wave.BossType && item.Value.Contains(quest.Id) && quest.Status == EQuestStatus.Started)
                        {
                            wave.ShallSpawn = true;
                            break;
                        }
                    }
                });
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(LocalGame), MethodType.Constructor)]
    internal class LocalGameCtorPatch
    {
        public static LocalGame Instance;
        public static void Postfix(LocalGame __instance)
        {
            Instance = __instance;
        }
    }

    internal class BotsEventsControllerSpawnPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotsEventsController), "SpawnAction");
        }
        [PatchPostfix]
        private static void PatchPostfix(BotsEventsController __instance)
        {
            Utils.Utils.FlagSet("RaidTransit", false);
            try
            {
                SpawnFollowers();
            }
            catch (Exception e)
            {
                Modules.Logger.LogError(e);
            }
        }

        public static bool squadSpawned = false;

        public static void SpawnFollowers()
        {

            if (squadSpawned || BotsControllerPatch.Controller == null) return;

            squadSpawned = true;

            List<Task> squadSpawners = new List<Task>();

            List<string> scavPlayers = new List<string>();
            BotsControllerPatch.spawnedPlayers.ForEach(playerBoss =>
            {
                if (playerBoss.realPlayer.Side == EPlayerSide.Savage && SpawnHelper.ScavSquad)
                {
                    Task scavSpanner = BotsControllerPatch.Instance.SpawnGroupBots(playerBoss);
                    squadSpawners.Add(scavSpanner);
                    scavPlayers.Add(playerBoss.realPlayer.ProfileId);
                    return;
                }
            });

            if (SpawnHelper.spawnMemberIds.Count > 0)
            {
                Modules.Logger.LogInfo("Start Squad Spawn");

                BotsControllerPatch.spawnedPlayers.ForEach(playerBoss =>
                {
                    if (scavPlayers.Contains(playerBoss.realPlayer.ProfileId)) return;
                    Task squadSpanner = BotsControllerPatch.Instance.SpawnGroupBots(playerBoss);
                    squadSpawners.Add(squadSpanner);
                });

                Task.WhenAll(squadSpawners).ContinueWith(t =>
                {
                    BotsControllerPatch.alliesCreationTask.Clear();
                }, TaskScheduler.FromCurrentSynchronizationContext()).HandleExceptions();

            }
            else if (SpawnHelper.spawnMemberIdsBoss.Count > 0)
            {
                Modules.Logger.LogInfo("Start Boss Ally Spawn");

                BotsControllerPatch.Controller.BotSpawner.SetBlockedRoles(new string[] { "bossKnight", "followerBirdEye", "followerBigPipe" });

                BotsControllerPatch.alliesCreationTask.Clear();

                List<Task> bossSpawners = new List<Task>();

                BotsControllerPatch.spawnedPlayers.ForEach(playerBoss =>
                {
                    if (scavPlayers.Contains(playerBoss.realPlayer.ProfileId)) return;

                    try
                    {
                        WildSpawnType type = WildSpawnType.bossKnight;
                        if (!SpawnHelper.spawnMemberIdsBoss.Contains(WildSpawnType.bossKnight))
                        {
                            if (SpawnHelper.spawnMemberIdsBoss.Contains(WildSpawnType.followerBigPipe))
                            {
                                type = WildSpawnType.followerBigPipe;
                            }
                            else if (SpawnHelper.spawnMemberIdsBoss.Contains(WildSpawnType.followerBirdEye))
                            {
                                type = WildSpawnType.followerBirdEye;
                            }
                        }
                        bossSpawners.Add(BotsControllerPatch.Instance.SpawnBossFollower(playerBoss, type));
                    }
                    catch (Exception e)
                    {
                        Modules.Logger.LogError("Failed to spawn Boss Ally");
                        Modules.Logger.LogError(e);
                    }
                });

                Task.WhenAll(bossSpawners).ContinueWith(t =>
                {
                    BotsControllerPatch.bossCreationTask.Clear();
                }, TaskScheduler.FromCurrentSynchronizationContext()).HandleExceptions();
            }
        }

    }
    // Spawn followers after the initial spawn of the game

    internal class BotsControllerStopPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotsController), "StopGettingInfo");

        }
        [PatchPrefix]
        private static bool PatchPrefix(BotsController __instance)
        {

            bool raidTransit = Utils.Utils.FlagGet("RaidTransit");

            try
            {
                BossStandingSave(__instance);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Failed to save boss standing");
                Modules.Logger.LogError(ex);
            }

            InteractableObjects.Dispose();
            NpcMessage.Dispose();
            BossPlayers.Dispose();

            BotsControllerPatch.spawnedPlayers.Clear();
            BotsControllerPatch.followerCreationTask.Clear();
            BotsControllerPatch.alliesCreationTask.Clear();
            BotsControllerPatch.bossCreationTask.Clear();

            BotsControllerPatch.Controller = null;

            BotOwnerManualUpdatePatch.BotOwnerUpdate.Clear();

            PingTeamates.Disable();

            Enemy.ClearEnemiesLocations();
            Utils.Utils.FlagsClear();
            Utils.Utils.ValuesClear();

            if (!raidTransit)
            {
                TeammateAutoJoinRuntime.ClearAllSuppression();
                MainMenuControllerPatch.GroupPlayers.Clear();
                MainMenuControllerPatch.TransitPlayers.Clear();
                SpawnHelper.spawnMemberIds.Clear();
                SpawnHelper.spawnMemberIdsScav.Clear();
                SpawnHelper.spawnMemberIdsBoss.Clear();
                FollowerTransitStateCache.Clear();
            }


            BotsEventsControllerSpawnPatch.squadSpawned = false;

            LocalGameCtorPatch.Instance = null;


            Modules.Logger.LogInfo("Raid Ended");
            BattleRecorder.EndRaid(raidTransit ? "raidTransit" : "raidStopped");

            if (raidTransit)
            {
                Utils.Utils.FlagSet("RaidTransit", true);
                Utils.Utils.FlagSet("isBadGuy", pitFireTeam.badGuy.Value || SpawnHelper.spawnMemberIdsBoss.Count > 0);
                Utils.Utils.FlagSet("pitFireTeam", pitFireTeam.pitFireTeamFLAG.Value);
            }

            return true;
        }
        /** On raid end, increase standing with Knight trader if we have Knight as follower **/
        private static void BossStandingSave(BotsController controller)
        {

            pitAIBossPlayer boss = null;
            controller.Players.ExecuteForEach(new Action<IPlayer>(player =>
            {
                if (boss == null)
                {
                    boss = BossPlayers.GetBoss(player.ProfileId);
                }
            }));

            if (boss == null) return;

            Profile profile = boss.realPlayer.Profile;

            bool knightIncrease = false;
            bool pipeIncrease = false;
            bool birdEyeIncrease = false;


            double maxStanding = 2;

            profile.QuestsData.ForEach(quest =>
            {

                foreach (var item in Utils.Props.Quests)
                {
                    // allow Knight standing to increase only after we complete the first quest
                    if (item.Key == "Knight" && quest.Id == item.Value[0] && quest.Status == EFT.Quests.EQuestStatus.Success)
                    {
                        knightIncrease = true;
                    }
                    // allow BigPipe to add to the standing only after we complete the payback quest
                    if (item.Key == "BigPipe" && (quest.Id == item.Value[0]) && quest.Status == EFT.Quests.EQuestStatus.Success)
                    {
                        pipeIncrease = true;
                    }
                    // allow BirdEye to add to the standing only after we complete the enemy spotted quest
                    if (item.Key == "BirdEye" && quest.Id == item.Value[0] && quest.Status == EFT.Quests.EQuestStatus.Success)
                    {
                        birdEyeIncrease = true;
                    }

                }

            });

            if (boss.realPlayer.Profile.TryGetTraderInfo(Utils.Props.KnightTrader, out var traderInfo))
            {
                if (traderInfo.Disabled) return;
                boss.Followers.ForEach(bot =>
                {
                    if (
                        (bot.IsRole(WildSpawnType.bossKnight) && knightIncrease) ||
                        (bot.IsRole(WildSpawnType.followerBigPipe) && pipeIncrease) ||
                        (bot.IsRole(WildSpawnType.followerBirdEye) && birdEyeIncrease)
                    )
                    {
                        double standing = boss.realPlayer.Profile.GetTraderStanding(Utils.Props.KnightTrader);
                        if (standing < maxStanding)
                        {
                            traderInfo.SetStanding(Math.Min(maxStanding, standing + 0.05));
                            Modules.Logger.LogInfo("Increasing standing with Knight trader");
                        }
                    }
                });
            }
        }
    }

    internal class LocalGameCleanupPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(LocalGame), "CleanUp");

        }
        // fix errors during cleanup because of NULL players
        [PatchPrefix]
        private static bool PatchPrefix(LocalGame __instance)
        {
            try
            {
                var dictionary_2 = AccessTools.Field(typeof(LocalGame), "dictionary_2").GetValue(__instance) as Dictionary<string, Player>;
                if (dictionary_2 != null)
                {
                    List<string> keysToRemove = new List<string>();

                    // Iterate through the dictionary to find null values
                    foreach (var kvp in dictionary_2)
                    {
                        if (kvp.Value == null)
                        {
                            keysToRemove.Add(kvp.Key);
                        }
                    }

                    // Remove the keys with null values
                    foreach (var key in keysToRemove)
                    {
                        dictionary_2.Remove(key);
                    }
                }

                // Fire lifecycle event for addon integration (cache cleanup, etc).
                // Broadcast to all followers that raid is ending.
                foreach (var follower in Modules.BossPlayers.GetFollowers())
                {
                    if (follower?.GetBot() != null)
                    {
                        Modules.SainAddonBridge.RaiseFollowerLifecycleEvent(follower.GetBot(), FollowerLifecycleEvent.OnRaidEnd);
                    }
                }

                Modules.Logger.LogInfo("Raid CleanUp Finished");

            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Raid CleanUp Failed");
                Modules.Logger.LogError(ex);
            }

            return true;
        }
    }
}

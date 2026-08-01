using EFT;
using EFT.Counters;
using EFT.InventoryLogic;

using Comfort.Common;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using pitTeam.Components;
using pitTeam.Utils;

namespace pitTeam.Modules
{
    internal class RecruitPickupCandidateRequest
    {
        public string ProfileId { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public string Nickname { get; set; } = string.Empty;
        public int Level { get; set; }
        public string Side { get; set; } = string.Empty;
        public string Voice { get; set; } = string.Empty;
        public string Head { get; set; } = string.Empty;
        public string ProfileJson { get; set; } = string.Empty;
    }

    internal class RecruitPickupRequest
    {
        public List<RecruitPickupCandidateRequest> Candidates { get; set; } = new List<RecruitPickupCandidateRequest>();
    }

    /**
     *  Helper class to manage Boss Players and their followers
     */
    public class BossPlayers
    {
        private const float RecruitMinCombatAggression = 20f;
        private const float RecruitMaxCombatAggression = 60f;
        private static readonly Random RecruitAggressionRandom = new Random();
        private static readonly object RecruitAggressionRandomLock = new object();

        public static BossPlayers Instance { get; private set; }

        private Dictionary<string, pitAIBossPlayer> _bosses;
        private List<BotFollowerPlayer> _followers;
        private Dictionary<string, BotFollowerPlayer> _followersByProfileId;
        private HashSet<string> _progressSavedFollowerProfileIds;
        private HashSet<string> _recruitmentDeniedProfileIds;
        private List<string> _shallBeFollower;
        private List<int> _botsGroup;


        private List<string> _removedBosses;

        public bool IsDisposed = false;

        public BossPlayers()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            _bosses = new Dictionary<string, pitAIBossPlayer>();
            _followers = new List<BotFollowerPlayer> { };
            _followersByProfileId = new Dictionary<string, BotFollowerPlayer>(StringComparer.Ordinal);
            _progressSavedFollowerProfileIds = new HashSet<string>(StringComparer.Ordinal);
            _recruitmentDeniedProfileIds = new HashSet<string>(StringComparer.Ordinal);
            _shallBeFollower = new List<string> { };
            _removedBosses = new List<string> { };
            _botsGroup = new List<int> { };
        }

        public static void Dispose()
        {
            if (Instance != null)
            {
                Instance.Destroy();
                Instance = null;
            }
        }


        private pitAIBossPlayer AddBossPlayer(Player player, BotsController botsController)
        {
            if (_bosses.ContainsKey(player.ProfileId)) return _bosses[player.ProfileId];


            WildSpawnType roleType = player.Profile.Info.Settings.Role;
            player.Profile.Info.Settings.Role = WildSpawnType.bossKnight; // temp switch to boss role
            pitAIBossPlayer playerBoss = new pitAIBossPlayer(player, botsController);
            player.Profile.Info.Settings.Role = roleType; // revert role back to original

            // force bosses to not warn the player (these will be exUsec and the Goons)
            if (player.Side != EPlayerSide.Savage && Utils.Utils.PlayerHasKnightQuest(player.Profile))
            {
                var field = typeof(FenceLoyaltyLevel).GetField("HostileBosses", BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    FieldInfo attrField = typeof(FieldInfo).GetField("m_fieldAttributes", BindingFlags.NonPublic | BindingFlags.Instance);
                    attrField?.SetValue(field, field.Attributes & ~FieldAttributes.InitOnly);

                    field.SetValue(player.Profile.FenceInfo.FenceLoyalty, false);
                }
            }

            if (!playerBoss.IAmBoos)
            {
                Logger.LogInfo($"Could not make player {player.Profile.Nickname} as BOSS");
                return null;
            }
            else
            {
                Logger.LogInfo($"Made player {player.Profile.Nickname} a BOSS");
            }

            string name = player.ProfileId;

            if (string.IsNullOrEmpty(player.Profile.Info.GroupId))
            {
                player.Profile.Info.GroupId = "bossGroup_" + name;
            }

            if (_removedBosses.Contains(name))
            {
                _removedBosses.Remove(name);
            }

            _bosses[name] = playerBoss;

            return playerBoss;

        }

        private bool RemoveBossPlayer(string name, bool died = false)
        {
            if (_bosses.ContainsKey(name))
            {
                pitAIBossPlayer boss = _bosses[name];

                List<BotOwner> ownersToRemove = new List<BotOwner>();
                List<BotFollowerPlayer> followersToRemove = new List<BotFollowerPlayer>();

                boss.Followers.ForEach(fl =>
                {
                    ownersToRemove.Add(fl);
                    _followers.ForEach(follower =>
                    {
                        if (follower.IsBot(fl))
                        {
                            followersToRemove.Add(follower);
                        }
                    });
                });
                if (followersToRemove.Count > 0 || (died && FollowerDeathEscapeResolver.HasFallenSquadmateSnapshots()))
                {
                    if (died)
                    {
                        // Player death tears down the boss/follower relationship before normal raid cleanup.
                        // Resolve simulated follower escapes while live bot state is still available.
                        // Already-dead squadmates may no longer be in boss.Followers, but their
                        // fallen snapshot still needs to become a lost outcome for gear-loss rules.
                        FollowerDeathEscapeResolver.ResolveAndSend(boss, followersToRemove);
                    }

                    if (followersToRemove.Count > 0)
                    {
                        SaveFollowersProgress(died ? followersToRemove.FindAll(fl => fl.IsSquadMate) : followersToRemove);
                    }
                }

                ownersToRemove.ForEach(follower =>
                {
                    boss.RemoveFollower(follower);
                });

                followersToRemove.ForEach(_follower =>
                {
                    _followers.Remove(_follower);
                    BotOwner followerBot = _follower.GetBot();
                    if (followerBot?.ProfileId != null)
                    {
                        _followersByProfileId.Remove(followerBot.ProfileId);
                    }
                    _follower.Dismiss();
                });

                boss.Followers.Clear();

                boss.DisposeBoss();

                _bosses.Remove(name);
                _removedBosses.Add(name);
                return true;
            }
            else if (_removedBosses.Contains(name))
            {
                return true;
            }

            return false;
        }

        private void Destroy()
        {
            if (IsDisposed) return;

            if (_bosses.Count > 0)
            {
                List<string> keys = new List<string>(_bosses.Keys);
                foreach (string key in keys)
                {
                    RemoveBossPlayer(key);
                }
            }
            _bosses.Clear();
            _removedBosses.Clear();
            _followers.Clear();
            _followersByProfileId.Clear();
            _progressSavedFollowerProfileIds.Clear();
            _recruitmentDeniedProfileIds.Clear();
            _shallBeFollower.Clear();
            FollowerGrenadeCooldowns.ClearAll();
            FollowerGrenadeRuntimeGate.ClearAll();
            FollowerDeathEscapeResolver.ClearFallenSquadmateSnapshots();

            IsDisposed = true;
            Instance = null;
        }

        private BotFollowerPlayer AddBotFollower(BotOwner bot, pitAIBossPlayer player, bool squadMate = false, WildSpawnType role = WildSpawnType.assault, string tactic = "Default", float aggression = 50f)
        {

            BotFollowerPlayer? _follower = null;

            _followers.ForEach(follower =>
            {
                if (follower.IsBot(bot))
                {
                    _follower = follower;
                }
            });

            if (_follower != null)
            {
                return _follower;
            }

            if (!squadMate)
            {
                tactic = "Default";
                aggression = CreateRecruitCombatAggression();
            }

            if (_shallBeFollower.Contains(bot.ProfileId)) _shallBeFollower.Remove(bot.ProfileId);

            bool isAIBoss = false;

            foreach (var item in Utils.Props.BossFollowersType)
            {
                if (role == item)
                {
                    isAIBoss = true;
                    break;
                }
            }

            if (!isAIBoss)
            {
                _follower = new BotFollowerPlayer(bot, player, squadMate);
            }
            else
            {
                _follower = new BossFollowerPlayer(bot, player, role);

            }

            _follower.CombatTactic = BotFollowerPlayer.ParseCombatTactic(tactic);
            _follower.CombatAggression = aggression;

            try
            {
                _follower.Init();

            }
            catch (Exception e)
            {
                Modules.Logger.LogError("Failed to init follower");
                Modules.Logger.LogError(e);
                return null;
            }

            _followers.Add(_follower);
            if (bot?.ProfileId != null)
            {
                _followersByProfileId[bot.ProfileId] = _follower;
            }

            // Fire lifecycle event for addon integration (cache registration, etc).
            SainAddonBridge.RaiseFollowerLifecycleEvent(bot, FollowerLifecycleEvent.OnRecruited);

            return _follower;
        }

        private static float CreateRecruitCombatAggression()
        {
            lock (RecruitAggressionRandomLock)
            {
                return RecruitMinCombatAggression +
                       (float)RecruitAggressionRandom.NextDouble() *
                       (RecruitMaxCombatAggression - RecruitMinCombatAggression);
            }
        }

        private static double GetLevelFactor(int playerLevel, int botLevel)
        {
            const double minValue = 1;
            const double maxValue = 4;
            const int threshold = 7; // Point where the decrease starts

            if (botLevel <= playerLevel - threshold)
                return maxValue;
            if (botLevel >= playerLevel + threshold)
                return minValue;

            // Linear interpolation 
            double factor = maxValue - ((botLevel - (playerLevel + threshold)) / threshold) * (maxValue - minValue);

            return Math.Max(minValue, Math.Min(maxValue, factor)); // Ensure it's within range
        }


        public static void SaveFollowersProgress(List<BotFollowerPlayer> followers = null)
        {
            if (Instance._followers.Count == 0 && (followers == null || followers.Count < 1)) return;

            List<RecruitPickupCandidateRequest> recruitCandidates = new List<RecruitPickupCandidateRequest>();

            var converterClass = typeof(AbstractGame).Assembly.GetTypes()
                   .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);

            var _defaultJsonConverters = Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;

            try
            {
                List<BotFollowerPlayer> _followers = followers ?? Instance._followers;
                List<object> data = new List<object>();

                foreach (var item in _followers)
                {
                    Profile pr = item.GetBot().Profile;
                    WildSpawnType role = pr.Info.Settings.Role;

                    if (Utils.Props.BossFollowersType.Contains(role) || item.GetBot().Side == EPlayerSide.Savage)
                    {
                        continue;
                    }

                    if (!item.IsSquadMate)
                    {
                        if (pitFireTeam.recruitPickup.Value && item.GetBoss()?.realPlayer?.Side != EPlayerSide.Savage)
                        {
                            string voiceId = pr.Customization != null && pr.Customization.TryGetValue(EBodyModelPart.Voice, out MongoID voice) ? voice.ToString() : string.Empty;
                            string headId = pr.Customization != null && pr.Customization.TryGetValue(EBodyModelPart.Head, out MongoID head) ? head.ToString() : string.Empty;

                            if (!string.IsNullOrWhiteSpace(pr.Id) &&
                                !string.IsNullOrWhiteSpace(pr.Nickname) &&
                                !string.IsNullOrWhiteSpace(voiceId) &&
                                !string.IsNullOrWhiteSpace(headId))
                            {
                                recruitCandidates.Add(new RecruitPickupCandidateRequest
                                {
                                    ProfileId = pr.Id,
                                    AccountId = pr.AccountId ?? string.Empty,
                                    Nickname = pr.Nickname,
                                    Level = pr.Info?.Level ?? 1,
                                    Side = pr.Info?.Side.ToString() ?? string.Empty,
                                    Voice = voiceId,
                                    Head = headId,
                                    ProfileJson = CreateRecruitProfileJson(pr, _defaultJsonConverters),
                                });
                            }
                            else
                            {
                                Modules.Logger.LogInfo(
                                    $"Skipped recruit pickup candidate due to missing identity data. " +
                                    $"profileId='{pr.Id ?? string.Empty}' nickname='{pr.Nickname ?? string.Empty}' " +
                                    $"voice='{voiceId}' head='{headId}'");
                            }
                        }

                        continue;
                    }

                    string progressSaveKey = GetProgressSaveKey(item, pr);
                    if (!string.IsNullOrEmpty(progressSaveKey) && Instance._progressSavedFollowerProfileIds.Contains(progressSaveKey))
                    {
                        Modules.Logger.LogInfo($"[Progress] Skipped duplicate squadmate progress save for '{pr.Nickname ?? progressSaveKey}'.");
                        continue;
                    }

                    LogFollowerProgressSource(item, pr);
                    FollowerMovementSkillProgress movementProgress = CalculateFollowerMovementSkillProgress(item.GetBot());
                    List<object> skills = new List<object>();

                    pr.Skills.DisplayList.ExecuteForEach(skill =>
                    {
                        float baseCurrent = UnityEngine.Mathf.Max(0f, skill.Current - skill.PointsEarned);
                        float progress = skill.ProgressValue;
                        float pointsEarned = skill.PointsEarned;

                        if (skill.Id == ESkillId.Strength)
                        {
                            progress += movementProgress.StrengthProgress;
                            pointsEarned += movementProgress.StrengthProgress;
                        }
                        else if (skill.Id == ESkillId.Endurance && movementProgress.SuppressEnduranceProgress)
                        {
                            if (progress > 0f || pointsEarned > 0f)
                            {
                                Modules.Logger.LogInfo(
                                    $"[Progress] Suppressed vanilla AI Endurance for '{pr.Nickname ?? pr.ProfileId}': " +
                                    $"progress={progress:0.00} points={pointsEarned:0.00} because custom follower weight is overweight.");
                            }

                            progress = 0f;
                            pointsEarned = 0f;
                        }

                        skills.Add(new
                        {
                            Id = skill.Id,
                            Current = baseCurrent,
                            Progress = progress,
                            PointsEarnedDuringSession = pointsEarned,
                        });
                    });

                    if (skills.Find(skill => (ESkillId)skill.GetType().GetProperty("Id").GetValue(skill) == ESkillId.BotReload) == null)
                    {
                        skills.Add(new
                        {
                            Id = ESkillId.BotReload,
                            Current = pr.Skills.BotReload.Current,
                            Progress = pr.Skills.BotReload.ProgressValue,
                            PointsEarnedDuringSession = 0,
                        });
                    }

                    var boss = item.GetBoss();

                    int bossLevel = boss.realPlayer.Profile.Info.Level;
                    int botLevel = pr.Info.Level;
                    data.Add(new
                    {
                        Aid = pr.AccountId,
                        BotExperienceSession = Math.Round(pr.EftStats.SessionCounters.GetAllInt(new object[] { CounterTag.ExpKill }) + (boss.realPlayer.Profile.EftStats.SessionCounters.GetAllInt(new object[] { CounterTag.Exp }) * GetLevelFactor(bossLevel, botLevel))),
                        KillCount = pr.EftStats.SessionCounters.GetAllInt(new object[] { CounterTag.Kills }),
                        RaidSeconds = GetFollowerRaidSeconds(item.GetBot()),
                        Skills = skills
                    });

                    if (!string.IsNullOrEmpty(progressSaveKey))
                    {
                        Instance._progressSavedFollowerProfileIds.Add(progressSaveKey);
                    }
                }

                string progressJson = new
                {
                    Entries = data
                }.ToJson(_defaultJsonConverters);
                if (data.Count > 0)
                {
                    PostRaidRequestAsync("/client/game/bot/followerprogress", progressJson, "followers progress");
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Failed to save squadmate follower progress");
                Modules.Logger.LogError(ex);
            }

            try
            {
                if (recruitCandidates.Count > 0)
                {
                    string recruitJson = new RecruitPickupRequest
                    {
                        Candidates = recruitCandidates
                    }.ToJson(_defaultJsonConverters);
                    PostRaidRequestAsync("/singleplayer/pitfireteam/recruitpickup", recruitJson, "recruit pickup candidates");
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"Failed to send recruit pickup candidates. candidates={recruitCandidates.Count}");
                Modules.Logger.LogError(ex);
            }
        }

        private static string GetProgressSaveKey(BotFollowerPlayer follower, Profile profile)
        {
            string profileId = follower?.GetBot()?.ProfileId ?? profile?.Id;
            if (!string.IsNullOrWhiteSpace(profileId))
            {
                return profileId;
            }

            string accountId = profile?.AccountId;
            return string.IsNullOrWhiteSpace(accountId) ? string.Empty : $"aid:{accountId}";
        }

        private sealed class FollowerMovementSkillProgress
        {
            public float StrengthProgress { get; set; }
            public bool SuppressEnduranceProgress { get; set; }
        }

        private static FollowerMovementSkillProgress CalculateFollowerMovementSkillProgress(BotOwner bot)
        {
            FollowerMovementSkillProgress result = new FollowerMovementSkillProgress();
            try
            {
                Player player = bot?.GetPlayer;
                if (player?.Pedometer == null)
                {
                    Modules.Logger.LogInfo(
                        $"[ProgressDebug] Movement skill skipped for '{bot?.Profile?.Nickname ?? bot?.ProfileId ?? "<null>"}': " +
                        $"playerNull={player == null} pedometerNull={player?.Pedometer == null} " +
                        $"inventoryNull={player?.InventoryController?.Inventory == null} equipmentNull={player?.InventoryController?.Inventory?.Equipment == null}.");
                    return result;
                }

                float movementWeight = GetFollowerMovementWeightKg(player);
                float overweight = CalculatePlayerStyleSkillOverweight(player);
                if (overweight <= 0f || !Singleton<BackendConfigSettingsClass>.Instantiated)
                {
                    Modules.Logger.LogInfo(
                        $"[Progress] Follower movement skill for '{player.Profile?.Nickname ?? bot.ProfileId}': " +
                        $"weight={movementWeight:0.0}kg baseOverweight={overweight:0.00}; keeping vanilla movement skill routing.");
                    return result;
                }

                result.SuppressEnduranceProgress = true;
                BackendConfigSettingsClass.GlobalSkillsSettings settings = Singleton<BackendConfigSettingsClass>.Instance.SkillsSettings;
                float runDistance = GetPedometerDistance(player, EPlayerState.Run);
                float sprintDistance = GetPedometerDistance(player, EPlayerState.Sprint);
                float movementGain = runDistance * UnityEngine.Mathf.Lerp(settings.Strength.MovementActionMin, settings.Strength.MovementActionMax, overweight);
                float sprintGain = sprintDistance * UnityEngine.Mathf.Lerp(settings.Strength.SprintActionMin, settings.Strength.SprintActionMax, overweight);
                float total = (movementGain + sprintGain) * settings.SkillProgressRate;
                result.StrengthProgress = UnityEngine.Mathf.Max(0f, total);

                if (result.StrengthProgress > 0f)
                {
                    Modules.Logger.LogInfo(
                        $"[Progress] Synthetic Strength for '{player.Profile?.Nickname ?? bot.ProfileId}': " +
                        $"weight={movementWeight:0.0}kg baseOverweight={overweight:0.00} " +
                        $"run={runDistance:0.0}m sprint={sprintDistance:0.0}m progress={result.StrengthProgress:0.00}");
                }

                return result;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[Progress] Failed to calculate follower movement skill progress.");
                Modules.Logger.LogError(ex);
                return result;
            }
        }

        private static float CalculatePlayerStyleSkillOverweight(Player player)
        {
            if (player == null || !Singleton<BackendConfigSettingsClass>.Instantiated)
            {
                Modules.Logger.LogInfo(
                    $"[ProgressDebug] Overweight unavailable: playerNull={player == null} backendConfigInstantiated={Singleton<BackendConfigSettingsClass>.Instantiated}.");
                return 0f;
            }

            float totalWeight = GetFollowerMovementWeightKg(player);
            float inventoryWeight = GetInventoryWeightKg(player);
            float equipmentTreeWeight = GetItemTotalWeight(player.InventoryController?.Inventory?.Equipment);
            BackendConfigSettingsClass.GClass1736 stamina = Singleton<BackendConfigSettingsClass>.Instance.Stamina;
            float skillRelative = player.Skills?.CarryingWeightRelativeModifier ?? 1f;
            float healthRelative = player.HealthController?.CarryingWeightRelativeModifier ?? 1f;
            float healthAbsolute = player.HealthController?.CarryingWeightAbsoluteModifier ?? 0f;
            float relative = skillRelative * healthRelative;
            UnityEngine.Vector2 walkLimits = ApplyCarryingModifiers(stamina.WalkOverweightLimits, relative, healthAbsolute);
            UnityEngine.Vector2 baseLimits = ApplyCarryingModifiers(stamina.BaseOverweightLimits, relative, healthAbsolute);
            UnityEngine.Vector2 sprintLimits = ApplyCarryingModifiers(stamina.SprintOverweightLimits, relative, healthAbsolute);
            UnityEngine.Vector2 walkSpeedLimits = ApplyCarryingModifiers(stamina.WalkSpeedOverweightLimits, relative, healthAbsolute);
            float uiLowerLimit = UnityEngine.Mathf.Min(walkLimits.x, baseLimits.x, sprintLimits.x, walkSpeedLimits.x);
            float uiUpperLimit = baseLimits.y;
            UnityEngine.Vector2 limits = baseLimits;

            Modules.Logger.LogInfo(
                $"[ProgressDebug] Overweight inputs for '{player.Profile?.Nickname ?? player.ProfileId}': " +
                $"movementWeight={totalWeight:0.00}kg inventoryWeight={inventoryWeight:0.00}kg equipmentTreeWeight={equipmentTreeWeight:0.00}kg " +
                $"skillRelative={skillRelative:0.000} healthRelative={healthRelative:0.000} healthAbsolute={healthAbsolute:0.00} " +
                $"uiLower={uiLowerLimit:0.00}kg uiUpper={uiUpperLimit:0.00}kg baseLimits={limits.x:0.00}-{limits.y:0.00}kg.");

            LogFollowerOverweightLimitDebug(
                player,
                inventoryWeight,
                equipmentTreeWeight,
                stamina.WalkOverweightLimits,
                stamina.BaseOverweightLimits,
                stamina.SprintOverweightLimits,
                stamina.WalkSpeedOverweightLimits,
                walkLimits,
                baseLimits,
                sprintLimits,
                walkSpeedLimits);

            if (inventoryWeight <= 0f)
            {
                LogFollowerInventoryWeightDebug(player, "zeroTotalWeight");
            }

            if (limits.y <= limits.x)
            {
                return totalWeight > limits.x ? 1f : 0f;
            }

            return UnityEngine.Mathf.Clamp01(UnityEngine.Mathf.InverseLerp(limits.x, limits.y, totalWeight));
        }

        private static float GetFollowerMovementWeightKg(Player player)
        {
            float inventoryWeight = GetInventoryWeightKg(player);
            if (inventoryWeight > 0f)
            {
                return inventoryWeight;
            }

            return GetItemTotalWeight(player?.InventoryController?.Inventory?.Equipment);
        }

        private static UnityEngine.Vector2 ApplyCarryingModifiers(UnityEngine.Vector2 limits, float relative, float absolute)
        {
            return limits * relative + new UnityEngine.Vector2(absolute, absolute);
        }

        private static float InverseLerp(UnityEngine.Vector2 limits, float weight)
        {
            if (limits.y <= limits.x)
            {
                return weight > limits.x ? 1f : 0f;
            }

            return UnityEngine.Mathf.Clamp01(UnityEngine.Mathf.InverseLerp(limits.x, limits.y, weight));
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogFollowerOverweightLimitDebug(
            Player player,
            float inventoryWeight,
            float equipmentTreeWeight,
            UnityEngine.Vector2 rawWalk,
            UnityEngine.Vector2 rawBase,
            UnityEngine.Vector2 rawSprint,
            UnityEngine.Vector2 rawWalkSpeed,
            UnityEngine.Vector2 walk,
            UnityEngine.Vector2 baseLimits,
            UnityEngine.Vector2 sprint,
            UnityEngine.Vector2 walkSpeed)
        {
            try
            {
                Modules.Logger.LogInfo(
                    $"[ProgressDebug] Raw overweight limits for '{player?.Profile?.Nickname ?? player?.ProfileId ?? "<null>"}': " +
                    $"walk={rawWalk.x:0.00}-{rawWalk.y:0.00}kg base={rawBase.x:0.00}-{rawBase.y:0.00}kg " +
                    $"sprint={rawSprint.x:0.00}-{rawSprint.y:0.00}kg walkSpeed={rawWalkSpeed.x:0.00}-{rawWalkSpeed.y:0.00}kg.");

                Modules.Logger.LogInfo(
                    $"[ProgressDebug] Effective overweight limits for '{player?.Profile?.Nickname ?? player?.ProfileId ?? "<null>"}': " +
                    $"walk={walk.x:0.00}-{walk.y:0.00}kg base={baseLimits.x:0.00}-{baseLimits.y:0.00}kg " +
                    $"sprint={sprint.x:0.00}-{sprint.y:0.00}kg walkSpeed={walkSpeed.x:0.00}-{walkSpeed.y:0.00}kg.");

                Modules.Logger.LogInfo(
                    $"[ProgressDebug] Overweight values for '{player?.Profile?.Nickname ?? player?.ProfileId ?? "<null>"}': " +
                    $"inventoryWeight={inventoryWeight:0.00}kg inventoryBase={InverseLerp(baseLimits, inventoryWeight):0.000} " +
                    $"inventoryWalk={InverseLerp(walk, inventoryWeight):0.000} inventorySprint={InverseLerp(sprint, inventoryWeight):0.000} " +
                    $"inventoryWalkSpeed={InverseLerp(walkSpeed, inventoryWeight):0.000}; " +
                    $"equipmentWeight={equipmentTreeWeight:0.00}kg equipmentBase={InverseLerp(baseLimits, equipmentTreeWeight):0.000} " +
                    $"equipmentWalk={InverseLerp(walk, equipmentTreeWeight):0.000} equipmentSprint={InverseLerp(sprint, equipmentTreeWeight):0.000} " +
                    $"equipmentWalkSpeed={InverseLerp(walkSpeed, equipmentTreeWeight):0.000}.");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[ProgressDebug] Failed to log follower overweight limit debug.");
                Modules.Logger.LogError(ex);
            }
        }

        private static float GetInventoryWeightKg(Player player)
        {
            try
            {
                return UnityEngine.Mathf.Max(0f, player?.InventoryController?.Inventory?.TotalWeight?.Value ?? 0f);
            }
            catch
            {
                return 0f;
            }
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogFollowerProgressSource(BotFollowerPlayer follower, Profile profile)
        {
            try
            {
                BotOwner bot = follower?.GetBot();
                Player player = bot?.GetPlayer;
                var inventory = player?.InventoryController?.Inventory;
                InventoryEquipment equipment = inventory?.Equipment;

                Modules.Logger.LogInfo(
                    $"[ProgressDebug] Saving progress source for '{profile?.Nickname ?? bot?.Profile?.Nickname ?? bot?.ProfileId ?? "<null>"}': " +
                    $"profileId={profile?.Id ?? bot?.ProfileId ?? string.Empty} aid={profile?.AccountId ?? string.Empty} " +
                    $"isSquadMate={follower?.IsSquadMate} botAlive={bot?.HealthController?.IsAlive} " +
                    $"playerNull={player == null} inventoryNull={inventory == null} equipmentNull={equipment == null} " +
                    $"inventoryTotalWeight={GetInventoryWeightKg(player):0.00}kg equipmentTotalWeight={GetItemTotalWeight(equipment):0.00}kg " +
                    $"equipmentItems={CountEquipmentItems(equipment)} run={GetPedometerDistance(player, EPlayerState.Run):0.0}m " +
                    $"sprint={GetPedometerDistance(player, EPlayerState.Sprint):0.0}m.");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[ProgressDebug] Failed to log follower progress source.");
                Modules.Logger.LogError(ex);
            }
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogFollowerInventoryWeightDebug(Player player, string reason)
        {
            try
            {
                var inventory = player?.InventoryController?.Inventory;
                InventoryEquipment equipment = inventory?.Equipment;
                Modules.Logger.LogInfo(
                    $"[ProgressDebug] Inventory weight breakdown for '{player?.Profile?.Nickname ?? player?.ProfileId ?? "<null>"}' reason={reason}: " +
                    $"inventoryNull={inventory == null} equipmentNull={equipment == null} " +
                    $"inventoryTotalWeight={GetInventoryWeightKg(player):0.00}kg equipmentTotalWeight={GetItemTotalWeight(equipment):0.00}kg " +
                    $"equipmentWeight={GetItemShellWeight(equipment):0.00}kg equipmentItems={CountEquipmentItems(equipment)}.");

                if (equipment?.Slots == null)
                {
                    return;
                }

                foreach (Slot slot in equipment.Slots)
                {
                    Item item = slot?.ContainedItem;
                    if (item == null)
                    {
                        continue;
                    }

                    Modules.Logger.LogInfo(
                        $"[ProgressDebug] Equipment slot '{slot.ID}' root='{item.Name.Localized()}' template={item.TemplateId} id={item.Id} " +
                        $"weight={GetItemShellWeight(item):0.00}kg totalWeight={GetItemTotalWeight(item):0.00}kg children={CountItemTree(item) - 1}.");
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[ProgressDebug] Failed to log follower inventory weight breakdown.");
                Modules.Logger.LogError(ex);
            }
        }

        private static int CountEquipmentItems(InventoryEquipment equipment)
        {
            try
            {
                return equipment?.GetAllItems()?.Count() ?? 0;
            }
            catch
            {
                return -1;
            }
        }

        private static int CountItemTree(Item item)
        {
            try
            {
                return item?.GetAllItems()?.Count() ?? 0;
            }
            catch
            {
                return -1;
            }
        }

        private static float GetItemTotalWeight(Item item)
        {
            try
            {
                return UnityEngine.Mathf.Max(0f, item?.TotalWeight ?? 0f);
            }
            catch
            {
                return 0f;
            }
        }

        private static float GetItemShellWeight(Item item)
        {
            try
            {
                return UnityEngine.Mathf.Max(0f, item?.Weight ?? 0f);
            }
            catch
            {
                return 0f;
            }
        }

        private static float GetPedometerDistance(Player player, EPlayerState state)
        {
            float[] distances = player?.Pedometer?.Float_1;
            int index = (int)state;
            if (distances == null || index < 0 || index >= distances.Length)
            {
                return 0f;
            }

            return UnityEngine.Mathf.Max(0f, distances[index]);
        }

        private static string CreateRecruitProfileJson(Profile profile, JsonConverter[] converters)
        {
            if (profile == null)
            {
                return string.Empty;
            }

            try
            {
                CompleteProfileDescriptorClass descriptor = new CompleteProfileDescriptorClass(profile, GClass2240.Instance);
                return descriptor.ToJson(converters);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"Failed to capture recruited bot profile '{profile.ProfileId ?? profile.Nickname}' for friend request; recruit invite will fall back to generated profile if accepted.");
                Modules.Logger.LogError(ex);
                return string.Empty;
            }
        }

        private static int GetFollowerRaidSeconds(BotOwner bot)
        {
            try
            {
                float bornTime = bot?.BotPersonalStats?.BornTime ?? 0f;
                if (bornTime <= 0f)
                {
                    return 0;
                }

                return Math.Max(0, (int)Math.Round(UnityEngine.Time.time - bornTime));
            }
            catch
            {
                return 0;
            }
        }

        private static void PostRaidRequestAsync(string route, string json, string description)
        {
            Task.Run(() =>
            {
                try
                {
                    RequestHandler.PostJson(route, json);
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogError($"Failed to send post-raid {description}");
                    Modules.Logger.LogError(ex);
                }
            });
        }

        private bool IsBotFollower(BotOwner bot, AIBossPlayer boss = null)
        {
            if (bot == null || bot.BotFollower == null || !bot.BotFollower.HaveBoss) return false;

            if (!string.IsNullOrEmpty(bot.ProfileId) && _followersByProfileId.TryGetValue(bot.ProfileId, out BotFollowerPlayer cachedFollower))
            {
                if (boss != null)
                {
                    return bot.BotFollower.HaveBoss &&
                           bot.BotFollower.BossToFollow != null &&
                           bot.BotFollower.BossToFollow.Player().ProfileId == boss.Player().ProfileId;
                }

                return cachedFollower != null;
            }

            BotFollowerPlayer _follower = null;

            foreach (var item in _followers)
            {
                if (item != null && item.IsBot(bot))
                {
                    _follower = item;
                    break;
                }
            }

            if (_follower != null && boss != null && bot != null && bot.BotFollower.HaveBoss)
            {
                return bot.BotFollower.BossToFollow.Player().ProfileId == boss.Player().ProfileId;
            }

            return _follower != null;
        }

        private void RemoveBotFollower(BotOwner bot, pitAIBossPlayer player)
        {

            BotFollowerPlayer _follower = null;

            _followers.ForEach(follower =>
            {
                if (follower.IsBot(bot))
                {
                    _follower = follower;
                }
            });

            if (_follower != null)
            {
                _followers.Remove(_follower);
                if (bot?.ProfileId != null)
                {
                    _followersByProfileId.Remove(bot.ProfileId);
                }
                if (player.bossGroup != null)
                {
                    player.bossGroup.RemoveAlly(bot);

                }

                player.RemoveFollower(bot);
                bot.BotFollower.BossToFollow = null;
            }
        }

        public BotFollowerPlayer GetFollower(BotOwner bot)
        {
            if (bot == null) return null;

            if (!string.IsNullOrEmpty(bot.ProfileId) && _followersByProfileId.TryGetValue(bot.ProfileId, out BotFollowerPlayer cachedFollower))
            {
                return cachedFollower;
            }

            BotFollowerPlayer _follower = null;

            foreach (var item in _followers)
            {
                if (item != null && item.IsBot(bot))
                {
                    _follower = item;
                    break;
                }
            }
            return _follower;

        }

        private bool IsBoss(string id)
        {
            if (_bosses == null) return false;

            return _bosses.ContainsKey(id);
        }

        public pitAIBossPlayer GetBossPlayer(string name)
        {
            if (_bosses == null) return null;

            if (!_bosses.ContainsKey(name))
            {
                return null;
            }
            return _bosses[name];
        }

        public Dictionary<string, pitAIBossPlayer> GetBossPlayers()
        {
            return _bosses;
        }

        private List<BotFollowerPlayer> GetBossFollowers(string name)
        {
            List<BotFollowerPlayer> botFollowers = new List<BotFollowerPlayer>();

            if (_bosses == null) return botFollowers;

            if (!_bosses.ContainsKey(name) || _bosses[name] == null)
            {
                return botFollowers;
            }

            pitAIBossPlayer player = _bosses[name];

            foreach (var item in _followers)
            {
                if (item.GetBoss() == player)
                {
                    botFollowers.Add(item);
                }
            }

            return botFollowers;
        }

        public static pitAIBossPlayer? GetBoss(string name)
        {
            if (Instance == null) return null;
            return Instance.GetBossPlayer(name);
        }

        public static Dictionary<string, pitAIBossPlayer> GetBosses()
        {
            if (Instance == null) return new Dictionary<string, pitAIBossPlayer>();
            return Instance.GetBossPlayers();
        }

        public static bool IsPlayerBoss(string profileId)
        {
            if (Instance == null) return false;

            return Instance.IsBoss(profileId);
        }

        public static List<BotFollowerPlayer> GetFollowersByBoss(string bossName)
        {
            if (Instance == null) return new List<BotFollowerPlayer>();
            return Instance.GetBossFollowers(bossName);
        }

        public static List<BotFollowerPlayer> GetFollowers()
        {
            if (Instance == null) return new List<BotFollowerPlayer>();
            return Instance._followers;
        }

        public static bool IsFollowerProfileId(string profileId)
        {
            if (Instance == null || string.IsNullOrEmpty(profileId)) return false;
            return Instance._followersByProfileId.ContainsKey(profileId);
        }

        public static bool HasDeniedRecruitment(string profileId)
        {
            return Instance != null &&
                   !string.IsNullOrEmpty(profileId) &&
                   Instance._recruitmentDeniedProfileIds.Contains(profileId);
        }

        public static void RememberRecruitmentDenial(string profileId)
        {
            if (Instance == null || string.IsNullOrEmpty(profileId))
            {
                return;
            }

            Instance._recruitmentDeniedProfileIds.Add(profileId);
        }

        public static BotFollowerPlayer GetFollowerByProfileId(string profileId)
        {
            if (Instance == null || string.IsNullOrEmpty(profileId)) return null;
            Instance._followersByProfileId.TryGetValue(profileId, out BotFollowerPlayer follower);
            return follower;
        }

        public static bool IsFollower(BotOwner bot, AIBossPlayer boss = null)
        {
            if (Instance == null || bot == null) return false;
            return Instance.IsBotFollower(bot, boss) || Instance._shallBeFollower.Contains(bot.ProfileId);
        }

        public static void AddGroupToBoss(pitAIBossPlayer player, BotsGroup group)
        {
            player.bossGroup = group;
            player.bossGroup.Lock();
            // prevent group from ever making the player an enemy
            player.bossGroup.OnEnemyAdd += (IPlayer pl, EBotEnemyCause cause) =>
            {
                if (pl != null)
                {
                    if (player.Player().ProfileId == pl.ProfileId)
                    {
                        player.bossGroup.RemoveEnemy(player.Player());
                        player.bossGroup.AddAlly(player.realPlayer);
                    }
                    else if (pl.IsAI && player.bossGroup.Contains(pl.AIData.BotOwner))
                    {
                        player.bossGroup.RemoveEnemy(pl);
                        player.bossGroup.AddAlly(pl.AIData.Player);
                    }
                }
                Enemy.ForceIgnoreUntilAggressionOff(player.bossGroup);
            };
            if (!Instance._botsGroup.Contains(group.Id)) Instance._botsGroup.Add(group.Id);

            player.bossGroup.AddAlly(player.realPlayer);

            foreach (var enemy in player.GetEnemies())
            {
                player.bossGroup.AddEnemy(enemy, EBotEnemyCause.addPlayerToBoss);
            }

            Enemy.ForceIgnoreUntilAggressionOff(player.bossGroup);
        }

        public static bool IsBossGroup(int id)
        {
            if (Instance == null) return false;
            return Instance._botsGroup.Contains(id);
        }

        public static pitAIBossPlayer GetBossByGroup(int id)
        {
            if (Instance == null) return null;
            if (Instance._bosses.Count == 0) return null;
            if (Instance._botsGroup.Contains(id))
            {
                foreach (var item in Instance._bosses)
                {
                    if (item.Value.bossGroup != null && item.Value.bossGroup.Id == id)
                    {
                        return item.Value;
                    }
                }
            }

            return null;
        }

        public static void RemoveFollower(BotOwner bot, pitAIBossPlayer player)
        {
            if (Instance == null) return;
            Instance.RemoveBotFollower(bot, player);
        }

        public static pitAIBossPlayer AddPlayerAsBoss(Player player, BotsController botsController)
        {
            return Instance.AddBossPlayer(player, botsController);
        }

        public static void KillPlayerBoss(string profileId)
        {
            Instance.RemoveBossPlayer(profileId, true);
        }

        public static BotFollowerPlayer AddFollower(BotOwner bot, pitAIBossPlayer player, bool squadMate = false, WildSpawnType role = WildSpawnType.assault, string tactic = "Default", float aggression = 50f)
        {
            return Instance.AddBotFollower(bot, player, squadMate, role, tactic, aggression);
        }

        public static void ShallBeFollower(string profileId)
        {
            if (!Instance._shallBeFollower.Contains(profileId)) Instance._shallBeFollower.Add(profileId);
        }
    }
}

using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;


namespace pitTeam.Patches
{
    /** Skip checking bot's role if we have made this bot a follower of a boss player **/
    internal class BotOwnerIsFolowerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotOwner), "IsFollower");

        }

        [PatchPrefix]
        private static bool PatchPrefix(BotOwner __instance, ref bool __result)
        {

            if (BossPlayers.IsFollower(__instance))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
    /** Patch on botOwner UpdateManual to allow us to execute custom code **/
    internal class BotOwnerManualUpdatePatch : ModulePatch
    {

        public static Dictionary<string, Action<BotOwner>> BotOwnerUpdate = new Dictionary<string, Action<BotOwner>>();
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotOwner), "UpdateManual");
        }

        [PatchPostfix]
        private static void PatchPostfix(BotOwner __instance)
        {
            try
            {
                if (
                    __instance != null &&
                    __instance.BotState == EBotState.Active &&
                    __instance.GetPlayer != null &&
                    __instance.GetPlayer.HealthController != null &&
                    __instance.ProfileId != null &&
                    __instance.GetPlayer.HealthController.IsAlive
                )
                {
                    Action<BotOwner> OnUpdate;
                    BotOwnerUpdate.TryGetValue(__instance.ProfileId, out OnUpdate);
                    if (OnUpdate != null) OnUpdate(__instance);
                    if (BotOwnerUpdateHub.HasSubscribers)
                    {
                        BotOwnerUpdateHub.Invoke(__instance);
                    }

                }
            }
            catch (Exception e)
            {
                Modules.Logger.LogError("Exception on BotOwner UpdateManual PatchPostfix");
                Modules.Logger.LogError(e);
            }
        }
    }
    internal class BotOwnerActivatePatch : ModulePatch
    {
        private static List<(MongoID id, string name, string prefab)> _bearEnglishVoiceCache;
        private static List<(MongoID id, string name, string prefab)> _bearVoiceFallbackCache;

        private static bool IsEnglishBearVoice(EFT.Customization.CustomizationPlayerVoice voice)
        {
            if (voice == null) return false;

            string name = voice.Name?.ToLowerInvariant() ?? string.Empty;
            string prefab = voice.Prefab?.ToLowerInvariant() ?? string.Empty;

            // BEAR voice data uses tags in name/prefab for language variants.
            // Keep this conservative: only explicit English markers.
            return name.Contains("eng") ||
                   name.Contains("english") ||
                   prefab.Contains("eng") ||
                   prefab.Contains("english");
        }

        private static bool TryGetEnglishVoiceSeeded(string seedSource, out MongoID voiceId, out string voiceName)
        {
            voiceId = default;
            voiceName = null;

            try
            {
                EFT.CustomizationSolver solver = Singleton<EFT.CustomizationSolver>.Instance;
                if (solver == null) return false;

                if (_bearVoiceFallbackCache == null || _bearVoiceFallbackCache.Count == 0)
                {
                    _bearVoiceFallbackCache = solver
                        .GetAvailableVoices(EPlayerSide.Bear)
                        .Where(v => v != null && !string.IsNullOrEmpty(v.Name))
                        .OrderBy(v => v.Name)
                        .Select(v => (v.Id, v.Name, v.Prefab))
                        .ToList();
                }

                if (_bearEnglishVoiceCache == null)
                {
                    _bearEnglishVoiceCache = solver
                        .GetAvailableVoices(EPlayerSide.Bear)
                        .Where(v => v != null && !string.IsNullOrEmpty(v.Name) && IsEnglishBearVoice(v))
                        .OrderBy(v => v.Name)
                        .Select(v => (v.Id, v.Name, v.Prefab))
                        .ToList();
                }

                List<(MongoID id, string name, string prefab)> pool =
                    (_bearEnglishVoiceCache != null && _bearEnglishVoiceCache.Count > 0)
                    ? _bearEnglishVoiceCache
                    : _bearVoiceFallbackCache;

                if (pool == null || pool.Count == 0) return false;

                int seed = seedSource?.GetHashCode() ?? 0;
                if (seed == int.MinValue) seed = 0;
                int index = Math.Abs(seed) % pool.Count;

                voiceId = pool[index].id;
                voiceName = pool[index].name;
                return !string.IsNullOrEmpty(voiceName);
            }
            catch
            {
                return false;
            }
        }

        internal static void ApplyEnglishVoiceForProfile(Profile profile)
        {
            if (pitFireTeam.englishBear?.Value != true) return;
            if (profile == null || profile.Info == null) return;
            WildSpawnType role = profile.Info.Settings?.Role ?? WildSpawnType.assault;
            bool isBearSide = profile.Info.Side == EPlayerSide.Bear;
            bool isBearRole = role == WildSpawnType.pmcBEAR;
            if (!isBearSide && !isBearRole) return;
            if (profile.Customization == null) return;

            string seed = profile.ProfileId ?? profile.Id ?? profile.Nickname ?? string.Empty;
            if (!TryGetEnglishVoiceSeeded(seed, out MongoID englishVoiceId, out _)) return;

            try
            {
                profile.Customization[EBodyModelPart.Voice] = englishVoiceId;
            }
            catch
            {
                // Keep profile generation resilient if customization map cannot be written.
            }

        }

        private static List<Action<BotOwner>> onActivate = new List<Action<BotOwner>>
        {
           new Action<BotOwner>((BotOwner bot) =>
           {

               bool isRogue = Utils.Props.BossFollowersType.ToList().AddItem(WildSpawnType.exUsec).Contains(bot.Profile.Info.Settings.Role);

                if(Utils.Utils.FlagGet("pitFireTeam") && (bot.Side == EPlayerSide.Bear || bot.Side == EPlayerSide.Usec))
                {
                    bot.Settings.FileSettings.Boss.SHALL_WARN = false;
                    bot.Settings.FileSettings.Patrol.MAX_YDIST_TO_START_WARN_REQUEST_TO_REQUESTER = 0f;
                    bot.Settings.FileSettings.Patrol.MAX_YDIST_TO_START_WARN_REQUEST_TO_REQUESTER_ALLY = 0f;
                }

               // force bosses to always be hostile to PMCs
                if(Utils.Props.BossFollowersType.Contains(bot.Profile.Info.Settings.Role))
                {
                   bot.Settings.FileSettings.Mind.DEFAULT_USEC_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;
                   bot.Settings.FileSettings.Mind.DEFAULT_BEAR_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;
                }


                BossPlayers.GetFollowers().ForEach(follower=>{
                    var fl = follower.GetBot();
                    WildSpawnType role = bot.Profile.Info.Settings.Role;
                    pitAIBossPlayer bossPlayer = follower.GetBoss();

                    if(bossPlayer == null) return;

                    bool friendlyRogue = Utils.Utils.PlayerHasKnightQuest(bossPlayer.realPlayer.Profile);
                    // fix 0.16 bug where followers add BTR or the Rogues to their enemy list because they are spawning late
                    if(Utils.Props.friendlyBotTypes.Contains(role) || (isRogue && friendlyRogue))
                    {
                        fl.BotsGroup.RemoveEnemy(bot);
                        fl.BotsGroup.AddNeutral(bot);
                        fl.Memory.DeleteInfoAboutEnemy(bot);
                        return;
                    }

                    // fix 0.16 bug where bots are not attacking player followers
                    // ensure zombies see the followers as enemies as well
                    if(
                        fl != null && !fl.IsDead &&
                        (
                            fl.Side != bot.Side || Utils.Props.ZombieTypes.Contains(role) ||
                            ( Utils.Utils.FlagGet("isBadGuy") && fl.Side != EPlayerSide.Savage )
                        )
                    )
                    {

                        if(!bot.BotsGroup.AddEnemy(fl,EBotEnemyCause.initial))
                        {

                            bool isInEnemyList = false;
                            bool isInNeutralList = false;

                            if(bot.BotsGroup.Enemies.TryGetValue(fl,out var enemy))
                            {
                                isInEnemyList = true;
                            }

                            if(bot.BotsGroup.Neutrals.TryGetValue(fl, out var neutral))
                            {
                                isInNeutralList = true;
                            }

                            if(isInEnemyList) return;

                            var botSettingsClass = new BotGroupEnemyInfo(fl.GetPlayer, bot.BotsGroup, EBotEnemyCause.initial);

                            bot.BotsGroup.Enemies.Add(fl,botSettingsClass);
                            if(isInNeutralList)
                            {
                                bot.BotsGroup.Neutrals.Remove(fl);
                            }
                            bot.Memory.AddEnemy(fl, botSettingsClass, false);
                        }
                        // - ensure followers see zombies as enemies
                        if(Utils.Props.ZombieTypes.Contains(role))
                        {
                            fl.BotsGroup.AddEnemy(bot, EBotEnemyCause.addPlayerToBoss);
                        }
                    }
                });
            }),
            new Action<BotOwner>(FactionHostility.Apply)
        };
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotOwner), "method_10");

        }

        [PatchPostfix]
        private static void PatchPostfix(BotOwner __instance)
        {
            try
            {
                if (BossPlayers.IsFollower(__instance)) return;
                onActivate.ForEach(action => action(__instance));
            }
            catch (Exception e)
            {
                Modules.Logger.LogError(e);
            }
        }

        public static void AddOnActivate(Action<BotOwner> action)
        {
            if (!onActivate.Contains(action)) onActivate.Add(action);
        }

        public static void RemoveOnActivate(Action<BotOwner> action)
        {
            if (onActivate.Contains(action)) onActivate.Remove(action);
        }
    }

    internal class SessionLoadBotsEnglishVoicePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.ClientBackendSession), "LoadBots");
        }

        [PatchPostfix]
        private static void PatchPostfix(ref Task<Profile[]> __result)
        {
            if (pitFireTeam.englishBear?.Value != true || __result == null) return;
            __result = ApplyEnglishVoiceAsync(__result);
        }

        private static async Task<Profile[]> ApplyEnglishVoiceAsync(Task<Profile[]> originalTask)
        {
            Profile[] profiles = await originalTask;
            if (profiles == null || profiles.Length == 0) return profiles;

            for (int i = 0; i < profiles.Length; i++)
            {
                BotOwnerActivatePatch.ApplyEnglishVoiceForProfile(profiles[i]);
            }

            return profiles;
        }
    }
}

using Comfort.Common;
using EFT;
using EFT.Counters;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.Quests;
using pitTeam.Components;
using pitTeam.Modules;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

using GestureData = BotReceiverGestus;
using EventInfo = GlobalEventDispatcher.PhraseDelegateInfo;

namespace pitTeam.Patches
{
    internal static class BossGestureCommandRouter
    {
        public static bool IsBossCommandGesture(EInteraction gesture)
        {
            return gesture == EInteraction.ComeWithMeGesture ||
                   gesture == EInteraction.ThereGesture ||
                   gesture == EInteraction.HoldGesture ||
                   gesture == (EInteraction)CustomGestures.OverThere;
        }

        public static bool TryPlayOverThereGesture(Player player)
        {
            if (player == null)
            {
                return false;
            }

            pitAIBossPlayer? boss = BossPlayers.GetBoss(player.ProfileId);
            if (boss != null)
            {
                try
                {
                    InteractableObjects.CheckSeenEnemies(boss.Player());
                }
                catch (Exception ex)
                {
                    pitTeam.Modules.Logger.LogError($"Over There legacy seen-enemy scan failed; continuing with boss gesture command. {ex}");
                }

                boss.GestusShown(new GestureData
                {
                    Gesture = (EInteraction)CustomGestures.OverThere,
                    Player = player
                });
            }

            if (!player.HandsController.IsInInteractionStrictCheck())
            {
                player.MovementContext.SetInteractInHands(EInteraction.ThereGesture);
            }

            return true;
        }
    }

    internal class AIDataContructPatch : ModulePatch
    {

        public static Dictionary<string, AIData> playerAIData = new Dictionary<string, AIData>();
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Constructor(typeof(AIData), new Type[] { typeof(BotOwner), typeof(Player) });
        }
        // overwrite AIData to make it use our pitAIBossPlayer
        [PatchPostfix]
        private static void PatchPostfix(AIData __instance, BotOwner owner, Player player)
        {
            if (owner == null && player != null)
            {
                // remove old AIBossPlayer
                try
                {
                    if (__instance.AIBossPlayer != null && __instance.AIBossPlayer.GetType() != typeof(pitAIBossPlayer))
                    {
                        __instance.AIBossPlayer.Dispose();
                        pitAIBossPlayer? boss = BossPlayers.GetBoss(player.ProfileId);
                        // replace AIBossPlayer with ours
                        if (boss != null)
                        {
                            var field = AccessTools.Field(typeof(AIData), "_aIBossPlayer");
                            field.SetValue(__instance, boss);
                            Logger.LogInfo("Replaced AIBossPlayer in AIData with ours");
                        }
                    }

                }
                catch (Exception ex)
                {
                    Logger.LogError("Failed to dispose old AIBossPlayer");
                    Logger.LogError(ex);
                }



                if (!playerAIData.ContainsKey(player.ProfileId))
                    playerAIData.Add(player.ProfileId, __instance);
            }

        }
    }
    /** Handle firing TeamStatus and boss gesture commands inside PlayPhraseOrGesture so enemies do not hear command-only traffic. **/
    internal class PlayPhraseOrGesturePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GamePlayerOwner), "PlayPhraseOrGesture");
        }

        [PatchPrefix]
        private static bool PatchPrefix(GamePlayerOwner __instance, int actionId, bool aggressive)
        {
            if ((EPhraseTrigger)actionId == (EPhraseTrigger)CustomPhrases.ViewBackpack)
            {
                TeammateBackpackInspection.TryOpenFromQuickInteraction(__instance);
                return false;
            }

            pitAIBossPlayer? boss = BossPlayers.GetBoss(__instance.Player.ProfileId);
            bool isGesture = EFT.UI.Gestures.GestureCommands.IsPlayerGesture(actionId);
            EPhraseTrigger phrase = (EPhraseTrigger)actionId;

            if (!isGesture && IsLootCommandPhrase(phrase))
            {
                UnityEngine.Object liveTarget = __instance.Player?.InteractableObject as UnityEngine.Object;
                Logger.LogInfo(
                    $"[LootCommand][Route] result=menuAction phrase={phrase} " +
                    $"liveTarget='{DescribeLootTarget(liveTarget)}' " +
                    $"storedPickup='{DescribeLootTarget(InteractableObjects.GetCurLootItem())}' " +
                    $"storedBody='{DescribeLootTarget(InteractableObjects.GetCurBodyLootTarget())}' " +
                    $"storedContainer='{DescribeLootTarget(InteractableObjects.GetCurLootContainerTarget())}'");
            }

            if (!isGesture &&
                phrase == (EPhraseTrigger)CustomPhrases.TeamStatus)
            {

                if (boss != null)
                {
                    EventInfo info = new EventInfo
                    {
                        phrase = (EPhraseTrigger)CustomPhrases.TeamStatus,
                        PlayerRequester = __instance.Player
                    };

                    boss.PhraseSaid(info);

                }
                return false;

            }
            else
            {
                // fix for boss gestures not propagating to followers
                EInteraction gesture = (EInteraction)actionId;
                if (gesture == (EInteraction)CustomGestures.OverThere)
                {
                    BossGestureCommandRouter.TryPlayOverThereGesture(__instance.Player);
                    return false;
                }

                if (boss != null && BossGestureCommandRouter.IsBossCommandGesture(gesture))
                {
                    boss.GestusShown(new GestureData
                    {
                        Gesture = gesture,
                        Player = __instance.Player
                    });
                }

            }

            return true;
        }

        private static bool IsLootCommandPhrase(EPhraseTrigger phrase)
        {
            return phrase == EPhraseTrigger.LootGeneric ||
                   phrase == EPhraseTrigger.LootWeapon ||
                   phrase == EPhraseTrigger.LootKey ||
                   phrase == EPhraseTrigger.LootMoney ||
                   phrase == EPhraseTrigger.CheckHim ||
                   phrase == EPhraseTrigger.LootBody ||
                   phrase == EPhraseTrigger.LootContainer;
        }

        private static string DescribeLootTarget(UnityEngine.Object target)
        {
            return target != null
                ? $"{target.name}#{target.GetInstanceID()}"
                : "none";
        }
    }

    internal class QuickMumbleStartViewBackpackPatch : ModulePatch
    {
        private static readonly FieldInfo BattleUiControllerField = AccessTools.Field(typeof(GamePlayerOwner), "BattleUIScreenController");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GamePlayerOwner), nameof(GamePlayerOwner.QuickMumbleStart));
        }

        [PatchPrefix]
        private static bool PatchPrefix(GamePlayerOwner __instance)
        {
            EPhraseTrigger viewBackpackPhrase = (EPhraseTrigger)CustomPhrases.ViewBackpack;
            EFT.UI.IBattleUIScreenController battleUi = BattleUiControllerField.GetValue(__instance) as EFT.UI.IBattleUIScreenController;
            if (battleUi?.GesturesQuickPanel?.PrioritizedCommand != viewBackpackPhrase)
            {
                return true;
            }

            battleUi.GesturesQuickPanel.ActivateCommand();
            TeammateBackpackInspection.TryOpenFromQuickInteraction(__instance);
            return false;
        }
    }

    /** Have follower kills grant the same raid XP counters used by the legacy plugin. **/
    internal class PlayerKilledPatch : ModulePatch
    {
        private const string KillMessageRoute = "/singleplayer/pitfireteam/postraid/kill-message";
        private static readonly HashSet<string> RecordedKillMessageVictims = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> PlayerDamagedPmcVictims = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> FriendlyBeforePlayerDamageVictims = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> RecordedDeadSquadmates = new HashSet<string>(StringComparer.Ordinal);
        private static readonly object RecordedKillMessageLock = new object();
        private static readonly object RecordedDeadSquadmatesLock = new object();
        private static FieldInfo _questControllerProfileField;
        private static bool _questControllerProfileFieldPrepared;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), "OnBeenKilledByAggressor");
        }

        [PatchPrefix]
        private static void PatchPrefix(Player __instance, IPlayer aggressor)
        {
            try
            {
                global::pitTeam.Utils.PingTeamates.TryRememberEnemyDown(__instance, aggressor);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        [PatchPostfix]
        private static void PatchPostfix(Player __instance, IPlayer aggressor, EFT.Ballistics.DamageInfo damageInfo, EBodyPart bodyPart, EDamageType lethalDamageType)
        {
            try
            {
                TryRecordDeadSquadmate(__instance, aggressor, bodyPart, lethalDamageType);
                TryRecordPlayerKillMessage(__instance, aggressor);

                if (aggressor?.Profile?.Info?.Settings == null || __instance?.GameWorld == null)
                {
                    return;
                }

                Player followerPlayer = __instance.GameWorld.GetAlivePlayerByProfileID(aggressor.ProfileId);
                if (followerPlayer == null)
                {
                    return;
                }

                BotFollowerPlayer follower = BossPlayers.GetFollowers().Find(fl => fl.GetBot().ProfileId == followerPlayer.ProfileId);
                if (follower == null || !follower.IsSquadMate)
                {
                    return;
                }

                SquadRaidKillReport.RecordTeammateKill(followerPlayer, __instance);
                TryCreditFollowerKillQuestProgress(__instance, followerPlayer, follower, damageInfo, bodyPart);

                EPlayerSide killedPlayerSide = __instance.Side;
                EFT.Counters.CountersCollection sessionCounters = followerPlayer.Profile.EftStats.SessionCounters;
                var experienceSettings = Singleton<EFT.GlobalConfiguration>.Instance.Experience;

                float headshotMultiplier = 0f;
                if (bodyPart == EBodyPart.Head)
                {
                    if (killedPlayerSide - EPlayerSide.Usec > 1)
                    {
                        if (killedPlayerSide == EPlayerSide.Savage)
                        {
                            headshotMultiplier = experienceSettings.Kill.BotHeadShotMult;
                        }
                    }
                    else
                    {
                        headshotMultiplier = experienceSettings.Kill.PmcHeadShotMult;
                    }
                }

                int killExperience = __instance.Profile.Info.Settings.Experience;
                int baseKillExperience = killExperience;
                switch (killedPlayerSide)
                {
                    case EPlayerSide.Usec:
                    case EPlayerSide.Bear:
                        baseKillExperience = experienceSettings.Kill.VictimLevelExp;
                        break;
                    case EPlayerSide.Savage:
                        if (baseKillExperience < 0)
                        {
                            baseKillExperience = experienceSettings.Kill.VictimBotLevelExp;
                        }

                        break;
                }

                int killCount = sessionCounters.GetInt(EFT.Counters.PredefinedCounters.Kills);
                sessionCounters.AddInt(1, EFT.Counters.PredefinedCounters.Kills);
                float streakMultiplier = (float)experienceSettings.Kill.GetKillingBonusPercent(killCount) / 100f;
                int bodyPartBonus = (int)(baseKillExperience * headshotMultiplier);
                int streakBonus = (int)(baseKillExperience * streakMultiplier);

                if (baseKillExperience > 0)
                {
                    sessionCounters.AddInt(baseKillExperience, EFT.Counters.PredefinedCounters.ExpKillBase);
                }

                if (bodyPartBonus > 0)
                {
                    sessionCounters.AddInt(bodyPartBonus, EFT.Counters.PredefinedCounters.ExpKillBodyPartBonus);
                }

                if (streakBonus > 0)
                {
                    sessionCounters.AddInt(streakBonus, EFT.Counters.PredefinedCounters.ExpKillStreakBonus);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        internal static void TryRecordDeadSquadmate(
            Player player,
            IPlayer aggressor,
            EBodyPart bodyPart,
            EDamageType lethalDamageType)
        {
            try
            {
                if (player == null || string.IsNullOrWhiteSpace(player.ProfileId))
                {
                    return;
                }

                lock (RecordedDeadSquadmatesLock)
                {
                    if (!RecordedDeadSquadmates.Add(player.ProfileId))
                    {
                        return;
                    }
                }

                BotFollowerPlayer deadFollower = BossPlayers.GetFollowers()
                    .Find(follower => follower?.GetBot()?.ProfileId == player.ProfileId);
                if (deadFollower == null || !deadFollower.IsSquadMate)
                {
                    lock (RecordedDeadSquadmatesLock)
                    {
                        RecordedDeadSquadmates.Remove(player.ProfileId);
                    }

                    return;
                }

                // A pending hands switch may finish after follower roster/chat cleanup begins.
                // Preserve exact follower identity for the OnWeaponTaken death guard before any
                // cleanup can make BossPlayers.IsFollower(...) return false.
                FollowerWeaponSwitchPolicyRuntime.MarkDeadFollowerWeaponCallbacks(deadFollower.GetBot());
                BattleRecorder.RecordFollowerDeath(
                    deadFollower,
                    player,
                    aggressor,
                    bodyPart,
                    lethalDamageType);
                NpcMessage.RemoveNpc(player.ProfileId, true);
                FollowerDeathEscapeResolver.RecordFallenSquadmate(player);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Failed to record dead squadmate for loadout loss");
                Modules.Logger.LogError(ex);
            }
        }

        internal static void ReleaseDeadSquadmateRecord(Player player)
        {
            if (player == null || string.IsNullOrWhiteSpace(player.ProfileId))
            {
                return;
            }

            lock (RecordedDeadSquadmatesLock)
            {
                RecordedDeadSquadmates.Remove(player.ProfileId);
            }
        }

        internal static void TryRememberFriendlyPmcBeforePlayerDamage(EFT.Ballistics.DamageInfo damageInfo, Player target)
        {
            IPlayer attacker = damageInfo.Player?.iPlayer;
            if (attacker == null ||
                !attacker.IsYourPlayer ||
                target == null ||
                !target.IsAI ||
                target.Side != attacker.Side ||
                !IsPmc(target.Side) ||
                string.IsNullOrWhiteSpace(target.ProfileId) ||
                !IsFriendlyPmcKillMessageContextEnabled())
            {
                return;
            }

            BotOwner targetBot = target.AIData?.BotOwner;
            BotsGroup targetGroup = targetBot?.BotsGroup;
            if (targetBot == null || targetGroup == null || targetBot.EnemiesController == null)
            {
                return;
            }

            lock (RecordedKillMessageLock)
            {
                // The first direct player hit is the relationship boundary. Later hits must not
                // turn a bot that was already hostile into an eligible friendly-fire victim.
                if (!PlayerDamagedPmcVictims.Add(target.ProfileId))
                {
                    return;
                }

                bool wasAlreadyEnemy =
                    targetGroup.IsEnemy(attacker) ||
                    targetBot.EnemiesController.IsEnemy(attacker) ||
                    string.Equals(
                        targetBot.Memory?.GoalEnemy?.Person?.ProfileId,
                        attacker.ProfileId,
                        StringComparison.Ordinal);

                if (!wasAlreadyEnemy)
                {
                    FriendlyBeforePlayerDamageVictims.Add(target.ProfileId);
                }
            }
        }

        internal static void ResetKillMessageRaidState()
        {
            lock (RecordedKillMessageLock)
            {
                RecordedKillMessageVictims.Clear();
                PlayerDamagedPmcVictims.Clear();
                FriendlyBeforePlayerDamageVictims.Clear();
            }
        }

        private static void TryRecordPlayerKillMessage(Player victim, IPlayer aggressor)
        {
            try
            {
                if (victim == null || aggressor == null || !aggressor.IsYourPlayer || !victim.IsAI)
                {
                    return;
                }

                string messageKind = GetKillMessageKind(victim, aggressor);
                if (string.IsNullOrEmpty(messageKind))
                {
                    return;
                }

                bool messagesEnabled = pitFireTeam.npcSendMessage?.Value == true;
                string messageText = messagesEnabled ? GetKillMessageText(messageKind) : string.Empty;
                if (messagesEnabled && string.IsNullOrWhiteSpace(messageText))
                {
                    return;
                }

                string victimProfileId = victim.ProfileId;
                if (string.IsNullOrEmpty(victimProfileId))
                {
                    return;
                }

                lock (RecordedKillMessageLock)
                {
                    if (!RecordedKillMessageVictims.Add(victimProfileId))
                    {
                        return;
                    }
                }

                string json = JsonConvert.SerializeObject(new
                {
                    victimProfileId,
                    victimAccountId = victim.AccountId,
                    messageKind,
                    messageText,
                });

                Task.Run(() =>
                {
                    try
                    {
                        RequestHandler.PostJson(KillMessageRoute, json);
                    }
                    catch (Exception ex)
                    {
                        Modules.Logger.LogError($"Failed to record post-raid kill message for {victim.Profile?.Info?.Nickname}");
                        Modules.Logger.LogError(ex);
                    }
                });
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Failed to classify post-raid kill message");
                Modules.Logger.LogError(ex);
            }
        }

        private static string GetKillMessageKind(Player victim, IPlayer aggressor)
        {
            if (!IsFriendlyPmcKillMessageContextEnabled() ||
                !WasFriendlyBeforePlayerDamage(victim.ProfileId))
            {
                return string.Empty;
            }

            BotFollowerPlayer follower = BossPlayers.GetFollowerByProfileId(victim.ProfileId);
            if (follower != null)
            {
                return follower.IsSquadMate ? string.Empty : "traitor";
            }

            return IsPmc(victim.Side) && victim.Side == aggressor.Side ? "jerk" : string.Empty;
        }

        private static bool WasFriendlyBeforePlayerDamage(string victimProfileId)
        {
            if (string.IsNullOrWhiteSpace(victimProfileId))
            {
                return false;
            }

            lock (RecordedKillMessageLock)
            {
                return FriendlyBeforePlayerDamageVictims.Contains(victimProfileId);
            }
        }

        private static bool IsFriendlyPmcKillMessageContextEnabled()
        {
            return pitFireTeam.pitFireTeamFLAG?.Value == true && pitFireTeam.badGuy?.Value != true;
        }

        private static bool IsPmc(EPlayerSide side)
        {
            return side == EPlayerSide.Bear || side == EPlayerSide.Usec;
        }

        private static string GetKillMessageText(string messageKind)
        {
            string[] messages = messageKind == "traitor"
                ? pitFireTeam.optionsLang?.traitorKillMessages
                : pitFireTeam.optionsLang?.jerkKillMessages;

            if (messages == null || messages.Length == 0)
            {
                return string.Empty;
            }

            return messages[UnityEngine.Random.Range(0, messages.Length)];
        }

        private static void TryCreditFollowerKillQuestProgress(
            Player victim,
            Player followerPlayer,
            BotFollowerPlayer follower,
            EFT.Ballistics.DamageInfo damageInfo,
            EBodyPart bodyPart)
        {
            pitAIBossPlayer boss = follower.GetBoss();
            if (boss?.realPlayer?.QuestController == null)
            {
                return;
            }

            if (Vector3.Distance(followerPlayer.Position, boss.Position) > 80f)
            {
                return;
            }

            List<string> targets = BuildQuestKillTargets(victim.Side);
            if (targets.Count == 0)
            {
                return;
            }

            EFT.Quests.QuestController questController = boss.realPlayer.QuestController;
            Profile originalProfile = null;

            try
            {
                FieldInfo profileField = GetQuestControllerProfileField();
                if (profileField != null)
                {
                    originalProfile = profileField.GetValue(questController) as Profile;
                    profileField.SetValue(questController, followerPlayer.Profile);
                }

                string location = followerPlayer.Location;
                float distance = Vector3.Distance(followerPlayer.Position, victim.Position);
                HealthEffects victimEffects = victim.HealthController?.BodyPartEffects;
                HealthEffects followerEffects = followerPlayer.HealthController?.BodyPartEffects;
                string[] followerBuffs = followerPlayer.HealthController?.ActiveBuffsNames() ?? Array.Empty<string>();
                List<string> victimZones = victim.TriggerZones ?? new List<string>();
                List<string> victimEquipment = victim.Inventory?.EquippedInSlotsTemplateIds ?? new List<string>();
                string victimRole = victim.Profile?.Info?.Settings?.Role.ToStringNoBox<WildSpawnType>() ?? string.Empty;

                foreach (string target in targets)
                {
                    questController.CheckKillConditionCounter(
                        target,
                        victim.ProfileId,
                        victimEquipment,
                        damageInfo.Weapon,
                        bodyPart,
                        location,
                        distance,
                        victimRole,
                        victim.CurrentHour,
                        victimEffects,
                        followerEffects,
                        victimZones,
                        followerBuffs);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            finally
            {
                try
                {
                    FieldInfo profileField = GetQuestControllerProfileField();
                    if (profileField != null && originalProfile != null)
                    {
                        profileField.SetValue(questController, originalProfile);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex);
                }
            }
        }

        private static List<string> BuildQuestKillTargets(EPlayerSide victimSide)
        {
            List<string> targets = new List<string> { "Any" };
            switch (victimSide)
            {
                case EPlayerSide.Usec:
                    targets.Add("Usec");
                    targets.Add("AnyPmc");
                    break;
                case EPlayerSide.Bear:
                    targets.Add("Bear");
                    targets.Add("AnyPmc");
                    break;
                case EPlayerSide.Savage:
                    targets.Add("Savage");
                    targets.Add("Bot");
                    break;
            }

            return targets;
        }

        private static FieldInfo GetQuestControllerProfileField()
        {
            if (_questControllerProfileFieldPrepared)
            {
                return _questControllerProfileField;
            }

            _questControllerProfileFieldPrepared = true;
            _questControllerProfileField = typeof(EFT.Quests.QuestController).GetField("Profile", BindingFlags.Public | BindingFlags.Instance);
            if (_questControllerProfileField == null)
            {
                return null;
            }

            FieldInfo attributesField = typeof(FieldInfo).GetField("m_fieldAttributes", BindingFlags.NonPublic | BindingFlags.Instance);
            attributesField?.SetValue(_questControllerProfileField, _questControllerProfileField.Attributes & ~FieldAttributes.InitOnly);
            return _questControllerProfileField;
        }
    }

    internal sealed class PlayerDeadFallbackPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), "OnDead");
        }

        [PatchPostfix]
        private static void PatchPostfix(Player __instance, EDamageType damageType)
        {
            try
            {
                PlayerKilledPatch.TryRecordDeadSquadmate(
                    __instance,
                    aggressor: null,
                    bodyPart: EBodyPart.Common,
                    lethalDamageType: damageType);
            }
            finally
            {
                PlayerKilledPatch.ReleaseDeadSquadmateRecord(__instance);
            }
        }
    }

    /** Report the exact target line when the player fires during a fight. **/
    internal sealed class PlayerMakingShotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), nameof(Player.OnMakingShot));
        }

        [PatchPostfix]
        private static void PatchPostfix(Player __instance)
        {
            pitAIBossPlayer? bossPlayer = BossPlayers.GetBoss(__instance.ProfileId);
            if (bossPlayer == null)
            {
                return;
            }

            Vector3 shotOrigin = __instance.PlayerBones?.WeaponRoot?.position ??
                                 (__instance.Position + Vector3.up * 1.2f);
            Vector3 shotDirection = __instance.LookDirection;
            if (__instance.HandsController is Player.FirearmController firearmController)
            {
                shotOrigin = firearmController.FireportPosition;
                shotDirection = firearmController.WeaponDirection;
            }

            bossPlayer.MarkBossShot(shotOrigin, shotDirection);
        }
    }

    /** Have followers increase weapon skills when they shoot. **/
    internal class PlayerShotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), "ExecuteShotSkill");
        }

        [PatchPrefix]
        private static bool PatchPrefix(Player __instance, Item weapon)
        {
            pitAIBossPlayer? bossPlayer = BossPlayers.GetBoss(__instance.ProfileId);

            foreach (var follower in BossPlayers.GetFollowers())
            {
                if (!follower.IsSquadMate || follower.GetBot().ProfileId != __instance.ProfileId)
                {
                    continue;
                }

                if (weapon is not EFT.InventoryLogic.ThrowWeap)
                {
                    __instance.Skills.WeaponShotAction.Complete(weapon, 1f);
                }

                return false;
            }

            return true;
        }
    }
}

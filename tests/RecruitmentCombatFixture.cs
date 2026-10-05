// Controlled game stand-ins for the production recruitment patch and SAIN reflection bridge.
using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using UnityEngine;
using pitTeam;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Patches;

namespace UnityEngine
{
    public struct Vector3 { public float x, y, z; public static Vector3 zero => default; }
    public static class Time { public static float time; }
    public static class Random { public static int Range(int min, int max) => min; }
}
namespace EFT.Interactive { public enum EInteraction { NoGesture, GetOffGesture, OkGesture } }
namespace EFT
{
    public enum EPhraseTrigger { None, Negative, DontKnow, Roger, Toxic, MumblePhrase, OnMutter, OnFight, OnBeingHurt, OnEnemyGrenade }
    public enum ETagStatus { Coop, Solo, Unaware }
    public enum EPlayerSide { Usec, Savage }
    public enum EBotState { Active, Inactive }
    public interface IPlayer { string ProfileId { get; } EPlayerSide Side { get; } Profile Profile { get; } }
    public class Profile { public Info Info = new(); public FenceInfo FenceInfo = new(); }
    public class Info { public int Level = 20; }
    public class FenceInfo { public double Standing = 6; }
    public class Health { public bool IsAlive = true; }
    public class Speaker
    {
        public bool Speaking, Busy;
        public object Play(EPhraseTrigger phrase, ETagStatus mask, bool force, object unused) => new();
    }
    public class Player : IPlayer
    {
        public bool IsAI = true;
        public AIData AIData = new();
        public EPhraseTrigger Spoken;
        public string ProfileId { get; set; } = "boss";
        public EPlayerSide Side { get; set; }
        public Profile Profile { get; } = new();
        public Health HealthController = new(); public Speaker Speaker = new(); public Vector3 Position;
        public void Say(EPhraseTrigger phrase) { if (NativeSpeech.PlayerPrefix(this, phrase)) Spoken = phrase; }
    }
    public class AIData { public BotOwner BotOwner; }
    public class EnemyInfo { public string ProfileId; public Player Person; }
    public class Memory { public EnemyInfo GoalEnemy; public bool HaveEnemy => GoalEnemy != null; }
    public class BotTalk
    {
        public BotOwner _owner;
        public int Queued;
        public bool QueueRequests;
        public EPhraseTrigger Last;
        public void TrySay(EPhraseTrigger phrase, bool withGroupDelay = true)
        {
            if (QueueRequests) Queued++;
            else Say(phrase, false);
        }
        public void Say(EPhraseTrigger phrase, bool immediately)
        {
            if (!NativeSpeech.BotPrefix(this, phrase)) return;
            Last = phrase;
            _owner.GetPlayer.Say(phrase);
        }
        public void SetSilence(float seconds) { }
        public void DropNextSayPeriod() { }
    }
    public class Gesture { public void TryGestus(EFT.Interactive.EInteraction interaction, bool force) { } }
    public class Boss { public bool IsMe(Player player) => true; }
    public class BotFollower { public bool HaveBoss; public Boss BossToFollow = new(); }
    public class Group { public int MembersCount; }
    public class BotOwner
    {
        public BotOwner() { BotTalk._owner = this; GetPlayer.AIData.BotOwner = this; }
        public string ProfileId = "candidate"; public EPlayerSide Side; public Profile Profile = new();
        public bool IsDead, IsFollower; public EBotState BotState = EBotState.Active;
        public Player GetPlayer = new(); public Memory Memory = new(); public BotTalk BotTalk = new();
        public Gesture Gesture = new(); public BotFollower BotFollower = new(); public Group BotsGroup;
        public bool IsEnemyLookingAtMe(EnemyInfo enemy) => false;
    }
    public class BotGroupRequestController { }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
    public class PatchPrefixAttribute : Attribute { }
}
namespace pitTeam.BigBrain { }
namespace pitTeam.Components
{
    public class pitAIBossPlayer { public Group bossGroup; public Player Value = new(); public Player Player() => Value; }
    public class BotFollowerPlayer { public bool IsSquadMate; public BotOwner Bot; public BotOwner GetBot() => Bot; }
}
namespace pitTeam
{
    public class Setting<T> { public T Value; public Setting(T value) { Value = value; } }
    public static class pitFireTeam
    {
        public static bool IsSAINInstalled = true;
        public static Setting<bool> pickupEnabled = new(true), tieredPickup = new(true);
        public static Setting<int> maximumPickup = new(10);
        public static Logger Log = new();
        public static bool ShouldDisableSainForFollower(BotOwner bot) => false;
    }
}
namespace pitTeam.Modules
{
    public class Logger
    {
        public static int Warnings;
        public void LogWarning(string message) => Warnings++;
        public static void LogError(object error) => throw new Exception("Unexpected recruitment failure", error as Exception);
    }
    public class BossPlayers
    {
        public static BossPlayers Instance = new(); public static pitAIBossPlayer Boss = new();
        public static HashSet<string> Denied = new(); public static int Added;
        public pitAIBossPlayer GetBossPlayer(string id) => Boss;
        public static bool IsFollower(BotOwner bot) => bot.IsFollower;
        public static bool HasDeniedRecruitment(string id) => Denied.Contains(id);
        public static void RememberRecruitmentDenial(string id) => Denied.Add(id);
        public static List<BotFollowerPlayer> GetFollowersByBoss(string id) => new();
        public static object AddFollower(BotOwner bot, pitAIBossPlayer boss) { Added++; return new(); }
    }
}
namespace pitTeam.Patches
{
    public static class BotOwnerManualUpdatePatch { public static Dictionary<string, Action<BotOwner>> BotOwnerUpdate = new(); }
}
namespace pitTeam.Utils
{
    public static class Utils
    {
        public static Action Pending;
        public static void SetTimeout(Action callback, int delayMs) => Pending = callback;
    }
}
namespace SAIN.Plugin
{
    public class NativeBot
    {
        public bool ActiveEnemy, Throw;
        public bool HasEnemy => Throw ? throw new InvalidOperationException("native probe failed") : ActiveEnemy;
    }
    public static class SAINEnableClass
    {
        public static NativeBot Bot;
        public static bool GetSAIN(string id, out NativeBot bot) { bot = Bot; return bot != null; }
    }
}
public static class RecruitmentCombatChecks
{
    private static int count;
    private static readonly MethodInfo Prefix = typeof(FollowRequestPatch).GetMethod("PatchPrefix", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly MethodInfo Complete = typeof(FollowRequestPatch).GetMethod("CompleteRecruitConversion", BindingFlags.NonPublic | BindingFlags.Static);
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
    private static BotOwner Fresh()
    {
        BossPlayers.Denied.Clear(); BossPlayers.Added = 0; BossPlayers.Boss = new();
        BotOwnerManualUpdatePatch.BotOwnerUpdate.Clear(); pitTeam.Utils.Utils.Pending = null;
        pitFireTeam.IsSAINInstalled = true; pitFireTeam.pickupEnabled.Value = true;
        pitFireTeam.tieredPickup.Value = true; pitFireTeam.maximumPickup.Value = 10;
        SAIN.Plugin.SAINEnableClass.Bot = new();
        Logger.Warnings = 0;
        return new();
    }
    private static void Ask(BotOwner bot)
    {
        object[] args = { new BotGroupRequestController(), true, BossPlayers.Boss.Value, bot };
        Check(!(bool)Prefix.Invoke(null, args) && !(bool)args[1], "Recruitment owns the request result");
    }
    private static void Deferred(BotOwner bot) => BotOwnerManualUpdatePatch.BotOwnerUpdate[bot.ProfileId](bot);
    public static int Run()
    {
        var bot = Fresh(); bot.Memory.GoalEnemy = new(); bot.Profile.Info.Level = 40; Ask(bot);
        Check(bot.BotTalk.Last == EPhraseTrigger.DontKnow && !BossPlayers.HasDeniedRecruitment(bot.ProfileId), "EFT combat does not cache a level refusal");
        bot = Fresh(); bot.Profile.Info.Level = 40; SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = true; Ask(bot); Ask(bot);
        Check(bot.Memory.GoalEnemy == null && bot.BotTalk.Last == EPhraseTrigger.DontKnow && BossPlayers.Denied.Count == 0 && BotOwnerManualUpdatePatch.BotOwnerUpdate.Count == 0, "SAIN-only combat retries stay temporary");
        SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = false; bot.Profile.Info.Level = 1; Ask(bot); Deferred(bot);
        pitTeam.Utils.Utils.Pending(); Check(BossPlayers.Added == 1, "Combat-only refusal permits recruitment after combat");
        bot = Fresh(); bot.Profile.Info.Level = 40; Ask(bot);
        Check(bot.BotTalk.Last == EPhraseTrigger.Negative && BossPlayers.HasDeniedRecruitment(bot.ProfileId), "Peaceful level denial is cached");
        SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = true; Ask(bot);
        Check(bot.BotTalk.Last == EPhraseTrigger.DontKnow && BossPlayers.HasDeniedRecruitment(bot.ProfileId), "Combat takes priority while preserving earlier refusal");
        SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = false; bot.Profile.Info.Level = 1; Ask(bot);
        Check(bot.BotTalk.Last == EPhraseTrigger.Negative && BotOwnerManualUpdatePatch.BotOwnerUpdate.Count == 0, "Earlier refusal remains final after combat");
        bot = Fresh(); bot.Profile.Info.Level = 1; Ask(bot);
        SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = true; Deferred(bot);
        Check(bot.BotTalk.Last == EPhraseTrigger.DontKnow && BossPlayers.Added == 0 && BossPlayers.Denied.Count == 0 && pitTeam.Utils.Utils.Pending == null, "Combat before manual update prevents conversion");
        bot = Fresh(); bot.Profile.Info.Level = 1; Ask(bot); bot.Memory.GoalEnemy = new(); Deferred(bot);
        Check(bot.BotTalk.Last == EPhraseTrigger.DontKnow && BossPlayers.Added == 0 && BossPlayers.Denied.Count == 0, "Deferred EFT combat remains temporary");
        bot = Fresh(); bot.Profile.Info.Level = 1; Ask(bot); BossPlayers.Denied.Add(bot.ProfileId);
        SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = true; Deferred(bot);
        Check(bot.BotTalk.Last == EPhraseTrigger.DontKnow && BossPlayers.Added == 0, "Deferred combat precedes cached denial");
        bot = Fresh(); bot.Profile.Info.Level = 1; Ask(bot); Deferred(bot);
        SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = true; pitTeam.Utils.Utils.Pending();
        Check(bot.BotTalk.Last == EPhraseTrigger.DontKnow && BossPlayers.Added == 0 && BossPlayers.Denied.Count == 0, "Combat during first-conversion delay prevents conversion");
        BossPlayers.Denied.Add(bot.ProfileId); Complete.Invoke(null, new object[] { bot, BossPlayers.Boss });
        Check(bot.BotTalk.Last == EPhraseTrigger.DontKnow, "Final conversion combat precedes cached denial");
        SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = false; Complete.Invoke(null, new object[] { bot, BossPlayers.Boss });
        Check(bot.BotTalk.Last == EPhraseTrigger.Negative && BossPlayers.Added == 0, "Final conversion still enforces peaceful cached denial");
        bot = Fresh(); pitFireTeam.IsSAINInstalled = false; SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = true;
        Check(!SainGoalEnemyBridge.HasEnemy(bot), "SAIN absence ignores native fixture");
        bot.Memory.GoalEnemy = new(); Ask(bot); Check(bot.BotTalk.Last == EPhraseTrigger.DontKnow, "SAIN absence preserves EFT combat gate");
        bot = Fresh(); SAIN.Plugin.SAINEnableClass.Bot = null;
        Check(!SainGoalEnemyBridge.HasEnemy(bot), "Missing SAIN component is safe");
        Check(!SainGoalEnemyBridge.HasEnemy(null), "Null owner is safe");
        SAIN.Plugin.SAINEnableClass.Bot = new() { Throw = true };
        Check(!SainGoalEnemyBridge.HasEnemy(bot), "Reflection failure is contained");
        Check(Logger.Warnings == 1 && !SainGoalEnemyBridge.HasEnemy(bot) && Logger.Warnings == 1, "Reflection failure logs once");
        return count;
    }
}

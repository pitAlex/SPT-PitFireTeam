using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using pitTeam;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Patches;
using UnityEngine;

namespace pitTeam { /* CUSTOM_PHRASES */ }
namespace pitTeam.Patches { /* ROUTER */ /* INPUT_PATCHES */ }
namespace SPT.Reflection.Patching
{
    internal abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
    internal sealed class PatchPrefixAttribute : Attribute { }
}
namespace HarmonyLib
{
    internal static class AccessTools
    {
        public static FieldInfo Field(Type type, string name) => type.GetField(name);
        public static MethodInfo Method(Type type, string name) => type.GetMethod(name);
        public static MethodInfo Method(Type type, string name, Type[] args) => type.GetMethod(name, args);
    }
}
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 zero => new Vector3(0, 0, 0);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized => this * (1f / (float)Math.Sqrt(sqrMagnitude));
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
        public static float Distance(Vector3 a, Vector3 b) => (float)Math.Sqrt((a - b).sqrMagnitude);
        public static float Angle(Vector3 a, Vector3 b) => (float)(Math.Acos(Math.Max(-1, Math.Min(1,
            (a.x * b.x + a.y * b.y + a.z * b.z) / Math.Sqrt(a.sqrMagnitude * b.sqrMagnitude)))) * 180 / Math.PI);
    }
    public struct Ray { public Vector3 origin, direction; }
}
namespace EFT
{
    public enum EPhraseTrigger { PhraseNone, Cooperation }
    public enum EBotState { Active, Inactive }
    public enum BodyPartType { body }
    public sealed class BodyPart { public Vector3 Position; }
    public sealed class Health { public bool IsAlive = true; }
    public sealed class AIData { public BotOwner BotOwner; }
    public sealed class Player
    {
        public bool IsAI = true;
        public Health HealthController = new Health();
        public Vector3 Position;
        public Player InteractablePlayer;
        public AIData AIData;
        public Ray InteractionRay;
        public Dictionary<BodyPartType, BodyPart> MainParts = null;
    }
    public sealed class BotOwner
    {
        public EBotState BotState = EBotState.Active;
        public bool IsDead = false;
        public bool HealPending;
        public Player GetPlayer;
    }
    public sealed class GamePlayerOwner
    {
        public EFT.UI.IBattleUIScreenController BattleUIScreenController;
        public int Spoken;
        public void QuickMumbleStart() { }
        public void NativeSelected(EPhraseTrigger phrase) { Spoken++; }
    }
}
namespace EFT.UI
{
    public interface IBattleUIScreenController { EFT.UI.Gestures.GesturesQuickPanel GesturesQuickPanel { get; } }
    public sealed class BattleUi : IBattleUIScreenController
    {
        public EFT.UI.Gestures.GesturesQuickPanel GesturesQuickPanel { get; set; }
    }
}
namespace EFT.UI.Gestures
{
    public sealed class GesturesQuickPanel
    {
        public bool DropdownPanelActive = false;
        public EPhraseTrigger PrioritizedCommand;
        public EPhraseTrigger Activation;
        public int Activations;
        public EPhraseTrigger ActivateCommand() { Activations++; return Activation; }
        public void CloseDropdown(Action<EPhraseTrigger> onPhraseSelected) { }
    }
}
namespace pitTeam.Components
{
    public sealed class Boss { public Player realPlayer; }
    public sealed class BotFollowerPlayer
    {
        public bool IsSquadMate;
        public bool IsSpawnedSquadMate;
        public BotOwner Bot;
        public Boss Boss;
        public BotOwner GetBot() => Bot;
        public Boss GetBoss() => Boss;
    }
}
namespace pitTeam.Modules
{
    internal static class TeammateGearSwap
    {
        internal static int OpenCalls;
        internal static void Open(GamePlayerOwner owner) { OpenCalls++; }
    }
    public sealed class BossPlayers
    {
        public static BossPlayers Instance = new BossPlayers();
        public static readonly List<BotFollowerPlayer> Followers = new List<BotFollowerPlayer>();
        public static List<BotFollowerPlayer> GetFollowers() => Followers;
        public BotFollowerPlayer GetFollower(BotOwner bot) => Followers.Find(f => f != null && bot != null && f.Bot == bot);
    }
    internal static class TeammateBackpackInspection
    {
        private const float MaxInteractionDistance = 2.5f;
        private const float QuickInteractionMaxAngle = 18f;
        public static int OpenCalls;
        public static bool TryOpenFromQuickInteraction(GamePlayerOwner owner) { OpenCalls++; return true; }
        public static bool HasActiveOrPendingHealWork(BotOwner bot) => bot.HealPending;
        /* SWAP_TARGET */
        /* TARGET_RESOLUTION */
    }
}
internal static class QuickInteractionFixture
{
    private static int _assertions;
    private static readonly EPhraseTrigger Backpack = (EPhraseTrigger)CustomPhrases.ViewBackpack;
    private static readonly EPhraseTrigger Swap = (EPhraseTrigger)CustomPhrases.SwapGear;
    private static bool Quick(GamePlayerOwner owner)
    {
        return (bool)typeof(QuickMumbleStartViewBackpackPatch).GetMethod("PatchPrefix", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { owner });
    }
    private static Action<EPhraseTrigger> Wrap(Action<EPhraseTrigger> original)
    {
        object[] args = { original };
        typeof(FollowerQuickInteractionDropdownPatch).GetMethod("PatchPrefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        return (Action<EPhraseTrigger>)args[0];
    }
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _assertions++;
    }
    public static void Main()
    {
        var panel = new EFT.UI.Gestures.GesturesQuickPanel { PrioritizedCommand = Backpack, Activation = Backpack };
        var owner = new GamePlayerOwner { BattleUIScreenController = new EFT.UI.BattleUi { GesturesQuickPanel = panel } };
        Check(!Quick(owner) && TeammateBackpackInspection.OpenCalls == 1, "existing custom quick dispatch opens backpack");
        panel.PrioritizedCommand = panel.Activation = Swap;
        Check(!Quick(owner) && TeammateBackpackInspection.OpenCalls == 1 && TeammateGearSwap.OpenCalls == 1 && owner.Spoken == 0, "quick swap opens editor silently");
        panel.PrioritizedCommand = EPhraseTrigger.Cooperation;
        int activations = panel.Activations;
        Check(Quick(owner) && panel.Activations == activations, "ordinary quick phrase preserved");
        Action<EPhraseTrigger> callback = Wrap(owner.NativeSelected);
        callback(Swap);
        Check(owner.Spoken == 0 && TeammateBackpackInspection.OpenCalls == 1 && TeammateGearSwap.OpenCalls == 2, "dropdown swap opens editor, not prioritized backpack");
        callback(Backpack);
        Check(owner.Spoken == 0 && TeammateBackpackInspection.OpenCalls == 2, "dropdown backpack opens without speech");
        callback(EPhraseTrigger.Cooperation);
        Check(owner.Spoken == 1, "stock dropdown selection preserved");
        Check(Wrap(null) == null, "null callback preserved");
        int unrelatedCalls = 0;
        Action<EPhraseTrigger> unrelated = _ => unrelatedCalls++;
        Check(ReferenceEquals(Wrap(unrelated), unrelated), "unrelated callback not wrapped");
        Check(!FollowerQuickInteractionRouter.TryHandle(owner, EPhraseTrigger.PhraseNone), "no-selection remains unhandled");

        var player = new Player { IsAI = false, InteractionRay = new Ray { origin = new Vector3(0, 1.1f, 0), direction = new Vector3(0, 0, 1) } };
        var target = new Player { Position = new Vector3(0, 0, 2) };
        var bot = new BotOwner { GetPlayer = target };
        target.AIData = new AIData { BotOwner = bot };
        var follower = new BotFollowerPlayer { Bot = bot, Boss = new Boss { realPlayer = player }, IsSquadMate = false };
        BossPlayers.Followers.Add(follower);
        Check(!TeammateBackpackInspection.CanShowSwapGearInteraction(player), "field recruit has no Swap Gear prompt through roster fallback");
        player.InteractablePlayer = target;
        Check(!TeammateBackpackInspection.CanShowSwapGearInteraction(player), "field recruit has no Swap Gear prompt through native target");
        follower.IsSquadMate = true;
        Check(!TeammateBackpackInspection.CanShowSwapGearInteraction(player), "membership alone does not grant spawned gear access");
        follower.IsSpawnedSquadMate = true;
        Check(TeammateBackpackInspection.CanShowSwapGearInteraction(player), "spawned follower eligible");
        player.InteractablePlayer = null;
        Check(TeammateBackpackInspection.CanShowSwapGearInteraction(player), "spawned follower without backpack found by roster fallback");
        bot.HealPending = true;
        Check(!TeammateBackpackInspection.CanShowSwapGearInteraction(player), "active or pending healing hides Swap Gear through roster fallback");
        player.InteractablePlayer = target;
        Check(!TeammateBackpackInspection.CanShowSwapGearInteraction(player), "active or pending healing hides Swap Gear through native target");
        bot.HealPending = false;
        Check(TeammateBackpackInspection.CanShowSwapGearInteraction(player), "Swap Gear returns after healing ends through native target");
        player.InteractablePlayer = null;
        Check(TeammateBackpackInspection.CanShowSwapGearInteraction(player), "Swap Gear returns after healing ends through roster fallback");
        target.Position = new Vector3(0, 0, 2.5f);
        Check(TeammateBackpackInspection.CanShowSwapGearInteraction(player), "range boundary included");
        target.Position = new Vector3(0, 0, 2.51f);
        player.InteractablePlayer = target;
        Check(!TeammateBackpackInspection.CanShowSwapGearInteraction(player), "stale stock target outside range excluded");
        target.Position = new Vector3(2, 0, 0);
        Check(!TeammateBackpackInspection.CanShowSwapGearInteraction(player), "looking away excluded");
        target.Position = new Vector3(0, 0, 2);
        target.HealthController.IsAlive = false;
        Check(!TeammateBackpackInspection.CanShowSwapGearInteraction(player), "dead player excluded");
        target.HealthController.IsAlive = true;
        bot.IsDead = true;
        Check(!TeammateBackpackInspection.CanShowSwapGearInteraction(player), "dead bot excluded");
        bot.IsDead = false;
        bot.BotState = EBotState.Inactive;
        Check(!TeammateBackpackInspection.CanShowSwapGearInteraction(player), "inactive bot excluded");
        bot.BotState = EBotState.Active;
        follower.Boss.realPlayer = new Player();
        Check(!TeammateBackpackInspection.CanShowSwapGearInteraction(player), "other player's follower excluded");
        BossPlayers.Followers.Clear();
        Check(!TeammateBackpackInspection.CanShowSwapGearInteraction(player), "non-follower excluded");
        Check(!TeammateBackpackInspection.CanShowSwapGearInteraction(null), "missing player excluded");
        Console.WriteLine("Follower quick interaction fixture passed: " + _assertions + " assertions.");
    }
}

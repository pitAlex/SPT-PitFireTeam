using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using pitTeam.Modules;
using pitTeam.Patches;
using UnityEngine;

namespace Comfort.Common
{
    public interface IResult { bool Succeed { get; } }
    public class Result : IResult { public bool Succeed { get; set; } }
    public delegate void Callback(IResult result);
    public static class CallbackExtensions { public static void Fail(this Callback callback, string reason) => callback(new Result()); }
}
namespace UnityEngine { public static class Time { public static float time = 10; } }
namespace HarmonyLib { public static class AccessTools { public static MethodInfo Method(Type t, string n, Type[] p) => t.GetMethod(n, p); } }
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
    public class PatchPrefix : Attribute { } public class PatchPostfix : Attribute { }
}
namespace EFT.InventoryLogic
{
    public class Magazine { } public class ItemAddress { }
    public class Weapon { public string Id = "primary"; }
    public enum EquipmentSlot { FirstPrimaryWeapon, SecondPrimaryWeapon, Holster }
}
namespace EFT
{
    public class Profile { public string Nickname = "recruit"; }
    public class Memory { public bool HaveEnemy; }
    public class Selector { public EquipmentSlot LastEquipmentSlot; }
    public class Reload { public bool Reloading; }
    public class Weapons { public Weapon CurrentWeapon = new Weapon(); public Selector Selector = new Selector(); public Reload Reload = new Reload(); }
    public class BotOwner { public bool Follower = true; public Profile Profile = new Profile(); public Memory Memory = new Memory(); public Weapons WeaponManager = new Weapons(); }
    public class AIData { public BotOwner BotOwner = new BotOwner(); }
    public class Interactions
    {
        public bool IsInteractionPlaying, SurvivesStop; public int Stops;
        public void ForceStopInteractions() { Stops++; if (!SurvivesStop) IsInteractionPlaying = false; }
    }
    public class Animator { public Interactions AnimatedInteractions = new Interactions(); }
    public class Movement { public Animator PlayerAnimator = new Animator(); }
    public class Player
    {
        public AIData AIData = new AIData(); public Movement MovementContext = new Movement();
        public void RemoveLeftHandItem(float seconds) { }
        public class FirearmController
        {
            public Player _player = new Player(); public bool Blindfire, Allowed = true;
            public Weapon Item = new Weapon(); public FirearmOperation CurrentOperation;
            public FirearmController() { CurrentOperation = new Idling(this); }
            public bool CanStartReload() => Allowed;
            /* NATIVE RELOAD */
            public class FirearmOperation
            {
                internal FirearmController Controller; public Callback Pending; public bool ImmediateFailure, NoTransition;
                public FirearmOperation(FirearmController c) { Controller = c; }
                public void ReloadMag(Magazine mag, ItemAddress address, Callback finish, Callback start)
                {
                    if (ImmediateFailure) { finish.Fail("native rejection"); return; }
                    if (NoTransition) { Pending = finish; return; }
                    var reload = new FirearmOperation(Controller) { Pending = finish };
                    Controller.CurrentOperation = reload;
                }
            }
            public class Idling : FirearmOperation { public Idling(FirearmController c) : base(c) { } }
        }
    }
}
static class Profiler
{
    class Sample : IDisposable { public void Dispose() { } }
    public static IDisposable BeginSampleWithToken(string a, string b) => new Sample();
}
namespace pitTeam.Modules
{
    static class BossPlayers { public static bool IsFollower(BotOwner b) => b.Follower; }
    static class Logger { public static bool FailLogging; public static void LogInfo(string s) { if (FailLogging) throw new Exception("injected logging fault"); } public static void LogError(string s) { throw new Exception(s); } }
}
namespace pitTeam { static class pitFireTeam { public static bool IsDebugBuild => true; } }
class PatrolHarness
{
    public BotOwner BotOwner; public bool reloadingInProgress = true; public string reloadingWeaponId = "primary";
    public EquipmentSlot? forcedTopOffSlot; public float nextReloadCheckAt; public int Failures, Returns;
    public readonly HashSet<EquipmentSlot> reloadSlotsTried = new HashSet<EquipmentSlot> { EquipmentSlot.FirstPrimaryWeapon };
    const float OutOfCombatReloadSlotCooldown = 2;
    void RecordOutOfCombatReloadFailure(EquipmentSlot s, Weapon w, string why) { Failures++; }
    bool IsOutOfCombatReloadGiveUpActive(EquipmentSlot s, Weapon w) => Failures >= 2;
    void TryCompleteReturnAfterTopOffSwitch(Selector s) { Returns++; }
    /* PATROL RETRY */
    public bool Tick() => TryHandleSkippedReloadStart();
}
static class FollowerReloadStartFixture
{
    static int checks;
    static readonly MethodInfo Prefix = typeof(FollowerMagazineReloadStartPatch).GetMethod("PatchPrefix", BindingFlags.Static | BindingFlags.NonPublic);
    static readonly MethodInfo Postfix = typeof(FollowerMagazineReloadStartPatch).GetMethod("PatchPostfix", BindingFlags.Static | BindingFlags.NonPublic);
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    static void Call(Player.FirearmController controller, Callback callback, bool patched = true)
    {
        object[] args = { controller, callback, null };
        if (patched) Prefix.Invoke(null, args);
        controller.ReloadMag(new Magazine(), null, (Callback)args[1]);
        if (patched) Postfix.Invoke(null, new object[] { controller, args[2], true });
    }
    static void Main()
    {
        var c = new Player.FirearmController(); var interactions = c._player.MovementContext.PlayerAnimator.AnimatedInteractions;
        interactions.IsInteractionPlaying = interactions.SurvivesStop = true;
        int completed = 0; bool flag = true;
        Callback callback = r => { completed++; flag = false; };
        Call(c, callback, false);
        Check(completed == 0 && flag, "native method reproduces no callback and stuck BotReload flag");
        Call(c, callback);
        Check(completed == 1 && !flag, "patch invokes native failure callback exactly once");
        Check(c.CurrentOperation is Player.FirearmController.Idling, "no hands operation forced");
        var bot = c._player.AIData.BotOwner;
        var patrol = new PatrolHarness { BotOwner = bot };
        bot.WeaponManager.Reload.Reloading = true;
        Check(!patrol.Tick() && patrol.Failures == 0 && patrol.reloadingInProgress, "live reload defers pending stale failure without disturbing patrol state");
        bot.WeaponManager.Reload.Reloading = false;
        Check(patrol.Tick(), "skipped start reaches production patrol handler");
        Check(patrol.Failures == 1 && patrol.forcedTopOffSlot == EquipmentSlot.FirstPrimaryWeapon, "first failure retains same-slot retry");
        Check(!patrol.reloadingInProgress && patrol.reloadingWeaponId == null, "failed start not mistaken for completed reload");
        Check(patrol.nextReloadCheckAt == 12 && !patrol.reloadSlotsTried.Contains(EquipmentSlot.FirstPrimaryWeapon), "stationary retry gets two-second cooldown");
        Check(!patrol.Tick(), "failure consumed once");
        Call(c, callback);
        Check(patrol.Tick() && patrol.Failures == 2 && patrol.forcedTopOffSlot == null && patrol.Returns == 1, "second failure spends existing bounded budget");
        interactions.IsInteractionPlaying = false;
        int before = completed;
        FollowerReloadStartRecovery.Record(bot, c.Item.Id, "previousSkippedStart");
        Call(c, callback);
        Check(completed == before && !(c.CurrentOperation is Player.FirearmController.Idling), "real reload left pending");
        c.CurrentOperation.Pending(new Result { Succeed = true });
        Check(completed == before + 1, "genuine async completion preserved");
        Check(!FollowerReloadStartRecovery.TryConsume(bot, c.Item.Id, out _), "real reload creates no retry marker");
        c = new Player.FirearmController { Blindfire = true }; before = completed;
        Call(c, callback);
        Check(completed == before + 1, "idle blindfire skipped start completes failure");
        c = new Player.FirearmController { Allowed = false }; before = completed;
        Call(c, callback);
        Check(completed == before + 1, "native rejection not double completed");
        c = new Player.FirearmController(); c.CurrentOperation.ImmediateFailure = true; before = completed;
        Call(c, callback);
        Check(completed == before + 1, "inventory rejection remains single callback");
        c = new Player.FirearmController(); c._player.AIData.BotOwner.Follower = false; c.Blindfire = true; before = completed;
        Call(c, callback);
        Check(completed == before, "ordinary bot untouched");
        c = new Player.FirearmController { Blindfire = true }; c.CurrentOperation = new Player.FirearmController.FirearmOperation(c); before = completed;
        Call(c, callback);
        Check(completed == before, "non-idle active operation never force-completed");
        c = new Player.FirearmController(); c.CurrentOperation.NoTransition = true; before = completed;
        Call(c, callback);
        Check(completed == before, "unproven same-operation deferred start untouched");
        c.CurrentOperation.Pending(new Result { Succeed = true });
        Check(completed == before + 1, "deferred callback preserved");
        c = new Player.FirearmController(); interactions = c._player.MovementContext.PlayerAnimator.AnimatedInteractions;
        interactions.IsInteractionPlaying = true; before = completed;
        Call(c, callback);
        Check(interactions.Stops == 1 && completed == before && !(c.CurrentOperation is Player.FirearmController.Idling), "normally stopped interaction starts native reload");
        c = new Player.FirearmController { Blindfire = true }; c._player.AIData.BotOwner.Memory.HaveEnemy = true;
        Call(c, callback);
        Check(!FollowerReloadStartRecovery.TryConsume(c._player.AIData.BotOwner, c.Item.Id, out _), "combat failure not queued for patrol retry");
        FollowerReloadStartRecovery.Record(bot, "other", "interactionPlaying");
        Check(!FollowerReloadStartRecovery.TryConsume(bot, "primary", out _), "weapon identity change discards old retry");
        Check(!FollowerReloadStartRecovery.TryConsume(bot, "other", out _), "discard cannot leak to later slot");
        c = new Player.FirearmController { Blindfire = true };
        Call(c, null);
        Check(!FollowerReloadStartRecovery.TryConsume(c._player.AIData.BotOwner, c.Item.Id, out _), "null callback untouched");
        c = new Player.FirearmController { Blindfire = true }; before = completed;
        object[] skipped = { c, callback, null };
        Prefix.Invoke(null, skipped);
        Postfix.Invoke(null, new object[] { c, skipped[2], false });
        Check(completed == before, "another patch skipping original retains its own callback ownership");
        Logger.FailLogging = true; c = new Player.FirearmController { Blindfire = true }; before = completed;
        Call(c, callback);
        Check(completed == before + 1, "logging fault cannot suppress failed-start callback");
        c = new Player.FirearmController(); before = completed;
        Call(c, callback); c.CurrentOperation.Pending(new Result { Succeed = true });
        Check(completed == before + 1, "logging fault cannot interrupt real start or completion");
        Logger.FailLogging = false;
        Console.WriteLine("Follower reload start: " + checks + " assertions passed; source/stub checks are not raid verification.");
    }
}

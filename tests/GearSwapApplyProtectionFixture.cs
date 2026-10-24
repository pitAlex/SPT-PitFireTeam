using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using EFT;
using EFT.HealthSystem;
using pitTeam.Modules;
using pitTeam.Patches;
using UnityEngine;

namespace EFT
{
    enum BotLogicDecision { Hold }
    enum ExitStatus { Survived }
    class LocalGame { public void Stop(string profileId, ExitStatus status, string name, float time) { } }
    class BotsController { public void StopGettingInfo() { } }
    class Player
    {
        public bool Destroyed = false;
        public ActiveHealthController ActiveHealthController = new ActiveHealthController();
        public ActiveHealthController HealthController => ActiveHealthController;
        public void Move(Vector2 direction) { }
        public object ApplyShot() => null;
        public void ApplyDamageInfo() { }
        public void ApplyExplosionDamageToArmor() { }
        public void OnDead() { }
        public class FirearmController
        {
            public void SetTriggerPressed(bool pressed) { }
            public class AutomaticFireOperation
            {
                public Player Player;
                public bool Idle;
                public void InternalOnFireEvent() { }
                public void SwitchToIdling() { Idle = true; }
            }
        }
    }
    class BotOwner
    {
        public Player GetPlayer = new Player();
        public Mover Mover = new Mover();
        public Brain Brain = new Brain();
        public void UpdateManual() { }
    }
    class Mover { public bool Pause; }
    class Brain { public AICoreAgent<BotLogicDecision> Agent = new AICoreAgent<BotLogicDecision>(); }
    class Owner { public Player Player = new Player(); }
}
namespace EFT.HealthSystem
{
    class ActiveHealthController
    {
        public bool IsAlive = true;
        public float ApplyDamage() => 1;
        public void ChangeHealth(float value) { }
        public void Kill() { }
    }
}
class AICoreAgent<T> { public void Update() { } }
class BotWeaponSelector
{
    public BotOwner _owner;
    public bool TryChangeToSlot() => true;
}
namespace UnityEngine
{
    struct Vector2 { public float X; public static Vector2 zero => default; }
}
namespace HarmonyLib
{
    class HarmonyPriority : Attribute { public HarmonyPriority(int value) { } }
    static class Priority { public const int First = 800; }
    static class AccessTools
    {
        public static MethodBase Method(Type type, string name, Type[] args = null) =>
            args == null ? type.GetMethod(name) : type.GetMethod(name, args);
    }
}
namespace SPT.Reflection.Patching
{
    class PatchPrefix : Attribute { }
    abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
}
namespace pitTeam.Utils
{
    static class FollowerRecovery { public static void StopShooting(BotOwner bot) { } }
}
namespace pitTeam.Modules
{
    static class Logger { public static void LogError(string message) { } }
    class TeammateGearSwap
    {
        internal static TeammateGearSwap Current;
        internal readonly Owner Owner = new Owner();
        internal readonly BotOwner Bot = new BotOwner();
        internal bool Applying, _worldEnding, _movementHeld, _previousMoverPause, _closed, _cancelled;
        internal string _cancelReason;
        internal GearSwapApplyScope _applyScope;
        internal int Closes;
        /* PROTECTION */
        /* CANCEL */
        internal void Close()
        {
            Closes++;
            _closed = true;
            _applyScope?.Stop();
            ReleaseMovement();
            if (ReferenceEquals(Current, this)) Current = null;
        }
        internal void Begin(int timeout = 10000)
        {
            Applying = true;
            _applyScope = new GearSwapApplyScope(timeout);
            HoldMovement();
        }
        internal bool Continues(Player player) => CanContinue(player);
    }
}

static class GearSwapApplyProtectionFixture
{
    static int checks;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
    static object Call<T>(params object[] arguments) => typeof(T).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, arguments);
    static TeammateGearSwap Session() => TeammateGearSwap.Current = new TeammateGearSwap();
    static void DamageGates(Player player, bool protectedActor)
    {
        Check((bool)Call<GearSwapApplyShotPatch>(player) == !protectedActor, "shot gate matches exact protection");
        Check((bool)Call<GearSwapApplyDamageInfoPatch>(player) == !protectedActor, "damage-info gate matches exact protection");
        Check((bool)Call<GearSwapApplyExplosionArmorPatch>(player) == !protectedActor, "explosion armor gate matches exact protection");
        object[] damage = { player.ActiveHealthController, 8f };
        Check((bool)Call<GearSwapApplyHealthDamagePatch>(damage) == !protectedActor, "health damage gate matches exact protection");
        Check((float)damage[1] == (protectedActor ? 0f : 8f), "blocked health damage reports zero without changing ordinary result");
        Check((bool)Call<GearSwapApplyHealthDrainPatch>(player.ActiveHealthController, -1f) == !protectedActor,
            "bleeding/metabolic health drain matches exact protection");
        Check((bool)Call<GearSwapApplyHealthDrainPatch>(player.ActiveHealthController, 1f), "positive health changes always allowed");
    }
    static void Main()
    {
        var session = Session();
        DamageGates(session.Bot.GetPlayer, false);
        Check(!TeammateGearSwap.Protects(session.Bot.GetPlayer), "draft has no protection");
        session.Begin();
        DamageGates(session.Bot.GetPlayer, true);
        DamageGates(session.Owner.Player, false);
        DamageGates(new Player(), false);
        Check(session.Bot.Mover.Pause && session.Continues(session.Bot.GetPlayer), "accepted Apply holds movement and permits callbacks");
        Check(!(bool)Call<GearSwapApplyBotTickPatch>(session.Bot), "native action ticks held only for applying follower");
        Check((bool)Call<GearSwapApplyBotTickPatch>(new BotOwner()), "ordinary bot ticks unchanged");
        Check(!(bool)Call<GearSwapApplyBrainTickPatch>(session.Bot.Brain.Agent), "brain prefix blocks BigBrain action execution");
        Check((bool)Call<GearSwapApplyBrainTickPatch>(new AICoreAgent<BotLogicDecision>()), "other brain action execution unchanged");
        object[] selection = { new BotWeaponSelector { _owner = session.Bot }, true };
        Check(!(bool)Call<GearSwapApplyWeaponSelectionPatch>(selection) && !(bool)selection[1],
            "independent weapon selection cannot race exchange hands");
        object[] move = { session.Bot.GetPlayer, new Vector2 { X = 1f } };
        Call<GearSwapApplyMovementPatch>(move);
        Check(((Vector2)move[1]).X == 0f, "independent movement requests become zero");
        object[] trigger = { session.Bot.GetPlayer, true };
        Call<GearSwapApplyTriggerPatch>(trigger);
        Check(!(bool)trigger[1], "independent fire requests cannot press trigger");
        var automatic = new Player.FirearmController.AutomaticFireOperation { Player = session.Bot.GetPlayer };
        Check(!(bool)Call<GearSwapApplyAutomaticFirePatch>(automatic) && automatic.Idle, "pending automatic round switches to native idle");
        session._applyScope.Commit();
        Check(session._applyScope.Committed, "completed transaction recorded");
        Call<GearSwapRaidStopPatch>();
        Check(session._closed && session._worldEnding && TeammateGearSwap.Current == null, "raid stop closes and detaches session before snapshots");
        Check(!session.Bot.Mover.Pause && !session._applyScope.Active, "raid stop releases movement and protection");
        Check(session._applyScope.Committed, "raid stop never undoes committed ownership");
        Check(!session.Continues(session.Bot.GetPlayer), "late continuations rejected after teardown");
        Check((bool)Call<GearSwapApplyBrainTickPatch>(session.Bot.Brain.Agent), "brain resumes after protection release");
        Call<GearSwapBotsStopPatch>();
        Check(session.Closes == 1, "fallback teardown idempotent");

        session = Session();
        session.Begin();
        Call<GearSwapActorDeathPatch>(session.Bot.GetPlayer.ActiveHealthController);
        session.Bot.GetPlayer.HealthController.IsAlive = false;
        Check(session._applyScope.Preparation.IsCancellationRequested && session._applyScope.Active,
            "follower death cancels preparation but leaves living player recovery available");
        Check(!session.Continues(session.Bot.GetPlayer) && session.Continues(session.Owner.Player),
            "dead follower never restores hands while living player may recover");
        Check(!TeammateGearSwap.Protects(session.Bot.GetPlayer), "forced death is not prevented or resurrected");
        TeammateGearSwap.EndRaid();

        session = Session();
        session.Begin();
        Call<GearSwapActorDeathPatch>(session.Owner.Player.ActiveHealthController);
        Check(session._worldEnding && !session._applyScope.Active && !session._applyScope.Committed,
            "player death before commit stops live transfer");
        session = Session();
        session.Begin();
        session._applyScope.Commit();
        Call<GearSwapActorDeathPatch>(session.Owner.Player.ActiveHealthController);
        Check(session._applyScope.Committed && TeammateGearSwap.Current == null,
            "player death after commit keeps final equipment and invalidates callbacks");

        session = Session();
        Call<GearSwapActorDeathPatch>(session.Bot.GetPlayer.ActiveHealthController);
        Check(session.Closes == 1 && TeammateGearSwap.Current == null, "follower death during draft discards it immediately");
        session = Session();
        Call<GearSwapActorDeadFallbackPatch>(session.Owner.Player);
        Check(session.Closes == 1 && TeammateGearSwap.Current == null, "native OnDead fallback also cancels staged editor");
        session = Session();
        session.Begin();
        var old = session;
        session = Session();
        session.Begin();
        Check(!old.Continues(old.Bot.GetPlayer) && !TeammateGearSwap.Protects(old.Bot.GetPlayer),
            "replaced session cannot continue or protect actors in a new exchange");
        old._applyScope.Dispose();
        TeammateGearSwap.EndRaid();
        using (var boundary = new GearSwapApplyScope())
        {
            boundary.CancelPreparation();
            boundary.Commit();
            Check(boundary.Committed, "cancellation received after synchronous replay cannot erase commit result");
        }
        session = Session();
        session.Begin();
        session.Bot.GetPlayer.Destroyed = true;
        Check(!TeammateGearSwap.Protects(session.Bot.GetPlayer) && !session.Continues(session.Bot.GetPlayer),
            "destroyed actor never receives protection or late hands work");
        TeammateGearSwap.EndRaid();
        session = Session();
        session.Bot.Mover.Pause = true; // editor inspection hold, not an earlier unrelated pause
        session.Begin();
        TeammateGearSwap.EndRaid();
        Check(!session.Bot.Mover.Pause, "completion releases the editor's own pause rather than preserving it as preexisting");
        session = Session();
        session.Bot.Mover.Pause = true;
        session._previousMoverPause = true; // opening snapshot predates the editor
        session.Begin();
        TeammateGearSwap.EndRaid();
        Check(session.Bot.Mover.Pause, "cleanup preserves preexisting movement hold");

        session = Session();
        session.Begin(25);
        Thread.Sleep(60);
        TeammateGearSwap.CheckApplyDeadline();
        Check(session._cancelled && !session._applyScope.Active && !session.Bot.Mover.Pause,
            "overall deadline cancels pending preparation and releases protection without callback");
        DamageGates(session.Bot.GetPlayer, false);
        session._applyScope.Dispose();
        session._applyScope.Dispose();
        session._applyScope.CancelPreparation();
        Check(session._applyScope.Lifetime.IsCancellationRequested && session._applyScope.Preparation.IsCancellationRequested,
            "tokens remain safe for late continuations after idempotent disposal");
        TeammateGearSwap.EndRaid();
        Console.WriteLine("Gear Swap Apply protection: " + checks + " assertions passed.");
    }
}

using System;
using EFT;
using pitTeam.Modules;

namespace UnityEngine
{
    public class Component
    {
        public bool Destroyed;
        public static bool operator ==(Component first, Component second)
        {
            bool firstNull = ReferenceEquals(first, null), secondNull = ReferenceEquals(second, null);
            if (firstNull && secondNull) return true;
            if (firstNull || secondNull) return (firstNull ? second : first).Destroyed;
            return ReferenceEquals(first, second);
        }
        public static bool operator !=(Component first, Component second) => !(first == second);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public static class Time { public static float time; }
    public class GameObject
    {
        public Component Objective;
        public Component GetComponent(Type type) => Objective != null && type.IsInstanceOfType(Objective) ? Objective : null;
    }
}
namespace EFT.HealthSystem
{
    public interface IHealthController { bool IsAlive { get; } event Action<EDamageType> DiedEvent; }
    public class Health : IHealthController
    {
        public bool IsAlive { get; set; } = true;
        private Action<EDamageType> died;
        public int Listeners;
        public event Action<EDamageType> DiedEvent { add { died += value; Listeners++; } remove { died -= value; Listeners--; } }
        public void Die() { IsAlive = false; died?.Invoke(EDamageType.Undefined); }
    }
}
namespace EFT
{
    public enum EDamageType { Undefined }
    public class Player
    {
        public HealthSystem.Health HealthController = new();
        public UnityEngine.GameObject gameObject = new();
    }
    public class BotOwner : UnityEngine.Component
    {
        private bool dead;
        public int DestroyedReads;
        public bool IsDead
        {
            get { if (Destroyed) { DestroyedReads++; throw new InvalidOperationException("destroyed owner access"); } return dead; }
            set { dead = value; }
        }
        public Player GetPlayer = new();
    }
}
namespace pitTeam.Modules
{
    public static class Logger { public static int Errors; public static void LogError(object message) { Errors++; } }
}
#if !NO_QUESTING_BOTS
namespace QuestingBots.Components
{
    public class BotObjectiveManager : UnityEngine.Component
    {
        public bool HasInitialQuestBeenSelected { get; private set; }
        public bool IsQuestingAllowed { get; private set; }
        public int Stops;
        public bool ThrowAfterStop;
        public void Initialize(bool allowed) { IsQuestingAllowed = allowed; HasInitialQuestBeenSelected = true; }
        public void Enable() { IsQuestingAllowed = true; }
        public void StopQuesting() { Stops++; IsQuestingAllowed = false; if (ThrowAfterStop) throw new Exception("native failure"); }
    }
}
#endif
internal static class QuestingBotsHandoffChecks
{
    private static int checks;
    private static int Retained => ((System.Collections.IDictionary)typeof(FollowerQuestingBots)
        .GetField("Paused", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null)).Count;
    private static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    private static void Tick(BotOwner bot) { UnityEngine.Time.time += 0.6f; BotOwnerUpdateHub.InvokeFollower(bot); }
    public static void Main()
    {
#if NO_QUESTING_BOTS
        var ordinary = new BotOwner();
        FollowerQuestingBots.Pause(ordinary);
        Tick(ordinary);
        FollowerQuestingBots.Release(ordinary);
        FollowerQuestingBots.ClearRaid();
        Check(!BotOwnerUpdateHub.HasFollowerSubscribers, "absent mod must not register a callback");
        Check(ordinary.GetPlayer.HealthController.Listeners == 0 && Logger.Errors == 0, "absent mod must stay silent and leave no listeners");
#else
        foreach (bool originallyAllowed in new[] { false, true })
        {
            var bot = new BotOwner();
            var manager = new QuestingBots.Components.BotObjectiveManager();
            bot.GetPlayer.gameObject.Objective = manager;
            manager.Initialize(originallyAllowed);
            FollowerQuestingBots.Pause(bot);
            Check(!manager.IsQuestingAllowed, "recruitment must stop native questing");
            Check(manager.Stops == (originallyAllowed ? 1 : 0), "already disabled objectives must not receive redundant stops");
            FollowerQuestingBots.Pause(bot);
            for (int i = 0; i < 100; i++) Tick(bot);
            Check(bot.GetPlayer.HealthController.Listeners == 1 && manager.Stops == (originallyAllowed ? 1 : 0), "repeated pause/ticks must retain one owner");
            manager.Enable();
            Tick(bot);
            Check(!manager.IsQuestingAllowed && manager.Stops == (originallyAllowed ? 2 : 1), "native re-enable during ownership must be suspended again");
            FollowerQuestingBots.Release(bot);
            Check(manager.IsQuestingAllowed == originallyAllowed, "departure must restore the saved permission");
            Check(bot.GetPlayer.HealthController.Listeners == 0 && !BotOwnerUpdateHub.HasFollowerSubscribers, "departure must remove all owned subscriptions");
            manager.Enable();
            FollowerQuestingBots.Release(bot);
            Check(manager.IsQuestingAllowed, "duplicate departure must not write the native state again");
        }
        // Components and their first quest can both arrive after follower conversion.
        var late = new BotOwner();
        FollowerQuestingBots.Pause(late);
        Tick(late);
        var objective = new QuestingBots.Components.BotObjectiveManager();
        late.GetPlayer.gameObject.Objective = objective;
        Tick(late);
        Check(objective.Stops == 0, "uninitialized false must not be captured as the original permission");
        objective.Initialize(true);
        Tick(late);
        Check(!objective.IsQuestingAllowed && objective.Stops == 1, "late native setup must be suspended");
        FollowerQuestingBots.Release(late);
        Check(objective.IsQuestingAllowed, "late initialization must still resume on departure");
        // Leaving before setup should leave that setup entirely native-owned.
        var earlyExit = new BotOwner();
        earlyExit.GetPlayer.gameObject.Objective = new QuestingBots.Components.BotObjectiveManager();
        FollowerQuestingBots.Pause(earlyExit);
        FollowerQuestingBots.Release(earlyExit);
        var untouched = (QuestingBots.Components.BotObjectiveManager)earlyExit.GetPlayer.gameObject.Objective;
        untouched.Initialize(true);
        Tick(earlyExit);
        Check(untouched.IsQuestingAllowed && untouched.Stops == 0, "departure before initialization must not retain ownership");
        // Native despawn can destroy a MonoBehaviour without raising a death event.
        foreach (int exit in new[] { 0, 1, 2 }) foreach (bool destroyComponent in new[] { false, true })
        {
            var bot = new BotOwner();
            var manager = new QuestingBots.Components.BotObjectiveManager();
            manager.Initialize(true); bot.GetPlayer.gameObject.Objective = manager;
            FollowerQuestingBots.Pause(bot);
            bot.Destroyed = true; manager.Destroyed = destroyComponent;
            Check(bot == null && !ReferenceEquals(bot, null), "fixture must model Unity's destroyed-object null comparison");
            if (exit == 0) FollowerQuestingBots.Release(bot);
            else if (exit == 1) Tick(bot);
            else FollowerQuestingBots.ClearRaid();
            Check(Retained == 0, "destroyed-owner release/tick/teardown must remove the managed dictionary key");
            Check(bot.GetPlayer.HealthController.Listeners == 0 && !BotOwnerUpdateHub.HasFollowerSubscribers,
                "destroyed-owner cleanup must detach death and update subscriptions");
            Check(!manager.IsQuestingAllowed && bot.DestroyedReads == 0,
                "destroyed-owner cleanup must not resume objectives or access native bot state");
            FollowerQuestingBots.Release(bot); Tick(bot); FollowerQuestingBots.ClearRaid(); FollowerQuestingBots.Pause(bot);
            Check(Retained == 0 && bot.DestroyedReads == 0 && bot.GetPlayer.HealthController.Listeners == 0,
                "repeated cleanup or pause must not resurrect a destroyed bot's ownership");
        }
        foreach (bool die in new[] { false, true })
        {
            var bot = new BotOwner();
            var manager = new QuestingBots.Components.BotObjectiveManager();
            bot.GetPlayer.gameObject.Objective = manager;
            manager.Initialize(true);
            FollowerQuestingBots.Pause(bot);
            if (die) bot.GetPlayer.HealthController.Die(); else FollowerQuestingBots.ClearRaid();
            Check(!manager.IsQuestingAllowed && bot.GetPlayer.HealthController.Listeners == 0, "death/teardown must detach without resuming objectives");
            Check(!BotOwnerUpdateHub.HasFollowerSubscribers, "death/teardown must release the hub callback");
            FollowerQuestingBots.Release(bot);
            Tick(bot);
            Check(!manager.IsQuestingAllowed, "later cleanup/ticks must not revive questing");
        }
        // One departing bot must not release another bot's suspension.
        var first = new BotOwner(); var second = new BotOwner();
        foreach (var bot in new[] { first, second })
        {
            var manager = new QuestingBots.Components.BotObjectiveManager();
            manager.Initialize(true); bot.GetPlayer.gameObject.Objective = manager;
            FollowerQuestingBots.Pause(bot);
        }
        FollowerQuestingBots.Release(first);
        Check(BotOwnerUpdateHub.HasFollowerSubscribers && second.GetPlayer.HealthController.Listeners == 1, "other followers must retain their ownership");
        FollowerQuestingBots.ClearRaid();
        Check(first.GetPlayer.HealthController.Listeners == 0 && second.GetPlayer.HealthController.Listeners == 0, "multi-bot cleanup must detach every listener");
        // Native StopQuesting can mutate its flag before failing in optional logging.
        var failing = new BotOwner();
        var failingManager = new QuestingBots.Components.BotObjectiveManager { ThrowAfterStop = true };
        failingManager.Initialize(true); failing.GetPlayer.gameObject.Objective = failingManager;
        FollowerQuestingBots.Pause(failing);
        Check(!failingManager.IsQuestingAllowed && Logger.Errors == 1, "optional exception must be contained after native mutation");
        FollowerQuestingBots.Release(failing);
        Check(failingManager.IsQuestingAllowed && failing.GetPlayer.HealthController.Listeners == 0, "failed conversion must still restore and detach");
        Check(!BotOwnerUpdateHub.HasFollowerSubscribers, "all handoff work must be released");
#endif
        Console.WriteLine(checks + " Questing Bots handoff checks passed.");
    }
}

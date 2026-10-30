using System;
using System.Collections.Generic;
using pitTeam.Modules;
using pitTeam.GoingRogue;

public interface IPlayer { }
public sealed class Requester : IPlayer { }
public enum EBotState { Active, NonActive }
public enum EPhraseTrigger { Roger }
public enum EInteraction { OkGesture }
public sealed class Settings { public string Role; }
public sealed class Info { public Settings Settings = new Settings(); }
public sealed class Profile { public Info Info = new Info(); }
public sealed class Talk { public void TrySay(EPhraseTrigger phrase, bool ignored) { } }
public sealed class Gesture { public void TryGestus(EInteraction gesture, bool ignored) { } }
public sealed class BotOwner
{
    public bool IsDead;
    public EBotState BotState = EBotState.Active;
    public Profile Profile = new Profile();
    public Talk BotTalk = new Talk();
    public Gesture Gesture = new Gesture();
}
public sealed class BotFollowerPlayer
{
    public float Determination = 50, Aggression = 37;
    public string Source;
    public bool Patrol;
    public void ClearQueuedPushEnemyWhileHealing(string reason) { }
    public void SetCombatIndependent(bool value) { Determination = value ? 100 : 20; }
    public void SetTemporaryCombatAggressionOverride(float value, string source) { Aggression = value; Source = source; }
    public void ClearCommand(string reason) { }
    public void ClearTemporaryCombatAggressionOverride(string reason) { Aggression = 37; Source = null; }
    public void SetCanPatrol(bool value) { Patrol = value; }
}
public sealed class BossPlayers
{
    public static BossPlayers Instance = new BossPlayers();
    public Dictionary<BotOwner, BotFollowerPlayer> States = new Dictionary<BotOwner, BotFollowerPlayer>();
    public BotFollowerPlayer GetFollower(BotOwner owner) => States.TryGetValue(owner, out var state) ? state : null;
}
namespace pitTeam.Modules
{
    public static class GameplayModeAddons
    {
        public static bool Registered = true, KnightPresent = true;
        public static CompanionCommandPolicy GetCompanionCommandPolicy(string role) => Registered ? GoonCommandPolicy.ForRole(role, KnightPresent) : null;
    }
}
public sealed class Receiver
{
    public List<BotOwner> Followers = new List<BotOwner>();
    public bool Combat = true, LootLocked;
    private bool IsCombatRegroupContext() => Combat;
    private bool IsFollowerLockedByLootCommand(BotOwner owner) => LootLocked;
    public void Issue(IPlayer requester) => ApplyOnYourOwnCommand(requester);
__RECEIVER__
}
public static class Program
{
    private static int checks;
    private static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    public static void Main()
    {
        foreach (bool registered in new[] { false, true })
        foreach (bool knight in new[] { false, true })
        foreach (string role in new[] { "bossKnight", "followerBigPipe", "followerBirdEye", "exUsec", "pmcUSEC", "assault" })
        {
            GameplayModeAddons.Registered = registered; GameplayModeAddons.KnightPresent = knight;
            var owner = new BotOwner(); owner.Profile.Info.Settings.Role = role;
            var state = new BotFollowerPlayer { Aggression = 100, Determination = 20 };
            BossPlayers.Instance.States[owner] = state;
            var receiver = new Receiver(); receiver.Followers.Add(owner); receiver.Issue(new Requester());
            bool goon = role == "bossKnight" || role == "followerBigPipe" || role == "followerBirdEye";
            Check(state.Determination == 100, "combat On Your Own sets exactly 100 determination: " + role);
            Check(state.Aggression == (registered && goon ? 70 : 100), "aggression policy stays addon/role scoped: " + role);
            Check(state.Source == (registered && goon ? "OnYourOwn" : null), "only scoped Goons record the aggression override source: " + role);
        }
        GameplayModeAddons.Registered = true;
        foreach (string gate in new[] { "peaceful", "dead", "inactive", "loot", "nullRequester" })
        {
            var owner = new BotOwner(); owner.Profile.Info.Settings.Role = "bossKnight";
            var state = new BotFollowerPlayer(); BossPlayers.Instance.States[owner] = state;
            var receiver = new Receiver(); receiver.Followers.Add(owner);
            if (gate == "peaceful") receiver.Combat = false;
            if (gate == "dead") owner.IsDead = true;
            if (gate == "inactive") owner.BotState = EBotState.NonActive;
            if (gate == "loot") receiver.LootLocked = true;
            receiver.Issue(gate == "nullRequester" ? null : new Requester());
            Check(state.Determination == 50 && state.Aggression == 37, "noncombat/ineligible follower does not acquire combat percentages: " + gate);
            Check(state.Patrol == (gate == "peaceful"), "peaceful patrol behavior preserved: " + gate);
        }
        Check(new CompanionCommandPolicy(false, null).OnYourOwnAggression == null, "old two-argument policy remains unchanged");
        Check(new CompanionCommandPolicy(false, null, null).OnYourOwnAggression == null, "old three-argument policy remains unchanged");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Check(new CompanionCommandPolicy(false, null, null, invalid).OnYourOwnAggression == null, "nonfinite optional policy cannot reach combat state");
        Console.WriteLine("PASS: " + checks + " production On Your Own receiver and Goon policy checks.");
    }
}

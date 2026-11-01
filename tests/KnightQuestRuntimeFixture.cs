using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Comfort.Common;
using EFT;
using EFT.Quests;
using HarmonyLib;
using pitTeam.GoingRogue.Quests;
using pitTeam.Modules;
using UnityEngine;

internal static class QuestRuntimeChecks
{
    private static int checks;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    public static void Main()
    {
        new Harmony("pitTeam.fixture.knight-quests").PatchAll(typeof(KnightQuestRuntime).Assembly);
        var world = new GameWorld(); Singleton<GameWorld>.Instance = world;
        var player = new Player { ProfileId = "human", IsYourPlayer = true, GameWorld = world };
        player.Profile.Id = player.ProfileId;
        player.QuestController.Profile = player.Profile;
        world.MainPlayer = player;
        var knight = new Player { ProfileId = "knight", GameWorld = world };
        knight.Profile.Id = knight.ProfileId; knight.Profile.Info.Settings.Role = WildSpawnType.bossKnight;
        var owner = new BotOwner { ProfileId = knight.ProfileId, Profile = knight.Profile, GetPlayer = knight };
        var companion = new Follower { Bot = owner, Boss = new Leader { realPlayer = player } };
        BossPlayers.Followers.Add(companion);
        GameplayModeRuntime.IsGoingRogue = true;
        KnightQuestRuntime.RegisterKnight(owner, player);
        var first = new Quest { Id = KnightQuestPolicy.Competition };
        var second = new Quest { Id = KnightQuestPolicy.MyLand };
        var third = new Quest { Id = KnightQuestPolicy.Thieves };
        var ordinary = new Quest { Id = "ordinary" };
        player.QuestController.Quests.AddRange(new[] { first, second, third, ordinary });
        TaskConditionCounter Add(Quest quest, string id)
        {
            var counter = new TaskConditionCounter { Id = id, Conditional = quest };
            quest.ConditionCountersManager.Counters.Add(counter); return counter;
        }
        var woods = Add(first, KnightQuestPolicy.WoodsKills);
        var myLand = Add(second, KnightQuestPolicy.MyLandKills);
        var unrelated = Add(ordinary, "ordinary-counter");
        var failure = Add(first, KnightQuestPolicy.CompetitionFailure);
        var myLandFailure = Add(second, KnightQuestPolicy.MyLandFailure);
        void Tick(TaskConditionCounter counter) => ConditionCounterManager.Test(1, counter, new ConditionCheck[0]);
        Tick(woods); Check(woods.Value == 1, "human kill admitted with nearby owned living Knight");
        owner.Position = new Vector3 { X = 80 }; Tick(woods); Check(woods.Value == 2, "80m boundary included");
        owner.Position.X = 80.01f; Tick(woods); Check(woods.Value == 2, "outside 80m cannot credit player kill");
        owner.Position.X = 0;
        foreach (bool living in new[] { false, true })
        {
            owner.IsDead = !living; player.HealthController.IsAlive = living;
            Tick(woods);
            Check(woods.Value == (living ? 3 : 2), "living companion and player gate");
        }
        player.QuestController.Profile = new Profile { Id = "ordinary-follower" };
        Tick(woods); Tick(myLand);
        Check(woods.Value == 3 && myLand.Value == 0, "Core forwarded ordinary follower kills cannot count for either task");
        player.QuestController.Profile = knight.Profile;
        Tick(woods); Tick(myLand);
        Check(woods.Value == 3 && myLand.Value == 1, "Knight attribution excluded from personal task but admitted for team task");
        player.QuestController.Profile = player.Profile;
        GameplayModeRuntime.IsGoingRogue = false;
        Tick(woods); Tick(myLand); Tick(failure); Tick(unrelated);
        Check(woods.Value == 3 && myLand.Value == 1 && failure.Value == 0 && unrelated.Value == 1, "other mode leaves custom progress/failure untouched and ordinary counters working");
        GameplayModeRuntime.IsGoingRogue = true;
        player.HealthController.IsAlive = false; owner.IsDead = true; Tick(failure);
        Check(failure.Value == 1, "native player exit failure can count after player/Knight death");
        player.HealthController.IsAlive = true; owner.IsDead = false;
        TaskConditionCounter ai = new TaskConditionCounter { Id = KnightQuestPolicy.WoodsKills, Conditional = new Quest() };
        Tick(ai); Check(ai.Value == 0, "non-local conditional cannot gain custom credit");

        Player Victim(string id, bool inside = true) => new Player { ProfileId = id, GameWorld = world, TriggerZones = inside ? new List<string> { "prapor_27_1" } : new List<string>() };
        var damage = new EFT.Ballistics.DamageInfo { Weapon = new object() };
        var victim = Victim("scav-one");
        victim.OnBeenKilledByAggressor(knight, damage, EBodyPart.Head);
        Check(myLand.Value == 2 && unrelated.Value == 1 && woods.Value == 3, "actual death hook credits only My Land from Knight kill");
        victim.OnBeenKilledByAggressor(knight, damage, EBodyPart.Head);
        Check(myLand.Value == 2, "repeated callback cannot double credit same victim");
        Victim("outside", false).OnBeenKilledByAggressor(knight, damage, EBodyPart.Head);
        Check(myLand.Value == 2, "victim zone copied into native condition checks");
        Victim("other-killer").OnBeenKilledByAggressor(new Player { ProfileId = "pipe" }, damage, EBodyPart.Head);
        Check(myLand.Value == 2, "Pipe or unrelated bot cannot receive Knight credit");
        owner.Position.X = 90; Victim("too-far").OnBeenKilledByAggressor(knight, damage, EBodyPart.Head);
        Check(myLand.Value == 2, "remote Knight kill not forwarded"); owner.Position.X = 0;
        player.Location = "woods";
        first.QuestStatus = EQuestStatus.AvailableForFinish;
        knight.RemoveFollowersOnDeath = true;
        knight.OnBeenKilledByAggressor(player, damage, EBodyPart.Head);
        Check(first.QuestStatus == EQuestStatus.Fail && second.QuestStatus == EQuestStatus.Started,
            "Knight death captured before cleanup fails ready task on its map, without failing task on another map");
        Check(BossPlayers.Followers.Count == 0 && player.QuestController.Profile == player.Profile, "death cleanup and profile ownership untouched");
        KnightQuestRuntime.Reset(); BossPlayers.Followers.Add(companion); knight.HealthController.IsAlive = true;
        first.QuestStatus = EQuestStatus.Started; player.Location = "bigmap";
        knight.OnBeenKilledByAggressor(player, damage, EBodyPart.Head);
        Check(second.QuestStatus == EQuestStatus.Fail && first.QuestStatus == EQuestStatus.Started, "Customs death fails My Land only");
        Check(third.QuestStatus == EQuestStatus.Started && KnightQuestPolicy.Owns(third.Id) &&
            !KnightQuestPolicy.UsesMap(third.Id, "TarkovStreets"), "retrieval task remains mode-owned without companion spawning or Knight-death failure");
        Check(KnightQuestPolicy.UsesMap(KnightQuestPolicy.Competition, "LIGHTHOUSE") && !KnightQuestPolicy.UsesMap(KnightQuestPolicy.Competition, "factory4_day"), "map matching is case insensitive and scoped");
        KnightQuestRuntime.Reset();
        BossPlayers.Followers.Clear();
        int previousKills = woods.Value, previousFailure = failure.Value;
        Tick(woods); Tick(myLand); Tick(failure);
        Check(woods.Value == previousKills && failure.Value == previousFailure, "new raid with ordinary teammate and no Knight cannot advance kills or failure");
        GameplayModeRuntime.IsGoingRogue = false;
        KnightQuestRuntime.RegisterKnight(owner, player);
        Tick(woods); Tick(failure);
        Check(woods.Value == previousKills && failure.Value == previousFailure, "other mode cannot register quest Knight or advance counters");
        GameplayModeRuntime.IsGoingRogue = true;
        KnightQuestRuntime.RegisterKnight(owner, player);
        Tick(failure);
        Check(failure.Value == previousFailure + 1, "participant marker preserves native exit failure after follower cleanup");
        player.GameWorld = world = new GameWorld { MainPlayer = player }; Singleton<GameWorld>.Instance = world;
        Tick(failure);
        Check(failure.Value == previousFailure + 1, "a different raid cannot inherit prior Knight participation");
        KnightQuestRuntime.Reset(); BossPlayers.Followers.Clear();
        player.HealthController.IsAlive = false;
        int firstBeforeDeath = failure.Value, secondBeforeDeath = myLandFailure.Value;
        Tick(failure); Tick(myLandFailure);
        Check(failure.Value == firstBeforeDeath && myLandFailure.Value == secondBeforeDeath,
            "dead player without Knight participation cannot advance either native failure counter");
        KnightQuestRuntime.RegisterKnight(owner, player);
        Tick(failure); Tick(myLandFailure);
        Check(failure.Value == firstBeforeDeath + 1 && myLandFailure.Value == secondBeforeDeath + 1,
            "player death still reaches both native failure counters when Knight participated, even after follower cleanup");
        player.GameWorld = world = new GameWorld { MainPlayer = player }; Singleton<GameWorld>.Instance = world;
        Tick(failure); Tick(myLandFailure);
        Check(failure.Value == firstBeforeDeath + 1 && myLandFailure.Value == secondBeforeDeath + 1,
            "a later raid without Knight cannot inherit participation and fail on player death");
        player.HealthController.IsAlive = true;
        KnightQuestRuntime.Reset();
        Console.WriteLine("PASS: " + checks + " Knight quest runtime/Harmony checks. Native quest/navigation behavior requires EFT qualification.");
    }
}

namespace Comfort.Common { public static class Singleton<T> where T : class { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace UnityEngine
{
    public struct Vector3 { public float X; public static float Distance(Vector3 a, Vector3 b) => Math.Abs(a.X - b.X); }
}
namespace EFT
{
    public enum EPlayerSide { Usec, Bear, Savage }
    public enum EBodyPart { Head }
    public enum WildSpawnType { assault, bossKnight }
    public interface IPlayer { string ProfileId { get; } }
    public class Settings { public WildSpawnType Role; }
    public class Info { public Settings Settings = new Settings(); }
    public class Profile { public string Id; public Info Info = new Info(); }
    public class Health { public bool IsAlive = true; public object BodyPartEffects; }
    public class Inventory { public List<string> EquippedInSlotsTemplateIds = new List<string>(); }
    public class GameWorld { public Player MainPlayer; }
    public class BotOwner { public string ProfileId; public Profile Profile; public bool IsDead; public Vector3 Position; public Player GetPlayer; }
    public class Player : IPlayer
    {
        public string ProfileId { get; set; }
        public EPlayerSide Side;
        public bool IsYourPlayer, RemoveFollowersOnDeath;
        public GameWorld GameWorld;
        public Profile Profile = new Profile();
        public Health HealthController = new Health();
        public Quests.QuestController QuestController = new Quests.QuestController();
        public string Location = "bigmap";
        public Vector3 Position;
        public int CurrentHour;
        public List<string> TriggerZones = new List<string>();
        public Inventory Inventory = new Inventory();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnBeenKilledByAggressor(IPlayer aggressor, Ballistics.DamageInfo damageInfo, EBodyPart bodyPart)
        {
            HealthController.IsAlive = false;
            if (RemoveFollowersOnDeath) BossPlayers.Followers.Clear();
        }
    }
}
namespace EFT.Ballistics { public class DamageInfo { public object Weapon; } }
namespace EFT.Quests
{
    public enum EQuestStatus { Started, AvailableForFinish, Fail }
    public class Quest
    {
        public string Id;
        public EQuestStatus QuestStatus;
        public ConditionCounterManager ConditionCountersManager = new ConditionCounterManager();
        public void SetStatus(EQuestStatus status, bool notify, bool fromServer) { QuestStatus = status; }
    }
    public class TaskConditionCounter { public string Id; public Quest Conditional; public int Value; }
    public class ConditionKills { }
    public class ConditionLocation { }
    public class ConditionInZone { }
    public struct ConditionCheck { public Type Type; public object Value; public ConditionCheck(Type type) { Type = type; Value = null; } public ConditionCheck Test(object value) { Value = value; return this; } }
    public class ConditionCounterManager
    {
        public List<TaskConditionCounter> Counters = new List<TaskConditionCounter>();
        public void Test(int value, params ConditionCheck[] checks) { foreach (var counter in Counters) Test(value, counter, checks); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Test(int valueToAdd, TaskConditionCounter counter, ConditionCheck[] checks)
        {
            if (checks.Any(c => c.Type == typeof(ConditionInZone) && !((List<string>)c.Value).Contains("prapor_27_1"))) return;
            counter.Value += valueToAdd;
        }
    }
    public class QuestController
    {
        public Profile Profile;
        public List<Quest> Quests = new List<Quest>();
        public object CheckHitConditionCounter(string target, List<string> equipment, object weapon, EBodyPart part, float distance, string role, int hour, object effects) => weapon;
    }
}
namespace pitTeam.Modules
{
    public static class GameplayModeRuntime { public static bool IsGoingRogue; }
    public class Leader { public Player realPlayer; }
    public class Follower { public BotOwner Bot; public Leader Boss; public BotOwner GetBot() => Bot; public Leader GetBoss() => Boss; }
    public static class BossPlayers { public static List<Follower> Followers = new List<Follower>(); public static List<Follower> GetFollowers() => Followers; }
    public static class Logger { public static void LogError(string text) { throw new Exception(text); } public static void LogInfo(string text) {} }
}

using EFT;
using pitTeam.Modules;
using pitTeam.Components;
using pitTeam.GoingRogue.Combat;
using UnityEngine;

internal static class Program
{
    internal static int checks;
    internal static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static void Main()
    {
        var human = new pitAIBossPlayer();
        BotOwner Add(WildSpawnType role, pitAIBossPlayer leader)
        {
            var bot = new BotOwner(); bot.Profile.Info.Settings.Role = role;
            BossPlayers.Followers.Add(new BotFollowerPlayer { Bot = bot, Leader = leader }); return bot;
        }
        Check(!KnightSquadCoordinator.HasLocalKnight(), "no selected/absent actor can count as Knight");
        var knight = Add(WildSpawnType.bossKnight, human);
        Check(KnightSquadCoordinator.HasLocalKnight(), "living registered Knight governs command willingness before combat ownership");
        knight.IsDead = true; Check(!KnightSquadCoordinator.HasLocalKnight(), "dead Knight does not govern Big Pipe"); knight.IsDead = false;
        knight.BotState = EBotState.Inactive; Check(!KnightSquadCoordinator.HasLocalKnight(), "inactive Knight does not govern Big Pipe"); knight.BotState = EBotState.Active;
        knight.GetPlayer.HealthController.IsAlive = false; Check(!KnightSquadCoordinator.HasLocalKnight(), "dead health excludes Knight"); knight.GetPlayer.HealthController.IsAlive = true;
        human.realPlayer.IsYourPlayer = false; Check(!KnightSquadCoordinator.HasLocalKnight(), "foreign leader Knight is excluded"); human.realPlayer.IsYourPlayer = true;
        var pipe = Add(WildSpawnType.followerBigPipe, human); var bird = Add(WildSpawnType.followerBirdEye, human);
        var stranger = Add(WildSpawnType.followerBigPipe, new pitAIBossPlayer());
        var enemy = new BotOwner(); enemy.ProfileId = "hostile";
        foreach (var bot in new[] { knight, pipe, bird, stranger }) bot.Memory.GoalEnemy = new EnemyInfo { ProfileId = enemy.ProfileId, Person = enemy.GetPlayer };
        var native = new BossKnight { _owner = knight }; knight.Boss.BossLogic = native;
        KnightCombatLayer.Owned.Add(knight);
        using (var coordinator = new KnightSquadCoordinator(knight))
        {
            coordinator.Refresh();
            Check(coordinator.Members.Count == 2 && coordinator.Members.Contains(pipe) && coordinator.Members.Contains(bird), "native follower view contains only this human's Goons");
            Check(!KnightSquadCoordinator.TryGetMemberPosition(pipe, out _), "seeded current positions are not actual Knight orders");
            native.Publish = true; native.UpdatePositions(true);
            foreach (var bot in new[] { pipe, bird })
            {
                Check(KnightSquadCoordinator.TryGetMemberPosition(bot, out var point) && point.x == 20 + bot.Id, "actual Knight assignment reaches role " + bot.Profile.Info.Settings.Role);
                var follower = BossPlayers.Instance.GetFollower(bot); follower.Command = FollowerCommandType.Regroup;
                Check(!KnightSquadCoordinator.TryGetMemberPosition(bot, out _), "player orders retain priority for " + bot.Profile.Info.Settings.Role); follower.Command = FollowerCommandType.None;
                bot.Memory.GoalEnemy.ProfileId = "other";
                Check(!KnightSquadCoordinator.TryGetMemberPosition(bot, out _), "assignment cannot redirect a different enemy for " + bot.Profile.Info.Settings.Role); bot.Memory.GoalEnemy.ProfileId = enemy.ProfileId;
            }
            Check(!KnightSquadCoordinator.TryGetMemberPosition(stranger, out _), "unrelated Goon cannot read Knight orders");
            Check(coordinator.RequestMemberPositionRefresh(pipe.Id), "native recalc routes to managed Big Pipe");
            Check(!KnightSquadCoordinator.TryGetMemberPosition(pipe, out _), "recalc invalidates the old assignment until native recomputation");
            Time.time = 2; coordinator.Refresh();
            Check(KnightSquadCoordinator.TryGetMemberPosition(pipe, out _), "native recomputation republishes the managed assignment");
            Check(coordinator.RequestMemberPositionRefresh(bird.Id), "native recalc routes to managed Birdeye");
            Check(!coordinator.RequestMemberPositionRefresh(stranger.Id), "foreign follower recalc retains native routing");
            Time.time = 4; coordinator.Refresh();
            knight.Memory.GoalEnemy.ProfileId = "new-target";
            Check(!KnightSquadCoordinator.TryGetMemberPosition(pipe, out _), "Knight target changes invalidate old position orders");
            knight.Memory.GoalEnemy.ProfileId = enemy.ProfileId; enemy.GetPlayer.HealthController.IsAlive = false;
            Check(!KnightSquadCoordinator.TryGetMemberPosition(pipe, out _), "dead targets invalidate orders"); enemy.GetPlayer.HealthController.IsAlive = true;
            pipe.IsDead = true; Time.time = 6; coordinator.Refresh();
            Check(!coordinator.Members.Contains(pipe) && !native.FollowersPositions.ContainsKey(pipe), "dead followers lose native assignment state");
            Check(KnightSquadCoordinator.TryGetMemberPosition(bird, out _), "remaining Birdeye keeps his valid assignment");
        }
        Check(!KnightSquadCoordinator.TryGetMemberPosition(bird, out _) && native.Listeners == 0, "release removes orders and native event subscription");
        LeadershipChecks.Run();
        OrderFreshnessChecks.Run();
        UnitTacticsChecks.Run();
        Console.WriteLine($"PASS: {checks} production Knight coordination, shared push verdict and delegation checks; controlled EFT inputs.");
    }
}

namespace UnityEngine
{
    public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; } public float sqrMagnitude=>x*x+y*y+z*z; public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z); }
    public static class Time { public static float time; }
}
namespace EFT
{
    public enum EBotState { Active, Inactive }
    public enum WildSpawnType { bossKnight, followerBigPipe, followerBirdEye, pmcUSEC }
    public interface IPlayer { string ProfileId {get;} Vector3 Position {get;} }
    public class Health { public bool IsAlive = true; }
    public class Player : IPlayer { public string ProfileId {get;set;}=Guid.NewGuid().ToString();public Vector3 Position {get;set;} public Health HealthController = new(); public bool IsYourPlayer = true; public AIData AIData = new(); }
    public class AIData { public BotOwner BotOwner; }
    public class Profile { public Info Info = new(); }
    public class Info { public Settings Settings = new(); }
    public class Settings { public WildSpawnType Role; }
    public class EnemyInfo { public string ProfileId; public Player Person; public bool IsVisible, CanShoot, Eligible=true,Known=true; public float PersonalLastSeenTime = -10; }
    public class Memory { public EnemyInfo GoalEnemy; public bool IsInCover, IsUnderFire; }
    public class Medecine { public bool Using; }
    public class Enemies { public Dictionary<int, EnemyInfo> EnemyInfos = new(); }
    public class Follower { public object BossToFollow; }
    public class BotOwner
    {
        static int next; public int Id = ++next; public string ProfileId = Guid.NewGuid().ToString();
        public bool IsDead, RecentHit; public EBotState BotState; public Profile Profile = new(); public Player GetPlayer = new();
        public Memory Memory = new(); public Medecine Medecine = new(); public Enemies EnemiesController = new();
        public BotBoss Boss = new(); public Follower BotFollower = new(); public Vector3 Position;
    }
}
public class BotBoss { public BossKnight BossLogic; }
public class BossKnight
{
    public BotOwner _owner; public bool Publish; public int Listeners => OnPositionsRecalculated?.GetInvocationList().Length ?? 0;
    public Dictionary<BotOwner, Vector3> FollowersPositions = new(); public Dictionary<int,bool> _shooterPosGood = new();
    public event Action OnPositionsRecalculated;
    public void SetFightPosition(bool good, BotOwner bot) { _shooterPosGood[bot.Id] = good; }
    public void UpdatePositions(bool force)
    {
        if (!Publish) return;
        foreach (var bot in FollowersPositions.Keys.ToArray()) FollowersPositions[bot] = new Vector3(20 + bot.Id, 0, 0);
        OnPositionsRecalculated?.Invoke();
    }
}
namespace pitTeam.Components
{
    public class BossLogic { public bool IsHitted; }
    public class pitAIBossPlayer { public Player realPlayer = new();public BossLogic Logic=new();public BossLogic GetBossLogic()=>Logic; }
    public class BotFollowerPlayer
    {
        public float? UnitAggression,UnitDetermination;public int DeathResets;public bool CommandOverride;
        public BotOwner CompanionLeader;
        public void SetCompanionCombatLeader(BotOwner leader)=>CompanionLeader=leader;
        public bool Independent;
        public void SetCompanionCombatPreferences(float aggression,float determination){UnitAggression=aggression;UnitDetermination=determination;}
        public void ClearCompanionCombatPreferences(){UnitAggression=UnitDetermination=null;CompanionLeader=null;}
        public void ResetCombatPercentageOverrides(string source){CommandOverride=false;if(source=="KnightDied")DeathResets++;}
        public BotOwner Bot; public pitAIBossPlayer Leader; public FollowerCommandType Command;
        public int PushEnemyIssueSequence;public bool HasOrderedPushCancelRequest, HasOrderedPushTargetLock, CombatRegroupUsesBossAnchor;
        public BotOwner GetBot() => Bot; public pitAIBossPlayer GetBoss() => Leader;
        public bool TryPeekActiveCommand(out FollowerCommandType command, out float a, out float b) { command=Command; a=b=0; return command!=FollowerCommandType.None; }
    }
}
namespace pitTeam.Modules
{
    public enum GameplayMode {GoingRogue,Allegiance}
    public static class GameplayModeAddons {public static GameplayMode Current=GameplayMode.GoingRogue;}
    public enum FollowerCommandType { None, Regroup, PushEnemy }
    public static class FollowerEnemyTracking {
        public static bool IsEligible(EnemyInfo enemy)=>enemy.Eligible;
        public static bool TryGetKnownPosition(EnemyInfo enemy,out Vector3 point,out string reason){point=enemy.Person?.Position ?? default;reason="";return enemy.Known;}
    }
    public static class FollowerCombatAnchor {public static bool IsCombatIndependent(BotOwner bot)=>BossPlayers.Instance.GetFollower(bot) is {} f && (f.Independent||f.UnitDetermination==100);}
    public class BossPlayers
    {
        public static BossPlayers Instance = new(); public static List<BotFollowerPlayer> Followers = new();
        public static IEnumerable<BotFollowerPlayer> GetFollowers() => Followers;
        public BotFollowerPlayer GetFollower(BotOwner bot) => Followers.FirstOrDefault(f => f.Bot == bot);
    }
}
namespace pitTeam.BigBrain
{
    public class FollowerCombatCommon(BotOwner owner)
    {
        public static bool WasHitRecently(BotOwner bot,float seconds)=>bot.RecentHit;
        public static bool IsFinite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
        public bool TryUseSupportGoalEnemy(BotOwner enemy, string reason, out EnemyInfo info) { info=owner.Memory.GoalEnemy; return true; }
    }
}
namespace pitTeam.GoingRogue.Combat
{
    public static class GoonCombatLayer { public static HashSet<BotOwner> Owned=new(); public static bool IsOwned(BotOwner bot,string role)=>Owned.Contains(bot); }
    public static class KnightCombatLayer { public static HashSet<BotOwner> Owned = new(); public static bool IsOwned(BotOwner bot) => Owned.Contains(bot) && !bot.IsDead; }
}

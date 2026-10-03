using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Patches;

namespace Comfort.Common { }
namespace UnityEngine {
    public struct Vector3 { public float sqrMagnitude; public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(); }
    public static class Time { public static float time; }
}
namespace SPT.Reflection.Patching {
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
    public class PatchPrefix : Attribute { }
    public class PatchPostfix : Attribute { }
}
namespace EFT {
    public enum EPlayerSide { Usec, Bear, Savage }
    public enum WildSpawnType { exUsec, pmcUSEC, assault, bossKnight, shooterBTR, gifter }
    public interface IPlayer {
        string ProfileId { get; } bool IsAI { get; } Profile Profile { get; }
        AIData AIData { get; } Health HealthController { get; } UnityEngine.Vector3 Position { get; }
    }
    public class Health { public bool IsAlive = true; }
    public class Settings { public WildSpawnType Role; }
    public class ProfileInfo { public EPlayerSide Side; public Settings Settings = new Settings(); }
    public class Profile { public ProfileInfo Info = new ProfileInfo(); public bool KnightFriend; }
    public class AIData { public BotOwner BotOwner; }
    public class Player : IPlayer {
        public string ProfileId { get; set; } public bool IsAI { get; set; }
        public Profile Profile { get; set; } = new Profile(); public AIData AIData { get; set; } = new AIData();
        public Health HealthController { get; set; } = new Health(); public UnityEngine.Vector3 Position => new UnityEngine.Vector3();
        public EPlayerSide Side => Profile.Info.Side;
    }
    public class BotOwner : Player {
        public BotOwner() { IsAI = true; AIData.BotOwner = this; }
        public Player GetPlayer => this; public bool IsDead; public BotsGroup BotsGroup;
        public BotFollower BotFollower = new BotFollower(); public Memory Memory = new Memory();
        public EnemiesController EnemiesController = new EnemiesController();
    }
    public class BotFollower { public pitAIBossPlayer BossToFollow; }
    public class Memory { public EnemyInfo GoalEnemy; }
    public class EnemiesController { public Dictionary<IPlayer, EnemyInfo> EnemyInfos = new Dictionary<IPlayer, EnemyInfo>(); }
    public class EnemyInfo {
        public string ProfileId; public bool IsVisible, CanShoot, HaveSeenPersonal; public float PersonalLastSeenTime;
    }
}
public class BotGroupEnemyInfo { }
public class BotsGroup {
    private static int nextId;
    public int Id = ++nextId; public EPlayerSide Side = EPlayerSide.Savage; public WildSpawnType InitialBotType = WildSpawnType.exUsec;
    public List<BotOwner> Members = new List<BotOwner>(); public int MembersCount => Members.Count;
    public BotOwner Member(int i) => Members[i];
    public Dictionary<IPlayer,BotGroupEnemyInfo> Enemies = new Dictionary<IPlayer,BotGroupEnemyInfo>();
    public Dictionary<IPlayer,BotGroupEnemyInfo> Neutrals = new Dictionary<IPlayer,BotGroupEnemyInfo>();
    public List<IPlayer> Allies = new List<IPlayer>();
    public bool PolicyEnemy, Reject, Throw; public int Calls, MemoryWrites;
    public bool IsEnemy(IPlayer p) => Enemies.ContainsKey(p);
    public bool IsPlayerEnemy(IPlayer p) => PolicyEnemy;
    public bool IsAlly(IPlayer p) => Allies.Contains(p);
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool AddEnemy(IPlayer person, EBotEnemyCause cause) {
        Calls++;
        if (Throw) throw new InvalidOperationException("native failure");
        if (Reject || MembersCount == 0) return false;
        if (!Enemies.ContainsKey(person)) { Enemies.Add(person,new BotGroupEnemyInfo()); Neutrals.Remove(person); Allies.Remove(person); }
        MemoryWrites += MembersCount; return true;
    }
}
namespace pitTeam.Components {
    public class pitAIBossPlayer { public Player realPlayer; public BotsGroup bossGroup; public List<BotOwner> Followers = new List<BotOwner>(); }
    public class BotFollowerPlayer { public pitAIBossPlayer Boss; public pitAIBossPlayer GetBoss() => Boss; }
}
namespace pitTeam.Patches { public class BotsGroupPlayer : BotsGroup { public pitAIBossPlayer Boss; } }
namespace pitTeam.Utils {
    public static class Utils {
        public static bool Friendly, Bad;
        public static bool FlagGet(string s) => s == "pitFireTeam" ? Friendly : s == "isBadGuy" && Bad;
        public static bool PlayerHasKnightQuest(Profile p) => p.KnightFriend;
    }
    public static class Props {
        public static List<WildSpawnType> friendlyBotTypes = new List<WildSpawnType> {WildSpawnType.shooterBTR,WildSpawnType.gifter};
        public static List<WildSpawnType> BossFollowersType = new List<WildSpawnType> {WildSpawnType.bossKnight};
    }
    public static class Enemy {
        public static void ForceIgnoreUntilAggressionOff(BotsGroup group) { }
        __AWARENESS_METHOD__
    }
}
namespace pitTeam.Modules {
    public static class Logger { public static int Errors; public static void LogError(object s) { Errors++; } }
    public static class BossPlayers {
        public static pitAIBossPlayer Boss;
        public static pitAIBossPlayer GetBoss(string id) => Boss.realPlayer.ProfileId == id ? Boss : null;
        public static BotFollowerPlayer GetFollowerByProfileId(string id) => IsFollowerProfileId(id) ? new BotFollowerPlayer{Boss=Boss} : null;
        public static bool IsFollowerProfileId(string id) => Boss.Followers.Exists(f => f.ProfileId == id);
        public static bool IsFollower(BotOwner b) => IsFollowerProfileId(b.ProfileId);
        public static bool IsPlayerBoss(string id) => GetBoss(id) != null;
        public static pitAIBossPlayer GetBossByGroup(int id) => Boss.bossGroup.Id == id ? Boss : null;
    }
    public static class FactionHostility { public static bool IsScavFaction(Player p) => p.Profile.Info.Settings.Role == WildSpawnType.assault; }
    public static class FollowerCalcGoalEnemyAcquire {
        __ACQUISITION_METHOD__
        public static bool HasDebouncedSameSideHostileIntent(BotOwner owner, string id, bool hostile) => hostile;
        public static bool CandidateHasBossOrFollowerAsEnemy(BotOwner owner, Player p) => CandidateHasBossOrFollowerAsEnemy(owner.BotFollower.BossToFollow,p);
        public static bool CandidateHasGoalEnemyBossOrFollower(BotOwner owner, Player p) => false;
        public static bool CandidateHasBossOrFollowerAsEnemy(pitAIBossPlayer b, Player p) => p.AIData.BotOwner.BotsGroup.IsEnemy(b.realPlayer) || b.Followers.Exists(f => p.AIData.BotOwner.BotsGroup.IsEnemy(f));
        public static bool CandidateHasGoalEnemyBossOrFollower(pitAIBossPlayer b, Player p) => false;
    }
}
public static class HostilityChecks {
    private static int checks;
    private static pitAIBossPlayer boss; private static BotsGroup outside; private static BotOwner rogue, first, second;
    private static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    private static void Setup() {
        pitTeam.Utils.Utils.Friendly = pitTeam.Utils.Utils.Bad = false;
        boss = new pitAIBossPlayer { realPlayer = new Player {ProfileId="boss"} };
        var own = new BotsGroupPlayer {Boss=boss,Side=EPlayerSide.Usec,InitialBotType=WildSpawnType.pmcUSEC};boss.bossGroup=own;BossPlayers.Boss=boss;
        first = new BotOwner {ProfileId="first",BotsGroup=own};second = new BotOwner{ProfileId="second",BotsGroup=own};
        foreach (var f in new[]{first,second}) { f.BotFollower.BossToFollow=boss;f.Profile.Info.Settings.Role=WildSpawnType.pmcUSEC;boss.Followers.Add(f);own.Members.Add(f); }
        outside = new BotsGroup();rogue = new BotOwner {ProfileId="rogue",BotsGroup=outside};rogue.Profile.Info.Side=EPlayerSide.Savage;rogue.Profile.Info.Settings.Role=WildSpawnType.exUsec;outside.Members.Add(rogue);
    }
    public static void Main() {
        var h=new Harmony("pit.tests.hostility");
        h.Patch(AccessTools.Method(typeof(BotsGroup),nameof(BotsGroup.AddEnemy)),new HarmonyMethod(typeof(BotGroupAddEnemyPatch),"PatchPrefix"),new HarmonyMethod(typeof(BotGroupAddEnemyPatch),"PatchPostfix"));
        foreach(var cause in new[]{EBotEnemyCause.initial,EBotEnemyCause.AddNewMember,EBotEnemyCause.checkAddTODO,EBotEnemyCause.addPlayerToBoss,EBotEnemyCause.initCauseEnemy}) {
            Setup();Check(!outside.AddEnemy(first,cause),"neutral rogue must not acquire follower: "+cause);
            Check(!boss.bossGroup.AddEnemy(rogue,cause),"follower must not acquire neutral rogue: "+cause);
            Check(outside.Calls==0 && boss.bossGroup.Calls==0,"rejection before native memory mutation");
            Check(FollowerCalcGoalEnemyAcquire.ShouldBlockCandidateForMissingHostileIntent(first,rogue,false),"idle/MakeEnemy scan also refuses neutral rogue");
        }
        Setup();outside.PolicyEnemy=true;outside.Neutrals[boss.realPlayer]=new BotGroupEnemyInfo();
        Check(!outside.AddEnemy(first,EBotEnemyCause.initial),"explicit neutrality wins over faction default");
        outside.Neutrals.Clear();outside.Allies.Add(boss.realPlayer);
        Check(!boss.bossGroup.AddEnemy(rogue,EBotEnemyCause.checkAddTODO),"ally status wins in outgoing scan");
        Setup();outside.PolicyEnemy=true;first.Profile.Info.Side=EPlayerSide.Bear;
        Check(outside.AddEnemy(first,EBotEnemyCause.initial),"hostile player policy admits a differently sided follower");
        Check(outside.IsEnemy(boss.realPlayer)&&outside.IsEnemy(second)&&boss.bossGroup.IsEnemy(rogue),"successful enemy addition fans out both ways");
        Check(first.Memory.GoalEnemy==null && second.Memory.GoalEnemy==null && rogue.Memory.GoalEnemy==null,"sharing never selects a goal");
        int writes=outside.MemoryWrites+boss.bossGroup.MemoryWrites;
        FollowerGroupHostility.ShareHostility(outside,boss,EBotEnemyCause.initial);
        Check(writes==outside.MemoryWrites+boss.bossGroup.MemoryWrites,"repeat share has no native writes");
        Setup();outside.Neutrals[boss.realPlayer]=new BotGroupEnemyInfo();outside.Allies.Add(first);boss.bossGroup.Allies.Add(rogue);
        FollowerGroupHostility.DeclareContact(first,rogue);
        Check(outside.IsEnemy(boss.realPlayer)&&outside.IsEnemy(first)&&outside.IsEnemy(second)&&boss.bossGroup.IsEnemy(rogue),"Contact overrides neutral rogue for entire squad");
        Check(!outside.Allies.Contains(first)&&!outside.Neutrals.ContainsKey(boss.realPlayer)&&!boss.bossGroup.Allies.Contains(rogue),"Contact removes contradictory neutral/ally entries");
        Check(!FollowerGroupHostility.ShouldBlockCandidate(boss,rogue),"Contact remains eligible after command scope ends");
        Check(!FollowerCalcGoalEnemyAcquire.ShouldBlockCandidateForMissingHostileIntent(first,rogue,false),"Contact passes Core acquisition gate");
        Setup();rogue.Profile.Info.Settings.Role=WildSpawnType.assault;outside.InitialBotType=WildSpawnType.assault;
        Check(!boss.bossGroup.AddEnemy(rogue,EBotEnemyCause.checkAddTODO),"neutral Scav ignored");
        FollowerGroupHostility.DeclareContact(first,rogue);
        Check(boss.bossGroup.IsEnemy(rogue)&&outside.IsEnemy(boss.realPlayer),"Contact also overrides neutral Scav");
        foreach(var cause in new[]{EBotEnemyCause.byKill,EBotEnemyCause.followGetHit,EBotEnemyCause.AddEnemyToAllGroupsInBotZone,EBotEnemyCause.AddEnemyToAllGroups}) {
            Setup();Check(outside.AddEnemy(first,cause),"real aggression bypasses neutrality: "+cause);
            Check(outside.IsEnemy(boss.realPlayer)&&outside.IsEnemy(second),"aggression includes player and sibling");
            Setup();rogue.Profile.Info.Settings.Role=WildSpawnType.assault;
            Check(boss.bossGroup.AddEnemy(rogue,cause),"incoming damage admits neutral attacker");
            Check(outside.IsEnemy(boss.realPlayer)&&outside.IsEnemy(first)&&outside.IsEnemy(second),"incoming damage reciprocates");
        }
        Setup();outside.PolicyEnemy=true;outside.Members.Clear();
        Check(!outside.AddEnemy(first,EBotEnemyCause.initial)&&!outside.IsEnemy(boss.realPlayer),"deferred construction never fans out");
        outside.Members.Add(rogue);Check(outside.AddEnemy(first,EBotEnemyCause.AddNewMember)&&outside.IsEnemy(boss.realPlayer),"successful construction replay fans out");
        Setup();outside.PolicyEnemy=true;outside.Reject=true;
        Check(!outside.AddEnemy(first,EBotEnemyCause.initial)&&outside.Enemies.Count==0&&boss.bossGroup.Enemies.Count==0,"native validation rejection has no raw fallback");
        Setup();second.IsDead=true;second.HealthController.IsAlive=false;FollowerGroupHostility.DeclareContact(first,rogue);
        Check(!outside.IsEnemy(second)&&outside.IsEnemy(boss.realPlayer),"dead followers excluded");
        Setup();outside.InitialBotType=WildSpawnType.shooterBTR;rogue.Profile.Info.Settings.Role=WildSpawnType.shooterBTR;
        FollowerGroupHostility.DeclareContact(first,rogue);Check(outside.Enemies.Count==0&&boss.bossGroup.Enemies.Count==0,"protected BTR not converted");
        Setup();outside.PolicyEnemy=true;boss.realPlayer.Profile.KnightFriend=true;
        Check(!outside.AddEnemy(first,EBotEnemyCause.initial),"quest friendship protects follower");
        FollowerGroupHostility.DeclareContact(first,rogue);Check(outside.IsEnemy(boss.realPlayer),"explicit Contact overrides quest-neutral rogue");
        Setup();outside.InitialBotType=WildSpawnType.pmcUSEC;outside.Side=EPlayerSide.Usec;outside.PolicyEnemy=true;pitTeam.Utils.Utils.Friendly=true;
        Check(!outside.AddEnemy(first,EBotEnemyCause.initial),"same-side friendly setting retained");
        Setup();var unrelated=new BotOwner{ProfileId="ordinary"};Check(outside.AddEnemy(unrelated,EBotEnemyCause.initial)&&!outside.IsEnemy(boss.realPlayer),"ordinary bots unaffected");
        Setup();outside.Throw=true;FollowerGroupHostility.DeclareContact(first,rogue);
        Check(!FollowerGroupHostility.IsSharing&&Logger.Errors>0,"native failure releases sharing guard");
        outside.Throw=false;FollowerGroupHostility.DeclareContact(first,rogue);Check(outside.IsEnemy(boss.realPlayer),"subsequent contact still works");
        Setup();outside.Reject=true;FollowerGroupHostility.DeclareContact(first,rogue);
        Check(outside.Enemies.Count==0 && boss.bossGroup.Enemies.Count==0,"Contact cannot bypass native rejection");
        Setup();outside.PolicyEnemy=true;boss.realPlayer.Profile.Info.Side=EPlayerSide.Bear;
        Check(outside.AddEnemy(first,EBotEnemyCause.initial)&&outside.IsEnemy(boss.realPlayer),"USEC follower inherits hostile BEAR player relationship");
        Setup();first.Profile.Info.Side=EPlayerSide.Bear;
        Check(!outside.AddEnemy(first,EBotEnemyCause.initial),"BEAR follower inherits neutral USEC player relationship");
        Setup();FollowerGroupHostility.OnDamage(first,rogue);
        Check(outside.IsEnemy(boss.realPlayer)&&outside.IsEnemy(second)&&boss.bossGroup.IsEnemy(rogue),"actual AI damage authorizes retaliation before native checkAddTODO");
        Setup();FollowerGroupHostility.OnDamage(rogue,first);
        Check(outside.IsEnemy(boss.realPlayer)&&outside.IsEnemy(second)&&boss.bossGroup.IsEnemy(rogue),"follower damaging neutral AI makes whole squad hostile");
        Setup();FollowerGroupHostility.OnDamage(first,second);FollowerGroupHostility.OnDamage(first,boss.realPlayer);
        Check(outside.Enemies.Count==0&&boss.bossGroup.Enemies.Count==0,"squad friendly fire never declares hostility");
        Setup();FollowerGroupHostility.OnDamage(rogue,new BotOwner{ProfileId="stranger"});
        Check(outside.Enemies.Count==0&&boss.bossGroup.Enemies.Count==0,"ordinary AI damage remains native");
        h.UnpatchSelf();Console.WriteLine("Passed "+checks+" production hostility checks.");
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Patches;
using pitTeam.Utils;
using UnityEngine;

namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace UnityEngine {
    public struct Vector3 { public float sqrMagnitude; public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(); }
    public static class Time { public static float time; }
    public static class Random { public static int Rolls; public static float NextValue; public static float value { get { Rolls++; return NextValue; } } }
}
namespace SPT.Reflection.Patching {
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
    public class PatchPrefix : Attribute { }
    public class PatchPostfix : Attribute { }
}
namespace EFT {
    public enum EPlayerSide { Usec, Bear, Savage }
    public enum EBodyPart { Chest }
    public enum WildSpawnType { exUsec, pmcUSEC, pmcBEAR, assault, bossKnight, shooterBTR, gifter }
    public interface IPlayer {
        string ProfileId { get; } string GroupId { get; } EPlayerSide Side { get; } bool IsAI { get; } Profile Profile { get; }
        AIData AIData { get; } Health HealthController { get; } UnityEngine.Vector3 Position { get; }
    }
    public class Health { public bool IsAlive = true; }
    public class Settings { public WildSpawnType Role; }
    public class ProfileInfo { public EPlayerSide Side; public Settings Settings = new Settings(); }
    public class Profile { public ProfileInfo Info = new ProfileInfo(); public bool KnightFriend; }
    public class AIData { public BotOwner BotOwner; }
    public class Player : IPlayer {
        public string ProfileId { get; set; } public bool IsAI { get; set; }
        public string GroupId => "group-" + ProfileId;
        public Profile Profile { get; set; } = new Profile(); public AIData AIData { get; set; } = new AIData();
        public Health HealthController { get; set; } = new Health(); public UnityEngine.Vector3 Position => new UnityEngine.Vector3();
        public EPlayerSide Side => Profile.Info.Side;
    }
    public class BotOwner : Player {
        public BotOwner() { IsAI = true; AIData.BotOwner = this; }
        public Player PlayerObject; public Player GetPlayer => PlayerObject ?? this; public bool IsDead; public BotsGroup BotsGroup;
        public SpawnData SpawnProfileData = new SpawnData();
        public BotFollower BotFollower = new BotFollower(); public Memory Memory = new Memory();
        public EnemiesController EnemiesController = new EnemiesController();
    }
    public class BotFollower { public pitAIBossPlayer BossToFollow; }
    public class Memory { public EnemyInfo GoalEnemy; public void DeleteInfoAboutEnemy(IPlayer player) { if (GoalEnemy?.ProfileId == player.ProfileId) GoalEnemy = null; } }
    public class GameWorld { public Player MainPlayer; }
    public class SpawnData { public SpawnParams SpawnParams = new SpawnParams(); }
    public class SpawnParams { public GroupParams ShallBeGroup = new GroupParams(); }
    public class GroupParams { public bool Group; public int StartCount = 1; }
    public class EnemiesController { public Dictionary<IPlayer, EnemyInfo> EnemyInfos = new Dictionary<IPlayer, EnemyInfo>(); }
    public class EnemyInfo {
        public string ProfileId; public bool IsVisible, CanShoot, HaveSeenPersonal; public float PersonalLastSeenTime;
    }
}
namespace EFT.Ballistics { public struct DamageInfo { public EFT.Player Player; public float Damage; } }
public class BotGroupEnemyInfo { }
public class BotsGroup {
    private static int nextId;
    public int Id = ++nextId; public EPlayerSide Side = EPlayerSide.Savage; public WildSpawnType InitialBotType = WildSpawnType.exUsec;
    public List<BotOwner> Members = new List<BotOwner>(); public int MembersCount => Members.Count;
    public int TargetMembersCount; public event Action<BotOwner> OnMemberAdd;
    public HashSet<string> _enemyPlayerGroups = new HashSet<string>();
    public void AddMember(BotOwner member) { Members.Add(member); OnMemberAdd?.Invoke(member); }
    public event Action<IPlayer> OnEnemyRemove;
    public void RemoveEnemy(IPlayer player) { Enemies.Remove(player); OnEnemyRemove?.Invoke(player); Neutrals.Remove(player); foreach (var m in Members) m.Memory.DeleteInfoAboutEnemy(player); }
    public void AddNeutral(IPlayer player) { Neutrals[player] = new BotGroupEnemyInfo(); }
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
    public class pitAIBossPlayer { public Player realPlayer; public BotsGroup bossGroup; public List<BotOwner> Followers = new List<BotOwner>(); public int Engagements; public void MarkPlayerEngagement(Player enemy,string source) {Engagements++;} }
    public class AIBossPlayerLogic {
        public pitAIBossPlayer _aiplayer; public float _lastTimeHit;
        __BOSS_HIT_METHOD__
    }
    public class BotFollowerPlayer { public pitAIBossPlayer Boss; public BotOwner Bot; public BotOwner GetBot() => Bot; public pitAIBossPlayer GetBoss() => Boss; }
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
    internal static class PmcKarmaRuntime { internal static void NoteFriendly(BotOwner bot) { } }
    internal sealed class AllegianceFriendlyGreeting { internal static int Calls; internal void Update(BotOwner bot, Player human) { Calls++; } }
    public static class FriendlyEncounterPenaltyRuntime { public static int Points; public static int GetPoints() => pitTeam.Shared.FriendlyEncounterPenaltyPolicy.ScalePoints(Points,pitTeam.pitFireTeam.friendlyChanceMultiplier.Value); }
    public static class Logger { public static int Errors; public static void LogError(object s) { Errors++; } public static void LogInfo(string s) { } }
    public static class GameplayModeRuntime { public static bool IsAllegiance; }
    public static class BossPlayers {
        public static pitAIBossPlayer Boss;
        public static pitAIBossPlayer GetBoss(string id) => Boss.realPlayer.ProfileId == id ? Boss : null;
        public static BotFollowerPlayer GetFollowerByProfileId(string id) => IsFollowerProfileId(id) ? new BotFollowerPlayer{Boss=Boss} : null;
        public static bool IsFollowerProfileId(string id) => Boss.Followers.Exists(f => f.ProfileId == id);
        public static bool IsFollower(BotOwner b,pitAIBossPlayer boss=null) => IsFollowerProfileId(b.ProfileId);
        public static List<BotFollowerPlayer> GetFollowersByBoss(string id) => Boss.Followers.ConvertAll(b => new BotFollowerPlayer { Boss=Boss, Bot=b });
        public static bool IsPlayerBoss(string id) => GetBoss(id) != null;
        public static pitAIBossPlayer GetBossByGroup(int id) => Boss.bossGroup.Id == id ? Boss : null;
    }
    public static class FactionHostility {
        public static bool IsScavFaction(Player p) => p.Profile.Info.Settings.Role == WildSpawnType.assault;
        __NEUTRAL_METHOD__
    }
    public static class FollowerCalcGoalEnemyAcquire {
        __ACQUISITION_METHOD__
        public static bool HasDebouncedSameSideHostileIntent(BotOwner owner, string id, bool hostile) => hostile;
        public static bool CandidateHasBossOrFollowerAsEnemy(BotOwner owner, Player p) => CandidateHasBossOrFollowerAsEnemy(owner.BotFollower.BossToFollow,p);
        public static bool CandidateHasGoalEnemyBossOrFollower(BotOwner owner, Player p) => false;
        public static bool CandidateHasBossOrFollowerAsEnemy(pitAIBossPlayer b, Player p) => p.AIData.BotOwner.BotsGroup.IsEnemy(b.realPlayer) || b.Followers.Exists(f => p.AIData.BotOwner.BotsGroup.IsEnemy(f));
        public static bool CandidateHasGoalEnemyBossOrFollower(pitAIBossPlayer b, Player p) => false;
    }
}
namespace pitTeam {
    public static class pitFireTeam {
        public sealed class IntSetting { public int Value=1; }
        public static IntSetting friendlyChanceMultiplier=new IntSetting();
    }
}
namespace pitTeam.Utils { public static class FollowerAwareness { public static void RegisterBossRangedThreatWatch(BotOwner follower,BotOwner enemy) {} } }
public static class HostilityChecks {
    private static int checks;
    private static pitAIBossPlayer boss; private static BotsGroup outside; private static BotOwner rogue, first, second;
    private static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    private static void Setup() {
        AllegiancePmcFriendship.Reset(); GameplayModeRuntime.IsAllegiance=false; UnityEngine.Random.Rolls=0; UnityEngine.Random.NextValue=0f;
        pitTeam.pitFireTeam.friendlyChanceMultiplier.Value=1;
        FriendlyEncounterPenaltyRuntime.Points=0;
        pitTeam.Utils.Utils.Friendly = pitTeam.Utils.Utils.Bad = false;
        boss = new pitAIBossPlayer { realPlayer = new Player {ProfileId="boss"} };
        var own = new BotsGroupPlayer {Boss=boss,Side=EPlayerSide.Usec,InitialBotType=WildSpawnType.pmcUSEC};boss.bossGroup=own;BossPlayers.Boss=boss;
        first = new BotOwner {ProfileId="first",BotsGroup=own};second = new BotOwner{ProfileId="second",BotsGroup=own};
        foreach (var f in new[]{first,second}) { f.BotFollower.BossToFollow=boss;f.Profile.Info.Settings.Role=WildSpawnType.pmcUSEC;boss.Followers.Add(f);own.Members.Add(f); }
        outside = new BotsGroup();rogue = new BotOwner {ProfileId="rogue",BotsGroup=outside};rogue.Profile.Info.Side=EPlayerSide.Savage;rogue.Profile.Info.Settings.Role=WildSpawnType.exUsec;outside.Members.Add(rogue);
        Comfort.Common.Singleton<GameWorld>.Instance = new GameWorld { MainPlayer=boss.realPlayer };
    }
    private static BotOwner Solo(string id, EPlayerSide? side=null) {
        var faction=side ?? boss.realPlayer.Side;
        var group=new BotsGroup {Side=faction, InitialBotType=faction==EPlayerSide.Bear ? WildSpawnType.pmcBEAR : WildSpawnType.pmcUSEC};
        var bot=new BotOwner {ProfileId=id,BotsGroup=group};bot.Profile.Info.Side=faction;bot.Profile.Info.Settings.Role=group.InitialBotType;group.Members.Add(bot);return bot;
    }
    private static void AllegianceChecks() {
        Setup();var bot=Solo("solo");AllegiancePmcFriendship.Apply(bot);
        Check(UnityEngine.Random.Rolls==0 && AllegiancePmcFriendship.CanRecruit(bot,boss.realPlayer),"Guns for Hire does not roll or gate recruitment");
        GameplayModeRuntime.IsAllegiance=true;
        AllegiancePmcFriendship.Apply(rogue);AllegiancePmcFriendship.Apply(first);
        Check(UnityEngine.Random.Rolls==0,"Rogues and owned followers never roll");
        Check(!AllegiancePmcFriendship.CanRecruit(bot,boss.realPlayer),"Recruit request cannot roll an unobserved bot");
        var grouped=Solo("grouped");grouped.SpawnProfileData.SpawnParams.ShallBeGroup.Group=true;grouped.SpawnProfileData.SpawnParams.ShallBeGroup.StartCount=3;
        AllegiancePmcFriendship.Apply(grouped);grouped.SpawnProfileData.SpawnParams.ShallBeGroup.Group=false;AllegiancePmcFriendship.Apply(grouped);
        Check(UnityEngine.Random.Rolls==0 && !AllegiancePmcFriendship.CanRecruit(grouped,boss.realPlayer),"Original group first member excluded permanently");
        grouped=Solo("target-group");grouped.BotsGroup.TargetMembersCount=2;AllegiancePmcFriendship.Apply(grouped);
        grouped.BotsGroup.TargetMembersCount=0;AllegiancePmcFriendship.Apply(grouped);
        Check(UnityEngine.Random.Rolls==0 && !AllegiancePmcFriendship.CanRecruit(grouped,boss.realPlayer),"Target group size excludes a later survivor permanently");
        grouped=Solo("current-group");grouped.BotsGroup.Members.Add(Solo("other"));AllegiancePmcFriendship.Apply(grouped);grouped.BotsGroup.Members.RemoveAt(1);AllegiancePmcFriendship.Apply(grouped);
        Check(UnityEngine.Random.Rolls==0 && !AllegiancePmcFriendship.CanRecruit(grouped,boss.realPlayer),"Current group survivor cannot reroll");
        bot.BotsGroup.Enemies[boss.realPlayer]=new BotGroupEnemyInfo();bot.BotsGroup.Enemies[first]=new BotGroupEnemyInfo();bot.Memory.GoalEnemy=new EnemyInfo {ProfileId=boss.realPlayer.ProfileId};
        boss.bossGroup.Enemies[bot]=new BotGroupEnemyInfo();first.Memory.GoalEnemy=new EnemyInfo {ProfileId=bot.ProfileId};bot.BotsGroup._enemyPlayerGroups.Add(boss.realPlayer.GroupId);
        AllegiancePmcFriendship.Apply(bot);
        Check(AllegiancePmcFriendship.CanRecruit(bot,boss.realPlayer) && UnityEngine.Random.Rolls==1,"Successful friendship roll makes solo recruitable");
        Check(bot.BotsGroup.Neutrals.ContainsKey(boss.realPlayer) && bot.BotsGroup.Neutrals.ContainsKey(first) && boss.bossGroup.Neutrals.ContainsKey(bot),"Selection repairs setup relationships both ways");
        Check(bot.Memory.GoalEnemy==null && first.Memory.GoalEnemy==null && !bot.BotsGroup._enemyPlayerGroups.Contains(boss.realPlayer.GroupId),"Only selected squad setup memories and hostile group cache are removed");
        AllegiancePmcFriendship.Apply(bot);Check(UnityEngine.Random.Rolls==1,"Repeated activation does not reroll");
        foreach(var cause in new[]{EBotEnemyCause.initial,EBotEnemyCause.AddNewMember,EBotEnemyCause.checkAddTODO,EBotEnemyCause.addPlayerToBoss,EBotEnemyCause.initCauseEnemy}) {
            Check(!bot.BotsGroup.AddEnemy(boss.realPlayer,cause) && !bot.BotsGroup.AddEnemy(first,cause) && !boss.bossGroup.AddEnemy(bot,cause),"Selected candidate resists ambient scan and queued setup replay: "+cause);
        }
        var stranger=Solo("stranger");Check(bot.BotsGroup.AddEnemy(stranger,EBotEnemyCause.initial),"Selected candidate still has ordinary bot hostility");
        var next=Solo("next");var third=Solo("third");var capped=Solo("capped");AllegiancePmcFriendship.Apply(next);AllegiancePmcFriendship.Apply(third);AllegiancePmcFriendship.Apply(capped);
        Check(UnityEngine.Random.Rolls==3 && !AllegiancePmcFriendship.CanRecruit(capped,boss.realPlayer),"Raid lifetime cap is three selections");
        bot.IsDead=true;bot.HealthController.IsAlive=false;boss.Followers.Add(next);AllegiancePmcFriendship.Apply(capped);AllegiancePmcFriendship.Apply(Solo("replacement"));
        Check(UnityEngine.Random.Rolls==3,"Death and recruitment do not replenish selections");boss.Followers.Remove(next);
        FollowerGroupHostility.OnDamage(third,first);AllegiancePmcFriendship.Apply(third);
        Check(!AllegiancePmcFriendship.CanRecruit(third,boss.realPlayer) && third.BotsGroup.IsEnemy(boss.realPlayer) && UnityEngine.Random.Rolls==3,"Squad damage revokes friendship without rerolling");
        FollowerGroupHostility.DeclareContact(first,next);AllegiancePmcFriendship.Apply(next);
        Check(!AllegiancePmcFriendship.CanRecruit(next,boss.realPlayer) && next.BotsGroup.IsEnemy(second),"Explicit Contact revokes selected friendship for whole squad");
        Setup();GameplayModeRuntime.IsAllegiance=true;bot=Solo("growing");AllegiancePmcFriendship.Apply(bot);bot.BotsGroup.AddMember(Solo("joined"));
        Check(!AllegiancePmcFriendship.CanRecruit(bot,boss.realPlayer) && bot.BotsGroup.IsEnemy(boss.realPlayer),"Joining a group ends individual friendship");
        Setup();GameplayModeRuntime.IsAllegiance=true;bot=Solo("converted");AllegiancePmcFriendship.Apply(bot);var formerGroup=bot.BotsGroup;formerGroup.Members.Remove(bot);boss.Followers.Add(bot);bot.BotsGroup=boss.bossGroup;
        formerGroup.AddMember(Solo("new-first"));formerGroup.AddMember(Solo("new-second"));
        Check(!formerGroup.IsEnemy(boss.realPlayer),"Recruit conversion detaches original group observer without declaring unrelated hostility");
        AllegiancePmcFriendship.Reset();UnityEngine.Random.Rolls=0;bot=Solo("fresh-raid");AllegiancePmcFriendship.Apply(bot);
        Check(UnityEngine.Random.Rolls==1 && AllegiancePmcFriendship.CanRecruit(bot,boss.realPlayer),"Raid reset clears decisions and lifetime cap");
        Setup();GameplayModeRuntime.IsAllegiance=true;boss.realPlayer.Profile.Info.Side=EPlayerSide.Bear;bot=Solo("bear");AllegiancePmcFriendship.Apply(bot);
        Check(AllegiancePmcFriendship.CanRecruit(bot,boss.realPlayer),"BEAR players select BEAR solo PMCs");
        Setup();GameplayModeRuntime.IsAllegiance=true;bot=Solo("standby-contact");FollowerGroupHostility.DeclareContact(first,bot);AllegiancePmcFriendship.Apply(bot);
        Check(UnityEngine.Random.Rolls==0 && !AllegiancePmcFriendship.CanRecruit(bot,boss.realPlayer) && bot.BotsGroup.IsEnemy(boss.realPlayer),"Contact before initial activation prevents a later friendliness roll");
        Setup();GameplayModeRuntime.IsAllegiance=true;bot=Solo("incoming-damage");AllegiancePmcFriendship.Apply(bot);FollowerGroupHostility.OnDamage(first,bot);AllegiancePmcFriendship.Apply(bot);
        Check(!AllegiancePmcFriendship.CanRecruit(bot,boss.realPlayer) && bot.BotsGroup.IsEnemy(boss.realPlayer),"Candidate damaging follower also revokes friendship");
        Setup();GameplayModeRuntime.IsAllegiance=true;boss.realPlayer.Profile.Info.Side=EPlayerSide.Savage;AllegiancePmcFriendship.Apply(Solo("scav-raid"));
        Check(UnityEngine.Random.Rolls==0 && AllegiancePmcFriendship.CanRecruit(rogue,boss.realPlayer),"Player Scav faction and Fence recruitment remain native");
        AllegiancePmcFriendship.Reset();
    }
    private static void RecruitedFriendshipChecks() {
        foreach (bool existingSquad in new[]{false,true})
        foreach (var playerSide in new[]{EPlayerSide.Usec,EPlayerSide.Bear})
        foreach (var recruitSide in new[]{EPlayerSide.Usec,EPlayerSide.Bear})
        foreach (var candidateSide in new[]{EPlayerSide.Usec,EPlayerSide.Bear})
        foreach (bool cacheOnly in new[]{false,true}) {
            Setup();GameplayModeRuntime.IsAllegiance=true;boss.realPlayer.Profile.Info.Side=playerSide;
            if (!existingSquad) { boss.Followers.Clear();boss.bossGroup=null; }
            var recruit=Solo("new-recruit",recruitSide);var candidate=Solo("remaining-friendly",candidateSide);
            foreach (var bot in new[]{recruit,candidate})
                bot.PlayerObject=new Player {ProfileId=bot.ProfileId,IsAI=true,Profile=bot.Profile,AIData=bot.AIData};
            // Both decisions predate recruitment; they may still regard each other as enemies.
            candidate.BotsGroup.Enemies[recruit]=new BotGroupEnemyInfo();
            candidate.BotsGroup.Enemies[recruit.GetPlayer]=new BotGroupEnemyInfo();
            candidate.BotsGroup._enemyPlayerGroups.Add(recruit.GetPlayer.GroupId);
            AllegiancePmcFriendship.Apply(candidate);AllegiancePmcFriendship.Apply(recruit);
            Check(candidate.BotsGroup.IsEnemy(recruit),"Initial selection does not neutralize an unrelated future recruit");
            recruit.BotsGroup.Members.Remove(recruit);
            if (boss.bossGroup==null) boss.bossGroup=new BotsGroupPlayer {Boss=boss,Side=playerSide};
            recruit.BotsGroup=boss.bossGroup;recruit.BotFollower.BossToFollow=boss;
            boss.Followers.Add(recruit);boss.bossGroup.Members.Add(recruit);
            boss.bossGroup.Enemies[candidate]=new BotGroupEnemyInfo();
            boss.bossGroup.Enemies[candidate.GetPlayer]=new BotGroupEnemyInfo();
            candidate.Memory.GoalEnemy=new EnemyInfo {ProfileId=recruit.ProfileId};
            recruit.Memory.GoalEnemy=new EnemyInfo {ProfileId=candidate.ProfileId};
            var hostile=Solo("ordinary-hostile");
            candidate.BotsGroup.Enemies[hostile]=new BotGroupEnemyInfo();boss.bossGroup.Enemies[hostile]=new BotGroupEnemyInfo();
            var candidateCache=new HashSet<string>{recruit.ProfileId};var recruitCache=new HashSet<string>{candidate.ProfileId};
            candidate.BotsGroup.OnEnemyRemove+=person=>candidateCache.Remove(person.ProfileId);
            boss.bossGroup.OnEnemyRemove+=person=>recruitCache.Remove(person.ProfileId);
            if (cacheOnly) { candidate.BotsGroup.Enemies.Clear();boss.bossGroup.Enemies.Clear(); }
            AllegiancePmcFriendship.OnFollowerAdded(recruit,boss.realPlayer);
            Check(!candidate.BotsGroup.IsEnemy(recruit) && !candidate.BotsGroup.IsEnemy(recruit.GetPlayer),"Remaining friendly drops new recruit's BotOwner and Player enemy aliases");
            Check(!boss.bossGroup.IsEnemy(candidate) && !boss.bossGroup.IsEnemy(candidate.GetPlayer),"New recruit's group drops remaining friendly in the reverse direction");
            Check(candidate.BotsGroup.Neutrals.ContainsKey(recruit.GetPlayer) && boss.bossGroup.Neutrals.ContainsKey(candidate.GetPlayer),"Recruitment establishes reciprocal neutrality for either faction");
            Check(candidate.Memory.GoalEnemy==null && recruit.Memory.GoalEnemy==null,"Only stale friendly-pair combat targets are cleared");
            Check(candidateCache.Count==0 && recruitCache.Count==0,"Native removal notifications clear both external enemy caches even without dictionary entries");
            Check(!candidate.BotsGroup._enemyPlayerGroups.Contains(recruit.GetPlayer.GroupId),"Remaining friendly drops recruit's hostile group cache");
            if (!cacheOnly) Check(candidate.BotsGroup.IsEnemy(hostile) && boss.bossGroup.IsEnemy(hostile),"Ordinary hostile relationships survive the recruitment refresh");
            Check(UnityEngine.Random.Rolls==2 && AllegiancePmcFriendship.CanRecruit(candidate,boss.realPlayer),"Recruitment refresh neither rolls again nor revokes remaining friendship");
            Check(!candidate.BotsGroup.AddEnemy(recruit.GetPlayer,EBotEnemyCause.initial) && !boss.bossGroup.AddEnemy(candidate.GetPlayer,EBotEnemyCause.initial),"Ambient faction scans cannot restore repaired hostility");
            Check(recruit.Side==recruitSide && candidate.Side==candidateSide,"Recruitment refresh preserves both native factions");
        }

        Setup();GameplayModeRuntime.IsAllegiance=true;
        var friendly=Solo("revoked-friendly");AllegiancePmcFriendship.Apply(friendly);
        FollowerGroupHostility.OnDamage(first,friendly);
        AllegiancePmcFriendship.OnFollowerAdded(second,boss.realPlayer);
        Check(!AllegiancePmcFriendship.CanRecruit(friendly,boss.realPlayer) && friendly.BotsGroup.IsEnemy(second) && boss.bossGroup.IsEnemy(friendly),"Recruitment cannot restore friendship revoked by actual aggression");
        Setup();GameplayModeRuntime.IsAllegiance=true;
        friendly=Solo("selected-friendly");AllegiancePmcFriendship.Apply(friendly);
        friendly.BotsGroup.Enemies[second]=new BotGroupEnemyInfo();boss.bossGroup.Enemies[friendly]=new BotGroupEnemyInfo();
        GameplayModeRuntime.IsAllegiance=false;
        AllegiancePmcFriendship.OnFollowerAdded(second,boss.realPlayer);
        Check(friendly.BotsGroup.IsEnemy(second) && boss.bossGroup.IsEnemy(friendly),"Guns for Hire does not apply Allegiance relationship repair");
        GameplayModeRuntime.IsAllegiance=true;
        var stranger=new Player {ProfileId="other-leader"};
        AllegiancePmcFriendship.OnFollowerAdded(second,stranger);
        Check(friendly.BotsGroup.IsEnemy(second),"Another leader cannot trigger local Allegiance repair");
        boss.realPlayer.Profile.Info.Side=EPlayerSide.Savage;
        AllegiancePmcFriendship.OnFollowerAdded(second,boss.realPlayer);
        Check(friendly.BotsGroup.IsEnemy(second),"Player Scav relationships remain outside Allegiance repair");
        AllegiancePmcFriendship.Reset();
    }
    private static void CrossFactionChecks() {
        Setup();GameplayModeRuntime.IsAllegiance=true;
        var aliasedBear=Solo("aliased-bear",EPlayerSide.Bear);
        first.PlayerObject=new Player {ProfileId=first.ProfileId,IsAI=true,Profile=first.Profile,AIData=first.AIData};
        aliasedBear.PlayerObject=new Player {ProfileId=aliasedBear.ProfileId,IsAI=true,Profile=aliasedBear.Profile,AIData=aliasedBear.AIData};
        // Production activation adds BotOwner; friendship receives GetPlayer instead.
        aliasedBear.BotsGroup.AddEnemy(first,EBotEnemyCause.initial);
        boss.bossGroup.Enemies[aliasedBear]=new BotGroupEnemyInfo();
        var nativeEnemies=new HashSet<string> {first.ProfileId};
        aliasedBear.BotsGroup.OnEnemyRemove+=person=>nativeEnemies.Remove(person.ProfileId);
        AllegiancePmcFriendship.Apply(aliasedBear);
        Check(!aliasedBear.BotsGroup.Enemies.ContainsKey(first) && !boss.bossGroup.Enemies.ContainsKey(aliasedBear),"Separate BotOwner/Player keys removed in both friendship directions");
        Check(nativeEnemies.Count==0,"Native enemy removal notification clears the selected follower cache");
        // SAIN can still hold a contact after its group dictionary entry is gone.
        nativeEnemies.Add(first.ProfileId);
        AllegiancePmcFriendship.Apply(aliasedBear);
        Check(nativeEnemies.Count==0,"Neutrality repair publishes removal even without a group enemy entry");
        Setup();GameplayModeRuntime.IsAllegiance=true;
        var bear=Solo("friendly-bear",EPlayerSide.Bear);
        bear.BotsGroup.Enemies[boss.realPlayer]=new BotGroupEnemyInfo();
        bear.BotsGroup.Enemies[first]=new BotGroupEnemyInfo();
        bear.Memory.GoalEnemy=new EnemyInfo {ProfileId=boss.realPlayer.ProfileId};
        boss.bossGroup.Enemies[bear]=new BotGroupEnemyInfo();
        first.Memory.GoalEnemy=new EnemyInfo {ProfileId=bear.ProfileId};
        var outsider=Solo("outsider");bear.BotsGroup.Enemies[outsider]=new BotGroupEnemyInfo();
        second.Memory.GoalEnemy=new EnemyInfo {ProfileId=outsider.ProfileId};
        AllegiancePmcFriendship.Apply(bear);
        Check(AllegiancePmcFriendship.CanRecruit(bear,boss.realPlayer),"USEC may recruit selected BEAR solo");
        Check(bear.Side==EPlayerSide.Bear && bear.Profile.Info.Settings.Role==WildSpawnType.pmcBEAR,"Selection preserves native BEAR faction and role");
        Check(!bear.BotsGroup.IsEnemy(boss.realPlayer) && !bear.BotsGroup.IsEnemy(first) && !boss.bossGroup.IsEnemy(bear),"Cross-faction setup hostility repaired in both directions");
        Check(bear.Memory.GoalEnemy==null && first.Memory.GoalEnemy==null && second.Memory.GoalEnemy.ProfileId==outsider.ProfileId && bear.BotsGroup.IsEnemy(outsider),"Only the selected squad relationship and memories are repaired");
        Check(!bear.BotsGroup.AddEnemy(boss.realPlayer,EBotEnemyCause.initial) && !bear.BotsGroup.AddEnemy(second,EBotEnemyCause.initial) && !boss.bossGroup.AddEnemy(bear,EBotEnemyCause.initial),"Faction policy replay cannot undo selected BEAR friendship");
        var own=Solo("friendly-usec");var third=Solo("third-bear",EPlayerSide.Bear);var capped=Solo("fourth-usec");
        AllegiancePmcFriendship.Apply(own);AllegiancePmcFriendship.Apply(third);AllegiancePmcFriendship.Apply(capped);
        Check(UnityEngine.Random.Rolls==3 && !AllegiancePmcFriendship.CanRecruit(capped,boss.realPlayer),"Mixed factions share one three-selection lifetime cap");
        FollowerGroupHostility.OnDamage(bear,first);AllegiancePmcFriendship.Apply(bear);
        Check(!AllegiancePmcFriendship.CanRecruit(bear,boss.realPlayer) && bear.BotsGroup.IsEnemy(second) && UnityEngine.Random.Rolls==3,"Cross-faction aggression revokes selection without rerolling");
        Setup();GameplayModeRuntime.IsAllegiance=true;
        var grouped=Solo("grouped-bear",EPlayerSide.Bear);grouped.SpawnProfileData.SpawnParams.ShallBeGroup.Group=true;grouped.SpawnProfileData.SpawnParams.ShallBeGroup.StartCount=2;
        AllegiancePmcFriendship.Apply(grouped);grouped.SpawnProfileData.SpawnParams.ShallBeGroup.Group=false;AllegiancePmcFriendship.Apply(grouped);
        Check(UnityEngine.Random.Rolls==0 && !AllegiancePmcFriendship.CanRecruit(grouped,boss.realPlayer),"Opposite-faction group survivor stays excluded");
        bear=Solo("standby-bear",EPlayerSide.Bear);FollowerGroupHostility.DeclareContact(first,bear);AllegiancePmcFriendship.Apply(bear);
        Check(UnityEngine.Random.Rolls==0 && !AllegiancePmcFriendship.CanRecruit(bear,boss.realPlayer) && bear.BotsGroup.IsEnemy(boss.realPlayer),"Cross-faction Contact before activation prevents selection");
        Setup();GameplayModeRuntime.IsAllegiance=true;boss.realPlayer.Profile.Info.Side=EPlayerSide.Bear;
        var usec=Solo("friendly-usec",EPlayerSide.Usec);AllegiancePmcFriendship.Apply(usec);
        Check(AllegiancePmcFriendship.CanRecruit(usec,boss.realPlayer) && usec.Side==EPlayerSide.Usec,"BEAR may recruit selected USEC without changing its faction");
        AllegiancePmcFriendship.Reset();
    }
    private static void FriendshipChanceChecks() {
        foreach(var playerSide in new[]{EPlayerSide.Usec,EPlayerSide.Bear})
        foreach(bool sameSide in new[]{true,false})
        foreach(float roll in new[]{0f,0.149f,0.15f,0.20f,0.299f,0.30f,1f}) {
            Setup();GameplayModeRuntime.IsAllegiance=true;boss.realPlayer.Profile.Info.Side=playerSide;
            var botSide=sameSide ? playerSide : playerSide==EPlayerSide.Usec ? EPlayerSide.Bear : EPlayerSide.Usec;
            var candidate=Solo("chance-candidate",botSide);UnityEngine.Random.NextValue=roll;
            AllegiancePmcFriendship.Apply(candidate);
            bool expected=roll<(sameSide ? 0.30f : 0.15f);
            Check(AllegiancePmcFriendship.CanRecruit(candidate,boss.realPlayer)==expected,"Faction chance boundary: player="+playerSide+" bot="+botSide+" roll="+roll);
            pitTeam.pitFireTeam.friendlyChanceMultiplier.Value=5;
            UnityEngine.Random.NextValue=expected ? 1f : 0f;AllegiancePmcFriendship.Apply(candidate);
            Check(UnityEngine.Random.Rolls==1 && AllegiancePmcFriendship.CanRecruit(candidate,boss.realPlayer)==expected,"Success and failure remain sticky without rerolling");
        }
        Setup();GameplayModeRuntime.IsAllegiance=true;UnityEngine.Random.NextValue=1f;
        AllegiancePmcFriendship.Apply(Solo("failed-own"));AllegiancePmcFriendship.Apply(Solo("failed-other",EPlayerSide.Bear));
        UnityEngine.Random.NextValue=0f;
        foreach(var id in new[]{"selected-one","selected-two","selected-three"}) {
            var candidate=Solo(id);AllegiancePmcFriendship.Apply(candidate);
            Check(AllegiancePmcFriendship.CanRecruit(candidate,boss.realPlayer),"Failed rolls do not consume a friendly selection slot");
        }
        var capped=Solo("chance-capped",EPlayerSide.Bear);AllegiancePmcFriendship.Apply(capped);
        Check(UnityEngine.Random.Rolls==5 && !AllegiancePmcFriendship.CanRecruit(capped,boss.realPlayer),"Faction-dependent chances retain shared three-selection cap");
        AllegiancePmcFriendship.Reset();
    }
    private static void FriendshipMultiplierChecks() {
        float[] sameChances={0.30f,0.475f,0.65f,0.825f,1f};
        float[] oppositeChances={0.15f,0.3625f,0.575f,0.7875f,1f};
        foreach(var playerSide in new[]{EPlayerSide.Usec,EPlayerSide.Bear})
        foreach(bool sameSide in new[]{true,false})
        foreach(int multiplier in new[]{1,2,3,4,5}) {
            float chance=(sameSide ? sameChances : oppositeChances)[multiplier-1];
            foreach(float roll in new[]{chance-0.0001f,chance+0.0001f,1f}) {
                Setup();GameplayModeRuntime.IsAllegiance=true;boss.realPlayer.Profile.Info.Side=playerSide;
                pitTeam.pitFireTeam.friendlyChanceMultiplier.Value=multiplier;
                var botSide=sameSide ? playerSide : playerSide==EPlayerSide.Usec ? EPlayerSide.Bear : EPlayerSide.Usec;
                var candidate=Solo("multiplier-candidate",botSide);UnityEngine.Random.NextValue=Math.Min(1f,roll);
                AllegiancePmcFriendship.Apply(candidate);
                Check(AllegiancePmcFriendship.CanRecruit(candidate,boss.realPlayer)==(multiplier==5 || roll<chance),"Multiplier chance: player="+playerSide+" same="+sameSide+" multiplier="+multiplier+" roll="+roll);
            }
        }
        Setup();pitTeam.pitFireTeam.friendlyChanceMultiplier.Value=5;AllegiancePmcFriendship.Apply(Solo("guns-for-hire"));
        Check(UnityEngine.Random.Rolls==0,"Multiplier cannot add friendship rolls in Guns for Hire");
        Setup();GameplayModeRuntime.IsAllegiance=true;pitTeam.pitFireTeam.friendlyChanceMultiplier.Value=5;UnityEngine.Random.NextValue=1f;
        foreach(var id in new[]{"guaranteed-one","guaranteed-two","guaranteed-three"}) {
            var candidate=Solo(id,EPlayerSide.Bear);AllegiancePmcFriendship.Apply(candidate);
            Check(AllegiancePmcFriendship.CanRecruit(candidate,boss.realPlayer),"Multiplier 5 guarantees eligible candidates even at endpoint roll=1");
        }
        var capped=Solo("multiplier-capped");AllegiancePmcFriendship.Apply(capped);
        Check(UnityEngine.Random.Rolls==3 && !AllegiancePmcFriendship.CanRecruit(capped,boss.realPlayer),"100 percent multiplier preserves three-selection cap");
        AllegiancePmcFriendship.Reset();
    }
    private static void BossDamageChecks() {
        foreach(bool sameSide in new[]{true,false})
        foreach(int multiplier in new[]{1,2,3,4,5}) {
            Setup();GameplayModeRuntime.IsAllegiance=true;FriendlyEncounterPenaltyRuntime.Points=5;
            pitTeam.pitFireTeam.friendlyChanceMultiplier.Value=multiplier;
            float baseChance=sameSide ? 0.30f : 0.15f;
            float scaledChance=baseChance+(1f-baseChance)*((multiplier-1)/4f)-0.05f*multiplier;
            UnityEngine.Random.NextValue=scaledChance+0.001f;
            var penalizedCandidate=Solo("penalized",sameSide ? EPlayerSide.Usec : EPlayerSide.Bear);
            AllegiancePmcFriendship.Apply(penalizedCandidate);
            Check(!AllegiancePmcFriendship.CanRecruit(penalizedCandidate,boss.realPlayer),"Penalty scales with chance multiplier before subtraction for both factions");
            var acceptedCandidate=Solo("below-penalty-threshold",sameSide ? EPlayerSide.Usec : EPlayerSide.Bear);
            UnityEngine.Random.NextValue=scaledChance-0.001f;
            AllegiancePmcFriendship.Apply(acceptedCandidate);
            Check(AllegiancePmcFriendship.CanRecruit(acceptedCandidate,boss.realPlayer),"Roll below the scaled penalty threshold still permits friendship");
            FriendlyEncounterPenaltyRuntime.Points=0;AllegiancePmcFriendship.Apply(penalizedCandidate);
            Check(UnityEngine.Random.Rolls==2 && !AllegiancePmcFriendship.CanRecruit(penalizedCandidate,boss.realPlayer),"Penalty expiry does not reroll an existing bot");
        }
        Setup();GameplayModeRuntime.IsAllegiance=true;FriendlyEncounterPenaltyRuntime.Points=100;
        pitTeam.pitFireTeam.friendlyChanceMultiplier.Value=5;UnityEngine.Random.NextValue=0f;
        var clamped=Solo("zero-chance");AllegiancePmcFriendship.Apply(clamped);
        Check(!AllegiancePmcFriendship.CanRecruit(clamped,boss.realPlayer),"Stacked penalties clamp to zero chance");
        foreach(bool emptySquad in new[]{false,true}) {
            Setup();GameplayModeRuntime.IsAllegiance=true;
            var bear=Solo("bear-attacker",EPlayerSide.Bear);AllegiancePmcFriendship.Apply(bear);
            if(emptySquad) {boss.Followers.Clear();boss.bossGroup=null;}
            var logic=new AIBossPlayerLogic {_aiplayer=boss};
            logic.OnHit(new EFT.Ballistics.DamageInfo {Player=bear,Damage=15f},EBodyPart.Chest,0);
            Check(!AllegiancePmcFriendship.CanRecruit(bear,boss.realPlayer) && bear.BotsGroup.IsEnemy(boss.realPlayer),"Actual human damage revokes cross-faction friendship, including empty squad");
            if(!emptySquad) Check(bear.BotsGroup.IsEnemy(first) && bear.BotsGroup.IsEnemy(second) && boss.bossGroup.IsEnemy(bear),"Human damage shares relationship with living squad in both directions");
            Check(bear.Memory.GoalEnemy==null && first.Memory.GoalEnemy==null && boss.Engagements==1,"Human damage retains engagement notification without assigning goals");
            AllegiancePmcFriendship.Apply(bear);
            Check(UnityEngine.Random.Rolls==1 && !AllegiancePmcFriendship.CanRecruit(bear,boss.realPlayer),"Repeated activation cannot reroll human-damage revocation");
        }
        Setup();GameplayModeRuntime.IsAllegiance=true;var candidate=Solo("zero-damage",EPlayerSide.Bear);AllegiancePmcFriendship.Apply(candidate);
        new AIBossPlayerLogic {_aiplayer=boss}.OnHit(new EFT.Ballistics.DamageInfo {Player=candidate,Damage=0},EBodyPart.Chest,0);
        Check(AllegiancePmcFriendship.CanRecruit(candidate,boss.realPlayer),"Zero-damage notification does not revoke selected friendship");
        new AIBossPlayerLogic {_aiplayer=boss}.OnHit(new EFT.Ballistics.DamageInfo {Player=first,Damage=15f},EBodyPart.Chest,0);
        Check(!boss.bossGroup.IsEnemy(boss.realPlayer) && !boss.bossGroup.IsEnemy(first),"Human hit by own follower preserves squad friendship");
        Setup();GameplayModeRuntime.IsAllegiance=true;candidate=Solo("pre-activation-hit",EPlayerSide.Bear);
        new AIBossPlayerLogic {_aiplayer=boss}.OnHit(new EFT.Ballistics.DamageInfo {Player=candidate,Damage=15f},EBodyPart.Chest,0);
        AllegiancePmcFriendship.Apply(candidate);
        Check(UnityEngine.Random.Rolls==0 && !AllegiancePmcFriendship.CanRecruit(candidate,boss.realPlayer),"Damage to human before activation prevents later friendship selection");
        Setup();GameplayModeRuntime.IsAllegiance=true;var protectedBot=Solo("protected");protectedBot.Profile.Info.Settings.Role=WildSpawnType.shooterBTR;
        FollowerGroupHostility.OnBossDamage(boss,protectedBot);
        Check(!protectedBot.BotsGroup.IsEnemy(boss.realPlayer),"Protected roles retain existing hostility exclusion");
        AllegiancePmcFriendship.Reset();
    }
    private static void GreetingLifecycleChecks() {
        Setup(); GameplayModeRuntime.IsAllegiance=true;
        var friendly=Solo("greeting"); var rejected=Solo("rejected");
        AllegiancePmcFriendship.Apply(friendly);
        UnityEngine.Random.NextValue=0.99f; AllegiancePmcFriendship.Apply(rejected);
        AllegianceFriendlyGreeting.Calls=0;
        BotOwnerUpdateHub.Invoke(rejected); BotOwnerUpdateHub.Invoke(rogue);
        Check(AllegianceFriendlyGreeting.Calls==0,"Only selected friendlies reach greeting");
        BotOwnerUpdateHub.Invoke(friendly);
        Check(AllegianceFriendlyGreeting.Calls==1,"Selected friendly reaches greeting through production hub");
        boss.Followers.Add(friendly); BotOwnerUpdateHub.Invoke(friendly);
        Check(AllegianceFriendlyGreeting.Calls==1,"Recruitment removes candidate from greeting dispatch");
        boss.Followers.Remove(friendly);
        AllegiancePmcFriendship.RevokeGroup(friendly.BotsGroup,EBotEnemyCause.byKill);
        BotOwnerUpdateHub.Invoke(friendly);
        Check(AllegianceFriendlyGreeting.Calls==1,"Revoked friendship cannot greet");
        AllegiancePmcFriendship.Reset();
        Check(!BotOwnerUpdateHub.HasSubscribers,"Raid teardown unregisters greeting callback");
        AllegiancePmcFriendship.Apply(friendly); BotOwnerUpdateHub.Invoke(friendly);
        Check(AllegianceFriendlyGreeting.Calls==1,"New raid rejection cannot reuse previous friendly state");
        AllegiancePmcFriendship.Reset();
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
        AllegianceChecks();
        FriendshipChanceChecks();
        FriendshipMultiplierChecks();
        CrossFactionChecks();
        RecruitedFriendshipChecks();
        BossDamageChecks();
        GreetingLifecycleChecks();
        h.UnpatchSelf();Console.WriteLine("Passed "+checks+" production hostility checks (including Allegiance selection).");
    }
}

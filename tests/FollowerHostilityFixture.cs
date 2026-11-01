using System;
using System.Collections.Generic;
using System.Linq;
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
    public struct Vector3 { public float x,y,z;private float? squared;public float sqrMagnitude {get=>squared ?? x*x+y*y+z*z;set=>squared=value;}
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3{x=a.x-b.x,y=a.y-b.y,z=a.z-b.z};
        public static float Angle(Vector3 a,Vector3 b)=>(float)(Math.Acos(Math.Max(-1,Math.Min(1,(a.x*b.x+a.y*b.y+a.z*b.z)/Math.Sqrt(a.sqrMagnitude*b.sqrMagnitude))))*180/Math.PI); }
    public struct Ray {public Vector3 origin,direction;}
    public static class Time { public static float time; }
    public static class Random { public static int Rolls; public static float NextValue; public static float value { get { Rolls++; return NextValue; } } }
}
namespace SPT.Reflection.Patching {
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
    public class PatchPrefix : Attribute { }
    public class PatchPostfix : Attribute { }
}
namespace EFT {
    public enum EPhraseTrigger {Roger,Negative,Cooperation,FollowMe}
    public enum EInteraction {OkGesture,NoGesture}
    public enum BodyPartType {body}
    public class BodyPart {public UnityEngine.Vector3 Position;}
    public enum EPlayerSide { Usec, Bear, Savage }
    public enum EBotState { Active, Inactive }
    public enum EBodyPart { Chest }
    public enum EDamageType { Bullet }
    public enum EBodyPartColliderType { Chest }
    public interface IPlayer {
        string ProfileId { get; } string GroupId { get; } EPlayerSide Side { get; } bool IsAI { get; } Profile Profile { get; }
        AIData AIData { get; } Health HealthController { get; } UnityEngine.Vector3 Position { get; }
    }
    public class Health {
        public bool IsAlive = true; public float Remaining = 100f;
        public event Action<EDamageType> DiedEvent;
        public int DeathListeners => DiedEvent?.GetInvocationList().Length ?? 0;
        public void RaiseDeath() => DiedEvent?.Invoke(EDamageType.Bullet);
    }
    public class Settings { public WildSpawnType Role; }
    public class ProfileInfo { public int Level=20; public EPlayerSide Side; public string GroupId,TeamId; public Settings Settings = new Settings(); }
    public class Profile { public ProfileInfo Info = new ProfileInfo(); public bool KnightFriend; public FenceData FenceInfo = new FenceData(); }
    public class FenceData { public double Standing=0.2; public int Changes;
        public void AddStanding(double delta,EFT.Counters.EFenceStandingSource source,bool discount) {
            if(discount || source!=EFT.Counters.EFenceStandingSource.ScavKill)throw new Exception("Expected exact Fence kill penalty");
            Standing+=delta;Changes++;
        }
    }
    public class AIData { public BotOwner BotOwner; }
    public class Player : IPlayer {
        public string ProfileId { get; set; } public bool IsAI { get; set; } public IPlayer iPlayer => this;
        public string GroupId => Profile.Info.GroupId ?? "group-" + ProfileId;
        public Profile Profile { get; set; } = new Profile(); public AIData AIData { get; set; } = new AIData();
        public Health HealthController { get; set; } = new Health(); public UnityEngine.Vector3 Position {get;set;}
        public EPlayerSide Side => Profile.Info.Side;
        public Action BeforeDeath;
        public Player InteractablePlayer;public UnityEngine.Ray InteractionRay;
        public Dictionary<BodyPartType,BodyPart> MainParts=new();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ApplyDamageInfo(EFT.Ballistics.DamageInfo damageInfo, EBodyPart bodyPartType, EBodyPartColliderType colliderType, float absorbed) {
            // Native order: health (including death) precedes BotMemory.GetHit,
            // which in turn precedes the global BotsController.OnBeingHit event.
            if (!HealthController.IsAlive) return;
            HealthController.Remaining -= damageInfo.Damage;
            if (HealthController.Remaining <= 0f) {
                HealthController.IsAlive=false; BeforeDeath?.Invoke();
                AllegianceScavRelations.RecordKill(this,damageInfo.Player?.iPlayer);
                HealthController.RaiseDeath();
            }
            if (AIData.BotOwner != null && damageInfo.Damage > 0f && damageInfo.Player != null)
                FollowerGroupHostility.OnDamage(AIData.BotOwner,damageInfo.Player.iPlayer);
        }
    }
    public class BotOwner : Player {
        public EBotState BotState=EBotState.Active;
        public BotOwner() { IsAI = true; AIData.BotOwner = this; }
        public Player PlayerObject; public Player GetPlayer => PlayerObject ?? this; public bool IsDead; public BotsGroup BotsGroup;
        public SpawnData SpawnProfileData = new SpawnData();
        public BotFollower BotFollower = new BotFollower(); public Memory Memory = new Memory();
        public EnemiesController EnemiesController = new EnemiesController();
    }
    public class BotFollower { public pitAIBossPlayer BossToFollow; }
    public class Memory { public EnemyInfo GoalEnemy; public bool HaveEnemy => GoalEnemy != null; public void DeleteInfoAboutEnemy(IPlayer player) { if (GoalEnemy?.ProfileId == player.ProfileId) GoalEnemy = null; } }
    public class GameWorld { public Player MainPlayer; public List<Player> AllAlivePlayersList = new List<Player>(); }
    public class SpawnData { public SpawnParams SpawnParams = new SpawnParams(); }
    public class SpawnParams { public GroupParams ShallBeGroup = new GroupParams(); }
    public class GroupParams { public bool Group; public int StartCount = 1; }
    public class EnemiesController { public Dictionary<IPlayer, EnemyInfo> EnemyInfos = new Dictionary<IPlayer, EnemyInfo>(); }
    public class EnemyInfo {
        public string ProfileId; public bool IsVisible, CanShoot, HaveSeenPersonal; public float PersonalLastSeenTime;
    }
}
namespace EFT.Ballistics { public struct DamageInfo { public EFT.Player Player; public float Damage; } }
namespace EFT.Counters { public enum EFenceStandingSource { ScavKill } }
namespace EFT.UI { public class GUISounds { public int Negative; public void PlayKarmaSound(bool positive) { if(positive)throw new Exception("Scavs must not play positive karma sound");Negative++; } } }
public class BotGroupEnemyInfo { }
public class BotsGroup {
    public RequestController RequestsController=new();
    private static int nextId;
    public int Id = ++nextId; public EPlayerSide Side = EPlayerSide.Savage; public WildSpawnType InitialBotType = WildSpawnType.exUsec;
    public List<BotOwner> Members = new List<BotOwner>(); public int MembersCount => Members.Count;
    public int TargetMembersCount; public event Action<BotOwner> OnMemberAdd;
    public bool Locked { get; set; } public void Lock() { Locked = true; }
    public HashSet<string> _enemyPlayerGroups = new HashSet<string>();
    public void AddMember(BotOwner member) { Members.Add(member); OnMemberAdd?.Invoke(member); }
    public event Action<IPlayer> OnEnemyRemove;
    public void RemoveEnemy(IPlayer player) { Enemies.Remove(player); OnEnemyRemove?.Invoke(player); Neutrals.Remove(player); foreach (var m in Members) m.Memory.DeleteInfoAboutEnemy(player); }
    public void AddNeutral(IPlayer player) { Neutrals[player] = new BotGroupEnemyInfo(); }
    public void AddAlly(Player player) { if (!Allies.Contains(player)) Allies.Add(player); }
    public BotOwner Member(int i) => Members[i];
    public Dictionary<IPlayer,BotGroupEnemyInfo> Enemies = new Dictionary<IPlayer,BotGroupEnemyInfo>();
    public Dictionary<IPlayer,BotGroupEnemyInfo> Neutrals = new Dictionary<IPlayer,BotGroupEnemyInfo>();
    public List<IPlayer> Allies = new List<IPlayer>();
    public bool PolicyEnemy, Reject, Throw; public int Calls, MemoryWrites;
    public bool IsEnemy(IPlayer p) => Enemies.ContainsKey(p);
    public bool IsPlayerEnemy(IPlayer p) => PolicyEnemy;
    public bool IsAlly(IPlayer p) => Allies.Contains(p);
    public bool Contains(BotOwner bot) => Members.Contains(bot);
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool AddEnemy(IPlayer person, EBotEnemyCause cause) {
        Calls++;
        if (Throw) throw new InvalidOperationException("native failure");
        if (Reject || MembersCount == 0) return false;
        if (!Enemies.ContainsKey(person)) { Enemies.Add(person,new BotGroupEnemyInfo()); Neutrals.Remove(person); Allies.Remove(person); }
        MemoryWrites += MembersCount; return true;
    }
}
public class RequestController {public int Calls;public bool TryAskFollowMeRequest(Player player,BotOwner bot){Calls++;return true;}}
namespace EFT.Interactive { }
namespace pitTeam.Components {
    public class pitAIBossPlayer { public Player realPlayer; public BotsGroup bossGroup; public List<BotOwner> Followers = new List<BotOwner>(); public int Engagements; public void MarkPlayerEngagement(Player enemy,string source) {Engagements++;}
        public bool CanContact(IPlayer requester,Player candidate)=>IsEligibleBossDirectedContactTarget(requester,candidate);
        __CONTACT_ELIGIBILITY_METHOD__
    }
    public class AIBossPlayerLogic {
        public pitAIBossPlayer _aiplayer; public float _lastTimeHit;
        __BOSS_HIT_METHOD__
    }
    public class BotFollowerPlayer { public pitAIBossPlayer Boss; public BotOwner Bot; public BotOwner GetBot() => Bot; public pitAIBossPlayer GetBoss() => Boss;
        private BotOwner _bot => Bot; private pitAIBossPlayer _player => Boss;
        private string _grouId,_teamId;private bool _profileIdentityAssigned;
        __PROFILE_IDENTITY_METHODS__
        public void AssignIdentity() => AssignProfileIdentity();
        public void Dismiss(bool warn) { if(warn) throw new Exception("Scav departure must remain peaceful"); RestoreProfileIdentity();Bot.BotFollower.BossToFollow=null; }
    }
}
namespace pitTeam.Patches { public class BotsGroupPlayer : BotsGroup { public pitAIBossPlayer Boss; } }
namespace pitTeam.Utils {
    public static class Utils {
        public static bool Friendly, Bad;
        public static bool FlagGet(string s) => s == "pitFireTeam" ? Friendly : s == "isBadGuy" && Bad;
        public static bool PlayerHasKnightQuest(Profile p) => p.KnightFriend;
        public static readonly List<Action> Timers = new List<Action>();
        public static void SetTimeout(Action action, int ms) => Timers.Add(action);
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
    // Hostility/selection tests control this external admission boundary. The production
    // population scanner and capacity policy have their own native-world fixture.
    public static class FriendlyEncounterPopulation {
        public static bool Allowed=true,UseReal;public static int Queries;
        public static bool CanAdmit(BotOwner bot,out int alive,out int friendly,out int limit){Queries++;if(UseReal)return FriendlyEncounterPopulationCounter.CanAdmit(bot,out alive,out friendly,out limit);alive=10;friendly=0;limit=3;return Allowed;}
    }
    internal static class PmcKarmaRuntime { internal static void NoteFriendly(BotOwner bot) { } }
    internal sealed class AllegianceCombatGreeting { internal void Cancel() {} internal void Update(BotOwner bot,Player human) {} }
    internal sealed class AllegianceFriendlyGreeting {
        internal static int Calls, Cancellations; private bool cancelled;
        internal void Cancel() { cancelled=true; Cancellations++; }
        internal void Update(BotOwner bot, Player human) { if(!cancelled) Calls++; }
        internal static bool HasSight(BotOwner bot,Player human,float range)=>true;
    }
    internal sealed class FriendlyEncounterInteraction {
        internal const float Range=12f;internal bool Waiting,Cancelled,Refusing;internal static BotOwner InvitingBot;
        internal static int CancelCalls;internal void Cancel(BotOwner bot){Waiting=false;Cancelled=true;CancelCalls++;if(InvitingBot==bot)InvitingBot=null;}
        internal void BeginRefusal(BotOwner bot){if(Waiting)Refusing=true;else Cancel(bot);}
        internal static void ResetRaid()=>InvitingBot=null;
        internal bool CanWait(BotOwner bot,Player human)=>true;
        internal void End(BotOwner bot){Waiting=false;if(InvitingBot==bot)InvitingBot=null;}
        internal bool Respond(BotOwner bot,Player human,bool accept){End(bot);if(accept)bot.BotsGroup.RequestsController.TryAskFollowMeRequest(human,bot);return true;}
    }
    public static class FriendlyEncounterPenaltyRuntime { public static int Points; public static int GetReductionPercent() => Points; }
    public static class Logger { public static int Errors; public static void LogError(object s) { Errors++; } public static void LogInfo(string s) { } }
    public static class BattleRecorder { public static void RecordFriendlyEncounter(BotOwner bot,string stage,Func<object> details) { } }
    public static class GameplayModeRuntime { public static bool IsAllegiance;public static bool GetEffectiveValue(pitTeam.pitFireTeam.BoolSetting entry)=>entry.Value; }
    public static class GameplayModeAddons { public static bool IsRogueRaid; }
    public static class BossPlayers {
        public static HashSet<string> Denied=new();
        public static bool HasDeniedRecruitment(string id)=>Denied.Contains(id);
        public static void RememberRecruitmentDenial(string id)=>Denied.Add(id);
        public static pitAIBossPlayer Boss;
        public static pitAIBossPlayer GetBoss(string id) => Boss.realPlayer.ProfileId == id ? Boss : null;
        public static readonly Dictionary<string,BotFollowerPlayer> Registered = new Dictionary<string,BotFollowerPlayer>();
        public static BotFollowerPlayer GetFollowerByProfileId(string id) => Registered.TryGetValue(id,out var follower) ? follower : IsFollowerProfileId(id) ? new BotFollowerPlayer{Boss=Boss,Bot=Boss.Followers.Find(f=>f.ProfileId==id)} : null;
        public static void RemoveFollower(BotOwner bot,pitAIBossPlayer boss) { AllegianceScavRelations.OnRemoved(bot); boss.Followers.Remove(bot); Registered.Remove(bot.ProfileId); }
        public static bool IsFollowerProfileId(string id) => Boss.Followers.Exists(f => f.ProfileId == id);
        public static bool IsFollower(BotOwner b,pitAIBossPlayer boss=null) => IsFollowerProfileId(b.ProfileId);
        public static List<BotFollowerPlayer> GetFollowersByBoss(string id) => Boss.Followers.ConvertAll(b => new BotFollowerPlayer { Boss=Boss, Bot=b });
        public static bool IsPlayerBoss(string id) => GetBoss(id) != null;
        public static pitAIBossPlayer GetBossByGroup(int id) => Boss.bossGroup.Id == id ? Boss : null;
    }
    public static class FactionHostility {
        public static bool IsScavFaction(Player p) => p.Side==EPlayerSide.Savage &&
            (p.Profile.Info.Settings.Role == WildSpawnType.assault || p.Profile.Info.Settings.Role == WildSpawnType.marksman);
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
        public sealed class BoolSetting {public bool Value=true;}
        public static BoolSetting tieredPickup=new();
        public static BoolSetting friendlyScavEncounters=new();
        public sealed class IntSetting { public int Value=30; }
        public static IntSetting friendlyEncounterChance=new IntSetting();
    }
}
namespace pitTeam.Utils { public static class FollowerAwareness { public static void RegisterBossRangedThreatWatch(BotOwner follower,BotOwner enemy) {} } }
public static class HostilityChecks {
    private static int checks;
    private static pitAIBossPlayer boss; private static BotsGroup outside; private static BotOwner rogue, first, second;
    private static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    private static void Setup() {
        FriendlyEncounterPopulation.Allowed=true;FriendlyEncounterPopulation.UseReal=false;FriendlyEncounterPopulation.Queries=0;
        GameplayModeAddons.IsRogueRaid=false;
        BossPlayers.Denied.Clear();pitTeam.pitFireTeam.tieredPickup.Value=true;
        AllegianceScavRelations.Reset(); BossPlayers.Registered.Clear(); pitTeam.Utils.Utils.Timers.Clear();
        AllegiancePmcFriendship.Reset(); GameplayModeRuntime.IsAllegiance=false; UnityEngine.Random.Rolls=0; UnityEngine.Random.NextValue=0f;
        pitTeam.pitFireTeam.friendlyEncounterChance.Value=30;
        pitTeam.pitFireTeam.friendlyScavEncounters.Value=true;
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
    private static BotOwner Scav(string id) {
        var bot=Solo(id,EPlayerSide.Savage);bot.Profile.Info.Settings.Role=WildSpawnType.assault;bot.BotsGroup.InitialBotType=WildSpawnType.assault;return bot;
    }
    private static void EncounterChanceChecks() {
        foreach(var sample in new[]{(5,0.05f),(10,0.10f),(15,0.1125f),(20,0.125f),(25,0.1375f),(30,0.15f),(40,0.20f),(60,0.30f),(80,0.40f)}) {
            Setup();pitTeam.pitFireTeam.friendlyEncounterChance.Value=sample.Item1;
            Check(Math.Abs(AllegiancePmcFriendship.GetSameSideChance()-sample.Item1/100f)<0.000001,"Configured percentage drives same-side chance");
            Check(Math.Abs(AllegiancePmcFriendship.GetOppositeSideChance(sample.Item1/100f)-sample.Item2)<0.000001,"Opposite-side curve converges at 10% and halves at 30% or above");
        }
        foreach(int setting in new[]{-10,0,5,80,90,100}) {
            Setup();pitTeam.pitFireTeam.friendlyEncounterChance.Value=setting;
            Check(Math.Abs(AllegiancePmcFriendship.GetSameSideChance()-Math.Max(5,Math.Min(80,setting))/100f)<0.000001,"Invalid config percentage clamps to 5 through 80");
        }
        foreach(bool allegiance in new[]{false,true}) foreach(int setting in new[]{5,10,20,30,80}) {
            Setup();GameplayModeRuntime.IsAllegiance=allegiance;pitTeam.pitFireTeam.friendlyEncounterChance.Value=setting;
            for(int i=0;i<30;i++) {
                var pmc=Solo("uncapped-pmc-"+i,allegiance && i%2==0 ? EPlayerSide.Bear : EPlayerSide.Usec);AllegiancePmcFriendship.Apply(pmc);
                Check(AllegiancePmcFriendship.IsSelectedFriendly(pmc,boss.realPlayer),"PMC selections have no raid cap");
                if(allegiance){var scav=Scav("uncapped-scav-"+i);AllegiancePmcFriendship.Apply(scav);Check(AllegiancePmcFriendship.IsSelectedFriendly(scav,boss.realPlayer),"Scav selections have no raid cap");}
            }
            int rolls=UnityEngine.Random.Rolls;var existing=Solo("uncapped-pmc-0");AllegiancePmcFriendship.Apply(existing);
            Check(UnityEngine.Random.Rolls==rolls,"Removing caps does not allow a profile to reroll");
        }
        foreach(bool scav in new[]{false,true}) {
            Setup();GameplayModeRuntime.IsAllegiance=true;UnityEngine.Random.NextValue=1;
            var failed=scav ? Scav("sticky-failure") : Solo("sticky-failure");AllegiancePmcFriendship.Apply(failed);
            pitTeam.pitFireTeam.friendlyEncounterChance.Value=80;boss.realPlayer.Profile.FenceInfo.Standing=6;UnityEngine.Random.NextValue=0;AllegiancePmcFriendship.Apply(failed);
            Check(!AllegiancePmcFriendship.IsSelectedFriendly(failed,boss.realPlayer) && UnityEngine.Random.Rolls==1,"Chance and Fence changes never reroll failure");
            AllegiancePmcFriendship.Reset();AllegiancePmcFriendship.Apply(failed);
            Check(AllegiancePmcFriendship.IsSelectedFriendly(failed,boss.realPlayer),"New raid clears prior decisions");
        }
    }
    private static void FriendlyContactExclusionChecks() {
        Setup();GameplayModeRuntime.IsAllegiance=true;pitTeam.pitFireTeam.friendlyEncounterChance.Value=80;boss.realPlayer.Profile.FenceInfo.Standing=6;
        var friendlyScav=Scav("contact-friendly-scav");var same=Solo("contact-friendly-pmc");var other=Solo("contact-friendly-opposite",EPlayerSide.Bear);
        foreach(var friendly in new[]{friendlyScav,same,other}) {
            AllegiancePmcFriendship.Apply(friendly);
            Check(!boss.CanContact(boss.realPlayer,friendly),"Directed Contact excludes selected friendly Scavs and PMCs before hostile admission: "+friendly.ProfileId);
            Check(AllegiancePmcFriendship.IsSelectedFriendly(friendly,boss.realPlayer),"Contact eligibility probing preserves selected friendship");
        }
        var hostile=Solo("contact-real-enemy",EPlayerSide.Bear);UnityEngine.Random.NextValue=1;AllegiancePmcFriendship.Apply(hostile);
        Check(boss.CanContact(boss.realPlayer,hostile),"The actual hostile target remains eligible beside friendly candidates");
        RecruitScavCompanion();var calmed=Scav("contact-calmed-scav");UnityEngine.Random.NextValue=0;AllegianceScavRelations.Apply(calmed);
        Check(!boss.CanContact(boss.realPlayer,calmed),"Contact excludes Scavs calmed by the recruited companion");
        friendlyScav.BotsGroup.AddEnemy(boss.realPlayer,EBotEnemyCause.followGetHit);
        Check(boss.CanContact(boss.realPlayer,friendlyScav),"Real aggression can revoke friendship and restore Contact eligibility");
    }
    private static void ScavEncounterToggleChecks() {
        foreach(bool allegiance in new[]{false,true}) foreach(bool enabled in new[]{false,true}) {
            Setup();GameplayModeRuntime.IsAllegiance=allegiance;pitTeam.pitFireTeam.friendlyScavEncounters.Value=enabled;
            boss.realPlayer.Profile.FenceInfo.Standing=6;pitTeam.pitFireTeam.friendlyEncounterChance.Value=80;
            var scav=Scav("toggle-scav");AllegiancePmcFriendship.Apply(scav);
            bool selected=allegiance&&enabled;
            Check(AllegiancePmcFriendship.IsSelectedFriendly(scav,boss.realPlayer)==selected,"Scav encounter toggle is default-on policy scoped to Allegiance: "+allegiance+"/"+enabled);
            Check(UnityEngine.Random.Rolls==(selected?1:0),"Disabled Scav encounters do not consume an encounter roll: "+allegiance+"/"+enabled);
            if(allegiance&&!enabled) {
                Check(!AllegiancePmcFriendship.CanRecruit(scav,boss.realPlayer)&&!AllegiancePmcFriendship.TryGetInteraction(scav,out _,out _),"Disabled Scav encounters cannot recruit or own friendly interaction");
                Check(!AllegiancePmcFriendship.ShouldBlockEnemyAddition(scav.BotsGroup,boss.realPlayer,EBotEnemyCause.initial),"Disabled Scav encounters do not block native hostility");
            }
            var same=Solo("toggle-same");var opposite=Solo("toggle-opposite",EPlayerSide.Bear);
            AllegiancePmcFriendship.Apply(same);AllegiancePmcFriendship.Apply(opposite);
            Check(AllegiancePmcFriendship.IsSelectedFriendly(same,boss.realPlayer),"Scav toggle preserves same-side PMC selection: "+allegiance+"/"+enabled);
            Check(AllegiancePmcFriendship.IsSelectedFriendly(opposite,boss.realPlayer)==allegiance,"Scav toggle preserves opposite-side PMC mode policy: "+allegiance+"/"+enabled);
        }
        Setup();GameplayModeRuntime.IsAllegiance=true;boss.realPlayer.Profile.Info.Side=EPlayerSide.Savage;pitTeam.pitFireTeam.friendlyScavEncounters.Value=false;
        Check(AllegiancePmcFriendship.CanRecruit(Scav("native-scav-recruit"),boss.realPlayer),"Disabled friendly encounters preserve native player-Scav recruitment");
    }
    private static void ScavAcceptanceChecks() {
        foreach(var sample in new[]{(0f,1f),(0.15f,1f),(0.325f,0.75f),(0.5f,0.5f),(1f,0.5f)})
            Check(Math.Abs(ScavFenceKarma.RecruitmentAcceptanceChance(sample.Item1)-sample.Item2)<0.000001f,"Scav acceptance curve clamps at 50% for slider values at or above 50%");
        Setup();GameplayModeRuntime.IsAllegiance=true;boss.realPlayer.Profile.FenceInfo.Standing=1;pitTeam.pitFireTeam.friendlyEncounterChance.Value=15;
        var baseline=Scav("baseline-acceptance");AllegiancePmcFriendship.Apply(baseline);int rolls=UnityEngine.Random.Rolls;
        boss.realPlayer.Profile.FenceInfo.Standing=6;pitTeam.pitFireTeam.friendlyEncounterChance.Value=80;UnityEngine.Random.NextValue=1f;
        Check(AllegiancePmcFriendship.AcceptScavRecruitment(baseline,boss.realPlayer) && UnityEngine.Random.Rolls==rolls,"Original 15% slider guarantees acceptance despite later settings changes");
        foreach(int chancePercent in new[]{50,80}) foreach(float roll in new[]{0.499f,0.5f,0.999f}) {
            Setup();GameplayModeRuntime.IsAllegiance=true;boss.realPlayer.Profile.FenceInfo.Standing=6;pitTeam.pitFireTeam.friendlyEncounterChance.Value=chancePercent;
            var candidate=Scav("cached-acceptance");AllegiancePmcFriendship.Apply(candidate);rolls=UnityEngine.Random.Rolls;
            UnityEngine.Random.NextValue=roll;bool accepted=AllegiancePmcFriendship.AcceptScavRecruitment(candidate,boss.realPlayer);
            Check(accepted==(roll<0.5f) && UnityEngine.Random.Rolls==rolls+1,"Slider values of 50% and 80% both have 50% acceptance");
            UnityEngine.Random.NextValue=accepted ? 1f : 0f;boss.realPlayer.Profile.FenceInfo.Standing=0;pitTeam.pitFireTeam.friendlyEncounterChance.Value=30;
            Check(AllegiancePmcFriendship.AcceptScavRecruitment(candidate,boss.realPlayer)==accepted && UnityEngine.Random.Rolls==rolls+1,"Acceptance or refusal stays cached without rerolling");
            FollowerGroupHostility.DeclareContact(first,candidate);
            Check(!AllegiancePmcFriendship.AcceptScavRecruitment(candidate,boss.realPlayer) && UnityEngine.Random.Rolls==rolls+1,"Revocation overrides cached acceptance");
        }
        Setup();GameplayModeRuntime.IsAllegiance=true;var grouped=Scav("ineligible");grouped.BotsGroup.TargetMembersCount=2;AllegiancePmcFriendship.Apply(grouped);
        Check(!AllegiancePmcFriendship.AcceptScavRecruitment(grouped,boss.realPlayer) && UnityEngine.Random.Rolls==0,"Grouped Scavs cannot roll acceptance");
        var bossScav=Scav("boss");bossScav.Profile.Info.Settings.Role=WildSpawnType.bossKilla;AllegiancePmcFriendship.Apply(bossScav);
        Check(!AllegiancePmcFriendship.AcceptScavRecruitment(bossScav,boss.realPlayer) && UnityEngine.Random.Rolls==0,"Excluded bosses cannot roll acceptance");
        AllegiancePmcFriendship.Reset();boss.realPlayer.Profile.FenceInfo.Standing=6;var reset=Scav("cached-acceptance");UnityEngine.Random.NextValue=0;AllegiancePmcFriendship.Apply(reset);
        Check(AllegiancePmcFriendship.AcceptScavRecruitment(reset,boss.realPlayer),"Raid reset clears previous refusal");
    }
    private static void ScavAcceptanceSettingOnlyChecks() {
        foreach(var sample in new[]{(5,1f),(15,1f),(30,11f/14f),(40,9f/14f),(50,0.5f),(80,0.5f)})
        foreach(double standing in new[]{0.2d,1d,2d,6d}) foreach(int penalty in new[]{0,15,45})
        foreach(bool below in new[]{false,true}) {
            Setup();GameplayModeRuntime.IsAllegiance=true;pitTeam.pitFireTeam.friendlyEncounterChance.Value=sample.Item1;
            boss.realPlayer.Profile.FenceInfo.Standing=standing;FriendlyEncounterPenaltyRuntime.Points=penalty;
            var candidate=Scav("setting-only-acceptance");AllegiancePmcFriendship.Apply(candidate);
            Check(AllegiancePmcFriendship.IsSelectedFriendly(candidate,boss.realPlayer),"Positive encounter chance can select the Scav at every tested Fence/penalty setting");
            int rolls=UnityEngine.Random.Rolls;
            // Freeze the slider at selection, not at the later player request.
            pitTeam.pitFireTeam.friendlyEncounterChance.Value=sample.Item1==80?5:80;
            boss.realPlayer.Profile.FenceInfo.Standing=0;FriendlyEncounterPenaltyRuntime.Points=100;
            UnityEngine.Random.NextValue=sample.Item2+(below?-0.001f:0.001f);
            bool accepted=AllegiancePmcFriendship.AcceptScavRecruitment(candidate,boss.realPlayer);
            Check(accepted==(sample.Item2>=1f||below),"Only the original raw encounter slider determines Scav acceptance; Fence and penalties cannot change it: "+sample.Item1+"/"+standing+"/"+penalty);
            int expectedRolls=rolls+(sample.Item2>=1f?0:1);
            UnityEngine.Random.NextValue=accepted?1f:0f;
            Check(AllegiancePmcFriendship.AcceptScavRecruitment(candidate,boss.realPlayer)==accepted&&UnityEngine.Random.Rolls==expectedRolls,"Setting-only acceptance still caches YES and NO without rerolls");
        }
        foreach(int setting in new[]{30,50}) foreach(float roll in new[]{0.7f,0.9f}) {
            Setup();GameplayModeRuntime.IsAllegiance=true;pitTeam.pitFireTeam.friendlyEncounterChance.Value=setting;
            boss.realPlayer.Profile.FenceInfo.Standing=6;var candidate=Scav("setting-only-proactive");AllegiancePmcFriendship.Apply(candidate);
            int rolls=UnityEngine.Random.Rolls;UnityEngine.Random.NextValue=roll;
            bool resolved=AllegiancePmcFriendship.TryResolveRecruitmentAcceptance(candidate,boss.realPlayer,true,out bool accepted);
            Check(resolved==(setting==30)&&UnityEngine.Random.Rolls==rolls+(setting==30?1:0),"Proactive threshold uses slider-only acceptance, independently of Fence");
            Check(!BossPlayers.HasDeniedRecruitment(candidate.ProfileId),"A proactive roll never becomes an actual spoken refusal");
            if(resolved) {
                UnityEngine.Random.NextValue=accepted?1f:0f;
                Check(AllegiancePmcFriendship.AcceptScavRecruitment(candidate,boss.realPlayer)==accepted&&UnityEngine.Random.Rolls==rolls+1,"Requested recruitment reuses the proactive setting-only answer");
            }
        }
    }
    private static void ScavEncounterChecks() {
        // Use the installed game's entire role enum, including solo bosses, guards and marksmen.
        foreach(WildSpawnType role in Enum.GetValues(typeof(WildSpawnType))) {
            Setup();GameplayModeRuntime.IsAllegiance=true;
            pitTeam.pitFireTeam.friendlyEncounterChance.Value=80;
            var candidate=Scav("role-"+role);candidate.Profile.Info.Settings.Role=role;candidate.BotsGroup.InitialBotType=role;
            Check(AllegiancePmcFriendship.IsRegularScav(candidate)==(role==WildSpawnType.assault),"Only Savage assault counts as a regular Scav: "+role);
            if(role==WildSpawnType.assault || role==WildSpawnType.pmcUSEC || role==WildSpawnType.pmcBEAR) continue;
            candidate.BotsGroup.Enemies[boss.realPlayer]=new BotGroupEnemyInfo();
            AllegiancePmcFriendship.Apply(candidate);
            Check(UnityEngine.Random.Rolls==0 && !AllegiancePmcFriendship.IsSelectedFriendly(candidate,boss.realPlayer),"Excluded role never rolls even at maximum multiplier: "+role);
            Check(candidate.BotsGroup.IsEnemy(boss.realPlayer),"Excluded role retains native hostility: "+role);
            foreach(var ordinary in new[]{Scav("eligible-1"),Scav("eligible-2"),Scav("eligible-3")}) AllegiancePmcFriendship.Apply(ordinary);
            Check(UnityEngine.Random.Rolls==3,"Excluded roles do not prevent later ordinary Scav rolls: "+role);
        }
        foreach(var side in new[]{EPlayerSide.Usec,EPlayerSide.Bear}) {
            Setup();GameplayModeRuntime.IsAllegiance=true;var candidate=Scav("wrong-side");candidate.Profile.Info.Side=side;
            AllegiancePmcFriendship.Apply(candidate);
            Check(!AllegiancePmcFriendship.IsRegularScav(candidate) && UnityEngine.Random.Rolls==0,"Assault also requires Savage side: "+side);
        }
        foreach(var side in new[]{EPlayerSide.Usec,EPlayerSide.Bear})
        foreach(int chancePercent in new[]{5,10,15,20,25,30,40,60,80})
        foreach(double reputation in new[]{-7d,-1d,0d,0.2d,0.5d,1d,2d,3d,4d,5d,6d,15d})
        foreach(int penalty in new[]{0,15,30,100})
        foreach(float offset in new[]{-0.0001f,0.0001f}) {
            Setup();GameplayModeRuntime.IsAllegiance=true;boss.realPlayer.Profile.Info.Side=side;
            pitTeam.pitFireTeam.friendlyEncounterChance.Value=chancePercent;
            boss.realPlayer.Profile.FenceInfo.Standing=reputation;FriendlyEncounterPenaltyRuntime.Points=penalty;
            float opposite=chancePercent<=10 ? chancePercent/100f : chancePercent<30 ? (10f+(chancePercent-10)/4f)/100f : chancePercent/200f;
            double rep=Math.Max(0d,Math.Min(6d,reputation));
            double multiplier=rep<=1d?rep:1d+(rep-1d)/5d;
            float chance=(float)Math.Min(1d,opposite*multiplier)*(1f-penalty/100f);
            UnityEngine.Random.NextValue=Math.Min(1f,Math.Max(0f,chance+offset));var bot=Scav("scav");AllegiancePmcFriendship.Apply(bot);
            Check(AllegiancePmcFriendship.IsSelectedFriendly(bot,boss.realPlayer)==(chance>=1f||(chance>0&&offset<0)),"Scav chance follows side curve times Fence standing, then relative penalties");
            Check(bot.Side==EPlayerSide.Savage && bot.Profile.Info.Settings.Role==WildSpawnType.assault,"Scav native identity retained");
        }
        foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
            Check(ScavFenceKarma.EncounterChance(0.15f,invalid)==0,"Invalid Fence standing falls back to zero reputation");
        Check(Math.Abs(ScavFenceKarma.EncounterChance(0.15f,0.2d)-0.03f)<0.000001,"Default 15% Scav base times 0.2 reputation gives 3%");
        Check(Math.Abs(ScavFenceKarma.EncounterChance(0.15f,6d)-0.30f)<0.000001,"Fence reputation 6 caps the multiplier at 2x");
        Check(Math.Abs(ScavFenceKarma.EncounterChance(0.1125f,4.89d)-0.200025f)<0.000001,"Current 4.89 reputation and 15% setting produce about 20% chance");
        Setup();GameplayModeRuntime.IsAllegiance=true;boss.realPlayer.Profile.FenceInfo.Standing=0;
        UnityEngine.Random.NextValue=0.2f;var fixedRoll=Scav("standing-change");AllegiancePmcFriendship.Apply(fixedRoll);
        boss.realPlayer.Profile.FenceInfo.Standing=6;AllegiancePmcFriendship.Apply(fixedRoll);
        Check(UnityEngine.Random.Rolls==1&&!AllegiancePmcFriendship.IsSelectedFriendly(fixedRoll,boss.realPlayer),"Fence changes never reroll an existing Scav");
        Setup();var hired=Scav("hired-mode");AllegiancePmcFriendship.Apply(hired);
        Check(UnityEngine.Random.Rolls==0 && !AllegiancePmcFriendship.IsSelectedFriendly(hired,boss.realPlayer),"Guns for Hire does not roll Scavs");
        Setup();GameplayModeRuntime.IsAllegiance=true;var group=Scav("grouped-scav");group.BotsGroup.TargetMembersCount=2;AllegiancePmcFriendship.Apply(group);group.BotsGroup.TargetMembersCount=0;AllegiancePmcFriendship.Apply(group);
        Check(UnityEngine.Random.Rolls==0 && !AllegiancePmcFriendship.CanRecruit(group,boss.realPlayer),"Grouped Scav and surviving group member never roll");
        Setup();GameplayModeRuntime.IsAllegiance=true;pitTeam.pitFireTeam.friendlyEncounterChance.Value=80;
        foreach(var bot in new[]{Solo("usec"),Scav("scav"),Solo("bear",EPlayerSide.Bear),Scav("second-scav")}) {AllegiancePmcFriendship.Apply(bot);Check(AllegiancePmcFriendship.CanRecruit(bot,boss.realPlayer),"PMCs and Scavs have independent selections");}
        var capped=Scav("capped");AllegiancePmcFriendship.Apply(capped);Check(UnityEngine.Random.Rolls==5 && AllegiancePmcFriendship.CanRecruit(capped,boss.realPlayer),"PMC and Scav selections have no cap");
        Setup();GameplayModeRuntime.IsAllegiance=true;var neutral=Scav("neutral-scav");AllegiancePmcFriendship.Apply(neutral);
        Check(!neutral.BotsGroup.IsEnemy(boss.realPlayer) && !boss.bossGroup.IsEnemy(neutral),"Selected Scav is neutral to player and followers");
        FollowerGroupHostility.OnDamage(neutral,boss.realPlayer);
        Check(!AllegiancePmcFriendship.CanRecruit(neutral,boss.realPlayer),"Real aggression revokes Scav friendship");
        AllegiancePmcFriendship.Reset();
    }
    private static void AllegianceChecks() {
        Setup();var bot=Solo("solo");AllegiancePmcFriendship.Apply(bot);
        Check(UnityEngine.Random.Rolls==1 && AllegiancePmcFriendship.IsSelectedFriendly(bot,boss.realPlayer) && AllegiancePmcFriendship.CanRecruit(bot,boss.realPlayer),"Guns for Hire rolls same-faction encounters without changing recruitment admission");
        Setup();bot=Solo("solo");
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
        var next=Solo("next");var third=Solo("third");var capped=Solo("capped");AllegiancePmcFriendship.Apply(next);AllegiancePmcFriendship.Apply(third);
        foreach(var id in new[]{"fourth","fifth","sixth"}) AllegiancePmcFriendship.Apply(Solo(id));AllegiancePmcFriendship.Apply(capped);
        Check(UnityEngine.Random.Rolls==7 && AllegiancePmcFriendship.CanRecruit(capped,boss.realPlayer),"No lifetime cap blocks later PMC selections");
        bot.IsDead=true;bot.HealthController.IsAlive=false;boss.Followers.Add(next);AllegiancePmcFriendship.Apply(capped);AllegiancePmcFriendship.Apply(Solo("replacement"));
        Check(UnityEngine.Random.Rolls==8,"Death and recruitment preserve sticky decisions while new profiles can roll");boss.Followers.Remove(next);
        FollowerGroupHostility.OnDamage(third,first);AllegiancePmcFriendship.Apply(third);
        Check(!AllegiancePmcFriendship.CanRecruit(third,boss.realPlayer) && third.BotsGroup.IsEnemy(boss.realPlayer) && UnityEngine.Random.Rolls==8,"Squad damage revokes friendship without rerolling");
        FollowerGroupHostility.DeclareContact(first,next);AllegiancePmcFriendship.Apply(next);
        Check(!AllegiancePmcFriendship.CanRecruit(next,boss.realPlayer) && next.BotsGroup.IsEnemy(second),"Explicit Contact revokes selected friendship for whole squad");
        Setup();GameplayModeRuntime.IsAllegiance=true;bot=Solo("growing");AllegiancePmcFriendship.Apply(bot);
        int groupCalls=bot.BotsGroup.Calls,squadCalls=boss.bossGroup.Calls;bot.BotsGroup.AddMember(Solo("joined"));
        Check(bot.BotsGroup.Locked,"Selected solo PMC group is reserved against later native spawn reuse");
        Check(!AllegiancePmcFriendship.CanRecruit(bot,boss.realPlayer) && !bot.BotsGroup.IsEnemy(boss.realPlayer),"Joining a group ends solo eligibility without declaring hostility");
        Check(bot.BotsGroup.Calls==groupCalls && boss.bossGroup.Calls==squadCalls && bot.BotsGroup.Neutrals.ContainsKey(boss.realPlayer),"Group growth preserves the established neutral relationship without enemy publication");
        FollowerGroupHostility.OnDamage(first,bot);
        Check(bot.BotsGroup.IsEnemy(boss.realPlayer) && boss.bossGroup.IsEnemy(bot),"Actual aggression after group growth still reaches the whole squad");
        Setup();GameplayModeRuntime.IsAllegiance=true;var growingScav=Scav("leva-regression");AllegiancePmcFriendship.Apply(growingScav);
        var scavGroup=growingScav.BotsGroup;groupCalls=scavGroup.Calls;squadCalls=boss.bossGroup.Calls;int selectionRolls=UnityEngine.Random.Rolls;
        Check(scavGroup.Locked,"Selected solo Scav group is reserved against later native spawn reuse");
        var newScav=Scav("later-scav");newScav.BotsGroup=scavGroup;scavGroup.AddMember(newScav);
        Check(!AllegiancePmcFriendship.CanRecruit(growingScav,boss.realPlayer) && !scavGroup.IsEnemy(boss.realPlayer) && !scavGroup.IsEnemy(first) && !boss.bossGroup.IsEnemy(growingScav),"Later Scav spawn cannot turn the selected neutral Scav into the PMC follower's enemy");
        Check(scavGroup.Calls==groupCalls && boss.bossGroup.Calls==squadCalls && FollowerGroupHostility.ShouldBlockCandidate(boss,growingScav),"Group growth never manufactures Scav hostile intent and the follower acquisition gate retains neutrality");
        scavGroup.Members.Remove(newScav);AllegiancePmcFriendship.Apply(growingScav);
        Check(UnityEngine.Random.Rolls==selectionRolls && !AllegiancePmcFriendship.CanRecruit(growingScav,boss.realPlayer),"A group survivor cannot reroll friendship or restore solo recruitment");
        FollowerGroupHostility.DeclareContact(first,growingScav);
        Check(scavGroup.IsEnemy(boss.realPlayer) && boss.bossGroup.IsEnemy(growingScav),"Explicit Contact after group growth still authorizes hostility");
        Check(scavGroup.Locked,"The selected profile's group reservation lasts for its raid even after aggression");
        Setup();GameplayModeRuntime.IsAllegiance=true;UnityEngine.Random.NextValue=0.99f;var failedSolo=Scav("failed-solo");AllegiancePmcFriendship.Apply(failedSolo);
        Check(!failedSolo.BotsGroup.Locked,"Failed friendship roll leaves ordinary native grouping unchanged");
        Setup();GameplayModeRuntime.IsAllegiance=true;var excludedGroup=Scav("excluded-group-lock");excludedGroup.BotsGroup.TargetMembersCount=2;AllegiancePmcFriendship.Apply(excludedGroup);
        Check(!excludedGroup.BotsGroup.Locked,"Original grouped spawn remains eligible for its native group members");
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
    private static BotOwner RecruitScavCompanion(string id="companion") {
        GameplayModeRuntime.IsAllegiance=true;pitTeam.pitFireTeam.friendlyEncounterChance.Value=80;
        UnityEngine.Random.NextValue=0;var scav=Scav(id);AllegiancePmcFriendship.Apply(scav);
        scav.BotFollower.BossToFollow=boss;boss.Followers.Add(scav);
        BossPlayers.Registered[scav.ProfileId]=new BotFollowerPlayer{Boss=boss,Bot=scav};
        BossPlayers.Registered[scav.ProfileId].AssignIdentity();
        AllegianceScavRelations.OnFollowerAdded(scav,boss,false);return scav;
    }
    private static void ScavProfileIdentityChecks() {
        foreach(string departure in new[]{"dismissal","death","removal"}) {
            Setup();boss.realPlayer.Profile.Info.GroupId="leader-group";boss.realPlayer.Profile.Info.TeamId="leader-team";
            GameplayModeRuntime.IsAllegiance=true;pitTeam.pitFireTeam.friendlyEncounterChance.Value=80;UnityEngine.Random.NextValue=0;
            var scav=Scav("identity-companion");scav.Profile.Info.GroupId="original-scav-group";scav.Profile.Info.TeamId="original-scav-team";
            AllegiancePmcFriendship.Apply(scav);var originalGroup=scav.BotsGroup;
            var follower=new BotFollowerPlayer{Boss=boss,Bot=scav};follower.AssignIdentity();
            // A repeated assignment must not overwrite the saved original IDs.
            follower.AssignIdentity();scav.BotFollower.BossToFollow=boss;boss.Followers.Add(scav);BossPlayers.Registered[scav.ProfileId]=follower;
            AllegianceScavRelations.OnFollowerAdded(scav,boss,false);
            Check(scav.GroupId==boss.realPlayer.GroupId&&scav.Profile.Info.TeamId=="leader-team","Scav shares leader profile IDs during recruitment: "+departure);
            Check(scav.BotsGroup==originalGroup&&originalGroup.Contains(scav)&&!boss.bossGroup.Contains(scav)&&scav.Side==EPlayerSide.Savage,"Shared profile IDs preserve native Scav group and side: "+departure);
            var enemy=Scav("identity-hostile");enemy.BotsGroup.Enemies[boss.realPlayer]=new();UnityEngine.Random.NextValue=0.9f;AllegianceScavRelations.Apply(enemy);
            enemy.BotsGroup._enemyPlayerGroups.Add("leader-group");enemy.BotsGroup._enemyPlayerGroups.Add("original-scav-group");
            if(departure=="dismissal") {follower.Dismiss(false);BossPlayers.RemoveFollower(scav,boss);}
            else if(departure=="death") {scav.HealthController.IsAlive=false;scav.HealthController.RaiseDeath();}
            else BossPlayers.RemoveFollower(scav,boss);
            Check(scav.Profile.Info.GroupId=="original-scav-group"&&scav.Profile.Info.TeamId=="original-scav-team","Departure restores both original IDs: "+departure);
            Check(enemy.BotsGroup._enemyPlayerGroups.Contains("leader-group")&&!enemy.BotsGroup._enemyPlayerGroups.Contains("original-scav-group"),"Departure cleanup clears the Scav's cache without clearing the leader's group hostility: "+departure);
            Check(enemy.BotsGroup.IsEnemy(boss.realPlayer)&&scav.BotsGroup==originalGroup,"Departure preserves human hostility and native group membership: "+departure);
            scav.Profile.Info.GroupId="later-group";scav.Profile.Info.TeamId="later-team";follower.RestoreProfileIdentity();
            Check(scav.GroupId=="later-group"&&scav.Profile.Info.TeamId=="later-team","Repeated cleanup cannot overwrite a later identity: "+departure);
        }
    }
    private static void ScavDiplomacyChecks() {
        foreach(var role in new[]{WildSpawnType.assault,WildSpawnType.marksman})
        foreach(float roll in new[]{0f,0.7499f,0.75f,0.9999f}) {
            Setup();var companion=RecruitScavCompanion();var own=companion.BotsGroup;
            Check(own.Side==EPlayerSide.Savage && own.MembersCount==1 && own.Contains(companion) && !boss.bossGroup.Contains(companion),"Scav remains a member only of the independent Savage group");
            Check(own.IsAlly(boss.realPlayer)&&own.IsAlly(first)&&own.IsAlly(second)&&boss.bossGroup.IsAlly(companion),"Player and PMC squad are reciprocal allies, not Scav group members");
            var target=Scav("ordinary");var group=target.BotsGroup;target.Profile.Info.Settings.Role=role;group.InitialBotType=role;group.Enemies[boss.realPlayer]=new BotGroupEnemyInfo();
            group.Enemies[first]=new BotGroupEnemyInfo();boss.bossGroup.Enemies[target]=new BotGroupEnemyInfo();
            UnityEngine.Random.NextValue=roll;int before=UnityEngine.Random.Rolls;
            AllegianceScavRelations.Apply(target);AllegianceScavRelations.Apply(target);
            Check(UnityEngine.Random.Rolls==before+1,"Each Scav group gets exactly one diplomacy roll");
            bool neutral=roll<0.75f;
            Check(group.Neutrals.ContainsKey(boss.realPlayer)==neutral && !group.IsAlly(boss.realPlayer),"75 percent boundary makes outsiders neutral, never allied");
            Check(group.IsEnemy(boss.realPlayer)!=neutral && group.IsEnemy(first)!=neutral && boss.bossGroup.IsEnemy(target)!=neutral,"Neutrality repairs both directions and preserves failed-roll hostility");
            if(neutral) {
                Check(!group.AddEnemy(boss.realPlayer,EBotEnemyCause.initial) && !boss.bossGroup.AddEnemy(target,EBotEnemyCause.initial),"Ambient native scans cannot undo group neutrality");
                Check(group.AddEnemy(boss.realPlayer,EBotEnemyCause.followGetHit) && own.IsEnemy(target),"Actual aggression ends neutrality and reaches the separate Scav group");
            } else {
                Check(group.IsEnemy(companion)&&own.IsEnemy(target),"A Scav remaining hostile to the leader is already mutually hostile with his Scav companion: "+role);
                group.AddEnemy(boss.realPlayer,EBotEnemyCause.initial);
                Check(own.IsEnemy(target),"Existing hostility toward the PMC leader reaches the independent Scav group");
            }
        }
        Setup();var recruitedScav=RecruitScavCompanion();var npc=Scav("new-pmc-target");boss.bossGroup.AddEnemy(npc,EBotEnemyCause.addPlayer);
        Check(recruitedScav.BotsGroup.IsEnemy(npc),"PMC group enemy publication reaches the Scav without making him a member");
        Setup();GameplayModeRuntime.IsAllegiance=true;var alreadyAggressive=Scav("prior-aggressor");
        FollowerGroupHostility.OnBossDamage(boss,alreadyAggressive);
        recruitedScav=RecruitScavCompanion();UnityEngine.Random.NextValue=0;AllegianceScavRelations.Apply(alreadyAggressive);
        Check(alreadyAggressive.BotsGroup.IsEnemy(boss.realPlayer),"Recruitment cannot neutralize explicit aggression from earlier in the raid");
        Setup();recruitedScav=RecruitScavCompanion();
        var groupedNeutral=Scav("group-neutral");var groupedMember=Scav("group-peer");groupedMember.BotsGroup=groupedNeutral.BotsGroup;groupedNeutral.BotsGroup.Members.Add(groupedMember);
        UnityEngine.Random.NextValue=0;int groupRolls=UnityEngine.Random.Rolls;AllegianceScavRelations.Apply(groupedNeutral);AllegianceScavRelations.Apply(groupedMember);
        Check(UnityEngine.Random.Rolls==groupRolls+1&&boss.bossGroup.Neutrals.ContainsKey(groupedNeutral)&&boss.bossGroup.Neutrals.ContainsKey(groupedMember),"An ordinary Scav group rolls once and neutralizes every member");
        Setup();recruitedScav=RecruitScavCompanion();var mixed=Scav("mixed-group");var sniper=Scav("sniper");sniper.Profile.Info.Settings.Role=WildSpawnType.marksman;mixed.BotsGroup.Members.Add(sniper);
        groupRolls=UnityEngine.Random.Rolls;AllegianceScavRelations.Apply(mixed);
        Check(UnityEngine.Random.Rolls==groupRolls+1&&mixed.BotsGroup.Neutrals.ContainsKey(boss.realPlayer),"Ordinary assault/marksman groups receive the same single diplomacy roll");
        Setup();recruitedScav=RecruitScavCompanion();
        foreach(WildSpawnType role in Enum.GetValues(typeof(WildSpawnType))) {
            if(role==WildSpawnType.assault||role==WildSpawnType.marksman)continue;
            var excluded=Scav("diplomacy-excluded-"+role);excluded.Profile.Info.Settings.Role=role;excluded.BotsGroup.InitialBotType=role;
            excluded.BotsGroup.Enemies[boss.realPlayer]=new BotGroupEnemyInfo();int before=UnityEngine.Random.Rolls;
            AllegianceScavRelations.Apply(excluded);
            Check(UnityEngine.Random.Rolls==before && excluded.BotsGroup.IsEnemy(boss.realPlayer),"Diplomacy leaves non-regular roles unchanged: "+role);
        }
        Setup();var beforeRecruit=Scav("before-recruit");beforeRecruit.BotsGroup.AddNeutral(boss.realPlayer);
        AllegianceScavRelations.RememberBeforeDamage(new EFT.Ballistics.DamageInfo{Player=boss.realPlayer,Damage=100},beforeRecruit);
        AllegianceScavRelations.RecordKill(beforeRecruit,boss.realPlayer);recruitedScav=RecruitScavCompanion();
        Check(!AllegianceScavRelations.TrustBroken,"Neutral kills before recruitment do not count");
        foreach(var killer in new Player[]{boss.realPlayer,first,second}) {
            var victim=Scav("neutral-victim-"+killer.ProfileId);UnityEngine.Random.NextValue=0;AllegianceScavRelations.Apply(victim);
            if(killer!=boss.realPlayer)FollowerGroupHostility.DeclareContact((BotOwner)killer,victim);
            AllegianceScavRelations.RememberBeforeDamage(new EFT.Ballistics.DamageInfo{Player=killer,Damage=100},victim);
            FollowerGroupHostility.OnDamage(victim,killer);
            Check(victim.BotsGroup.IsEnemy(boss.realPlayer),"Actual squad damage revokes neutrality before soft relationship sharing");
            AllegianceScavRelations.RecordKill(victim,killer);AllegianceScavRelations.RecordKill(victim,killer);
        }
        Check(AllegianceScavRelations.TrustBroken && pitTeam.Utils.Utils.Timers.Count==1,"Three distinct neutral kills across player and PMC followers queue one departure");
        Check(!AllegiancePmcFriendship.CanRecruit(Scav("another-recruit"),boss.realPlayer),"Broken trust rejects new Scav recruits");
        foreach(var action in pitTeam.Utils.Utils.Timers.ToArray())action();
        Check(!BossPlayers.IsFollower(recruitedScav)&&recruitedScav.BotFollower.BossToFollow==null,"Third kill removes the Scav from follower ownership");
        Check(recruitedScav.BotsGroup.IsEnemy(boss.realPlayer) && boss.bossGroup.IsEnemy(recruitedScav) && !recruitedScav.BotsGroup.IsAlly(boss.realPlayer),"Betrayal departure becomes mutually hostile");
        foreach(var companionEnemy in recruitedScav.BotsGroup.Enemies.Keys)
            Check(!AllegiancePmcFriendship.IsRegularScav(companionEnemy.AIData?.BotOwner),"Peaceful departure drops borrowed PMC-versus-Scav conflicts");
        Check(recruitedScav.BotsGroup.AddEnemy(boss.realPlayer,EBotEnemyCause.initial),"Departed Scav retains hostility toward the former leader");
        Check(recruitedScav.BotsGroup.AddEnemy(boss.realPlayer,EBotEnemyCause.addPlayer),"Departed Scav has no leftover friendship guard");
        FollowerGroupHostility.OnDamage(recruitedScav,boss.realPlayer);
        Check(recruitedScav.BotsGroup.AddEnemy(boss.realPlayer,EBotEnemyCause.followGetHit),"Departed Scav can retaliate against an actual shot");
        Setup();recruitedScav=RecruitScavCompanion();
        var hostile=Scav("already-enemy");hostile.BotsGroup.Enemies[boss.realPlayer]=new BotGroupEnemyInfo();
        var neutralByOther=Scav("outsider-kill");neutralByOther.BotsGroup.AddNeutral(boss.realPlayer);
        foreach(var target in new[]{hostile,neutralByOther}) {
            var attacker=target==hostile?boss.realPlayer:Solo("outsider");
            AllegianceScavRelations.RememberBeforeDamage(new EFT.Ballistics.DamageInfo{Player=attacker,Damage=100},target);
            AllegianceScavRelations.RecordKill(target,attacker);
        }
        Check(!AllegianceScavRelations.TrustBroken && pitTeam.Utils.Utils.Timers.Count==0,"Enemy Scav kills and outside killers do not consume trust");
        var trustProbe=Scav("unchanged-trust");UnityEngine.Random.NextValue=0.6f;AllegianceScavRelations.Apply(trustProbe);
        Check(trustProbe.BotsGroup.Neutrals.ContainsKey(boss.realPlayer),"Excluded kills leave the 75 percent neutrality threshold unchanged");
        for(int i=0;i<3;i++) {
            var victim=Scav("scav-killer-"+i);victim.BotsGroup.AddNeutral(boss.realPlayer);
            AllegianceScavRelations.RememberBeforeDamage(new EFT.Ballistics.DamageInfo{Player=recruitedScav,Damage=100},victim);
            AllegianceScavRelations.RecordKill(victim,recruitedScav);
        }
        Check(!AllegianceScavRelations.TrustBroken,"Kills by the Scav companion do not count as player or PMC betrayal");
        Setup();recruitedScav=RecruitScavCompanion();
        var highRoll=Scav("75-to-50");UnityEngine.Random.NextValue=0.6f;AllegianceScavRelations.Apply(highRoll);
        var midRoll=Scav("50-to-25");UnityEngine.Random.NextValue=0.3f;AllegianceScavRelations.Apply(midRoll);
        for(int i=0;i<2;i++) {
            var victim=Scav("escalation-"+i);UnityEngine.Random.NextValue=0;AllegianceScavRelations.Apply(victim);
            AllegianceScavRelations.RememberBeforeDamage(new EFT.Ballistics.DamageInfo{Player=boss.realPlayer,Damage=100},victim);
            AllegianceScavRelations.RecordKill(victim,boss.realPlayer);
            Check(highRoll.BotsGroup.IsEnemy(boss.realPlayer)&&midRoll.BotsGroup.IsEnemy(boss.realPlayer)==(i==1),"Trust loss uses the original group rolls at 50 and 25 percent");
        }
        AllegianceScavRelations.Reset();
        Check(!AllegianceScavRelations.RefusesRecruitment(recruitedScav),"Raid reset clears diplomatic trust and refusal");
    }
    private static void ScavReciprocalHostilityChecks() {
        foreach(var role in new[]{WildSpawnType.assault,WildSpawnType.marksman}) {
            Setup();var one=RecruitScavCompanion("reciprocal-one");var two=RecruitScavCompanion("reciprocal-two");
            var target=Scav("reciprocal-hostile");target.Profile.Info.Settings.Role=role;target.BotsGroup.InitialBotType=role;
            var group=target.BotsGroup;group.Enemies[boss.realPlayer]=new();group.Allies.Add(one);group.Allies.Add(two);
            one.BotsGroup.Enemies[target]=new(); // The old one-way relationship must also be repaired.
            UnityEngine.Random.NextValue=0.9f;AllegianceScavRelations.Apply(target);
            Check(group.IsEnemy(one)&&group.IsEnemy(two)&&one.BotsGroup.IsEnemy(target)&&two.BotsGroup.IsEnemy(target),"Every living Scav companion and an uncalmed group become mutual enemies: "+role);
            Check(!group.IsAlly(one)&&!group.IsAlly(two),"Native enemy admission removes stale same-side allies: "+role);
            Check(one.Side==EPlayerSide.Savage&&one.BotsGroup!=boss.bossGroup&&group.MembersCount==1,"Traitor treatment preserves native side and group membership: "+role);
            Check(one.Memory.GoalEnemy==null&&target.Memory.GoalEnemy==null&&one.EnemiesController.EnemyInfos.Count==0,"Relationship repair does not manufacture perception or select a combat goal: "+role);
            int calls=group.Calls;AllegianceScavRelations.Apply(target);
            Check(group.Calls==calls,"Already reciprocal hostility is not republished on repeated application: "+role);

            Setup();one=RecruitScavCompanion();target=Scav("reciprocal-later-aggression");target.Profile.Info.Settings.Role=role;target.BotsGroup.InitialBotType=role;
            UnityEngine.Random.NextValue=0;AllegianceScavRelations.Apply(target);
            Check(target.BotsGroup.Neutrals.ContainsKey(one)&&one.BotsGroup.Neutrals.ContainsKey(target),"Successful calming leaves both Scav directions neutral: "+role);
            target.BotsGroup.AddEnemy(boss.realPlayer,EBotEnemyCause.followGetHit);
            Check(target.BotsGroup.IsEnemy(one)&&one.BotsGroup.IsEnemy(target),"Later actual aggression against the leader updates both Scav directions: "+role);

            Setup();one=RecruitScavCompanion();target=Scav("reciprocal-native-rejection");target.Profile.Info.Settings.Role=role;target.BotsGroup.InitialBotType=role;
            target.BotsGroup.Enemies[boss.realPlayer]=new();target.BotsGroup.Reject=true;UnityEngine.Random.NextValue=0.9f;
            AllegianceScavRelations.Apply(target);Check(!target.BotsGroup.IsEnemy(one),"Reciprocal sharing respects native enemy rejection: "+role);
            target.BotsGroup.Reject=false;AllegianceScavRelations.Apply(target);
            Check(target.BotsGroup.IsEnemy(one),"A previously rejected reciprocal relationship can be admitted later: "+role);

            foreach(bool directAggressor in new[]{false,true}) {
                Setup();one=RecruitScavCompanion();target=Scav("reciprocal-departure");target.Profile.Info.Settings.Role=role;target.BotsGroup.InitialBotType=role;
                target.BotsGroup.Enemies[boss.realPlayer]=new();UnityEngine.Random.NextValue=0.9f;AllegianceScavRelations.Apply(target);
                if(directAggressor) AllegianceScavRelations.OnDamage(one,target);
                BossPlayers.RemoveFollower(one,boss);
                Check(target.BotsGroup.IsEnemy(one)==directAggressor&&one.BotsGroup.IsEnemy(target)==directAggressor,"Departure clears borrowed hostility in both directions but retains direct aggression: "+role+"/"+directAggressor);
                Check(target.BotsGroup.IsEnemy(boss.realPlayer),"Companion departure preserves the group's original player hostility: "+role+"/"+directAggressor);
            }

            Setup();one=RecruitScavCompanion();target=Scav("reciprocal-independent-neutral");target.Profile.Info.Settings.Role=role;target.BotsGroup.InitialBotType=role;
            target.BotsGroup.AddNeutral(boss.realPlayer);UnityEngine.Random.NextValue=0.9f;AllegianceScavRelations.Apply(target);
            Check(!target.BotsGroup.IsEnemy(one)&&!one.BotsGroup.IsEnemy(target),"A failed calming roll does not turn an independently neutral Scav hostile: "+role);

            Setup();GameplayModeRuntime.IsAllegiance=true;target=Scav("reciprocal-before-recruitment");target.Profile.Info.Settings.Role=role;target.BotsGroup.InitialBotType=role;
            target.BotsGroup.Enemies[boss.realPlayer]=new();FollowerGroupHostility.OnBossDamage(boss,target);
            Comfort.Common.Singleton<GameWorld>.Instance.AllAlivePlayersList.Add(target);one=RecruitScavCompanion();
            Check(target.BotsGroup.IsEnemy(one)&&one.BotsGroup.IsEnemy(target),"Recruitment also repairs already-present aggressive Scav groups: "+role);
        }
        Setup();var survivor=RecruitScavCompanion();var sniper=Scav("neutral-sniper-trust");sniper.Profile.Info.Settings.Role=WildSpawnType.marksman;sniper.BotsGroup.InitialBotType=WildSpawnType.marksman;
        UnityEngine.Random.NextValue=0;AllegianceScavRelations.Apply(sniper);
        sniper.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=boss.realPlayer,Damage=100},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
        Check(boss.realPlayer.Profile.FenceInfo.Changes==1,"Intentionally killing a calmed sniper follows the neutral-Scav Fence rule");
        var threshold=Scav("sniper-trust-threshold");UnityEngine.Random.NextValue=0.6f;AllegianceScavRelations.Apply(threshold);
        Check(!threshold.BotsGroup.Neutrals.ContainsKey(boss.realPlayer),"A calmed sniper kill also reduces the saved trust threshold");
        Check(!AllegiancePmcFriendship.CanRecruit(sniper,boss.realPlayer),"Diplomatic inclusion does not make sniper Scavs recruitable");
    }
    private static void ScavCompanionLossChecks() {
        Setup();var companion=RecruitScavCompanion();var target=Scav("loss-target");
        target.BotsGroup.Enemies[boss.realPlayer]=new();target.BotsGroup.Enemies[first]=new();
        boss.bossGroup.Enemies[target]=new();UnityEngine.Random.NextValue=0;AllegianceScavRelations.Apply(target);
        Check(companion.HealthController.DeathListeners==1,"Companion subscribes once to native death cleanup");
        AllegianceScavRelations.OnFollowerAdded(companion,boss,false);
        Check(companion.HealthController.DeathListeners==1,"Repeated registration cannot leak death handlers");
        companion.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=Solo("enemy-killer",EPlayerSide.Bear),Damage=100},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
        Check(target.BotsGroup.IsEnemy(boss.realPlayer)&&target.BotsGroup.IsEnemy(first)&&boss.bossGroup.IsEnemy(target),"Enemy kill of the last companion restores borrowed hostility in both directions");
        Check(companion.HealthController.DeathListeners==0&&!AllegianceScavRelations.IsCompanionGroup(companion.BotsGroup),"Death removes diplomatic ownership and detaches its listener");
        Check(boss.realPlayer.Profile.FenceInfo.Changes==0&&!AllegianceScavRelations.TrustBroken,"Enemy kill adds no Fence or trust penalty");
        int rolls=UnityEngine.Random.Rolls;AllegianceScavRelations.Apply(target);
        Check(UnityEngine.Random.Rolls==rolls&&target.BotsGroup.IsEnemy(boss.realPlayer),"No living companion means no renewed diplomacy or rolls");
        Check(target.BotsGroup.AddEnemy(second,EBotEnemyCause.initial),"Ambient hostility is no longer suppressed after the last companion dies");
        RecruitScavCompanion("replacement");AllegianceScavRelations.Apply(target);
        Check(target.BotsGroup.Neutrals.ContainsKey(boss.realPlayer)&&UnityEngine.Random.Rolls==rolls+1,"A replacement companion reuses the ordinary group's saved roll");

        Setup();var one=RecruitScavCompanion("loss-one");var two=RecruitScavCompanion("loss-two");target=Scav("two-companion-target");
        target.BotsGroup.Enemies[boss.realPlayer]=new();AllegianceScavRelations.Apply(target);
        one.HealthController.IsAlive=false;one.IsDead=true;one.HealthController.RaiseDeath();
        Check(target.BotsGroup.Neutrals.ContainsKey(boss.realPlayer)&&!target.BotsGroup.AddEnemy(boss.realPlayer,EBotEnemyCause.initial),"One surviving Scav retains group neutrality");
        two.HealthController.IsAlive=false;two.HealthController.RaiseDeath();
        Check(target.BotsGroup.IsEnemy(boss.realPlayer),"Death without an aggressor still releases the last companion's neutrality");

        Setup();companion=RecruitScavCompanion();target=Scav("dismissal-target");target.BotsGroup.Enemies[boss.realPlayer]=new();
        AllegianceScavRelations.Apply(target);BossPlayers.RemoveFollower(companion,boss);
        Check(target.BotsGroup.IsEnemy(boss.realPlayer)&&companion.HealthController.DeathListeners==0,"Last companion dismissal restores ordinary hostility and detaches death cleanup");
        Check(!companion.BotsGroup.IsEnemy(boss.realPlayer),"Manual departure remains peaceful toward the dismissed Scav");

        Setup();companion=RecruitScavCompanion();var independent=Scav("independent-neutral");independent.BotsGroup.AddNeutral(boss.realPlayer);
        var selected=Scav("independent-selected");AllegiancePmcFriendship.Apply(selected);
        var baseline=Scav("native-friendly");AllegianceScavRelations.Apply(independent);AllegianceScavRelations.Apply(selected);AllegianceScavRelations.Apply(baseline);
        companion.HealthController.IsAlive=false;companion.HealthController.RaiseDeath();
        Check(independent.BotsGroup.Neutrals.ContainsKey(boss.realPlayer),"Pre-existing independent neutrality survives companion loss");
        Check(selected.BotsGroup.Neutrals.ContainsKey(boss.realPlayer)&&!selected.BotsGroup.AddEnemy(boss.realPlayer,EBotEnemyCause.initial),"Selected friendship survives loss of its diplomatic companion");
        Check(!baseline.BotsGroup.Neutrals.ContainsKey(boss.realPlayer)&&!baseline.BotsGroup.IsEnemy(boss.realPlayer),"Removing borrowed neutrality respects native non-hostile policy");

        Setup();companion=RecruitScavCompanion();target=Scav("native-hostile-after-loss");target.BotsGroup.PolicyEnemy=true;AllegianceScavRelations.Apply(target);
        var aggressor=Scav("preserve-real-aggressor");AllegianceScavRelations.Apply(aggressor);
        aggressor.BotsGroup.AddEnemy(boss.realPlayer,EBotEnemyCause.followGetHit);
        companion.HealthController.IsAlive=false;companion.HealthController.RaiseDeath();
        Check(target.BotsGroup.IsEnemy(boss.realPlayer),"Native hostile policy resumes even when no enemy entry existed before diplomacy");
        Check(aggressor.BotsGroup.IsEnemy(boss.realPlayer),"Releasing diplomacy never clears actual aggression");
        RecruitScavCompanion("after-aggression");AllegianceScavRelations.Apply(aggressor);
        Check(aggressor.BotsGroup.IsEnemy(boss.realPlayer),"A replacement companion cannot forgive the saved aggressor");

        Setup();companion=RecruitScavCompanion();target=Scav("native-rejection-after-loss");target.BotsGroup.Enemies[boss.realPlayer]=new();AllegianceScavRelations.Apply(target);
        target.BotsGroup.Reject=true;companion.HealthController.IsAlive=false;companion.HealthController.RaiseDeath();
        Check(!target.BotsGroup.IsEnemy(boss.realPlayer)&&!target.BotsGroup.Neutrals.ContainsKey(boss.realPlayer),"Restoration never bypasses native AddEnemy rejection");
        target.BotsGroup.Reject=false;Check(target.BotsGroup.AddEnemy(boss.realPlayer,EBotEnemyCause.initial),"Subsequent native admission is no longer blocked by stale companion neutrality");

        foreach(var killerKind in new[]{"human","ordered-pmc","uncommanded-pmc"}) {
            Setup();companion=RecruitScavCompanion();var sound=new EFT.UI.GUISounds();Comfort.Common.Singleton<EFT.UI.GUISounds>.Instance=sound;
            target=Scav("kill-policy-"+killerKind);target.BotsGroup.Enemies[boss.realPlayer]=new();AllegianceScavRelations.Apply(target);
            if(killerKind=="ordered-pmc")AllegianceScavRelations.RememberContact(boss,companion);
            companion.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=killerKind=="human"?boss.realPlayer:first,Damage=100},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
            Check(target.BotsGroup.IsEnemy(boss.realPlayer),"All last-companion killer paths release neutrality: "+killerKind);
            Check(boss.realPlayer.Profile.FenceInfo.Changes==(killerKind=="uncommanded-pmc"?0:1)&&sound.Negative==(killerKind=="uncommanded-pmc"?0:1),"Intentional versus uncommanded companion deaths retain their Fence policy: "+killerKind);
            var replacement=RecruitScavCompanion("penalty-replacement");var threshold=Scav("loss-trust-threshold");UnityEngine.Random.NextValue=0.6f;AllegianceScavRelations.Apply(threshold);
            Check(threshold.BotsGroup.Neutrals.ContainsKey(boss.realPlayer)==(killerKind=="uncommanded-pmc"),"Companion loss preserves the raid's intentional trust history: "+killerKind);
            AllegianceScavRelations.Reset();Check(replacement.HealthController.DeathListeners==0,"Raid reset detaches surviving death handlers");
        }
    }
    private static void ScavTrustDamageChecks() {
        Setup();var survivor=RecruitScavCompanion();
        foreach(var killer in new Player[]{boss.realPlayer,first,second}) {
            var victim=Scav("wounded-"+killer.ProfileId);UnityEngine.Random.NextValue=0;AllegianceScavRelations.Apply(victim);
            if(killer!=boss.realPlayer)FollowerGroupHostility.DeclareContact((BotOwner)killer,victim);
            victim.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=killer,Damage=10},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
            Check(victim.HealthController.IsAlive && victim.BotsGroup.IsEnemy(boss.realPlayer),"Nonlethal squad hit causes native retaliation after pre-damage capture");
            // The victim fights back before the same player/squad kills him.
            FollowerGroupHostility.OnDamage(first,victim);
            victim.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=killer,Damage=100},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
            AllegianceScavRelations.RecordKill(victim,killer);
        }
        Check(AllegianceScavRelations.TrustBroken && pitTeam.Utils.Utils.Timers.Count==1,"Three wounded-then-retaliating kills count once each through the real damage prefix");
        foreach(var timer in pitTeam.Utils.Utils.Timers.ToArray()) timer();
        Check(!BossPlayers.IsFollower(survivor) && survivor.BotsGroup.IsEnemy(boss.realPlayer),"Intentional wounded Scav kills make the survivor leave hostile");

        Setup();survivor=RecruitScavCompanion();var companion=RecruitScavCompanion("killed-companion");
        companion.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=boss.realPlayer,Damage=10},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
        companion.BeforeDeath=()=>BossPlayers.RemoveFollower(companion,boss);
        companion.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=boss.realPlayer,Damage=100},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
        for(int i=0;i<2;i++) {
            var victim=Scav("instant-"+i);UnityEngine.Random.NextValue=0;AllegianceScavRelations.Apply(victim);
            victim.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=boss.realPlayer,Damage=100},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
        }
        Check(AllegianceScavRelations.TrustBroken && pitTeam.Utils.Utils.Timers.Count==1,"Companion killed after wounding/registration cleanup plus two instant kills break trust");
        foreach(var timer in pitTeam.Utils.Utils.Timers.ToArray()) timer();
        Check(!BossPlayers.IsFollower(survivor),"Killing a companion contributes to the remaining Scav's departure");

        Setup();survivor=RecruitScavCompanion();
        for(int i=0;i<3;i++) {
            var hostile=Scav("hostile-first-hit-"+i);hostile.BotsGroup.Enemies[boss.realPlayer]=new BotGroupEnemyInfo();
            hostile.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=boss.realPlayer,Damage=10},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
            FactionHostility.EnsureNeutral(hostile.BotsGroup,boss.realPlayer);FactionHostility.EnsureNeutral(boss.bossGroup,hostile);
            hostile.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=boss.realPlayer,Damage=100},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
        }
        Check(!AllegianceScavRelations.TrustBroken,"Originally hostile victims cannot become betrayal kills through later relationship changes");
        var probe=Scav("zero-damage");UnityEngine.Random.NextValue=0;AllegianceScavRelations.Apply(probe);
        probe.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=boss.realPlayer,Damage=0},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
        probe.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=boss.realPlayer,Damage=100},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
        var threshold=Scav("one-valid-kill");UnityEngine.Random.NextValue=0.6f;AllegianceScavRelations.Apply(threshold);
        Check(!threshold.BotsGroup.Neutrals.ContainsKey(boss.realPlayer),"Zero damage does not freeze capture; later real damage counts the friendly kill");
        AllegianceScavRelations.Reset();
        AllegianceScavRelations.RecordKill(probe,boss.realPlayer);
        Check(!AllegianceScavRelations.TrustBroken,"Raid reset clears captured wounded victim identities");
    }
    private static void ScavIntentChecks() {
        Setup();var companion=RecruitScavCompanion();
        var sound=new EFT.UI.GUISounds();Comfort.Common.Singleton<EFT.UI.GUISounds>.Instance=sound;
        for(int i=0;i<4;i++) {
            var victim=Scav("accidental-"+i);UnityEngine.Random.NextValue=0;AllegianceScavRelations.Apply(victim);
            victim.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=first,Damage=10},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
            // Finishing the AI-started fight personally must not retroactively blame the player.
            victim.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=i%2==0?boss.realPlayer:second,Damage=100},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
        }
        Check(!AllegianceScavRelations.TrustBroken && boss.realPlayer.Profile.FenceInfo.Changes==0,"Uncommanded PMC aggression never consumes trust or Fence standing, even if player finishes");
        var unrelated=Scav("unmarked-sibling");var ordered=Scav("marked-target");
        ordered.BotsGroup.Members.Add(unrelated);unrelated.BotsGroup=ordered.BotsGroup;
        UnityEngine.Random.NextValue=0;AllegianceScavRelations.Apply(ordered);
        FollowerGroupHostility.DeclareContact(first,ordered);
        ordered.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=first,Damage=100},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
        unrelated.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=second,Damage=100},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
        Check(boss.realPlayer.Profile.FenceInfo.Changes==1 && Math.Abs(boss.realPlayer.Profile.FenceInfo.Standing-0.175)<0.000001 && sound.Negative==0,"Contact authorizes only the marked victim; exact -0.025 Fence penalty, no sound for unrecruited target");
        var wounded=Scav("human-first");UnityEngine.Random.NextValue=0;AllegianceScavRelations.Apply(wounded);
        wounded.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=boss.realPlayer,Damage=10},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
        wounded.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=second,Damage=100},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
        var remaining=Scav("selected-survivor");AllegiancePmcFriendship.Apply(remaining);
        var existing=Scav("existing-neutral");UnityEngine.Random.NextValue=0;AllegianceScavRelations.Apply(existing);
        Comfort.Common.Singleton<GameWorld>.Instance.AllAlivePlayersList.AddRange(new[]{remaining,existing});
        companion.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=boss.realPlayer,Damage=100},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
        AllegianceScavRelations.RecordKill(companion,boss.realPlayer);
        Check(AllegianceScavRelations.TrustBroken && boss.realPlayer.Profile.FenceInfo.Changes==3 && sound.Negative==1,"Human-first PMC finish and companion kill count, with one recruited death sound and no duplicate penalty");
        Check(Math.Abs(boss.realPlayer.Profile.FenceInfo.Standing-0.1)<0.000001,"Two unrecruited friendly kills cost 0.025 each and the recruited kill costs 0.05");
        Check(remaining.BotsGroup.IsEnemy(boss.realPlayer)&&existing.BotsGroup.IsEnemy(boss.realPlayer),"Trust failure revokes existing selected and neutral Scavs immediately");
        var late=Scav("late-spawn");int rolls=UnityEngine.Random.Rolls;
        AllegiancePmcFriendship.Apply(late);AllegianceScavRelations.Apply(late);
        Check(UnityEngine.Random.Rolls==rolls && late.BotsGroup.IsEnemy(boss.realPlayer) && boss.bossGroup.IsEnemy(late),"Later spawns become hostile without a friendship reroll");
        var lateSniper=Scav("sniper-after-betrayal");lateSniper.Profile.Info.Settings.Role=WildSpawnType.marksman;lateSniper.BotsGroup.InitialBotType=WildSpawnType.marksman;
        AllegianceScavRelations.Apply(lateSniper);Check(lateSniper.BotsGroup.IsEnemy(boss.realPlayer),"Broken trust also affects sniper Scavs");
        foreach(var role in new[]{WildSpawnType.bossKilla,WildSpawnType.followerBully}) {
            var excluded=Scav("excluded-after-betrayal-"+role);excluded.Profile.Info.Settings.Role=role;excluded.BotsGroup.InitialBotType=role;
            AllegianceScavRelations.Apply(excluded);
            Check(!excluded.BotsGroup.IsEnemy(boss.realPlayer),"Raid hostility keeps special roles excluded: "+role);
        }
        Setup();var one=RecruitScavCompanion("leaving-one");var two=RecruitScavCompanion("leaving-two");
        for(int i=0;i<3;i++) {
            var victim=Scav("third-party-victim-"+i);UnityEngine.Random.NextValue=0;AllegianceScavRelations.Apply(victim);
            victim.ApplyDamageInfo(new EFT.Ballistics.DamageInfo{Player=boss.realPlayer,Damage=100},EBodyPart.Chest,EBodyPartColliderType.Chest,0);
        }
        foreach(var timer in pitTeam.Utils.Utils.Timers.ToArray())timer();
        Check(!BossPlayers.IsFollower(one)&&!BossPlayers.IsFollower(two)&&one.BotsGroup.IsEnemy(boss.realPlayer)&&two.BotsGroup.IsEnemy(boss.realPlayer),"All living companions leave and become hostile in the same deferred callback");
        Check(!one.BotsGroup.IsEnemy(two)&&!two.BotsGroup.IsEnemy(one),"Departing companions never inherit hostility toward each other");
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
        pitTeam.Utils.Utils.Friendly=true;
        AllegiancePmcFriendship.OnFollowerAdded(second,boss.realPlayer);
        Check(friendly.BotsGroup.IsEnemy(second) && boss.bossGroup.IsEnemy(friendly),"Disabled Guns for Hire encounters do not apply selected-candidate relationship repair");
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
        AllegiancePmcFriendship.Apply(own);AllegiancePmcFriendship.Apply(third);
        foreach(var id in new[]{"fourth","fifth","sixth"}) AllegiancePmcFriendship.Apply(Solo(id));AllegiancePmcFriendship.Apply(capped);
        Check(UnityEngine.Random.Rolls==7 && AllegiancePmcFriendship.CanRecruit(capped,boss.realPlayer),"Neither faction imposes an encounter cap");
        FollowerGroupHostility.OnDamage(bear,first);AllegiancePmcFriendship.Apply(bear);
        Check(!AllegiancePmcFriendship.CanRecruit(bear,boss.realPlayer) && bear.BotsGroup.IsEnemy(second) && UnityEngine.Random.Rolls==7,"Cross-faction aggression revokes selection without rerolling");
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
            pitTeam.pitFireTeam.friendlyEncounterChance.Value=80;
            UnityEngine.Random.NextValue=expected ? 1f : 0f;AllegiancePmcFriendship.Apply(candidate);
            Check(UnityEngine.Random.Rolls==1 && AllegiancePmcFriendship.CanRecruit(candidate,boss.realPlayer)==expected,"Success and failure remain sticky without rerolling");
        }
        Setup();GameplayModeRuntime.IsAllegiance=true;UnityEngine.Random.NextValue=1f;
        AllegiancePmcFriendship.Apply(Solo("failed-own"));AllegiancePmcFriendship.Apply(Solo("failed-other",EPlayerSide.Bear));
        UnityEngine.Random.NextValue=0f;
        foreach(var id in new[]{"selected-one","selected-two","selected-three","selected-four","selected-five","selected-six"}) {
            var candidate=Solo(id);AllegiancePmcFriendship.Apply(candidate);
            Check(AllegiancePmcFriendship.CanRecruit(candidate,boss.realPlayer),"Failed rolls do not consume a friendly selection slot");
        }
        var capped=Solo("chance-capped",EPlayerSide.Bear);AllegiancePmcFriendship.Apply(capped);
        Check(UnityEngine.Random.Rolls==9 && AllegiancePmcFriendship.CanRecruit(capped,boss.realPlayer),"Failed rolls never block later new candidates");
        AllegiancePmcFriendship.Reset();
    }
    private static void GunsForHireEncounterChecks() {
        foreach(var playerSide in new[]{EPlayerSide.Usec,EPlayerSide.Bear})
        foreach(int chancePercent in new[]{5,10,20,30,40,60,80}) {
            float chance=chancePercent/100f;
            foreach(float roll in new[]{chance-0.0001f,chance+0.0001f,1f}) {
                Setup();boss.realPlayer.Profile.Info.Side=playerSide;pitTeam.pitFireTeam.friendlyEncounterChance.Value=chancePercent;
                FriendlyEncounterPenaltyRuntime.Points=100;var candidate=Solo("hired-mode-chance");UnityEngine.Random.NextValue=roll;
                AllegiancePmcFriendship.Apply(candidate);
                Check(AllegiancePmcFriendship.IsSelectedFriendly(candidate,boss.realPlayer)==(roll<chance),"Guns for Hire uses configured same-side chance without Allegiance penalties");
                AllegiancePmcFriendship.Apply(candidate);Check(UnityEngine.Random.Rolls==1,"Guns for Hire never rerolls a candidate");
            }
            Setup();boss.realPlayer.Profile.Info.Side=playerSide;pitTeam.pitFireTeam.friendlyEncounterChance.Value=chancePercent;
            var opposite=Solo("opposite",playerSide==EPlayerSide.Usec ? EPlayerSide.Bear : EPlayerSide.Usec);AllegiancePmcFriendship.Apply(opposite);
            Check(UnityEngine.Random.Rolls==0 && !AllegiancePmcFriendship.IsSelectedFriendly(opposite,boss.realPlayer),"Opposite faction never rolls in Guns for Hire");
        }
        foreach(bool friendlyPmc in new[]{false,true}) {
            Setup();pitTeam.Utils.Utils.Friendly=friendlyPmc;pitTeam.Utils.Utils.Bad=!friendlyPmc;pitTeam.pitFireTeam.friendlyEncounterChance.Value=80;
            AllegiancePmcFriendship.Apply(Solo("disabled"));Check(UnityEngine.Random.Rolls==0 && !AllegiancePmcFriendship.IsEnabled,"Friendly PMC Side and Bad Guy take precedence over Guns for Hire encounters");
        }
        Setup();var grouped=Solo("grouped");grouped.BotsGroup.AddMember(Solo("group-member"));AllegiancePmcFriendship.Apply(grouped);
        Check(UnityEngine.Random.Rolls==0,"Guns for Hire excludes grouped candidates");
        for(int i=0;i<10;i++){var candidate=Solo("no-cap-"+i);AllegiancePmcFriendship.Apply(candidate);Check(AllegiancePmcFriendship.IsSelectedFriendly(candidate,boss.realPlayer),"Guns for Hire has no lifetime cap");}
        Setup();var candidateBot=Solo("friendly");AllegiancePmcFriendship.Apply(candidateBot);
        Check(!candidateBot.BotsGroup.AddEnemy(boss.realPlayer,EBotEnemyCause.initial),"Ambient enemy scans preserve Guns for Hire encounters");
        AllegianceFriendlyGreeting.Calls=0;BotOwnerUpdateHub.Invoke(candidateBot);
        Check(AllegianceFriendlyGreeting.Calls==1,"Selected Guns for Hire friendly receives greetings");
        var recruit=Solo("new-recruit");candidateBot.BotsGroup.Enemies[recruit]=new BotGroupEnemyInfo();
        recruit.BotsGroup=boss.bossGroup;recruit.BotFollower.BossToFollow=boss;
        boss.Followers.Add(recruit);boss.bossGroup.Members.Add(recruit);boss.bossGroup.Enemies[candidateBot]=new BotGroupEnemyInfo();
        AllegiancePmcFriendship.OnFollowerAdded(recruit,boss.realPlayer);
        Check(!candidateBot.BotsGroup.IsEnemy(recruit) && !boss.bossGroup.IsEnemy(candidateBot),"New Guns for Hire follower and remaining encounter are neutral in both directions");
        FollowerGroupHostility.DeclareContact(first,candidateBot);
        AllegiancePmcFriendship.Apply(candidateBot);
        Check(!AllegiancePmcFriendship.IsSelectedFriendly(candidateBot,boss.realPlayer) && candidateBot.BotsGroup.IsEnemy(boss.realPlayer) && UnityEngine.Random.Rolls==1,"Explicit aggression revokes Guns for Hire friendship without reroll");
        Setup();candidateBot=Solo("before-selection");FollowerGroupHostility.DeclareContact(first,candidateBot);
        AllegiancePmcFriendship.Apply(candidateBot);
        Check(UnityEngine.Random.Rolls==0,"Guns for Hire cannot clear hostility declared before selection");
        AllegiancePmcFriendship.Reset();
    }
    private static void ConfiguredChanceChecks() {
        foreach(var playerSide in new[]{EPlayerSide.Usec,EPlayerSide.Bear}) foreach(bool sameSide in new[]{true,false})
        foreach(int chancePercent in new[]{5,10,15,20,25,30,40,60,80}) {
            float chance=(sameSide ? chancePercent : chancePercent<=10 ? chancePercent : chancePercent<30 ? 10f+(chancePercent-10)/4f : chancePercent/2f)/100f;
            foreach(float offset in new[]{-0.0001f,0.0001f}) {
                Setup();GameplayModeRuntime.IsAllegiance=true;boss.realPlayer.Profile.Info.Side=playerSide;pitTeam.pitFireTeam.friendlyEncounterChance.Value=chancePercent;
                UnityEngine.Random.NextValue=chance+offset;var candidate=Solo("configured-chance",sameSide ? playerSide : playerSide==EPlayerSide.Usec ? EPlayerSide.Bear : EPlayerSide.Usec);
                AllegiancePmcFriendship.Apply(candidate);Check(AllegiancePmcFriendship.IsSelectedFriendly(candidate,boss.realPlayer)==(offset<0),"Faction chance follows the percentage curve for either human side");
                pitTeam.pitFireTeam.friendlyEncounterChance.Value=80;UnityEngine.Random.NextValue=offset<0 ? 1f : 0f;AllegiancePmcFriendship.Apply(candidate);
                Check(UnityEngine.Random.Rolls==1 && AllegiancePmcFriendship.IsSelectedFriendly(candidate,boss.realPlayer)==(offset<0),"Slider changes never reroll existing candidates");
            }
        }
    }
    private static void BossDamageChecks() {
        foreach(bool sameSide in new[]{true,false})
        foreach(int chancePercent in new[]{5,10,20,30,40,60,80}) {
            Setup();GameplayModeRuntime.IsAllegiance=true;FriendlyEncounterPenaltyRuntime.Points=15;
            pitTeam.pitFireTeam.friendlyEncounterChance.Value=chancePercent;
            float baseChance=(sameSide ? chancePercent : chancePercent<=10 ? chancePercent : chancePercent<30 ? 10f+(chancePercent-10)/4f : chancePercent/2f)/100f;
            float scaledChance=baseChance*0.85f;
            UnityEngine.Random.NextValue=scaledChance+0.001f;
            var penalizedCandidate=Solo("penalized",sameSide ? EPlayerSide.Usec : EPlayerSide.Bear);
            AllegiancePmcFriendship.Apply(penalizedCandidate);
            Check(!AllegiancePmcFriendship.CanRecruit(penalizedCandidate,boss.realPlayer),"Penalty reduces final chance by 15% for either faction");
            var acceptedCandidate=Solo("below-penalty-threshold",sameSide ? EPlayerSide.Usec : EPlayerSide.Bear);
            UnityEngine.Random.NextValue=scaledChance-0.001f;
            AllegiancePmcFriendship.Apply(acceptedCandidate);
            Check(AllegiancePmcFriendship.CanRecruit(acceptedCandidate,boss.realPlayer),"Roll below the relative penalty threshold permits friendship");
            FriendlyEncounterPenaltyRuntime.Points=0;AllegiancePmcFriendship.Apply(penalizedCandidate);
            Check(UnityEngine.Random.Rolls==2 && !AllegiancePmcFriendship.CanRecruit(penalizedCandidate,boss.realPlayer),"Penalty expiry does not reroll an existing bot");
        }
        Setup();GameplayModeRuntime.IsAllegiance=true;FriendlyEncounterPenaltyRuntime.Points=100;
        pitTeam.pitFireTeam.friendlyEncounterChance.Value=80;UnityEngine.Random.NextValue=0f;
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
        AllegianceFriendlyGreeting.Cancellations=0;
        boss.Followers.Add(friendly); BotOwnerUpdateHub.Invoke(friendly);
        AllegiancePmcFriendship.OnFollowerAdded(friendly,boss.realPlayer);
        Check(AllegianceFriendlyGreeting.Cancellations==1,"Successful recruitment cancels all pending encounter signals");
        Check(AllegianceFriendlyGreeting.Calls==1,"Recruitment removes candidate from greeting dispatch");
        boss.Followers.Remove(friendly);
        BotOwnerUpdateHub.Invoke(friendly);
        Check(AllegianceFriendlyGreeting.Calls==1,"Dismissal cannot resurrect a recruited bot's pending greeting");
        AllegiancePmcFriendship.RevokeGroup(friendly.BotsGroup,EBotEnemyCause.byKill);
        BotOwnerUpdateHub.Invoke(friendly);
        Check(AllegianceFriendlyGreeting.Calls==1,"Revoked friendship cannot greet");
        AllegiancePmcFriendship.Reset();
        Check(AllegianceFriendlyGreeting.Cancellations>=3,"Revocation and raid teardown cancel greeting state");
        Check(!BotOwnerUpdateHub.HasSubscribers,"Raid teardown unregisters greeting callback");
        AllegiancePmcFriendship.Apply(friendly); BotOwnerUpdateHub.Invoke(friendly);
        Check(AllegianceFriendlyGreeting.Calls==1,"New raid rejection cannot reuse previous friendly state");
        AllegiancePmcFriendship.Reset();
    }
    private static void RecruitmentOfferChecks()
    {
        foreach(int difference in new[]{-10,-2,0,1,2,3,10})
        foreach(float roll in new[]{0f,1f})
        {
            Setup();var bot=Solo("offer-pmc");AllegiancePmcFriendship.Apply(bot);
            boss.realPlayer.Profile.Info.Level=20+difference;bot.Profile.Info.Level=20;
            float chance=Math.Max(0f,Math.Min(1f,(difference+10)/20f));int before=UnityEngine.Random.Rolls;
            UnityEngine.Random.NextValue=roll;
            bool resolved=AllegiancePmcFriendship.TryResolveRecruitmentAcceptance(bot,boss.realPlayer,true,out bool accepted);
            Check(resolved==(chance>=0.6f),"Proactive threshold uses recruitment acceptance, with exact 60% inclusion");
            Check(BossPlayers.Denied.Count==0,"Proactive answer never stores a spoken refusal");
            if(chance<0.6f)Check(UnityEngine.Random.Rolls==before,"Below 60% does not proactively roll");
            else Check(accepted==(chance>=1f || roll<chance),"Proactive PMC result uses the existing level curve");
            Check(AllegiancePmcFriendship.TryResolveRecruitmentAcceptance(bot,boss.realPlayer,false,out accepted),"Actual player request resolves the same selected candidate");
            bool expected=chance>=1f || (chance>0f && roll<chance);
            Check(accepted==expected,"Player request consumes/reuses the proper acceptance answer");
            int after=UnityEngine.Random.Rolls;UnityEngine.Random.NextValue=1f-roll;boss.realPlayer.Profile.Info.Level=100;
            Check(AllegiancePmcFriendship.TryResolveRecruitmentAcceptance(bot,boss.realPlayer,false,out bool repeated) && repeated==expected && UnityEngine.Random.Rolls==after,
                "Saved answer survives repeating requests and later level/chance changes");
            if(!accepted)
            {
                AllegiancePmcFriendship.TryGetInteraction(bot,out var interaction,out _);interaction.Waiting=true;
                AllegiancePmcFriendship.NoteRequestedRefusal(bot);
                Check(BossPlayers.HasDeniedRecruitment(bot.ProfileId) && interaction.Refusing && interaction.Waiting,"Actual bot NO records denial and enters its brief refusal pause");
            }
        }
        foreach(float roll in new[]{0f,1f})
        {
            Setup();GameplayModeRuntime.IsAllegiance=true;boss.realPlayer.Profile.FenceInfo.Standing=1;pitTeam.pitFireTeam.friendlyEncounterChance.Value=15;
            var bot=Scav("offer-scav");AllegiancePmcFriendship.Apply(bot);UnityEngine.Random.NextValue=roll;
            Check(AllegiancePmcFriendship.TryResolveRecruitmentAcceptance(bot,boss.realPlayer,true,out bool accepted) && accepted,
                "Low-slider Scav's 100% recruitment probability permits proactive acceptance");
            int before=UnityEngine.Random.Rolls;
            Check(AllegiancePmcFriendship.AcceptScavRecruitment(bot,boss.realPlayer) && UnityEngine.Random.Rolls==before,"Scav request reuses its proactive answer");
        }
        Setup();var selected=Solo("offer-no-tier");AllegiancePmcFriendship.Apply(selected);pitTeam.pitFireTeam.tieredPickup.Value=false;
        Check(AllegiancePmcFriendship.TryResolveRecruitmentAcceptance(selected,boss.realPlayer,true,out bool noTier) && noTier,"Disabled tiering preserves guaranteed acceptance");
        Setup();selected=Solo("reply-target");AllegiancePmcFriendship.Apply(selected);
        AllegiancePmcFriendship.TryGetInteraction(selected,out var pending,out _);pending.Waiting=true;FriendlyEncounterInteraction.InvitingBot=selected;
        Check(!AllegiancePmcFriendship.HandleEncounterPhrase(new Player{ProfileId=boss.realPlayer.ProfileId},EPhraseTrigger.Roger),"A foreign player object cannot answer the human leader's invitation");
        Check(AllegiancePmcFriendship.HandleEncounterGesture(boss.realPlayer,EInteraction.NoGesture) && !pending.Waiting && BossPlayers.Denied.Count==0,
            "Negative gesture routes to one inviter without creating bot denial");
        pending.Waiting=true;FriendlyEncounterInteraction.InvitingBot=selected;
        Check(AllegiancePmcFriendship.HandleEncounterPhrase(boss.realPlayer,EPhraseTrigger.Roger) && selected.BotsGroup.RequestsController.Calls==1,"Roger routes recruitment to the inviter");
        pending.Waiting=true;FriendlyEncounterInteraction.InvitingBot=selected;
        Check(AllegiancePmcFriendship.HandleEncounterGesture(boss.realPlayer,EInteraction.OkGesture) && selected.BotsGroup.RequestsController.Calls==2,"OK gesture routes recruitment to the inviter");
        pending.Waiting=true;selected.Position=new Vector3{x=12};boss.realPlayer.InteractionRay=new Ray{direction=new Vector3{x=1}};
        Check(AllegiancePmcFriendship.HandleEncounterPhrase(boss.realPlayer,EPhraseTrigger.Cooperation) && selected.BotsGroup.RequestsController.Calls==3,
            "Directed Cooperate reaches the active interaction bot from 12m");
        Check(!AllegiancePmcFriendship.HandleEncounterPhrase(boss.realPlayer,EPhraseTrigger.Cooperation),"Inactive interaction falls back to the existing manual recruitment receiver");
    }
    private static void RealPopulationAdmissionChecks() {
        Setup();GameplayModeRuntime.IsAllegiance=true;pitTeam.pitFireTeam.friendlyEncounterChance.Value=55;boss.realPlayer.Profile.FenceInfo.Standing=6;
        FriendlyEncounterPopulation.UseReal=true;var world=Comfort.Common.Singleton<GameWorld>.Instance;
        var bots=new List<BotOwner>();for(int i=0;i<10;i++)bots.Add(Scav("actual-cap-"+i));world.AllAlivePlayersList.AddRange(bots);
        foreach(var bot in bots)AllegiancePmcFriendship.Apply(bot);
        Check(bots.Count(bot=>AllegiancePmcFriendship.IsSelectedFriendly(bot,boss.realPlayer))==5,
            "real selection plus real population scanner admits five of ten successful 55% Scav rolls");
        Check(UnityEngine.Random.Rolls==10,"real population cap preserves one roll per eligible spawn");
        bots[8].IsDead=bots[9].IsDead=true;
        var late=Scav("actual-cap-late");world.AllAlivePlayersList.Add(late);AllegiancePmcFriendship.Apply(late);
        Check(!AllegiancePmcFriendship.IsSelectedFriendly(late,boss.realPlayer),"real late spawn is blocked after hostile deaths leave five friendlies above capacity");
        Check(bots.Take(5).All(bot=>AllegiancePmcFriendship.IsSelectedFriendly(bot,boss.realPlayer)),"real population shrink leaves established friendships intact");
        Setup();GameplayModeRuntime.IsAllegiance=true;pitTeam.pitFireTeam.friendlyEncounterChance.Value=15;FriendlyEncounterPopulation.UseReal=true;
        world=Comfort.Common.Singleton<GameWorld>.Instance;bots=new List<BotOwner>();
        for(int i=0;i<10;i++)bots.Add(Solo("actual-pmc-"+i,i%2==0?EPlayerSide.Usec:EPlayerSide.Bear));world.AllAlivePlayersList.AddRange(bots);
        foreach(var bot in bots)AllegiancePmcFriendship.Apply(bot);
        Check(bots.Count(bot=>AllegiancePmcFriendship.IsSelectedFriendly(bot,boss.realPlayer))==2,"real solo PMC selection shares two minimum slots across USEC/BEAR");
        Setup();RecruitScavCompanion();pitTeam.pitFireTeam.friendlyEncounterChance.Value=30;boss.realPlayer.Profile.FenceInfo.Standing=6;
        FriendlyEncounterPopulation.UseReal=true;world=Comfort.Common.Singleton<GameWorld>.Instance;bots=new List<BotOwner>();
        for(int i=0;i<10;i++)bots.Add(Scav("actual-calming-"+i));world.AllAlivePlayersList.AddRange(bots);
        AllegiancePmcFriendship.Apply(bots[0]);AllegiancePmcFriendship.Apply(bots[1]);
        bots[3].BotsGroup=bots[2].BotsGroup;bots[2].BotsGroup.Members.Add(bots[3]);
        AllegianceScavRelations.Apply(bots[2]);
        Check(AllegianceScavRelations.IsCalmedGroup(bots[2].BotsGroup),"Scav companion calms the whole group independently of ordinary encounter capacity");
        AllegianceScavRelations.Apply(bots[4]);AllegiancePmcFriendship.Apply(bots[5]);
        Check(AllegianceScavRelations.IsCalmedGroup(bots[4].BotsGroup)&&AllegiancePmcFriendship.IsSelectedFriendly(bots[5],boss.realPlayer),
            "companion neutrality does not consume ordinary encounter slots");
        AllegiancePmcFriendship.Apply(bots[6]);
        Check(!AllegiancePmcFriendship.IsSelectedFriendly(bots[6],boss.realPlayer),"ordinary encounter selection still respects its cap alongside a Scav companion");
        Setup();RecruitScavCompanion();pitTeam.pitFireTeam.friendlyEncounterChance.Value=15;boss.realPlayer.Profile.FenceInfo.Standing=6;
        FriendlyEncounterPopulation.UseReal=true;world=Comfort.Common.Singleton<GameWorld>.Instance;bots=new List<BotOwner>();
        for(int i=0;i<12;i++)bots.Add(Scav("reported-calming-"+i));world.AllAlivePlayersList.AddRange(bots);
        AllegiancePmcFriendship.Apply(bots[0]);
        foreach(var bot in bots)AllegianceScavRelations.Apply(bot);
        Check(bots.All(bot=>AllegianceScavRelations.IsCalmedGroup(bot.BotsGroup)),"reported one-slot/twelve-Scav case retains companion neutrality for every passing group roll");
        Check(bots.Skip(1).All(bot=>!AllegiancePmcFriendship.IsSelectedFriendly(bot,boss.realPlayer)),"companion neutrality does not grant independent recruitable encounter status");
        Check(!FriendlyEncounterPopulation.CanAdmit(bots[1],out int live,out int selected,out int capacity)&&live==12&&selected==1&&capacity==1,
            "reported case keeps one selected encounter slot while companion neutrality remains independent");
    }
    private static void PopulationAdmissionChecks() {
        Setup();GameplayModeRuntime.IsAllegiance=true;boss.realPlayer.Profile.FenceInfo.Standing=6;
        var denied=Scav("quota-denied");denied.BotsGroup.Enemies[boss.realPlayer]=new();
        FriendlyEncounterPopulation.Allowed=false;AllegiancePmcFriendship.Apply(denied);
        Check(!AllegiancePmcFriendship.IsSelectedFriendly(denied,boss.realPlayer) && denied.BotsGroup.IsEnemy(boss.realPlayer),
            "passing roll cannot neutralize an encounter when population admission fails");
        Check(UnityEngine.Random.Rolls==1&&FriendlyEncounterPopulation.Queries==1,"population veto is checked after one successful chance roll");
        FriendlyEncounterPopulation.Allowed=true;AllegiancePmcFriendship.Apply(denied);
        Check(!AllegiancePmcFriendship.IsSelectedFriendly(denied,boss.realPlayer)&&UnityEngine.Random.Rolls==1&&FriendlyEncounterPopulation.Queries==1,
            "later capacity does not reroll or reconsider a previously quota-rejected spawn");
        var retained=Scav("quota-grandfathered");AllegiancePmcFriendship.Apply(retained);
        FriendlyEncounterPopulation.Allowed=false;AllegiancePmcFriendship.Apply(retained);
        Check(AllegiancePmcFriendship.IsSelectedFriendly(retained,boss.realPlayer),"population shrink never revokes an existing selected friendship");
        Setup();GameplayModeRuntime.IsAllegiance=true;boss.realPlayer.Profile.FenceInfo.Standing=6;UnityEngine.Random.NextValue=1;
        AllegiancePmcFriendship.Apply(Scav("roll-failed-before-quota"));
        Check(FriendlyEncounterPopulation.Queries==0,"capacity cannot turn a failed roll into guaranteed friendship");
        Setup();RecruitScavCompanion();var blocked=Scav("calming-cap-denied");
        int calmingQuotaQueries=FriendlyEncounterPopulation.Queries;
        FriendlyEncounterPopulation.Allowed=false;AllegianceScavRelations.Apply(blocked);
        Check(AllegianceScavRelations.IsCalmedGroup(blocked.BotsGroup)&&FriendlyEncounterPopulation.Queries==calmingQuotaQueries,"companion calming bypasses ordinary encounter quota entirely");
        int calmRolls=UnityEngine.Random.Rolls;
        FriendlyEncounterPopulation.Allowed=true;AllegianceScavRelations.Apply(blocked);
        Check(AllegianceScavRelations.IsCalmedGroup(blocked.BotsGroup)&&UnityEngine.Random.Rolls==calmRolls,"quota changes do not reroll companion neutrality");
        var calm=Scav("calming-grandfathered");AllegianceScavRelations.Apply(calm);
        Check(!calm.BotsGroup.Locked,"companion calming does not take ownership of native group reuse");
        FriendlyEncounterPopulation.Allowed=false;AllegianceScavRelations.Apply(calm);
        Check(AllegianceScavRelations.IsCalmedGroup(calm.BotsGroup),"population shrink preserves already-calmed Scav groups");
        Setup();RecruitScavCompanion();var pending=Scav("calming-group-pending");pending.BotsGroup.TargetMembersCount=2;
        int oldRolls=UnityEngine.Random.Rolls;AllegianceScavRelations.Apply(pending);
        Check(UnityEngine.Random.Rolls==oldRolls+1&&AllegianceScavRelations.IsCalmedGroup(pending.BotsGroup),"companion neutrality need not wait for a whole group quota reservation");
        var partner=Scav("calming-group-partner");partner.BotsGroup=pending.BotsGroup;pending.BotsGroup.Members.Add(partner);
        AllegianceScavRelations.Apply(partner);
        Check(UnityEngine.Random.Rolls==oldRolls+1&&!pending.BotsGroup.Locked&&AllegianceScavRelations.IsCalmedGroup(pending.BotsGroup),"later native group members share its existing neutrality roll without a new quota decision");
        Setup();RecruitScavCompanion();FriendlyEncounterPopulation.Allowed=false;
        UnityEngine.Random.NextValue=0.76f;var fails=Scav("calming-roll-76");AllegianceScavRelations.Apply(fails);
        UnityEngine.Random.NextValue=0.74f;var passes=Scav("calming-roll-74");AllegianceScavRelations.Apply(passes);
        Check(!AllegianceScavRelations.IsCalmedGroup(fails.BotsGroup)&&AllegianceScavRelations.IsCalmedGroup(passes.BotsGroup),"independent companion calming still obeys its 75 percent roll");
    }
    private static void RogueScavEncounterChecks() {
        foreach(var role in new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye})
        foreach(var side in new[]{EPlayerSide.Savage,EPlayerSide.Usec,EPlayerSide.Bear}) {
            Setup();GameplayModeRuntime.IsAllegiance=true;GameplayModeAddons.IsRogueRaid=true;
            first.Profile.Info.Settings.Role=role;first.Profile.Info.Side=side;
            var hostile=Scav("goon-not-scav-"+role+side);hostile.BotsGroup.Enemies[boss.realPlayer]=new();
            AllegianceScavRelations.OnFollowerAdded(first,boss,false);
            int goonRolls=UnityEngine.Random.Rolls;UnityEngine.Random.NextValue=0;
            AllegianceScavRelations.Apply(hostile);
            Check(!AllegiancePmcFriendship.IsRegularScav(first) && !AllegianceScavRelations.KeepSeparateGroup(first,boss.realPlayer,false),
                "Goon cannot masquerade as recruited ordinary Scav regardless of presentation side: "+role+"/"+side);
            Check(UnityEngine.Random.Rolls==goonRolls && !AllegianceScavRelations.IsCalmedGroup(hostile.BotsGroup) && hostile.BotsGroup.IsEnemy(boss.realPlayer),
                "Goon cannot enable 75% companion calming or remove hostile Scav aggression: "+role+"/"+side);
        }
        Setup();GameplayModeRuntime.IsAllegiance=true;GameplayModeAddons.IsRogueRaid=true;
        first.Profile.Info.Settings.Role=WildSpawnType.bossKnight;second.Profile.Info.Settings.Role=WildSpawnType.followerBigPipe;
        boss.realPlayer.Profile.FenceInfo.Standing=6;pitTeam.pitFireTeam.friendlyEncounterChance.Value=80;
        foreach(var side in new[]{EPlayerSide.Usec,EPlayerSide.Bear}) {
            var pmc=Solo("rogue-pmc-"+side,side);AllegiancePmcFriendship.Apply(pmc);
            Check(!AllegiancePmcFriendship.IsSelectedFriendly(pmc,boss.realPlayer) && !AllegiancePmcFriendship.CanRecruit(pmc,boss.realPlayer),"Goons still exclude PMC encounters: "+side);
        }
        Check(UnityEngine.Random.Rolls==0,"Excluded PMC encounters do not consume friendship rolls");
        var scav=Scav("rogue-scav");pitTeam.pitFireTeam.friendlyScavEncounters.Value=false;AllegiancePmcFriendship.Apply(scav);
        Check(UnityEngine.Random.Rolls==0 && !AllegiancePmcFriendship.IsSelectedFriendly(scav,boss.realPlayer),"Goons respect the disabled friendly Scav setting");
        pitTeam.pitFireTeam.friendlyScavEncounters.Value=true;
        scav.BotsGroup.Enemies[boss.realPlayer]=new BotGroupEnemyInfo();scav.BotsGroup.Enemies[first]=new BotGroupEnemyInfo();
        boss.bossGroup.Enemies[scav]=new BotGroupEnemyInfo();first.Memory.GoalEnemy=new EnemyInfo{ProfileId=scav.ProfileId};
        AllegiancePmcFriendship.Apply(scav);AllegiancePmcFriendship.Apply(scav);
        Check(UnityEngine.Random.Rolls==1 && AllegiancePmcFriendship.IsSelectedFriendly(scav,boss.realPlayer),"Goons allow one sticky native Scav encounter roll");
        Check(scav.BotsGroup.Neutrals.ContainsKey(boss.realPlayer) && scav.BotsGroup.Neutrals.ContainsKey(first) && scav.BotsGroup.Neutrals.ContainsKey(second) && boss.bossGroup.Neutrals.ContainsKey(scav),"Selected Scav is neutral to the human, Knight and Big Pipe in both directions");
        Check(first.Memory.GoalEnemy==null && !scav.BotsGroup.IsEnemy(first) && !boss.bossGroup.IsEnemy(scav),"Selected Scav repairs pre-existing Goon setup hostility");
        Check(!scav.BotsGroup.AddEnemy(first,EBotEnemyCause.initial) && !boss.bossGroup.AddEnemy(scav,EBotEnemyCause.initial),"Ambient Goon/Scav scans preserve selected friendship");
        Check(AllegianceScavRelations.KeepSeparateGroup(scav,boss.realPlayer,false),"Selected Scav keeps native group diplomacy when recruited alongside Goons");
        RecruitScavCompanion("rogue-scav-companion");
        var calmed=Scav("rogue-calmed-group");AllegianceScavRelations.Apply(calmed);
        Check(AllegianceScavRelations.IsCalmedGroup(calmed.BotsGroup),"Recruited Scav diplomacy works while Goons are present");
        FollowerGroupHostility.OnDamage(first,scav);AllegiancePmcFriendship.Apply(scav);
        Check(!AllegiancePmcFriendship.IsSelectedFriendly(scav,boss.realPlayer) && scav.BotsGroup.IsEnemy(boss.realPlayer) && boss.bossGroup.IsEnemy(scav),"Actual attack on Knight revokes Scav friendship for the squad");
        Setup();GameplayModeRuntime.IsAllegiance=true;GameplayModeAddons.IsRogueRaid=true;boss.realPlayer.Profile.FenceInfo.Standing=0;
        var noStanding=Scav("rogue-zero-fence");AllegiancePmcFriendship.Apply(noStanding);
        Check(!AllegiancePmcFriendship.IsSelectedFriendly(noStanding,boss.realPlayer),"Goons preserve zero-Fence encounter probability");
        var grouped=Scav("rogue-grouped");grouped.BotsGroup.TargetMembersCount=2;int rolls=UnityEngine.Random.Rolls;AllegiancePmcFriendship.Apply(grouped);
        Check(UnityEngine.Random.Rolls==rolls && !AllegiancePmcFriendship.CanRecruit(grouped,boss.realPlayer),"Goons preserve grouped-Scav exclusion");
    }
    public static void Main() {
        Setup();GameplayModeRuntime.IsAllegiance=true;GameplayModeAddons.IsRogueRaid=true;
        Check(AllegiancePmcFriendship.IsEnabled,"Rogue companions retain the Scav encounter service");
        var rolePolicy=typeof(BotGroupAddEnemyPatch).GetMethod("IsRogueFriendlyType",BindingFlags.NonPublic|BindingFlags.Static);
        Check((bool)rolePolicy.Invoke(null,new object[]{WildSpawnType.exUsec}),"Rogue raid protects exact exUsec role");
        Check(!(bool)rolePolicy.Invoke(null,new object[]{WildSpawnType.bossKnight}),"Rogue raid does not exempt unrelated wild Goons by role");
        var h=new Harmony("pit.tests.hostility");
        h.Patch(AccessTools.Method(typeof(Player),nameof(Player.ApplyDamageInfo)),new HarmonyMethod(typeof(AllegianceScavTrustDamagePatch),"PatchPrefix"));
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
        RogueScavEncounterChecks();
        PopulationAdmissionChecks();
        RealPopulationAdmissionChecks();
        ScavEncounterChecks();
        EncounterChanceChecks();
        ScavEncounterToggleChecks();
        FriendlyContactExclusionChecks();
        ScavAcceptanceChecks();
        ScavAcceptanceSettingOnlyChecks();
        ScavDiplomacyChecks();
        ScavProfileIdentityChecks();
        ScavReciprocalHostilityChecks();
        ScavCompanionLossChecks();
        ScavTrustDamageChecks();
        ScavIntentChecks();
        FriendshipChanceChecks();
        ConfiguredChanceChecks();
        GunsForHireEncounterChecks();
        CrossFactionChecks();
        RecruitedFriendshipChecks();
        BossDamageChecks();
        GreetingLifecycleChecks();
        RecruitmentOfferChecks();
        h.UnpatchSelf();Console.WriteLine("Passed "+checks+" production hostility checks (including Allegiance selection).");
    }
}

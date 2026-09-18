using HarmonyLib;
using pitTeam.SAINAddon;
using SAIN.SAINComponent.Classes.Info;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using SAIN.BotController.Classes;
using SAIN.Components;
using UnityEngine;

namespace UnityEngine {
    public struct Vector3 {
        public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static float Distance(Vector3 a,Vector3 b) => (float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
    }
}
namespace EFT {
    public class Health { public bool IsAlive=true; }
    public class Player { public string ProfileId; public Health HealthController=new(); public Vector3 Position; }
    public class FollowerLink { public object BossToFollow; }
    public class Memory { public object GoalEnemy; }
    public class BotOwner {
        public string ProfileId {get;set;} public bool IsDead; public Vector3 Position;
        public Memory Memory=new(); public FollowerLink BotFollower=new(); public BotsGroup BotsGroup;
    }
}
public class BotsGroup {
    public List<BotOwner> Members=new(); public int MembersCount=>Members.Count;
    public BotOwner Member(int index)=>Members[index];
}
namespace pitTeam.Components {
    __TACTIC_ENUM__
    public class BotFollowerPlayer { public static bool IsEnemyInfoAlive(object e)=>e!=null; __TACTIC_PARSER__ }
    public class pitAIBossPlayer { public Player realPlayer=new(); public BotsGroup bossGroup=new(); public List<BotOwner> Followers=new(); }
}
namespace pitTeam {
    public static class pitFireTeam {
        public static bool IsSAINInstalled=true, IsSAINAddonInstalled=true;
        __COMBAT_GATE__
    }
    namespace Utils { public static class FollowerMedical { public static void BeginPostCombatFullHeal(BotOwner b){} public static bool IsUsingMedical(BotOwner b)=>false; public static void CompletePostCombatFullHeal(BotOwner b){} } }
}
namespace pitTeam.Modules {
    public class Follower { public BotOwner Bot; public FollowerCombatTactic CombatTactic; public BotOwner GetBot()=>Bot; __CORE_TACTIC__ }
    public class BossPlayers {
        public static BossPlayers Instance=new();
        public Follower GetFollower(BotOwner bot)=>Followers.FirstOrDefault(f=>f.Bot==bot);
        public static List<Follower> Followers=new(); public static List<Follower> GetFollowers()=>Followers;
        public static bool IsFollower(BotOwner owner)=>owner?.BotFollower.BossToFollow is pitAIBossPlayer && Followers.Any(f=>f.Bot==owner);
    }
    public static class Logger {
        public static List<string> Errors=new(); public static void LogInfo(string value){}
        public static void LogError(string value){Errors.Add(value);} public static void LogError(Exception ex){Errors.Add(ex.ToString());}
    }
}
namespace SAIN {
    public static class SAINEnableClass {
        public static Dictionary<string,BotComponent> Bots=new();
        public static bool GetSAIN(string id,out BotComponent result)=>Bots.TryGetValue(id,out result);
    }
}
namespace SAIN.Components {
    public class BotManagerComponent {
        public static BotManagerComponent Instance {get;set;}=new();
        public BotSquads BotSquads {get;private set;}=new();
    }
    public class BotComponent {
        public BotOwner BotOwner {get;} public int DecisionSubscriptions; public bool IsBoss;
        public BotSquadContainer Squad {get;private set;}
        public BotComponent(BotOwner owner) {BotOwner=owner;Squad=new(this);Squad.SquadInfo.AddMember(this);}
    }
}
namespace SAIN.SAINComponent.Classes.Info {
    public class BotSquadContainer {
        public BotOwner BotOwner {get;} public Squad SquadInfo {get;private set;}
        public BotSquadContainer(BotComponent bot){BotOwner=bot.BotOwner;SquadInfo=BotManagerComponent.Instance.BotSquads.GetSquad(BotOwner);}
        public bool IAmLeader=>SquadInfo.LeaderId==BotOwner.ProfileId;
        public BotComponent LeaderComponent=>SquadInfo?.LeaderComponent;
        public float DistanceToSquadLeader {[MethodImpl(MethodImplOptions.NoInlining)]get=>-1;}
        public bool BotInGroup {[MethodImpl(MethodImplOptions.NoInlining)]get=>BotOwner.BotsGroup.MembersCount>1;}
        public void RemoveFromSquad(){SquadInfo=null;SquadInfo=BotManagerComponent.Instance.BotSquads.GetSquad(BotOwner);}
    }
}
namespace SAIN.BotController.Classes {
    public class Squad {
        public event Action<Squad> OnSquadEmpty;
        public Dictionary<string,BotComponent> Members {get;}=new();
        public string GUID {get;}=Guid.NewGuid().ToString();
        public string LeaderId {[MethodImpl(MethodImplOptions.NoInlining)]get=>_leaderId;}
        private string _leaderId;
        public BotComponent LeaderComponent {get;private set;}
        public bool LeaderIsDeadorNull {[MethodImpl(MethodImplOptions.NoInlining)]get=>LeaderComponent==null||LeaderComponent.BotOwner.IsDead;}
        public int Elections,Assignments,Disposals;
        [MethodImpl(MethodImplOptions.NoInlining)]private void findSquadLeader(){Elections++;if(Members.Count>0)assignSquadLeader(Members.Values.First());}
        [MethodImpl(MethodImplOptions.NoInlining)]private void assignSquadLeader(BotComponent sain){Assignments++;LeaderComponent=sain;_leaderId=sain.BotOwner.ProfileId;}
        public void Tick()=>findSquadLeader();
        public void AddMember(BotComponent bot){if(Members.ContainsKey(bot.BotOwner.ProfileId))return;Members.Add(bot.BotOwner.ProfileId,bot);bot.DecisionSubscriptions++;if(bot.IsBoss)assignSquadLeader(bot);}
        public void RemoveMember(string id){if(Members.TryGetValue(id,out var bot)){Members.Remove(id);bot.DecisionSubscriptions--;}if(Members.Count==0)OnSquadEmpty?.Invoke(this);}
        [MethodImpl(MethodImplOptions.NoInlining)]public void Dispose(){Disposals++;foreach(var id in Members.Keys)RemoveMember(id);}
    }
    public class BotSquads {
        public Dictionary<string,Squad> Squads {get;}=new(); public HashSet<Squad> SquadArray {get;}=new();
        private HashSet<Squad> pending=new();
        [MethodImpl(MethodImplOptions.NoInlining)]public Squad GetSquad(BotOwner botOwner){
            foreach(var member in botOwner.BotsGroup.Members){
                if(member!=botOwner&&SAIN.SAINEnableClass.GetSAIN(member.ProfileId,out var other)&&other.Squad?.SquadInfo!=null)return other.Squad.SquadInfo;
            }
            var result=new Squad();result.OnSquadEmpty+=RemoveSquad;Squads.Add(result.GUID,result);SquadArray.Add(result);return result;
        }
        private void RemoveSquad(Squad squad){squad.OnSquadEmpty-=RemoveSquad;squad.Dispose();pending.Add(squad);}
        public void Flush(){foreach(var s in pending){Squads.Remove(s.GUID);SquadArray.Remove(s);}pending.Clear();}
    }
}
public static class SelectionChecks {
    __DEFAULT_AGGRESSION__
    __SERVER_NORMALIZER__
    __UI_AVAILABILITY__
}
public static class LeadershipChecks {
    private static int checks,serial;
    private static void Check(bool value,string name){checks++;if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
    private static BotComponent Spawn(BotsGroup group=null){
        var owner=new BotOwner{ProfileId="bot"+(++serial),BotsGroup=group??new()};owner.BotsGroup.Members.Add(owner);
        var bot=new BotComponent(owner);SAIN.SAINEnableClass.Bots.Add(owner.ProfileId,bot);return bot;
    }
    private static pitAIBossPlayer Boss(string id)=>new(){realPlayer=new(){ProfileId=id,Position=new Vector3(150,0,0)}};
    private static void Recruit(BotComponent bot,pitAIBossPlayer boss,bool nativeTransfer=false,FollowerCombatTactic tactic=FollowerCombatTactic.SainMan){
        bot.BotOwner.BotsGroup.Members.Remove(bot.BotOwner);bot.BotOwner.BotsGroup=boss.bossGroup;boss.bossGroup.Members.Add(bot.BotOwner);
        bot.BotOwner.BotFollower.BossToFollow=boss;boss.Followers.Add(bot.BotOwner);BossPlayers.Followers.Add(new(){Bot=bot.BotOwner,CombatTactic=tactic});
        if(nativeTransfer){var previous=bot.Squad.SquadInfo;bot.Squad.RemoveFromSquad();previous.RemoveMember(bot.BotOwner.ProfileId);}
        SainAddonBridge.RaiseFollowerLifecycleEvent(bot.BotOwner,FollowerLifecycleEvent.OnRecruited);
    }
    private static void Dismiss(BotComponent bot,pitAIBossPlayer boss){
        boss.Followers.Remove(bot.BotOwner);boss.bossGroup.Members.Remove(bot.BotOwner);bot.BotOwner.BotFollower.BossToFollow=null;
        bot.BotOwner.BotsGroup=new();bot.BotOwner.BotsGroup.Members.Add(bot.BotOwner);BossPlayers.Followers.RemoveAll(f=>f.Bot==bot.BotOwner);
        SainAddonBridge.RaiseFollowerLifecycleEvent(bot.BotOwner,FollowerLifecycleEvent.OnDismiss);
    }
    public static int Main(){try{
        pitTeam.Patches.FollowerSainSquadLeaderPatch.Apply(new Harmony("pitTeam.core.leader.test"));
        SAINAddonPatches.Apply();
        Check(SainPlayerSquadBridge.Enable(),"adapter initialized");
        Check(SainAddonBridge.HasSquadProvider,"core sees passive leadership registration");
        Check(!pitTeam.pitFireTeam.IsSainFollowerCombatAvailable&&!SainAddonBridge.HasRuntimeCallbacks,"leadership does not enable combat");
        Check(BotFollowerPlayer.ParseCombatTactic(" sainMAN ")==FollowerCombatTactic.SainMan&&SelectionChecks.NormalizeCombatTactic(" sainMAN ")=="SainMan","SainMan survives server normalization and client parsing");
        Check(!SelectionChecks.IsUnavailableTactic("SainMan")&&!SelectionChecks.IsUnavailableTactic("Rifleman")&&SelectionChecks.IsUnavailableTactic("Protector"),"profile offers SainMan with both plugins installed");
        Check(BotFollowerPlayer.ParseCombatTactic(" sainShooter ")==FollowerCombatTactic.SAINShooter && SelectionChecks.NormalizeCombatTactic(" sainShooter ")=="SAINShooter",
            "Shooter identity survives normalization and client parsing");
        Check(!SelectionChecks.IsUnavailableTactic("SAINShooter") && SelectionChecks.GetDefaultAggressionForTactic("SAINShooter")==30f,
            "Shooter is selectable with both plugins and defaults to Marksman aggression");
        Check(new Follower{CombatTactic=FollowerCombatTactic.SAINShooter}.CoreCombatTactic==FollowerCombatTactic.Marksman,
            "Shooter falls back to Core Marksman without losing saved tactic");
        pitTeam.pitFireTeam.IsSAINAddonInstalled=false;
        Check(SelectionChecks.IsUnavailableTactic("SainMan")&&SelectionChecks.NormalizeCombatTactic("SainMan")=="SainMan","missing addon hides option without erasing saved selection");
        Check(SelectionChecks.IsUnavailableTactic("SAINShooter") && SelectionChecks.NormalizeCombatTactic("SAINShooter")=="SAINShooter",
            "missing addon hides Shooter without changing its saved identity");
        pitTeam.pitFireTeam.IsSAINAddonInstalled=true;pitTeam.pitFireTeam.IsSAINInstalled=false;
        Check(SelectionChecks.IsUnavailableTactic("SainMan"),"missing SAIN hides option");
        Check(SelectionChecks.IsUnavailableTactic("SAINShooter"),"missing SAIN hides Shooter");
        pitTeam.pitFireTeam.IsSAINInstalled=true;
        var fallback=new Follower{CombatTactic=FollowerCombatTactic.SainMan};
        Check(fallback.CoreCombatTactic==FollowerCombatTactic.Balanced&&fallback.CombatTactic==FollowerCombatTactic.SainMan,"SainMan keeps saved identity for unavailable-addon Rifleman fallback");
        var shooterBoss=Boss("shooterRole");var shooterMember=Spawn();Recruit(shooterMember,shooterBoss,tactic:FollowerCombatTactic.SAINShooter);
        Check(SainPlayerSquadBridge.TryGetPlayerLeader(shooterMember.BotOwner,out var shooterLeader) && ReferenceEquals(shooterLeader,shooterBoss.realPlayer),
            "Shooter binds to the real player-led native SAIN squad");
        BossPlayers.Instance.GetFollower(shooterMember.BotOwner).CombatTactic=FollowerCombatTactic.Marksman;
        SainAddonBridge.RaiseBossGroupStaticUpdate(shooterBoss);
        Check(!SainPlayerSquadBridge.TryGetPlayerLeader(shooterMember.BotOwner,out _),"Shooter opt-out releases addon squad binding");
        var selectionBoss=Boss("selection");var rifleman=Spawn();Recruit(rifleman,selectionBoss,tactic:FollowerCombatTactic.Balanced);
        var marksman=Spawn();Recruit(marksman,selectionBoss,tactic:FollowerCombatTactic.Marksman);
        SainAddonBridge.RaiseBossGroupStaticUpdate(selectionBoss);
        Check(!SainPlayerSquadBridge.TryGetPlayerLeader(rifleman.BotOwner,out _)&&!SainPlayerSquadBridge.TryGetPlayerLeader(marksman.BotOwner,out _),"installing addon does not bind Rifleman or Marksman");
        var selected=Spawn();Recruit(selected,selectionBoss);
        Check(SainPlayerSquadBridge.TryGetPlayerLeader(selected.BotOwner,out _)&&selected.Squad.SquadInfo!=rifleman.Squad.SquadInfo&&selected.Squad.SquadInfo.Members.Count==1,"mixed squad binds only selected SainMan");
        Func<BotOwner,bool> ready=owner=>true;Action<BotOwner> release=owner=>{};int resets=0;Func<BotOwner,bool> reset=owner=>{resets++;return true;};
        SainAddonBridge.RegisterRuntimeCallbacks(ready,release,reset,ready);
        Check(pitTeam.pitFireTeam.UseSainFollowerCombat(selected.BotOwner)&&!pitTeam.pitFireTeam.UseSainFollowerCombat(rifleman.BotOwner)&&!pitTeam.pitFireTeam.UseSainFollowerCombat(marksman.BotOwner),"ready combat callbacks can own only SainMan followers");
        Check(pitTeam.pitFireTeam.ShouldDisableSainForFollower(rifleman.BotOwner)&&!pitTeam.pitFireTeam.ShouldDisableSainForFollower(selected.BotOwner)&&!SainAddonBridge.TryResetDecisionState(rifleman.BotOwner)&&SainAddonBridge.TryResetDecisionState(selected.BotOwner)&&resets==1,"combat suppression and callbacks stay follower-specific");
        SainAddonBridge.UnregisterRuntimeCallbacks(ready,release,reset,ready);
        Check(!pitTeam.pitFireTeam.UseSainFollowerCombat(selected.BotOwner),"unregistering combat callbacks restores core fallback");
        BossPlayers.Instance.GetFollower(selected.BotOwner).CombatTactic=FollowerCombatTactic.Marksman;
        SainAddonBridge.RaiseBossGroupStaticUpdate(selectionBoss);
        Check(!SainPlayerSquadBridge.TryGetPlayerLeader(selected.BotOwner,out _)&&selected.DecisionSubscriptions==1,"changing away from SainMan releases membership once");
        BossPlayers.Instance.GetFollower(rifleman.BotOwner).CombatTactic=FollowerCombatTactic.SainMan;
        SainAddonBridge.RaiseBossGroupStaticUpdate(selectionBoss);
        Check(SainPlayerSquadBridge.TryGetPlayerLeader(rifleman.BotOwner,out _)&&rifleman.DecisionSubscriptions==1,"selecting SainMan opts existing follower into leadership");
        var boss=Boss("playerA");var ordinary=Spawn();ordinary.Squad.SquadInfo.Tick();var recruit=Spawn(ordinary.BotOwner.BotsGroup);
        var old=recruit.Squad.SquadInfo;var oldLeader=old.LeaderId;Recruit(recruit,boss);var squad=recruit.Squad.SquadInfo;
        Check(squad!=old&&!old.Members.ContainsKey(recruit.BotOwner.ProfileId)&&old.Members.Count==1,"recruit detached without moving previous squad");
        Check(old.LeaderId==oldLeader&&old.LeaderComponent==ordinary,"ordinary leader preserved");
        Check(squad.LeaderId=="playerA"&&!squad.LeaderIsDeadorNull&&squad.LeaderComponent==null,"real player identity and liveness without fake bot");
        Check(!recruit.Squad.IAmLeader&&recruit.Squad.BotInGroup&&recruit.Squad.DistanceToSquadLeader==150,"single follower recognizes distant player leader");
        Check(SainPlayerSquadBridge.TryGetPlayerLeader(recruit.BotOwner,out var player)&&ReferenceEquals(player,boss.realPlayer),"typed player leader exposed");
        squad.Tick();Check(squad.Elections==0&&squad.Assignments==0,"AI election suppressed only in player squad");
        ordinary.Squad.SquadInfo.Tick();Check(old.Elections==2,"ordinary election still runs");
        var recruitedLeader=Spawn();recruitedLeader.Squad.SquadInfo.Tick();var remaining=Spawn(recruitedLeader.BotOwner.BotsGroup);
        var formerSquad=recruitedLeader.Squad.SquadInfo;Recruit(recruitedLeader,Boss("leaderRecruit"));
        Check(formerSquad.LeaderComponent==remaining&&formerSquad.Members.Count==1,"recruiting native leader elects a remaining AI member");
        var transferredLeader=Spawn();transferredLeader.Squad.SquadInfo.Tick();var transferredPeer=Spawn(transferredLeader.BotOwner.BotsGroup);
        var transferredSquad=transferredLeader.Squad.SquadInfo;Recruit(transferredLeader,Boss("nativeTransfer"),true);
        Check(transferredSquad.LeaderComponent==transferredPeer&&transferredSquad.Members.Count==1&&transferredLeader.DecisionSubscriptions==1,"native group transfer before recruitment repairs only departed leader");
        var second=Spawn();second.IsBoss=true;Recruit(second,boss);
        Check(second.Squad.SquadInfo==squad&&squad.Members.Count==2&&squad.LeaderId=="playerA"&&squad.LeaderComponent==null,"boss type cannot replace player leader");
        for(int i=0;i<50;i++)SainAddonBridge.RaiseBossGroupStaticUpdate(boss);
        Check(recruit.DecisionSubscriptions==1&&second.DecisionSubscriptions==1&&squad.Members.Count==2,"idempotent membership and subscriptions");
        var bossB=Boss("playerB");var third=Spawn();Recruit(third,bossB);
        Check(third.Squad.SquadInfo!=squad&&third.Squad.SquadInfo.LeaderId=="playerB","independent player groups");
        boss.realPlayer.Position=new Vector3(200,0,0);Check(recruit.Squad.DistanceToSquadLeader==200,"live position tracks leader movement");
        boss.realPlayer.HealthController.IsAlive=false;squad.Tick();Check(squad.LeaderIsDeadorNull&&squad.LeaderId=="playerA"&&squad.Elections==0,"player death never promotes AI");
        boss.realPlayer.HealthController.IsAlive=true;
        // A native bot selection must not borrow the player-led squad even during an overlapping group transition.
        var accidental=Spawn(boss.bossGroup);Check(accidental.Squad.SquadInfo!=squad&&squad.Members.Count==2,"ordinary bot cannot borrow player squad");
        Dismiss(recruit,boss);recruit.Squad.SquadInfo.Tick();
        Check(!squad.Members.ContainsKey(recruit.BotOwner.ProfileId)&&recruit.Squad.LeaderComponent==recruit&&recruit.DecisionSubscriptions==1,"dismiss restores native membership and election");
        Check(!SainPlayerSquadBridge.TryGetPlayerLeader(recruit.BotOwner,out _),"dismiss clears typed leader");
        // SAIN initialization after recruitment: native GetSquad already routes to the player, and the boss tick publishes the binding.
        var lateOwner=new BotOwner{ProfileId="late",BotsGroup=boss.bossGroup};boss.bossGroup.Members.Add(lateOwner);lateOwner.BotFollower.BossToFollow=boss;
        boss.Followers.Add(lateOwner);BossPlayers.Followers.Add(new(){Bot=lateOwner,CombatTactic=FollowerCombatTactic.SainMan});SainAddonBridge.RaiseFollowerLifecycleEvent(lateOwner,FollowerLifecycleEvent.OnRecruited);
        var late=new BotComponent(lateOwner);SAIN.SAINEnableClass.Bots.Add(lateOwner.ProfileId,late);SainAddonBridge.RaiseBossGroupStaticUpdate(boss);
        Check(late.Squad.SquadInfo==squad&&late.DecisionSubscriptions==1&&SainPlayerSquadBridge.TryGetPlayerLeader(lateOwner,out _),"late component binds on leadership tick with combat disabled");
        // Reassign the same follower to a different player without moving its old peers.
        boss.Followers.Remove(second.BotOwner);boss.bossGroup.Members.Remove(second.BotOwner);second.BotOwner.BotFollower.BossToFollow=bossB;
        second.BotOwner.BotsGroup=bossB.bossGroup;bossB.bossGroup.Members.Add(second.BotOwner);bossB.Followers.Add(second.BotOwner);
        SainAddonBridge.RaiseBossGroupStaticUpdate(bossB);
        Check(second.Squad.SquadInfo==third.Squad.SquadInfo&&!squad.Members.ContainsKey(second.BotOwner.ProfileId)&&second.DecisionSubscriptions==1,"player-group replacement rebinds once");
        lateOwner.IsDead=true;SainAddonBridge.RaiseFollowerLifecycleEvent(lateOwner,FollowerLifecycleEvent.OnDismiss);
        Check(late.DecisionSubscriptions==0,"dead member removed without native rejoin");
        SainPlayerSquadBridge.Disable();BotManagerComponent.Instance.BotSquads.Flush();
        Check(!SainPlayerSquadBridge.IsEnabled&&!pitTeam.pitFireTeam.IsSainFollowerCombatAvailable&&!SainPlayerSquadBridge.TryGetPlayerLeader(third.BotOwner,out _),"addon teardown clears leadership without combat takeover");
        Check(third.DecisionSubscriptions==1&&second.DecisionSubscriptions==1,"live members restored on addon shutdown");
        Check(SainPlayerSquadBridge.Enable(),"adapter can reenable after clean shutdown");
        var nativeDeath=Spawn();Recruit(nativeDeath,bossB);nativeDeath.BotOwner.IsDead=true;
        nativeDeath.Squad.SquadInfo.RemoveMember(nativeDeath.BotOwner.ProfileId);
        Check(!SainPlayerSquadBridge.TryGetPlayerLeader(nativeDeath.BotOwner,out _)&&nativeDeath.DecisionSubscriptions==0,"native death clears binding without dismiss callback");
        var pendingOwner=new BotOwner{ProfileId="pending",BotsGroup=bossB.bossGroup};bossB.bossGroup.Members.Add(pendingOwner);
        pendingOwner.BotFollower.BossToFollow=bossB;BossPlayers.Followers.Add(new(){Bot=pendingOwner,CombatTactic=FollowerCombatTactic.SainMan});
        var pending=new BotComponent(pendingOwner);SAIN.SAINEnableClass.Bots.Add(pendingOwner.ProfileId,pending);
        SainAddonBridge.RaiseFollowerLifecycleEvent(null,FollowerLifecycleEvent.OnRaidEnd);BotManagerComponent.Instance.BotSquads.Flush();
        Check(pending.DecisionSubscriptions==0&&pending.Squad.SquadInfo==null,"raid cleanup covers native initialization before first sync");
        Check(second.DecisionSubscriptions==0&&third.DecisionSubscriptions==0,"raid cleanup removes all owned memberships");
        Check(Logger.Errors.Count==0,"no adapter errors");
        SainPlayerSquadBridge.Disable();SAINAddonPatches.Remove();
        Check(!SainAddonBridge.HasSquadProvider && SainAddonBridge.GetSquadSnapshot(third.BotOwner)==null,"shutdown removes passive snapshot provider");
        Check(!Harmony.GetAllPatchedMethods().Any(m=>Harmony.GetPatchInfo(m).Owners.Contains(SAINAddonPatches.HarmonyId)),"shutdown removes every addon-owned patch");
        Check(!SainPlayerSquadBridge.Enable()&&!SainSquadDecisionBridge.IsAvailable&&!SainCoverSelectionBridge.IsAvailable,"removed hooks cannot report ready");
        var coreOnly=Spawn();Recruit(coreOnly,Boss("coreOnly"),tactic:FollowerCombatTactic.Balanced);
        coreOnly.Squad.SquadInfo.Tick();
        Check(coreOnly.Squad.SquadInfo.LeaderComponent==null,"core guard blocks follower AI leadership without addon hooks");
        var ordinaryAfterRemoval=Spawn();ordinaryAfterRemoval.Squad.SquadInfo.Tick();
        Check(ReferenceEquals(ordinaryAfterRemoval.Squad.SquadInfo.LeaderComponent,ordinaryAfterRemoval),"ordinary native leadership survives addon removal");
        SainCoverSelectionBridge.FailApply=true;bool installFailed=false;
        try {SAINAddonPatches.Apply();}catch(InvalidOperationException){installFailed=true;}
        Check(installFailed&&!Harmony.GetAllPatchedMethods().Any(m=>Harmony.GetPatchInfo(m).Owners.Contains(SAINAddonPatches.HarmonyId)),"late installation failure rolls back already installed leadership and decision hooks");
        Check(!SainPlayerSquadBridge.Enable()&&!SainSquadDecisionBridge.IsAvailable&&!SainCoverSelectionBridge.IsAvailable,"failed installation clears readiness for every boundary");
        Check(Harmony.GetPatchInfo(HarmonyLib.AccessTools.Method(typeof(Squad),"assignSquadLeader")).Owners.Contains("pitTeam.core.leader.test"),"addon rollback preserves separate core compatibility patch");
        SainCoverSelectionBridge.FailApply=false;SainMedicalDecisionBridge.FailApply=true;installFailed=false;
        try {SAINAddonPatches.Apply();}catch(InvalidOperationException){installFailed=true;}
        Check(installFailed&&!Harmony.GetAllPatchedMethods().Any(m=>Harmony.GetPatchInfo(m).Owners.Contains(SAINAddonPatches.HarmonyId)),"medical installation failure rolls back all earlier addon hooks");
        Check(!SainCoverSelectionBridge.IsAvailable&&!SainMedicalDecisionBridge.IsAvailable,"medical installation failure resets addon boundary readiness");
        SainMedicalDecisionBridge.FailApply=false;SAINAddonPatches.Apply();Check(SainPlayerSquadBridge.Enable(),"clean retry after failed installation succeeds");
        SAINAddonPatches.Apply();
        Check(Harmony.GetPatchInfo(HarmonyLib.AccessTools.Method(typeof(Squad),"assignSquadLeader")).Prefixes.Count(p=>p.owner==SAINAddonPatches.HarmonyId)==1,"repeated addon installation does not duplicate hooks");
        SainPlayerSquadBridge.Disable();SAINAddonPatches.Remove();
        Console.WriteLine("Passed "+checks+" production leadership checks with real Harmony.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);foreach(var error in Logger.Errors)Console.Error.WriteLine(error);return 1;}}
}
// Combat behavior is covered by Verify-SainAddonCombat. These installers inject a
// late failure to exercise the production patch owner's rollback with real Harmony.
namespace pitTeam.SAINAddon {
    internal static class SainSquadDecisionBridge {
        public static bool IsAvailable;
        public static void Apply(Harmony h){if(IsAvailable)return;h.Patch(AccessTools.Method(typeof(InstallProbe),nameof(InstallProbe.Decision)),prefix:new HarmonyMethod(typeof(InstallProbe),nameof(InstallProbe.Prefix)));IsAvailable=true;}
        public static void Reset()=>IsAvailable=false;
    }
    internal static class SainCoverSelectionBridge {
        public static bool IsAvailable,FailApply;
        public static void Apply(Harmony h){if(FailApply)throw new InvalidOperationException("fixture late install failure");IsAvailable=true;}
        public static void Reset()=>IsAvailable=false;
    }
    internal static class SainMedicalDecisionBridge {
        public static bool IsAvailable,FailApply;
        public static void Apply(Harmony h){if(FailApply)throw new InvalidOperationException("fixture medical install failure");IsAvailable=true;}
        public static void Reset()=>IsAvailable=false;
    }
    internal static class InstallProbe {
        [MethodImpl(MethodImplOptions.NoInlining)]public static bool Decision()=>true;
        public static bool Prefix()=>true;
    }
}

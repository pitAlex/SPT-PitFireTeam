param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
# Production eligibility methods with controlled EFT stand-ins; not a raid simulation.
$ErrorActionPreference = 'Stop'
function Read-Method([string]$Path, [string]$Name) {
    $source = Get-Content -Raw (Join-Path $RepositoryRoot $Path)
    $matches = [regex]::Matches($source, '(?ms)^        (?:public|private|internal)[^\r\n]*\b' + [regex]::Escape($Name) + '\(.*?^        \}')
    if ($matches.Count -ne 1) { throw "Expected one method $Name, found $($matches.Count)" }
    return $matches[0].Value
}
$patrol = @('IsActive','HasVisibleKnownEnemy','HasRequestLayerCommand','ShouldWaitForCombatReadiness') | ForEach-Object { Read-Method 'client/BigBrain/FollowerPatrolLayer.cs' $_ }
$follower = @('IsEnemyInfoAlive','HasKnownEnemy','IsReadyForPatrolAfterCombat','IsSafelyOutOfCombat','HasActiveCombatSignal','HasRecentGroupCombatSignal','HasSquadmateCombatSignal','IsLiveEnemyPlayer') | ForEach-Object { Read-Method 'client/Components/BotFollowerPlayer.cs' $_ }
$medical = @('TryFindFirstAidTopOffTarget','HasVisibleKnownEnemy') | ForEach-Object { Read-Method 'client/Utils/FollowerMedical.cs' $_ }
$recovery = Read-Method 'client/Utils/FollowerRecovery.cs' 'ClearInvalidGoalEnemy'
$setterSource = (Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/BotMemoryPatch.cs')).Split('internal sealed class FollowerGoalEnemyClearRetentionPatch')[1]
$setter = [regex]::Match($setterSource, '(?ms)^        private static bool PatchPrefix\(.*?^        \}').Value
if (!$setter) { throw 'Missing production goal setter patch' }
$acquisition = Read-Method 'client/Patches/BotMemoryPatch.cs' 'ShouldBlockUnscopedMemoryOnlyGoal'
$code = @'
#nullable disable
#pragma warning disable CS0649, CS0414, CS8632
using System;
using System.Collections.Generic;
using EFT;
using Comfort.Common;
using UnityEngine;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using pitTeam.Patches;
using pitTeam.BigBrain.Actions;
namespace UnityEngine { public static class Time { public static float time=100; } }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace EFT {
    public enum EBotState { Active, Inactive }
    public enum BotLogicDecision { heal, other }
    public enum EBodyPart { Head }
    public class Health { public bool IsAlive=true; }
    public interface IPlayer { AIData AIData {get;} }
    public class AIData { public BotOwner BotOwner; }
    public class Player : IPlayer { public string ProfileId="boss"; public AIData AIData=>null; public Health HealthController=new Health(); public object ActiveHealthController=new object(); }
    public class GameWorld {
        public Dictionary<string,Player> Players=new Dictionary<string,Player>();
        public Player GetAlivePlayerByProfileID(string id)=>Players.TryGetValue(id,out var p)&&p.HealthController.IsAlive?p:null;
    }
    public class EnemyInfo { public string ProfileId; public Player Person; public bool IsVisible,CanShoot; public float PersonalLastSeenTime=-100; public GroupInfo GroupInfo; }
    public class GroupInfo { public object Cause; }
    public class BotMemory { public BotOwner Owner; public EnemyInfo GoalEnemy; public bool HaveEnemy=>GoalEnemy!=null; public bool IsUnderFire; public float LastTimeHit; }
    public class Memory:BotMemory {}
    public class Enemies { public Dictionary<string,EnemyInfo> EnemyInfos=new Dictionary<string,EnemyInfo>(); }
    public class Follower { public bool HaveBoss=true; public object BossToFollow=new pitAIBossPlayer(); }
    public class Result { public BotLogicDecision Action=BotLogicDecision.other; }
    public class Agent { public Result Result=new Result(); public Result LastResult()=>Result; }
    public class Brain { public Agent Agent=new Agent(); }
    public class BotFirstAid { public bool Using; }
    public class Surgery { public bool Using,HaveWork; }
    public class Medicine { public BotFirstAid FirstAid=new BotFirstAid(); public Surgery SurgicalKit=new Surgery(); }
    public class Mind { public bool CAN_USE_MEDS=true; }
    public class FileSettings { public Mind Mind=new Mind(); }
    public class Settings { public FileSettings FileSettings=new FileSettings(); }
    public class Group { public float EnemyLastSeenTimeReal; public Dictionary<IPlayer,object> Enemies=new Dictionary<IPlayer,object>(); }
    public class BotOwner {
        public BotOwner(){Memory.Owner=this;}
        public bool IsDead; public string ProfileId=Guid.NewGuid().ToString(); public Group BotsGroup=new Group();
        public EBotState BotState=EBotState.Active; public Player GetPlayer=new Player();
        public Health HealthController=>GetPlayer.HealthController;
        public Memory Memory=new Memory(); public Enemies EnemiesController=new Enemies();
        public Follower BotFollower=new Follower(); public Brain Brain=new Brain();
        public Medicine Medecine=new Medicine(); public Settings Settings=new Settings(); public BotFollowerPlayer Data;
    }
}
namespace EFT.InventoryLogic { public class Meds {} }
namespace pitTeam.Components {
    public class pitAIBossPlayer { public Player realPlayer=new Player(); }
    public enum FollowerCommandType { None,PushEnemy,SuppressEnemy,NeedSniper,HoldPosition }
    public class BossPlayers {
        public static BossPlayers Instance=new BossPlayers(); public static bool IsFollower(BotOwner b)=>b.Data!=null;
        public BotFollowerPlayer GetFollower(BotOwner b)=>b.Data;
        public static List<BotFollowerPlayer> Followers=new List<BotFollowerPlayer>();
        public static IEnumerable<BotFollowerPlayer> GetFollowersByBoss(string id)=>Followers;
    }
    public class BotFollowerPlayer {
        private BotOwner _bot; private string _knownEnemyProfileId; private float _knownEnemySince; private bool _knownEnemyLatched;
        private const float KnownEnemyAcquireHoldSeconds=1;
        public bool IsBackpackInspectionActive,HasCommand; private bool _sainAddonPatrolBridgeErrorLogged;
        private const float TemporaryCombatAggressionRecentEnemySeconds=3,TemporaryCombatAggressionGroupEnemySeconds=5;
        public BotFollowerPlayer(BotOwner b){_bot=b;b.Data=this;BossPlayers.Followers.Add(this);}
        public BotOwner GetBot()=>_bot;
        public pitAIBossPlayer GetBoss()=>_bot.BotFollower.BossToFollow as pitAIBossPlayer;
        private void ClearTemporaryCombatAggressionOverrideAfterCombatCooldown(){}
        public bool TryPeekActiveCommand(out FollowerCommandType c,out object target,out float until){c=FollowerCommandType.HoldPosition;target=null;until=0;return HasCommand;}
        private static bool HasLineOfSightToGoalEnemy(BotOwner b,EnemyInfo e)=>e.IsVisible&&e.Person.HealthController.IsAlive;
__FOLLOWER__
    }
}
namespace pitTeam { public static class pitFireTeam { public static bool UseSainFollowerCombat,IsSAINInstalled; } }
namespace pitTeam.Modules {
    public static class SainAddonBridge { public static bool HasRuntimeCallbacks=true; public static bool Ready; public static bool TryIsReadyForPatrolAfterCombat(BotOwner b,out bool ready){ready=Ready;return true;} }
    public static class Logger { public static void LogError(string s){} public static void LogError(Exception e){throw e;} }
    public static class FollowerGoalEnemyTracker {
        public static string CurrentReason="unscopedSetter";
        private class Scope:IDisposable{public string Previous; public void Dispose(){CurrentReason=Previous;}}
        public static IDisposable Begin(string s,string r){var scope=new Scope{Previous=CurrentReason};CurrentReason=r;return scope;}
        public static void RecordSetter(BotOwner b,EnemyInfo previous,EnemyInfo next,bool allowed,string blockedReason=null){}
    }
    public static class FollowerContactEnemyRetention {
        public static bool Retain,PermitNextClear;
        public static bool ShouldBlockGoalEnemyClear(BotOwner b,EnemyInfo e){if(PermitNextClear){PermitNextClear=false;return false;}return Retain;}
        public static bool ShouldAllowGoalEnemySet(BotOwner b,EnemyInfo old,EnemyInfo next,string reason,out string blockedReason){blockedReason=null;return true;}
    }
    public static class FollowerCombatTargetCommitments {
        public static bool ShouldAllowGoalEnemySet(BotOwner b,EnemyInfo old,EnemyInfo next,string reason,out string blockedReason){blockedReason=null;return true;}
    }
    public static class SainGoalEnemyBridge { public static bool Retain; public static bool TryGetRetainedSameGoalEnemy(BotOwner b,EnemyInfo e,out object position){position=null;return Retain;} }
    public static class FollowerEnemyInfoCorrection { public static bool IsInsideLookCheck; }
}
namespace pitTeam.Patches {
    public static class BotMemoryOwnerAccessor { public static BotOwner Get(BotMemory m)=>m.Owner; }
    public static class SetterHarness {
        public static bool Allow(BotMemory m,EnemyInfo next)=>PatchPrefix(m,next);
__SETTER__
__ACQUISITION__
    }
}
namespace pitTeam.BigBrain.Actions { public class HealAction {} public class PatrolHealWaitAction {} }
namespace pitTeam.BigBrain {
    public class CustomLayer { public virtual bool IsActive()=>false; }
    public class SelectedAction { public Type Type; }
    public class PatrolHarness : CustomLayer {
        public BotOwner BotOwner; private BotFollowerPlayer followerData; public SelectedAction selectedAction;
        public bool isHealing,patrolHealCoverFallback; public object patrolHealCover;
        public PatrolHarness(BotOwner b){BotOwner=b;}
        public bool Waiting()=>ShouldWaitForCombatReadiness();
__PATROL__
    }
}
namespace pitTeam.Utils {
    public static class Enemy {
        public static bool MemoryOnly;
        public static bool RequiresAcquisitionAwarenessGate(object cause)=>true;
        public static bool IsMemoryOnlyAcquisitionWithoutPersonalContact(EnemyInfo e)=>MemoryOnly;
    }
    public static class FollowerRecovery { __RECOVERY__ }
    public static class FollowerMedical {
        public static bool Using;
        public static bool IsUsingMedical(BotOwner b)=>Using;
        private static bool ShouldAllowManualFirstAidTopOff(BotOwner b)=>true;
        private static bool TryGetActiveBleeding(Player p,out object bleed){bleed=null;return false;}
        private static bool TryFindFirstAidTopOffTargetCore(Player p,BotFirstAid f,out EBodyPart b,out EFT.InventoryLogic.Meds m){b=default;m=new EFT.InventoryLogic.Meds();return true;}
        public static bool CanTopOff(BotOwner b)=>TryFindFirstAidTopOffTarget(b,out _,out _);
__MEDICAL__
    }
}
public static class HandoffChecks {
    private static int count;
    private static void Check(bool result,string name){if(!result)throw new Exception(name);count++;}
    private static EnemyInfo Enemy(bool alive,bool visible=false) {
        var p=new Player();p.HealthController.IsAlive=alive;
        var e=new EnemyInfo{ProfileId=Guid.NewGuid().ToString(),Person=p,IsVisible=visible};
        Singleton<GameWorld>.Instance.Players[e.ProfileId]=p;return e;
    }
    public static int Run() {
        Singleton<GameWorld>.Instance=new GameWorld();
        var bot=new BotOwner();var follower=new BotFollowerPlayer(bot);var patrol=new pitTeam.BigBrain.PatrolHarness(bot);
        var dead=Enemy(false);bot.Memory.GoalEnemy=dead;
        Check(patrol.IsActive(),"DeadGoal_LingerCanHandOffToPatrol");
        Check(pitTeam.Utils.FollowerMedical.CanTopOff(bot),"DeadGoal_DoesNotBlockMedicalTopOff");
        follower.HasKnownEnemy();Time.time+=2;
        Check(!follower.HasKnownEnemy(),"DeadGoal_DoesNotBecomeKnownAfterAcquireDelay");
        bot.EnemiesController.EnemyInfos[dead.ProfileId]=dead;dead.IsVisible=true;dead.CanShoot=true;
        Check(patrol.IsActive(),"DeadVisibleKnownEnemy_DoesNotBlockPatrol");
        Check(pitTeam.Utils.FollowerMedical.CanTopOff(bot),"DeadVisibleKnownEnemy_DoesNotBlockTopOff");
        patrol.selectedAction=new pitTeam.BigBrain.SelectedAction{Type=typeof(HealAction)};
        bot.Brain.Agent.Result.Action=BotLogicDecision.heal;
        Check(patrol.IsActive(),"StaleHealAndDeadEnemy_PatrolStillEligible");
        var live=Enemy(true,true);bot.Memory.GoalEnemy=live;
        Check(!patrol.IsActive(),"LiveGoal_StillBlocksPatrolAfterStaleHeal");
        Check(!pitTeam.Utils.FollowerMedical.CanTopOff(bot),"LiveGoal_StillBlocksTopOff");
        Check(follower.HasKnownEnemy(),"LiveVisibleGoal_IsKnown");
        live.Person.HealthController.IsAlive=false;
        Check(!follower.HasKnownEnemy(),"KilledLatchedEnemy_IsForgottenImmediately");
        Check(patrol.IsActive(),"KilledLatchedEnemy_AllowsPatrol");
        live.Person.HealthController.IsAlive=true;live.IsVisible=false;
        Check(!patrol.IsActive(),"LiveHiddenGoal_StillBlocksPatrol");
        bot.Memory.GoalEnemy=null;bot.EnemiesController.EnemyInfos[live.ProfileId]=live;live.IsVisible=true;
        Check(!patrol.IsActive(),"OtherLiveVisibleEnemy_StillBlocksPatrol");
        Check(!pitTeam.Utils.FollowerMedical.CanTopOff(bot),"OtherLiveVisibleEnemy_StillBlocksTopOff");
        live.IsVisible=false;
        var squadBot=new BotOwner();var squadmate=new BotFollowerPlayer(squadBot);squadBot.Memory.GoalEnemy=live;
        Check(!follower.IsReadyForPatrolAfterCombat(),"LivingSquadmateTarget_BlocksNormalFollowing");
        Check(patrol.IsActive()&&patrol.Waiting(),"SquadCombat_HasExplicitPatrolWaitOwner");
        patrol.isHealing=true;
        Check(!patrol.Waiting(),"CommittedMedicalSequenceNotInterruptedBySquadSignal");
        patrol.isHealing=false;patrol.patrolHealCover=new object();
        Check(!patrol.Waiting(),"CommittedPatrolHealCoverNotInterruptedBySquadSignal");
        patrol.patrolHealCoverFallback=true;
        Check(patrol.Waiting(),"AbandonedPatrolCoverDoesNotBypassReadiness");
        patrol.patrolHealCover=null;patrol.patrolHealCoverFallback=false;
        squadBot.Memory.GoalEnemy=null;squadBot.EnemiesController.EnemyInfos[live.ProfileId]=live;live.PersonalLastSeenTime=Time.time;
        Check(patrol.Waiting(),"FreshSquadContact_RetainsWait");
        Time.time+=3.1f;
        Check(!patrol.Waiting(),"ExpiredSquadContact_ReleasesWait");
        bot.BotsGroup.Enemies[live.Person]=new object();bot.BotsGroup.EnemyLastSeenTimeReal=Time.time;
        Check(patrol.Waiting(),"RecentGroupCombat_RetainsWait");
        Time.time+=5.1f;
        Check(!patrol.Waiting(),"ExpiredGroupCombat_ReleasesWait");
        squadBot.Memory.GoalEnemy=dead;
        Check(!patrol.Waiting(),"DeadSquadGoal_DoesNotRetainWait");
        squadBot.Memory.GoalEnemy=live;
        pitTeam.Utils.FollowerMedical.Using=true;
        Check(patrol.IsActive()&&!patrol.Waiting(),"ActiveMedicalUse_RetainsPatrolOwnership");
        pitTeam.Utils.FollowerMedical.Using=false;squadBot.Memory.GoalEnemy=null;follower.HasCommand=true;
        Check(!patrol.IsActive(),"RequestCommand_StillOwnsHandoff");
        follower.HasCommand=false;bot.EnemiesController.EnemyInfos.Clear();
        Check(patrol.IsActive(),"NoEnemy_AllowsPatrol");
        Check(!follower.HasKnownEnemy(),"NoEnemy_IsNotKnown");
        var missing=Enemy(true,true);Singleton<GameWorld>.Instance.Players.Remove(missing.ProfileId);bot.Memory.GoalEnemy=missing;
        Check(patrol.IsActive(),"RemovedPlayer_UsesSameLivenessAsCombat");
        missing.ProfileId=null;
        Check(!patrol.IsActive(),"NoProfileId_LivingPersonFallbackPreserved");
        missing.Person.HealthController.IsAlive=false;
        Check(patrol.IsActive(),"NoProfileId_DeadPersonDoesNotBlockPatrol");
        bot.Memory.GoalEnemy=dead;
        FollowerContactEnemyRetention.Retain=true;SainGoalEnemyBridge.Retain=true;
        Check(SetterHarness.Allow(bot.Memory,null),"DeadGoalCannotVetoClearEvenWithRetentionClaims");
        FollowerContactEnemyRetention.PermitNextClear=true;
        Check(SetterHarness.Allow(bot.Memory,null)&&!FollowerContactEnemyRetention.PermitNextClear,"DeadClearConsumesOneShotClearPermit");
        bot.Memory.GoalEnemy=live;
        Check(!SetterHarness.Allow(bot.Memory,null),"LivingCoreRetainedGoalStillBlocksClear");
        FollowerContactEnemyRetention.Retain=false;
        Check(!SetterHarness.Allow(bot.Memory,null),"LivingExactSainGoalStillBlocksUnscopedClear");
        SainGoalEnemyBridge.Retain=false;pitTeam.Utils.Enemy.MemoryOnly=true;
        foreach(bool sain in new[]{false,true}){
            pitTeam.pitFireTeam.IsSAINInstalled=sain;
            Check(!SetterHarness.Allow(bot.Memory,dead),"MemoryOnlyAcquisitionGuardPreserved");
        }
        using(FollowerGoalEnemyTracker.Begin("test","explicitCommand")){
            Check(SetterHarness.Allow(bot.Memory,live),"ScopedCommandAcquisitionPreserved");
        }
        pitTeam.Utils.Enemy.MemoryOnly=false;bot.Memory.GoalEnemy=dead;
        pitTeam.Utils.FollowerRecovery.ClearInvalidGoalEnemy(bot);
        Check(bot.Memory.GoalEnemy==null,"DeadGoal_ClearedWithoutReplacement");
        bot.Memory.GoalEnemy=live;
        pitTeam.Utils.FollowerRecovery.ClearInvalidGoalEnemy(bot);
        Check(bot.Memory.GoalEnemy==live,"CleanupPreservesLivingGoal");
        bot.Memory.GoalEnemy=null;pitTeam.pitFireTeam.UseSainFollowerCombat=true;
        Check(!patrol.IsActive(),"AddonUnavailableReadiness_StillFailsClosed");
        pitTeam.Modules.SainAddonBridge.Ready=true;
        Check(patrol.IsActive(),"AddonReady_StillAllowsPatrol");
        bot.Memory.GoalEnemy=dead;pitTeam.Utils.FollowerRecovery.ClearInvalidGoalEnemy(bot);
        Check(bot.Memory.GoalEnemy==dead,"CoreCleanupDoesNotOwnAddonGoal");
        pitTeam.pitFireTeam.UseSainFollowerCombat=false;
        bot.BotState=EBotState.Inactive;
        Check(!patrol.IsActive(),"InactiveFollower_StillRejected");
        return count;
    }
}
'@
$code=$code.Replace('__FOLLOWER__',($follower -join "`n")).Replace('__PATROL__',($patrol -join "`n")).Replace('__MEDICAL__',($medical -join "`n")).Replace('__RECOVERY__',$recovery).Replace('__SETTER__',$setter).Replace('__ACQUISITION__',$acquisition)
Add-Type -TypeDefinition $code -Language CSharp
$count=[HandoffChecks]::Run()
Write-Output "Passed $count post-combat handoff checks against production methods. Live raid verification remains required."

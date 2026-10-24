param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$BigBrainSource = 'F:\Projects\SPT-Tarkov\SPT-4.1.3\SPT-BigBrain-1.5.0'
)
$ErrorActionPreference = 'Stop'
function Read-Method([string]$Path, [string]$Name) {
    $source = Get-Content -Raw $Path
    $matches = [regex]::Matches($source, '(?ms)^        (?:public|private|internal)[^\r\n]*\b' + [regex]::Escape($Name) + '\(.*?^        \}')
    if ($matches.Count -ne 1) { throw "Expected one method $Name in $Path, found $($matches.Count)" }
    return $matches[0].Value
}
$medicalPath = Join-Path $RepositoryRoot 'client/Utils/FollowerMedical.cs'
$medical = @('BeginPostCombatFullHeal','MarkPostCombatHealingStarted','CompletePostCombatFullHeal','IsPostCombatFullHealActive','IsPostCombatFullHealRestoreWindowElapsed','TryCompletePostCombatFullHealRestore','IsUsingMedical') | ForEach-Object { Read-Method $medicalPath $_ }
$patrolPath = Join-Path $RepositoryRoot 'client/BigBrain/FollowerPatrolLayer.cs'
$patrol = @('EndHealing','TryEndTimedOutPatrolHealing') | ForEach-Object { Read-Method $patrolPath $_ }
$agent = Read-Method (Join-Path $BigBrainSource 'Patches/BotAgentUpdatePatch.cs') 'PatchPrefix'
$heal = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/Actions/HealAction.cs')
$heal = [regex]::Replace($heal, '(?m)^using [^\r\n]+\r?\n', '')
$diagnostic = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/FollowerPostCombatDiagnostics.cs')
$diagnostic = [regex]::Replace($diagnostic, '(?m)^using [^\r\n]+\r?\n', '')
$code = @'
#nullable disable
#pragma warning disable CS0649, CS0414, CS8632
using System;
using System.Collections.Generic;
using EFT;
using DrakiaXYZ.BigBrain.Brains;
using DrakiaXYZ.BigBrain.Internal;
using pitTeam.BigBrain.Actions;
using pitTeam.Utils;
using pitTeam.Components;
using pitTeam.BigBrain;
using UnityEngine;
namespace UnityEngine {
    public static class Time { public static float time; }
    public struct Vector3 { public float x; public float sqrMagnitude=>x*x; public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3{x=a.x-b.x}; }
}
namespace EFT {
    public enum BotLogicDecision { heal=9012,wait=9013 }
    public class CoreActionResultParams {}
    public struct AICoreActionResult<T,D> { public T Action; public D Data; public AICoreActionResult(T action){Action=action;Data=default;} }
    public class AICoreNode { public virtual void UpdateNodeByMain(object data){} }
    public class Strategy {
        public AICoreActionResult<BotLogicDecision,CoreActionResultParams>? Next;
        public void ManualUpdate(){}
        public AICoreActionResult<BotLogicDecision,CoreActionResultParams>? Update(AICoreActionResult<BotLogicDecision,CoreActionResultParams> last)=>Next;
    }
    public class AICoreAgent<T> {
        public Strategy _strategy=new Strategy(); public AICoreActionResult<BotLogicDecision,CoreActionResultParams> _lastResult;
        public Dictionary<BotLogicDecision,AICoreNode> _nodesDictionary=new Dictionary<BotLogicDecision,AICoreNode>();
        public Func<BotLogicDecision,AICoreNode> _lazyGetter;
    }
    public class Health { public bool IsAlive=true; }
    public class Player { public Health HealthController=new Health(); }
    public class Treatment { public bool Using,Have2Do,HaveWork; }
    public class Medicine {
        public Treatment FirstAid=new Treatment(),SurgicalKit=new Treatment(),Stimulators=new Treatment();
        public bool Using=>FirstAid.Using||SurgicalKit.Using||Stimulators.Using;
    }
    public class Weapon { public bool IsWeaponReady=true; }
    public class Profile { public string Nickname="test"; }
    public class EnemyInfo { public string ProfileId; public bool IsVisible,CanShoot; }
    public class Memory { public EnemyInfo GoalEnemy; public bool HaveEnemy=>GoalEnemy!=null; }
    public class Mover { public bool Pause; }
    public class Layer { public string Name()=>"pitTeam.FollowerPatrol"; }
    public class BaseBrain { public Layer CurLayerInfo=new Layer(); }
    public class Result { public string Reason="postCombatWait"; }
    public class Agent { public Result LastResult()=>new Result(); }
    public class Brain { public BaseBrain BaseBrain=new BaseBrain(); public Agent Agent=new Agent(); }
    public class BotOwner {
        public Vector3 Position; public Profile Profile=new Profile(); public Brain Brain=new Brain(); public Memory Memory=new Memory(); public Mover Mover=new Mover();
        public string ProfileId=Guid.NewGuid().ToString(); public Player GetPlayer=new Player();
        public Health HealthController=>GetPlayer.HealthController; public Medicine Medecine=new Medicine();
        public Weapon WeaponManager=new Weapon(); public bool MainSelected=true; public int ForceHeals,ReturnRequests,NodeUpdates;
    }
    public class HealNode { private BotOwner bot; public HealNode(BotOwner b){bot=b;} public void UpdateNodeByBrain(object d){bot.NodeUpdates++;} }
}
namespace DrakiaXYZ.BigBrain.Brains {
    public class CustomLayer { public class ActionData {} }
    public class CustomLogic { protected BotOwner BotOwner; public CustomLogic(BotOwner b){BotOwner=b;} public virtual void Start(){} public virtual void Update(CustomLayer.ActionData d){} }
}
namespace DrakiaXYZ.BigBrain.Internal {
    public class CustomLogicWrapper:AICoreNode {
        private HealAction action; public int Starts;
        public CustomLogicWrapper(BotOwner b){action=new HealAction(b);}
        public void Start(){Starts++;action.Start();}
        public override void UpdateNodeByMain(object d){action.Update(null);}
    }
}
namespace pitTeam {
    public static class pitFireTeam { public static bool AddonCombatEnabled; public static bool UseSainFollowerCombat(BotOwner owner)=>AddonCombatEnabled; public static LogStub Log=new LogStub(); }
    public class LogStub { public List<string> Lines=new List<string>(); public void LogWarning(string s){Lines.Add(s);} }
}
namespace pitTeam.Components { public class BotFollowerPlayer { public string DescribePatrolCombatBlock()=>"squadmate:test"; public static bool IsEnemyInfoAlive(EnemyInfo e)=>false; } }
namespace pitTeam.Modules { public static class SainGoalEnemyBridge { public static int Reads; public static string DescribeGoalEnemy(BotOwner b){Reads++;return "sainGoal=none";} } }
namespace pitTeam.Utils {
    public static class FollowerRecovery { public static void StopShooting(BotOwner b){} }
    public static class FollowerMedical {
        private class PostCombatFullHealState { public float StartedAt,HealStartedAt,LastWorkSeenAt; }
        private const float PostCombatFullHealRestoreDelay=12;
        private static Dictionary<string,PostCombatFullHealState> PostCombatFullHealStates=new Dictionary<string,PostCombatFullHealState>();
        public static string DescribePostCombatRecovery(BotOwner b)=>"recovery=test";
        private static string GetBotKey(BotOwner b)=>b?.ProfileId;
        private static bool IsMainWeaponSelected(BotOwner b)=>b.MainSelected;
        private static void TryReturnToMainWeapon(BotOwner b){b.ReturnRequests++;}
        public static void ForceHeal(BotOwner b){b.ForceHeals++;}
        public static void RefreshMedicalWork(BotOwner b){}
        public static void TryStartFirstAidTopOff(BotOwner b){}
__MEDICAL__
    }
}
namespace pitTeam.BigBrain {
    public static class FollowerCombatLayer { public static bool Active; public static bool IsFollowerCombatLayerActive(BotOwner b)=>Active; }
    public static class FollowerPatrolLayer { public const string CombatReadinessWaitReason="postCombatWait"; }
    public class SelectedAction { public Type Type=typeof(HealAction); }
    public class PatrolHarness {
        public BotOwner BotOwner; private SelectedAction selectedAction=new SelectedAction();
        private float healSoftTimeoutAt,healNodeEnteredAt; private bool healUseObserved;
        private const float HealNodeStartTimeout=4;
        public int Retries,Completed,Aborted;
        public PatrolHarness(BotOwner b){BotOwner=b;healNodeEnteredAt=Time.time;healSoftTimeoutAt=Time.time+20;}
        public bool End()=>EndHealing();
        private bool TryCompletePostCombatFullHealRestore()=>Utils.FollowerMedical.TryCompletePostCombatFullHealRestore(BotOwner);
        private void GetPatrolHealState(out bool use,out bool pending,out bool topoff){use=BotOwner.Medecine.FirstAid.Using||BotOwner.Medecine.SurgicalKit.Using;pending=true;topoff=false;}
        private void RefreshHealWorkForRetry(){Retries++;}
        private bool CanStartPatrolHealAction(bool use,bool pending,bool topoff)=>true;
        private void CompleteHealing(){Completed++;}
        private void AbortHealing(){Aborted++;}
        private bool IsActive()=>true;
__PATROL__
    }
}
__HEAL__
__DIAGNOSTIC__
public static class ActualBigBrainAgentPatch { __AGENT__ }
public static class MedicalChecks {
    private static int count;
    private static void Check(bool b,string s){if(!b)throw new Exception(s);count++;}
    public static int Run(){
        Time.time=100;var bot=new BotOwner();
        var agent=new AICoreAgent<BotLogicDecision>();var node=new CustomLogicWrapper(bot);
        agent._nodesDictionary[BotLogicDecision.heal]=node;
        agent._lastResult=new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.heal);
        agent._strategy.Next=agent._lastResult;
        FollowerMedical.BeginPostCombatFullHeal(bot);
        ActualBigBrainAgentPatch.PatchPrefix(agent);
        Check(node.Starts==0&&bot.NodeUpdates==1,"ActualBigBrain_ReusedHealSkipsStartButExecutesUpdate");
        Time.time=111;Check(!FollowerMedical.IsPostCombatFullHealRestoreWindowElapsed(bot),"RecoveryDoesNotFinishEarly");
        FollowerMedical.BeginPostCombatFullHeal(bot);ActualBigBrainAgentPatch.PatchPrefix(agent);
        Time.time=112;Check(FollowerMedical.IsPostCombatFullHealRestoreWindowElapsed(bot),"RepeatedBeginAndUpdate_DoNotRestartTimer");
        bot.Medecine.SurgicalKit.Using=true;
        Check(!FollowerMedical.TryCompletePostCombatFullHealRestore(bot)&&bot.ForceHeals==0,"ElapsedRecovery_PreservesActiveSurgery");
        bot.Medecine.SurgicalKit.Using=false;bot.WeaponManager.IsWeaponReady=false;
        Check(!FollowerMedical.TryCompletePostCombatFullHealRestore(bot)&&bot.ReturnRequests==1,"RecoveryWaitsForReadyWeapon");
        bot.WeaponManager.IsWeaponReady=true;bot.MainSelected=false;
        Check(!FollowerMedical.TryCompletePostCombatFullHealRestore(bot)&&bot.ReturnRequests==2,"RecoveryWaitsForMainWeapon");
        bot.MainSelected=true;
        Check(FollowerMedical.TryCompletePostCombatFullHealRestore(bot)&&bot.ForceHeals==1&&!FollowerMedical.IsPostCombatFullHealActive(bot),"ReadyRecovery_CompletesOnce");
        Check(!FollowerMedical.TryCompletePostCombatFullHealRestore(bot)&&bot.ForceHeals==1,"CompletedRecoveryCannotRepeat");
        Time.time=200;FollowerMedical.BeginPostCombatFullHeal(bot);
        Check(!FollowerMedical.IsPostCombatFullHealRestoreWindowElapsed(bot),"NewEpisodeHasNoOldTimer");
        Time.time=230;Check(!FollowerMedical.TryCompletePostCombatFullHealRestore(bot),"UnexecutedHealDoesNotStartTwelveSecondTimer");
        FollowerMedical.CompletePostCombatFullHeal(bot);
        Time.time=300;FollowerMedical.BeginPostCombatFullHeal(bot);var patrol=new pitTeam.BigBrain.PatrolHarness(bot);
        foreach(float t in new[]{305f,310f,315f}){Time.time=t;Check(!patrol.End(),"AdvertisedTreatment_CanRetryBeforeDeadline");}
        Time.time=320;Check(patrol.End()&&patrol.Retries==3&&bot.ForceHeals==2,"IdleHealRetryCannotRenewTwentySecondBudget");
        Time.time=400;FollowerMedical.BeginPostCombatFullHeal(bot);patrol=new pitTeam.BigBrain.PatrolHarness(bot);
        bot.Medecine.SurgicalKit.Using=true;Time.time=440;
        Check(!patrol.End()&&bot.ForceHeals==2,"IdleDeadlineDoesNotAbortRealSurgery");
        bot.Medecine.SurgicalKit.Using=false;Check(patrol.End()&&bot.ForceHeals==3,"ExpiredIdleBudgetRecoversAfterSurgeryEnds");
        Time.time=500;patrol=new pitTeam.BigBrain.PatrolHarness(bot);Time.time=525;
        Check(patrol.End()&&patrol.Aborted==1&&bot.ForceHeals==3,"OrdinaryHealingTimeoutDoesNotGrantFullHealth");
        Time.time=600;FollowerMedical.BeginPostCombatFullHeal(bot);patrol=new pitTeam.BigBrain.PatrolHarness(bot);
        bot.Medecine.Stimulators.Using=true;Time.time=625;
        Check(!patrol.End()&&bot.ForceHeals==3,"IdleDeadlineAlsoProtectsStimulatorUse");
        Time.time=800;var diagnosticBot=new BotOwner();var follower=new BotFollowerPlayer();var diagnostic=new pitTeam.Modules.FollowerPostCombatDiagnostics();
        diagnostic.Update(diagnosticBot,follower);Time.time=814.5f;diagnostic.Update(diagnosticBot,follower);
        Check(pitTeam.pitFireTeam.Log.Lines.Count==0&&pitTeam.Modules.SainGoalEnemyBridge.Reads==0,"DiagnosticsSilentBeforeThreshold_NoSainQueries");
        Time.time=815;diagnostic.Update(diagnosticBot,follower);
        Check(pitTeam.pitFireTeam.Log.Lines.Count==1&&pitTeam.Modules.SainGoalEnemyBridge.Reads==1,"ReleaseDiagnosticReportsProlongedWait");
        Time.time=830;diagnostic.Update(diagnosticBot,follower);
        Check(pitTeam.pitFireTeam.Log.Lines.Count==1,"DiagnosticRateLimitPreserved");
        for(float t=845;t<=1000;t+=30){Time.time=t;diagnostic.Update(diagnosticBot,follower);}
        Check(pitTeam.pitFireTeam.Log.Lines.Count==4,"DiagnosticMessageBudgetBounded");
        Time.time=1010;diagnosticBot.Position=new Vector3{x=2};diagnostic.Update(diagnosticBot,follower);
        Time.time=1025;diagnostic.Update(diagnosticBot,follower);
        Check(pitTeam.pitFireTeam.Log.Lines.Count==5,"MovementStartsNewDiagnosticWindow");
        diagnosticBot.Medecine.SurgicalKit.Using=true;Time.time=1100;diagnostic.Update(diagnosticBot,follower);
        Check(pitTeam.pitFireTeam.Log.Lines.Count==5,"ActiveProcedureDoesNotProduceStallLog");
        Check(diagnosticBot.ForceHeals==0&&diagnosticBot.ReturnRequests==0,"DiagnosticsDoNotMutateRecovery");
        return count;
    }
}
'@
$code=$code.Replace('__MEDICAL__',($medical -join "`n")).Replace('__PATROL__',($patrol -join "`n")).Replace('__HEAL__',$heal).Replace('__AGENT__',$agent).Replace('__DIAGNOSTIC__',$diagnostic)
Add-Type -TypeDefinition $code -Language CSharp
$count=[MedicalChecks]::Run()
Write-Output "Passed $count medical lifecycle checks using production methods and BigBrain's actual action-update patch. EFT item transactions are simulated; live raid verification remains required."

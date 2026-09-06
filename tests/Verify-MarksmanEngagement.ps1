param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))

# Dependency-free boundary tests. Compile the actual changed methods against small controlled
# EFT/Unity stand-ins; this does not simulate navigation, physics, weapon animations, or a raid.
$ErrorActionPreference = 'Stop'
$sniperSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatSniper.cs')
$commonSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatCommon.cs')
$phaseSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/CommittedCoverPhaseState.cs')
function Get-CombatMethod([string]$Source, [string]$Name) {
    $pattern = '(?ms)^        (?:public|private|internal)[^\r\n]*\b' + [regex]::Escape($Name) + '\(.*?^        \}'
    $matches = [regex]::Matches($Source, $pattern)
    if ($matches.Count -ne 1) { throw "Expected one source method: $Name; got $($matches.Count)" }
    return $matches[0].Value
}
$commonMethods = @('HandleCommittedCoverDecisionChanged', 'TryRenewCommittedPositionHold') | ForEach-Object { Get-CombatMethod $commonSource $_ }
$sniperMethods = @(
    'TryGetCloseQuarterDecision', 'IsMarksmanCloseSearchDestinationSafe', 'TryCreateSafeCloseSearchDecision',
    'TryPrepareAutomaticCloseWeapon', 'BeginCloseWeaponPreparation', 'BlockCloseWeaponPreparationRetry',
    'ClearCloseWeaponPreparation', 'EndCloseWeaponPreparationHold', 'IsCloseIntentDecisionReason',
    'IsAutomaticSupportIntentReason'
) | ForEach-Object { Get-CombatMethod $sniperSource $_ }
$harness = @'
#nullable enable
#pragma warning disable CS0649, CS0414
using System;
using UnityEngine;
using Decision = AICoreActionResult<BotLogicDecision, CoreActionResultParams>;
namespace UnityEngine {
    public static class Time { public static float time; }
    public static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b); public static float Min(float a,float b)=>Math.Min(a,b); }
    public struct Vector3 {
        public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 zero=>new Vector3(); public float sqrMagnitude=>x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    }
}
public enum BotLogicDecision { holdPosition, goToPointTactical, shootFromPlace, dogFight, runToCover }
public class CoreActionResultParams {}
public struct AICoreActionResult<T,P> { public T Action; public string Reason; public AICoreActionResult(T a,string r){Action=a;Reason=r;} }
public struct AICoreActionEnd { public string Reason; public bool Value; public AICoreActionEnd(string r,bool v){Reason=r;Value=v;} }
public class Cover { public int Id; }
public class PointData { public Vector3 Point; public bool Target=true; public bool HaveTarget()=>Target; public void SetPoint(Vector3 p){Point=p;} }
public class BotMemory { public Cover? CurCustomCoverPoint; public EnemyInfo? GoalEnemy; }
public class BotOwner { public Vector3 Position; public BotMemory Memory=new BotMemory(); public PointData GoToSomePointData=new PointData(); }
public class EnemyInfo { public bool IsVisible,CanShoot,Valid=true; public float Distance=32f; public string ProfileId="woods-enemy"; public Vector3 Anchor=new Vector3(32,0,0); }
public class CombatDistanceConfiguration { public static CombatDistanceConfiguration Instance=new CombatDistanceConfiguration(); public float GetCloseQuarterDistance()=>25f; }
namespace Utils { public static class Utils { public static bool Complete=true; public static float NavDistance=10f; public static bool TryGetCompletePathDistance(Vector3 a,Vector3 b,out float distance){distance=NavDistance;return Complete;} } }
public static class BattleRecorder { public static void RecordObjectiveDiagnostic(BotOwner b,string o,string a,string r){} }
public static class FollowerCombatCommon { public const float SupportWeaponPrepareTimeoutSeconds=3f; public static Vector3 GetEnemyAnchor(EnemyInfo e)=>e.Anchor; public static AICoreActionEnd Continue()=>default; }
public static class FollowerCombatSuppressionObjective { public static bool IsAutomaticSupportIntentReason(string? reason)=>reason=="ordered.automaticSupport"; }
public class CommonFake {
    public bool Ready,AcceptSwitch=true,SearchAvailable=true; public int SwitchRequests,DefensiveSuppressRequests;
    public bool IsAutomaticCloseCombatWeaponReady()=>Ready;
    public bool TryRequestAutomaticSupportForCloseCombat(){SwitchRequests++;return AcceptSwitch;}
    public Decision? TryGetDogFightDecision()=>null;
    public Decision? EnemyCoverSearch(string reason,bool weakEnemy,bool avoidBossFireLane)=>SearchAvailable?new Decision(BotLogicDecision.goToPointTactical,reason):(Decision?)null;
    public bool HasActiveCombatEnemy(EnemyInfo? e)=>e!=null&&e.Valid;
    public void HoldFor(float seconds){}
}
public class CoverHarness {
    private const float CommittedCoverArrivalHoldDistance=2f;
    public BotOwner botOwner=new BotOwner(); public Cover? committedCoverPoint,committedHoldCoverPoint;
    public float committedCoverUntil,committedPointTimer; public Decision? committedPositionDecision; public Vector3? committedPosition;
    public bool CoverValid=true; public int ClearCount,CommitCount; public BotLogicDecision StoredMove=BotLogicDecision.runToCover;
    private bool IsCommittedHoldCoverStillValid()=>CoverValid;
    private bool IsFinite(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsInfinity(v.x);
    private bool IsCoverAffinedDecision(BotLogicDecision a)=>a==BotLogicDecision.runToCover;
    private void CommitCover(Cover c,BotLogicDecision a,string r){CommitCount++;StoredMove=a;}
    private void ClearCommittedCover(){ClearCount++;committedCoverPoint=null;}
    private void HoldFor(float seconds){}
__COMMON_METHODS__
}
public class SniperHarness {
    private const string CloseWeaponPrepareHoldReason="sniper.closeWeaponPrepare";
    private const float CloseWeaponPrepareRetryCooldownSeconds=1f,FiringPositionCooldownSeconds=4f,MarksmanCloseSearchMinEnemyDistance=16f;
    public BotOwner BotOwner=new BotOwner(); public CommonFake CombatCommon=new CommonFake();
    private float closeWeaponPrepareUntil,closeWeaponPrepareRetryUntil,closeSearchRetryUntil;
    private string closeWeaponPrepareEnemyProfileId=string.Empty; private bool closeWeaponPreparationPending;
    private Decision? preparedCloseSearchDecision; private Vector3 preparedCloseSearchPoint;
    public bool OffensiveAllowed=true,Defer; public Decision? NextDecision;
    public SniperHarness(){BotOwner.Memory.GoalEnemy=new EnemyInfo();BotOwner.GoToSomePointData.Point=new Vector3(0,0,8);}
    private bool ShouldUseOffensiveAutoSearch(EnemyInfo e)=>OffensiveAllowed;
    private bool ShouldDeferCloseAutoToNearbyRifleman(EnemyInfo e)=>Defer;
    private bool TryCreateCloseSuppressMove(EnemyInfo e,string reason,out Decision d){CombatCommon.DefensiveSuppressRequests++;d=new Decision(BotLogicDecision.goToPointTactical,reason);return true;}
    private bool TryPrepareBreakDecision(Decision d,bool support,bool reposition){NextDecision=d;return true;}
    private static bool IsFinite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    private static bool IsFinite(Vector3 v)=>IsFinite(v.x)&&IsFinite(v.y)&&IsFinite(v.z);
    public bool Select(out Decision d)=>TryGetCloseQuarterDecision(BotOwner.Memory.GoalEnemy!,out d);
    public AICoreActionEnd EndPrepare()=>EndCloseWeaponPreparationHold();
__SNIPER_METHODS__
}
public static class MarksmanBoundaryChecks {
    private static int checks;
    private static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;}
    private static SniperHarness Fresh(){Time.time=100f;Utils.Utils.Complete=true;Utils.Utils.NavDistance=10f;return new SniperHarness();}
    public static int Run(){
        var hold=new Decision(BotLogicDecision.holdPosition,"committedCoverHold.sniper.reposition");
        Time.time=951.6476f;
        var cover=new CoverHarness{committedCoverPoint=new Cover{Id=9629},committedHoldCoverPoint=new Cover{Id=9629},committedPositionDecision=hold,committedCoverUntil=948.627f,committedPointTimer=Time.time+3f};
        cover.HandleCommittedCoverDecisionChanged(hold);
        Check(cover.ClearCount==0&&cover.committedCoverUntil>=cover.committedPointTimer,"Arrival_ExpiredTravelLease_RetainsSameCover");
        Check(cover.CommitCount==0&&cover.StoredMove==BotLogicDecision.runToCover,"Arrival_Hold_DoesNotOverwriteTravelAction");
        var phase=new pitTeam.BigBrain.CommittedCoverPhaseState();phase.BeginTravel();phase.PromoteToHoldOnArrival();phase.BeginHoldLifecycle(2.5f,10f);
        Check(cover.TryRenewCommittedPositionHold(hold,10f),"Hold_ValidCover_Renews");
        Time.time+=3.1f;
        Check(!phase.IsHoldExpired&&cover.committedPointTimer>Time.time,"Hold_ThreeSeconds_DoesNotReenterNoAction");
        Time.time=961.6476f;
        Check(phase.IsHoldExpired,"Hold_TenSeconds_OpensEngagementRetry");
        Check(cover.TryRenewCommittedPositionHold(hold,10f),"Hold_FailedSearch_CanRetainExactTarget");
        Check(!cover.TryRenewCommittedPositionHold(new Decision(BotLogicDecision.holdPosition,"different"),10f),"Hold_DifferentDecision_CannotRenew");
        cover.CoverValid=false;
        Check(!cover.TryRenewCommittedPositionHold(hold,10f),"Hold_InvalidCover_CannotRenew");
        var point=new CoverHarness{committedPositionDecision=hold,committedPosition=new Vector3(2,0,0)};
        Check(point.TryRenewCommittedPositionHold(hold,10f),"PointHold_TwoMeters_AcceptsBoundary");
        point.committedPosition=new Vector3(2.01f,0,0);
        Check(!point.TryRenewCommittedPositionHold(hold,10f),"PointHold_OutsideArrival_DoesNotRenew");
        var h=Fresh();Check(h.Select(out var decision)&&decision.Reason=="sniper.closeWeaponPrepare","Search_EligibleBeyond25m_Prepares");
        Check(h.CombatCommon.SwitchRequests==1,"Preparation_Entry_OneSwitchRequest");
        Time.time=102.9f;Check(!h.EndPrepare().Value&&h.CombatCommon.SwitchRequests==1,"Preparation_BeforeThreeSeconds_WaitsWithoutReswitch");
        h.CombatCommon.Ready=true;h.BotOwner.GoToSomePointData.SetPoint(new Vector3(500,0,500));
        Check(h.EndPrepare().Value&&h.NextDecision?.Reason=="sniper.closeSearch","Preparation_Ready_DirectMovementSuccessor");
        Check(h.BotOwner.GoToSomePointData.Point.z==8f,"Preparation_Ready_RestoresSelectedDestination");
        h=Fresh();h.CombatCommon.SearchAvailable=false;
        Check(!h.Select(out _)&&h.CombatCommon.SwitchRequests==0,"Search_NoRoute_DoesNotDraw");
        h=Fresh();Utils.Utils.Complete=false;
        Check(!h.Select(out _)&&h.CombatCommon.SwitchRequests==0,"Search_IncompletePath_DoesNotDraw");
        h=Fresh();Utils.Utils.NavDistance=90.01f;
        Check(!h.Select(out _)&&h.CombatCommon.SwitchRequests==0,"Search_ExcessiveNavDistance_DoesNotDraw");
        h=Fresh();h.BotOwner.GoToSomePointData.SetPoint(new Vector3(2,0,0));
        Check(!h.Select(out _)&&h.CombatCommon.SwitchRequests==0,"Search_AlreadyArrived_DoesNotDraw");
        h=Fresh();h.BotOwner.GoToSomePointData.SetPoint(new Vector3(17,0,0));
        Check(!h.Select(out _)&&h.CombatCommon.SwitchRequests==0,"Search_Inside16mEnemyStandoff_DoesNotDraw");
        h=Fresh();h.BotOwner.GoToSomePointData.SetPoint(new Vector3(16,0,0));
        Check(h.Select(out _),"Search_At16mEnemyStandoff_AcceptsBoundary");
        h=Fresh();h.OffensiveAllowed=false;
        Check(!h.Select(out _)&&h.CombatCommon.SwitchRequests==0&&h.CombatCommon.DefensiveSuppressRequests==0,"Search_IneligibleAt32m_DoesNotUseCloseDefense");
        h.BotOwner.Memory.GoalEnemy!.Distance=20;
        Check(h.Select(out _)&&h.CombatCommon.DefensiveSuppressRequests==1,"Defense_InsideCloseRange_RemainsAvailable");
        h=Fresh();h.Select(out _);Time.time=103.01f;
        Check(h.EndPrepare().Value&&h.NextDecision==null&&h.CombatCommon.SwitchRequests==1,"Preparation_Timeout_NoMovementOrRepeatedSwitch");
        h=Fresh();h.Select(out _);h.BotOwner.Memory.GoalEnemy!.ProfileId="other";
        Check(h.EndPrepare().Value&&h.NextDecision==null,"Preparation_EnemyChanged_Cancels");
        h=Fresh();h.Select(out _);h.OffensiveAllowed=false;
        Check(h.EndPrepare().Value&&h.NextDecision==null,"Preparation_LeftOffensiveEnvelope_Cancels");
        h=Fresh();h.Select(out _);h.BotOwner.Memory.GoalEnemy!.IsVisible=true;h.BotOwner.Memory.GoalEnemy.CanShoot=true;
        Check(h.EndPrepare().Value&&h.NextDecision?.Action==BotLogicDecision.shootFromPlace,"Preparation_ImmediateShot_SupersedesSearch");
        h=Fresh();h.Select(out _);h.CombatCommon.Ready=true;h.Defer=true;
        Check(h.EndPrepare().Value&&h.NextDecision==null,"Preparation_RiflemanNowPreferred_CancelsSearch");
        foreach(var reason in new[]{"sniper.closeSearch","sniper.closeWeaponPrepare","sniper.closeImmediateShoot","committedCoverHold.sniper.closeSearch","committedPositionHold.sniper.startCloseSearch","ordered.automaticSupport"})
            Check(SniperHarness.IsAutomaticSupportIntentReason(reason),"WeaponIntent_Preserves_"+reason);
        foreach(var reason in new[]{"sniper.noActionHold","sniper.position.reengage.goToPoint","committedCoverHold.sniper.reposition"})
            Check(!SniperHarness.IsAutomaticSupportIntentReason(reason),"WeaponIntent_DoesNotBroaden_"+reason);
        return checks;
    }
}
__PHASE_SOURCE__
'@
$harness = $harness.Replace('__COMMON_METHODS__', ($commonMethods -join "`n")).Replace('__SNIPER_METHODS__', ($sniperMethods -join "`n")).Replace('__PHASE_SOURCE__', $phaseSource.Replace('using UnityEngine;', ''))
Add-Type -TypeDefinition $harness -Language CSharp
$count = [MarksmanBoundaryChecks]::Run()
Write-Output "Passed $count marksman boundary checks against extracted production methods. Unity/NavMesh/weapon-animation behavior still requires a raid test."

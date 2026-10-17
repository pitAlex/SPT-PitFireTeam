#nullable enable
#pragma warning disable CS0649, CS0414, CS8602, CS8604
using System;
using Decision = AICoreActionResult<BotLogicDecision, CoreActionResultParams>;
public struct Vector3 { public static Vector3 zero => default; }
public static class Time { public static float time = 100; }
public enum BotLogicDecision { holdPosition, runToCover, attackMoving }
public enum CustomBotDecisions { attackRetreat = 107 }
public class CoreActionResultParams { }
public struct AICoreActionResult<T,P> { public T Action; public string? Reason; public AICoreActionResult(T a,string? r){Action=a;Reason=r;} }
public struct AICoreActionEnd { public bool Value; public string Reason; public AICoreActionEnd(string r,bool v){Reason=r;Value=v;} }
public class EnemyInfo { public bool Alive=true, IsVisible,CanShoot; }
public class CustomNavigationPoint { public bool IsSpotted; public Vector3 Position; }
public class Memory { public EnemyInfo? GoalEnemy; public bool IsInCover,IsUnderFire; public CustomNavigationPoint? CurCustomCoverPoint; }
public class CoverSettings { public float END_HOLD_IF_ENEMY_CLOSE_AND_VISIBLE=10; }
public class FileSettings { public CoverSettings Cover=new CoverSettings(); }
public class Settings { public FileSettings FileSettings=new FileSettings(); }
public class BotOwner { public Memory Memory=new Memory(); public Settings Settings=new Settings(); public float HitAge=999; public bool Damaged,Threatened; }
public static class FollowerAwareness { public static bool WasRecentlyDamaged(BotOwner b)=>b.Damaged; public static bool WasRecentlyThreatened(BotOwner b)=>b.Threatened; }
public static class FollowerCombatPush { public static bool IsNoPushHoldReason(string r)=>false; public static bool IsPushReason(string r)=>false; }
namespace pitTeam.Modules { public static class FollowerEnemyTracking { public static float Distance(EnemyInfo e)=>20; } }
public class HandoffCommon
{
    public BotOwner botOwner=new BotOwner();
    public bool Explosive,Gesture,AtDestination,Stalled,DogFight,Prepared,HealCleared,PointBlank,CloseThreat;
    public bool holdActive; public float holdEndTime;
    public CustomNavigationPoint? committedHealCover=new CustomNavigationPoint(),committedCoverPoint;
    public bool HaveCoverToShoot;
    const string HealRetryHoldReason="medicalRetryHold",RecoveryNoCoverThreatHoldReason="recoveryHold",TargetHandoffScanReason="targetScan";
    public AICoreActionEnd EndWalking()=>EndAttackRetreat("moveToHealPoint.attackRetreat");
    public AICoreActionEnd EndMoving()=>EndAttackMoving("moveToHeal.retreat");
    public AICoreActionEnd EndOrdinaryRetreat()=>EndAttackRetreat("retreatSafeCover");
    bool IsTrackedEnemyAlive(EnemyInfo? e)=>e?.Alive==true;
    bool HasImmediateExplosiveDanger()=>Explosive;
    void ClearCommittedHealCover(){HealCleared=true;committedHealCover=null;}
    public static bool WasHitRecently(BotOwner b,float seconds)=>b.HitAge<=seconds;
    public static AICoreActionEnd Continue()=>default;
    bool IsBotAtCommittedHealCover()=>AtDestination;
    void BlockHealCover(CustomNavigationPoint c){}
    void ResetHealRetreatProgress(){}
    AICoreActionEnd EndHealRetreatIfStalled()=>Stalled?new AICoreActionEnd("healRetreatStalled",true):Continue();
    bool HasActiveOrRetainedGoalEnemy(out EnemyInfo? e){e=botOwner.Memory.GoalEnemy;return e?.Alive==true;}
    bool IsCommittedShootingCoverReason(string? r)=>false;
    void ClearCommittedCover(){}
    void ClearCommittedMovement(){}
    bool IsCloseVisibleShootableThreat(EnemyInfo? e)=>CloseThreat;
    bool ShouldBreakRunToCoverForImmediateFire()=>false;
    bool IsPointBlankVisibleShootableThreat(EnemyInfo? e)=>PointBlank;
    bool IsCloseVisibleHealThreat(EnemyInfo? e)=>CloseThreat;
    Decision? TryGetCommittedHealMoveDecision(EnemyInfo? e)=>new Decision(BotLogicDecision.attackMoving,"moveToHeal.retreatSuppress");
    bool TryPrepareDecisionTransition(Decision d,string r,Decision next){Prepared=true;return true;}
    bool IsBotAtCoverDestination(CustomNavigationPoint? c)=>AtDestination;
    AICoreActionEnd EndRunToCoverIfStalled(string? r)=>Stalled?new AICoreActionEnd("runToCoverStalled",true):Continue();
    void HoldCoverForMaxDuration(){}
    void ArmCommittedArrivalHold(string? r,bool preferCover){}
    bool HasCurrentGoToPointArrivedAt(Vector3 p)=>false;
    bool IsDogFightActive()=>DogFight;
    bool IsMedicalRetreatMovementReason(string? r)=>r?.StartsWith("moveToHeal")==true;
    void RefreshShootCover(){}
    AICoreActionEnd EndBaseAttackMoving(string? r)=>Continue();
    bool IsBotInCommittedCover()=>AtDestination;
    bool IsAtCommittedMovementDestination()=>AtDestination;
    bool HasActiveCombatGestureOrder()=>Gesture;
    void ClearTargetHandoffScan(string r){}
    AICoreActionEnd EndTargetHandoffScan()=>Continue();
    bool IsReloadHoldReason(string r)=>r=="reloadNoCover";
    AICoreActionEnd EndReloadHold(Decision d)=>Continue();
    /* PRODUCTION METHODS */
}
public static class CombatHealHandoffChecks
{
    static int count;
    static void Check(bool pass,string label){count++;if(!pass)throw new Exception(label);}
    static HandoffCommon New(EnemyInfo? e=null){var c=new HandoffCommon();c.botOwner.Memory.GoalEnemy=e;return c;}
    public static int Run()
    {
        foreach(var route in new Func<HandoffCommon,AICoreActionEnd>[] { c=>c.EndWalking(),c=>c.EndMoving(),c=>c.EndRunToCover("runToHeal") })
        {
            var c=New();var end=route(c);
            Check(end.Value&&end.Reason=="healRetreatThreatCleared"&&c.HealCleared,"quiet enemy loss releases medical route before stall");
            c=New(new EnemyInfo{Alive=false});Check(route(c).Value&&c.HealCleared,"dead goal cannot retain medical retreat");
            c=New(new EnemyInfo());Check(!route(c).Value&&!c.HealCleared,"living hidden goal keeps retreat");
            c=New();c.botOwner.Memory.IsUnderFire=true;Check(!route(c).Value&&!c.HealCleared,"unseen incoming fire keeps retreat");
            c=New();c.botOwner.HitAge=1.5f;Check(!route(c).Value&&!c.HealCleared,"recent hit keeps retreat for existing two-second pressure window");
            c.botOwner.HitAge=2.01f;Check(route(c).Value&&c.HealCleared,"pressure expiry releases immediately without four-second movement timeout");
            c=New();c.botOwner.Damaged=true;Check(!route(c).Value,"recent damage keeps retreat");
            c=New();c.botOwner.Threatened=true;Check(!route(c).Value,"recent threat keeps retreat");
            c=New();c.Explosive=true;Check(!route(c).Value,"explosive danger keeps retreat");
            c=New(new EnemyInfo());c.AtDestination=true;c.botOwner.Memory.IsInCover=true;
            Check(route(c).Value&&!c.HealCleared,"live-enemy safe arrival keeps normal treatment handoff");
            c=New(new EnemyInfo());c.Stalled=true;Check(route(c).Value,"actual stalled live-enemy retreat retains recovery");
        }
        var h=New();
        h.botOwner.Threatened=true;
        bool stable=true;for(int i=0;i<221;i++)stable&=!h.EndBaseHoldPosition("nullEnemy").Value;
        Check(stable,"221 no-enemy/outside-cover evaluations with recorded lingering threat cannot recreate hold");
        h.botOwner.Memory.IsInCover=true;Check(!h.EndBaseHoldPosition("nullEnemy").Value,"missing-enemy hold stable inside cover too");
        h.botOwner.Memory.GoalEnemy=new EnemyInfo();Check(h.EndBaseHoldPosition("nullEnemy").Reason=="goalEnemyAvailable","new hidden goal interrupts fallback immediately");
        h.botOwner.Memory.GoalEnemy=null;h.botOwner.Memory.IsInCover=false;h.botOwner.Memory.IsUnderFire=true;
        Check(h.EndBaseHoldPosition("nullEnemy").Reason=="underFireNoCover","incoming fire interrupts fallback");
        h.botOwner.Memory.IsInCover=true;Check(h.EndBaseHoldPosition("nullEnemy").Reason=="nullEnemyThreat","incoming fire can replan missing-enemy fallback inside cover too");
        h.botOwner.Memory.IsInCover=false;
        h.botOwner.Memory.IsUnderFire=false;h.botOwner.Damaged=true;Check(h.EndBaseHoldPosition("nullEnemy").Reason=="hitHold","actual damage still interrupts despite lingering threat");
        h.botOwner.Damaged=false;
        h.botOwner.Memory.IsUnderFire=false;h.botOwner.HitAge=.5f;Check(h.EndBaseHoldPosition("nullEnemy").Reason=="hitHold","fresh hit interrupts fallback");
        h.botOwner.HitAge=999;h.Gesture=true;Check(h.EndBaseHoldPosition("nullEnemy").Reason=="combatGestureBreakHold","explicit combat order interrupts fallback");
        h.Gesture=false;h.holdActive=true;h.holdEndTime=99;Check(h.EndBaseHoldPosition("nullEnemy").Reason=="holdExpired","existing finite hold expiry is preserved");
        Check(h.EndBaseHoldPosition("ordinaryHold").Reason=="notInCover","ordinary cover-bound holds unchanged");
        h.botOwner.Memory.IsInCover=true;Check(h.EndBaseHoldPosition("goalEnemy.P").Reason=="canSearchEnemy","other no-enemy hold endings unchanged");
        Check(HandoffCommon.IsStableNoCoverHoldReason("nullEnemy"),"fallback classified stable without cover");
        var ordinary=New();Check(!ordinary.EndRunToCover("retreatSafeCover").Value&&!ordinary.HealCleared,"ordinary running retreat is not released by medical policy");
        Check(!ordinary.EndOrdinaryRetreat().Value&&!ordinary.HealCleared,"ordinary walking retreat is not released by medical policy");
        return count;
    }
}

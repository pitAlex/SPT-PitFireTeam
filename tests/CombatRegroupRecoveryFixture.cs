using System;
using Decision = AICoreActionResult<BotLogicDecision, CoreActionResultParams>;
public enum BotLogicDecision { holdPosition, runToCover, attackMovingWithSuppress, shootFromPlace, dogFight, heal }
public enum FollowerCommandType { None, RegroupNearBoss, PushEnemy, SuppressEnemy }
public enum EPhraseTrigger { Negative }
public enum EInteraction { NoGesture }
public struct Vector3 { }
public class CoreActionResultParams { }
public struct AICoreActionResult<T,D>
{
    public T Action; public string Reason;
    public AICoreActionResult(T action,string reason) { Action=action; Reason=reason; }
}
public struct AICoreActionEnd
{
    public bool Value; public string Reason;
    public AICoreActionEnd(string reason,bool value) { Reason=reason; Value=value; }
}
public class EnemyInfo { public bool Alive=true, Admitted=true; }
public class Memory { public EnemyInfo GoalEnemy=new EnemyInfo(); public bool HaveEnemy=true; }
public class Talk { public void TrySay(EPhraseTrigger phrase,bool force) { } }
public class Gesture { public void TryGestus(EInteraction gesture,bool force) { } }
public class BotOwner { public Memory Memory=new Memory(); public Talk BotTalk=new Talk(); public Gesture Gesture=new Gesture(); }
public class BotFollowerPlayer
{
    public FollowerCommandType Command=FollowerCommandType.RegroupNearBoss;
    public bool HasOrderedPushCancelRequest;
    public bool TryGetActiveCommand(out FollowerCommandType command,out Vector3 target)
    { command=Command; target=default; return command!=FollowerCommandType.None; }
    public void ClearCommand(string reason) { Command=FollowerCommandType.None; }
}
public class BossPlayers
{
    public static BossPlayers Instance=new BossPlayers();
    public BotFollowerPlayer Follower=new BotFollowerPlayer();
    public BotFollowerPlayer GetFollower(BotOwner bot)=>Follower;
}
public static class FollowerEnemyInfoCorrection { public static void CorrectDistanceOnly(BotOwner bot,EnemyInfo enemy) { } }
public class FollowerCombatCommon
{
    public BotOwner Owner;
    public bool Committed=true;
    public Decision Commitment;
    public int SharedEnds,TargetCorrections;
    public void BeginCoverEvaluationCycle() { }
    public void TryApplyPendingLauncherPrimaryFallback(Decision current) { }
    public bool HasActiveCombatEnemy(EnemyInfo enemy)=>Owner.Memory.HaveEnemy && enemy!=null && enemy.Alive && enemy.Admitted;
    public bool HasActiveCombatGestureOrder()=>false;
    public bool IsCommittedHolderReason(string reason)=>reason.StartsWith("committedCoverHold.",StringComparison.Ordinal);
    public bool HasCommittedPosition(out Decision decision) { decision=Commitment; return Committed; }
    public AICoreActionEnd EndBaseHoldPosition(string reason) { SharedEnds++; return Continue(); }
    public AICoreActionEnd ShallEndCurrentDecision(Decision current) { SharedEnds++; return Continue(); }
    public bool TryRestoreMissionTargetIfReady(string reason,out EnemyInfo enemy) { TargetCorrections++; enemy=null; return false; }
    public bool IsInFight(BotLogicDecision action)=>action==BotLogicDecision.shootFromPlace;
    public static bool IsMedicalDecision(Decision current)=>current.Action==BotLogicDecision.heal || current.Reason.Contains("heal",StringComparison.OrdinalIgnoreCase);
    public static bool IsMovementDecision(Decision current)=>current.Action==BotLogicDecision.runToCover || current.Action==BotLogicDecision.attackMovingWithSuppress;
    public static bool IsTargetHandoffScanReason(string reason)=>reason=="targetHandoffScan";
    private static bool IsReasonOrSubreason(string reason,string prefix)=>reason==prefix || reason?.StartsWith(prefix+".",StringComparison.Ordinal)==true;
    public static AICoreActionEnd Continue()=>new AICoreActionEnd("continue",false);
    /* RECOVERY REASON */
}
public class Objective
{
    public int Ends;
    public AICoreActionEnd ShallEndCurrentDecision(Decision current) { Ends++; return FollowerCombatCommon.Continue(); }
}
public class RecoveryRegroupRouter
{
    private enum CombatObjectiveKind { Default, Regroup, Suppression, OrderedPush, NeedSniper }
    private CombatObjectiveKind currentObjective=CombatObjectiveKind.Default;
    public readonly BotOwner BotOwner=new BotOwner();
    public readonly FollowerCombatCommon combatCommon=new FollowerCombatCommon();
    public readonly Objective Objective=new Objective();
    public RecoveryRegroupRouter() { combatCommon.Owner=BotOwner; BossPlayers.Instance=new BossPlayers(); }
    private Objective GetCurrentObjective()=>Objective;
    private bool HasActiveCombatGestureOrder(BotFollowerPlayer follower)=>false;
    private bool CanInterruptForCombatGestureOrder(Decision current)=>false;
    private bool ShouldConsumeSuppressCommand(BotFollowerPlayer follower,EnemyInfo enemy)=>false;
    private bool CanSatisfySuppressionOrder(BotFollowerPlayer follower)=>true;
    private bool CanInterruptForSuppressionOrder(Decision current)=>true;
    private bool ShouldConsumePushCommand(BotFollowerPlayer follower,EnemyInfo enemy)=>false;
    private bool CanInterruptForOrderedPushOrder(Decision current)=>false;
    private bool HasRenewedOrderedPushOrder(BotFollowerPlayer follower)=>false;
    private bool ShouldConsumeNeedSniperCommand(BotFollowerPlayer follower,EnemyInfo enemy)=>false;
    private bool ShouldRejectNeedSniperObjective(EnemyInfo enemy)=>false;
    /* REGROUP ADMISSION */
    /* REGROUP INTERRUPTION */
    /* PRODUCTION END ROUTER */
}
public static class CombatRegroupRecoveryChecks
{
    public static int Run()
    {
        int checks=0;
        Action<bool,string> check=(ok,why)=>{checks++;if(!ok)throw new Exception(why);};
        foreach(string suffix in new[]{"",".weak"})
        foreach(string contact in new[]{"active","missing","dead","unadmitted","memoryNotActive"})
        {
            var router=new RecoveryRegroupRouter();
            var current=new Decision(BotLogicDecision.holdPosition,"committedCoverHold.recovery.noEnemyThreatCover"+suffix);
            router.combatCommon.Commitment=current;
            switch(contact)
            {
                case "missing":router.BotOwner.Memory.GoalEnemy=null;break;
                case "dead":router.BotOwner.Memory.GoalEnemy.Alive=false;break;
                case "unadmitted":router.BotOwner.Memory.GoalEnemy.Admitted=false;break;
                case "memoryNotActive":router.BotOwner.Memory.HaveEnemy=false;break;
            }
            var end=router.ShallEndCurrentDecision(current);
            check(end.Value==(contact=="active"),"Only reacquired real contact restores normal objective routing: "+contact+suffix);
            if(contact=="active")
            {
                check(end.Reason=="objectiveRegroupOrder","Recorded committed recovery hold releases for received Regroup");
                check(router.combatCommon.SharedEnds==0 && router.Objective.Ends==0,"Regroup handoff precedes old hold end logic");
            }
            else check(router.combatCommon.TargetCorrections==0,"Actual no-enemy survival keeps its early owner");
            check(BossPlayers.Instance.Follower.Command==FollowerCommandType.RegroupNearBoss,"End polling does not consume/clear the order");
        }
        foreach(string scenario in new[]{"noOrder","heal","healMovement","dogfight","shoot","ordinaryHold","coverMovement","targetScan"})
        {
            var router=new RecoveryRegroupRouter();
            string reason="committedCoverHold.recovery.noEnemyThreatCover.weak";
            BotLogicDecision action=BotLogicDecision.holdPosition;
            switch(scenario)
            {
                case "noOrder":BossPlayers.Instance.Follower.Command=FollowerCommandType.None;break;
                case "heal":action=BotLogicDecision.heal;reason="healInCover";break;
                case "healMovement":action=BotLogicDecision.runToCover;reason="moveToHealPoint";break;
                case "dogfight":action=BotLogicDecision.dogFight;reason="closeVisibleDogFight";break;
                case "shoot":action=BotLogicDecision.shootFromPlace;reason="knight.native.FIRSTTIMESH";break;
                case "ordinaryHold":reason="knight.native.HoldFor";break;
                case "coverMovement":action=BotLogicDecision.runToCover;reason="knight.cover.CheckGoodFo";break;
                case "targetScan":reason="targetHandoffScan";break;
            }
            var end=router.ShallEndCurrentDecision(new Decision(action,reason));
            check(end.Value==(scenario=="ordinaryHold" || scenario=="coverMovement"),"Existing command/survival ownership remains: "+scenario);
            if(scenario=="heal" || scenario=="healMovement" || scenario=="dogfight" || scenario=="shoot")
                check(router.Objective.Ends==1,"Protected action retains its objective end handler: "+scenario);
        }
        return checks;
    }
}

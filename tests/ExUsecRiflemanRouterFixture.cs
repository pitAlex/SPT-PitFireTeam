using System;
using System.Collections.Generic;
using Decision = AICoreActionResult<BotLogicDecision, CoreActionResultParams>;
public enum BotLogicDecision { holdPosition, suppressFire, shootFromPlace }
public class CoreActionResultParams { }
public struct AICoreActionEnd(string reason,bool value){public string Reason=reason;public bool Value=value;}
public struct AICoreActionResult<T,D> { public T Action;public string Reason;public AICoreActionResult(T action,string reason){Action=action;Reason=reason;} }
public class EnemyInfo {public bool IsVisible,CanShoot;}
public class Owner {public Memory Memory=new Memory();}
public class Memory {public bool IsInCover;}
public class FollowerCombatCommon
{
    public const string HealRetryHoldReason="healRetry";
    public readonly HashSet<string> Active=new HashSet<string>();public int AdvanceReads;
    public bool PrepareAllowed=true;public int Clears,HoldCalls;
    public bool TryPrepareDecisionTransition(Decision current,string reason,Decision next)=>PrepareAllowed;
    public void ClearCommittedMovement(string reason)=>Clears++;
    public void ClearCommittedPosition(string reason)=>Clears++;
    public void ClearCommittedCover(string reason)=>Clears++;
    public void HoldFor(float seconds)=>HoldCalls++;
    public bool Emit(string name,out Decision d){d=new Decision(BotLogicDecision.holdPosition,name);return Active.Contains(name);}
    public void RefreshShootCover(){}public void ValidateCommittedCover(){}public void ClearInitialDecision(){}public void HoldCoverForMaxDuration(){}
    public bool HasInitialDecision=>Active.Contains("initial");public Decision ConsumeInitialDecision()=>new Decision(BotLogicDecision.holdPosition,"initial");
    public bool ShouldHoldForHealRetry()=>Active.Contains("healRetry");
    public bool TryGetReloadRetreatDecision(EnemyInfo e,out Decision d)=>Emit("reload",out d);
    public bool TryGetCombatLongGunPreparationDecision(EnemyInfo e,bool orderedPush,out Decision d)=>Emit("weapon",out d);
    public bool TryGetCommittedGrenadeDecision(out Decision d)=>Emit("grenade",out d);
    public bool HasCommittedPosition(out Decision d)=>Emit("position",out d);
    public bool TryGetCommittedMovementDecision(EnemyInfo e,bool a,bool b,out Decision d)=>Emit("movement",out d);
    public bool TryActivateFollowerGrenade(EnemyInfo e,out Decision d)=>Emit("newGrenade",out d);
    public bool ShouldAdvance(EnemyInfo e){AdvanceReads++;return Active.Contains("advance");}
}
public class Push(FollowerCombatCommon common)
{public int Clears;public void ClearCommittedPush(string reason)=>Clears++;public bool TryGetCommittedPushDecision(EnemyInfo e,out Decision d)=>common.Emit("push",out d);}
public class RouterHarness
{
    private const string BossHoldReason="bossHold",BossHoldOpenReason="bossHoldOpen";
    public FollowerCombatCommon combatCommon=new FollowerCombatCommon();private Owner botOwner=new Owner();private Push combatPush;
    public Func<EnemyInfo,Decision?> companionDecision;public Func<EnemyInfo,bool> companionAdvanceAllowed;
    public RouterHarness(){combatPush=new Push(combatCommon);}
    private Decision? preparedAdvanceDecision=new Decision(BotLogicDecision.holdPosition,"prepared");
    public bool HasPreparedAdvance=>preparedAdvanceDecision.HasValue;
    public int PushClears=>combatPush.Clears;
    public bool Advance(EnemyInfo e)=>ShouldAdvance(e);
    /* ROUTER */
    /* ADVANCE GATE */
    /* COMPANION STOP */
    private bool TryGetPreparedCloseCoverFightDecision(out Decision d)=>combatCommon.Emit("preparedFight",out d);
    private bool TryGetPreparedRecoveryDecision(out Decision d)=>combatCommon.Emit("preparedRecovery",out d);
    private bool TryGetImmediateFightDecision(out Decision d)=>combatCommon.Emit("fight",out d);
    private bool TryGetHealDecision(out Decision d)=>combatCommon.Emit("heal",out d);
    private bool TryGetAllySupportDecision(out Decision d)=>combatCommon.Emit("ally",out d);
    private bool ShouldPrioritizeRecoveryBeforeSupport(EnemyInfo e)=>false;
    private bool TryGetRecoverDecision(EnemyInfo e,out Decision d)=>combatCommon.Emit("recovery",out d);
    private bool TryGetBossDistanceRegroupDecision(out Decision d)=>combatCommon.Emit("regroup",out d);
    private bool TryGetPreparedAdvanceDecision(out Decision d)=>combatCommon.Emit("preparedAdvance",out d);
    private bool TryGetCommittedCoverDecision(EnemyInfo e,out Decision d)=>combatCommon.Emit("cover",out d);
    private bool TryGetOrderedPushDecision(EnemyInfo e,out Decision d)=>combatCommon.Emit("ordered",out d);
    private bool TryGetProtectorLowAggressionRegroupDecision(EnemyInfo e,out Decision d)=>combatCommon.Emit("protector",out d);
    private bool TryGetHoldPositionLocalSupportDecision(EnemyInfo e,out Decision d)=>combatCommon.Emit("local",out d);
    private bool TryGetBossUnderAttackDecision(EnemyInfo e,out Decision d)=>combatCommon.Emit("protect",out d);
    private bool TryGetAutonomousSuppressDecision(EnemyInfo e,out Decision d)=>combatCommon.Emit("suppress",out d);
    private bool TryGetVisibleDecision(EnemyInfo e,out Decision d)=>combatCommon.Emit("visible",out d);
    private bool TryGetAdvanceDecision(EnemyInfo e,out Decision d){d=new Decision(BotLogicDecision.holdPosition,"advance");return ShouldAdvance(e);}
    private bool HasExplicitRegroupOrder()=>false;private bool HasActivePushOrder()=>false;private bool ShouldBlockPassiveBossHold(EnemyInfo e)=>false;
}
public static class ExUsecRiflemanRouterFixture
{
    public static void Run()
    {
        int checks=0;Action<bool,string> check=(ok,message)=>{checks++;if(!ok)throw new Exception(message);};
        foreach(string protectedWork in new[]{"preparedFight","preparedRecovery","reload","fight","weapon","grenade","initial","heal","healRetry","ally","recovery","regroup","preparedAdvance","position","push","movement","cover","ordered"})
        {
            var r=new RouterHarness();int calls=0;r.companionDecision=e=>{calls++;return new Decision(BotLogicDecision.holdPosition,"commander");};
            r.combatCommon.Active.Add(protectedWork);
            var result=r.GetDecision(new EnemyInfo());
            check(result.Reason==protectedWork||protectedWork=="healRetry"&&result.Reason==FollowerCombatCommon.HealRetryHoldReason,"protected work retains router priority: "+protectedWork);
            check(calls==0,"commander is not evaluated/published behind protected work: "+protectedWork);
        }
        foreach(bool admitted in new[]{false,true})
        {
            var r=new RouterHarness();r.combatCommon.Active.Add("protector");
            r.companionDecision=e=>admitted?new Decision(BotLogicDecision.holdPosition,"commander"):(Decision?)null;
            check(r.GetDecision(new EnemyInfo()).Reason==(admitted?"commander":"protector"),"rejected commander plan leaves ordinary Rifleman selection intact");
        }
        foreach(bool normal in new[]{false,true})foreach(bool? permission in new bool?[]{null,false,true})
        {
            var r=new RouterHarness();if(normal)r.combatCommon.Active.Add("advance");
            if(permission.HasValue)r.companionAdvanceAllowed=e=>permission.Value;
            check(r.Advance(new EnemyInfo())==(normal&&permission!=false),"commander can restrict but cannot invent Rifleman advance willingness");
            check(r.combatCommon.AdvanceReads==(permission==false?0:1),"no discarded autonomous-provider evaluation");
        }
        foreach(bool prepare in new[]{false,true})
        {
            var r=new RouterHarness();r.combatCommon.PrepareAllowed=prepare;
            bool stopped=r.TryPrepareCompanionAdvanceStop(new Decision(BotLogicDecision.holdPosition,"push.automatic"),"wait",out var end);
            check(stopped==prepare&&end.Value==prepare,"automatic stop requires a prepared concrete successor");
            check(r.PushClears==(prepare?1:0)&&r.combatCommon.Clears==(prepare?3:0),"failed preparation preserves every active commitment");
            check(r.HasPreparedAdvance!=prepare,"only successful stop discards the old prepared advance");
            check(r.combatCommon.HoldCalls==(prepare?1:0),"only successful stop publishes a bounded hold deadline");
        }
        Console.WriteLine("PASS: "+checks+" extracted production Rifleman companion priority and autonomous-permission checks.");
    }
}

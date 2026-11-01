using EFT;
using pitTeam.BigBrain;
using pitTeam.GoingRogue.Combat;
using UnityEngine;

internal static class CommittedHoldChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(bool bigPipe in new[]{false,true})
        foreach(string reason in new[]{"committedCoverHold.recovery.retreatWeakCover.run","committedCoverHold.recovery.retreatSafeCover.run","committedPositionHold.recovery.retreatWeakCover"})
        foreach(string boundary in new[]{"arrivalGrace","expired","invalidated"})
        {
            Time.time=100;var owner=new BotOwner();
            var common=new FollowerCombatCommon{CommittedUntil=103,HoldUntil=0};
            GoonCombatObjective objective=bigPipe ? new BigPipeCombatObjective(owner,common):new KnightCombatObjective(owner,common);
            objective.StartDecision();
            var hold=new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.holdPosition,reason);
            if(boundary=="arrivalGrace")owner.Memory.IsUnderFire=true;
            if(boundary=="expired"){Time.time=104;common.HoldUntil=150;}
            if(boundary=="invalidated")common.CommittedInvalid=true;
            var end=objective.ShallEndCurrentDecision(hold);
            check(end.Value==(boundary!="arrivalGrace"),"Goon committed arrival has stable grace but finite deadline/invalidation: "+bigPipe+"/"+reason+"/"+boundary);
            check(common.Calls.Count(x=>x=="committedHoldEnd")==1 && !common.Calls.Contains("baseHoldEnd"),"Goon routes the original arrival decision to its single shared end owner");
        }
        foreach(bool bigPipe in new[]{false,true})
        foreach(string boundary in new[]{"blockedVisible","pressure","fire","expiry","medical"})
        {
            Time.time=100;var owner=new BotOwner();var common=new FollowerCombatCommon{Cover=true,Arrived=true};
            GoonCombatObjective objective=bigPipe ? new BigPipeCombatObjective(owner,common):new KnightCombatObjective(owner,common);
            objective.StartDecision();var hold=objective.GetDecision(owner.Memory.GoalEnemy);
            Time.time=100.1f;owner.Memory.GoalEnemy.IsVisible=boundary=="blockedVisible" || boundary=="fire";
            owner.Memory.IsUnderFire=boundary=="pressure";common.RecoveryMove=boundary=="pressure";common.Immediate=boundary=="fire";
            common.HealWork=boundary=="medical";if(boundary=="expiry")Time.time=103;
            var end=objective.ShallEndCurrentDecision(hold);
            check(end.Value==(boundary!="blockedVisible"),"Goon arrival pressure/visibility must prepare progress instead of ending into the same hold: "+bigPipe+"/"+boundary);
            if(boundary=="pressure" || boundary=="fire")
                check(common.PreparedTransition.HasValue && common.PreparedTransition.Value.Reason!=hold.Reason,"survival/fire interrupt publishes a concrete different successor");
            if(boundary=="blockedVisible")
            {
                int probes=common.Calls.Count(x=>x=="immediate");objective.ShallEndCurrentDecision(hold);
                check(common.Calls.Count(x=>x=="immediate")==probes,"failed arrival fire probe has bounded retry instead of a frame-rate loop");
            }
        }
        Time.time=100;
    }
}

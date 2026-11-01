using EFT;
using pitTeam.BigBrain;
using pitTeam.GoingRogue.Combat;
using UnityEngine;

internal static class GroupSearchChecks
{
    internal static void Run(Action<bool,string> check)
    {
        RecordedStairHandoff(check);
        ProgressDoesNotBlockInterruptions(check);
        SharedEndStillOwnsMovement(check);
        foreach(string mode in new[]{"shared","pressure","medical","reload","danger","visible","recentSight","blocked","launcher"})
        {
            Time.time=100;GoonPushLeadership.GroupSearch=true;
            var bot=new BotOwner();var enemy=bot.Memory.GoalEnemy;
            enemy.MemoryOnly=true;enemy.PersonalLastSeenTime=0;
            var c=new FollowerCombatCommon();var objective=new BigPipeCombatObjective(bot,c);objective.StartDecision();
            c.RecoveryPressure=mode=="pressure";c.HealWork=mode=="medical";c.Danger=mode=="danger";
            bot.WeaponManager.Reload.Reloading=mode=="reload";
            enemy.IsVisible=enemy.CanShoot=mode=="visible";enemy.PersonalLastSeenTime=mode=="recentSight"?99:0;
            c.GroupFollowPlan=mode!="blocked";
            c.UnplannedLauncher=mode=="launcher";
            var decision=objective.GetDecision(enemy);
            bool joins=decision.Reason.StartsWith("bigPipe.groupSearch");
            check(joins==(mode is "shared" or "blocked"),"positive group search respects personal urgency: "+mode);
            if(mode=="shared")
            {
                check(decision.Action==BotLogicDecision.goToPoint,"shared memory without personal sight creates leader-follow movement");
                check(c.Calls.Count(x=>x=="medicalProvider")==1,"joining search does not run an extra medical provider");
                objective.DecisionChanged(null,decision);
                check(pitTeam.Modules.FollowerEnemyTracking.GroupSearching && enemy.PersonalLastSeenTime==0 && enemy.MemoryOnly,
                    "actual group movement retains search memory without fabricating personal sight");
                objective.Deactivate();
                check(!pitTeam.Modules.FollowerEnemyTracking.GroupSearching,"command/objective interruption ends group tracking without marking the enemy searched");
            }
            if(mode=="blocked")
            {
                check(decision.Reason=="bigPipe.groupSearch.blocked","unreachable following uses a bounded group retry");
                for(int i=0;i<20;i++){Time.time=100+i/60f;check(!objective.ShallEndCurrentDecision(decision).Value,"blocked group hold does not churn each frame");}
            }
            if(mode=="launcher") check(decision.Reason=="launcherPrimaryFallback","group following restores an unplanned launcher through existing rifle maintenance");
            GoonPushLeadership.GroupSearch=false;
        }
        // Reserve stair regression: the next follow sample may fail while the current
        // native route is still making progress. Do not replace that route with StopMove.
        foreach(bool nextPlan in new[]{false,true})
        {
            Time.time=100;GoonPushLeadership.GroupSearch=true;
            var bot=new BotOwner();bot.Memory.GoalEnemy.PersonalLastSeenTime=0;
            var c=new FollowerCombatCommon();var objective=new BigPipeCombatObjective(bot,c);objective.StartDecision();
            var move=objective.GetDecision(bot.Memory.GoalEnemy);objective.DecisionChanged(null,move);var target=bot.GoToSomePointData.Point;
            bot.Mover.HasPathAndNoComplete=true;c.GroupFollowPlan=nextPlan;
            for(int i=1;i<=8;i++)
            {
                Time.time=100+i;bot.Position=new(i, i*0.5f,0);
                check(!objective.ShallEndCurrentDecision(move).Value,"progressing stair leg stays active despite next-plan result: "+nextPlan);
                check(!c.PreparedTransition.HasValue && (bot.GoToSomePointData.Point-target).sqrMagnitude==0,
                    "progressing leg retains published target and prepares no stop: "+nextPlan);
                check(c.Calls.Contains("sharedEnd"),"retaining the route still runs shared combat end handling");
            }
            Time.time=112;
            check(objective.ShallEndCurrentDecision(move).Value,"a genuinely stalled leg can refresh: "+nextPlan);
            check(c.PreparedTransition?.Reason==(nextPlan?"bigPipe.groupSearch.move":"bigPipe.groupSearch.blocked"),"stalled refresh prepares the actual successor");
        }
        {
            Time.time=100;GoonPushLeadership.GroupSearch=true;
            var bot=new BotOwner();bot.Memory.GoalEnemy.PersonalLastSeenTime=0;
            var c=new FollowerCombatCommon{TransitionAllowed=false};var objective=new BigPipeCombatObjective(bot,c);objective.StartDecision();
            var move=objective.GetDecision(bot.Memory.GoalEnemy);objective.DecisionChanged(null,move);
            var target=bot.GoToSomePointData.Point;bot.Mover.HasPathAndNoComplete=true;c.GroupFollowPlan=false;Time.time=105;
            check(!objective.ShallEndCurrentDecision(move).Value && !c.PreparedTransition.HasValue &&
                (bot.GoToSomePointData.Point-target).sqrMagnitude==0 && !c.Calls.Contains("clearCover"),
                "failed stalled-route handoff preserves the previous destination and commitments");
        }
        foreach(string mode in new[]{"airborne","unsupported","arrived","pathEnded","newEnemy","urgent"})
        {
            Time.time=100;GoonPushLeadership.GroupSearch=true;
            var bot=new BotOwner();bot.Memory.GoalEnemy.PersonalLastSeenTime=0;
            var c=new FollowerCombatCommon();var objective=new BigPipeCombatObjective(bot,c);objective.StartDecision();
            var move=objective.GetDecision(bot.Memory.GoalEnemy);objective.DecisionChanged(null,move);bot.Mover.HasPathAndNoComplete=true;c.GroupFollowPlan=false;
            if(mode=="airborne")bot.GetPlayer.MovementContext.IsGrounded=false;
            if(mode=="unsupported")FollowerCombatCommon.SupportedGround=false;
            if(mode=="arrived")bot.GoToSomePointData.Arrived=true;
            if(mode=="pathEnded")bot.Mover.HasPathAndNoComplete=false;
            if(mode=="newEnemy")bot.Memory.GoalEnemy.ProfileId="replacement";
            if(mode=="urgent")c.HealWork=true;
            Time.time=105;
            bool end=objective.ShallEndCurrentDecision(move).Value;
            check(end==(mode is not ("airborne" or "unsupported")),"routine handoff respects supported footing, completion, target and urgency: "+mode);
            if(mode=="urgent")check(c.PreparedTransition?.Reason=="bigPipe.groupSearch.release","medical priority is not retained by route progress");
            FollowerCombatCommon.SupportedGround=true;
        }
        foreach(bool prepared in new[]{false,true})
        {
            Time.time=100;GoonPushLeadership.GroupSearch=true;
            var bot=new BotOwner();bot.Memory.GoalEnemy.PersonalLastSeenTime=0;bot.Memory.GoalEnemy.MemoryOnly=true;
            var c=new FollowerCombatCommon{Settled=true,Cover=true,Arrived=true,TransitionAllowed=prepared,CommittedUntil=110};
            var objective=new BigPipeCombatObjective(bot,c);objective.StartDecision();
            var hold=new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.holdPosition,"committedCoverHold.recovery.safeCover.run");
            var end=objective.ShallEndCurrentDecision(hold);
            check(end.Value==prepared,"settled calm recovery can join an actual prepared group move: "+prepared);
            check(c.Cover==!prepared,"recovery cover is released only after successful handoff: "+prepared);
            GoonPushLeadership.GroupSearch=false;
        }
        {
            Time.time=100;GoonPushLeadership.GroupSearch=true;
            var bot=new BotOwner();bot.Memory.GoalEnemy.PersonalLastSeenTime=0;
            var c=new FollowerCombatCommon();var objective=new BigPipeCombatObjective(bot,c);objective.StartDecision();
            var move=objective.GetDecision(bot.Memory.GoalEnemy);Time.time=101;
            GoonPushLeadership.GroupSearch=false;
            check(objective.ShallEndCurrentDecision(move).Value && c.PreparedTransition?.Reason=="bigPipe.groupSearch.release",
                "ending Knight search prepares release instead of continuing a stale follower destination");
        }
    }

    private static void RecordedStairHandoff(Action<bool,string> check)
    {
        // 20261029-064631-RezervBase.jsonl: decision 104, snapshots 6784/6792/6799,
        // then blocked-hold transition 6804. Only pre-handoff positions are replayed;
        // the later fall is not a counterfactual physics simulation.
        var destination=new Vector3(-24.4613113f,0.0517458133f,125.803314f);
        Time.time=642.91749f;GoonPushLeadership.GroupSearch=true;
        // Last available position before publication: snapshot 6775 at 642.757324.
        var bot=new BotOwner{Position=new(-26.46759f,-6.94099426f,114.155563f)};
        bot.Memory.GoalEnemy.PersonalLastSeenTime=0;
        var c=new FollowerCombatCommon{GroupFollowPoint=destination};
        var objective=new BigPipeCombatObjective(bot,c);objective.StartDecision();
        var move=objective.GetDecision(bot.Memory.GoalEnemy);objective.DecisionChanged(null,move);
        bot.Mover.HasPathAndNoComplete=true;
        foreach(var frame in new[]{
            (642.95752f,new Vector3(-26.0305176f,-6.94099426f,114.873184f)),
            (643.15769f,new Vector3(-25.5085449f,-6.94099426f,115.730347f)),
            (643.55809f,new Vector3(-24.5818481f,-6.79119873f,117.252f))})
        {
            Time.time=frame.Item1;bot.Position=frame.Item2;
            check(!objective.ShallEndCurrentDecision(move).Value,"recorded pre-handoff stair movement remains active");
        }
        // At the recorded refresh time the next plan failed. Use the last observed
        // position, without inventing a new position between recorded snapshots.
        Time.time=643.67822f;c.GroupFollowPlan=false;
        check(!objective.ShallEndCurrentDecision(move).Value && !c.PreparedTransition.HasValue,
            "recorded failed next plan no longer prepares the destructive blocked hold");
        check((bot.GoToSomePointData.Point-destination).sqrMagnitude==0 && !c.Calls.Contains("clearCover"),
            "recorded stair handoff preserves the native destination and commitments");
        objective.Deactivate();GoonPushLeadership.GroupSearch=false;
    }

    private static void ProgressDoesNotBlockInterruptions(Action<bool,string> check)
    {
        foreach(string interrupt in new[]{"pressure","medical","reload","danger","visible","recentSight",
            "weaponDraw","handsReload","grenade","fallback","dead","leaderGone"})
        {
            Time.time=100;GoonPushLeadership.GroupSearch=true;FollowerCombatCommon.SupportedGround=false;
            var bot=new BotOwner();bot.Memory.GoalEnemy.PersonalLastSeenTime=0;
            var c=new FollowerCombatCommon();var objective=new BigPipeCombatObjective(bot,c);objective.StartDecision();
            var move=objective.GetDecision(bot.Memory.GoalEnemy);objective.DecisionChanged(null,move);
            Time.time=101;bot.Position=new(1,0,0);bot.Mover.HasPathAndNoComplete=true;
            switch(interrupt)
            {
                case "pressure":c.RecoveryPressure=true;break;
                case "medical":c.HealWork=true;break;
                case "reload":bot.WeaponManager.Reload.Reloading=true;break;
                case "danger":c.Danger=true;break;
                case "visible":bot.Memory.GoalEnemy.IsVisible=true;break;
                case "recentSight":bot.Memory.GoalEnemy.PersonalLastSeenTime=100;break;
                case "weaponDraw":bot.WeaponManager.Selector.IsChanging=true;break;
                case "handsReload":bot.WeaponManager.ShootController.Reloading=true;break;
                case "grenade":bot.WeaponManager.Grenades.ThrowindNow=true;break;
                case "fallback":c.LauncherFallback=true;break;
                case "dead":bot.Memory.GoalEnemy.Person.HealthController.IsAlive=false;break;
                case "leaderGone":GoonPushLeadership.GroupSearch=false;break;
            }
            check(objective.ShallEndCurrentDecision(move).Value && c.PreparedTransition?.Reason=="bigPipe.groupSearch.release",
                "neither progress nor unsupported-footing guard masks existing interruption: "+interrupt);
            objective.Deactivate();FollowerCombatCommon.SupportedGround=true;GoonPushLeadership.GroupSearch=false;
        }
    }

    private static void SharedEndStillOwnsMovement(Action<bool,string> check)
    {
        foreach(float probeAt in new[]{100.25f,101f})
        foreach(string commonReason in new[]{"underFire","arrivedAtPoint","tacticalPointStalled"})
        {
            Time.time=100;GoonPushLeadership.GroupSearch=true;
            var bot=new BotOwner();bot.Memory.GoalEnemy.PersonalLastSeenTime=0;
            var c=new FollowerCombatCommon();var objective=new BigPipeCombatObjective(bot,c);objective.StartDecision();
            var move=objective.GetDecision(bot.Memory.GoalEnemy);objective.DecisionChanged(null,move);
            bot.Mover.HasPathAndNoComplete=true;bot.Position=new(1,0,0);Time.time=probeAt;
            c.SharedEndResult=new AICoreActionEnd(commonReason,true);
            var end=objective.ShallEndCurrentDecision(move);
            check(end.Value && end.Reason==commonReason && !c.PreparedTransition.HasValue,
                "shared movement ending remains authoritative inside/outside refresh cooldown: "+commonReason);
            objective.Deactivate();GoonPushLeadership.GroupSearch=false;
        }
    }
}

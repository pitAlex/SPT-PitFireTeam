using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using pitTeam.GoingRogue.Combat;
using UnityEngine;

internal static class AggressorChecks
{
    internal static void Run(Action<bool,string> check)
    {
        (BotOwner actor,FollowerCombatCommon common,BotFollowerPlayer follower,GoonIndependentPursuit pursuit) Setup(WildSpawnType role)
        {
            Time.time=100;FollowerCombatAnchor.Independent=false;FollowerAwareness.Damaged=false;
            FollowerCombatCommon.RecentHitProbe=false;GoonPushLeadership.Knight=null;
            GoonPushLeadership.AdvanceAllowed=true;GoonPushLeadership.GroupSearch=false;
            KnightSquadCoordinator.Assigned=false;
            var actor=new BotOwner();actor.Profile.Info.Settings.Role=role;
            var enemy=actor.Memory.GoalEnemy;enemy.Person.Profile.Info.Settings.Role=WildSpawnType.assault;
            enemy.Distance=45;enemy.Anchor=new(45,0,0);enemy.MemoryOnly=true;
            var f=new BotFollowerPlayer{Owner=actor};actor.BotFollower.BossToFollow=f.Boss;
            BossPlayers.Instance.Followers[actor]=f;
            var c=new FollowerCombatCommon{ContactAdvance=true};
            return(actor,c,f,new(actor,c,"test.pursuit",10));
        }
        foreach(var role in new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe})
        foreach(string gate in new[]{"clear","harassment","underFire","damage","actualHit","critical","weapon","group","unknown","blocked",
            "medical","using","reload","drawing","hands","grenade","danger","hold","regroup","orderedPush","playerCommand","help","commanderRefused","elite","pmcPressure"})
        {
            var(a,c,f,p)=Setup(role);var e=a.Memory.GoalEnemy;
            switch(gate)
            {
                case "harassment":c.RecoveryPressure=true;break;
                case "underFire":c.RecoveryPressure=true;a.Memory.IsUnderFire=true;break;
                case "damage":c.RecoveryPressure=true;FollowerAwareness.Damaged=true;break;
                case "actualHit":c.RecoveryPressure=true;FollowerCombatCommon.RecentHitProbe=true;break;
                case "critical":c.RecoveryPressure=true;c.Critical=true;break;
                case "weapon":c.BlockWeaponThreat=true;break;
                case "group":a.KnownEnemies=3;break;
                case "unknown":e.Known=false;break;
                case "blocked":c.ContactAdvance=false;break;
                case "medical":c.HealWork=true;break;
                case "using":a.Medecine.Using=true;break;
                case "reload":a.WeaponManager.Reload.Reloading=true;break;
                case "drawing":a.WeaponManager.Selector.IsChanging=true;break;
                case "hands":a.WeaponManager.ShootController.Reloading=true;break;
                case "grenade":a.WeaponManager.Grenades.ReadyToThrow=true;break;
                case "danger":c.Danger=true;break;
                case "hold":c.HoldAggression=true;break;
                case "regroup":f.CombatRegroupUsesBossAnchor=true;break;
                case "orderedPush":f.HasOrderedPushTargetLock=true;break;
                case "playerCommand":f.Command=FollowerCommandType.SuppressEnemy;break;
                case "help":f.Boss.Logic.HasManualHelpRequest=true;break;
                case "commanderRefused":GoonPushLeadership.AdvanceAllowed=false;break;
                case "elite":e.Person.Profile.Info.Settings.Role=WildSpawnType.bossSanitar;break;
                case "pmcPressure":e.Person.Profile.Info.Settings.Role=WildSpawnType.pmcUSEC;c.RecoveryPressure=true;break;
            }
            bool expected=gate is "clear" or "harassment" or "underFire" || gate=="commanderRefused" && role==WildSpawnType.bossKnight;
            bool planned=p.TryGetDecision(out var next);
            check(planned==expected,"aggressor role admission and survival/authority gate: "+role+"/"+gate);
            check(c.Calls.Contains("commitContactAdvance")==planned,"only accepted plans publish a destination: "+gate);
            if(planned)check(next.Reason.EndsWith(".runToPoint"),"distant favourable assault requests running");
            BossPlayers.Instance.Followers.Remove(a);
        }
        {
            var(a,c,f,p)=Setup(WildSpawnType.followerBigPipe);a.HasLauncher=true;c.LauncherTarget=true;a.Memory.GoalEnemy.MemoryOnly=false;
            var objective=new BigPipeCombatObjective(a,c);objective.StartDecision();
            check(objective.GetDecision(a.Memory.GoalEnemy).Reason=="objectiveGrenadier.activateAuto",
                "favourable assault preserves Pipe's planned launcher opportunity");
            check(!c.Calls.Contains("planContactAdvance"),"launcher and movement retain one decision owner");
            objective.Deactivate();BossPlayers.Instance.Followers.Remove(a);
        }
        foreach(var role in new[]{WildSpawnType.followerBirdEye,WildSpawnType.exUsec})
        {
            var(a,c,f,p)=Setup(role);
            check(!p.TryGetDecision(out _),"supporter and subordinate do not acquire an aggressor policy: "+role);
            BossPlayers.Instance.Followers.Remove(a);
        }
        foreach(var role in new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe})
        {
            var(a,c,f,p)=Setup(role);c.Engagement=true;
            GoonCombatObjective objective=role==WildSpawnType.bossKnight?new KnightCombatObjective(a,c):new BigPipeCombatObjective(a,c);
            objective.StartDecision();var assault=objective.GetDecision(a.Memory.GoalEnemy);
            check(assault.Reason.Contains(".autonomousAdvance"),"normal assault precedes optional support and escort: "+role);
            check(!c.Calls.Contains("engagement"),"optional support cannot overwrite the offensive destination");
            objective.DecisionChanged(null,assault);
            check(GoonPushLeadership.PublishedAdvance,"normal aggressive approach publishes unit authority");
            // A native firing-position arrival follows an advance, then pauses without cancelling the unit task.
            objective.DecisionChanged(assault,new(BotLogicDecision.runToCover,role==WildSpawnType.bossKnight?"knight.cover.advance":"bigPipe.cover.toSFC"));
            var arrival=new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.holdPosition,
                role==WildSpawnType.bossKnight?"knight.coverArrival":"bigPipe.coverArrival");
            objective.DecisionChanged(null,arrival);
            check(GoonPushLeadership.PublishedAdvance,"ordinary arrival retains same-target advance intent");
            foreach(string pause in new[]{"committedPositionHold.tacticalPoint","committedCoverHold.tacticalPoint"})
            {
                objective.DecisionChanged(null,new(BotLogicDecision.holdPosition,pause));
                check(GoonPushLeadership.PublishedAdvance,"tactical point arrival retains the existing unit task: "+pause);
            }
            objective.DecisionChanged(null,new(BotLogicDecision.holdPosition,"committedPositionHold.recovery.retreat"));
            check(!GoonPushLeadership.PublishedAdvance,"recovery arrival cannot retain unit advance");
            objective.DecisionChanged(null,assault);
            f.Command=FollowerCommandType.Regroup;objective.DecisionChanged(arrival,arrival);
            check(!GoonPushLeadership.PublishedAdvance,"explicit command stops retained arrival authority");
            f.Command=FollowerCommandType.None;
            objective.DecisionChanged(null,assault);a.Memory.GoalEnemy.ProfileId="different";
            objective.DecisionChanged(null,arrival);
            check(!GoonPushLeadership.PublishedAdvance,"target changes cannot inherit old arrival authority");
            objective.Deactivate();BossPlayers.Instance.Followers.Remove(a);
        }
        FollowerAwareness.Damaged=false;FollowerCombatCommon.RecentHitProbe=false;
        GoonPushLeadership.AdvanceAllowed=true;GoonPushLeadership.Knight=null;FollowerCombatAnchor.Independent=false;
    }
}

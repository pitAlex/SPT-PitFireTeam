using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.GoingRogue.Combat;
using UnityEngine;

internal static class GoonRejoinChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(bool pipe in new[]{false,true})
        foreach(string gate in new[]{"none","nearby","recentFight","visible","shootable","underFire","medical","reload",
            "nativeReload","drawing","grenade","position","movement","orderedPush","playerCommand","HoldPosition","independent"})
        {
            Time.time=100;FollowerCombatAnchor.Independent=false;KnightSquadCoordinator.Assigned=false;
            var bot=new BotOwner();bot.Memory.GoalEnemy.Distance=145;
            var follower=new BotFollowerPlayer{Owner=bot};BossPlayers.Instance.Followers[bot]=follower;
            var common=new FollowerCombatCommon{BossPosition=new(100,0,0),BossNavDistance=100};
            GoonCombatObjective objective=pipe?new BigPipeCombatObjective(bot,common):new KnightCombatObjective(bot,common);
            objective.StartDecision();
            switch(gate)
            {
                case "nearby":common.BossPosition=new(15,0,0);common.BossNavDistance=15;break;
                case "recentFight":common.RecentFight=true;break;
                case "visible":bot.Memory.GoalEnemy.IsVisible=true;break;
                case "shootable":bot.Memory.GoalEnemy.CanShoot=true;break;
                case "underFire":bot.Memory.IsUnderFire=true;break;
                case "medical":common.Medical=true;break;
                case "reload":bot.WeaponManager.Reload.Reloading=true;break;
                case "nativeReload":bot.WeaponManager.ShootController.Reloading=true;break;
                case "drawing":bot.WeaponManager.Selector.IsChanging=true;break;
                case "grenade":common.Grenade=true;break;
                case "position":common.Position=true;break;
                case "movement":common.Movement=true;break;
                case "orderedPush":follower.HasOrderedPushTargetLock=true;break;
                case "playerCommand":follower.Command=FollowerCommandType.SuppressEnemy;break;
                case "HoldPosition":common.HoldAggression=true;break;
                case "independent":FollowerCombatAnchor.Independent=true;break;
            }
            var chosen=objective.GetDecision(bot.Memory.GoalEnemy);
            check((chosen.Reason=="regroup")==(gate=="none"),"idle Goon rejoins without stealing protected work: "+pipe+"/"+gate);
            check(bot.Memory.GoalEnemy?.Person.HealthController.IsAlive==true,"rejoin retains living enemy memory: "+gate);
            BossPlayers.Instance.Followers.Remove(bot);
        }
        FollowerCombatAnchor.Independent=false;
        foreach(bool pipe in new[]{false,true})
        {
            var bot=new BotOwner();var common=new FollowerCombatCommon{BossPosition=new(100,0,0),BossNavDistance=100,RecoveryEnded=true};
            BossPlayers.Instance.Followers[bot]=new BotFollowerPlayer{Owner=bot};
            GoonCombatObjective objective=pipe?new BigPipeCombatObjective(bot,common):new KnightCombatObjective(bot,common);
            objective.StartDecision();
            var oldHold=new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.holdPosition,FollowerCombatCommon.RecoveryNoCoverThreatHoldReason);
            check(objective.ShallEndCurrentDecision(oldHold).Value,"bounded no-cover retry releases through its existing end owner: "+pipe);
            check(objective.GetDecision(bot.Memory.GoalEnemy).Reason=="regroup","expired recovery retry hands off to regroup instead of renewing the same hold: "+pipe);
            BossPlayers.Instance.Followers.Remove(bot);
        }
    }
}

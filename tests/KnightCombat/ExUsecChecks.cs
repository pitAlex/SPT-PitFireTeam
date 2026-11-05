using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.GoingRogue.Combat;
using pitTeam.Modules;
using UnityEngine;

internal static class ExUsecChecks
{
    internal static void Run(Action<bool,string> check)
    {
        GoonCombatLayer.ReleaseAll();BossPlayers.Instance.Followers.Clear();
        foreach(string gate in new[]{"none","visible","shootable","medical","pressure","explosive","reload","draw","nativeReload","grenade","airborne","command","regroup","push","hold","humanHelp"})
        {
            Time.time=100;KnightSquadCoordinator.Assigned=true;KnightSquadCoordinator.Point=new(30,0,0);
            var human=new pitAIBossPlayer();var bot=new BotOwner();bot.Profile.Info.Settings.Role=WildSpawnType.exUsec;
            var leader=new BotOwner();leader.Profile.Info.Settings.Role=WildSpawnType.bossKnight;
            leader.Memory.GoalEnemy=bot.Memory.GoalEnemy;bot.Memory.GoalEnemy.Anchor=new(100,0,0);
            BossPlayers.Instance.Followers[leader]=new(){Owner=leader,Boss=human};
            var follower=new BotFollowerPlayer{Owner=bot,Boss=human};BossPlayers.Instance.Followers[bot]=follower;
            var common=new FollowerCombatCommon{FiringPoint=true};var objective=new ExUsecCombatObjective(bot,common);
            objective.StartDecision();check(FollowerCombatDefault.Last.Starts==0,"commander does not seed an independent Rifleman opener");
            switch(gate)
            {
                case "visible":bot.Memory.GoalEnemy.IsVisible=true;break;
                case "shootable":bot.Memory.GoalEnemy.CanShoot=true;break;
                case "medical":common.HealWork=true;break;
                case "pressure":common.RecoveryPressure=true;break;
                case "explosive":common.Danger=true;break;
                case "reload":bot.WeaponManager.Reload.Reloading=true;break;
                case "draw":bot.WeaponManager.Selector.IsChanging=true;break;
                case "nativeReload":bot.WeaponManager.ShootController.Reloading=true;break;
                case "grenade":bot.WeaponManager.Grenades.ThrowindNow=true;break;
                case "airborne":bot.GetPlayer.MovementContext.IsGrounded=false;break;
                case "command":follower.Command=FollowerCommandType.SuppressEnemy;break;
                case "regroup":follower.CombatRegroupUsesBossAnchor=true;break;
                case "push":follower.HasOrderedPushTargetLock=true;break;
                case "hold":common.HoldAggression=true;break;
                case "humanHelp":human.Logic.IsHitted=true;FollowerCombatAnchor.Independent=false;break;
            }
            GoonPushLeadership.GroupSearch=true;
            check(common.CompanionAdvanceOrder(bot.Memory.GoalEnemy)==(gate is "none" or "visible" or "shootable"),
                "authoritative commander advance retains protected work and player precedence: "+gate);
            GoonPushLeadership.GroupSearch=false;
            check(!common.CompanionAdvanceOrder(bot.Memory.GoalEnemy),"no live commander directive cannot bypass subordinate willingness");
            var next=objective.GetDecision(bot.Memory.GoalEnemy);
            check((next.Reason=="exUsec.command.position")== (gate=="none"),"commander position respects protected work: "+gate);
            objective.DecisionChanged(null,next);objective.ShallEndCurrentDecision(next);
            check(FollowerCombatDefault.Last.Changes==1&&FollowerCombatDefault.Last.Ends==1,"Rifleman retains publication and end ownership");
            BossPlayers.Instance.Followers.Clear();
        }
        KnightSquadCoordinator.Assigned=false;
        var solo=new BotOwner();solo.Profile.Info.Settings.Role=WildSpawnType.exUsec;
        BossPlayers.Instance.Followers[solo]=new(){Owner=solo};
        var plain=new ExUsecCombatObjective(solo,new());plain.StartDecision();
        check(FollowerCombatDefault.Last.Starts==1&&plain.GetDecision(solo.Memory.GoalEnemy).Reason=="rifleman","solo exUsec keeps ordinary Rifleman opener and selector");
        plain.Reset();BossPlayers.Instance.Followers.Clear();
        foreach(bool registered in new[]{false,true})
        {
            GoonCombatLayer.ReleaseAll();
            var bot=new BotOwner();bot.Profile.Info.Settings.Role=WildSpawnType.exUsec;bot.Brain.BaseBrain.BrainName="ExUsec";
            var request=new ExUsecPeacefulRequestLayer();var group=new GroupAttackLayer();var stationary=new StationaryWithSuppressLayer();
            foreach(var native in new AICoreLayer<BotLogicDecision>[]{request,group,stationary})
            {bot.Brain.BaseBrain._layers[native.Priority]=native;bot.Brain.BaseBrain._activeLayers.Add(native);}
            if(registered)BossPlayers.Instance.Followers[bot]=new(){Owner=bot};
            _=new ExUsecCombatLayer(bot,76);GoonCombatLayer.RefreshAll();
            check(GoonCombatLayer.IsSpawnReady(bot.ProfileId,"exUsec")==registered,"only registered exUsecs acknowledge dedicated combat readiness");
            check(bot.Brain.BaseBrain._activeLayers.Contains(request)!=registered&&bot.Brain.BaseBrain._activeLayers.Contains(group)!=registered,
                "Rogue-specific native request/attack layers are leased only for an owned teammate");
            GoonCombatLayer.ReleaseAll();
            check(bot.Brain.BaseBrain._activeLayers.Contains(request)&&bot.Brain.BaseBrain._activeLayers.Contains(group)&&
                bot.Brain.BaseBrain._activeLayers.Contains(stationary),"release restores existing native Rogue layers");
            BossPlayers.Instance.Followers.Clear();
        }
    }
}

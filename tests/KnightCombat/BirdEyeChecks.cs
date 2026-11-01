using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.GoingRogue.Combat;
using UnityEngine;

internal static class BirdEyeChecks
{
    internal static void Run(Action<bool,string> check)
    {
        (BotOwner bot,FollowerCombatCommon common,BirdEyeSniperPositioning policy,BotFollowerPlayer follower) Setup()
        {
            Time.time=200;KnightSquadCoordinator.Assigned=false;
            var bot=new BotOwner();bot.Profile.Info.Settings.Role=WildSpawnType.followerBirdEye;
            bot.Memory.GoalEnemy.Distance=100;bot.Memory.GoalEnemy.Anchor=new(100,0,0);
            var common=new FollowerCombatCommon{FiringPoint=true};
            var follower=new BotFollowerPlayer{Owner=bot};BossPlayers.Instance.Followers[bot]=follower;
            return(bot,common,new BirdEyeSniperPositioning(bot,common),follower);
        }
        {
            var(bot,common,policy,_)=Setup();
            var mate=new BotOwner(); mate.Memory.GoalEnemy.IsVisible=true;
            bot.BotsGroup.Members.AddRange(new[]{bot,mate});
            bot.Memory.GoalEnemy.GroupInfo.EnemyLastSeenTimeReal=Time.time-1;
            common.ContactAdvance=true;
            check(policy.GetDecision(bot.Memory.GoalEnemy)?.Reason=="sniper.birdEye.teamAdvance",
                "Birdeye advances on admitted contact while a teammate sees the same enemy");
            mate.Memory.GoalEnemy.ProfileId="other";policy.Reset();
            check(policy.GetDecision(bot.Memory.GoalEnemy)?.Reason!="sniper.birdEye.teamAdvance",
                "a different teammate fight cannot authorize advancing toward Birdeye's enemy");
            mate.Memory.GoalEnemy.ProfileId=bot.Memory.GoalEnemy.ProfileId;
            bot.Memory.GoalEnemy.GroupInfo.EnemyLastSeenTimeReal=Time.time-11;policy.Reset();
            check(policy.GetDecision(bot.Memory.GoalEnemy)?.Reason!="sniper.birdEye.teamAdvance",
                "stale group reports cannot start a native-style team advance");
        }
        {
            var(bot,common,policy,_)=Setup();
            check(policy.GetDecision(bot.Memory.GoalEnemy).HasValue,"Birdeye uses admitted native-distance firing position");
            check(common.AssignedPoint.x==40 && common.AssignedBackline && common.AssignedSniper,
                "native preferred 60m geometry retains Core backline and Marksman admission");
            int probes=common.Calls.Count;
            policy.GetDecision(bot.Memory.GoalEnemy);
            check(common.Calls.Count==probes,"position search is bounded to one probe per second");
            policy.Reset();check(policy.GetDecision(bot.Memory.GoalEnemy).HasValue,"new objective resets policy probe state");
        }
        foreach(string gate in new[]{"heal","danger","incomingFire","reload","nativeReload","drawing","unready","grenade",
            "readyGrenade","cleanShot","Regroup","consumedRegroup","NeedHelp","unknown","close","invalidAnchor","unsafePosition"})
        {
            var(bot,common,policy,follower)=Setup();
            switch(gate)
            {
                case "heal":common.HealWork=true;break;
                case "danger":common.Danger=true;break;
                case "incomingFire":bot.Memory.IsUnderFire=true;break;
                case "reload":bot.WeaponManager.Reload.Reloading=true;break;
                case "nativeReload":bot.WeaponManager.ShootController.Reloading=true;break;
                case "drawing":bot.WeaponManager.Selector.IsChanging=true;break;
                case "unready":bot.WeaponManager.IsWeaponReady=false;break;
                case "grenade":bot.WeaponManager.Grenades.ThrowindNow=true;break;
                case "readyGrenade":bot.WeaponManager.Grenades.ReadyToThrow=true;break;
                case "cleanShot":bot.Memory.GoalEnemy.IsVisible=bot.Memory.GoalEnemy.CanShoot=true;break;
                case "Regroup":follower.Command=FollowerCommandType.Regroup;break;
                case "consumedRegroup":follower.CombatRegroupUsesBossAnchor=true;break;
                case "NeedHelp":follower.Boss.Logic.IsHitted=true;break;
                case "unknown":bot.Memory.GoalEnemy.Known=false;break;
                case "close":bot.Memory.GoalEnemy.Distance=30;break;
                case "invalidAnchor":bot.Memory.GoalEnemy.Anchor=new(float.NaN,0,0);break;
                case "unsafePosition":common.FiringPoint=false;break;
            }
            check(!policy.GetDecision(bot.Memory.GoalEnemy).HasValue,"native preference defers to shared safety/weapon policy: "+gate);
        }
        {
            var(bot,common,policy,_)=Setup();
            KnightSquadCoordinator.Assigned=true;KnightSquadCoordinator.Point=new(20,0,15);
            check(policy.GetDecision(bot.Memory.GoalEnemy).HasValue && common.AssignedPoint.z==15,
                "Knight's firing position takes priority over autonomous native-distance planning");
            check(common.AssignedBackline&&common.AssignedSniper,"Knight cannot bypass sniper-position safety");
            common.FiringPoint=false;policy.Reset();
            int probes=common.Calls.Count;
            check(!policy.GetDecision(bot.Memory.GoalEnemy).HasValue && common.Calls.Count==probes+1,
                "rejected Knight point does not trigger another search inside the callback");
        }
        {
            var(bot,common,_,follower)=Setup();KnightSquadCoordinator.Assigned=false;
            var objective=new BirdEyeCombatObjective(bot,common);var sniper=FollowerCombatSniper.Last;
            objective.Activate();objective.DecisionChanged(null,new(BotLogicDecision.holdPosition,"sniper.closeWeaponPrepare"));
            check(sniper.Resets==1 && sniper.Changes==1,"Birdeye delegates lifecycle and secondary policy to shared Marksman");
            check(objective.ShallEndCurrentDecision(default).Value && sniper.Ends==1,"Marksman owns end/arrival transactions");
            var logic=new BirdEyeCombatLogic(bot);
            follower.Command=FollowerCommandType.PushEnemy;check(!logic.ProbePush(follower),"Birdeye never consumes an assault push");
            follower.Command=FollowerCommandType.NeedSniper;check(logic.ProbeNeedSniper(follower),"Birdeye accepts native sniper support command through Core");
            follower.Command=FollowerCommandType.SuppressEnemy;
            check(!logic.ProbeSuppress(follower),"sniper rifle cannot be forced into generic suppression");
            follower.SuppressEnemyUseAutomaticSecondary=true;check(logic.ProbeSuppress(follower),"automatic secondary may consume shared suppression fallback");
        }
        {
            var(bot,_,_,_)=Setup();bot.Brain.BaseBrain=new FollowerBirdEyeLayersStrategy{BrainName="BirdEye"};
            var fight=new BirdEyeFightLayer();var hold=new BirdEyeHoldLayer();
            bot.Brain.BaseBrain._layers[1]=fight;bot.Brain.BaseBrain._layers[2]=hold;
            bot.Brain.BaseBrain._activeLayers.AddRange(new AICoreLayer<BotLogicDecision>[]{hold,fight});
            var layer=new BirdEyeCombatLayer(bot,76);GoonCombatLayer.RefreshAll();
            check(GoonCombatLayer.IsSpawnReady(bot.ProfileId,"followerBirdEye")&&layer.IsActive(),"Birdeye readiness requires managed native-role ownership");
            check(bot.Brain.BaseBrain._activeLayers.Count==0,"both native fight and hold publication are leased");
            GoonCombatLayer.AbortSpawn(bot.ProfileId);
            check(bot.Brain.BaseBrain._activeLayers.Contains(fight)&&bot.Brain.BaseBrain._activeLayers.Contains(hold),"release restores initialized native sniper layers");
            check(fight.Activations==0&&hold.Activations==0,"restoration cannot duplicate native subscriptions");
        }
        KnightSquadCoordinator.Assigned=false;GoonCombatLayer.ReleaseAll();
        foreach(string scope in new[]{"managed","hostileRole","otherMode","foreignLeader"})
        {
            var(bot,_,_,follower)=Setup();
            if(scope=="hostileRole")bot.Profile.Info.Settings.Role=WildSpawnType.assault;
            if(scope=="otherMode")GameplayModeAddons.Current=GameplayMode.Allegiance;
            if(scope=="foreignLeader")follower.Boss.realPlayer.IsYourPlayer=false;
            typeof(BirdEyeFollowerTacticPatch).GetMethod("Prefix",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)
                .Invoke(null,new object[]{follower});
            check(follower.Tactic==(scope=="managed"?"Marksman":"Balanced"),"runtime tactic normalization stays scoped: "+scope);
            GameplayModeAddons.Current=GameplayMode.GoingRogue;
        }
    }
}

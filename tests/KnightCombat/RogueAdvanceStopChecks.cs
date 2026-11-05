using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.GoingRogue.Combat;
using pitTeam.Modules;
using UnityEngine;

internal static class RogueAdvanceStopChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(string gate in new[]{"none","blockedVisible","commanderAdvancing","shot","medical","reload","nativeReload","draw","grenade","pressure","explosive","airborne","unsupported","command","ordered","prepareFailed","dogfight"})
        {
            BossPlayers.Instance.Followers.Clear();Time.time=300;FollowerCombatCommon.SupportedGround=true;GoonPushLeadership.AdvanceAllowed=false;
            var bot=new BotOwner();bot.Profile.Info.Settings.Role=WildSpawnType.exUsec;
            var follower=new BotFollowerPlayer{Owner=bot};BossPlayers.Instance.Followers[bot]=follower;
            var common=new FollowerCombatCommon();var objective=new ExUsecCombatObjective(bot,common);
            switch(gate)
            {
                case "blockedVisible":bot.Memory.GoalEnemy.IsVisible=true;break;
                case "commanderAdvancing":GoonPushLeadership.AdvanceAllowed=true;break;
                case "shot":bot.Memory.GoalEnemy.IsVisible=bot.Memory.GoalEnemy.CanShoot=true;break;
                case "medical":common.HealWork=true;break;
                case "reload":bot.WeaponManager.Reload.Reloading=true;break;
                case "nativeReload":bot.WeaponManager.ShootController.Reloading=true;break;
                case "draw":bot.WeaponManager.Selector.IsChanging=true;break;
                case "grenade":bot.WeaponManager.Grenades.ThrowindNow=true;break;
                case "pressure":common.RecoveryPressure=true;break;
                case "explosive":common.Danger=true;break;
                case "airborne":bot.GetPlayer.MovementContext.IsGrounded=false;break;
                case "unsupported":FollowerCombatCommon.SupportedGround=false;break;
                case "command":follower.Command=FollowerCommandType.Regroup;break;
                case "ordered":follower.HasOrderedPushTargetLock=true;break;
                case "prepareFailed":FollowerCombatDefault.Last.StopAllowed=false;break;
            }
            var end=objective.ShallEndCurrentDecision(new(gate=="dogfight"?BotLogicDecision.dogFight:BotLogicDecision.goToEnemy,"push.automatic.test"));
            bool stops=gate=="none"||gate=="blockedVisible";
            check((end.Reason=="exUsec.command.wait")==stops,"automatic advance uses Pipe-style commander recheck: "+gate);
            check(FollowerCombatDefault.Last.Ends==(stops?0:1),"protected work and failed preparation retain Rifleman end owner: "+gate);
        }
        GoonPushLeadership.AdvanceAllowed=true;FollowerCombatCommon.SupportedGround=true;BossPlayers.Instance.Followers.Clear();
    }
}

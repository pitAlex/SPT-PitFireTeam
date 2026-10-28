using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.GoingRogue.Combat;
using UnityEngine;

internal static class BigPipeChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var cases = new[] {
            (false,false,20f,false,false,0f,1f,BigPipeFightChoice.Search),
            (false,false,1f,true,false,0f,1f,BigPipeFightChoice.ReloadHold),
            (true,true,0f,false,false,0f,0f,BigPipeFightChoice.Shoot),
            (false,false,1f,false,true,1f,1f,BigPipeFightChoice.CoverHold),
            (false,false,1f,false,false,0f,31f,BigPipeFightChoice.StaleContactHold),
            (false,false,1f,false,false,0f,1f,BigPipeFightChoice.Pressure),
            (true,false,0f,false,false,0f,0f,BigPipeFightChoice.Pressure)
        };
        foreach(var (visible,canShoot,unseen,reload,inCover,coverAge,groupAge,expected) in cases)
            check(BigPipeDecisionPolicy.Select(visible,canShoot,unseen,10,reload,inCover,coverAge,2,groupAge)==expected,
                "Big Pipe retains native selector branch order: "+expected);
        foreach(float distance in new[]{26.9f,27f,80f,130f,130.1f,float.NaN,float.PositiveInfinity})
            check(BigPipeDecisionPolicy.InLauncherRange(distance)==(distance>=27 && distance<=130),"native Big Pipe launcher distance envelope: "+distance);

        (BotOwner bot,FollowerCombatCommon c,BigPipeCombatObjective o) Setup()
        {
            Time.time=100;KnightSquadCoordinator.Assigned=false;
            var bot=new BotOwner{HasLauncher=true};bot.Memory.GoalEnemy.Distance=50;
            var c=new FollowerCombatCommon{LauncherTarget=true};var o=new BigPipeCombatObjective(bot,c);o.StartDecision();return(bot,c,o);
        }
        foreach(string priority in new[]{"reload","dogfight","grenade","medical","healRetry","weapon","position","movement","cover"})
        {
            var(bot,c,o)=Setup();
            KnightSquadCoordinator.Assigned=true;KnightSquadCoordinator.Point=new Vector3(20,0,0);c.FiringPoint=true;
            switch(priority){case "reload":c.Reload=true;break;case "dogfight":c.DogFight=true;break;case "grenade":c.Grenade=true;break;
                case "medical":c.Medical=true;break;case "healRetry":c.HealRetry=true;break;case "weapon":c.LongGunPreparation=true;break;
                case "position":c.Position=true;break;case "movement":c.Movement=true;break;default:c.Cover=true;break;}
            var selected=o.GetDecision(bot.Memory.GoalEnemy);
            check(selected.Reason!="objectiveGrenadier.activateAuto" && !c.Calls.Contains("launcherPlan"),"Big Pipe launcher cannot steal protected work: "+priority);
            check(c.Calls.Count(call=>call=="medicalProvider")<=1,"Big Pipe has one medical selector: "+priority);
            check(selected.Reason!="assignedPosition","Knight assignment cannot steal protected work: "+priority);
        }
        {
            var(bot,c,o)=Setup();c.Engagement=true;
            check(o.GetDecision(bot.Memory.GoalEnemy).Reason=="objectiveGrenadier.activateAuto","Big Pipe requests Core grenadier activation for a safe native-range opportunity");
            check(c.Calls.Count(x=>x=="launcherPlan")==1 && !c.Calls.Contains("engagement"),"launcher opportunity is planned once before optional rifle support");
            o.GetDecision(bot.Memory.GoalEnemy);
            check(c.Calls.Count(x=>x=="launcherPlan")==1,"launcher opportunity probe has a bounded retry");
        }
        foreach(var role in new[]{WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye})
        {
            var(bot,c,o)=Setup();bot.Profile.Info.Settings.Role=role;
            KnightSquadCoordinator.Assigned=true;KnightSquadCoordinator.Point=new Vector3(20,0,0);c.FiringPoint=true;
            check(o.GetDecision(bot.Memory.GoalEnemy).Reason=="assignedPosition","shared Goon objective consumes a Knight position for "+role);
            check(c.AssignedSniper==(role==WildSpawnType.followerBirdEye) && c.AssignedBackline==c.AssignedSniper,"Knight assignments preserve the role's sniper policy: "+role);
        }
        foreach(string reason in new[]{"noLauncher","tooClose","tooFar","memoryOnly","underFire","cooldown","fallback","unsafePlan"})
        {
            var(bot,c,o)=Setup();
            switch(reason){case "noLauncher":bot.HasLauncher=false;break;case "tooClose":bot.Memory.GoalEnemy.Distance=26;break;
                case "tooFar":bot.Memory.GoalEnemy.Distance=131;break;case "memoryOnly":bot.Memory.GoalEnemy.MemoryOnly=true;break;
                case "underFire":bot.Memory.IsUnderFire=true;break;case "cooldown":c.LauncherCooldown=true;break;
                case "fallback":c.LauncherFallback=true;break;default:c.LauncherTarget=false;break;}
            check(o.GetDecision(bot.Memory.GoalEnemy).Reason!="objectiveGrenadier.activateAuto","unsafe/unavailable launcher cannot activate: "+reason);
        }
        {
            var(bot,c,o)=Setup();c.LauncherTarget=false;c.UnplannedLauncher=true;
            bot.Memory.GoalEnemy.IsVisible=bot.Memory.GoalEnemy.CanShoot=true;
            check(o.GetDecision(bot.Memory.GoalEnemy).Reason=="launcherPrimaryFallback" && c.LauncherFallback,"unplanned native activation launcher draw hands back to shared primary recovery before rifle fire");
        }
        {
            var(bot,c,_)=Setup();var selector=new BigPipeVanillaCombat(bot);selector.Begin(c);
            bot.WeaponManager.Reload.Reloading=true;
            var hold=selector.GetDecision();bot.Memory.GoalEnemy.IsVisible=bot.Memory.GoalEnemy.CanShoot=true;
            check(!selector.ShallEndCurrentDecision(hold).Value,"native reload hold does not churn on visible contact while reload is active");
            bot.WeaponManager.Reload.Reloading=false;
            check(selector.ShallEndCurrentDecision(hold).Value,"finished native reload releases immediately for visible fire");
        }
        {
            var(bot,c,o)=Setup();c.LauncherTarget=false;bot.Memory.GoalEnemy.IsVisible=bot.Memory.GoalEnemy.CanShoot=true;
            check(o.GetDecision(bot.Memory.GoalEnemy).Reason=="bigPipe.native.ShootFromPl8","Big Pipe retains native immediate rifle fire");
        }
        {
            var(bot,c,_)=Setup();var selector=new BigPipeVanillaCombat(bot);selector.Begin(c);
            bot.Memory.IsInCover=true;bot.Memory.ComeToCoverTime=100;
            var hold=selector.GetDecision();
            check(hold.Reason=="bigPipe.native.bossKillaHo" && !selector.ShallEndCurrentDecision(hold).Value,"native cover hold does not restart each frame");
            Time.time=103;check(selector.ShallEndCurrentDecision(hold).Value,"native cover hold has a finite deadline");
        }
        {
            var(bot,c,_)=Setup();var selector=new BigPipeVanillaCombat(bot);selector.Begin(c);
            bot.Memory.GoalEnemy.TimeLastSeenReal=80;
            check(selector.GetDecision().Reason=="bigPipe.native.search.walk","native lost-contact search uses shared memory-only action");
            c.RecoveryPressure=true;c.RecoveryMove=true;
            check(selector.GetDecision().Reason=="medicalRecoveryCover","recovery pressure preempts an unsafe search/hold");
        }
        {
            var(bot,c,o)=Setup();var selector=new BigPipeVanillaCombat(bot);selector.Begin(c);
            KnightSquadCoordinator.Assigned=true;KnightSquadCoordinator.Point=new Vector3(20,0,0);c.FiringPoint=true;
            check(o.GetDecision(bot.Memory.GoalEnemy).Reason=="assignedPosition","Big Pipe admits Knight's actual assigned point before ordinary native combat or launcher opportunities");
            KnightSquadCoordinator.Assigned=false;c.FiringPoint=false;c.CandidateCover=true;
            check(selector.GetDecision().Reason=="bigPipe.cover.toSFC","native shooting cover uses the shared committed cover destination");
        }
        {
            var(bot,c,_)=Setup();var selector=new BigPipeVanillaCombat(bot);selector.Begin(c);c.Suppress=true;
            bot.Memory.GoalEnemy.WantsSuppress=true;
            check(selector.GetDecision().Action==BotLogicDecision.suppressFire,"Big Pipe native pressure burst uses shared suppression");
            int bursts=c.Calls.Count(x=>x=="leaderSuppress");selector.GetDecision();
            check(c.Calls.Count(x=>x=="leaderSuppress")==bursts,"native suppression burst cannot repeat immediately");
            Time.time=121;bot.Memory.GoalEnemy.TimeLastSeenReal=120;selector.GetDecision();
            check(c.Calls.Count(x=>x=="leaderSuppress")==bursts,"stationary target does not repeat native burst");
            bot.Memory.GoalEnemy.Anchor=new Vector3(34,0,0);
            check(selector.GetDecision().Action==BotLogicDecision.suppressFire,"native burst may refresh after cooldown and target movement");
        }
        foreach(float distance in new[]{0f,0.5f,2f,2.1f})
        {
            var(bot,c,_)=Setup();var selector=new BigPipeVanillaCombat(bot);selector.Begin(c);
            c.CandidateCover=true;c.CandidatePosition=new Vector3(distance,0,0);c.LowThreat=true;
            var decision=selector.GetDecision();
            check((decision.Reason=="bigPipe.cover.toSFC")== (distance>2f),"Big Pipe cannot schedule a new cover move/arrival transaction at its already reached firing point: "+distance);
            check(distance>2f || !c.Calls.Contains("commitSelectedCover"),"no-op cover selection cannot restart the committed hold");
        }
        {
            var(bot,c,_)=Setup();var selector=new BigPipeVanillaCombat(bot);selector.Begin(c);
            bot.Memory.GoalEnemy.GroupInfo.EnemyLastSeenTimeReal=60;
            check(selector.GetDecision().Reason=="bigPipe.native.KILLA_CLOSEATTACK_TIMES","native stale-contact wait is retained once");
            Time.time=103;bot.Memory.GoalEnemy.TimeLastSeenReal=102;
            check(selector.GetDecision().Reason=="bigPipe.staleContactSearch.walk","unchanged stale contact cannot repeat the same native wait");
            bot.Memory.GoalEnemy.GroupInfo.EnemyLastSeenTimeReal=65;
            check(selector.GetDecision().Reason=="bigPipe.native.KILLA_CLOSEATTACK_TIMES","a genuinely different report may own a new wait");
            bot.Memory.GoalEnemy.IsVisible=bot.Memory.GoalEnemy.CanShoot=true;selector.GetDecision();
            bot.Memory.GoalEnemy.IsVisible=bot.Memory.GoalEnemy.CanShoot=false;
            check(selector.GetDecision().Reason=="bigPipe.native.KILLA_CLOSEATTACK_TIMES","new visible contact rearms the bounded wait");
        }
        {
            var(bot,c,_)=Setup();var selector=new BigPipeVanillaCombat(bot);selector.Begin(c);
            c.CoverShot=true;c.CandidateCover=true;c.CandidatePosition=new Vector3(0.5f,0,0);
            check(selector.GetDecision().Reason=="bigPipe.coverFire" && !c.Calls.Contains("shootCoverSearch"),"productive current-cover fire is retained instead of forcing relocation");
        }
        {
            var logic=new BigPipeCombatLogic(new BotOwner());var follower=new BotFollowerPlayer{Command=FollowerCommandType.PushEnemy};
            check(logic.ProbePush(follower) && !logic.ProbeSuppress(follower),"Big Pipe accepted Go Forward enters ordered push");
            follower.Command=FollowerCommandType.SuppressEnemy;
            check(logic.ProbeSuppress(follower) && !logic.ProbePush(follower),"Big Pipe consumes suppression independently of push");
            follower.Command=FollowerCommandType.None;
            check(!logic.ProbePush(follower) && !logic.ProbeSuppress(follower),"Big Pipe cannot invent accepted commands");
        }
        foreach(var(role,brain) in new[]{("bossKnight","Knight"),("followerBigPipe","BigPipe")})
        {
            check(GoonCombatScope.Matches(role,brain,"GoingRogue",role,brain,true,true,true),"Goon scope admits its exact managed role: "+role);
            check(!GoonCombatScope.Matches(role,brain,"Allegiance",role,brain,true,true,true),"Goon scope excludes other modes");
            check(!GoonCombatScope.Matches(role,brain,"GoingRogue",role,brain,false,true,true),"Goon scope excludes hostile/non-followers");
            check(!GoonCombatScope.Matches(role,brain,"GoingRogue",role,brain,true,false,true),"Goon scope excludes inactive/dead actors");
            check(!GoonCombatScope.Matches(role,brain,"GoingRogue",role,brain,true,true,false),"Goon scope excludes unrelated leaders");
            check(!GoonCombatScope.Matches(role,brain,"GoingRogue",role,"Other",true,true,true),"Goon scope excludes replaced brains");
        }
        KnightSquadCoordinator.Assigned=false;
    }
}

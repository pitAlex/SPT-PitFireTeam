using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.GoingRogue.Combat;
using UnityEngine;

int checks = 0;
void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
(BotOwner bot, FollowerCombatCommon common, KnightCombatObjective objective) Setup()
{
    Time.time = 100;
    var bot = new BotOwner();
    var common = new FollowerCombatCommon();
    var objective = new KnightCombatObjective(bot, common);
    objective.StartDecision();
    return (bot, common, objective);
}

foreach (var branch in new[] { "reload", "dogfight", "grenade", "medical", "healRetry", "position", "movement", "committedCover" })
{
    var (bot,c,o) = Setup();
    c.Engagement=true;
    switch(branch)
    {
        case "reload": c.Reload=true; break; case "dogfight": c.DogFight=true; break;
        case "grenade": c.Grenade=true; break; case "medical": c.Medical=true; break;
        case "healRetry": c.HealRetry=true; break; case "position": c.Position=true; break;
        case "movement": c.Movement=true; break; case "committedCover": c.Cover=true; break;
    }
    int before=KnightVanillaCombat.Decisions;
    Check(o.GetDecision(bot.Memory.GoalEnemy).Reason==branch, branch+" retains priority");
    Check(!c.Calls.Contains("pushEvent") && !c.Calls.Contains("engagement"), branch+" prevents support replanning");
    Check(KnightVanillaCombat.Decisions==before, branch+" never invokes a second selector");
}
{
    var (bot,c,o)=Setup(); c.LongGunPreparation=true; c.Movement=true; c.Engagement=true;
    int before=KnightVanillaCombat.Decisions;
    Check(o.GetDecision(bot.Memory.GoalEnemy).Reason=="weaponSwitchLongGun.pistolReload", "shared rifle recovery precedes search/cover/support planning");
    Check(KnightVanillaCombat.Decisions==before && !c.Calls.Contains("movement") && !c.Calls.Contains("engagement"), "rifle preparation has one owner and cannot start an assault");
}
foreach(var priority in new[]{"reload","dogfight","grenade","medical","healRetry"})
{
    var (bot,c,o)=Setup(); c.LongGunPreparation=true;
    switch(priority) {case "reload":c.Reload=true;break;case "dogfight":c.DogFight=true;break;
        case "grenade":c.Grenade=true;break;case "medical":c.Medical=true;break;default:c.HealRetry=true;break;}
    Check(o.GetDecision(bot.Memory.GoalEnemy).Reason==priority && !c.Calls.Contains("weaponSwitchLongGun.pistolReload"), priority+" retains precedence over rifle preparation");
}
foreach(var action in new[]{BotLogicDecision.goToEnemy,BotLogicDecision.holdPosition})
{
    var (bot,c,o)=Setup(); c.LongGunPreparation=true;
    var end=o.ShallEndCurrentDecision(new(action,"knight.assaultSearch.walk"));
    Check(end.Value && end.Reason=="knight.prepareLongGun" && c.PreparedTransition?.Reason=="weaponSwitchLongGun.pistolReload", "long-lived action releases only with its prepared weapon successor: "+action);
    Check(c.Calls.Count(value=>value=="weaponSwitchLongGun.pistolReload")==1 && !c.Calls.Contains("engagement"), "weapon handoff does not replan support: "+action);
}
foreach(var hold in new[]{"weaponSwitchLongGun.pistolReload","weaponPrepareLongGun.reloadWait","reloadLongGun"})
{
    var (bot,c,o)=Setup(); c.LongGunPreparation=true;
    var current=new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.holdPosition,hold);
    Check(!o.ShallEndCurrentDecision(current).Value && !c.Calls.Contains("weaponSwitchLongGun.pistolReload"), "shared transition/reload lifetime cannot be replaced by support: "+hold);
    c.WeaponSettled=c.ReloadEnded=true;
    Check(o.ShallEndCurrentDecision(current).Value, "shared transition/reload owner completes its own hold: "+hold);
}
foreach(var urgent in new[]{"medical","danger","underFire","throwing"})
{
    var (bot,c,o)=Setup(); c.LongGunPreparation=true;
    switch(urgent) {case "medical":c.Medical=true;break;case "danger":c.Danger=true;break;
        case "underFire":bot.Memory.IsUnderFire=true;break;default:bot.WeaponManager.Grenades.ThrowindNow=true;break;}
    o.ShallEndCurrentDecision(new(BotLogicDecision.goToEnemy,"knight.assaultSearch.walk"));
    Check(!c.PreparedTransition.HasValue && !c.Calls.Contains("weaponSwitchLongGun.pistolReload"), urgent+" prevents optional weapon handoff");
}
{
    var (bot,c,o)=Setup(); c.Cover=true; c.Arrived=true; c.Medical=true;
    Check(o.GetDecision(bot.Memory.GoalEnemy).Action==BotLogicDecision.heal,"heal cover arrival goes directly to treatment");
}
{
    var (bot,c,o)=Setup(); c.Cover=true; c.Arrived=true;
    Check(o.GetDecision(bot.Memory.GoalEnemy).Reason=="knight.coverArrival","ordinary cover arrival settles");
    var current=new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.holdPosition,"knight.coverArrival");
    Time.time=101;
    Check(!o.ShallEndCurrentDecision(current).Value,"arrival hold does not churn before settle");
    Time.time=102.5f;
    Check(o.ShallEndCurrentDecision(current).Value,"arrival settles at bounded deadline");
    Check(o.GetDecision(bot.Memory.GoalEnemy).Reason=="vanilla","completed cover resumes Knight selection");
}
{
    var (bot,c,o)=Setup(); c.Settled=true; c.HoldUntil=200;
    var hold=new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.holdPosition,"passive");
    Check(!o.ShallEndCurrentDecision(hold).Value,"failed support plan cannot tear down hold");
    int attempts=c.Calls.Count(x=>x=="engagement");
    Time.time=100.5f;
    Check(!o.ShallEndCurrentDecision(hold).Value && c.Calls.Count(x=>x=="engagement")==attempts,"failed support backs off");
    c.Engagement=true; Time.time=101;
    Check(o.ShallEndCurrentDecision(hold).Reason=="knight.preparedAllySupport","successful plan permits support hold break");
    int built=c.Calls.Count(x=>x=="engagement");
    Check(o.GetDecision(bot.Memory.GoalEnemy).Reason=="allyEngagement","prepared successor consumed");
    Check(c.Calls.Count(x=>x=="engagement")==built,"prepared successor does not plan twice");
}
{
    var (bot,c,o)=Setup(); c.Engagement=true;
    Check(o.GetDecision(bot.Memory.GoalEnemy).Reason=="allyEngagement","support begins with a concrete plan");
    c.Cover=true; c.Arrived=true; c.Engagement=false;
    Check(o.GetDecision(bot.Memory.GoalEnemy).Reason=="knight.coverArrival","support arrival starts its hold lifecycle");
    Time.time=102.5f;
    Check(o.GetDecision(bot.Memory.GoalEnemy).Reason=="knight.supportHold" && c.Cover,"support cover survives the initial settle window");
    var hold=new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.holdPosition,"knight.supportHold");
    Check(!o.ShallEndCurrentDecision(hold).Value,"support hold remains bounded without early replanning");
    Time.time=108;
    Check(o.ShallEndCurrentDecision(hold).Value,"support hold expires at its deadline");
    Check(o.GetDecision(bot.Memory.GoalEnemy).Reason=="vanilla" && !c.Cover,"expired support cover returns to Knight selection");
}
{
    var (bot,c,o)=Setup();
    var hold=new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.holdPosition,"knight.noCover");
    Check(!o.ShallEndCurrentDecision(hold).Value,"failed native cover plan backs off");
    Time.time=101;
    Check(o.ShallEndCurrentDecision(hold).Value,"native cover retry becomes eligible at the deadline");
}
foreach(var gate in new[]{"medical","danger","underFire","throwing","readyGrenade"})
{
    var (bot,c,o)=Setup(); c.Settled=true; c.Engagement=true; c.HoldUntil=200;
    switch(gate) { case "medical":c.Medical=true;break; case "danger":c.Danger=true;break;
        case "underFire":bot.Memory.IsUnderFire=true;break; case "throwing":bot.WeaponManager.Grenades.ThrowindNow=true;break;
        default:bot.WeaponManager.Grenades.ReadyToThrow=true;break; }
    o.ShallEndCurrentDecision(new(BotLogicDecision.holdPosition,"passive"));
    Check(!c.Calls.Contains("engagement"),gate+" prevents support hold-break planning");
}
foreach(var path in new[]{"cover","immediate","searchHold","teamSearch","pushCover","firingPosition","engagement","none"})
{
    var bot=new BotOwner(); var c=new FollowerCombatCommon { NearbyPush=path!="engagement" && path!="none" };
    switch(path)
    {
        case "cover":c.CoverShot=true;break; case "immediate":c.Immediate=true;break;
        case "searchHold":c.Push.IsSearchPush=true;c.Position=true;break;
        case "teamSearch":c.Push.IsSearchPush=true;c.TeamSearch=true;break;
        case "pushCover":c.PushCover=true;break;case "firingPosition":c.FiringPoint=true;break;
        case "engagement":c.Engagement=true;break;
    }
    var planner=new FollowerCombatSupportPlanner(bot,c);
    bool success=planner.TryBuild(out var d);
    Check(success==(path!="none"),"support planner resolves "+path);
    Check(!success || (d.Action!=BotLogicDecision.runToEnemy && d.Action!=BotLogicDecision.goToEnemy),"support never copies assault destination: "+path);
    if(path=="searchHold") Check(!c.Calls.Contains("teamSearch") && !c.Calls.Contains("pushCover"),"search support preserves arrival hold");
    if(path=="cover") Check(c.Calls.Contains("extendCover"),"current-cover assistance extends commitment");
}
{
    var (bot,c,o)=Setup(); var human=new pitAIBossPlayer { Attacker=new BotOwner() }; human.Logic.IsHitted=true;
    bot.BotFollower.BossToFollow=human; c.Immediate=true;
    Check(o.GetDecision(bot.Memory.GoalEnemy).Reason=="knight.needHelpFire","Need Help uses admitted leader attacker");
    Check(c.Calls.Contains("admitLeaderThreat"),"help requires shared target admission");
    var (bot2,c2,o2)=Setup(); bot2.BotFollower.BossToFollow=human; c2.Admission=false;
    Check(o2.GetDecision(bot2.Memory.GoalEnemy).Reason=="vanilla","rejected help contact cannot seize combat");
}
{
    var (bot,c,o)=Setup(); int before=KnightVanillaCombat.Decisions;
    Check(o.GetDecision(bot.Memory.GoalEnemy).Reason=="vanilla" && KnightVanillaCombat.Decisions==before+1,"ordinary combat selects native replica exactly once");
    int disposed=KnightVanillaCombat.Disposals;
    o.Reset(); Check(KnightVanillaCombat.Disposals==disposed+1,"reset disposes replica subscriptions");
}
{
    var bot=new BotOwner(); var brain=bot.Brain.BaseBrain;
    var danger=new AICoreLayer<BotLogicDecision>("AvoidDanger",80); var malfunction=new AICoreLayer<BotLogicDecision>("Malfunction",78);
    var native=new BossKnightFightLayer(); var sain=new AICoreLayer<BotLogicDecision>("SAIN : Combat Layer",62);
    var building=new AssaultBuildingLayer(); var patrol=new AICoreLayer<BotLogicDecision>("FollowerPatrol",71);
    var ordered=new AICoreLayer<BotLogicDecision>[] { danger,malfunction,building,patrol,native,sain };
    for(int i=0;i<ordered.Length;i++) { brain._layers[i]=ordered[i];brain._activeLayers.Add(ordered[i]); }
    var original=brain._activeLayers.ToArray(); var lease=new GoonLayerLease(bot); lease.Refresh();
    Check(brain._activeLayers.SequenceEqual(new[]{danger,malfunction,patrol}),"lease preserves danger, malfunction and Core peaceful ownership");
    Check(bot.Brain.Agent.Deactivated.Count==3,"conflicting active actions deactivate exactly once");
    lease.Refresh(); Check(bot.Brain.Agent.Deactivated.Count==3,"lease refresh is idempotent");
    lease.Dispose(); Check(brain._activeLayers.SequenceEqual(original),"lease restores original priority/order");
    Check(original.All(layer=>layer.Activations==0),"restoration does not reactivate native subscriptions");
    lease.Dispose(); Check(brain._activeLayers.Count==original.Length,"release is idempotent");
    var stale=new GoonLayerLease(bot); stale.Refresh(); bot.Brain.BaseBrain=new BaseBrain(); stale.Dispose();
    Check(bot.Brain.BaseBrain._activeLayers.Count==0,"brain replacement never receives stale layers");
}
foreach (var mode in new[] { "medicalReady", "safeRetry", "exposedRetry", "quietFailedRetry", "noCover", "medicalCleared", "enemyGone", "activeUse" })
{
    var (bot,c,o)=Setup();
    var current = new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.holdPosition,FollowerCombatCommon.HealRetryHoldReason);
    c.HealWork=true;
    switch(mode)
    {
        case "medicalReady": c.Medical=true; c.RecoveryMove=true; break;
        case "safeRetry": c.HealRetry=true; c.SafeHeal=true; c.RecoveryPressure=true; c.Immediate=true; break;
        case "exposedRetry": c.HealRetry=true; c.RecoveryPressure=true; c.RecoveryMove=true; break;
        case "quietFailedRetry": c.RecoveryMove=true; break;
        case "medicalCleared": c.HealWork=false; break;
        case "enemyGone": bot.Memory.GoalEnemy=null; break;
        case "activeUse": c.MedicalUsing=true; c.RecoveryMove=true; break;
    }
    var end=o.ShallEndCurrentDecision(current);
    Check(c.Calls.Count(x=>x=="medicalProvider")<=1,"medical retry provider has one owner: "+mode);
    Check(!c.Calls.Contains("weaponSwitchLongGun.pistolReload") && !c.Calls.Contains("pushEvent"),"medical retry is handled before weapon/support selection: "+mode);
    if(mode=="medicalReady") Check(end.Value && c.PreparedTransition?.Action==BotLogicDecision.heal && !c.Calls.Contains("medicalRecoveryCover"),"ready treatment is prepared before recovery");
    if(mode=="safeRetry") Check(!end.Value && !c.PreparedTransition.HasValue && !c.Calls.Contains("medicalProvider"),"safe cover retains retry without hit/under-fire generic hold churn");
    if(mode=="exposedRetry" || mode=="quietFailedRetry") Check(end.Value && c.PreparedTransition?.Reason=="medicalRecoveryCover","failed retry selects a concrete shared recovery move: "+mode);
    if(mode=="noCover")
    {
        Check(end.Value && c.PreparedTransition?.Reason==FollowerCombatCommon.RecoveryNoCoverThreatHoldReason,"missing recovery cover uses the shared bounded fallback");
        var next=c.PreparedTransition.Value;
        Check(!o.ShallEndCurrentDecision(next).Value && c.Calls.Contains("recoveryEnd"),"no-cover fallback retains its dedicated end owner");
        c.RecoveryEnded=true;
        Check(o.ShallEndCurrentDecision(next).Value,"shared fallback deadline releases for a fresh medical retry");
    }
    if(mode=="medicalCleared") Check(end.Value && end.Reason=="medicalRetryCleared","finished medical work releases hold");
    if(mode=="enemyGone") Check(end.Value && end.Reason=="medicalRetryCombatEnded","enemy loss releases hold for peaceful healing");
    if(mode=="activeUse") Check(!end.Value && !c.PreparedTransition.HasValue,"active treatment cannot be replaced by recovery");
}
foreach (var action in new[] { BotLogicDecision.runToEnemy, BotLogicDecision.goToEnemy })
foreach (var scenario in new[] { "safe", "far", "pressure", "highThreat", "coverFire", "noCover" })
{
    var (bot,c,_) = Setup();
    var adapter = new KnightVanillaCombat(bot, 76); adapter.Begin(c);
    c.LowThreat = scenario != "highThreat";
    c.RecoveryPressure = scenario == "pressure";
    bot.Memory.GoalEnemy.Distance = scenario == "far" ? 96f : 30f;
    c.RecoveryMove = scenario != "noCover";
    c.CoverShot = scenario == "coverFire";
    if (scenario is "coverFire" or "noCover") c.LowThreat = false;
    bool adapted = adapter.TryAdaptCautiousDecision(bot.Memory.GoalEnemy, new(action,"nativePursuit"), out var result);
    Check(adapted == (scenario != "safe"), "only admitted low-threat nearby autonomous pursuit survives: " + action + "/" + scenario);
    if (adapted)
        Check(result.Reason == (scenario == "coverFire" ? "knight.cautiousCoverFire" :
            scenario == "noCover" ? FollowerCombatCommon.RecoveryNoCoverThreatHoldReason : "medicalRecoveryCover"),
            "unsafe pursuit uses shared cover/recovery owner: " + scenario);
    Check(!c.Calls.Contains("medicalProvider"), "caution never invokes medical provider");
}
foreach (bool inCover in new[] { false, true })
{
    var (bot,c,objective) = Setup();
    bot.Memory.IsInCover = inCover;
    var adapter = new KnightVanillaCombat(bot, 76); adapter.Begin(c);
    bool adapted = adapter.TryAdaptCautiousDecision(bot.Memory.GoalEnemy,
        new(BotLogicDecision.holdPosition,"periodWait"), out var result);
    Check(adapted == !inCover, "native exposed wait is replaced; covered wait remains native");
    if (adapted)
    {
        Check(result.Reason == FollowerCombatCommon.RecoveryNoCoverThreatHoldReason, "exposed wait has bounded shared fallback");
        Check(!objective.ShallEndCurrentDecision(result).Value && c.Calls.Contains("recoveryEnd"), "exposed wait cannot enter native per-frame hold-end loop");
    }
}
foreach (var action in new[] { BotLogicDecision.dogFight, BotLogicDecision.heal,
    BotLogicDecision.healStimulators, BotLogicDecision.suppressFire, BotLogicDecision.runToCover })
{
    var (bot,c,_) = Setup();
    var adapter = new KnightVanillaCombat(bot, 76); adapter.Begin(c);
    Check(!adapter.TryAdaptCautiousDecision(bot.Memory.GoalEnemy, new(action,"native"), out _),
        "caution cannot override urgent or non-pursuit decision: " + action);
    Check(c.Calls.Count == 0, "non-pursuit decision does not probe planners");
}
{
    var logic = new KnightCombatLogic(new BotOwner());
    var follower = new BotFollowerPlayer { Command = FollowerCommandType.PushEnemy };
    Check(logic.ProbePush(follower) && !logic.ProbeSuppress(follower), "Knight consumes accepted PushEnemy through Core's ordered-push hook");
    follower.Command = FollowerCommandType.SuppressEnemy;
    Check(!logic.ProbePush(follower) && logic.ProbeSuppress(follower), "Knight keeps push and suppression handoffs distinct");
    follower.Command = FollowerCommandType.None;
    Check(!logic.ProbePush(follower) && !logic.ProbePush(null), "Knight cannot invent a push without an accepted command");
}
BigPipeChecks.Run(Check);
LifecycleChecks.Run(Check);
CommittedHoldChecks.Run(Check);
ConfidenceChecks.Run(Check);
PushLeadershipHandoffChecks.Run(Check);
BirdEyeChecks.Run(Check);
GoonRejoinChecks.Run(Check);
Console.WriteLine($"PASS: {checks} production Knight/Big Pipe objective/support/caution/lease/command checks. Stubbed EFT inputs; no Unity raid simulation.");

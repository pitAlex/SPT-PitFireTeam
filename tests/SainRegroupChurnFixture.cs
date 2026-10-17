using EFT;
using pitTeam.SAINAddon;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes;
using SAIN.SAINComponent.SubComponents.CoverFinder;
using UnityEngine;

public static partial class CombatChecks
{
    private static BotOwner ArrivedRegroup(string id,bool shooter=false)
    {
        Time.time += 10;
        var b = CoverBot(id);
        if(shooter){b.Follower.CombatTactic=pitTeam.Components.FollowerCombatTactic.SAINShooter;Tick();b.Follower.Command=pitTeam.Components.FollowerCommandType.RegroupNearBoss;}
        RegroupDecision(b);
        var objective = SAINFollowerRuntime.GetRegroup(b);
        b.GetPlayer.Position = new Vector3(34,0,0);
        Time.time += 1; objective.Observe();
        b.Sain.Cover.CoverInUse = CoverAt(20);
        Time.time += 2; objective.Observe();
        Check(!objective.Active && b.Sain.Cover.CoverInUse == null, "completed regroup releases pre-regroup native cover: " + id);
        b.Sain.Decision.CurrentSquadDecision = ESquadDecision.None;
        b.Sain.Decision.CurrentCombatDecision = ECombatDecision.SeekCover;
        return b;
    }

    private static void TestRegroupChurn()
    {
        var b = ArrivedRegroup("regroupCoverEnvelope");
        var old = CoverAt(20); var near = CoverAt(35);
        b.Sain.Cover.CoverPoints.Add(old); b.Sain.Cover.NativePoint = old;
        Check(b.Sain.Cover.FindForTest() == null && b.Sain.Cover.NativeCalls == 0,
            "completed regroup cannot fall back to old cover outside completion envelope");
        var dogfight = new SAIN.SAINComponent.Classes.Mover.DogFight(b.Sain);
        dogfight.DogFightMove(true,b.Sain.GoalEnemy);
        Check(dogfight.Moves==1&&!dogfight.LastAggressive&&dogfight.Advances==0,
            "completed empty constrained selection reaches native defensive handling without outward pursuit");
        int queries=SainBotCoverData.Queries, probes=CoverAnalyzer.Rechecks;
        for(int i=0;i<8;i++){b.Sain.Cover.FindForTest();dogfight.DogFightMove(true,b.Sain.GoalEnemy);}
        Check(dogfight.Advances==0&&SainBotCoverData.Queries==queries&&CoverAnalyzer.Rechecks==probes,
            "cached empty local selection retains restriction without repeat scans or validation");
        dogfight.BackupAvailable=true;dogfight.DogFightMove(true,b.Sain.GoalEnemy);
        Check(dogfight.Backups==1&&!dogfight.LastAggressive,"no-cover restriction preserves native defensive backing up");
        dogfight.BackupAvailable=false;
        foreach(var urgent in new[]{ECombatDecision.DogFight,ECombatDecision.Retreat}){
            b.Sain.Decision.CurrentCombatDecision=urgent;dogfight.DogFightMove(true,b.Sain.GoalEnemy);
            Check(dogfight.LastAggressive,"actual urgent decision bypasses empty local restriction: "+urgent);
        }
        b.Sain.Decision.CurrentCombatDecision=ECombatDecision.SeekCover;
        b.Memory.IsUnderFire=true;dogfight.DogFightMove(true,b.Sain.GoalEnemy);
        Check(dogfight.LastAggressive,"incoming fire can escape empty post-regroup restriction immediately");
        b.Memory.IsUnderFire=false;b.Sain.Medical.TimeSinceShot=.1f;dogfight.DogFightMove(true,b.Sain.GoalEnemy);
        Check(dogfight.LastAggressive,"recent hit can escape empty post-regroup restriction immediately");
        b.Sain.Medical.TimeSinceShot=999;b.Sain.Decision.CurrentSelfDecision=ESelfActionType.FirstAid;
        dogfight.DogFightMove(true,b.Sain.GoalEnemy);
        Check(dogfight.LastAggressive,"medical recovery retains native escape after empty local selection");
        b.Sain.Decision.CurrentSelfDecision=ESelfActionType.Reload;dogfight.DogFightMove(true,b.Sain.GoalEnemy);
        Check(!dogfight.LastAggressive,"routine reload cannot reopen ordinary outward no-cover pursuit");
        b.Sain.Decision.CurrentSelfDecision=ESelfActionType.None;
        Time.time += 10;
        Check(b.Sain.Cover.FindForTest() == null, "post-regroup protection is geometric, not another cooldown");
        b.Sain.Cover.CoverPoints.Add(near);
        Check(b.Sain.Cover.FindForTest() == near, "post-regroup cover accepts safe player-local cover");
        Arrive(b,near); Time.time += 4;
        Check(!RegroupDecision(b), "accepted arrival cover cannot immediately retrigger distance regroup");

        var detour = ArrivedRegroup("regroupCoverRoute");
        var deceptive = CoverAt(30); detour.Sain.Cover.CoverPoints.Add(deceptive); detour.Sain.Cover.NativePoint=old;
        pitTeam.Utils.Utils.PathScale = 2;
        Check(detour.Sain.Cover.FindForTest() == null, "cover inside direct radius but outside route envelope is rejected");
        pitTeam.Utils.Utils.PathScale = 1; pitTeam.Utils.Utils.PathComplete = false;
        Check(detour.Sain.Cover.FindForTest() == null, "post-regroup cover requires a complete player route");
        pitTeam.Utils.Utils.PathComplete = true;
        deceptive.MovementAccepted = false;
        Check(detour.Sain.Cover.FindForTest() == null, "failed local movement cannot reopen outward native fallback");
        detour.Memory.IsUnderFire = true;
        Check(detour.Sain.Cover.FindForTest() == old, "incoming fire can still use urgent native cover outside envelope");
        detour.Memory.IsUnderFire = false; detour.Follower.CombatIndependent = true;
        Check(detour.Sain.Cover.FindForTest() == old, "On Your Own bypasses post-regroup cover constraint");

        var renewed = ArrivedRegroup("regroupRenewedContact");
        renewed.Sain.Cover.CoverPoints.Add(old); renewed.Sain.GoalEnemy.IsVisible=true; renewed.Sain.GoalEnemy.CanShoot=true;
        Check(renewed.Sain.Cover.FindForTest()==old, "renewed visible fire opportunity releases regroup-area preference");
        var moved = ArrivedRegroup("regroupNewAnchor"); moved.Sain.Cover.CoverPoints.Add(old);
        moved.Sain.GoalEnemy.KnownPlaces.LastKnownPosition = new Vector3(58,0,0);
        Check(moved.Sain.Cover.FindForTest()==old, "meaningfully changed enemy anchor permits fresh combat cover");
        var ordered = ArrivedRegroup("regroupNewOrder"); ordered.Sain.Cover.CoverPoints.Add(old);
        ordered.Sain.Cover.FindForTest();
        ordered.Follower.SetPushEnemy(20);
        dogfight=new SAIN.SAINComponent.Classes.Mover.DogFight(ordered.Sain);dogfight.DogFightMove(true,ordered.Sain.GoalEnemy);
        Check(dogfight.LastAggressive,"accepted Go Forward cannot inherit an old empty-result movement restriction");
        Check(ordered.Sain.Cover.FindForTest()==old, "accepted Go Forward releases post-regroup cover constraint");
        var cleared = ArrivedRegroup("regroupClear"); cleared.Sain.Cover.CoverPoints.Add(old);
        SAINFollowerRuntime.GetCover(cleared).Clear();
        Check(cleared.Sain.Cover.FindForTest()==old, "combat release clears post-regroup selection state");
        var ordinary=CoverBot("ordinaryEmptyFallback");ordinary.Sain.Cover.FindForTest();
        dogfight=new SAIN.SAINComponent.Classes.Mover.DogFight(ordinary.Sain);dogfight.DogFightMove(true,ordinary.Sain.GoalEnemy);
        Check(dogfight.LastAggressive,"unconstrained empty cover permits native fallback");
        ordinary.IsFollower=false;dogfight.DogFightMove(true,ordinary.Sain.GoalEnemy);
        Check(dogfight.LastAggressive,"ordinary SAIN bots retain native aggressive no-cover movement");
        var shooter=ArrivedRegroup("shooterEmptyCover",true);shooter.Sain.Cover.FindForTest();
        dogfight=new SAIN.SAINComponent.Classes.Mover.DogFight(shooter.Sain);dogfight.DogFightMove(true,shooter.Sain.GoalEnemy);
        Check(!dogfight.LastAggressive,"SAINShooter retains the same completed-empty local cover boundary");
        shooter.Follower.CombatIndependent=true;dogfight.DogFightMove(true,shooter.Sain.GoalEnemy);
        Check(dogfight.LastAggressive,"independence immediately bypasses a previously constrained empty result");
        var unready=ArrivedRegroup("unreadyEmptyCover");unready.Sain.Cover.FindForTest();
        unready.IsFollower=false;dogfight=new SAIN.SAINComponent.Classes.Mover.DogFight(unready.Sain);dogfight.DogFightMove(true,unready.Sain.GoalEnemy);
        Check(dogfight.LastAggressive,"a former follower cannot carry the empty-result restriction into ordinary SAIN");

        var waiting = CoverBot("regroupPublicationWait");
        var layer = new SAINFollowerSquadCombatLayer(waiting,75); Tick();
        RegroupDecision(waiting); Check(layer.IsActive(), "active regroup owns squad layer");
        layer.GetNextAction();
        waiting.GetPlayer.Position = new Vector3(34,0,0);
        Time.time += 1; SAINFollowerRuntime.GetRegroup(waiting).Observe();
        Time.time += 2; SAINFollowerRuntime.GetRegroup(waiting).Observe();
        layer.NativeDecisionChanged = true;
        bool ended = false;
        for (int i=0;i<8;i++) ended |= layer.IsCurrentActionEnding();
        Check(!ended && !layer.NativeDecisionChanged, "completed regroup drains stale event and never restarts before publication");
        waiting.Sain.Decision.CurrentSelfDecision=ESelfActionType.FirstAid;
        Check(!layer.IsActive(), "medical selection still preempts waiting squad action");
        waiting.Sain.Decision.CurrentSelfDecision=ESelfActionType.None;
        waiting.Sain.Decision.CurrentSquadDecision=ESquadDecision.Suppress;
        layer.NativeDecisionChanged=true;
        Check(layer.IsCurrentActionEnding() && layer.GetNextAction().Type.Name=="SuppressAction", "new native squad publication ends wait and selects support");
        waiting.Sain.Decision.CurrentSquadDecision=ESquadDecision.None;
        Check(!layer.IsActive(), "native solo publication releases squad ownership");
    }
}

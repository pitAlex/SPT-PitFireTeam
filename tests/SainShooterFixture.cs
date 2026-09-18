using System;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using SAIN.Components;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.Decision;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

// Controlled native geometry result; installed API shape is checked by the runner.
namespace SAIN.SAINComponent.Classes.Decision {
    public sealed class FiringPositionFinder(BotComponent bot) {
        public static int Calls; public static Vector3? Candidate;
        public Vector3? Position {get;private set;}
        public bool Find(Enemy enemy){Calls++;Position=Candidate;return Position.HasValue;}
        public void Clear(){Position=null;}
    }
}
public static partial class CombatChecks {
    private static BotOwner ShooterBot(string id) {
        var b=RegroupBot(id,0);b.Follower.CombatTactic=FollowerCombatTactic.SAINShooter;
        b.Follower.CombatAggression=30;b.Sain.GoalEnemy.Seen=true;
        b.Sain.GoalEnemy.KnownPlaces.LastKnownPosition=new Vector3(60,0,0);
        b.Sain.EnemyController.KnownEnemies.Add(b.Sain.GoalEnemy);
        Tick();FiringPositionFinder.Calls=0;FiringPositionFinder.Candidate=new Vector3(-10,0,0);return b;
    }
    private static void TestShooter() {
        Check(FollowerCombatTactics.CoreTactic(FollowerCombatTactic.SAINShooter)==FollowerCombatTactic.Marksman &&
            FollowerCombatTactics.CoreTactic(FollowerCombatTactic.SainMan)==FollowerCombatTactic.Balanced,
            "Shooter and Grunt retain separate Core fallback roles");
        var b=ShooterBot("shooterScan");var m=b.Sain.Decision.Manager;var p=SAINFollowerRuntime.GetMarksman(b);
        Check(p!=null && pitTeam.pitFireTeam.UseSainFollowerCombat(b),"ready Shooter owns both addon combat layers");
        m.Publish(ECombatDecision.Search);
        Check(b.Sain.Decision.CurrentCombatDecision==ECombatDecision.MoveToEngage && p.OwnsMovement && FiringPositionFinder.Calls==1,
            "Shooter translates native pursuit into one native firing-position attempt");
        for(int i=0;i<100;i++)m.Publish(ECombatDecision.Search);
        Check(FiringPositionFinder.Calls==1,"repeated native publications retain the candidate without rescanning");
        var move=new SAINFollowerMoveToEngageAction(b);move.Start();move.Update(null);
        Check((b.Sain.Mover.Destination-new Vector3(-10,0,0)).sqrMagnitude<.01f,"shared SAIN movement executes Shooter's committed native destination");
        Time.time+=7;move.Update(null);m.Publish(ECombatDecision.Search);
        Check(SAINFollowerRuntime.GetEngageAttempt(b).Failed && b.Sain.Decision.CurrentCombatDecision==ECombatDecision.SeekCover,
            "Shooter stalled movement falls back to native cover");
        for(int i=0;i<30;i++)m.Publish(ECombatDecision.Search);
        Check(FiringPositionFinder.Calls==1,"failed same-contact Shooter approach cannot rearm its finder");
        b.Follower.Command=FollowerCommandType.NeedSniper;m.Publish(ECombatDecision.Search);
        Check(!p.OwnsMovement && FiringPositionFinder.Calls==2 && SAINFollowerRuntime.GetEngageAttempt(b).Failed,"support order retries the native finder but cannot restart its failed position");
        move.Stop();Time.time+=2.1f;b.Sain.GoalEnemy.KnownPlaces.LastKnownPosition=new Vector3(70,0,0);m.Publish(ECombatDecision.Search);
        Check(FiringPositionFinder.Calls==3 && p.OwnsMovement,"meaningful native knowledge change permits a new firing attempt");
        b.Follower.SetPushEnemy(12);
        Check(!SAINFollowerRuntime.GetPush(b).Active && !b.Follower.IsTemporaryCombatAggressionOverrideActive,
            "Go Forward cannot create Shooter assault intent or force GigaChad");
        b.Follower.SetCombatMoveToPointTactical(new Vector3(5,0,0),8f);p.Observe();
        Check(!p.OwnsMovement && !p.Destination.HasValue,"replacement gesture releases Shooter destination before command consumption");

        b=ShooterBot("shooterRegroupCancel");m=b.Sain.Decision.Manager;p=SAINFollowerRuntime.GetMarksman(b);
        m.Publish(ECombatDecision.Search);move=new SAINFollowerMoveToEngageAction(b);move.Start();move.Update(null);
        b.Follower.Command=FollowerCommandType.RegroupNearBoss;m.Frame();
        Check(!p.Destination.HasValue && SAINFollowerRuntime.GetEngageAttempt(b).Failure=="marksmanCancelled",
            "regroup retires the movement action's cached firing point as well as objective intent");
        move.Update(null);move.Stop();
        SAINFollowerRuntime.GetRegroup(b).Complete("testArrival");m.Reset();
        FiringPositionFinder.Candidate=new Vector3(-14,0,0);Time.time+=3f;m.Publish(ECombatDecision.Search);
        Check(!p.OwnsMovement && FiringPositionFinder.Calls==1 && b.Sain.Decision.CurrentCombatDecision==ECombatDecision.SeekCover,
            "completed regroup cannot rearm or resume an abandoned same-contact firing leg");
        b.Sain.GoalEnemy.KnownPlaces.LastKnownPosition=new Vector3(70,0,0);m.Publish(ECombatDecision.Search);
        move.Start();move.Update(null);
        Check(p.OwnsMovement && (b.Sain.Mover.Destination-new Vector3(-14,0,0)).sqrMagnitude<.01f,
            "changed native knowledge permits a fresh destination after cancellation without resuming the old point");
        move.Stop();

        b=ShooterBot("shooterRetry");m=b.Sain.Decision.Manager;p=SAINFollowerRuntime.GetMarksman(b);
        FiringPositionFinder.Candidate=null;m.Publish(ECombatDecision.Search);
        Time.time+=3.9f;m.Publish(ECombatDecision.Search);
        Check(FiringPositionFinder.Calls==1,"empty sniper searches are throttled while holding the same contact");
        Time.time+=.2f;FiringPositionFinder.Candidate=new Vector3(-12,0,0);m.Publish(ECombatDecision.Search);
        Check(p.OwnsMovement && FiringPositionFinder.Calls==2,"same contact can produce a new firing opportunity after bounded retry");
        move=new SAINFollowerMoveToEngageAction(b);move.Start();move.Update(null);Time.time+=7f;move.Update(null);m.Publish(ECombatDecision.Search);move.Stop();
        Time.time+=4.1f;FiringPositionFinder.Candidate=new Vector3(-11,0,0);m.Publish(ECombatDecision.Search);
        Check(!p.OwnsMovement && SAINFollowerRuntime.GetEngageAttempt(b).Failed,"automatic replanning rejects the failed spot and nearby jitter");
        Time.time+=4.1f;FiringPositionFinder.Candidate=new Vector3(-20,0,0);m.Publish(ECombatDecision.Search);
        Check(p.OwnsMovement && !SAINFollowerRuntime.GetEngageAttempt(b).Failed,"a different safe firing position starts a fresh bounded leg for the same enemy");
        move.Start();move.Update(null);
        Check((b.Sain.Mover.Destination-new Vector3(-20,0,0)).sqrMagnitude<.01f,"replanned leg uses the new destination rather than the old attempt cache");move.Stop();
        b=ShooterBot("shooterFailureBudget");b.Follower.CombatIndependent=true;m=b.Sain.Decision.Manager;p=SAINFollowerRuntime.GetMarksman(b);
        move=new SAINFollowerMoveToEngageAction(b);
        for(int i=0;i<4;i++) {
            FiringPositionFinder.Candidate=new Vector3(-10*(i+1),0,0);Time.time+=4.1f;m.Publish(ECombatDecision.Search);
            Check(p.OwnsMovement,"distinct native firing leg admitted within failure budget "+i);
            move.Start();move.Update(null);Time.time+=7f;move.Update(null);m.Publish(ECombatDecision.Search);move.Stop();
        }
        int scans=FiringPositionFinder.Calls;FiringPositionFinder.Candidate=new Vector3(-60,0,0);Time.time+=10f;
        b.Follower.Command=FollowerCommandType.NeedSniper;m.Publish(ECombatDecision.Search);
        Check(!p.OwnsMovement && FiringPositionFinder.Calls==scans,"four failed destinations cap same-contact movement and scanning even for repeated orders");
        b.Sain.GoalEnemy.KnownPlaces.LastKnownPosition=new Vector3(80,0,0);m.Publish(ECombatDecision.Search);
        Check(p.OwnsMovement,"meaningful enemy knowledge change unlocks the bounded failed-position budget");

        b=ShooterBot("shooterPendingCover");m=b.Sain.Decision.Manager;
        b.Sain.Mover.Moving=true;b.Follower.Command=FollowerCommandType.NeedSniper;m.Publish(ECombatDecision.Search);
        Check(b.Follower.Command==FollowerCommandType.NeedSniper && FiringPositionFinder.Calls==0,"Need Sniper stays pending during committed cover travel");
        b.Sain.Mover.Moving=false;FiringPositionFinder.Candidate=null;m.Publish(ECombatDecision.Search);
        Time.time+=2.1f;FiringPositionFinder.Candidate=new Vector3(10,0,0);m.Publish(ECombatDecision.Search);
        Check(SAINFollowerRuntime.GetMarksman(b).OwnsMovement && FiringPositionFinder.Calls==2,"explicit support retains its bounded retry window and forward-position permission");

        foreach(var squad in new[]{ESquadDecision.Help,ESquadDecision.GroupSearch,ESquadDecision.PushSuppressedEnemy}) {
            b=ShooterBot("shooterSquad"+squad);m=b.Sain.Decision.Manager;
            m.Publish(ECombatDecision.None,squad);
            Check(b.Sain.Decision.CurrentSquadDecision==ESquadDecision.None && b.Sain.Decision.CurrentCombatDecision==ECombatDecision.MoveToEngage,
                "Shooter converts squad "+squad+" to firing-position support");
        }
        b=ShooterBot("shooterSafety");m=b.Sain.Decision.Manager;p=SAINFollowerRuntime.GetMarksman(b);
        b.Follower.Command=FollowerCommandType.NeedSniper;
        m.Publish(ECombatDecision.SeekCover,ESquadDecision.None,ESelfActionType.FirstAid);
        Check(FiringPositionFinder.Calls==0 && b.Follower.Command==FollowerCommandType.NeedSniper,
            "medicine defers support command and runs no native position scan");
        b.Sain.Decision.CurrentSelfDecision=ESelfActionType.None;b.Memory.IsUnderFire=true;
        m.Publish(ECombatDecision.RushEnemy,ESquadDecision.PushSuppressedEnemy);
        Check(FiringPositionFinder.Calls==0 && b.Sain.Decision.CurrentCombatDecision==ECombatDecision.SeekCover &&
            b.Sain.Decision.CurrentSquadDecision==ESquadDecision.None,"incoming pressure preserves cover instead of squad assault");
        b.Memory.IsUnderFire=false;FiringPositionFinder.Candidate=new Vector3(10,0,0);
        m.Publish(ECombatDecision.Search);
        Check(p.OwnsMovement && b.Follower.Command==FollowerCommandType.None,"Need Sniper permits a safe forward native firing position");
        b.Sain.GoalEnemy.IsVisible=true;b.Sain.GoalEnemy.CanShoot=true;m.Publish(ECombatDecision.StandAndShoot);
        Check(!p.OwnsMovement && !p.Destination.HasValue && b.Sain.Decision.CurrentCombatDecision==ECombatDecision.StandAndShoot,
            "useful native fire immediately releases support movement");
        m.Publish(ECombatDecision.RushEnemy,ESquadDecision.PushSuppressedEnemy);
        Check(b.Sain.Decision.CurrentCombatDecision==ECombatDecision.StandAndShoot && b.Sain.Decision.CurrentSquadDecision==ESquadDecision.None,
            "visible shootable Shooter contact chooses native fire rather than a vulnerable-enemy rush");
        m.Publish(ECombatDecision.None,ESquadDecision.Suppress);
        Check(b.Sain.Decision.CurrentSquadDecision==ESquadDecision.Suppress,"native retreat-support suppression remains native");

        b=ShooterBot("shooterNativeOnly");m=b.Sain.Decision.Manager;FiringPositionFinder.Candidate=null;
        m.Publish(ECombatDecision.Search);m.Publish(ECombatDecision.Search);
        Check(FiringPositionFinder.Calls==1 && b.Sain.Decision.CurrentCombatDecision==ECombatDecision.SeekCover,
            "no native position yields cover without a second Core geometry search");
        b.Sain.GoalEnemy.KnownPlaces.LastKnownPosition=new Vector3(70,0,0);m.Publish(ECombatDecision.Search);
        Check(FiringPositionFinder.Calls==1,"rapid knowledge changes respect native two-second scan cadence");
        Time.time+=2.1f;m.Publish(ECombatDecision.Search);
        Check(FiringPositionFinder.Calls==2,"deferred native scan runs once when its cadence permits");
        b=ShooterBot("shooterAutoForward");FiringPositionFinder.Candidate=new Vector3(10,0,0);b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(!SAINFollowerRuntime.GetMarksman(b).OwnsMovement,"autonomous support rejects an outward closing candidate");
        b=ShooterBot("shooterCoverTravel");b.Sain.Mover.Moving=true;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(FiringPositionFinder.Calls==0 && !SAINFollowerRuntime.GetMarksman(b).OwnsMovement,
            "committed native cover travel cannot trigger another firing-position scan");
        b=ShooterBot("shooterReuse");b.Sain.Decision.EnemyDecisions.FiringPosition=new Vector3(-9,0,0);
        b.Sain.Decision.Manager.Publish(ECombatDecision.MoveToEngage);
        Check(FiringPositionFinder.Calls==0 && (SAINFollowerRuntime.GetMarksman(b).Destination.Value-new Vector3(-9,0,0)).sqrMagnitude<.01f,
            "fresh native MoveToEngage candidate is reused without a duplicate native scan");
        b.Sain.Mover.Complete=false;move=new SAINFollowerMoveToEngageAction(b);move.Start();move.Update(null);
        Check(SAINFollowerRuntime.GetEngageAttempt(b).Failure=="pathRejected","rejected native Shooter movement stays failed");move.Stop();
        b=ShooterBot("shooterInvalidKnowledge");b.Sain.GoalEnemy.KnownPlaces.LastKnownPosition=new Vector3(float.NaN,0,0);
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(FiringPositionFinder.Calls==0,"invalid native knowledge never enters the firing-position finder");
        b=ShooterBot("shooterColdDistantHold");b.Leader.Position=new Vector3(68,0,0);
        b.Sain.GoalEnemy.IsVisible=false;b.Sain.GoalEnemy.CanShoot=true;b.Sain.GoalEnemy.InLineOfSight=true;b.Sain.GoalEnemy.TimeSinceSeen=20;
        b.Sain.Cover.CoverSeekingState=SAIN.SAINComponent.Classes.ECoverSeekingState.HoldInCover;b.Sain.Cover.CoverInUse=CoverAt(0);
        b.Sain.Decision.CurrentCombatDecision=ECombatDecision.SeekCover;
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(SAINFollowerRuntime.GetRegroup(b).Mode==SAINRegroupMode.Auto&&FiringPositionFinder.Calls==0,
            "distant cold Shooter regroups from settled cover before repeating an empty position search");
        b=ShooterBot("shooterArrival");b.Follower.CombatIndependent=true;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        move=new SAINFollowerMoveToEngageAction(b);move.Start();move.Update(null);
        b.GetPlayer.Position=new Vector3(-10,0,0);Time.time+=.1f;move.Update(null);Time.time+=2.1f;move.Update(null);
        Check(SAINFollowerRuntime.GetEngageAttempt(b).Failure=="arrivedWithoutShot",
            "independent Shooter retains bounded native firing-position arrival");
        move.Stop();
        Check(SainRegroupBridge.GetCompleteDistance(b,false)==24f &&
            SainRegroupBridge.GetTriggerDistance(b)==CombatDistanceConfiguration.Instance.Trigger*1.5f,
            "Shooter uses Marksman completion and trigger distances");
        b.Follower.CombatTactic=FollowerCombatTactic.SainMan;
        Check(!pitTeam.pitFireTeam.UseSainFollowerCombat(b),"role changes use Core fallback until prepared for the new role");Tick();
        Check(SAINFollowerRuntime.GetMarksman(b)==null && pitTeam.pitFireTeam.UseSainFollowerCombat(b),
            "switching to Grunt releases Shooter objective state and restores Grunt ownership");
    }
}

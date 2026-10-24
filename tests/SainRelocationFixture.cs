using System;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.BigBrain;
using pitTeam.SAINAddon;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.SubComponents.CoverFinder;
using UnityEngine;
using UnityEngine.AI;

namespace EFT {
    public enum EPhraseTrigger {Negative}
    public enum EInteraction {NoGesture}
    public class CommandTalk {public int Calls;public void TrySay(EPhraseTrigger p,bool b){Calls++;}}
    public class CommandGesture {public int Calls;public void TryGestus(EInteraction p,bool b){Calls++;}}
    public partial class BotOwner {public CommandTalk BotTalk=new CommandTalk();public CommandGesture Gesture=new CommandGesture();}
}
public static partial class CombatChecks {
    private static void TestRelocations(){
        var b=PushBot("thereDuringPush");var p=SAINFollowerRuntime.GetPush(b);var r=SAINFollowerRuntime.GetRelocation(b);
        var m=b.Sain.Decision.Manager;var layer=new SAINFollowerSoloCombatLayer(b,74);Tick();
        m.Publish(ECombatDecision.Search);b.Follower.SetCombatMoveToPointTactical(new Vector3(12,0,4),8);
        int publications=m.Publications;m.Publish(ECombatDecision.Search,ESquadDecision.Suppress);
        Check(m.Publications==publications+1&&m.EventSolo==ECombatDecision.MoveToEngage&&m.EventSquad==ESquadDecision.None,"There replaces ordinary solo/squad work in one native publication");
        Check(r.Mode==SAINRelocationMode.There&&!p.Active&&b.Follower.Command==FollowerCommandType.None,"combat gesture consumes command once and replaces push");
        Check(layer.IsActive()&&layer.GetNextAction().Type==typeof(SAINFollowerRelocationAction),"solo replica selects dedicated relocation action");
        var action=new SAINFollowerRelocationAction(b);action.Start();action.Update(null);
        Check(b.Sain.Mover.Runs==0&&b.Sain.Mover.Destination.z==4,"There executes complete-path walking to the exact selected point");
        var destination=r.Destination.Value;b.Leader.Position=new Vector3(150,0,0);m.Publish(ECombatDecision.SeekCover);
        Check(r.Destination.Value.z==destination.z&&r.Destination.Value.x==destination.x&&!SAINFollowerRuntime.GetRegroup(b).Active,"moving player does not redirect There or admit automatic regroup");
        b.GetPlayer.Position=destination;action.Update(null);Check(r.Settling&&!b.Sain.Mover.Moving,"arrival stops movement and arms Core-duration settle");
        Time.time+=2;m.Publish(ECombatDecision.Search);action.Update(null);Check(r.Settling&&r.Active,"repeated publication does not restart arrival or select native search");
        Time.time+=1.1f;action.Update(null);Check(!r.Active&&r.OwnsAction,"completed relocation drains stale publication quietly");
        Check(layer.GetNextAction().Type==typeof(SAINFollowerRelocationAction),"stale MoveToEngage cannot start an unrelated native approach after arrival");
        m.Publish(ECombatDecision.StandAndShoot);Check(!r.OwnsAction,"next native publication releases completed relocation action");

        b=PushBot("comeCover",false);b.Leader.Position=new Vector3(30,0,0);r=SAINFollowerRuntime.GetRelocation(b);m=b.Sain.Decision.Manager;
        b.Sain.Cover.CoverPoints.Add(CoverAt(-5));var cover=CoverAt(25);b.Sain.Cover.CoverPoints.Add(cover);
        b.Follower.SetCombatComeToBossCover(8);m.Publish(ECombatDecision.SeekCover);
        Check(r.Mode==SAINRelocationMode.ComeHere&&r.Reason=="bossCover"&&r.Destination.Value.x==25,"Come here prefers valid closer cover inside player's search radius");
        b.Leader.Position=new Vector3(50,0,0);m.Publish(ECombatDecision.Search);
        Check(r.Destination.Value.x==25,"Come here commits cover instead of chasing player every poll");
        action=new SAINFollowerRelocationAction(b);action.Start();action.Update(null);Check(b.Sain.Mover.Runs==0,"boss-cover approach walks like Core attackMoving");
        cover.Valid=false;Time.time+=1.1f;action.Update(null);Check(!r.Active&&r.Reason=="destinationInvalidated","compromised selected cover releases relocation");
        b=PushBot("comeBudget",false);b.Leader.Position=new Vector3(30,0,0);r=SAINFollowerRuntime.GetRelocation(b);m=b.Sain.Decision.Manager;
        for(int i=0;i<12;i++)SainBotCoverData.Scene.Add(new SainBotColliderData{Collider=new Collider{Point=CoverAt(23+i*.1f)}});
        b.Follower.SetCombatComeToBossCover(8);int creates=CoverAnalyzer.Creates;m.Publish(ECombatDecision.SeekCover);
        Check(r.Active&&!r.Destination.HasValue&&CoverAnalyzer.Creates-creates<=4,"Come here incremental cover scan stays within four native probes per frame");
        int work=CoverAnalyzer.Creates;for(int i=0;i<5;i++)m.Publish(ECombatDecision.SeekCover);
        Check(CoverAnalyzer.Creates==work&&!r.Destination.HasValue,"same-frame command polls cannot spend another cover scan budget");
        for(int i=0;i<12&&!r.Destination.HasValue;i++){Time.time+=.5f;m.Publish(ECombatDecision.SeekCover);}
        Check(r.Reason=="bossCover"&&r.Destination.HasValue,"slow publication completes bounded cover discovery before fallback");
        b.Sain.GoalEnemy.EnemyPlayer.HealthController.IsAlive=false;r.Observe();
        Check(!r.Active&&!r.OwnsAction,"enemy death clears relocation and movement ownership");
        b=PushBot("comeBudgetExpiry",false);b.Leader.Position=new Vector3(30,0,0);r=SAINFollowerRuntime.GetRelocation(b);
        for(int i=0;i<12;i++)SainBotCoverData.Scene.Add(new SainBotColliderData{Collider=new Collider{Point=CoverAt(23)}});
        b.Follower.SetCombatComeToBossCover(8);b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        action=new SAINFollowerRelocationAction(b);action.Start();Time.time+=8.1f;action.Update(null);
        Check(!r.Active&&r.Reason=="coverPlanningExpired","pending cover planning remains bounded if decision publication stops");
        b=PushBot("comeFallback",false);b.Leader.Position=new Vector3(30,0,0);r=SAINFollowerRuntime.GetRelocation(b);
        b.Sain.Cover.CoverPoints.Add(CoverAt(-2));b.Follower.SetCombatComeToBossCover(8);b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(r.Reason=="bossApproach"&&Math.Abs(r.Destination.Value.x-28.5f)<.01f,"cover that moves away is rejected for Core's stop-short fallback");
        NavMesh.Route=new[]{new Vector3(),new Vector3(30,0,0),new Vector3(30,0,3)};
        Check(FollowerCombatCommandGeometry.TryBossApproach(new Vector3(),new Vector3(30,0,3),out var step)&&Math.Abs(step.z-1.5f)<.01f,"shared fallback walks back along last path segment, not direct bearing");
        NavMesh.Route=null;NavMesh.RouteComplete=false;
        Check(!FollowerCombatCommandGeometry.TryBossApproach(new Vector3(),new Vector3(30,0,0),out step),"shared Core fallback rejects incomplete route");

        foreach(var kind in new[]{FollowerCommandType.CombatMoveToPointTactical,FollowerCommandType.CombatComeToBossCover}){
            b=PushBot("invalid"+kind,false);r=SAINFollowerRuntime.GetRelocation(b);b.Leader.Position=new Vector3(30,0,0);
            if(kind==FollowerCommandType.CombatMoveToPointTactical){b.Follower.SetCombatMoveToPointTactical(new Vector3(float.NaN,0,0),8);}
            else{b.Follower.SetCombatComeToBossCover(8);NavMesh.RouteComplete=false;}
            b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
            Check(!r.Active&&b.Follower.Command==FollowerCommandType.None&&b.BotTalk.Calls==1&&b.Gesture.Calls==1,"invalid command uses Core negative feedback once "+kind);
        }
        b=PushBot("partialThere",false);r=SAINFollowerRuntime.GetRelocation(b);pitTeam.Utils.Utils.PathComplete=false;
        b.Follower.SetCombatMoveToPointTactical(new Vector3(12,0,0),8);b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(!r.Active&&r.Reason=="invalidTacticalPoint","There rejects incomplete navigation");
        b=PushBot("thereReserved",false);r=SAINFollowerRuntime.GetRelocation(b);
        ((pitAIBossPlayer)b.BotFollower.BossToFollow).CombatEvents.Claims["other"]=new Vector3(12,0,0);
        b.Follower.SetCombatMoveToPointTactical(new Vector3(12,0,0),8);b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(!r.Active&&r.Reason=="invalidTacticalPoint","There respects squad destination reservations");

        foreach(var urgent in new[]{ECombatDecision.Retreat,ECombatDecision.AvoidGrenade,ECombatDecision.ThrowGrenade,ECombatDecision.DogFight,ECombatDecision.MeleeAttack}){
            b=PushBot("defer"+urgent,false);r=SAINFollowerRuntime.GetRelocation(b);b.Follower.SetCombatMoveToPointTactical(new Vector3(12,0,0),8);
            b.Sain.Decision.Manager.Publish(urgent);
            Check(!r.Active&&b.Follower.Command==FollowerCommandType.CombatMoveToPointTactical&&b.Sain.Decision.CurrentCombatDecision==urgent,"pending gesture protects native survival "+urgent);
            b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);Check(r.Active,"gesture begins when survival releases "+urgent);
        }
        b=PushBot("medicalGestureTimeout",false);r=SAINFollowerRuntime.GetRelocation(b);b.Follower.SetCombatComeToBossCover(8);b.UsingMedical=true;
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover,ESquadDecision.None,ESelfActionType.FirstAid);
        Check(!r.Active&&b.Follower.Command==FollowerCommandType.CombatComeToBossCover,"running medicine preserves pending gesture within original timeout");
        Time.time+=9;b.UsingMedical=false;b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(!r.Active&&b.Follower.Command==FollowerCommandType.None,"deferred combat gesture expires through production command timeout");
        b=PushBot("relocationInterrupted",false);r=SAINFollowerRuntime.GetRelocation(b);b.Follower.SetCombatMoveToPointTactical(new Vector3(12,0,0),8);b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        action=new SAINFollowerRelocationAction(b);action.Start();action.Update(null);b.Sain.GoalEnemy.IsVisible=b.Sain.GoalEnemy.CanShoot=true;action.Update(null);
        Check(!r.Active&&!b.Sain.Mover.Moving,"tactical relocation yields to visible shootable contact like Core");
        b=PushBot("relocationUnderFire",false);r=SAINFollowerRuntime.GetRelocation(b);b.Follower.SetCombatMoveToPointTactical(new Vector3(12,0,0),8);b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        action=new SAINFollowerRelocationAction(b);action.Start();action.Update(null);b.Memory.IsUnderFire=true;action.Update(null);
        Check(!r.Active&&!b.Sain.Mover.Moving,"open tactical movement yields to incoming fire");
        b=PushBot("relocationStall",false);r=SAINFollowerRuntime.GetRelocation(b);b.Follower.SetCombatMoveToPointTactical(new Vector3(12,0,0),8);b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        action=new SAINFollowerRelocationAction(b);action.Start();action.Update(null);Time.time+=4.1f;action.Update(null);
        Check(!r.Active&&r.Reason=="noProgress","relocation uses Core four-second stall bound");
        b=PushBot("relocationRejectedPath",false);r=SAINFollowerRuntime.GetRelocation(b);b.Follower.SetCombatMoveToPointTactical(new Vector3(12,0,0),8);b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);b.Sain.Mover.Complete=false;
        action=new SAINFollowerRelocationAction(b);action.Start();action.Update(null);Check(!r.Active&&r.Reason=="pathRejected","failed native walk cannot remain a stuck gesture");
        b=PushBot("replaceRelocation",false);r=SAINFollowerRuntime.GetRelocation(b);b.Follower.SetCombatMoveToPointTactical(new Vector3(12,0,0),8);b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        b.Follower.SetPushEnemy(12);Check(!r.Active&&SAINFollowerRuntime.GetPush(b).Ordered,"Go Forward replaces accepted relocation");
        b.Follower.SetCombatMoveToPointTactical(new Vector3(12,0,0),8);b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        b.Follower.Command=FollowerCommandType.RegroupNearBoss;SAINFollowerRuntime.GetCombatPhase(b);
        Check(!r.Active&&SAINFollowerRuntime.GetRegroup(b).Active,"Regroup replaces accepted relocation before command consumption");
        b=PushBot("independentGesture",false);b.Follower.CombatIndependent=true;r=SAINFollowerRuntime.GetRelocation(b);b.Follower.SetCombatMoveToPointTactical(new Vector3(12,0,0),8);b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(r.Active&&b.Follower.CombatIndependent,"explicit gesture works in On Your Own without changing independent intent");
        b.Sain.Mover.WalkToPoint(new Vector3(99,0,0));var newer=b.Sain.Mover.ActivePath;
        ((pitAIBossPlayer)b.BotFollower.BossToFollow).CombatEvents.Claims[b.ProfileId]=new Vector3(99,0,0);
        r.Clear("testReplacement");
        Check(!r.Active&&b.Sain.Mover.ActivePath==newer,"cleanup releases gesture without stopping another owner's path");
        Check(((pitAIBossPlayer)b.BotFollower.BossToFollow).CombatEvents.Claims[b.ProfileId].x==99,"cleanup preserves newer destination reservation");
        b.Follower.SetCombatMoveToPointTactical(new Vector3(12,0,0),8);b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        SainAddonBridge.TryForceReleaseFollowerCombatState(b);Check(!r.Active&&!r.OwnsAction,"explicit combat release clears relocation state");
    }
}

using System;
using System.Runtime.CompilerServices;
using EFT;
using pitTeam;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using SAIN.Components;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.SubComponents.CoverFinder;
using UnityEngine;

namespace UnityEngine { public static partial class Physics { public static bool Blocked; public static bool Linecast(Vector3 from,Vector3 to,int mask)=>Blocked; } }
namespace SAIN.Components {
    public class PushPose {public int Calls;public void SetPoseToCover(Enemy enemy){Calls++;}}
    public class PushLean {public void HoldLean(float seconds){}}
    public partial class Mover {public PushPose Pose=new PushPose();public PushLean Lean=new PushLean();}
    public class DogFightDecision { public bool DogFightActive; }
    public partial class Decision { public DogFightDecision DogFightDecision=new DogFightDecision(); }
}
namespace SAIN.SAINComponent.Classes.EnemyClasses {
    public class SAINEnemyController : SAIN.SAINComponent.BotBase {
        private Enemy candidate; public int NativeCalls,Publications;
        public SAINEnemyController(BotComponent bot):base(bot){}
        public Enemy ChooseForTest(Enemy value){candidate=value;var selected=SelectEnemy();Publications++;return selected;}
        [MethodImpl(MethodImplOptions.NoInlining)] private Enemy SelectEnemy(){NativeCalls++;return candidate;}
    }
}
public static partial class CombatChecks {
    private static BotOwner PushBot(string id,bool ordered=true){
        Physics.Blocked=false;UnityEngine.AI.NavMesh.SampleAllowed=null;UnityEngine.AI.NavMesh.Route=null;UnityEngine.AI.NavMesh.RouteComplete=true;SainBotCoverData.Scene.Clear();pitTeam.Utils.Utils.PathComplete=true;pitTeam.Utils.Utils.PathScale=1;
        pitTeam.Utils.FollowerAwareness.Damaged=false;
        var b=RegroupBot(id,0);b.Sain.EnemyController.KnownEnemies.Add(b.Sain.GoalEnemy);
        b.Memory.GoalEnemy.ProfileId=b.Sain.GoalEnemy.EnemyProfileId;
        if(ordered)b.Follower.SetPushEnemy(12);else b.Follower.CombatAggression=100;
        return b;
    }
    private static void TestPushObjectives(){
        var b=PushBot("objectiveOrdered");var layer=new SAINFollowerSoloCombatLayer(b,74);Tick();var p=SAINFollowerRuntime.GetPush(b);var m=b.Sain.Decision.Manager;
        Check(p.Ordered&&p.EnemyId==b.Sain.GoalEnemy.EnemyProfileId&&b.Follower.Command==pitTeam.Components.FollowerCommandType.None,"accepted push creates addon objective without core pending order");
        var point=CoverAt(15);b.Sain.Cover.CoverPoints.Add(point);
        int publications=m.Publications;m.Publish(ECombatDecision.Freeze);
        Check(m.Publications==publications+1&&m.EventSolo==ECombatDecision.MoveToEngage,"ordered objective replaces discretionary wait in one native publication");
        Check(p.OwnsMovement&&p.Destination.Value.x==15&&p.Reason=="forwardFiringCover","ordered approach prefers forward firing cover");
        var action=new SAINFollowerMoveToEngageAction(b);action.Start();action.Update(new DrakiaXYZ.BigBrain.Brains.CustomLayer.ActionData());
        Check(b.Sain.Mover.Destination.x==15&&b.Sain.Mover.Runs==0,"existing SAIN-derived movement action walks the objective destination");
        Check(SAINFollowerRuntime.GetEngageAttempt(b).EnemyId==null,"push movement does not consume unreachable firing-position attempt");
        b.Leader.Position=new Vector3(200,0,0);b.Sain.GoalEnemy.EnemyPosition=new Vector3(-400,0,0);m.Publish(ECombatDecision.Search);
        Check(p.Destination.Value.x==15,"player and hidden live-enemy movement do not redirect committed approach");
        b.GetPlayer.Position=point.Position;m.Publish(ECombatDecision.Search);
        Check(p.Phase==SAINPushPhase.Pressure&&b.Sain.Decision.CurrentCombatDecision==ECombatDecision.StandAndShoot,"arrival uses native shooting action and objective hold");
        action.Stop();
        layer.IsActive();
        Check(layer.GetNextAction().Type==typeof(SAINFollowerPushHoldAction),"arrival selects stationary native-derived hold instead of random swing");
        var hold=new SAINFollowerPushHoldAction(b);hold.Start();hold.Update(new DrakiaXYZ.BigBrain.Brains.CustomLayer.ActionData());
        Check(!b.Sain.Mover.Moving&&b.Sain.Mover.Pose.Calls==1,"arrival action remains stationary and updates native cover posture");
        Time.time+=2;m.Publish(ECombatDecision.Search);
        Check(p.Phase==SAINPushPhase.Pressure,"arrival hold survives repeated decision polls");
        b.Sain.GoalEnemy.IsVisible=true;b.Sain.GoalEnemy.CanShoot=true;m.Publish(ECombatDecision.Search);
        Check(b.Sain.Decision.CurrentCombatDecision==ECombatDecision.StandAndShoot&&p.Ordered,"real shooting opportunity retains ordered intent");
        b.Sain.GoalEnemy.IsVisible=b.Sain.GoalEnemy.CanShoot=false;
        b.Memory.IsUnderFire=true;m.Publish(ECombatDecision.Search);Time.time+=0.5f;b.Memory.IsUnderFire=false;m.Publish(ECombatDecision.Search);
        Check(p.Phase==SAINPushPhase.Recovery&&b.Sain.Decision.CurrentCombatDecision==ECombatDecision.SeekCover,"pressure arms recovery beyond the immediate hit without cancelling target");
        Time.time+=3;m.Publish(ECombatDecision.Search);
        Check(p.OwnsMovement&&p.Ordered,"ordered objective resumes same target after recovery");
        foreach(var urgent in new[]{ECombatDecision.AvoidGrenade,ECombatDecision.ThrowGrenade,ECombatDecision.DogFight,ECombatDecision.MeleeAttack,ECombatDecision.Retreat}){
            m.Publish(urgent);Check(b.Sain.Decision.CurrentCombatDecision==urgent&&p.Ordered&&!p.OwnsMovement,"native urgent action interrupts but retains push "+urgent);
        }
        m.Publish(ECombatDecision.SeekCover,ESquadDecision.None,ESelfActionType.FirstAid);
        Check(b.Sain.Decision.CurrentSelfDecision==ESelfActionType.FirstAid&&p.Ordered&&!p.OwnsMovement,"medical publication preserves push mission");
        m.Publish(ECombatDecision.Search);action.Start();action.Update(new DrakiaXYZ.BigBrain.Brains.CustomLayer.ActionData());
        m.Publish(ECombatDecision.SeekCover,ESquadDecision.None,ESelfActionType.Surgery);action.Stop();Time.time+=60;
        m.Publish(ECombatDecision.Search);action.Start();action.Update(new DrakiaXYZ.BigBrain.Brains.CustomLayer.ActionData());
        Check(!p.Exhausted,"medical interruption does not spend movement execution budget");
        Time.time+=7;action.Update(new DrakiaXYZ.BigBrain.Brains.CustomLayer.ActionData());
        Check(p.Exhausted&&p.Reason=="noProgress","stalled push has bounded execution");
        var pathAfterFailure=b.Sain.Mover.ActivePath;action.Update(new DrakiaXYZ.BigBrain.Brains.CustomLayer.ActionData());
        Check(b.Sain.Mover.ActivePath==pathAfterFailure&&SAINFollowerRuntime.GetEngageAttempt(b).EnemyId==null,"stale movement action cannot replace failed push with native engagement");
        b.Follower.SetPushEnemy(12);m.Publish(ECombatDecision.Search);
        Check(p.Exhausted&&!p.OwnsMovement,"repeated same-target Go Forward cannot rearm failed approach");
        b.Memory.IsUnderFire=true;m.Publish(ECombatDecision.Search);b.Memory.IsUnderFire=false;Time.time+=4;m.Publish(ECombatDecision.Search);
        Check(p.Exhausted,"pressure recovery cannot erase exhausted approach");
        var exhaustedRegroup=SAINFollowerRuntime.GetRegroup(b);
        Check(exhaustedRegroup.Mode==SAINRegroupMode.Auto&&p.Ordered,"exhausted ordered approach permits distant-player recovery without erasing the order");
        b.Leader.Position=b.GetPlayer.Position;exhaustedRegroup.Observe();Time.time+=2;exhaustedRegroup.Observe();
        Check(!exhaustedRegroup.Active&&p.Exhausted,"arrival completes recovery without rearming the failed push");
        m.Publish(ECombatDecision.SeekCover);
        b.Sain.GoalEnemy.KnownPlaces.LastKnownPosition=new Vector3(60,0,0);m.Publish(ECombatDecision.Search);
        Check(!p.Exhausted&&p.OwnsMovement,"meaningfully changed knowledge permits a fresh approach");
        m.Publish(ECombatDecision.ShootDistantEnemy);
        Check(b.Sain.Decision.CurrentCombatDecision==ECombatDecision.ShootDistantEnemy&&p.Ordered,"native useful firing retains priority over ordered advance");
        var candidate=new Enemy();candidate.EnemyPlayer.ProfileId="other";b.Sain.EnemyController.KnownEnemies.Add(candidate);
        var selector=new SAINEnemyController(b.Sain);
        Check(selector.ChooseForTest(candidate)==b.Sain.GoalEnemy&&selector.NativeCalls==1&&selector.Publications==1,"target preference restores mission before one native target publication");
        candidate.IsVisible=true;Check(selector.ChooseForTest(candidate)==candidate,"visible temporary threat keeps native target priority");candidate.IsVisible=false;
        b.Sain.Decision.DogFightDecision.DogFightActive=true;Check(selector.ChooseForTest(candidate)==candidate,"dogfight selection overrides ordered preference");b.Sain.Decision.DogFightDecision.DogFightActive=false;
        b.Sain.Medical.TimeSinceShot=0.5f;Check(selector.ChooseForTest(candidate)==candidate,"recent attacker retains native preference");b.Sain.Medical.TimeSinceShot=999;
        b.Follower.ClearTemporaryCombatAggressionOverride("Gogogo");p.Observe();Check(!p.Active,"Gogogo clears ordered objective");
        b.Follower.SetPushEnemy(12);b.Follower.Command=pitTeam.Components.FollowerCommandType.RegroupNearBoss;SAINFollowerRuntime.GetCombatPhase(b);
        Check(!p.Active&&SAINFollowerRuntime.GetRegroup(b).Active,"regroup replaces push before its command is consumed");
        foreach(string reason in new[]{"CoverMe","NeedHelp","ContactHelp"}){
            b=PushBot("cancel"+reason);p=SAINFollowerRuntime.GetPush(b);b.Follower.RequestOrderedPushCancel(reason);p.Observe();
            Check(!p.Active,"core support cancellation releases push "+reason);
        }
        b=PushBot("objectiveInvalidKnowledge");p=SAINFollowerRuntime.GetPush(b);b.Sain.GoalEnemy.KnownPlaces.LastKnownPosition=new Vector3(float.NaN,0,0);p.Observe();
        Check(p.AwaitingTarget&&!p.OwnsMovement,"invalid remembered position pauses ordered movement");
        Time.time+=3;p.Observe();Check(!p.Active,"persistently invalid remembered position releases the order");
        b=PushBot("objectiveForget");p=SAINFollowerRuntime.GetPush(b);b.Sain.GoalEnemy.EnemyKnown=false;p.Observe();
        Check(p.AwaitingTarget&&!b.Sain.GoalEnemy.EnemyKnown,"contact grace does not resurrect native memory");
        Time.time+=3;p.Observe();Check(!p.Active,"sustained native forgetting releases ordered target");
        b=PushBot("objectivePathFailure");p=SAINFollowerRuntime.GetPush(b);b.Sain.Decision.Manager.Publish(ECombatDecision.Search);b.Sain.Mover.Complete=false;
        action=new SAINFollowerMoveToEngageAction(b);action.Start();action.Update(new DrakiaXYZ.BigBrain.Brains.CustomLayer.ActionData());
        Check(p.Exhausted&&p.Reason=="pathRejected","rejected native movement exhausts the approach");
        b=PushBot("objectiveAutomatic",false);p=SAINFollowerRuntime.GetPush(b);b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.Mode==SAINPushMode.Automatic&&p.OwnsMovement,"native automatic Search enters shared approach objective");
        b.Leader.Position=new Vector3(200,0,0);p.Fail("fixture");b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(SAINFollowerRuntime.GetRegroup(b).Mode==SAINRegroupMode.Auto,"failed automatic push yields through existing regroup safety gates");
        b=PushBot("failedOrderedSafety");p=SAINFollowerRuntime.GetPush(b);b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        b.Leader.Position=new Vector3(200,0,0);p.Fail("approachStepInvalid");
        b.Follower.CombatIndependent=true;b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(!SAINFollowerRuntime.GetRegroup(b).Active&&p.Ordered&&p.Exhausted,"On Your Own prevents exhausted-order automatic regroup");
        b.Follower.CombatIndependent=false;b.Sain.GoalEnemy.IsVisible=true;b.Sain.GoalEnemy.CanShoot=true;
        // Fresh contact intentionally reopens the failed attempt; fail again to test fire priority.
        p.Observe();p.Fail("approachStepInvalid");b.Sain.Decision.Manager.Publish(ECombatDecision.ShootDistantEnemy);
        Check(!SAINFollowerRuntime.GetRegroup(b).Active,"useful native fire retains priority over exhausted-order recovery");
        b.Sain.GoalEnemy.IsVisible=false;b.Sain.GoalEnemy.CanShoot=false;p.Fail("approachStepInvalid");
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover,ESquadDecision.None,ESelfActionType.FirstAid);
        Check(!SAINFollowerRuntime.GetRegroup(b).Active&&p.Exhausted,"medicine retains priority over exhausted-order recovery");
        b.Sain.Decision.CurrentSelfDecision=ESelfActionType.None;
        b.Sain.Cover.CoverPoint_MovingTo=CoverAt(5);
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(!SAINFollowerRuntime.GetRegroup(b).Active,"exhausted ordered push still preserves assigned native recovery cover");
        b.Sain.Cover.CoverPoint_MovingTo=null;
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(SAINFollowerRuntime.GetRegroup(b).Mode==SAINRegroupMode.Auto&&p.Ordered&&p.Exhausted,"failed ordered advance yields to regroup after survival clears");
        foreach(var decision in new[]{ECombatDecision.RushEnemy,ECombatDecision.None}){
            b=PushBot("objectiveRush"+decision,false);p=SAINFollowerRuntime.GetPush(b);
            b.Sain.Decision.Manager.Publish(decision,decision==ECombatDecision.None?ESquadDecision.PushSuppressedEnemy:ESquadDecision.None);
            Check(p.OwnsMovement&&b.Sain.Decision.CurrentSquadDecision==ESquadDecision.None,"solo and squad rush share prudent objective "+decision);
        }
        b=PushBot("failedAutoPriority",false);p=SAINFollowerRuntime.GetPush(b);b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        p.Fail("fixture");b.Leader.Position=new Vector3(200,0,0);
        b.Sain.Decision.Manager.Publish(ECombatDecision.ShootDistantEnemy);
        Check(!SAINFollowerRuntime.GetRegroup(b).Active&&b.Sain.Decision.CurrentCombatDecision==ECombatDecision.ShootDistantEnemy,"exhausted auto push cannot regroup over useful native firing");
        var other=new Enemy();other.EnemyPlayer.ProfileId="newThreat";b.Sain.GoalEnemy=other;b.Sain.EnemyController.KnownEnemies.Add(other);
        b.Sain.Decision.Manager.Publish(ECombatDecision.MoveToEngage);
        Check(!SAINFollowerRuntime.GetRegroup(b).Active&&b.Sain.Decision.CurrentCombatDecision==ECombatDecision.MoveToEngage,"exhausted old target cannot regroup over another contact's approach");
        b=PushBot("delayedNativeTarget",false);var delayed=b.Sain.GoalEnemy;b.Sain.GoalEnemy=null;b.Sain.EnemyController.KnownEnemies.Clear();
        b.Follower.SetPushEnemy(12);p=SAINFollowerRuntime.GetPush(b);p.Observe();
        var bindingPhase=SAINFollowerRuntime.GetCombatPhase(b);
        Check(p.Ordered&&bindingPhase!=SAINFollowerCombatPhase.Combat,"bounded pending target survives handoff polling without activating combat");
        Time.time+=1;b.Sain.GoalEnemy=delayed;b.Sain.EnemyController.KnownEnemies.Add(delayed);b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.Ordered&&p.OwnsMovement,"native contact binding resumes accepted pending push");
        b=PushBot("neverBoundTarget",false);b.Sain.GoalEnemy=null;b.Sain.EnemyController.KnownEnemies.Clear();b.Follower.SetPushEnemy(12);
        p=SAINFollowerRuntime.GetPush(b);Time.time+=4;p.Observe();Check(!p.Active,"missing native contact cannot retain an unbound order indefinitely");
        b=PushBot("objectiveIndependent",false);b.Follower.CombatIndependent=true;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(!SAINFollowerRuntime.GetPush(b).Active&&b.Sain.Decision.CurrentCombatDecision==ECombatDecision.Search,"On Your Own preserves native autonomous approach");
        b=PushBot("objectiveUnreachable");p=SAINFollowerRuntime.GetPush(b);b.Sain.GoalEnemy.Path.PathToEnemyStatus=UnityEngine.AI.NavMeshPathStatus.PathPartial;
        b.Sain.Decision.EnemyDecisions.FiringPosition=new Vector3(12,0,0);b.Sain.Decision.Manager.Publish(ECombatDecision.MoveToEngage);
        Check(!p.OwnsMovement&&b.Sain.Decision.CurrentCombatDecision==ECombatDecision.MoveToEngage,"unreachable target retains existing native firing-position action");
        var attempt=SAINFollowerRuntime.GetEngageAttempt(b);attempt.Tick(b.Sain.GoalEnemy,new Vector3(12,0,0));attempt.Fail("fixture");b.Sain.Decision.Manager.Publish(ECombatDecision.MoveToEngage);
        Check(p.Exhausted&&b.Sain.Decision.CurrentCombatDecision==ECombatDecision.SeekCover,"push cannot bypass prior failed firing-position attempt");
        b=PushBot("objectiveGeometry");p=SAINFollowerRuntime.GetPush(b);
        b.Sain.Cover.CoverPoints.Add(CoverAt(-10));b.Sain.Cover.CoverPoints.Add(CoverAt(40));b.Sain.Cover.CoverPoints.Add(CoverAt(15));
        Physics.Blocked=true;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.Reason=="provisionalAdvance"&&p.Destination.Value.x==20,"wrong-direction long-route and blocked firing covers fall back to bounded walking");
        Physics.Blocked=false;Time.time+=2;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.Reason=="forwardCoverAvailable"&&p.Destination.Value.x==15,"provisional approach upgrades to newly usable forward cover");
        int queries=SainBotCoverData.Queries;for(int i=0;i<8;i++)b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(SainBotCoverData.Queries==queries,"stable objective polls do not repeat cover discovery");
        TestPushContactInterruption();
        TestPushRoute();
        var newer=new Vector3(7,0,9);((pitTeam.Components.pitAIBossPlayer)b.BotFollower.BossToFollow).CombatEvents.Claims[b.ProfileId]=newer;
        p.Clear("test");Check(((pitTeam.Components.pitAIBossPlayer)b.BotFollower.BossToFollow).CombatEvents.Claims[b.ProfileId].z==9,"push cleanup preserves newer destination claim");
        b.Follower.SetPushEnemy(12);SainAddonBridge.TryForceReleaseFollowerCombatState(b);Check(!p.Active,"explicit combat release clears objective");
        Check(pitTeam.BigBrain.FollowerPushGeometry.IsForwardPosition(new Vector3(),new Vector3(50,0,0),new Vector3(15,0,0))&&
            !pitTeam.BigBrain.FollowerPushGeometry.IsForwardPosition(new Vector3(),new Vector3(50,0,0),new Vector3(2,0,0)),"shared core forward geometry enforces minimum progress");
    }
    private static void TestPushContactInterruption(){
        var b=PushBot("brickContactGap");var p=SAINFollowerRuntime.GetPush(b);var m=b.Sain.Decision.Manager;
        var enemy=b.Sain.GoalEnemy;var accepted=b.Memory.GoalEnemy;var known=enemy.KnownPlaces.LastKnownPosition;
        m.Publish(ECombatDecision.Search);SAINFollowerRuntime.GetCombatPhase(b);
        var destination=p.Destination;var action=new SAINFollowerMoveToEngageAction(b);action.Start();action.Update(null);
        Time.time+=2;action.Update(null);
        enemy.KnownPlaces.LastKnownPosition=null;action.Update(null);
        Check(p.Ordered&&p.AwaitingTarget&&!p.OwnsMovement&&!b.Sain.Mover.Moving,"lost position pauses and stops owned advance before next publication");
        Check(p.Destination.HasValue&&(p.Destination.Value-destination.Value).sqrMagnitude<.001f&&p.Reason=="contactInterrupted","contact interruption retains committed leg and records cause");
        m.Publish(ECombatDecision.Search);
        Check(m.EventSolo==ECombatDecision.SeekCover&&p.Ordered&&!SAINFollowerRuntime.GetRegroup(b).Active,"missing position cannot downgrade ordered push or trigger auto regroup");
        b.Memory.GoalEnemy=null;b.Sain.GoalEnemy=null;b.Sain.EnemyController.KnownEnemies.Clear();
        Check(SAINFollowerRuntime.GetCombatPhase(b)!=SAINFollowerCombatPhase.Combat&&p.Ordered&&p.AwaitingTarget,"accepted-goal gap retains intent without activating combat");
        action.Update(null);Check(!b.Sain.Mover.Moving,"null native goal cannot continue stale push path");
        Time.time+=.15f;b.Memory.GoalEnemy=accepted;b.Sain.GoalEnemy=enemy;b.Sain.EnemyController.KnownEnemies.Add(enemy);enemy.KnownPlaces.LastKnownPosition=known;
        p.Observe();Check(p.Ordered&&!p.AwaitingTarget&&p.Reason=="contactRestored","same contact restoration records resumption without losing order");
        m.Publish(ECombatDecision.Search);action.Start();action.Update(null);
        Check(p.Ordered&&p.OwnsMovement&&p.Destination.HasValue&&(p.Destination.Value-destination.Value).sqrMagnitude<.001f&&!SAINFollowerRuntime.GetRegroup(b).Active,"Brick replay resumes ordered leg instead of automatic risk rejection");
        Time.time+=4.01f;action.Update(null);
        Check(p.Exhausted&&p.Reason=="noProgress","contact gap preserves pre-interruption stall budget");
        enemy.EnemyKnown=false;p.Observe();Time.time+=.1f;enemy.EnemyKnown=true;m.Publish(ECombatDecision.Search);
        Check(p.Ordered&&p.Exhausted&&!p.OwnsMovement,"contact reacquisition cannot rearm an exhausted same-contact approach");

        b=PushBot("contactDeadline");p=SAINFollowerRuntime.GetPush(b);enemy=b.Sain.GoalEnemy;enemy.EnemyKnown=false;p.Observe();
        for(int i=0;i<5;i++){Time.time+=.5f;b.Follower.SetPushEnemy(12);SAINFollowerRuntime.GetCombatPhase(b);}
        Check(p.Ordered&&p.AwaitingTarget,"repeated polls and same-target orders keep original pending intent");
        Time.time+=.5f;p.Observe();Check(!p.Active&&p.Reason=="targetLost","polls and repeated orders cannot extend three-second contact deadline");
        b=PushBot("lateRestoration");p=SAINFollowerRuntime.GetPush(b);enemy=b.Sain.GoalEnemy;enemy.EnemyKnown=false;p.Observe();
        Time.time+=3.1f;enemy.EnemyKnown=true;p.Observe();Check(!p.Active,"late restoration cannot revive expired order between polls");
        b=PushBot("deathDuringGap");p=SAINFollowerRuntime.GetPush(b);enemy=b.Sain.GoalEnemy;enemy.EnemyKnown=false;p.Observe();
        b.Sain.GoalEnemy=null;b.Sain.EnemyController.KnownEnemies.Clear();enemy.EnemyPlayer.HealthController.IsAlive=false;p.Observe();
        Check(!p.Active&&p.Reason=="targetDead","confirmed death cancels immediately even while native contact is absent");
        b=PushBot("replaceDuringGap");p=SAINFollowerRuntime.GetPush(b);b.Sain.GoalEnemy.EnemyKnown=false;p.Observe();
        b.Follower.Command=pitTeam.Components.FollowerCommandType.RegroupNearBoss;p.Observe();Check(!p.Active,"replacement command immediately cancels interrupted push");
        b=PushBot("releaseDuringGap");p=SAINFollowerRuntime.GetPush(b);b.Sain.GoalEnemy.EnemyKnown=false;p.Observe();
        SainAddonBridge.TryForceReleaseFollowerCombatState(b);Check(!p.Active&&!p.AwaitingTarget,"explicit release clears interrupted order and deadline");
        b=PushBot("failedEngageContactGap");p=SAINFollowerRuntime.GetPush(b);enemy=b.Sain.GoalEnemy;
        var attempt=SAINFollowerRuntime.GetEngageAttempt(b);attempt.Tick(enemy,new Vector3(12,0,0));attempt.Fail("fixture");
        b.Memory.GoalEnemy=null;SAINFollowerRuntime.GetCombatPhase(b);
        Check(p.AwaitingTarget&&attempt.FailedFor(enemy),"combat handoff during contact gap cannot erase failed engagement attempt");
        b=PushBot("automaticContactLoss",false);p=SAINFollowerRuntime.GetPush(b);b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        b.Sain.GoalEnemy.EnemyKnown=false;p.Observe();Check(!p.Active,"automatic pushes retain immediate native contact-loss handling");
        b=PushBot("temporaryOtherThreat");p=SAINFollowerRuntime.GetPush(b);enemy=b.Sain.GoalEnemy;enemy.EnemyKnown=false;p.Observe();
        var other=new Enemy();other.EnemyPlayer.ProfileId="otherThreat";b.Sain.GoalEnemy=other;b.Sain.EnemyController.KnownEnemies.Add(other);b.Leader.Position=new Vector3(200,0,0);
        b.Sain.Decision.Manager.Publish(ECombatDecision.StandAndShoot);
        Check(p.Ordered&&p.EnemyId==enemy.EnemyProfileId&&b.Sain.Decision.CurrentCombatDecision==ECombatDecision.StandAndShoot,"temporary visible-threat action retains interrupted mission identity");
        b.Sain.Decision.CurrentCombatDecision=ECombatDecision.SeekCover;b.Sain.Cover.CoverSeekingState=SAIN.SAINComponent.Classes.ECoverSeekingState.NoCover;
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(p.Ordered&&!SAINFollowerRuntime.GetRegroup(b).Active,"other-contact passive fallback cannot auto regroup over retained order");
        b=PushBot("medicalDuringGap");p=SAINFollowerRuntime.GetPush(b);b.Sain.GoalEnemy.EnemyKnown=false;p.Observe();
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover,ESquadDecision.None,ESelfActionType.FirstAid);
        Check(p.Ordered&&b.Sain.Decision.CurrentSelfDecision==ESelfActionType.FirstAid,"native medicine remains higher priority during contact grace");
    }
    private static void TestPushRoute(){
        var b=PushBot("bentRoute");var p=SAINFollowerRuntime.GetPush(b);
        UnityEngine.AI.NavMesh.SampleAllowed=v=>!(Math.Abs(v.x-20)<.01f&&Math.Abs(v.z)<.01f);
        UnityEngine.AI.NavMesh.Route=new[]{new Vector3(),new Vector3(-5,0,0),new Vector3(-5,0,25),new Vector3(50,0,25),new Vector3(50,0,0)};
        b.Sain.GoalEnemy.EnemyPosition=new Vector3(-300,0,-300);
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.OwnsMovement&&p.Reason=="routeAdvance"&&p.Destination.Value.x==-5&&p.Destination.Value.z==15,"complete detour advances along route even when initial leg is away from hidden enemy");
        var action=new SAINFollowerMoveToEngageAction(b);action.Start();action.Update(null);
        Check(b.Sain.Mover.Runs==1&&b.Sain.Mover.Destination.z==15,"long committed route leg permits native sprint through existing SAIN movement action");
        int probes=UnityEngine.AI.NavMesh.Calculations;for(int i=0;i<8;i++)b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(UnityEngine.AI.NavMesh.Calculations==probes,"committed route fallback does not recalculate on every decision poll");
        Time.time+=7;action.Update(null);Check(p.Exhausted&&p.Reason=="noProgress","route fallback retains bounded stalled-approach failure");
        b=PushBot("invalidFallbackRoute");p=SAINFollowerRuntime.GetPush(b);
        UnityEngine.AI.NavMesh.SampleAllowed=v=>v.x!=20;UnityEngine.AI.NavMesh.RouteComplete=false;
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.Exhausted&&p.Reason=="approachRouteIncomplete","stale native complete status cannot admit an actually partial fallback path");
        var route=new SAINFollowerApproachRoute();UnityEngine.AI.NavMesh.SampleAllowed=null;UnityEngine.AI.NavMesh.RouteComplete=true;
        UnityEngine.AI.NavMesh.Route=new[]{new Vector3(),new Vector3(40,0,0)};
        Check(!route.TryStep(new Vector3(),new Vector3(50,0,0),out _,out var why)&&why=="approachRouteCorners","route endpoint must reach remembered position");
        UnityEngine.AI.NavMesh.Route=new Vector3[65];UnityEngine.AI.NavMesh.Route[63]=new Vector3(50,0,0);
        Check(!route.TryStep(new Vector3(),new Vector3(50,0,0),out _,out why)&&why=="approachRouteCorners","truncated route-corner buffer is rejected");
        UnityEngine.AI.NavMesh.Route=null;pitTeam.Utils.Utils.PathScale=2;
        Check(!route.TryStep(new Vector3(),new Vector3(50,0,0),out _,out why)&&why=="approachStepInvalid","actual leg route remains under core maximum approach distance");
        pitTeam.Utils.Utils.PathScale=1;
        b=PushBot("reservedFallbackRoute");p=SAINFollowerRuntime.GetPush(b);
        UnityEngine.AI.NavMesh.SampleAllowed=v=>v.x!=20;UnityEngine.AI.NavMesh.Route=new[]{new Vector3(),new Vector3(0,0,25),new Vector3(50,0,25),new Vector3(50,0,0)};
        ((pitTeam.Components.pitAIBossPlayer)b.BotFollower.BossToFollow).CombatEvents.Claims["other"]=new Vector3(0,0,20);
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.Exhausted&&p.Reason=="approachReserved","route fallback respects teammate destination reservations");
        UnityEngine.AI.NavMesh.SampleAllowed=null;UnityEngine.AI.NavMesh.Route=null;
    }

}

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using pitTeam;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using SAIN.Components;
using SAIN.Models.Enums;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes;
using SAIN.SAINComponent.SubComponents.CoverFinder;
using UnityEngine;

namespace UnityEngine {
    public class Collider { public CoverPoint Point; }
    public partial struct Vector3 { public static Vector3 up=>new Vector3(0,1,0); }
}
namespace EFT { public static class LayersMaskController { public const int HighPolyWithTerrainNoGrassMask=1,HighPolyWithTerrainMask=2; } }
namespace SAIN.Preset.Shared.GlobalSettings {
    public class GlobalSettingsClass {
        public static GlobalSettingsClass Instance=new GlobalSettingsClass();public GeneralSettings General=new GeneralSettings();
        public class GeneralSettings { public CoverSettings Cover=new CoverSettings(); }
        public class CoverSettings { public float CoverMinHeight=0.75f; }
    }
}
namespace SAIN.Components {
    public class Medical {public float TimeSinceShot=float.MaxValue;public BotSurgery Surgery;}
    public partial class BotComponent {public Medical Medical=new Medical();public Vector3 NavMeshPosition=>Position;}
    public partial class Mover {
        public bool GoToCoverPoint(CoverPoint point,bool sprint,ESprintUrgency urgency) =>
            point.MovementAccepted && WalkToPoint(point.Position);
    }
}
namespace SAIN.SAINComponent.Classes {
    public class SAINCoverClass : SAIN.Components.Cover {
        public BotOwner BotOwner{get;}public object CoverFinder=new object();
        private bool _shallSprint=false;
        public int NativeCalls;public CoverPoint NativePoint;
        public SAINCoverClass(BotOwner bot){BotOwner=bot;}
        [MethodImpl(MethodImplOptions.NoInlining)]
        private CoverPoint FindCoverPoint(){NativeCalls++;return NativePoint;}
        public CoverPoint FindForTest(){CoverInUse=null;return CoverPoint_MovingTo=FindCoverPoint();}
        public void StopSeekingCover(){CoverInUse=null;CoverPoint_MovingTo=null;}
    }
}
namespace SAIN.SAINComponent.SubComponents.CoverFinder {
    public class NativeCoverPath {public float PathLength;}
    public partial class CoverPoint {
        public NativeCoverPath PathData=new NativeCoverPath();public bool Valid=true,MovementAccepted=true;
    }
    public class SainBotColliderData {public Collider Collider;}
    public class SainBotCoverData {
        public static List<SainBotColliderData> Scene=new List<SainBotColliderData>();
        public static int Queries;public static Vector3 LastOrigin;
        public List<SainBotColliderData> ValidCollidersList=new List<SainBotColliderData>();
        public struct BotColliderQueryParams {public Vector3 origin,halfExtents,minColliderSize,maxColliderSize;public int mask;}
        public void OverlapBoxAndFilter(BotColliderQueryParams p){Queries++;LastOrigin=p.origin;ValidCollidersList=new List<SainBotColliderData>(Scene);}
        public void HandleLists(Vector3 p){ValidCollidersList.Sort((a,b)=>(a.Collider.Point.Position-p).magnitude.CompareTo((b.Collider.Point.Position-p).magnitude));}
    }
    public class CoverAnalyzer {
        public static int Creates,Rechecks;
        public CoverAnalyzer(BotComponent bot,object finder){}
        public bool CheckCreateNewCoverPoint(Collider c,Vector3 threat,Vector3 bot,Vector3 direction,out CoverPoint p,out string reason){Creates++;p=c.Point;reason="";return p.Valid;}
        public bool RecheckCoverPoint(CoverPoint p,Vector3 threat,Vector3 direction,Vector3 bot,out string reason){Rechecks++;reason="";return p.Valid;}
    }
}
public static partial class CombatChecks {
    private static CoverPoint CoverAt(float x,float path=-1)=>new CoverPoint{Position=new Vector3(x,0,0),PathData=new NativeCoverPath{PathLength=path<0?Math.Abs(x):path}};
    private static BotOwner CoverBot(string name){SainBotCoverData.Scene.Clear();return RegroupBot(name);}
    private static void Arrive(BotOwner b,CoverPoint p){b.GetPlayer.Position=p.Position;b.Sain.Mover.Moving=false;b.Sain.Cover.CoverPoint_MovingTo=null;b.Sain.Cover.CoverInUse=p;b.Sain.Cover.CoverSeekingState=ECoverSeekingState.HoldInCover;SAINFollowerRuntime.GetCover(b).Observe();}
    private static void TestCover(){
        Time.time=1000;
        var b=CoverBot("coverChoice");var away=CoverAt(-5);var near=CoverAt(35);b.Sain.Cover.CoverPoints.Add(away);b.Sain.Cover.CoverPoints.Add(near);b.Sain.Cover.NativePoint=away;
        var chosen=b.Sain.Cover.FindForTest();
        Check(chosen==near&&b.Sain.Cover.NativeCalls==0,"boss-oriented cover beats a shorter route in the opposite direction");
        Check(b.Sain.Mover.Paths==1&&b.Sain.Mover.Destination.x==35,"preferred cover starts native movement once");
        Check(Math.Abs(SainBotCoverData.LastOrigin.x-40)<0.01f,"candidate discovery is centered on the player");
        var policy=SAINFollowerRuntime.GetCover(b);int queries=SainBotCoverData.Queries;
        policy.TrySelect(false,out _);Check(SainBotCoverData.Queries==queries,"unchanged geometry does not repeat the boss-area scan");
        b.Leader.Position=new Vector3(51,0,0);policy.TrySelect(false,out _);Check(SainBotCoverData.Queries==queries+1,"meaningful player sector change permits a new scan");
        b.Leader.Position=new Vector3(40,0,0);
        Arrive(b,near);b.Leader.Position=new Vector3(100,0,0);
        Check(policy.HoldsArrival(b.Sain.GoalEnemy),"reached cover receives a stable arrival hold despite player distance");
        Check(!RegroupDecision(b),"automatic regroup cannot discard freshly reached cover");
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(b.Sain.Decision.CurrentCombatDecision==ECombatDecision.SeekCover,"ordinary search is deferred during arrival use");
        b.Sain.Decision.Manager.Publish(ECombatDecision.MoveToEngage);
        Check(b.Sain.Decision.CurrentCombatDecision==ECombatDecision.SeekCover,"ordinary engagement movement cannot immediately replace arrival use");
        b.Sain.Decision.Manager.Publish(ECombatDecision.StandAndShoot);
        Check(b.Sain.Decision.CurrentCombatDecision==ECombatDecision.StandAndShoot,"firing action retains priority during arrival hold");
        b.Sain.GoalEnemy.IsVisible=true;b.Sain.GoalEnemy.CanShoot=true;
        Check(!policy.HoldsArrival(b.Sain.GoalEnemy),"visible shootable contact interrupts ordinary arrival hold");
        b.Sain.GoalEnemy.IsVisible=false;b.Sain.GoalEnemy.CanShoot=false;
        b.Follower.Command=FollowerCommandType.RegroupNearBoss;
        Check(!policy.HoldsArrival(b.Sain.GoalEnemy),"explicit regroup interrupts arrival hold");b.Follower.Command=FollowerCommandType.None;
        Time.time+=3.1f;policy.Selected(near);Arrive(b,near);
        Check(!policy.HoldsArrival(b.Sain.GoalEnemy),"reselecting the same reached cover cannot rearm its arrival timer");
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(b.Sain.Decision.CurrentCombatDecision==ECombatDecision.MoveToEngage&&SAINFollowerRuntime.GetPush(b).OwnsMovement,"hold expiry permits an approach objective without forcing regroup");
        SAINFollowerRuntime.GetPush(b).Clear("coverFixturePassiveContext");
        b.Sain.Decision.CurrentCombatDecision=ECombatDecision.SeekCover;
        Check(RegroupDecision(b),"passive distant cover can yield naturally to regroup after use");

        var solo=CoverBot("independentCover");solo.Follower.CombatIndependent=true;solo.Sain.Cover.NativePoint=away;solo.Sain.Cover.CoverPoints.Add(near);
        queries=SainBotCoverData.Queries;
        Check(solo.Sain.Cover.FindForTest()==away&&solo.Sain.Cover.NativeCalls==1&&SainBotCoverData.Queries==queries,"On Your Own keeps native cover choice and performs no boss scan");
        var core=CoverBot("coreCover");core.Follower.CombatTactic=FollowerCombatTactic.Balanced;core.Sain.Cover.NativePoint=away;
        Check(core.Sain.Cover.FindForTest()==away&&core.Sain.Cover.NativeCalls==1,"core tactic never enters addon cover selection");
        var urgent=CoverBot("ambushCover");urgent.Memory.IsUnderFire=true;urgent.Sain.Cover.NativePoint=away;urgent.Sain.Cover.CoverPoints.Add(near);
        Check(urgent.Sain.Cover.FindForTest()==away&&urgent.Sain.Cover.NativeCalls==1,"ambush retains immediate native cover escape");
        Arrive(urgent,away);urgent.Memory.IsUnderFire=false;
        Check(SAINFollowerRuntime.GetCover(urgent).HoldsArrival(urgent.Sain.GoalEnemy),"recovery arrival is used before moving back toward the boss");
        urgent.Sain.Decision.Manager.Publish(ECombatDecision.DogFight);
        Check(urgent.Sain.Decision.CurrentCombatDecision==ECombatDecision.DogFight,"urgent dogfight interrupts recovery hold");
        urgent.Sain.Cover.CoverInUse.Spotted=true;
        Check(!SAINFollowerRuntime.GetCover(urgent).HoldsArrival(urgent.Sain.GoalEnemy),"compromised cover does not retain arrival protection");away.Spotted=false;
        var healing=CoverBot("healingCover");healing.Sain.Decision.CurrentSelfDecision=ESelfActionType.FirstAid;healing.Sain.Cover.NativePoint=away;
        Check(healing.Sain.Cover.FindForTest()==away&&healing.Sain.Cover.NativeCalls==1,"medical recovery retains native nearest-cover selection");

        var discovery=CoverBot("newBossCover");var found=CoverAt(36);SainBotCoverData.Scene.Add(new SainBotColliderData{Collider=new Collider{Point=found}});discovery.Sain.Cover.NativePoint=away;
        Check(discovery.Sain.Cover.FindForTest()==found,"boss-area discovery finds cover absent from native five-point pool");
        found.Valid=false;Time.time+=1.1f;SAINFollowerRuntime.GetCover(discovery).Observe();
        Check(found.CoverData.IsBad&&discovery.Sain.Cover.CoverPoint_MovingTo==null&&!discovery.Sain.Mover.Moving,"invalid cover releases its matching path for native reselection");
        var changedPath=CoverBot("coverForeignPath");var invalid=CoverAt(35);changedPath.Sain.Cover.CoverPoints.Add(invalid);changedPath.Sain.Cover.FindForTest();
        changedPath.Sain.Mover.WalkToPoint(new Vector3(99,0,0));invalid.Valid=false;SAINFollowerRuntime.GetCover(changedPath).Observe();
        Check(changedPath.Sain.Mover.Moving&&changedPath.Sain.Mover.Destination.x==99,"invalid-cover cleanup leaves another movement destination running");
        var reject=CoverBot("rejectedBossCover");var bad=CoverAt(35);bad.Valid=false;reject.Sain.Cover.CoverPoints.Add(bad);reject.Sain.Cover.NativePoint=away;
        Check(reject.Sain.Cover.FindForTest()==away,"unsafe boss cover falls back to native selection");
        bad.Valid=true;bad.MovementAccepted=false;
        Check(reject.Sain.Cover.FindForTest()==away,"rejected boss-cover movement falls back instead of stalling");
        var multi=CoverBot("rankBossCover");var shorter=CoverAt(25,10);var closer=CoverAt(35,20);multi.Sain.Cover.CoverPoints.Add(shorter);multi.Sain.Cover.CoverPoints.Add(closer);
        Check(multi.Sain.Cover.FindForTest()==closer,"boss cover ranking uses the shared core travel-plus-player-distance score");
        var step=CoverBot("bosswardStep");step.Leader.Position=new Vector3(100,0,0);var intermediate=CoverAt(20);step.Sain.Cover.CoverPoints.Add(intermediate);step.Sain.Cover.CoverPoints.Add(away);
        Check(step.Sain.Cover.FindForTest()==intermediate,"safe intermediate cover toward a distant player beats outward fallback");
        var order=CoverBot("coverPushOrder");var orderCover=CoverAt(35);order.Sain.Cover.NativePoint=orderCover;order.Sain.Cover.FindForTest();Arrive(order,orderCover);
        order.Follower.SetPushEnemy(20);
        Check(!SAINFollowerRuntime.GetCover(order).HoldsArrival(order.Sain.GoalEnemy),"accepted Go Forward releases arrival hold without resetting cover selection");
        var occupied=CoverBot("occupiedBossCover");var occupiedCover=CoverAt(35);occupied.Sain.Cover.CoverPoints.Add(occupiedCover);occupied.Sain.Cover.NativePoint=away;
        var squadBoss=new pitAIBossPlayer();occupied.BotFollower.BossToFollow=squadBoss;squadBoss.CombatEvents.Claims["other"]=occupiedCover.Position;
        Check(occupied.Sain.Cover.FindForTest()==away,"another follower destination claim excludes preferred cover");
        squadBoss.CombatEvents.Claims.Clear();occupied.Sain.Cover.FindForTest();
        squadBoss.CombatEvents.Claims[occupied.ProfileId]=new Vector3(99,0,0);SAINFollowerRuntime.GetCover(occupied).Clear();
        Check(squadBoss.CombatEvents.Claims[occupied.ProfileId].x==99,"cover cleanup preserves a newer movement owner's claim");
        var absent=CoverBot("missingAddonCover");absent.Sain.Cover.NativePoint=away;pitFireTeam.IsSAINAddonInstalled=false;
        Check(absent.Sain.Cover.FindForTest()==away&&absent.Sain.Cover.NativeCalls==1,"missing addon leaves native selection untouched");pitFireTeam.IsSAINAddonInstalled=true;
        var otherFloor=CoverBot("otherFloorCover");var upstairs=CoverAt(35);upstairs.Position=new Vector3(35,4,0);otherFloor.Sain.Cover.CoverPoints.Add(upstairs);otherFloor.Sain.Cover.NativePoint=away;
        Check(otherFloor.Sain.Cover.FindForTest()==away,"boss preference does not choose another floor as nearby cover");
        var capped=CoverBot("boundedScan");for(int i=0;i<80;i++)SainBotCoverData.Scene.Add(new SainBotColliderData{Collider=new Collider{Point=CoverAt(10+i)}});
        int creates=CoverAnalyzer.Creates;capped.Sain.Cover.FindForTest();
        Check(CoverAnalyzer.Creates-creates==4,"boss scan spends at most four native probes in one frame");
        capped.Sain.Cover.FindForTest();
        Check(CoverAnalyzer.Creates-creates==4,"repeated layer polls cannot reset the cover frame budget");
        for(int frame=0;frame<10;frame++){Time.time+=0.05f;capped.Sain.Cover.FindForTest();}
        Check(CoverAnalyzer.Creates-creates==32,"incremental boss scan retains the total 32-candidate bound");
        Check(capped.Sain.Cover.CoverPoint_MovingTo!=null,"incremental scan eventually selects a ranked cover");
        int rechecks=CoverAnalyzer.Rechecks;
        capped.Sain.Cover.FindForTest();
        Check(CoverAnalyzer.Rechecks==rechecks,"cached candidates do not repeat native physics and path validation");
        var slow=CoverBot("slowBudgetedScan");for(int i=0;i<32;i++)SainBotCoverData.Scene.Add(new SainBotColliderData{Collider=new Collider{Point=CoverAt(10+i)}});
        bool slowSelected=false;for(int frame=0;frame<12;frame++){Time.time+=.3f;slowSelected |= slow.Sain.Cover.FindForTest()!=null;}
        Check(slowSelected,"slow native decision cadence cannot starve incremental validation through cache expiry");
        var pending=CoverBot("pendingNoCover");for(int i=0;i<32;i++)SainBotCoverData.Scene.Add(new SainBotColliderData{Collider=new Collider{Point=CoverAt(10+i)}});
        pending.Sain.Cover.FindForTest();var dogfight=new SAIN.SAINComponent.Classes.Mover.DogFight(pending.Sain);
        dogfight.DogFightMove(true,pending.Sain.GoalEnemy);
        Check(dogfight.Moves==0,"unfinished boss scan cannot trigger native no-cover aggressive advance");
        dogfight.DogFightMove(false,pending.Sain.GoalEnemy);Check(dogfight.Moves==1,"defensive dogfight retains priority while cover scan is pending");
        pending.Sain.Decision.CurrentCombatDecision=ECombatDecision.DogFight;dogfight.DogFightMove(true,pending.Sain.GoalEnemy);
        Check(dogfight.Moves==2,"actual native dogfight decision can preempt pending cover selection");
        var idle=CoverBot("idleRoute");int calls=pitTeam.Utils.Utils.PathCalls;
        for(int i=0;i<10;i++){Time.time+=0.6f;SAINFollowerRuntime.GetRegroup(idle).Observe();}
        Check(pitTeam.Utils.Utils.PathCalls==calls,"inactive regroup observes orders without calculating player routes");
        var regroup=SAINFollowerRuntime.GetRegroup(idle);
        regroup.TryGetPlayerDistance(idle.Leader.Position,out _);var scratch=pitTeam.Utils.Utils.LastPath;
        calls=pitTeam.Utils.Utils.PathCalls;regroup.TryGetPlayerDistance(idle.Leader.Position,out _);
        Check(pitTeam.Utils.Utils.PathCalls==calls,"explicit player route consumers share the cached measurement");
        Time.time+=0.6f;regroup.TryGetPlayerDistance(idle.Leader.Position,out _);
        Check(scratch!=null&&ReferenceEquals(scratch,pitTeam.Utils.Utils.LastPath),"distance probes reuse their native scratch path");
        SainBotCoverData.Scene.Clear();
        policy.Clear();Check(!policy.HoldsArrival(b.Sain.GoalEnemy),"combat release clears cover commitment");
        TestRegroupChurn();
    }
}

using System;
using System.Collections.Generic;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.Classes;
using SAIN.SAINComponent.SubComponents.CoverFinder;
using UnityEngine;

namespace UnityEngine {
    public static class Mathf { public static float Abs(float value)=>Math.Abs(value); public static float Min(float a,float b)=>Math.Min(a,b);public static float Max(float a,float b)=>Math.Max(a,b);public static float Lerp(float a,float b,float t)=>a+(b-a)*t; }
    public partial struct Vector3 {public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;}
}
namespace EFT {
    public class BotFollower {public object BossToFollow;}
    public class ShootData {public float LastTriggerPressd;public bool Shooting;}
    public partial class BotOwner {public BotFollower BotFollower=new BotFollower();public ShootData ShootData=new ShootData();}
}
namespace pitTeam.Components {
    public static class PickupFollowerPersonality {public const float RegroupMaxTriggerMultiplier=2.35f;}
    public partial class CombatEvents {
        public Dictionary<string,Vector3> Claims=new Dictionary<string,Vector3>();
        public bool TryFindBossSpreadDestination(BotOwner o,Vector3 p,float min,float max,float floor,float spacing,out Vector3 target){target=default;return false;}
        public bool HasDestinationClaimConflict(BotOwner o,Vector3 p,float spacing){foreach(var pair in Claims)if(pair.Key!=o.ProfileId&&(pair.Value-p).magnitude<spacing)return true;return false;}
        public void UpsertDestinationClaim(BotOwner o,Vector3 p,float ttl){Claims[o.ProfileId]=p;}
        public void TryReleaseDestinationClaim(BotOwner o,Vector3 p,float tolerance){if(Claims.TryGetValue(o.ProfileId,out var old)&&(p-old).magnitude<=tolerance)Claims.Remove(o.ProfileId);}
    }
}
namespace pitTeam.BigBrain {
    public static class FollowerCombatRegroupObjective {
        public const float TightRegroupCompleteDistance=4f;
        public static float GetOrderedRegroupDistance(FollowerCombatTactic t)=>CombatDistanceConfiguration.Instance.Factory?10f:t==FollowerCombatTactic.Marksman?24f:18f;
        public static bool IsSameBossLevel(Vector3 a,Vector3 b)=>Math.Abs(a.y-b.y)<=1.75f;
    }
    public sealed partial class FollowerCombatCommon {public static float GetSafeRegroupDistance(float nav,float direct)=>Math.Max(nav,direct);
        public static float GetCommittedCoverHoldDuration(string reason)=>reason=="retreatSafeCover"?3.5f:3f;
        public static float ScoreBossCover(float path,float boss)=>path*0.5f+boss;}
}
namespace pitTeam.Modules {
    public class CombatDistanceConfiguration {
        public static CombatDistanceConfiguration Instance=new CombatDistanceConfiguration();
        public bool Factory,Urban;public float Trigger=18;
        public float GetBossRegroupTriggerDistance(BotOwner o)=>Trigger;
        public float GetRegroupNeededDistanceMarksman(BotOwner o)=>Trigger*(Factory?2f:1.5f);
        public float GetRegroupBossMoveRefreshDistance()=>10;
        public float GetBossCoverSearchRadius()=>25;
        public bool IsUrbanDetourRegroup(float direct,float path)=>Urban&&direct<45&&path>75;
    }
    public static partial class BattleRecorder {public static List<string> Records=new List<string>();public static void RecordObjectiveSwitch(BotOwner o,string mode,string reason){Records.Add(mode+":"+reason);}}
}
namespace pitTeam.Utils {
    public static class FollowerAwareness {public static bool Damaged;public static bool WasRecentlyDamaged(BotOwner owner)=>Damaged;}
    public static class Utils {
        public static bool PathComplete=true;public static float PathScale=1;public static int PathCalls;public static UnityEngine.AI.NavMeshPath LastPath;
        public static bool TryGetCompletePathDistance(Vector3 a,Vector3 b,out float distance,UnityEngine.AI.NavMeshPath path=null){PathCalls++;LastPath=path;distance=(a-b).magnitude*PathScale;return PathComplete;}
    }
}
namespace SAIN.SAINComponent.Classes {public enum ECoverSeekingState {None,NoCover,MoveTo,Shift,HoldInCover}}
namespace SAIN.SAINComponent.Classes.Decision {
    // Exercise production Harmony interception and native publication ownership. Choices
    // represent outputs of the separately source-verified SAIN decision branches.
    public class BotDecisionManager : SAIN.SAINComponent.BotBase {
        public int SoloEvaluations,Publications,Events;
        public event Action<ECombatDecision,ESquadDecision,ESelfActionType,Enemy,SAIN.Components.BotComponent> OnDecisionMade;
        public ECombatDecision EventSolo;public ESquadDecision EventSquad;
        public BotDecisionManager(SAIN.Components.BotComponent bot):base(bot){}
        public void Frame(ECombatDecision solo=ECombatDecision.SeekCover){
            var provider=new SquadDecisionClass(Bot);
            if(provider.GetDecision(out var squad,Bot.GoalEnemy)){Publish(ECombatDecision.None,squad);return;}
            SoloEvaluations++;
            Publish(solo);
        }
        public void Publish(ECombatDecision solo,ESquadDecision squad=ESquadDecision.None,ESelfActionType self=ESelfActionType.None){SetDecisions(solo,squad,self,Bot.GoalEnemy);}
        public void Reset(){SetDecisions(ECombatDecision.None,ESquadDecision.None,ESelfActionType.None,null);}
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void SetDecisions(ECombatDecision solo,ESquadDecision squad,ESelfActionType self,Enemy enemy){
            Publications++;
            bool changed=solo!=Bot.Decision.CurrentCombatDecision||squad!=Bot.Decision.CurrentSquadDecision||self!=Bot.Decision.CurrentSelfDecision;
            Bot.Decision.CurrentCombatDecision=solo;Bot.Decision.CurrentSquadDecision=squad;Bot.Decision.CurrentSelfDecision=self;
            if(changed){Events++;EventSolo=solo;EventSquad=squad;OnDecisionMade?.Invoke(solo,squad,self,enemy,Bot);}
        }
    }
}
namespace SAIN.SAINComponent.SubComponents.CoverFinder {
    public class CoverData {public bool IsBad;}
    public partial class CoverPoint {public Vector3 Position;public bool Spotted;public CoverData CoverData=new CoverData();}
}
public static partial class CombatChecks {
    private static BotOwner RegroupBot(string id,float playerX=40){
        var bot=Spawn(id);new SAINFollowerSoloCombatLayer(bot,74);new SAINFollowerSquadCombatLayer(bot,75);Tick();
        bot.Leader.Position=new Vector3(playerX,0,0);bot.Sain.GoalEnemy=new Enemy();
        bot.Memory.GoalEnemy.ProfileId=bot.Sain.GoalEnemy.EnemyProfileId;
        bot.Sain.Decision.CurrentCombatDecision=ECombatDecision.SeekCover;
        bot.Sain.Cover.CoverSeekingState=ECoverSeekingState.NoCover;
        bot.BotFollower.BossToFollow=new pitAIBossPlayer();return bot;
    }
    private static bool RegroupDecision(BotOwner bot){
        bot.Sain.Decision.Manager.Frame();
        return bot.Sain.Decision.CurrentSquadDecision==ESquadDecision.Regroup;
    }
    private static void TestAutoRegroupPriority(){
        // All fresh useful decisions survive even at extreme player distance, including
        // MoveToEngage after an unreachable-enemy firing-position search succeeds.
        foreach(ECombatDecision decision in Enum.GetValues(typeof(ECombatDecision))){
            if(decision==ECombatDecision.SeekCover)continue;
            var bot=RegroupBot("autoPriority"+decision,200);
            bot.Sain.Decision.Manager.Publish(decision);
            Check(!SAINFollowerRuntime.GetRegroup(bot).Active&&bot.Sain.Decision.CurrentCombatDecision==((decision==ECombatDecision.Search||decision==ECombatDecision.RushEnemy)?ECombatDecision.MoveToEngage:decision),"fresh "+decision+" takes priority over distant-player regroup");
        }
        foreach(ESquadDecision decision in Enum.GetValues(typeof(ESquadDecision))){
            if(decision==ESquadDecision.None)continue;
            var bot=RegroupBot("autoSquadPriority"+decision,200);
            bot.Sain.Decision.Manager.Publish(ECombatDecision.None,decision);
            Check(!SAINFollowerRuntime.GetRegroup(bot).Active&&(decision==ESquadDecision.PushSuppressedEnemy?SAINFollowerRuntime.GetPush(bot).OwnsMovement:bot.Sain.Decision.CurrentSquadDecision==decision),"fresh squad "+decision+" retains priority");
        }
        var held=RegroupBot("autoHeld",200);var objective=SAINFollowerRuntime.GetRegroup(held);var manager=held.Sain.Decision.Manager;
        held.Sain.Cover.CoverSeekingState=ECoverSeekingState.None;
        Check(!RegroupDecision(held),"first SeekCover gets an action tick to find a destination");
        held.Sain.Cover.CoverSeekingState=ECoverSeekingState.MoveTo;
        Check(!RegroupDecision(held),"committed cover movement beats automatic regroup");
        held.Sain.Cover.CoverSeekingState=ECoverSeekingState.Shift;
        Check(!RegroupDecision(held),"cover shift is a commitment, not a passive fallback");
        held.Sain.Cover.CoverSeekingState=ECoverSeekingState.NoCover;held.Sain.Mover.Moving=true;
        Check(!RegroupDecision(held),"live movement prevents auto regroup even with stale NoCover state");
        held.Sain.Mover.Moving=false;held.Sain.Cover.CoverPoint_MovingTo=new CoverPoint();
        Check(!RegroupDecision(held),"assigned cover is preserved before movement starts");
        held.Sain.Cover.CoverPoint_MovingTo=null;held.Sain.Cover.CoverSeekingState=ECoverSeekingState.HoldInCover;
        Check(!RegroupDecision(held),"stale hold without an actual cover point is not eligible");
        held.Sain.Cover.CoverInUse=new CoverPoint{Spotted=true};
        Check(!RegroupDecision(held),"compromised cover retains recovery priority");
        held.Sain.Cover.CoverInUse.Spotted=false;held.Sain.Cover.CoverInUse.CoverData.IsBad=true;
        Check(!RegroupDecision(held),"bad cover retains recovery priority");
        held.Sain.Cover.CoverInUse.CoverData.IsBad=false;
        held.UsingMedical=true;Check(!RegroupDecision(held),"actual medicine blocks passive regroup");held.UsingMedical=false;
        manager.Publish(ECombatDecision.SeekCover,ESquadDecision.None,ESelfActionType.Reload);
        Check(!objective.Active&&held.Sain.Decision.CurrentSelfDecision==ESelfActionType.Reload,"fresh recovery decision is published without automatic regroup");
        held.Sain.Decision.CurrentSelfDecision=ESelfActionType.None;
        held.Sain.GoalEnemy.IsVisible=true;held.Sain.GoalEnemy.CanShoot=true;Check(!RegroupDecision(held),"visible shootable contact wins even beyond extreme-distance grace");held.Sain.GoalEnemy.IsVisible=false;held.Sain.GoalEnemy.CanShoot=false;

        var visible=new Enemy{IsVisible=true,CanShoot=true};held.Sain.EnemyController.KnownEnemies.Add(visible);
        Check(!RegroupDecision(held),"another visible enemy prevents automatic passive fallback");held.Sain.EnemyController.KnownEnemies.Clear();
        held.Follower.Command=FollowerCommandType.PushEnemy;
        Check(!RegroupDecision(held)&&held.Follower.Command==FollowerCommandType.PushEnemy,"pending push is retained and prevents automatic regroup");held.Follower.Command=FollowerCommandType.None;
        held.Sain.GoalEnemy.EnemyPlayer.HealthController.IsAlive=false;
        Check(!RegroupDecision(held),"dead remembered enemy cannot start auto regroup");held.Sain.GoalEnemy.EnemyPlayer.HealthController.IsAlive=true;
        held.Sain.Decision.ResetDecisions(false);
        Check(!objective.Active&&held.Sain.Decision.CurrentCombatDecision==ECombatDecision.None,"native reset cannot start automatic regroup");
        Check(!RegroupDecision(held),"return from reset first establishes the native cover action");
        Check(RegroupDecision(held)&&objective.Mode==SAINRegroupMode.Auto,"established passive cover hold can regroup after combat options fail");
        int evaluations=manager.SoloEvaluations;manager.Frame();
        Check(objective.Active&&manager.SoloEvaluations==evaluations,"active regroup remains objective-owned without re-entering solo pursuit");
        held.Follower.CombatIndependent=true;objective.Observe();
        Check(!objective.Active,"independent mode cancels an automatic objective");
        var ordinary=RegroupBot("ordinaryPublisher");ordinary.Follower.CombatTactic=FollowerCombatTactic.Balanced;
        ordinary.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(!SAINFollowerRuntime.GetRegroup(ordinary).Active&&ordinary.Sain.Decision.CurrentCombatDecision==ECombatDecision.SeekCover,"publication filter does not replace other tactics' native result");
        var unready=Spawn("unreadyPublisher");unready.Sain.GoalEnemy=new Enemy();
        unready.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(unready.Sain.Decision.CurrentCombatDecision==ECombatDecision.SeekCover,"unready addon preserves native publication");
    }
    private static void TestPassiveRegroupContact(){
        foreach(float separation in new[]{25f,100f}){
            var b=RegroupBot("laneOnly"+separation,separation);
            b.Sain.GoalEnemy.IsVisible=false;b.Sain.GoalEnemy.CanShoot=true;b.Sain.GoalEnemy.InLineOfSight=true;b.Sain.GoalEnemy.TimeSinceSeen=20;
            b.Sain.Cover.CoverSeekingState=ECoverSeekingState.HoldInCover;b.Sain.Cover.CoverInUse=new CoverPoint();
            Check(RegroupDecision(b),"hidden geometric lane cannot pin a cold passive cover hold at "+separation);
        }
        var bot=RegroupBot("freshSightGrace",25);var regroup=SAINFollowerRuntime.GetRegroup(bot);
        bot.Sain.GoalEnemy.IsVisible=true;bot.Sain.GoalEnemy.CanShoot=false;
        Check(!RegroupDecision(bot),"visible non-shootable contact retains nearby fight grace");
        bot.Leader.Position=new Vector3(100,0,0);Time.time+=1;
        Check(RegroupDecision(bot),"extreme separation releases passive non-shootable sight grace");
        bot=RegroupBot("hiddenLaneRecentFight",25);regroup=SAINFollowerRuntime.GetRegroup(bot);
        bot.Sain.GoalEnemy.InLineOfSight=true;bot.Sain.GoalEnemy.TimeSinceSeen=2;
        Check(!RegroupDecision(bot),"real recent personal sight still protects a hidden lane");
        Check(regroup.AutoReason=="recentFight"&&regroup.AutoDistance==25&&regroup.AutoTrigger==18,"passive diagnostics report the actual grace rejection and evaluated distance");
        int probes=pitTeam.Utils.Utils.PathCalls;float checkedAt=regroup.AutoCheckedAt;
        for(int i=0;i<100;i++){var reason=regroup.AutoReason;var d=regroup.AutoDistance;}
        Check(pitTeam.Utils.Utils.PathCalls==probes&&regroup.AutoCheckedAt==checkedAt,"reading cached regroup diagnostics never reevaluates navigation or policy");
        Time.time+=3;bot.Sain.GoalEnemy.TimeSinceSeen=5;
        Check(RegroupDecision(bot),"expired personal sight permits regroup despite geometric lane");
        bot=RegroupBot("otherEnemyShot",100);
        var known=new Enemy{IsVisible=true,CanShoot=true};bot.Sain.EnemyController.KnownEnemies.Add(known);
        Check(!RegroupDecision(bot),"another living visible shootable contact protects useful fire");
        known.CanShoot=false;
        Check(RegroupDecision(bot),"another non-shootable contact cannot pin extreme passive regroup");
    }
    private static void TestRegroup(){
        TestPassiveRegroupContact();
        TestAutoRegroupPriority();
        var bot=RegroupBot("regroupAuto");var objective=SAINFollowerRuntime.GetRegroup(bot);
        Check(!objective.GetDecision(),"squad provider cannot start auto regroup before native solo evaluation");
        int published=bot.Sain.Decision.Manager.Publications;
        Check(RegroupDecision(bot)&&objective.Mode==SAINRegroupMode.Auto,"passive fallback selects automatic regroup after native solo evaluation");
        Check(bot.Sain.Decision.Manager.SoloEvaluations==1&&bot.Sain.Decision.Manager.Publications==published+1,"automatic regroup evaluates solo once and preserves native publication");
        Check(bot.Sain.Decision.Manager.EventSquad==ESquadDecision.Regroup&&bot.Sain.Decision.Manager.EventSolo==ECombatDecision.None,"native decision event receives the final regroup choice");
        Check(bot.Follower.Command==FollowerCommandType.None,"auto regroup does not create a player command");
        var action=new SAINFollowerSquadRegroupAction(bot);action.Start();action.Update(null);
        Check(bot.Sain.Mover.Runs==1&&bot.Sain.Mover.Destination.x==40,"cooled regroup runs toward the human player");
        Check(((pitAIBossPlayer)bot.BotFollower.BossToFollow).CombatEvents.Claims.ContainsKey(bot.ProfileId),"regroup reserves its destination");
        bot.Sain.Decision.CurrentSelfDecision=ESelfActionType.Surgery;
        Check(!objective.GetDecision()&&objective.Active,"medical interruption preserves auto objective");
        bot.Sain.Decision.CurrentSelfDecision=ESelfActionType.None;bot.Sain.Decision.CurrentCombatDecision=ECombatDecision.DogFight;
        Check(!objective.GetDecision()&&objective.Active,"dogfight interruption preserves regroup objective");
        bot.Sain.Decision.CurrentCombatDecision=ECombatDecision.ThrowGrenade;
        Check(!objective.GetDecision()&&objective.Active,"grenade throw interruption preserves regroup objective");
        bot.Sain.Decision.CurrentCombatDecision=ECombatDecision.MeleeAttack;
        Check(!objective.GetDecision()&&objective.Active,"melee interruption preserves regroup objective");
        bot.Sain.Decision.CurrentCombatDecision=ECombatDecision.AvoidGrenade;
        Check(!objective.GetDecision()&&objective.Active,"grenade avoidance preserves regroup objective");
        bot.Sain.Decision.CurrentCombatDecision=ECombatDecision.None;
        Check(objective.GetDecision(),"regroup resumes after native survival interruption");
        bot.Leader.Position=new Vector3(60,0,0);Time.time+=1;action.Update(null);
        Check(bot.Sain.Mover.Destination.x==60,"regroup refreshes when the player moves sector");
        var previousPath=bot.Sain.Mover.ActivePath;bot.Sain.Mover.ActivePath=new SAIN.Components.PathData();int stops=bot.Sain.Mover.Stops;action.Stop();
        Check(bot.Sain.Mover.Stops==stops,"regroup stop does not cancel a successor path");
        Check(!((pitAIBossPlayer)bot.BotFollower.BossToFollow).CombatEvents.Claims.ContainsKey(bot.ProfileId),"regroup stop releases its reservation");
        bot.GetPlayer.Position=new Vector3(45,0,0);Time.time+=1;objective.Observe();
        Check(objective.Settling,"auto arrival uses inner hysteresis distance and a short settle");
        Time.time+=2;objective.Observe();Check(!objective.Active,"settled arrival completes regroup");
        Check(!RegroupDecision(bot),"completed regroup does not immediately rearm");
        // Returning to productive search may cross the radius repeatedly, even after the
        // retry timer expires. Distance never interrupts that renewed combat choice.
        for(int i=0;i<4;i++){
            bot.GetPlayer.Position=new Vector3(i%2==0?30:45,0,0);Time.time+=3;
            bot.Sain.Decision.Manager.Frame(ECombatDecision.Search);
            Check(!objective.Active&&bot.Sain.Decision.CurrentCombatDecision==ECombatDecision.MoveToEngage,"search across regroup boundary remains combat, cycle "+i);
        }

        bot=RegroupBot("regroupGrace",25);objective=SAINFollowerRuntime.GetRegroup(bot);
        bot.Sain.GoalEnemy.IsVisible=true;
        Check(!RegroupDecision(bot),"auto regroup preserves a nearby live fire opportunity");
        bot.Sain.GoalEnemy.IsVisible=false;bot.Sain.GoalEnemy.TimeSinceSeen=3;
        Check(!RegroupDecision(bot),"auto regroup respects four-second seen grace");
        bot.Sain.GoalEnemy.TimeSinceSeen=20;bot.ShootData.LastTriggerPressd=Time.time-1;
        Check(!RegroupDecision(bot),"recent personal firing preserves auto fight grace after target changes");
        bot.ShootData.LastTriggerPressd=0;pitTeam.Utils.FollowerAwareness.Damaged=true;
        Check(!RegroupDecision(bot),"recent damage defers nearby automatic regroup");
        pitTeam.Utils.FollowerAwareness.Damaged=false;bot.Memory.IsUnderFire=true;
        Check(!RegroupDecision(bot),"nearby incoming fire defers auto regroup");
        bot.Leader.Position=new Vector3(40,0,0);Time.time+=1;
        Check(RegroupDecision(bot),"extreme distance overrides bounded automatic fight grace");
        objective.Clear("test");bot.Sain.Decision.CurrentCombatDecision=ECombatDecision.SeekCover;bot.Sain.Decision.CurrentSquadDecision=ESquadDecision.None;bot.Memory.IsUnderFire=false;bot.Follower.CombatIndependent=true;
        Check(!RegroupDecision(bot),"On Your Own disables automatic player regroup");
        bot.Follower.Command=FollowerCommandType.RegroupNearBoss;bot.Sain.GoalEnemy.IsVisible=true;
        Check(objective.GetDecision()&&objective.Mode==SAINRegroupMode.Command,"command regroup overrides fight grace and independent mode");
        Check(bot.Sain.Decision.Resets>0&&bot.Sain.Mover.Stops>0,"command regroup cancels the previous native movement latch through lifecycle APIs");
        Check(bot.Follower.Command==FollowerCommandType.None&&bot.Follower.CombatRegroupUsesBossAnchor,"order is consumed once into a player-anchored objective");
        int resets=bot.Sain.Decision.Resets;objective.Observe();
        Check(bot.Sain.Decision.Resets==resets,"active regroup does not repeatedly reset native decisions");
        Time.time+=25;Check(objective.GetDecision(),"consumed command survives its original command timeout");
        bot.Follower.Command=FollowerCommandType.RegroupNearBoss;bot.Follower.TightRegroupRequested=true;objective.Observe();
        Check(objective.Tight&&objective.Active,"renewed tight regroup replaces the previous regroup state");
        bot.GetPlayer.Position=new Vector3(35,0,0);Time.time+=1;objective.Observe();
        Check(objective.Active&&!objective.Settling,"tight order does not complete at five metres");
        bot.GetPlayer.Position=new Vector3(37,0,0);Time.time+=1;objective.Observe();
        Check(!objective.Active&&!bot.Follower.CombatRegroupUsesBossAnchor,"tight arrival completes inside four metres and releases anchor");

        bot=RegroupBot("regroupGeometry");objective=SAINFollowerRuntime.GetRegroup(bot);
        bot.Follower.Command=FollowerCommandType.RegroupNearBoss;objective.Observe();
        bot.GetPlayer.Position=new Vector3(39,4,0);Time.time+=1;objective.Observe();
        Check(objective.Active&&!objective.Settling,"another floor cannot complete regroup despite small direct distance");
        bot.GetPlayer.Position=new Vector3(30,0,0);pitTeam.Utils.Utils.PathScale=5;Time.time+=1;objective.Observe();
        Check(objective.Active&&!objective.Settling,"long complete path prevents through-wall arrival");
        pitTeam.Utils.Utils.PathComplete=false;Time.time+=1;objective.Observe();
        Check(objective.Active&&!objective.Settling,"missing path is not treated as arrival");
        Check(!objective.TryGetTarget(out _,out _),"missing complete target path never emits movement");
        pitTeam.Utils.Utils.PathComplete=true;pitTeam.Utils.Utils.PathScale=1;
        bot.Follower.Command=FollowerCommandType.PushEnemy;objective.Observe();
        Check(!objective.Active&&bot.Follower.Command==FollowerCommandType.PushEnemy,"replacement push cancels regroup without consuming the new order");
        bot.Follower.Command=FollowerCommandType.RegroupNearBoss;bot.UsingMedical=true;objective.Observe();
        Check(!objective.Active&&bot.Follower.Command==FollowerCommandType.RegroupNearBoss,"new regroup order waits while medicine is actually in use");
        bot.UsingMedical=false;bot.Sain.GoalEnemy.EnemyPlayer.HealthController.IsAlive=false;objective.Observe();
        Check(!objective.Active&&bot.Follower.Command==FollowerCommandType.RegroupNearBoss,"peace handoff leaves an unconsumed regroup command intact");

        bot=RegroupBot("regroupSightGait");objective=SAINFollowerRuntime.GetRegroup(bot);
        bot.Sain.GoalEnemy.Seen=true;bot.Sain.GoalEnemy.TimeSinceSeen=1f;
        bot.Follower.Command=FollowerCommandType.RegroupNearBoss;objective.Observe();
        action=new SAINFollowerSquadRegroupAction(bot);action.Start();action.Update(null);
        Check(bot.Sain.Mover.Runs==0 && bot.Sain.Mover.Paths==1,"recent personal sight starts commanded regroup walking");
        bot.Sain.GoalEnemy.TimeSinceSeen=3f;bot.Sain.GoalEnemy.InLineOfSight=true;bot.ShootData.LastTriggerPressd=Time.time;
        Time.time+=.6f;action.Update(null);
        Check(bot.Sain.Mover.Runs==1 && objective.Active,"same regroup action switches to running when personal sight expires despite native LOS and own shots");
        bot.Sain.GoalEnemy.IsVisible=true;Time.time+=.6f;int walking=bot.Sain.Mover.Paths;action.Update(null);
        Check(bot.Sain.Mover.Paths==walking+1 && bot.Sain.Mover.Runs==1,"renewed personal contact returns the running regroup to combat withdrawal");
        action.Stop();

        bot=RegroupBot("regroupCover");objective=SAINFollowerRuntime.GetRegroup(bot);bot.Sain.GoalEnemy.IsVisible=true;
        bot.Follower.Command=FollowerCommandType.RegroupNearBoss;objective.Observe();
        var good=new CoverPoint{Position=new Vector3(35,0,0)};
        bot.Sain.Cover.CoverPoints.Add(new CoverPoint{Position=new Vector3(2,0,0)});
        bot.Sain.Cover.CoverPoints.Add(new CoverPoint{Position=new Vector3(30,4,0)});
        bot.Sain.Cover.CoverPoints.Add(new CoverPoint{Position=new Vector3(31,0,0),Spotted=true});
        bot.Sain.Cover.CoverPoints.Add(good);
        Check(objective.TryGetTarget(out var target,out bool sprint)&&target.x==35&&!sprint,"hot regroup selects unspotted same-floor bossward SAIN cover and walks");
        Check(objective.TryGetTarget(out var same,out _)&&same.x==35,"regroup commits and reuses its selected cover");
        bot.GetPlayer.Position=new Vector3(24,0,0);Time.time+=1;objective.Observe();
        Check(objective.Active&&!objective.Settling&&objective.TryGetTarget(out target,out _)&&target.x==35,"entering the player radius does not abandon committed cover before arrival");
        bot.GetPlayer.Position=new Vector3(35,0,0);Time.time+=1;objective.Observe();
        Check(!objective.Active,"reaching committed cover completes hot regroup");
        bot.GetPlayer.Position=Vector3.zero;bot.Follower.Command=FollowerCommandType.RegroupNearBoss;objective.Observe();
        Check(objective.TryGetTarget(out target,out _)&&target.x==35,"new command acquires a fresh cover commitment");
        good.Spotted=true;Time.time+=1;
        Check(objective.TryGetTarget(out target,out _)&&target.x==40,"compromised cover falls back to a valid player destination");
        bot.Sain.GoalEnemy.IsVisible=false;bot.Sain.GoalEnemy.TimeSinceSeen=20;action=new SAINFollowerSquadRegroupAction(bot);action.Start();action.Update(null);
        int paths=bot.Sain.Mover.Paths;bot.Sain.Mover.Complete=false;Time.time+=1;action.Update(null);
        Check(bot.Sain.Mover.Paths>paths,"rejected sprint attempts the native walking fallback");
        Check(bot.Sain.Mover.ActivePath==null,"rejected movement cancels only the old regroup path");
        bot.Leader.HealthController.IsAlive=false;objective.Observe();
        Check(!objective.Active,"player death clears regroup without electing a substitute bot");
        bot=RegroupBot("regroupOptOut");objective=SAINFollowerRuntime.GetRegroup(bot);Check(RegroupDecision(bot),"opt-out fixture entered automatic regroup");
        bot.Follower.CombatTactic=FollowerCombatTactic.Balanced;Tick();
        Check(!objective.Active,"tactic opt-out releases regroup state");
        Check(SainRegroupBridge.GetCompleteDistance(false)==18&&SainRegroupBridge.GetCompleteDistance(true)==4,"addon consumes shared normal and tight arrival radii");
        CombatDistanceConfiguration.Instance.Factory=true;Check(SainRegroupBridge.GetCompleteDistance(false)==10,"Factory/Labs uses the shared compressed arrival radius");CombatDistanceConfiguration.Instance.Factory=false;
        Check(!SainRegroupBridge.TryGetDistance(Vector3.zero,new Vector3(float.NaN,0,0),out _),"nonfinite destination is rejected before calling Unity navigation");
        var claims=((pitAIBossPlayer)bot.BotFollower.BossToFollow).CombatEvents;
        claims.Claims["other"]=bot.Leader.Position;
        Check(!SainRegroupBridge.TrySpreadDestination(bot,bot.Leader.Position,false,out _),"unavailable spread fallback cannot pile onto another destination claim");
        CombatDistanceConfiguration.Instance.Urban=true;bot=RegroupBot("regroupUrban",20);objective=SAINFollowerRuntime.GetRegroup(bot);pitTeam.Utils.Utils.PathScale=5;
        Check(!RegroupDecision(bot),"same-floor urban detour does not trigger automatic regroup");
        bot.GetPlayer.Position=new Vector3(0,4,0);Time.time+=1;
        Check(RegroupDecision(bot),"other-floor return is not dismissed as an urban detour");
        CombatDistanceConfiguration.Instance.Urban=false;objective.Clear("test");
        pitTeam.Utils.Utils.PathScale=.1f;SainRegroupBridge.TryGetDistance(Vector3.zero,new Vector3(30,0,0),out var distance);
        Check(distance==30,"short navigation sample cannot undercut direct distance");pitTeam.Utils.Utils.PathScale=1;
    }
}

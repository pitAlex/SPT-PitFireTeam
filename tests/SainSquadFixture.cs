using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using pitTeam;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using SAIN.Components;
using SAIN.Models.Enums;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.Classes.Decision;
using UnityEngine;
namespace UnityEngine {
    public partial struct Vector3 {
        public static Vector3 zero=>new Vector3();
        public Vector3 normalized=>magnitude>0?new Vector3(x/magnitude,y/magnitude,z/magnitude):zero;
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
    }
    public class SupportWeaponData {public static SupportWeaponData Current=new SupportWeaponData();public Vector3 FirePort=new Vector3(0,1,0),PointDirection=new Vector3(1,0,0);}
    public class NavData {public bool IsOnNavMesh=true;public Vector3 Position;}
    public class Transform {public SupportWeaponData WeaponData=>SupportWeaponData.Current;public Vector3 Position; public Vector3 WeaponRoot=>Position;public NavData NavData=new NavData();}
}
namespace UnityEngine.AI {
    public enum NavMeshPathStatus {PathComplete,PathPartial,PathInvalid}
    public class NavMeshPath {
        public NavMeshPathStatus status; public Vector3[] Points=new Vector3[0]; public Vector3[] corners=>Points;
        public int GetCornersNonAlloc(Vector3[] buffer){int n=Math.Min(buffer.Length,Points.Length);Array.Copy(Points,buffer,n);return n;}
    }
    public struct NavMeshHit {public Vector3 position;}
    public static class NavMesh {
        public const int AllAreas=-1;
        public static Func<Vector3,bool> SampleAllowed;
        public static Vector3[] Route;public static bool RouteComplete=true;public static int Calculations;
        public static bool CalculatePath(Vector3 from,Vector3 to,int mask,NavMeshPath path){Calculations++;path.Points=Route??new[]{from,to};path.status=RouteComplete?NavMeshPathStatus.PathComplete:NavMeshPathStatus.PathPartial;return true;}
        public static bool SamplePosition(Vector3 pos,out NavMeshHit hit,float radius,int mask){hit=new NavMeshHit{position=pos};return SampleAllowed?.Invoke(pos)!=false;}
        public static bool Raycast(Vector3 from,Vector3 to,out NavMeshHit hit,int mask){hit=new NavMeshHit{position=to};return false;}
    }
}
namespace EFT {
    public enum ETagStatus {Healthy,Injured,Dying,BadlyInjured}
    public enum WildSpawnType {bossKnight,pmc,assault,marksman}
    public class HealthController {public bool IsAlive=true;}
    public partial class Player {
        public HealthController HealthController=new HealthController();public string ProfileId="enemy";
        public ETagStatus HealthStatus=ETagStatus.Healthy;public bool IsInPronePose;
    }
    public partial class BotOwner {public Vector3 Position=>GetPlayer.Position;public bool CanSprintPlayer=true;}
}
namespace SAIN.Models.Enums {public enum EEnemyAction{None,UsingSurgery,Reload} public enum ESprintUrgency{Low,Middle,High}}

namespace SAIN.SAINComponent {
    public abstract class BotBase {
        public BotComponent Bot{get;}public BotOwner BotOwner=>Bot.BotOwner;
        protected BotBase(BotComponent bot){Bot=bot;}
    }
}
namespace SAIN.SAINComponent.Classes.EnemyClasses {
    public class Path {public float PathLength=20;public UnityEngine.AI.NavMeshPathStatus PathToEnemyStatus=UnityEngine.AI.NavMeshPathStatus.PathComplete;}
    public class Status {public EEnemyAction VulnerableAction;public bool EnemyIsSuppressed;}
    public class EnemyPlace {public SAINSoundType SoundType;public Vector3 Position;}
    public partial class Places {public float BotDistanceFromLastKnown=100;public Vector3? LastKnownPosition=new Vector3(50,0,0);public float TimeSinceLastKnownUpdated=20;public EnemyPlace LastKnownPlace,LastHeardPlace;}
    public class EnemyHearing {public bool EnemyHeardFromPeace;}
    public class Enemy {
        public string EnemyProfileId=>EnemyPlayer.ProfileId;public Vector3? LastKnownPosition=>KnownPlaces.LastKnownPosition;
        public bool IsZombie;public bool CanShoot,IsVisible,Seen=true,Heard,InLineOfSight,Active=true,Valid=true;
        public bool WasValid=>Valid;public bool EnemyKnown=true;
        public float TimeSinceLastKnownUpdated=>KnownPlaces.TimeSinceLastKnownUpdated;
        public string EPathDistance="Far";
        public float TimeSinceSeen=20;
        private EnemyInfo enemyInfo;public EnemyInfo EnemyInfo => enemyInfo ?? (enemyInfo=new EnemyInfo { Person=EnemyPlayer,ProfileId=EnemyProfileId });
        public Player EnemyPlayer=new Player();public Path Path=new Path();public Status Status=new Status();public EnemyHearing Hearing=new EnemyHearing();
        public Places KnownPlaces=new Places();public Vector3? SuppressionTarget=new Vector3(50,1,0);public Vector3 EnemyPosition;
        public static bool IsEnemyActive(Enemy enemy)=>enemy?.Active==true;
        public bool CheckValid()=>Valid;
    }
}
namespace SAIN.SAINComponent.Classes.Info {
    public class SquadInfo {
        public BotComponent LeaderComponent,Suppressor;
        public bool SquadIsSuppressEnemy(string id,out BotComponent member){member=Suppressor;return member!=null;}
    }
    public class BotSquadContainer {
        public bool BotInGroup=true,IAmLeader;
        public SquadInfo SquadInfo=new SquadInfo();
        public BotComponent LeaderComponent=>SquadInfo.LeaderComponent;
        public Dictionary<string,BotComponent> Members=new Dictionary<string,BotComponent>();
    }
    public class Profile {public string NickName="Searcher";public bool IsBoss;public WildSpawnType WildSpawnType=WildSpawnType.pmc;}
    public partial class SAINBotInfoClass {public SAIN.Preset.Shared.Personalities.BasePersonality.Categories.PersonalityBehaviorSettings PersonalitySettings=>PersonalitySettingsClass.Behavior;public Profile Profile=new Profile();}
}
namespace SAIN.SAINComponent.Classes.Decision {
    public class SAINDecisionClass : SAIN.Components.Decision {}
    public class SquadDecisionClass : SAIN.SAINComponent.BotBase {
        public int NativeCalls;public SquadDecisionClass(BotComponent bot):base(bot){}
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool GetDecision(out ESquadDecision decision,Enemy enemy){NativeCalls++;decision=ESquadDecision.Search;return true;}
    }
}
namespace SAIN.Layers.Combat.Squad {public class SuppressAction:SAIN.Layers.BotAction{public SuppressAction(BotOwner o):base(o,"") {}}}
namespace SAIN.Components {
    public class SelfActions {public float AmmoRatio=1;public bool LowOnAmmo(float ratio)=>AmmoRatio<ratio;}
    public partial class Decision {public ESquadDecision CurrentSquadDecision;public SelfActions SelfActionDecisions=new SelfActions();}
    public partial class BotHealth {public ETagStatus HealthStatus=ETagStatus.Healthy;}
    public class BotMemory {public BotHealth Health=new BotHealth();}
    public class Gear {public bool HasEarPiece=true;}
    public class Equipment {public Gear GearInfo=new Gear();}
    public class PlayerComponent {public Equipment Equipment=new Equipment();}
    public class Shooter : SAIN.SAINComponent.Classes.SAINShootData {}
    public partial class Suppression {public bool IsHeavySuppressed;public bool TrySuppressAnyEnemy(Enemy enemy,object known){Calls++;return false;}}
    public partial class EnemyController {public List<Enemy> KnownEnemies=new List<Enemy>();}
    public class Steering {
        public bool SteerByPriority(Enemy enemy=null,bool allow=true)=>false;
        public bool LookToMovingDirection()=>true;public int FallbackLooks;public void LookToLastKnownEnemyPosition(Enemy enemy){FallbackLooks++;} public int Looks; public Vector3 LookPoint; public void LookToPoint(Vector3 point){Looks++;LookPoint=point;}
        public float AimAngle;public float AngleToPointFromLookDir(Vector3 point)=>AimAngle;
    }
    public class Search {public Enemy Enemy;public bool Enabled;public void ToggleSearch(bool value,Enemy enemy){Enabled=value;Enemy=enemy;}}
    public partial class Mover {
        public int Runs;public bool Running;
        public bool RunToPoint(Vector3 target,bool complete=true,float distance=-1,ESprintUrgency urgency=ESprintUrgency.Middle,bool check=true){Runs++;Destination=target;if(Complete){ActivePath=new PathData{Destination=target};Moving=true;}return Complete;}
        public void SetTargetPose(float pose){} public void SetTargetMoveSpeed(float speed){}
    }
    public partial class BotComponent {
        public bool BotActive=>BotOwner.Active&&!IsDead;public string ProfileId=>BotOwner.ProfileId;public Vector3 Position=>BotOwner.Position;
        public Transform Transform=>new Transform{Position=Position};
        public Enemy GoalEnemy;public bool HasEnemy=>GoalEnemy!=null;
        public SAIN.SAINComponent.Classes.Info.BotSquadContainer Squad=new SAIN.SAINComponent.Classes.Info.BotSquadContainer();
        public BotMemory Memory=new BotMemory();public PlayerComponent PlayerComponent=new PlayerComponent();
        public Shooter Shoot=new Shooter();public Suppression Suppression=new Suppression();public EnemyController EnemyController=new EnemyController();
        public Steering Steering=new Steering();public Search Search=new Search();
    }
}
public static partial class CombatChecks {
    private static void TestSquad(BotOwner selected,BotOwner rifle,SAINFollowerSoloCombatLayer solo,SAINFollowerSquadCombatLayer layer){
        Check(SAINFollowerSquadCombatLayer.LayerPriority>SAINFollowerSoloCombatLayer.LayerPriority,"squad replica has priority over solo");
        var native=new SAIN.SAINComponent.Classes.Decision.SquadDecisionClass(selected.Sain);
        var enemy=new Enemy();selected.Sain.GoalEnemy=enemy;
        bool squad=native.GetDecision(out var decision,enemy);
        Check(!squad&&decision==ESquadDecision.None&&native.NativeCalls==0,"handled None bypasses native AI-leader provider");
        var mate=Spawn("mate");new SAINFollowerSoloCombatLayer(mate,74);new SAINFollowerSquadCombatLayer(mate,75);Tick();
        mate.Sain.GoalEnemy=enemy;mate.Sain.Decision.CurrentCombatDecision=ECombatDecision.Search;
        selected.Sain.Squad.Members[mate.ProfileId]=mate.Sain;
        Check(native.GetDecision(out decision,enemy)&&decision==ESquadDecision.GroupSearch&&native.NativeCalls==0,"player-led squad preserves native group search branch");
        selected.Sain.Decision.CurrentSquadDecision=decision;selected.Sain.Decision.CurrentCombatDecision=ECombatDecision.None;
        Check(layer.IsActive()&&!solo.IsActive(),"published squad decision activates squad and releases solo");
        Check(layer.GetNextAction().Type==typeof(SAINFollowerFollowSearchPartyAction)&&layer.GetNextAction().Reason.Contains("Follow Searcher Searcher"),"group search names the retained searcher instead of the player leader");
        selected.Leader.Position=new Vector3(20,0,0);mate.GetPlayer.Position=new Vector3(30,0,0);
        selected.Sain.Squad.SquadInfo.LeaderComponent=rifle.Sain;rifle.GetPlayer.Position=new Vector3(-100,0,0);
        var search=new SAINFollowerFollowSearchPartyAction(selected);search.Start();search.Update(null);
        Check(selected.Sain.Mover.Destination.x==28,"search destination follows initiator rather than player or arbitrary AI squad leader");
        search.Stop();Check(!selected.Sain.Search.Enabled,"search action releases native search state");
        selected.Leader.Position=new Vector3(40,0,0);
        selected.Leader.Position=Vector3.zero; // Regroup behavior has its own extension checks below.
        selected.Leader.HealthController.IsAlive=false;
        Check(!native.GetDecision(out decision,enemy)&&decision==ESquadDecision.None&&native.NativeCalls==0,"dead player is not replaced by another squad member");
        selected.Leader.HealthController.IsAlive=true;
        enemy.IsVisible=true;
        Check(!native.GetDecision(out decision,enemy)&&decision==ESquadDecision.None,"native visible enemy rule leaves combat to solo");
        enemy.IsVisible=false;enemy.TimeSinceSeen=5;
        Check(!native.GetDecision(out decision,enemy),"native recent-contact threshold preserved");
        enemy.TimeSinceSeen=20;mate.Sain.Decision.CurrentCombatDecision=ECombatDecision.Retreat;
        selected.Sain.Decision.CurrentSquadDecision=ESquadDecision.None;
        Check(native.GetDecision(out decision,enemy)&&decision==ESquadDecision.Suppress,"native retreating-teammate suppression preserved");
        selected.Sain.Decision.SelfActionDecisions.AmmoRatio=.2f;
        Check(!native.GetDecision(out decision,enemy),"native suppression start ammo threshold preserved");
        selected.Sain.Decision.CurrentSquadDecision=ESquadDecision.Suppress;
        Check(native.GetDecision(out decision,enemy)&&decision==ESquadDecision.Suppress,"native suppression continuation ammo threshold preserved");
        selected.Sain.Decision.SelfActionDecisions.AmmoRatio=1;
        selected.Sain.Squad.SquadInfo.Suppressor=mate.Sain;enemy.Status.VulnerableAction=EEnemyAction.Reload;
        Check(native.GetDecision(out decision,enemy)&&decision==ESquadDecision.PushSuppressedEnemy,"native vulnerable suppressed-enemy push preserved");
        selected.Sain.Squad.SquadInfo.Suppressor=null;enemy.Status.VulnerableAction=EEnemyAction.None;
        var route=new Dictionary<ESquadDecision,string>{{ESquadDecision.Regroup,"SAINFollowerSquadRegroupAction"},{ESquadDecision.Suppress,"SuppressAction"},{ESquadDecision.Search,"SearchAction"},{ESquadDecision.GroupSearch,"SAINFollowerFollowSearchPartyAction"},{ESquadDecision.Help,"SearchAction"},{ESquadDecision.PushSuppressedEnemy,"RushEnemyAction"}};
        foreach(var pair in route){selected.Sain.Decision.CurrentSquadDecision=pair.Key;Check(layer.GetNextAction().Type.Name==pair.Value,"squad action map "+pair.Key);}
        selected.Sain.Decision.CurrentSelfDecision=ESelfActionType.Surgery;Check(!layer.IsActive(),"native self-care preempts squad");
        selected.Sain.Decision.CurrentSelfDecision=ESelfActionType.None;selected.Sain.Decision.CurrentCombatDecision=ECombatDecision.DogFight;
        Check(!layer.IsActive(),"native dogfight preempts squad");selected.Sain.Decision.CurrentCombatDecision=ECombatDecision.None;
        var ordinary=new SAIN.SAINComponent.Classes.Decision.SquadDecisionClass(rifle.Sain);
        Check(ordinary.GetDecision(out decision,enemy)&&ordinary.NativeCalls==1,"non-SainMan uses native provider");
        selected.Sain.Decision.CurrentSquadDecision=ESquadDecision.None;
        Check(layer.IsCurrentActionEnding(),"squad decision change ends its action");
        selected.Sain.Squad.Members.Clear();selected.Sain.GoalEnemy=null;
        TestSearchParty();
    }
    private static void AssignSearcher(BotOwner receiver, BotOwner searcher) {
        searcher.Sain.GoalEnemy=receiver.Sain.GoalEnemy;
        searcher.Sain.Decision.CurrentCombatDecision=ECombatDecision.Search;
        searcher.Sain.Decision.CurrentSquadDecision=ESquadDecision.None;
        receiver.Sain.Squad.Members[searcher.ProfileId]=searcher.Sain;
        var native=new SAIN.SAINComponent.Classes.Decision.SquadDecisionClass(receiver.Sain);
        Check(native.GetDecision(out var result,receiver.Sain.GoalEnemy)&&result==ESquadDecision.GroupSearch,"searcher admitted for shared native contact");
        receiver.Sain.Decision.CurrentSquadDecision=result;
        receiver.Sain.Decision.CurrentCombatDecision=ECombatDecision.None;
    }
    private static void TestSearchParty() {
        var b=RegroupBot("searchHelper",0);var lead=RegroupBot("searchInitiator",0);
        b.GetPlayer.Position=new Vector3(-10,0,0);lead.GetPlayer.Position=Vector3.zero;
        AssignSearcher(b,lead);
        Check(SAINFollowerRuntime.GetSearchLeader(b)==lead.Sain,"search initiator retained separately from player leadership");
        var native=new SAIN.SAINComponent.Classes.Decision.SquadDecisionClass(b.Sain);
        var other=RegroupBot("otherSearcher",0);other.Sain.GoalEnemy=b.Sain.GoalEnemy;other.Sain.Decision.CurrentCombatDecision=ECombatDecision.Search;
        b.Sain.Squad.Members.Clear();b.Sain.Squad.Members[other.ProfileId]=other.Sain;b.Sain.Squad.Members[lead.ProfileId]=lead.Sain;
        native.GetDecision(out _,b.Sain.GoalEnemy);
        Check(SAINFollowerRuntime.GetSearchLeader(b)==lead.Sain,"valid initiator does not churn with squad enumeration order");
        var action=new SAINFollowerFollowSearchPartyAction(b);action.Start();action.Update(null);
        Check(b.Sain.Mover.Destination.x==-2,"world-origin searcher receives an initial route");
        lead.Sain.Decision.CurrentCombatDecision=ECombatDecision.StandAndShoot;action.Update(null);
        Check(!b.Sain.Mover.Moving&&SAINFollowerRuntime.GetSearchLeader(b)==null,"ending search releases only the helper's movement before next publication");
        native.GetDecision(out _,b.Sain.GoalEnemy);
        Check(SAINFollowerRuntime.GetSearchLeader(b)==other.Sain,"remaining real searcher replaces ended initiator");
        action.Update(null);
        Check(b.Sain.Mover.Moving,"replacement searcher starts a fresh route even at the old leader position");
        b.Sain.Mover.WalkToPoint(new Vector3(80,0,0),true);
        var newerPath=b.Sain.Mover.ActivePath;action.Stop();
        Check(b.Sain.Mover.Moving&&ReferenceEquals(newerPath,b.Sain.Mover.ActivePath),"search cleanup preserves another action's newer path");
        b.Sain.Mover.Stop();
        other.IsDead=true;
        Check(SAINFollowerRuntime.GetSearchLeader(b)==null,"dead initiator invalidates assignment");other.IsDead=false;
        other.Sain.Decision.CurrentSquadDecision=ESquadDecision.GroupSearch;
        Check(!native.GetDecision(out _,b.Sain.GoalEnemy),"group helpers cannot become leaders and form follow loops");
        other.Sain.Decision.CurrentSquadDecision=ESquadDecision.None;
        b.Sain.Decision.CurrentSquadDecision=ESquadDecision.None;b.Sain.Decision.CurrentCombatDecision=ECombatDecision.Search;
        Check(!native.GetDecision(out _,b.Sain.GoalEnemy),"existing initiating Search is never converted to following a peer");
        b.Sain.Decision.CurrentCombatDecision=ECombatDecision.SeekCover;AssignSearcher(b,other);
        other.Sain.GoalEnemy=new Enemy{EnemyPlayer=new Player{ProfileId="different"}};
        Check(SAINFollowerRuntime.GetSearchLeader(b)==null,"changed enemy cannot retain a search assignment");
        AssignSearcher(b,other);b.Sain.Squad.Members.Remove(other.ProfileId);
        Check(SAINFollowerRuntime.GetSearchLeader(b)==null,"dismissed squad member cannot remain search leader");
        AssignSearcher(b,other);other.Follower.CombatTactic=pitTeam.Components.FollowerCombatTactic.Balanced;
        Check(SAINFollowerRuntime.GetSearchLeader(b)==null,"tactic fallback invalidates native search leadership");
        other.Follower.CombatTactic=pitTeam.Components.FollowerCombatTactic.SainMan;
        AssignSearcher(b,other);other.Sain.Decision.CurrentSelfDecision=ESelfActionType.FirstAid;
        Check(SAINFollowerRuntime.GetSearchLeader(b)==null,"medical interruption ends search-party leadership");
        other.Sain.Decision.CurrentSelfDecision=ESelfActionType.None;
        AssignSearcher(b,other);b.Leader.Position=new Vector3(60,0,0);b.Follower.Command=pitTeam.Components.FollowerCommandType.RegroupNearBoss;
        native.GetDecision(out var regroup,b.Sain.GoalEnemy);
        Check(regroup==ESquadDecision.Regroup&&SAINFollowerRuntime.GetSearchLeader(b)==null,"command regroup overrides search assignment");
        action.Stop();
        b.Follower.Command=pitTeam.Components.FollowerCommandType.None;
        SAINFollowerRuntime.GetRegroup(b).Clear("test");
        b.Sain.Decision.CurrentSquadDecision=ESquadDecision.None;b.Sain.Decision.CurrentCombatDecision=ECombatDecision.SeekCover;
        b.Follower.CombatIndependent=true;AssignSearcher(b,other);
        Check(SAINFollowerRuntime.GetSearchLeader(b)==other.Sain,"On Your Own retains teammate search cooperation");
        other.Sain.GoalEnemy.EnemyKnown=false;
        Check(SAINFollowerRuntime.GetSearchLeader(b)==null,"forgotten native contact ends search cooperation");
        TestSearchShooterSupport();
    }
    private static void TestSearchShooterSupport() {
        var b=SupportBot("searchShooter");b.Follower.CombatTactic=pitTeam.Components.FollowerCombatTactic.SAINShooter;Tick();
        var lead=RegroupBot("shooterSearchLead",0);lead.GetPlayer.Position=new Vector3(10,0,0);
        AssignSearcher(b,lead);
        var support=SAINFollowerRuntime.GetSquadSupport(b);
        support.Filter(b.Sain.GoalEnemy,ECombatDecision.None,ESquadDecision.GroupSearch,ESelfActionType.None,out _);
        Time.time+=1.1f;
        Check(support.Filter(b.Sain.GoalEnemy,ECombatDecision.None,ESquadDecision.GroupSearch,ESelfActionType.None,out var decision)&&
            decision==ESquadDecision.Help&&support.Destination.HasValue&&(support.Destination.Value-new Vector3(-10,0,0)).sqrMagnitude<.01f,"Shooter prepares the existing backline firing support for a nearby searcher");
        int scans=FiringPositionFinder.Calls;
        for(int i=0;i<20;i++)support.Filter(b.Sain.GoalEnemy,ECombatDecision.None,ESquadDecision.GroupSearch,ESelfActionType.None,out _);
        Check(FiringPositionFinder.Calls==scans,"committed search support does not repeat native geometry planning");
        lead.Sain.Decision.CurrentCombatDecision=ECombatDecision.StandAndShoot;support.Observe();
        Check(!support.Active,"search ending releases Shooter's temporary support intent");
    }

}
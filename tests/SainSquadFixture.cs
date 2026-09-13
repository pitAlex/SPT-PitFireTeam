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
using UnityEngine;
namespace UnityEngine {
    public partial struct Vector3 {
        public static Vector3 zero=>new Vector3();
        public Vector3 normalized=>magnitude>0?new Vector3(x/magnitude,y/magnitude,z/magnitude):zero;
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
    }
    public class Transform {public Vector3 Position;}
}
namespace UnityEngine.AI {
    public struct NavMeshHit {public Vector3 position;}
    public static class NavMesh {
        public static bool SamplePosition(Vector3 pos,out NavMeshHit hit,float radius,int mask){hit=new NavMeshHit{position=pos};return true;}
        public static bool Raycast(Vector3 from,Vector3 to,out NavMeshHit hit,int mask){hit=new NavMeshHit{position=to};return false;}
    }
}
namespace EFT {
    public enum ETagStatus {Healthy,Injured,Dying,BadlyInjured}
    public enum WildSpawnType {bossKnight,pmc}
    public class HealthController {public bool IsAlive=true;}
    public partial class Player {
        public HealthController HealthController=new HealthController();public string ProfileId="enemy";
        public ETagStatus HealthStatus=ETagStatus.Healthy;public bool IsInPronePose;
    }
    public partial class BotOwner {public Vector3 Position=>GetPlayer.Position;public bool CanSprintPlayer=true;}
}
namespace SAIN.Models.Enums {public enum EEnemyAction{None,UsingSurgery,Reload} public enum ESprintUrgency{Middle}}
namespace SAIN.Preset.Shared.Enums {public enum ESquadDecision{None,Surround,Retreat,Suppress,PushSuppressedEnemy,BoundingRetreat,Regroup,SpreadOut,HoldPositions,Help,Search,GroupSearch}}
namespace SAIN.SAINComponent {
    public abstract class BotBase {
        public BotComponent Bot{get;}public BotOwner BotOwner=>Bot.BotOwner;
        protected BotBase(BotComponent bot){Bot=bot;}
    }
}
namespace SAIN.SAINComponent.Classes.EnemyClasses {
    public class Path {public float PathLength=20;}
    public class Status {public EEnemyAction VulnerableAction;}
    public class Places {public float BotDistanceFromLastKnown=100;}
    public class Enemy {
        public bool IsVisible,Seen=true,InLineOfSight,Active=true,Valid=true;
        public float TimeSinceSeen=20;
        public Player EnemyPlayer=new Player();public Path Path=new Path();public Status Status=new Status();
        public Places KnownPlaces=new Places();public object SuppressionTarget=new object();public Vector3 EnemyPosition;
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
    public class Rush {public bool CanRushEnemyReloadHeal=true;}
    public class PersonalitySettings {public Rush Rush=new Rush();}
    public class Profile {public bool IsBoss;public WildSpawnType WildSpawnType=WildSpawnType.pmc;}
    public partial class SAINBotInfoClass {public PersonalitySettings PersonalitySettings=new PersonalitySettings();public Profile Profile=new Profile();}
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
    public class BotHealth {public ETagStatus HealthStatus=ETagStatus.Healthy;}
    public class BotMemory {public BotHealth Health=new BotHealth();}
    public class Gear {public bool HasEarPiece=true;}
    public class Equipment {public Gear GearInfo=new Gear();}
    public class PlayerComponent {public Equipment Equipment=new Equipment();}
    public class Shooter {public bool ShootAnyVisibleEnemies(Enemy enemy)=>false;}
    public class Suppression {public bool TrySuppressAnyEnemy(Enemy enemy,object known)=>false;}
    public class EnemyController {public object KnownEnemies=new object();}
    public class Steering {
        public bool SteerByPriority(Enemy enemy,bool allow=true)=>false;
        public void LookToMovingDirection(){}
    }
    public class Search {public Enemy Enemy;public bool Enabled;public void ToggleSearch(bool value,Enemy enemy){Enabled=value;Enemy=enemy;}}
    public partial class Mover {
        public int Runs;public bool Running;
        public bool RunToPoint(Vector3 target,bool complete=true,int distance=-1,ESprintUrgency urgency=ESprintUrgency.Middle,bool check=true){Runs++;Destination=target;return Complete;}
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
        Check(layer.GetNextAction().Type==typeof(SAINFollowerFollowSearchPartyAction),"group search follows human leader");
        selected.Leader.Position=new Vector3(20,0,0);
        selected.Sain.Squad.SquadInfo.LeaderComponent=rifle.Sain;rifle.GetPlayer.Position=new Vector3(-100,0,0);
        var search=new SAINFollowerFollowSearchPartyAction(selected);search.Start();search.Update(null);
        Check(selected.Sain.Mover.Destination.x==18,"search destination uses player rather than arbitrary AI member");
        search.Stop();Check(!selected.Sain.Search.Enabled,"search action releases native search state");
        selected.Leader.Position=new Vector3(40,0,0);
        var regroup=new SAINFollowerSquadRegroupAction(selected);regroup.Update(null);
        Check(selected.Sain.Mover.Destination.x==40&&selected.Sain.Mover.Runs==1,"native regroup sprint policy uses real player position");
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
    }
}
namespace DrakiaXYZ.BigBrain.Brains { public static class BrainManager { public static CustomLayer? ActiveLayer; public static object? GetActiveLayer(BotOwner bot)=>ActiveLayer; } }
namespace EFT {
    public enum BotLogicDecision { __DECISIONS__ }
    public class WeaponGrenades { public bool ThrowindNow; }
    public class WeaponManager { public WeaponGrenades Grenades=new(); }
    public class DangerSensor { public bool IsActive;public bool ShallRunAway()=>IsActive; }
    public class TurnAwaySensor { public bool IsActive; }
    public class FlashSensor { public bool IsFlashed; }
    public class SmokeSensor { public bool IsInSmoke; }
    public class MineSensor { public bool Enabled;public bool CanDeactivate()=>Enabled; }
    public class EnemyInfo { public bool CanShoot,IsVisible; public float PersonalLastSeenTime;public string ProfileId="";public Player Person=new(); }
    public class BotMemory { public bool HaveEnemy,IsInCover,IsUnderFire; public float LastTimeHit;public EnemyInfo GoalEnemy=new();public CustomNavigationPoint? CurCustomCoverPoint; }
}
public class CoreActionResultParams { }
public struct AICoreActionResult<T,W> where W:CoreActionResultParams {
    public T Action;public string Reason;public W? Data;
    public AICoreActionResult(T action,string reason,W? data){Action=action;Reason=reason;Data=data;}
}
public struct AICoreActionEnd {
    public string Reason;public bool Value;
    public AICoreActionEnd(string reason,bool val=true){Reason=reason;Value=val;}
}
public class BaseLogicLayerSimple {
    public BotOwner _owner=null!;
    public AICoreActionEnd FinishNodeLogic=new("Base logic",true),ContinueNodeLogic=new("Base logic",false);
    public virtual bool ShallUseNow()=>false;
    public virtual AICoreActionResult<BotLogicDecision,CoreActionResultParams> GetDecision()=>default;
    public virtual AICoreActionEnd EndHoldPosition()=>FinishNodeLogic;
    public virtual AICoreActionEnd EndRunAwayGrenade()=>FinishNodeLogic;
    public static BotLogicDecision HoldOrCover(BotOwner owner)=>owner.Memory.IsInCover?BotLogicDecision.holdPosition:BotLogicDecision.goToCoverPoint;
    public static BotLogicDecision HoldOrCoverRun(BotOwner owner)=>owner.Memory.IsInCover?BotLogicDecision.holdPosition:BotLogicDecision.runToCover;
}
public class AvoidDangerLayer : BaseLogicLayerSimple {
    public float _lastUseAvoidLayer;
    public bool GoodGrenadeCover;
    public AvoidDangerLayer(BotOwner bot){_owner=bot;}
    public bool CanFightUnderArtillery()=>false;
    public bool WantKillEnemyNow()=>GoodGrenadeCover;
    public bool IsInGoodCoverForArtillery()=>false;
    public bool PeaceSmokeGrenadeNear()=>!_owner.Memory.HaveEnemy&&_owner.SmokeGrenade.IsInSmoke;
__NATIVE_AVOIDANCE__
}
public class CustomNavigationPoint {
    public bool IsSpotted,Free=true,Shielded=true;
    public bool IsFreeById(int id)=>Free;
    public bool IsGoodForGrenade(GrenadeDangerPoint danger,BotOwner bot)=>Shielded;
}
namespace EFT {
    public class MedicalItem { public bool Using,Pending;public int Canceled;public bool ShallStartUse()=>Pending;public void CancelCurrent(){Using=false;Canceled++;} }
    public class Medical { public MedicalItem FirstAid=new(),SurgicalKit=new(),Stimulators=new();public bool TopOff; }
    public class BotsGroup { public enum BotCurrentTactic { Attack } }
    public class Tactic { public void SetTactic(BotsGroup.BotCurrentTactic tactic){} }
    public partial class BotOwner {
        public int Id,Stops,MoveCalls;public Medical Medecine=new();public Tactic Tactic=new();
        public void StopMove(){Stops++;}
        public UnityEngine.AI.NavMeshPathStatus GoToPoint(Vector3 p,bool a,float b,bool c,bool d){MoveCalls++;return UnityEngine.AI.NavMeshPathStatus.PathComplete;}
    }
}
namespace UnityEngine {
    public static class Mathf { public static float Sqrt(float n)=>(float)Math.Sqrt(n);public static float Clamp01(float n)=>Math.Max(0,Math.Min(1,n)); }
    public struct Vector2 {
        public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;}public float sqrMagnitude=>x*x+y*y;
        public static float Dot(Vector2 a,Vector2 b)=>a.x*b.x+a.y*b.y;
        public static Vector2 operator -(Vector2 a,Vector2 b)=>new(a.x-b.x,a.y-b.y);
        public static Vector2 operator +(Vector2 a,Vector2 b)=>new(a.x+b.x,a.y+b.y);
        public static Vector2 operator *(Vector2 a,float n)=>new(a.x*n,a.y*n);
    }
}
namespace UnityEngine.AI {
    public enum NavMeshPathStatus { PathComplete,PathPartial,PathInvalid }
    public class NavMeshPath { public NavMeshPathStatus status;public Vector3[] corners=new Vector3[0]; }
    public static class NavMesh {
        public static Vector3[]? Corners;public static bool Complete=true;
        public static bool CalculatePath(Vector3 from,Vector3 to,int mask,NavMeshPath path){path.status=Complete?NavMeshPathStatus.PathComplete:NavMeshPathStatus.PathPartial;path.corners=Corners??new[]{from,to};return true;}
    }
}
namespace pitTeam.Utils {
    public static class FollowerMedical {
__MEDICAL_HELPERS__
        public static void RefreshMedicalWork(BotOwner bot){}
        public static bool CanStartFirstAidTopOff(BotOwner bot)=>bot.Medecine.TopOff;
    }
    public static class Covers {
__COVER_HELPERS__
    }
}
namespace pitTeam.BigBrain {
    public static class FollowerCombatCommon {
__COMBAT_HELPERS__
    }
    public static class FollowerCombatRegroupObjective { public static bool IsRunReason(string reason)=>false; }
    public enum CustomBotDecisions { attackRetreat=10000 }
    public class FollowerCombatLayer : CustomLayer {
        internal const string LingerReason="linger";
        private static readonly HashSet<BotLogicDecision> LoggedUnsupportedDecisions=new();
        public FollowerCombatLayer(BotOwner b):base(b,72){}
        public override string GetName()=>"";public override bool IsActive()=>false;public override Action GetNextAction()=>null!;public override bool IsCurrentActionEnding()=>true;
__ACTION_FACTORY__
    }
}
namespace pitTeam.BigBrain.Actions {
__ACTION_DATA__
__ACTION_STUBS__
    internal class CombatDogFightAction {
        private BotOwner BotOwner;private Func<Vector3,bool>? movementAllowed;
        public CombatDogFightAction(BotOwner bot){BotOwner=bot;}
        public NavMeshPathStatus Move(FollowerCombatActionData data,Vector3 point){movementAllowed=(ResolveCurrentActionData(data) as FollowerCombatActionData)?.MovementAllowed;return TryGoToDogFightPoint(point);}
__DOGFIGHT_MOVEMENT__
__DOGFIGHT_DATA__
    }
}
public static class TripwireAvoidanceChecks {
    private static int checks;
    private static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
    private static (BotOwner bot,BaseTripwire wire,pitTeam.BigBrain.FollowerTripwireLayer layer,AvoidDangerLayer native) Fresh(){
        BossPlayers.Instance=new();BossPlayers.Followers.Clear();Logger.Errors.Clear();Physics.Blocked=false;Time.time=100;
        UnityEngine.AI.NavMesh.Corners=null;UnityEngine.AI.NavMesh.Complete=true;
        var bot=new BotOwner();bot.Transform.position=new(2,0,0);BossPlayers.Followers.Add(new(){Bot=bot});
        var wire=new BaseTripwire();wire.CollisionEnter(new(){Player=bot.GetPlayer});Time.time+=.1f;
        return(bot,wire,new(bot,pitTeam.BigBrain.FollowerTripwireLayer.LayerPriority),new(bot));
    }
    private static void Cover(BotOwner bot){bot.Memory.IsInCover=true;bot.Memory.CurCustomCoverPoint=new();}
    public static int Run(){
        var f=Fresh();
        Check(f.layer.Priority>100,"priority_above_supported_native_and_follow_combat_layers");
        Check(!f.layer.IsActive()&&f.native.GetDecision().Action==BotLogicDecision.runAwayGrenade,"unsafe_position_yields_native_escape");
        f.bot.Transform.position=new(15,0,0);
        Check(!f.native.ShallUseNow()&&f.layer.IsActive(),"safe_position_retained_while_native_avoidance_drops");
        Check(f.layer.GetNextAction().Type==typeof(CombatHoldPositionAction)&&!f.layer.IsCurrentActionEnding(),"shared_combat_hold_and_stable_wait");
        Time.time=106;Check(f.layer.IsActive(),"long_fuse_keeps_response_past_native_default_timeout");
        f.bot.Memory.HaveEnemy=true;f.bot.Memory.GoalEnemy.IsVisible=true;f.bot.Memory.GoalEnemy.CanShoot=true;
        Check(f.layer.IsCurrentActionEnding()&&f.layer.GetNextAction().Type==typeof(CombatShootFromPlaceAction),"visible_shootable_enemy_breaks_wait_into_our_shoot_action");
        f.bot.Memory.IsUnderFire=true;
        Check(f.layer.IsCurrentActionEnding()&&f.layer.GetNextAction().Type==typeof(CombatDogFightAction),"incoming_fire_breaks_shoot_into_our_dogfight");
        var action=f.layer.GetNextAction();var payload=(FollowerCombatActionData)action.Data;
        f.layer.CurrentAction=action;BrainManager.ActiveLayer=f.layer;
        var dogfight=new CombatDogFightAction(f.bot);
        Check(dogfight.Move(new(BotLogicDecision.holdPosition,"stalePreviousAction",null),new(1,0,0))==NavMeshPathStatus.PathInvalid&&f.bot.MoveCalls==0,"first_update_uses_current_payload_even_when_bigbrain_supplies_stale_data");
        BrainManager.ActiveLayer=null;
        Check(dogfight.Move(payload,new(17,0,3))==NavMeshPathStatus.PathComplete&&f.bot.MoveCalls==1,"shared_dogfight_allows_safe_movement");
        Check(dogfight.Move(payload,new(1,0,0))==NavMeshPathStatus.PathInvalid&&f.bot.MoveCalls==1,"shared_dogfight_rejects_unsafe_destination_before_move");
        Check(dogfight.Move(payload,new(-15,0,0))==NavMeshPathStatus.PathInvalid,"safe_destination_across_grenade_rejects_whole_path");
        NavMesh.Corners=new[]{f.bot.Position,new Vector3(0,0,0),new Vector3(18,0,0)};
        Check(dogfight.Move(payload,new(18,0,0))==NavMeshPathStatus.PathInvalid,"navmesh_detour_through_grenade_is_rejected");
        NavMesh.Corners=null;NavMesh.Complete=false;
        Check(dogfight.Move(payload,new(17,0,0))==NavMeshPathStatus.PathInvalid,"incomplete_path_is_rejected");NavMesh.Complete=true;
        Check(dogfight.Move(new(BotLogicDecision.dogFight,"normal",null),new(1,0,0))==NavMeshPathStatus.PathComplete,"normal_combat_dogfight_without_predicate_unchanged");
        f.bot.Memory.IsUnderFire=false;f.bot.Memory.LastTimeHit=Time.time;
        Check(f.layer.GetNextAction().Type==typeof(CombatDogFightAction),"recent_hit_uses_shared_dogfight");
        f.bot.Memory.LastTimeHit=0;f.bot.Memory.HaveEnemy=false;f.bot.Medecine.FirstAid.Pending=true;
        Check(f.layer.GetNextAction().Type==typeof(CombatHoldPositionAction),"no_healing_in_exposed_safe_distance_position");
        Cover(f.bot);
        Check(f.layer.GetNextAction().Type==typeof(HealAction),"safe_occupied_cover_uses_shared_heal_action");
        f.bot.Medecine.FirstAid.Using=true;f.bot.Memory.HaveEnemy=true;f.bot.Memory.IsUnderFire=true;
        Check(f.layer.IsCurrentActionEnding()&&!f.bot.Medecine.FirstAid.Using&&f.bot.Medecine.FirstAid.Canceled==1,"incoming_fire_cancels_medical_before_dogfight");
        Check(f.layer.GetNextAction().Type==typeof(CombatDogFightAction),"heal_to_dogfight_handoff");
        f.bot.Memory.HaveEnemy=false;f.bot.Memory.IsUnderFire=false;
        f.layer.GetNextAction();f.bot.Medecine.SurgicalKit.Using=true;f.bot.Memory.CurCustomCoverPoint!.IsSpotted=true;
        Check(f.layer.IsCurrentActionEnding()&&!f.bot.Medecine.SurgicalKit.Using&&f.layer.GetNextAction().Type==typeof(CombatHoldPositionAction),"compromised_cover_cancels_surgery");
        f.bot.Transform.position=new(2,0,0);
        Check(!f.layer.IsActive(),"displacement_back_into_unsafe_position_resumes_escape");
        f.bot.Memory.CurCustomCoverPoint.IsSpotted=false;
        Check(f.layer.IsActive()&&f.layer.GetNextAction().Type==typeof(HealAction),"shielded_occupied_cover_can_heal_inside_radius");
        f.bot.Memory.HaveEnemy=true;f.bot.Memory.IsUnderFire=true;
        payload=(FollowerCombatActionData)f.layer.GetNextAction().Data;
        Check(dogfight.Move(payload,new(17,0,0))==NavMeshPathStatus.PathInvalid,"shielded_dogfight_retains_cover_instead_of_crossing_blast_radius");
        f.bot.Memory.CurCustomCoverPoint.Shielded=false;
        Check(!f.layer.IsActive(),"lost_shielding_yields_native_escape");
        f=Fresh();f.bot.Transform.position=new(15,0,0);Cover(f.bot);f.bot.Medecine.FirstAid.Pending=true;
        f.bot.Memory.HaveEnemy=true;f.bot.Memory.GoalEnemy.PersonalLastSeenTime=Time.time;
        Check(f.layer.GetNextAction().Type==typeof(CombatHoldPositionAction),"recent_enemy_contact_blocks_heal");
        Time.time+=3.1f;
        Check(f.layer.GetNextAction().Type==typeof(HealAction),"cooled_enemy_contact_allows_heal_in_safe_cover");
        f.bot.Medecine.FirstAid.Pending=false;
        Check(f.layer.IsCurrentActionEnding()&&f.layer.GetNextAction().Type==typeof(CombatHoldPositionAction),"medical_completion_returns_to_combat_hold");
        f.wire._tripwireSyncObject.Live.BlowUp();
        Check(!f.layer.IsActive()&&f.layer.IsCurrentActionEnding(),"detonation_releases_to_follow_or_combat");
        f=Fresh();f.bot.Transform.position=new(15,0,0);Time.time=109;
        Check(!f.layer.IsActive(),"danger_expiry_releases_response");
        foreach(int other in new[]{0,1,2,3}){
            f=Fresh();f.bot.Transform.position=new(15,0,0);
            if(other==0)f.bot.ArtilleryDangerPlace.IsActive=true;
            if(other==1)f.bot.BewareBTR.IsActive=true;
            if(other==2)f.bot.BotTurnAwayLight.IsActive=true;
            if(other==3)f.bot.FlashGrenade.IsFlashed=true;
            Check(!f.layer.IsActive()&&f.native.ShallUseNow(),"native_other_danger_preempts_response_"+other);
        }
        f=Fresh();f.bot.Transform.position=new(15,0,0);f.bot.WeaponManager.Grenades.ThrowindNow=true;
        Check(!f.layer.IsActive(),"in_progress_throw_guard");
        f=Fresh();f.bot.Transform.position=new(15,0,0);BossPlayers.Followers.Clear();
        Check(!f.layer.IsActive(),"dismissed_follower_releases_layer");
        f=Fresh();f.bot.Transform.position=new(15,0,0);f.bot.IsDead=true;Check(!f.layer.IsActive(),"dead_follower_not_retained");
        f.bot.IsDead=false;f.bot.BotState=EBotState.Inactive;Check(!f.layer.IsActive(),"inactive_follower_not_retained");
        f=Fresh();f.bot.Transform.position=new(15,0,0);
        f.bot.BewareGrenade.GrenadeDangerPoint!.Destroy();
        f.bot.BewareGrenade.SetGrenadeDangerPoint(new(default,new Grenade(),f.bot,0));Time.time+=.1f;
        Check(!f.layer.IsActive(),"ordinary_grenade_does_not_activate_tripwire_response");
        f=Fresh();f.bot.Transform.position=new(15,0,0);Cover(f.bot);f.bot.Medecine.FirstAid.Pending=true;
        f.layer.GetNextAction();f.bot.Medecine.FirstAid.Using=true;f.bot.Memory.IsUnderFire=true;
        Check(f.layer.IsCurrentActionEnding()&&!f.bot.Medecine.FirstAid.Using&&f.layer.GetNextAction().Type==typeof(CombatHoldPositionAction),"fire_without_identified_enemy_cancels_heal_and_uses_defensive_hold");
        f.bot.Memory.IsUnderFire=false;f.layer.GetNextAction();f.bot.Medecine.FirstAid.Using=true;
        f.bot.Transform.position=new(2,0,0);f.bot.Memory.CurCustomCoverPoint!.Shielded=false;
        Check(!f.layer.IsActive()&&!f.bot.Medecine.FirstAid.Using,"unsafe_position_cancels_heal_before_native_escape");
        f=Fresh();f.bot.Transform.position=new(15,0,0);Cover(f.bot);f.bot.Medecine.FirstAid.Pending=true;
        f.layer.GetNextAction();f.bot.Medecine.FirstAid.Using=true;f.wire._tripwireSyncObject.Live.BlowUp();
        Check(!f.layer.IsActive(),"detonation_releases_active_medical_response");f.layer.Stop();
        Check(f.bot.Medecine.FirstAid.Using,"detonation_allows_ongoing_medical_handoff");
        return checks;
    }
}
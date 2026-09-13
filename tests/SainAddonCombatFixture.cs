using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using SAIN;
using SAIN.Components;
using SAIN.Layers;
using SAIN.Preset.Shared.Enums;
using UnityEngine;
[assembly: AssemblyVersion("4.5.1.0")]
namespace UnityEngine {
    public static class Time { public static float time; }
    public partial struct Vector3 {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public float sqrMagnitude=>x*x+y*y+z*z;
        public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    }
}
namespace EFT {
    public partial class Player { public Vector3 Position; }
    public class Memory { public bool HaveEnemy,DeadGoal; }
    public class Mind { public float TIME_TO_FORGOR_ABOUT_ENEMY_SEC=60; }
    public class FileSettings { public Mind Mind=new Mind(); }
    public class Settings { public FileSettings FileSettings=new FileSettings(); }
    public partial class BotOwner {
        public string ProfileId="selected"; public bool IsDead, Active=true, IsFollower=true, FriendlyInLane;
        public Player GetPlayer=new Player(); public Memory Memory=new Memory(); public Settings Settings=new Settings();
        public BotComponent Sain; public BotFollowerPlayer Follower; public Player Leader=new Player();
    }
}
namespace DrakiaXYZ.BigBrain.Brains {
    public class CustomLayer {
        public class ActionData {}
        public class Action {
            public Type Type;public string Reason;
            public Action(Type type,string reason){Type=type;Reason=reason;}
        }
    }
}
namespace SAIN.Extensions { public static class BotExtensions { public static bool IsBotActive(this BotOwner owner)=>owner.Active&&!owner.IsDead; } }
namespace SAIN.Models.Enums { public enum ESAINLayer { None,Combat,Squad,AvoidThreat,Flashed } }
namespace SAIN.Preset.Shared.Enums {
    public enum ECombatDecision { None,MoveToEngage,MeleeAttack,FightZombies,RushEnemy,ThrowGrenade,ShiftCover,SeekCover,Retreat,ShootDistantEnemy,StandAndShoot,Search,Freeze,DogFight,AvoidGrenade }
    public enum ESelfActionType { None,Surgery,Reload }
}
namespace SAIN.Layers {
    public abstract class SAINLayer : DrakiaXYZ.BigBrain.Brains.CustomLayer {
        public BotOwner BotOwner;public BotComponent Bot;public bool NativeDecisionChanged;private SAIN.Models.Enums.ESAINLayer category;
        protected SAINLayer(BotOwner owner,int priority,string name,SAIN.Models.Enums.ESAINLayer layer){BotOwner=owner;category=layer;}
        protected virtual bool GetBotComponent(){Bot=BotOwner.Sain;return Bot!=null;}
        protected void CheckActiveChanged(bool active){if(Bot!=null){if(active)Bot.ActiveLayer=category;else if(Bot.ActiveLayer==category)Bot.ActiveLayer=SAIN.Models.Enums.ESAINLayer.None;}}
        public abstract bool IsActive();public abstract Action GetNextAction();
        public virtual bool IsCurrentActionEnding(){bool changed=NativeDecisionChanged;NativeDecisionChanged=false;return changed;}
        public virtual void Stop(){}
    }
    public interface IBotAction {}
    public abstract class BotAction {
        protected BotOwner BotOwner;public BotComponent Bot=>BotOwner.Sain;protected Shooter Shoot=>Bot.Shoot;public virtual void OnSteeringTicked(){}
        protected BotAction(BotOwner owner,string name){BotOwner=owner;}
        public virtual void Start(){} public virtual void Stop(){}
        public virtual void Update(DrakiaXYZ.BigBrain.Brains.CustomLayer.ActionData data){}
    }
    public class NativeLayer {public BotOwner BotOwner{get;set;}} public class SAINAvoidThreatLayer : NativeLayer {} public class ExtractLayer : NativeLayer {}
}
namespace SAIN.Layers.Flashed { public class SAINFlashedLayer : SAIN.Layers.NativeLayer {} }
namespace SAIN.Layers.Combat.Run { public class DebugLayer : SAIN.Layers.NativeLayer {} }
namespace SAIN.Layers.Combat.Squad { public class CombatSquadLayer : SAIN.Layers.NativeLayer {} }
namespace SAIN.Layers.Combat.Solo {
    public class CombatSoloLayer : SAIN.Layers.NativeLayer {}
    public class StandAndShootAction : BotAction { public StandAndShootAction(BotOwner o):base(o,""){} }
    public class MoveToEngageAction : BotAction { public MoveToEngageAction(BotOwner o):base(o,""){} }
    public class MeleeAttackAction : BotAction { public MeleeAttackAction(BotOwner o):base(o,""){} }
    public class FightZombiesAction : BotAction { public FightZombiesAction(BotOwner o):base(o,""){} }
    public class RushEnemyAction : BotAction { public RushEnemyAction(BotOwner o):base(o,""){} }
    public class ThrowGrenadeAction : BotAction { public ThrowGrenadeAction(BotOwner o):base(o,""){} }
    public class SearchAction : BotAction { public SearchAction(BotOwner o):base(o,""){} }
    public class FreezeAction : BotAction { public FreezeAction(BotOwner o):base(o,""){} }
}
namespace SAIN.Layers.Combat.Solo.Cover {
    public class DoSurgeryAction : BotAction { public DoSurgeryAction(BotOwner o):base(o,""){} }
    public class ShiftCoverAction : BotAction { public ShiftCoverAction(BotOwner o):base(o,""){} }
    public class SeekCoverAction : BotAction { public SeekCoverAction(BotOwner o):base(o,""){} }
}
namespace SAIN.Components {
    public partial class Decision {
        public ECombatDecision CurrentCombatDecision;
        public ESelfActionType CurrentSelfDecision;
        public bool HasDecision=>CurrentCombatDecision!=ECombatDecision.None || CurrentSquadDecision!=ESquadDecision.None;
        public int Resets;public void ResetDecisions(bool active){Resets++;CurrentCombatDecision=ECombatDecision.None;CurrentSquadDecision=ESquadDecision.None;}
    }
    public class Cover { public object CoverInUse; }
    public partial class Mover {
        public int Stops,Paths;public bool Complete=true;public Vector3 Destination;
        public void Stop(){Stops++;}
        public bool WalkToPoint(Vector3 target,bool complete=true,float distance=-1){Paths++;Destination=target;return Complete;}
    }
    public partial class BotComponent {
        public BotOwner BotOwner{get;set;} public bool IsDead=>BotOwner.IsDead;
        public SAIN.SAINComponent.Classes.Decision.SAINDecisionClass Decision=new SAIN.SAINComponent.Classes.Decision.SAINDecisionClass(); public Cover Cover=new Cover();public Mover Mover=new Mover();
        public SAIN.SAINComponent.Classes.Info.SAINBotInfoClass Info;
        public SAIN.Models.Enums.ESAINLayer ActiveLayer;
        public BotComponent(BotOwner owner){BotOwner=owner;Info=new SAIN.SAINComponent.Classes.Info.SAINBotInfoClass(owner);}
    }
}
namespace SAIN {
    public static class SAINEnableClass {public static bool GetSAIN(string id,out BotComponent bot){bot=CombatChecks.Bots.Find(b=>b.ProfileId==id)?.Sain;return bot!=null;}}
    public static class SAINPlugin {public static object LoadedPreset=new object();}
}
namespace SAIN.SAINComponent.Classes.Info {
    public enum Personality { Normal,Chad,GigaChad }
    public class Difficulty {
        public float AggressionModifier{get;private set;}=1;
        public int Updates;public void UpdateSettings(object preset){Updates++;AggressionModifier=2;}
    }
    public partial class SAINBotInfoClass {
        private BotOwner owner;public SAINBotInfoClass(BotOwner bot){owner=bot;}
        public Personality Personality{get;private set;}
        public object PersonalitySettingsClass{get;private set;}=new object();
        public Difficulty Difficulty{get;}=new Difficulty();
        public float ForgetEnemyTime{get;private set;}=60;public int SearchRefreshes,HoldRefreshes;
        public void SetPersonality(Personality personality){Personality=personality;PersonalitySettingsClass=new object();}
        public void CalcTimeBeforeSearch(){SearchRefreshes++;ForgetEnemyTime=999;owner.Settings.FileSettings.Mind.TIME_TO_FORGOR_ABOUT_ENEMY_SEC=999;}
        public void CalcHoldGroundDelay(){HoldRefreshes++;}
    }
}
namespace SAIN.SAINComponent.Classes {
    public enum FriendlyFireStatus { None,Clear,FriendlyBlock }
    public static class SAINFriendlyFireClass {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static FriendlyFireStatus CheckFriendlyFireStatus(Vector3 target,Vector3 origin,Vector3 direction,BotComponent bot)=>FriendlyFireStatus.None;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static FriendlyFireStatus CheckFriendlyFireStatus(float distance,Vector3 origin,Vector3 direction,BotComponent bot)=>FriendlyFireStatus.Clear;
    }
}
namespace pitTeam.Components {
    public enum FollowerCombatTactic { Balanced,Marksman,SainMan }
    public enum FollowerCommandType { None,RegroupNearBoss,CombatComeToBossCover,CombatMoveToPointTactical,PushEnemy }
    public class pitAIBossPlayer {}
    public class BotFollowerPlayer {
        public BotOwner Owner;public FollowerCombatTactic CombatTactic=FollowerCombatTactic.SainMan;
        public FollowerCommandType Command;public Vector3 Target;public string EndReason;
        public BotOwner GetBot()=>Owner;public bool HasCombatHandoffSignal()=>Owner.Memory.HaveEnemy&&!Owner.Memory.DeadGoal;
        public bool TryGetActiveCommand(out FollowerCommandType command,out Vector3 target){command=Command;target=Target;return command!=FollowerCommandType.None;}
        public void ClearCommand(string reason){Command=FollowerCommandType.None;EndReason=reason;}
    }
}
namespace pitTeam.Modules {
    public static class Logger {
        public static List<string> Errors=new List<string>();public static void LogInfo(string text){}
        public static void LogError(string text){Errors.Add(text);}
    }
    public class BossPlayers {
        public static BossPlayers Instance=new BossPlayers();
        public BotFollowerPlayer GetFollower(BotOwner owner)=>owner?.IsFollower==true?owner.Follower:null;
        public static bool IsFollower(BotOwner owner)=>owner.IsFollower;
        public static List<BotFollowerPlayer> GetFollowers()=>CombatChecks.Bots.FindAll(b=>b.IsFollower).ConvertAll(b=>b.Follower);
    }
    public static class SainPlayerSquadBridge {
        public static bool IsEnabled=true;
        public static bool TryGetPlayerLeader(BotOwner owner,out Player leader){leader=owner.Leader;return leader!=null;}
    }
}
namespace pitTeam.Utils {
    public static class FollowerMedical {public static void BeginPostCombatFullHeal(BotOwner owner){}}
    public static class FollowerShotSafety {public static bool IsFriendlyInShotLane(BotOwner owner,Vector3 origin,Vector3 direction,float distance)=>owner.FriendlyInLane;}
}
namespace pitTeam {
    public static class pitFireTeam {
        public static bool IsSAINInstalled=true,IsSAINAddonInstalled=true;
__COMBAT_GATES__
    }
}
public static partial class CombatChecks {
    public static List<BotOwner> Bots=new List<BotOwner>();private static int count;
    public static Type ResolveType(string name)=>typeof(CombatChecks).Assembly.GetType(name,true);
    private static void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;Console.WriteLine("PASS "+text);}
    private static BotOwner Spawn(string id,FollowerCombatTactic tactic=FollowerCombatTactic.SainMan) {
        var owner=new BotOwner{ProfileId=id};owner.Sain=new BotComponent(owner);
        owner.Follower=new BotFollowerPlayer{Owner=owner,CombatTactic=tactic};Bots.Add(owner);return owner;
    }
    private static void Tick(){Time.time+=1;SainAddonBridge.RaiseBossGroupStaticUpdate(new pitAIBossPlayer());}
    public static void Main(){
        SAINActionTypes.Validate();
        SainSquadDecisionBridge.Apply(new Harmony("pitTeam.sain.squad.test"));
        Check(SainSquadDecisionBridge.IsAvailable,"native squad decision bridge installed");
        var selected=Spawn("selected");var rifle=Spawn("rifle",FollowerCombatTactic.Balanced);var marks=Spawn("marks",FollowerCombatTactic.Marksman);
        var layer=new SAINFollowerSoloCombatLayer(selected,72);new SAINFollowerSoloCombatLayer(rifle,72);new SAINFollowerSoloCombatLayer(marks,74);
        var squadLayer=new SAINFollowerSquadCombatLayer(selected,75);new SAINFollowerSquadCombatLayer(rifle,75);new SAINFollowerSquadCombatLayer(marks,75);
        Check(!pitFireTeam.UseSainFollowerCombat(selected),"registration alone does not take core combat");
        SAINFollowerRuntime.Enable();
        Check(!pitFireTeam.UseSainFollowerCombat(selected),"unprepared bot retains core fallback");
        Tick();
        Check(pitFireTeam.UseSainFollowerCombat(selected),"ready SainMan selects addon combat");
        Check(!pitFireTeam.UseSainFollowerCombat(rifle)&&!pitFireTeam.UseSainFollowerCombat(marks),"Rifleman and Marksman retain core ownership");
        Check(selected.Sain.Info.Personality==SAIN.SAINComponent.Classes.Info.Personality.Chad&&rifle.Sain.Info.Personality==SAIN.SAINComponent.Classes.Info.Personality.Normal,"Chad applies only to SainMan");
        Check(selected.Settings.FileSettings.Mind.TIME_TO_FORGOR_ABOUT_ENEMY_SEC==60&&selected.Sain.Info.ForgetEnemyTime==60&&selected.Sain.Info.Difficulty.Updates==1,"personality refresh preserves follower memory and refreshes native difficulty");
        Tick();Check(selected.Sain.Info.Difficulty.Updates==1,"stable personality is not reapplied every tick");
        selected.Sain.Info.SetPersonality(SAIN.SAINComponent.Classes.Info.Personality.Normal);Tick();
        Check(selected.Sain.Info.Personality==SAIN.SAINComponent.Classes.Info.Personality.Chad&&selected.Sain.Info.Difficulty.Updates==2,"preset reroll restores Chad");
        Check(!layer.IsActive(),"no decision leaves addon combat inactive");
        var expected=new Dictionary<ECombatDecision,string>{
            {ECombatDecision.MoveToEngage,"MoveToEngageAction"},{ECombatDecision.MeleeAttack,"MeleeAttackAction"},
            {ECombatDecision.FightZombies,"FightZombiesAction"},{ECombatDecision.RushEnemy,"RushEnemyAction"},
            {ECombatDecision.ThrowGrenade,"ThrowGrenadeAction"},{ECombatDecision.ShiftCover,"ShiftCoverAction"},
            {ECombatDecision.SeekCover,"SeekCoverAction"},{ECombatDecision.Retreat,"SeekCoverAction"},
            {ECombatDecision.ShootDistantEnemy,"StandAndShootAction"},{ECombatDecision.StandAndShoot,"StandAndShootAction"},
            {ECombatDecision.Search,"SearchAction"},{ECombatDecision.Freeze,"FreezeAction"}};
        foreach(var pair in expected){
            selected.Sain.Decision.CurrentCombatDecision=pair.Key;
            Check(layer.IsActive()&&layer.GetNextAction().Type.Name==pair.Value,"native action mapping "+pair.Key);
        }
        layer.NativeDecisionChanged=true;Check(layer.IsCurrentActionEnding(),"native decision event ends current action");
        selected.Sain.Decision.CurrentSelfDecision=ESelfActionType.Surgery;selected.Sain.Cover.CoverInUse=new object();
        Check(layer.IsCurrentActionEnding()&&layer.GetNextAction().Type.Name=="DoSurgeryAction","surgery arrives in cover and selects native surgery");
        selected.Sain.Decision.CurrentSelfDecision=ESelfActionType.None;Check(layer.IsCurrentActionEnding(),"finished surgery releases action");
        selected.Follower.Command=FollowerCommandType.RegroupNearBoss;
        Check(layer.GetNextAction().Type.Name=="FreezeAction"&&selected.Follower.Command==FollowerCommandType.RegroupNearBoss,"solo replica does not consume follower commands");
        selected.Follower.Command=FollowerCommandType.None;
        TestSquad(selected,rifle,layer,squadLayer);
        selected.Sain.Decision.CurrentCombatDecision=ECombatDecision.Search;selected.Memory.HaveEnemy=true;
        SainAddonBridge.TryIsReadyForPatrolAfterCombat(selected,out bool ready);Check(!ready,"combat blocks patrol");
        SainAddonBridge.TryResetDecisionState(selected);
        Check(selected.Sain.Decision.CurrentCombatDecision==ECombatDecision.None&&selected.Memory.HaveEnemy,"native reset preserves living enemy memory");
        selected.Memory.DeadGoal=true;
        SainAddonBridge.TryIsReadyForPatrolAfterCombat(selected,out ready);Check(ready,"dead remembered goal cannot hold patrol hostage");
        selected.Memory.HaveEnemy=false;selected.Memory.DeadGoal=false;
        SainAddonBridge.TryIsReadyForPatrolAfterCombat(selected,out ready);Check(ready,"cleared combat releases patrol");
        selected.Sain.ActiveLayer=SAIN.Models.Enums.ESAINLayer.AvoidThreat;layer.Stop();
        Check(selected.Sain.ActiveLayer==SAIN.Models.Enums.ESAINLayer.AvoidThreat,"addon stop preserves higher priority native threat ownership");
        var late=Spawn("late");Tick();Check(!pitFireTeam.UseSainFollowerCombat(late),"missing addon layer retains core fallback");
        new SAINFollowerSoloCombatLayer(late,74);Tick();Check(!pitFireTeam.UseSainFollowerCombat(late),"solo alone cannot enable SainMan");
        new SAINFollowerSquadCombatLayer(late,75);Tick();Check(pitFireTeam.UseSainFollowerCombat(late),"both replicas enable late SainMan");
        selected.Follower.CombatTactic=FollowerCombatTactic.Marksman;Tick();
        Check(!pitFireTeam.UseSainFollowerCombat(selected)&&selected.Sain.Info.Personality==SAIN.SAINComponent.Classes.Info.Personality.Normal,"opt out restores previous personality and core ownership");
        selected.Follower.CombatTactic=FollowerCombatTactic.SainMan;Tick();selected.IsFollower=false;
        SainAddonBridge.RaiseFollowerLifecycleEvent(selected,FollowerLifecycleEvent.OnDismiss);
        Check(!pitFireTeam.UseSainFollowerCombat(selected)&&selected.Sain.Info.Personality==SAIN.SAINComponent.Classes.Info.Personality.Normal,"dismiss restores native personality");
        var nativeLayers=new SAIN.Layers.NativeLayer[]{
            new SAIN.Layers.Combat.Solo.CombatSoloLayer(),new SAIN.Layers.Combat.Squad.CombatSquadLayer(),
            new SAIN.Layers.ExtractLayer(),new SAIN.Layers.Combat.Run.DebugLayer(),
            new SAIN.Layers.SAINAvoidThreatLayer(),new SAIN.Layers.Flashed.SAINFlashedLayer()};
        for(int i=0;i<nativeLayers.Length;i++){
            var native=nativeLayers[i];native.BotOwner=late;
            Check(NativeOwnershipGate.Allow(native)==(i>=4),"SainMan native layer ownership "+native.GetType().Name);
            native.BotOwner=rifle;Check(!NativeOwnershipGate.Allow(native),"core tactic suppresses native layer "+native.GetType().Name);
            native.BotOwner=selected;Check(NativeOwnershipGate.Allow(native),"ordinary bot retains native layer "+native.GetType().Name);
        }
        var harmony=new Harmony("pitTeam.sainaddon.test");
        pitTeam.Patches.FollowerSainFriendlyFirePatch.Apply(harmony);
        late.FriendlyInLane=true;
        Check(SAIN.SAINComponent.Classes.SAINFriendlyFireClass.CheckFriendlyFireStatus(new Vector3(10,0,0),new Vector3(),new Vector3(1,0,0),late.Sain)==SAIN.SAINComponent.Classes.FriendlyFireStatus.FriendlyBlock,"human leader safety overrides SAIN single-member bypass");
        Check(SAIN.SAINComponent.Classes.SAINFriendlyFireClass.CheckFriendlyFireStatus(10,new Vector3(),new Vector3(1,0,0),late.Sain)==SAIN.SAINComponent.Classes.FriendlyFireStatus.FriendlyBlock,"distance overload applies shared safety");
        late.FriendlyInLane=false;
        Check(SAIN.SAINComponent.Classes.SAINFriendlyFireClass.CheckFriendlyFireStatus(10,new Vector3(),new Vector3(1,0,0),late.Sain)==SAIN.SAINComponent.Classes.FriendlyFireStatus.Clear,"clear follower lane preserves native result");
        late.IsFollower=false;late.FriendlyInLane=true;
        Check(SAIN.SAINComponent.Classes.SAINFriendlyFireClass.CheckFriendlyFireStatus(10,new Vector3(),new Vector3(1,0,0),late.Sain)==SAIN.SAINComponent.Classes.FriendlyFireStatus.Clear,"ordinary bot friendly-fire policy untouched");
        late.IsFollower=true;
        SAINFollowerRuntime.Disable();
        Check(!SainAddonBridge.HasRuntimeCallbacks&&!pitFireTeam.UseSainFollowerCombat(late),"addon shutdown restores core fallback");
        Check(Logger.Errors.Count==0,"no lifecycle errors");
        Console.WriteLine("Passed "+count+" production addon combat checks. Unity movement and raid AI still require in-game validation.");
    }
}
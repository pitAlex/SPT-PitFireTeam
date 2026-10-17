#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using Time = UnityEngine.Time;
using Random = UnityEngine.Random;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using SAIN.Components;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.Classes.WeaponFunction;
using SAIN.Preset.Shared.GlobalSettings;
using UnityEngine;
using pitTeam.Components;

namespace UnityEngine {
    public static class Time { public static float time; }
    public static class Random {public static float Range(float a,float b)=>a;}
    public static class Mathf {public static float CeilToInt(float v)=>(float)Math.Ceiling(v);public static float Clamp01(float v)=>Math.Max(0,Math.Min(1,v));}
    public struct Quaternion {}
    public struct Vector2 {
        public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}
        public float sqrMagnitude=>x*x+y*y;
        public static Vector2 operator -(Vector2 a,Vector2 b)=>new(a.x-b.x,a.y-b.y);
        public static Vector2 operator +(Vector2 a,Vector2 b)=>new(a.x+b.x,a.y+b.y);
        public static Vector2 operator *(Vector2 a,float b)=>new(a.x*b,a.y*b);
        public static float Dot(Vector2 a,Vector2 b)=>a.x*b.x+a.y*b.y;
    }
    public struct Vector3 {
        public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public float sqrMagnitude=>x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
    }
}
namespace Comfort.Common {}
namespace pitTeam.Modules { static class FollowerBlockedGrenadeRecovery { public static void Record(BotOwner bot,Player.BaseGrenadeHandsController hands){} } }
namespace EFT.InventoryLogic {public class ThrowWeap {}}
namespace EFT {
    public enum EPhraseTrigger {NeedFrag}
    public class Health {public bool IsAlive=true;}
    public class AIData {public BotOwner BotOwner;}
    public class Grenade {}
    public class Player {
        public string ProfileId;public Health HealthController=new();public Vector3 Position;public AIData AIData=new();public object HandsController;
        public class BaseGrenadeHandsController {
            public Player _player;public int Consumed,Spawned;public bool InventorySucceeds=true;
            [MethodImpl(MethodImplOptions.NoInlining)]
            public virtual void ThrowGrenade(float timeSinceSafetyLevelRemoved,Vector3 position,Quaternion rotation,Vector3 force,bool lowThrow) {
                if(!InventorySucceeds)return;Consumed++;CreateAndRegisterGrenade(position,rotation,force,timeSinceSafetyLevelRemoved);
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            public Grenade CreateAndRegisterGrenade(Vector3 position,Quaternion rotation,Vector3 force,float prewarm=0) {Spawned++;return new();}
        }
    }
    public interface IBossToFollow {Player Player();List<BotOwner> Followers {get;}}
    public class TestBoss : pitAIBossPlayer {}
    public class BotFollower {public IBossToFollow BossToFollow;}
    public class BotsGroup {public string Id;}
    public class CoreSettings {public bool CanGrenade;}
    public class FileSettings {public CoreSettings Core=new();}
    public class BotSettings {public FileSettings FileSettings=new();}
    public class WeaponManager {public BotGrenadeController Grenades;}
    public class Profile {public string Nickname;}
    public class MovementTarget {public bool Active,Arrived;public Vector3 Point;public bool HaveTarget()=>Active;public bool IsCome()=>Arrived;}
    public class BotOwner {
        public string ProfileId;public bool IsFollower,Addon,Ready=true,Contact=true;
        public bool IsDead;public Player GetPlayer=new();public Vector3 Position=>GetPlayer.Position;public Profile Profile=new();public MovementTarget GoToSomePointData=new();
        public BotSettings Settings=new();public BotFollower BotFollower=new();public BotsGroup BotsGroup;
        public WeaponManager WeaponManager=new();public void SetPose(float v) {}
    }
    public class AIGreanageThrowData {public bool Valid=true;public Vector3 Target=new(20,0,0);public bool IsUpToDate()=>Valid;}
    public class BotGrenadeController {
        public BotOwner _owner;public EFT.InventoryLogic.ThrowWeap grenade=new();
        public bool ThrowindNow,ReadyToThrow=true;public int Throws,NativeCalls;
        public AIGreanageThrowData AIGreanageThrowData=new();
        public bool HaveGrenade { [MethodImpl(MethodImplOptions.NoInlining)] get => grenade!=null && GlobalSettingsClass.Instance.General.BotsUseGrenades && _owner.Settings.FileSettings.Core.CanGrenade; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool DoThrow(){NativeCalls++;if(ThrowindNow||!ReadyToThrow)return false;Throws++;ThrowindNow=true;return true;}
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void EndAll(EFT.InventoryLogic.ThrowWeap grenade=null){ThrowindNow=false;}
    }
}
namespace pitTeam.Components {
    public class pitAIBossPlayer : IBossToFollow {
        public Player Boss=new(){ProfileId="player",Position=new(-50,0,0)};
        public Player realPlayer=>Boss;public List<BotOwner> Followers{get;}=new();public Player Player()=>Boss;
    }
}
namespace SAIN.Preset.Shared.GlobalSettings.Categories.General {
    public class GeneralSettings {public bool BotsUseGrenades,BotVsBotGrenade;}
}
namespace SAIN.Preset.Shared.GlobalSettings {
    public class GlobalSettingsClass {public static GlobalSettingsClass Instance=new();public Categories.General.GeneralSettings General=new();}
}
namespace SAIN.SAINComponent.Classes.EnemyClasses {
    public class Enemy {public bool IsAI=true,Active=true;public Player EnemyPlayer=new();public static bool IsEnemyActive(Enemy e)=>e.Active;}
}
namespace SAIN.Components {
    public class Talk {public void GroupSay(EPhraseTrigger p,object o,bool b,float chance) {}}
    public class Squad {public Dictionary<string,BotComponent> Members=new();}
    public class BotComponent {public BotOwner BotOwner;public Talk Talk=new();public Squad Squad=new();public Vector3 Position=>BotOwner.Position;}
}
namespace SAIN.SAINComponent.Classes.WeaponFunction {
    public class GrenadeThrowDecider {
        public BotComponent Bot;public BotOwner BotOwner=>Bot.BotOwner;public GlobalSettingsClass GlobalSettings=>GlobalSettingsClass.Instance;
        private bool _grenadesEnabled,_canThrowGrenades;private readonly bool initialEnabled,initialCapable;
        private float _nextPossibleAttempt,_throwGrenadeFreq=5,_throwGrenadeFreqMax=10,_nextSayNeedGrenadeTime,_sayNeedGrenadeFreq=10,_sayNeedGrenadeChance=5;
        public bool HandsSafe=true,TargetSafe=true,ArcSafe=true;public bool ThrowException;public int Checks;
        public Vector3 ReportTarget=new(20,0,0),FinalTarget=new(20,0,0);private float _minFriendlyDistToThrow_SQR=64;
        public GrenadeThrowDecider(BotComponent bot,bool enabled=false,bool capable=false){Bot=bot;_grenadesEnabled=initialEnabled=enabled;_canThrowGrenades=initialCapable=capable;}
        public bool FlagsUnchanged=>_grenadesEnabled==initialEnabled&&_canThrowGrenades==initialCapable;
        private bool CheckCanThrow(out string reason){Checks++;if(ThrowException)throw new InvalidOperationException("fixture planner error");reason="hands";return HandsSafe;}
        private bool CanThrowAtEnemy(Enemy e,out string reason){reason="targetSafety";return TargetSafe;}
        private bool FindThrowTarget(Enemy e){if(!ArcSafe||!CheckFriendlyDistances(ReportTarget)||!CheckFriendlyDistances(FinalTarget))return false;BotOwner.WeaponManager.Grenades.AIGreanageThrowData.Target=FinalTarget;return true;}
        __NATIVE_FRIENDLY__
        __NATIVE_DECISION__
        __NATIVE_THROW__
    }
}
namespace SPT.Reflection.Patching {
    public abstract class ModulePatch {protected abstract MethodBase GetTargetMethod();}
    public class PatchPrefixAttribute:Attribute {}
    public class PatchPostfixAttribute:Attribute {}
}
namespace pitTeam {
    public class Switch {public bool Value=true;}
    public static class pitFireTeam {public static Switch botGrenades=new();}
}
namespace pitTeam.Modules {
    public static class BossPlayers {public static Dictionary<string,BotOwner> Bots=new();public static bool IsFollower(BotOwner b)=>b.IsFollower;public static object GetFollowerByProfileId(string id)=>Bots.TryGetValue(id,out var b)&&b.IsFollower?b:null;}
    public static class SainAddonBridge {public static bool IsFollowerCombatEnabled(BotOwner b)=>b.IsFollower&&b.Addon;public static bool IsCombatReady(BotOwner b)=>b.Ready;}
    public static class BattleRecorder {
        public static readonly List<string> Events=new();public static Vector3? LastTarget;
        public static void RecordGrenadeEvent(BotOwner bot,string action,string reason,Vector3? target=null){Events.Add(action+":"+reason);LastTarget=target;}
    }
}
namespace pitTeam.Utils {
    public static class FollowerShotSafety {
        public const float RegularGrenadeUnsafeRadius=8;
        public static bool TrajectoryUnsafe;
        public static bool IsRegularGrenadeTrajectoryUnsafeForThrower(BotOwner bot,BotGrenadeController controller,AIGreanageThrowData data,out string reason){reason="fixtureTrajectoryUnsafe";return TrajectoryUnsafe;}
        __CORE_IMPACT_GEOMETRY__
    }
}
namespace pitTeam.SAINAddon {public static class SAINFollowerCombatHandoff {public static bool AllowsEnemyCombat(BotOwner b)=>b.Contact;}}
public static class GrenadeChecks {
    private static int count;
    private static void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
    private static BotOwner Bot(string id,TestBoss boss,bool addon=true){var b=new BotOwner{ProfileId=id,IsFollower=true,Addon=addon};b.GetPlayer.AIData.BotOwner=b;b.BotFollower.BossToFollow=boss;boss.Followers.Add(b);b.WeaponManager.Grenades=new(){_owner=b};BossPlayers.Bots[id]=b;return b;}
    private static GrenadeThrowDecider Decider(BotOwner b){var component=new BotComponent{BotOwner=b};component.Squad.Members[b.ProfileId]=component;return new(component);}
    private static void Clear(){SainGrenadeThrowBridge.Reset();FollowerGrenadeRuntimeGate.ClearAll();FollowerGrenadeCooldowns.ClearAll();UnityEngine.Time.time=0;pitTeam.pitFireTeam.botGrenades.Value=true;GlobalSettingsClass.Instance.General.BotsUseGrenades=false;GlobalSettingsClass.Instance.General.BotVsBotGrenade=false;pitTeam.Utils.FollowerShotSafety.TrajectoryUnsafe=false;BattleRecorder.Events.Clear();}
    private static void CoreHooks(Harmony h) {
        h.Patch(AccessTools.PropertyGetter(typeof(BotGrenadeController),nameof(BotGrenadeController.HaveGrenade)),postfix:new HarmonyMethod(typeof(pitTeam.Patches.FollowerGrenadeAvailabilityPatch),"PatchPostfix"));
        h.Patch(AccessTools.Method(typeof(BotGrenadeController),nameof(BotGrenadeController.DoThrow)),prefix:new HarmonyMethod(typeof(pitTeam.Patches.FollowerGrenadeCooldownPatch),"PatchPrefix"),postfix:new HarmonyMethod(typeof(pitTeam.Patches.FollowerGrenadeCooldownPatch),"PatchPostfix"));
        h.Patch(AccessTools.Method(typeof(BotGrenadeController),nameof(BotGrenadeController.EndAll)),postfix:new HarmonyMethod(typeof(pitTeam.Patches.FollowerGrenadeThrowFinishPatch),"PatchPostfix"));
        h.Patch(AccessTools.Method(typeof(Player.BaseGrenadeHandsController),nameof(Player.BaseGrenadeHandsController.ThrowGrenade)),prefix:new HarmonyMethod(typeof(pitTeam.Patches.FollowerGrenadeReleasePatch),"PatchPrefix"));
        h.Patch(AccessTools.Method(typeof(Player.BaseGrenadeHandsController),nameof(Player.BaseGrenadeHandsController.CreateAndRegisterGrenade)),postfix:new HarmonyMethod(typeof(pitTeam.Patches.FollowerGrenadeSpawnPatch),"PatchPostfix"));
    }
    private static Player.BaseGrenadeHandsController Hands(BotOwner bot) {
        var hands=new Player.BaseGrenadeHandsController{_player=bot.GetPlayer};bot.GetPlayer.HandsController=hands;return hands;
    }
    private static void Drop(Player.BaseGrenadeHandsController hands) => hands.ThrowGrenade(0,default,default,default,false);
    public static int Main(){
        var core=new Harmony("xyz.pit.fireteam");var addon=new Harmony("xyz.pit.fireteam.sainaddon");CoreHooks(core);SainGrenadeThrowBridge.Apply(addon);
        foreach(bool own in new[]{false,true}) foreach(bool master in new[]{false,true}) foreach(bool capability in new[]{false,true}) foreach(bool versus in new[]{false,true}) {
            Clear();pitTeam.pitFireTeam.botGrenades.Value=own;GlobalSettingsClass.Instance.General.BotsUseGrenades=master;GlobalSettingsClass.Instance.General.BotVsBotGrenade=versus;
            var matrixBot=Bot("matrix",new TestBoss());var matrix=new GrenadeThrowDecider(new BotComponent{BotOwner=matrixBot},master,capability);
            Check(matrix.GetDecision(new Enemy(),out _)==own,"own toggle is authoritative across native switch combinations");
            Check(matrix.FlagsUnchanged&&GlobalSettingsClass.Instance.General.BotsUseGrenades==master&&GlobalSettingsClass.Instance.General.BotVsBotGrenade==versus,"switch combinations never mutate original flags");
            matrixBot.WeaponManager.Grenades.EndAll();
        }
        var enemy=new Enemy();Clear();var boss=new TestBoss();var b=Bot("grunt",boss);var d=Decider(b);var g=b.WeaponManager.Grenades;
        Check(d.GetDecision(enemy,out _)&&g.Throws==1,"own toggle overrides all three native enable flags and Core permission before DoThrow");
        Check(d.FlagsUnchanged&&!GlobalSettingsClass.Instance.General.BotsUseGrenades&&!GlobalSettingsClass.Instance.General.BotVsBotGrenade,"cached and shared SAIN switches are never mutated");
        Check(d.GetDecision(enemy,out _)&&g.Throws==1,"native ongoing throw remains one sequence");
        var teammate=Bot("shooter",boss);var other=Decider(teammate);
        Check(!other.GetDecision(enemy,out _)&&teammate.WeaponManager.Grenades.Throws==0,"squad throw owner blocks other ready addon followers");
        FollowerGrenadeRuntimeGate.MarkThrowReleased(b);g.EndAll();
        Check(!FollowerGrenadeRuntimeGate.IsThrowAllowed(b),"native EndAll closes the owned throw window");
        Check(!d.GetDecision(enemy,out _)&&!other.GetDecision(enemy,out _),"actual release starts individual and group cooldowns");
        UnityEngine.Time.time=5.1f;Check(other.GetDecision(enemy,out _),"group cooldown expiry allows teammate while individual cooldown remains");
        teammate.WeaponManager.Grenades.EndAll();Check(!d.GetDecision(enemy,out _),"original follower retains individual cooldown");
        UnityEngine.Time.time=15.1f;Check(d.GetDecision(enemy,out _),"individual cooldown expiry allows a new native throw");g.EndAll();
        foreach(string veto in new[]{"hands","target","arc","noGrenade","ready","staleThrowData"}) {
            Clear();b=Bot("reject"+veto,new TestBoss());d=Decider(b);g=b.WeaponManager.Grenades;
            if(veto=="hands")d.HandsSafe=false;if(veto=="target")d.TargetSafe=false;if(veto=="arc")d.ArcSafe=false;
            if(veto=="noGrenade")g.grenade=null;if(veto=="ready")g.ReadyToThrow=false;if(veto=="staleThrowData")g.AIGreanageThrowData.Valid=false;
            Check(!d.GetDecision(enemy,out _)&&g.Throws==0,"native veto retained: "+veto);
            Check(!FollowerGrenadeRuntimeGate.IsThrowAllowed(b),"failed attempt releases Core permission: "+veto);
            var ready=Bot("success"+veto,b.BotFollower.BossToFollow as TestBoss);Check(Decider(ready).GetDecision(enemy,out _),"failed planning cannot strand squad ownership: "+veto);ready.WeaponManager.Grenades.EndAll();
        }
        Clear();b=Bot("off",new TestBoss());d=Decider(b);pitTeam.pitFireTeam.botGrenades.Value=false;
        Check(!d.GetDecision(enemy,out _)&&d.Checks==0&&b.WeaponManager.Grenades.Throws==0,"own disabled toggle blocks before native planning");
        foreach(string mode in new[]{"core","ordinary","unready","noContact","deadEnemy","inactiveEnemy"}) {
            Clear();b=Bot(mode,new TestBoss());d=Decider(b);if(mode=="core")b.Addon=false;if(mode=="ordinary")b.IsFollower=false;if(mode=="unready")b.Ready=false;if(mode=="noContact")b.Contact=false;
            var target=new Enemy();if(mode=="deadEnemy")target.EnemyPlayer.HealthController.IsAlive=false;if(mode=="inactiveEnemy")target.Active=false;
            Check(!d.GetDecision(target,out _)&&b.WeaponManager.Grenades.Throws==0,"native or admission gate retained: "+mode);
        }
        Clear();b=Bot("exception",new TestBoss());d=Decider(b);d.ThrowException=true;bool threw=false;
        try{d.GetDecision(enemy,out _);}catch(InvalidOperationException){threw=true;}
        Check(threw&&!FollowerGrenadeRuntimeGate.IsThrowAllowed(b),"exception propagates after permission cleanup");
        Clear();b=Bot("release",new TestBoss());d=Decider(b);d.GetDecision(enemy,out _);SainGrenadeThrowBridge.Release(b);
        Check(!FollowerGrenadeRuntimeGate.IsThrowAllowed(b),"tactic or lifecycle release removes owned permission");
        Clear();b=Bot("shutdown",new TestBoss());d=Decider(b);d.GetDecision(enemy,out _);SainGrenadeThrowBridge.Reset();
        Check(!FollowerGrenadeRuntimeGate.IsThrowAllowed(b),"addon shutdown removes pending permission");b.WeaponManager.Grenades.EndAll();
        Clear();b=Bot("normalNative",new TestBoss());b.IsFollower=false;b.Settings.FileSettings.Core.CanGrenade=true;
        GlobalSettingsClass.Instance.General.BotsUseGrenades=true;GlobalSettingsClass.Instance.General.BotVsBotGrenade=true;
        d=new GrenadeThrowDecider(new BotComponent{BotOwner=b},true,true);Check(d.GetDecision(enemy,out _),"ordinary enabled SAIN bot still throws natively");b.WeaponManager.Grenades.EndAll();
        GlobalSettingsClass.Instance.General.BotVsBotGrenade=false;Check(!d.GetDecision(enemy,out _),"ordinary SAIN bot still obeys its own bot-versus-bot restriction");
        foreach(string near in new[]{"bossAtReport","bossAtDispersedTarget","allyAtImpact","allyCrossingImpact"}) {
            Clear();boss=new TestBoss();b=Bot(near,boss);d=Decider(b);
            if(near=="bossAtReport")boss.Boss.Position=new(27.6f,0,0);
            if(near=="bossAtDispersedTarget"){boss.Boss.Position=new(30,0,0);d.FinalTarget=new(23,0,0);}
            if(near.StartsWith("ally")){
                var ally=Bot("ally",boss);ally.GetPlayer.Position=new(27,0,0);
                if(near=="allyCrossingImpact"){ally.GetPlayer.Position=new(10,0,0);ally.GoToSomePointData.Active=true;ally.GoToSomePointData.Point=new(30,0,0);}
            }
            Check(!d.GetDecision(enemy,out _)&&b.WeaponManager.Grenades.Throws==0,"Core planning impact geometry vetoes "+near+" even without a native human/member entry");
            Check(!FollowerGrenadeRuntimeGate.IsThrowAllowed(b),"planning safety rejection releases throw ownership");
        }
        Clear();boss=new TestBoss();b=Bot("nativeFriendly",boss);d=Decider(b);
        var nativeAlly=Bot("nativeAlly",new TestBoss());nativeAlly.GetPlayer.Position=new(20,0,0);
        d.Bot.Squad.Members[nativeAlly.ProfileId]=new BotComponent{BotOwner=nativeAlly};
        Check(!d.GetDecision(enemy,out _),"addon preserves native-only squad member veto");
        foreach(bool addonTactic in new[]{false,true}) foreach(string veto in new[]{"bossEnters","allyEnters","trajectory","missingData","revoked","staleHands"}) {
            Clear();boss=new TestBoss();b=Bot("final"+veto,boss,addonTactic);g=b.WeaponManager.Grenades;
            if(addonTactic)Check(Decider(b).GetDecision(enemy,out _),"safe addon plan starts before final safety test");
            else {FollowerGrenadeRuntimeGate.EnableExplicitThrow(b);Check(g.DoThrow(),"safe Core sequence starts before final safety test");}
            var hands=Hands(b);
            if(veto=="bossEnters")boss.Boss.Position=new(20,0,0);
            if(veto=="allyEnters")Bot("lateAlly",boss).GetPlayer.Position=new(20,0,0);
            if(veto=="trajectory")pitTeam.Utils.FollowerShotSafety.TrajectoryUnsafe=true;
            if(veto=="missingData")g.AIGreanageThrowData=null;
            if(veto=="revoked")FollowerGrenadeRuntimeGate.EnforceDisabled(b);
            if(veto=="staleHands")b.GetPlayer.HandsController=new object();
            Drop(hands);
            Check(hands.Consumed==0&&hands.Spawned==0,"final veto precedes inventory consumption and spawn: "+addonTactic+"/"+veto);
            Check(!FollowerGrenadeRuntimeGate.HasReleasedThrow(b)&&!BattleRecorder.Events.Contains("release:cooldownStart"),"blocked throw is never recorded as released");
            Check(g.ThrowindNow,"safety veto leaves executing hands operation to finish normally");
            g.EndAll();
            Check(!g.ThrowindNow&&!FollowerGrenadeRuntimeGate.IsThrowAllowed(b)&&FollowerGrenadeCooldowns.CanProceedToThrow(b),"native completion cleans veto without successful-throw cooldown");
        }
        foreach(bool addonTactic in new[]{false,true}) {
            Clear();boss=new TestBoss();b=Bot("successful",boss,addonTactic);g=b.WeaponManager.Grenades;
            if(addonTactic)Decider(b).GetDecision(enemy,out _);else {FollowerGrenadeRuntimeGate.EnableExplicitThrow(b);g.DoThrow();}
            var hands=Hands(b);UnityEngine.Time.time=1;Drop(hands);
            Check(hands.Consumed==1&&hands.Spawned==1&&FollowerGrenadeRuntimeGate.HasReleasedThrow(b),"successful physical spawn marks release for either combat owner");
            Check(BattleRecorder.LastTarget.HasValue&&BattleRecorder.LastTarget.Value.x==20,"release records the actual planned target");
            Drop(hands);Check(hands.Consumed==1&&hands.Spawned==1,"duplicate animation release cannot consume a second grenade");
            UnityEngine.Time.time=4;g.EndAll();
            UnityEngine.Time.time=6.1f;var ready=Bot("nextMember",boss);
            Check(FollowerGrenadeCooldowns.CanProceedToThrow(ready),"group cooldown measured from spawn, not delayed completion");
            UnityEngine.Time.time=16.1f;Check(FollowerGrenadeCooldowns.CanProceedToThrow(b),"individual cooldown measured from spawn, not delayed completion");
        }
        Clear();b=Bot("inventoryFailure",new TestBoss());Decider(b).GetDecision(enemy,out _);
        var failedHands=Hands(b);failedHands.InventorySucceeds=false;Drop(failedHands);
        Check(!FollowerGrenadeRuntimeGate.HasReleasedThrow(b)&&failedHands.Spawned==0,"failed native inventory transaction never starts cooldown");
        b.WeaponManager.Grenades.EndAll();
        Check(FollowerGrenadeCooldowns.CanProceedToThrow(b),"failed inventory completion does not fabricate release");
        Clear();boss=new TestBoss();b=Bot("ordinaryHumanProximity",boss);b.IsFollower=false;boss.Boss.Position=new(20,0,0);
        var ordinaryHands=Hands(b);Drop(ordinaryHands);
        Check(ordinaryHands.Spawned==1&&!FollowerGrenadeRuntimeGate.HasReleasedThrow(b),"ordinary bot release and cooldown ownership remain native");
        var playerHands=new Player.BaseGrenadeHandsController{_player=new Player()};playerHands._player.HandsController=playerHands;Drop(playerHands);
        Check(playerHands.Spawned==1,"human grenade throwing remains unchanged");
        Clear();b=Bot("hotOff",new TestBoss());d=Decider(b);pitTeam.pitFireTeam.botGrenades.Value=false;
        for(int i=0;i<1000;i++)d.GetDecision(enemy,out _);
        Check(d.Checks==0&&b.WeaponManager.Grenades.NativeCalls==0,"disabled polling never executes native planning or throws");
        addon.UnpatchSelf();Check(!d.GetDecision(enemy,out _),"unpatch restores native enable switches");
        Console.WriteLine($"Passed {count} grenade integration checks with native SAIN decision source and production Core gates.");return 0;
    }
}

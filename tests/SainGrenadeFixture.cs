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

namespace UnityEngine {
    public static class Time { public static float time; }
    public static class Random {public static float Range(float a,float b)=>a;}
    public static class Mathf {public static float CeilToInt(float v)=>(float)Math.Ceiling(v);}
}
namespace Comfort.Common {}
namespace EFT.InventoryLogic {public class ThrowWeap {}}
namespace EFT {
    public enum EPhraseTrigger {NeedFrag}
    public class Health {public bool IsAlive=true;}
    public class Player {public string ProfileId;public Health HealthController=new();}
    public interface IBossToFollow {Player Player();List<BotOwner> Followers {get;}}
    public class TestBoss : IBossToFollow {public Player Boss=new(){ProfileId="player"};public List<BotOwner> Followers{get;}=new();public Player Player()=>Boss;}
    public class BotFollower {public IBossToFollow BossToFollow;}
    public class BotsGroup {public string Id;}
    public class CoreSettings {public bool CanGrenade;}
    public class FileSettings {public CoreSettings Core=new();}
    public class BotSettings {public FileSettings FileSettings=new();}
    public class WeaponManager {public BotGrenadeController Grenades;}
    public class BotOwner {
        public string ProfileId;public bool IsFollower,Addon,Ready=true,Contact=true;
        public BotSettings Settings=new();public BotFollower BotFollower=new();public BotsGroup BotsGroup;
        public WeaponManager WeaponManager=new();public void SetPose(float v) {}
    }
    public class ThrowData {public bool Valid=true;public bool IsUpToDate()=>Valid;}
    public class BotGrenadeController {
        public BotOwner _owner;public EFT.InventoryLogic.ThrowWeap grenade=new();
        public bool ThrowindNow,ReadyToThrow=true;public int Throws,NativeCalls;
        public ThrowData AIGreanageThrowData=new();
        public bool HaveGrenade { [MethodImpl(MethodImplOptions.NoInlining)] get => grenade!=null && GlobalSettingsClass.Instance.General.BotsUseGrenades && _owner.Settings.FileSettings.Core.CanGrenade; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool DoThrow(){NativeCalls++;if(ThrowindNow||!ReadyToThrow)return false;Throws++;ThrowindNow=true;return true;}
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void EndAll(EFT.InventoryLogic.ThrowWeap grenade=null){ThrowindNow=false;}
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
    public class BotComponent {public BotOwner BotOwner;public Talk Talk=new();}
}
namespace SAIN.SAINComponent.Classes.WeaponFunction {
    public class GrenadeThrowDecider {
        public BotComponent Bot;public BotOwner BotOwner=>Bot.BotOwner;public GlobalSettingsClass GlobalSettings=>GlobalSettingsClass.Instance;
        private bool _grenadesEnabled,_canThrowGrenades;private readonly bool initialEnabled,initialCapable;
        private float _nextPossibleAttempt,_throwGrenadeFreq=5,_throwGrenadeFreqMax=10,_nextSayNeedGrenadeTime,_sayNeedGrenadeFreq=10,_sayNeedGrenadeChance=5;
        public bool HandsSafe=true,TargetSafe=true,ArcSafe=true;public bool ThrowException;public int Checks;
        public GrenadeThrowDecider(BotComponent bot,bool enabled=false,bool capable=false){Bot=bot;_grenadesEnabled=initialEnabled=enabled;_canThrowGrenades=initialCapable=capable;}
        public bool FlagsUnchanged=>_grenadesEnabled==initialEnabled&&_canThrowGrenades==initialCapable;
        private bool CheckCanThrow(out string reason){Checks++;if(ThrowException)throw new InvalidOperationException("fixture planner error");reason="hands";return HandsSafe;}
        private bool CanThrowAtEnemy(Enemy e,out string reason){reason="targetSafety";return TargetSafe;}
        private bool FindThrowTarget(Enemy e)=>ArcSafe;
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
}
namespace pitTeam.Utils {}
namespace pitTeam.SAINAddon {public static class SAINFollowerCombatHandoff {public static bool AllowsEnemyCombat(BotOwner b)=>b.Contact;}}
public static class GrenadeChecks {
    private static int count;
    private static void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
    private static BotOwner Bot(string id,TestBoss boss,bool addon=true){var b=new BotOwner{ProfileId=id,IsFollower=true,Addon=addon};b.BotFollower.BossToFollow=boss;boss.Followers.Add(b);b.WeaponManager.Grenades=new(){_owner=b};BossPlayers.Bots[id]=b;return b;}
    private static GrenadeThrowDecider Decider(BotOwner b)=>new(new BotComponent{BotOwner=b});
    private static void Clear(){SainGrenadeThrowBridge.Reset();FollowerGrenadeRuntimeGate.ClearAll();FollowerGrenadeCooldowns.ClearAll();UnityEngine.Time.time=0;pitTeam.pitFireTeam.botGrenades.Value=true;GlobalSettingsClass.Instance.General.BotsUseGrenades=false;GlobalSettingsClass.Instance.General.BotVsBotGrenade=false;}
    private static void CoreHooks(Harmony h) {
        h.Patch(AccessTools.PropertyGetter(typeof(BotGrenadeController),nameof(BotGrenadeController.HaveGrenade)),postfix:new HarmonyMethod(typeof(pitTeam.Patches.FollowerGrenadeAvailabilityPatch),"PatchPostfix"));
        h.Patch(AccessTools.Method(typeof(BotGrenadeController),nameof(BotGrenadeController.DoThrow)),prefix:new HarmonyMethod(typeof(pitTeam.Patches.FollowerGrenadeCooldownPatch),"PatchPrefix"),postfix:new HarmonyMethod(typeof(pitTeam.Patches.FollowerGrenadeCooldownPatch),"PatchPostfix"));
        h.Patch(AccessTools.Method(typeof(BotGrenadeController),nameof(BotGrenadeController.EndAll)),postfix:new HarmonyMethod(typeof(pitTeam.Patches.FollowerGrenadeThrowFinishPatch),"PatchPostfix"));
    }
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
        Clear();b=Bot("hotOff",new TestBoss());d=Decider(b);pitTeam.pitFireTeam.botGrenades.Value=false;
        for(int i=0;i<1000;i++)d.GetDecision(enemy,out _);
        Check(d.Checks==0&&b.WeaponManager.Grenades.NativeCalls==0,"disabled polling never executes native planning or throws");
        addon.UnpatchSelf();Check(!d.GetDecision(enemy,out _),"unpatch restores native enable switches");
        Console.WriteLine($"Passed {count} grenade integration checks with native SAIN decision source and production Core gates.");return 0;
    }
}

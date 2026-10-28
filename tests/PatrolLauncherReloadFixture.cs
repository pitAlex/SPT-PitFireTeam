#nullable enable annotations
#nullable disable warnings
#pragma warning disable CS0649
using System;
using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;
using BotWeaponManager = Manager;
public enum EquipmentSlot { FirstPrimaryWeapon, SecondPrimaryWeapon, Holster }
public enum FollowerCommandType { None, Regroup }
public class EnemyInfo { public bool Alive=true; }
public class Memory { public bool IsUnderFire; public EnemyInfo GoalEnemy; }
public class Work { public bool Have2Do,HaveWork; }
public class Medical { public Work FirstAid=new(),SurgicalKit=new(); public bool Using; }
public class Danger { public bool Active; public bool ShallRunAway()=>Active; }
public class Grenades { public bool ThrowindNow,ReadyToThrow; }
public class Settings { public FileSettings FileSettings=new(); }
public class FileSettings { public BossSettings Boss=new(); }
public class BossSettings { public float PERCENT_BULLET_TO_RELOAD=0.6f; }
public class Player { public class FirearmController { public bool HandsReload; public bool IsInReloadOperation()=>HandsReload; public bool CanStartReload()=>true; } }
namespace EFT.InventoryLogic {
    public class Magazine { public int MaxCount=30,Count=30; }
    public class Weapon {
        public enum EReloadMode { ExternalMagazine, InternalMagazine }
        public string Id=Guid.NewGuid().ToString(); public bool Launcher,SingleUse,Looted; public int Loaded,Capacity=30;
        public EReloadMode ReloadMode; public Magazine GetCurrentMagazine()=>new Magazine{Count=Loaded,MaxCount=Capacity};
        public int GetCurrentMagazineCount()=>Loaded; public int GetMaxMagazineCount()=>Capacity;
    }
}
public class BotWeaponSelector {
    public EquipmentSlot LastEquipmentSlot=EquipmentSlot.FirstPrimaryWeapon; public EquipmentSlot? Pending;
    public bool IsChanging,IsWeaponReady=true;
}
public class Reload {
    public bool Reloading; public int RifleStarts;
    public bool TryReload(){RifleStarts++;Reloading=true;return true;}
}
public class Manager {
    public BotOwner Owner; public Reload Reload=new(); public BotWeaponSelector Selector=new();
    public Player.FirearmController ShootController=new(); public Grenades Grenades=new();public bool IsWeaponReady=true;
    public Weapon CurrentWeapon=>Owner.Slots.TryGetValue(Selector.LastEquipmentSlot,out var weapon)?weapon:null;
}
public class BotOwner {
    public BotOwner(){WeaponManager.Owner=this;}
    public Manager WeaponManager=new();public Dictionary<EquipmentSlot,Weapon> Slots=new();public Memory Memory=new();
    public bool IsDead,VisibleEnemy,RecentHit;public Danger BewareGrenade=new(),BewareBTR=new(); public Medical Medecine=new();public Settings Settings=new();
    public int GrenadesAvailable=12,LauncherStarts,Switches,Fallbacks;public FollowerData Follower=new();
}
public class FollowerData {
    public bool IsBackpackInspectionActive;public FollowerCommandType Command;
    public bool TryPeekActiveCommand(out FollowerCommandType command,out float a,out float b){command=Command;a=b=0;return command!=FollowerCommandType.None;}
}
public class BotFollowerPlayer { public static bool IsEnemyInfoAlive(EnemyInfo enemy)=>enemy?.Alive==true; }
public class BossPlayers {public static BossPlayers Instance=new();public FollowerData GetFollower(BotOwner bot)=>bot.Follower;}
public static class Time {public static float time;}
namespace Utils {
    public static class FollowerMedical {public static bool IsUsingMedical(BotOwner bot)=>bot.Medecine.Using;}
    public static class FollowerRecovery {public static void CheckReloadTimeout(BotOwner bot){} }
}
public static class InteractableObjects {
    public static bool IsLootedWeapon(BotOwner bot,Weapon weapon)=>weapon.Looted;
    public static bool IsApprovedLootedWeaponMagazine(BotOwner bot,Weapon weapon,Magazine magazine)=>false;
}
public static class FollowerCombatCommon {
    public static bool IsGrenadeLauncherWeapon(Weapon weapon)=>weapon?.Launcher==true;
    public static bool IsSingleUseLauncherWeapon(Weapon weapon)=>weapon.SingleUse;
    public static bool WasHitRecently(BotOwner bot,float seconds)=>bot.RecentHit;
    public static bool CanReloadGrenadeLauncherFromLooseAmmo(BotOwner bot,Weapon weapon)=>weapon.Launcher&&!weapon.SingleUse&&weapon.Loaded<weapon.Capacity&&bot.GrenadesAvailable>0;
    public static bool TryStartActiveGrenadeLauncherLooseAmmoReload(BotOwner bot,Weapon weapon,out string reason){reason="";bot.LauncherStarts++;bot.WeaponManager.Reload.Reloading=true;return true;}
    public static bool IsSupportGrenadeLauncherSelectedOrActive(BotOwner bot)=>bot.WeaponManager.Selector.LastEquipmentSlot==EquipmentSlot.SecondPrimaryWeapon && bot.WeaponManager.CurrentWeapon?.Launcher==true;
    public static bool TrySwitchSelectedGrenadeLauncherToPrimaryForOpportunity(BotOwner bot,EnemyInfo goalEnemy,string reason,bool tacticalIntent,out string waitReason){waitReason="";bot.Fallbacks++;bot.WeaponManager.Selector.Pending=EquipmentSlot.FirstPrimaryWeapon;return true;}
}
public static class FollowerWeaponSwitchPolicyRuntime {public static bool ShouldSuppressHoldLingerReload(BotOwner bot)=>false;}
public static class FollowerOutOfCombatReloadPolicy {
    public static int SupplyRevision(BotOwner bot)=>0;public static bool HasRefillWork(BotOwner bot)=>false;
    public static bool TryFillCarriedWeaponMagazines(BotOwner bot)=>false;
    public static bool CanTopOffWeapon(BotOwner bot,Weapon weapon)=>!weapon.Launcher&&weapon.Loaded<weapon.Capacity;
    public static bool HasBetterMagazine(BotOwner bot,Weapon weapon)=>!weapon.Launcher&&weapon.Loaded<weapon.Capacity;
    public static Magazine FindBetterMagazine(BotOwner bot,Weapon weapon)=>HasBetterMagazine(bot,weapon)?new Magazine{Count=weapon.Capacity}:null;
}
public static class BattleRecorder {
    public static void RecordAmmoRefill(BotOwner bot,string action,string reason,Func<object> context){}
    public static void RecordGrenadeEvent(BotOwner bot,string action,string reason){}
}
public class PatrolHarness {
    private const float OutOfCombatReloadCheckInterval=3,OutOfCombatReloadSlotCooldown=2,OutOfCombatReloadFullCycleCooldown=30,OutOfCombatReloadActionCooldown=5;
    private static readonly EquipmentSlot[] ReloadSlotOrder={EquipmentSlot.FirstPrimaryWeapon,EquipmentSlot.SecondPrimaryWeapon,EquipmentSlot.Holster};
    public BotOwner BotOwner=new();private FollowerData followerData;
    private int _supplyRevision;private bool triedFillMagazines,reloadingInProgress;
    private float nextReloadCheckAt,nextMagazineFillCheckAt;
    private string reloadingWeaponId;private EquipmentSlot? forcedTopOffSlot,returnAfterTopOffSlot,patrolReloadFinalSlot;
    private readonly HashSet<EquipmentSlot> reloadSlotsTried=new();private readonly HashSet<string> reloadWeaponsProcessed=new();
    public bool GiveUp,Cooling;
    public Weapon Rifle=>BotOwner.Slots[EquipmentSlot.FirstPrimaryWeapon]; public Weapon Launcher=>BotOwner.Slots[EquipmentSlot.SecondPrimaryWeapon];
    public bool InProgress=>reloadingInProgress;public bool LauncherProcessed=>IsReloadWeaponProcessed(Launcher);
    public bool Deferred=>!forcedTopOffSlot.HasValue&&!LauncherProcessed;
    public void MarkPrimary()=>MarkReloadWeaponProcessed(Rifle.Id);
    public void Tick(){if(!TryReturnSelectedLauncherToPrimaryAfterCombat())TryHandleOutOfCombatReloadInternal();}
    public bool SelectLauncher(out bool terminal)=>TrySelectWeaponToTopOff(BotOwner.WeaponManager.Selector,EquipmentSlot.SecondPrimaryWeapon,out terminal);
    public void SettleDraw(){var s=BotOwner.WeaponManager.Selector;if(s.Pending.HasValue){s.LastEquipmentSlot=s.Pending.Value;s.Pending=null;}s.IsChanging=false;}
    private Weapon GetWeaponInSlot(EquipmentSlot slot)=>BotOwner.Slots.TryGetValue(slot,out var weapon)?weapon:null;
    private bool HasVisibleKnownEnemy()=>BotOwner.VisibleEnemy;
    private bool TryHandleSkippedReloadStart()=>false;
    private void ResetReloadState(){reloadWeaponsProcessed.Clear();reloadSlotsTried.Clear();triedFillMagazines=reloadingInProgress=false;forcedTopOffSlot=returnAfterTopOffSlot=null;}
    private void CapturePatrolReloadFinalSlot(BotWeaponSelector selector){patrolReloadFinalSlot??=EquipmentSlot.FirstPrimaryWeapon;}
    private bool TryConfirmForcedTopOffSwitch(BotWeaponSelector selector)=>!forcedTopOffSlot.HasValue||IsPatrolWeaponSlotConfirmed(selector,forcedTopOffSlot.Value);
    private EquipmentSlot NormalizePatrolReturnSlot(BotWeaponSelector selector,EquipmentSlot slot)=>slot;
    private bool IsPatrolWeaponSlotConfirmed(BotWeaponSelector selector,EquipmentSlot slot)=>selector.LastEquipmentSlot==slot&&!selector.IsChanging;
    private bool RequestPatrolWeaponSlot(BotWeaponSelector selector,EquipmentSlot slot){selector.Pending=slot;selector.IsChanging=true;BotOwner.Switches++;return true;}
    private bool TryRestorePatrolReloadFinalSlot(BotWeaponSelector selector){returnAfterTopOffSlot=patrolReloadFinalSlot;return TryCompleteReturnAfterTopOffSwitch(selector);}
    private bool IsOutOfCombatReloadGiveUpActive(EquipmentSlot slot,Weapon weapon)=>weapon.Launcher&&GiveUp;
    private bool IsReloadSlotRetryCoolingDown(EquipmentSlot slot)=>slot==EquipmentSlot.SecondPrimaryWeapon&&Cooling;
    private bool IsOutOfCombatReloadSwitchBudgetSpent(EquipmentSlot slot,Weapon weapon)=>false;
    private void RecordOutOfCombatReloadSwitchAttempt(EquipmentSlot slot,Weapon weapon){}
    private void RecordOutOfCombatReloadFailure(EquipmentSlot slot,Weapon weapon,string reason){}
    private void ClearOutOfCombatReloadAttemptBudget(string id){}
    private void MarkReloadSlotFailed(EquipmentSlot slot){reloadSlotsTried.Add(slot);}
    private string GetReloadWeaponId(EquipmentSlot slot)=>GetWeaponInSlot(slot)?.Id;
    private bool CanReloadLootedFirstPrimaryLauncher(EquipmentSlot slot,Weapon weapon)=>false;
    private bool CanReloadLootedWeaponPackage(EquipmentSlot slot,Weapon weapon)=>false;
    private void RecordPatrolLauncherFallback(bool requested,string reason){}
    /* PRODUCTION METHODS */
}
public static class PatrolLauncherReloadChecks {
    private static int checks;
    private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    private static PatrolHarness Fresh(){Time.time=0;var h=new PatrolHarness();h.BotOwner.Slots[EquipmentSlot.FirstPrimaryWeapon]=new Weapon();h.BotOwner.Slots[EquipmentSlot.SecondPrimaryWeapon]=new Weapon{Launcher=true,Capacity=6,ReloadMode=Weapon.EReloadMode.InternalMagazine};return h;}
    private static void Tick(PatrolHarness h){Time.time+=4;h.Tick();}
    public static void Run(){
        var h=Fresh();Check(!h.SelectLauncher(out bool terminal)&&!terminal&&h.Deferred,"unfinished rifle defers launcher without consuming it");
        Tick(h);Tick(h);Check(h.BotOwner.WeaponManager.Reload.RifleStarts==1&&h.BotOwner.LauncherStarts==0&&h.BotOwner.Switches==0,"primary reload owns maintenance before launcher");
        Tick(h);Check(h.BotOwner.Switches==0&&h.InProgress,"launcher cannot compete with active primary reload");
        h.Rifle.Loaded=30;h.BotOwner.WeaponManager.Reload.Reloading=false;h.BotOwner.WeaponManager.ShootController.HandsReload=true;
        Tick(h);Check(h.BotOwner.Switches==0&&h.InProgress,"primary hands completion remains required");
        h.BotOwner.WeaponManager.ShootController.HandsReload=false;Tick(h);Tick(h);
        Check(h.BotOwner.WeaponManager.Selector.Pending==EquipmentSlot.SecondPrimaryWeapon,"launcher is queued only after primary maintenance settles");
        h.SettleDraw();Tick(h);Check(h.BotOwner.LauncherStarts==1&&h.InProgress&&h.BotOwner.Fallbacks==0,"patrol reload queue owns launcher draw and native reload without combat fallback churn");
        Tick(h);Check(h.BotOwner.LauncherStarts==1&&h.BotOwner.WeaponManager.Selector.Pending==null,"active launcher reload is not restarted or switched away");
        h.Launcher.Loaded=6;h.BotOwner.WeaponManager.Reload.Reloading=false;h.BotOwner.WeaponManager.ShootController.HandsReload=true;
        Tick(h);Check(!h.LauncherProcessed&&h.BotOwner.WeaponManager.Selector.Pending==null,"launcher native hands completion precedes rifle return");
        h.BotOwner.WeaponManager.ShootController.HandsReload=false;Tick(h);
        Check(h.LauncherProcessed&&h.BotOwner.WeaponManager.Selector.Pending==EquipmentSlot.FirstPrimaryWeapon,"completed launcher restores the prior rifle through the same queue");
        h.SettleDraw();Tick(h);Tick(h);Check(h.BotOwner.LauncherStarts==1&&h.BotOwner.Fallbacks==0,"completed maintenance cannot loop into another launcher draw");
        var hazards=new Dictionary<string,Action<PatrolHarness>>{
            ["enemy"]=x=>x.BotOwner.Memory.GoalEnemy=new EnemyInfo(),["visible"]=x=>x.BotOwner.VisibleEnemy=true,
            ["underFire"]=x=>x.BotOwner.Memory.IsUnderFire=true,["recentHit"]=x=>x.BotOwner.RecentHit=true,
            ["grenadeDanger"]=x=>x.BotOwner.BewareGrenade.Active=true,["BTR"]=x=>x.BotOwner.BewareBTR.Active=true,
            ["throwing"]=x=>x.BotOwner.WeaponManager.Grenades.ThrowindNow=true,["readyToThrow"]=x=>x.BotOwner.WeaponManager.Grenades.ReadyToThrow=true,
            ["medicine"]=x=>x.BotOwner.Medecine.Using=true,["firstAid"]=x=>x.BotOwner.Medecine.FirstAid.Have2Do=true,
            ["surgery"]=x=>x.BotOwner.Medecine.SurgicalKit.HaveWork=true,["command"]=x=>x.BotOwner.Follower.Command=FollowerCommandType.Regroup,
            ["inspection"]=x=>x.BotOwner.Follower.IsBackpackInspectionActive=true,["dead"]=x=>x.BotOwner.IsDead=true};
        foreach(var hazard in hazards){h=Fresh();h.MarkPrimary();hazard.Value(h);Check(!h.SelectLauncher(out terminal)&&!terminal&&h.Deferred&&h.BotOwner.Switches==0,"unsafe launcher work defers without budget/selection churn: "+hazard.Key);}
        foreach(string excluded in new[]{"noAmmo","full","singleUse","looted","giveUp","cooldown"}){
            h=Fresh();h.MarkPrimary();if(excluded=="noAmmo")h.BotOwner.GrenadesAvailable=0;if(excluded=="full")h.Launcher.Loaded=6;
            if(excluded=="singleUse")h.Launcher.SingleUse=true;if(excluded=="looted")h.Launcher.Looted=true;if(excluded=="giveUp")h.GiveUp=true;if(excluded=="cooldown")h.Cooling=true;
            Check(!h.SelectLauncher(out terminal)&&h.BotOwner.Switches==0&&h.BotOwner.LauncherStarts==0,"excluded launcher cannot start maintenance: "+excluded);
        }
        h=Fresh();h.MarkPrimary();Check(h.SelectLauncher(out terminal),"safe missing cylinder rounds permit launcher draw");h.SettleDraw();h.BotOwner.Memory.IsUnderFire=true;h.Tick();
        Check(h.BotOwner.LauncherStarts==0&&h.BotOwner.WeaponManager.Selector.Pending==EquipmentSlot.FirstPrimaryWeapon,"safety lost during draw restores rifle without starting reload");
        Console.WriteLine($"PASS: {checks} production patrol launcher reload/primary-priority/safety/ownership checks; native hands are controlled stand-ins.");
    }
}

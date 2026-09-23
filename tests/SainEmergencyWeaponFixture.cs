using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using pitTeam.SAINAddon;
using SAIN.Components;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.Decision;
using SAIN.SAINComponent.Classes.EnemyClasses;

namespace UnityEngine { public static class Time { public static float time = 100f; } }
namespace EFT.InventoryLogic {
    public enum EquipmentSlot { FirstPrimaryWeapon, SecondPrimaryWeapon, Holster }
    public class Weapon {
        public string Id; public int Rounds; public bool Launcher;
        public enum EMalfunctionState { None, Misfire }
        public class MalfunctionState { public EMalfunctionState State; }
        public MalfunctionState MalfState = new();
    }
    public class Slot { public Weapon ContainedItem; }
    public class Equipment {
        public Dictionary<EquipmentSlot,Slot> Slots = new();
        public Slot GetSlot(EquipmentSlot slot) => Slots.TryGetValue(slot,out var value) ? value : null;
    }
}
namespace EFT {
    public class Health { public bool IsAlive=true; }
    public class Player {
        public class FirearmController {
            public Weapon Item; public bool Reloading, Interacting;
            public bool IsInReloadOperation()=>Reloading;
            public bool IsInInteraction()=>Interacting;
        }
        public FirearmController HandsController = new();
        public Health HealthController = new();
        public InventoryController InventoryController = new();
    }
    public class InventoryController { public Inventory Inventory = new(); }
    public class Inventory { public Equipment Equipment = new(); }
    public class BotOwner {
        public bool Grunt=true, Ready=true, Admitted=true, Medical;
        public Player GetPlayer = new(); public BotWeaponManager WeaponManager = new();
    }
}
public class BotReload { public bool Reloading; }
public class BotWeaponManager {
    public BotReload Reload = new(); public BotWeaponSelector Selector = new();
    public bool IsReady=true, IsMelee; public Weapon CurrentWeapon;
}
public class BotWeaponSelector {
    public bool IsWeaponReady=true, IsChanging, CanChangeToSecondWeapons=true, _canChangeToSupportWeapons=true, Accept=true;
    public EquipmentSlot LastEquipmentSlot=EquipmentSlot.FirstPrimaryWeapon, Requested;
    public int Requests;
    public bool TryChangeToSlot(EquipmentSlot slot,bool main) {
        if(main) throw new Exception("must preserve native support selection");
        Requests++;Requested=slot;if(Accept)IsWeaponReady=false;return Accept;
    }
}
namespace SAIN.Preset.Shared.Enums {
    public enum ESelfActionType { None, Reload, FirstAid, Surgery, Stims }
    public enum ECombatDecision { StandAndShoot, ThrowGrenade, AvoidGrenade, MeleeAttack, DogFight }
}
namespace SAIN.Components {
    public class Decision { public ESelfActionType CurrentSelfDecision; public ECombatDecision CurrentCombatDecision; }
    public class BotComponent { public BotOwner BotOwner=new(); public Decision Decision=new(); }
}
namespace SAIN.SAINComponent.Classes.EnemyClasses {
    public class Enemy {
        public bool Active=true, EnemyKnown=true, IsVisible=true, CanShoot=true;
        public float RealDistance=8; public Player EnemyPlayer=new();
        public static bool IsEnemyActive(Enemy e)=>e.Active;
    }
}
namespace SAIN.SAINComponent.Classes.Decision {
    public class SelfActionDecisionClass {
        public BotComponent Bot=new(); public int NativeCalls;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool GetDecision(out ESelfActionType decision,Enemy enemy) { NativeCalls++; decision=ESelfActionType.Reload;return true; }
    }
}
namespace pitTeam {
    public class pitFireTeam {
        public static bool IsDebugBuild=true;
        public static bool UseSainFollowerCombat(BotOwner b)=>b.Ready;
    }
}
namespace pitTeam.Modules {
    public static class SainAddonBridge {
        public static bool IsSainManSelected(BotOwner b)=>b.Grunt;
        public static bool IsUsingMedical(BotOwner b)=>b.Medical;
    }
    public static class Logger { public static void LogError(string s)=>throw new Exception(s); }
    public static class SainCombatRecorderBridge { public static int Events; public static void RecordEvent(BotOwner b,string kind,object data)=>Events++; }
}
namespace pitTeam.BigBrain {
    public static class FollowerCombatCommon {
        public static int CountLoadedRounds(Weapon w)=>w?.Rounds??0;
        internal static bool IsGrenadeLauncherWeapon(Weapon w)=>w?.Launcher==true;
    }
}
namespace pitTeam.SAINAddon {
    public static class SAINFollowerCombatHandoff { public static bool AllowsEnemyCombat(BotOwner b)=>b.Admitted; }
}
public static class Checks {
    static int count;
    static void Check(bool value,string name) { if(!value)throw new Exception(name);count++; }
    static SelfActionDecisionClass New() {
        var d=new SelfActionDecisionClass();var b=d.Bot.BotOwner;
        var main=new Weapon{Id="main"};b.WeaponManager.CurrentWeapon=main;b.GetPlayer.HandsController.Item=main;
        b.GetPlayer.InventoryController.Inventory.Equipment.Slots[EquipmentSlot.SecondPrimaryWeapon]=new Slot{ContainedItem=new Weapon{Id="second",Rounds=10}};
        b.GetPlayer.InventoryController.Inventory.Equipment.Slots[EquipmentSlot.Holster]=new Slot{ContainedItem=new Weapon{Id="pistol",Rounds=6}};
        return d;
    }
    static Weapon Slot(SelfActionDecisionClass d,EquipmentSlot s)=>d.Bot.BotOwner.GetPlayer.InventoryController.Inventory.Equipment.GetSlot(s).ContainedItem;
    static BotWeaponSelector Selector(SelfActionDecisionClass d)=>d.Bot.BotOwner.WeaponManager.Selector;
    static void Native(Action<SelfActionDecisionClass,Enemy> configure,string name) {
        var d=New();var e=new Enemy();configure(d,e);
        Check(d.GetDecision(out var result,e)&&result==ESelfActionType.Reload&&d.NativeCalls==1&&Selector(d).Requests==0,name);
    }
    public static void Main() {
        var h=new Harmony("pitFireTeam.test.emergency");SainEmergencyWeaponBridge.Apply(h);
        try {
            var d=New();var e=new Enemy();var b=d.Bot.BotOwner;
            Check(!d.GetDecision(out var result,e)&&result==ESelfActionType.None&&d.NativeCalls==0,"accepted draw does not run reload/heal provider");
            Check(Selector(d).Requests==1&&Selector(d).Requested==EquipmentSlot.SecondPrimaryWeapon,"loaded second primary preferred");
            Check(!d.GetDecision(out result,e)&&Selector(d).Requests==1&&d.NativeCalls==0,"pending asynchronous draw does not repeat request");
            Selector(d).IsWeaponReady=true;Selector(d).LastEquipmentSlot=EquipmentSlot.SecondPrimaryWeapon;
            Check(!d.GetDecision(out result,e),"selector-only completion cannot prove hands ready");
            b.GetPlayer.HandsController.Item=Slot(d,EquipmentSlot.SecondPrimaryWeapon);b.WeaponManager.CurrentWeapon=b.GetPlayer.HandsController.Item;
            Check(d.GetDecision(out result,e)&&d.NativeCalls==1,"actual hands readiness restores native provider");
            Native((x,y)=>x.Bot.BotOwner.Grunt=false,"Shooter and ordinary bots untouched");
            Native((x,y)=>x.Bot.BotOwner.Ready=false,"unready addon untouched");
            Native((x,y)=>x.Bot.BotOwner.Admitted=false,"unadmitted enemy cannot cause switch");
            Native((x,y)=>y.EnemyKnown=false,"unknown enemy");
            Native((x,y)=>y.Active=false,"inactive enemy");
            Native((x,y)=>y.EnemyPlayer.HealthController.IsAlive=false,"dead enemy");
            Native((x,y)=>y.IsVisible=false,"hidden enemy reloads normally");
            Native((x,y)=>y.CanShoot=false,"blocked enemy reloads normally");
            Native((x,y)=>y.RealDistance=10.01f,"outside emergency distance");
            Native((x,y)=>y.RealDistance=float.NaN,"invalid distance");
            Native((x,y)=>y.RealDistance=0,"zero distance");
            Native((x,y)=>x.Bot.BotOwner.WeaponManager.CurrentWeapon.Rounds=1,"one loaded round preserves current gun");
            Native((x,y)=>x.Bot.BotOwner.WeaponManager.CurrentWeapon.Launcher=true,"launcher primary untouched");
            Native((x,y)=>x.Bot.BotOwner.WeaponManager.Reload.Reloading=true,"active reload preserved");
            Native((x,y)=>x.Bot.BotOwner.Medical=true,"active medicine preserved");
            foreach(var self in new[]{ESelfActionType.Reload,ESelfActionType.FirstAid,ESelfActionType.Surgery,ESelfActionType.Stims})
                Native((x,y)=>x.Bot.Decision.CurrentSelfDecision=self,"existing self-action preserved "+self);
            foreach(var combat in new[]{ECombatDecision.ThrowGrenade,ECombatDecision.AvoidGrenade,ECombatDecision.MeleeAttack})
                Native((x,y)=>x.Bot.Decision.CurrentCombatDecision=combat,"urgent action preserved "+combat);
            Native((x,y)=>Selector(x).IsChanging=true,"unrelated transition");
            Native((x,y)=>Selector(x).IsWeaponReady=false,"unready selector");
            Native((x,y)=>x.Bot.BotOwner.WeaponManager.IsReady=false,"unready manager");
            Native((x,y)=>x.Bot.BotOwner.WeaponManager.IsMelee=true,"melee hands");
            Native((x,y)=>x.Bot.BotOwner.GetPlayer.HandsController.Interacting=true,"hands interaction");
            Native((x,y)=>x.Bot.BotOwner.GetPlayer.HandsController.Reloading=true,"hands reload");
            Native((x,y)=>x.Bot.BotOwner.GetPlayer.HandsController.Item=new Weapon{Id="other"},"mismatched actual hands");
            Native((x,y)=>Selector(x).LastEquipmentSlot=EquipmentSlot.Holster,"no pistol to secondary chain");
            Native((x,y)=>Selector(x).LastEquipmentSlot=EquipmentSlot.SecondPrimaryWeapon,"no secondary to pistol chain");
            Native((x,y)=>{Slot(x,EquipmentSlot.SecondPrimaryWeapon).Rounds=0;Slot(x,EquipmentSlot.Holster).Rounds=0;},"empty backups");
            Native((x,y)=>{Slot(x,EquipmentSlot.SecondPrimaryWeapon).Launcher=true;Slot(x,EquipmentSlot.Holster).Launcher=true;},"launcher backups excluded");
            Native((x,y)=>{Slot(x,EquipmentSlot.SecondPrimaryWeapon).MalfState.State=Weapon.EMalfunctionState.Misfire;Slot(x,EquipmentSlot.Holster).MalfState.State=Weapon.EMalfunctionState.Misfire;},"malfunctioning backups excluded");
            Native((x,y)=>{Selector(x).CanChangeToSecondWeapons=false;Selector(x)._canChangeToSupportWeapons=false;},"native selection capability preserved");
            d=New();Slot(d,EquipmentSlot.SecondPrimaryWeapon).Rounds=0;
            Check(!d.GetDecision(out result,e)&&Selector(d).Requested==EquipmentSlot.Holster,"loaded pistol fallback");
            d=New();Slot(d,EquipmentSlot.SecondPrimaryWeapon).Launcher=true;
            Check(!d.GetDecision(out result,e)&&Selector(d).Requested==EquipmentSlot.Holster,"launcher skipped for pistol");
            d=New();e.RealDistance=10;
            Check(!d.GetDecision(out result,e),"ten-metre boundary included");
            UnityEngine.Time.time+=3;
            Check(d.GetDecision(out result,e)&&d.NativeCalls==1,"draw timeout restores provider");
            Selector(d).IsWeaponReady=true;
            Check(d.GetDecision(out result,e)&&Selector(d).Requests==1,"accepted attempt cooldown prevents repeat after timeout");
            UnityEngine.Time.time+=22;
            Check(!d.GetDecision(out result,e)&&Selector(d).Requests==2,"retry only after full cooldown");
            d=New();Selector(d).Accept=false;
            Check(d.GetDecision(out result,e)&&d.NativeCalls==1&&Selector(d).Requests==1,"rejected switch immediately falls back to native");
            Check(d.GetDecision(out result,e)&&Selector(d).Requests==1,"rejected request also throttled");
            d=New();d.GetDecision(out result,e);d.Bot.BotOwner.Medical=true;
            Check(d.GetDecision(out result,e)&&d.NativeCalls==1,"medicine preempts pending draw suppression");
            d=New();d.GetDecision(out result,e);d.Bot.BotOwner.Admitted=false;
            Check(d.GetDecision(out result,e)&&d.NativeCalls==1,"combat release preempts pending draw suppression");
            d=New();d.GetDecision(out result,e);UnityEngine.Time.time+=4;
            var replacement=New();
            Check(!replacement.GetDecision(out result,e),"new native component gets independent lifecycle state");
            Check(pitTeam.Modules.SainCombatRecorderBridge.Events>0,"accepted draws emit passive events");
            Console.WriteLine(count+" emergency weapon checks passed.");
        } finally {h.UnpatchSelf();SainEmergencyWeaponBridge.Reset();}
    }
}

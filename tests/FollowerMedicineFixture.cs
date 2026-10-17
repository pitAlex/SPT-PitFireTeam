#nullable disable
#pragma warning disable CS0649, CS0414
using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using pitTeam.Modules;
using pitTeam.Patches;
using pitTeam.Utils;
using SPT.Reflection.Patching;
using UnityEngine;
namespace UnityEngine {
    public static class Time { public static float time=100; }
    public static class Mathf { public const float Epsilon=float.Epsilon; public static float Max(float a,float b)=>Math.Max(a,b); }
}
namespace HarmonyLib {
    public static class AccessTools {
        public static MethodInfo Method(Type t,string n)=>t.GetMethod(n);
        public static MethodInfo PropertyGetter(Type t,string n)=>t.GetProperty(n).GetMethod;
    }
}
namespace SPT.Reflection.Patching {
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
    public class PatchPrefixAttribute:Attribute {} public class PatchPostfixAttribute:Attribute {}
}
namespace pitTeam.Modules {
    public static class BossPlayers { public static bool IsFollower(BotOwner b)=>b?.Follower==true; }
    public static class Logger { public static int Errors; public static void LogError(string s){Errors++;} public static void LogInfo(string s){} }
}
public static class JsonType { public class DamageEffectSpecification { public int Cost; } }
namespace EFT.InventoryLogic {
    public class HealthEffectsComponent { public Dictionary<EDamageEffectType,JsonType.DamageEffectSpecification> DamageEffects=new Dictionary<EDamageEffectType,JsonType.DamageEffectSpecification>(); }
    public class MedKitComponent { public float HpResource,MaxHpResource; }
    public class Meds {
        public object Owner;public bool Applicable=true;public int Uses;
        public HealthEffectsComponent Effects=new HealthEffectsComponent();public MedKitComponent Kit;
        public bool TryGetItemComponent<T>(out T value) where T:class {
            value=typeof(T)==typeof(HealthEffectsComponent)?Effects as T:Kit as T;return value!=null;
        }
    }
}
namespace EFT.HealthSystem {
    public interface IHeavyBleeding { EBodyPart BodyPart{get;} }
    public interface ILightBleeding { EBodyPart BodyPart{get;} }
    public static class HealthHelper {public static EBodyPart[] RealBodyParts={EBodyPart.Head,EBodyPart.Chest,EBodyPart.Stomach,EBodyPart.LeftArm,EBodyPart.RightArm,EBodyPart.LeftLeg,EBodyPart.RightLeg};}
}
namespace EFT {
    public enum EBodyPart { Common=-1,Head,Chest,Stomach,LeftArm,RightArm,LeftLeg,RightLeg }
    public enum EDamageEffectType { HeavyBleeding,LightBleeding,Fracture }
    public enum ETagStatus {Healthy,Injured}
    public interface IQuickUseItem {}
    public delegate void Callback<T>(T result);
    public struct ValueStruct { public float Current,Maximum; public float Normalized=>Current/Maximum; }
    public class Health {
        public bool IsAlive=true; public int Removals;
        public event Action<object> EffectRemovedEvent { add {} remove {} }
        public Dictionary<EBodyPart,ValueStruct> Parts=new Dictionary<EBodyPart,ValueStruct>();
        public HashSet<EBodyPart> Destroyed=new HashSet<EBodyPart>();
        public HashSet<EBodyPart> Fractured=new HashSet<EBodyPart>();
        public IHeavyBleeding HeavyBleeding;public ILightBleeding LightBleeding;
        public ValueStruct GetBodyPartHealth(EBodyPart p,bool rounded)=>Parts[p];
        public bool IsBodyPartDestroyed(EBodyPart p)=>Destroyed.Contains(p);
        public bool IsBodyPartBroken(EBodyPart p)=>Fractured.Contains(p);
        public T FindExistingEffect<T>(EBodyPart p) where T:class=>typeof(T)==typeof(IHeavyBleeding)?HeavyBleeding as T:LightBleeding as T;
        public bool CanApplyItem(Meds med,EBodyPart part)=>med.Applicable&&
            (IsBodyPartBroken(part)||GetBodyPartHealth(part,false).Normalized<1);
        public void RemoveMedEffect(){Removals++;}
    }
    public class ActiveHealthController:Health {}
    public class FirearmsAnimator { public void SetActiveParam(bool a,bool b){} }
    public class Player {
        public enum EOperationState { Executing,Finished }
        public object HandsController; public object InventoryController=new object(); public ActiveHealthController HealthController=new ActiveHealthController();
        public ETagStatus HealthStatus=ETagStatus.Healthy;
        public ActiveHealthController ActiveHealthController=>HealthController;
        public class MedsController:IQuickUseItem {
            public Player _player; public Meds Item; public bool _failedToApply;
            public FirearmsAnimator _firearmsAnimator=new FirearmsAnimator();
            public MedsInHandsOperation CurrentOperation;
            public class MedsInHandsOperation {
                public MedsController _controller; public Action _onHiddenCallback;
                public Callback<IQuickUseItem> _onUsedCallback; public EOperationState State;
                public void OnEffectRemoved(object effect) {}
                __HIDE__
            }
        }
    }
    public class Mind { public bool CAN_USE_MEDS=true; public float PART_PERCENT_TO_HEAL=0.8f; }
    public class FileSettings { public Mind Mind=new Mind(); }
    public class Settings { public FileSettings FileSettings=new FileSettings(); }
    public class Selector {
        public BotOwner Owner; public int Takes;
        public bool TakePrevWeapon(){
            Takes++;
            if(Owner.GetPlayer.HandsController is Player.MedsController h)
                h.CurrentOperation.HideWeapon(()=>Owner.GetPlayer.HandsController=null);
            return true;
        }
    }
    public class Grenades {public bool ThrowindNow;}
    public class Reload {public bool Reloading;}
    public class WeaponManager { public Selector Selector=new Selector(); public Grenades Grenades=new Grenades();public Reload Reload=new Reload(); }
    public class Medicine { public bool Using;public BotFirstAid FirstAid;public BotSurgicalKit SurgicalKit=new BotSurgicalKit();public BotStimulators Stimulators;public int Refreshes;public void AidCallback(bool usingMeds){Using=usingMeds;} }
    public class ProfileInfo {public string Nickname="fixture";}
    public class BotOwner {
        public bool Follower=true; public Player GetPlayer=new Player(); public Medicine Medecine=new Medicine();
        public string name="fixture";public ProfileInfo Profile=new ProfileInfo();
        public Health HealthController=>GetPlayer.HealthController;
        public WeaponManager WeaponManager=new WeaponManager(); public Settings Settings=new Settings();
        public BotOwner(){WeaponManager.Selector.Owner=this;foreach(EBodyPart p in Enum.GetValues(typeof(EBodyPart)))if(p!=EBodyPart.Common)GetPlayer.HealthController.Parts[p]=new ValueStruct{Current=100,Maximum=100};}
    }
    public class BotAbstractMeds {
        public BotOwner _owner; public bool _using,_lastIsCanceled; public float _nextPosibleUseTime;
        public Action<bool> _callback;
        __USING__
        __CANCEL__
    }
    public class BotAbstractMedsToPart:BotAbstractMeds {
        public Meds CurUsingMeds; public EBodyPart? _bodyPartToHeal; public Health _health;
        public bool HaveSmth2Use=>CurUsingMeds!=null;
        public EBodyPart[] parts={EBodyPart.Head,EBodyPart.Chest,EBodyPart.Stomach,EBodyPart.LeftArm,EBodyPart.RightArm,EBodyPart.LeftLeg,EBodyPart.RightLeg};
    }
    public class BotFirstAid:BotAbstractMedsToPart {
        public bool Damaged,_isBleedingHeavy,_isBleedingLight; public Action _onNoDamagedParts;
        public List<Meds> _medsList=new List<Meds>();
        public bool IsBleeding=>_isBleedingHeavy||_isBleedingLight;
        public float min_percent { get { float value=0.05f;FollowerMedicineChecks.FirstAid(this,ref value);return value; } }
        public bool CanHealDamageEffectType(Meds m,EDamageEffectType d)=>m?.Effects.DamageEffects.ContainsKey(d)==true;
        public bool CanUseByTime()=>_nextPosibleUseTime<=Time.time;
        public void FindDamagedPart(){FindDamagedPartNative();FollowerMedicineChecks.Fracture(this);}
        public void ApplyToSelf(int? variant,Action callback){
            CurUsingMeds.Uses++;Using=true;
            if(CurUsingMeds.Effects.DamageEffects.TryGetValue(EDamageEffectType.Fracture,out var effect)){
                if(CurUsingMeds.Kit!=null)CurUsingMeds.Kit.HpResource-=effect.Cost;
                _health.Fractured.Remove(_bodyPartToHeal.Value);
            }
        }
        __FIRST_AID__
        __FIRST_AID_WORK__
        __FIRST_AID_START__
        __FIRST_AID_APPLY__
    }
    public class BotSurgicalKit:BotAbstractMedsToPart { public bool Damaged;public bool HaveWork=>Damaged; __SURGERY__ }
    public class BotStimulators:BotAbstractMeds { public Meds _stimulator; }
}
namespace pitTeam.Utils {
public static class FollowerFirstAidRetry { public static void Begin(BotFirstAid aid){} public static void ValidateSelection(BotFirstAid aid){} }
public static class FollowerMedical {
    public static void RefreshMedicalWork(BotOwner bot){bot.Medecine.Refreshes++;bot.Medecine.FirstAid.FindDamagedPart();}
    __SELECTION__
}}
namespace pitTeam.BigBrain {
    public class CombatRefreshHarness {
        public BotOwner botOwner;private float nextCombatHealWorkRefreshAt;
        public void Tick()=>RefreshCombatHealWorkIfNeeded();
        __COMBAT_REFRESH__
    }
    public class PatrolRefreshHarness {
        public BotOwner BotOwner;private float nextHealWorkRefreshAt;
        public void Tick()=>RefreshHealWorkIfNeeded();
        private void LogLayerException(string reason,Exception error){throw error;}
        __PATROL_REFRESH__
    }
}
__PATCHES__
public static class FollowerMedicineChecks {
    private static int count;
    private static readonly MethodInfo cancel=Hook(typeof(FollowerMedicalCancelPatch),"PatchPrefix");
    private static readonly MethodInfo minimum=Hook(typeof(FollowerFirstAidMinimumHealthPatch),"PatchPostfix");
    private static readonly MethodInfo fracture=Hook(typeof(FollowerFirstAidFracturePatch),"PatchPostfix");
    private static readonly MethodInfo surgery=Hook(typeof(FollowerSurgeryTargetPatch),"PatchPostfix");
    private static MethodInfo Hook(Type t,string name)=>t.GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static);
    private static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
    public static void FirstAid(BotFirstAid aid,ref float value){object[] args={aid,value};minimum.Invoke(null,args);value=(float)args[1];}
    public static void Fracture(BotFirstAid aid)=>fracture.Invoke(null,new object[]{aid});
    private static T Treatment<T>(BotOwner b) where T:BotAbstractMedsToPart,new()=>new T{_owner=b,_health=b.HealthController,CurUsingMeds=new Meds(),_callback=v=>b.Medecine.Using=v};
    private static Player.MedsController Hands(BotAbstractMeds meds,Meds item,Action complete){
        var hands=new Player.MedsController{_player=meds._owner.GetPlayer,Item=item};
        hands.CurrentOperation=new Player.MedsController.MedsInHandsOperation{_controller=hands,_onUsedCallback=_=>complete()};
        meds._owner.GetPlayer.HandsController=hands;return hands;
    }
    private static void Cancel(BotAbstractMeds meds){cancel.Invoke(null,new object[]{meds});meds.CancelCurrent();}
    private static void Select(BotSurgicalKit kit){kit.FindDamagedPart();surgery.Invoke(null,new object[]{kit});}
    public static int Run(){
        foreach(var kind in new[]{"firstAid","surgery","stim"}){
            var b=new BotOwner();BotAbstractMeds meds;
            if(kind=="stim")meds=new BotStimulators{_owner=b,_stimulator=new Meds(),_callback=v=>b.Medecine.Using=v};
            else meds=kind=="firstAid"?(BotAbstractMeds)Treatment<BotFirstAid>(b):Treatment<BotSurgicalKit>(b);
            var item=meds is BotAbstractMedsToPart part?part.CurUsingMeds:((BotStimulators)meds)._stimulator;
            meds.Using=true;int callbacks=0;
            var hands=Hands(meds,item,()=>{callbacks++;meds.Using=false;});
            Cancel(meds);
            Check(hands._failedToApply&&hands.CurrentOperation.State==Player.EOperationState.Finished,kind+" finishes cancelled operation");
            Check(callbacks==1&&!meds._using&&!b.Medecine.Using&&b.GetPlayer.HandsController==null,kind+" native hide callback clears medical ownership");
            Check(meds._lastIsCanceled&&meds._nextPosibleUseTime==5&&b.WeaponManager.Selector.Takes==1,kind+" preserves native cancellation cooldown and weapon handoff");
            Cancel(meds);Check(callbacks==1&&b.WeaponManager.Selector.Takes==1,kind+" cancellation cannot complete twice");
            meds.Using=true;Check(!meds._lastIsCanceled&&b.Medecine.Using,kind+" subsequent medicine can start");
        }
        var ordinary=new BotOwner{Follower=false};var aid=Treatment<BotFirstAid>(ordinary);aid.Using=true;
        var normalHands=Hands(aid,aid.CurUsingMeds,()=>aid.Using=false);Cancel(aid);
        Check(!normalHands._failedToApply&&normalHands.CurrentOperation.State==Player.EOperationState.Executing,"ordinary bot retains native cancellation behavior");
        var owner=new BotOwner();aid=Treatment<BotFirstAid>(owner);aid.Using=true;
        var unrelated=Hands(aid,new Meds(),()=>aid.Using=false);cancel.Invoke(null,new object[]{aid});
        Check(!unrelated._failedToApply&&unrelated.CurrentOperation.State==Player.EOperationState.Executing,"different medicine cannot be cancelled by stale controller");
        unrelated=Hands(aid,aid.CurUsingMeds,()=>aid.Using=false);aid._lastIsCanceled=true;cancel.Invoke(null,new object[]{aid});
        Check(!unrelated._failedToApply,"already cancelled procedure remains untouched");
        aid._lastIsCanceled=false;aid._using=false;cancel.Invoke(null,new object[]{aid});Check(!unrelated._failedToApply,"idle medicine cannot finish an operation");
        aid._using=true;unrelated.CurrentOperation.State=Player.EOperationState.Finished;cancel.Invoke(null,new object[]{aid});Check(!unrelated._failedToApply,"finished treatment cannot be retroactively failed");
        owner.GetPlayer.HandsController=new object();cancel.Invoke(null,new object[]{aid});Check(Logger.Errors==0,"firearm hands retain native cancellation without error");
        owner.GetPlayer.HandsController=new Player.MedsController{Item=aid.CurUsingMeds};cancel.Invoke(null,new object[]{aid});Check(Logger.Errors==0,"missing medical operation safely defers to recovery");
        owner=new BotOwner();aid=Treatment<BotFirstAid>(owner);aid.Using=true;aid._lastIsCanceled=true;
        int resetCallbacks=0;var resetHands=Hands(aid,aid.CurUsingMeds,()=>{resetCallbacks++;aid.Using=false;});
        pitTeam.Utils.FollowerMedical.FinishExplicitHealHands(owner.GetPlayer);
        Check(resetHands._failedToApply&&resetHands.CurrentOperation.State==Player.EOperationState.Finished,"explicit heal finishes actual medical hands even when already cancelled");
        owner.WeaponManager.Selector.TakePrevWeapon();
        Check(resetCallbacks==1&&!aid._using&&!owner.Medecine.Using&&owner.GetPlayer.HandsController==null,"native hide releases previously cancelled medical ownership");
        aid.Using=true;var nextHands=Hands(aid,aid.CurUsingMeds,()=>{resetCallbacks++;aid.Using=false;});Cancel(aid);
        Check(resetCallbacks==2&&!aid.Using&&owner.GetPlayer.HandsController==null,"next medical use completes through its own native callback");
        var firearm=new object();owner.GetPlayer.HandsController=firearm;pitTeam.Utils.FollowerMedical.FinishExplicitHealHands(owner.GetPlayer);
        Check(ReferenceEquals(owner.GetPlayer.HandsController,firearm),"explicit medical completion preserves unrelated firearm hands");
        owner.Medecine.FirstAid=aid;aid._using=true;aid._lastIsCanceled=true;owner.Medecine.Using=true;
        pitTeam.Utils.FollowerMedical.ReconcileCancelledMedicalOwnership(owner);
        Check(!aid._using&&!owner.Medecine.Using,"lost cancelled callback clears orphan native and aggregate medical ownership after firearm return");
        aid.Using=true;pitTeam.Utils.FollowerMedical.ReconcileCancelledMedicalOwnership(owner);
        Check(aid.Using&&owner.Medecine.Using,"new treatment starting from firearm hands retains ownership");
        aid._lastIsCanceled=true;owner.GetPlayer.HandsController=new Player.MedsController();pitTeam.Utils.FollowerMedical.ReconcileCancelledMedicalOwnership(owner);
        Check(aid._using&&owner.Medecine.Using,"actual medical hands retain native callback ownership");
        owner.GetPlayer.HandsController=firearm;pitTeam.Utils.FollowerMedical.ReconcileCancelledMedicalOwnership(owner);aid.Using=true;
        Check(aid.Using&&!aid._lastIsCanceled&&owner.Medecine.Using,"fresh treatment can start after orphan cancellation reconciliation");
        foreach(var part in new[]{EBodyPart.Head,EBodyPart.Chest,EBodyPart.Stomach,EBodyPart.LeftArm,EBodyPart.RightLeg}){
            var b=new BotOwner();aid=Treatment<BotFirstAid>(b);b.HealthController.Parts[part]=new ValueStruct{Current=1,Maximum=100};
            aid.FindDamagedPart();Check(aid.Damaged&&aid._bodyPartToHeal==part&&b.HealthController.Parts[part].Current==1,"native first aid admits living low health "+part);
            b.Follower=false;aid.FindDamagedPart();Check(!aid.Damaged&&aid._bodyPartToHeal==null,"ordinary bot keeps native five percent threshold "+part);
        }
        owner=new BotOwner();aid=Treatment<BotFirstAid>(owner);owner.HealthController.Parts[EBodyPart.LeftArm]=new ValueStruct{Current=0,Maximum=100};
        aid.FindDamagedPart();Check(!aid.Damaged,"zero health still requires surgery");
        owner.HealthController.Parts[EBodyPart.LeftArm]=new ValueStruct{Current=85,Maximum=100};aid.FindDamagedPart();Check(!aid.Damaged,"upper native healing threshold is unchanged");
        owner.HealthController.Parts[EBodyPart.LeftArm]=new ValueStruct{Current=1,Maximum=100};owner.Settings.FileSettings.Mind.CAN_USE_MEDS=false;aid.FindDamagedPart();Check(!aid.Damaged,"medical permission still gates low health selection");
        foreach(var vital in new[]{EBodyPart.Head,EBodyPart.Chest}){
            owner=new BotOwner();var kit=Treatment<BotSurgicalKit>(owner);var originalParts=kit.parts;
            owner.HealthController.Destroyed.UnionWith(new[]{vital,EBodyPart.LeftLeg});Select(kit);
            Check(kit.Damaged&&kit._bodyPartToHeal==EBodyPart.LeftLeg,"invalid surgery target cannot block limb "+vital);
            Check(ReferenceEquals(originalParts,kit.parts)&&kit.parts[0]==EBodyPart.Head,"surgery never mutates parts array "+vital);
            owner.HealthController.Destroyed.Remove(EBodyPart.LeftLeg);Select(kit);Check(!kit.Damaged&&kit._bodyPartToHeal==null,"no valid surgery target clears advertised work "+vital);
            owner.Follower=false;Select(kit);Check(kit.Damaged&&kit._bodyPartToHeal==vital,"ordinary surgery selection remains native "+vital);
        }
        owner=new BotOwner();var ordered=Treatment<BotSurgicalKit>(owner);ordered.parts=new[]{EBodyPart.Head,EBodyPart.RightLeg,EBodyPart.Stomach};
        owner.HealthController.Destroyed.UnionWith(ordered.parts);Select(ordered);Check(ordered._bodyPartToHeal==EBodyPart.RightLeg,"custom native part priority remains intact");
        owner.HealthController.Destroyed.Remove(EBodyPart.Head);Select(ordered);Check(ordered._bodyPartToHeal==EBodyPart.RightLeg,"valid native target remains intact");
        owner.HealthController.Destroyed.Add(EBodyPart.Head);ordered._bodyPartToHeal=EBodyPart.Head;
        owner.Settings.FileSettings.Mind.CAN_USE_MEDS=false;Select(ordered);Check(ordered._bodyPartToHeal==EBodyPart.Head,"disabled medical permission preserves native early return");
        owner.Settings.FileSettings.Mind.CAN_USE_MEDS=true;owner.HealthController.IsAlive=false;Select(ordered);Check(ordered._bodyPartToHeal==EBodyPart.Head,"dead bot preserves native surgery early return");
        owner.HealthController.IsAlive=true;ordered.parts=null;surgery.Invoke(null,new object[]{ordered});Check(!ordered.Damaged&&ordered._bodyPartToHeal==null,"missing candidate array clears invalid surgery without throwing");
        TestFractures();
        return count;
    }

    private sealed class HeavyBleed:IHeavyBleeding {public EBodyPart BodyPart=>EBodyPart.Chest;}
    private static Meds FractureMed(BotOwner bot,float resource=100,int cost=50,bool kit=true){
        var med=new Meds{Owner=bot.GetPlayer.InventoryController,Kit=kit?new MedKitComponent{HpResource=resource,MaxHpResource=1800}:null};
        med.Effects.DamageEffects[EDamageEffectType.Fracture]=new JsonType.DamageEffectSpecification{Cost=cost};return med;
    }
    private static void TestFractures(){
        var bot=new BotOwner();var aid=Treatment<BotFirstAid>(bot);var grizzly=FractureMed(bot);
        aid._medsList.Add(grizzly);bot.HealthController.Fractured.Add(EBodyPart.LeftLeg);
        aid.FindDamagedPart();Check(aid.Damaged&&aid._bodyPartToHeal==EBodyPart.LeftLeg&&aid.CurUsingMeds==grizzly,
            "full HP fracture selects cached Grizzly without HP damage");
        Check(bot.HealthController.IsBodyPartBroken(EBodyPart.LeftLeg)&&grizzly.Kit.HpResource==100&&grizzly.Uses==0,
            "selection cannot cure fracture or consume medicine");
        Check(aid.Have2Do&&aid.ShallStartUse(),"fracture reaches native first aid work and start gates");
        aid._nextPosibleUseTime=Time.time+1;Check(!aid.Have2Do&&!aid.ShallStartUse(),"fracture retains native medical cooldown");
        aid._nextPosibleUseTime=0;bot.WeaponManager.Grenades.ThrowindNow=true;Check(!aid.ShallStartUse(),"grenade throw prevents fracture start");
        bot.WeaponManager.Grenades.ThrowindNow=false;bot.WeaponManager.Reload.Reloading=true;aid.TryApplyToCurrentPart();
        Check(grizzly.Uses==0,"reload prevents fracture execution");bot.WeaponManager.Reload.Reloading=false;
        aid.TryApplyToCurrentPart();Check(grizzly.Uses==1&&!bot.HealthController.IsBodyPartBroken(EBodyPart.LeftLeg)&&grizzly.Kit.HpResource==50,
            "native apply entry delegates one fracture treatment and resource charge");
        aid.Using=false;aid.FindDamagedPart();Check(!aid.Damaged&&!aid.Have2Do,"cured full HP limb stops advertising treatment");
        foreach(var scenario in new[]{"ordinary","permission","dead","using","otherMedical","missing","depleted","wrongOwner","notApplicable","destroyed","zeroHp","noParts","bleeding"}){
            bot=new BotOwner();aid=Treatment<BotFirstAid>(bot);grizzly=FractureMed(bot);aid._medsList.Add(grizzly);
            bot.HealthController.Fractured.Add(EBodyPart.LeftLeg);
            switch(scenario){
                case "ordinary":bot.Follower=false;break;
                case "permission":bot.Settings.FileSettings.Mind.CAN_USE_MEDS=false;break;
                case "dead":bot.HealthController.IsAlive=false;break;
                case "using":aid.Using=true;break;
                case "otherMedical":bot.Medecine.Using=true;break;
                case "missing":aid._medsList.Clear();break;
                case "depleted":grizzly.Kit.HpResource=49;break;
                case "wrongOwner":grizzly.Owner=new object();break;
                case "notApplicable":grizzly.Applicable=false;break;
                case "destroyed":bot.HealthController.Destroyed.Add(EBodyPart.LeftLeg);break;
                case "zeroHp":bot.HealthController.Parts[EBodyPart.LeftLeg]=new ValueStruct{Current=0,Maximum=100};break;
                case "noParts":aid.parts=null;break;
                case "bleeding":bot.HealthController.HeavyBleeding=new HeavyBleed();break;
            }
            Fracture(aid);Check(!aid.Damaged&&grizzly.Uses==0,"fracture selection preserves "+scenario+" gate");
        }
        bot=new BotOwner();aid=Treatment<BotFirstAid>(bot);grizzly=FractureMed(bot,50);aid._medsList.Add(grizzly);
        bot.HealthController.Fractured.Add(EBodyPart.RightLeg);aid.FindDamagedPart();
        Check(aid.Damaged&&aid.CurUsingMeds==grizzly,"exact fracture removal cost is usable");
        bot.HealthController.Parts[EBodyPart.Chest]=new ValueStruct{Current=10,Maximum=100};aid.FindDamagedPart();
        Check(aid._bodyPartToHeal==EBodyPart.Chest,"native low HP treatment retains priority");
        bot.HealthController.Parts[EBodyPart.Chest]=new ValueStruct{Current=100,Maximum=100};
        grizzly.Effects.DamageEffects[EDamageEffectType.HeavyBleeding]=new JsonType.DamageEffectSpecification{Cost=10};
        bot.HealthController.HeavyBleeding=new HeavyBleed();aid.FindDamagedPart();
        Check(aid._isBleedingHeavy&&aid._bodyPartToHeal==EBodyPart.Chest,"native bleeding retains priority over fracture");
        bot.HealthController.HeavyBleeding=null;var splint=FractureMed(bot,0,0,false);aid._medsList.Add(splint);aid.FindDamagedPart();
        Check(aid.CurUsingMeds==splint&&aid._bodyPartToHeal==EBodyPart.RightLeg&&!aid.IsBleeding,"fracture-only item can be selected without a medkit component");
        bot.HealthController.Fractured.Add(EBodyPart.LeftLeg);aid.FindDamagedPart();Check(aid._bodyPartToHeal==EBodyPart.LeftLeg,"multiple fractures retain native part order");
        aid.TryApplyToCurrentPart();aid.Using=false;aid.FindDamagedPart();Check(aid._bodyPartToHeal==EBodyPart.RightLeg,"remaining fracture is selected after first native treatment");
        bot.HealthController.Fractured.Clear();aid.FindDamagedPart();Check(!aid.Damaged&&splint.Uses==1,"no repeated fracture work after cure");
        foreach(bool combat in new[]{false,true}){
            bot=new BotOwner();aid=Treatment<BotFirstAid>(bot);bot.Medecine.FirstAid=aid;aid._medsList.Add(FractureMed(bot));
            bot.HealthController.Fractured.Add(EBodyPart.LeftLeg);
            var core=new pitTeam.BigBrain.CombatRefreshHarness{botOwner=bot};
            var patrol=new pitTeam.BigBrain.PatrolRefreshHarness{BotOwner=bot};
            Action refresh=combat?(Action)core.Tick:patrol.Tick;
            refresh();Check(bot.Medecine.Refreshes==1&&aid.Have2Do,"healthy fracture refresh reaches native work: combat="+combat);
            aid._nextPosibleUseTime=Time.time+10;refresh();
            Check(bot.Medecine.Refreshes==1,"fracture refresh keeps existing one-second budget: combat="+combat);
            Time.time+=1.1f;refresh();Check(bot.Medecine.Refreshes==2,"fracture refresh retries after interval: combat="+combat);
            bot.HealthController.Fractured.Clear();aid.FindDamagedPart();Time.time+=1.1f;refresh();
            Check(bot.Medecine.Refreshes==2&&!aid.Damaged,"healthy cured bot stops refresh: combat="+combat);
        }
    }
}

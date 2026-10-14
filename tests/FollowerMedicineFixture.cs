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
using SPT.Reflection.Patching;
using UnityEngine;
namespace UnityEngine { public static class Time { public static float time=100; } }
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
    public static class Logger { public static int Errors; public static void LogError(string s){Errors++;} }
}
namespace EFT.InventoryLogic { public class Meds {} }
namespace EFT.HealthSystem {
    public interface IHeavyBleeding { EBodyPart BodyPart{get;} }
    public interface ILightBleeding { EBodyPart BodyPart{get;} }
}
namespace EFT {
    public enum EBodyPart { Common=-1,Head,Chest,Stomach,LeftArm,RightArm,LeftLeg,RightLeg }
    public enum EDamageEffectType { HeavyBleeding,LightBleeding }
    public interface IQuickUseItem {}
    public delegate void Callback<T>(T result);
    public struct ValueStruct { public float Current,Maximum; public float Normalized=>Current/Maximum; }
    public class Health {
        public bool IsAlive=true; public int Removals;
        public event Action<object> EffectRemovedEvent { add {} remove {} }
        public Dictionary<EBodyPart,ValueStruct> Parts=new Dictionary<EBodyPart,ValueStruct>();
        public HashSet<EBodyPart> Destroyed=new HashSet<EBodyPart>();
        public ValueStruct GetBodyPartHealth(EBodyPart p,bool rounded)=>Parts[p];
        public bool IsBodyPartDestroyed(EBodyPart p)=>Destroyed.Contains(p);
        public T FindExistingEffect<T>(EBodyPart p) where T:class=>null;
        public void RemoveMedEffect(){Removals++;}
    }
    public class ActiveHealthController:Health {}
    public class FirearmsAnimator { public void SetActiveParam(bool a,bool b){} }
    public class Player {
        public enum EOperationState { Executing,Finished }
        public object HandsController; public ActiveHealthController HealthController=new ActiveHealthController();
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
    public class WeaponManager { public Selector Selector=new Selector(); }
    public class Medicine { public bool Using; }
    public class BotOwner {
        public bool Follower=true; public Player GetPlayer=new Player(); public Medicine Medecine=new Medicine();
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
        public EBodyPart[] parts={EBodyPart.Head,EBodyPart.Chest,EBodyPart.Stomach,EBodyPart.LeftArm,EBodyPart.RightArm,EBodyPart.LeftLeg,EBodyPart.RightLeg};
    }
    public class BotFirstAid:BotAbstractMedsToPart {
        public bool Damaged,_isBleedingHeavy,_isBleedingLight; public Action _onNoDamagedParts;
        public float min_percent { get { float value=0.05f;FollowerMedicineChecks.FirstAid(this,ref value);return value; } }
        public bool CanHealDamageEffectType(Meds m,EDamageEffectType d)=>false;
        __FIRST_AID__
    }
    public class BotSurgicalKit:BotAbstractMedsToPart { public bool Damaged; __SURGERY__ }
    public class BotStimulators:BotAbstractMeds { public Meds _stimulator; }
}
__PATCHES__
public static class FollowerMedicineChecks {
    private static int count;
    private static readonly MethodInfo cancel=Hook(typeof(FollowerMedicalCancelPatch),"PatchPrefix");
    private static readonly MethodInfo minimum=Hook(typeof(FollowerFirstAidMinimumHealthPatch),"PatchPostfix");
    private static readonly MethodInfo surgery=Hook(typeof(FollowerSurgeryTargetPatch),"PatchPostfix");
    private static MethodInfo Hook(Type t,string name)=>t.GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static);
    private static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
    public static void FirstAid(BotFirstAid aid,ref float value){object[] args={aid,value};minimum.Invoke(null,args);value=(float)args[1];}
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
        return count;
    }
}

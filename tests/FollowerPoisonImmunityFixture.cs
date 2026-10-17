#nullable disable
#pragma warning disable CS0649,CS0414
using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using pitTeam.Modules;
using pitTeam.Patches;
using pitTeam.Utils;

namespace HarmonyLib {public static class AccessTools {public static MethodInfo Method(Type t,string name)=>t.GetMethod(name);}}
namespace SPT.Reflection.Patching {
    public abstract class ModulePatch {protected abstract MethodBase GetTargetMethod();}
    public class PatchPrefix:Attribute{} public class PatchPostfix:Attribute{}
}
namespace EFT {
    public enum EBodyPart { Head,Stomach,LeftLeg }
    public interface IAIData {BotOwner BotOwner{get;}}
    public class AIData:IAIData {public BotOwner BotOwner{get;set;}}
    public class Player {public bool IsAI;public AIData AIData=new AIData();public Inventory InventoryController=new Inventory();public ActiveHealthController ActiveHealthController;}
    public class BotOwner {
        public bool Follower;public Player GetPlayer=new Player();public BotSettings Settings=new BotSettings();
        public BotOwner(){GetPlayer.IsAI=true;GetPlayer.AIData.BotOwner=this;GetPlayer.ActiveHealthController=new ActiveHealthController{Player=GetPlayer};}
    }
    public class BotSettings {public BotSettingsComponents FileSettings=new BotSettingsComponents();}
    public class BotSettingsComponents {public Mind Mind=new Mind();}
    public class Mind {public bool CAN_USE_MEDS=true,IGNORE_TRAP;}
    public class Inventory {
        public int Scans;public EquipmentSlot[] LastSlots;public List<Meds> Items=new List<Meds>();
        public void GetAcceptableItemsNonAlloc<T>(EquipmentSlot[] slots,IList<T> result,object a,object b) where T:Meds {
            Scans++;LastSlots=slots;foreach(var med in Items)if(med is T item)result.Add(item);
        }
    }
}
namespace EFT.InventoryLogic {
    public enum EquipmentSlot {Pockets,SecuredContainer}
    public class Meds {}
    public class Stimulator:Meds {public HealthEffectsComponent HealthEffectsComponent=new HealthEffectsComponent();}
    public class HealthEffectsComponent {public EffectsSettings.StimulatorSettings.StimulatorBuffSettings[] BuffSettings=Array.Empty<EffectsSettings.StimulatorSettings.StimulatorBuffSettings>();}
}
namespace EFT.HealthSystem {
    public enum EStimulatorBuffType {Antidote,HealthRate}
    public interface IIntoxication{} public interface ILethalIntoxication{}
    public class EffectsSettings {public class StimulatorSettings {public struct StimulatorBuffSettings {public EStimulatorBuffType BuffType;public float Chance;}}}
    public class Skill {public bool Roll()=>false;}
    public class Skills {public Skill ImmunityAvoidPoisonChance=new Skill();}
    public class BaseHealthController {public bool IsAlive=true;public float DamageCoeff=1;}
    public class ActiveHealthController:BaseHealthController {
        public Player Player;public bool Antidote;public Skills _skills=new Skills();public List<Effect> Effects=new List<Effect>();
        public bool HasStimulatorBuff(EStimulatorBuffType type)=>Antidote;
        public T AddEffect<T>(EBodyPart part,float? delay,float? duration,float? residue,float? strength,Action<T> callback) where T:Effect,new() {
            var effect=new T{Health=this,BodyPart=part};Effects.Add(effect);return effect;
        }
        public class Effect {public ActiveHealthController Health;public EBodyPart BodyPart;public void ForceRemove(){Health.Effects.Remove(this);}}
        public class Intoxication:Effect,IIntoxication {public static EBodyPart DefaultBodyPart=>EBodyPart.Stomach;}
        public class LethalIntoxication:Effect,ILethalIntoxication {public static EBodyPart DefaultBodyPart=>EBodyPart.Stomach;}
        /* NATIVE ORDINARY */
        /* NATIVE LETHAL */
    }
}
public static class BotMedecine {public static EquipmentSlot[] anySlots={EquipmentSlot.Pockets},secureSlots={EquipmentSlot.SecuredContainer};}
public class BotStimulators {
    public BotOwner _owner;public bool HaveSmt,_shallUseInSafe;public Stimulator _stimulator;public List<Meds> _medsList=new List<Meds>();
    public void TryApply(){}
    /* NATIVE SELECTION */
}
namespace pitTeam.Modules {
    public static class BossPlayers {public static bool IsFollower(BotOwner bot)=>bot?.Follower==true;}
    public static class Logger {public static void LogError(string message){throw new Exception(message);}}
}
namespace pitTeam.Utils {
    public static class FollowerStimulatorPolicy { /* ANTIDOTE PREDICATE */ }
    public static class FollowerMedical { /* CLEAR POISON */ }
}
/* PRODUCTION PATCHES */

public static class FollowerPoisonChecks {
    static int count;
    static void Check(bool valid,string name){count++;if(!valid)throw new Exception(name);}
    static ActiveHealthController.Effect Poison(ActiveHealthController health,bool lethal) {
        var patch=lethal?typeof(FollowerLethalPoisonImmunityPatch):typeof(FollowerPoisonImmunityPatch);
        var hook=patch.GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic);
        object[] args={health,null};bool run=(bool)hook.Invoke(null,args);
        return run?(lethal?health.DoLethalIntoxication(1,10):health.DoIntoxication()):(ActiveHealthController.Effect)args[1];
    }
    static void Select(BotStimulators stims) {
        stims.FindMeds();typeof(FollowerStimulatorSelectionPatch).GetMethod("FilterAntidotes",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{stims});
    }
    public static int Run() {
        foreach(bool lethal in new[]{false,true}) {
            var bot=new BotOwner{Follower=true};var health=bot.GetPlayer.ActiveHealthController;
            Check(Poison(health,lethal)==null&&health.Effects.Count==0,"follower poison admission blocked before effect creation");
            bot.Follower=false;Check(Poison(health,lethal)!=null&&health.Effects.Count==1,"dismissal restores native poison admission");
            var human=new ActiveHealthController{Player=new Player()};Check(Poison(human,lethal)!=null,"human player poison remains native");
            var ordinary=new BotOwner();Check(Poison(ordinary.GetPlayer.ActiveHealthController,lethal)!=null,"ordinary bot poison remains native");
            bot.Follower=true;Check(Poison(health,lethal)==null,"recruitment enables follower immunity without role mutation");
        }
        var recruit=new BotOwner();var hc=recruit.GetPlayer.ActiveHealthController;
        hc.AddEffect<ActiveHealthController.Intoxication>(EBodyPart.LeftLeg,null,null,null,null,null);
        hc.AddEffect<ActiveHealthController.LethalIntoxication>(EBodyPart.Stomach,null,null,null,null,null);
        var positive=hc.AddEffect<ActiveHealthController.Effect>(EBodyPart.Head,null,null,null,null,null);
        FollowerMedical.ClearFollowerPoison(recruit.GetPlayer);
        Check(hc.Effects.Count==1&&ReferenceEquals(hc.Effects[0],positive),"onboarding clears every poison instance including non-default parts while retaining other effects");
        FollowerMedical.ClearFollowerPoison(recruit.GetPlayer);Check(hc.Effects.Count==1,"poison cleanup is idempotent");
        FollowerMedical.ClearFollowerPoison(null);Check(true,"missing player cleanup safely ignored");
        var antidote=new Stimulator();antidote.HealthEffectsComponent.BuffSettings=new[]{new EffectsSettings.StimulatorSettings.StimulatorBuffSettings{BuffType=EStimulatorBuffType.Antidote,Chance=1}};
        var other=new Stimulator();recruit.GetPlayer.InventoryController.Items.AddRange(new Meds[]{new Meds(),antidote,other});
        var stims=new BotStimulators{_owner=recruit,_shallUseInSafe=true};recruit.Follower=true;
        Select(stims);Check(ReferenceEquals(stims._stimulator,other)&&stims.HaveSmt,"native follower selection skips antidote and retains first unrelated stim");
        Check(recruit.GetPlayer.InventoryController.Scans==1,"antidote filtering adds no inventory scan");
        Check(ReferenceEquals(recruit.GetPlayer.InventoryController.LastSlots,BotMedecine.secureSlots),"native accessible-slot restriction retained");
        recruit.GetPlayer.InventoryController.Items.Remove(other);Select(stims);Check(stims._stimulator==null&&!stims.HaveSmt,"only antidotes does not advertise available stim treatment");
        recruit.Follower=false;Select(stims);Check(ReferenceEquals(stims._stimulator,antidote)&&stims.HaveSmt,"ordinary and dismissed bots retain antidote selection");
        recruit.Follower=true;recruit.GetPlayer.InventoryController.Items.Clear();recruit.GetPlayer.InventoryController.Items.Add(other);Select(stims);
        Check(ReferenceEquals(stims._stimulator,other)&&stims.HaveSmt,"unrelated native stim selection unchanged");
        stims._medsList.Clear();stims._medsList.AddRange(new Meds[]{antidote,other});stims._stimulator=antidote;
        int scans=recruit.GetPlayer.InventoryController.Scans;
        typeof(FollowerStimulatorUsePatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{stims});
        Check(ReferenceEquals(stims._stimulator,other),"execution filters antidote cached before recruitment");
        Check(recruit.GetPlayer.InventoryController.Scans==scans,"execution guard reuses native cache without inventory scan");
        return count;
    }
}

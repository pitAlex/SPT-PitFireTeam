using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using EFT.InventoryLogic;
using EFT.HealthSystem;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.SAINAddon;
using pitTeam.Utils;
using SAIN.Components;
using SAIN.SAINComponent.Classes.Decision;
using Enemy = SAIN.SAINComponent.Classes.EnemyClasses.Enemy;
using UnityEngine;

public class BotStimulators {
    public bool Using, HaveSmt=true, Allowed=true, _shallUseInSafe;
    public float LastEndUseTime=-100; public Stimulator _stimulator; public int Refreshes;
    public bool CanUseNow()=>Allowed;
    public void Refresh(){Refreshes++;}
}
public static class BotMedecine {
    public static EquipmentSlot[] secureSlots={EquipmentSlot.SecuredContainer}, anySlots={EquipmentSlot.Pockets};
}
namespace EFT {
    public enum EBodyPart {Head,Chest,Stomach,LeftArm,RightArm,LeftLeg,RightLeg}
    public enum EPhysicalCondition {OnPainkillers}
    public enum EDamageEffectType {Pain}
    public class StimHealth {
        public HashSet<EBodyPart> Destroyed=new HashSet<EBodyPart>();
        public bool IsBodyPartDestroyed(EBodyPart part)=>Destroyed.Contains(part);
    }
    public class StimMovement {public bool Painkillers;public bool PhysicalConditionIs(EPhysicalCondition condition)=>Painkillers;}
    public partial class Player {
        public StimHealth ActiveHealthController=new StimHealth();public StimMovement MovementContext=new StimMovement();
        public StimInventory InventoryController=new StimInventory();
    }
    public class StimInventory {
        public List<Meds> Items=new List<Meds>();public EquipmentSlot[] LastSlots;public int Scans;
        public void GetAcceptableItemsNonAlloc<T>(EquipmentSlot[] slots,IList<T> result,object a,object b) where T: Meds {
            Scans++;LastSlots=slots;foreach(var item in Items)if(item is T typed)result.Add(typed);
        }
    }
    public class StimSurgery {public bool Using,HaveWork;public EBodyPart? _bodyPartToHeal;public int Finds;public void FindDamagedPart(){Finds++;}}
    public partial class Medicine {
        public BotStimulators Stimulators=new BotStimulators();public StimSurgery SurgicalKit=new StimSurgery();
        public bool Using=>FirstAid.Using||SurgicalKit.Using||Stimulators.Using;
    }
    public class StimReload {public bool Reloading;}
    public partial class RiskWeaponManager {public StimReload Reload=new StimReload();}
}
namespace EFT.InventoryLogic {
    public enum EquipmentSlot {Pockets,SecuredContainer}
    public class Meds {}
    public class Stimulator : Meds {public HealthEffectsComponent HealthEffectsComponent=new HealthEffectsComponent();}
    public class HealthEffectsComponent {
        public Dictionary<EDamageEffectType,object> DamageEffects=new Dictionary<EDamageEffectType,object>();
        public EffectsSettings.StimulatorSettings.StimulatorBuffSettings[] BuffSettings=Array.Empty<EffectsSettings.StimulatorSettings.StimulatorBuffSettings>();
    }
}
namespace EFT.HealthSystem {
    public enum EStimulatorBuffType {HealthRate,Other}
    public class EffectsSettings {public class StimulatorSettings {public struct StimulatorBuffSettings {public EStimulatorBuffType BuffType;public float Value;}}}
}
namespace SAIN.Components {
    public partial class EnemyController {public bool AtPeace=>KnownEnemies.Count==0;}
    public partial class Decision {public bool RunningToCover;}
    public partial class BotHealth {public bool Dying=>HealthStatus==ETagStatus.Dying;public bool BadlyInjured=>HealthStatus==ETagStatus.BadlyInjured;}
}
namespace SAIN.SAINComponent.Classes.Decision {
    public partial class SelfActionDecisionClass {
        public int NativeStimCalls;public bool NativeStimResult;
        public bool StimsForTest()=>startUseStims();
        [MethodImpl(MethodImplOptions.NoInlining)] private bool startUseStims(){NativeStimCalls++;return NativeStimResult;}
        [MethodImpl(MethodImplOptions.NoInlining)] private static bool ShallUseStimsCheckEnemy(Enemy enemy)=>enemy==null||!enemy.InLineOfSight;
    }
}
public static partial class CombatChecks {
    private static void TestStimulators(){
        var b=PushBot("stims");var bot=b.Sain;var decision=new SelfActionDecisionClass(bot);var enemy=bot.GoalEnemy;
        var cover=CoverAt(0);bot.Cover.CoverInUse=cover;bot.Mover.Moving=false;enemy.Seen=true;enemy.TimeSinceSeen=20;enemy.InLineOfSight=false;
        var inventory=b.GetPlayer.InventoryController;var stims=b.Medecine.Stimulators;
        var unrelated=new Stimulator();var pain=new Stimulator();pain.HealthEffectsComponent.DamageEffects.Add(EDamageEffectType.Pain,new object());
        var negative=new Stimulator();negative.HealthEffectsComponent.BuffSettings=new[]{new EffectsSettings.StimulatorSettings.StimulatorBuffSettings{BuffType=EStimulatorBuffType.HealthRate,Value=-1}};
        var health=new Stimulator();health.HealthEffectsComponent.BuffSettings=new[]{new EffectsSettings.StimulatorSettings.StimulatorBuffSettings{BuffType=EStimulatorBuffType.HealthRate,Value=1}};
        inventory.Items.AddRange(new Meds[]{new Meds(),unrelated,negative,health,pain});stims._stimulator=unrelated;
        b.GetPlayer.ActiveHealthController.Destroyed.Add(EBodyPart.Stomach);
        stims.HaveSmt=false;
        Check(decision.StimsForTest()&&stims._stimulator==pain&&stims.HaveSmt&&decision.NativeStimCalls==0,"blacked stomach selects pain relief below native serious-injury threshold and recovers stale HaveSmt");
        Check(bot.Memory.Health.HealthStatus==ETagStatus.Healthy&&!stims.Using,"selection neither changes health state nor executes medicine");
        b.GetPlayer.MovementContext.Painkillers=true;stims._stimulator=unrelated;
        Check(!decision.StimsForTest()&&stims._stimulator==unrelated,"active painkillers prevent redundant pain-specific selection");b.GetPlayer.MovementContext.Painkillers=false;
        stims.LastEndUseTime=Time.time-2;Check(!decision.StimsForTest(),"stim cooldown cannot be bypassed");stims.LastEndUseTime=-100;
        stims.Allowed=false;Check(!decision.StimsForTest(),"native CanUseNow gate remains authoritative");stims.Allowed=true;
        b.WeaponManager.Reload.Reloading=true;Check(!decision.StimsForTest(),"reload blocks extended stim selection");b.WeaponManager.Reload.Reloading=false;
        b.Medecine.FirstAid.Using=true;Check(!decision.StimsForTest(),"running first aid is not preempted by stim selection");b.Medecine.FirstAid.Using=false;
        stims.Using=true;Check(!decision.StimsForTest(),"running stim cannot be selected again");stims.Using=false;
        b.GetPlayer.ActiveHealthController.Destroyed.Clear();bot.Memory.Health.HealthStatus=ETagStatus.BadlyInjured;
        Check(decision.StimsForTest()&&stims._stimulator==health,"serious injury chooses positive regeneration rather than first inventory stim or negative regeneration");
        b.GetPlayer.ActiveHealthController.Destroyed.Add(EBodyPart.Stomach);
        Check(decision.StimsForTest()&&stims._stimulator==pain,"Core black-stomach priority precedes health regeneration");
        b.GetPlayer.ActiveHealthController.Destroyed.Clear();bot.Memory.Health.HealthStatus=ETagStatus.Healthy;
        b.GetPlayer.ActiveHealthController.Destroyed.Add(EBodyPart.LeftLeg);b.Medecine.SurgicalKit.HaveWork=true;b.Medecine.SurgicalKit._bodyPartToHeal=EBodyPart.LeftLeg;
        Check(decision.StimsForTest()&&stims._stimulator==pain&&b.Medecine.SurgicalKit.Finds==0,"advertised destroyed-limb surgery permits pain relief without executing surgical provider");
        b.Medecine.SurgicalKit._bodyPartToHeal=null;Check(decision.StimsForTest()&&b.Medecine.SurgicalKit.Finds==0,"missing surgery target uses read-only destroyed-part fallback");
        stims._shallUseInSafe=true;decision.StimsForTest();Check(ReferenceEquals(inventory.LastSlots,BotMedecine.secureSlots),"selection preserves game's secure-slot restriction");stims._shallUseInSafe=false;
        enemy.InLineOfSight=true;enemy.IsVisible=true;
        Check(!decision.StimsForTest(),"visible enemy rejects both native and protected-cover admission");enemy.IsVisible=false;enemy.Heard=true;enemy.Seen=false;
        Check(decision.StimsForTest(),"reached hard cover can admit stim against native heard-contact LOS rejection");
        bot.Mover.Moving=true;Check(!decision.StimsForTest(),"travelling to cover does not count as protected treatment");
        bot.Decision.RunningToCover=true;Check(decision.StimsForTest(),"native running-to-cover stim exception remains available");bot.Decision.RunningToCover=false;bot.Mover.Moving=false;
        var other=new Enemy{InLineOfSight=true,IsVisible=true};bot.EnemyController.KnownEnemies.Add(other);
        Check(!decision.StimsForTest(),"a non-goal visible threat blocks protected-cover stim admission");bot.EnemyController.KnownEnemies.Remove(other);
        enemy.InLineOfSight=false;inventory.Items.Clear();stims._stimulator=unrelated;decision.NativeStimResult=true;
        int calls=decision.NativeStimCalls;Check(decision.StimsForTest()&&decision.NativeStimCalls==calls+1&&stims._stimulator==unrelated&&stims.Refreshes==0,"missing matching stim executes native fallback once without refreshing or replacing cached item");
        decision.NativeStimResult=false;inventory.Items.Add(pain);
        b.Follower.CombatTactic=FollowerCombatTactic.Balanced;Check(!decision.StimsForTest(),"Core tactic remains outside addon hook");
        b.Follower.CombatTactic=FollowerCombatTactic.SAINShooter;Tick();Check(decision.StimsForTest(),"SAINShooter shares the same medical extension");
        b.IsFollower=false;Check(!decision.StimsForTest(),"ordinary SAIN bots retain native stim policy");b.IsFollower=true;
        b.Memory.GoalEnemy.Alive=false;Check(!decision.StimsForTest(),"missing accepted living goal cannot reopen addon medicine");b.Memory.GoalEnemy.Alive=true;
        pitTeam.pitFireTeam.IsSAINAddonInstalled=false;Check(!decision.StimsForTest(),"unready addon uses native behavior");pitTeam.pitFireTeam.IsSAINAddonInstalled=true;
        var policy=new FollowerStimulatorPolicy();inventory.Items.Clear();policy.TrySelectPainStimulator(b,stims);Check(stims.Refreshes==1,"Core retains refresh-on-miss behavior");
        var hook=AccessTools.Method(typeof(SelfActionDecisionClass),"startUseStims");
        Check(Harmony.GetPatchInfo(hook).Prefixes.Count==1&&Harmony.GetPatchInfo(hook).Owners.Contains("xyz.pit.fireteam.sainaddon"),"stim hook installs once under addon owner");
    }
}
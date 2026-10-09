param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$medicalSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/FollowerMedical.cs')
$methods = @('HasRecoverableFirstAidDamage','CanStartFirstAidTopOff','TryStartFirstAidTopOff','CanAttemptFirstAidTopOff','CanAttemptSelectedFirstAidTopOff','TryFindFirstAidTopOffTarget','ShouldAllowManualFirstAidTopOff','TryFindFirstAidTopOffTargetCore','TrySelectTopOffMed','HasVisibleKnownEnemy') | ForEach-Object {
    $match = [regex]::Matches($medicalSource, '(?ms)^        (?:public|private|internal)[^\r\n]*\b' + $_ + '\(.*?^        \}')
    if ($match.Count -ne 1) { throw "Expected one production method $_" }
    $match[0].Value
}
$state = [regex]::Match($medicalSource, '(?ms)^        private sealed class FirstAidTopOffState\s*\{.*?^        \}').Value
if (!$state) { throw 'Missing production top-off state' }
$policy = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/FollowerFirstAidTopOffPolicy.cs')
$policy = [regex]::Replace($policy, '(?m)^using [^\r\n]+\r?\n', '')
$code = @'
#nullable disable
#pragma warning disable CS0649, CS8632
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using EFT.InventoryLogic;
using pitTeam.Components;
using pitTeam.Utils;
using UnityEngine;
namespace UnityEngine {
    public static class Time { public static float time; }
    public static class Mathf { public static float Abs(float x)=>Math.Abs(x); }
}
namespace EFT {
    public enum EBodyPart { Head,Chest,Stomach,LeftArm,RightArm,LeftLeg,RightLeg }
    public struct ValueStruct { public float Current,Maximum; }
    public class Health {
        public bool IsAlive=true;
        public Dictionary<EBodyPart,ValueStruct> Parts=new Dictionary<EBodyPart,ValueStruct>();
        public HashSet<EBodyPart> Destroyed=new HashSet<EBodyPart>();
        public ValueStruct GetBodyPartHealth(EBodyPart part,bool rounded)=>Parts[part];
        public bool IsBodyPartDestroyed(EBodyPart part)=>Destroyed.Contains(part);
        public bool CanApplyItem(Meds med,EBodyPart part)=>med.Allowed&&Parts[part].Current<Parts[part].Maximum&&!Destroyed.Contains(part);
    }
    public class Inventory {
        public List<Meds> Items=new List<Meds>(); public int Queries; public EquipmentSlot[] LastSlots;
        public void GetAcceptableItemsNonAlloc<T>(EquipmentSlot[] slots,List<T> output,object a,object b){Queries++;LastSlots=slots;foreach(var med in Items)if(med!=null)output.Add((T)(object)med);}
    }
    public class Player { public Health HealthController=new Health(); public Health ActiveHealthController=>HealthController; public Inventory InventoryController=new Inventory(); public bool Bleeding; }
    public class BotFirstAid {
        public bool Using,Damaged,_shallUseInSafe,_isBleedingLight,_isBleedingHeavy,Ready=true,ApplyAllowed=true,ThrowOnApply;
        public Meds CurUsingMeds; public EBodyPart? _bodyPartToHeal; public int Attempts;
        public bool CanUseByTime()=>Ready;
        public void TryApplyToCurrentPart(){Attempts++;if(ThrowOnApply)throw new Exception("simulated transaction failure");Using=ApplyAllowed;}
    }
    public class Surgery { public bool HaveWork,Using; }
    public class Medicine { public BotFirstAid FirstAid=new BotFirstAid(); public Surgery SurgicalKit=new Surgery(); public bool StimUsing; public bool Using=>FirstAid.Using||SurgicalKit.Using||StimUsing; }
    public class Grenades { public bool ThrowindNow; }
    public class Reload { public bool Reloading; }
    public class Weapons { public Grenades Grenades=new Grenades(); public Reload Reload=new Reload(); }
    public class Mind { public bool CAN_USE_MEDS=true; }
    public class FileSettings { public Mind Mind=new Mind(); }
    public class Settings { public FileSettings FileSettings=new FileSettings(); }
    public class EnemyInfo { public bool Alive=true,IsVisible; }
    public class Memory { public EnemyInfo GoalEnemy; public bool HaveEnemy=>GoalEnemy!=null; }
    public class Enemies { public Dictionary<string,EnemyInfo> EnemyInfos=new Dictionary<string,EnemyInfo>(); }
    public class BotOwner {
        public Player GetPlayer=new Player(); public Health HealthController=>GetPlayer.HealthController;
        public Medicine Medecine=new Medicine(); public Weapons WeaponManager=new Weapons(); public Settings Settings=new Settings();
        public Memory Memory=new Memory(); public Enemies EnemiesController=new Enemies(); public bool Recovery,Elapsed;
    }
}
namespace EFT.HealthSystem { public static class HealthHelper { public static readonly EBodyPart[] RealBodyParts=(EBodyPart[])Enum.GetValues(typeof(EBodyPart)); } }
namespace EFT.InventoryLogic {
    public enum EquipmentSlot { SecuredContainer,Pockets }
    public class MedKitComponent { public float HpResource; }
    public class Meds {
        public MedKitComponent Kit=new MedKitComponent{HpResource=100}; public bool Allowed=true; public object Owner;
        public bool TryGetItemComponent<T>(out T item)where T:class {item=Kit as T;return item!=null;}
    }
}
public static class BotMedecine { public static readonly EquipmentSlot[] secureSlots={EquipmentSlot.SecuredContainer},anySlots={EquipmentSlot.Pockets}; }
namespace pitTeam.Components { public static class BotFollowerPlayer { public static bool IsEnemyInfoAlive(EnemyInfo info)=>info?.Alive==true; } }
__POLICY__
namespace pitTeam.Utils {
    public static class FollowerMedical {
__STATE__
        private static readonly ConditionalWeakTable<Player,FirstAidTopOffState> FirstAidTopOffStates=new ConditionalWeakTable<Player,FirstAidTopOffState>();
        private static bool IsPostCombatFullHealActive(BotOwner b)=>b.Recovery;
        private static bool IsPostCombatFullHealRestoreWindowElapsed(BotOwner b)=>b.Elapsed;
        private static bool TryGetActiveBleeding(Player p,out object bleeding){bleeding=null;return p.Bleeding;}
__METHODS__
    }
}
public static class TopOffChecks {
    private static int count;
    private static void Check(bool value,string label){if(!value)throw new Exception(label);count++;}
    private static BotOwner Bot(){
        var b=new BotOwner();Time.time=100;
        foreach(var p in EFT.HealthSystem.HealthHelper.RealBodyParts)b.GetPlayer.HealthController.Parts[p]=new ValueStruct{Current=100,Maximum=100};
        b.GetPlayer.InventoryController.Items.Add(new Meds{Owner=b.GetPlayer.InventoryController});return b;
    }
    private static void Hp(BotOwner b,EBodyPart part,float hp,float max=100)=>b.GetPlayer.HealthController.Parts[part]=new ValueStruct{Current=hp,Maximum=max};
    private static bool Needs(EBodyPart p,float hp,float max,bool full=false)=>FollowerFirstAidTopOffPolicy.NeedsTreatment(p,hp,max,full);
    public static int Run(){
        Check(Needs(EBodyPart.Chest,69.09f,85),"Recorded recruit chest deficit qualifies without combat");
        Check(Needs(EBodyPart.Head,32.76f,35),"Recorded recruit head deficit qualifies");
        Check(!Needs(EBodyPart.LeftLeg,60.83f,65),"Small recorded leg deficit does not cause constant top-off");
        Check(!Needs(EBodyPart.Head,33.5f,35),"Sub-two-HP scratch does not start healing");
        Check(!Needs(EBodyPart.Chest,95,100)&&Needs(EBodyPart.Chest,94.9f,100),"Vital threshold boundary is 95 percent");
        Check(!Needs(EBodyPart.LeftArm,90,100)&&Needs(EBodyPart.LeftArm,89.9f,100),"Other-part threshold boundary is 90 percent");
        Check(Needs(EBodyPart.LeftArm,99,100,true)&&!Needs(EBodyPart.Head,99.5f,100,true),"Existing full-recovery half-HP floor retained");
        Check(!Needs(EBodyPart.Chest,0,100)&&!Needs(EBodyPart.Chest,1,0),"Invalid and destroyed health cannot qualify");
        var b=Bot();Check(!FollowerMedical.HasRecoverableFirstAidDamage(b),"Healthy patrol has no healing work");
        for(int i=0;i<100;i++)FollowerMedical.HasRecoverableFirstAidDamage(b);
        Check(b.GetPlayer.InventoryController.Queries==0,"Healthy patrol never scans inventory");
        b=Bot();Hp(b,EBodyPart.Chest,69.09f,85);Hp(b,EBodyPart.LeftLeg,30,65);
        Check(FollowerMedical.HasRecoverableFirstAidDamage(b)&&FollowerMedical.CanStartFirstAidTopOff(b),"Peaceful admission does not need a recovery episode");
        for(int i=0;i<100;i++)FollowerMedical.HasRecoverableFirstAidDamage(b);
        Check(b.GetPlayer.InventoryController.Queries==1,"Repeated eligibility polls share one bounded inventory scan");
        Check(FollowerMedical.TryStartFirstAidTopOff(b)&&b.Medecine.FirstAid._bodyPartToHeal==EBodyPart.Chest,"Peaceful top-off prioritizes vital part and uses native first aid");
        Check(!b.Recovery&&b.Medecine.FirstAid.Attempts==1,"Peaceful treatment never arms free full-health restoration");
        Check(!FollowerMedical.CanStartFirstAidTopOff(b),"Active treatment cannot overlap");
        Hp(b,EBodyPart.Chest,85,85);b.Medecine.FirstAid.Using=false;Time.time+=6;
        Check(FollowerMedical.TryStartFirstAidTopOff(b)&&b.Medecine.FirstAid._bodyPartToHeal==EBodyPart.LeftLeg,"Health progress permits the next qualifying part");
        b=Bot();Hp(b,EBodyPart.Chest,80);b.Medecine.FirstAid.ApplyAllowed=false;
        Check(!FollowerMedical.TryStartFirstAidTopOff(b),"Rejected native transaction is observed");
        Time.time+=.6f;Check(FollowerMedical.HasRecoverableFirstAidDamage(b)&&!FollowerMedical.CanStartFirstAidTopOff(b)&&b.Medecine.FirstAid.Attempts==1,"Failed attempt advertises stationary work while waiting three seconds");
        Time.time+=3;Check(!FollowerMedical.TryStartFirstAidTopOff(b)&&b.Medecine.FirstAid.Attempts==2,"One bounded retry is allowed");
        Time.time+=10;for(int i=0;i<20;i++){FollowerMedical.TryStartFirstAidTopOff(b);Time.time+=1;}
        Check(b.Medecine.FirstAid.Attempts==2,"Unchanged failed part cannot churn indefinitely");
        Check(!FollowerMedical.HasRecoverableFirstAidDamage(b),"Exhausted part releases patrol healing ownership");
        Hp(b,EBodyPart.Head,80);Time.time+=1;Check(!FollowerMedical.TryStartFirstAidTopOff(b)&&b.Medecine.FirstAid._bodyPartToHeal==EBodyPart.Head,"Blocked chest cannot starve another part");
        Hp(b,EBodyPart.Head,100);b.GetPlayer.InventoryController.Items[0].Kit.HpResource=50;Time.time+=1;
        Check(!FollowerMedical.CanStartFirstAidTopOff(b),"Supply consumption cannot rearm failed chest");
        b.GetPlayer.InventoryController.Items.Add(new Meds());Time.time+=1;b.Medecine.FirstAid.ApplyAllowed=true;
        Check(FollowerMedical.TryStartFirstAidTopOff(b),"Replenished medical resources permit a changed attempt");
        b.Medecine.FirstAid.Using=false;Time.time+=5;Hp(b,EBodyPart.Chest,70);
        Check(FollowerMedical.TryStartFirstAidTopOff(b),"Meaningful new damage reopens the part");
        b=Bot();Hp(b,EBodyPart.Chest,80);b.Medecine.FirstAid.ThrowOnApply=true;
        FollowerMedical.TryStartFirstAidTopOff(b);Time.time+=4;FollowerMedical.TryStartFirstAidTopOff(b);Time.time+=4;
        Check(!FollowerMedical.TryStartFirstAidTopOff(b)&&b.Medecine.FirstAid.Attempts==2,"Thrown transaction also consumes bounded failure budget");
        b=Bot();Hp(b,EBodyPart.Chest,80);b.Medecine.FirstAid.Ready=false;
        Check(FollowerMedical.HasRecoverableFirstAidDamage(b)&&!FollowerMedical.CanStartFirstAidTopOff(b),"Work remains advertised through native cooldown");
        b.Medecine.FirstAid.Ready=true;b.WeaponManager.Reload.Reloading=true;
        Check(!FollowerMedical.TryStartFirstAidTopOff(b),"Reload protects hands");b.WeaponManager.Reload.Reloading=false;
        b.WeaponManager.Grenades.ThrowindNow=true;Check(!FollowerMedical.CanStartFirstAidTopOff(b),"Grenade use retains priority");b.WeaponManager.Grenades.ThrowindNow=false;
        b.Medecine.StimUsing=true;Check(!FollowerMedical.CanStartFirstAidTopOff(b),"Other active medicine retains priority");b.Medecine.StimUsing=false;
        b.Medecine.SurgicalKit.HaveWork=true;Check(!FollowerMedical.HasRecoverableFirstAidDamage(b),"Surgery remains prior to top-off");b.Medecine.SurgicalKit.HaveWork=false;
        b.GetPlayer.Bleeding=true;Check(!FollowerMedical.HasRecoverableFirstAidDamage(b),"Bleeding treatment remains prior to top-off");b.GetPlayer.Bleeding=false;
        b.Memory.GoalEnemy=new EnemyInfo();Check(!FollowerMedical.HasRecoverableFirstAidDamage(b),"Living combat goal blocks peaceful healing");b.Memory.GoalEnemy.Alive=false;
        Check(FollowerMedical.HasRecoverableFirstAidDamage(b),"Dead stale goal cannot block healing");
        b.EnemiesController.EnemyInfos["visible"]=new EnemyInfo{IsVisible=true};Check(!FollowerMedical.HasRecoverableFirstAidDamage(b),"Live visible known threat still blocks healing");
        b=Bot();Hp(b,EBodyPart.Chest,80);b.Settings.FileSettings.Mind.CAN_USE_MEDS=false;Check(!FollowerMedical.HasRecoverableFirstAidDamage(b),"Native med permission retained");
        b=Bot();Hp(b,EBodyPart.Chest,80);b.GetPlayer.ActiveHealthController.Destroyed.Add(EBodyPart.Chest);Check(!FollowerMedical.HasRecoverableFirstAidDamage(b),"Destroyed parts cannot receive first-aid top-off");
        b=Bot();Hp(b,EBodyPart.Chest,80);b.GetPlayer.InventoryController.Items[0].Kit.HpResource=0;Check(!FollowerMedical.HasRecoverableFirstAidDamage(b),"Depleted supplies cannot advertise work");
        b.GetPlayer.InventoryController.Items[0].Kit.HpResource=100;b.GetPlayer.InventoryController.Items[0].Allowed=false;Time.time+=1;Check(!FollowerMedical.HasRecoverableFirstAidDamage(b),"Native item applicability is required");
        b=Bot();Hp(b,EBodyPart.Head,34,35);b.Recovery=true;
        Check(FollowerMedical.HasRecoverableFirstAidDamage(b),"Combat recovery retains small-deficit eligibility");b.Elapsed=true;
        Check(!FollowerMedical.HasRecoverableFirstAidDamage(b),"Elapsed combat recovery cannot become a peaceful top-off loop");
        b=Bot();Hp(b,EBodyPart.Chest,80);b.Medecine.FirstAid._shallUseInSafe=true;FollowerMedical.HasRecoverableFirstAidDamage(b);
        Check(b.GetPlayer.InventoryController.LastSlots==BotMedecine.secureSlots,"Native medical inventory access restriction retained");
        b.Medecine.FirstAid._shallUseInSafe=false;FollowerMedical.HasRecoverableFirstAidDamage(b);
        Check(b.GetPlayer.InventoryController.Queries==2&&b.GetPlayer.InventoryController.LastSlots==BotMedecine.anySlots,"Changed native medical access invalidates cached selection immediately");
        b.GetPlayer.InventoryController.Items[0].Owner=null;b.GetPlayer.InventoryController.Items.Clear();
        Check(!FollowerMedical.TryStartFirstAidTopOff(b)&&b.Medecine.FirstAid.Attempts==0,"Detached cached medicine cannot begin treatment");
        var other=Bot();Hp(other,EBodyPart.Chest,80);Check(FollowerMedical.CanStartFirstAidTopOff(other),"Medical policy and caches are follower-local");
        return count;
    }
}
'@
$code = $code.Replace('__POLICY__', $policy).Replace('__STATE__', $state).Replace('__METHODS__', ($methods -join "`n"))
Add-Type -TypeDefinition $code -Language CSharp
$count = [TopOffChecks]::Run()
Write-Output "Passed $count production first-aid top-off checks. EFT item transactions are simulated; live healing still requires raid validation."

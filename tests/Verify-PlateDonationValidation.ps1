param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/TeammateGearSwap.cs')
$method = [regex]::Match($source, '(?ms)^        private void ValidateFinalEquipment\(\).*?^        \}')
if (!$method.Success) { throw 'Missing production final equipment validation' }
$fixture = @'
using System;
using System.Linq;
using System.Collections.Generic;
public enum EquipmentSlot { FirstPrimaryWeapon,SecondPrimaryWeapon,Holster }
public class Weapon { public string[] MissingVitalParts = new string[0]; }
public class Slot { public object ContainedItem; }
public class Equipment {
    public bool HasWeapon=true;
    public Slot GetSlot(EquipmentSlot slot)=>new Slot{ContainedItem=HasWeapon?(object)new Weapon():null};
}
public class GiveGearPlanner { public bool OnlyPlateUpgrades; }
public class GearSwapValidationException : Exception { public GearSwapValidationException(string key):base(key){} }
namespace pitTeam.BigBrain.Actions {
    public static class GestureCommandAction {
        public static bool Fits;public static int Calls;
        public static bool CanFitGearSwapReloadReserves(Equipment kit){Calls++;return Fits;}
    }
}
public class Session {
    public bool Giving;public GiveGearPlanner _donationPlan;public Equipment FollowerEquipment=new Equipment();
    public static EquipmentSlot[] FirearmSlots={EquipmentSlot.FirstPrimaryWeapon,EquipmentSlot.SecondPrimaryWeapon,EquipmentSlot.Holster};
    public void Validate()=>ValidateFinalEquipment();
    /* PRODUCTION */
}
public static class PlateValidationChecks {
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Expect(Session session,string key){try{session.Validate();throw new Exception("Expected "+key);}catch(GearSwapValidationException ex){Check(ex.Message==key,"preserved validation: "+key);}}
    public static int Run(){
        var s=new Session{Giving=true,_donationPlan=new GiveGearPlanner{OnlyPlateUpgrades=true}};
        pitTeam.BigBrain.Actions.GestureCommandAction.Fits=false;
        s.Validate();
        Check(pitTeam.BigBrain.Actions.GestureCommandAction.Calls==0,"plate-only donation must not query magazine space on an already-full kit");
        s._donationPlan.OnlyPlateUpgrades=false;Expect(s,"SwapGearMagazineSpace");
        Check(pitTeam.BigBrain.Actions.GestureCommandAction.Calls==1,"weapon/storage/consumable changes still query reload reserves");
        s._donationPlan.OnlyPlateUpgrades=true;s.Giving=false;Expect(s,"SwapGearMagazineSpace");
        s.Giving=true;s._donationPlan=null;Expect(s,"SwapGearMagazineSpace");
        s._donationPlan=new GiveGearPlanner();pitTeam.BigBrain.Actions.GestureCommandAction.Fits=true;s.Validate();
        Check(pitTeam.BigBrain.Actions.GestureCommandAction.Calls==4,"ordinary donation with room retains reserve validation");
        s.Giving=false;s.FollowerEquipment.HasWeapon=false;Expect(s,"SwapGearNeedsWeapon");
        return checks;
    }
}
'@
Add-Type -TypeDefinition $fixture.Replace('/* PRODUCTION */', $method.Value) -Language CSharp
$checks = [PlateValidationChecks]::Run()
"PASS: $checks production plate-only donation/final-validation checks; controlled inventory inputs."

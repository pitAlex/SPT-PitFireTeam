param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/TeammateGearSwap.cs')
$methods = foreach ($signature in @('void ValidateFinalEquipment', 'bool IsBackpackOnlyEdit', 'IEnumerable<string> SnapshotNonBackpackSlots')) {
    $method = [regex]::Match($source, '(?ms)^        private ' + [regex]::Escape($signature) + '\(\).*?^        \}')
    if (!$method.Success) { throw "Missing production final equipment method: $signature" }
    $method.Value
}
$fixture = @'
using System;
using System.Linq;
using System.Collections.Generic;
public enum EquipmentSlot { FirstPrimaryWeapon, SecondPrimaryWeapon, Holster, Backpack, TacticalVest, Pockets }
public class Weapon { public string[] MissingVitalParts = new string[0]; public string State = "original-magazine"; }
public class Slot { public object ContainedItem; }
public class Equipment {
    public Slot[] Slots = Enumerable.Range(0, 6).Select(_ => new Slot()).ToArray();
    public Equipment() { HasWeapon = true; }
    public bool HasWeapon { set { GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem = value ? new Weapon() : null; } }
    public Slot GetSlot(EquipmentSlot slot) => Slots[(int)slot];
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
    private string _backpackDraftBefore;
    private string[] _otherFollowerSlotsBefore;
    public void CaptureDraft() {
        _backpackDraftBefore = Snapshot(FollowerEquipment.GetSlot(EquipmentSlot.Backpack).ContainedItem);
        _otherFollowerSlotsBefore = SnapshotNonBackpackSlots().ToArray();
    }
    private static string Snapshot(object item) => item is Weapon weapon ? weapon.State : item?.ToString();
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
        s = new Session();
        var bag = s.FollowerEquipment.GetSlot(EquipmentSlot.Backpack);
        var rig = s.FollowerEquipment.GetSlot(EquipmentSlot.TacticalVest);
        var pockets = s.FollowerEquipment.GetSlot(EquipmentSlot.Pockets);
        bag.ContainedItem = "original bag with cargo";
        rig.ContainedItem = "full rig";
        pockets.ContainedItem = "full pockets";
        s.CaptureDraft();
        pitTeam.BigBrain.Actions.GestureCommandAction.Fits = false;
        int before = pitTeam.BigBrain.Actions.GestureCommandAction.Calls;
        bag.ContainedItem = "replacement bag";
        s.Validate();
        bag.ContainedItem = null;
        s.Validate();
        bag.ContainedItem = "original bag with different cargo";
        s.Validate();
        Check(pitTeam.BigBrain.Actions.GestureCommandAction.Calls == before, "backpack replacement, removal and cargo edits skip the check");
        rig.ContainedItem = "different rig cargo";
        Expect(s, "SwapGearMagazineSpace");
        rig.ContainedItem = "full rig";
        pockets.ContainedItem = "different pockets cargo";
        Expect(s, "SwapGearMagazineSpace");
        pockets.ContainedItem = "full pockets";
        var weapon = (Weapon)s.FollowerEquipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem;
        weapon.State = "different-magazine";
        Expect(s, "SwapGearMagazineSpace");
        weapon.State = "original-magazine";
        s.Validate();
        Check(pitTeam.BigBrain.Actions.GestureCommandAction.Calls == before + 3, "fully undone changes do not prevent the backpack exception");
        bag.ContainedItem = "original bag with cargo";
        Expect(s, "SwapGearMagazineSpace");
        bag.ContainedItem = "replacement bag";
        s.Giving = true;
        Expect(s, "SwapGearMagazineSpace");
        s.Giving = false;
        s.FollowerEquipment.HasWeapon = false;
        Expect(s, "SwapGearNeedsWeapon");
        return checks;
    }
}
'@
Add-Type -TypeDefinition $fixture.Replace('/* PRODUCTION */', ($methods -join "`n")) -Language CSharp
$checks = [PlateValidationChecks]::Run()
"PASS: $checks production plate-only donation and backpack-only Swap Gear checks using controlled inventory inputs."

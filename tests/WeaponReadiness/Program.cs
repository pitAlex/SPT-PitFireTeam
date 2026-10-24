using EFT.InventoryLogic;
using pitTeam.Modules;

internal static class Program
{
    static int checks;
    static void Check(bool condition, string reason)
    {
        checks++;
        if (!condition) throw new Exception(reason);
    }

    static void Main()
    {
        FollowerWeaponPrimaryReadiness.RunDeterministicSelfTests();
        foreach (var (caliber, required) in new[]
        {
            ("Caliber9x19PARA",60), ("Caliber1143x23ACP",60), ("Caliber46x30",60),
            ("Caliber57x28",60), ("Caliber545x39",60), ("Caliber556x45NATO",60),
            ("Caliber762x39",60), ("Caliber762x35",60), ("Caliber9x39",40),
            ("Caliber366TKM",40), ("Caliber762x51",30), ("Caliber762x54R",30),
            ("Caliber68x51",30), ("Caliber86x70",20), ("Caliber12g",12),
            ("Caliber20g",12), ("Caliber23x75",12), ("ModCaliber",60), ("",60),
            (" caliber9X39 ",40), ("9x39",40)
        })
        {
            Check(FollowerWeaponAmmoReadinessPolicy.GetRequiredRounds(caliber) == required, $"mapping {caliber}");
            foreach (int capacity in new[] { 5, 20, 30, 60, 100 })
            {
                foreach (int total in new[] { required - 1, required, required + 1 })
                {
                    int loaded = Math.Min(capacity, total);
                    var result = FollowerWeaponPrimaryReadiness.EvaluateFormula(caliber, true, loaded, capacity, new[] { total - loaded });
                    Check(result.Threshold == required && result.TotalContribution == total && result.PrimaryReady == (total >= required), $"detachable {caliber}/{capacity}/{total}");
                    result = FollowerWeaponInternalReadiness.EvaluateFormula(capacity, loaded, new[] { total - loaded }, caliber: caliber);
                    Check(result.Threshold == required && result.TotalContribution == total && result.PrimaryReady == (total >= required), $"internal {caliber}/{capacity}/{total}");
                    result = FollowerWeaponChamberReadiness.EvaluateFormula(capacity, loaded, new[] { total - loaded }, caliber: caliber);
                    Check(result.Threshold == required && result.TotalContribution == total && result.PrimaryReady == (total >= required), $"chamber {caliber}/{capacity}/{total}");
                }
            }
        }

        var inventory = new InventoryController();
        var weapon = new Weapon { AmmoCaliber = "Caliber9x39", Magazine = Mag(8,20,"Caliber9x39") };
        var spare = Mag(9,20,"Caliber9x39"); inventory.Reachable.Add(spare);
        Check(!FollowerWeaponPrimaryReadiness.EvaluateActual(inventory, weapon).PrimaryReady, "VSS 8+9 must fail");
        weapon.AmmoCaliber = "Caliber9x19PARA"; weapon.Magazine = Mag(19,30,weapon.AmmoCaliber);
        inventory.Reachable.Clear(); inventory.Reachable.AddRange(Enumerable.Range(0,3).Select(_ => Mag(20,30,weapon.AmmoCaliber)));
        Check(FollowerWeaponPrimaryReadiness.EvaluateActual(inventory, weapon).PrimaryReady, "PP19 19+60 must pass");
        inventory.Reachable.RemoveAt(2);
        Check(!FollowerWeaponPrimaryReadiness.EvaluateActual(inventory, weapon).PrimaryReady, "59 actual rounds fails");
        weapon.Chambers = new[] { new Slot { ContainedItem = new Ammo { Caliber = weapon.AmmoCaliber } } };
        var chambered = FollowerWeaponPrimaryReadiness.EvaluateActual(inventory, weapon);
        Check(chambered.PrimaryReady && chambered.TotalContribution == 60 && chambered.ChamberRounds == 1, "chamber counts once");
        ((Ammo)weapon.Chambers[0].ContainedItem!).IsUsed = true;
        Check(!FollowerWeaponPrimaryReadiness.EvaluateActual(inventory, weapon).PrimaryReady, "spent chamber does not count");
        inventory.Reachable.Clear(); weapon.Chambers = Array.Empty<Slot>();
        var projected = Mag(41,60,weapon.AmmoCaliber);
        Check(!FollowerWeaponPrimaryReadiness.EvaluateActual(inventory, weapon).PrimaryReady, "cargo absent from fast access");
        Check(FollowerWeaponPrimaryReadiness.EvaluatePlannedProjection(inventory, weapon, new[] { projected, projected }).TotalContribution == 60, "projection deduplicates magazine IDs");
        Check(!FollowerWeaponPrimaryReadiness.EvaluateActual(inventory, weapon).PrimaryReady, "projection never mutates actual inventory");
        inventory.Reachable.Add(projected);
        Check(!FollowerWeaponPrimaryReadiness.EvaluateActual(inventory, weapon, fastAccessMagazineEligibility: _ => false).PrimaryReady, "eligibility gate preserved");
        weapon.Magazine!.Compatible = false;
        Check(!FollowerWeaponPrimaryReadiness.EvaluateActual(inventory, weapon).PrimaryReady, "wrong seated ammo vetoes compatible spare");
        weapon.Magazine = null;
        var missing = FollowerWeaponPrimaryReadiness.EvaluateActual(inventory, weapon);
        Check(!missing.PrimaryReady, "41 rounds insufficient without loaded magazine");
        projected.Count = 60;
        missing = FollowerWeaponPrimaryReadiness.EvaluateActual(inventory, weapon);
        Check(missing.PrimaryReady && missing.RequiresMagazineLoad, "missing magazine requires actual insertion");
        var equipment = inventory.Inventory.Equipment;
        equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem = new SearchableItem { Grids = new[] { new Grid { Fits = false } } };
        equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem = new SearchableItem { Grids = new[] { new Grid { Fits = true } } };
        Check(FollowerWeaponPrimaryReadiness.HasMagazineReloadLandingSpace(equipment, projected), "pocket landing space allowed");
        ((SearchableItem)equipment.GetSlot(EquipmentSlot.Pockets).ContainedItem!).Grids[0].Fits = false;
        Check(!FollowerWeaponPrimaryReadiness.HasMagazineReloadLandingSpace(equipment, projected), "no landing space rejected separately");
        foreach (string caliber in new[] { "Caliber26x75", "Caliber30x29", "Caliber40mmRU", "Caliber40x46" })
        {
            Check(!FollowerWeaponPrimaryReadiness.EvaluateFormula(caliber,true,100,100,new[]{100}).PrimaryReady, "launcher excluded");
            var launcher = FollowerWeaponInternalReadiness.EvaluateFormula(6,6,new[]{6},caliber:caliber);
            Check(launcher.Threshold == 12 && launcher.PrimaryReady, "specialized internal launcher retains two-load policy");
        }
        var shotgun = new Weapon { AmmoCaliber = "Caliber12g", ReloadMode = Weapon.EReloadMode.InternalMagazine, Magazine = Mag(8,8,"Caliber12g") };
        shotgun.Chambers = new[] { new Slot() };
        var reserves = new Ammo { Caliber = "Caliber12g", StackObjectsCount = 4 };
        equipment.GetSlot(EquipmentSlot.Backpack).ContainedItem = reserves;
        Check(FollowerWeaponPrimaryReadiness.EvaluateActual(inventory, shotgun).PrimaryReady, "loose-feed shared dispatch 8+4");
        Check(!FollowerWeaponPrimaryReadiness.EvaluateActual(inventory, shotgun, _=>false).PrimaryReady, "loose reserve eligibility");
        Check(FollowerWeaponLooseFeedReadiness.EvaluateProjected(shotgun,new[]{4}).PrimaryReady, "loose projection uses same target");
        Check(!FollowerWeaponChamberReadiness.IsSupportedChamberWeapon(new GrenadeLauncher { ReloadMode=Weapon.EReloadMode.OnlyBarrel, Chambers=new[]{new Slot()} }), "launcher feed remains unsupported");
        Console.WriteLine($"PASS: {checks} production caliber/readiness checks plus embedded detachable/internal/chamber scenarios.");
    }

    static Magazine Mag(int rounds, int capacity, string caliber) => new() { Count=rounds, MaxCount=capacity, Caliber=caliber };
}

// Controlled EFT boundary doubles; the production policy, scanners, arithmetic and dispatch are linked above.
namespace EFT.InventoryLogic
{
    public enum EquipmentSlot { Pockets,TacticalVest,Backpack,SecuredContainer }
    public enum EErrorHandlingType { Ignore }
    public class Item
    {
        public string Id = Guid.NewGuid().ToString(); public object? CurrentAddress;
        public List<Item> Parents = new();
        public virtual Item CloneItem() => (Item)MemberwiseClone();
        public IEnumerable<Item> GetAllItems() => new[] { this };
        public IEnumerable<Item> GetAllParentItems(bool _) => Parents;
    }
    public class SearchableItem : Item { public Grid[] Grids = Array.Empty<Grid>(); }
    public class Grid { public bool Fits; public Result AddAnywhere(Item _, EErrorHandlingType __) => new() { Succeeded=Fits }; }
    public class Result { public bool Succeeded; }
    public class Slot { public Item? ContainedItem; public bool CanAccept(Item _) => true; }
    public class Ammo : Item { public string Caliber=""; public bool IsUsed; public int StackObjectsCount=1; }
    public class Magazine : Item
    {
        public int Count,MaxCount; public string Caliber=""; public bool Compatible=true;
        public bool CheckCompatibility(Ammo ammo) => Caliber == ammo.Caliber;
    }
    public class Weapon : Item
    {
        public enum EReloadMode { ExternalMagazine,InternalMagazine,OnlyBarrel }
        public EReloadMode ReloadMode; public string AmmoCaliber=""; public Magazine? Magazine; public Slot[] Chambers=Array.Empty<Slot>();
        public int ChamberAmmoCount => Chambers.Count(c=>c.ContainedItem is Ammo a && !a.IsUsed);
        public Magazine? GetCurrentMagazine() => Magazine;
        public Slot GetMagazineSlot() => new();
    }
    public class Revolver : Weapon { }
    public class GrenadeLauncher : Weapon { }
    public class RocketLauncher : Weapon { }
    public class InventoryEquipment
    {
        readonly Dictionary<EquipmentSlot,Slot> slots = new();
        public Slot GetSlot(EquipmentSlot slot) => slots.TryGetValue(slot,out var value) ? value : slots[slot]=new();
    }
    public class Inventory { public InventoryEquipment Equipment=new(); }
    public class InventoryController
    {
        public Inventory Inventory=new(); public List<Magazine> Reachable=new();
        public void GetReachableItemsOfTypeNonAlloc<T>(List<T> result, object? _) => result.AddRange(Reachable.OfType<T>());
    }
}
namespace pitTeam.Modules
{
    internal static class FollowerWeaponMagazineCompatibility
    {
        internal static bool AreLoadedCartridgesCompatible(Weapon weapon, Magazine magazine) => magazine.Compatible && weapon.AmmoCaliber==magazine.Caliber;
    }
    internal static class FollowerWeaponLooseAmmoSupport
    {
        internal static bool IsCartridgeCompatible(Weapon weapon,Ammo ammo) => weapon.AmmoCaliber==ammo.Caliber && !ammo.IsUsed;
        internal static bool IsCompatible(Weapon weapon,Ammo ammo) => IsCartridgeCompatible(weapon,ammo) && !ammo.Parents.OfType<Weapon>().Any();
    }
    internal static class FollowerTacticalAmmoPolicy { internal static void RunDeterministicSelfTests() { } }
    internal static class Logger { internal static void LogInfo(string _) { } }
    internal static class pitFireTeam
    {
        internal static class Log { internal static void LogError(string message) => throw new Exception(message); }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using pitTeam.Modules;

enum EquipmentSlot { FirstPrimaryWeapon, SecondPrimaryWeapon, TacticalVest, ArmorVest, Headwear, FaceCover, Eyewear, Earpiece, Holster, Scabbard, Pockets, Backpack, SecuredContainer, Dogtag, ArmBand }
class Item
{
    public string Id = Guid.NewGuid().ToString();
    public ItemAddress CurrentAddress;
    public ItemAddress Parent
    {
        get { return CurrentAddress ?? throw new Exception("Item has no parent, as with native preset roots"); }
        set { CurrentAddress = value; }
    }
}
class InventoryEquipment : Item { }
class Container { public Item ParentItem; public string ID; }
class ItemAddress { public Container Container; }
static class AccessPolicy
{
    /* SLOTS */
    /* ACCESS */
}

class CommitScope { public bool Mutating; }
static class CommitKnownPolicy
{
    internal static CommitScope Current;
    internal static int AddressCalls;
    internal static bool TreatCommitAddressKnown(ItemAddress address)
    {
        AddressCalls++;
        return Current?.Mutating == true && address != null;
    }
    /* COMMIT KNOWN */
}

class ProvenancePolicy
{
    private enum Kind { Move, Split, Merge, Transfer }
    private Kind _kind;
    private string _item = "source", _other = "target", _created = "split";
    internal ProvenancePolicy(string kind) { _kind = (Kind)Enum.Parse(typeof(Kind), kind); }
    /* PROVENANCE */
}

static class GearSwapFixture
{
    static int _assertions;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        _assertions++;
    }
    static Item Child(Item parent, string slot) => new Item { Parent = Address(parent, slot) };
    static ItemAddress Address(Item parent, string slot) => new ItemAddress { Container = new Container { ParentItem = parent, ID = slot } };

    static void Main()
    {
        var player = new InventoryEquipment();
        var bot = new InventoryEquipment();
        var detachedPreset = new Item();
        Check(!CommitKnownPolicy.TreatCommitItemKnown(detachedPreset), "menu preset root ignored without a session");
        Check(CommitKnownPolicy.AddressCalls == 0, "no address traversal outside a session");
        CommitKnownPolicy.Current = new CommitScope();
        Check(!CommitKnownPolicy.TreatCommitItemKnown(detachedPreset), "inactive draft ignores detached preset");
        Check(CommitKnownPolicy.AddressCalls == 0, "no address traversal before Apply");
        CommitKnownPolicy.Current.Mutating = true;
        Check(!CommitKnownPolicy.TreatCommitItemKnown(detachedPreset), "detached item safe even during commit");
        Check(!CommitKnownPolicy.TreatCommitItemKnown(null), "null item safe during commit");
        var explicitAddress = Address(bot, "TacticalVest");
        Check(CommitKnownPolicy.TreatCommitItemKnown(detachedPreset, explicitAddress), "explicit address takes precedence");
        Check(CommitKnownPolicy.TreatCommitItemKnown(Child(bot, "TacticalVest")), "placed item keeps commit override");
        CommitKnownPolicy.Current = null;
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            var item = Child(bot, slot.ToString());
            Check(AccessPolicy.CanEdit(item, player, bot) == AccessPolicy.VisibleSlots.Contains(slot), "exact follower slot policy " + slot);
            Check(AccessPolicy.CanEdit(Child(player, slot.ToString()), player, bot), "player layout unaffected " + slot);
        }
        Check(AccessPolicy.VisibleSlots.Count == 9, "exactly nine editable follower slots including holster");
        foreach (bool spawned in new[] { false, true })
        {
            var pistol = Child(bot, "Holster");
            Check(AccessPolicy.IsVisibleFollowerSlot(EquipmentSlot.Holster, spawned), "holster visible for spawned=" + spawned);
            Check(AccessPolicy.CanEdit(pistol, player, bot, spawned), "equipped pistol removable for spawned=" + spawned);
            Check(AccessPolicy.CanPlace(Address(bot, "Holster"), player, bot, spawned), "empty holster accepts replacement for spawned=" + spawned);
            Check(AccessPolicy.CanEdit(Child(pistol, "mod_magazine"), player, bot, spawned), "pistol magazine editable for spawned=" + spawned);
        }
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
            Check(AccessPolicy.IsVisibleFollowerSlot(slot, true) ==
                (AccessPolicy.VisibleSlots.Contains(slot) || slot == EquipmentSlot.Backpack),
                "spawned followers expose only the additional backpack slot " + slot);
        Check(!AccessPolicy.IsVisibleFollowerSlot(EquipmentSlot.Backpack, false), "recruited backpack stays hidden");
        Check(AccessPolicy.CanPlace(Address(bot, "Backpack"), player, bot, true), "spawned empty backpack slot accepts a replacement");
        Check(!AccessPolicy.CanPlace(Address(bot, "Backpack"), player, bot, false), "recruited empty backpack slot stays locked");
        var backpack = Child(bot, "Backpack");
        var backpackLoot = Child(backpack, "main");
        var nestedBag = Child(backpack, "main");
        var nestedLoot = Child(nestedBag, "main");
        var sealedBags = new HashSet<string> { backpack.Id };
        Check(AccessPolicy.IsInOpaqueBackpack(backpack, sealedBags), "whole backpack cannot open a contents window");
        Check(AccessPolicy.IsInOpaqueBackpack(nestedBag, sealedBags), "nested container cannot open a contents window");
        Check(AccessPolicy.IsInOpaqueBackpack(nestedLoot, sealedBags), "deep descendants stay in sealed tree");
        Check(!AccessPolicy.IsInOpaqueBackpack(detachedPreset, sealedBags), "detached unrelated item is safe");
        Check(!AccessPolicy.IsInOpaqueBackpack(null, sealedBags), "null item is not sealed");
        Check(AccessPolicy.CanEdit(backpack, player, bot, true, sealedBags), "whole spawned backpack can move");
        Check(!AccessPolicy.CanEdit(backpack, player, bot, false, sealedBags), "whole recruited backpack cannot move");
        Check(!AccessPolicy.CanEdit(backpackLoot, player, bot, true, sealedBags), "backpack loose loot stays locked");
        Check(!AccessPolicy.CanEdit(nestedLoot, player, bot, true, sealedBags), "nested backpack loot stays locked");
        Check(!AccessPolicy.CanPlace(Address(backpack, "main"), player, bot, true, sealedBags), "cannot add to sealed backpack grid");
        Check(!AccessPolicy.CanEdit(backpackLoot, player, bot, true), "follower backpack descendants denied even without registered seal");
        backpack.Parent = Address(player, "Backpack");
        Check(AccessPolicy.CanEdit(backpack, player, bot, true, sealedBags), "removed whole backpack stays movable on player side");
        Check(AccessPolicy.IsInOpaqueBackpack(backpack, sealedBags), "removed whole backpack contents window remains blocked");
        Check(!AccessPolicy.CanEdit(backpackLoot, player, bot, true, sealedBags), "removing backpack cannot bypass contents lock");
        Check(ReferenceEquals(backpackLoot.Parent.Container.ParentItem, backpack), "whole removal leaves child attached to same backpack");
        var playerBag = Child(player, "Backpack");
        var playerBagLoot = Child(playerBag, "main");
        Check(AccessPolicy.CanEdit(playerBagLoot, player, bot, true, sealedBags), "unrelated player backpack contents remain editable");
        // Replay must follow draft chronology, not apply the final seal set to earlier edits.
        Check(AccessPolicy.CanEdit(playerBagLoot, player, bot, true, sealedBags), "player bag can be prepared before handover");
        playerBag.Parent = Address(bot, "Backpack");
        sealedBags.Add(playerBag.Id);
        Check(AccessPolicy.CanEdit(playerBag, player, bot, true, sealedBags), "replacement backpack can be returned as a whole");
        Check(!AccessPolicy.CanEdit(playerBagLoot, player, bot, true, sealedBags), "handed-over replacement contents become locked");
        playerBag.Parent = Address(backpack, "main");
        Check(!AccessPolicy.CanEdit(playerBagLoot, player, bot, true, sealedBags), "nesting a sealed replacement cannot expose contents");
        var vest = Child(bot, "TacticalVest");
        var valuable = Child(vest, "main");
        Check(AccessPolicy.CanEdit(valuable, player, bot), "ordinary vest loot editable");
        Check(AccessPolicy.CanPlace(valuable.Parent, player, bot), "vest accepts loose items");
        Check(AccessPolicy.CanEdit(Child(Child(bot, "ArmorVest"), "plate_front"), player, bot), "individual plate editable");
        Check(!AccessPolicy.CanEdit(Child(Child(bot, "Backpack"), "main"), player, bot), "nested hidden backpack denied");
        Check(!AccessPolicy.CanEdit(Child(Child(bot, "Pockets"), "main"), player, bot), "nested pockets denied");
        Check(!AccessPolicy.CanPlace(Address(bot, "SecuredContainer"), player, bot), "empty hidden destination denied");
        Check(!AccessPolicy.CanEdit(bot, player, bot) && !AccessPolicy.CanEdit(player, player, bot), "equipment roots cannot move");
        Check(!AccessPolicy.CanEdit(Child(new InventoryEquipment(), "TacticalVest"), player, bot), "foreign inventory denied");
        Check(!AccessPolicy.CanEdit(null, player, bot), "null item denied");
        Check(!AccessPolicy.CanPlace(null, player, bot), "world drop denied");
        Check(!AccessPolicy.CanPlace(Address(bot, "unexpected_mod_slot"), player, bot), "unknown mod slot denied");
        Check(!AccessPolicy.CanPlace(Address(new Item(), "main"), null, bot), "unowned address denied");
        var cycle = new Item(); cycle.Parent = Address(cycle, "cycle");
        Check(!AccessPolicy.IsInOpaqueBackpack(cycle, sealedBags), "cyclic container lookup terminates safely");
        Check(!AccessPolicy.CanEdit(cycle, player, bot), "cyclic path terminates safely");

        int state = 0, published = 0;
        var draft = new List<Func<int>> { () => { state++; return 1; }, () => { state++; return 2; } };
        Check(state == 0, "recording draft does not execute edits");
        var applied = GearSwapTransaction.Execute(draft, _ => state--, () => Check(state == 2, "validate complete arrangement"));
        published += applied.Count;
        Check(state == 2 && published == 2, "successful transaction available for publication");

        var rollbackOrder = new List<int>(); state = 0; published = 0;
        try
        {
            GearSwapTransaction.Execute(draft.Concat(new Func<int>[] { () => throw new InvalidOperationException("rejected") }),
                id => { rollbackOrder.Add(id); state--; }, () => { published++; });
            throw new Exception("expected rejection");
        }
        catch (InvalidOperationException) { }
        Check(state == 0 && published == 0, "failed operation rolls back without publishing");
        Check(rollbackOrder.SequenceEqual(new[] { 2, 1 }), "reverse rollback order");
        state = 0;
        try { GearSwapTransaction.Execute(draft, _ => state--, () => { throw new InvalidOperationException("stale final state"); }); }
        catch (InvalidOperationException) { }
        Check(state == 0, "final validation failure rolls back all edits");
        rollbackOrder.Clear(); state = 0;
        try
        {
            GearSwapTransaction.Execute(draft, id => { rollbackOrder.Add(id); if (id == 2) throw new Exception("rollback failure"); },
                () => { throw new InvalidOperationException("validation"); });
        }
        catch (AggregateException ex) { Check(ex.InnerExceptions.Count == 2, "preserve operation and recovery errors"); }
        Check(rollbackOrder.SequenceEqual(new[] { 2, 1 }), "rollback continues after individual recovery failure");
        Check(GearSwapTransaction.Execute(Array.Empty<Func<int>>(), _ => {}, () => {}).Count == 0, "empty draft safe");
        foreach (string kind in new[] { "Merge", "Transfer" })
            foreach (bool sourceOwned in new[] { false, true })
                foreach (bool targetOwned in new[] { false, true })
                {
                    var owned = new HashSet<string>();
                    if (sourceOwned) owned.Add("source");
                    if (targetOwned) owned.Add("target");
                    bool rejected = false;
                    try { new ProvenancePolicy(kind).ValidateProvenance(owned); }
                    catch (InvalidOperationException) { rejected = true; }
                    Check(rejected == (sourceOwned != targetOwned), kind + " preserves stack ownership");
                }
        foreach (bool sourceOwned in new[] { false, true })
        {
            var owned = new HashSet<string>();
            if (sourceOwned) owned.Add("source");
            var split = new ProvenancePolicy("Split");
            split.ValidateProvenance(owned);
            split.UpdateProvenance(owned);
            Check(owned.Contains("split") == sourceOwned, "split inherits source provenance");
        }
        Console.WriteLine("Gear Swap: " + _assertions + " assertions passed.");
    }
}

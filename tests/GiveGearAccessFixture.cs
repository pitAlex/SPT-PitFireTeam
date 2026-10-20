using System;
using System.Linq;
using System.Collections.Generic;

class Item { public string Id; public ItemAddress CurrentAddress; public bool Protected; }
static class InteractableObjects
{
    internal static bool IsProtectedFollowerEquipment(Item item) => item.Protected;
}
class Container { public Item ParentItem; }
class ItemAddress { public Container Container; }
class DonationAccess
{
    Item PlayerEquipment = new Item(), FollowerEquipment = new Item(), Donation = new Item(), _donationOverflow = new Item();
    HashSet<string> _playerOwnedIds = new HashSet<string>();
    Dictionary<string, Item> _draftItems = new Dictionary<string, Item>();
    /* ACCESS */
    static ItemAddress Address(Item parent) => new ItemAddress { Container = new Container { ParentItem = parent } };
    static Item Child(Item parent) => new Item { CurrentAddress = Address(parent) };
    static int checks;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    static void Main()
    {
        var s = new DonationAccess();
        Check(s.CanEditDonation(Child(s.PlayerEquipment)), "Player items may be offered");
        Check(s.CanEditDonation(Child(s.Donation)), "Player can withdraw staged offerings");
        Check(s.CanEditDonation(Child(Child(s.Donation))), "Nested player offerings remain rearrangeable");
        Check(!s.CanEditDonation(s.Donation) && !s.CanEditDonation(s.PlayerEquipment), "Virtual donation root/player equipment cannot be transferred");
        Check(!s.CanEditDonation(Child(s.FollowerEquipment)), "Recruit equipment cannot be stripped");
        Check(!s.CanEditDonation(Child(Child(s.FollowerEquipment))), "Nested recruit inventory cannot be stripped");
        Check(!s.CanEditDonation(Child(s._donationOverflow)), "Hidden displaced gear is not an editor source");
        Check(!s.CanPlaceDonation(Address(s.FollowerEquipment)), "Recruit slots are not editing destinations");
        Check(!s.CanPlaceDonation(Address(Child(s.FollowerEquipment))), "Recruit containers are not editing destinations");
        Check(s.CanPlaceDonation(Address(s.Donation)), "Donation grid accepts player items");
        Check(!s.CanPlaceDonation(Address(s._donationOverflow)), "Hidden planning escrow cannot be opened/filled by UI");
        Check(!s.CanEditDonation(Child(new Item())) && !s.CanPlaceDonation(Address(new Item())), "Foreign roots rejected by object identity");
        Check(!s.CanEditDonation(null) && !s.CanPlaceDonation(null), "Null addresses fail closed");
        var cycle = new Item(); cycle.CurrentAddress = Address(cycle);
        Check(!s.CanEditDonation(cycle), "Cyclic ancestry is bounded and rejected");
        var ordinary = Child(s.PlayerEquipment);
        var protectedGear = Child(s.PlayerEquipment); protectedGear.Protected = true;
        Check(s.CanEditDonation(protectedGear) && s.CanPlaceDonation(Address(s.Donation)), "Protected gear may be staged for temporary use");
        protectedGear.CurrentAddress = Address(Child(s.Donation));
        Check(s.CanEditDonation(protectedGear), "Borrowed gear can be rearranged and withdrawn from the offering");
        ordinary.Id = "gift"; protectedGear.Id = "loan";
        s._draftItems.Add(ordinary.Id, ordinary); s._draftItems.Add(protectedGear.Id, protectedGear);
        s._playerOwnedIds.UnionWith(new[] { ordinary.Id, protectedGear.Id });
        s.PreserveBorrowedGearOwnership();
        Check(s._playerOwnedIds.Contains(ordinary.Id) && !s._playerOwnedIds.Contains(protectedGear.Id),
            "Temporary loan keeps original teammate ownership instead of becoming a permanent gift");
        Check(s.CanEditDonation(protectedGear), "Ownership exclusion does not deny transfer");
        Console.WriteLine("Give Gear production donation access: " + checks + " assertions passed.");
    }
}

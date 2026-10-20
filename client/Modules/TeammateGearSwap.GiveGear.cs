using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;

namespace pitTeam.Modules
{
    internal sealed partial class TeammateGearSwap
    {
        internal readonly bool Giving;
        private bool _planningDonation;
        internal SearchableItem Donation { get; private set; }
        private SearchableItem _donationOverflow;
        private GiveGearPlanner _donationPlan;
        private GiveGearWorldDrops _donationDrops;
        private int _donationEditCount;
        private Dictionary<string, int> _scavBeforeDonation;
        private readonly List<string> _nativeDonationSplitIds = new List<string>();
        private HashSet<string> _offeredDonationIds;
        private HashSet<string> _borrowedDonationIds;
        private void InitializeDonation()
        {
            _playerOwnedIds.UnionWith(AllegianceScavCargo.GetPureGivenIds(Bot));
            foreach (Weapon weapon in Tree(FollowerEquipment).OfType<Weapon>()
                .Where(w => Follower.GivenGearWeaponIds.Contains(w.Id)))
                _playerOwnedIds.UnionWith(Tree(weapon).Select(i => (string)i.Id));
            PreserveBorrowedGearOwnership();
            Donation = GiveGearContainer.Create(MongoID.Generate(true), 10, 12, Bot.Profile.Nickname);
            _donationOverflow = GiveGearContainer.Create(MongoID.Generate(true), 64, 64, Bot.Profile.Nickname);
            _draftItems.Add(Donation.Id, Donation);
            _draftItems.Add(_donationOverflow.Id, _donationOverflow);
        }

        private bool CanPlaceDonation(ItemAddress address)
        {
            for (int depth = 0; address != null && depth < 64; depth++)
            {
                var parent = address.Container.ParentItem;
                if (ReferenceEquals(parent, PlayerEquipment) || ReferenceEquals(parent, Donation)) return true;
                // Existing follower equipment is never an editor source or destination.
                if (ReferenceEquals(parent, FollowerEquipment) || ReferenceEquals(parent, _donationOverflow)) return false;
                address = parent?.CurrentAddress;
            }
            return false;
        }

        private bool CanEditDonation(Item item) => item != null && !ReferenceEquals(item, Donation) &&
            !ReferenceEquals(item, PlayerEquipment) && CanPlaceDonation(item.CurrentAddress);

        private void PreserveBorrowedGearOwnership()
        {
            // Usable in this raid, but still owned by the original saved teammate.
            _playerOwnedIds.RemoveWhere(id => _draftItems.TryGetValue(id, out Item item) &&
                InteractableObjects.IsProtectedFollowerEquipment(item));
        }

        private void PrepareDonation()
        {
            _donationEditCount = _edits.Count;
            PreserveBorrowedGearOwnership();
            _scavBeforeDonation = AllegianceScavCargo.CaptureCommandedLoot(Bot);
            _offeredDonationIds = new HashSet<string>(Tree(Donation).Where(i => _playerOwnedIds.Contains(i.Id)).Select(i => (string)i.Id));
            _borrowedDonationIds = new HashSet<string>(Tree(Donation).Where(i => !ReferenceEquals(i, Donation) &&
                !_playerOwnedIds.Contains(i.Id)).Select(i => (string)i.Id));
            _donationPlan = new GiveGearPlanner(BotDraft, Donation, _donationOverflow, Bot,
                edit => { _edits.Add(edit); edit.UpdateProvenance(_playerOwnedIds); },
                _playerOwnedIds, item => _draftItems[item.Id] = item,
                id => _nativeDonationSplitIds.Add(id));
            _planningDonation = true;
            try { _donationPlan.Build(); }
            finally { _planningDonation = false; }
            foreach (Item item in Tree(PlayerEquipment).Concat(Tree(FollowerEquipment)).Concat(Tree(Donation)).Concat(Tree(_donationOverflow)))
                _draftItems[item.Id] = item;
        }

        private void UndoDonationPlan()
        {
            if (_donationPlan == null) return;
            _donationPlan.Rollback();
            if (_edits.Count > _donationEditCount) _edits.RemoveRange(_donationEditCount, _edits.Count - _donationEditCount);
            _donationPlan = null;
            _nativeDonationSplitIds.Clear();
        }

        private bool CanReplayDonation(Item item, ItemAddress destination, Dictionary<string, Item> items)
        {
            if (!Giving) return false;
            return item != null ? items.TryGetValue(item.Id, out var found) && ReferenceEquals(found, item) &&
                item.Id != Donation.Id && item.Id != _donationOverflow.Id &&
                item.Id != PlayerEquipment.Id && item.Id != FollowerEquipment.Id :
                destination != null && items.ContainsKey(destination.Container.ParentItem.Id);
        }

        private void TrackDonation()
        {
            var live = Bot.GetPlayer.InventoryController.Inventory.Equipment;
            // PMC gifts remain with the recruit/invitation. Scavs return only exact donated trees.
            AllegianceScavCargo.RegisterNonReturnableIds(Bot, _nativeDonationSplitIds.Concat(_borrowedDonationIds));
            AllegianceScavCargo.TrackCommandedLoot(Bot, live, _scavBeforeDonation);
            foreach (EquipmentSlot slot in GiveGearPlanner.EquipmentSlots)
            {
                var root = live.GetSlot(slot)?.ContainedItem;
                if (Bot.Side != EPlayerSide.Savage && root != null && _playerOwnedIds.Contains(root.Id) &&
                    (_offeredDonationIds.Contains(root.Id) || Follower.GivenGearWeaponIds.Contains(root.Id)))
                    Follower.GivenEquipmentSlots.Add(slot);
            }
            foreach (Weapon weapon in Tree(live).OfType<Weapon>().Where(w => _donationPlan.OfferedWeaponIds.Contains(w.Id)))
                Follower.GivenGearWeaponIds.Add(weapon.Id);
            foreach (Weapon weapon in FirearmSlots.Select(s => live.GetSlot(s)?.ContainedItem).OfType<Weapon>())
            {
                InteractableObjects.RegisterLootedWeaponTree(Bot, weapon);
                var magazineSlot = weapon.GetMagazineSlot();
                if (magazineSlot == null) continue;
                foreach (var root in new[] { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets })
                    if (live.GetSlot(root)?.ContainedItem is SearchableItem container)
                        foreach (var magazine in container.Grids.SelectMany(g => g.Items).OfType<Magazine>())
                            if (magazineSlot.CanAccept(magazine))
                                InteractableObjects.RegisterLootedWeaponMagazine(Bot, weapon, magazine);
            }
        }

        private void TrackDonationInsurance()
        {
            if (Bot.Side != EPlayerSide.Usec && Bot.Side != EPlayerSide.Bear) return;
            GiveGearInsurance.Track(Owner.Session as IEftSession, Owner.Player, Bot,
                Tree(Bot.GetPlayer.InventoryController.Inventory.Equipment)
                    .Where(i => _offeredDonationIds.Contains(i.Id) && !InteractableObjects.IsProtectedFollowerEquipment(i)).Select(i => (string)i.Id));
        }

        private void AddLiveDonationContainers(Dictionary<string, Item> items)
        {
            items.Add(Donation.Id, GiveGearContainer.Create(Donation.Id, 10, 12, Bot.Profile.Nickname));
            items.Add(_donationOverflow.Id, GiveGearContainer.Create(_donationOverflow.Id, 64, 64, Bot.Profile.Nickname));
        }

        private bool CanCommitDonationAddress(ItemAddress address)
        {
            for (int depth = 0; address != null && depth < 64; depth++)
            {
                Item parent = address.Container.ParentItem;
                if (ReferenceEquals(parent, Owner.Player.InventoryController.Inventory.Equipment) ||
                    ReferenceEquals(parent, Bot.GetPlayer.InventoryController.Inventory.Equipment) ||
                    (parent != null && (parent.Id == Donation.Id || parent.Id == _donationOverflow.Id))) return true;
                address = parent?.CurrentAddress;
            }
            return false;
        }

        private void PrepareDonationDrops(Dictionary<string, Item> items)
        {
            var offer = (SearchableItem)items[Donation.Id];
            var overflow = (SearchableItem)items[_donationOverflow.Id];
            if (offer.Grids.Any(g => g.Items.Any()) || Snapshot(overflow) != Snapshot(_donationOverflow))
                throw new InvalidOperationException("Donation escrow replay mismatch.");
            _donationDrops = GiveGearWorldDrops.Prepare(Bot.GetPlayer, overflow.Grids.SelectMany(g => g.Items).ToArray());
            try
            {
                _applyScope.Preparation.ThrowIfCancellationRequested();
                if (UnsafeReason(Owner, Bot, Follower, applying: true, giving: true) != null)
                    throw new InvalidOperationException("Donation actors changed during world-owner preparation.");
            }
            catch { _donationDrops.Rollback(); _donationDrops = null; throw; }
        }
    }

    internal static class GiveGearHistory
    {
        internal static IEnumerable<Weapon> CarriedWeapons(BotOwner bot, InventoryEquipment kit)
        {
            var follower = BossPlayers.Instance?.GetFollower(bot);
            return follower == null ? Enumerable.Empty<Weapon>() : TeammateGearSwap.Tree(kit).OfType<Weapon>()
                .Where(w => follower.GivenGearWeaponIds.Contains(w.Id) && (w.CurrentAddress is GridItemAddress ||
                    ReferenceEquals(w.CurrentAddress?.Container.ParentItem, kit)));
        }
    }

    internal static class GiveGearContainer
    {
        // Private, session-owned template. Never insert it into ItemFactory or a profile.
        internal static SearchableItem Create(string id, int width, int height, string name)
        {
            var template = new SearchableItemTemplate
            {
                _id = "00000000000000000000f17e", Name = name, Weight = 0,
                CanPutIntoDuringTheRaid = true
            };
            var root = new SearchableItem(id, template);
            root.Grids = new Grid[] { new SearchableGrid(new Grid("main", width, height, false, false,
                new ItemFilter[0], root), root) };
            new ItemController(root, id, name, false);
            return root;
        }
    }
}

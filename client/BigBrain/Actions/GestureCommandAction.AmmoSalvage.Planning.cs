using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private bool TryPlanMagazineAmmoSalvage(
            InventoryEquipment followerEquipment,
            BodyGearCandidate marker,
            out List<BodyGearCandidate> transfers,
            out string reason)
        {
            transfers = new List<BodyGearCandidate>();
            reason = "invalidMarker";
            if (followerEquipment == null ||
                marker?.FollowUpDestination != BodyGearFollowUpDestination.SalvageMagazineAmmo ||
                marker.Item is not MagazineItemClass magazine ||
                marker.AmmoSalvageWeapon is not Weapon weapon)
            {
                return false;
            }

            if (!IsWeaponEquippedAsPrimaryForAmmoSalvage(followerEquipment, weapon))
            {
                reason = "weaponNotPrimary";
                return false;
            }

            if (IsLootNowInBotInventory(BotOwner?.GetPlayer, magazine))
            {
                reason = "magazineAlreadyMoved";
                return false;
            }

            if (magazine.Count <= 0 ||
                IsMagazineInstalledInWeapon(magazine) ||
                !IsMagazineCompatibleWithWeapon(weapon, magazine))
            {
                reason = magazine.Count <= 0 ? "magazineEmpty" : "magazineNoLongerEligible";
                return false;
            }

            // StackSlot can only remove its last item. Plan in vanilla unload order and consolidate
            // separated runs of the same ammo type into the output stack already moved earlier.
            List<AmmoItemClass> ammoStacks = magazine.Cartridges.Items
                .OfType<AmmoItemClass>()
                .Where(ammo => ammo != null && ammo.StackObjectsCount > 0)
                .Reverse()
                .ToList();
            if (ammoStacks.Count == 0)
            {
                reason = "ammoStacksMissing";
                return false;
            }

            SearchableItemItemClass simulatedSecure = CloneEquipmentContainer(
                followerEquipment,
                EquipmentSlot.SecuredContainer);
            SearchableItemItemClass simulatedPockets = CloneEquipmentContainer(
                followerEquipment,
                EquipmentSlot.Pockets);
            SearchableItemItemClass simulatedBackpack = CloneEquipmentContainer(
                followerEquipment,
                EquipmentSlot.Backpack);
            SearchableItemItemClass simulatedVest = CloneEquipmentContainer(
                followerEquipment,
                EquipmentSlot.TacticalVest);
            VestReloadReserveSet vestReloadReserves = FindVestReloadReserves(
                followerEquipment,
                simulatedVest);
            // Consolidate only output created from this source magazine. Merging raid-earned
            // rounds into a pre-existing follower stack would erase quantity ownership needed by
            // Simple/Restricted post-raid cargo return.
            Dictionary<string, List<AmmoSalvageOutputStack>> outputStacksByAmmo =
                new Dictionary<string, List<AmmoSalvageOutputStack>>(StringComparer.Ordinal);

            foreach (AmmoItemClass ammoStack in ammoStacks)
            {
                string ammoKey = GetAmmoSalvageStackKey(ammoStack);
                if (!outputStacksByAmmo.TryGetValue(ammoKey, out List<AmmoSalvageOutputStack> outputStacks))
                {
                    outputStacks = new List<AmmoSalvageOutputStack>();
                    outputStacksByAmmo.Add(ammoKey, outputStacks);
                }

                int remaining = ammoStack.StackObjectsCount;
                int outputStackMax = Math.Max(1, ammoStack.StackMaxSize);
                AmmoSalvageOutputStack openStack = outputStacks
                    .FirstOrDefault(stack => stack.Count < stack.MaxCount);
                if (openStack != null)
                {
                    int transferCount = Math.Min(remaining, openStack.MaxCount - openStack.Count);
                    transfers.Add(CreateAmmoSalvageTransferCandidate(
                        marker,
                        ammoStack,
                        weapon,
                        magazine,
                        BodyGearFollowUpDestination.SalvagedAmmoStackTransfer,
                        openStack.Target,
                        transferCount));
                    openStack.Count += transferCount;
                    remaining -= transferCount;
                }

                if (remaining <= 0)
                {
                    continue;
                }

                // StackSlot may contain a generated cartridge group larger than the ammo
                // template's normal stack limit. Reserve one physical cell for every full split
                // before reserving the final source stack, so a large drum cannot create an
                // illegal over-sized loose-ammo stack or begin unloading without enough room.
                while (remaining > outputStackMax)
                {
                    if (!TryPlanAmmoStackDestination(
                            ammoStack,
                            vestReloadReserves,
                            ref simulatedSecure,
                            ref simulatedPockets,
                            ref simulatedBackpack,
                            ref simulatedVest,
                            out BodyGearFollowUpDestination splitDestination))
                    {
                        transfers.Clear();
                        reason = $"noRoomForStack:{ammoStack.TemplateId}:{remaining}";
                        return false;
                    }

                    transfers.Add(CreateAmmoSalvageTransferCandidate(
                        marker,
                        ammoStack,
                        weapon,
                        magazine,
                        splitDestination,
                        transferCount: outputStackMax));
                    remaining -= outputStackMax;
                }

                if (!TryPlanAmmoStackDestination(
                        ammoStack,
                        vestReloadReserves,
                        ref simulatedSecure,
                        ref simulatedPockets,
                        ref simulatedBackpack,
                        ref simulatedVest,
                        out BodyGearFollowUpDestination destination))
                {
                    transfers.Clear();
                    reason = $"noRoomForStack:{ammoStack.TemplateId}:{remaining}";
                    return false;
                }

                transfers.Add(CreateAmmoSalvageTransferCandidate(
                    marker,
                    ammoStack,
                    weapon,
                    magazine,
                    destination,
                    transferCount: remaining));
                outputStacks.Add(new AmmoSalvageOutputStack(
                    ammoStack,
                    remaining,
                    outputStackMax));
            }

            reason = "planned";
            int outputStackCount = transfers.Count(transfer =>
                transfer.FollowUpDestination != BodyGearFollowUpDestination.SalvagedAmmoStackTransfer);
            Modules.Logger.LogInfo(
                $"[LootCommand][AmmoSalvage] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} magazine={DescribeLootDebugItem(magazine)} " +
                $"cartridgeGroups={ammoStacks.Count} outputStacks={outputStackCount} transactions={transfers.Count} " +
                $"reserveLongGun={DescribeLootDebugItem(vestReloadReserves.LongGunMagazine)} " +
                $"reserveInitialHolster={DescribeLootDebugItem(vestReloadReserves.InitialHolsterMagazine)} " +
                $"destinations={string.Join(",", transfers.Select(transfer => transfer.FollowUpDestination.ToString()))}");
            return true;
        }

        private static BodyGearCandidate CreateAmmoSalvageTransferCandidate(
            BodyGearCandidate marker,
            AmmoItemClass ammo,
            Weapon weapon,
            MagazineItemClass magazine,
            BodyGearFollowUpDestination destination,
            AmmoItemClass? targetStack = null,
            int transferCount = 0)
        {
            return new BodyGearCandidate(
                    ammo,
                    null,
                    $"{marker.SourceName}.LeftBehindMagazineAmmo",
                    marker.SourceTier,
                    bypassPriceThreshold: true,
                    bypassCategoryFilter: true,
                    bypassBodyGearLootability: true,
                    reportAsLootNothing: true)
                .WithAmmoSalvageContext(
                    destination,
                    weapon,
                    magazine,
                    targetStack,
                    transferCount);
        }

        private static string GetAmmoSalvageStackKey(AmmoItemClass ammo)
        {
            return $"{ammo?.TemplateId ?? "unknown"}:{ammo?.IsUsed == true}";
        }

        private static SearchableItemItemClass? CloneEquipmentContainer(
            InventoryEquipment equipment,
            EquipmentSlot slot)
        {
            return CloneSearchableContainer(equipment?.GetSlot(slot)?.ContainedItem);
        }

        private static bool TryPlanAmmoStackDestination(
            AmmoItemClass ammoStack,
            VestReloadReserveSet vestReloadReserves,
            ref SearchableItemItemClass? secure,
            ref SearchableItemItemClass? pockets,
            ref SearchableItemItemClass? backpack,
            ref SearchableItemItemClass? vest,
            out BodyGearFollowUpDestination destination)
        {
            destination = BodyGearFollowUpDestination.Default;
            // Unloaded rounds use the same carry policy as loose source ammo: preserve the
            // largest reload-safe vest space first, then fall back through slower storage.
            if (TrySimulateContainerAdd(vest, ammoStack, out SearchableItemItemClass? nextVest) &&
                CanFitVestReloadReserves(nextVest, vestReloadReserves))
            {
                vest = nextVest;
                destination = BodyGearFollowUpDestination.SalvagedAmmoVest;
                return true;
            }

            if (TrySimulateContainerAdd(pockets, ammoStack, out SearchableItemItemClass? nextPockets))
            {
                pockets = nextPockets;
                destination = BodyGearFollowUpDestination.SalvagedAmmoPockets;
                return true;
            }

            if (TrySimulateContainerAdd(backpack, ammoStack, out SearchableItemItemClass? nextBackpack))
            {
                backpack = nextBackpack;
                destination = BodyGearFollowUpDestination.SalvagedAmmoBackpack;
                return true;
            }

            if (TrySimulateContainerAdd(secure, ammoStack, out SearchableItemItemClass? nextSecure))
            {
                secure = nextSecure;
                destination = BodyGearFollowUpDestination.SalvagedAmmoSecuredContainer;
                return true;
            }

            return false;
        }

        private VestReloadReserveSet FindVestReloadReserves(
            InventoryEquipment equipment,
            SearchableItemItemClass? vest)
        {
            if (equipment == null || vest == null)
            {
                return new VestReloadReserveSet(null, null);
            }

            List<MagazineItemClass> fastAccessMagazines = GetFastAccessMagazines(equipment);
            List<Weapon> longGuns = LongGunSlotsForAmmoReserve
                .Select(slot => equipment.GetSlot(slot)?.ContainedItem as Weapon)
                .OfType<Weapon>()
                .ToList();
            List<MagazineItemClass> longGunCandidates = longGuns
                .Select(GetCurrentMagazineSafely)
                .OfType<MagazineItemClass>()
                .ToList();
            longGunCandidates.AddRange(fastAccessMagazines.Where(magazine =>
                longGuns.Any(weapon => IsMagazineCompatibleWithWeapon(weapon, magazine))));

            MagazineItemClass longGunReserve = SelectLargestVestCompatibleMagazine(
                vest,
                longGunCandidates);

            MagazineItemClass initialHolsterReserve = null;
            Weapon holsterWeapon = equipment.GetSlot(EquipmentSlot.Holster)?.ContainedItem as Weapon;
            if (holsterWeapon != null && followerData?.IsInitialHolsterWeapon(holsterWeapon) == true)
            {
                List<MagazineItemClass> holsterCandidates = new List<MagazineItemClass>();
                MagazineItemClass insertedHolsterMagazine = GetCurrentMagazineSafely(holsterWeapon);
                if (insertedHolsterMagazine != null)
                {
                    holsterCandidates.Add(insertedHolsterMagazine);
                }

                holsterCandidates.AddRange(fastAccessMagazines.Where(magazine =>
                    IsMagazineCompatibleWithWeapon(holsterWeapon, magazine)));
                initialHolsterReserve = SelectLargestVestCompatibleMagazine(
                    vest,
                    holsterCandidates);
            }

            return new VestReloadReserveSet(longGunReserve, initialHolsterReserve);
        }

        private static List<MagazineItemClass> GetFastAccessMagazines(InventoryEquipment equipment)
        {
            List<MagazineItemClass> magazines = new List<MagazineItemClass>();
            foreach (EquipmentSlot slot in new[] { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets })
            {
                Item root = equipment?.GetSlot(slot)?.ContainedItem;
                if (root != null)
                {
                    magazines.AddRange(SnapshotLootTreeItems(root).OfType<MagazineItemClass>());
                }
            }

            return magazines
                .Where(magazine =>
                    magazine != null &&
                    !string.IsNullOrEmpty(magazine.Id) &&
                    !IsMagazineInstalledInWeapon(magazine))
                .GroupBy(magazine => magazine.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
        }

        private static MagazineItemClass? GetCurrentMagazineSafely(Weapon weapon)
        {
            try
            {
                return weapon?.GetCurrentMagazine();
            }
            catch
            {
                return null;
            }
        }

        private static MagazineItemClass? SelectLargestVestCompatibleMagazine(
            SearchableItemItemClass vest,
            IEnumerable<MagazineItemClass> candidates)
        {
            return candidates
                .Where(candidate =>
                    candidate != null &&
                    !string.IsNullOrEmpty(candidate.Id) &&
                    CanStructurallyFitInVest(vest, candidate))
                .GroupBy(candidate => candidate.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderByDescending(GetMagazineCellArea)
                .ThenByDescending(GetMagazineLongestSide)
                .ThenByDescending(candidate => candidate.MaxCount)
                .FirstOrDefault();
        }

        private static bool CanFitVestReloadReserves(
            SearchableItemItemClass vest,
            VestReloadReserveSet reserves)
        {
            List<MagazineItemClass> orderedReserves = reserves.Magazines
                .OrderByDescending(GetMagazineCellArea)
                .ThenByDescending(GetMagazineLongestSide)
                .ToList();
            if (CanFitVestReloadReserveOrder(vest, orderedReserves))
            {
                return true;
            }

            // With two differently shaped landing magazines, the opposite placement order can
            // fit a segmented vest even when the largest-first greedy placement cannot.
            return orderedReserves.Count == 2 &&
                   CanFitVestReloadReserveOrder(vest, orderedReserves.AsEnumerable().Reverse());
        }

        private static bool CanFitVestReloadReserveOrder(
            SearchableItemItemClass vest,
            IEnumerable<MagazineItemClass> reserves)
        {
            SearchableItemItemClass currentVest = vest;
            foreach (MagazineItemClass reserve in reserves)
            {
                if (!TrySimulateContainerAdd(
                        currentVest,
                        reserve,
                        out SearchableItemItemClass? nextVest))
                {
                    return false;
                }

                currentVest = nextVest;
            }

            return true;
        }

        private static bool CanStructurallyFitInVest(
            SearchableItemItemClass vest,
            MagazineItemClass magazine)
        {
            if (vest?.Grids == null || magazine == null)
            {
                return false;
            }

            XYCellSizeStruct size;
            try
            {
                size = magazine.CalculateCellSize();
            }
            catch
            {
                return false;
            }

            foreach (StashGridClass grid in vest.Grids)
            {
                if (grid == null)
                {
                    continue;
                }

                bool compatible;
                try
                {
                    compatible = grid.CheckCompatibility(magazine);
                }
                catch
                {
                    compatible = false;
                }

                if (!compatible)
                {
                    continue;
                }

                bool horizontalFit = size.X <= grid.GridWidth && size.Y <= grid.GridHeight;
                bool verticalFit = size.Y <= grid.GridWidth && size.X <= grid.GridHeight;
                if (horizontalFit || verticalFit)
                {
                    return true;
                }
            }

            // An inserted 2x2 drum in a vest made only from 1x2 grids lands in backpack or on the
            // ground during reload. Reserving impossible vest geometry would reject useful ammo.
            return false;
        }

        private static int GetMagazineCellArea(MagazineItemClass magazine)
        {
            try
            {
                XYCellSizeStruct size = magazine.CalculateCellSize();
                return size.X * size.Y;
            }
            catch
            {
                return 0;
            }
        }

        private static int GetMagazineLongestSide(MagazineItemClass magazine)
        {
            try
            {
                XYCellSizeStruct size = magazine.CalculateCellSize();
                return Math.Max(size.X, size.Y);
            }
            catch
            {
                return 0;
            }
        }

        private static bool IsWeaponEquippedAsPrimaryForAmmoSalvage(
            InventoryEquipment equipment,
            Weapon weapon)
        {
            // Ammo from magazines left at the source supports a weapon the follower has actually
            // adopted. Secondary, holster, and cargo placements must not consume ammunition merely
            // because that weapon could become useful in a later phase.
            return equipment != null &&
                   weapon != null &&
                   IsSameLootItem(
                       equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem,
                       weapon);
        }

        private sealed class VestReloadReserveSet
        {
            public VestReloadReserveSet(
                MagazineItemClass? longGunMagazine,
                MagazineItemClass? initialHolsterMagazine)
            {
                LongGunMagazine = longGunMagazine;
                InitialHolsterMagazine = initialHolsterMagazine;
            }

            public MagazineItemClass? LongGunMagazine { get; }
            public MagazineItemClass? InitialHolsterMagazine { get; }

            public IEnumerable<MagazineItemClass> Magazines
            {
                get
                {
                    if (LongGunMagazine != null)
                    {
                        yield return LongGunMagazine;
                    }

                    if (InitialHolsterMagazine != null)
                    {
                        // Initial-kit pistols keep their own landing footprint in addition to the
                        // long-gun reserve. A raid-acquired holster weapon gets no such privilege.
                        yield return InitialHolsterMagazine;
                    }
                }
            }
        }

        private sealed class AmmoSalvageOutputStack
        {
            public AmmoSalvageOutputStack(AmmoItemClass target, int count, int maxCount)
            {
                Target = target;
                Count = count;
                MaxCount = maxCount;
            }

            public AmmoItemClass Target { get; }
            public int Count { get; set; }
            public int MaxCount { get; }
        }
    }
}

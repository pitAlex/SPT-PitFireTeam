using EFT.InventoryLogic;
using pitTeam.Modules;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private static readonly EquipmentSlot[] LongGunSlotsForAmmoReserve =
        {
            EquipmentSlot.FirstPrimaryWeapon,
            EquipmentSlot.SecondPrimaryWeapon
        };

        // Simulated ammo-unload operations create a new loose stack id. Later planned cartridge
        // groups use this map to find that live stack after the seed transaction completes.
        private readonly Dictionary<string, string> ammoSalvageReplacementItemIds =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private void AppendOverflowMagazineAmmoSalvageMarkers(
            List<BodyGearCandidate> followUps,
            Weapon weapon,
            IEnumerable<BodyGearCandidate>? compatibleMagazineCandidates)
        {
            if (followUps == null || weapon == null || compatibleMagazineCandidates == null)
            {
                return;
            }

            HashSet<string> addedMagazineIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyGearCandidate candidate in compatibleMagazineCandidates)
            {
                if (candidate?.Item is not MagazineItemClass magazine ||
                    string.IsNullOrEmpty(magazine.Id) ||
                    magazine.Count <= 0 ||
                    IsLootNowInBotInventory(BotOwner?.GetPlayer, magazine) ||
                    !addedMagazineIds.Add(magazine.Id))
                {
                    continue;
                }

                // Every source magazine gets a late marker. Magazines successfully loaded or
                // moved to fast access are in follower inventory by then and the marker becomes a
                // no-op; only magazines the accepted weapon plan truly left behind are unloaded.
                followUps.Add(candidate.WithAmmoSalvageContext(
                    BodyGearFollowUpDestination.SalvageMagazineAmmo,
                    weapon,
                    magazine));
            }
        }

        private AmmoSalvageFollowUpResult HandleAmmoSalvageFollowUp(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            Queue<BodyGearCandidate> pendingFollowUps,
            HashSet<string> attemptedItemIds,
            bool bodyContext)
        {
            string context = bodyContext ? "body" : "container";
            if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.SalvageMagazineAmmo)
            {
                bool queued = TryPlanMagazineAmmoSalvage(
                    followerEquipment,
                    candidate,
                    out List<BodyGearCandidate> transfers,
                    out string reason);
                bool claimLeftBehindMagazine = queued ||
                    string.Equals(reason, "ammoStacksMissing", StringComparison.Ordinal) ||
                    reason.StartsWith("noRoomForStack:", StringComparison.Ordinal);
                if (claimLeftBehindMagazine && candidate.Item != null)
                {
                    attemptedItemIds.Add(candidate.Item.Id);
                }

                if (queued)
                {
                    PrependFollowUps(pendingFollowUps, transfers);
                }

                Modules.Logger.LogInfo(
                    $"[LootCommand][AmmoSalvage] {context} marker follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"magazine={DescribeLootDebugItem(candidate?.Item)} queued={queued} reason={reason}");
                return AmmoSalvageFollowUpResult.Continue;
            }

            if (!IsAmmoSalvageTransfer(candidate))
            {
                return AmmoSalvageFollowUpResult.NotHandled;
            }

            if (!TryBuildAmmoSalvageMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out BodyGearMove? move,
                    out string moveReason))
            {
                RemovePendingAmmoSalvageTransfers(
                    pendingFollowUps,
                    candidate.AmmoSalvageMagazine?.Id);
                Modules.Logger.LogInfo(
                    $"[LootCommand][AmmoSalvage] {context} transfer stopped follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"magazine={DescribeLootDebugItem(candidate.AmmoSalvageMagazine)} " +
                    $"ammo={DescribeLootDebugItem(candidate.Item)} reason={moveReason}");
                return AmmoSalvageFollowUpResult.Continue;
            }

            attemptedItemIds.Add(candidate.Item.Id);
            if (bodyContext)
            {
                StartBodyGearMove(inventory, move);
            }
            else
            {
                StartContainerLootMove(inventory, move);
            }

            return AmmoSalvageFollowUpResult.MoveStarted;
        }

        private static bool IsAmmoSalvageTransfer(BodyGearCandidate candidate)
        {
            return candidate != null &&
                   (candidate.FollowUpDestination == BodyGearFollowUpDestination.SalvagedAmmoStackTransfer ||
                    TryGetAmmoSalvageEquipmentSlot(candidate.FollowUpDestination, out _));
        }

        private static void PrependFollowUps(
            Queue<BodyGearCandidate> queue,
            IReadOnlyList<BodyGearCandidate> candidates)
        {
            if (queue == null || candidates == null || candidates.Count == 0)
            {
                return;
            }

            List<BodyGearCandidate> remainder = queue.ToList();
            queue.Clear();
            foreach (BodyGearCandidate candidate in candidates)
            {
                queue.Enqueue(candidate);
            }

            foreach (BodyGearCandidate candidate in remainder)
            {
                queue.Enqueue(candidate);
            }
        }

        private static void RemovePendingAmmoSalvageTransfers(
            Queue<BodyGearCandidate> queue,
            string? magazineId)
        {
            if (queue == null || string.IsNullOrEmpty(magazineId))
            {
                return;
            }

            List<BodyGearCandidate> keep = queue
                .Where(candidate =>
                    !IsAmmoSalvageTransfer(candidate) ||
                    !string.Equals(candidate.AmmoSalvageMagazine?.Id, magazineId, StringComparison.Ordinal))
                .ToList();
            queue.Clear();
            foreach (BodyGearCandidate candidate in keep)
            {
                queue.Enqueue(candidate);
            }
        }

        private void RegisterAmmoSalvageTargetReplacement(BodyGearMove move, Item completedItem)
        {
            string sourceId = move?.AmmoSalvageReplacementSourceId;
            string targetId = completedItem?.Id;
            if (string.IsNullOrEmpty(sourceId) ||
                string.IsNullOrEmpty(targetId) ||
                string.Equals(sourceId, targetId, StringComparison.Ordinal))
            {
                return;
            }

            ammoSalvageReplacementItemIds[sourceId] = targetId;
        }

        private AmmoItemClass? ResolveAmmoSalvageTargetStack(
            InventoryController inventory,
            AmmoItemClass plannedTarget)
        {
            if (inventory == null || plannedTarget == null || string.IsNullOrEmpty(plannedTarget.Id))
            {
                return null;
            }

            string targetId = plannedTarget.Id;
            HashSet<string> visitedIds = new HashSet<string>(StringComparer.Ordinal);
            while (visitedIds.Add(targetId) &&
                   ammoSalvageReplacementItemIds.TryGetValue(targetId, out string replacementId) &&
                   !string.IsNullOrEmpty(replacementId))
            {
                targetId = replacementId;
            }

            return inventory.TryFindItem(targetId, out Item liveItem)
                ? liveItem as AmmoItemClass
                : null;
        }

        private void ClearAmmoSalvageRuntimeState()
        {
            ammoSalvageReplacementItemIds.Clear();
        }

        private enum AmmoSalvageFollowUpResult
        {
            NotHandled,
            Continue,
            MoveStarted
        }

    }
}

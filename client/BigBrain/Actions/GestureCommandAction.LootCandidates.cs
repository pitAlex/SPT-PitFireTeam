using Comfort.Common;
using Diz.LanguageExtensions;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using EFT.UI;
using EFT.UI.DragAndDrop;
using JsonType;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Patches;
using pitTeam.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private IEnumerable<BodyGearCandidate> GetBodyGearCandidates(InventoryEquipment corpseEquipment)
        {
            // Teammate recovery keeps whole equipment pieces first, then loose contents. This is not
            // the filtered enemy-body search path.
            foreach (EquipmentSlot slot in BodyGearTopLevelSlotOrder)
            {
                Item item = corpseEquipment.GetSlot(slot)?.ContainedItem;
                if (item != null)
                {
                    yield return new BodyGearCandidate(item, slot, slot.ToString(), 0);
                }
            }

            foreach (EquipmentSlot slot in BodyGearContentSlotOrder)
            {
                Item root = corpseEquipment.GetSlot(slot)?.ContainedItem;
                if (root is not CompoundItem compound ||
                    (slot != EquipmentSlot.Pockets && root is SearchableItemItemClass))
                {
                    continue;
                }

                List<Item> contents = new List<Item>();
                compound.GetAllAssembledItems(contents);

                foreach (Item item in contents
                             .Where(item => item != null && item != root && item is not SearchableItemItemClass)
                             .OrderByDescending(GetBodyGearContentPriority)
                             .ThenByDescending(GetItemArea)
                             .ThenByDescending(item => item.Template?.CreditsPrice ?? 0))
                {
                    yield return new BodyGearCandidate(item, null, $"{slot}.Contents", 1);
                }
            }
        }

        private IEnumerable<BodyGearCandidate> GetFilteredBodyLootCandidates(InventoryEquipment corpseEquipment)
        {
            // Enemy PMC dogtags are always attempted, but report as "nothing" for voice feedback so
            // dogtag-only searches do not sound like useful loot was found.
            if (TryCreateNonTeammatePmcDogtagCandidate(corpseEquipment, out BodyGearCandidate dogtagCandidate))
            {
                yield return dogtagCandidate;
            }

            // Enemy body content order is backpack, pockets, worn equipment, then weapons.
            // A wearable is considered as one tree before its eligible fallback contents.
            foreach (BodyGearCandidate candidate in GetStorageLootCandidates(
                          corpseEquipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem,
                          "Backpack.Contents",
                          skipMagazines: false))
            {
                yield return candidate;
            }

            // Pocket and vest magazines belong to the corpse's reload setup; backpack/container
            // magazines are regular cargo and can be moved into follower backpack/pocket space.
            foreach (BodyGearCandidate candidate in GetStorageLootCandidates(
                          corpseEquipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem,
                          "Pockets.Contents",
                          skipMagazines: true))
            {
                yield return candidate;
            }

            foreach (EquipmentSlot slot in FilteredBodyWearableSlotOrder)
            {
                Item wearable = corpseEquipment.GetSlot(slot)?.ContainedItem;
                if (!FollowerLootCategoryService.IsWholeWearableTree(wearable))
                {
                    continue;
                }

                foreach (BodyGearCandidate candidate in GetWearableLootCandidates(
                             wearable,
                             slot,
                             slot.ToString(),
                             sourceTier: 1,
                             skipMagazines: slot == EquipmentSlot.TacticalVest))
                {
                    yield return candidate;
                }
            }

            foreach (EquipmentSlot slot in BodyGearWeaponSlotOrder)
            {
                Item item = corpseEquipment.GetSlot(slot)?.ContainedItem;
                if (item is Weapon && item.GetItemComponent<KnifeComponent>() == null)
                {
                    yield return new BodyGearCandidate(item, slot, slot.ToString(), 2);
                }
            }
        }

        private static bool TryGetNonTeammatePmcDogtag(InventoryEquipment corpseEquipment, out Item dogtag)
        {
            dogtag = null;
            if (corpseEquipment == null ||
                TeammateCorpseIdentity.IsTeammateCorpseEquipment(corpseEquipment))
            {
                return false;
            }

            Slot dogtagSlot = corpseEquipment.GetSlot(EquipmentSlot.Dogtag);
            Item item = dogtagSlot?.ContainedItem;
            DogtagComponent dogtagComponent = item?.GetItemComponent<DogtagComponent>();
            if (item == null ||
                dogtagComponent == null ||
                (dogtagComponent.Side != EPlayerSide.Bear && dogtagComponent.Side != EPlayerSide.Usec))
            {
                return false;
            }

            dogtag = item;
            return true;
        }

        private static bool TryCreateNonTeammatePmcDogtagCandidate(
            InventoryEquipment corpseEquipment,
            out BodyGearCandidate candidate)
        {
            candidate = null;
            if (!TryGetNonTeammatePmcDogtag(corpseEquipment, out Item dogtag))
            {
                return false;
            }

            candidate = new BodyGearCandidate(
                dogtag,
                EquipmentSlot.Dogtag,
                "Dogtag",
                0,
                bypassPriceThreshold: true,
                bypassCategoryFilter: true,
                bypassBodyGearLootability: true,
                reportAsLootNothing: true);
            return true;
        }

        private static IEnumerable<BodyGearCandidate> GetStorageLootCandidates(
            Item root,
            string sourceName,
            bool skipMagazines)
        {
            if (root == null)
            {
                yield break;
            }

            foreach (Item item in GetDirectLootChildren(root)
                         .OrderByDescending(GetBodyGearContentPriority)
                         .ThenByDescending(GetItemArea)
                         .ThenByDescending(item => item.Template?.CreditsPrice ?? 0))
            {
                if (item == null)
                {
                    continue;
                }

                if (item is BuiltInInsertsItemClass)
                {
                    continue;
                }

                if (FollowerLootCategoryService.IsWholeWearableTree(item))
                {
                    foreach (BodyGearCandidate wearableCandidate in GetWearableLootCandidates(
                                 item,
                                 sourceSlot: null,
                                 sourceName: sourceName,
                                 sourceTier: 1,
                                 skipMagazines: skipMagazines))
                    {
                        yield return wearableCandidate;
                    }

                    continue;
                }

                // Searchable backpacks/rigs/containers are treated as their own trees: inspect the
                // children once instead of taking the root container as a filtered loot item.
                if (ShouldSearchContentsInsteadOfMovingRoot(item))
                {
                    foreach (BodyGearCandidate child in GetStorageLootCandidates(
                                 item,
                                 $"{sourceName}.{item.TemplateId}",
                                 skipMagazines))
                    {
                        yield return child;
                    }

                    continue;
                }

                yield return new BodyGearCandidate(item, null, sourceName, 1, skipMagazines);
            }
        }

        private static IEnumerable<BodyGearCandidate> GetWearableLootCandidates(
            Item wearable,
            EquipmentSlot? sourceSlot,
            string sourceName,
            int sourceTier,
            bool skipMagazines)
        {
            if (!FollowerLootCategoryService.IsWholeWearableTree(wearable))
            {
                yield break;
            }

            // The root carries its plates, attachments, and grid contents as one priced tree.
            // Fallback children are only reached on a later planning pass when the root stayed put.
            yield return new BodyGearCandidate(wearable, sourceSlot, sourceName, sourceTier);

            if (wearable is VestItemClass)
            {
                foreach (BodyGearCandidate candidate in GetStorageLootCandidates(
                             wearable,
                             $"{sourceName}.Contents",
                             skipMagazines))
                {
                    yield return candidate;
                }

                yield break;
            }

            if (wearable is ArmorItemClass)
            {
                foreach (ArmorPlateItemClass plate in GetDirectLootChildren(wearable).OfType<ArmorPlateItemClass>())
                {
                    yield return new BodyGearCandidate(
                        plate,
                        sourceSlot: null,
                        sourceName: $"{sourceName}.InstalledPlate",
                        sourceTier: sourceTier + 1);
                }
            }
        }

        private static IEnumerable<Item> GetDirectLootChildren(Item root)
        {
            if (root == null)
            {
                yield break;
            }

            foreach (Item child in root.GetAllItems())
            {
                if (child == null || ReferenceEquals(child, root))
                {
                    continue;
                }

                if (ReferenceEquals(child.Parent?.Container?.ParentItem, root))
                {
                    yield return child;
                }
            }
        }

        private static bool ShouldSearchContentsInsteadOfMovingRoot(Item item)
        {
            return item is SearchableItemItemClass && item is not Weapon;
        }

        private bool CanTryFilteredLootCandidate(
            BodyGearCandidate candidate,
            HashSet<string> attemptedItemIds)
        {
            return CanConsiderFilteredLootCandidate(candidate, attemptedItemIds, markRejectedAttempt: true);
        }

        private bool CanConsiderFilteredLootCandidate(
            BodyGearCandidate candidate,
            HashSet<string> attemptedItemIds,
            bool markRejectedAttempt = false)
        {
            Item item = candidate?.Item;
            if (item == null ||
                string.IsNullOrEmpty(item.Id) ||
                attemptedItemIds.Contains(item.Id) ||
                InteractableObjects.IsProtectedFollowerEquipment(item))
            {
                return false;
            }

            if (!candidate.BypassBodyGearLootability && !IsBodyGearCandidateLootable(item))
            {
                return false;
            }

            if (item is ArmorPlateItemClass && !IsEligibleInstalledArmorPlate(item))
            {
                if (markRejectedAttempt)
                {
                    attemptedItemIds.Add(item.Id);
                }

                return false;
            }

            if (candidate.SkipMagazine && item is MagazineItemClass)
            {
                if (markRejectedAttempt)
                {
                    attemptedItemIds.Add(item.Id);
                }

                return false;
            }

            // Category and price are the main loot gates. Dogtags set bypass flags above; money is
            // handled by the price service so valuables can always take it when enabled.
            if (!candidate.BypassCategoryFilter && !FollowerLootCategoryService.PassesCategoryFilter(item))
            {
                if (markRejectedAttempt)
                {
                    attemptedItemIds.Add(item.Id);
                }

                return false;
            }

            if (!candidate.BypassPriceThreshold && !FollowerLootPriceService.PassesPriceThreshold(item))
            {
                if (markRejectedAttempt)
                {
                    attemptedItemIds.Add(item.Id);
                }

                return false;
            }

            return true;
        }

        private static bool IsEligibleInstalledArmorPlate(Item item)
        {
            if (item is not ArmorPlateItemClass ||
                item.CurrentAddress?.Container is not Slot slot ||
                (slot.ParentItem is not ArmorItemClass && slot.ParentItem is not VestItemClass) ||
                !item.TryGetItemComponent<RepairableComponent>(out RepairableComponent repairable))
            {
                return false;
            }

            float maxDurability = repairable.MaxDurability > 0f
                ? repairable.MaxDurability
                : repairable.TemplateDurability;
            return maxDurability > 0f && repairable.Durability >= maxDurability * 0.5f;
        }

    }
}

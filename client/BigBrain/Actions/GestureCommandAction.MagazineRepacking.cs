using EFT.InventoryLogic;
using pitTeam.Modules;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private bool TryBuildMagazineDonorTopOffStagingMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Item sourceRoot,
            Weapon weapon,
            IEnumerable<MagazineTopOffTarget> targets,
            OperationalMagazinePlan refillPlan,
            out BodyGearMove? move)
        {
            move = null;
            List<EFT.InventoryLogic.Magazine> donors = refillPlan?.CompatibleLoadedCandidates
                .Select(candidate => candidate.Item)
                .OfType<EFT.InventoryLogic.Magazine>()
                .Where(magazine =>
                    magazine.Count > 0 &&
                    !IsMagazineInstalledInWeapon(magazine) &&
                    IsMagazineCompatibleWithWeapon(weapon, magazine) &&
                    FollowerWeaponMagazineCompatibility.AreLoadedCartridgesCompatible(weapon, magazine))
                // Drain the least-full useful donor first so the accepted package converges on
                // full magazines and leaves as few partials as possible.
                .OrderBy(magazine => magazine.Count)
                .ThenBy(magazine => magazine.Id, StringComparer.Ordinal)
                .ToList() ?? new List<EFT.InventoryLogic.Magazine>();
            if (donors.Count == 0)
            {
                return false;
            }

            List<MagazineTopOffTarget> orderedTargets = targets?
                .Where(target => target?.Magazine != null)
                .ToList() ?? new List<MagazineTopOffTarget>();
            Modules.Logger.LogInfo(
                $"[LootCommand][MagazineRepack] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} result=evaluate targets={orderedTargets.Count} " +
                $"donors={donors.Count} targetRounds=[{string.Join(",", orderedTargets.Select(target => $"{target.Magazine.Count}/{target.Magazine.MaxCount}"))}] " +
                $"donorRounds=[{string.Join(",", donors.Select(donor => $"{donor.Count}/{donor.MaxCount}"))}]");
            HashSet<string> acceptedFastAccessIds = new HashSet<string>(
                refillPlan.FollowUps
                    .Where(IsOperationalFastAccessFollowUp)
                    .Select(candidate => candidate.Item?.Id)
                    .Where(id => !string.IsNullOrEmpty(id)),
                StringComparer.Ordinal);
            foreach (MagazineTopOffTarget target in orderedTargets)
            {
                if (target.Magazine.Count >= target.Magazine.MaxCount)
                {
                    continue;
                }

                foreach (EFT.InventoryLogic.Magazine donor in donors)
                {
                    if (IsSameLootItem(target.Magazine, donor) ||
                        (!target.IsInsertedMagazine &&
                         acceptedFastAccessIds.Contains(donor.Id) &&
                         donor.Count > target.Magazine.Count) ||
                        donor.Cartridges.Last is not EFT.InventoryLogic.Ammo donorAmmo ||
                        donorAmmo.StackObjectsCount <= 0 ||
                        !CanTopOffMagazineWithAmmo(weapon, target.Magazine, donorAmmo))
                    {
                        continue;
                    }

                    TacticalAmmoDecision decision = EvaluateTacticalAmmoCandidate(
                        donorAmmo,
                        GetMagazineCartridgeItems(target.Magazine),
                        donorAmmo.StackObjectsCount,
                        Math.Max(1, target.Magazine.MaxCount),
                        allowUpgrade: true);
                    if (!decision.ShouldAcquire)
                    {
                        Modules.Logger.LogInfo(
                            $"[LootCommand][MagazineRepack] weapon={DescribeLootDebugItem(weapon)} " +
                            $"target={DescribeLootDebugItem(target.Magazine)} donor={DescribeLootDebugItem(donor)} " +
                            $"ammo={DescribeLooseAmmo(donorAmmo)} result=skipTacticalPolicy " +
                            decision.ToDiagnosticString());
                        continue;
                    }

                    int transferCount = Math.Min(
                        target.Magazine.MaxCount - target.Magazine.Count,
                        donorAmmo.StackObjectsCount);
                    if (transferCount <= 0)
                    {
                        continue;
                    }

                    BodyGearCandidate donorCandidate = new BodyGearCandidate(
                            donorAmmo,
                            null,
                            "MagazineDonorTopOff",
                            0,
                            bypassPriceThreshold: true,
                            bypassCategoryFilter: true,
                            bypassBodyGearLootability: true,
                            reportAsLootNothing: true)
                        .WithMagazineAmmoTransferContext(
                            BodyGearFollowUpDestination.TopOffWeaponMagazine,
                            weapon,
                            target.Magazine,
                            transferCount);
                    if (target.IsInsertedMagazine)
                    {
                        if (!TryBuildInsertedMagazineTopOffChain(
                                inventory,
                                followerEquipment,
                                sourceRoot,
                                weapon,
                                target.Magazine,
                                donorCandidate,
                                out move,
                                out string insertedReason))
                        {
                            Modules.Logger.LogInfo(
                                $"[LootCommand][MagazineRepack] weapon={DescribeLootDebugItem(weapon)} " +
                                $"target={DescribeLootDebugItem(target.Magazine)} donor={DescribeLootDebugItem(donor)} " +
                                $"result=skipInserted reason={insertedReason}");
                            continue;
                        }
                    }
                    else if (!TryBuildMagazineTopOffMove(
                                 inventory,
                                 donorCandidate,
                                 out move,
                                 out string repackReason))
                    {
                        Modules.Logger.LogInfo(
                            $"[LootCommand][MagazineRepack] weapon={DescribeLootDebugItem(weapon)} " +
                            $"target={DescribeLootDebugItem(target.Magazine)} donor={DescribeLootDebugItem(donor)} " +
                            $"result=skip reason={repackReason}");
                        continue;
                    }

                    Modules.Logger.LogInfo(
                        $"[LootCommand][MagazineRepack] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                        $"weapon={DescribeLootDebugItem(weapon)} target={DescribeLootDebugItem(target.Magazine)} " +
                        $"donor={DescribeLootDebugItem(donor)} ammo={DescribeLooseAmmo(donorAmmo)} " +
                        $"transfer={transferCount} {decision.ToDiagnosticString()}");
                    return true;
                }
            }

            return false;
        }
    }
}

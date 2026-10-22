using System;
using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;

namespace pitTeam.Modules
{
    // An allocation, never a simulated load: no stack, magazine or item identity is mutated.
    internal sealed class FollowerWeaponSupplyBudget
    {
        private readonly Dictionary<Weapon, int> _rounds = new Dictionary<Weapon, int>();
        private readonly Dictionary<Magazine, Dictionary<Ammo, int>> _fills = new Dictionary<Magazine, Dictionary<Ammo, int>>();
        private readonly Dictionary<Weapon, HashSet<Magazine>> _magazines = new Dictionary<Weapon, HashSet<Magazine>>();
        internal int Rounds(Weapon weapon) => weapon != null && _rounds.TryGetValue(weapon, out int count) ? count : 0;
        internal int Fill(Magazine magazine, Ammo ammo) => magazine != null && ammo != null &&
            _fills.TryGetValue(magazine, out var stacks) && stacks.TryGetValue(ammo, out int count) ? count : 0;
        internal bool Owns(Weapon weapon, Magazine magazine) => weapon != null && magazine != null &&
            _magazines.TryGetValue(weapon, out var magazines) && magazines.Contains(magazine);
        internal bool HasReachableRefill => _fills.Any(pair => !(pair.Key.CurrentAddress?.Container is Slot) && pair.Value.Values.Any(count => count > 0));

        internal static IEnumerable<Item> LooseItems(InventoryEquipment kit, bool fastOnly = false) =>
            (fastOnly ? new[] { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets } :
                new[] { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets, EquipmentSlot.Backpack, EquipmentSlot.SecuredContainer })
            .Select(slot => kit.GetSlot(slot)?.ContainedItem).OfType<SearchableItem>()
            .SelectMany(item => item.Grids).SelectMany(grid => grid.Items);

        internal static FollowerWeaponSupplyBudget Build(InventoryEquipment kit,
            Func<Weapon, Magazine, bool> magazineAllowed = null, Func<Ammo, bool> ammoAllowed = null, bool spareFirst = false)
        {
            var result = new FollowerWeaponSupplyBudget();
            var remaining = LooseItems(kit).OfType<Ammo>().Distinct()
                .Where(ammo => !ammo.IsUsed && ammo.StackObjectsCount > 0 && (ammoAllowed?.Invoke(ammo) ?? true))
                .ToDictionary(ammo => ammo, ammo => ammo.StackObjectsCount);
            var fast = LooseItems(kit, true).OfType<Magazine>().Distinct().OrderBy(mag => (string)mag.Id, StringComparer.Ordinal).ToArray();
            var claimed = new HashSet<Magazine>();
            bool primaryAssigned = false;
            foreach (EquipmentSlot slot in new[] { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster })
            {
                if (!(kit.GetSlot(slot)?.ContainedItem is Weapon weapon)) continue;
                bool primary = !primaryAssigned;
                primaryAssigned = true;
                var magazines = new List<Magazine>();
                var inserted = weapon.GetCurrentMagazine();
                if (inserted != null && FollowerWeaponMagazineCompatibility.AreLoadedCartridgesCompatible(weapon, inserted))
                    magazines.Add(inserted);
                magazines.AddRange(fast.Where(mag => !claimed.Contains(mag) &&
                    FollowerWeaponMagazineCompatibility.IsMechanicallyCompatible(weapon, mag) &&
                    FollowerWeaponMagazineCompatibility.AreLoadedCartridgesCompatible(weapon, mag) &&
                    (magazineAllowed?.Invoke(weapon, mag) ?? true)));
                claimed.UnionWith(magazines);
                result._magazines[weapon] = new HashSet<Magazine>(magazines);
                int rounds = Math.Max(0, weapon.ChamberAmmoCount) + magazines.Sum(mag => Math.Max(0, mag.Count));
                foreach (Magazine magazine in spareFirst ? magazines.OrderBy(mag => ReferenceEquals(mag, inserted)) : magazines.AsEnumerable())
                {
                    int free = Math.Max(0, magazine.MaxCount - magazine.Count);
                    var fills = new Dictionary<Ammo, int>();
                    foreach (Ammo ammo in remaining.Keys.ToArray())
                    {
                        if (free == 0) break;
                        if (!FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, ammo) ||
                            !magazine.Cartridges.Filters.CheckItemFilter(ammo)) continue;
                        int count = Math.Min(free, remaining[ammo]);
                        if (count <= 0) continue;
                        fills.Add(ammo, count); remaining[ammo] -= count; free -= count; rounds += count;
                    }
                    result._fills[magazine] = fills;
                }
                if (FollowerWeaponLooseFeedReadiness.IsSupported(weapon))
                {
                    foreach (Ammo ammo in remaining.Keys.ToArray().Where(ammo => FollowerWeaponLooseFeedReadiness.IsCompatibleLooseAmmo(weapon, ammo)))
                    { rounds += remaining[ammo]; remaining[ammo] = 0; }
                }
                result._rounds[weapon] = rounds;
                if (!primary) continue;
                // The existing tactical stock target is 1.5 times caliber readiness, not merely
                // enough to fill today's partial magazine. Only genuinely surplus rounds are shared.
                int reserve = Math.Max(0, (int)Math.Ceiling(FollowerWeaponAmmoReadinessPolicy.GetRequiredRounds(weapon.AmmoCaliber) * 1.5) - rounds);
                foreach (Ammo ammo in remaining.Keys.ToArray())
                {
                    if (!FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, ammo)) continue;
                    int count = Math.Min(reserve, remaining[ammo]); remaining[ammo] -= count; reserve -= count;
                }
            }
            return result;
        }
    }
}

using System;
using System.Collections;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;

namespace pitTeam.Modules
{
    /// <summary>Optional external-SAIN cache refresh; core has no typed SAIN dependency.</summary>
    internal static class SainEquipmentBridge
    {
        internal static void RefreshAfterExchange(BotOwner bot)
        {
            Type componentType = Type.GetType("SAIN.Components.PlayerComponentSpace.PlayerComponent, SAIN");
            if (componentType == null) return;
            object component = bot.GetPlayer.GetComponent(componentType);
            if (component == null) return;
            object equipment = componentType.GetProperty("Equipment")?.GetValue(component);
            if (equipment == null) throw new InvalidOperationException("SAIN equipment cache is unavailable.");
            Type type = equipment.GetType();
            var infos = type.GetProperty("WeaponInfos")?.GetValue(equipment) as IDictionary;
            MethodInfo rebuild = type.GetMethod("getAllWeapons", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo equipped = type.GetMethod("OnWeaponEquiped", BindingFlags.Instance | BindingFlags.NonPublic);
            if (infos == null || rebuild == null || equipped == null)
                throw new InvalidOperationException("Unsupported SAIN equipment refresh API.");
            // Native getAllWeapons updates occupied slots but does not remove an emptied slot.
            foreach (object info in infos.Values)
                info?.GetType().GetMethod("Dispose")?.Invoke(info, null);
            infos.Clear();
            rebuild.Invoke(equipment, null);
            equipped.Invoke(equipment, new object[] { bot.GetPlayer.HandsController?.Item as Weapon, null });
            // GearInfo re-reads armor/headset/plates in its native two-second update loop.
        }
    }
}

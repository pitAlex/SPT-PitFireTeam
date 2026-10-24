using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;

namespace pitTeam.Modules
{
    internal static class FollowerLootedPrimaryWeaponBinding
    {
        /* RECONCILE */

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
        internal static void Main()
        {
            int checks = 0;
            foreach (bool supplied in new[] { false, true })
            foreach (bool promote in new[] { false, true })
            {
                var bot = new BotOwner();
                var first = new Weapon(); var second = new Weapon { Rounds = supplied ? 60 : 0 };
                var kit = bot.GetPlayer.InventoryController.Inventory.Equipment;
                kit.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem = promote ? second : first;
                kit.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem = promote ? first : second;
                bot.WeaponManager.info[EquipmentSlot.Holster] = new BotWeaponInfo(bot, new Weapon(), EquipmentSlot.Holster, null);
                ReconcileExchangedEquipment(bot);
                foreach (var slot in new[] { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon })
                {
                    Check(bot.WeaponManager.info[slot].weapon == kit.GetSlot(slot).ContainedItem,
                        "Every occupied shoulder is registered against its actual weapon, including zero-ammo weapons"); checks++;
                    Check(bot.WeaponManager.info[slot].Slot == slot, "Re-registration must update the native record's slot"); checks++;
                }
                Check(!bot.WeaponManager.info.ContainsKey(EquipmentSlot.Holster), "Removed slots cannot retain a stale weapon record"); checks++;
                Check(bot.WeaponManager.Selector.Updates == 1 &&
                    bot.WeaponManager._currentWeaponInfo.weapon == kit.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem,
                    "Selector and current weapon are refreshed after final slot assignment"); checks++;
            }
            Console.WriteLine("Gear Swap native weapon-record reconciliation: " + checks + " production-method checks passed; not live selector behavior.");
        }
    }
}
namespace EFT
{
    public enum EquipmentSlot { FirstPrimaryWeapon, SecondPrimaryWeapon, Holster }
    public sealed class BotOwner { public BotWeaponManager WeaponManager = new BotWeaponManager(); public Player GetPlayer = new Player(); }
    public sealed class Player { public InventoryController InventoryController = new InventoryController(); }
    public sealed class InventoryController { public Inventory Inventory = new Inventory(); }
    public sealed class Inventory { public InventoryEquipment Equipment = new InventoryEquipment(); }
    public sealed class BotWeaponInfo
    {
        public Weapon weapon; public EquipmentSlot Slot;
        public BotWeaponInfo(BotOwner bot, Weapon item, EquipmentSlot slot, Action mode) { weapon = item; Slot = slot; }
    }
    public sealed class BotWeaponManager
    {
        public Dictionary<EquipmentSlot, BotWeaponInfo> info = new Dictionary<EquipmentSlot, BotWeaponInfo>();
        public BotWeaponSelector Selector = new BotWeaponSelector(); public BotWeaponInfo _currentWeaponInfo;
        public void ChangeToMode() { }
    }
    public sealed class BotWeaponSelector
    {
        public int Updates; public EquipmentSlot _mainWeapon, _lastEquipmentSlot; public float _nextChangeTime;
        public bool IsWeaponReady; public void UpdateWeaponsList() { Updates++; }
    }
}
namespace EFT.InventoryLogic
{
    public class Weapon { public int Rounds; public List<object> MissingVitalParts = new List<object>(); }
    public sealed class Slot { public object ContainedItem; }
    public sealed class InventoryEquipment
    {
        private readonly Dictionary<EquipmentSlot, Slot> _slots = Enum.GetValues(typeof(EquipmentSlot)).Cast<EquipmentSlot>().ToDictionary(slot => slot, _ => new Slot());
        public Slot GetSlot(EquipmentSlot slot) => _slots[slot];
    }
}

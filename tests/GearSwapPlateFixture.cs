using System;
using System.Collections.Generic;

namespace pitTeam.Modules
{
    internal class Item { internal object CurrentAddress; }
    internal class Slot { internal bool Locked; internal Item ParentItem; }
    internal sealed class ArmorSlot : Slot { }
    internal sealed class TeammateGearSwap
    {
        internal bool _closed, _cancelled, Applying, Mutating;
        internal readonly HashSet<Item> DraftItems = new HashSet<Item>();
        internal readonly HashSet<Item> Editable = new HashSet<Item>();
        internal static readonly HashSet<object> CommitAddresses = new HashSet<object>();
        private bool IsDraft(Item item) => DraftItems.Contains(item);
        private bool CanEditDraft(Item item) => Editable.Contains(item);
        private static bool TreatCommitAddressKnown(object address) => address != null && CommitAddresses.Contains(address);
        /* PLATE POLICY */
    }
    internal static class Program
    {
        private static int _assertions;
        private static void Check(bool result, string message)
        {
            _assertions++;
            if (!result) throw new Exception(message);
        }
        private static void Main()
        {
            var session = new TeammateGearSwap();
            var carrier = new Item();
            var slot = new ArmorSlot { ParentItem = carrier };
            Check(!session.CanEditPlateSlot(null), "Null slot rejected");
            Check(!session.CanEditPlateSlot(new Slot { ParentItem = carrier }), "Non-armor slot rejected");
            Check(!session.CanEditPlateSlot(new ArmorSlot()), "Detached slot rejected");
            Check(!session.CanEditPlateSlot(slot), "Foreign/live item rejected during draft");
            session.DraftItems.Add(carrier);
            Check(!session.CanEditPlateSlot(slot), "Hidden/sealed draft carrier rejected");
            session.Editable.Add(carrier);
            Check(session.CanEditPlateSlot(slot), "Editable draft carrier allowed");
            slot.Locked = true;
            Check(!session.CanEditPlateSlot(slot), "Permanent insert lock retained");
            slot.Locked = false;
            session.Applying = true;
            Check(!session.CanEditPlateSlot(slot, true), "Draft freezes during Apply");
            session.Applying = false;
            session._closed = true;
            Check(!session.CanEditPlateSlot(slot, true), "Closed draft rejected");
            session._closed = false;
            session._cancelled = true;
            Check(!session.CanEditPlateSlot(slot, true), "Cancelled draft rejected");
            session._cancelled = false;
            var original = new Item { CurrentAddress = new object() };
            slot.ParentItem = original;
            TeammateGearSwap.CommitAddresses.Add(original.CurrentAddress);
            Check(!session.CanEditPlateSlot(slot, true), "Live item rejected outside replay");
            session.Mutating = true;
            Check(session.CanEditPlateSlot(slot, true), "Accepted original carrier allowed inside replay");
            Check(!session.CanEditPlateSlot(slot), "Live inspection UI never unlocked");
            slot.Locked = true;
            Check(!session.CanEditPlateSlot(slot, true), "Replay retains permanent locks");
            slot.Locked = false;
            original.CurrentAddress = new object();
            Check(!session.CanEditPlateSlot(slot, true), "Foreign/hidden live carrier rejected during replay");
            original.CurrentAddress = null;
            Check(!session.CanEditPlateSlot(slot, true), "Detached original carrier rejected");
            session.Mutating = false;
            Check(!session.CanEditPlateSlot(slot, true), "Replay scope ends exception");
            Console.WriteLine("Gear Swap plate scope fixture: " + _assertions + " assertions passed.");
        }
    }
}

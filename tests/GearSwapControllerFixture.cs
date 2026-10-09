using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using pitTeam.Modules;

namespace Comfort.Common
{
    public interface IResult { bool Succeed { get; } }
    public delegate void Callback(IResult result);
    public class FailedResult : IResult
    {
        public FailedResult(string message, int code) { }
        public bool Succeed => false;
    }
    public class SuccessfulResult : IResult
    {
        public static IResult New => new SuccessfulResult();
        public bool Succeed => true;
    }
}
namespace EFT
{
    public class Profile { }
    public class InventoryOperationDescriptor { }
}
namespace EFT.InventoryLogic
{
    public enum EquipmentSlot { FirstPrimaryWeapon, Backpack }
    public class Slot { public object ParentItem; public bool Deleted; }
    public class Inventory { public object Equipment = new object(); }
    public interface IOperationResult { }
    public class TestMove : IOperationResult { public string From, To; }
    public class UnsupportedEdit : IOperationResult { }
    public class InventoryController
    {
        public Inventory Inventory = new Inventory();
        public int ActiveEvents, BeginCount, CompleteCount;
        public InventoryController(Profile profile, bool examined) { }
        public ushort GetAndIncrementNextOperationId() => 1;
        public virtual bool IsAllowedToSeeSlot(Slot slot, EquipmentSlot name) => true;
        public virtual bool IsAllowedToSeeEquipmentSlot(Slot slot, EquipmentSlot name) => true;
        public virtual AbstractOperation ConvertOperationResultToOperation(IOperationResult result) => new NativeOperation(this);
        public virtual void Execute(AbstractOperation operation, Callback callback) => throw new Exception("Native execution is forbidden");
        private sealed class NativeOperation : AbstractOperation
        {
            // Mirrors AbstractAsyncOperation's native constructor-side Begin notification.
            internal NativeOperation(InventoryController owner) : base(1, owner) { owner.ActiveEvents++; owner.BeginCount++; }
            public override void ExecuteInternal(Callback callback) { }
            public override InventoryOperationDescriptor ToDescriptor() => null;
            public override BaseInventoryCommand ToBaseInventoryCommand(string ownerId) => null;
            public override void Dispose() { }
        }
    }
}
namespace EFT.InventoryLogic.Operations
{
    public class BaseInventoryCommand { }
    public abstract class AbstractOperation
    {
        protected AbstractOperation(ushort id, InventoryController controller) { }
        public abstract void ExecuteInternal(Callback callback);
        public abstract InventoryOperationDescriptor ToDescriptor();
        public abstract BaseInventoryCommand ToBaseInventoryCommand(string ownerId);
        public abstract void Dispose();
    }
}
namespace pitTeam
{
    static class pitFireTeam { internal static string GetSocialUiText(string key) => key; }
}
namespace pitTeam.Modules
{
    static class Logger { internal static void LogInfo(string message) { } }
    class GearSwapEdit
    {
        internal TestMove Move;
        internal static GearSwapEdit Capture(IOperationResult result) => result is TestMove move
            ? new GearSwapEdit { Move = move } : throw new InvalidOperationException();
    }
    class TeammateGearSwap
    {
        internal bool Applying;
        internal object FollowerEquipment = new object();
        internal static HashSet<EquipmentSlot> VisibleSlots = new HashSet<EquipmentSlot> { EquipmentSlot.FirstPrimaryWeapon };
        internal bool AllowBackpackSwap;
        internal bool CanSeeFollowerSlot(EquipmentSlot slot) => VisibleSlots.Contains(slot) ||
            (AllowBackpackSwap && slot == EquipmentSlot.Backpack);
        internal Dictionary<string, string> Slots = new Dictionary<string, string>
        {
            ["botPrimary"] = "botGun", ["botSecondary"] = "botSpare", ["playerSecondary"] = "playerGun"
        };
        internal void Stage(GearSwapEdit edit, InventoryController controller)
        {
            if (Slots.ContainsKey(edit.Move.To)) throw new InvalidOperationException("Occupied");
            string item = Slots[edit.Move.From];
            Slots.Remove(edit.Move.From); Slots.Add(edit.Move.To, item);
            // Same notification pair as the production Stage method.
            controller.BeginCount++; controller.ActiveEvents++;
            controller.CompleteCount++; controller.ActiveEvents--;
        }
    }
}
static class GearSwapControllerFixture
{
    static int assertions;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); assertions++; }
    static GearSwapInventoryController New() => new GearSwapInventoryController(new Profile()) { Session = new TeammateGearSwap() };
    static void Move(GearSwapInventoryController controller, string from, string to)
    {
        var operation = controller.ConvertOperationResultToOperation(new TestMove { From = from, To = to });
        Check(controller.ActiveEvents == 0, "conversion must not reserve the item or slot");
        bool success = false;
        controller.Execute(operation, result => success = result.Succeed);
        Check(success, "move accepted: " + from + " -> " + to);
        Check(controller.ActiveEvents == 0, "completed move leaves no stale busy markers");
        Check(controller.BeginCount == controller.CompleteCount, "every Begin has exactly one completion");
    }
    static void Main(string[] args)
    {
        var controller = New();
        if (args.Length > 0 && args[0] == "--expect-native-regression")
        {
            var native = controller.ConvertOperationResultToOperation(new TestMove { From = "botPrimary", To = "backpack" });
            Check(controller.ActiveEvents == 1, "native wrapper begins an event in its constructor");
            controller.Execute(native, _ => { });
            Check(controller.ActiveEvents == 1 && controller.BeginCount == 2 && controller.CompleteCount == 1,
                "old wrapper reproduces the leftover busy event after one move");
            Console.WriteLine("Reproduced old wrapper regression: two Begin notifications, only one completion.");
            return;
        }
        Move(controller, "botPrimary", "backpack");
        Move(controller, "playerSecondary", "botPrimary");
        Check(controller.Session.Slots["botPrimary"] == "playerGun", "player secondary replaces bot primary");
        controller = New();
        Move(controller, "botPrimary", "backpack");
        Move(controller, "botSecondary", "botPrimary");
        Check(controller.Session.Slots["botPrimary"] == "botSpare", "bot secondary moves to primary");
        controller = New();
        Move(controller, "botPrimary", "backpack");
        Move(controller, "backpack", "botPrimary");
        Check(controller.Session.Slots["botPrimary"] == "botGun", "original gun can return to primary");
        var rejected = controller.ConvertOperationResultToOperation(new UnsupportedEdit());
        bool failed = false;
        controller.Execute(rejected, result => failed = !result.Succeed);
        Check(failed && controller.ActiveEvents == 0, "unsupported action leaves no busy event");
        var occupied = controller.ConvertOperationResultToOperation(new TestMove { From = "botSecondary", To = "botPrimary" });
        failed = false;
        controller.Execute(occupied, result => failed = !result.Succeed);
        Check(failed && controller.ActiveEvents == 0, "rejected move leaves no busy event");
        controller.Session.Applying = true;
        var duringApply = controller.ConvertOperationResultToOperation(new TestMove { From = "botPrimary", To = "backpack" });
        controller.Execute(duringApply, result => failed = !result.Succeed);
        Check(failed && controller.ActiveEvents == 0, "Apply-time edit rejected without busy event");
        var followerSlot = new Slot { ParentItem = controller.Session.FollowerEquipment };
        Check(!controller.IsAllowedToSeeSlot(followerSlot, EquipmentSlot.Backpack), "recruited container backpack hidden");
        Check(!controller.IsAllowedToSeeEquipmentSlot(followerSlot, EquipmentSlot.Backpack), "recruited equipment backpack hidden");
        controller.Session.AllowBackpackSwap = true;
        Check(controller.IsAllowedToSeeSlot(followerSlot, EquipmentSlot.Backpack), "spawned container backpack visible");
        Check(controller.IsAllowedToSeeEquipmentSlot(followerSlot, EquipmentSlot.Backpack), "spawned equipment backpack visible");
        followerSlot.Deleted = true;
        Check(!controller.IsAllowedToSeeSlot(followerSlot, EquipmentSlot.Backpack), "deleted backpack slots remain hidden");
        Check(controller.IsAllowedToSeeSlot(new Slot(), EquipmentSlot.Backpack), "player backpack visibility unaffected");
        controller.Session = null;
        duringApply.Dispose();
        Check(controller.ActiveEvents == 0, "cancel/disposal never starts an inventory operation");
        Console.WriteLine("Gear Swap controller: " + assertions + " assertions passed.");
    }
}

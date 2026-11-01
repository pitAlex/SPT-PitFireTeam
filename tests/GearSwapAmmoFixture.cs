// Runs the production edit journal and stack guard against a small deterministic inventory.
// Native API/IL contracts are checked separately; this is not a Unity drag/drop test.
using System;
using System.Collections.Generic;
using System.Reflection;
using Diz.LanguageExtensions;
using EFT;
using EFT.InventoryLogic;
using pitTeam.Modules;
using pitTeam.Patches;

namespace Diz.LanguageExtensions
{
    public class Error { }
    public struct OperationResult
    {
        public IOperationResult Value;
        public Error Error;
        public bool Failed => Error != null;
        public static implicit operator OperationResult(TestResult value) => new OperationResult { Value = value };
    }
    public struct OperationResult<T> { public Error Error; public static implicit operator OperationResult<T>(Error error) => new OperationResult<T> { Error = error }; }
}
namespace EFT
{
    public struct MongoID
    {
        public string Value;
        public static implicit operator MongoID(string value) => new MongoID { Value = value };
        public static implicit operator string(MongoID value) => value.Value;
    }
}
namespace EFT.InventoryLogic
{
    public abstract class InventoryError : Error { }
    public interface IDatabaseIdGenerator { MongoID NextId { get; } void RollBack(); }
    public class ItemController { }
    public class InventoryController : ItemController { }
    public class Item
    {
        public string Id;
        public int Count;
        public ItemAddress Parent;
        public T GetItemComponent<T>() where T : new() => new T();
    }
    public class CompoundItem : Item
    {
        public Container Container;
        public Container GetContainer(string name) => Container;
    }
    public class Container
    {
        public CompoundItem ParentItem;
        public string ID = "cartridges";
        public ItemAddress CreateItemAddress(ItemAddressDescriptor descriptor) => new ItemAddress { Container = this };
    }
    public class ItemAddressDescriptor { }
    public class ItemAddress { public Container Container; public ItemAddressDescriptor ToDescriptor() => new ItemAddressDescriptor(); }
    public class FoldableComponent { public Item Item; }
    public interface IOperationResult { void RollBack(); }
    public interface ISyncOperationResult : IOperationResult { }
    public interface ILoadMagOperationResult : IOperationResult { IOperationResult OperationResult { get; } }
    public interface IUnloadMagOperationResult : IOperationResult { IOperationResult OperationResult { get; } }
    public interface IPossibleDestroyResult { bool ItemsDestroyRequired { get; } }
    public class LoadMagOperationResult : ILoadMagOperationResult
    {
        public IOperationResult OperationResult { get; }
        public LoadMagOperationResult(IOperationResult inner) { OperationResult = inner; }
        public void RollBack() => OperationResult.RollBack();
    }
    public class UnloadMagOperationResult : IUnloadMagOperationResult
    {
        public IOperationResult OperationResult { get; }
        public UnloadMagOperationResult(IOperationResult inner) { OperationResult = inner; }
        public void RollBack() => OperationResult.RollBack();
    }
    public class TestResult : ISyncOperationResult { public Action Undo = () => { }; public void RollBack() => Undo(); }
    public class MoveResult : TestResult { public Item Item; public ItemAddress To; }
    public class SwapResult : TestResult { public Item Item, Item2; public ItemAddress To, To2; }
    public class SplitResult : TestResult { public Item Item, ResultItem; public ItemAddress To; public int Count; }
    public class MergeResult : TestResult { public Item Item, TargetItem; }
    public class TransferResult : TestResult { public Item Item, TargetItem; public int Count; }
    public class FoldResult : TestResult { public FoldableComponent Foldable; public bool NewValue; }
    public class DestructiveResult : TestResult, IPossibleDestroyResult { public bool ItemsDestroyRequired => true; }
    public static class ItemManipulator
    {
        public static OperationResult Move(Item item, ItemAddress to, InventoryController controller, bool simulate)
        {
            var from = item.Parent;
            item.Parent = to;
            return new MoveResult { Item = item, To = to, Undo = () => item.Parent = from };
        }
        public static OperationResult SplitExact(Item item, int count, ItemAddress to, InventoryController controller, IDatabaseIdGenerator ids, bool simulate)
        {
            item.Count -= count;
            var created = new Item { Id = ids.NextId, Count = count, Parent = to };
            return new SplitResult { Item = item, ResultItem = created, To = to, Count = count,
                Undo = () => { item.Count += count; created.Parent = null; } };
        }
        public static OperationResult TransferExact(Item item, int count, Item target, InventoryController controller, bool simulate)
        {
            item.Count -= count; target.Count += count;
            return new TransferResult { Item = item, TargetItem = target, Count = count,
                Undo = () => { item.Count += count; target.Count -= count; } };
        }
        public static OperationResult Merge(Item item, Item target, InventoryController controller, bool simulate)
        {
            var from = item.Parent; int count = item.Count;
            item.Parent = null; item.Count = 0; target.Count += count;
            return new MergeResult { Item = item, TargetItem = target,
                Undo = () => { item.Parent = from; item.Count = count; target.Count -= count; } };
        }
        public static OperationResult Swap(Item item, ItemAddress to, Item other, ItemAddress toOther, InventoryController controller, bool simulate) => throw new NotSupportedException();
        public static OperationResult Fold(FoldableComponent foldable, bool fold, bool simulate) => throw new NotSupportedException();
        public static void TransferMaxStackCount(Item source, Item target, int count, ItemController itemController, bool simulate) { }
    }
}
namespace SPT.Reflection.Patching
{
    public class PatchPrefix : Attribute { }
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
}
namespace HarmonyLib
{
    public static class AccessTools { public static MethodInfo Method(Type type, string name, Type[] parameters) => type.GetMethod(name, parameters); }
}
namespace pitTeam { static class pitFireTeam { internal static string GetSocialUiText(string key) => key; } }
namespace pitTeam.Modules
{
    class GearSwapInventoryController : InventoryController { internal TeammateGearSwap Session; }
    class TeammateGearSwap
    {
        internal bool _closed, _cancelled, Applying;
        internal HashSet<string> _playerOwnedIds = new HashSet<string>();
        internal Dictionary<string, Item> Items;
        internal bool IsDraft(Item item) => item != null && Items.TryGetValue(item.Id, out var exact) && ReferenceEquals(exact, item);
        internal bool CanEditDraft(Item item) => item.Id != "hidden";
        /* STACK POLICY */
    }
}
static class GearSwapAmmoFixture
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static Dictionary<string, Item> Inventory()
    {
        var magazine = new CompoundItem { Id = "magazine" };
        magazine.Container = new Container { ParentItem = magazine };
        return new Dictionary<string, Item> {
            ["magazine"] = magazine,
            ["loose"] = new Item { Id = "loose", Count = 60 },
            ["loaded"] = new Item { Id = "loaded", Count = 10, Parent = new ItemAddress { Container = magazine.Container } }
        };
    }
    static bool Preview(ItemController controller, Item source, Item target)
    {
        object[] args = { source, target, controller, new OperationResult<TransferResult>() };
        return (bool)typeof(GearSwapStackTransferPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    }
    static void Main()
    {
        var live = Inventory(); var draft = Inventory();
        var controller = new InventoryController();
        var owned = new HashSet<string>();
        var edit = GearSwapEdit.Capture(new LoadMagOperationResult(new TransferResult { Item = draft["loose"], TargetItem = draft["loaded"], Count = 20 }));
        edit.ValidateProvenance(owned);
        edit.Execute(draft, controller, _ => true, _ => true);
        Check(draft["loose"].Count == 40 && draft["loaded"].Count == 30, "magazine top-off staged");
        Check(live["loose"].Count == 60 && live["loaded"].Count == 10, "cancel leaves live counts unchanged");
        var committed = edit.Execute(live, controller, _ => true, _ => true);
        Check(live["loose"].Count == draft["loose"].Count && live["loaded"].Count == 30, "Apply replays top-off once");
        committed.RollBack();
        Check(live["loose"].Count == 60 && live["loaded"].Count == 10, "failed Apply restores round counts");

        draft = Inventory(); live = Inventory(); owned.Add("loose");
        var split = GearSwapEdit.Capture(new LoadMagOperationResult(new SplitResult { Item = draft["loose"],
            ResultItem = new Item { Id = "splitId" }, Count = 25, To = draft["loaded"].Parent }));
        split.ValidateProvenance(owned);
        split.Execute(draft, controller, _ => true, _ => true); split.UpdateProvenance(owned);
        split.Execute(live, controller, _ => true, _ => true);
        Check(draft["splitId"].Count == 25 && live["splitId"].Count == 25, "split rounds retain the same ID at Apply");
        Check(owned.Contains("splitId"), "split player rounds preserve return provenance");
        Check(ReferenceEquals(live["splitId"].Parent.Container.ParentItem, live["magazine"]), "Apply resolves live magazine, not clone");

        var unload = GearSwapEdit.Capture(new UnloadMagOperationResult(new MoveResult { Item = draft["splitId"], To = draft["loaded"].Parent }));
        Check(unload != null, "native unload wrapper accepted");
        foreach (bool sourceOwned in new[] { false, true }) foreach (bool targetOwned in new[] { false, true })
        {
            draft = Inventory();
            var session = new TeammateGearSwap { Items = draft };
            if (sourceOwned) session._playerOwnedIds.Add("loose");
            if (targetOwned) session._playerOwnedIds.Add("loaded");
            var draftController = new GearSwapInventoryController { Session = session };
            Check(Preview(draftController, draft["loose"], draft["loaded"]) == (sourceOwned == targetOwned), "native preview follows provenance");
            foreach (bool merge in new[] { false, true })
            {
                IOperationResult inner = merge ? (IOperationResult)new MergeResult { Item = draft["loose"], TargetItem = draft["loaded"] } :
                    new TransferResult { Item = draft["loose"], TargetItem = draft["loaded"], Count = 20 };
                var stackEdit = GearSwapEdit.Capture(new LoadMagOperationResult(inner));
                bool denied = false;
                try { stackEdit.ValidateProvenance(session._playerOwnedIds); } catch (InvalidOperationException) { denied = true; }
                Check(denied == (sourceOwned != targetOwned), "journal and preview agree for merge/partial top-off");
            }
            // Model EFT's repeated successful partial preview with an unchanged rejected edit.
            // The production prefix must terminate it before execution, not merely yield a frame.
            if (sourceOwned != targetOwned)
            {
                int attempts = 0;
                while (Preview(draftController, draft["loose"], draft["loaded"]) && attempts < 4) attempts++;
                Check(attempts == 0, "mixed-owner partial transfer cannot enter native retry loop");
            }
            Check(Preview(new InventoryController(), draft["loose"], draft["loaded"]), "normal live/loadout controller unaffected");
            session.Applying = true;
            Check(!Preview(draftController, draft["loose"], draft["loaded"]), "no edits during Apply");
            session.Applying = false; session._closed = true;
            Check(!Preview(draftController, draft["loose"], draft["loaded"]), "closed draft rejected early");
            session._closed = false; session._cancelled = true;
            Check(!Preview(draftController, draft["loose"], draft["loaded"]), "cancelled draft rejected early");
            session._cancelled = false;
            Check(!Preview(draftController, live["loose"], draft["loaded"]), "same-ID live source is not a draft");
        }
        bool destructiveDenied = false;
        try { GearSwapEdit.Capture(new LoadMagOperationResult(new DestructiveResult())); }
        catch (InvalidOperationException) { destructiveDenied = true; }
        Check(destructiveDenied, "magazine wrapper cannot hide destruction");
        bool unsupportedDenied = false;
        try { GearSwapEdit.Capture(new TestResult()); } catch (InvalidOperationException) { unsupportedDenied = true; }
        Check(unsupportedDenied, "other unsupported operations stay blocked");
        Console.WriteLine("Gear Swap ammunition: " + checks + " assertions passed.");
    }
}

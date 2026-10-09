using System;
namespace pitTeam.Modules
{
    internal static class Logger { internal static void LogInfo(string message) { } }
}
namespace pitTeam.Patches
{
    internal sealed class Transform
    {
        internal Transform Parent;
        internal bool IsChildOf(Transform other)
        {
            for (var current = this; current != null; current = current.Parent)
                if (ReferenceEquals(current, other)) return true;
            return false;
        }
    }
    internal sealed class GameObject { internal bool activeSelf = true; }
    internal class SlotView
    {
        internal string name = "Backpack Slot";
        internal Transform transform = new Transform();
        internal GameObject gameObject = new GameObject();
        internal void ShowGameObject() => gameObject.activeSelf = true;
    }
    internal sealed class SearchableItemView : SlotView
    {
        internal SlotView SharedSlot;
        internal bool Disposed;
        internal void Close() { Disposed = true; gameObject.activeSelf = false; }
        internal T GetComponent<T>() where T : class => SharedSlot as T;
    }
    /* PRESENTATION */
    internal static class Program
    {
        private static int _assertions;
        private static void Check(bool value, string message)
        {
            _assertions++;
            if (!value) throw new Exception(message);
        }
        private static void Main()
        {
            GearSwapBackpackPresentation.CloseContents(null);
            var shared = new SearchableItemView();
            var row = new SlotView { transform = shared.transform, gameObject = shared.gameObject };
            GearSwapBackpackPresentation.CloseContents(shared, row);
            Check(shared.Disposed, "Shared contents must be disposed");
            Check(row.gameObject.activeSelf, "Shared equipment row must remain visible");
            var separate = new SearchableItemView();
            var parent = new SlotView();
            separate.transform.Parent = parent.transform;
            GearSwapBackpackPresentation.CloseContents(separate, parent);
            Check(separate.Disposed && !separate.gameObject.activeSelf, "Separate contents stay hidden");
            Check(parent.gameObject.activeSelf, "Parent equipment row stays visible");
            var ancestor = new SearchableItemView();
            var child = new SlotView();
            child.transform.Parent = ancestor.transform;
            GearSwapBackpackPresentation.CloseContents(ancestor, child);
            Check(ancestor.gameObject.activeSelf && child.gameObject.activeSelf, "Ancestor of row must remain visible");
            var inferred = new SearchableItemView();
            inferred.SharedSlot = new SlotView { transform = inferred.transform, gameObject = inferred.gameObject };
            GearSwapBackpackPresentation.CloseContents(inferred);
            Check(inferred.gameObject.activeSelf && inferred.Disposed, "Direct contents hook preserves shared slot");
            var standalone = new SearchableItemView();
            GearSwapBackpackPresentation.CloseContents(standalone);
            Check(standalone.Disposed && !standalone.gameObject.activeSelf, "Standalone contents remain closed");
            Console.WriteLine("Gear Swap backpack presentation fixture: " + _assertions + " assertions passed.");
        }
    }
}

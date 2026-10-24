using System;
using System.Reflection;

class Item { }
class GameObject
{
    internal bool Active = true;
    internal void SetActive(bool value) => Active = value;
}
class Transform { internal GameObject gameObject = new GameObject(); }
class SearchableSlotView
{
    internal Transform _specSlotsPanel = new Transform();
    internal GameObject NormalGrid = new GameObject();
    public void CreateSlots(Item item) { }
}
class TeammateGearSwap
{
    internal static TeammateGearSwap Current;
    internal Item FollowerPockets;
    internal bool IsFollowerPockets(Item item) => item != null && ReferenceEquals(item, FollowerPockets);
}
abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
class PatchPrefix : Attribute { }
static class AccessTools
{
    internal static MethodInfo Method(Type type, string name) => type.GetMethod(name);
}

/* POCKETS PATCH */

static class GearSwapPocketsFixture
{
    static int assertions;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); assertions++; }
    static bool Prefix(SearchableSlotView view, Item item) => (bool)typeof(GearSwapPocketSpecialSlotsPatch)
        .GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { view, item });
    static void Main()
    {
        var pockets = new Item();
        var view = new SearchableSlotView();
        Check(Prefix(view, pockets), "no session leaves native presentation unchanged");
        Check(view._specSlotsPanel.gameObject.Active && view.NormalGrid.Active, "outside editor no UI suppression");
        TeammateGearSwap.Current = new TeammateGearSwap { FollowerPockets = pockets };
        Check(!Prefix(view, pockets), "cloned follower pockets skip special panel creation");
        Check(!view._specSlotsPanel.gameObject.Active, "follower special panel hidden");
        Check(view.NormalGrid.Active, "normal pocket grids remain visible");
        view._specSlotsPanel = new Transform();
        Check(Prefix(view, new Item()), "player pockets, bags and live roots retain native presentation");
        Check(view._specSlotsPanel.gameObject.Active, "unrelated panel not hidden");
        Check(Prefix(view, null), "empty slot remains native");
        view._specSlotsPanel = null;
        Check(!Prefix(view, pockets), "missing special panel safe");
        TeammateGearSwap.Current = null;
        Check(Prefix(view, pockets), "closed editor removes special panel exception");
        Console.WriteLine("Gear Swap pockets: " + assertions + " assertions passed.");
    }
}

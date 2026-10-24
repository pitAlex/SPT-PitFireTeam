using EFT;
using EFT.InventoryLogic;
using pitTeam.Modules;

pitTeam.BigBrain.Actions.GestureCommandAction.Verify();

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private sealed class Follower { internal bool IsSquadMate; }
        private sealed class Candidate { internal Item Item; }
        private readonly Follower followerData = new();
        private readonly BotOwner BotOwner = new();
        private FollowerLootRequest ActiveLootRequest;
        private Target activeLootItem = new(), activeBodyLootCorpse = new();
        private readonly HashSet<string> bodyLootAttemptedItemIds = new();
        private int bodyLootMovesSucceeded, bodyLootReportedMovesSucceeded;
        private bool bodyLootSuccessSpoken, bodyLootSearchStarted;
        private float bodyLootNextMoveAt, bodyLootReadyAt, lootPickupReadyAt;
        private const float LootPickupSuccessLeadSeconds = 1.1f;
        private string ended;
        private bool taken;
        private static bool IsLootNowInBotInventory(Player player, Item item) => player.Items.Contains(item);
        private void FinishLootPickupSuccess(Player player, Item item, string reason) { taken = true; ended = reason; ResetRecruitGearCommand(); }
        private void ClearTakeLootState(string reason) { ended = reason; ResetRecruitGearCommand(); }
        private void ClearBodyLootState(string reason) { ended = reason; ResetRecruitGearCommand(); }
        private IEnumerable<Candidate> GetBodyOperationalMagazineCandidates(InventoryEquipment source, Weapon weapon) =>
            source.GetAllItems().OfType<Magazine>().Where(m => m.Family == weapon.Family).Select(i => new Candidate { Item = i });
        private IEnumerable<Candidate> GetBodyWeaponLooseAmmoCandidates(InventoryEquipment source, Weapon weapon) =>
            source.GetAllItems().OfType<Ammo>().Where(m => m.Family == weapon.Family).Select(i => new Candidate { Item = i });

        internal static void Verify()
        {
            int checks = 0;
            void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
            foreach (var mode in new[] { FollowerLootMode.LootAndGetWeapon, FollowerLootMode.LootAndGetGear })
            {
                var action = new GestureCommandAction { ActiveLootRequest = new(mode) };
                var source = new InventoryEquipment();
                source.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem = new Weapon("protected") { Protected = true };
                TeammateGearSwap.Reset();
                Check(!action.TryStartRecruitBodyKit(source, new()) && TeammateGearSwap.Calls == 0,
                    "commanded recovery does not offer Restricted protected roots as manual loans");
            }
            foreach (bool squad in new[] { false, true })
            foreach (FollowerLootMode mode in Enum.GetValues<FollowerLootMode>())
            {
                var action = new GestureCommandAction { ActiveLootRequest = new(mode) };
                action.followerData.IsSquadMate = squad;
                var source = new InventoryEquipment(); var kit = new InventoryEquipment();
                var gun = new Weapon("primary"); source.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem = gun;
                TeammateGearSwap.Reset();
                bool started = action.TryStartRecruitBodyKit(source, kit);
                bool expected = !squad && (mode == FollowerLootMode.LootAndGetGear || mode == FollowerLootMode.LootAndGetWeapon);
                Check(started == expected && TeammateGearSwap.Calls == (expected ? 1 : 0), "only requested recruit body modes use upgrade engine");
                if (expected)
                {
                    Check(TeammateGearSwap.Body && TeammateGearSwap.Offered.Contains(gun), "body source and weapon reach shared engine");
                    Check(action.ActiveLootRequest.PriorityPending && action.bodyLootReportedMovesSucceeded == 0, "ordinary loot waits for committed upgrade");
                    TeammateGearSwap.Complete(true);
                    Check(!action.ActiveLootRequest.PriorityPending && action.bodyLootReportedMovesSucceeded == 1 && !action.recruitGearInProgress,
                        "successful upgrade completes priority and resumes ordinary loot");
                    Check(action.ActiveLootRequest.GearExchangeCommitted && action.bodyLootNextMoveAt == UnityEngine.Time.time + LootPickupSuccessLeadSeconds,
                        "committed exchange is owned by the request and leaves the normal speech lead");
                    action.bodyLootReportedMovesSucceeded = 0;
                    action.recruitBodyKitAttempted = false;
                    Check(action.ActiveLootRequest.HasSuccessfulLoot(action.bodyLootReportedMovesSucceeded),
                        "action restart cannot turn a committed equipment exchange into noSpace");
                    Check(!action.TryStartRecruitBodyKit(source, kit), "same corpse command never repeats kit exchange");
                }
            }
            {
                var request = new FollowerLootRequest(FollowerLootMode.LootAndGetGear);
                Check(!request.HasSuccessfulLoot(0) && request.HasSuccessfulLoot(1), "ordinary success and empty search retain their existing distinction");
                request.CompleteGearExchange();
                Check(request.HasSuccessfulLoot(0) && !request.PriorityPending, "committed exchange survives loss of local non-dogtag counts");
                var replacement = new FollowerLootRequest(FollowerLootMode.LootAndGetGear);
                Check(!replacement.GearExchangeCommitted && !replacement.HasSuccessfulLoot(0), "replacement command never inherits prior success");
            }
            {
                var action = new GestureCommandAction { ActiveLootRequest = new(FollowerLootMode.LootAndGetWeapon) };
                action.ActiveLootRequest.InitializeWeaponSelection(false, "removed-primary", "second", null, false, false);
                var source = new InventoryEquipment(); source.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem = new Weapon("second");
                TeammateGearSwap.Reset();
                Check(!action.TryStartRecruitBodyKit(source, new()) && TeammateGearSwap.Calls == 0,
                    "selective command never advances to another corpse gun when its pinned primary disappears");
            }
            {
                var action = new GestureCommandAction { ActiveLootRequest = new(FollowerLootMode.LootAndGetWeapon) };
                action.ActiveLootRequest.InitializeWeaponSelection(false, "pinned", "old-second", null, false, false);
                var source = new InventoryEquipment(); var pinned = new Weapon("pinned");
                source.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem = new Weapon("new-first");
                source.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem = pinned;
                TeammateGearSwap.Reset(); action.TryStartRecruitBodyKit(source, new());
                Check(TeammateGearSwap.Offered.Contains(pinned) && !TeammateGearSwap.Offered.Any(i => i.Id == "new-first"),
                    "moving the pinned gun between shoulder slots does not authorize a newly arrived gun");
            }
            {
                var action = new GestureCommandAction { ActiveLootRequest = new(FollowerLootMode.LootAndGetGear) };
                var source = new InventoryEquipment(); var kit = new InventoryEquipment();
                var primary = new Weapon("first"); var secondary = new Weapon("second"); var pistol = new Weapon("pistol");
                var vest = new SearchableItem("vest"); var magazine = new Magazine("inside-vest"); vest.Grids[0].Items.Add(magazine);
                var bag = new SearchableItem("backpack"); var nestedBag = new SearchableItem("nested"); bag.Grids[0].Items.Add(nestedBag);
                var pockets = new SearchableItem("pockets"); var ammo = new Ammo("pocket-ammo"); pockets.Grids[0].Items.Add(ammo);
                var helmet = new Item("helmet"); var armor = new Item("armor"); var secure = new SearchableItem("secure");
                foreach (var pair in new[] { (EquipmentSlot.FirstPrimaryWeapon, (Item)primary), (EquipmentSlot.SecondPrimaryWeapon, secondary),
                    (EquipmentSlot.Holster, pistol), (EquipmentSlot.TacticalVest, vest), (EquipmentSlot.Backpack, bag),
                    (EquipmentSlot.Pockets, pockets), (EquipmentSlot.Headwear, helmet), (EquipmentSlot.ArmorVest, armor), (EquipmentSlot.SecuredContainer, secure) })
                    source.GetSlot(pair.Item1).ContainedItem = pair.Item2;
                TeammateGearSwap.Reset(); Check(action.TryStartRecruitBodyKit(source, kit), "full-kit request starts");
                Check(TeammateGearSwap.Offered.ToHashSet().SetEquals(new Item[] { primary, secondary, pistol, vest, bag, ammo, helmet, armor }),
                    "full kit includes both long guns, pistol, armor, helmet, vest, backpack and pocket contents");
                Check(!TeammateGearSwap.Offered.Contains(pockets) && !TeammateGearSwap.Offered.Contains(secure), "permanent pockets and secure root never donated");
                Check(!TeammateGearSwap.Offered.Contains(magazine) && !TeammateGearSwap.Offered.Contains(nestedBag), "contained items stay inside offered parent packages");
                Check(action.bodyLootAttemptedItemIds.Contains(magazine.Id), "selected descendant cannot be looted twice in normal phase");
                action.ResetRecruitGearCommand(); TeammateGearSwap.Complete(true);
                Check(action.bodyLootReportedMovesSucceeded == 0, "late completion cannot affect another command");
            }
            foreach (bool holsterFull in new[] { false, true })
            {
                var action = new GestureCommandAction { ActiveLootRequest = new(FollowerLootMode.LootAndGetWeapon) };
                var source = new InventoryEquipment(); var kit = new InventoryEquipment();
                var first = new Weapon("first") { Family = "a" }; var second = new Weapon("second") { Family = "b" };
                var pistol = new Weapon("pistol") { Family = "c" }; var mags = new SearchableItem("mags");
                var matching = new Magazine("a-spare") { Family = "a" }; var wrong = new Magazine("b-spare") { Family = "b" };
                var inserted = new Magazine("inserted") { Family = "a" }; first.Children.Add(inserted);
                mags.Grids[0].Items.AddRange(new Item[] { matching, wrong });
                source.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem = first;
                source.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem = second;
                source.GetSlot(EquipmentSlot.Holster).ContainedItem = pistol;
                source.GetSlot(EquipmentSlot.TacticalVest).ContainedItem = mags;
                if (holsterFull) kit.GetSlot(EquipmentSlot.Holster).ContainedItem = new Weapon("old-pistol");
                TeammateGearSwap.Reset(); action.TryStartRecruitBodyKit(source, kit);
                Check(TeammateGearSwap.Offered.Contains(first) && !TeammateGearSwap.Offered.Contains(second), "weapon priority retains selective primary-then-secondary fallback");
                Check(TeammateGearSwap.Offered.Contains(pistol) == !holsterFull, "pistol offered only for an empty holster");
                Check(TeammateGearSwap.Offered.Contains(matching) && !TeammateGearSwap.Offered.Contains(wrong) && !TeammateGearSwap.Offered.Contains(mags),
                    "weapon request includes compatible supplies but does not donate worn gear or unrelated magazines");
                Check(!TeammateGearSwap.Offered.Contains(inserted), "inserted magazine is not independently moved from selected weapon");
            }
            foreach (bool squad in new[] { false, true })
            foreach (Item offered in new Item[] { new Weapon("loose"), new Magazine("loose-mag"), new Ammo("loose-ammo") })
            {
                var action = new GestureCommandAction(); action.followerData.IsSquadMate = squad;
                var gun = offered; TeammateGearSwap.Reset();
                Check(action.TryStartRecruitLooseEquipment(gun) == !squad, "loose weapon/magazine/ammo reevaluation is recruited-only");
                if (!squad)
                {
                    Check(!TeammateGearSwap.Body && TeammateGearSwap.Offered.Single() == gun, "loose command offers exactly the requested item");
                    action.BotOwner.GetPlayer.Items.Add(gun); TeammateGearSwap.Complete(true);
                    Check(action.taken && !action.recruitGearInProgress, "loose pickup completes only after retained committed ownership");
                }
            }
            foreach (bool unavailable in new[] { false, true })
            {
                var action = new GestureCommandAction(); TeammateGearSwap.Reset(); TeammateGearSwap.Available = !unavailable;
                action.TryStartRecruitLooseEquipment(new Weapon("rejected"));
                if (!unavailable) TeammateGearSwap.Complete(false);
                Check(!action.taken && action.ended != null && !action.recruitGearInProgress && action.BotOwner.BotTalk.Last == EPhraseTrigger.Negative,
                    "unavailable or rolled-back upgrade terminates safely without ordinary pickup fallback");
            }
            foreach (bool body in new[] { false, true })
            {
                var action = new GestureCommandAction { ActiveLootRequest = new(body ? FollowerLootMode.LootAndGetGear : FollowerLootMode.Normal) };
                var source = new InventoryEquipment(); var gun = new Weapon("late");
                source.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem = gun;
                TeammateGearSwap.Reset();
                if (body) action.TryStartRecruitBodyKit(source, new()); else action.TryStartRecruitLooseEquipment(gun);
                var replacement = new FollowerLootRequest(FollowerLootMode.LootAndGetWeapon);
                action.ActiveLootRequest = replacement;
                action.BotOwner.GetPlayer.Items.Add(gun);
                TeammateGearSwap.Complete(true);
                Check(replacement.PriorityPending && !replacement.GearExchangeCommitted && action.ended == null && !action.taken && !action.recruitGearInProgress,
                    "replacement request identity blocks late completion even before action cleanup runs");
                Check(body ? action.activeBodyLootCorpse == null && !action.recruitBodyKitAttempted :
                    action.activeLootItem == null && action.lootPickupReadyAt == 0f,
                    "stale callback discards only its old target cache without clearing the replacement reservation");
            }
            {
                var action = new GestureCommandAction { ActiveLootRequest = new(FollowerLootMode.LootAndGetGear) };
                TeammateGearSwap.Reset(); TeammateGearSwap.Current = new object();
                Check(action.TryStartRecruitBodyKit(new(), new()) && !action.recruitBodyKitAttempted && TeammateGearSwap.Calls == 0,
                    "other active exchange defers body kit without consuming its attempt");
                Check(action.TryStartRecruitLooseEquipment(new Weapon("wait")) && !action.recruitGearInProgress && action.ended == null,
                    "loose command waits for shared exchange instead of falsely refusing");
                TeammateGearSwap.Reset();
            }
            {
                var action = new GestureCommandAction(); TeammateGearSwap.Reset();
                Check(!action.TryStartRecruitLooseEquipment(new SearchableItem("whole-backpack")) && TeammateGearSwap.Calls == 0,
                    "Taking a whole backpack is still ordinary pickup, not automatic kit unpacking");
            }
            Console.WriteLine($"PASS: {checks} production recruit command routing/offer/callback checks; not live world ownership.");
        }
    }
}

namespace EFT
{
    public enum EquipmentSlot { FirstPrimaryWeapon, SecondPrimaryWeapon, Holster, ArmorVest, TacticalVest, Headwear, Earpiece, FaceCover, Eyewear, Backpack, Pockets, SecuredContainer }
    public enum EPhraseTrigger { Negative, LootWeapon, LootGeneric }
    public class Player { public HashSet<Item> Items = new(); }
    public class Talk { public EPhraseTrigger Last; public void TrySay(EPhraseTrigger phrase, bool unused) => Last = phrase; }
    public class BotOwner { public UnityEngine.Vector3 Position; public Player GetPlayer = new(); public Talk BotTalk = new(); }
    public class Target { public object gameObject = new(); public UnityEngine.Transform transform = new(); }
}
namespace EFT.InventoryLogic
{
    public class KnifeComponent { }
    public class Item(string id) { public string Id = id; public bool Protected; public List<Item> Children = new(); public T GetItemComponent<T>() => default; }
    public class Weapon(string id) : Item(id) { public string Family = "default"; }
    public class Magazine(string id) : Item(id) { public string Family = "default"; }
    public class Ammo(string id) : Item(id) { public string Family = "default"; }
    public class Grid { public List<Item> Items = new(); }
    public class SearchableItem(string id) : Item(id) { public Grid[] Grids = { new() }; }
    public class Slot { public Item ContainedItem; }
    public class InventoryEquipment() : Item("equipment")
    {
        private Dictionary<EquipmentSlot, Slot> Slots = Enum.GetValues<EquipmentSlot>().ToDictionary(s => s, _ => new Slot());
        public Slot GetSlot(EquipmentSlot slot) => Slots[slot];
        public IEnumerable<Item> GetAllItems() => Slots.Values.Where(s => s.ContainedItem != null).SelectMany(s => TeammateGearSwap.Tree(s.ContainedItem));
    }
}
namespace UnityEngine
{
    public struct Vector3 { public static float Distance(Vector3 a, Vector3 b) => 0; }
    public class Transform { public Vector3 position; }
    public static class Time { public static float time; }
}
namespace pitTeam.Modules
{
    internal static class InteractableObjects { internal static bool IsProtectedFollowerEquipment(Item item) => item.Protected; }
    internal static class GiveGearPlanner { public static readonly EquipmentSlot[] EquipmentSlots = Enum.GetValues<EquipmentSlot>()
        .Where(s => s != EquipmentSlot.Pockets && s != EquipmentSlot.SecuredContainer).ToArray(); }
    internal static class TeammateGearSwap
    {
        public static int Calls; public static bool Body, Available = true; public static Item[] Offered;
        public static object Current;
        private static Action<bool> Completion;
        public static void Reset() { Calls = 0; Available = true; Offered = null; Completion = null; Current = null; }
        public static bool TryCommandLoot(BotOwner bot, Item source, IEnumerable<Item> roots, bool body, Func<bool> valid, Action<bool> callback)
        { Calls++; Body = body; Offered = roots.ToArray(); Completion = callback; return Available && valid(); }
        public static void Complete(bool committed) => Completion(committed);
        public static IEnumerable<Item> Tree(Item root) => new[] { root }.Concat(root.Children.SelectMany(Tree))
            .Concat(root is SearchableItem container ? container.Grids.SelectMany(g => g.Items).SelectMany(Tree) : Enumerable.Empty<Item>());
    }
}

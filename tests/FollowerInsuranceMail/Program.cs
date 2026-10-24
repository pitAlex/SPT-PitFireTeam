using EFT;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using JsonType;
using pitTeam.Modules;

int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
FollowerInsuranceMailClaim.Policy Policy(string id, string trader = "prapor") => new() { ItemId = id, TraderId = trader };
var plate = new Item { Id = "plate" };
var vest = new CompoundItem { Id = "vest", Children = [plate] };
var ordinary = new Item { Id = "ordinary" };
var profile = new Profile { Id = "pmc", Inventory = new() { Items = [vest, plate, ordinary] },
    InsuredItems = [new() { ItemId = "ordinary", TraderId = "therapist" }] };
var session = new ClientBackendSession { Profile = profile, InsuranceCompany = new() };
var move = new MoveOperation.MoveCommand { Item = "vest", FromOwner = new("mail", EOwnerType.Mail),
    ToOwner = new("pmc", EOwnerType.Profile) };
var ids = FollowerInsuranceMailClaim.ClaimedIds([move], profile);
Check(ids.SetEquals(["vest", "plate"]), "whole vest claim includes insured plate descendants");
move.ToOwner = null;
Check(FollowerInsuranceMailClaim.ClaimedIds([move], profile).SetEquals(ids), "native omitted main-profile owner is supported");
move.ToOwner = new("pmc", EOwnerType.Profile);
Check(FollowerInsuranceMailClaim.ClaimedIds([move, move], profile).Count == 2, "repeated roots deduplicate");
Check(FollowerInsuranceMailClaim.ClaimedIds(null, profile).Count == 0, "missing command list is safe");
move.FromOwner = new("pmc", EOwnerType.Profile);
Check(FollowerInsuranceMailClaim.ClaimedIds([move], profile).Count == 0, "ordinary inventory move does not fetch insurance");
move.FromOwner = new("mail", EOwnerType.Mail);
move.ToOwner = new("other", EOwnerType.Profile);
Check(FollowerInsuranceMailClaim.ClaimedIds([move], profile).Count == 0, "other profile mail move is ignored");
move.ToOwner = new("pmc", EOwnerType.Mail);
Check(FollowerInsuranceMailClaim.ClaimedIds([move], profile).Count == 0, "mail-to-mail move is ignored");
move.ToOwner = new("pmc", EOwnerType.Profile);
move.Item = "absent";
Check(FollowerInsuranceMailClaim.ClaimedIds([move], profile).Count == 0, "failed or removed root is ignored");
move.Item = "vest";
profile.Inventory.Items.Add(vest);
Check(FollowerInsuranceMailClaim.ClaimedIds([move], profile).Count == 0, "ambiguous root identity is ignored");
profile.Inventory.Items.RemoveAt(profile.Inventory.Items.Count - 1);
Check(FollowerInsuranceMailClaim.Apply(session, profile, ids, [Policy("plate")]) == 1,
    "exact claimed plate registered with native company");
Check(profile.InsuredItems.Length == 2 && profile.InsuredItems[0].TraderId == "therapist",
    "existing unrelated coverage and insurer preserved");
Check(session.InsuranceCompany.Registered.Single() == plate, "registration uses actual live item instance");
Check(session.InsuranceCompany.OnItemInsured.Items.Single().Id == "plate", "native shield refresh event published");
Check(FollowerInsuranceMailClaim.Apply(session, profile, ids, [Policy("plate")]) == 0,
    "repeated sync does not duplicate policy or event");
Check(session.InsuranceCompany.OnItemInsured.Items.Count == 1, "repeat does not republish insured event");
Check(FollowerInsuranceMailClaim.Apply(session, profile, ids, [Policy("ordinary")]) == 0,
    "response cannot modify unclaimed item");
Check(FollowerInsuranceMailClaim.Apply(session, profile, ids, [Policy("vest"), Policy("vest")]) == 0,
    "duplicate response identity rejected");
Check(FollowerInsuranceMailClaim.Apply(session, profile, ids, [Policy("plate", "therapist")]) == 0,
    "conflicting insurer does not overwrite policy");
Check(FollowerInsuranceMailClaim.Apply(session, profile, ids, [Policy("vest", "")]) == 0,
    "missing insurer rejected");
profile.Inventory.Items.Remove(plate);
Check(FollowerInsuranceMailClaim.Apply(session, profile, ids, [Policy("plate")]) == 0,
    "late response cannot register removed item");
profile.Inventory.Items.Add(plate);
session.InsuranceCompany = new();
Check(FollowerInsuranceMailClaim.Apply(session, profile, ids, [Policy("plate")]) == 1,
    "already saved profile policy still refreshes missing company entry");
session.Profile = new();
Check(FollowerInsuranceMailClaim.Apply(session, profile, ids, [Policy("vest")]) == 0,
    "changed session profile rejects stale snapshot");
session.Profile = profile;
profile.Inventory.Items.Add(new Item { Id = "vest" });
Check(FollowerInsuranceMailClaim.Apply(session, profile, ids, [Policy("vest")]) == 0,
    "duplicate live item identity rejected");
profile.Inventory.Items.RemoveAt(profile.Inventory.Items.Count - 1);
profile.InsuredItems = [.. profile.InsuredItems, new() { ItemId = "vest", TraderId = "prapor" },
    new() { ItemId = "vest", TraderId = "prapor" }];
Check(FollowerInsuranceMailClaim.Apply(session, profile, ids, [Policy("vest")]) == 0,
    "duplicate live policies rejected");

var loop = FollowerInsuranceMailClaim.Refresh(session, ids);
while (loop.MoveNext()) Thread.Yield();
Check(pitTeam.pitFireTeam.Log.Warnings == 1, "failed optional fetch is contained after successful claim");
Check(profile.Inventory.Items.Count == 3, "display refresh never moves inventory");
Console.WriteLine($"PASS: {checks} mail insurance client checks");

namespace JsonType
{
    public class InsuredProfileItems { public string ItemId; public string TraderId; }
}
namespace EFT.InventoryLogic
{
    public enum EOwnerType { Profile, Mail }
    public class Item { public string Id; }
    public class CompoundItem : Item
    {
        public List<Item> Children = [];
        public IEnumerable<Item> GetAllItems() => Children;
    }
}
namespace EFT.InventoryLogic.Operations
{
    public class MoveOperation
    {
        public class MoveCommand { public string Item; public EFT.OwnerInfo FromOwner; public EFT.OwnerInfo ToOwner; }
    }
}
namespace EFT
{
    public class OwnerInfo(string id, EOwnerType type) { public string Id = id; public EOwnerType Type = type; }
    public class Profile { public string Id; public Inventory Inventory; public InsuredProfileItems[] InsuredItems; }
    public class Inventory { public List<Item> Items = []; public IEnumerable<Item> AllRealPlayerItems => Items; }
    public class ClientBackendSession { public Profile Profile; public InsuranceCompany InsuranceCompany; }
    public class InsuranceCompany
    {
        public List<Item> Registered = [];
        public InsuranceEvent OnItemInsured = new();
        public bool Insured(string id) => Registered.Any(item => item.Id == id);
        public Item RegisterSingleItem(InsuredProfileItems policy, Item item) { Registered.Add(item); return item; }
    }
    public class InsuranceEvent { public List<Item> Items = []; public void Invoke(Item item) => Items.Add(item); }
}
namespace SPT.Common.Http
{
    public static class RequestHandler
    {
        public static string PostJson(string route, string payload) => throw new Exception("simulated offline request");
    }
}
namespace pitTeam
{
    public static class Logger { public static void LogInfo(string message) { } }
    public static class pitFireTeam { public static TestLog Log = new(); }
    public class TestLog { public int Warnings; public void LogWarning(string message) { Warnings++; } }
}

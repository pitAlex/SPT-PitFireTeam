using EFT;
using pitTeam.Modules;
using Newtonsoft.Json.Linq;

int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
Policy Policy(string id) => new() { ItemId = id };
var live = new Profile { InsuredItems = [Policy("gift"), Policy("scope"), Policy("keep")] };
var backend = new Profile { InsuredItems = [Policy("gift"), Policy("keep")] };
var session = new Session { Profile = backend, InsuranceCompany = new() };
session.InsuranceCompany.InsuredItems.AddRange([new() { Id = "gift" }, new() { Id = "keep" }]);
session.InsuranceCompany._missingInsuredItems.AddRange([Policy("scope"), Policy("keep")]);
GiveGearInsurance.Cancel(session, live, ["GIFT", "scope", "GIFT"]);
await FollowerInsuranceRaidReports.Last.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
Check(live.InsuredItems.Single().ItemId == "keep", "live policy cancellation exact");
Check(backend.InsuredItems.Single().ItemId == "keep", "backend profile updated even when distinct");
Check(session.InsuranceCompany.InsuredItems.Single().Id == "keep", "native registered coverage cancelled");
Check(session.InsuranceCompany._missingInsuredItems.Single().ItemId == "keep", "missing native coverage cannot reappear");
Check(FollowerInsuranceRaidDisplay.Cancelled.SetEquals(["GIFT", "scope"]), "follower shield cache receives exact IDs");
Check(JObject.Parse(FollowerInsuranceRaidReports.Last.Json)["ItemIds"].Values<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
    .SetEquals(["gift", "scope"]), "report exact IDs deduplicated");
Check(FollowerInsuranceRaidReports.Last.Route == "/singleplayer/pitfireteam/give-gear/insurance-cancel", "tracked cancellation route");
int prepared = FollowerInsuranceRaidReports.Prepared;
GiveGearInsurance.Cancel(session, live, []);
Check(FollowerInsuranceRaidReports.Prepared == prepared, "empty retained gift does not create report");
session.Profile = live;
GiveGearInsurance.Cancel(session, live, ["gift"]);
await FollowerInsuranceRaidReports.Last.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
Check(live.InsuredItems.Single().ItemId == "keep", "same profile/repeated cancellation safe");

Player Human(params string[] ids) => new() { ProfileId = "human", IsYourPlayer = true,
    Profile = new() { InsuredItems = ids.Select(Policy).ToArray() } };
BotOwner Pmc(params string[] ids) => new() { Side = EPlayerSide.Usec, GetPlayer = new() {
    InventoryController = new() { Inventory = new() { Equipment = new(ids) } } } };
void Begin() { GiveGearInsurance.Reset(); BossPlayers.Followers.Clear(); FollowerInsuranceRaidDisplay.Cancelled.Clear(); }
async Task Finish(Player human)
{
    int before = FollowerInsuranceRaidReports.Prepared;
    GiveGearInsurance.EndRaid(human.ProfileId);
    if (FollowerInsuranceRaidReports.Prepared > before)
        await FollowerInsuranceRaidReports.Last.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
}
Begin();
var human = Human("gift", "scope", "keep");
var pmc = Pmc("gift", "scope");
session.Profile = human.Profile;
prepared = FollowerInsuranceRaidReports.Prepared;
GiveGearInsurance.Track(session, human, pmc, ["gift", "scope", "gift"]);
Check(human.Profile.InsuredItems.Length == 3 && FollowerInsuranceRaidReports.Prepared == prepared,
    "committed handover keeps native insurance and sends no cancellation");
Check(FollowerInsuranceRaidDisplay.Cancelled.Count == 0, "in-raid shields remain unchanged");
GiveGearInsurance.EndRaid("other-human");
Check(FollowerInsuranceRaidReports.Prepared == prepared, "wrong owner cannot settle gift coverage");
human.InventoryController.Inventory.Equipment = new("scope");
await Finish(human);
Check(human.Profile.InsuredItems.Select(p => p.ItemId).SequenceEqual(["scope", "keep"]),
    "surviving PMC retains gift but detached recovered scope stays insured");
Check(FollowerInsuranceRaidDisplay.Cancelled.SetEquals(["gift"]), "only final retained exact IDs lose shield");
prepared = FollowerInsuranceRaidReports.Prepared;
await Finish(human);
Check(FollowerInsuranceRaidReports.Prepared == prepared, "death teardown plus Stop is idempotent");

Begin();
human = Human("gift", "scope"); pmc = Pmc("gift", "scope"); session.Profile = human.Profile;
GiveGearInsurance.Track(session, human, pmc, ["gift", "scope"]);
pmc.IsDead = true; human.InventoryController.Inventory.Equipment = new("gift", "scope");
prepared = FollowerInsuranceRaidReports.Prepared;
await Finish(human);
Check(human.Profile.InsuredItems.Length == 2 && FollowerInsuranceRaidReports.Prepared == prepared,
    "corpse recovery keeps original policies without report or restoration");

Begin();
human = Human("gift"); pmc = Pmc("gift"); session.Profile = human.Profile;
GiveGearInsurance.Track(session, human, pmc, ["gift"]); pmc.GetPlayer.HealthController.IsAlive = false;
prepared = FollowerInsuranceRaidReports.Prepared;
await Finish(human);
Check(human.Profile.InsuredItems.Length == 1 && FollowerInsuranceRaidReports.Prepared == prepared,
    "dead health controller is authoritative before BotOwner death publication");

Begin();
human = Human("gift", "dropped", "loan"); pmc = Pmc("loan"); session.Profile = human.Profile;
GiveGearInsurance.Track(session, human, pmc, ["gift", "dropped", "loan"]);
var second = Pmc("gift"); BossPlayers.Followers.Add(new() { Bot = second });
pmc.GetPlayer.InventoryController.Inventory.Equipment.Items.Single().Protected = true;
await Finish(human);
Check(human.Profile.InsuredItems.Select(p => p.ItemId).SequenceEqual(["dropped", "loan"]),
    "other living field PMC can retain gift; dropped gifts and protected loans keep coverage");

Begin();
human = Human("gift"); pmc = Pmc(); session.Profile = human.Profile;
GiveGearInsurance.Track(session, human, pmc, ["gift"]);
BossPlayers.Followers.Add(new() { Bot = Pmc("gift"), IsSquadMate = true });
prepared = FollowerInsuranceRaidReports.Prepared;
await Finish(human);
Check(human.Profile.InsuredItems.Length == 1 && FollowerInsuranceRaidReports.Prepared == prepared,
    "saved teammate retention is not a permanent recruit gift");

Begin();
human = Human("gift"); pmc = Pmc("gift"); pmc.Side = EPlayerSide.Savage; session.Profile = human.Profile;
GiveGearInsurance.Track(session, human, pmc, ["gift"]);
await Finish(human);
Check(human.Profile.InsuredItems.Length == 1, "Scav gift handling unchanged");

Begin();
human = Human("gift", "second"); pmc = Pmc("gift", "second"); session.Profile = human.Profile;
GiveGearInsurance.Track(session, human, pmc, ["gift"]);
GiveGearInsurance.Track(session, human, pmc, ["second"]);
await Finish(human);
Check(human.Profile.InsuredItems.Length == 0, "multiple handovers and dismissed original recipient merge exactly once");

Begin();
human = Human("gift"); pmc = Pmc("gift"); session.Profile = human.Profile;
GiveGearInsurance.Track(session, human, pmc, ["gift"]);
GiveGearInsurance.Reset(); prepared = FollowerInsuranceRaidReports.Prepared;
await Finish(human);
Check(human.Profile.InsuredItems.Length == 1 && FollowerInsuranceRaidReports.Prepared == prepared,
    "new raid reset cannot cancel previous raid gift IDs");

Begin();
human = Human("gift"); pmc = Pmc("gift"); session.Profile = human.Profile;
GiveGearInsurance.Track(session, human, pmc, ["gift"]);
pmc.GetPlayer.InventoryController.Inventory.Equipment = null;
await Finish(human);
Check(human.Profile.InsuredItems.Length == 1 && FollowerInsuranceRaidReports.Failed && Logger.Errors.Count == 1,
    "unavailable live inventory fails settlement without guessing away coverage");

Begin();
human = Human("gift"); pmc = Pmc("gift"); session.Profile = human.Profile;
GiveGearInsurance.Track(session, human, pmc, ["gift"]);
pmc.GetPlayer.Destroyed = true;
int errors = Logger.Errors.Count;
await Finish(human);
Check(human.Profile.InsuredItems.Length == 1 && Logger.Errors.Count == errors + 1,
    "destroyed but not confirmed dead recipient cannot silently become a lost gift");

Begin();
human = Human("gift"); pmc = Pmc("gift"); session.Profile = human.Profile;
GiveGearInsurance.Track(session, human, pmc, ["gift"]);
human.HealthController.IsAlive = false;
await Finish(human);
Check(human.Profile.InsuredItems.Length == 0, "player death still settles gifts on surviving PMC before teardown");
Console.WriteLine($"PASS: {checks} production Give Gear client insurance checks.");

namespace EFT
{
    public enum EPlayerSide { Usec, Bear, Savage }
    public class Player { public string ProfileId; public bool IsYourPlayer, Destroyed;
        public Profile Profile; public Health HealthController = new(); public Controller InventoryController = new(); }
    public class Health { public bool IsAlive = true; }
    public class Controller { public Inventory Inventory = new(); }
    public class Inventory { public EFT.InventoryLogic.Item Equipment = new(); }
    public class BotOwner { public EPlayerSide Side; public bool IsDead; public Player GetPlayer; }
    public class Policy { public string ItemId; }
    public class Profile { public Policy[] InsuredItems; }
    public class InsuredItem { public string Id; }
    public class Insurance { public List<InsuredItem> InsuredItems = []; public List<Policy> _missingInsuredItems = []; }
    public interface IEftSession { Profile Profile { get; } Insurance InsuranceCompany { get; } }
    public class Session : IEftSession { public Profile Profile { get; set; } public Insurance InsuranceCompany { get; set; } }
}
namespace EFT.InventoryLogic
{
    public class Item(params string[] ids)
    {
        public string Id; public bool Protected;
        public List<Item> Items = ids.Select(id => new Item { Id = id }).ToList();
        public IEnumerable<Item> GetAllItems() => Items;
    }
}
namespace pitTeam.Modules
{
    internal static class Logger { public static List<object> Errors = []; public static void LogError(object error) => Errors.Add(error); }
    internal static class InteractableObjects { public static bool IsProtectedFollowerEquipment(EFT.InventoryLogic.Item item) => item.Protected; }
    internal class Follower { public bool IsSquadMate; public BotOwner Bot; public BotOwner GetBot() => Bot; }
    internal static class BossPlayers { public static List<Follower> Followers = []; public static List<Follower> GetFollowersByBoss(string id) => Followers; }
    internal static class FollowerInsuranceRaidDisplay
    {
        public static HashSet<string> Cancelled = new(StringComparer.OrdinalIgnoreCase);
        internal static void CancelGifts(IEnumerable<string> ids) => Cancelled.UnionWith(ids);
    }
    internal static class FollowerInsuranceRaidReports
    {
        public static Report Last; public static int Prepared; public static bool Failed;
        public static void Fail() => Failed = true;
        public static Report Prepare(string json) { Prepared++; return Last = new() { Json = json }; }
        internal sealed class Report
        {
            public string Json, Route;
            public TaskCompletionSource<bool> Sent = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public void Send(string route) { Route = route; Sent.TrySetResult(true); }
        }
    }
}

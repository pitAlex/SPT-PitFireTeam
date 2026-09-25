using pitTeam.Server.Models;
using pitTeam.Server.Services;
using System.Text.Json;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
HashSet<string> Ids(params string[] ids) => new(ids, StringComparer.OrdinalIgnoreCase);
FriendlyTeammateInsuredItem Policy(string id, string trader = "prapor") => new() { ItemId = id, TraderId = trader };
var none = new Dictionary<string, string>();
var initial = new[] { Policy("gun"), Policy("scope", "therapist"), Policy("playerArmor") };
var moved = FollowerInsuranceReconciler.Reconcile(initial, [], Ids("gun", "scope", "playerArmor"), [],
    Ids("playerArmor"), Ids("gun", "scope", "uninsuredPart"), none, true);
Check(moved.Player.Count == 1 && moved.Player[0].ItemId == "playerArmor", "unrelated player equipment preserved");
Check(moved.Follower.Count == 2, "gun and scope migrate, uninsured attachment does not inherit");
Check(moved.Follower.Single(p => p.ItemId == "scope").TraderId == "therapist", "separate insurer retained");
Check(initial.Length == 3 && initial[0].ItemId == "gun", "planning does not mutate inputs / cancel");

var settings = new FriendlyTeammateSettings { InsuredItems = moved.Follower };
var restarted = JsonSerializer.Deserialize<FriendlyTeammateSettings>(JsonSerializer.Serialize(settings))!;
Check(restarted.InsuredItems.SequenceEqual(moved.Follower), "settings restart round trip");
Check(JsonSerializer.Deserialize<FriendlyTeammateSettings>("{}")!.InsuredItems.Count == 0, "legacy settings default");
var partial = FollowerInsuranceReconciler.Reconcile(moved.Player, restarted.InsuredItems,
    Ids("playerArmor"), Ids("gun", "scope", "uninsuredPart"), Ids("playerArmor", "scope"), Ids("gun", "uninsuredPart"), none, true);
Check(partial.Player.Any(p => p.ItemId == "scope") && partial.Follower.Single().ItemId == "gun", "detached scope returns separately");
var returned = FollowerInsuranceReconciler.Reconcile(partial.Player, partial.Follower,
    Ids("playerArmor", "scope"), Ids("gun", "uninsuredPart"), Ids("playerArmor", "scope", "gun", "uninsuredPart"), [], none, true);
Check(returned.Player.Count == 3 && returned.Follower.Count == 0, "gun returns without duplicate policy");
var sold = FollowerInsuranceReconciler.Reconcile(returned.Player.Where(p => p.ItemId != "scope"), [],
    Ids("playerArmor", "gun", "uninsuredPart"), [], Ids("playerArmor", "gun", "uninsuredPart"), [], none, true);
Check(sold.Player.All(p => p.ItemId != "scope"), "stock-deleted policy cannot resurrect");
var pruned = FollowerInsuranceReconciler.Reconcile([], [Policy("missing"), Policy("gun")], [], Ids("gun"), [], Ids("gun"), none, true);
Check(pruned.Follower.Count == 1, "orphan policy pruned");
var restricted = FollowerInsuranceReconciler.Reconcile(initial, [Policy("oldFollower")],
    Ids("gun", "scope", "playerArmor"), Ids("oldFollower"), Ids("playerArmor", "oldFollower"), Ids("gun", "scope"), none, false);
Check(restricted.Player.Single().ItemId == "playerArmor" && restricted.Follower.Count == 0, "Restricted clears follower coverage");
var remap = new Dictionary<string, string> { ["collision"] = "repaired" };
var collision = FollowerInsuranceReconciler.Reconcile([Policy("collision")], [], Ids("collision"), Ids("collision"),
    Ids("collision"), Ids("repaired"), remap, true);
Check(collision.Player.Count == 1 && collision.Follower.Count == 0, "collision repair cannot copy player coverage");
var repaired = FollowerInsuranceReconciler.Reconcile([Policy("collision")], [Policy("collision", "therapist")],
    Ids("collision"), Ids("collision"), Ids("collision"), Ids("repaired"), remap, true);
Check(repaired.Follower.Single() == Policy("repaired", "therapist"), "verified follower policy remap");
bool rejected = false;
try { FollowerInsuranceReconciler.Reconcile([Policy("gun")], [Policy("gun", "therapist")], Ids("gun"), Ids("gun"), [], Ids("gun"), none, true); }
catch (InvalidOperationException) { rejected = true; }
Check(rejected, "conflicting traders rejected");
rejected = false;
try { FollowerInsuranceReconciler.Reconcile([Policy("gun")], [], Ids("gun"), [], Ids("gun"), Ids("gun"), none, true); }
catch (InvalidOperationException) { rejected = true; }
Check(rejected, "two owners rejected");
var repeat = FollowerInsuranceReconciler.Reconcile(moved.Player, moved.Follower, Ids("playerArmor"), Ids("gun", "scope"),
    Ids("playerArmor"), Ids("gun", "scope"), none, true);
Check(repeat.Player.SequenceEqual(moved.Player) && repeat.Follower.SequenceEqual(moved.Follower), "repeat save idempotent");
var owned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["gun"] = "gun", ["scope"] = "scope", ["med"] = "med" };
double Quote(string id) => id == "gun" ? 500 : 100;
var plan = FollowerInsurancePurchasePlanner.Plan(["gun", "scope", "GUN"], owned, Ids(), id => id != "med", Quote, 600);
Check(plan.Total == 600 && plan.Items.Count == 2, "purchase deduplicates exact IDs");
var retry = FollowerInsurancePurchasePlanner.Plan(["gun", "scope"], owned, Ids("gun", "scope"), _ => true, Quote, 600);
Check(retry.Total == 0 && retry.Items.Count == 0, "purchase retry requires no payment");
var upgrade = FollowerInsurancePurchasePlanner.Plan(["gun", "scope"], owned, Ids("gun"), _ => true, Quote, 600);
Check(upgrade.Total == 100 && upgrade.Items.Single() == "scope", "already insured gun never charged again");
void RejectPurchase(string name, IEnumerable<string> ids, Func<string, bool> eligible, Func<string, double> quote, int ceiling)
{
    bool failed = false;
    try { FollowerInsurancePurchasePlanner.Plan(ids, owned, Ids(), eligible, quote, ceiling); }
    catch (InvalidOperationException) { failed = true; }
    Check(failed, name);
}
RejectPurchase("unknown / another owner's item", ["gun", "foreign"], _ => true, Quote, 9999);
RejectPurchase("ineligible child rejects whole selection", ["gun", "med"], id => id != "med", Quote, 9999);
RejectPurchase("empty purchase", [], _ => true, Quote, 0);
RejectPurchase("underquoted price", ["gun"], _ => true, Quote, 499);
RejectPurchase("invalid price NaN", ["gun"], _ => true, _ => double.NaN, 9999);
RejectPurchase("negative premium", ["gun"], _ => true, _ => -1, 9999);
RejectPurchase("fractional non-stock premium", ["gun"], _ => true, _ => 1.5, 9999);
RejectPurchase("overflow premium", ["gun", "scope"], _ => true, _ => int.MaxValue, int.MaxValue);
var raid = new FollowerInsuranceRaidDiagnostic
{
    RaidId = "raid-test", ServerId = "factory.pmc 1", Enabled = true,
    ReportsComplete = true,
    Participants = [new() { Aid = "1", ProfileId = "follower1", InitialEquipmentJson = "[{snapshot}]" }],
    InsuredItems = [new("helmet", "helmet-template", "prapor", "1", "backpack", "main"),
        new("nvg", "nvg-template", "therapist", "1", "helmet", "mod_nvg")]
};
FollowerInsuranceRaidFinding Finding(string id) => FollowerInsuranceRaidClassifier.Classify(raid).Single(f => f.ItemId == id);
Check(Finding("helmet").Status == "pending", "no loss before final player end");
raid.EndReceived = true;
raid.PlayerInventoryKnown = true;
Check(Finding("helmet").Status == "unresolved", "missing follower outcome is not loss");
raid.Participants[0].OutcomeReceived = true;
raid.Participants[0].Escaped = true;
Check(Finding("helmet").Status == "unresolved", "survivor without equipment evidence is not loss");
raid.Participants[0].EquipmentSnapshotKnown = true;
Check(Finding("helmet").Status == "lost-candidate", "discarded insured helmet from backpack qualifies despite owner surviving");
raid.Participants[0].SavedItemIds = ["helmet"];
Check(Finding("helmet").Reason == "teammate-saved-equipment", "retained helmet excludes return");
Check(Finding("nvg").Status == "lost-candidate" && Finding("nvg").TraderId == "therapist", "lost attachment evaluated independently with its original insurer");
raid.PlayerItemIds = ["nvg"];
Check(Finding("nvg").Reason == "player-final-inventory", "player looted attachment excludes return");
raid.PlayerItemIds.Clear();
raid.Participants.Add(new() { Aid = "2", ProfileId = "follower2", OutcomeReceived = true,
    Escaped = true, EquipmentSnapshotKnown = true, SavedItemIds = ["nvg"] });
Check(Finding("nvg").Reason == "teammate-saved-equipment", "other teammate retained attachment excludes return");
raid.Participants[1].SavedItemIds.Clear();
raid.Participants[1].EscapedItemIds = ["nvg"];
Check(Finding("nvg").Reason == "escaped-carrier-awaiting-destination", "raw escaped cargo cannot be misclassified as lost");
raid.CourierItemIds = ["nvg"];
Check(Finding("nvg").Reason == "pitfireteam-courier", "late courier evidence resolves escaped cargo");
raid.CourierItemIds.Clear();
raid.Participants[1].EscapedItemIds.Clear();
raid.TransferItemIds = ["nvg"];
Check(Finding("nvg").Reason == "stock-transfer-service", "stock transfer excludes duplicate insurance");
raid.TransferItemIds.Clear();
raid.Participants[0].Escaped = false;
raid.Participants[0].EquipmentSnapshotKnown = false;
Check(Finding("nvg").Status == "lost-candidate", "dead owner outcome allows loss without reading looted corpse");
Check(Finding("helmet").Status == "retained", "permanent retained gear after owner death is not lost");
raid.Participants[1].OutcomeReceived = false;
Check(Finding("nvg").Status == "unresolved", "unknown second carrier blocks loss even when owner dead");
raid.Participants[1].OutcomeReceived = true;
raid.AwaitingTransit = true;
Check(FollowerInsuranceRaidClassifier.Classify(raid).All(f => f.Status == "deferred"), "transit segment cannot settle even retained items");
raid.AwaitingTransit = false;
raid.IncompleteTransit = true;
Check(Finding("nvg").Reason == "transit-evidence-incomplete", "broken transit evidence is not loss");
raid.IncompleteTransit = false;
raid.PlayerInventoryKnown = false;
Check(Finding("nvg").Reason == "player-final-inventory-missing", "missing player evidence is not loss");
raid.PlayerInventoryKnown = true;
raid.Enabled = false;
Check(FollowerInsuranceRaidClassifier.Classify(raid).All(f => f.Status == "ineligible"), "mode exit disables diagnostic loss eligibility");
raid.Enabled = true;
raid.InsuredItems.Add(raid.InsuredItems[0]);
Check(FollowerInsuranceRaidClassifier.Classify(raid).Count == 2, "duplicate identical policy snapshot collapses");
raid.InsuredItems.Add(new("nvg", "nvg-template", "prapor", "2", "root", "slot"));
Check(FollowerInsuranceRaidClassifier.Classify(raid).Where(f => f.ItemId == "nvg").All(f => f.Status == "unresolved"), "conflicting ownership or insurers block loss classification");
var raidRestart = JsonSerializer.Deserialize<FollowerInsuranceRaidDiagnostic>(JsonSerializer.Serialize(raid))!;
Check(raidRestart.RaidId == raid.RaidId && raidRestart.Participants[0].InitialEquipmentJson == "[{snapshot}]"
    && FollowerInsuranceRaidClassifier.Classify(raidRestart).SequenceEqual(FollowerInsuranceRaidClassifier.Classify(raid)),
    "persistent diagnostic round trip retains snapshot and classifications");
string beforeClassification = JsonSerializer.Serialize(raid);
var once = FollowerInsuranceRaidClassifier.Classify(raid);
Check(once.SequenceEqual(FollowerInsuranceRaidClassifier.Classify(raid)) && JsonSerializer.Serialize(raid) == beforeClassification,
    "classification repeat is deterministic and does not mutate evidence or policies");
var deliveredSources = FollowerInsuranceRaidClassifier.DeliveredSourceIds(["mail-clone"],
    new Dictionary<string, List<string>> { ["mail-clone"] = ["nvg", "nvg", ""], ["filtered-root"] = ["helmet"] });
Check(deliveredSources.Count == 2 && deliveredSources.Contains("nvg") && !deliveredSources.Contains("helmet"),
    "courier provenance follows accepted cloned trees only and collapses duplicate source IDs");
Check(FollowerInsuranceRaidClassifier.DeliveredSourceIds(["original", "original"], null).SequenceEqual(["original"]),
    "legacy courier without provenance preserves exact delivered IDs");
var barrier = new FollowerInsuranceRaidDiagnostic { ServerId = "raid-one" };
string reportA = Guid.NewGuid().ToString("N"), reportB = Guid.NewGuid().ToString("N");
Check(!FollowerInsuranceRaidBarrier.IsComplete(barrier), "legacy or missing completion cannot authorize loss");
Check(!FollowerInsuranceRaidBarrier.Observe(barrier, "old-raid", reportA) && barrier.ReceivedReportIds.Count == 0,
    "late prior raid receipt cannot contaminate current raid");
Check(!FollowerInsuranceRaidBarrier.Complete(barrier, "old-raid", [], false) && !barrier.ReportsComplete,
    "late prior raid completion is ignored");
FollowerInsuranceRaidBarrier.Complete(barrier, "raid-one", [reportB, reportA], false);
Check(!FollowerInsuranceRaidBarrier.IsComplete(barrier), "manifest arriving before receipts waits");
FollowerInsuranceRaidBarrier.Observe(barrier, "raid-one", reportA);
Check(!FollowerInsuranceRaidBarrier.IsComplete(barrier), "one missing delivery prevents completion");
FollowerInsuranceRaidBarrier.Observe(barrier, "raid-one", reportB);
Check(FollowerInsuranceRaidBarrier.IsComplete(barrier), "all matching receipts complete the barrier");
FollowerInsuranceRaidBarrier.Observe(barrier, "raid-one", reportA);
FollowerInsuranceRaidBarrier.Complete(barrier, "raid-one", [reportA, reportB, reportA], false);
Check(FollowerInsuranceRaidBarrier.IsComplete(barrier) && barrier.ReceivedReportIds.Count == 2,
    "duplicate receipts and equivalent repeated manifest are idempotent");
var barrierRestart = JsonSerializer.Deserialize<FollowerInsuranceRaidDiagnostic>(JsonSerializer.Serialize(barrier))!;
Check(FollowerInsuranceRaidBarrier.IsComplete(barrierRestart), "barrier receipts survive serialization restart");
FollowerInsuranceRaidBarrier.Observe(barrier, "raid-one", Guid.NewGuid().ToString("N"));
Check(!FollowerInsuranceRaidBarrier.IsComplete(barrier) && barrier.ReportsFailed,
    "unexpected report after completion invalidates negative conclusions");
FollowerInsuranceRaidBarrier.Complete(barrierRestart, "raid-one", [reportA], false);
Check(!FollowerInsuranceRaidBarrier.IsComplete(barrierRestart), "changed manifest cannot remove expected deliveries");
var failedBarrier = new FollowerInsuranceRaidDiagnostic { ServerId = "raid-one" };
FollowerInsuranceRaidBarrier.Complete(failedBarrier, "raid-one", [], true);
FollowerInsuranceRaidBarrier.Complete(failedBarrier, "raid-one", [], false);
Check(!FollowerInsuranceRaidBarrier.IsComplete(failedBarrier), "preparation or network failure stays sticky");
var malformedBarrier = new FollowerInsuranceRaidDiagnostic { ServerId = "raid-one" };
Check(!FollowerInsuranceRaidBarrier.Observe(malformedBarrier, "raid-one", ""), "untagged reports are not accepted");
FollowerInsuranceRaidBarrier.Complete(malformedBarrier, "raid-one", null, false);
Check(!FollowerInsuranceRaidBarrier.IsComplete(malformedBarrier), "missing manifest fails closed");
raid.InsuredItems = [raid.InsuredItems[1]];
raid.ReportsComplete = false;
Check(Finding("nvg").Reason == "raid-reports-incomplete", "apparently lost gear waits for completion");
raid.CourierItemIds = ["nvg"];
Check(Finding("nvg").Status == "recovered", "positive recovery evidence remains usable while completion is missing");
var settlement = new FollowerInsuranceRaidDiagnostic
{
    Enabled = true, SettlementEligible = true, EndReceived = true, PlayerInventoryKnown = true, ReportsComplete = true,
    Participants = [new() { Aid = "1", ProfileId = "follower1", OutcomeReceived = true }],
    InsuredItems = [new("gun", "gun-template", "prapor", "1", "root", "FirstPrimaryWeapon"),
        new("scope", "scope-template", "therapist", "1", "gun", "mod_scope")]
};
Check(FollowerInsuranceSettlementPlanner.Plan(settlement).Count == 2, "completed lost gear plans exact per-item insurer claims");
settlement.SettlementEligible = false;
Check(FollowerInsuranceSettlementPlanner.Plan(settlement).Count == 0, "legacy diagnostic raids cannot create retroactive rewards");
settlement.SettlementEligible = true;
settlement.PlayerItemIds = ["scope"];
Check(FollowerInsuranceSettlementPlanner.Plan(settlement).Single().ItemId == "gun", "extracted attachment cannot be claimed");
Check(FollowerInsuranceSettlementPlanner.PlanPlayerPolicyTransfer(settlement).Single().ItemId == "scope",
    "extracted follower attachment transfers its exact policy to the player");
settlement.Participants[0].SavedItemIds = ["scope"];
Check(FollowerInsuranceSettlementPlanner.PlanPlayerPolicyTransfer(settlement).Count == 0,
    "duplicate player and teammate ownership cannot grant another policy");
settlement.Participants[0].SavedItemIds.Clear();
settlement.CourierItemIds = ["scope"];
Check(FollowerInsuranceSettlementPlanner.PlanPlayerPolicyTransfer(settlement).Count == 0,
    "courier-recovered item cannot transfer as player-retained gear");
settlement.CourierItemIds.Clear();
settlement.PlayerPolicyTransferState = "reserved";
Check(FollowerInsuranceSettlementPlanner.PlanPlayerPolicyTransfer(settlement).Count == 0,
    "reserved player policy handoff cannot replay");
var transferRestart = JsonSerializer.Deserialize<FollowerInsuranceRaidDiagnostic>(JsonSerializer.Serialize(settlement))!;
Check(FollowerInsuranceSettlementPlanner.PlanPlayerPolicyTransfer(transferRestart).Count == 0,
    "player policy reservation survives restart");
settlement.PlayerPolicyTransferState = "";
settlement.PlayerItemIds.Clear();
settlement.Participants[0].SavedItemIds = ["gun"];
Check(FollowerInsuranceSettlementPlanner.Plan(settlement).Single().ItemId == "scope", "retained gun cannot be claimed with lost attachment");
settlement.Participants[0].SavedItemIds.Clear();
settlement.IncompleteTransit = true;
Check(FollowerInsuranceSettlementPlanner.Plan(settlement).Count == 0, "broken transit continuity prevents all claims");
settlement.IncompleteTransit = false;
settlement.SettlementState = "reserved";
Check(FollowerInsuranceSettlementPlanner.Plan(settlement).Count == 0, "durable reservation blocks retry or duplicate package");
var settlementRestart = JsonSerializer.Deserialize<FollowerInsuranceRaidDiagnostic>(JsonSerializer.Serialize(settlement))!;
Check(settlementRestart.SettlementState == "reserved" && FollowerInsuranceSettlementPlanner.Plan(settlementRestart).Count == 0,
    "reservation survives server restart without replaying a claim");
settlement.SettlementState = "";
settlement.ReportsFailed = true;
Check(FollowerInsuranceSettlementPlanner.Plan(settlement).Count == 0, "failed report barrier prevents all claims");
settlement.ReportsFailed = false;
settlement.InsuredItems.Add(new("scope", "scope-template", "prapor", "2", "root", "slot"));
Check(FollowerInsuranceSettlementPlanner.Plan(settlement).Count == 0, "conflicting insurer or owner prevents all claims");
settlement.EndReceived = false;
Check(FollowerInsuranceRaidClassifier.DisplayPolicies(settlement).Single().ItemId == "gun",
    "raid shields only include unambiguous exact insured IDs");
settlement.EndReceived = true;
Check(FollowerInsuranceRaidClassifier.DisplayPolicies(settlement).Count == 0, "raid shields stop after raid end");
settlement.EndReceived = false;
settlement.Enabled = false;
Check(FollowerInsuranceRaidClassifier.DisplayPolicies(settlement).Count == 0, "disabled insurance mode shows no follower shields");
Console.WriteLine($"PASS: {checks} follower insurance checks");

using pitTeam.Server.Services;
using pitTeam.Server.Persistence;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Common;
using pitTeam.Server.Models;

int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
Item I(string id, string tpl, string parent, string slot) => new(id, tpl, parent, slot);
var roots = new[] { "FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster", "Headwear", "FaceCover", "Eyewear", "Earpiece", "TacticalVest", "ArmorVest", "Backpack", "Scabbard" };
var items = roots.Select(slot => I(slot, slot, "equipment", slot)).ToList();
items.AddRange([
    I("scope", "part", "FirstPrimaryWeapon", "mod_scope"),
    I("mount", "part", "scope", "mod_mount"),
    I("mag", "part", "FirstPrimaryWeapon", "mod_magazine"),
    I("loaded", "ammo", "mag", "cartridges"),
    I("chamber", "ammo", "FirstPrimaryWeapon", "patron_in_weapon"),
    I("visor", "part", "Headwear", "mod_equipment"),
    I("nvg", "part", "visor", "mod_nvg"),
    I("rigPlate", "plate", "TacticalVest", "Front_plate"),
    I("armorPlate", "plate", "ArmorVest", "Back_plate"),
    I("integral", "insert", "ArmorVest", "soft_armor_front"),
    I("rigCargo", "part", "TacticalVest", "grid"),
    I("rigCargoChild", "part", "rigCargo", "mod_scope"),
    I("backpackCargo", "part", "Backpack", "mod_equipment"),
    I("pocketMeds", "part", "pockets", "grid"),
    I("pockets", "Pockets", "equipment", "Pockets"),
    I("secure", "SecuredContainer", "equipment", "SecuredContainer"),
    I("securePart", "part", "secure", "mod_scope"),
    I("dogtag", "part", "equipment", "Dogtag"),
    I("armband", "part", "equipment", "ArmBand")
]);
var quoted = new HashSet<string>();
int Calculate(IEnumerable<Item> source, Func<Item, double>? price = null) => TeammateEquipmentPrice.Calculate(source, "equipment",
    _ => ["mod_scope", "mod_mount", "mod_magazine", "patron_in_weapon", "cartridges", "mod_equipment", "mod_nvg", "Front_plate", "Back_plate", "soft_armor_front"],
    i => i.Template is "ammo" or "insert", i => i.Template == "plate",
    price ?? (i => { quoted.Add(i.Id); return 100; }));
Check(Calculate(items) == 1700, "Ten roots and seven mounted parts/plates are priced exactly once; knife is free");
Check(roots.Where(slot => slot != "Scabbard").All(quoted.Contains) && !quoted.Contains("Scabbard"), "All eligible equipment counts, excluding knife");
Check(!quoted.Overlaps(new[] { "loaded", "chamber", "integral", "rigCargo", "rigCargoChild", "backpackCargo", "pocketMeds", "securePart", "dogtag", "armband" }), "Supplies, grid subtrees and structural inserts excluded");
Check(Calculate(items.Concat([items[0]])) == 1700, "Duplicate references cannot double charge");
Check(Calculate([I("only", "part", "equipment", "headwear")], _ => 100.2) == 101, "Slot matching ignores case and total rounds upward once");
Check(Calculate([I("root", "part", "equipment", "Scabbard")], _ => throw new Exception("Knife must never be priced")) == 0, "Knife never requests a price, even if its template price is missing or expensive");
foreach (double bad in new[] { 0, -1, double.NaN, double.PositiveInfinity, (double)int.MaxValue + 1 })
{
    bool failed = false;
    try { Calculate([I("root", "part", "equipment", "Holster")], _ => bad); }
    catch (InvalidOperationException) { failed = true; }
    Check(failed, $"Unsafe price {bad} rejected");
}
Check(!TeammateDatabase.IsProfileDocument("pending-creation.json"), "Pending quote cannot enter the roster");
string root = Path.Combine(Path.GetTempPath(), "pit-hiring-tests-" + Guid.NewGuid().ToString("N"));
var db = new TeammateDatabase(root, "eeeeeeeeeeeeeeeeeeeeeeee");
db.Initialize(() => new Dictionary<string, string>());
db.WriteBatch(new Dictionary<string, string> { ["pending-creation.json"] = "{\"state\":\"paying\"}" });
Check(db.ReadProfiles().Count == 0, "A persisted quote/payment intent creates no teammate");
db.WriteBatch(new Dictionary<string, string>
{
    ["123.json"] = "{\"nickname\":\"Previewed teammate\"}",
    ["123-equipment.json"] = "[]",
    ["123-settings.json"] = "{}",
    ["pending-creation.json"] = "{\"state\":\"complete\"}"
});
var reopened = new TeammateDatabase(root, "eeeeeeeeeeeeeeeeeeeeeeee");
Check(reopened.ReadProfiles().Count == 1 && reopened.Read("pending-creation.json")!.Contains("complete"), "Teammate and completion receipt survive reopening together");
var session = new MongoId("eeeeeeeeeeeeeeeeeeeeeeee");
FriendlyTeammateService Service(string name) => new(Path.Combine(root, name));
FriendlyTeammateCreationPreview Prepare(FriendlyTeammateService service) => service.PrepareTeammateCreation(session, new() { Nickname = "Exact preview", Head = "head", Voice = "voice" });
int Money(FriendlyTeammateService service) => service.Player.Inventory.Items.Single(i => i.Template == "roubles").Upd!.StackObjectsCount;
void Reject(Action operation, string description)
{
    bool rejected = false;
    try { operation(); } catch (Exception ex) when (ex is FriendlyTeammateException or IOException) { rejected = true; }
    Check(rejected, description);
}
var hire = Service("purchase");
var unitPrices = Service("unit-prices");
var priceConfig = unitPrices.ragfairConfig.Dynamic.GenerateBaseFleaPrices;
priceConfig.PriceMultiplier = 1.5;
unitPrices.itemHelper.Handbook["weapon"] = 100;
unitPrices.itemHelper.Market["weapon"] = 400; // Existing flea 250 + generated 150 in SPT's additive table.
var priceItem = I("gun", "weapon", "equipment", "FirstPrimaryWeapon");
Check(unitPrices.PriceLookup()(priceItem) == 150, "Handbook generation ignores the inflated flea table and applies the multiplier once");
Check(unitPrices.itemHelper.Market["weapon"] == 400, "Hiring never mutates the shared flea table");
unitPrices.itemHelper.Parents["weapon"] = "firearm";
priceConfig.ItemTypeMultiplierOverride["firearm"] = 2;
Check(unitPrices.PriceLookup()(priceItem) == 200, "Base-class override takes precedence over default multiplier");
priceConfig.ItemTplMultiplierOverride["weapon"] = 3;
Check(unitPrices.PriceLookup()(priceItem) == 300, "Specific-template override takes precedence over base-class override");
priceConfig.UseHideoutCraftMultiplier = true;
priceConfig.HideoutCraftMultiplier = .8;
var recipe = new FixtureRecipe();
recipe.Requirements.Add(new() { TemplateId = "weapon" });
recipe.Requirements.Add(new() { TemplateId = "weapon" });
unitPrices.hideoutTable.Production.Recipes.Add(recipe);
Check(unitPrices.PriceLookup()(priceItem) == 380, "Crafting multiplier is additive and applies once despite duplicate recipes/requirements");
priceConfig.PreventPriceBeingBelowTraderBuyPrice = true;
unitPrices.traderHelper.Floor = 500;
Check(unitPrices.PriceLookup()(priceItem) == 500, "Configured trader floor is preserved");
priceConfig.UseHandbookPrice = false;
Check(unitPrices.PriceLookup()(priceItem) == 400, "Disabled handbook generation uses configured market prices");
unitPrices.itemHelper.Market["weapon"] = null;
Check(unitPrices.PriceLookup()(priceItem) == 100, "Missing market price falls back to handbook");
priceConfig.UseHandbookPrice = true;
unitPrices.itemHelper.Handbook["weapon"] = 0;
unitPrices.itemHelper.Market["weapon"] = 75;
Check(unitPrices.PriceLookup()(priceItem) == 75, "Modded item without handbook value uses its market price");

// Price inputs from the retained 485,538-rouble candidate audited on 2026-09-29.
// The knife is deliberately present but must never enter the corrected total.
var regression = Service("captured-prices");
var regressionConfig = regression.ragfairConfig.Dynamic.GenerateBaseFleaPrices;
regressionConfig.PriceMultiplier = 1.5;
regressionConfig.UseHideoutCraftMultiplier = true;
regressionConfig.HideoutCraftMultiplier = .8;
var captured = new[] {
    ("pack", "Backpack", 90480d, 0d, false), ("knife", "Scabbard", 5112d, 11184d, true),
    ("headset", "Earpiece", 24567d, 28216d, false), ("rig", "TacticalVest", 2907d, 76509d, false),
    ("ppsh", "FirstPrimaryWeapon", 10251d, 24749d, false), ("barrel", "mod_scope", 8200d, 0d, false),
    ("cover", "mod_scope", 1200d, 0d, false), ("stock", "mod_scope", 2120d, 0d, false),
    ("drum", "mod_magazine", 16000d, 17000d, true), ("pistol", "Holster", 19292d, 38537d, false),
    ("magazine", "mod_magazine", 1506d, 0d, false)
};
var capturedItems = new List<Item>();
foreach (var (tpl, slot, handbook, oldMarket, craft) in captured)
{
    regression.itemHelper.Handbook[tpl] = handbook;
    regression.itemHelper.Market[tpl] = oldMarket + handbook * (craft ? 2.3 : 1.5);
    if (craft) { var craftRecipe = new FixtureRecipe(); craftRecipe.Requirements.Add(new() { TemplateId = tpl }); regression.hideoutTable.Production.Recipes.Add(craftRecipe); }
    capturedItems.Add(I(tpl, tpl, slot.StartsWith("mod_") ? (tpl == "magazine" ? "pistol" : "ppsh") : "equipment", slot));
}
Check(Calculate(capturedItems, regression.PriceLookup()) == 277585, "Captured quote drops from 485,538 to 277,585 after fixing unit prices and excluding the knife");

var bayonet = Service("bayonet");
bayonet.GeneratedItems = [I("equipment", "root", "", "hideout"), I("gun", "weapon", "equipment", "FirstPrimaryWeapon"),
    I("oldKnife", "expensive", "equipment", "Scabbard"), I("knifeChild", "part", "oldKnife", "mod_equipment"),
    I("secondKnife", "expensive", "equipment", "scabbard"), I("dogtag", "tag", "equipment", "Dogtag"),
    I("pockets", "pockets", "equipment", "Pockets"), I("cargoKnife", "knife", "pockets", "grid")];
var bayonetPreview = Prepare(bayonet);
var normalized = bayonet.storage.Read<FriendlyTeammateCreationQuote>(session, "pending-creation.json")!;
var newKnives = normalized.Teammate.Inventory.Items.Where(i => i.SlotId == "Scabbard").ToList();
Check(newKnives.Count == 1 && newKnives[0].Template == pitTeam.Server.Constants.FriendlyItemTemplateIds.Weapon.HiringBayonet
    && newKnives[0].ParentId == "equipment" && newKnives[0].Upd!.StackObjectsCount == 1 && !newKnives[0].Upd!.SpawnedInSession,
    "Prepared candidate has exactly one fresh 6Kh5 bayonet in its equipment slot");
Check(!normalized.Teammate.Inventory.Items.Any(i => i.Id is "oldKnife" or "knifeChild" or "secondKnife")
    && normalized.Teammate.Inventory.Items.Any(i => i.Id == "dogtag") && normalized.Teammate.Inventory.Items.Any(i => i.Id == "cargoKnife"),
    "Replacement removes only equipped knife trees, preserving dogtag, inventory containers and unrelated cargo");
bayonet.CreateTeammate(session, new() { QuoteToken = bayonetPreview.QuoteToken, WithoutKit = true });
Check(bayonet.storage.Read<BotBase>(session, $"{bayonetPreview.Aid}.json")!.Inventory.Items
    .Single(i => i.SlotId == "Scabbard").Template == pitTeam.Server.Constants.FriendlyItemTemplateIds.Weapon.HiringBayonet,
    "No-gear hire retains the previewed free bayonet");
var preview = Prepare(hire);
Check(preview.Price == 100 && Money(hire) == 1000 && hire.storage.Database.ReadProfiles().Count == 0, "Prepare returns server price without charging or adding");
Check(hire.TryGetPendingTeammateProfile(session, preview.Aid, out _) && !hire.TryGetPendingTeammateProfile(session, "999", out _), "Only the quoted account is previewable");
hire.CancelTeammateCreation(session, new() { QuoteToken = preview.QuoteToken });
Reject(() => hire.CreateTeammate(session, new() { QuoteToken = preview.QuoteToken }), "Cancelled quotes cannot be purchased");
Check(Money(hire) == 1000 && hire.storage.Database.ReadProfiles().Count == 0, "Cancel has no financial or roster effect");
preview = Prepare(hire);
hire.CreateTeammate(session, new() { QuoteToken = preview.QuoteToken });
Check(Money(hire) == 900 && hire.GenerationCount == 2 && hire.storage.Database.ReadProfiles().Count == 1, "Confirm buys the existing candidate without regeneration");
hire.CreateTeammate(session, new() { QuoteToken = preview.QuoteToken });
Check(Money(hire) == 900 && hire.saveServer.Saves == 1, "Repeated confirmation returns receipt without another debit or player save");
Reject(() => hire.CreateTeammate(session, new()), "Legacy tokenless create cannot add a free teammate");

var reroll = Service("regenerate");
var replaced = Prepare(reroll);
for (int i = 0; i < 3; i++)
{
    var next = Prepare(reroll);
    Check(next.QuoteToken != replaced.QuoteToken && next.Aid != replaced.Aid, "Regenerate issues a new candidate and quote");
    Check(!reroll.TryGetPendingTeammateProfile(session, replaced.Aid, out _)
        && reroll.TryGetPendingTeammateProfile(session, next.Aid, out _), "Only the latest regenerated candidate can be previewed");
    Reject(() => reroll.CreateTeammate(session, new() { QuoteToken = replaced.QuoteToken }), "Replaced quote cannot be purchased");
    reroll.CancelTeammateCreation(session, new() { QuoteToken = replaced.QuoteToken });
    Check(reroll.TryGetPendingTeammateProfile(session, next.Aid, out _), "A stale cancel cannot discard the replacement candidate");
    Check(Money(reroll) == 1000 && reroll.storage.Database.ReadProfiles().Count == 0, "Regeneration never charges or adds a teammate");
    replaced = next;
}
reroll.CreateTeammate(session, new() { QuoteToken = replaced.QuoteToken });
Check(Money(reroll) == 900 && reroll.GenerationCount == 4
    && reroll.storage.Database.Read($"{replaced.Aid}.json") != null, "Confirm buys only the last regenerated candidate once");

foreach (string mode in new[] { "Restricted", "Immersive", "Extreme" })
{
    var bare = Service("no-kit-" + mode);
    bare.settingsService.LoadoutManagementMode = mode;
    var barePreview = Prepare(bare);
    Check(barePreview.SupportsWithoutKit, "New server advertises the no-kit purchase capability");
    var pending = bare.storage.Read<FriendlyTeammateCreationQuote>(session, "pending-creation.json")!;
    pending.Teammate.Inventory.Equipment = new MongoId("equipment");
    pending.Teammate.Inventory.Items.RemoveAll(i => i.SlotId == "Scabbard");
    pending.Teammate.Inventory.Items.AddRange([
        I("equipment", "root", "", "hideout"),
        I("knife", "knife", "equipment", "Scabbard"),
        I("dogtag", "tag", "equipment", "Dogtag"),
        I("armband", "band", "equipment", "ArmBand"),
        I("pockets", "pockets", "equipment", "Pockets"),
        I("special", "special", "pockets", "SpecialSlot1"),
        I("specialPart", "part", "special", "mod_equipment"),
        I("pocketMeds", "meds", "pockets", "grid"),
        I("secure", "container", "equipment", "SecuredContainer"),
        I("secureCargo", "bag", "secure", "grid"),
        I("secureAmmo", "ammo", "secureCargo", "grid"),
        I("scope", "scope", "gun", "mod_scope"),
        I("backpack", "bag", "equipment", "Backpack"),
        I("cargo", "loot", "backpack", "grid")
    ]);
    bare.storage.Write(session, "pending-creation.json", pending);
    bare.Player.Inventory.Items[0].Upd!.StackObjectsCount = 0;
    bare.CreateTeammate(session, new() { QuoteToken = barePreview.QuoteToken, WithoutKit = true });
    var receipt = bare.storage.Read<FriendlyTeammateCreationQuote>(session, "pending-creation.json")!;
    var retained = receipt.Teammate.Inventory.Items.Select(i => i.Id).ToHashSet();
    Check(retained.SetEquals(new[] { "equipment", "knife", "dogtag", "armband", "pockets", "special", "specialPart", "secure" }), $"{mode}: no-kit preserves permanent identity slots and empties cargo without orphaned gear");
    Check(receipt.WithoutKit && receipt.Price == 0 && receipt.State == "complete"
        && Money(bare) == 0 && bare.saveServer.Saves == 0, $"{mode}: no-kit works without funds and never saves or debits player money");
    Check(receipt.Teammate.Inventory.Equipment!.ToString() == "equipment"
        && receipt.Teammate.Info.Nickname == pending.Teammate.Info.Nickname && bare.GenerationCount == 1,
        $"{mode}: no-kit retains the previewed profile and equipment root");
    var savedDefault = bare.storage.Read<List<Item>>(session, $"{barePreview.Aid}-equipment.json")!;
    Check(savedDefault.Select(i => i.Id).ToHashSet().SetEquals(retained), $"{mode}: saved default contains no discarded kit");
    bare.CreateTeammate(session, new() { QuoteToken = barePreview.QuoteToken, WithoutKit = true });
    bare.CreateTeammate(session, new() { QuoteToken = barePreview.QuoteToken });
    Check(bare.storage.Database.ReadProfiles().Count == 1 && Money(bare) == 0 && bare.saveServer.Saves == 0,
        $"{mode}: retries and switching buttons cannot duplicate the no-kit hire or charge afterward");
}
var bareFailure = Service("no-kit-failure");
var bareFailurePreview = Prepare(bareFailure);
bareFailure.FailTeammateSave = true;
Reject(() => bareFailure.CreateTeammate(session, new() { QuoteToken = bareFailurePreview.QuoteToken, WithoutKit = true }), "No-kit storage failure propagates");
var originalQuote = bareFailure.storage.Read<FriendlyTeammateCreationQuote>(session, "pending-creation.json")!;
Check(originalQuote.State == "pending" && !originalQuote.WithoutKit && originalQuote.Price == 100
    && originalQuote.Teammate.Inventory.Items.Any(i => i.Id == "gun")
    && bareFailure.storage.Database.ReadProfiles().Count == 0 && Money(bareFailure) == 1000,
    "Failed no-kit commit preserves original paid quote, gear and money");
bareFailure.FailTeammateSave = false;
bareFailure.CreateTeammate(session, new() { QuoteToken = bareFailurePreview.QuoteToken });
Check(Money(bareFailure) == 900 && bareFailure.storage.Database.ReadProfiles().Count == 1, "Paid hire still works after a failed no-kit commit");
bareFailure.CreateTeammate(session, new() { QuoteToken = bareFailurePreview.QuoteToken, WithoutKit = true });
Check(bareFailure.storage.Read<BotBase>(session, $"{bareFailurePreview.Aid}.json")!.Inventory.Items.Any(i => i.Id == "gun")
    && Money(bareFailure) == 900, "No-kit retry cannot strip or refund an already purchased teammate");

var poor = Service("insufficient");
preview = Prepare(poor);
poor.Player.Inventory.Items[0].Upd!.StackObjectsCount = 99;
Reject(() => poor.CreateTeammate(session, new() { QuoteToken = preview.QuoteToken }), "Insufficient funds rejected");
try { poor.CreateTeammate(session, new() { QuoteToken = preview.QuoteToken }); }
catch (FriendlyTeammateException ex) { Check(ex.Message == "TeammateHireInsufficientFunds", "Insufficient funds returns the exact key used by the vanilla popup path"); }
Check(Money(poor) == 99 && poor.storage.Database.ReadProfiles().Count == 0, "Insufficient funds cannot partly charge or add");
Check(poor.TryGetPendingTeammateProfile(session, preview.Aid, out _), "Insufficient funds preserves the pending candidate");
poor.Player.Inventory.Items[0].Upd!.StackObjectsCount = 100;
poor.CreateTeammate(session, new() { QuoteToken = preview.QuoteToken });
Check(Money(poor) == 0 && poor.storage.Database.ReadProfiles().Count == 1, "Retry with exactly enough money adds the same candidate");

foreach (bool failPlayer in new[] { false, true })
{
    var failure = Service("failure-" + failPlayer);
    preview = Prepare(failure);
    failure.FailTeammateSave = !failPlayer;
    failure.saveServer.FailNext = failPlayer;
    Reject(() => failure.CreateTeammate(session, new() { QuoteToken = preview.QuoteToken }), "Injected save failure propagates");
    Check(Money(failure) == 1000 && failure.storage.Database.ReadProfiles().Count == 0, "Save failure restores money without adding teammate");
    failure.FailTeammateSave = false;
    failure.CreateTeammate(session, new() { QuoteToken = preview.QuoteToken });
    Check(Money(failure) == 900 && failure.storage.Database.ReadProfiles().Count == 1, "Retry after rollback charges once");
}
foreach (int balance in new[] { 1000, 900, 950 })
{
    var recovery = Service("recovery-" + balance);
    preview = Prepare(recovery);
    var record = recovery.storage.Read<FriendlyTeammateCreationQuote>(session, "pending-creation.json")!;
    record.State = "paying";
    record.MoneyBefore = "money:1000:stash:grid";
    record.MoneyAfter = "money:900:stash:grid";
    recovery.storage.Write(session, "pending-creation.json", record);
    recovery.Player.Inventory.Items[0].Upd!.StackObjectsCount = balance;
    if (balance == 950) Reject(() => recovery.RecoverTeammateCreation(session), "Ambiguous interrupted payment is blocked");
    else recovery.RecoverTeammateCreation(session);
    Check(Money(recovery) == balance && recovery.storage.Database.ReadProfiles().Count == (balance == 900 ? 1 : 0), "Recovery commits only evidence of paid equipment and never re-debits");
}
foreach (string scenario in new[] { "mode", "expired", "nickname", "pricing-version" })
{
    var invalid = Service(scenario);
    preview = Prepare(invalid);
    if (scenario == "mode") invalid.settingsService.LoadoutManagementMode = "Extreme";
    if (scenario == "nickname") invalid.DuplicateNickname = true;
    if (scenario is "expired" or "pricing-version")
    {
        var record = invalid.storage.Read<FriendlyTeammateCreationQuote>(session, "pending-creation.json")!;
        if (scenario == "expired") record.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
        else record.PricingVersion = 0;
        invalid.storage.Write(session, "pending-creation.json", record);
    }
    Reject(() => invalid.CreateTeammate(session, new() { QuoteToken = preview.QuoteToken }), $"Changed {scenario} rejected at commit");
    Reject(() => invalid.CreateTeammate(session, new() { QuoteToken = preview.QuoteToken, WithoutKit = true }), $"Changed {scenario} rejected for no-gear addition too");
    Check(Money(invalid) == 1000 && invalid.storage.Database.ReadProfiles().Count == 0, "Invalid commit leaves money and roster unchanged");
}
// The invite snapshot remains fixed even if the market or inventory changes later.
var capture = Service("recruit-capture");
var capturedRecruit = new FriendlyRecruitRequestEntry
{
    ProfileJson = System.Text.Json.JsonSerializer.Serialize(new BotBase
    {
        Aid = 701, Inventory = new() { Items = [I("rifle", "weapon", "equipment", "FirstPrimaryWeapon"), I("taiga", "expensiveKnife", "equipment", "Scabbard")] }
    })
};
capture.CaptureRecruitmentGearPrice(capturedRecruit);
Check(capturedRecruit.RecruitmentGearPrice == 100, "Recruit invite prices the captured gear and excludes the original knife");
capture.ragfairConfig.Dynamic.GenerateBaseFleaPrices.PriceMultiplier = 99;
capture.CaptureRecruitmentGearPrice(capturedRecruit);
Check(capturedRecruit.RecruitmentGearPrice == 100, "Re-reading an invite never reprices its stored recruitment fee");
Reject(() => capture.CaptureRecruitmentGearPrice(new()), "Unpriceable recruit profile cannot silently become a free invite");

void SaveMember(FriendlyTeammateService service, int? fee)
{
    service.storage.Write(session, "701.json", new BotBase { Aid = 701, Info = new() { Nickname = "Recruit" } });
    service.storage.Write(session, "701-settings.json", new FixtureTeammateSettings { RecruitmentGearPrice = fee });
    service.storage.Write(session, "701-equipment.json", new[] { I("gear", "weapon", "equipment", "FirstPrimaryWeapon") });
}
var removalRequest = new FriendlyTeammateDeleteRequest { AccountId = "701" };
foreach (int? fee in new int?[] { null, 0, 100 })
{
    var deletion = Service("delete-" + (fee?.ToString() ?? "manual"));
    SaveMember(deletion, fee);
    deletion.ragfairConfig.Dynamic.GenerateBaseFleaPrices.PriceMultiplier = 999;
    var deleted = deletion.DeleteTeammateWithPayment(session, removalRequest);
    Check(deleted.Deleted && Money(deletion) == 1000 - (fee ?? 0), "Deletion charges only the stored recruit fee; manual/legacy and zero-price recruits are free");
    Check(deletion.storage.Database.ReadProfiles().Count == 0 && !deletion.storage.Exists(session, "701-settings.json")
        && !deletion.storage.Exists(session, "701-equipment.json"), "Deletion removes profile/settings/default together");
    int saves = deletion.saveServer.Saves;
    Check(deletion.DeleteTeammateWithPayment(session, removalRequest).Deleted && Money(deletion) == 1000 - (fee ?? 0)
        && deletion.saveServer.Saves == saves, "Lost-response retry returns a receipt and cannot charge twice");
    Check(saves == (fee > 0 ? 1 : 0), "Free deletion does not save or debit the player");
}
var deletionPoor = Service("delete-poor");
SaveMember(deletionPoor, 1001);
try { deletionPoor.DeleteTeammateWithPayment(session, removalRequest); throw new Exception("Expected insufficient deletion funds"); }
catch (FriendlyTeammateException ex) { Check(ex.Message == "TeammateDeleteInsufficientFunds", "Deletion uses the dedicated insufficient-funds popup key"); }
Check(Money(deletionPoor) == 1000 && deletionPoor.storage.Database.ReadProfiles().Count == 1
    && deletionPoor.saveServer.Saves == 0, "Insufficient funds preserve member, money and default equipment");
deletionPoor.Player.Inventory.Items[0].Upd!.StackObjectsCount = 1001;
Check(deletionPoor.DeleteTeammateWithPayment(session, removalRequest).Deleted && Money(deletionPoor) == 0,
    "Exactly enough money permits deleting the recruit");

foreach (bool failPlayer in new[] { false, true })
{
    var failure = Service("delete-failure-" + failPlayer);
    SaveMember(failure, 100);
    failure.saveServer.FailNext = failPlayer;
    failure.storage.FailDeletion = !failPlayer;
    Reject(() => failure.DeleteTeammateWithPayment(session, removalRequest), "Deletion save failure propagates");
    Check(Money(failure) == 1000 && failure.storage.Database.ReadProfiles().Count == 1,
        "Failed player or deletion save restores money and retains the member");
    failure.storage.FailDeletion = false;
    Check(failure.DeleteTeammateWithPayment(session, removalRequest).Deleted && Money(failure) == 900,
        "Retry after failed deletion charges exactly once");
}
foreach (int balance in new[] { 1000, 900, 950 })
{
    var recovery = Service("delete-recovery-" + balance);
    SaveMember(recovery, 100);
    recovery.storage.Write(session, "pending-deletion.json", new FriendlyTeammateDeletionJournal
    {
        Aid = 701, Price = 100, State = "paying", MoneyBefore = "money:1000:stash:grid", MoneyAfter = "money:900:stash:grid"
    });
    recovery.Player.Inventory.Items[0].Upd!.StackObjectsCount = balance;
    if (balance == 950) Reject(() => recovery.RecoverTeammateDeletion(session), "Ambiguous deletion payment blocks recovery");
    else recovery.RecoverTeammateDeletion(session);
    Check(Money(recovery) == balance && recovery.storage.Database.ReadProfiles().Count == (balance == 900 ? 0 : 1),
        "Recovery deletes only when money proves payment, without another debit");
    if (balance == 900)
        Check(recovery.DeleteTeammateWithPayment(session, removalRequest).Deleted && Money(recovery) == 900,
            "Recovered deletion remains safe to retry");
}
// Payment rollback is not complete until the player's file contains the refund.
foreach (bool deletion in new[] { false, true })
{
    var refund = Service("refund-double-failure-" + deletion);
    string document = deletion ? "pending-deletion.json" : "pending-creation.json";
    string? token = null;
    if (deletion) { SaveMember(refund, 100); refund.storage.FailDeletion = true; }
    else { token = refund.PrepareTeammateCreation(session, new() { Nickname = "Refund" }).QuoteToken; refund.FailTeammateSave = true; }
    refund.saveServer.SkipCachedWrites = true;
    refund.saveServer.FailOnSaves.Add(2);
    Reject(() => { if (deletion) refund.DeleteTeammateWithPayment(session, removalRequest); else refund.CreateTeammate(session, new() { QuoteToken = token }); },
        "Database failure followed by refund-file failure propagates");
    Check(Money(refund) == 1000 && refund.saveServer.DiskMoney == 900, "Failed refund leaves differing memory/disk balances for reproduction");
    string State() => System.Text.Json.JsonDocument.Parse(refund.storage.Database.Read(document)!).RootElement.GetProperty("State").GetString()!;
    Check(State() == "refunding", "Refund intent and money snapshot survive the failed save");
    Reject(() => { if (deletion) refund.RecoverTeammateDeletion(session); else refund.RecoverTeammateCreation(session); },
        "SPT cached-write skip cannot falsely acknowledge a refund");
    Check(State() == "refunding" && refund.saveServer.DiskMoney == 900, "Recovery remains blocked while disk still holds the debit");
    refund.saveServer.Restart();
    if (deletion) refund.RecoverTeammateDeletion(session); else refund.RecoverTeammateCreation(session);
    Check(State() == "pending" && Money(refund) == 1000 && refund.saveServer.DiskMoney == 1000,
        "Restart recovery refunds persistently before enabling retry");
    Check(refund.storage.Database.ReadProfiles().Count == (deletion ? 1 : 0), "Refund recovery preserves the intended roster state");
    refund.storage.FailDeletion = false; refund.FailTeammateSave = false;
    if (deletion) refund.DeleteTeammateWithPayment(session, removalRequest); else refund.CreateTeammate(session, new() { QuoteToken = token });
    Check(Money(refund) == 900 && refund.saveServer.DiskMoney == 900, "Retry after recovered refund charges exactly once on disk");
}

var refundInventory = Service("refund-preserves-unrelated-items");
SaveMember(refundInventory, 100);
refundInventory.storage.FailDeletion = true;
refundInventory.saveServer.FailOnSaves.Add(2);
Reject(() => refundInventory.DeleteTeammateWithPayment(session, removalRequest), "Prepare unresolved refund for ownership checks");
refundInventory.saveServer.Restart();
refundInventory.Player.Inventory.Items.Add(I("new-cargo", "scope", "stash", "grid"));
refundInventory.RecoverTeammateDeletion(session);
Check(refundInventory.Player.Inventory.Items.Any(item => item.Id == "new-cargo") && refundInventory.saveServer.DiskMoney == 1000,
    "Refund recovery preserves unrelated items added after the payment failure");

var refundAmbiguous = Service("refund-ambiguous-money");
SaveMember(refundAmbiguous, 100); refundAmbiguous.storage.FailDeletion = true; refundAmbiguous.saveServer.FailOnSaves.Add(2);
Reject(() => refundAmbiguous.DeleteTeammateWithPayment(session, removalRequest), "Prepare unresolved refund for ambiguous-money checks");
refundAmbiguous.Player.Inventory.Items[0].Upd!.StackObjectsCount = 950;
Reject(() => refundAmbiguous.RecoverTeammateDeletion(session), "Changed money cannot be overwritten by a refund snapshot");
Check(Money(refundAmbiguous) == 950 && refundAmbiguous.storage.Read<FriendlyTeammateDeletionJournal>(session, "pending-deletion.json")!.State == "refunding",
    "Ambiguous refund retains the journal and live balance");

FriendlyRecruitRequestEntry Invite(string id, int aid) => new() { ProfileId = id, AccountId = aid.ToString(), RecruitmentGearPrice = 100 };
var acceptance = Service("acceptance-retry");
acceptance.storage.Write(session, "recruit-requests.json", new[] { Invite("recruit-one", 801) });
acceptance.FailRecruitCreationOn = 1;
Reject(() => acceptance.AcceptRecruitInvitation(session, "recruit-one"), "Acceptance commit failure propagates");
Check(acceptance.storage.Database.ReadProfiles().Count == 0 && acceptance.storage.Read<List<FriendlyRecruitRequestEntry>>(session, "recruit-requests.json")!.Count == 1
    && acceptance.storage.Read<Dictionary<string, int>>(session, "accepted-recruits.json") == null, "Failed acceptance preserves invite and publishes neither member nor receipt");
acceptance.FailRecruitCreationOn = 0;
Check(acceptance.AcceptRecruitInvitation(session, "recruit-one"), "Acceptance succeeds after failed commit");
Check(acceptance.storage.Database.ReadProfiles().Count == 1 && acceptance.storage.Read<List<FriendlyRecruitRequestEntry>>(session, "recruit-requests.json")!.Count == 0
    && acceptance.storage.Exists(session, "801-settings.json") && acceptance.storage.Exists(session, "801-equipment.json"), "Member, settings, Default, receipt and invite consumption commit together");
int creations = acceptance.RecruitCreations;
Check(acceptance.AcceptRecruitInvitation(session, "recruit-one") && acceptance.RecruitCreations == creations, "Lost-response retry uses the acceptance receipt");
acceptance.storage.DeleteTeammate(session, 801);
Check(acceptance.AcceptRecruitInvitation(session, "recruit-one") && acceptance.storage.Database.ReadProfiles().Count == 0,
    "Acceptance replay after member deletion cannot resurrect the member");

var overlapping = Service("acceptance-overlap");
overlapping.storage.Write(session, "recruit-requests.json", new[] { Invite("recruit-parallel", 802) });
var accepts = Enumerable.Range(0, 8).Select(_ => Task.Run(() => overlapping.AcceptRecruitInvitation(session, "recruit-parallel"))).ToArray();
Task.WaitAll(accepts);
Check(accepts.All(task => task.Result) && overlapping.RecruitCreations == 1 && overlapping.storage.Database.ReadProfiles().Count == 1,
    "Overlapping acceptance requests create exactly one member");

var acceptAll = Service("accept-all-partial-failure");
acceptAll.storage.Write(session, "recruit-requests.json", new[] { Invite("all-one", 810), Invite("all-two", 811), Invite("all-three", 812) });
acceptAll.FailRecruitCreationOn = 2;
Reject(() => acceptAll.AcceptAllRecruitInvitations(session), "Accept All partial failure propagates");
Check(acceptAll.storage.Database.ReadProfiles().Count == 1 && acceptAll.storage.Read<List<FriendlyRecruitRequestEntry>>(session, "recruit-requests.json")!.Select(i => i.ProfileId)
    .SequenceEqual(new[] { "all-two", "all-three" }), "Accept All leaves only unfinished invites pending");
acceptAll.FailRecruitCreationOn = 0;
Check(acceptAll.AcceptAllRecruitInvitations(session) && acceptAll.storage.Database.ReadProfiles().Count == 3
    && acceptAll.RecruitCreations == 4, "Accept All retry resumes without duplicating the first member");

var legacyAcceptance = Service("acceptance-legacy-window");
legacyAcceptance.storage.Write(session, "recruit-requests.json", new[] { Invite("legacy-recruit", 820) });
legacyAcceptance.storage.Write(session, "820.json", new BotBase { Id = new("legacy-recruit"), Aid = 820 });
Check(legacyAcceptance.AcceptRecruitInvitation(session, "legacy-recruit") && legacyAcceptance.RecruitCreations == 0
    && legacyAcceptance.storage.Database.ReadProfiles().Count == 1, "Old member-saved/invite-pending window consumes the invite without duplicating gear");
Check(!TeammateDatabase.IsProfileDocument("accepted-recruits.json"), "Acceptance receipts never enter the roster");

Check(!TeammateDatabase.IsProfileDocument("pending-deletion.json"), "Deletion journal never enters the roster");

// Courier returns use actual saved equipment, never the stored Default copy or a generation pass.
List<Item> RemovalKit() => [
    I("scope", "scope", "gun", "mod_scope"), // Child deliberately precedes its root in the saved array.
    I("ammo", "ammo", "mag", "cartridges") with { Upd = new() { StackObjectsCount = 17 } },
    I("gun", "gun", "equipment", "FirstPrimaryWeapon") with { Upd = new() { Durability = 42 }, Location = new { x = 4, y = 1 } },
    I("mag", "mag", "gun", "mod_magazine"),
    I("rig", "rig", "equipment", "TacticalVest"), I("plate", "plate", "rig", "Front_plate"),
    I("med", "med", "rig", "main"),
    I("pack", "pack", "equipment", "Backpack"), I("loot", "loot", "pack", "main"),
    I("armor", "armor", "equipment", "ArmorVest"), I("insert", "insert", "armor", "soft_armor_front"),
    I("pockets", "pockets", "equipment", "Pockets"), I("pocket", "pocket", "pockets", "pocket1"),
    I("special", "special", "pockets", "SpecialSlot1"),
    I("knife", "knife", "equipment", "Scabbard"), I("band", "band", "equipment", "ArmBand"),
    I("tag", "tag", "equipment", "Dogtag"),
    I("secure", "secure", "equipment", "SecuredContainer"), I("secureLoot", "secureLoot", "secure", "main"),
    I("equipment", "equipment", "", "hideout")
];
void SaveEquippedMember(FriendlyTeammateService service, int? fee)
{
    SaveMember(service, fee);
    service.storage.Write(session, "701.json", new BotBase { Aid = 701,
        Inventory = new() { Equipment = new("equipment"), Items = RemovalKit() } });
}
List<SPTarkov.Server.Core.Models.Eft.Profile.Message> Mail(FriendlyTeammateService service) =>
    service.saveServer.Profile.DialogueRecords?.Values.SelectMany(dialogue => dialogue.Messages ?? []).ToList() ?? [];
FriendlyTeammateDeletionJournal RemovalReceipt(FriendlyTeammateService service) =>
    service.storage.Read<FriendlyTeammateDeletionJournal>(session, "pending-deletion.json")!;
foreach (string mode in new[] { "Restricted", "Immersive", "Extreme" })
foreach (int? fee in new int?[] { null, 0, 100 })
{
    var courier = Service($"courier-{mode}-{fee?.ToString() ?? "manual"}");
    courier.settingsService.LoadoutManagementMode = mode;
    SaveEquippedMember(courier, fee);
    courier.DeleteTeammateWithPayment(session, removalRequest);
    var receipt = RemovalReceipt(courier);
    var mail = Mail(courier).Single();
    var returnedItems = mail.Items!.Data!;
    var expected = RemovalKit().Select(item => item.Id).Except(new[] { "equipment", "pockets", "tag" })
        .Except(mode == "Extreme" ? [] : new[] { "secure", "secureLoot" }).ToHashSet();
    Check(receipt.State == "complete" && receipt.DeliverySourceIds.ToHashSet().SetEquals(expected),
        $"{mode}/{fee}: all actual gear, ammo, consumables, cargo and permanent usable items return; shells and dogtag do not");
    Check(returnedItems.Count == expected.Count && !returnedItems.Any(item => expected.Contains(item.Id) || item.Id == "gear"),
        "Return uses fresh IDs and never the separate stored Default snapshot");
    Check(returnedItems.All(item => item.ParentId == mail.Items.Stash!.ToString() || returnedItems.Any(parent => parent.Id == item.ParentId)),
        "Returned trees retain their parent links, including children stored before their root");
    Check(returnedItems.Where(item => item.ParentId == mail.Items.Stash!.ToString()).All(item => item.SlotId == "main" && item.Location == null),
        "Only mail roots lose their previous equipment/grid location");
    Check(returnedItems.Single(item => item.Template == "gun").Upd!.Durability == 42
        && returnedItems.Single(item => item.Template == "ammo").Upd!.StackObjectsCount == 17,
        "Return preserves durability and remaining ammunition quantities");
    Check(courier.saveServer.DiskProfile.DialogueRecords!.Values.Single().Messages!.Single().Id == mail.Id
        && Money(courier) == 1000 - (fee ?? 0) && courier.notificationSendHelper.Messages.Count == 1,
        "Mail is saved before notification and recruit fee stays fixed");
    courier.DeleteTeammateWithPayment(session, removalRequest);
    Check(Mail(courier).Count == 1 && Money(courier) == 1000 - (fee ?? 0), "Completed removal replay neither mails nor charges again");
    Check(courier.insuranceDiagnostics.CourierIds.SetEquals(expected), "Future insurance claims receive every source ownership ID");
}
var courierPoor = Service("courier-insufficient");
SaveEquippedMember(courierPoor, 1001);
Reject(() => courierPoor.DeleteTeammateWithPayment(session, removalRequest), "Equipped recruit still requires the complete stored fee");
Check(Mail(courierPoor).Count == 0 && !courierPoor.storage.Exists(session, "pending-deletion.json")
    && courierPoor.storage.Exists(session, "701.json"), "Insufficient funds do not stage, mail or remove equipment");
foreach (bool playerFails in new[] { false, true })
{
    var courierRollback = Service("courier-rollback-" + playerFails);
    SaveEquippedMember(courierRollback, 100);
    courierRollback.storage.FailDeletion = !playerFails;
    courierRollback.saveServer.FailNext = playerFails;
    Reject(() => courierRollback.DeleteTeammateWithPayment(session, removalRequest), "Failure before committed deletion is reported");
    Check(Mail(courierRollback).Count == 0 && courierRollback.storage.Exists(session, "701.json") && Money(courierRollback) == 1000,
        "Uncommitted deletion retains the kit, refunds the fee and sends nothing");
}
foreach (int? fee in new int?[] { null, 100 })
{
    var courierFailure = Service("courier-save-failure-" + (fee?.ToString() ?? "manual"));
    SaveEquippedMember(courierFailure, fee);
    courierFailure.saveServer.SkipCachedWrites = true;
    courierFailure.saveServer.FailOnSaves.Add(fee > 0 ? 2 : 1);
    Reject(() => courierFailure.DeleteTeammateWithPayment(session, removalRequest), "Mail save failure retains a durable outbox");
    var staged = RemovalReceipt(courierFailure);
    Check(staged.State == "delivering" && !courierFailure.storage.Exists(session, "701.json")
        && Money(courierFailure) == 1000 - (fee ?? 0) && courierFailure.notificationSendHelper.Messages.Count == 0,
        "Committed removal is never refunded or notified before mail persistence");
    Reject(() => courierFailure.RecoverTeammateDeletion(session), "Cached save success cannot falsely acknowledge mail persistence");
    courierFailure.saveServer.Restart();
    courierFailure.RecoverTeammateDeletion(session);
    Check(RemovalReceipt(courierFailure).State == "complete" && Mail(courierFailure).Single().Id == staged.Delivery!.Id,
        "Restart resumes the exact prepared message, with no reroll or changed attachment IDs");
    Check(courierFailure.saveServer.DiskMoney == 1000 - (fee ?? 0) && Mail(courierFailure).Count == 1,
        "Restart recovery preserves paid/free removal balance and one delivery");
}
foreach (bool collected in new[] { false, true })
{
    var completionFailure = Service("courier-receipt-failure-" + collected);
    SaveEquippedMember(completionFailure, 100);
    completionFailure.storage.FailDeliveryCompletion = true;
    Reject(() => completionFailure.DeleteTeammateWithPayment(session, removalRequest), "Receipt failure leaves the outbox recoverable after saved mail");
    Check(completionFailure.saveServer.DiskProfile.SptData!.Migrations!.ContainsKey("pitFireTeam/removal-courier"),
        "Player receipt is durable before marking the teammate outbox complete");
    if (collected)
    {
        completionFailure.saveServer.Profile.DialogueRecords!.Clear(); // Simulate collect + delete dialogue.
        completionFailure.saveServer.SaveProfileAsync(session).GetAwaiter().GetResult();
    }
    completionFailure.saveServer.Restart();
    completionFailure.storage.FailDeliveryCompletion = false;
    completionFailure.RecoverTeammateDeletion(session);
    Check(Mail(completionFailure).Count == (collected ? 0 : 1) && Money(completionFailure) == 900
        && RemovalReceipt(completionFailure).State == "complete", "Receipt recovery cannot remail a collected/deleted kit or recharge its fee");
}
var evidenceFailure = Service("courier-evidence-failure");
SaveEquippedMember(evidenceFailure, null);
evidenceFailure.insuranceDiagnostics.FailNext = true;
Reject(() => evidenceFailure.DeleteTeammateWithPayment(session, removalRequest), "Insurance ownership failure preserves pending delivery");
Check(RemovalReceipt(evidenceFailure).State == "delivering" && evidenceFailure.saveServer.DiskProfile.DialogueRecords == null,
    "Mail cannot be saved ahead of durable insurance suppression evidence");
evidenceFailure.RecoverTeammateDeletion(session);
Check(RemovalReceipt(evidenceFailure).State == "complete" && Mail(evidenceFailure).Count == 1, "Ownership evidence failure recovers without losing the kit");

var oldJournal = Service("courier-old-payment-journal");
SaveEquippedMember(oldJournal, 100);
oldJournal.Player.Inventory.Items[0].Upd!.StackObjectsCount = 900;
oldJournal.storage.Write(session, "pending-deletion.json", new FriendlyTeammateDeletionJournal
{
    Aid = 701, Price = 100, State = "paying", MoneyBefore = "money:1000:stash:grid", MoneyAfter = "money:900:stash:grid"
});
oldJournal.RecoverTeammateDeletion(session);
Check(Mail(oldJournal).Count == 1 && Money(oldJournal) == 900 && RemovalReceipt(oldJournal).State == "complete",
    "Unfinished payment journals predating returns capture the member's saved kit before completing removal");

var insuranceReturn = Service("courier-insurance-cleanup");
SaveEquippedMember(insuranceReturn, null);
insuranceReturn.Player.InsuredItems = [new() { ItemId = new("gun") }, new() { ItemId = new("unrelated") }];
insuranceReturn.saveServer.Profile.InsuranceList = [new() { Items = [
    I("gun", "gun", "mailstash", "main"), I("insurance-child", "part", "gun", "mod_scope"),
    I("unrelated", "unrelated", "mailstash", "main") ] }];
insuranceReturn.DeleteTeammateWithPayment(session, removalRequest);
Check(insuranceReturn.Player.InsuredItems.Single().ItemId!.ToString() == "unrelated"
    && insuranceReturn.saveServer.Profile.InsuranceList.Single().Items!.Single().Id == "unrelated",
    "Courier cancels matching active policies and pending insurance trees while preserving unrelated coverage");
foreach (bool otherMember in new[] { false, true })
{
    var collisionReturn = Service("courier-ownership-collision-" + otherMember);
    SaveEquippedMember(collisionReturn, 100);
    if (otherMember) collisionReturn.storage.Write(session, "702.json", new BotBase { Aid = 702, Inventory = new() { Items = [I("gun", "gun", "equipment", "FirstPrimaryWeapon")] } });
    else collisionReturn.Player.Inventory.Items.Add(I("gun", "gun", "stash", "main"));
    Reject(() => collisionReturn.DeleteTeammateWithPayment(session, removalRequest), "Shared source ownership is rejected before courier cloning");
    Check(Mail(collisionReturn).Count == 0 && collisionReturn.storage.Exists(session, "701.json") && Money(collisionReturn) == 1000,
        "Ownership conflict cannot create a duplicate item or charge a fee");
}
var notificationFailure = Service("courier-notification-failure");
SaveEquippedMember(notificationFailure, null);
notificationFailure.notificationSendHelper.FailNext = true;
Check(notificationFailure.DeleteTeammateWithPayment(session, removalRequest).Deleted
    && Mail(notificationFailure).Count == 1 && RemovalReceipt(notificationFailure).State == "complete",
    "Websocket notification failure cannot undo a saved removal or remail the package");
Console.WriteLine($"PASS: {checks} pricing, recruitment, purchase/deletion, courier, rollback, replay and recovery checks. Fixture database: {root}");

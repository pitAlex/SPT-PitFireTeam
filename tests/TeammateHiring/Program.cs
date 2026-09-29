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
Check(!TeammateDatabase.IsProfileDocument("pending-deletion.json"), "Deletion journal never enters the roster");
Console.WriteLine($"PASS: {checks} pricing, recruitment, purchase/deletion, rollback, replay and recovery checks. Fixture database: {root}");

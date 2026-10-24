using pitTeam.Server.Services;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    checks++;
}
Item Item(string id, string tpl, string? parent = null, string? slot = null, object? location = null, int rounds = 0)
    => new() { Id = id, Template = tpl, ParentId = parent, SlotId = slot, Location = location, Rounds = rounds };
var items = new ItemHelper();
items.Templates["magLarge"] = new(new(null, 2, 2));
items.Templates["magSmall"] = new(new(null, 1, 2));
items.Templates["block"] = new(new(null, 1, 1));
items.Templates["weapon"] = new(new(null));
items.Templates["rig"] = new(new([
    new("small", new(1, 2)), new("large", new(2, 2))
]));
items.Templates["blockedRig"] = new(new([
    new("blocked", new(2, 2, [new(ExcludedFilter: ["magazine"])]))
]));
items.Templates["narrowRig"] = new(new([new("strip", new(1, 4))]));
var inventory = new InventoryHelper(items);
bool Place(List<Item> target, Item mag) => RestrictedMagazineRecovery.TryPlace(target, mag, items, inventory);
bool Restore(List<Item> saved, List<Item> target, out int count, out string? unplaced)
    => RestrictedMagazineRecovery.TryRestore(saved, target, i => items.IsOfBaseclass(i.Template, "magazine"),
        i => i with { }, Place, out count, out unplaced);
var equipment = Item("root", "equipment");
var weapon = Item("gun", "weapon", "root", "FirstPrimaryWeapon");
var rig = Item("rig", "rig", "root", "TacticalVest");
var big = Item("original", "magLarge", "gun", "mod_magazine");
var oldAmmo = Item("spent", "ammo", "original", "cartridges", rounds: 60);
List<Item> saved = [equipment, weapon, rig, big, oldAmmo];
List<Item> escaped = [equipment, weapon, rig];
Check(Restore(saved, escaped, out int restored, out _) && restored == 1, "Dropped original magazine recovered");
Check(escaped.Any(i => i.Id == "original") && !escaped.Any(i => i.Id == "spent"), "Recover shell without old ammunition");
Check(escaped.Single(i => i.Id == "original").ParentId == "gun", "Original empty attachment reused");
Check(!ReferenceEquals(big, escaped.Single(i => i.Id == "original")), "Saved baseline not mutated");
Check(Restore(saved, escaped, out restored, out _) && restored == 0, "Repeated recovery is idempotent");

var spare = Item("spare", "magSmall", "gun", "mod_magazine");
var liveAmmo = Item("remaining", "ammo", "spare", "cartridges", rounds: 7);
List<Item> reloaded = [equipment, weapon, rig, spare, liveAmmo,
    Item("smallBlock", "magSmall", "rig", "small", new Location(0, 0))];
Check(Restore(saved, reloaded, out restored, out _) && restored == 1, "Reloaded weapon keeps its new inserted magazine");
Check(spare.ParentId == "gun" && liveAmmo.Rounds == 7, "Current magazine and ammo stay untouched");
var recovered = reloaded.Single(i => i.Id == "original");
Check(recovered.ParentId == "rig" && recovered.SlotId == "large", "Four-cell magazine fits separate large rig grid");
Check(big.ParentId == "gun" && big.Location == null, "Recovery placement leaves saved original unchanged");

List<Item> present = [equipment, weapon, rig, big, Item("live", "ammo", "original", "cartridges", rounds: 3)];
Check(Restore(saved, present, out restored, out _) && restored == 0 && present.Last().Rounds == 3,
    "Surviving original magazine keeps actual remaining rounds");

List<Item> filtered = [equipment, weapon, rig];
Check(Restore(saved, filtered, out restored, out _) && !filtered.Any(i => i.Id == "lootMag"),
    "Recovery only uses original saved magazine identities");
List<Item> trackedOnlySaved = [equipment, weapon, rig];
List<Item> trackedOnlyFiltered = [equipment, weapon, rig];
Check(Restore(trackedOnlySaved, trackedOnlyFiltered, out restored, out _) && restored == 0,
    "Acquired loot magazine is not recreated");

List<Item> full = [equipment, weapon, rig, spare,
    Item("fullSmall", "magSmall", "rig", "small", new Location(0, 0)),
    Item("fullLarge", "magLarge", "rig", "large", new Location(0, 0))];
int before = full.Count;
Check(!Restore(saved, full, out _, out string? failed) && failed == "original", "Full inventory reports preservation failure");
Check(full.Count == before && saved.Contains(big) && saved.Contains(oldAmmo), "Failed recovery cannot mutate saved loadout");

List<Item> narrow = [equipment, weapon, Item("rig", "narrowRig", "root", "TacticalVest"), spare];
Check(!Restore(saved, narrow, out _, out _), "Four total cells in a 1x4 strip cannot hold 2x2 magazine");
List<Item> excluded = [equipment, weapon, Item("rig", "blockedRig", "root", "TacticalVest"), spare];
Check(!Restore(saved, excluded, out _, out _), "Grid exclusion filters honored");
List<Item> missingParent = [equipment, rig];
Check(Restore(saved, missingParent, out _, out _) && missingParent.Last().ParentId == "rig",
    "Missing original weapon uses valid carry grid without orphan attachment");

// Extraction and death use the same recovery policy; no corpse-time state is needed here.
List<Item> deathSnapshot = [equipment, weapon, rig, spare];
Check(Restore(saved, deathSnapshot, out restored, out _) && restored == 1, "Death snapshot restores original magazine too");

// Every insertion participates in subsequent occupancy checks.
var secondLost = Item("secondOriginal", "magLarge", "gun", "mod_magazine");
List<Item> severalSaved = [equipment, weapon, rig, big, secondLost];
List<Item> severalLive = [equipment, weapon, rig, spare];
Check(!Restore(severalSaved, severalLive, out restored, out failed)
    && restored == 1 && failed == "secondOriginal", "Multiple recovery placements cannot overlap");
Check(severalSaved.Count == 5, "Partial staged recovery still leaves saved baseline untouched");
Console.WriteLine($"PASS: {checks} Restricted magazine recovery checks.");

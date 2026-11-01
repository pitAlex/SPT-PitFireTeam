using System.Text.Json.Nodes;
using pitTeam.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Locales;

namespace pitTeam.Server.Services;

[Injectable(InjectionType.Singleton)]
public class FriendlyTeammateEditorService(FriendlyTeammateStorage storage, SaveServer saves, TemplateTable templates, ProfileHelper profiles, LocaleService locales)
{
    public IReadOnlyList<TeammateEditorChoice> Players() => saves.GetProfiles().Select(p => new TeammateEditorChoice(
        p.Key.ToString(), (p.Value.CharacterData?.PmcData?.Info?.Nickname ?? p.Key.ToString()) + " · " + p.Key)).ToList();

    public IReadOnlyList<TeammateEditorChoice> Modes() => pitTeam.Shared.GameplayModePolicy.Modes
        .Where(FriendlyGameplayModeAddons.IsAvailable).Select(m => new TeammateEditorChoice(m, m)).ToList();

    public IReadOnlyList<TeammateEditorChoice> Heads(string locale = "en") => CustomizationChoices(false, locale);

    public IReadOnlyList<TeammateEditorChoice> Voices(string locale = "en") => CustomizationChoices(true, locale);

    private IReadOnlyList<TeammateEditorChoice> CustomizationChoices(bool voices, string locale)
    {
        // Our client locale is 'chs'; the installed EFT global database uses 'ch'.
        var selected = locales.GetLocaleDb(locale == "chs" ? "ch" : locale);
        var english = locale == "en" ? selected : locales.GetLocaleDb("en");
        return templates.Customization.Values.Where(t => voices ? FriendlyTeammateIdentityPolicy.IsVoice(t) : t.Properties?.BodyPart == "Head")
            .Select(t => new TeammateEditorChoice(t.Id.ToString(), FriendlyTeammateIdentityPolicy.DisplayName(t.Id.ToString(), t.Name, selected, english)))
            .OrderBy(t => t.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
    }

    public Dictionary<string, TeammateEditorPocket> Pockets() => templates.Items.Values
        .Where(t => t.Parent.ToString() == "557596e64bdc2dc2118b4571" && t.Properties?.Grids?.Any() == true)
        .ToDictionary(t => t.Id.ToString(), t =>
        {
            var grids = t.Properties!.Grids!.ToDictionary(g => g.Name!, g => (g.Properties?.CellsH ?? 0, g.Properties?.CellsV ?? 0));
            string size = string.Join(" + ", grids.Values.Select(g => $"{g.Item1}×{g.Item2}"));
            return new TeammateEditorPocket(t.Id.ToString(), $"{t.Name} ({size})", grids,
                (t.Properties.Slots ?? []).Select(s => s.Name!).ToHashSet());
        });

    public int[] LevelExperience() => Enumerable.Range(1, profiles.GetMaxLevel()).Select(l => profiles.GetExperience(l) ?? 0).ToArray();

    public Task<List<TeammateEditorSummary>> Roster(string player, string mode) => Gated(() =>
    {
        var session = Validate(player, mode);
        return storage.ReadModeProfiles(session, mode).Select(p => JsonNode.Parse(p.Value)!)
            .Select(p => new TeammateEditorSummary(int.Parse(p["aid"]!.ToString()), p["Info"]?["Nickname"]?.GetValue<string>() ?? p["aid"]!.ToString(), CanEdit(mode, p)))
            .OrderBy(p => p.Name).ToList();
    });

    public Task<TeammateEditorDraft> Load(string player, string mode, int aid) => Gated(() =>
    {
        var session = Validate(player, mode);
        var (profile, equipment) = Read(session, mode, aid);
        return FriendlyTeammateEditorPolicy.Read(profile, equipment);
    });

    public Task<TeammateEditorDraft> Save(string player, string mode, int aid, TeammateEditorDraft edit) => Gated(() =>
    {
        var session = Validate(player, mode);
        var journal = storage.ReadModeDocument<JsonObject>(session, "pending-treatment.json", mode);
        if (journal?["State"]?.GetValue<string>() == "paying") throw new InvalidDataException("EditorBusy");
        var (profile, equipment) = Read(session, mode, aid);
        if (edit.Level != profiles.GetLevelFromExperience(edit.Experience)) throw new InvalidDataException("EditorLevelXp");
        if (string.IsNullOrWhiteSpace(edit.Nickname)) throw new InvalidDataException("EditorInvalidName");
        string nickname = FriendlyTeammateIdentityPolicy.NormalizeNickname(edit.Nickname);
        var otherNames = storage.ReadModeProfiles(session, mode).Where(p => p.Key != aid + ".json")
            .Select(p => JsonNode.Parse(p.Value)?["Info"]?["Nickname"]?.GetValue<string>());
        if (FriendlyTeammateIdentityPolicy.NicknameInUse(otherNames, nickname)) throw new InvalidDataException("EditorNameTaken");
        var changed = FriendlyTeammateEditorPolicy.Apply(profile, equipment, edit,
            templates.Customization.Values.Where(t => t.Properties?.BodyPart == "Head").Select(t => t.Id.ToString()).ToHashSet(), Pockets(),
            templates.Customization.Values.Where(FriendlyTeammateIdentityPolicy.IsVoice).Select(t => t.Id.ToString()).ToHashSet());
        var documents = new Dictionary<string, string> { [aid + ".json"] = changed.Profile };
        if (changed.Equipment != equipment && changed.Equipment != null) documents[aid + "-equipment.json"] = changed.Equipment;
        storage.WriteModeDocuments(session, mode, documents, backupPrevious: true);
        return FriendlyTeammateEditorPolicy.Read(changed.Profile, changed.Equipment);
    });

    private (string Profile, string? Equipment) Read(MongoId session, string mode, int aid)
    {
        if (aid <= 0) throw new InvalidDataException("EditorNotFound");
        string? profile = storage.ReadModeDocument<JsonObject>(session, aid + ".json", mode)?.ToJsonString();
        if (profile == null || JsonNode.Parse(profile)?["aid"]?.ToString() != aid.ToString()) throw new InvalidDataException("EditorNotFound");
        if (!CanEdit(mode, JsonNode.Parse(profile)!)) throw new InvalidDataException("EditorManagedCompanion");
        string? equipment = storage.ReadModeDocument<JsonArray>(session, aid + "-equipment.json", mode)?.ToJsonString();
        return (profile, equipment);
    }

    // Capabilities come from the registered addon, never a name check or an addon assembly reference.
    private static bool CanEdit(string mode, JsonNode profile) =>
        FriendlyGameplayModeAddons.PolicyForRole(mode, profile["Info"]?["Settings"]?["Role"]?.GetValue<string>())?.ProficiencyOnly != true;

    private MongoId Validate(string player, string mode)
    {
        if (!pitTeam.Server.Persistence.TeammateDatabase.IsProfileId(player) ||
            !pitTeam.Shared.GameplayModePolicy.IsKnown(mode) || !FriendlyGameplayModeAddons.IsAvailable(mode))
            throw new InvalidDataException("EditorNotFound");
        var session = new MongoId(player);
        if (!saves.ProfileExists(session)) throw new InvalidDataException("EditorNotFound");
        if (FriendlyModeRequestGate.HasActiveRaid) throw new InvalidDataException("EditorInRaid");
        return session;
    }

    private static async Task<T> Gated<T>(Func<T> action)
    {
        T result = default!;
        await FriendlyModeRequestGate.Run(() => { result = action(); return new ValueTask<string>(string.Empty); });
        return result;
    }
}

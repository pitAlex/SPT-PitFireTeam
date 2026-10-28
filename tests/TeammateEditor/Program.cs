using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using Path = System.IO.Path;

internal static class Program
{
    private static int checks;
    private const string Head = "111111111111111111111111", OtherHead = "222222222222222222222222";
    private const string Small = "557ffd194bdc2d28148b457f", Large = "5af99e9186f7747c447120b8";
    private const string Profile = """
    {"_id":"333333333333333333333333","aid":123,"Info":{"Nickname":"Fixture","Side":"Usec","Level":1,"Experience":0},
    "Customization":{"Head":"111111111111111111111111","Voice":"444444444444444444444444"},
    "Skills":{"Common":[{"Id":"Strength","Progress":100,"LastAccess":12}],"Mastering":[{"Id":"m4","Progress":50}],"Points":9},
    "Health":{"UpdateTime":100,"BodyParts":{"Head":{"Health":{"Current":20,"Maximum":35,"Minimum":0},"Effects":{"Pain":{"Time":10}}}}},
    "Inventory":{"equipment":"555555555555555555555555","items":[
    {"_id":"666666666666666666666666","_tpl":"557ffd194bdc2d28148b457f","parentId":"555555555555555555555555","slotId":"Pockets"},
    {"_id":"777777777777777777777777","_tpl":"888888888888888888888888","parentId":"666666666666666666666666","slotId":"pocket1","location":{"x":0,"y":0,"r":0}},
    {"_id":"999999999999999999999999","_tpl":"aaaaaaaaaaaaaaaaaaaaaaaa","parentId":"555555555555555555555555","slotId":"FirstPrimaryWeapon","upd":{"Repairable":{"Durability":40}}}]},
    "Stats":{"Eft":{"Kills":22}},"InsuredItems":[{"test":true}],"unknownFutureField":{"retained":true}}
    """;

    public static int Main(string[] args)
    {
        string runtime = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(runtime, name.Name + ".dll"))
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime, name.Name + ".dll")) : null;
        return Run(runtime).GetAwaiter().GetResult();
    }
    private static void Check(bool value, string description) { if (!value) throw new Exception(description); checks++; }
    private static async Task Refuses(Func<Task> action, string key)
    {
        try { await action(); } catch (InvalidDataException ex) { Check(ex.Message == key, $"Expected {key}, got {ex.Message}"); return; }
        throw new Exception("Expected refusal: " + key);
    }
    private static T Construct<T>(Dictionary<string, object?> dependencies) => (T)typeof(T).GetConstructors().Single()
        .Invoke(typeof(T).GetConstructors().Single().GetParameters().Select(p => dependencies.GetValueOrDefault(p.Name!)).ToArray());

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<int> Run(string runtime)
    {
        var pockets = new Dictionary<string, TeammateEditorPocket>
        {
            [Small] = new(Small, "Small", new() { ["pocket1"] = (1, 1) }, []),
            [Large] = new(Large, "Large", new() { ["pocket1"] = (1, 2) }, [])
        };
        var heads = new HashSet<string> { Head, OtherHead };
        string equipment = JsonNode.Parse(Profile)!["Inventory"]!["items"]!.ToJsonString();
        TeammateEditorDraft Draft() => FriendlyTeammateEditorPolicy.Read(Profile, equipment);
        var edit = Draft(); edit.HeadId = OtherHead; edit.Level = 2; edit.Experience = 1000;
        edit.Skills[0].Progress = 5100; edit.Skills[1].Progress = 900;
        edit.BodyParts[0].Current = 50; edit.BodyParts[0].Maximum = 60;
        var result = FriendlyTeammateEditorPolicy.Apply(Profile, equipment, edit, heads, pockets);
        var original = JsonNode.Parse(Profile)!; var changed = JsonNode.Parse(result.Profile)!;
        foreach (string key in new[] { "Inventory", "_id", "aid", "Stats", "InsuredItems", "unknownFutureField" })
            Check(JsonNode.DeepEquals(original[key], changed[key]), "Protected property retained: " + key);
        Check(changed["Customization"]!["Head"]!.GetValue<string>() == OtherHead, "Head changed");
        Check(JsonNode.DeepEquals(original["Customization"]!["Voice"], changed["Customization"]!["Voice"]), "Voice retained");
        Check(changed["Skills"]!["Common"]![0]!["Progress"]!.GetValue<int>() == 5100, "Skill changed");
        Check(changed["Skills"]!["Common"]![0]!["LastAccess"]!.GetValue<int>() == 12, "Skill metadata retained");
        Check(JsonNode.DeepEquals(original["Health"]!["BodyParts"]!["Head"]!["Effects"], changed["Health"]!["BodyParts"]!["Head"]!["Effects"]), "Effects retained");
        Check(result.Equipment == equipment, "Ordinary edits leave Default kit bytes alone");
        Check(changed["Health"]!["BodyParts"]!["Head"]!["Health"]!["Maximum"]!.GetValue<int>() == 60, "Health changed");
        async Task Bad(Action<TeammateEditorDraft> mutate, string key = "EditorInvalid")
        {
            var draft = Draft(); mutate(draft);
            await Refuses(() => { FriendlyTeammateEditorPolicy.Apply(Profile, equipment, draft, heads, pockets); return Task.CompletedTask; }, key);
        }
        await Bad(d => d.Revision = "old", "EditorStale");
        await Bad(d => d.Level = 0); await Bad(d => d.Experience = -1);
        await Bad(d => d.HeadId = "gear-template", "EditorInvalidHead");
        await Bad(d => d.Nickname = "  ", "EditorInvalidName");
        await Bad(d => d.VoiceId = Head, "EditorInvalidVoice");
        var identity = Draft(); identity.Nickname = "  Renamed  "; identity.VoiceId = OtherHead;
        var identityResult = FriendlyTeammateEditorPolicy.Apply(Profile, equipment, identity, heads, pockets, new HashSet<string> { OtherHead });
        var identityJson = JsonNode.Parse(identityResult.Profile)!;
        Check(identityJson["Info"]!["Nickname"]!.GetValue<string>() == "Renamed" && identityJson["Info"]!["LowerNickname"]!.GetValue<string>() == "renamed", "Name trimmed and lowercase synchronized");
        Check(identityJson["Customization"]!["Voice"]!.GetValue<string>() == OtherHead, "Valid voice changed");
        Check(JsonNode.DeepEquals(original["Inventory"], identityJson["Inventory"]), "Identity edits preserve equipment");
        foreach (double bad in new[] { -1d, 5101, double.NaN, double.PositiveInfinity }) await Bad(d => d.Skills[0].Progress = bad);
        await Bad(d => d.Skills[0].Id = "invented"); await Bad(d => d.Skills.RemoveAt(0));
        await Bad(d => d.Skills.Add(d.Skills[0])); await Bad(d => d.Skills[0].Group = "Stats");
        await Bad(d => d.BodyParts[0].Current = 99); await Bad(d => d.BodyParts[0].Maximum = double.NaN);
        await Bad(d => d.BodyParts[0].Maximum = 10001); await Bad(d => d.BodyParts[0].Name = "invented");
        await Bad(d => d.PocketId = "weapon", "EditorInvalidPockets");
        edit = Draft(); edit.PocketId = Large;
        result = FriendlyTeammateEditorPolicy.Apply(Profile, equipment, edit, heads, pockets);
        var expanded = JsonNode.Parse(result.Profile)!;
        Check(expanded["Inventory"]!["items"]![0]!["_tpl"]!.GetValue<string>() == Large, "Pocket shell expanded");
        Check(JsonNode.Parse(result.Equipment!)![0]!["_tpl"]!.GetValue<string>() == Large, "Default pocket shell expanded");
        foreach (int index in new[] { 1, 2 }) Check(JsonNode.DeepEquals(original["Inventory"]!["items"]![index], expanded["Inventory"]!["items"]![index]), "Pocket contents/weapon unchanged");
        var shrink = FriendlyTeammateEditorPolicy.Read(result.Profile, result.Equipment); shrink.PocketId = Small;
        await Refuses(() => { FriendlyTeammateEditorPolicy.Apply(result.Profile, result.Equipment, shrink, heads, pockets); return Task.CompletedTask; }, "EditorEmptyPockets");

        string work = Path.GetFullPath(Path.Combine("tests", "artifacts", "teammate-editor", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(work); string previous = Environment.CurrentDirectory; Environment.CurrentDirectory = work;
        try
        {
            var json = new JsonUtil([new SptJsonConverterRegistrator()]);
            var settings = new FriendlyServerSettingsService(DispatchProxy.Create<ISptLogger<FriendlyServerSettingsService>, TestLogger>(), null!, null!);
            var storage = new FriendlyTeammateStorage(new FileUtil(), json, DispatchProxy.Create<ISptLogger<FriendlyTeammateStorage>, TestLogger>(), settings);
            var session = new MongoId("bbbbbbbbbbbbbbbbbbbbbbbb");
            storage.WriteModeDocuments(session, "GunsForHire", new Dictionary<string, string> { ["123.json"] = Profile, ["123-equipment.json"] = equipment, ["123-settings.json"] = "{\"Aggression\":70}" });
            storage.WriteModeDocuments(session, "Allegiance", new Dictionary<string, string> { ["123.json"] = Profile });
            var save = (SaveServer)RuntimeHelpers.GetUninitializedObject(typeof(SaveServer));
            typeof(SaveServer).GetField("profiles", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(save,
                new ConcurrentDictionary<MongoId, SptProfile>([new(session, new())]));
            var templates = (TemplateTable)RuntimeHelpers.GetUninitializedObject(typeof(TemplateTable));
            typeof(TemplateTable).GetProperty(nameof(TemplateTable.Customization))!.SetValue(templates,
                new Dictionary<MongoId, CustomizationItem> { [new(Head)] = new() { Id = new(Head), Name = "Head", Properties = new() { BodyPart = "Head" } }, [new(OtherHead)] = new() { Id = new(OtherHead), Name = "Other head", Properties = new() { BodyPart = "Head" } } });
            var globals = json.Deserialize<GlobalTable>(File.ReadAllText(Path.Combine(runtime, "SPT_Data/database/globals.json")))!;
            var profileHelper = Construct<ProfileHelper>(new() { ["globalTable"] = globals });
            const string voiceId = "444444444444444444444444";
            templates.Customization[new(voiceId)] = new() { Id = new(voiceId), Name = "Usec_4", Parent = "5fc100cf95572123ae738483" };
            var english = new GlobalLocaleDictionary { [Head + " Name"] = "Mullen", [voiceId + " Name"] = "Brent" };
            var russian = new GlobalLocaleDictionary { [Head + " Name"] = "Маллен", [voiceId + " Name"] = "Брент" };
            var chinese = new GlobalLocaleDictionary { [Head + " Name"] = "马伦" };
            var localeTable = new LocaleTable { Global = new()
            {
                ["en"] = new(() => english), ["ru"] = new(() => russian), ["ch"] = new(() => chinese)
            }, Menu = new(), Languages = new() };
            var locales = new LocaleService(DispatchProxy.Create<ISptLogger<LocaleService>, TestLogger>(), localeTable, null!);
            var service = new FriendlyTeammateEditorService(storage, save, templates, profileHelper, locales);
            Check(service.Heads().Single(h => h.Id == Head).Label == "Mullen", "English native head localization");
            Check(service.Heads("ru").Single(h => h.Id == Head).Label == "Маллен", "Russian native head localization");
            Check(service.Voices().Single().Label == "Brent", "English native voice localization");
            Check(service.Voices("ru").Single().Label == "Брент", "Russian native voice localization");
            Check(service.Heads("chs").Single(h => h.Id == Head).Label == "马伦", "Client Chinese locale maps to native ch database");
            Check(service.Voices("chs").Single().Label == "Brent", "Missing selected-language entry falls back to English");
            Check(service.Heads("ru").Single(h => h.Id == OtherHead).Label == "Other head", "Untranslated custom entry retains template name");
            Check(service.Heads("missing").Single(h => h.Id == Head).Label == "Mullen", "Unavailable native language falls back to English");
            Check(service.Heads("ru").Select(h => h.Id).ToHashSet().SetEquals(service.Heads().Select(h => h.Id)), "Labels never change selection IDs");
            Check(FriendlyTeammateIdentityPolicy.DisplayName(Head, "raw", new Dictionary<string, string> { [Head + " Name"] = " " }, english) == "Mullen", "Whitespace translation falls back to English");
            Check(FriendlyTeammateIdentityPolicy.DisplayName(Head, "raw", new Dictionary<string, string> { [Head + " Name"] = Head + " Name" }, english) == "Mullen", "Echoed key falls back to English");
            Check(FriendlyTeammateIdentityPolicy.DisplayName(OtherHead, "", new Dictionary<string, string>(), english) == OtherHead, "Missing label and raw name fall back to ID");
            var loaded = await service.Load(session.ToString(), "GunsForHire", 123);
            Check((await service.Roster(session.ToString(), "GunsForHire")).Single().Aid == 123, "Roster comes from selected database");
            Check(service.Players().Single().Id == session.ToString(), "Only loaded player profiles listed");
            Check(service.Heads().Count == 2, "Head options come from head templates");
            typeof(TemplateTable).GetProperty(nameof(TemplateTable.Items))!.SetValue(templates, new Dictionary<MongoId, TemplateItem>());
            loaded.HeadId = OtherHead;
            await service.Save(session.ToString(), "GunsForHire", 123, loaded);
            Check(storage.ReadModeDocument<JsonObject>(session, "123.json", "GunsForHire")!["Customization"]!["Head"]!.GetValue<string>() == OtherHead, "Save uses encrypted storage");
            Check(storage.ReadModeDocument<JsonObject>(session, "123.json", "Allegiance")!["Customization"]!["Head"]!.GetValue<string>() == Head, "Other mode untouched");
            Check(storage.ReadModeDocument<JsonObject>(session, "123-settings.json", "GunsForHire")!["Aggression"]!.GetValue<int>() == 70, "Settings unchanged");
            await Refuses(() => service.Save(session.ToString(), "GunsForHire", 123, loaded), "EditorStale");
            storage.WriteModeDocuments(session, "GunsForHire", new Dictionary<string, string> { ["124.json"] = Profile.Replace("\"aid\":123", "\"aid\":124") });
            var duplicateName = await service.Load(session.ToString(), "GunsForHire", 123);
            duplicateName.Nickname = " fixture ";
            await Refuses(() => service.Save(session.ToString(), "GunsForHire", 123, duplicateName), "EditorNameTaken");
            await Refuses(() => service.Load(session.ToString(), "../escape", 123), "EditorNotFound");
            await Refuses(() => service.Load("../../", "GunsForHire", 123), "EditorNotFound");
            await Refuses(() => service.Load("cccccccccccccccccccccccc", "GunsForHire", 123), "EditorNotFound");
            await Refuses(() => service.Load(session.ToString(), "GunsForHire", 125), "EditorNotFound");
            await FriendlyModeRequestGate.Run(() => { FriendlyModeRequestGate.StartRaid(session.ToString()); return new ValueTask<string>(""); });
            try
            {
                await Refuses(() => service.Load(session.ToString(), "GunsForHire", 123), "EditorInRaid");
                await Refuses(() => service.Save(session.ToString(), "GunsForHire", 123, loaded), "EditorInRaid");
            }
            finally { await FriendlyModeRequestGate.Run(() => { FriendlyModeRequestGate.EndRaid(session.ToString()); return new ValueTask<string>(""); }); }

            Check(!service.Modes().Any(m => m.Id == "GoingRogue"), "Absent addon mode is not offered");
            await Refuses(() => service.Roster(session.ToString(), "GoingRogue"), "EditorNotFound");
            await Refuses(() => service.Load(session.ToString(), "GoingRogue", 123), "EditorNotFound");
            await Refuses(() => service.Save(session.ToString(), "GoingRogue", 123, loaded), "EditorNotFound");
            Check(!Directory.EnumerateDirectories(work, "teammates-going-rogue", SearchOption.AllDirectories).Any(), "Absent addon requests never initialize its database directory");
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "pitFireTeam.GoingRogue.Server"), "Editor has not loaded the optional addon assembly");
            FriendlyGameplayModeAddons.Register("GoingRogue", [
                new("bossKnight", "Knight", "", ProficiencyOnly: true),
                new("followerBigPipe", "Big Pipe", "", ProficiencyOnly: true),
                new("followerBirdEye", "Birdeye", "", ProficiencyOnly: true),
                new("exUsec", "", "")
            ]);
            Check(service.Modes().Any(m => m.Id == "GoingRogue"), "Registered addon mode is offered");
            var rogueDocuments = new Dictionary<string, string>();
            int bossAid = 200;
            foreach (string role in new[] { "bossKnight", "followerBigPipe", "FOLLOWERBIRDEYE", "exUsec" })
            {
                var boss = JsonNode.Parse(Profile)!;
                boss["aid"] = bossAid;
                boss["Info"]!["Nickname"] = "Renamed companion " + bossAid;
                boss["Info"]!["Settings"] = new JsonObject { ["Role"] = role };
                rogueDocuments[bossAid++ + ".json"] = boss.ToJsonString();
            }
            storage.WriteModeDocuments(session, "GoingRogue", rogueDocuments);
            var rogueRoster = await service.Roster(session.ToString(), "GoingRogue");
            foreach (int protectedAid in new[] { 200, 201, 202 })
            {
                Check(!rogueRoster.Single(t => t.Aid == protectedAid).Editable, "Renamed managed companion remains disabled: " + protectedAid);
                await Refuses(() => service.Load(session.ToString(), "GoingRogue", protectedAid), "EditorManagedCompanion");
                var bypass = FriendlyTeammateEditorPolicy.Read(rogueDocuments[protectedAid + ".json"], null);
                bypass.Nickname = "Attempted bypass";
                await Refuses(() => service.Save(session.ToString(), "GoingRogue", protectedAid, bypass), "EditorManagedCompanion");
                Check(JsonNode.DeepEquals(JsonNode.Parse(rogueDocuments[protectedAid + ".json"]),
                    storage.ReadModeDocument<JsonObject>(session, protectedAid + ".json", "GoingRogue")), "Protected companion unchanged: " + protectedAid);
            }
            Check(rogueRoster.Single(t => t.Aid == 203).Editable, "Ordinary addon recruit remains editable");
            var ordinaryRogue = await service.Load(session.ToString(), "GoingRogue", 203);
            ordinaryRogue.Nickname = "Ordinary Rogue";
            await service.Save(session.ToString(), "GoingRogue", 203, ordinaryRogue);
            Check((await service.Load(session.ToString(), "GoingRogue", 203)).Nickname == "Ordinary Rogue", "Ordinary addon recruit saves normally");
            Check((await service.Roster(session.ToString(), "GunsForHire")).All(t => t.Editable), "Base rosters retain editor access");
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "pitFireTeam.GoingRogue.Server"), "Capability checks do not load the optional addon assembly");
        }
        finally { Environment.CurrentDirectory = previous; }
        Console.WriteLine($"PASS: {checks} teammate editor checks. Synthetic fixtures: {work}");
        return 0;
    }
}

public class TestLogger : DispatchProxy { protected override object? Invoke(MethodInfo? method, object?[]? args) => null; }

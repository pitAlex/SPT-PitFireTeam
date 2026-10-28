using System.Reflection;
using System.Runtime.CompilerServices;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;

internal static class TeammateVoiceTests
{
    internal static void Run(FriendlyTeammateStorage storage, FriendlyServerSettingsService settings,
        JsonUtil json, Action<bool, string> check)
    {
        var usec = new MongoId("6284d6a28e4092597733b7a6");
        var bear = new MongoId("666666666666666666666666");
        var scav = new MongoId("999999999999999999999999");
        var unrestricted = new MongoId("aaaaaaaaaaaaaaaaaaaaaaaa");
        var head = new MongoId("777777777777777777777777");
        var originalVoice = new MongoId("888888888888888888888888");
        var table = (TemplateTable)RuntimeHelpers.GetUninitializedObject(typeof(TemplateTable));
        typeof(TemplateTable).GetProperty(nameof(TemplateTable.Customization))!.SetValue(table,
            new Dictionary<MongoId, CustomizationItem>
            {
                [usec] = new() { Id = usec, Parent = "5fc100cf95572123ae738483", Properties = new() { Side = ["Usec"] } },
                [bear] = new() { Id = bear, Parent = "5fc100cf95572123ae738483", Properties = new() { Side = ["Bear"] } },
                [scav] = new() { Id = scav, Parent = "5fc100cf95572123ae738483", Properties = new() { Side = ["Savage"] } },
                [unrestricted] = new() { Id = unrestricted, Parent = "5fc100cf95572123ae738483", Properties = new() { Side = [] } },
                [head] = new() { Id = head, Parent = "5cc085e214c02e000c6bea67", Properties = new() { Side = ["Usec"] } },
            });
        var constructor = typeof(FriendlyTeammateService).GetConstructors().Single();
        var service = (FriendlyTeammateService)constructor.Invoke(constructor.GetParameters().Select(parameter => parameter.Name switch
        {
            "storage" => (object)storage, "settingsService" => settings, "jsonUtil" => json, "templateTable" => table,
            "logger" => DispatchProxy.Create<ISptLogger<FriendlyTeammateService>, TestLogger>(), _ => null
        }).ToArray());
        var session = new MongoId("bbbbbbbbbbbbbbbbbbbbbbbb");
        foreach (string mode in new[] { "GunsForHire", "Allegiance" })
        {
            settings.SaveAndApply(new() { GameplayMode = mode });
            storage.InitializeProfile(session);
            foreach (string side in new[] { "Usec", "Bear" })
            {
                var teammate = new BotBase { Aid = 123, Id = new MongoId(), Info = new() { Side = side, Nickname = "Voice test" },
                    Customization = new() { Voice = originalVoice, Head = head }, Inventory = new() { Items = [] } };
                var other = new BotBase { Aid = 124, Id = new MongoId(), Info = new() { Side = "Usec", Nickname = "Unchanged" },
                    Customization = new() { Voice = originalVoice }, Inventory = new() { Items = [] } };
                storage.Write(session, "123.json", teammate);
                storage.Write(session, "124.json", other);
                string untouched = json.Serialize(storage.Read<BotBase>(session, "124.json"))!;
                foreach (var selectedVoice in new[] { usec, bear, scav, unrestricted })
                {
                    service.SetTeammateVoice(session, new() { Aid = "123", Voice = selectedVoice.ToString() });
                    var selectedProfile = storage.Read<BotBase>(session, "123.json")!;
                    check(selectedProfile.Customization!.Voice == selectedVoice && selectedProfile.Customization.Head == head
                        && selectedProfile.Info!.Nickname == teammate.Info.Nickname,
                        mode + " " + side + " accepts voices from every faction and factionless templates while preserving identity");
                }
                var saved = storage.Read<BotBase>(session, "123.json")!;
                check(json.Serialize(storage.Read<BotBase>(session, "124.json")) == untouched,
                    "voice change leaves other teammates untouched");
                string beforeRejection = json.Serialize(saved)!;
                foreach (string? rejected in new[] { "dddddddddddddddddddddddd", head.ToString(), "Usec_4", "", null })
                {
                    bool refused = false;
                    try { service.SetTeammateVoice(session, new() { Aid = "123", Voice = rejected }); }
                    catch (FriendlyTeammateException) { refused = true; }
                    check(refused && json.Serialize(storage.Read<BotBase>(session, "123.json")) == beforeRejection,
                        "unknown, invalid and non-voice selections preserve the saved voice");
                }
                bool unknownAidRefused = false;
                try { service.SetTeammateVoice(session, new() { Aid = "999", Voice = usec.ToString() }); }
                catch (FriendlyTeammateException) { unknownAidRefused = true; }
                check(unknownAidRefused, "voice route requires an owned teammate");
            }
            storage.DeleteTeammate(session, 123);
            storage.DeleteTeammate(session, 124);
        }
        settings.SaveAndApply(new());
    }
}

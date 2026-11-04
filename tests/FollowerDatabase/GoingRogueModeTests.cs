using System.Reflection;
using System.Runtime.CompilerServices;
using pitTeam.GoingRogue.Server;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Constants;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Services.Items;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using Path = System.IO.Path;

internal static class GoingRogueModeTests
{
    internal static void Run(FriendlyTeammateStorage storage, FriendlyServerSettingsService settings,
        JsonUtil json, Action<bool, string> check)
    {
        check(!FriendlyGameplayModeAddons.HasExtension("Allegiance"), "Going Rogue is unavailable without the server addon");
        string database = Path.Combine(Path.GetDirectoryName(typeof(GlobalTable).Assembly.Location)!, "SPT_Data", "database");
        var globals = json.Deserialize<GlobalTable>(File.ReadAllText(Path.Combine(database, "globals.json")))!;
        var bots = new BotTable { Base = new(), Core = null!, Types = new() };
        foreach (string role in new[] { "bossknight", "followerbigpipe", "followerbirdeye" })
            bots.Types[role] = json.Deserialize<BotType>(File.ReadAllText(Path.Combine(database, "bots", "types", role + ".json")));
        string nativeKnight = json.Serialize(bots.Types["bossknight"])!;
        var templates = (TemplateTable)RuntimeHelpers.GetUninitializedObject(typeof(TemplateTable));
        typeof(TemplateTable).GetProperty(nameof(TemplateTable.Items))!.SetValue(templates,
            json.Deserialize<Dictionary<MongoId, TemplateItem>>(File.ReadAllText(Path.Combine(database, "templates", "items.json")))!);
        typeof(TemplateTable).GetProperty(nameof(TemplateTable.Quests))!.SetValue(templates,
            json.Deserialize<Dictionary<MongoId, Quest>>(File.ReadAllText(Path.Combine(database, "templates", "quests.json")))!);
        var baseClasses = (ItemBaseClassService)typeof(ItemBaseClassService).GetConstructors().Single().Invoke(
            typeof(ItemBaseClassService).GetConstructors().Single().GetParameters().Select(parameter =>
                parameter.Name == "templateTable" ? (object)templates : null).ToArray());
        baseClasses.HydrateItemBaseClassCache();
        var itemHelper = (ItemHelper)typeof(ItemHelper).GetConstructors().Single().Invoke(
            typeof(ItemHelper).GetConstructors().Single().GetParameters().Select(parameter => parameter.Name switch
            {
                "templateTable" => (object)templates, "itemBaseClassService" => baseClasses,
                "randomUtil" => new RandomUtil(DispatchProxy.Create<ISptLogger<RandomUtil>, TestLogger>(), new FastCloner()), _ => null
            }).ToArray());
        var inventoryHelper = (SPTarkov.Server.Core.Helpers.Profile.InventoryHelper)typeof(SPTarkov.Server.Core.Helpers.Profile.InventoryHelper)
            .GetConstructors().Single().Invoke(typeof(SPTarkov.Server.Core.Helpers.Profile.InventoryHelper).GetConstructors().Single()
                .GetParameters().Select(parameter => parameter.Name switch
                {
                    "itemHelper" => (object)itemHelper,
                    "logger" => DispatchProxy.Create<ISptLogger<SPTarkov.Server.Core.Helpers.Profile.InventoryHelper>, TestLogger>(),
                    "cloner" => new FastCloner(), _ => null
                }).ToArray());
        new GoingRogueServerPlugin(globals, bots, itemHelper, inventoryHelper,
            json.Deserialize<SPTarkov.Server.Core.Models.Spt.Config.BotConfig>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(database)!, "configs", "bot.json")))!,
            (SPTarkov.Server.Core.Generators.Bot.BotInventoryGenerator)RuntimeHelpers.GetUninitializedObject(typeof(SPTarkov.Server.Core.Generators.Bot.BotInventoryGenerator)),
            (SPTarkov.Server.Core.Helpers.Bot.BotHelper)RuntimeHelpers.GetUninitializedObject(typeof(SPTarkov.Server.Core.Helpers.Bot.BotHelper)), templates).OnLoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        var bosses = new Dictionary<string, BotBase>();
        foreach (var companion in FriendlyGameplayModeAddons.Companions("Allegiance").Where(companion => companion.ProficiencyOnly))
        {
            var boss = new BotBase { Info = new() { Level = 14, Experience = 1400, Settings = new() { Role = companion.Role } },
                Skills = new() { Common = [new() { Id = SkillTypes.Assault, Progress = 17.25 }, new() { Id = SkillTypes.RecoilControl, Progress = 31.5 }], Mastering = [], Points = 2 } };
            string generatedSkills = json.Serialize(boss.Skills)!;
            check(companion.PrepareProfile!(boss), "low-level boss profile is repaired: " + companion.Role);
            check(boss.Info.Level == Math.Min(60, globals.Configuration.Exp.Level.ExperienceTable.Length) &&
                boss.Info.Experience == globals.Configuration.Exp.Level.ExperienceTable.Take(boss.Info.Level.Value).Sum(entry => entry.Experience),
                "boss level and XP consistently use legacy level 60: " + companion.Role);
            check(json.Serialize(boss.Skills) == generatedSkills, "profile preparation preserves generated skill values exactly: " + companion.Role);
            string skillSnapshot = json.Serialize(boss.Skills)!;
            boss.Info.Level = 1; boss.Info.Experience = 0;
            companion.PrepareProfile(boss);
            check(json.Serialize(boss.Skills) == skillSnapshot && !companion.PrepareProfile(boss),
                "boss skills ignore character level and preparation is idempotent: " + companion.Role);
            bosses[companion.Role] = boss;
        }
        check(bosses.Values.All(boss => boss.Skills!.Common.Single(skill => skill.Id == SkillTypes.Assault).Progress == 17.25),
            "all Goons retain actual generated assault progress instead of invented role floors");
        var noSkills = new BotBase { Info = new() { Settings = new() { Role = "bossKnight" } } };
        FriendlyGameplayModeAddons.Policy("Allegiance", noSkills)!.PrepareProfile!(noSkills);
        check(noSkills.Skills == null, "level/profile preparation never fabricates missing skills");
        check(json.Serialize(bots.Types["bossknight"]) == nativeKnight, "boss preparation never mutates the shared native template");
        var session = new MongoId("c0c0c0c0c0c0c0c0c0c0c0c0");
        foreach (string mode in new[] { "GunsForHire", "Allegiance" })
        {
            settings.SaveAndApply(new() { GameplayMode = mode });
            storage.InitializeProfile(session);
            storage.Write(session, "123.json", new BotBase { Id = new MongoId(), Aid = 123,
                Info = new() { Nickname = mode, Side = "Usec", Settings = new() { Role = "bossKnight" } },
                Inventory = new() { Items = [] }, Customization = new() });
        }
        foreach (string mode in new[] { "GunsForHire", "Allegiance" })
        {
            settings.SaveAndApply(new() { GameplayMode = mode });
            check(storage.ReadProfiles(session).Single().Info?.Nickname == mode, "same account ID remains isolated in " + mode);
        }
        check(settings.LoadSettings().IsAllegiance, "Going Rogue inherits Allegiance rules");
        // Keep this single-Knight persistence fixture isolated from native creation;
        // the existing-profile seed/receipt path for Big Pipe is checked below.
        storage.Write(session, "addon-companion-followerbigpipe.json", new { initialized = true });
        storage.Write(session, "addon-companion-followerbirdeye.json", new { initialized = true });
        var insurance = (FriendlyTeammateInsuranceService)typeof(FriendlyTeammateInsuranceService).GetConstructors().Single().Invoke(
            typeof(FriendlyTeammateInsuranceService).GetConstructors().Single().GetParameters().Select(parameter =>
                parameter.Name == "settingsService" ? (object)settings : null).ToArray());
        var nativeProfiles = new System.Collections.Concurrent.ConcurrentDictionary<MongoId, SptProfile>();
        foreach (string id in new[]{"c0c0c0c0c0c0c0c0c0c0c0c0","c1c1c1c1c1c1c1c1c1c1c1c1","c2c2c2c2c2c2c2c2c2c2c2c2","c3c3c3c3c3c3c3c3c3c3c3c3"})
            nativeProfiles[new(id)] = new() { CharacterData = new() { PmcData = new() { Info = new() { Level=20, Side="Usec" },
                Quests=[], TradersInfo = new() { [new("67768b19fa281ca31708b187")]=new(){Standing=1} } } } };
        var nativeSave=(SPTarkov.Server.Core.Servers.SaveServer)RuntimeHelpers.GetUninitializedObject(typeof(SPTarkov.Server.Core.Servers.SaveServer));
        typeof(SPTarkov.Server.Core.Servers.SaveServer).GetField("profiles",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(nativeSave,nativeProfiles);
        var profileHelper=(SPTarkov.Server.Core.Helpers.Profile.ProfileHelper)typeof(SPTarkov.Server.Core.Helpers.Profile.ProfileHelper).GetConstructors().Single().Invoke(
            typeof(SPTarkov.Server.Core.Helpers.Profile.ProfileHelper).GetConstructors().Single().GetParameters().Select(p=>p.Name=="saveServer"?(object)nativeSave:null).ToArray());
        var ammoTemplate = new MongoId("54527a984bdc2d4e668b4567"); // Native 5.56 M855.
        var language = new FriendlyLanguageService(DispatchProxy.Create<ISptLogger<FriendlyLanguageService>, TestLogger>());
        language.GetLanguageJson(session, "en", File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../server/Resources/lang/en.json"))));
        var service = (FriendlyTeammateService)typeof(FriendlyTeammateService).GetConstructors().Single().Invoke(
            typeof(FriendlyTeammateService).GetConstructors().Single().GetParameters().Select(parameter => parameter.Name switch
            {
                "storage" => (object)storage, "settingsService" => settings, "jsonUtil" => json,
                "profileHelper" => profileHelper,
                "languageService" => language,
                "teammateInsuranceService" => insurance,
                "itemHelper" => itemHelper,
                "cloner" => new FastCloner(),
                "logger" => DispatchProxy.Create<ISptLogger<FriendlyTeammateService>, TestLogger>(), _ => null
            }).ToArray());
        var knight = storage.ReadProfiles(session).Single();
        var policy = FriendlyGameplayModeAddons.Policy("Allegiance", knight)!;
        storage.Write(session, "123-settings.json", new FriendlyTeammateSettings
        {
            SelectedLoadoutId = "000000000000000000000000", Aggression = 73,
            Proficiency = new() { Accuracy = 140, VisionDistance = 125 }
        });
        check(policy.ProficiencyOnly && policy.RaidReady && policy.PortraitColor == "#667D91", "Knight has the addon profile and raid-readiness policy");
        check(policy.GenerateEquipmentEachRaid, "Knight uses native equipment generation for each raid");
        check(FriendlyGameplayModeAddons.Companions("Allegiance").All(companion => !companion.SeedForTesting), "test Goons are no longer added automatically to any roster");
        check(FriendlyGameplayModeAddons.Companions("Allegiance").Where(companion => companion.ProficiencyOnly).All(companion => companion.CanSpawnForQuest != null), "all Goons register quest deployment admission");
        check(!service.TryGetQuestCompanionProfile(session, "bossKnight", null, 1, null, out _) &&
            !service.TryGetQuestCompanionProfile(session, "bossKnight", "123", 0, null, out _), "selected-member requests cannot use temporary quest generation");
        check(FriendlyGameplayModeAddons.Companions("Allegiance").Where(companion => companion.ProficiencyOnly)
            .All(companion => companion.PrepareRaidEquipment != null), "all three Goons register raid-only spare provisioning");
        check(FriendlyGameplayModeAddons.Companions("Allegiance").Single(companion => companion.Role == "exUsec")
            .PrepareRaidEquipment == null, "exUsec has no Goon spare-magazine policy");
        check(policy.ProtectSpawnInventory, "Knight protects native inventory independently of loadout mode");
        var equipmentReady = typeof(FriendlyTeammateService).GetMethod("HasRaidEquipmentOrGenerator", BindingFlags.Instance | BindingFlags.NonPublic)!;
        check((bool)equipmentReady.Invoke(service, [knight])!, "Knight can be invited with no permanent equipment");
        var merge = typeof(FriendlyTeammateService).GetMethod("MergeGeneratedCompanionEquipment", BindingFlags.Instance | BindingFlags.NonPublic)!;
        string beforeSpawn = json.Serialize(knight)!;
        BotBase NativeKit()
        {
            var root = new MongoId(); var weapon = new MongoId(); var magazine = new MongoId();
            return new()
            {
                Info = new() { Settings = new() { Role = "bossKnight" } },
                Inventory = new() { Equipment = root, Items = [
                    new() { Id = root, Template = new MongoId("55d7217a4bdc2d86028b456d") },
                    new() { Id = weapon, Template = new MongoId("5447a9cd4bdc2dbd208b4567"), ParentId = root.ToString(), SlotId = "FirstPrimaryWeapon" },
                    new() { Id = new MongoId(), Template = ammoTemplate, ParentId = weapon.ToString(), SlotId = "patron_in_weapon", Upd = new() { StackObjectsCount = 1 } },
                    new() { Id = magazine, Template = new MongoId("55d4887d4bdc2d962f8b4570"), ParentId = weapon.ToString(), SlotId = "mod_magazine" },
                    new() { Id = new MongoId(), Template = ammoTemplate, ParentId = magazine.ToString(), SlotId = "cartridges", Upd = new() { StackObjectsCount = 30 } },
                    new() { Id = new MongoId(), Template = new MongoId("5af99e9186f7747c447120b8"), ParentId = root.ToString(), SlotId = "Pockets" }
                ] }
            };
        }
        var nativeOne = NativeKit(); var nativeTwo = NativeKit();
        nativeOne.Skills = new() { Common = [new() { Id = SkillTypes.Vitality, Progress = 999 }], Mastering = [], Points = 0 };
        BossMagazineProvisioningTests.Run(new BossMagazineProvisioning(itemHelper, inventoryHelper), itemHelper,
            NativeKit, json, check);
        var firstSpawn = (BotBase)merge.Invoke(service, [knight, nativeOne, null])!;
        var secondSpawn = (BotBase)merge.Invoke(service, [knight, nativeTwo, null])!;
        CompanionRaidPreparationTests.Run(service, session, knight, firstSpawn, secondSpawn, json, check);
        var nativePlayer=nativeProfiles[session].CharacterData!.PmcData!;
        nativePlayer.Quests=[new(){QId=new("6179ad0a6e9dd54ac275e3f2"),Status=QuestStatusEnum.Started,StartTime=0,StatusTimers=[]}];
        check(!service.TryGetSpawnProfile(session,"123",null,out _),"hostile task acquired after invitation blocks actual saved Knight spawn");
        var inviteText = language.GetStringMap(session, "socialUi");
        try
        {
            foreach (string role in new[] { "bossKnight", "followerBigPipe", "followerBirdEye", "exUsec" })
            {
                var invited = json.Deserialize<BotBase>(json.Serialize(knight)!)!;
                invited.Info!.Settings!.Role = role;
                storage.Write(session, "123.json", invited);
                check(!service.TryGetRaidGroupCharacter(session, "123", out var declined, out var reason, out var sender)
                    && declined != null && reason == inviteText["RogueAllianceHostileTaskReply"]
                    && sender != null && sender.Id == knight.Id && sender.Aid == 123 && sender.Info?.Nickname == declined.Info?.Nickname,
                    "hostile-task decline comes from invited companion with localized hint: " + role);
                nativePlayer.Quests = [];
                nativePlayer.TradersInfo![new("67768b19fa281ca31708b187")].Standing = 0.25;
                if (role != "bossKnight")
                    check(!service.TryGetRaidGroupCharacter(session, "123", out _, out reason, out sender)
                        && reason == inviteText["RogueAllianceStandingLowReply"] && sender?.Id == knight.Id,
                        "low-standing decline comes from invited companion: " + role);
                nativePlayer.Quests = [new() { QId = new("6179ad0a6e9dd54ac275e3f2"), Status = QuestStatusEnum.Started, StartTime = 0, StatusTimers = [] }];
                nativePlayer.TradersInfo![new("67768b19fa281ca31708b187")].Standing = 1;
            }
            var ordinaryInvite = json.Deserialize<BotBase>(json.Serialize(knight)!)!;
            ordinaryInvite.Info!.Settings!.Role = "pmcUSEC";
            storage.Write(session, "123.json", ordinaryInvite);
            service.TryGetRaidGroupCharacter(session, "123", out _, out _, out var ordinarySender);
            check(ordinarySender == null, "ordinary PMC rejection retains its existing system sender");
        }
        finally { storage.Write(session, "123.json", knight); }
        nativePlayer.Quests=[];
        check(firstSpawn.Inventory!.Items!.Count(item => item.Template.ToString() == "55d4887d4bdc2d962f8b4570") == 2,
            "Core invokes addon equipment provisioning before preview persistence");
        check(firstSpawn.Id == knight.Id && firstSpawn.Aid == knight.Aid && firstSpawn.Info!.Settings!.Role == "bossKnight",
            "fresh raid equipment preserves Knight's roster identity and native role");
        check(json.Serialize(firstSpawn.Skills) == json.Serialize(knight.Skills),
            "a new generator skill roll cannot replace the saved companion skills");
        check(firstSpawn.Inventory!.Equipment == nativeOne.Inventory!.Equipment &&
            secondSpawn.Inventory!.Equipment == nativeTwo.Inventory!.Equipment &&
            firstSpawn.Inventory.Equipment != secondSpawn.Inventory.Equipment,
            "separate native kits replace the preview equipment on consecutive raids");
        var savePreview = typeof(FriendlyTeammateService).GetMethod("SaveCompanionSpawnPreview", BindingFlags.Instance | BindingFlags.NonPublic)!;
        // Production spawn lookup already ran normal roster migration. Bring this deliberately
        // raw storage fixture through that same read before testing inventory-only persistence.
        typeof(FriendlyTeammateService).GetMethod("LoadTeammates", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [session]);
        string settingsBeforePreview = json.Serialize(storage.Read<FriendlyTeammateSettings>(session, "123-settings.json"))!;
        var rosterBeforePreview = storage.ReadProfiles(session).Single();
        savePreview.Invoke(service, [session, firstSpawn]);
        var lastKit = storage.ReadProfiles(session).Single();
        check(lastKit.Inventory!.Equipment == nativeOne.Inventory.Equipment &&
            lastKit.Inventory.Items!.Any(item => item.Id == nativeOne.Inventory.Items![1].Id),
            "last generated raid kit becomes the durable companion profile preview");
        check(lastKit.Inventory.Items!.Any(item => item.SlotId == "SecuredContainer") &&
            lastKit.Inventory.Items!.Any(item => item.Template.ToString() == "590c657e86f77412b013051d"),
            "preview retains actual boss container and medical reserves outside Realistic mode");
        check(lastKit.Id == rosterBeforePreview.Id && lastKit.Aid == rosterBeforePreview.Aid, "preview save preserves roster identity");
        check(json.Serialize(lastKit.Health) == json.Serialize(rosterBeforePreview.Health), "preview save preserves recovered health");
        check(json.Serialize(lastKit.Info) == json.Serialize(rosterBeforePreview.Info), "preview save preserves boss info");
        check(json.Serialize(lastKit.Skills) == json.Serialize(rosterBeforePreview.Skills), "preview save preserves boss skills");
        check(json.Serialize(lastKit.Customization) == json.Serialize(rosterBeforePreview.Customization), "preview save preserves appearance");
        check(json.Serialize(storage.Read<FriendlyTeammateSettings>(session, "123-settings.json")) == settingsBeforePreview,
            "preview save preserves Proficiency settings");
        check(!storage.Exists(session, "123-equipment.json"), "preview save does not create a permanent default loadout");
        savePreview.Invoke(service, [session, secondSpawn]);
        lastKit = storage.ReadProfiles(session).Single();
        check(lastKit.Inventory!.Equipment == nativeTwo.Inventory!.Equipment &&
            !lastKit.Inventory.Items!.Any(item => item.Id == nativeOne.Inventory.Items![1].Id),
            "the next generated kit replaces the earlier preview instead of accumulating gear");
        string savedPreview = json.Serialize(lastKit.Inventory)!;
        BossLootProtectionTests.Run(service, storage, settings, json, itemHelper, session, lastKit, check);
        var previewFetch = typeof(FriendlyTeammateService).GetMethod("PrepareTeammateForFetch", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var viewed = (BotBase)previewFetch.Invoke(service, [lastKit, null, false])!;
        check(json.Serialize(viewed.Inventory) == savedPreview, "viewing companion profile reuses the saved kit without generating or refilling it");
        var equipmentView = (GetOtherProfileResponse)typeof(FriendlyTeammateService)
            .GetMethod("ToOtherProfileResponse", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [viewed])!;
        check(equipmentView.Equipment!.Id == nativeTwo.Inventory!.Equipment!.ToString() &&
            equipmentView.Equipment.Items!.Any(item => item.Id == nativeTwo.Inventory.Items![1].Id),
            "native profile-view response exposes the last spawned weapon and equipment root");
        viewed.Inventory!.Items!.Clear();
        check(json.Serialize(storage.ReadProfiles(session).Single().Inventory) == savedPreview,
            "profile-view inventory cannot mutate the durable preview");
        var invalidPreview = new FastCloner().Clone(secondSpawn)!;
        invalidPreview.Id = new MongoId();
        bool rejectedPreview = false;
        try { savePreview.Invoke(service, [session, invalidPreview]); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejectedPreview = true; }
        check(rejectedPreview && json.Serialize(storage.ReadProfiles(session).Single().Inventory) == savedPreview,
            "mismatched actor identity cannot overwrite companion preview");
        invalidPreview = new FastCloner().Clone(secondSpawn)!;
        invalidPreview.Info!.Settings!.Role = "followerBigPipe";
        rejectedPreview = false;
        try { savePreview.Invoke(service, [session, invalidPreview]); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejectedPreview = true; }
        check(rejectedPreview && json.Serialize(storage.ReadProfiles(session).Single().Inventory) == savedPreview,
            "different companion role cannot overwrite Knight's preview");
        settings.SaveAndApply(new() { GameplayMode = "GunsForHire" });
        string ordinaryModeBefore = json.Serialize(storage.ReadProfiles(session).Single())!;
        rejectedPreview = false;
        try { savePreview.Invoke(service, [session, secondSpawn]); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejectedPreview = true; }
        check(rejectedPreview && json.Serialize(storage.ReadProfiles(session).Single()) == ordinaryModeBefore,
            "spawn preview cannot write a same-account roster in another mode");
        settings.SaveAndApply(new() { GameplayMode = "Allegiance" });
        firstSpawn.Inventory.Items!.Clear();
        check(nativeOne.Inventory.Items!.Count == 6 && secondSpawn.Inventory!.Items!.Count > 3 && json.Serialize(knight) == beforeSpawn,
            "raid equipment is isolated from the generated template, next raid and persistent profile");
        foreach (string mode in new[] { "Restricted", "Immersive", "Extreme" })
        {
            settings.SaveAndApply(new() { GameplayMode = "Allegiance", LoadoutManagementMode = mode });
            foreach (string? containerTemplate in new[] { null, "5857a8bc2459772bad15db29", "5c0a794586f77461c458f892" })
            {
                var native = NativeKit();
                if (containerTemplate != null)
                {
                    var container = new Item { Id = new MongoId(), Template = new MongoId(containerTemplate),
                        ParentId = native.Inventory!.Equipment!.ToString(), SlotId = "SecuredContainer" };
                    native.Inventory.Items!.Add(container);
                    native.Inventory.Items.Add(new() { Id = new MongoId(), Template = ammoTemplate,
                        ParentId = container.Id.ToString(), SlotId = "main", Upd = new() { StackObjectsCount = 1 } });
                }
                string nativeSnapshot = json.Serialize(native)!;
                var raid = (BotBase)merge.Invoke(service, [knight, native, null])!;
                var raidItems = raid.Inventory!.Items!;
                var secure = raidItems.Single(item => item.SlotId == "SecuredContainer");
                var supplies = raidItems.Where(item => item.ParentId == secure.Id.ToString()).ToList();
                check(secure.Template.ToString() == "5c0a794586f77461c458f892" &&
                    secure.ParentId == raid.Inventory.Equipment!.ToString(), "raid clone has one equipped boss container: " + mode);
                check(supplies.Any(item => item.Template.ToString() == "590c657e86f77412b013051d") &&
                    supplies.Any(item => item.Template.ToString() == "5d02797c86f774203f38e30a"),
                    "Knight receives teammate medical reserves: " + mode);
                check(supplies.Count(item => item.Template == ammoTemplate) == 10 &&
                    supplies.Where(item => item.Template == ammoTemplate).All(item =>
                        item.Upd?.StackObjectsCount == templates.Items[ammoTemplate].Properties!.StackMaxSize),
                    "Knight receives ten full stacks matching his weapon ammunition: " + mode);
                check(json.Serialize(native) == nativeSnapshot && json.Serialize(knight) == beforeSpawn,
                    "managed reserves never change native generation or stored preview: " + mode);
            }
        }
        var preparePreview = typeof(FriendlyTeammateService).GetMethod("PrepareTeammateForFetch", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var ordinary = NativeKit();
        ordinary.Info!.Settings!.Role = "pmcUSEC";
        var realisticOrdinary = (BotBase)preparePreview.Invoke(service, [ordinary, null, false])!;
        check(!realisticOrdinary.Inventory!.Items!.Any(item => item.SlotId == "SecuredContainer"),
            "ordinary Realistic teammates do not gain managed containers or reserves");
        var preview = (BotBase)preparePreview.Invoke(service, [knight, null, false])!;
        check(json.Serialize(preview.Inventory) == json.Serialize(knight.Inventory),
            "Knight's off-raid preview does not receive raid-only reserves");
        foreach (var companion in FriendlyGameplayModeAddons.Companions("Allegiance"))
            check(FriendlyGameplayModeAddons.Policy("GunsForHire", new BotBase { Info = new() { Settings = new() { Role = companion.Role } } }) == null,
                "companion presentation cannot leak into Guns for Hire: " + companion.Role);
        Reject(() => service.RenameTeammate(session, new() { Aid = "123", Nickname = "Changed" }), check);
        Reject(() => service.SetTeammateSuit(session, new() { Aid = "123", Suit = ["a", "b"] }), check);
        Reject(() => service.SetTeammateVoice(session, new() { Aid = "123", Voice = "a" }), check);
        Reject(() => service.SetTeammateTactic(session, new() { Aid = "123", Tactic = "Marksman" }), check);
        Reject(() => service.BuyTeammateKit(session, new() { Aid = "123" }), check);
        // A legacy profile used our former floors; only progress above that imposed
        // contribution is carried onto a fresh actual generated baseline once.
        var legacy = storage.ReadProfiles(session).Single();
        legacy.Skills = new() { Common = [
            new() { Id = SkillTypes.Assault, Progress = 4500 },
            new() { Id = SkillTypes.RecoilControl, Progress = 4125 },
            new() { Id = SkillTypes.Endurance, Progress = 5100 },
            new() { Id = SkillTypes.Perception, Progress = 3520 },
        ], Mastering = [], Points = 3 };
        storage.Write(session, "123.json", legacy);
        var nativeSkills = NativeKit();
        nativeSkills.Skills = new() { Common = [
            new() { Id = SkillTypes.RecoilControl, Progress = 21.5 },
            new() { Id = SkillTypes.Perception, Progress = 11.25 },
            new() { Id = SkillTypes.BotReload, Progress = 1437.5 },
        ], Mastering = [], Points = 0 };
        string generatedSkillSnapshot = json.Serialize(nativeSkills)!;
        var nativeSkillPreparation = typeof(FriendlyTeammateService).GetMethod("PrepareCompanionGeneratedSkills", BindingFlags.Instance | BindingFlags.NonPublic)!;
        nativeSkillPreparation.Invoke(service, [session, legacy, nativeSkills]);
        var rebased = storage.ReadProfiles(session).Single();
        check(!rebased.Skills!.Common.Any(skill => skill.Id == SkillTypes.Assault), "exact imposed assault floor is removed when native generation supplies no assault skill");
        check(rebased.Skills.Common.Single(skill => skill.Id == SkillTypes.RecoilControl).Progress == 146.5 &&
            rebased.Skills.Common.Single(skill => skill.Id == SkillTypes.Perception).Progress == 31.25,
            "earned progress above old floors is added to actual generated values");
        check(rebased.Skills.Common.Single(skill => skill.Id == SkillTypes.BotReload).Progress == 1437.5 &&
            rebased.Skills.Common.Single(skill => skill.Id == SkillTypes.Endurance).Progress == 5100 && rebased.Skills.Points == 3,
            "generated skills use actual rolled values while native/learned skills and points survive");
        check(json.Serialize(nativeSkills) == generatedSkillSnapshot && storage.Exists(session, $"123-{rebased.Id}-companion-skills-native-v1.json"),
            "native source remains unchanged and corrected profile has a durable one-time receipt");
        service.PersistFollowerProgress(session, [new() { Aid = "123", Skills = [new() {
            Id = System.Text.Json.JsonSerializer.SerializeToElement("RecoilControl"), Current = 146.5, Progress = 7.25
        }] }]);
        nativeSkills.Skills.Common.Single(skill => skill.Id == SkillTypes.RecoilControl).Progress = 999;
        nativeSkillPreparation.Invoke(service, [session, legacy, nativeSkills]);
        rebased = storage.ReadProfiles(session).Single();
        check(rebased.Skills!.Common.Single(skill => skill.Id == SkillTypes.RecoilControl).Progress == 153.75,
            "later generated raid kits never replace or rebase earned skill progression");
        var fresh = new FastCloner().Clone(rebased)!;
        fresh.Aid = 124; fresh.Id = new MongoId();
        fresh.Skills = new() { Common = [new() { Id = SkillTypes.Assault, Progress = 12.75 }], Mastering = [], Points = 0 };
        var receiptMethod = typeof(FriendlyTeammateService).GetMethod("CompanionSkillsReceipt", BindingFlags.Static | BindingFlags.NonPublic)!;
        string freshReceipt = (string)receiptMethod.Invoke(null, [fresh, policy])!;
        storage.WriteBatch(session, new Dictionary<string,string> {
            ["124.json"] = json.Serialize(fresh)!, [freshReceipt] = "{\"initialized\":true}"
        });
        string freshSkills = json.Serialize(fresh.Skills)!;
        var preparedFresh = (BotBase)nativeSkillPreparation.Invoke(service, [session, fresh, nativeSkills])!;
        check(json.Serialize(preparedFresh.Skills) == freshSkills,
            "newly generated companions already have a native receipt and keep their exact initial roll");
        fresh.Id = new MongoId();
        check((string)receiptMethod.Invoke(null, [fresh, policy])! != freshReceipt,
            "reused account ID cannot reuse another actor's skill receipt");
        storage.DeleteTeammate(session, 124);
        // Keep the later general migration assertion tied to the progressed value.
        var migrated = storage.ReadProfiles(session).Single();
        check(migrated.Info!.Level == Math.Min(60, globals.Configuration.Exp.Level.ExperienceTable.Length) &&
            migrated.Skills!.Common.Any(skill => skill.Id == SkillTypes.RecoilControl && skill.Progress == 153.75),
            "native-skill correction and level preparation preserve roster identity and receipts");
        var keptSettings = storage.Read<FriendlyTeammateSettings>(session, "123-settings.json")!;
        check(keptSettings.Aggression == 73 && keptSettings.Proficiency.Accuracy == 140 && keptSettings.Proficiency.VisionDistance == 125,
            "boss migration preserves existing Proficiency customization");
        var projected = (GetOtherProfileResponse)typeof(FriendlyTeammateService)
            .GetMethod("ToOtherProfileResponse", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [migrated])!;
        check(projected.Info!.Experience == migrated.Info.Experience,
            "managed profile projection preserves legacy boss level XP");
        check(migrated.Info.MemberCategory == MemberCategory.Sherpa &&
            migrated.Info.SelectedMemberCategory == MemberCategory.Sherpa &&
            projected.Info.MemberCategory == (int)MemberCategory.Sherpa,
            "saved and viewed Goons use the legacy special Sherpa category");
        foreach (string projection in new[] { "ToFriendSummary", "ToGroupCharacter", "ToTeammateSummary" })
        {
            var method = typeof(FriendlyTeammateService).GetMethod(projection, BindingFlags.Instance | BindingFlags.NonPublic)!;
            var dto = method.Invoke(service, method.GetParameters().Length == 1 ? [migrated] : [migrated, null])!;
            var info = dto.GetType().GetProperty("Info")!.GetValue(dto)!;
            check(Convert.ToInt32(info.GetType().GetProperty("MemberCategory")!.GetValue(info)) == (int)MemberCategory.Sherpa,
                "special boss category survives projection: " + projection);
        }
        // AcceptScreen rebuilds previews from the serialized profile response after
        // equipment preparation, bypassing AddPlayerToGroup. Test the actual wire fields.
        var iconCases = new List<object>();
        string iconDirectory = Path.Combine(AppContext.BaseDirectory, "icon-contract");
        Directory.CreateDirectory(iconDirectory);
        foreach (string role in new[] { "bossKnight", "followerBigPipe", "followerBirdEye", "pmcUSEC" })
        {
            var iconProfile = new FastCloner().Clone(migrated)!;
            iconProfile.Info!.Settings!.Role = role;
            iconProfile.Info.MemberCategory = iconProfile.Info.SelectedMemberCategory = MemberCategory.Sherpa;
            int expected = (int)(role == "pmcUSEC" ? MemberCategory.Unheard : MemberCategory.Sherpa);
            var other = (GetOtherProfileResponse)typeof(FriendlyTeammateService)
                .GetMethod("ToOtherProfileResponse", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [iconProfile])!;
            var group = (SPTarkov.Server.Core.Models.Eft.Match.GroupCharacter)typeof(FriendlyTeammateService)
                .GetMethod("ToGroupCharacter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [iconProfile])!;
            foreach (var (boundary, info, kind) in new (string, object, string)[] {
                ("profile", other.Info!, "other"), ("group", group.Info!, "player"),
                ("visual", group.VisualRepresentation!.Info!, "player") })
            {
                string serialized = json.Serialize(info)!;
                using var document = System.Text.Json.JsonDocument.Parse(serialized);
                var fields = document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value,
                    StringComparer.OrdinalIgnoreCase);
                check(fields["MemberCategory"].GetInt32() == expected, role + "/" + boundary + " serializes authoritative member category");
                check(fields.TryGetValue("SelectedMemberCategory", out var selected) && selected.GetInt32() == expected,
                    role + "/" + boundary + " serializes selected category for native preview refresh");
                string path = Path.Combine(iconDirectory, role + "-" + boundary + ".json");
                File.WriteAllText(path, serialized);
                iconCases.Add(new { Path = path, Kind = kind, Expected = expected });
            }
        }
        File.WriteAllText(Path.Combine(iconDirectory, "manifest.json"), System.Text.Json.JsonSerializer.Serialize(iconCases));
        check(policy.GetVisibleSkills!().Contains("Assault") && !policy.GetVisibleSkills().Contains("Sniper"),
            "Knight's visible skills focus on his assault role");
        check(FriendlyGameplayModeAddons.Companions("Allegiance").Single(item => item.Role == "followerBigPipe") is
            { RaidReady: true, SeedForTesting: false, ProtectSpawnInventory: true, HideRaidStats: true }, "Big Pipe retains protected combat support without test seeding");
        check(FriendlyGameplayModeAddons.Companions("Allegiance").Single(item => item.Role == "followerBirdEye") is
            { RaidReady: true, SeedForTesting: false, HideRaidStats: true, ProtectSpawnInventory: true, ProficiencyOnly: true, GenerateEquipmentEachRaid: true },
            "Birdeye's implemented brain enables protected native-kit raid spawning with boss presentation");
        check(FriendlyGameplayModeAddons.Companions("Allegiance").Single(item => item.Role == "followerBirdEye").CanJoinRoster != null, "Partizan hunt registers permanent Birdeye roster reconciliation");
        check(service.ListFollowerDetails(session).Single().Role == "bossKnight", "saved native role reaches raid-selection metadata");
        service.SetTeammateProficiency(session, new() { Aid = "123", Proficiency = new() });
        var savedBossRole = knight.Info!.Settings!.Role;
        var savedBossSettings = storage.Read<FriendlyTeammateSettings>(session, "123-settings.json")!;
        foreach (string role in new[] { "bossKnight", "followerBigPipe", "followerBirdEye" })
        {
            knight.Info.Settings.Role = role;storage.Write(session, "123.json", knight);
            storage.Write(session, "123-settings.json", new FriendlyTeammateSettings
                { Aggression = 73, SelfDetermination = 42, CombatTactic = "Marksman" });
            var bossOptions = service.GetProfileOptions(session, new() { Aid = "123" });
            check(bossOptions.ProficiencyOnly && bossOptions.LockCombatSettings,
                "all Goons publish proficiency-only combat controls: " + role);
            using (var summary = System.Text.Json.JsonDocument.Parse(json.Serialize(service.ListTeammates(session).Single())!))
                check(summary.RootElement.GetProperty("PinnedRoster").GetBoolean(), "Goon summary pins the fixed non-removable roster card: " + role);
            foreach (string route in new[] { "account", "profile" })
            {
                bool rejected = false;
                string before = json.Serialize(storage.Read<BotBase>(session, "123.json"))!;
                try
                {
                    if (route == "account") service.DeleteTeammateWithPayment(session, new() { AccountId = "123" });
                    else service.DeleteTeammateByProfileId(session, new MongoId(knight.Id.ToString()!));
                }
                catch (FriendlyTeammateException ex) { rejected = ex.Message == "CompanionRemovalLocked"; }
                check(rejected && json.Serialize(storage.Read<BotBase>(session, "123.json")) == before,
                    "all Goon removal routes reject before editing the roster: " + role + "/" + route);
                check(!storage.Exists(session, "pending-deletion.json"), "Goon removal creates no payment/removal journal");
            }
            foreach (string change in new[] { "aggression", "tactic" })
            {
                bool rejected = false;
                try
                {
                    if (change == "aggression") service.SetTeammateAggression(session, new() { Aid = "123", Aggression = 99 });
                    else service.SetTeammateTactic(session, new() { Aid = "123", Tactic = "Rifleman" });
                }
                catch (FriendlyTeammateException) { rejected = true; }
                check(rejected, "server rejects forged Goon combat edits: " + role + "/" + change);
            }
            service.SetTeammateProficiency(session, new() { Aid = "123", Proficiency = new() { Accuracy = 130 }, SelfDetermination = 99 });
            var bossSettings = storage.Read<FriendlyTeammateSettings>(session, "123-settings.json")!;
            check(bossSettings.Aggression == 73 && bossSettings.SelfDetermination == 42 && bossSettings.Proficiency.Accuracy == 130,
                "Goon proficiency edits cannot alter hidden aggression/determination: " + role);
            check(service.GetProfileOptions(session, new() { Aid = "123" }).CurrentTactic == "Marksman",
                "hiding boss controls does not apply the Rogue Rifleman tactic override: " + role);
        }
        knight.Info.Settings.Role = savedBossRole;storage.Write(session, "123.json", knight);
        storage.Write(session, "123-settings.json", savedBossSettings);
        check(storage.Exists(session, "123-settings.json"), "Knight proficiency remains editable");
        check(storage.ReadProfiles(session).Count == 1 && !storage.Exists(session, "addon-companion-bossknight.json"),
            "an existing Knight is retained without a duplicate or new seed receipt");
        storage.DeleteTeammate(session, 123);
        check(service.ListTeammates(session).Count == 0 && !storage.Exists(session, "addon-companion-bossknight.json"),
            "removing Knight cannot trigger a new seed grant");
        var rogue = new FastCloner().Clone(knight)!;
        rogue.Id = new MongoId(); rogue.Aid = 129; rogue.Info!.Settings!.Role = "exUsec";
        storage.Write(session, "129.json", rogue);
        using (var summary = System.Text.Json.JsonDocument.Parse(json.Serialize(service.ListTeammates(session).Single())!))
            check(!summary.RootElement.GetProperty("PinnedRoster").GetBoolean(), "ordinary Rogue remains in the removable scrolling roster");
        storage.Write(session, "129-settings.json", new FriendlyTeammateSettings
        { Aggression = 68, SelfDetermination = 42, CombatTactic = "SainMan" });
        check(FriendlyGameplayModeAddons.Policy("Allegiance", rogue) is { LockCombatSettings: true, ProficiencyOnly: false },
            "Rogue locks combat controls without locking ordinary identity/equipment customization");
        check(service.ListFollowerDetails(session).Single().Tactic == "Rifleman",
            "existing Rogue tactic resolves to the fixed Rifleman brain without rerolling numeric preferences");
        foreach (string change in new[] { "aggression", "tactic" })
        {
            bool rejected = false;
            try
            {
                if (change == "aggression") service.SetTeammateAggression(session, new() { Aid = "129", Aggression = 99 });
                else service.SetTeammateTactic(session, new() { Aid = "129", Tactic = "Marksman" });
            }
            catch (FriendlyTeammateException) { rejected = true; }
            check(rejected, "server rejects forged Rogue combat edits: " + change);
        }
        service.SetTeammateProficiency(session, new() { Aid = "129", Proficiency = new() { Accuracy = 130 }, SelfDetermination = 99 });
        var locked = storage.Read<FriendlyTeammateSettings>(session, "129-settings.json")!;
        check(locked.Aggression == 68 && locked.SelfDetermination == 42 && locked.Proficiency.Accuracy == 130,
            "proficiency remains editable while a forged determination field cannot change saved combat percentages");
        storage.DeleteTeammate(session, 129);
        var bigPipeSession = new MongoId("c1c1c1c1c1c1c1c1c1c1c1c1");
        storage.InitializeProfile(bigPipeSession);
        storage.Write(bigPipeSession, "addon-companion-bossknight.json", new { initialized = true });
        storage.Write(bigPipeSession, "addon-companion-followerbirdeye.json", new { initialized = true });
        var bigPipe = new FastCloner().Clone(bosses["followerBigPipe"])!;
        bigPipe.Id = new MongoId(); bigPipe.Aid = 125; bigPipe.Inventory = new() { Items = [] };
        storage.Write(bigPipeSession, "125.json", bigPipe);
        nativeProfiles[bigPipeSession].CharacterData!.PmcData!.TradersInfo![new("67768b19fa281ca31708b187")].Standing=0.49;
        check(!service.TryGetSpawnProfile(bigPipeSession,"125",null,out _),"standing lost after invitation blocks actual Big Pipe spawn");
        nativeProfiles[bigPipeSession].CharacterData!.PmcData!.TradersInfo![new("67768b19fa281ca31708b187")].Standing=1;
        check(service.ListFollowerDetails(bigPipeSession).Single().Role == "followerBigPipe" &&
            !storage.Exists(bigPipeSession, "addon-companion-followerbigpipe.json"), "existing Big Pipe reaches raid-selection metadata without a seed grant");
        storage.DeleteTeammate(bigPipeSession, 125);
        check(service.ListTeammates(bigPipeSession).Count == 0, "Big Pipe seed receipt prevents regrant after removal");
        var birdEyeSession = new MongoId("c3c3c3c3c3c3c3c3c3c3c3c3");
        storage.InitializeProfile(birdEyeSession);
        storage.Write(birdEyeSession, "addon-companion-bossknight.json", new { initialized = true });
        storage.Write(birdEyeSession, "addon-companion-followerbigpipe.json", new { initialized = true });
        var birdEye = new FastCloner().Clone(bosses["followerBirdEye"])!;
        birdEye.Id = new MongoId(); birdEye.Aid = 126; birdEye.Inventory = new() { Items = [] };
        storage.Write(birdEyeSession, "126.json", birdEye);
        check(service.ListFollowerDetails(birdEyeSession).Single().Role == "followerBirdEye" &&
            !storage.Exists(birdEyeSession, "addon-companion-followerbirdeye.json"), "existing Birdeye reaches raid selection without a seed grant");
        check(storage.ReadProfiles(birdEyeSession).Count == 1, "Birdeye test seed does not duplicate an existing member");
        storage.DeleteTeammate(birdEyeSession, 126);
        check(service.ListTeammates(birdEyeSession).Count == 0, "Birdeye seed receipt prevents regrant after removal");
        var penaltySession = new MongoId("c2c2c2c2c2c2c2c2c2c2c2c2");
        storage.InitializeProfile(penaltySession);
        var penaltyService = new FriendlyEncounterPenaltyService(storage, settings);
        long killedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int allyAid = 2000;
        foreach (var companion in FriendlyGameplayModeAddons.Companions("Allegiance").Where(item => item.ProficiencyOnly))
        {
            check(companion.IgnoreEncounterKillPenalty, "Goons register encounter-penalty exemption: " + companion.Role);
            var ally = new FastCloner().Clone(bosses[companion.Role])!;
            ally.Id = new MongoId(); ally.Aid = allyAid++;
            storage.Write(penaltySession, ally.Aid + ".json", ally);
            var report = new FriendlyPostRaidKillMessageRequest { RaidId = "boss-test", VictimProfileId = ally.Id.ToString(),
                MessageKind = "traitor", KilledAtUnixMs = killedAt };
            check(penaltyService.Record(penaltySession, report, killedAt).Entries.Count == 0,
                "server rejects queued/stale boss traitor report using saved role, not nickname: " + companion.Role);
            storage.DeleteTeammate(penaltySession, (int)ally.Aid);
        }
        check(penaltyService.Record(penaltySession, new() { RaidId = "recruit-test", VictimProfileId = "ordinary-recruit",
            MessageKind = "traitor", KilledAtUnixMs = killedAt }, killedAt).Entries.Count == 1,
            "boss exemption preserves penalties for ordinary recruits");
        var registrations = (System.Collections.IDictionary)typeof(FriendlyGameplayModeAddons).GetField("Modes", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var installed = registrations["Allegiance"]!;
        registrations.Remove("Allegiance");
        try
        {
            var ready = typeof(FriendlyTeammateService).GetMethod("IsCompanionRaidReady", BindingFlags.NonPublic | BindingFlags.Instance)!;
            foreach(string role in new[] { "bossKnight", "followerBigPipe", "followerBirdEye" })
                check(!(bool)ready.Invoke(service, [new BotBase { Info = new() { Settings = new() { Role = role } } }])!, "missing addon cannot spawn a stored Goon through ordinary Allegiance: " + role);
            check((bool)ready.Invoke(service, [new BotBase { Info = new() { Settings = new() { Role = "pmcUSEC" } } }])!, "ordinary Allegiance teammates remain available without addon");
        }
        finally { registrations["Allegiance"] = installed; }
        settings.SaveAndApply(new());
    }
    private static void Reject(Action action, Action<bool, string> check)
    {
        try { action(); } catch (FriendlyTeammateException ex) when (ex.Message == "CompanionCustomizationLocked")
        { check(true, "companion customization rejected before mutation"); return; }
        check(false, "companion customization was accepted");
    }
}

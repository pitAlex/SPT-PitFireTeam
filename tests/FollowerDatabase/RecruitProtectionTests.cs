using System.Reflection;
using System.Collections.Concurrent;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;

internal static class RecruitProtectionTests
{
    internal static void Run(FriendlyTeammateStorage storage, FriendlyServerSettingsService settings,
        JsonUtil json, MongoId session, Action<bool, string> check)
    {
        var constructor = typeof(FriendlyTeammateService).GetConstructors().Single();
        var service = (FriendlyTeammateService)constructor.Invoke(constructor.GetParameters().Select(parameter => parameter.Name switch
        {
            "storage" => (object)storage, "settingsService" => settings, "jsonUtil" => json,
            "logger" => DispatchProxy.Create<ISptLogger<FriendlyTeammateService>, TestLogger>(), _ => null
        }).ToArray());
        var sanitize = typeof(FriendlyTeammateService).GetMethod("SanitizeRecruitCandidateEquipment", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Item New(string? parent = null, string? slot = null) => new() { Id = new MongoId(), Template = new MongoId(), ParentId = parent, SlotId = slot };
        Item originalRoot = New(), gun = New(originalRoot.Id.ToString(), "FirstPrimaryWeapon"),
            helmet = New(originalRoot.Id.ToString(), "Headwear"), scope = New(gun.Id.ToString(), "mod_scope");
        var original = new BotBase { Aid = 992, Id = new MongoId(), Info = new() { Nickname = "fallen" },
            Inventory = new() { Equipment = originalRoot.Id, Items = [originalRoot, gun, scope, helmet] } };
        storage.Write(session, "992.json", original);
        var originalJson = json.Serialize(original);
        Item recruitRoot = New(), ownGun = New(recruitRoot.Id.ToString(), "SecondPrimaryWeapon"),
            ownBag = New(recruitRoot.Id.ToString(), "Backpack"), cargo = New(ownBag.Id.ToString(), "main");
        var givenGun = json.Deserialize<Item>(json.Serialize(gun)!)!; givenGun.ParentId = recruitRoot.Id.ToString();
        var givenHelmet = json.Deserialize<Item>(json.Serialize(helmet)!)!; givenHelmet.ParentId = recruitRoot.Id.ToString();
        Item newAttachment = New(gun.Id.ToString(), "mod_test");
        Item liveOnly = New(ownBag.Id.ToString(), "main"), liveChild = New(liveOnly.Id.ToString(), "mod_test");
        var registrations = (ConcurrentDictionary<string, HashSet<string>>)typeof(FriendlyPostRaidService)
            .GetField("ProtectedRaidItemIds", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        registrations.TryGetValue(session.ToString(), out var previousRegistration);
        registrations[session.ToString()] = [liveOnly.Id.ToString()];
        var recruit = new BotBase { Id = new MongoId(), Info = new() { Nickname = "recruit" },
            Inventory = new() { Equipment = recruitRoot.Id, Items = [recruitRoot, givenGun, scope, newAttachment, givenHelmet, ownGun, ownBag, cargo, liveOnly, liveChild] } };
        string offeredJson = json.Serialize(recruit)!;
        FriendlyRecruitPickupCandidate Candidate() => new() { ProfileId = recruit.Id!.Value.ToString(), Side = "Usec", ProfileJson = offeredJson,
#if DEBUG
            DebugForceInvite = true,
#endif
            GivenEquipmentSlots = ["FirstPrimaryWeapon", "Headwear", "Backpack"] };
        bool Clean(FriendlyRecruitPickupCandidate candidate) => (bool)sanitize.Invoke(service, [session, candidate])!;
        foreach (string mode in new[] { "Restricted", "Immersive", "Realistic" })
        {
            settings.SaveAndApply(new() { LoadoutManagementMode = mode });
            var candidate = Candidate();
            check(Clean(candidate) == (mode == "Restricted"), mode + " respects existing equipment ownership mode");
            var result = json.Deserialize<BotBase>(candidate.ProfileJson)!;
            var resultItems = result.Inventory!.Items!;
            if (mode == "Restricted")
            {
                check(!resultItems.Any(item => new[] { gun.Id, scope.Id, helmet.Id, newAttachment.Id }.Contains(item.Id)),
                    "Invite drops protected weapon/helmet plus complete descendants before remapping IDs");
                check(resultItems.Select(item => item.Id).ToHashSet().SetEquals(new[] { recruitRoot.Id, ownGun.Id, ownBag.Id, cargo.Id }),
                    "Recruit native kit and valid unrelated gifts remain exact");
                check(candidate.GivenEquipmentSlots.SequenceEqual(new[] { "Backpack" }), "Protected equipment cannot grant invitation slot bonus");
                check(!resultItems.Any(item => item.Id == liveOnly.Id || item.Id == liveChild.Id),
                    "Live-only registered protected trees are excluded even when absent from saved Default");
                check(!Clean(candidate), "Repeated pending preview/acceptance sanitation is idempotent");
                storage.Write(session, "protected-invite-test.json", new[] { new FriendlyRecruitRequestEntry { ProfileJson = candidate.ProfileJson } });
                check(!json.Deserialize<BotBase>(storage.Read<List<FriendlyRecruitRequestEntry>>(session, "protected-invite-test.json")!.Single().ProfileJson)!
                    .Inventory!.Items!.Any(item => item.Id == gun.Id), "Sanitized pending capture persists without protected copies");
            }
            else check(resultItems.Count == recruit.Inventory!.Items!.Count && candidate.GivenEquipmentSlots.Length == 3,
                mode + " retains legitimate recovered field gear and donation credit");
            check(json.Serialize(storage.Read<BotBase>(session, "992.json")) == originalJson, "Original fallen teammate equipment never changed");
        }
        if (previousRegistration == null) registrations.TryRemove(session.ToString(), out _);
        else registrations[session.ToString()] = previousRegistration;
        storage.DeleteTeammate(session, 992);
        settings.SaveAndApply(new());
    }
}

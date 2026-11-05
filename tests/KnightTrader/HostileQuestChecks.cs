using pitTeam.GoingRogue.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

internal static class HostileQuestChecks
{
    internal static void Run(JsonUtil json, TemplateTable templates, Action<bool, string> check)
    {
        string runtime = Path.GetDirectoryName(typeof(GlobalTable).Assembly.Location)!;
        var quests = json.Deserialize<Dictionary<MongoId, Quest>>(File.ReadAllText(
            Path.Combine(runtime, "SPT_Data/database/templates/quests.json")))!;
        typeof(TemplateTable).GetProperty(nameof(TemplateTable.Quests))!.SetValue(templates, quests);
        RogueAlliance.Configure(templates);
        var expected = new HashSet<string> {
            "649570491bb4d158bc4d0168", "6658a15615cbb1b2c6014d5b", "66e01ad6835f78499f049180",
            "626bdcc3a371ee3a7a3514c5", "647710905320c660d91c15a5", "6179ad0a6e9dd54ac275e3f2",
            "6613f307fca4f2f386029409", "6615141bfda04449120269a7"
        };
        foreach (var entry in quests)
            check(RogueAlliance.HasHostileObjectives(entry.Value) == expected.Contains(entry.Key.ToString()),
                "installed quest completion/failure classification: " + entry.Key);
        // Optional installed custom content is an additional integration check.
        string armory = Path.Combine(runtime, "user/mods/WTT-Armory/db/CustomQuests/PEACEKEEPER/Quests/PeacekeeperQuests.json");
        if (File.Exists(armory))
        {
            var custom = json.Deserialize<Dictionary<MongoId, Quest>>(File.ReadAllText(armory))!;
            var culling = custom[new MongoId("686dd4f871840e292fbd1a14")];
            check(RogueAlliance.HasHostileObjectives(culling), "installed WTT Culling The Herd needs no predefined ID");
            quests[culling.Id] = culling;
        }
        var player = new BotBase { Quests = [], TradersInfo = new() { [KnightTraderCatalog.Id] = new() { Standing = 2 } } };
        // A novel ID added after registration proves that load order is not snapshotted.
        var novel = quests[new MongoId("6179ad0a6e9dd54ac275e3f2")] with { Id = new MongoId() };
        quests[novel.Id] = novel;
        player.Quests = [new() { QId = novel.Id, Status = QuestStatusEnum.Started, StartTime = 0, StatusTimers = [] }];
        foreach (var status in new[] { QuestStatusEnum.Started, QuestStatusEnum.Success, QuestStatusEnum.AvailableForFinish, QuestStatusEnum.Fail, QuestStatusEnum.AvailableForStart, QuestStatusEnum.Locked })
        {
            player.Quests[0].Status = status;
            bool conflict = status is QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish;
            check(RogueAlliance.Conflict(player) == conflict, "live custom task status without cached IDs: " + status);
            foreach (string role in new[] { "bossKnight", "followerBigPipe", "followerBirdEye", "exUsec" })
                check((RogueAlliance.Restriction(player, role) == "RogueAllianceHostileTask") == conflict,
                    "live status admission: " + role + "/" + status);
        }
        player.Quests[0].Status = QuestStatusEnum.Started;
        quests.Remove(novel.Id);
        check(!RogueAlliance.Conflict(player), "removed definition does not retain a cached conflict");
        var repeatableJson = System.Text.Json.Nodes.JsonNode.Parse(json.Serialize(novel)!)!;
        repeatableJson["changeCost"] = new System.Text.Json.Nodes.JsonArray();
        var repeatable = json.Deserialize<RepeatableQuest>(repeatableJson.ToJsonString())!;
        player.RepeatableQuests = [new() { ActiveQuests = [repeatable], ChangeRequirement = [] }];
        check(RogueAlliance.Conflict(player), "profile-local active repeatable quest objectives are checked");
        var otherPlayer = new BotBase { Quests = player.Quests };
        check(!RogueAlliance.Conflict(otherPlayer), "repeatable definitions do not leak between players");
        player.RepeatableQuests[0].ActiveQuests!.Clear();
        check(!RogueAlliance.Conflict(player), "expired/replaced repeatable definition releases restriction");
        check(!RogueAlliance.HasHostileObjectives(null), "missing template has no invented kill target");
    }
}

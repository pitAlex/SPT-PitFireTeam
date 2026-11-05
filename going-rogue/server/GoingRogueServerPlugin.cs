using pitTeam.Server.Services;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Helpers.Bot;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace pitTeam.GoingRogue.Server;

public record GoingRogueMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "xyz.pit.fireteam.goingrogue";
    public string Name { get; init; } = "PitFireTeam Going Rogue";
    public string Author { get; init; } = "PitAlex";
    public List<string>? Contributors { get; init; }
    public Version Version { get; init; } = new("1.0.0");
    public Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; } = new() { ["xyz.pit.fireteam"] = new(">=1.1.1") };
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.Preload)]
public sealed class GoingRogueServerPlugin(GlobalTable globals, BotTable bots, ItemHelper itemHelper, InventoryHelper inventoryHelper,
    BotConfig botConfig, BotInventoryGenerator inventoryGenerator, BotHelper botHelper, TemplateTable templates) : IOnLoad
{
    private const string AddonId = "xyz.pit.fireteam.goingrogue";
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RogueAlliance.Configure(templates);
        var profiles = new BossProfilePolicy(globals, bots);
        var magazines = new BossMagazineProvisioning(itemHelper, inventoryHelper);
        var headwear = new BossHeadwearPolicy(bots, botConfig, inventoryGenerator, botHelper, itemHelper);
        void PrepareRaidEquipment(SPTarkov.Server.Core.Models.Eft.Common.Tables.BotBase profile)
        {
            headwear.Ensure(profile);
            magazines.Ensure(profile);
        }
        FriendlyGameplayModeAddons.Register("Allegiance", new FriendlyModeCompanion[] {
            new("bossKnight", "Knight", "#667D91", ProficiencyOnly: true, SeedForTesting: false, RaidReady: true)
                { PinnedRoster = true, HideRaidStats = true, GenerateEquipmentEachRaid = true, ProtectSpawnInventory = true, IgnoreEncounterKillPenalty = true, PrepareProfile = profiles.Apply, PrepareRaidEquipment = PrepareRaidEquipment, GeneratedSkillsVersion = "native-v1", AdoptGeneratedSkills = profiles.AdoptGeneratedSkills, GetVisibleSkills = () => profiles.VisibleSkills("bossKnight"), CanSpawnForQuest = KnightQuestModule.CanSpawnKnight, CanJoinRoster = KnightQuestModule.CanJoinKnightRoster, RaidDeclineMessageKey = RogueAlliance.DeclineMessageKey, RaidRestriction = player => RogueAlliance.Restriction(player, "bossKnight") },
            new("followerBigPipe", "Big Pipe", "#C9A044", ProficiencyOnly: true, SeedForTesting: false, RaidReady: true)
                { PinnedRoster = true, HideRaidStats = true, GenerateEquipmentEachRaid = true, ProtectSpawnInventory = true, IgnoreEncounterKillPenalty = true, PrepareProfile = profiles.Apply, PrepareRaidEquipment = PrepareRaidEquipment, GeneratedSkillsVersion = "native-v1", AdoptGeneratedSkills = profiles.AdoptGeneratedSkills, GetVisibleSkills = () => profiles.VisibleSkills("followerBigPipe"), CanSpawnForQuest = KnightQuestModule.CanSpawnGoon, CanJoinRoster = KnightQuestModule.CanJoinBigPipeRoster, RaidDeclineMessageKey = RogueAlliance.DeclineMessageKey, RaidRestriction = player => RogueAlliance.Restriction(player, "followerBigPipe") },
            new("followerBirdEye", "Birdeye", "#789B78", ProficiencyOnly: true, SeedForTesting: false, RaidReady: true)
                { PinnedRoster = true, HideRaidStats = true, GenerateEquipmentEachRaid = true, ProtectSpawnInventory = true, IgnoreEncounterKillPenalty = true, PrepareProfile = profiles.Apply, PrepareRaidEquipment = PrepareRaidEquipment, GeneratedSkillsVersion = "native-v1", AdoptGeneratedSkills = profiles.AdoptGeneratedSkills, GetVisibleSkills = () => profiles.VisibleSkills("followerBirdEye"), CanSpawnForQuest = KnightQuestModule.CanSpawnBirdeye, CanJoinRoster = KnightQuestModule.CanJoinBirdeyeRoster, RaidDeclineMessageKey = RogueAlliance.DeclineMessageKey, RaidRestriction = player => RogueAlliance.Restriction(player, "followerBirdEye") },
            new("exUsec", "", "#2D9C9A") { LockCombatSettings = true, PrepareProfile = RogueAlliance.PrepareMember, RaidDeclineMessageKey = RogueAlliance.DeclineMessageKey, RaidRestriction = player => RogueAlliance.Restriction(player, "exUsec"), RosterInvitationChance = RogueAlliance.RosterInvitation, PenalizeRecruitmentOnFriendlyKill = true }
        }.Select(companion => companion with { AddonRequired = AddonId }).ToArray());
        return Task.CompletedTask;
    }
}

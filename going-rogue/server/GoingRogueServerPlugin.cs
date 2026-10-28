using pitTeam.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace pitTeam.GoingRogue.Server;

public record GoingRogueMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "xyz.pit.fireteam.goingrogue";
    public string Name { get; init; } = "PitFireTeam Going Rogue";
    public string Author { get; init; } = "PitAlex";
    public List<string>? Contributors { get; init; }
    public Version Version { get; init; } = new("0.1.0");
    public Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; } = new() { ["xyz.pit.fireteam"] = new(">=1.0.1") };
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.Preload)]
public sealed class GoingRogueServerPlugin(GlobalTable globals, BotTable bots) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var profiles = new BossProfilePolicy(globals, bots);
        FriendlyGameplayModeAddons.Register("GoingRogue", [
            new("bossKnight", "Knight", "#667D91", ProficiencyOnly: true, SeedForTesting: true, RaidReady: true)
                { HideRaidStats = true, GenerateEquipmentEachRaid = true, ProtectSpawnInventory = true, IgnoreEncounterKillPenalty = true, PrepareProfile = profiles.Apply, GeneratedSkillsVersion = "native-v1", AdoptGeneratedSkills = profiles.AdoptGeneratedSkills, GetVisibleSkills = () => profiles.VisibleSkills("bossKnight") },
            new("followerBigPipe", "Big Pipe", "#C9A044", ProficiencyOnly: true, SeedForTesting: true, RaidReady: true)
                { HideRaidStats = true, GenerateEquipmentEachRaid = true, ProtectSpawnInventory = true, IgnoreEncounterKillPenalty = true, PrepareProfile = profiles.Apply, GeneratedSkillsVersion = "native-v1", AdoptGeneratedSkills = profiles.AdoptGeneratedSkills, GetVisibleSkills = () => profiles.VisibleSkills("followerBigPipe") },
            new("followerBirdEye", "Birdeye", "#789B78", ProficiencyOnly: true, SeedForTesting: true, RaidReady: true)
                { HideRaidStats = true, GenerateEquipmentEachRaid = true, ProtectSpawnInventory = true, IgnoreEncounterKillPenalty = true, PrepareProfile = profiles.Apply, GeneratedSkillsVersion = "native-v1", AdoptGeneratedSkills = profiles.AdoptGeneratedSkills, GetVisibleSkills = () => profiles.VisibleSkills("followerBirdEye") },
            new("exUsec", "", "#2D9C9A")
        ]);
        return Task.CompletedTask;
    }
}

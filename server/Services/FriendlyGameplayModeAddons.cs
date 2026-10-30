using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace pitTeam.Server.Services;

public sealed record FriendlyModeCompanion(string Role, string Nickname, string PortraitColor,
    bool ProficiencyOnly = false, bool SeedForTesting = false, bool RaidReady = true)
{
    public bool HideRaidStats { get; init; }
    public bool GenerateEquipmentEachRaid { get; init; }
    public bool ProtectSpawnInventory { get; init; }
    public bool IgnoreEncounterKillPenalty { get; init; }
    public Func<BotBase, bool>? PrepareProfile { get; init; }
    public Func<IReadOnlyList<string>>? GetVisibleSkills { get; init; }
    public string? GeneratedSkillsVersion { get; init; }
    public Action<BotBase, BotBase>? AdoptGeneratedSkills { get; init; }
    public Action<BotBase>? PrepareRaidEquipment { get; init; }
    public Func<BotBase, string, bool>? CanSpawnForQuest { get; init; }
}

public static class FriendlyGameplayModeAddons
{
    private static readonly Dictionary<string, IReadOnlyList<FriendlyModeCompanion>> Modes = new(StringComparer.Ordinal);
    public static void Register(string mode, IReadOnlyList<FriendlyModeCompanion> companions)
    {
        if (mode != pitTeam.Shared.GameplayModePolicy.GoingRogue) throw new ArgumentOutOfRangeException(nameof(mode));
        Modes.Add(mode, companions);
    }
    public static bool IsAvailable(string mode) => mode is "GunsForHire" or "Allegiance" || Modes.ContainsKey(mode);
    public static IReadOnlyList<FriendlyModeCompanion> Companions(string mode) => Modes.TryGetValue(mode, out var value) ? value : [];
    public static FriendlyModeCompanion? Policy(string mode, BotBase profile) => PolicyForRole(mode, profile.Info?.Settings?.Role);
    public static FriendlyModeCompanion? PolicyForRole(string mode, string? role) => Companions(mode)
        .FirstOrDefault(companion => string.Equals(companion.Role, role, StringComparison.OrdinalIgnoreCase));
}

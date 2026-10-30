using pitTeam.Server.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    // Route-gate owned, one loading attempt per player. Cache only equipment:
    // skills, health and roster identity are resolved again at actual spawn.
    private sealed record PreparedCompanionRaid(string Token, long ModeGeneration, string Configuration)
    {
        public Dictionary<string, BotBase> Kits { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> JoinRevisions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> RetiredRevisions { get; } = new(StringComparer.Ordinal);
    }
    private readonly Dictionary<string, PreparedCompanionRaid> preparedCompanionRaids = new(StringComparer.Ordinal);

    public void ClearCompanionRaidPreparation(MongoId sessionId) => preparedCompanionRaids.Remove(sessionId.ToString());

    public FriendlyCompanionRaidPreparationResponse PrepareCompanionRaid(MongoId sessionId, FriendlyCompanionRaidPreparationRequest request)
    {
        if (!Guid.TryParseExact(request.Token, "N", out _) || request.Members == null || request.Members.Count > 20 ||
            request.MemberRevisions == null || request.Members.Any(account => string.IsNullOrWhiteSpace(account) || !request.MemberRevisions.TryGetValue(account, out string? revision) || !Guid.TryParseExact(revision, "N", out _)) ||
            request.RaidConfiguration is not { Side: SideType.Pmc } configuration || string.IsNullOrWhiteSpace(configuration.Location))
            throw new InvalidOperationException("Invalid companion raid preparation");
        string configurationKey = jsonUtil.Serialize(configuration)!;
        string key = sessionId.ToString();
        if (!preparedCompanionRaids.TryGetValue(key, out var preparation) || preparation.Token != request.Token ||
            preparation.ModeGeneration != FriendlyModeRequestGate.Generation)
        {
            preparation = new(request.Token, FriendlyModeRequestGate.Generation, configurationKey);
            preparedCompanionRaids[key] = preparation;
        }
        if (preparation.Configuration != configurationKey) throw new InvalidOperationException("Prepared raid configuration changed");
        var members = request.Members.Distinct(StringComparer.Ordinal).ToArray();
        if (members.Any(account => preparation.RetiredRevisions.Contains(request.MemberRevisions[account])))
            throw new InvalidOperationException("Companion preparation belongs to a removed join");
        foreach (string removed in preparation.Kits.Keys.Except(members, StringComparer.Ordinal).ToArray())
        {
            preparation.Kits.Remove(removed);
            if (preparation.JoinRevisions.TryGetValue(removed, out string? retired)) preparation.RetiredRevisions.Add(retired);
            preparation.JoinRevisions.Remove(removed);
            logger.Info($"[CompanionRaidKit] released token={request.Token} aid={removed}");
        }
        foreach (string account in members)
        {
            if (!TryFindByAccountId(sessionId, account, out var teammate) || CompanionPolicy(teammate!)?.GenerateEquipmentEachRaid != true) continue;
            if (CompanionPolicy(teammate!)?.RaidReady == false) throw new InvalidOperationException("Companion is not raid ready");
            if (!preparation.Kits.ContainsKey(account) || !preparation.JoinRevisions.TryGetValue(account, out string? revision) || revision != request.MemberRevisions[account])
            {
                // A failed rejoin generation must not leave the previous join's
                // equipment available through the shared group token.
                preparation.Kits.Remove(account);
                if (preparation.JoinRevisions.TryGetValue(account, out string? retired)) preparation.RetiredRevisions.Add(retired);
                preparation.JoinRevisions.Remove(account);
                var generated = PrepareNativeCompanionForRaid(sessionId, teammate!, null, configuration);
                // Keep only the identity/role needed to validate this inventory's owner.
                preparation.Kits[account] = new BotBase
                {
                    Id = generated.Id, Aid = generated.Aid,
                    Info = new() { Settings = new() { Role = generated.Info!.Settings!.Role } },
                    Inventory = cloner.Clone(generated.Inventory)
                };
                preparation.JoinRevisions[account] = request.MemberRevisions[account];
                logger.Info($"[CompanionRaidKit] prepared token={request.Token} aid={account} equipment={generated.Inventory?.Equipment}");
            }
        }
        return new(request.Token, members.Where(preparation.Kits.ContainsKey).ToArray());
    }

    private BotBase PreparedCompanionForSpawn(MongoId sessionId, BotBase teammate, double? healthMultiplier, string token)
    {
        if (!preparedCompanionRaids.TryGetValue(sessionId.ToString(), out var preparation) || preparation.Token != token ||
            preparation.ModeGeneration != FriendlyModeRequestGate.Generation ||
            !preparation.Kits.TryGetValue(teammate.Aid!.Value.ToString(), out var kit) || kit.Id != teammate.Id ||
            kit.Info?.Settings?.Role != teammate.Info?.Settings?.Role)
            throw new InvalidOperationException("The displayed companion raid kit is unavailable; refusing to reroll it");
        var result = cloner.Clone(teammate) ?? throw new InvalidOperationException("Could not clone companion");
        result.Inventory = cloner.Clone(kit.Inventory) ?? throw new InvalidOperationException("Could not clone prepared companion inventory");
        ApplyTemporaryHealthMultiplier(result, healthMultiplier);
        foreach (var part in result.Health?.BodyParts?.Values.AsEnumerable() ?? []) part.Effects = null;
        logger.Info($"[CompanionRaidKit] spawn token={token} aid={teammate.Aid} equipment={result.Inventory.Equipment}");
        return result;
    }
}

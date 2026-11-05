using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    public void SendCompanionInvitationDecline(MongoId sessionId, UserDialogInfo sender, string message)
    {
        // Use native mail for both new and existing conversations. The installed
        // NotificationSendHelper's direct-chat path fails on existing dialogs.
        var dialogs = saveServer.GetProfile(sessionId).DialogueRecords ??= [];
        if (!dialogs.TryGetValue(sender.Id, out var dialog))
        {
            dialog = new Dialogue { Id = sender.Id, Type = MessageType.UserMessage, Messages = [],
                New = 0, AttachmentsNew = 0, Pinned = false, Users = [] };
            dialogs[sender.Id] = dialog;
        }
        dialog.Users ??= [];
        if (dialog.Users.All(user => user.Id != sender.Id)) dialog.Users.Add(sender);
        mailSendService.SendUserMessageToPlayer(sessionId, sender, message);
    }

    private string? CompanionRaidRestriction(MongoId sessionId, BotBase teammate) =>
        CompanionPolicy(teammate)?.RaidRestriction?.Invoke(GetPlayerProfile(sessionId));

    internal int? CompanionRosterInvitationChance(MongoId sessionId, pitTeam.Server.Models.FriendlyRecruitPickupCandidate candidate) =>
        TryDeserializeRecruitProfile(candidate, out var captured)
            ? CompanionPolicy(captured)?.RosterInvitationChance?.Invoke(GetPlayerProfile(sessionId)) : null;
    private bool IsCompanionRaidReady(BotBase teammate)
    {
        var policy = CompanionPolicy(teammate);
        string role = teammate.Info?.Settings?.Role?.ToLowerInvariant() ?? "";
        return role is "bossknight" or "followerbigpipe" or "followerbirdeye"
            ? policy?.RaidReady == true : policy?.RaidReady != false;
    }

    private bool HasRaidEquipmentOrGenerator(BotBase teammate) =>
        IsCompanionRaidReady(teammate) && (CompanionPolicy(teammate)?.GenerateEquipmentEachRaid == true || HasProperRaidKit(teammate));

    private BotBase PrepareNativeCompanionForRaid(MongoId sessionId, BotBase teammate, double? healthMultiplier,
        GetRaidConfigurationRequestData? configuration = null)
    {
        var player = GetPlayerProfile(sessionId);
        var generated = GenerateTeammateBot(sessionId, new BotGenerationDetails
        {
            IsPmc = false, Side = teammate.Info!.Side!, Role = teammate.Info.Settings!.Role!,
            PlayerLevel = Math.Max(1, player.Info?.Level ?? 1), PlayerName = player.Info?.Nickname,
            BotCountToGenerate = 1, BotDifficulty = "hard", IsPlayerScav = false,
            AllPmcsHaveSameNameAsPlayer = false,
            Location = configuration?.Location ?? profileActivityService.GetProfileActivityRaidData(sessionId).RaidConfiguration?.Location
                ?? TeammateGenerationLocation
        }, configuration);
        if (!HasProperRaidKit(generated))
            throw new InvalidOperationException($"Native companion generation returned no firearm: {teammate.Info.Settings.Role}");
        teammate = PrepareCompanionGeneratedSkills(sessionId, teammate, generated);
        var raidProfile = MergeGeneratedCompanionEquipment(teammate, generated, healthMultiplier);
        SaveCompanionSpawnPreview(sessionId, raidProfile);
        return raidProfile;
    }

    private static string? CompanionSkillsReceipt(BotBase profile, FriendlyModeCompanion? policy) =>
        policy?.AdoptGeneratedSkills == null || string.IsNullOrWhiteSpace(policy.GeneratedSkillsVersion)
            ? null : $"{profile.Aid}-{profile.Id}-companion-skills-{policy.GeneratedSkillsVersion}.json";

    private BotBase PrepareCompanionGeneratedSkills(MongoId sessionId, BotBase teammate, BotBase generated)
    {
        lock (RaidResultPersistenceLock)
        {
            var policy = CompanionPolicy(teammate);
            string? receipt = CompanionSkillsReceipt(teammate, policy);
            if (receipt == null) return teammate;
            if (!TryFindByAccountId(sessionId, teammate.Aid?.ToString(), out var saved) || saved == null ||
                saved.Id != teammate.Id ||
                !string.Equals(saved.Info?.Settings?.Role, teammate.Info?.Settings?.Role, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(saved.Info?.Settings?.Role, generated.Info?.Settings?.Role, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Could not match companion native skills to its saved profile");
            if (storage.Exists(sessionId, receipt)) return saved;
            policy!.AdoptGeneratedSkills!(saved, generated);
            // The receipt and corrected skills commit atomically. Later raid kit generation
            // therefore preserves normal learned progress instead of rerolling skill values.
            SaveTeammate(sessionId, saved, new Dictionary<string, string> { [receipt] = "{\"initialized\":true}" });
            return saved;
        }
    }

    private void SaveCompanionSpawnPreview(MongoId sessionId, BotBase raidProfile)
    {
        lock (RaidResultPersistenceLock)
        {
            // Re-read under the same lock as progress/outcomes. Only the generated inventory
            // changes; recovered HP, skills, identity and saved combat preferences stay current.
            if (!TryFindByAccountId(sessionId, raidProfile.Aid?.ToString(), out var saved) || saved == null ||
                saved.Id != raidProfile.Id || CompanionPolicy(saved)?.GenerateEquipmentEachRaid != true ||
                !string.Equals(saved.Info?.Settings?.Role, raidProfile.Info?.Settings?.Role, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Could not match companion spawn preview to its saved profile");
            saved.Inventory = cloner.Clone(raidProfile.Inventory)
                ?? throw new InvalidOperationException("Could not clone companion spawn preview");
            SaveTeammate(sessionId, saved);
        }
    }

    // Preserve the roster identity, boss skills and recovered HP. Only native raid
    // equipment is adopted; neither the preview nor the generated template is mutated.
    private BotBase MergeGeneratedCompanionEquipment(BotBase teammate, BotBase generated, double? healthMultiplier)
    {
        if (!string.Equals(teammate.Info?.Settings?.Role, generated.Info?.Settings?.Role, StringComparison.OrdinalIgnoreCase)
            || generated.Inventory?.Items == null || generated.Inventory.Items.Count == 0)
            throw new InvalidOperationException("Invalid native companion equipment profile");
        var clone = cloner.Clone(teammate) ?? throw new InvalidOperationException("Could not clone companion profile");
        clone.Inventory = cloner.Clone(generated.Inventory) ?? throw new InvalidOperationException("Could not clone companion equipment");
        // Fresh native companions receive the same managed reserves in every mode,
        // including Realistic. Replace any generated container only on this raid clone.
        RemoveSecureContainerTree(clone);
        EnsureManagedSecureContainer(clone);
        EnsureFollowerHasSecureContainerSupplies(clone);
        CompanionPolicy(clone)?.PrepareRaidEquipment?.Invoke(clone);
        ApplyTemporaryHealthMultiplier(clone, healthMultiplier);
        foreach (var part in clone.Health?.BodyParts?.Values.AsEnumerable() ?? []) part.Effects = null;
        return clone;
    }
}

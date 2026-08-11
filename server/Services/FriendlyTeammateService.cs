using pitTeam.Server.Models;
using pitTeam.Server.Constants;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Constants;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Dialog;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Eft.Repair;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Enums.RaidSettings;
using SPTarkov.Server.Core.Models.Spt.Dialog;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using CommonCustomization = SPTarkov.Server.Core.Models.Eft.Common.Tables.Customization;
using CommonInfo = SPTarkov.Server.Core.Models.Eft.Common.Tables.Info;
using System;
using System.Text.Json;

namespace pitTeam.Server.Services;

[Injectable]
public class FriendlyTeammateService(
    BotGenerator botGenerator,
    DatabaseService databaseService,
    FileUtil fileUtil,
    HashUtil hashUtil,
    JsonUtil jsonUtil,
    ItemHelper itemHelper,
    MailSendService mailSendService,
    ProfileHelper profileHelper,
    ProfileActivityService profileActivityService,
    RepairService repairService,
    FriendlyServerSettingsService settingsService,
    FriendlyLanguageService languageService,
    SaveServer saveServer,
    ICloner cloner,
    ISptLogger<FriendlyTeammateService> logger
)
{
    private const string ProfileRecoveryMessage =
        "The profile of this teammate has been recovered from a bad state. Some items from his inventory may have been deleted in the process.";
    private const string DuplicateProfileRecoveryTitleFallback = "Profile recovered";
    private const string DuplicateProfileRecoveryBodyFallback =
        "Duplicate items were found in both player and teammate profiles. The following teammate profiles have been stripped of the duplicate in order to safely recover them: {0}";
    private const string LockedStashItemBlockedFallback =
        "Teammate loadout save blocked: an item inside a locked stash container was moved or changed. Unlock the container and try again. Locked container: id={0}, tpl={1}. Blocked item: id={2}, tpl={3}.";

    private readonly Dictionary<string, FriendlyTeammateProfileRecoveryNotice> profileRecoveryNotices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FriendlyTeammateStartupRecoveryNotice> startupRecoveryNotices = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> duplicateRecoveryCheckedSessions = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] WeaponSkillNames =
    [
        "Pistol",
        "Revolver",
        "SMG",
        "Assault",
        "Shotgun",
        "Sniper",
        "LMG",
    ];

    private const string ModFolderName = "pitFireTeam-ServerMod";
    private const string TeammateFolderName = "teammates";
    private const string DefaultLoadoutName = "Default";
    private const string DefaultLoadoutId = "000000000000000000000000";
    private static readonly string[] TacticOptions = ["Rifleman", "Marksman"];
    private const int RelativeLevelDelta = 5;
    private const int SecureContainerAmmoStackCount = 10;
    private const string TeammateGenerationLocation = "factory4_day";
    private const double DeathEscapeMinChance = 0.05d;
    private const double DeathEscapeMaxChance = 0.90d;
    private const double DeathEscapeCloseExtractDistance = 150d;
    private const double DeathEscapeFarExtractDistance = 900d;
    private static readonly Random DeathEscapeRandom = new();
    private static readonly object DeathEscapeRandomLock = new();
    private static readonly object RaidResultPersistenceLock = new();
    private static readonly string[] RequiredRaidWeaponSlots =
    [
        nameof(EquipmentSlots.FirstPrimaryWeapon),
        nameof(EquipmentSlots.SecondPrimaryWeapon),
        nameof(EquipmentSlots.Holster),
    ];

    private static readonly HashSet<string> LoadedAmmoSlotIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "cartridges",
    };

    public void RecoverDuplicateTeammateItemsForAllProfiles()
    {
        try
        {
            foreach (var profileEntry in saveServer.GetProfiles())
            {
                string? sessionIdValue = profileEntry.Key.ToString();
                if (string.IsNullOrWhiteSpace(sessionIdValue))
                {
                    continue;
                }

                try
                {
                    RecoverDuplicateTeammateItemsForSession(new MongoId(sessionIdValue));
                }
                catch (Exception ex)
                {
                    logger.Warning($"{nameof(FriendlyTeammateService)}: failed duplicate item startup recovery for session '{sessionIdValue}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"{nameof(FriendlyTeammateService)}: failed duplicate item startup recovery: {ex.Message}");
        }
    }

    public FriendlyTeammateStartupRecoveryNotice GetStartupRecoveryNotice(MongoId sessionId)
    {
        RecoverDuplicateTeammateItemsForSession(sessionId);

        string key = sessionId.ToString();
        if (!startupRecoveryNotices.TryGetValue(key, out var notice))
        {
            return new FriendlyTeammateStartupRecoveryNotice();
        }

        LocalizeStartupRecoveryNotice(sessionId, notice);
        return notice;
    }

    public void AcknowledgeStartupRecoveryNotice(MongoId sessionId)
    {
        string key = sessionId.ToString();
        startupRecoveryNotices.Remove(key);
    }

    private static readonly HashSet<string> SurgicalKitTemplateIds =
    [
        FriendlyItemTemplateIds.Medical.Surv12SurgicalKit,
        FriendlyItemTemplateIds.Medical.CmsSurgicalKit,
    ];

    private sealed class AvailableAmmoStack(Item item, int remaining)
    {
        public Item Item { get; } = item;
        public int Remaining { get; set; } = remaining;
    }

    public SearchFriendResponse CreateTeammate(MongoId sessionId, FriendlyTeammateCreateRequest request)
    {
        var playerPmc = GetPlayerProfile(sessionId);
        var nickname = NormalizeRequiredValue(request.Nickname, "nickname");
        var voice = NormalizeRequiredValue(request.Voice, "voice");
        var head = NormalizeRequiredValue(request.Head, "head");

        EnsureNicknameIsUnique(sessionId, nickname);

        var teammate = GenerateTeammateBot(
            sessionId,
            new BotGenerationDetails
            {
                IsPmc = true,
                Side = playerPmc.Info!.Side!,
                Role = GetPmcRole(playerPmc.Info.Side),
                PlayerLevel = Math.Max(1, playerPmc.Info.Level ?? 1),
                PlayerName = playerPmc.Info.Nickname,
                BotRelativeLevelDeltaMax = RelativeLevelDelta,
                BotRelativeLevelDeltaMin = RelativeLevelDelta,
                BotCountToGenerate = 1,
                BotDifficulty = "hard",
                Location = TeammateGenerationLocation,
                LocationSpecificPmcLevelOverride = new MinMax<int>
                {
                    Min = Math.Max(1, (playerPmc.Info.Level ?? 1) - RelativeLevelDelta),
                    Max = Math.Min(100, (playerPmc.Info.Level ?? 1) + RelativeLevelDelta),
                },
                IsPlayerScav = false,
                AllPmcsHaveSameNameAsPlayer = false,
            }
        );

        teammate.Info!.Nickname = nickname;
        teammate.Info.LowerNickname = nickname.ToLowerInvariant();
        teammate.Customization!.Voice = voice;
        teammate.Customization.Head = head;
        teammate.Aid = GetUniqueAccountId(sessionId);

        NormalizeTeammateProfile(teammate, playerPmc);
        NormalizeTeammateSkillsForCreation(teammate, playerPmc);
        // Temporarily disabled: this floor baseline can over-inflate teammate skills.
        // ApplyPmcFollowerSkillBaseline(teammate);
        PrepareNewTeammateDefaultForCurrentLoadoutMode(teammate);
        SaveDefaultEquipmentSnapshot(sessionId, teammate, overwrite: true, includeSecureContainer: IsCurrentLoadoutManagementModeExtreme());
        SaveTeammate(sessionId, teammate);
        SaveTeammateSettings(sessionId, teammate, CreateDefaultTeammateSettings());

        logger.Info($"Created teammate '{nickname}' for session '{sessionId}' with aid '{teammate.Aid}'");

        return ToFriendSummary(teammate);
    }

    public SearchFriendResponse CreateTeammateFromRecruitCandidate(MongoId sessionId, FriendlyRecruitPickupCandidate candidate)
    {
        var playerPmc = GetPlayerProfile(sessionId);
        var nickname = EnsureUniqueRecruitNickname(sessionId, NormalizeRequiredValue(candidate.Nickname, "nickname"));
        var voice = NormalizeRequiredValue(candidate.Voice, "voice");
        var head = NormalizeRequiredValue(candidate.Head, "head");
        var targetLevel = Math.Max(1, candidate.Level);

        bool usedCapturedProfile = TryDeserializeRecruitProfile(candidate, out var teammate);
        if (!usedCapturedProfile)
        {
            teammate = GenerateTeammateBot(
                sessionId,
                new BotGenerationDetails
                {
                    IsPmc = true,
                    Side = playerPmc.Info!.Side!,
                    Role = GetPmcRole(playerPmc.Info.Side),
                    PlayerLevel = targetLevel,
                    PlayerName = playerPmc.Info.Nickname,
                    BotRelativeLevelDeltaMax = 0,
                    BotRelativeLevelDeltaMin = 0,
                    BotCountToGenerate = 1,
                    BotDifficulty = "hard",
                    Location = TeammateGenerationLocation,
                    LocationSpecificPmcLevelOverride = new MinMax<int>
                    {
                        Min = targetLevel,
                        Max = targetLevel,
                    },
                    IsPlayerScav = false,
                    AllPmcsHaveSameNameAsPlayer = false,
                }
            );
        }

        teammate.Info ??= new CommonInfo();
        teammate.Customization ??= new CommonCustomization();
        teammate.Info.Nickname = nickname;
        teammate.Info.LowerNickname = nickname.ToLowerInvariant();
        teammate.Customization.Voice = voice;
        teammate.Customization.Head = head;
        teammate.Aid = GetRecruitAccountIdOrUnique(sessionId, candidate.AccountId);

        if (usedCapturedProfile)
        {
            NormalizeCapturedRecruitProfile(teammate, playerPmc, candidate);
        }
        else
        {
            NormalizeTeammateProfile(teammate, playerPmc);
        }

        NormalizeTeammateSkillsForCreation(teammate, playerPmc);
        InitializeRecruitRaidStats(teammate, targetLevel, GetRecruitStatsSeed(candidate));
        PrepareNewTeammateDefaultForCurrentLoadoutMode(teammate);
        SaveDefaultEquipmentSnapshot(sessionId, teammate, overwrite: true, includeSecureContainer: IsCurrentLoadoutManagementModeExtreme());
        SaveTeammate(sessionId, teammate);
        SaveTeammateSettings(sessionId, teammate, CreateDefaultTeammateSettings());

        logger.Info($"Accepted recruit pickup '{nickname}' for session '{sessionId}' with aid '{teammate.Aid}' capturedProfile={usedCapturedProfile}");

        return ToFriendSummary(teammate);
    }

    public bool TryGetRecruitCandidateProfile(MongoId sessionId, FriendlyRecruitPickupCandidate candidate, out GetOtherProfileResponse? profile)
    {
        profile = null;
        if (!TryDeserializeRecruitProfile(candidate, out var recruit))
        {
            return false;
        }

        var playerPmc = GetPlayerProfile(sessionId);
        recruit.Info ??= new CommonInfo();
        recruit.Customization ??= new CommonCustomization();

        if (!string.IsNullOrWhiteSpace(candidate.Nickname))
        {
            recruit.Info.Nickname = candidate.Nickname.Trim();
            recruit.Info.LowerNickname = recruit.Info.Nickname.ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(candidate.Voice))
        {
            recruit.Customization.Voice = candidate.Voice.Trim();
        }

        if (!string.IsNullOrWhiteSpace(candidate.Head))
        {
            recruit.Customization.Head = candidate.Head.Trim();
        }

        if (int.TryParse(candidate.AccountId, out var aid) && aid > 0)
        {
            recruit.Aid = aid;
        }

        NormalizeCapturedRecruitProfile(recruit, playerPmc, candidate);
        NormalizeTeammateSkillsForCreation(recruit, playerPmc);
        InitializeRecruitRaidStats(recruit, Math.Max(1, candidate.Level), GetRecruitStatsSeed(candidate));
        profile = ToOtherProfileResponse(recruit);

        // Pending recruits are bot profiles, not player hideouts. Let the stock other-profile
        // screen render normally while suppressing the View Hideout action.
        profile.Hideout = null!;
        profile.HideoutAreaStashes = [];
        profile.CustomizationStash = string.Empty;
        return true;
    }

    private bool TryDeserializeRecruitProfile(FriendlyRecruitPickupCandidate candidate, out BotBase teammate)
    {
        teammate = null!;
        if (string.IsNullOrWhiteSpace(candidate.ProfileJson))
        {
            return false;
        }

        try
        {
            var deserialized = jsonUtil.Deserialize<BotBase>(candidate.ProfileJson);
            if (deserialized?.Info == null)
            {
                return false;
            }

            teammate = deserialized;
            return true;
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to deserialize captured recruit profile '{candidate.ProfileId}'; falling back to generated recruit profile: {ex.Message}");
            teammate = null!;
            return false;
        }
    }

    private void NormalizeCapturedRecruitProfile(BotBase teammate, PmcData playerPmc, FriendlyRecruitPickupCandidate candidate)
    {
        teammate.Info ??= new CommonInfo();
        teammate.Customization ??= new CommonCustomization();
        teammate.Inventory ??= new BotBaseInventory { Items = [] };
        teammate.Inventory.Items ??= [];
        teammate.Stats ??= new Stats();
        teammate.Stats.Eft ??= new EftStats();
        teammate.Stats.Eft.OverallCounters ??= new OverallCounters { Items = [] };
        teammate.Stats.Eft.OverallCounters.Items ??= [];
        teammate.Hideout ??= new Hideout();
        teammate.Inventory.HideoutAreaStashes ??= [];

        if (teammate.Id == null && !string.IsNullOrWhiteSpace(candidate.ProfileId))
        {
            teammate.Id = new MongoId(candidate.ProfileId);
        }

        teammate.Info.Side ??= playerPmc.Info?.Side;
        teammate.Info.LowerNickname = teammate.Info.Nickname?.ToLowerInvariant();
        teammate.Info.MemberCategory = MemberCategory.Unheard;
        teammate.Info.SelectedMemberCategory = MemberCategory.Unheard;
        teammate.Info.BannedState = playerPmc.Info?.BannedState;
        teammate.Info.BannedUntil = playerPmc.Info?.BannedUntil;
    }

    public List<object> ListTeammates(MongoId sessionId)
    {
        return LoadTeammates(sessionId)
            .Select(teammate => ToTeammateSummary(teammate, GetTeammateSettings(sessionId, teammate)))
            .ToList();
    }

    public HashSet<string> GetProtectedTeammateItemIdsForExtraction(MongoId sessionId)
    {
        string mode = NormalizeLoadoutManagementMode(settingsService.LoadSettings().LoadoutManagementMode);
        if (IsImmersiveLikeLoadoutManagementMode(mode))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        HashSet<string> itemIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (HashSet<string> teammateItemIds in BuildProtectedTeammateItemIdsByAid(sessionId).Values)
        {
            itemIds.UnionWith(teammateItemIds);
        }

        return itemIds;
    }

    public HashSet<string> GetProtectedSpawnItemIdsForExtraction(BotBase? profile)
    {
        string mode = NormalizeLoadoutManagementMode(settingsService.LoadSettings().LoadoutManagementMode);
        if (IsImmersiveLikeLoadoutManagementMode(mode))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        List<Item>? items = profile?.Inventory?.Items;
        if (items == null || items.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        string? equipmentRootId = profile?.Inventory?.Equipment?.ToString();
        if (string.IsNullOrWhiteSpace(equipmentRootId))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        HashSet<string> protectedIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (Item rootItem in items.Where(item =>
                     item?.Id != null &&
                     string.Equals(item.ParentId, equipmentRootId, StringComparison.OrdinalIgnoreCase) &&
                     !IsIgnoredSpawnProtectionSlot(item.SlotId)))
        {
            protectedIds.UnionWith(GetItemTreeIds(items, rootItem.Id.ToString()));
        }

        return protectedIds;
    }

    private Dictionary<string, HashSet<string>> BuildProtectedTeammateItemIdsByAid(MongoId sessionId)
    {
        var itemIdsByAid = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (BotBase teammate in LoadTeammates(sessionId))
        {
            string aid = teammate.Aid?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(aid))
            {
                continue;
            }

            HashSet<string> itemIds = GetProtectedTeammateItemIds(teammate);
            if (itemIds.Count > 0)
            {
                itemIdsByAid[aid] = itemIds;
            }
        }

        return itemIdsByAid;
    }

    private static HashSet<string> GetProtectedTeammateItemIds(BotBase? teammate)
    {
        HashSet<string> itemIds = new(StringComparer.OrdinalIgnoreCase);
        List<Item>? items = teammate?.Inventory?.Items;
        if (items == null || items.Count == 0)
        {
            return itemIds;
        }

        string? equipmentRootId = teammate?.Inventory?.Equipment?.ToString();
        foreach (Item item in items)
        {
            if (item?.Id == null ||
                string.Equals(item.Id.ToString(), equipmentRootId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            itemIds.Add(item.Id.ToString());
        }

        return itemIds;
    }

    private static HashSet<string> GetOtherProtectedTeammateItemIds(
        Dictionary<string, HashSet<string>> protectedItemIdsByAid,
        string? currentAid)
    {
        HashSet<string> itemIds = new(StringComparer.OrdinalIgnoreCase);
        if (protectedItemIdsByAid == null || protectedItemIdsByAid.Count == 0)
        {
            return itemIds;
        }

        foreach (KeyValuePair<string, HashSet<string>> pair in protectedItemIdsByAid)
        {
            if (!string.IsNullOrWhiteSpace(currentAid) &&
                string.Equals(pair.Key, currentAid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            itemIds.UnionWith(pair.Value);
        }

        return itemIds;
    }

    private static bool IsIgnoredSpawnProtectionSlot(string? slotId)
    {
        return !string.IsNullOrWhiteSpace(slotId)
            && (slotId.Contains("Dogtag", StringComparison.OrdinalIgnoreCase)
                || slotId.Contains("SpecialSlot", StringComparison.OrdinalIgnoreCase)
                || slotId.Contains("ArmBand", StringComparison.OrdinalIgnoreCase)
                || slotId.Contains("Armband", StringComparison.OrdinalIgnoreCase)
                || string.Equals(slotId, nameof(EquipmentSlots.Scabbard), StringComparison.OrdinalIgnoreCase));
    }

    public void LogLoadoutManagementModeChange(MongoId sessionId, string previousMode, string nextMode)
    {
        logger.Info($"Loadout management mode changed for session '{sessionId}' from '{previousMode}' to '{nextMode}'.");
    }

    public void SelectDefaultLoadoutForAllTeammates(MongoId sessionId, string? previousMode = null, string? nextMode = null)
    {
        var teammates = LoadTeammates(sessionId);
        string normalizedPreviousMode = NormalizeLoadoutManagementMode(previousMode);
        string normalizedNextMode = NormalizeLoadoutManagementMode(nextMode);
        bool crossedRealisticBoundary = IsExtremeLoadoutManagementMode(normalizedPreviousMode)
            || IsExtremeLoadoutManagementMode(normalizedNextMode);
        bool shouldSelectDefault = IsSimpleLoadoutManagementMode(normalizedPreviousMode)
            && !IsSimpleLoadoutManagementMode(normalizedNextMode);

        foreach (var teammate in teammates)
        {
            try
            {
                if (crossedRealisticBoundary && RemoveSecureContainerTree(teammate))
                {
                    SaveDefaultEquipmentSnapshot(sessionId, teammate, overwrite: true, includeSecureContainer: true);
                    SaveTeammate(sessionId, teammate);
                    logger.Info($"Removed secure container from teammate '{teammate.Aid}' default loadout after Realistic boundary switch.");
                }

                if (shouldSelectDefault)
                {
                    var settings = GetTeammateSettings(sessionId, teammate);
                    settings.SelectedLoadoutId = DefaultLoadoutId;

                    SaveTeammateSettings(sessionId, teammate, settings);
                    logger.Info($"Selected existing Default loadout for teammate '{teammate.Aid}' after leaving Simple loadout management.");
                }
            }
            catch (Exception ex)
            {
                logger.Warning($"Failed to apply loadout management mode change for teammate '{teammate.Aid}': {ex.Message}");
            }
        }
    }

    public List<string> GetAutoJoinTeammateAccountIds(MongoId sessionId)
    {
        var accountIds = new List<string>();
        foreach (var teammate in LoadTeammates(sessionId))
        {
            if (!GetTeammateSettings(sessionId, teammate).AutoJoinEnabled)
            {
                continue;
            }

            var preparedTeammate = PrepareTeammateForFetch(teammate);
            if (!HasProperRaidKit(preparedTeammate))
            {
                logger.Info($"Skipped auto-join for teammate '{GetTeammateDisplayName(preparedTeammate)}' because their Default loadout has no primary or pistol weapon.");
                continue;
            }

            var aid = teammate.Aid?.ToString();
            if (!string.IsNullOrWhiteSpace(aid))
            {
                accountIds.Add(aid);
            }
        }

        return accountIds;
    }

    private void PrepareNewTeammateDefaultForCurrentLoadoutMode(BotBase teammate)
    {
        string mode = NormalizeLoadoutManagementMode(settingsService.LoadSettings().LoadoutManagementMode);
        if (!IsExtremeLoadoutManagementMode(mode))
        {
            return;
        }

        AssignInitialRealisticSecureContainer(teammate);
    }

    private void AssignInitialRealisticSecureContainer(BotBase teammate)
    {
        teammate.Inventory ??= new BotBaseInventory { Items = [] };
        teammate.Inventory.Items ??= [];
        if (teammate.Inventory.Items.Count == 0)
        {
            return;
        }

        RemoveSecureContainerTree(teammate);

        int level = Math.Max(1, teammate.Info?.Level ?? 1);
        string templateId = level < 15
            ? FriendlyItemTemplateIds.SecureContainer.Beta
            : level < 30
                ? FriendlyItemTemplateIds.SecureContainer.Epsilon
                : FriendlyItemTemplateIds.SecureContainer.Gamma;

        teammate.Inventory.Items.Add(new Item
        {
            Id = new MongoId(),
            Template = new MongoId(templateId),
            ParentId = GetEquipmentRootId(teammate),
            SlotId = nameof(EquipmentSlots.SecuredContainer),
            Location = null,
            Upd = new Upd
            {
                StackObjectsCount = 1,
                SpawnedInSession = false,
            },
        });

        logger.Info($"Assigned initial Realistic secure container '{templateId}' to teammate '{teammate.Aid}' at level {level}.");
    }

    private static bool RemoveSecureContainerTree(BotBase teammate)
    {
        return RemoveSecureContainerTree(teammate?.Inventory?.Items);
    }

    private static bool RemoveSecureContainerTree(List<Item>? inventoryItems)
    {
        if (inventoryItems == null || inventoryItems.Count == 0)
        {
            return false;
        }

        Item? secureContainer = inventoryItems.FirstOrDefault(item =>
            string.Equals(item?.SlotId, nameof(EquipmentSlots.SecuredContainer), StringComparison.OrdinalIgnoreCase));
        if (secureContainer?.Id == null)
        {
            return false;
        }

        var removeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { secureContainer.Id.ToString() };
        bool foundChild = true;
        while (foundChild)
        {
            foundChild = false;
            foreach (var item in inventoryItems)
            {
                if (item?.Id == null || string.IsNullOrWhiteSpace(item.ParentId) || !removeIds.Contains(item.ParentId))
                {
                    continue;
                }

                if (removeIds.Add(item.Id.ToString()))
                {
                    foundChild = true;
                }
            }
        }

        return inventoryItems.RemoveAll(item => item?.Id != null && removeIds.Contains(item.Id.ToString())) > 0;
    }

    private static bool RemoveItemTreesById(List<Item>? inventoryItems, IEnumerable<string>? rootItemIds)
    {
        if (inventoryItems == null || inventoryItems.Count == 0 || rootItemIds == null)
        {
            return false;
        }

        var removeIds = rootItemIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (removeIds.Count == 0)
        {
            return false;
        }

        bool foundChild = true;
        while (foundChild)
        {
            foundChild = false;
            foreach (var item in inventoryItems)
            {
                if (item?.Id == null || string.IsNullOrWhiteSpace(item.ParentId) || !removeIds.Contains(item.ParentId))
                {
                    continue;
                }

                if (removeIds.Add(item.Id.ToString()))
                {
                    foundChild = true;
                }
            }
        }

        return inventoryItems.RemoveAll(item => item?.Id != null && removeIds.Contains(item.Id.ToString())) > 0;
    }

    private BotBase GenerateTeammateBot(MongoId sessionId, BotGenerationDetails details)
    {
        var raidData = profileActivityService.GetProfileActivityRaidData(sessionId);
        var previousRaidConfiguration = raidData.RaidConfiguration;
        var createdTemporaryRaidConfiguration = previousRaidConfiguration == null;

        if (createdTemporaryRaidConfiguration)
        {
            raidData.RaidConfiguration = CreateMenuTeammateGenerationRaidConfiguration();
        }

        try
        {
            return botGenerator.PrepareAndGenerateBot(sessionId, details);
        }
        finally
        {
            if (createdTemporaryRaidConfiguration)
            {
                raidData.RaidConfiguration = previousRaidConfiguration;
            }
        }
    }

    private static GetRaidConfigurationRequestData CreateMenuTeammateGenerationRaidConfiguration()
    {
        return new GetRaidConfigurationRequestData
        {
            Location = TeammateGenerationLocation,
            TimeVariant = DateTimeEnum.CURR,
            IsNightRaid = false,
            RaidMode = RaidMode.Local,
            Side = SideType.Pmc,
            PlayersSpawnPlace = PlayersSpawnPlace.SamePlace,
            TransitionType = TransitionType.NONE,
            WavesSettings = new WavesSettings
            {
                BotAmount = BotAmount.AsOnline,
                BotDifficulty = BotDifficulty.Hard,
                IsBosses = true,
                IsTaggedAndCursed = false,
            },
            BotSettings = new BotSettings
            {
                BotAmount = BotAmount.AsOnline,
                IsScavWars = false,
            },
            TimeAndWeatherSettings = new TimeAndWeatherSettings
            {
                IsRandomTime = false,
                IsRandomWeather = false,
            },
            IsLocationTransition = false,
            CanShowGroupPreview = false,
            OnlinePveRaidStates = [],
        };
    }

    public List<FriendlyTeammateFollowerDetailsResponse> ListFollowerDetails(MongoId sessionId)
    {
        var fullProfile = profileHelper.GetFullProfile(sessionId);
        var loadoutNames = GetCustomEquipmentBuilds(fullProfile)
            .ToDictionary(build => build.Id.ToString(), build => build.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        return LoadTeammates(sessionId)
            .Select(teammate =>
            {
                var settings = GetTeammateSettings(sessionId, teammate);
                var loadoutId = NormalizeCurrentLoadoutId(fullProfile, settings.SelectedLoadoutId);
                var equipmentName = string.Equals(loadoutId, DefaultLoadoutId, StringComparison.OrdinalIgnoreCase)
                    ? DefaultLoadoutName
                    : loadoutNames.TryGetValue(loadoutId, out var customName)
                        ? customName
                        : DefaultLoadoutName;

                return new FriendlyTeammateFollowerDetailsResponse
                {
                    Aid = teammate.Aid?.ToString() ?? string.Empty,
                    Tactic = NormalizeCombatTactic(settings.CombatTactic),
                    Aggression = NormalizeAggression(settings.Aggression),
                    Equipment = equipmentName,
                    Voice = teammate.Customization?.Voice?.ToString() ?? string.Empty,
                    Head = teammate.Customization?.Head?.ToString() ?? string.Empty,
                };
            })
            .ToList();
    }

    public List<UserDialogInfo> ListTeammateDialogs(MongoId sessionId)
    {
        return LoadTeammates(sessionId).Select(ToFriendDialog).ToList();
    }

    public GetOtherProfileResponse GetTeammateProfile(MongoId sessionId, GetOtherProfileRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.AccountId);
        return ToOtherProfileResponse(PrepareTeammateForFetch(teammate));
    }

    public FriendlyTeammateProfileOptionsResponse GetProfileOptions(MongoId sessionId, FriendlyTeammateProfileOptionsRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.Aid);
        var profile = profileHelper.GetFullProfile(sessionId);
        var selectedLoadoutId = NormalizeCurrentLoadoutId(profile, GetTeammateSettings(sessionId, teammate).SelectedLoadoutId);

        var response = new FriendlyTeammateProfileOptionsResponse
        {
            CurrentLoadoutId = selectedLoadoutId,
            CurrentTactic = NormalizeCombatTactic(GetTeammateSettings(sessionId, teammate).CombatTactic),
            Aggression = NormalizeAggression(GetTeammateSettings(sessionId, teammate).Aggression),
            RecoveryNotice = ConsumeProfileRecoveryNotice(sessionId, teammate),
            Loadouts =
            [
                new FriendlyTeammateLoadoutOption
                {
                    Id = DefaultLoadoutId,
                    Name = DefaultLoadoutName,
                },
            ],
            Tactics = TacticOptions
                .Select(tactic => new FriendlyTeammateTacticOption
                {
                    Id = tactic,
                    Name = tactic,
                })
                .ToList(),
        };

        foreach (var build in GetCustomEquipmentBuilds(profile))
        {
            response.Loadouts.Add(
                new FriendlyTeammateLoadoutOption
                {
                    Id = build.Id.ToString(),
                    Name = build.Name ?? string.Empty,
                }
            );
        }

        return response;
    }

    public void SetTeammateSuit(MongoId sessionId, FriendlyTeammateSuitRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.Aid);
        if (request.Suit == null || request.Suit.Length < 2)
        {
            throw new FriendlyTeammateException("Missing teammate suit values");
        }

        teammate.Customization ??= new CommonCustomization();
        teammate.Customization.Body = NormalizeRequiredValue(request.Suit[0], "body");
        teammate.Customization.Feet = NormalizeRequiredValue(request.Suit[1], "feet");

        SaveTeammate(sessionId, teammate);
    }

    public void RenameTeammate(MongoId sessionId, FriendlyTeammateRenameRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.Aid);
        var nickname = NormalizeRequiredValue(request.Nickname, "nickname");

        EnsureNicknameIsUnique(sessionId, nickname, teammate.Aid);

        teammate.Info ??= new CommonInfo();
        teammate.Info.Nickname = nickname;
        teammate.Info.LowerNickname = nickname.ToLowerInvariant();

        SaveTeammate(sessionId, teammate);
    }

    public void SetTeammateLoadout(MongoId sessionId, FriendlyTeammateLoadoutRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.Aid);
        var selectedLoadoutId = NormalizeRequiredValue(request.LoadoutId, "loadoutId");
        var settings = GetTeammateSettings(sessionId, teammate);

        if (string.Equals(selectedLoadoutId, DefaultLoadoutId, StringComparison.OrdinalIgnoreCase))
        {
            RestoreDefaultEquipment(sessionId, teammate);
            settings.SelectedLoadoutId = DefaultLoadoutId;
            SaveTeammateSettings(sessionId, teammate, settings);
            SaveTeammate(sessionId, teammate);
            return;
        }

        var fullProfile = profileHelper.GetFullProfile(sessionId);
        var playerPmc = GetPlayerProfile(sessionId);
        var equipmentBuild = GetCustomEquipmentBuilds(fullProfile)
            .FirstOrDefault(build => string.Equals(build.Id.ToString(), selectedLoadoutId, StringComparison.OrdinalIgnoreCase));

        if (equipmentBuild == null)
        {
            throw new FriendlyTeammateException($"Unable to find teammate equipment build '{selectedLoadoutId}'");
        }

        ApplyEquipmentBuild(teammate, equipmentBuild, playerPmc);
        settings.SelectedLoadoutId = selectedLoadoutId;
        SaveTeammateSettings(sessionId, teammate, settings);
        SaveTeammate(sessionId, teammate);
    }

    public FriendlyTeammateDefaultEquipmentResponse SaveTeammateDefaultEquipment(MongoId sessionId, FriendlyTeammateDefaultEquipmentRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.Aid);
        var items = request.Items?.Where(item => item != null).ToList();
        if (items == null || items.Count == 0)
        {
            throw new FriendlyTeammateException("Missing teammate default equipment items");
        }

        string mode = NormalizeLoadoutManagementMode(settingsService.LoadSettings().LoadoutManagementMode);
        if (request.RealItemCommit && IsRealTransferLoadoutManagementMode(mode))
        {
            // Restricted/Immersive/Extreme Default edits are real ownership transfers. Keep the normal
            // clone-save path for Simple and for future non-default loadout modes until those rules are explicit.
            return SaveTeammateDefaultEquipmentWithRealItemCommit(sessionId, teammate, request, items, mode);
        }

        teammate.Inventory ??= new BotBaseInventory();
        var mergedItems = MergeEquipmentWithPreservedSpecialItems(teammate.Inventory.Items, cloner.Clone(items) ?? items);
        teammate.Inventory.Items = mergedItems;
        teammate.Inventory.Equipment = teammate.Inventory.Items.First().Id;

        var settings = GetTeammateSettings(sessionId, teammate);
        settings.SelectedLoadoutId = DefaultLoadoutId;

        SaveDefaultEquipmentSnapshot(sessionId, teammate, overwrite: true, includeSecureContainer: IsExtremeLoadoutManagementMode(mode));
        SaveTeammateSettings(sessionId, teammate, settings);
        SaveTeammate(sessionId, teammate);

        return new FriendlyTeammateDefaultEquipmentResponse();
    }

    public FriendlyTeammateBuyKitResponse BuyTeammateKit(MongoId sessionId, FriendlyTeammateBuyKitRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.Aid);
        string mode = NormalizeLoadoutManagementMode(settingsService.LoadSettings().LoadoutManagementMode);
        if (!IsRealTransferLoadoutManagementMode(mode))
        {
            throw CreateKitPurchaseException(
                sessionId,
                "Rejected teammate kit purchase outside a real-transfer loadout management mode.");
        }

        var buildItems = request.Items?.Where(item => item != null).ToList();
        if (buildItems == null || buildItems.Count == 0)
        {
            throw new FriendlyTeammateException("Missing teammate kit equipment items");
        }

        int price = Math.Max(0, request.Price);
        var playerPmc = GetPlayerProfile(sessionId);
        playerPmc.Inventory ??= new BotBaseInventory { Items = [] };
        playerPmc.Inventory.Items ??= [];
        teammate.Inventory ??= new BotBaseInventory { Items = [] };
        teammate.Inventory.Items ??= [];

        var originalPlayerItems = cloner.Clone(playerPmc.Inventory.Items) ?? playerPmc.Inventory.Items.ToList();
        var originalTeammateItems = cloner.Clone(teammate.Inventory.Items) ?? teammate.Inventory.Items.ToList();
        var originalTeammateEquipment = teammate.Inventory.Equipment;
        var fullProfile = profileHelper.GetFullProfile(sessionId);
        var originalDialogueRecords = cloner.Clone(fullProfile.DialogueRecords);

        try
        {
            if (request.UseItemsInStash)
            {
                ConsumeStashItemsForKit(sessionId, playerPmc, request.UsedItems);
            }

            DeductRoublesFromPlayerStash(playerPmc, price);

            var clonedBuild = cloner.Clone(buildItems) ?? buildItems;
            var normalizedBuild = itemHelper.ReplaceIDs(clonedBuild, playerPmc).ToList();
            MongoId rootId = normalizedBuild.First().Id;

            List<Item> previousKitDeliveryItems = BuildCurrentTeammateKitDeliveryItems(teammate, includeSecureContainer: IsExtremeLoadoutManagementMode(mode));
            teammate.Inventory.Items = MergeEquipmentWithPreservedSpecialItems(
                teammate.Inventory.Items,
                normalizedBuild,
                useReplacementSecureContainer: IsExtremeLoadoutManagementMode(mode));
            teammate.Inventory.Equipment = rootId;

            var settings = GetTeammateSettings(sessionId, teammate);
            settings.SelectedLoadoutId = DefaultLoadoutId;

            SendPreviousTeammateKitDelivery(sessionId, teammate, previousKitDeliveryItems);
            SaveDefaultEquipmentSnapshot(sessionId, teammate, overwrite: true, includeSecureContainer: IsExtremeLoadoutManagementMode(mode));
            SaveTeammateSettings(sessionId, teammate, settings);
            SaveTeammate(sessionId, teammate);
            saveServer.SaveProfileAsync(sessionId).GetAwaiter().GetResult();

            logger.Info($"Bought teammate kit for '{teammate.Aid}' with price={price}, useItemsInStash={request.UseItemsInStash}; previous active kit sent by delivery when present.");

            return new FriendlyTeammateBuyKitResponse
            {
                PlayerStashItems = GetPlayerStashItems(playerPmc),
            };
        }
        catch
        {
            playerPmc.Inventory.Items = originalPlayerItems;
            teammate.Inventory.Items = originalTeammateItems;
            teammate.Inventory.Equipment = originalTeammateEquipment;
            fullProfile.DialogueRecords = originalDialogueRecords;
            throw;
        }
    }

    public FriendlyTeammateRepairEquipmentResponse RepairTeammateDefaultEquipment(MongoId sessionId, FriendlyTeammateRepairEquipmentRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.Aid);
        string targetItemId = NormalizeRequiredValue(request.Target, "target");
        var repairKits = request.RepairKitsInfo?.Where(kit => kit != null).ToList();
        bool repairWithKit = repairKits is { Count: > 0 };
        bool repairWithTrader = !string.IsNullOrWhiteSpace(request.TraderId) && request.RepairCount.GetValueOrDefault(0) > 0;
        if (!repairWithKit && !repairWithTrader)
        {
            throw new FriendlyTeammateException("Missing repair information for teammate repair");
        }

        teammate.Inventory ??= new BotBaseInventory { Items = [] };
        teammate.Inventory.Items ??= [];
        Item teammateItem = teammate.Inventory.Items
            .FirstOrDefault(item => string.Equals(item.Id.ToString(), targetItemId, StringComparison.OrdinalIgnoreCase))
            ?? throw new FriendlyTeammateException("Unable to find teammate equipment item to repair");

        var playerPmc = GetPlayerProfile(sessionId);
        playerPmc.Inventory ??= new BotBaseInventory { Items = [] };
        playerPmc.Inventory.Items ??= [];

        if (repairWithKit)
        {
            foreach (var repairKit in repairKits!)
            {
                if (repairKit.Count.GetValueOrDefault(0) <= 0)
                {
                    throw new FriendlyTeammateException("Invalid repair kit amount for teammate repair");
                }

                if (!playerPmc.Inventory.Items.Any(item => item.Id == repairKit.Id))
                {
                    throw new FriendlyTeammateException($"Player repair kit '{repairKit.Id}' was unavailable for teammate repair");
                }
            }
        }

        // Build a temporary PMC-shaped profile by cloning the real player, then inserting the teammate
        // equipment tree. SPT's stock RepairService can then read normal player skills/bonuses and real
        // repair kits while treating the teammate item as if it belonged to the player for this operation.
        var fakeRepairProfile = cloner.Clone(playerPmc) ?? throw new FriendlyTeammateException("Unable to clone player profile for teammate repair");
        fakeRepairProfile.Inventory ??= new BotBaseInventory { Items = [] };
        fakeRepairProfile.Inventory.Items ??= [];

        var teammateRepairItems = cloner.Clone(teammate.Inventory.Items) ?? teammate.Inventory.Items.ToList();
        var fakeExistingIds = fakeRepairProfile.Inventory.Items
            .Select(item => item.Id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in teammateRepairItems)
        {
            if (item?.Id == null || fakeExistingIds.Contains(item.Id.ToString()))
            {
                continue;
            }

            fakeRepairProfile.Inventory.Items.Add(item);
        }

        var originalPlayerItems = cloner.Clone(playerPmc.Inventory.Items) ?? playerPmc.Inventory.Items.ToList();
        var originalTeammateItems = cloner.Clone(teammate.Inventory.Items) ?? teammate.Inventory.Items.ToList();
        var originalTeammateEquipment = teammate.Inventory.Equipment;

        try
        {
            var output = CreateEmptyRepairOutput(sessionId, playerPmc);
            RepairDetails repairDetails;
            if (repairWithKit)
            {
                repairDetails = repairService.RepairItemByKit(
                    sessionId,
                    fakeRepairProfile,
                    repairKits!,
                    new MongoId(targetItemId),
                    output);
                repairService.AddBuffToItem(repairDetails, fakeRepairProfile);
                repairService.AddRepairSkillPoints(sessionId, repairDetails, fakeRepairProfile);
            }
            else
            {
                var traderId = new MongoId(NormalizeRequiredValue(request.TraderId, "traderId"));
                var repairItem = new RepairItem
                {
                    Id = new MongoId(targetItemId),
                    Count = request.RepairCount,
                };
                repairDetails = repairService.RepairItemByTrader(sessionId, fakeRepairProfile, repairItem, traderId);
                repairService.PayForRepair(sessionId, playerPmc, targetItemId, repairDetails.RepairCost.GetValueOrDefault(0), traderId, output);
                if (output.Warnings is { Count: > 0 })
                {
                    throw new FriendlyTeammateException("Unable to pay for teammate trader repair");
                }

                repairService.AddRepairSkillPoints(sessionId, repairDetails, fakeRepairProfile);
            }

            Item repairedFakeItem = fakeRepairProfile.Inventory.Items
                .FirstOrDefault(item => string.Equals(item.Id.ToString(), targetItemId, StringComparison.OrdinalIgnoreCase))
                ?? throw new FriendlyTeammateException("Stock repair did not return the repaired teammate item");
            var repairedRepairable = repairedFakeItem.Upd?.Repairable
                ?? throw new FriendlyTeammateException("Stock repair did not produce teammate repair durability data");
            var repairedBuff = repairedFakeItem.Upd?.Buff;

            if (repairWithKit)
            {
                foreach (var repairKit in repairKits!)
                {
                    Item? fakeRepairKit = fakeRepairProfile.Inventory.Items.FirstOrDefault(item => item.Id == repairKit.Id);
                    Item? playerRepairKit = playerPmc.Inventory.Items.FirstOrDefault(item => item.Id == repairKit.Id);
                    if (fakeRepairKit == null)
                    {
                        RemoveItemTreesById(playerPmc.Inventory.Items, [repairKit.Id.ToString()]);
                        continue;
                    }

                    if (playerRepairKit == null)
                    {
                        throw new FriendlyTeammateException($"Player repair kit '{repairKit.Id}' disappeared during teammate repair");
                    }

                    playerRepairKit.Upd ??= new Upd();
                    playerRepairKit.Upd.RepairKit = cloner.Clone(fakeRepairKit.Upd?.RepairKit);
                    playerRepairKit.Upd.StackObjectsCount = fakeRepairKit.Upd?.StackObjectsCount ?? playerRepairKit.Upd.StackObjectsCount;
                }
            }

            playerPmc.Skills = cloner.Clone(fakeRepairProfile.Skills) ?? playerPmc.Skills;
            playerPmc.Bonuses = cloner.Clone(fakeRepairProfile.Bonuses) ?? playerPmc.Bonuses;

            teammateItem.Upd ??= new Upd();
            var savedRepairable = cloner.Clone(repairedRepairable) ?? repairedRepairable;
            teammateItem.Upd.Repairable = savedRepairable;
            teammateItem.Upd.Buff = cloner.Clone(repairedBuff);

            string mode = NormalizeLoadoutManagementMode(settingsService.LoadSettings().LoadoutManagementMode);
            SaveDefaultEquipmentSnapshot(sessionId, teammate, overwrite: true, includeSecureContainer: IsExtremeLoadoutManagementMode(mode));
            SaveTeammate(sessionId, teammate);
            saveServer.SaveProfileAsync(sessionId).GetAwaiter().GetResult();

            logger.Info($"Repaired teammate '{teammate.Aid}' default equipment item '{targetItemId}' through stock {(repairWithKit ? "kit" : "trader")} repair service.");

            var playerStashDelta = BuildPlayerStashDelta(playerPmc, originalPlayerItems);
            return new FriendlyTeammateRepairEquipmentResponse
            {
                ItemId = targetItemId,
                Durability = savedRepairable.Durability,
                MaxDurability = savedRepairable.MaxDurability,
                PlayerStashItems = GetPlayerStashItems(playerPmc),
                PlayerNewStashItems = playerStashDelta.NewItems,
                PlayerChangedStashItems = playerStashDelta.ChangedItems,
                PlayerDeletedStashItemIds = playerStashDelta.DeletedItemIds,
            };
        }
        catch
        {
            playerPmc.Inventory.Items = originalPlayerItems;
            teammate.Inventory.Items = originalTeammateItems;
            teammate.Inventory.Equipment = originalTeammateEquipment;
            throw;
        }
    }

    private FriendlyTeammateDefaultEquipmentResponse SaveTeammateDefaultEquipmentWithRealItemCommit(
        MongoId sessionId,
        BotBase teammate,
        FriendlyTeammateDefaultEquipmentRequest request,
        List<Item> replacementEquipmentItems,
        string mode)
    {
        var replacementStashItems = request.PlayerStashItems?.Where(item => item != null).ToList();
        if (replacementStashItems == null || replacementStashItems.Count == 0)
        {
            throw new FriendlyTeammateException("Missing player stash items for real teammate equipment save");
        }

        replacementEquipmentItems = PruneSubmittedEquipmentToRootTree(replacementEquipmentItems, out int prunedEquipmentItems);
        if (prunedEquipmentItems > 0)
        {
            logger.Warning($"Pruned {prunedEquipmentItems} foreign/orphan items from submitted teammate equipment before real commit validation.");
        }

        var playerPmc = GetPlayerProfile(sessionId);
        playerPmc.Inventory ??= new BotBaseInventory { Items = [] };
        playerPmc.Inventory.Items ??= [];
        teammate.Inventory ??= new BotBaseInventory { Items = [] };
        teammate.Inventory.Items ??= [];

        string playerStashRootId = GetPlayerStashRootId(playerPmc);
        string replacementStashRootId = replacementStashItems.First().Id.ToString();
        if (!string.Equals(playerStashRootId, replacementStashRootId, StringComparison.OrdinalIgnoreCase))
        {
            throw new FriendlyTeammateException("Submitted player stash root does not match the active profile stash");
        }

        // Only two inventories may contribute real item ids: the player's current stash and the teammate's
        // current saved inventory. Anything else would mean the client submitted equipped/player-external gear.
        var currentPlayerStashIds = GetItemTreeIds(playerPmc.Inventory.Items, playerStashRootId);
        var currentTeammateItemIds = teammate.Inventory.Items
            .Select(item => item.Id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowedMovedItemIds = new HashSet<string>(currentPlayerStashIds, StringComparer.OrdinalIgnoreCase);
        allowedMovedItemIds.UnionWith(currentTeammateItemIds);
        HashSet<string> remappedTeammateItemIds = RemapReplacementEquipmentPlayerEquippedIdCollisions(
            playerPmc,
            replacementEquipmentItems,
            currentPlayerStashIds,
            currentTeammateItemIds);
        allowedMovedItemIds.UnionWith(remappedTeammateItemIds);

        // Validate the staged final state before mutating either profile. This prevents partial transfer
        // corruption and lets the catch block restore both inventories if persistence fails later.
        ValidateRealCommitItemSet(
            replacementEquipmentItems,
            allowedMovedItemIds,
            "teammate equipment",
            allowGeneratedSlotDescendants: true);
        ValidateRealCommitItemSet(
            replacementStashItems,
            allowedMovedItemIds,
            "player stash",
            allowGeneratedSlotDescendants: true);
        ValidateNoRealCommitOverlap(replacementEquipmentItems, replacementStashItems, playerStashRootId);
        ValidateNoEquippedPlayerItemCommit(playerPmc, replacementEquipmentItems, currentPlayerStashIds);
        string lockedStashItemMessageTemplate = GetLanguageValue(
            languageService.GetStringMap(sessionId, "socialUi"),
            "LockedStashItemBlocked",
            LockedStashItemBlockedFallback);
        ValidateLockedPlayerStashItemsUnchanged(
            playerPmc.Inventory.Items,
            replacementEquipmentItems,
            replacementStashItems,
            playerStashRootId,
            lockedStashItemMessageTemplate);

        var originalPlayerItems = cloner.Clone(playerPmc.Inventory.Items) ?? playerPmc.Inventory.Items.ToList();
        var originalTeammateItems = cloner.Clone(teammate.Inventory.Items) ?? teammate.Inventory.Items.ToList();
        var originalTeammateEquipment = teammate.Inventory.Equipment;

        try
        {
            var replacementEquipment = cloner.Clone(replacementEquipmentItems) ?? replacementEquipmentItems;
            var mergedEquipment = MergeEquipmentWithPreservedSpecialItems(
                teammate.Inventory.Items,
                replacementEquipment,
                useReplacementSecureContainer: IsExtremeLoadoutManagementMode(mode));

            // Replace only the stash tree. Player equipment, quest inventory, sorting table, and other
            // profile-owned item roots are left untouched by real teammate loadout commits.
            playerPmc.Inventory.Items = playerPmc.Inventory.Items
                .Where(item => !currentPlayerStashIds.Contains(item.Id.ToString()))
                .ToList();
            playerPmc.Inventory.Items.AddRange(cloner.Clone(replacementStashItems) ?? replacementStashItems);

            // The teammate stores the committed equipment as its new Default snapshot. Spawn preparation may
            // still inject mode-specific protected items such as a fallback knife on the temporary spawn clone.
            teammate.Inventory.Items = mergedEquipment;
            teammate.Inventory.Equipment = teammate.Inventory.Items.First().Id;

            var settings = GetTeammateSettings(sessionId, teammate);
            settings.SelectedLoadoutId = DefaultLoadoutId;

            SaveDefaultEquipmentSnapshot(sessionId, teammate, overwrite: true, includeSecureContainer: IsExtremeLoadoutManagementMode(mode));
            SaveTeammateSettings(sessionId, teammate, settings);
            SaveTeammate(sessionId, teammate);
            saveServer.SaveProfileAsync(sessionId).GetAwaiter().GetResult();

            var playerStashDelta = BuildPlayerStashDelta(playerPmc, originalPlayerItems);
            logger.Info($"Committed real default equipment movement for teammate '{teammate.Aid}' in loadout management mode '{NormalizeLoadoutManagementMode(settingsService.LoadSettings().LoadoutManagementMode)}'.");

            return new FriendlyTeammateDefaultEquipmentResponse
            {
                RealItemCommit = true,
                PlayerStashItems = cloner.Clone(replacementStashItems) ?? replacementStashItems,
                PlayerNewStashItems = playerStashDelta.NewItems,
                PlayerChangedStashItems = playerStashDelta.ChangedItems,
                PlayerDeletedStashItemIds = playerStashDelta.DeletedItemIds,
            };
        }
        catch
        {
            playerPmc.Inventory.Items = originalPlayerItems;
            teammate.Inventory.Items = originalTeammateItems;
            teammate.Inventory.Equipment = originalTeammateEquipment;
            throw;
        }
    }

    public void SetTeammateAggression(MongoId sessionId, FriendlyTeammateAggressionRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.Aid);
        var settings = GetTeammateSettings(sessionId, teammate);
        settings.Aggression = NormalizeAggression(request.Aggression);
        SaveTeammateSettings(sessionId, teammate, settings);
    }

    public void SetTeammateTactic(MongoId sessionId, FriendlyTeammateTacticRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.Aid);
        var settings = GetTeammateSettings(sessionId, teammate);
        settings.CombatTactic = NormalizeCombatTactic(request.Tactic);
        settings.Aggression = GetDefaultAggressionForTactic(settings.CombatTactic);
        SaveTeammateSettings(sessionId, teammate, settings);
        logger.Info($"Set teammate tactic '{settings.CombatTactic}' for aid '{teammate.Aid}' in session '{sessionId}'");
    }

    public void SetTeammateAutoJoin(MongoId sessionId, FriendlyTeammateAutoJoinRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.Aid);
        var settings = GetTeammateSettings(sessionId, teammate);
        settings.AutoJoinEnabled = request.Enabled;
        SaveTeammateSettings(sessionId, teammate, settings);
    }

    public bool TryGetTeammateProfile(MongoId sessionId, string? accountId, out GetOtherProfileResponse? profile)
    {
        profile = null;
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return false;
        }

        var teammate = LoadTeammates(sessionId).FirstOrDefault(candidate => candidate.Aid?.ToString() == accountId);
        if (teammate == null)
        {
            return false;
        }

        profile = ToOtherProfileResponse(PrepareTeammateForFetch(teammate));
        return true;
    }

    public bool TryGetRaidGroupCharacter(MongoId sessionId, string? accountId, out GroupCharacter? groupCharacter)
    {
        var accepted = TryGetRaidGroupCharacter(sessionId, accountId, out groupCharacter, out _);
        if (!accepted)
        {
            groupCharacter = null;
        }

        return accepted;
    }

    public bool TryGetRaidGroupCharacter(MongoId sessionId, string? accountId, out GroupCharacter? groupCharacter, out string? rejectionReason)
    {
        groupCharacter = null;
        rejectionReason = null;
        if (!TryFindByAccountId(sessionId, accountId, out var teammate))
        {
            return false;
        }

        var preparedTeammate = PrepareTeammateForFetch(teammate!);
        groupCharacter = ToGroupCharacter(preparedTeammate);
        if (!HasProperRaidKit(preparedTeammate))
        {
            rejectionReason = $"Cannot add {GetTeammateDisplayName(preparedTeammate)} to the raid group without a proper kit.";
            return false;
        }

        return true;
    }

    public bool TryGetSpawnProfile(MongoId sessionId, string? accountId, double? healthMultiplier, out BotBase? profile)
    {
        profile = null;
        if (!TryFindByAccountId(sessionId, accountId, out var teammate))
        {
            return false;
        }

        profile = PrepareTeammateForFetch(teammate!, healthMultiplier, refillMagazinesForSpawn: true);
        return true;
    }

    private BotBase PrepareTeammateForFetch(
        BotBase teammate,
        double? healthMultiplier = null,
        bool refillMagazinesForSpawn = false)
    {
        var clone = cloner.Clone(teammate) ?? teammate;
        // Temporarily disabled: this floor baseline can over-inflate teammate skills.
        // ApplyPmcFollowerSkillBaseline(clone);
        ApplyTemporaryHealthMultiplier(clone, healthMultiplier);
        EnsureFollowerHasPockets(clone);

        var mode = NormalizeLoadoutManagementMode(settingsService.LoadSettings().LoadoutManagementMode);
        if (!IsExtremeLoadoutManagementMode(mode))
        {
            EnsureManagedSecureContainer(clone);
            EnsureFollowerHasSecureContainerSupplies(clone);
        }

        // Magazine refill mutates item stacks. Keep it strictly on the raid-spawn clone so profile
        // viewing, invite validation, and loadout editing do not silently change teammate gear.
        if (refillMagazinesForSpawn)
        {
            EnsureFollowerHasScabbardKnife(clone);
            RefillFollowerMagazinesFromInventoryAmmo(clone);
        }

        return clone;
    }

    public void PersistFollowerProgress(MongoId sessionId, IEnumerable<FriendlyTeammateFollowerProgressRequest>? progressEntries)
    {
        if (progressEntries == null)
        {
            return;
        }

        // Progress and raid-outcome requests are posted independently during teardown. Keep each
        // read-modify-save operation atomic so a stale progress copy cannot overwrite live equipment.
        lock (RaidResultPersistenceLock)
        {
            foreach (var progressEntry in progressEntries)
            {
                if (!TryFindByAccountId(sessionId, progressEntry.Aid, out var teammate) || teammate == null)
                {
                    continue;
                }

                ApplyFollowerProgress(teammate, progressEntry);
                SaveTeammate(sessionId, teammate);
            }
        }
    }

    public FriendlyTeammateDeathEscapeSummary PersistDeathEscapeOutcomes(
        MongoId sessionId,
        IEnumerable<FriendlyTeammateDeathEscapeEntry>? entries)
    {
        lock (RaidResultPersistenceLock)
        {
            var summary = new FriendlyTeammateDeathEscapeSummary();
            if (entries == null)
            {
                return summary;
            }

            Dictionary<string, HashSet<string>> protectedTeammateItemIdsByAid =
                BuildProtectedTeammateItemIdsByAid(sessionId);

            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Aid))
                {
                    continue;
                }

                string displayName = string.IsNullOrWhiteSpace(entry.Nickname) ? "Squadmate" : entry.Nickname;
                if (entry.Escaped)
                {
                    summary.EscapedNames.Add(displayName);
                }
                else
                {
                    summary.LostNames.Add(displayName);
                }

                if (entry.Escaped
                    && string.IsNullOrWhiteSpace(summary.ExtractName)
                    && !string.IsNullOrWhiteSpace(entry.ExtractName))
                {
                    summary.ExtractName = entry.ExtractName;
                }

                if (!TryFindByAccountId(sessionId, entry.Aid, out var teammate) || teammate == null)
                {
                    continue;
                }

                // Escape rolls are server-owned by the time outcomes are persisted. Client-provided
                // live raid state is only input data for ResolveRaidOutcomes().
                ApplyFollowerRaidOutcomeStats(teammate, entry.Escaped);
                ApplyDeathEscapeOutcome(teammate, entry);

                // Eligible surviving Default loadouts keep the in-raid state here. Immersive/Realistic
                // always qualify; Restricted only qualifies when Field Upkeep is enabled.
                ApplyImmersiveEscapedDefaultEquipmentState(sessionId, teammate, entry, protectedTeammateItemIdsByAid);

                // Field Upkeep does not enable death gear loss, but it still needs the
                // death-time equipment state so used consumables and durability loss cannot be duplicated.
                ApplyRestrictedGearMaintenanceDeathEquipmentState(sessionId, teammate, entry, protectedTeammateItemIdsByAid);

                // Immersive/Extreme loss is applied after the health/death outcome so the teammate's
                // saved Default equipment reflects the post-raid penalty instead of regenerating gear.
                ApplyImmersiveDefaultGearLoss(sessionId, teammate, entry);
                SaveTeammate(sessionId, teammate);
            }

            return summary;
        }
    }

    public FriendlyTeammateRaidOutcomeResponse ResolveRaidOutcomes(IEnumerable<FriendlyTeammateDeathEscapeEntry>? entries)
    {
        var response = new FriendlyTeammateRaidOutcomeResponse();
        if (entries == null)
        {
            return response;
        }

        foreach (var entry in entries)
        {
            if (entry == null)
            {
                continue;
            }

            var resolved = CloneRaidOutcomeEntry(entry);
            if (resolved.RollEscape)
            {
                resolved.Chance = CalculateDeathEscapeChance(resolved);
                resolved.Escaped = RollDeathEscape(resolved.Chance);
                logger.Info(
                    $"Resolved teammate raid escape roll for '{resolved.Nickname ?? resolved.Aid}': escaped={resolved.Escaped}, chance={resolved.Chance:P0}, health={resolved.HealthRatio:P0}, gear={resolved.EquipmentPower:0.0}, routeEnemyAvg={resolved.RouteEnemyAveragePower:0.0}, fightEnemyAvg={resolved.CurrentFightEnemyAveragePower:0.0}.");
            }

            response.Entries.Add(resolved);
        }

        return response;
    }

    private static FriendlyTeammateDeathEscapeEntry CloneRaidOutcomeEntry(FriendlyTeammateDeathEscapeEntry entry)
    {
        return new FriendlyTeammateDeathEscapeEntry
        {
            Aid = entry.Aid,
            ProfileId = entry.ProfileId,
            Nickname = entry.Nickname,
            Escaped = entry.Escaped,
            RollEscape = entry.RollEscape,
            Chance = entry.Chance,
            ExtractName = entry.ExtractName,
            Distance = entry.Distance,
            HealthRatio = entry.HealthRatio,
            EquipmentPower = entry.EquipmentPower,
            EnemyAveragePower = entry.EnemyAveragePower,
            RouteEnemyAveragePower = entry.RouteEnemyAveragePower,
            CurrentFightEnemyAveragePower = entry.CurrentFightEnemyAveragePower,
            RouteEnemyCount = entry.RouteEnemyCount,
            CurrentFightEnemyCount = entry.CurrentFightEnemyCount,
            AliveSquadmates = entry.AliveSquadmates,
            HasSecureMeds = entry.HasSecureMeds,
            VitalsDestroyed = entry.VitalsDestroyed,
            EquipmentItems = entry.EquipmentItems,
            TrackedItemIds = entry.TrackedItemIds,
        };
    }

    private static double CalculateDeathEscapeChance(FriendlyTeammateDeathEscapeEntry entry)
    {
        double distanceScore = CalculateDeathEscapeDistanceScore(entry.Distance);
        double squadScore = Math.Clamp(entry.AliveSquadmates / 3d, 0d, 1d);
        if (entry.AliveSquadmates == 1)
        {
            squadScore = 0.35d;
        }
        else if (entry.AliveSquadmates == 2)
        {
            squadScore = 0.70d;
        }

        double routeEnemyAveragePower = entry.RouteEnemyAveragePower > 0d
            ? entry.RouteEnemyAveragePower
            : entry.EnemyAveragePower;
        double fightEnemyAveragePower = entry.CurrentFightEnemyAveragePower;
        double equipmentScore = CalculateDeathEscapeEquipmentScore(entry.EquipmentPower, routeEnemyAveragePower);
        double medScore = entry.HasSecureMeds ? 1d : 0d;

        double chance =
            0.20d +
            0.25d * distanceScore +
            0.25d * Math.Clamp(entry.HealthRatio, 0d, 1d) +
            0.20d * equipmentScore +
            0.15d * squadScore +
            0.10d * medScore;

        if (entry.VitalsDestroyed)
        {
            chance *= 0.25d;
        }

        chance *= CalculateCurrentFightSurvivalMultiplier(
            entry.AliveSquadmates,
            entry.HealthRatio,
            entry.EquipmentPower,
            fightEnemyAveragePower,
            entry.CurrentFightEnemyCount);

        return Math.Clamp(chance, DeathEscapeMinChance, DeathEscapeMaxChance);
    }

    private static double CalculateDeathEscapeDistanceScore(double distance)
    {
        if (distance <= 0d)
        {
            return 0.5d;
        }

        return 1d - Math.Clamp(
            (distance - DeathEscapeCloseExtractDistance) / (DeathEscapeFarExtractDistance - DeathEscapeCloseExtractDistance),
            0d,
            1d);
    }

    private static double CalculateDeathEscapeEquipmentScore(double followerPower, double enemyAveragePower)
    {
        if (enemyAveragePower <= 0.01d)
        {
            return 0.75d;
        }

        double ratio = Math.Clamp(followerPower / enemyAveragePower, 0.25d, 1.25d);
        return Math.Clamp((ratio - 0.25d) / 1d, 0d, 1d);
    }

    private static double CalculateCurrentFightSurvivalMultiplier(
        int aliveCount,
        double healthRatio,
        double equipmentPower,
        double currentFightEnemyAveragePower,
        int currentFightEnemyCount)
    {
        if (currentFightEnemyCount <= 0 || currentFightEnemyAveragePower <= 0.01d)
        {
            return 1d;
        }

        double squadScore = aliveCount == 1
            ? 0.35d
            : aliveCount == 2
                ? 0.70d
                : Math.Clamp(aliveCount / 3d, 0d, 1d);
        double equipmentScore = CalculateDeathEscapeEquipmentScore(equipmentPower, currentFightEnemyAveragePower);
        double enemyCountPressure = Math.Clamp((currentFightEnemyCount - aliveCount) / 4d, 0d, 1d);

        double fightSurvival =
            0.35d +
            0.25d * Math.Clamp(healthRatio, 0d, 1d) +
            0.25d * equipmentScore +
            0.15d * squadScore -
            0.15d * enemyCountPressure;

        return Math.Clamp(fightSurvival, 0.25d, 0.95d);
    }

    private static bool RollDeathEscape(double chance)
    {
        lock (DeathEscapeRandomLock)
        {
            return DeathEscapeRandom.NextDouble() <= chance;
        }
    }

    private void ApplyImmersiveEscapedDefaultEquipmentState(
        MongoId sessionId,
        BotBase teammate,
        FriendlyTeammateDeathEscapeEntry entry,
        Dictionary<string, HashSet<string>> protectedTeammateItemIdsByAid)
    {
        if (!entry.Escaped)
        {
            return;
        }

        FriendlyServerSettingsRequest serverSettings = settingsService.LoadSettings();
        string mode = NormalizeLoadoutManagementMode(serverSettings.LoadoutManagementMode);
        if (!ShouldPersistEscapedDefaultEquipmentState(mode, serverSettings))
        {
            return;
        }

        var settings = GetTeammateSettings(sessionId, teammate);
        if (!string.Equals(settings.SelectedLoadoutId, DefaultLoadoutId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var equipmentItems = entry.EquipmentItems?.Where(item => item != null).ToList();
        if (equipmentItems == null || equipmentItems.Count == 0)
        {
            logger.Warning($"Skipped escaped Default equipment persistence for teammate '{teammate.Aid}' because no equipment snapshot was provided.");
            return;
        }

        var replacementItems = cloner.Clone(equipmentItems) ?? equipmentItems;
        RemoveItemTreesById(replacementItems, entry.TrackedItemIds);
        RemoveItemTreesById(
            replacementItems,
            GetOtherProtectedTeammateItemIds(protectedTeammateItemIdsByAid, entry.Aid));

        if (replacementItems.Count == 0)
        {
            logger.Warning($"Skipped escaped Default equipment persistence for teammate '{teammate.Aid}' because the filtered equipment snapshot was empty.");
            return;
        }

        teammate.Inventory ??= new BotBaseInventory { Items = [] };
        teammate.Inventory.Items = MergeEquipmentWithPreservedSpecialItems(
            teammate.Inventory.Items,
            replacementItems,
            useReplacementSecureContainer: IsExtremeLoadoutManagementMode(mode));
        teammate.Inventory.Equipment = teammate.Inventory.Items.First().Id;

        bool keepSecureContainer = IsExtremeLoadoutManagementMode(mode);
        if (!keepSecureContainer)
        {
            RemoveSecureContainerTree(teammate);
        }

        EnsureFollowerHasScabbardKnife(teammate);
        SaveDefaultEquipmentSnapshot(sessionId, teammate, overwrite: true, includeSecureContainer: keepSecureContainer);
        logger.Info($"Persisted escaped Default equipment state for teammate '{teammate.Aid}' in loadout management mode '{mode}'.");
    }

    private void ApplyRestrictedGearMaintenanceDeathEquipmentState(
        MongoId sessionId,
        BotBase teammate,
        FriendlyTeammateDeathEscapeEntry entry,
        Dictionary<string, HashSet<string>> protectedTeammateItemIdsByAid)
    {
        if (entry.Escaped)
        {
            return;
        }

        FriendlyServerSettingsRequest serverSettings = settingsService.LoadSettings();
        string mode = NormalizeLoadoutManagementMode(serverSettings.LoadoutManagementMode);
        if (!ShouldPersistRestrictedGearMaintenanceDeathEquipmentState(mode, serverSettings))
        {
            return;
        }

        var settings = GetTeammateSettings(sessionId, teammate);
        if (!string.Equals(settings.SelectedLoadoutId, DefaultLoadoutId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var equipmentItems = entry.EquipmentItems?.Where(item => item != null).ToList();
        if (equipmentItems == null || equipmentItems.Count == 0)
        {
            logger.Warning($"Skipped fallen Default equipment maintenance persistence for teammate '{teammate.Aid}' because no death-time equipment snapshot was provided.");
            return;
        }

        var replacementItems = cloner.Clone(equipmentItems) ?? equipmentItems;
        RemoveItemTreesById(replacementItems, entry.TrackedItemIds);
        RemoveItemTreesById(
            replacementItems,
            GetOtherProtectedTeammateItemIds(protectedTeammateItemIdsByAid, entry.Aid));

        if (replacementItems.Count == 0)
        {
            logger.Warning($"Skipped fallen Default equipment maintenance persistence for teammate '{teammate.Aid}' because the filtered death-time equipment snapshot was empty.");
            return;
        }

        teammate.Inventory ??= new BotBaseInventory { Items = [] };
        teammate.Inventory.Items = MergeEquipmentWithPreservedSpecialItems(
            teammate.Inventory.Items,
            replacementItems,
            useReplacementSecureContainer: false);
        teammate.Inventory.Equipment = teammate.Inventory.Items.First().Id;

        RemoveSecureContainerTree(teammate);
        EnsureFollowerHasScabbardKnife(teammate);
        SaveDefaultEquipmentSnapshot(sessionId, teammate, overwrite: true, includeSecureContainer: false);
        logger.Info($"Persisted fallen Default equipment maintenance state for teammate '{teammate.Aid}' in loadout management mode '{mode}'.");
    }

    private void ApplyImmersiveDefaultGearLoss(MongoId sessionId, BotBase teammate, FriendlyTeammateDeathEscapeEntry entry)
    {
        // Escaped teammates are handled by ApplyImmersiveEscapedDefaultEquipmentState. Only a death
        // result can strip the saved Default down to the non-loss exceptions.
        if (entry.Escaped)
        {
            return;
        }

        // Simple/Restricted never lose teammate gear on death; Immersive and Extreme do.
        string mode = NormalizeLoadoutManagementMode(settingsService.LoadSettings().LoadoutManagementMode);
        if (!IsImmersiveLikeLoadoutManagementMode(mode))
        {
            return;
        }

        var settings = GetTeammateSettings(sessionId, teammate);
        // This phase only applies death loss to the live Default loadout. Non-default preset loss
        // needs separate ownership tracking before it can safely mutate saved preset data.
        if (!string.Equals(settings.SelectedLoadoutId, DefaultLoadoutId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Persist the stripped equipment as the new Default snapshot so the next spawn/portrait
        // sees the loss instead of silently restoring the pre-raid gear. Permanent bot identity
        // slots are kept, but the secure-container tree is only persisted for Realistic/Extreme.
        bool keepSecureContainer = IsExtremeLoadoutManagementMode(mode);
        StripDefaultEquipmentAfterDeath(teammate, keepSecureContainer);
        SaveDefaultEquipmentSnapshot(sessionId, teammate, overwrite: true, includeSecureContainer: keepSecureContainer);
        logger.Info($"Stripped teammate '{teammate.Aid}' default equipment after death in loadout management mode '{mode}'.");
    }

    private void StripDefaultEquipmentAfterDeath(BotBase teammate, bool keepSecureContainer)
    {
        teammate.Inventory ??= new BotBaseInventory { Items = [] };
        teammate.Inventory.Items ??= [];
        if (teammate.Inventory.Items.Count == 0)
        {
            return;
        }

        // Keep or inject a scabbard knife before stripping. EFT already prevents knife looting, and
        // this gives later spawn/preview validation a stable legal melee slot to work with.
        EnsureFollowerHasPockets(teammate);
        EnsureFollowerHasScabbardKnife(teammate);

        string rootId = GetEquipmentRootId(teammate);
        var keepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootId };
        foreach (var preservedItem in teammate.Inventory.Items.Where(item =>
                     !string.IsNullOrWhiteSpace(item?.SlotId) &&
                     IsPermanentTeammateEquipmentSlot(item.SlotId, keepSecureContainer)).ToList())
        {
            // Pockets are a permanent equipment container, but pocket contents are normal loot
            // and should still be lost on teammate death. Keep the container itself so special
            // slots under it remain anchored, then preserve special-slot trees separately.
            if (IsPocketsSlotItem(preservedItem))
            {
                keepIds.Add(preservedItem.Id.ToString());
                continue;
            }

            // Preserve descendants for kept equipment items so attached child items are not orphaned.
            AddItemAndDescendantsToKeepSet(teammate.Inventory.Items, preservedItem.Id.ToString(), keepIds);
        }

        // The filter is the actual loss operation: anything outside the keep set is removed from
        // the teammate profile before the Default snapshot is saved.
        teammate.Inventory.Items = teammate.Inventory.Items
            .Where(item => keepIds.Contains(item.Id.ToString()))
            .ToList();
        teammate.Inventory.Equipment = new MongoId(rootId);
    }

    private static void AddItemAndDescendantsToKeepSet(List<Item> inventoryItems, string itemId, HashSet<string> keepIds)
    {
        if (!keepIds.Add(itemId))
        {
            return;
        }

        foreach (var child in inventoryItems.Where(item =>
                     string.Equals(item.ParentId, itemId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            AddItemAndDescendantsToKeepSet(inventoryItems, child.Id.ToString(), keepIds);
        }
    }

    private void EnsureFollowerHasScabbardKnife(BotBase profile)
    {
        profile.Inventory ??= new BotBaseInventory { Items = [] };
        profile.Inventory.Items ??= [];
        if (profile.Inventory.Items.Count == 0)
        {
            return;
        }

        if (profile.Inventory.Items.Any(item =>
                string.Equals(item.SlotId, nameof(EquipmentSlots.Scabbard), StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        string rootId = GetEquipmentRootId(profile);
        profile.Inventory.Items.Add(new Item
        {
            Id = new MongoId(),
            Template = new MongoId(FriendlyItemTemplateIds.Weapon.DefaultKnife),
            ParentId = rootId,
            SlotId = nameof(EquipmentSlots.Scabbard),
            Location = null,
            Upd = new Upd
            {
                StackObjectsCount = 1,
                SpawnedInSession = false,
            },
        });

        logger.Info($"Injected default scabbard knife for teammate '{profile.Aid}'.");
    }

    private void EnsureFollowerHasPockets(BotBase profile)
    {
        profile.Inventory ??= new BotBaseInventory { Items = [] };
        profile.Inventory.Items ??= [];
        if (profile.Inventory.Items.Count == 0)
        {
            return;
        }

        string rootId = GetEquipmentRootId(profile);
        var pockets = profile.Inventory.Items.FirstOrDefault(IsPocketsSlotItem);
        if (pockets == null)
        {
            pockets = new Item
            {
                Id = new MongoId(),
                Template = new MongoId(FriendlyItemTemplateIds.EquipmentContainer.Pockets),
                ParentId = rootId,
                SlotId = nameof(EquipmentSlots.Pockets),
                Location = null,
                Upd = new Upd
                {
                    StackObjectsCount = 1,
                    SpawnedInSession = false,
                },
            };
            profile.Inventory.Items.Add(pockets);
            logger.Info($"Injected missing pockets container for teammate '{profile.Aid}'.");
        }

        string pocketsId = pockets.Id.ToString();
        foreach (var specialSlot in profile.Inventory.Items.Where(item =>
                     item?.SlotId != null &&
                     item.SlotId.Contains("SpecialSlot", StringComparison.OrdinalIgnoreCase)))
        {
            specialSlot.ParentId = pocketsId;
        }
    }

    private void EnsureManagedSecureContainer(BotBase profile)
    {
        profile.Inventory ??= new BotBaseInventory { Items = [] };
        profile.Inventory.Items ??= [];
        if (profile.Inventory.Items.Count == 0)
        {
            return;
        }

        bool hasSecureContainer = profile.Inventory.Items.Any(item =>
            string.Equals(item?.SlotId, nameof(EquipmentSlots.SecuredContainer), StringComparison.OrdinalIgnoreCase));
        if (hasSecureContainer)
        {
            return;
        }

        profile.Inventory.Items.Add(new Item
        {
            Id = new MongoId(),
            Template = new MongoId(FriendlyItemTemplateIds.SecureContainer.Boss),
            ParentId = GetEquipmentRootId(profile),
            SlotId = nameof(EquipmentSlots.SecuredContainer),
            Location = null,
            Upd = new Upd
            {
                StackObjectsCount = 1,
                SpawnedInSession = false,
            },
        });

        logger.Debug($"Injected managed secure container for teammate '{profile.Aid}' in non-Realistic loadout mode.");
    }

    private static string GetEquipmentRootId(BotBase profile)
    {
        string? rootId = profile.Inventory?.Equipment?.ToString();
        if (!string.IsNullOrWhiteSpace(rootId))
        {
            return rootId;
        }

        Item rootItem = profile.Inventory?.Items?.FirstOrDefault()
            ?? throw new FriendlyTeammateException("Teammate inventory is missing equipment root item");
        profile.Inventory!.Equipment = rootItem.Id;
        return rootItem.Id.ToString();
    }

    private static string GetPlayerStashRootId(PmcData profile)
    {
        string? rootId = profile.Inventory?.Stash?.ToString();
        if (!string.IsNullOrWhiteSpace(rootId))
        {
            return rootId;
        }

        Item rootItem = profile.Inventory?.Items?.FirstOrDefault(item =>
            string.Equals(item.SlotId, "hideout", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.SlotId, "main", StringComparison.OrdinalIgnoreCase))
            ?? throw new FriendlyTeammateException("Player inventory is missing stash root item");

        profile.Inventory!.Stash = rootItem.Id;
        return rootItem.Id.ToString();
    }

    private List<Item> GetPlayerStashItems(PmcData profile)
    {
        profile.Inventory ??= new BotBaseInventory { Items = [] };
        profile.Inventory.Items ??= [];
        string playerStashRootId = GetPlayerStashRootId(profile);
        var stashIds = GetItemTreeIds(profile.Inventory.Items, playerStashRootId);
        return cloner.Clone(profile.Inventory.Items.Where(item => stashIds.Contains(item.Id.ToString())).ToList())
            ?? profile.Inventory.Items.Where(item => stashIds.Contains(item.Id.ToString())).ToList();
    }

    private (List<Item> NewItems, List<Item> ChangedItems, List<string> DeletedItemIds) BuildPlayerStashDelta(
        PmcData profile,
        List<Item> originalPlayerItems)
    {
        profile.Inventory ??= new BotBaseInventory { Items = [] };
        profile.Inventory.Items ??= [];
        originalPlayerItems ??= [];

        string playerStashRootId = GetPlayerStashRootId(profile);
        var originalStashIds = GetItemTreeIds(originalPlayerItems, playerStashRootId);
        var currentStashIds = GetItemTreeIds(profile.Inventory.Items, playerStashRootId);
        var originalById = ToItemDictionary(originalPlayerItems.Where(item =>
            item?.Id != null && originalStashIds.Contains(item.Id.ToString())));
        var currentById = ToItemDictionary(profile.Inventory.Items.Where(item =>
            item?.Id != null && currentStashIds.Contains(item.Id.ToString())));

        var newItems = new List<Item>();
        var changedItems = new List<Item>();
        var deletedItemIds = new List<string>();

        foreach (string originalId in originalStashIds)
        {
            if (!currentStashIds.Contains(originalId))
            {
                deletedItemIds.Add(originalId);
            }
        }

        foreach (string currentId in currentStashIds)
        {
            if (!currentById.TryGetValue(currentId, out Item? currentItem))
            {
                continue;
            }

            if (!originalById.TryGetValue(currentId, out Item? originalItem))
            {
                newItems.Add(currentItem);
                continue;
            }

            if (!ItemPlacementEquals(originalItem, currentItem))
            {
                deletedItemIds.Add(currentId);
                newItems.Add(currentItem);
                continue;
            }

            if (!JsonValueEquals(originalItem.Upd, currentItem.Upd))
            {
                changedItems.Add(currentItem);
            }
        }

        return (
            cloner.Clone(newItems) ?? newItems,
            cloner.Clone(changedItems) ?? changedItems,
            deletedItemIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private static bool ItemPlacementEquals(Item left, Item right)
    {
        return left != null
            && right != null
            && string.Equals(left.Template.ToString(), right.Template.ToString(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.ParentId, right.ParentId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.SlotId, right.SlotId, StringComparison.Ordinal)
            && JsonValueEquals(left.Location, right.Location);
    }

    private List<Item> BuildCurrentTeammateKitDeliveryItems(BotBase teammate, bool includeSecureContainer)
    {
        var items = teammate.Inventory?.Items;
        if (items == null || items.Count == 0)
        {
            return [];
        }

        string equipmentRootId = GetEquipmentRootId(teammate);
        var deliveryItems = new List<Item>();
        foreach (var slotItem in items.Where(item =>
                     item?.ParentId != null
                     && string.Equals(item.ParentId, equipmentRootId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (slotItem?.Id == null || string.IsNullOrWhiteSpace(slotItem.SlotId))
            {
                continue;
            }

            if (IsIgnoredReturnedEquipmentSlot(slotItem.SlotId, includeSecureContainer))
            {
                continue;
            }

            if (IsPocketsSlotItem(slotItem))
            {
                foreach (var pocketChild in items.Where(item =>
                             item?.ParentId != null
                             && string.Equals(item.ParentId, slotItem.Id.ToString(), StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    AddDeliveryItemTree(items, pocketChild, deliveryItems);
                }

                continue;
            }

            AddDeliveryItemTree(items, slotItem, deliveryItems);
        }

        return deliveryItems;
    }

    private void AddDeliveryItemTree(List<Item> sourceItems, Item rootItem, List<Item> deliveryItems)
    {
        if (rootItem?.Id == null || IsIgnoredKitRequirementItem(rootItem))
        {
            return;
        }

        var treeIds = GetItemTreeIds(sourceItems, rootItem.Id.ToString());
        var tree = cloner.Clone(sourceItems.Where(item => treeIds.Contains(item.Id.ToString())).ToList())
            ?? sourceItems.Where(item => treeIds.Contains(item.Id.ToString())).ToList();
        if (tree.Count == 0)
        {
            return;
        }

        tree[0].ParentId = null;
        tree[0].SlotId = null;
        tree[0].Location = null;
        deliveryItems.AddRange(tree);
    }

    private void SendPreviousTeammateKitDelivery(MongoId sessionId, BotBase teammate, List<Item> deliveryItems)
    {
        if (deliveryItems.Count == 0)
        {
            return;
        }

        mailSendService.SendMessageToPlayer(new SendMessageDetails
        {
            RecipientId = sessionId,
            Sender = MessageType.NpcTraderMessage,
            DialogType = MessageType.NpcTraderMessage,
            Trader = FriendlyCourierTraderProfile.CourierTraderIdValue,
            MessageText = "Teammate's previous kit is ready for pickup.",
            Items = deliveryItems,
            ItemsMaxStorageLifetimeSeconds = 86400,
        });

        logger.Info($"Sent {deliveryItems.Count} previous teammate kit items by delivery for '{teammate.Aid}'.");
    }

    private static bool IsIgnoredReturnedEquipmentSlot(string slotId, bool includeSecureContainer)
    {
        return slotId.Contains("Dogtag", StringComparison.OrdinalIgnoreCase)
            || (!includeSecureContainer && slotId.Contains("SecuredContainer", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPocketsSlotItem(Item item)
    {
        return string.Equals(item?.SlotId, nameof(EquipmentSlots.Pockets), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPermanentTeammateEquipmentSlot(string? slotId, bool keepSecureContainer)
    {
        return !string.IsNullOrWhiteSpace(slotId)
            && (slotId.Contains("Dogtag", StringComparison.OrdinalIgnoreCase)
                || slotId.Contains("SpecialSlot", StringComparison.OrdinalIgnoreCase)
                || (keepSecureContainer && slotId.Contains("SecuredContainer", StringComparison.OrdinalIgnoreCase))
                || string.Equals(slotId, nameof(EquipmentSlots.Pockets), StringComparison.OrdinalIgnoreCase)
                || string.Equals(slotId, nameof(EquipmentSlots.Scabbard), StringComparison.OrdinalIgnoreCase)
                || string.Equals(slotId, "ArmBand", StringComparison.OrdinalIgnoreCase)
                || string.Equals(slotId, "Armband", StringComparison.OrdinalIgnoreCase));
    }

    private void ConsumeStashItemsForKit(
        MongoId sessionId,
        PmcData profile,
        IEnumerable<FriendlyTeammateBuyKitUsedItem>? usedItems)
    {
        profile.Inventory ??= new BotBaseInventory { Items = [] };
        profile.Inventory.Items ??= [];
        List<FriendlyTeammateBuyKitUsedItem> requestedItems = usedItems?.ToList() ?? [];
        if (requestedItems.Count == 0)
        {
            return;
        }

        string playerStashRootId = GetPlayerStashRootId(profile);
        var stashIds = GetItemTreeIds(profile.Inventory.Items, playerStashRootId);
        var inventoryById = ToItemDictionary(profile.Inventory.Items);
        var selectedItems = new Dictionary<string, (Item Item, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var request in requestedItems)
        {
            if (request == null
                || string.IsNullOrWhiteSpace(request.ItemId)
                || string.IsNullOrWhiteSpace(request.TemplateId)
                || request.Count <= 0)
            {
                throw CreateKitStashSelectionException(
                    sessionId,
                    "Rejected an invalid exact player stash item selection for teammate kit purchase.");
            }

            if (!selectedItems.TryAdd(request.ItemId, default))
            {
                throw CreateKitStashSelectionException(
                    sessionId,
                    $"Player stash item '{request.ItemId}' was selected more than once for teammate kit purchase.");
            }

            if (!inventoryById.TryGetValue(request.ItemId, out Item? candidate)
                || candidate?.Id == null
                || !stashIds.Contains(request.ItemId)
                || IsIgnoredKitRequirementItem(candidate)
                || IsLockedForStashUse(candidate, inventoryById)
                || !string.Equals(candidate.Template.ToString(), request.TemplateId, StringComparison.OrdinalIgnoreCase))
            {
                throw CreateKitStashSelectionException(
                    sessionId,
                    $"Player stash item '{request.ItemId}' was unavailable, locked, outside the stash, or changed template before teammate kit purchase.");
            }

            int candidateCount = GetItemStackCount(candidate);
            if (request.Count > candidateCount)
            {
                throw CreateKitStashSelectionException(
                    sessionId,
                    $"Player stash item '{request.ItemId}' did not contain the requested quantity for teammate kit purchase.");
            }

            selectedItems[request.ItemId] = (candidate, request.Count);
        }

        var fullyConsumedIds = selectedItems
            .Where(entry => entry.Value.Count == GetItemStackCount(entry.Value.Item))
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string fullyConsumedId in fullyConsumedIds)
        {
            foreach (string descendantId in GetItemTreeIds(profile.Inventory.Items, fullyConsumedId))
            {
                if (string.Equals(descendantId, fullyConsumedId, StringComparison.OrdinalIgnoreCase)
                    || !inventoryById.TryGetValue(descendantId, out Item? descendant)
                    || IsImplicitKitTreeItem(descendant))
                {
                    continue;
                }

                if (!selectedItems.TryGetValue(descendantId, out var selectedDescendant)
                    || selectedDescendant.Count != GetItemStackCount(descendant))
                {
                    throw CreateKitStashContainerContentsException(
                        sessionId,
                        $"Player stash item '{fullyConsumedId}' contains descendant '{descendantId}' that was not selected in full.");
                }
            }
        }

        foreach (KeyValuePair<string, (Item Item, int Count)> entry in selectedItems)
        {
            int currentCount = GetItemStackCount(entry.Value.Item);
            if (entry.Value.Count >= currentCount)
            {
                continue;
            }

            if (GetItemTreeIds(profile.Inventory.Items, entry.Key).Count > 1)
            {
                throw CreateKitStashContainerContentsException(
                    sessionId,
                    $"Player stash item '{entry.Key}' was selected partially while it still contained descendants.");
            }

            entry.Value.Item.Upd ??= new Upd();
            entry.Value.Item.Upd.StackObjectsCount = currentCount - entry.Value.Count;
        }

        if (fullyConsumedIds.Count > 0)
        {
            RemoveItemTreesById(profile.Inventory.Items, fullyConsumedIds);
        }
    }

    private void DeductRoublesFromPlayerStash(PmcData profile, int amount)
    {
        profile.Inventory ??= new BotBaseInventory { Items = [] };
        profile.Inventory.Items ??= [];
        if (amount <= 0)
        {
            return;
        }

        string playerStashRootId = GetPlayerStashRootId(profile);
        var stashIds = GetItemTreeIds(profile.Inventory.Items, playerStashRootId);
        var inventoryById = ToItemDictionary(profile.Inventory.Items);
        int available = profile.Inventory.Items
            .Where(item => item?.Id != null
                && stashIds.Contains(item.Id.ToString())
                && !IsLockedForStashUse(item, inventoryById)
                && string.Equals(item.Template.ToString(), FriendlyItemTemplateIds.Currency.Roubles, StringComparison.OrdinalIgnoreCase))
            .Sum(GetItemStackCount);
        if (available < amount)
        {
            throw new FriendlyTeammateException($"Not enough roubles to buy teammate kit. Required {amount}, available {available}");
        }

        int remaining = amount;
        var removeIds = new List<string>();
        foreach (var money in profile.Inventory.Items.ToList())
        {
            if (remaining <= 0)
            {
                break;
            }

            if (money?.Id == null
                || !stashIds.Contains(money.Id.ToString())
                || IsLockedForStashUse(money, inventoryById)
                || !string.Equals(money.Template.ToString(), FriendlyItemTemplateIds.Currency.Roubles, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int stackCount = GetItemStackCount(money);
            if (stackCount > remaining)
            {
                money.Upd ??= new Upd();
                money.Upd.StackObjectsCount = stackCount - remaining;
                remaining = 0;
                break;
            }

            removeIds.Add(money.Id.ToString());
            remaining -= stackCount;
        }

        RemoveItemTreesById(profile.Inventory.Items, removeIds);
    }

    private static bool IsIgnoredKitRequirementItem(Item? item)
    {
        if (item?.Id == null || string.IsNullOrWhiteSpace(item.Template.ToString()))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(item.ParentId))
        {
            return true;
        }

        if (string.Equals(item.SlotId, "Dogtag", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsPocketsSlotItem(item);
    }

    private static int GetItemStackCount(Item item)
    {
        return Math.Max(1, (int)Math.Ceiling(item.Upd?.StackObjectsCount ?? 1d));
    }

    private bool IsImplicitKitTreeItem(Item item)
    {
        return IsIgnoredKitRequirementItem(item)
            || item?.Template.IsEmpty == false
            && itemHelper.IsOfBaseclass(item.Template, BaseClasses.BUILT_IN_INSERTS);
    }

    private FriendlyTeammateException CreateKitStashSelectionException(MongoId sessionId, string diagnostic)
    {
        logger.Warning(diagnostic);
        Dictionary<string, string> socialUiText = languageService.GetStringMap(sessionId, "socialUi");
        string fallback = GetLanguageValue(
            socialUiText,
            "KitLoadoutPurchaseFailed",
            "KitStashSelectionChanged");
        return new FriendlyTeammateException(
            GetLanguageValue(socialUiText, "KitStashSelectionChanged", fallback));
    }

    private FriendlyTeammateException CreateKitStashContainerContentsException(
        MongoId sessionId,
        string diagnostic)
    {
        logger.Warning(diagnostic);
        Dictionary<string, string> socialUiText = languageService.GetStringMap(sessionId, "socialUi");
        string fallback = GetLanguageValue(
            socialUiText,
            "KitStashSelectionChanged",
            "KitLoadoutPurchaseFailed");
        return new FriendlyTeammateException(
            GetLanguageValue(socialUiText, "KitStashContainerHasUnselectedContents", fallback));
    }

    private FriendlyTeammateException CreateKitPurchaseException(MongoId sessionId, string diagnostic)
    {
        logger.Warning(diagnostic);
        Dictionary<string, string> socialUiText = languageService.GetStringMap(sessionId, "socialUi");
        return new FriendlyTeammateException(
            GetLanguageValue(socialUiText, "KitLoadoutPurchaseFailed", "KitLoadoutPurchaseFailed"));
    }

    private static ItemEventRouterResponse CreateEmptyRepairOutput(MongoId sessionId, PmcData profile)
    {
        return new ItemEventRouterResponse
        {
            Warnings = [],
            ProfileChanges = new Dictionary<MongoId, ProfileChange>
            {
                {
                    sessionId,
                    new ProfileChange
                    {
                        Id = sessionId.ToString(),
                        Experience = profile.Info?.Experience,
                        Quests = [],
                        RagFairOffers = [],
                        WeaponBuilds = [],
                        EquipmentBuilds = [],
                        Items = new ItemChanges
                        {
                            NewItems = [],
                            ChangedItems = [],
                            DeletedItems = [],
                        },
                        Production = [],
                        Improvements = [],
                        Skills = new Skills
                        {
                            Common = [],
                            Mastering = [],
                            Points = 0,
                        },
                        Health = profile.Health ?? new BotBaseHealth(),
                        TraderRelations = [],
                        QuestsStatus = [],
                    }
                },
            },
        };
    }

    private static HashSet<string> GetItemTreeIds(List<Item> items, string rootId)
    {
        var treeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddItemTreeIds(items, rootId, treeIds);
        return treeIds;
    }

    private static void AddItemTreeIds(List<Item> items, string itemId, HashSet<string> treeIds)
    {
        if (!treeIds.Add(itemId))
        {
            return;
        }

        foreach (var child in items.Where(item =>
                     string.Equals(item.ParentId, itemId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            AddItemTreeIds(items, child.Id.ToString(), treeIds);
        }
    }

    private static Dictionary<string, Item> ToItemDictionary(IEnumerable<Item>? items)
    {
        return (items ?? [])
            .Where(item => item?.Id != null)
            .GroupBy(item => item.Id.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsLockedForStashUse(Item item, IReadOnlyDictionary<string, Item> inventoryById)
    {
        if (item == null)
        {
            return false;
        }

        if (item.Upd?.PinLockState == PinLockState.Locked)
        {
            return true;
        }

        string? parentId = item.ParentId;
        while (!string.IsNullOrWhiteSpace(parentId))
        {
            if (!inventoryById.TryGetValue(parentId, out Item? parent))
            {
                return false;
            }

            if (parent.Upd?.PinLockState == PinLockState.Locked)
            {
                return true;
            }

            parentId = parent.ParentId;
        }

        return false;
    }

    private static void ValidateLockedPlayerStashItemsUnchanged(
        List<Item> currentPlayerItems,
        List<Item> replacementEquipmentItems,
        List<Item> replacementStashItems,
        string playerStashRootId,
        string messageTemplate)
    {
        var currentById = ToItemDictionary(currentPlayerItems);
        var replacementStashById = ToItemDictionary(replacementStashItems);
        var replacementEquipmentIds = replacementEquipmentItems
            .Where(item => item?.Id != null)
            .Select(item => item.Id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var currentStashIds = GetItemTreeIds(currentPlayerItems, playerStashRootId);
        var lockedRootByBlockedId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in currentPlayerItems)
        {
            if (item?.Id == null
                || item.Upd?.PinLockState != PinLockState.Locked
                || !currentStashIds.Contains(item.Id.ToString()))
            {
                continue;
            }

            string lockedRootId = item.Id.ToString();
            foreach (string blockedId in GetItemTreeIds(currentPlayerItems, lockedRootId))
            {
                lockedRootByBlockedId.TryAdd(blockedId, lockedRootId);
            }
        }

        foreach (KeyValuePair<string, string> blockedEntry in lockedRootByBlockedId)
        {
            string id = blockedEntry.Key;
            if (!currentById.TryGetValue(id, out Item? currentItem)
                || !currentStashIds.Contains(id))
            {
                continue;
            }

            currentById.TryGetValue(blockedEntry.Value, out Item? lockedRootItem);
            if (replacementEquipmentIds.Contains(id))
            {
                throw new FriendlyTeammateException(FormatLockedStashItemMessage(messageTemplate, lockedRootItem, currentItem));
            }

            if (!replacementStashById.TryGetValue(id, out Item? replacementItem))
            {
                throw new FriendlyTeammateException(FormatLockedStashItemMessage(messageTemplate, lockedRootItem, currentItem));
            }

            if (!LockedStashItemStateEquals(currentItem, replacementItem))
            {
                throw new FriendlyTeammateException(FormatLockedStashItemMessage(messageTemplate, lockedRootItem, currentItem));
            }

            PreservePinLockState(currentItem, replacementItem);
        }
    }

    private static string FormatLockedStashItemMessage(string messageTemplate, Item? lockedRootItem, Item blockedItem)
    {
        string lockedId = lockedRootItem != null ? lockedRootItem.Id.ToString() : blockedItem.Id.ToString();
        string lockedTemplate = lockedRootItem != null ? lockedRootItem.Template.ToString() : blockedItem.Template.ToString();
        string blockedId = blockedItem.Id.ToString();
        string blockedTemplate = blockedItem.Template.ToString();

        try
        {
            return string.Format(messageTemplate, lockedId, lockedTemplate, blockedId, blockedTemplate);
        }
        catch
        {
            return string.Format(LockedStashItemBlockedFallback, lockedId, lockedTemplate, blockedId, blockedTemplate);
        }
    }

    private static bool LockedStashItemStateEquals(Item currentItem, Item replacementItem)
    {
        return currentItem != null
            && replacementItem != null
            && string.Equals(currentItem.Template.ToString(), replacementItem.Template.ToString(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(currentItem.ParentId, replacementItem.ParentId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(currentItem.SlotId, replacementItem.SlotId, StringComparison.OrdinalIgnoreCase)
            && JsonValueEquals(currentItem.Location, replacementItem.Location)
            && GetItemStackCount(currentItem) == GetItemStackCount(replacementItem);
    }

    private static void PreservePinLockState(Item currentItem, Item replacementItem)
    {
        if (currentItem?.Upd?.PinLockState == null || replacementItem == null)
        {
            return;
        }

        replacementItem.Upd ??= new Upd();
        replacementItem.Upd.PinLockState = currentItem.Upd.PinLockState;
    }

    private static bool JsonValueEquals(object? left, object? right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        return string.Equals(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right), StringComparison.Ordinal);
    }

    private static List<Item> PruneSubmittedEquipmentToRootTree(List<Item> items, out int prunedCount)
    {
        prunedCount = 0;
        if (items == null || items.Count == 0)
        {
            return [];
        }

        string rootId = items[0].Id.ToString();
        var keepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootId };
        bool changed;
        do
        {
            changed = false;
            foreach (var item in items)
            {
                if (item?.Id == null
                    || string.IsNullOrWhiteSpace(item.ParentId)
                    || !keepIds.Contains(item.ParentId)
                    || !keepIds.Add(item.Id.ToString()))
                {
                    continue;
                }

                changed = true;
            }
        }
        while (changed);

        var pruned = items
            .Where(item => item?.Id != null && keepIds.Contains(item.Id.ToString()))
            .ToList();

        prunedCount = items.Count - pruned.Count;
        return pruned;
    }

    private void ValidateRealCommitItemSet(
        List<Item> items,
        HashSet<string> allowedItemIds,
        string setName,
        bool allowGeneratedSlotDescendants = false)
    {
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var submittedById = items
            .Where(item => item?.Id != null)
            .GroupBy(item => item.Id.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            string id = item.Id.ToString();
            bool isAllowed = allowedItemIds.Contains(id);
            if (!isAllowed
                && allowGeneratedSlotDescendants
                && IsGeneratedSlotDescendantOfAllowedItem(item, submittedById, allowedItemIds))
            {
                isAllowed = true;
            }

            if (!isAllowed)
            {
                throw new FriendlyTeammateException(
                    $"Submitted {setName} contains an item that was not available for movement: id={id}, tpl={item.Template}, parent={item.ParentId}, slot={item.SlotId}");
            }

            if (!seenIds.Add(id))
            {
                throw new FriendlyTeammateException($"Submitted {setName} contains duplicate item ids");
            }
        }
    }

    private bool IsGeneratedSlotDescendantOfAllowedItem(
        Item item,
        Dictionary<string, Item> submittedById,
        HashSet<string> allowedItemIds)
    {
        // EFT and item mods can materialize non-lootable slot children while the editor reconstructs an
        // owned weapon/armor tree or while the user loads ammo into a stash weapon during the staged edit.
        // Those children do not exist in the saved JSON yet, but they are still part of an already-owned
        // parent item. Grid/location items remain blocked unless their own id came from the player stash or
        // teammate inventory; cartridge/chamber slots are the only generated children allowed with location.
        bool isGeneratedAmmoSlot = IsLoadedAmmoSlotId(item?.SlotId) && IsAmmoItem(item);

        if (item == null
            || (item.Location != null && !isGeneratedAmmoSlot)
            || string.IsNullOrWhiteSpace(item.ParentId)
            || string.IsNullOrWhiteSpace(item.SlotId)
            || string.Equals(item.SlotId, "main", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.SlotId, "hideout", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? parentId = item.ParentId;
        while (!string.IsNullOrWhiteSpace(parentId))
        {
            if (allowedItemIds.Contains(parentId))
            {
                return true;
            }

            if (!submittedById.TryGetValue(parentId, out Item? parent))
            {
                return false;
            }

            parentId = parent.ParentId;
        }

        return false;
    }

    private static bool IsLoadedAmmoSlotId(string? slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            return false;
        }

        return LoadedAmmoSlotIds.Contains(slotId)
            || slotId.StartsWith("patron_in_weapon", StringComparison.OrdinalIgnoreCase)
            || (int.TryParse(slotId, out int numericSlotId) && numericSlotId >= 0);
    }

    private bool IsAmmoItem(Item? item)
    {
        return item?.Template.IsEmpty == false && itemHelper.IsOfBaseclass(item.Template, BaseClasses.AMMO);
    }

    private static void ValidateNoRealCommitOverlap(
        List<Item> replacementEquipmentItems,
        List<Item> replacementStashItems,
        string playerStashRootId)
    {
        var equipmentIds = replacementEquipmentItems
            .Select(item => item.Id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var stashItem in replacementStashItems)
        {
            string id = stashItem.Id.ToString();
            if (string.Equals(id, playerStashRootId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (equipmentIds.Contains(id))
            {
                throw new FriendlyTeammateException("Submitted teammate equipment and player stash both contain the same moved item");
            }
        }
    }

    private static void ValidateNoEquippedPlayerItemCommit(
        PmcData playerPmc,
        List<Item> replacementEquipmentItems,
        HashSet<string> currentPlayerStashIds)
    {
        var nonStashPlayerItemIds = (playerPmc.Inventory?.Items ?? [])
            .Select(item => item.Id.ToString())
            .Where(id => !currentPlayerStashIds.Contains(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in replacementEquipmentItems)
        {
            if (nonStashPlayerItemIds.Contains(item.Id.ToString()))
            {
                throw new FriendlyTeammateException(
                    $"Submitted teammate equipment contains an item equipped on the player: id={item.Id}, tpl={item.Template}, parent={item.ParentId}, slot={item.SlotId}");
            }
        }
    }

    private HashSet<string> RemapReplacementEquipmentPlayerEquippedIdCollisions(
        PmcData playerPmc,
        List<Item> replacementEquipmentItems,
        HashSet<string> currentPlayerStashIds,
        HashSet<string> currentTeammateItemIds)
    {
        var remappedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nonStashPlayerItemIds = (playerPmc.Inventory?.Items ?? [])
            .Select(item => item.Id.ToString())
            .Where(id => !currentPlayerStashIds.Contains(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in replacementEquipmentItems)
        {
            if (item?.Id == null)
            {
                continue;
            }

            string id = item.Id.ToString();
            if (!nonStashPlayerItemIds.Contains(id) || !currentTeammateItemIds.Contains(id))
            {
                continue;
            }

            string newId = new MongoId().ToString();
            idMap[id] = newId;
            remappedIds.Add(newId);
        }

        if (idMap.Count == 0)
        {
            return remappedIds;
        }

        // Some old/legacy defaults can contain generated teammate items whose ids collide with the
        // player's currently equipped gear. Keep blocking real player-equipped transfers, but re-id
        // teammate-owned colliders so the save can repair the bad snapshot instead of staying stuck.
        foreach (var item in replacementEquipmentItems)
        {
            if (item?.Id != null && idMap.TryGetValue(item.Id.ToString(), out string? newId))
            {
                item.Id = new MongoId(newId);
            }

            if (!string.IsNullOrWhiteSpace(item?.ParentId)
                && idMap.TryGetValue(item.ParentId, out string? newParentId))
            {
                item.ParentId = newParentId;
            }
        }

        logger.Warning($"Remapped {idMap.Count} teammate-owned item id collision(s) with currently equipped player gear during real loadout commit.");
        return remappedIds;
    }

    private static string NormalizeLoadoutManagementMode(string? mode)
    {
        return string.IsNullOrWhiteSpace(mode)
            ? FriendlyServerSettingsRequest.DefaultLoadoutManagementMode
            : mode.Trim();
    }

    private static bool IsExtremeLoadoutManagementMode(string mode)
    {
        return string.Equals(mode, "Extreme", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSimpleLoadoutManagementMode(string mode)
    {
        return string.Equals(mode, "Simple", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsCurrentLoadoutManagementModeExtreme()
    {
        return IsExtremeLoadoutManagementMode(NormalizeLoadoutManagementMode(settingsService.LoadSettings().LoadoutManagementMode));
    }

    private static bool IsImmersiveLikeLoadoutManagementMode(string mode)
    {
        return string.Equals(mode, "Immersive", StringComparison.OrdinalIgnoreCase)
            || IsExtremeLoadoutManagementMode(mode);
    }

    private static bool IsRestrictedLoadoutManagementMode(string mode)
    {
        return string.Equals(mode, "Restricted", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldPersistEscapedDefaultEquipmentState(
        string mode,
        FriendlyServerSettingsRequest settings)
    {
        return IsImmersiveLikeLoadoutManagementMode(mode)
            || (IsRestrictedLoadoutManagementMode(mode) && settings?.RestrictedGearMaintenance == true);
    }

    private static bool ShouldPersistRestrictedGearMaintenanceDeathEquipmentState(
        string mode,
        FriendlyServerSettingsRequest settings)
    {
        return IsRestrictedLoadoutManagementMode(mode) && settings?.RestrictedGearMaintenance == true;
    }

    private static bool IsRealTransferLoadoutManagementMode(string mode)
    {
        return IsRestrictedLoadoutManagementMode(mode)
            || IsImmersiveLikeLoadoutManagementMode(mode);
    }

    private void EnsureFollowerHasSecureContainerSupplies(BotBase profile)
    {
        if (profile?.Inventory?.Items == null)
        {
            return;
        }

        if (!TryGetSecureContainerId(profile, out string secureContainerId))
        {
            return;
        }

        bool hasGrizzlyInBackpack = HasTemplateInBackpack(profile.Inventory.Items, FriendlyItemTemplateIds.Medical.GrizzlyMedicalKit);
        //bool hasSalewaInBackpack = HasTemplateInBackpack(profile.Inventory.Items, FriendlyItemTemplateIds.Medical.SalewaFirstAidKit);
        bool hasSurgeryKitInBackpack = HasAnyTemplateInBackpack(profile.Inventory.Items, SurgicalKitTemplateIds);

        ClearSecureContainerContents(profile.Inventory.Items, secureContainerId);

        if (!hasGrizzlyInBackpack)
        {
            AddSecureContainerSupply(profile.Inventory.Items, secureContainerId, FriendlyItemTemplateIds.Medical.GrizzlyMedicalKit);
        }

        /* if (!hasSalewaInBackpack)
        {
            AddSecureContainerSupply(profile.Inventory.Items, secureContainerId, FriendlyItemTemplateIds.Medical.SalewaFirstAidKit);
        } */

        if (!hasSurgeryKitInBackpack)
        {
            AddSecureContainerSupply(profile.Inventory.Items, secureContainerId, FriendlyItemTemplateIds.Medical.Surv12SurgicalKit);
        }

        var ammoTemplate = FindMainWeaponAmmoTemplate(profile.Inventory.Items.ToList());
        if (ammoTemplate == null || ammoTemplate.Value.IsEmpty)
        {
            return;
        }

        var ammoDetails = itemHelper.GetItem(ammoTemplate.Value);
        if (!ammoDetails.Key || ammoDetails.Value == null)
        {
            return;
        }

        var stackSize = ammoDetails.Value.Properties?.StackMaxSize ?? 0;
        if (stackSize <= 0)
        {
            return;
        }

        for (var i = 0; i < SecureContainerAmmoStackCount; i++)
        {
            profile.Inventory.Items.Add(new Item
            {
                Id = new MongoId(),
                Template = ammoTemplate.Value,
                ParentId = secureContainerId,
                SlotId = "main",
                Location = null,
                Upd = new Upd
                {
                    StackObjectsCount = stackSize,
                    SpawnedInSession = false,
                },
            });
        }
    }

    private static void AddSecureContainerSupply(List<Item> inventoryItems, string secureContainerId, string templateId)
    {
        inventoryItems.Add(new Item
        {
            Id = new MongoId(),
            Template = new MongoId(templateId),
            ParentId = secureContainerId,
            SlotId = "main",
            Location = null,
            Upd = new Upd
            {
                StackObjectsCount = 1,
                SpawnedInSession = false,
            },
        });
    }

    private void RefillFollowerMagazinesFromInventoryAmmo(BotBase profile)
    {
        var inventoryItems = profile.Inventory?.Items;
        if (inventoryItems == null || inventoryItems.Count == 0)
        {
            return;
        }

        try
        {
            var availableAmmo = GetAvailableLooseAmmoStacks(inventoryItems);
            if (availableAmmo.Count == 0)
            {
                return;
            }

            int filledRounds = 0;
            foreach (var magazine in inventoryItems.ToList())
            {
                if (magazine?.Id == null || !itemHelper.IsOfBaseclass(magazine.Template, BaseClasses.MAGAZINE))
                {
                    continue;
                }

                filledRounds += RefillMagazineFromAmmoStacks(inventoryItems, magazine, availableAmmo);
            }

            RemoveItemTreesById(
                inventoryItems,
                availableAmmo
                    .Where(stack => stack.Remaining <= 0)
                    .Select(stack => stack.Item.Id.ToString()));

            foreach (var stack in availableAmmo.Where(stack => stack.Remaining > 0))
            {
                stack.Item.Upd ??= new Upd();
                stack.Item.Upd.StackObjectsCount = stack.Remaining;
            }

            if (filledRounds > 0)
            {
                logger.Debug($"Refilled {filledRounds} follower magazine rounds from carried ammo for teammate '{profile.Aid}'.");
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"Skipped follower magazine spawn refill for teammate '{profile.Aid}': {ex.Message}");
        }
    }

    private List<AvailableAmmoStack> GetAvailableLooseAmmoStacks(List<Item> inventoryItems)
    {
        var result = new List<AvailableAmmoStack>();
        foreach (var item in inventoryItems)
        {
            if (item?.Id == null
                || item.Template.IsEmpty
                || string.IsNullOrWhiteSpace(item.SlotId)
                || IsLoadedAmmoSlotId(item.SlotId)
                || !itemHelper.IsOfBaseclass(item.Template, BaseClasses.AMMO))
            {
                continue;
            }

            int count = GetItemStackCount(item);
            if (count > 0)
            {
                result.Add(new AvailableAmmoStack(item, count));
            }
        }

        return result;
    }

    private int RefillMagazineFromAmmoStacks(List<Item> inventoryItems, Item magazine, List<AvailableAmmoStack> availableAmmo)
    {
        var magTemplateResult = itemHelper.GetItem(magazine.Template);
        var magTemplate = magTemplateResult.Key ? magTemplateResult.Value : null;
        var allowedAmmoTemplates = magTemplate?.Properties?.Cartridges?
            .SelectMany(slot => slot.Properties?.Filters ?? [])
            .SelectMany(filter => filter.Filter ?? [])
            .ToHashSet();
        int? maxCount = (int?)magTemplate?.Properties?.Cartridges?.FirstOrDefault()?.MaxCount;
        if (allowedAmmoTemplates == null || allowedAmmoTemplates.Count == 0 || maxCount is null or <= 0)
        {
            return 0;
        }

        string magazineId = magazine.Id.ToString();
        int currentCount = inventoryItems
            .Where(item => item?.ParentId == magazineId && string.Equals(item.SlotId, "cartridges", StringComparison.OrdinalIgnoreCase))
            .Sum(GetItemStackCount);
        int deficit = maxCount.Value - currentCount;
        if (deficit <= 0)
        {
            return 0;
        }

        var existingAmmoTemplates = inventoryItems
            .Where(item => item?.ParentId == magazineId && string.Equals(item.SlotId, "cartridges", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Template)
            .ToHashSet();

        int filled = 0;
        while (deficit > 0)
        {
            var ammoStack = availableAmmo
                .Where(stack => stack.Remaining > 0 && allowedAmmoTemplates.Contains(stack.Item.Template))
                .OrderByDescending(stack => existingAmmoTemplates.Contains(stack.Item.Template))
                .FirstOrDefault();
            if (ammoStack == null)
            {
                break;
            }

            int roundsToMove = Math.Min(deficit, ammoStack.Remaining);
            int roundsAdded = AddCartridgesToMagazine(inventoryItems, magazine, ammoStack.Item.Template, roundsToMove);
            if (roundsAdded <= 0)
            {
                break;
            }

            ammoStack.Remaining -= roundsAdded;
            filled += roundsAdded;
            deficit -= roundsAdded;
            existingAmmoTemplates.Add(ammoStack.Item.Template);
        }

        NormalizeMagazineCartridgeLocations(inventoryItems, magazineId);
        return filled;
    }

    private int AddCartridgesToMagazine(List<Item> inventoryItems, Item magazine, MongoId ammoTemplate, int roundsToAdd)
    {
        if (roundsToAdd <= 0)
        {
            return 0;
        }

        int maxStackSize = itemHelper.GetItem(ammoTemplate).Value?.Properties?.StackMaxSize ?? 1;
        maxStackSize = Math.Max(1, maxStackSize);
        string magazineId = magazine.Id.ToString();
        int remaining = roundsToAdd;

        foreach (var existingCartridge in inventoryItems
                     .Where(item => item?.ParentId == magazineId
                         && string.Equals(item.SlotId, "cartridges", StringComparison.OrdinalIgnoreCase)
                         && item.Template == ammoTemplate)
                     .ToList())
        {
            int currentCount = GetItemStackCount(existingCartridge);
            int room = maxStackSize - currentCount;
            if (room <= 0)
            {
                continue;
            }

            int moved = Math.Min(room, remaining);
            existingCartridge.Upd ??= new Upd();
            existingCartridge.Upd.StackObjectsCount = currentCount + moved;
            remaining -= moved;
            if (remaining <= 0)
            {
                return roundsToAdd;
            }
        }

        while (remaining > 0)
        {
            int moved = Math.Min(maxStackSize, remaining);
            inventoryItems.Add(itemHelper.CreateCartridges(magazine.Id, ammoTemplate, moved, 0));
            remaining -= moved;
        }

        return roundsToAdd;
    }

    private static void NormalizeMagazineCartridgeLocations(List<Item> inventoryItems, string magazineId)
    {
        var cartridges = inventoryItems
            .Where(item => item?.ParentId == magazineId && string.Equals(item.SlotId, "cartridges", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (cartridges.Count == 0)
        {
            return;
        }

        if (cartridges.Count == 1)
        {
            cartridges[0].Location = null;
            return;
        }

        for (var i = 0; i < cartridges.Count; i++)
        {
            cartridges[i].Location = i;
        }
    }

    private bool TryGetSecureContainerId(BotBase profile, out string secureContainerId)
    {
        secureContainerId = string.Empty;

        if (profile?.Inventory?.Items == null)
        {
            return false;
        }

        Item? secureContainer = profile.Inventory.Items.FirstOrDefault(item =>
            string.Equals(item.SlotId, nameof(EquipmentSlots.SecuredContainer), StringComparison.OrdinalIgnoreCase));
        if (secureContainer?.Id == null)
        {
            return false;
        }

        var containerId = secureContainer.Id.ToString();
        secureContainerId = containerId;
        return true;
    }

    private void ClearSecureContainerContents(List<Item> inventoryItems, string secureContainerId)
    {
        if (inventoryItems == null || string.IsNullOrWhiteSpace(secureContainerId))
        {
            return;
        }

        var ownedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { secureContainerId };
        var removedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var foundChild = true;

        while (foundChild)
        {
            foundChild = false;
            foreach (var item in inventoryItems)
            {
                if (item?.Id == null || string.IsNullOrEmpty(item.ParentId) || !ownedIds.Contains(item.ParentId))
                {
                    continue;
                }

                var itemId = item.Id.ToString();
                if (removedIds.Add(itemId))
                {
                    ownedIds.Add(itemId);
                    foundChild = true;
                }
            }
        }

        inventoryItems.RemoveAll(item => item?.Id != null && removedIds.Contains(item.Id.ToString()));
    }

    private bool HasTemplateInBackpack(List<Item> inventoryItems, string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return false;
        }

        return HasAnyTemplateInBackpack(inventoryItems, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { templateId });
    }

    private bool HasAnyTemplateInBackpack(List<Item> inventoryItems, IReadOnlySet<string> templateIds)
    {
        if (inventoryItems == null || templateIds == null || templateIds.Count == 0)
        {
            return false;
        }

        Item? backpack = inventoryItems.FirstOrDefault(item =>
            string.Equals(item.SlotId, nameof(EquipmentSlots.Backpack), StringComparison.OrdinalIgnoreCase));
        if (backpack?.Id == null)
        {
            return false;
        }

        var ownedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { backpack.Id.ToString() };
        var foundChild = true;

        while (foundChild)
        {
            foundChild = false;
            foreach (var item in inventoryItems)
            {
                if (item?.Id == null || string.IsNullOrEmpty(item.ParentId) || !ownedIds.Contains(item.ParentId))
                {
                    continue;
                }

                string itemId = item.Id.ToString();
                if (!ownedIds.Add(itemId))
                {
                    continue;
                }

                foundChild = true;

                if (templateIds.Contains(item.Template.ToString()))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private MongoId? FindMainWeaponAmmoTemplate(List<Item> inventoryItems)
    {
        var mainWeapon = inventoryItems.FirstOrDefault(item => item.SlotId == nameof(EquipmentSlots.FirstPrimaryWeapon))
            ?? inventoryItems.FirstOrDefault(item => item.SlotId == nameof(EquipmentSlots.SecondPrimaryWeapon))
            ?? inventoryItems.FirstOrDefault(item => item.SlotId == nameof(EquipmentSlots.Holster));

        if (mainWeapon?.Id == null)
        {
            return null;
        }

        foreach (var weaponChild in inventoryItems.Where(item => item.ParentId == mainWeapon.Id.ToString()))
        {
            var directAmmo = FindAmmoTemplateRecursive(inventoryItems, weaponChild);
            if (directAmmo != null)
            {
                return directAmmo;
            }
        }

        return null;
    }

    private bool HasProperRaidKit(BotBase teammate)
    {
        var items = teammate.Inventory?.Items;
        if (items == null || items.Count == 0)
        {
            return false;
        }

        foreach (var slotId in RequiredRaidWeaponSlots)
        {
            var item = items.FirstOrDefault(candidate => string.Equals(candidate.SlotId, slotId, StringComparison.OrdinalIgnoreCase));
            if (item?.Template == null)
            {
                continue;
            }

            if (itemHelper.IsOfBaseclass(item.Template, BaseClasses.WEAPON)
                && !itemHelper.IsOfBaseclass(item.Template, BaseClasses.KNIFE))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetTeammateDisplayName(BotBase teammate)
    {
        return teammate.Info?.Nickname
            ?? teammate.Aid?.ToString()
            ?? teammate.Id?.ToString()
            ?? "teammate";
    }

    private MongoId? FindAmmoTemplateRecursive(List<Item> inventoryItems, Item item)
    {
        if (itemHelper.IsOfBaseclass(item.Template, BaseClasses.AMMO))
        {
            return item.Template;
        }

        var childItems = inventoryItems.Where(child => child.ParentId == item.Id.ToString());
        foreach (var childItem in childItems)
        {
            var ammoTemplate = FindAmmoTemplateRecursive(inventoryItems, childItem);
            if (ammoTemplate != null)
            {
                return ammoTemplate;
            }
        }

        return null;
    }

    private bool IsMedicalItem(string? templateId, bool isSurgical = false)
    {
        if (string.IsNullOrEmpty(templateId))
        {
            return false;
        }

        if (isSurgical)
        {
            // Surgical item template IDs
            var surgicalTemplates = new[]
            {
                FriendlyItemTemplateIds.Medical.Surv12SurgicalKit,
                FriendlyItemTemplateIds.Medical.CmsSurgicalKit,
            };
            return surgicalTemplates.Contains(templateId);
        }
        else
        {
            // Medical item template IDs (non-surgical)
            var medicalTemplates = new[]
            {
                FriendlyItemTemplateIds.Medical.GrizzlyMedicalKit,
                FriendlyItemTemplateIds.Medical.SalewaFirstAidKit,
                FriendlyItemTemplateIds.Medical.Ai2Medkit,
                FriendlyItemTemplateIds.Medical.CarFirstAidKit,
                FriendlyItemTemplateIds.Medical.AnalginPainkillers,
                FriendlyItemTemplateIds.Medical.MorphineInjector,
                FriendlyItemTemplateIds.Medical.ArmyBandage,
                FriendlyItemTemplateIds.Medical.RegularBandage,
            };
            return medicalTemplates.Contains(templateId);
        }
    }

    public bool DeleteTeammate(MongoId sessionId, FriendlyTeammateDeleteRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.AccountId);
        return DeleteTeammate(sessionId, teammate);
    }

    public bool DeleteTeammateByProfileId(MongoId sessionId, MongoId teammateId)
    {
        var teammate = LoadTeammates(sessionId).FirstOrDefault(profile => profile.Id == teammateId);
        if (teammate == null)
        {
            return false;
        }

        return DeleteTeammate(sessionId, teammate);
    }

    public bool IsTeammateIdentity(MongoId sessionId, MongoId? profileId, string? accountId)
    {
        int? aid = null;
        if (!string.IsNullOrWhiteSpace(accountId) && int.TryParse(accountId, out var parsedAid))
        {
            aid = parsedAid;
        }

        if (profileId is null && aid is null)
        {
            return false;
        }

        return LoadTeammates(sessionId).Any(profile =>
            (profileId is not null && profile.Id == profileId)
            || (aid is not null && profile.Aid == aid.Value));
    }

    private bool DeleteTeammate(MongoId sessionId, BotBase teammate)
    {
        var filePath = GetTeammateFilePath(sessionId, teammate);
        var deleted = fileUtil.DeleteFile(filePath);
        fileUtil.DeleteFile(GetTeammateSettingsFilePath(sessionId, teammate));
        fileUtil.DeleteFile(GetDefaultEquipmentFilePath(sessionId, teammate));
        if (deleted)
        {
            logger.Info($"Deleted teammate '{teammate.Info?.Nickname}' for session '{sessionId}'");
        }

        return deleted;
    }

    private string GetPmcRole(string? side)
    {
        return side switch
        {
            Sides.Usec => Sides.PmcUsec,
            Sides.Bear => Sides.PmcBear,
            _ => throw new FriendlyTeammateException($"Unsupported teammate side '{side}'"),
        };
    }

    private PmcData GetPlayerProfile(MongoId sessionId)
    {
        var pmc = profileHelper.GetPmcProfile(sessionId);
        if (pmc?.Info?.Side is null)
        {
            throw new FriendlyTeammateException($"Unable to resolve PMC profile for session '{sessionId}'");
        }

        return pmc;
    }

    private void EnsureNicknameIsUnique(MongoId sessionId, string nickname, int? ignoreAid = null)
    {
        var exists = LoadTeammates(sessionId).Any(profile =>
            (!ignoreAid.HasValue || profile.Aid != ignoreAid.Value) &&
            string.Equals(profile.Info?.Nickname, nickname, StringComparison.OrdinalIgnoreCase)
        );

        if (exists)
        {
            throw new FriendlyTeammateException($"Teammate nickname '{nickname}' already exists");
        }
    }

    private string EnsureUniqueRecruitNickname(MongoId sessionId, string nickname)
    {
        if (!LoadTeammates(sessionId).Any(profile => string.Equals(profile.Info?.Nickname, nickname, StringComparison.OrdinalIgnoreCase)))
        {
            return nickname;
        }

        var suffix = 1;
        string candidate;
        do
        {
            candidate = $"{nickname}{suffix}";
            suffix++;
        }
        while (LoadTeammates(sessionId).Any(profile => string.Equals(profile.Info?.Nickname, candidate, StringComparison.OrdinalIgnoreCase)));

        return candidate;
    }

    private void NormalizeTeammateProfile(BotBase teammate, PmcData playerPmc)
    {
        teammate.Info ??= new CommonInfo();
        teammate.Customization ??= new CommonCustomization();
        teammate.Inventory ??= new BotBaseInventory { Items = [] };
        teammate.Stats ??= new Stats();
        teammate.Stats.Eft ??= new EftStats();
        teammate.Hideout ??= new Hideout();
        teammate.Inventory.HideoutAreaStashes ??= [];

        teammate.Info.Side = playerPmc.Info?.Side;
        teammate.Info.MemberCategory = MemberCategory.Unheard;
        teammate.Info.SelectedMemberCategory = MemberCategory.Unheard;
        teammate.Info.BannedState = playerPmc.Info?.BannedState;
        teammate.Info.BannedUntil = playerPmc.Info?.BannedUntil;
        teammate.Info.RegistrationDate = GetCurrentUnixTimestampSeconds();
        teammate.Achievements = playerPmc.Achievements;
        teammate.Stats.Eft.TotalInGameTime = 0;
        teammate.Stats.Eft.OverallCounters = new OverallCounters { Items = [] };
    }

    private static void NormalizeTeammateSkillsForCreation(BotBase teammate, PmcData playerPmc)
    {
        if (teammate?.Skills?.Common == null || playerPmc?.Skills?.Common == null)
        {
            return;
        }

        var generatedSkillProgress = teammate.Skills.Common
            .Where(skill => skill != null)
            .GroupBy(skill => skill.Id)
            .ToDictionary(group => group.Key, group => group.First().Progress);

        var playerSkillProgress = playerPmc.Skills.Common
            .Where(skill => skill != null)
            .GroupBy(skill => skill.Id)
            .ToDictionary(group => group.Key, group => group.First().Progress);

        if (playerSkillProgress.Count == 0)
        {
            return;
        }

        double playerGlobalCap = playerSkillProgress.Values.Max();

        foreach (var teammateSkill in teammate.Skills.Common)
        {
            if (teammateSkill == null)
            {
                continue;
            }

            if (!playerSkillProgress.TryGetValue(teammateSkill.Id, out var playerProgress))
            {
                teammateSkill.Progress = Math.Min(teammateSkill.Progress, playerGlobalCap);
                teammateSkill.PointsEarnedDuringSession = 0;
                continue;
            }

            teammateSkill.Progress = Math.Min(teammateSkill.Progress, playerProgress);
            teammateSkill.PointsEarnedDuringSession = 0;
        }

        ApplyRandomWeaponSpecialty(teammate.Skills.Common, playerSkillProgress, generatedSkillProgress);
    }

    private static void ApplyRandomWeaponSpecialty(
        IEnumerable<CommonSkill> teammateSkills,
        Dictionary<SkillTypes, double> playerSkillProgress,
        Dictionary<SkillTypes, double> generatedSkillProgress)
    {
        if (teammateSkills == null)
        {
            return;
        }

        var weaponSkillTypes = ResolveWeaponSkillTypes();
        if (weaponSkillTypes.Count == 0)
        {
            return;
        }

        var weaponSkills = teammateSkills
            .Where(skill => skill != null && weaponSkillTypes.Contains(skill.Id))
            .ToList();

        if (weaponSkills.Count == 0)
        {
            return;
        }

        var playerWeaponMax = weaponSkills
            .Select(skill => playerSkillProgress.TryGetValue(skill.Id, out var progress) ? progress : skill.Progress)
            .DefaultIfEmpty(0d)
            .Max();

        var specialtySkill = weaponSkills[Random.Shared.Next(weaponSkills.Count)];
        var bestOtherWeaponProgress = weaponSkills
            .Where(skill => skill != specialtySkill)
            .Select(skill => skill.Progress)
            .DefaultIfEmpty(specialtySkill.Progress)
            .Max();

        var specialtyBonus = Random.Shared.Next(125, 276);
        var specialtyCap = Math.Min(5100d, playerWeaponMax + specialtyBonus);
        var specialtyFloor = Math.Min(specialtyCap, bestOtherWeaponProgress + specialtyBonus);
        var generatedSpecialtyProgress = generatedSkillProgress.TryGetValue(specialtySkill.Id, out var generatedProgress)
            ? generatedProgress
            : specialtyFloor;

        specialtySkill.Progress = Math.Max(specialtySkill.Progress, Math.Min(generatedSpecialtyProgress, specialtyCap));
        specialtySkill.Progress = Math.Max(specialtySkill.Progress, specialtyFloor);
        specialtySkill.PointsEarnedDuringSession = 0;
    }

    private static HashSet<SkillTypes> ResolveWeaponSkillTypes()
    {
        var result = new HashSet<SkillTypes>();

        foreach (var skillName in WeaponSkillNames)
        {
            if (Enum.TryParse(skillName, ignoreCase: false, out SkillTypes skillType))
            {
                result.Add(skillType);
            }
        }

        return result;
    }

    private void ApplyFollowerProgress(BotBase teammate, FriendlyTeammateFollowerProgressRequest progressEntry)
    {
        teammate.Info ??= new CommonInfo();
        teammate.Skills ??= new Skills
        {
            Common = [],
            Mastering = [],
            Points = 0,
        };

        var gainedExperience = (int)Math.Round(progressEntry.BotExperienceSession);
        if (gainedExperience > 0)
        {
            teammate.Info.Experience += gainedExperience;
            RecalculateTeammateLevel(teammate);
        }

        ApplyFollowerLifetimeProgress(teammate, progressEntry);

        if (progressEntry.Skills == null || progressEntry.Skills.Count == 0)
        {
            return;
        }

        var commonSkills = teammate.Skills.Common?.ToList() ?? [];
        foreach (var skillEntry in progressEntry.Skills)
        {
            if (!TryParseSkillType(skillEntry.Id, out var skillType))
            {
                continue;
            }

            var progress = Math.Round(skillEntry.Current + skillEntry.Progress, 2);
            var existingSkill = commonSkills.FirstOrDefault(skill => skill.Id == skillType);
            if (existingSkill != null)
            {
                existingSkill.Progress = progress;
                continue;
            }

            commonSkills.Add(new CommonSkill
            {
                Id = skillType,
                Progress = progress,
                PointsEarnedDuringSession = 0,
                LastAccess = 0,
            });
        }

        teammate.Skills.Common = commonSkills;
    }

    private static void ApplyFollowerLifetimeProgress(BotBase teammate, FriendlyTeammateFollowerProgressRequest progressEntry)
    {
        if (progressEntry.KillCount > 0)
        {
            AddOverallCounter(teammate, progressEntry.KillCount, "Kills");
        }

        if (progressEntry.RaidSeconds > 0)
        {
            EnsureTeammateEftStats(teammate);
            teammate.Stats!.Eft!.TotalInGameTime = (teammate.Stats.Eft.TotalInGameTime ?? 0) + progressEntry.RaidSeconds;
            AddOverallCounter(teammate, progressEntry.RaidSeconds, "LifeTime", "Pmc");
        }
    }

    private static void ApplyFollowerRaidOutcomeStats(BotBase teammate, bool escaped)
    {
        AddOverallCounter(teammate, 1, "Sessions", "Pmc");
        if (escaped)
        {
            AddOverallCounter(teammate, 1, "ExitStatus", "Survived", "Pmc");
            return;
        }

        AddOverallCounter(teammate, 1, "Deaths");
    }

    private static void InitializeRecruitRaidStats(BotBase teammate, int targetLevel, int? deterministicSeed = null)
    {
        EnsureTeammateEftStats(teammate);

        var rng = deterministicSeed.HasValue ? new Random(deterministicSeed.Value) : Random.Shared;
        var level = Math.Clamp(targetLevel, 1, 79);
        var sessions = Math.Max(1, (int)Math.Round(level * RandomRange(rng, 2.5d, 5.5d) + rng.Next(0, 13)));
        var survivalRate = Math.Clamp(0.25d + level * 0.0045d + RandomRange(rng, -0.08d, 0.12d), 0.18d, 0.72d);
        var survived = Math.Clamp((int)Math.Round(sessions * survivalRate), 0, sessions);
        var deaths = Math.Max(0, sessions - survived);
        var killRate = Math.Clamp(0.55d + level * 0.035d + RandomRange(rng, -0.35d, 0.65d), 0.2d, 4.25d);
        var kills = Math.Max(0, (int)Math.Round(sessions * killRate));
        var lifetimeSeconds = Math.Max(600, (long)Math.Round(sessions * RandomRange(rng, 850d, 2100d)));

        teammate.Stats!.Eft!.TotalInGameTime = lifetimeSeconds;
        teammate.Stats.Eft.OverallCounters = new OverallCounters { Items = [] };

        AddOverallCounter(teammate, sessions, "Sessions", "Pmc");
        AddOverallCounter(teammate, survived, "ExitStatus", "Survived", "Pmc");
        AddOverallCounter(teammate, deaths, "Deaths");
        AddOverallCounter(teammate, kills, "Kills");
        AddOverallCounter(teammate, lifetimeSeconds, "LifeTime", "Pmc");
    }

    private static int GetRecruitStatsSeed(FriendlyRecruitPickupCandidate candidate)
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + StableStringHash(candidate.ProfileId);
            hash = hash * 31 + StableStringHash(candidate.AccountId);
            hash = hash * 31 + Math.Max(1, candidate.Level);
            return hash & int.MaxValue;
        }
    }

    private static int StableStringHash(string? value)
    {
        unchecked
        {
            var hash = 23;
            foreach (var ch in value ?? string.Empty)
            {
                hash = hash * 31 + ch;
            }

            return hash;
        }
    }

    private static double RandomRange(Random rng, double min, double max)
    {
        return min + rng.NextDouble() * (max - min);
    }

    private static void AddOverallCounter(BotBase teammate, double value, params string[] key)
    {
        if (value <= 0 || key == null || key.Length == 0)
        {
            return;
        }

        EnsureTeammateEftStats(teammate);
        var counters = teammate.Stats!.Eft!.OverallCounters!;
        counters.Items ??= [];

        var existing = counters.Items.FirstOrDefault(counter => CounterKeyMatches(counter?.Key, key));
        if (existing != null)
        {
            existing.Value = (existing.Value ?? 0) + value;
            return;
        }

        counters.Items.Add(new CounterKeyValue
        {
            Key = new HashSet<string>(key, StringComparer.Ordinal),
            Value = value,
        });
    }

    private static bool CounterKeyMatches(HashSet<string>? existingKey, string[] expectedKey)
    {
        if (existingKey == null || existingKey.Count != expectedKey.Length)
        {
            return false;
        }

        foreach (var keyPart in expectedKey)
        {
            if (!existingKey.Contains(keyPart))
            {
                return false;
            }
        }

        return true;
    }

    private static void EnsureTeammateEftStats(BotBase teammate)
    {
        teammate.Stats ??= new Stats();
        teammate.Stats.Eft ??= new EftStats();
        teammate.Stats.Eft.OverallCounters ??= new OverallCounters { Items = [] };
        teammate.Stats.Eft.OverallCounters.Items ??= [];
        teammate.Stats.Eft.TotalInGameTime ??= 0;
    }

    private static void ApplyDeathEscapeOutcome(BotBase teammate, FriendlyTeammateDeathEscapeEntry entry)
    {
        var bodyParts = teammate.Health?.BodyParts;
        if (bodyParts == null || bodyParts.Count == 0)
        {
            return;
        }

        double healthRatio = Math.Clamp(entry.HealthRatio, 0.05d, 1d);
        foreach (var (partName, bodyPart) in bodyParts)
        {
            var health = bodyPart?.Health;
            if (health == null)
            {
                continue;
            }

            double maximum = Math.Max(1d, health.Maximum ?? health.Current ?? 1d);
            health.Maximum = maximum;

            if (!entry.Escaped)
            {
                // Failed escape means the teammate remains dead for later roster/spawn logic.
                health.Current = 0d;
                continue;
            }

            // Successful escape preserves the raid-end health ratio while forcing vital parts above
            // zero so the teammate is considered alive by subsequent profile fetch/spawn paths.
            double minimumAlive = IsVitalBodyPart(partName) ? 1d : 0d;
            health.Current = Math.Clamp(maximum * healthRatio, minimumAlive, maximum);
        }

        if (entry.Escaped)
        {
            teammate.Health!.Hydration ??= new CurrentMinMax { Current = 100d, Maximum = 100d, Minimum = 0d };
            teammate.Health.Energy ??= new CurrentMinMax { Current = 100d, Maximum = 100d, Minimum = 0d };
            teammate.Health.Hydration.Current = Math.Max(teammate.Health.Hydration.Current ?? 0d, 1d);
            teammate.Health.Energy.Current = Math.Max(teammate.Health.Energy.Current ?? 0d, 1d);
        }
    }

    private static bool IsVitalBodyPart(string partName)
    {
        return string.Equals(partName, "Head", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(partName, "Chest", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyPmcFollowerSkillBaseline(BotBase teammate)
    {
        teammate.Info ??= new CommonInfo();
        teammate.Skills ??= new Skills
        {
            Common = [],
            Mastering = [],
            Points = 0,
        };

        var commonSkills = teammate.Skills.Common?.ToList() ?? [];
        var botLevel = Math.Max(1, teammate.Info.Level ?? 1);

        EnsureSkillProgressFloor(commonSkills, SkillTypes.AimDrills, 4500);
        EnsureSkillProgressFloor(commonSkills, SkillTypes.Health, Math.Min(40 * botLevel, 5100));
        EnsureSkillProgressFloor(commonSkills, SkillTypes.Vitality, Math.Min(30 * botLevel, 5100));
        EnsureSkillProgressFloor(commonSkills, SkillTypes.HeavyVests, Math.Min(20 * botLevel, 5100));
        EnsureSkillProgressFloor(commonSkills, SkillTypes.LightVests, Math.Min(20 * botLevel, 5100));
        EnsureSkillProgressFloor(commonSkills, SkillTypes.StressResistance, Math.Min(20 * botLevel, 5100));

        teammate.Skills.Common = commonSkills;
    }

    private static void EnsureSkillProgressFloor(List<CommonSkill> commonSkills, SkillTypes skillType, double minimumProgress)
    {
        var skill = commonSkills.FirstOrDefault(existingSkill => existingSkill.Id == skillType);
        if (skill == null)
        {
            commonSkills.Add(new CommonSkill
            {
                Id = skillType,
                Progress = minimumProgress,
                PointsEarnedDuringSession = 0,
                LastAccess = 0,
            });

            return;
        }

        skill.Progress = Math.Max(skill.Progress, minimumProgress);
    }

    private void RecalculateTeammateLevel(BotBase teammate)
    {
        if (teammate.Info == null)
        {
            return;
        }

        var accumulatedExperience = 0;
        var experienceTable = databaseService.GetGlobals().Configuration.Exp.Level.ExperienceTable;
        for (var i = 0; i < experienceTable.Length; i++)
        {
            accumulatedExperience += experienceTable[i].Experience;
            if (teammate.Info.Experience < accumulatedExperience)
            {
                break;
            }

            teammate.Info.Level = i + 1;
        }
    }

    private static bool TryParseSkillType(JsonElement idValue, out SkillTypes skillType)
    {
        skillType = default;

        if (idValue.ValueKind == JsonValueKind.String)
        {
            var rawValue = idValue.GetString();
            return !string.IsNullOrWhiteSpace(rawValue) && Enum.TryParse(rawValue, ignoreCase: true, out skillType);
        }

        if (idValue.ValueKind == JsonValueKind.Number && idValue.TryGetInt32(out var numericValue))
        {
            skillType = (SkillTypes)numericValue;
            return Enum.IsDefined(skillType);
        }

        return false;
    }

    private void LocalizeStartupRecoveryNotice(MongoId sessionId, FriendlyTeammateStartupRecoveryNotice notice)
    {
        if (notice?.Recovered != true)
        {
            return;
        }

        Dictionary<string, string> socialUiText = languageService.GetStringMap(sessionId, "socialUi");
        notice.Title = GetLanguageValue(socialUiText, "DuplicateProfileRecoveryTitle", DuplicateProfileRecoveryTitleFallback);

        string bodyTemplate = GetLanguageValue(
            socialUiText,
            "DuplicateProfileRecoveryBody",
            DuplicateProfileRecoveryBodyFallback);
        string names = notice.TeammateNames == null || notice.TeammateNames.Count == 0
            ? string.Empty
            : string.Join(", ", notice.TeammateNames.Where(name => !string.IsNullOrWhiteSpace(name)));

        try
        {
            notice.Message = string.Format(bodyTemplate, names);
        }
        catch
        {
            notice.Message = $"{bodyTemplate} {names}";
        }
    }

    private static string GetLanguageValue(Dictionary<string, string> values, string key, string fallback)
    {
        return values != null && values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private void RecoverDuplicateTeammateItemsForSession(MongoId sessionId)
    {
        string sessionKey = sessionId.ToString();
        if (duplicateRecoveryCheckedSessions.Contains(sessionKey))
        {
            return;
        }

        try
        {
            var playerPmc = GetPlayerProfile(sessionId);
            var playerItems = playerPmc.Inventory?.Items;
            if (playerItems == null || playerItems.Count == 0)
            {
                duplicateRecoveryCheckedSessions.Add(sessionKey);
                return;
            }

            string stashRootId = GetPlayerStashRootId(playerPmc);
            var playerStashIds = GetItemTreeIds(playerItems, stashRootId);
            if (playerStashIds.Count == 0)
            {
                duplicateRecoveryCheckedSessions.Add(sessionKey);
                return;
            }

            string directory = GetTeammateDirectory(sessionId);
            if (!fileUtil.DirectoryExists(directory))
            {
                duplicateRecoveryCheckedSessions.Add(sessionKey);
                return;
            }

            var recoveredNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            int removedTotal = 0;

            foreach (string profileFilePath in fileUtil.GetFiles(directory).Where(IsTeammateProfileFile))
            {
                BotBase? teammate;
                try
                {
                    teammate = jsonUtil.DeserializeFromFile<BotBase>(profileFilePath);
                }
                catch (Exception ex)
                {
                    logger.Warning($"{nameof(FriendlyTeammateService)}: skipped duplicate item recovery for unreadable teammate profile file '{profileFilePath}': {ex.Message}");
                    continue;
                }

                if (teammate?.Id is null)
                {
                    continue;
                }

                int removedProfileItems = RecoverDuplicateTeammateProfileItems(teammate, profileFilePath, playerStashIds);
                int removedDefaultItems = RecoverDuplicateDefaultEquipmentItems(sessionId, teammate, playerStashIds);
                int removedForTeammate = removedProfileItems + removedDefaultItems;
                if (removedForTeammate <= 0)
                {
                    continue;
                }

                recoveredNames.Add(GetTeammateDisplayName(teammate));
                removedTotal += removedForTeammate;
            }

            if (recoveredNames.Count > 0)
            {
                startupRecoveryNotices[sessionKey] = new FriendlyTeammateStartupRecoveryNotice
                {
                    Recovered = true,
                    RemovedItemCount = removedTotal,
                    TeammateNames = recoveredNames.ToList(),
                };
                logger.Warning($"Recovered duplicate player/teammate item IDs for session '{sessionId}' by removing {removedTotal} item(s) from teammate profile(s): {string.Join(", ", recoveredNames)}.");
            }

            duplicateRecoveryCheckedSessions.Add(sessionKey);
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to run duplicate teammate item recovery for session '{sessionId}': {ex.Message}");
        }
    }

    private int RecoverDuplicateTeammateProfileItems(BotBase teammate, string profileFilePath, HashSet<string> playerStashIds)
    {
        var items = teammate.Inventory?.Items;
        if (items == null || items.Count == 0)
        {
            return 0;
        }

        int removedCount = RemoveDuplicatePlayerStashItemTrees(
            items,
            teammate.Inventory?.Equipment?.ToString(),
            playerStashIds);
        if (removedCount <= 0)
        {
            return 0;
        }

        BackupFileBeforeRecovery(profileFilePath);
        WriteSerializedFile(profileFilePath, teammate, "teammate profile");
        logger.Warning($"Recovered teammate '{GetTeammateDisplayName(teammate)}' profile by removing {removedCount} duplicate player stash item(s).");
        return removedCount;
    }

    private int RecoverDuplicateDefaultEquipmentItems(MongoId sessionId, BotBase teammate, HashSet<string> playerStashIds)
    {
        string filePath = GetDefaultEquipmentFilePath(sessionId, teammate);
        if (!fileUtil.FileExists(filePath))
        {
            return 0;
        }

        List<Item>? items;
        try
        {
            items = jsonUtil.DeserializeFromFile<List<Item>>(filePath);
        }
        catch (Exception ex)
        {
            logger.Warning($"{nameof(FriendlyTeammateService)}: skipped duplicate item recovery for unreadable teammate default equipment file '{filePath}': {ex.Message}");
            return 0;
        }

        if (items == null || items.Count == 0)
        {
            return 0;
        }

        string? rootId = teammate.Inventory?.Equipment?.ToString()
            ?? items.FirstOrDefault(item => item?.Id != null)?.Id.ToString();
        int removedCount = RemoveDuplicatePlayerStashItemTrees(items, rootId, playerStashIds);
        if (removedCount <= 0)
        {
            return 0;
        }

        if (items.Count == 0)
        {
            logger.Warning($"{nameof(FriendlyTeammateService)}: skipped duplicate recovery write for teammate {GetTeammateDisplayName(teammate)} default equipment because no item remained.");
            return 0;
        }

        BackupFileBeforeRecovery(filePath);
        WriteSerializedFile(filePath, items, "teammate default equipment");
        logger.Warning($"Recovered teammate '{GetTeammateDisplayName(teammate)}' default equipment by removing {removedCount} duplicate player stash item(s).");
        return removedCount;
    }

    private static int RemoveDuplicatePlayerStashItemTrees(List<Item> items, string? protectedRootId, HashSet<string> playerStashIds)
    {
        if (items == null || items.Count == 0 || playerStashIds == null || playerStashIds.Count == 0)
        {
            return 0;
        }

        var duplicateRootIds = items
            .Where(item => item?.Id != null)
            .Select(item => item.Id.ToString())
            .Where(id =>
                playerStashIds.Contains(id)
                && !string.Equals(id, protectedRootId, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (duplicateRootIds.Count == 0)
        {
            return 0;
        }

        var removeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string duplicateRootId in duplicateRootIds)
        {
            foreach (string treeId in GetItemTreeIds(items, duplicateRootId))
            {
                if (!string.Equals(treeId, protectedRootId, StringComparison.OrdinalIgnoreCase))
                {
                    removeIds.Add(treeId);
                }
            }
        }

        return items.RemoveAll(item => item?.Id != null && removeIds.Contains(item.Id.ToString()));
    }

    private void RecoverTeammateProfileIfNeeded(MongoId sessionId, BotBase teammate, string profileFilePath)
    {
        if (teammate?.Aid == null)
        {
            return;
        }

        int removedProfileItems = RecoverTeammateInventoryItems(teammate);
        if (removedProfileItems > 0)
        {
            BackupFileBeforeRecovery(profileFilePath);
            WriteSerializedFile(profileFilePath, teammate, "teammate profile");
            logger.Warning($"Recovered teammate '{GetTeammateDisplayName(teammate)}' profile by removing {removedProfileItems} bad item(s).");
        }

        int removedDefaultItems = RecoverDefaultEquipmentSnapshotIfNeeded(sessionId, teammate);
        int removedTotal = removedProfileItems + removedDefaultItems;
        if (removedTotal <= 0)
        {
            return;
        }

        profileRecoveryNotices[GetRecoveryNoticeKey(sessionId, teammate)] = new FriendlyTeammateProfileRecoveryNotice
        {
            Recovered = true,
            RemovedItemCount = removedTotal,
            Message = ProfileRecoveryMessage,
        };
    }

    private int RecoverTeammateInventoryItems(BotBase teammate)
    {
        if (teammate?.Inventory?.Items == null || teammate.Inventory.Items.Count == 0)
        {
            return 0;
        }

        var recoveredItems = RecoverEquipmentItems(teammate.Inventory.Items, teammate.Inventory.Equipment?.ToString(), out int removedCount);
        if (removedCount <= 0 || recoveredItems.Count == 0)
        {
            return 0;
        }

        teammate.Inventory.Items = recoveredItems;
        teammate.Inventory.Equipment = recoveredItems.First().Id;
        return removedCount;
    }

    private int RecoverDefaultEquipmentSnapshotIfNeeded(MongoId sessionId, BotBase teammate)
    {
        string filePath = GetDefaultEquipmentFilePath(sessionId, teammate);
        if (!fileUtil.FileExists(filePath))
        {
            return 0;
        }

        List<Item>? items = jsonUtil.DeserializeFromFile<List<Item>>(filePath);
        if (items == null || items.Count == 0)
        {
            return 0;
        }

        Item? fallbackRoot = items.FirstOrDefault(item => item != null);
        string? rootId = teammate.Inventory != null
            ? teammate.Inventory.Equipment.ToString()
            : fallbackRoot?.Id.ToString();
        var recoveredItems = RecoverEquipmentItems(items, rootId, out int removedCount);
        if (removedCount <= 0)
        {
            return 0;
        }

        if (recoveredItems.Count == 0)
        {
            logger.Warning($"{nameof(FriendlyTeammateService)}: skipped recovery write for teammate {GetTeammateDisplayName(teammate)} default equipment because no valid root item remained.");
            return 0;
        }

        BackupFileBeforeRecovery(filePath);
        WriteSerializedFile(filePath, recoveredItems, "teammate default equipment");
        logger.Warning($"Recovered teammate '{GetTeammateDisplayName(teammate)}' default equipment by removing {removedCount} bad item(s).");
        return removedCount;
    }

    private List<Item> RecoverEquipmentItems(List<Item> items, string? preferredRootId, out int removedCount)
    {
        removedCount = 0;
        if (items == null || items.Count == 0)
        {
            return [];
        }

        var validById = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (item?.Id == null)
            {
                continue;
            }

            string id = item.Id.ToString();
            if (validById.ContainsKey(id) || !HasKnownTemplate(item))
            {
                continue;
            }

            validById[id] = item;
        }

        if (validById.Count == 0)
        {
            removedCount = items.Count;
            return [];
        }

        string rootId = !string.IsNullOrWhiteSpace(preferredRootId) && validById.ContainsKey(preferredRootId)
            ? preferredRootId
            : validById.Keys.First();

        var keepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootId };
        bool changed;
        do
        {
            changed = false;
            foreach (var item in validById.Values)
            {
                if (item?.Id == null
                    || string.IsNullOrWhiteSpace(item.ParentId)
                    || !keepIds.Contains(item.ParentId)
                    || !keepIds.Add(item.Id.ToString()))
                {
                    continue;
                }

                changed = true;
            }
        }
        while (changed);

        var recovered = new List<Item>();
        var recoveredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (item?.Id == null)
            {
                continue;
            }

            string id = item.Id.ToString();
            if (!keepIds.Contains(id) || !validById.ContainsKey(id) || !recoveredIds.Add(id))
            {
                continue;
            }

            recovered.Add(validById[id]);
        }

        removedCount = items.Count - recovered.Count;
        return recovered;
    }

    private bool HasKnownTemplate(Item item)
    {
        if (item?.Template == null)
        {
            return false;
        }

        try
        {
            return itemHelper.GetItem(item.Template).Key;
        }
        catch
        {
            return false;
        }
    }

    private FriendlyTeammateProfileRecoveryNotice? ConsumeProfileRecoveryNotice(MongoId sessionId, BotBase teammate)
    {
        string key = GetRecoveryNoticeKey(sessionId, teammate);
        if (!profileRecoveryNotices.TryGetValue(key, out var notice))
        {
            return null;
        }

        profileRecoveryNotices.Remove(key);
        return notice;
    }

    private static string GetRecoveryNoticeKey(MongoId sessionId, BotBase teammate)
    {
        return $"{sessionId}:{teammate?.Aid?.ToString() ?? string.Empty}";
    }

    private void BackupFileBeforeRecovery(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
        {
            return;
        }

        string? directory = System.IO.Path.GetDirectoryName(filePath);
        string fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
        string extension = System.IO.Path.GetExtension(filePath);
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        string backupPath = System.IO.Path.Combine(directory ?? string.Empty, $"{fileName}.recovery-backup-{timestamp}{extension}");

        try
        {
            System.IO.File.Copy(filePath, backupPath, overwrite: false);
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to create teammate recovery backup '{backupPath}': {ex.Message}");
        }
    }

    private void WriteSerializedFile<T>(string filePath, T value, string description)
    {
        string? json = jsonUtil.Serialize(value, indented: true);
        if (json is null)
        {
            throw new FriendlyTeammateException($"Unable to serialize recovered {description}");
        }

        fileUtil.WriteFile(filePath, json);
    }

    private List<BotBase> LoadTeammates(MongoId sessionId)
    {
        var directory = GetTeammateDirectory(sessionId);
        if (!fileUtil.DirectoryExists(directory))
        {
            return [];
        }

        var teammates = new List<BotBase>();
        foreach (var file in fileUtil.GetFiles(directory).Where(IsTeammateProfileFile))
        {
            BotBase? teammate;
            try
            {
                teammate = jsonUtil.DeserializeFromFile<BotBase>(file);
            }
            catch (Exception ex)
            {
                logger.Warning($"{nameof(FriendlyTeammateService)}: skipped unreadable teammate profile file '{file}': {ex.Message}");
                continue;
            }

            if (teammate?.Id is null)
            {
                continue;
            }

            RecoverTeammateProfileIfNeeded(sessionId, teammate, file);
            teammates.Add(teammate);
        }

        return teammates
            .OrderBy(profile => profile.Info?.RegistrationDate ?? int.MaxValue)
            .ThenBy(profile => profile.Aid ?? int.MaxValue)
            .ToList();
    }

    private static int GetCurrentUnixTimestampSeconds()
    {
        return (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private BotBase FindByAccountId(MongoId sessionId, string? accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new FriendlyTeammateException("Missing teammate accountId");
        }

        if (!int.TryParse(accountId, out var aid))
        {
            throw new FriendlyTeammateException($"Invalid teammate accountId '{accountId}'");
        }

        var teammate = LoadTeammates(sessionId).FirstOrDefault(profile => profile.Aid == aid);
        return teammate ?? throw new FriendlyTeammateException($"Unable to find teammate with accountId '{accountId}'");
    }

    private bool TryFindByAccountId(MongoId sessionId, string? accountId, out BotBase? teammate)
    {
        teammate = null;
        if (string.IsNullOrWhiteSpace(accountId) || !int.TryParse(accountId, out var aid))
        {
            return false;
        }

        teammate = LoadTeammates(sessionId).FirstOrDefault(profile => profile.Aid == aid);
        return teammate != null;
    }

    private void SaveTeammate(MongoId sessionId, BotBase teammate)
    {
        EnsureFollowerHasPockets(teammate);

        // Simple/Restricted/Immersive use a temporary managed secure container only on the spawn clone.
        // Do not persist generated meds/ammo, or a stale hidden container, into the teammate profile.
        if (!IsCurrentLoadoutManagementModeExtreme() && RemoveSecureContainerTree(teammate))
        {
            logger.Info($"Removed non-Realistic secure container tree before saving teammate '{teammate.Aid}'.");
        }

        PruneUnreachableEquipmentItems(teammate);

        var filePath = GetTeammateFilePath(sessionId, teammate);
        var json = jsonUtil.Serialize(teammate, indented: true);
        if (json is null)
        {
            throw new FriendlyTeammateException("Unable to serialize teammate profile");
        }

        fileUtil.WriteFile(filePath, json);
    }

    private string GetTeammateDirectory(MongoId sessionId)
    {
        return System.IO.Path.Combine(fileUtil.GetModPath(ModFolderName), "Resources", TeammateFolderName, sessionId.ToString());
    }

    private string GetTeammateFilePath(MongoId sessionId, BotBase teammate)
    {
        return System.IO.Path.Combine(GetTeammateDirectory(sessionId), $"{teammate.Aid}.json");
    }

    private string GetTeammateSettingsFilePath(MongoId sessionId, BotBase teammate)
    {
        return System.IO.Path.Combine(GetTeammateDirectory(sessionId), $"{teammate.Aid}-settings.json");
    }

    private string GetDefaultEquipmentFilePath(MongoId sessionId, BotBase teammate)
    {
        return System.IO.Path.Combine(GetTeammateDirectory(sessionId), $"{teammate.Aid}-equipment.json");
    }

    private string NormalizeRequiredValue(string? value, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new FriendlyTeammateException($"Missing teammate {fieldName}");
        }

        return normalized;
    }

    private int GetUniqueAccountId(MongoId sessionId)
    {
        var usedAids = new HashSet<int>(
            saveServer.GetProfiles().Values.Select(profile => profile.ProfileInfo?.Aid ?? 0).Where(aid => aid > 0)
        );

        foreach (var teammate in LoadTeammates(sessionId))
        {
            if (teammate.Aid is > 0)
            {
                usedAids.Add(teammate.Aid.Value);
            }
        }

        var teammateRoot = System.IO.Path.Combine(fileUtil.GetModPath(ModFolderName), "Resources", TeammateFolderName);
        if (fileUtil.DirectoryExists(teammateRoot))
        {
            foreach (var teammateAid in fileUtil
                .GetFiles(teammateRoot, recursive: true, searchPattern: "*.json")
                .Where(IsTeammateProfileFile)
                .Select(path => jsonUtil.DeserializeFromFile<BotBase>(path)?.Aid ?? 0)
                .Where(aid => aid > 0))
            {
                usedAids.Add(teammateAid);
            }
        }

        for (var attempts = 0; attempts < 1024; attempts++)
        {
            var candidateAid = hashUtil.GenerateAccountId();
            if (!usedAids.Contains(candidateAid))
            {
                return candidateAid;
            }
        }

        throw new FriendlyTeammateException("Unable to allocate a unique teammate account id");
    }

    public int GetRecruitAccountIdOrUnique(MongoId sessionId, string? accountId)
    {
        if (int.TryParse(accountId, out var aid) && aid > 0 && !IsAccountIdInUse(sessionId, aid))
        {
            return aid;
        }

        return GetUniqueAccountId(sessionId);
    }

    private bool IsAccountIdInUse(MongoId sessionId, int aid)
    {
        if (saveServer.GetProfiles().Values.Any(profile => profile.ProfileInfo?.Aid == aid))
        {
            return true;
        }

        if (LoadTeammates(sessionId).Any(teammate => teammate.Aid == aid))
        {
            return true;
        }

        var teammateRoot = System.IO.Path.Combine(fileUtil.GetModPath(ModFolderName), "Resources", TeammateFolderName);
        if (!fileUtil.DirectoryExists(teammateRoot))
        {
            return false;
        }

        return fileUtil
            .GetFiles(teammateRoot, recursive: true, searchPattern: "*.json")
            .Where(IsTeammateProfileFile)
            .Any(path => jsonUtil.DeserializeFromFile<BotBase>(path)?.Aid == aid);
    }

    private SearchFriendResponse ToFriendSummary(BotBase teammate)
    {
        var info = teammate.Info ?? throw new FriendlyTeammateException("Teammate profile is missing Info");
        return new SearchFriendResponse
        {
            Id = teammate.Id ?? throw new FriendlyTeammateException("Teammate profile is missing Id"),
            Aid = teammate.Aid,
            Info = new UserDialogDetails
            {
                Nickname = info.Nickname,
                Side = info.Side,
                Level = info.Level,
                MemberCategory = MemberCategory.Unheard,
                SelectedMemberCategory = MemberCategory.Unheard,
            },
        };
    }

    private object ToTeammateSummary(BotBase teammate, FriendlyTeammateSettings? settings = null)
    {
        var info = teammate.Info ?? throw new FriendlyTeammateException("Teammate profile is missing Info");
        settings ??= CreateDefaultTeammateSettings();

        return new
        {
            Id = teammate.Id ?? throw new FriendlyTeammateException("Teammate profile is missing Id"),
            Aid = teammate.Aid,
            Info = new UserDialogDetails
            {
                Nickname = info.Nickname,
                Side = info.Side,
                Level = info.Level,
                MemberCategory = MemberCategory.Unheard,
                SelectedMemberCategory = MemberCategory.Unheard,
            },
            AutoJoinEnabled = settings.AutoJoinEnabled,
            HasProperRaidKit = HasProperRaidKit(PrepareTeammateForFetch(teammate)),
        };
    }

    private UserDialogInfo ToFriendDialog(BotBase teammate)
    {
        SearchFriendResponse summary = ToFriendSummary(teammate);
        return new UserDialogInfo
        {
            Id = summary.Id,
            Aid = summary.Aid,
            Info = summary.Info,
        };
    }

    private GetOtherProfileResponse ToOtherProfileResponse(BotBase teammate)
    {
        var info = teammate.Info ?? throw new FriendlyTeammateException("Teammate profile is missing Info");
        var customization = teammate.Customization ?? new CommonCustomization();
        var inventory = teammate.Inventory ?? new BotBaseInventory { Items = [] };
        var stats = teammate.Stats ?? new Stats { Eft = new EftStats() };

        return new GetOtherProfileResponse
        {
            Id = teammate.Id,
            Aid = teammate.Aid,
            Info = new OtherProfileInfo
            {
                Nickname = info.Nickname,
                Side = info.Side,
                Experience = info.Experience,
                MemberCategory = (int)MemberCategory.Unheard,
                BannedState = info.BannedState,
                BannedUntil = info.BannedUntil,
                RegistrationDate = info.RegistrationDate,
            },
            Customization = new OtherProfileCustomization
            {
                Head = customization.Head,
                Body = customization.Body,
                Feet = customization.Feet,
                Hands = customization.Hands,
                Dogtag = customization.DogTag,
                Voice = customization.Voice,
            },
            Skills = teammate.Skills,
            Equipment = new OtherProfileEquipment
            {
                Id = inventory.Equipment?.ToString(),
                Items = inventory.Items ?? [],
            },
            Achievements = teammate.Achievements,
            FavoriteItems = [],
            PmcStats = new OtherProfileStats
            {
                Eft = new OtherProfileSubStats
                {
                    TotalInGameTime = stats.Eft?.TotalInGameTime,
                    OverAllCounters = stats.Eft?.OverallCounters,
                },
            },
            ScavStats = new OtherProfileStats
            {
                Eft = new OtherProfileSubStats
                {
                    TotalInGameTime = stats.Eft?.TotalInGameTime,
                    OverAllCounters = stats.Eft?.OverallCounters,
                },
            },
            Hideout = teammate.Hideout ?? new Hideout(),
            CustomizationStash = inventory.HideoutCustomizationStashId?.ToString() ?? string.Empty,
            HideoutAreaStashes = inventory.HideoutAreaStashes ?? [],
            Items = [],
        };
    }

    private GroupCharacter ToGroupCharacter(BotBase teammate)
    {
        var info = teammate.Info ?? throw new FriendlyTeammateException("Teammate profile is missing Info");
        var customization = teammate.Customization ?? new CommonCustomization();
        var inventory = teammate.Inventory ?? new BotBaseInventory { Items = [] };

        return new GroupCharacter
        {
            Id = teammate.Id?.ToString(),
            Aid = teammate.Aid,
            Info = new CharacterInfo
            {
                Nickname = info.Nickname,
                SavageNickname = info.Nickname,
                Side = info.Side,
                Level = info.Level,
                MemberCategory = MemberCategory.Unheard,
                GameVersion = info.GameVersion,
                HasCoopExtension = info.HasCoopExtension,
            },
            VisualRepresentation = new PlayerVisualRepresentation
            {
                Info = new VisualInfo
                {
                    Nickname = info.Nickname,
                    Side = info.Side,
                    Level = info.Level,
                    MemberCategory = MemberCategory.Unheard,
                    GameVersion = info.GameVersion,
                },
                Customization = new SPTarkov.Server.Core.Models.Eft.Match.Customization
                {
                    Head = customization.Head?.ToString(),
                    Body = customization.Body?.ToString(),
                    Feet = customization.Feet?.ToString(),
                    Hands = customization.Hands?.ToString(),
                },
                Equipment = new SPTarkov.Server.Core.Models.Eft.Match.Equipment
                {
                    Id = inventory.Equipment?.ToString(),
                    Items = inventory.Items ?? [],
                },
            },
            IsLeader = false,
            IsReady = true,
            LookingGroup = false,
            Region = string.Empty,
        };
    }

    private void ApplyTemporaryHealthMultiplier(BotBase teammate, double? healthMultiplier)
    {
        if (healthMultiplier is null || Math.Abs(healthMultiplier.Value - 1d) < 0.0001d)
        {
            return;
        }

        var bodyParts = teammate.Health?.BodyParts;
        if (bodyParts == null)
        {
            return;
        }

        foreach (var bodyPart in bodyParts.Values)
        {
            var health = bodyPart?.Health;
            if (health?.Maximum == null)
            {
                continue;
            }

            health.Maximum *= healthMultiplier.Value;
            health.Current = health.Maximum;
        }
    }

    private bool IsTeammateProfileFile(string path)
    {
        if (!string.Equals(fileUtil.GetFileExtension(path), "json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fileName = System.IO.Path.GetFileName(path);
        string accountId = System.IO.Path.GetFileNameWithoutExtension(fileName);
        return int.TryParse(accountId, out _);
    }

    private FriendlyTeammateSettings GetTeammateSettings(MongoId sessionId, BotBase teammate)
    {
        string filePath = GetTeammateSettingsFilePath(sessionId, teammate);
        if (!fileUtil.FileExists(filePath))
        {
            return CreateDefaultTeammateSettings();
        }

        FriendlyTeammateSettings? settings = jsonUtil.DeserializeFromFile<FriendlyTeammateSettings>(filePath);
        FriendlyTeammateSettings loadedSettings = settings ?? CreateDefaultTeammateSettings();

        if (string.IsNullOrWhiteSpace(loadedSettings.SelectedLoadoutId))
        {
            loadedSettings.SelectedLoadoutId = DefaultLoadoutId;
        }
        loadedSettings.Aggression = NormalizeAggression(loadedSettings.Aggression);
        loadedSettings.CombatTactic = NormalizeCombatTactic(loadedSettings.CombatTactic);
        return loadedSettings;
    }

    private static FriendlyTeammateSettings CreateDefaultTeammateSettings()
    {
        return new FriendlyTeammateSettings
        {
            SelectedLoadoutId = DefaultLoadoutId,
            AutoJoinEnabled = false,
            Aggression = 50f,
            CombatTactic = "Rifleman",
        };
    }

    private void SaveTeammateSettings(MongoId sessionId, BotBase teammate, FriendlyTeammateSettings settings)
    {
        string filePath = GetTeammateSettingsFilePath(sessionId, teammate);
        string? json = jsonUtil.Serialize(settings, indented: true);
        if (json is null)
        {
            throw new FriendlyTeammateException("Unable to serialize teammate settings");
        }

        fileUtil.WriteFile(filePath, json);
    }

    private void SaveDefaultEquipmentSnapshot(
        MongoId sessionId,
        BotBase teammate,
        bool overwrite = false,
        bool includeSecureContainer = false)
    {
        teammate.Inventory ??= new BotBaseInventory { Items = [] };

        string filePath = GetDefaultEquipmentFilePath(sessionId, teammate);
        if (!overwrite && fileUtil.FileExists(filePath))
        {
            return;
        }

        var items = cloner.Clone(teammate.Inventory.Items ?? []) ?? [];
        if (!includeSecureContainer)
        {
            RemoveSecureContainerTree(items);
        }

        PruneUnreachableEquipmentItems(items, teammate.Inventory?.Equipment?.ToString());

        string? json = jsonUtil.Serialize(items, indented: true);
        if (json is null)
        {
            throw new FriendlyTeammateException("Unable to serialize teammate default equipment");
        }

        fileUtil.WriteFile(filePath, json);
    }

    private void RestoreDefaultEquipment(MongoId sessionId, BotBase teammate)
    {
        string filePath = GetDefaultEquipmentFilePath(sessionId, teammate);
        if (!fileUtil.FileExists(filePath))
        {
            throw new FriendlyTeammateException("Teammate default equipment snapshot is missing");
        }

        List<Item>? items = jsonUtil.DeserializeFromFile<List<Item>>(filePath);
        if (items == null || items.Count == 0)
        {
            throw new FriendlyTeammateException("Unable to load teammate default equipment");
        }

        teammate.Inventory ??= new BotBaseInventory();
        teammate.Inventory.Items = cloner.Clone(items) ?? items;
        teammate.Inventory.Equipment = teammate.Inventory.Items.First().Id;
    }

    private List<EquipmentBuild> GetCustomEquipmentBuilds(SptProfile profile)
    {
        return profile.UserBuildData?.EquipmentBuilds?
            .Where(build => build.BuildType == EquipmentBuildType.Custom)
            .Where(build => !string.IsNullOrWhiteSpace(build.Name))
            .ToList()
            ?? [];
    }

    private string NormalizeCurrentLoadoutId(SptProfile profile, string? selectedLoadoutId)
    {
        if (string.IsNullOrWhiteSpace(selectedLoadoutId)
            || string.Equals(selectedLoadoutId, DefaultLoadoutId, StringComparison.OrdinalIgnoreCase))
        {
            return DefaultLoadoutId;
        }

        bool exists = GetCustomEquipmentBuilds(profile)
            .Any(build => string.Equals(build.Id.ToString(), selectedLoadoutId, StringComparison.OrdinalIgnoreCase));

        return exists ? selectedLoadoutId : DefaultLoadoutId;
    }

    private static float NormalizeAggression(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 50f;
        }

        return Math.Clamp(value, 0f, 100f);
    }

    private static float GetDefaultAggressionForTactic(string tactic)
    {
        return string.Equals(tactic, "Marksman", StringComparison.OrdinalIgnoreCase)
            ? 30f
            : 50f;
    }

    private static string NormalizeCombatTactic(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Rifleman";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "marksman" => "Marksman",
            "protector" => "Rifleman",
            "guard" => "Rifleman",
            "holder" => "Rifleman",
            "support" => "Rifleman",
            "assist" => "Rifleman",
            "rifleman" => "Rifleman",
            "balanced" => "Rifleman",
            "default" => "Rifleman",
            "pusher" => "Rifleman",
            _ => "Rifleman",
        };
    }

    private void ApplyEquipmentBuild(BotBase teammate, EquipmentBuild equipmentBuild, PmcData playerPmc)
    {
        if (equipmentBuild.Items == null || equipmentBuild.Items.Count == 0)
        {
            throw new FriendlyTeammateException("Teammate equipment build has no items");
        }

        var clonedBuild = cloner.Clone(equipmentBuild.Items) ?? equipmentBuild.Items;
        var normalizedBuild = itemHelper.ReplaceIDs(clonedBuild, playerPmc).ToList();
        MongoId rootId = normalizedBuild.First().Id;
        teammate.Inventory ??= new BotBaseInventory { Items = [] };
        teammate.Inventory.Items = MergeEquipmentWithPreservedSpecialItems(teammate.Inventory.Items, normalizedBuild);
        teammate.Inventory.Equipment = rootId;
    }

    private List<Item> MergeEquipmentWithPreservedSpecialItems(
        List<Item>? existingItems,
        List<Item> replacementItems,
        bool useReplacementSecureContainer = false)
    {
        if (replacementItems == null || replacementItems.Count == 0)
        {
            throw new FriendlyTeammateException("Replacement teammate equipment items are missing");
        }

        var replacementSource = replacementItems.ToList();
        if (!useReplacementSecureContainer)
        {
            RemoveSecureContainerTree(replacementSource);
        }

        MongoId rootId = replacementSource.First().Id;
        var specialItems = (existingItems ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.SlotId))
            .Where(item =>
                item.SlotId!.Contains("Dogtag", StringComparison.OrdinalIgnoreCase)
                || (!useReplacementSecureContainer && item.SlotId.Contains("SecuredContainer", StringComparison.OrdinalIgnoreCase)))
            .Select(item => cloner.Clone(item) ?? item)
            .ToList();

        var mergedItems = replacementSource
            .Where(item =>
                string.IsNullOrWhiteSpace(item.SlotId)
                || ((useReplacementSecureContainer || !item.SlotId.Contains("SecuredContainer", StringComparison.OrdinalIgnoreCase))
                    && !item.SlotId.Contains("Dogtag", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        MongoId? pocketsId = mergedItems.FirstOrDefault(item =>
            string.Equals(item.SlotId, "Pockets", StringComparison.OrdinalIgnoreCase))?.Id;

        foreach (var item in specialItems)
        {
            item.ParentId = item.SlotId != null && item.SlotId.Contains("SpecialSlot", StringComparison.OrdinalIgnoreCase)
                ? pocketsId ?? rootId
                : rootId;
            mergedItems.Add(item);
        }

        PruneUnreachableEquipmentItems(mergedItems, rootId.ToString());
        return mergedItems;
    }

    private static int PruneUnreachableEquipmentItems(BotBase teammate)
    {
        return PruneUnreachableEquipmentItems(teammate?.Inventory?.Items, teammate?.Inventory?.Equipment?.ToString());
    }

    private static int PruneUnreachableEquipmentItems(List<Item>? inventoryItems, string? preferredRootId)
    {
        if (inventoryItems == null || inventoryItems.Count == 0)
        {
            return 0;
        }

        string? rootId = !string.IsNullOrWhiteSpace(preferredRootId)
            ? preferredRootId
            : inventoryItems.FirstOrDefault(item => item?.Id != null)?.Id.ToString();
        if (string.IsNullOrWhiteSpace(rootId))
        {
            return 0;
        }

        var keepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootId };
        bool foundChild = true;
        while (foundChild)
        {
            foundChild = false;
            foreach (var item in inventoryItems)
            {
                if (item?.Id == null || string.IsNullOrWhiteSpace(item.ParentId) || !keepIds.Contains(item.ParentId))
                {
                    continue;
                }

                if (keepIds.Add(item.Id.ToString()))
                {
                    foundChild = true;
                }
            }
        }

        return inventoryItems.RemoveAll(item => item?.Id == null || !keepIds.Contains(item.Id.ToString()));
    }

}

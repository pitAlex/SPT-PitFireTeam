using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using pitTeam.Components;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace pitTeam.Modules
{
    internal class FollowerDeathEscapeOutcomeRequest
    {
        public bool Notify { get; set; } = true;
        public bool ResolveOnly { get; set; }
        public List<FollowerDeathEscapeOutcomeEntry> Entries { get; set; } = new List<FollowerDeathEscapeOutcomeEntry>();
    }

    internal class FollowerDeathEscapeOutcomeResponse
    {
        public List<FollowerDeathEscapeOutcomeEntry> Entries { get; set; } = new List<FollowerDeathEscapeOutcomeEntry>();
    }

    internal class FollowerDeathEscapeBodyResponse<T>
    {
        public int err { get; set; }
        public string errmsg { get; set; }
        public T data { get; set; }
    }

    internal class FollowerDeathEscapeOutcomeEntry
    {
        public string Aid { get; set; } = string.Empty;
        public string ProfileId { get; set; } = string.Empty;
        public string Nickname { get; set; } = string.Empty;
        public bool Escaped { get; set; }
        public bool RollEscape { get; set; }
        public float Chance { get; set; }
        public string ExtractName { get; set; } = string.Empty;
        public float Distance { get; set; }
        public float HealthRatio { get; set; }
        public float EquipmentPower { get; set; }
        public float EnemyAveragePower { get; set; }
        public float RouteEnemyAveragePower { get; set; }
        public float CurrentFightEnemyAveragePower { get; set; }
        public int RouteEnemyCount { get; set; }
        public int CurrentFightEnemyCount { get; set; }
        public int AliveSquadmates { get; set; }
        public bool HasSecureMeds { get; set; }
        public bool VitalsDestroyed { get; set; }
        public JsonType.FlatItem[] EquipmentItems { get; set; }
        public string[] TrackedItemIds { get; set; }
    }

    internal static partial class FollowerDeathEscapeResolver
    {
        private const string OutcomeRoute = "/singleplayer/pitfireteam/teammate/raid-outcomes";
        private const string LostOnDeathRoute = "/singleplayer/pitfireteam/lostondeath";
        private const float DeathGearRecoveryDistance = 70f;
        private const float FallenTeammateSnapshotRadius = 50f;

        private static readonly EBodyPart[] HealthParts =
        {
            EBodyPart.Head,
            EBodyPart.Chest,
            EBodyPart.Stomach,
            EBodyPart.LeftArm,
            EBodyPart.RightArm,
            EBodyPart.LeftLeg,
            EBodyPart.RightLeg
        };

        public static void ResolveAndSend(pitAIBossPlayer boss, List<BotFollowerPlayer> followers)
        {
            if (boss == null)
            {
                return;
            }

            followers ??= new List<BotFollowerPlayer>();
            bool hasFallenSnapshots = HasFallenSquadmateSnapshots();
            if (followers.Count == 0 && !hasFallenSnapshots)
            {
                return;
            }

            try
            {
                bool teamEscapeEnabled = pitFireTeam.teamEscape?.Value == true;

                // Include all squadmates in the result so already-dead followers appear in the summary,
                // but only followers still alive at player death get an escape roll.
                List<BotFollowerPlayer> squadmates = followers
                    .Where(follower => follower?.GetBot() != null && follower.IsSquadMate)
                    .ToList();

                if (squadmates.Count > 0)
                {
                    // Team Escape is a player-death raid-end path; persist raid-earned progress here
                    // while live follower state is still available. Boss cleanup may call this again,
                    // but BossPlayers de-duplicates per follower profile for the raid.
                    BossPlayers.SaveFollowersProgress(squadmates);
                }

                List<BotFollowerPlayer> aliveSquadmates = squadmates
                    .Where(follower =>
                    {
                        BotOwner bot = follower?.GetBot();
                        return bot != null &&
                               bot.BotState == EBotState.Active &&
                               bot.GetPlayer != null &&
                               bot.HealthController?.IsAlive == true;
                    })
                    .ToList();
                int aliveCount = aliveSquadmates.Count;

                if (squadmates.Count == 0)
                {
                    List<FollowerDeathEscapeOutcomeEntry> fallenEntries = new List<FollowerDeathEscapeOutcomeEntry>();
                    AddMissingFallenSquadmateOutcomes(fallenEntries);
                    if (fallenEntries.Count > 0)
                    {
                        Logger.LogInfo("[DeathEscape] Player died with no live squadmate followers; persisting fallen squadmate loss outcomes.");
                        SendOutcomes(fallenEntries);
                    }
                    else
                    {
                        Logger.LogInfo("[DeathEscape] Player died, but no squadmate followers were available for escape resolution.");
                    }

                    return;
                }

                // Shared context is captured once before follower dismissal and raid cleanup can invalidate
                // bot ownership, extract objects, or group enemy state.
                Vector3 deathPosition = boss.realPlayer?.Position ?? boss.Position;

                if (!teamEscapeEnabled)
                {
                    // Team Escape controls whether surviving followers get an escape roll after
                    // player death. With it disabled, live squadmates do not roll or recover gear,
                    // but they still need a lost raid outcome so sessions/deaths stay accurate.
                    Logger.LogInfo("[DeathEscape] Player died, Team Escape is disabled; persisting live and fallen squadmate loss outcomes.");
                    List<FollowerDeathEscapeOutcomeEntry> lostEntries = CreateLostOutcomesForSquadmates(squadmates, aliveCount);
                    AddMissingFallenSquadmateOutcomes(lostEntries);
                    if (lostEntries.Count > 0)
                    {
                        SendOutcomes(lostEntries);
                    }

                    return;
                }

                ExtractSnapshot extract = ChooseExtract(boss, deathPosition);
                LostOnDeathRules lostOnDeathRules = LoadLostOnDeathRules();
                HashSet<string> trackedLootIds = BuildTrackedLootIdSet(squadmates.Select(follower => follower.GetBot()));
                List<RecoverableGearCandidate> deathGearSnapshot = CreateDeathGearSnapshot(
                    squadmates.Select(follower => follower.GetBot()),
                    boss.realPlayer,
                    trackedLootIds,
                    deathPosition,
                    lostOnDeathRules);
                List<RecoverableGearCandidate> trackedLootSnapshot = CreateTrackedLootRecoverySnapshot(
                    squadmates.Select(follower => follower.GetBot()));

                Logger.LogInfo(
                    $"[DeathEscape] Resolving follower escape rolls. squadmates={squadmates.Count} alive={aliveCount} " +
                    $"extract='{extract.Name}' distance={extract.Distance:0.0}");

                List<FollowerDeathEscapeOutcomeEntry> entries = new List<FollowerDeathEscapeOutcomeEntry>();
                List<BotOwner> escapedBots = new List<BotOwner>();
                Dictionary<string, BotOwner> entryBotsByAid = new Dictionary<string, BotOwner>(StringComparer.Ordinal);
                Dictionary<string, BotOwner> liveBotsByAid = new Dictionary<string, BotOwner>(StringComparer.Ordinal);

                // Each follower rolls independently. Squad count and extraction route are shared inputs,
                // while health, gear power, and secure meds are follower-specific.
                foreach (BotFollowerPlayer follower in squadmates)
                {
                    BotOwner bot = follower.GetBot();
                    FollowerReadiness readiness = SnapshotReadiness(bot);
                    bool alive = bot.BotState == EBotState.Active &&
                                 bot.GetPlayer != null &&
                                 bot.HealthController?.IsAlive == true;
                    RouteThreatSnapshot routeThreat = alive
                        ? CalculateRouteEnemyAveragePower(boss, bot.Position, extract)
                        : RouteThreatSnapshot.Empty;
                    RouteThreatSnapshot fightThreat = alive
                        ? CalculateCurrentFightEnemyAveragePower(boss, squadmates, bot, deathPosition)
                        : RouteThreatSnapshot.Empty;
                    string aid = bot.Profile?.AccountId ?? string.Empty;
                    if (alive && !string.IsNullOrWhiteSpace(aid))
                    {
                        liveBotsByAid[aid] = bot;
                    }

                    entries.Add(new FollowerDeathEscapeOutcomeEntry
                    {
                        Aid = aid,
                        ProfileId = bot.ProfileId ?? string.Empty,
                        Nickname = bot.Profile?.Nickname ?? "Squadmate",
                        Escaped = false,
                        RollEscape = alive,
                        Chance = 0f,
                        ExtractName = extract.Name,
                        Distance = extract.Distance,
                        HealthRatio = readiness.HealthRatio,
                        EquipmentPower = readiness.EquipmentPower,
                        EnemyAveragePower = Mathf.Max(routeThreat.AveragePower, fightThreat.AveragePower),
                        RouteEnemyAveragePower = routeThreat.AveragePower,
                        CurrentFightEnemyAveragePower = fightThreat.AveragePower,
                        RouteEnemyCount = routeThreat.Count,
                        CurrentFightEnemyCount = fightThreat.Count,
                        AliveSquadmates = aliveCount,
                        HasSecureMeds = readiness.HasSecureMeds,
                        VitalsDestroyed = readiness.VitalsDestroyed,
                        EquipmentItems = null,
                        TrackedItemIds = alive ? GetTrackedFollowerItemIds(bot) : Array.Empty<string>()
                    });
                }

                AddMissingFallenSquadmateOutcomes(entries);
                entries = ResolveEscapeRollsOnServer(entries);

                foreach (FollowerDeathEscapeOutcomeEntry entry in entries)
                {
                    bool serverRolledEscape = entry.RollEscape;
                    entry.RollEscape = false;
                    if (entry.Escaped && !string.IsNullOrWhiteSpace(entry.Aid) && liveBotsByAid.TryGetValue(entry.Aid, out BotOwner bot))
                    {
                        escapedBots.Add(bot);
                        entryBotsByAid[entry.Aid] = bot;
                    }

                    Logger.LogInfo(
                        $"[DeathEscape] Server roll follower='{entry.Nickname ?? "Squadmate"}' rollEscape={serverRolledEscape} " +
                        $"escaped={entry.Escaped} chance={entry.Chance:P0} health={entry.HealthRatio:P0} " +
                        $"gear={entry.EquipmentPower:0.0} routeEnemies={entry.RouteEnemyCount} " +
                        $"routeEnemyAvgPower={entry.RouteEnemyAveragePower:0.0} fightEnemies={entry.CurrentFightEnemyCount} " +
                        $"fightEnemyAvgPower={entry.CurrentFightEnemyAveragePower:0.0} secureMeds={entry.HasSecureMeds}");
                }

                ApplyDeathGearRecoveryToEscapedEquipment(
                    entries,
                    entryBotsByAid,
                    escapedBots,
                    deathGearSnapshot,
                    trackedLootSnapshot);

                SendOutcomes(entries);
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to resolve follower death escape outcomes");
                Logger.LogError(ex);
            }
            finally
            {
                ClearFallenSquadmateSnapshots();
            }
        }

        private static List<FollowerDeathEscapeOutcomeEntry> CreateLostOutcomesForSquadmates(List<BotFollowerPlayer> squadmates, int aliveCount)
        {
            List<FollowerDeathEscapeOutcomeEntry> entries = new List<FollowerDeathEscapeOutcomeEntry>();
            if (squadmates == null || squadmates.Count == 0)
            {
                return entries;
            }

            HashSet<string> seenAids = new HashSet<string>(StringComparer.Ordinal);
            foreach (BotFollowerPlayer follower in squadmates)
            {
                BotOwner bot = follower?.GetBot();
                if (bot == null)
                {
                    continue;
                }

                string aid = bot.Profile?.AccountId ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(aid) && !seenAids.Add(aid))
                {
                    continue;
                }

                FollowerReadiness readiness = SnapshotReadiness(bot);
                entries.Add(new FollowerDeathEscapeOutcomeEntry
                {
                    Aid = aid,
                    ProfileId = bot.ProfileId ?? string.Empty,
                    Nickname = bot.Profile?.Nickname ?? "Squadmate",
                    Escaped = false,
                    RollEscape = false,
                    Chance = 0f,
                    ExtractName = string.Empty,
                    Distance = 0f,
                    HealthRatio = readiness.HealthRatio,
                    EquipmentPower = readiness.EquipmentPower,
                    EnemyAveragePower = 0f,
                    RouteEnemyAveragePower = 0f,
                    CurrentFightEnemyAveragePower = 0f,
                    RouteEnemyCount = 0,
                    CurrentFightEnemyCount = 0,
                    AliveSquadmates = aliveCount,
                    HasSecureMeds = readiness.HasSecureMeds,
                    VitalsDestroyed = readiness.VitalsDestroyed,
                    EquipmentItems = null,
                    TrackedItemIds = Array.Empty<string>()
                });
            }

            return entries;
        }

        private static FollowerReadiness SnapshotReadiness(BotOwner bot)
        {
            float current = 0f;
            float maximum = 0f;
            bool vitalsDestroyed = false;

            foreach (EBodyPart part in HealthParts)
            {
                try
                {
                    ValueStruct health = bot.GetPlayer.ActiveHealthController.GetBodyPartHealth(part, false);
                    current += Mathf.Max(0f, health.Current);
                    maximum += Mathf.Max(0f, health.Maximum);

                    if ((part == EBodyPart.Head || part == EBodyPart.Chest) && health.Current <= 0f)
                    {
                        vitalsDestroyed = true;
                    }
                }
                catch
                {
                    // Missing body-part data should not break raid-end cleanup.
                }
            }

            return new FollowerReadiness
            {
                HealthRatio = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f,
                EquipmentPower = bot.AIData?.PowerOfEquipment ?? 0f,
                HasSecureMeds = HasSecureContainerMeds(bot),
                VitalsDestroyed = vitalsDestroyed
            };
        }

        private static bool HasSecureContainerMeds(BotOwner bot)
        {
            try
            {
                Item secureContainer = bot.GetPlayer?.InventoryController?.Inventory?.Equipment?
                    .GetSlot(EquipmentSlot.SecuredContainer)?.ContainedItem;
                if (secureContainer is not EFT.InventoryLogic.SearchableItem searchable)
                {
                    return false;
                }

                foreach (Item item in searchable.GetAllItems())
                {
                    if (item == null)
                    {
                        continue;
                    }

                    if (item is EFT.InventoryLogic.Stimulator)
                    {
                        return true;
                    }

                    MedKitComponent medKit = item.GetItemComponent<MedKitComponent>();
                    if (medKit != null && medKit.HpResource > 0f)
                    {
                        return true;
                    }

                    if (item is EFT.InventoryLogic.Meds)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to inspect follower secure-container meds");
                Logger.LogError(ex);
            }

            return false;
        }

        private static ExtractSnapshot ChooseExtract(pitAIBossPlayer boss, Vector3 deathPosition)
        {
            try
            {
                ExfiltrationPoint[] candidates = GetDeathEscapeExtractCandidates(boss);
                if (candidates == null || candidates.Length == 0)
                {
                    return ExtractSnapshot.Unknown;
                }

                // Prefer simple normal extracts with no active requirements. This is a simulation of
                // a practical squad escape route, not a full key/switch/paid-extract planner.
                ExfiltrationPoint selected = candidates
                    .Where(IsUsableExtract)
                    .OrderBy(point => Vector3.Distance(deathPosition, point.transform.position))
                    .FirstOrDefault();

                if (selected == null)
                {
                    selected = candidates
                        .Where(point => point != null && point.Status != EExfiltrationStatus.NotPresent)
                        .OrderBy(point => Vector3.Distance(deathPosition, point.transform.position))
                        .FirstOrDefault();
                }

                if (selected == null)
                {
                    return ExtractSnapshot.Unknown;
                }

                return new ExtractSnapshot
                {
                    Name = selected.Settings?.Name ?? string.Empty,
                    Distance = Vector3.Distance(deathPosition, selected.transform.position),
                    Position = selected.transform.position,
                    HasPosition = true
                };
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to choose follower death escape extract");
                Logger.LogError(ex);
                return ExtractSnapshot.Unknown;
            }
        }

        private static ExfiltrationPoint[] GetDeathEscapeExtractCandidates(pitAIBossPlayer boss)
        {
            CommonAssets.Scripts.Game.ExfiltrationController controller = CommonAssets.Scripts.Game.ExfiltrationController.Instance;
            if (controller == null)
            {
                return Array.Empty<ExfiltrationPoint>();
            }

            bool useAnyExtract = pitFireTeam.teamEscapeUseAnyExtract?.Value != false;
            ExfiltrationPoint[] candidates;

            if (useAnyExtract)
            {
                // Forgiving mode: the squad may route to any normal map extract that is present and usable.
                candidates = controller.ExfiltrationPoints ?? Array.Empty<ExfiltrationPoint>();
                Logger.LogInfo($"[DeathEscape] Using all map extraction points for escape routing. candidates={candidates.Length}");
                return candidates;
            }

            // Strict mode: mirror the player's spawn-side extract assignment from the raid profile.
            Profile profile = boss?.realPlayer?.Profile;
            candidates = profile != null
                ? controller.EligiblePoints(profile)
                : Array.Empty<ExfiltrationPoint>();

            Logger.LogInfo($"[DeathEscape] Using player-assigned extraction points for escape routing. candidates={candidates.Length}");
            return candidates;
        }

        private static bool IsUsableExtract(ExfiltrationPoint point)
        {
            return point != null &&
                   point.Status != EExfiltrationStatus.NotPresent &&
                   point.Requirements != null &&
                   point.Requirements.Length == 0;
        }

        private static void SendOutcomes(List<FollowerDeathEscapeOutcomeEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return;
            }

            // Death escape can mutate teammate profiles after raid end: escaped teammates may save
            // their surviving loadout state, and lost teammates may have gear stripped. Force the
            // My Squad roster to rebuild portraits the next time it is shown.
            Components.SquadControlMenuUi.RequestRosterRefreshOnNextInject();

            Logger.LogInfo(
                "[DeathEscape] Posting escape outcomes: " +
                string.Join(", ", entries.Select(entry => $"{entry.Nickname}={(entry.Escaped ? "escaped" : "lost")}({entry.Chance:P0})")));

            JsonConverter[] defaultJsonConverters = GetDefaultJsonConverters();

            string json = new FollowerDeathEscapeOutcomeRequest
            {
                Notify = true,
                ResolveOnly = false,
                Entries = entries
            }.ToJson(defaultJsonConverters);

            Task.Run(() =>
            {
                try
                {
                    RequestHandler.PostJson(OutcomeRoute, json);
                }
                catch (Exception ex)
                {
                    Logger.LogError("Failed to send follower death escape outcomes");
                    Logger.LogError(ex);
                }
            });
        }

        private static List<FollowerDeathEscapeOutcomeEntry> ResolveEscapeRollsOnServer(List<FollowerDeathEscapeOutcomeEntry> entries)
        {
            if (entries == null || entries.All(entry => entry?.RollEscape != true))
            {
                return entries ?? new List<FollowerDeathEscapeOutcomeEntry>();
            }

            JsonConverter[] defaultJsonConverters = GetDefaultJsonConverters();
            string json = new FollowerDeathEscapeOutcomeRequest
            {
                Notify = false,
                ResolveOnly = true,
                Entries = entries
            }.ToJson(defaultJsonConverters);

            string responseJson = RequestHandler.PostJson(OutcomeRoute, json);
            FollowerDeathEscapeBodyResponse<FollowerDeathEscapeOutcomeResponse> response =
                JsonConvert.DeserializeObject<FollowerDeathEscapeBodyResponse<FollowerDeathEscapeOutcomeResponse>>(responseJson);
            if (response == null || response.err != 0)
            {
                throw new InvalidOperationException(response?.errmsg ?? "Server failed to resolve teammate raid outcomes.");
            }

            return response.data?.Entries ?? entries;
        }

        private static JsonConverter[] GetDefaultJsonConverters()
        {
            var converterClass = typeof(AbstractGame).Assembly.GetTypes()
                .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);
            return Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;
        }


        private struct FollowerReadiness
        {
            public float HealthRatio;
            public float EquipmentPower;
            public bool HasSecureMeds;
            public bool VitalsDestroyed;
        }

        private struct ExtractSnapshot
        {
            public string Name;
            public float Distance;
            public Vector3 Position;
            public bool HasPosition;

            public static ExtractSnapshot Unknown => new ExtractSnapshot
            {
                Name = string.Empty,
                Distance = -1f,
                Position = Vector3.zero,
                HasPosition = false
            };
        }

        private readonly struct RouteThreatSnapshot
        {
            public RouteThreatSnapshot(float averagePower, int count)
            {
                AveragePower = averagePower;
                Count = count;
            }

            public float AveragePower { get; }
            public int Count { get; }

            public static RouteThreatSnapshot Empty => new RouteThreatSnapshot(0f, 0);
        }

        private readonly struct RecoverableGearCandidate
        {
            public RecoverableGearCandidate(
                Item item,
                Vector3 position,
                string ownerId,
                string ownerName,
                bool isPlayer,
                EquipmentSlot slot,
                int itemPriority,
                int sequence,
                bool useAsRecoveryCapacity,
                bool countsAsBackpackCarry,
                bool ignoreCarryWeight = false,
                string coveredByItemId = null,
                bool cloneReturnWithNewIds = false)
            {
                Item = item;
                Position = position;
                OwnerId = ownerId;
                OwnerName = ownerName;
                IsPlayer = isPlayer;
                Slot = slot;
                ItemPriority = itemPriority;
                Sequence = sequence;
                UseAsRecoveryCapacity = useAsRecoveryCapacity;
                CountsAsBackpackCarry = countsAsBackpackCarry;
                IgnoreCarryWeight = ignoreCarryWeight;
                CoveredByItemId = coveredByItemId;
                CloneReturnWithNewIds = cloneReturnWithNewIds;
            }

            public Item Item { get; }
            public Vector3 Position { get; }
            public string OwnerId { get; }
            public string OwnerName { get; }
            public bool IsPlayer { get; }
            public EquipmentSlot Slot { get; }
            public int OwnerPriority => IsPlayer ? 0 : 1;
            public int ItemPriority { get; }
            public int Sequence { get; }
            public bool UseAsRecoveryCapacity { get; }
            public bool CountsAsBackpackCarry { get; }
            public bool IgnoreCarryWeight { get; }
            public string CoveredByItemId { get; }
            public bool CloneReturnWithNewIds { get; }

            public RecoverableGearCandidate WithSequence(int sequence)
            {
                return new RecoverableGearCandidate(
                    Item,
                    Position,
                    OwnerId,
                    OwnerName,
                    IsPlayer,
                    Slot,
                    ItemPriority,
                    sequence,
                    UseAsRecoveryCapacity,
                    CountsAsBackpackCarry,
                    IgnoreCarryWeight,
                    CoveredByItemId,
                    CloneReturnWithNewIds);
            }
        }

        private sealed class RecoveryAttemptStats
        {
            public int NoNearbyCarrier { get; set; }
            public int NoEquipmentSnapshot { get; set; }
            public int OverWeight { get; set; }
            public int BackpackLimit { get; set; }
            public int NoSpace { get; set; }
        }

        private sealed class RecoveryCarrierState
        {
            public RecoveryCarrierState(InventoryEquipment equipment, float maxWeightKg)
            {
                Equipment = equipment;
                MaxWeightKg = maxWeightKg;
                CurrentWeightKg = GetCarrierStartingWeightKg(equipment);
                BackpackCarryCapacity = equipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem == null ? 2 : 1;
            }

            public InventoryEquipment Equipment { get; }
            public float MaxWeightKg { get; }
            public float CurrentWeightKg { get; private set; }
            public int BackpackCarryCapacity { get; }
            public int RecoveredBackpacks { get; private set; }
            public List<EFT.InventoryLogic.SearchableItem> ExternallyCarriedBackpacks { get; } = new List<EFT.InventoryLogic.SearchableItem>();

            public bool CanCarryWeight(float itemWeight)
            {
                return CurrentWeightKg + itemWeight <= MaxWeightKg;
            }

            public bool CanCarryBackpack(RecoverableGearCandidate candidate)
            {
                return !candidate.CountsAsBackpackCarry || RecoveredBackpacks < BackpackCarryCapacity;
            }

            public void RecordRecovered(RecoverableGearCandidate candidate, float itemWeight)
            {
                CurrentWeightKg += itemWeight;
                if (candidate.CountsAsBackpackCarry)
                {
                    RecoveredBackpacks++;
                }
            }

            public void AddExternallyCarriedBackpack(Item item)
            {
                if (item is EFT.InventoryLogic.SearchableItem backpack)
                {
                    ExternallyCarriedBackpacks.Add(backpack);
                }
            }
        }

        private sealed class FallenSquadmateInfo
        {
            public string Aid { get; set; } = string.Empty;
            public string ProfileId { get; set; } = string.Empty;
            public string Nickname { get; set; } = string.Empty;
            public Vector3 Position { get; set; }
            public JsonType.FlatItem[] EquipmentItems { get; set; }
            public string[] TrackedItemIds { get; set; } = Array.Empty<string>();
        }

        private sealed class LostOnDeathRules
        {
            public LostOnDeathRules(Dictionary<string, bool> equipment, bool playerGearProtectedByRaidStatusOverride = false)
            {
                Equipment = equipment ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                PlayerGearProtectedByRaidStatusOverride = playerGearProtectedByRaidStatusOverride;
            }

            public static LostOnDeathRules KeepAll { get; } = new LostOnDeathRules(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

            private Dictionary<string, bool> Equipment { get; }
            public bool PlayerGearProtectedByRaidStatusOverride { get; }

            public int LostEquipmentSlotCount => Equipment.Count(pair => pair.Value);

            public bool IsEquipmentSlotLost(EquipmentSlot slot)
            {
                string key = slot.ToString();
                if (slot == EquipmentSlot.Pockets)
                {
                    return Equipment.TryGetValue("PocketItems", out bool pocketItemsLost) && pocketItemsLost ||
                           Equipment.TryGetValue(key, out bool pocketsLost) && pocketsLost;
                }

                return Equipment.TryGetValue(key, out bool lost) && lost;
            }
        }
    }
}

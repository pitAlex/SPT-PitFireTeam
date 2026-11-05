using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Quests;
using EFT.UI.Matchmaker;
using Newtonsoft.Json;
using pitTeam.Patches;
using ProfileResult = EFT.ProfileDescriptor;

namespace pitTeam.Modules
{
    // Visual-only membership. Never put automatic quest allies into the selected
    // roster: doing so would turn a solo quest raid into an ineligible party.
    internal static class QuestCompanionPreview
    {
        private static readonly Dictionary<string, RaidPlayer> previews = new Dictionary<string, RaidPlayer>(StringComparer.OrdinalIgnoreCase);
        private static MatchmakerPlayersController owner;
        internal static bool IsPreview(string account) => previews.Values.Any(preview => preview.AccountId == account);

        internal static List<string> RolesFor(Profile profile, RaidSettings settings)
        {
            var roles = new List<string>();
            if (!GameplayModeRuntime.IsGoingRogueActive || settings?.IsPmc != true || profile?.Info == null || profile.Info.Level < 20 ||
                MainMenuControllerPatch.GroupPlayers.Count != 0 ||
                !profile.TryGetTraderInfo(Utils.Props.KnightTrader, out var trader) || trader.Disabled) return roles;
            foreach (var quest in profile.QuestsData)
            {
                if (quest.Status != EQuestStatus.Started || !GameplayModeAddons.CanSpawnQuestCompanion(quest.Id, settings.LocationId,
                    objective => quest.CompletedConditions?.Contains(new MongoID(objective)) == true)) continue;
                foreach (var entry in Utils.Props.Quests)
                {
                    if (!entry.Value.Contains(quest.Id)) continue;
                    if (Utils.Props.QuestsLocations.TryGetValue(quest.Id, out var locations) &&
                        !locations.Contains(settings.LocationId.ToLowerInvariant())) continue;
                    string role = entry.Key == "Knight" ? "bossKnight" : entry.Key == "BigPipe" ? "followerBigPipe" : entry.Key == "BirdEye" ? "followerBirdEye" : null;
                    if (role != null && !roles.Contains(role)) roles.Add(role);
                }
            }
            return roles;
        }

        internal static void Refresh(MatchmakerPlayersController controller, string role) =>
            RefreshAll(controller, role == null ? Array.Empty<string>() : new[] { role });

        internal static void RefreshAll(MatchmakerPlayersController controller, IEnumerable<string> roles)
        {
            var selected = (roles ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (controller?.CurrentPlayer?.Info == null || selected.Length == 0) { Remove(); return; }
            if (!ReferenceEquals(owner, controller)) Remove();
            owner = controller;
            foreach (var removed in previews.Keys.Except(selected, StringComparer.OrdinalIgnoreCase).ToArray())
            {
                controller.GroupPlayers.Remove(previews[removed]);
                previews.Remove(removed);
            }
            var serializer = JsonSerializer.Create(new JsonSerializerSettings
                { Converters = OtherPlayerProfileScreenPatch.GetDefaultJsonConverters() });
            foreach (var role in selected)
            {
                var descriptor = CompanionRaidPreparation.QuestProfileFor(role) ??
                    (selected.Length == 1 ? CompanionRaidPreparation.QuestProfile : null);
                if (descriptor == null)
                {
                    if (previews.TryGetValue(role, out var missing)) controller.GroupPlayers.Remove(missing);
                    previews.Remove(role);
                    continue;
                }
                var profile = new Profile(descriptor.ToObject<ProfileResult>(serializer));
                if (previews.TryGetValue(role, out var preview))
                {
                    if (preview.Id == profile.Id)
                    {
                        if (!controller.GroupPlayers.Contains(preview)) controller.GroupPlayers.Add(preview);
                        continue;
                    }
                    controller.GroupPlayers.Remove(preview);
                }
                var visual = profile.GetVisualEquipmentState(false);
                var current = controller.CurrentPlayer.Info;
                visual.Info.SavageNickname = current.Nickname ?? visual.Info.Nickname ?? string.Empty;
                visual.Info.SavageLockTime = current.SavageLockTime;
                visual.Info.GameVersion = current.GameVersion;
                visual.Info.HasCoopExtension = current.HasCoopExtension;
                preview = new RaidPlayer(new GroupPlayer
                {
                    Id = profile.Id, AccountId = profile.AccountId,
                    Info = visual.Info, PlayerVisualRepresentation = visual, IsReady = true
                });
                previews[role] = preview;
                controller.GroupPlayers.Add(preview);
                Logger.LogInfo($"[QuestCompanion] preview role={role} profile={profile.Id} selectedMembers=0");
            }
            if (previews.Count > 0) SyntheticTeammateAutoJoinLoader.EnsureLocalGroupOwner(controller);
            else if (controller.GroupPlayers.Count <= 1) controller.Group?.RemoveOwner();
        }

        internal static void Remove()
        {
            if (owner?.GroupPlayers != null)
            {
                foreach (var preview in previews.Values) owner.GroupPlayers.Remove(preview);
                if (owner.GroupPlayers.Count <= 1) owner.Group?.RemoveOwner();
            }
            previews.Clear();
            owner = null;
        }
    }
}

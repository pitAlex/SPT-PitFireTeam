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
        private static RaidPlayer preview;
        private static MatchmakerPlayersController owner;
        internal static bool IsPreview(string account) => preview != null && preview.AccountId == account;

        internal static List<string> RolesFor(Profile profile, RaidSettings settings)
        {
            var roles = new List<string>();
            if (!GameplayModeRuntime.IsGoingRogue || settings?.IsPmc != true || profile?.Info == null || profile.Info.Level < 20 ||
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

        internal static void Refresh(MatchmakerPlayersController controller, string role)
        {
            var descriptor = CompanionRaidPreparation.QuestProfile;
            if (controller?.CurrentPlayer?.Info == null || role == null || descriptor == null)
            { Remove(); return; }
            var serializer = JsonSerializer.Create(new JsonSerializerSettings
                { Converters = OtherPlayerProfileScreenPatch.GetDefaultJsonConverters() });
            var profile = new Profile(descriptor.ToObject<ProfileResult>(serializer));
            if (preview?.Id == profile.Id && ReferenceEquals(owner, controller))
            {
                if (!controller.GroupPlayers.Contains(preview)) controller.GroupPlayers.Add(preview);
                return;
            }
            Remove();
            var visual = profile.GetVisualEquipmentState(false);
            var current = controller.CurrentPlayer.Info;
            // Native Show transliterates this even for PMC raids, before binding
            // the model. Match the ordinary teammate matchmaking metadata.
            visual.Info.SavageNickname = current.Nickname ?? visual.Info.Nickname ?? string.Empty;
            visual.Info.SavageLockTime = current.SavageLockTime;
            visual.Info.GameVersion = current.GameVersion;
            visual.Info.HasCoopExtension = current.HasCoopExtension;
            preview = new RaidPlayer(new GroupPlayer
            {
                Id = profile.Id, AccountId = profile.AccountId,
                Info = visual.Info, PlayerVisualRepresentation = visual, IsReady = true
            });
            owner = controller;
            controller.GroupPlayers.Add(preview);
            SyntheticTeammateAutoJoinLoader.EnsureLocalGroupOwner(controller);
            Logger.LogInfo($"[QuestCompanion] preview role={role} profile={profile.Id} selectedMembers=0");
        }

        internal static void Remove()
        {
            if (preview != null && owner?.GroupPlayers != null)
            {
                owner.GroupPlayers.Remove(preview);
                if (owner.GroupPlayers.Count <= 1) owner.Group?.RemoveOwner();
            }
            preview = null;
            owner = null;
        }
    }
}

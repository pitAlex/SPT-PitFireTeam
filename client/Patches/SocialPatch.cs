using ChatShared;
using Comfort.Common;

using EFT;
using EFT.UI.Chat;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using pitTeam.Components;
using Newtonsoft.Json.Linq;

using HarmonyLib;
using SPT.Common.Http;
using SPT.Common.Utils;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UI.Matchmaker.Group;
using UnityEngine;

namespace pitTeam.Patches
{
    internal class SocialNetworkClassPatch : ModulePatch
    {
        private static EFT.SocialNetwork? socialNetworkClass;
        private static EFT.ISocial? iChatInteractions;
        private static readonly MethodInfo RefreshFriendsCallbackMethod = AccessTools.Method(typeof(EFT.SocialNetwork), "CG_method_13");
        private static readonly MethodInfo RefreshInputRequestsCallbackMethod = AccessTools.Method(typeof(EFT.SocialNetwork), "CG_method_14");
        private static readonly MethodInfo RefreshOutputRequestsCallbackMethod = AccessTools.Method(typeof(EFT.SocialNetwork), "CG_method_15");
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.SocialNetwork), "method_1");
        }

        [PatchPostfix]
        private static void PatchPostfix(EFT.SocialNetwork __instance, EFT.ISocial session, InventoryController inventoryController, string matchingVersion)
        {
            socialNetworkClass = __instance;
            iChatInteractions = session;
        }

        private static float delay = 0;

        public static void RefreshFriendsList(bool force = false)
        {
            if (socialNetworkClass != null && iChatInteractions != null && (force || delay < Time.time))
            {
                delay = Time.time + (force ? 0.25f : 2f);
                iChatInteractions.GetFriendsList(new Callback<ChatShared.ChatContacts>(result =>
                {
                    RefreshFriendsCallbackMethod?.Invoke(socialNetworkClass, new object[] { result });
                }));

                iChatInteractions.GetInputFriendsRequests(new Callback<ChatShared.FriendsInvitation[]>(result =>
                {
                    RefreshInputRequestsCallbackMethod?.Invoke(socialNetworkClass, new object[] { result });
                }));

                iChatInteractions.GetOutputFriendsRequests(new Callback<ChatShared.FriendsInvitation[]>(result =>
                {
                    RefreshOutputRequestsCallbackMethod?.Invoke(socialNetworkClass, new object[] { result });
                }));
            }
        }

        public static void RefreshFriendsListAndSquadRoster()
        {
            SquadControlMenuUi.RequestRosterRefreshNowOrNextInject();
            RefreshFriendsList(true);
        }
    }

    internal class ChatInvitePlayersPanelRefreshPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ChatInvitePlayersPanel), "Show");
        }

        [PatchPrefix]
        private static void PatchPrefix()
        {
            SocialNetworkClassPatch.RefreshFriendsList();
        }
    }

    internal class ChatCreateDialoguePanelRefreshPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ChatCreateDialoguePanel), "Show");
        }

        [PatchPrefix]
        private static void PatchPrefix()
        {
            SocialNetworkClassPatch.RefreshFriendsList();
        }
    }

    internal class ChatFriendsRequestsPanelRefreshPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ChatFriendsRequestsPanel), "Show");
        }

        [PatchPrefix]
        private static void PatchPrefix()
        {
            SocialNetworkClassPatch.RefreshFriendsList();
        }
    }

    internal class FriendRequestAcceptRefreshPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            Type acceptCallbackType = typeof(EFT.SocialNetwork).GetNestedType("CG_AcceptFriendRequest", BindingFlags.Public | BindingFlags.NonPublic);
            return AccessTools.Method(acceptCallbackType, "method_1");
        }

        [PatchPostfix]
        private static void PatchPostfix()
        {
            SocialNetworkClassPatch.RefreshFriendsListAndSquadRoster();
        }
    }

    internal class FriendRequestAcceptAllRefreshPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.SocialNetwork), nameof(EFT.SocialNetwork.AcceptAllFriendRequests));
        }

        [PatchPostfix]
        private static async void PatchPostfix(Task __result)
        {
            try
            {
                if (__result != null)
                {
                    await __result;
                }

                SocialNetworkClassPatch.RefreshFriendsListAndSquadRoster();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo("[UI] Failed to refresh squad roster after accepting all friend requests.");
                Modules.Logger.LogError(ex);
            }
        }
    }

    internal class TeammateContextMenuButtonsPatch : ModulePatch
    {
        private const string TeammatesRoute = "/singleplayer/pitfireteam/teammates";
        private static readonly HashSet<string> TeammateAccountIds = new HashSet<string>(StringComparer.Ordinal);
        private static float _nextRefreshTime;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.UI.Chat.ChatMemberContextInteractions), nameof(EFT.UI.Chat.ChatMemberContextInteractions.IsActive));
        }

        [PatchPrefix]
        private static bool PatchPrefix(EFT.UI.Chat.ChatMemberContextInteractions __instance, EFriendInteractionButton button, ref bool __result)
        {
            if (button == EFriendInteractionButton.WatchProfile)
            {
                return true;
            }

            UpdatableChatMember? member = __instance?._selectedMember;
            if (member == null)
            {
                return true;
            }

            if (!IsTeammateMember(member))
            {
                return true;
            }

            __result = false;
            return false;
        }

        private static bool IsTeammateMember(UpdatableChatMember member)
        {
            if (member == null || string.IsNullOrWhiteSpace(member.AccountId))
            {
                return false;
            }

            RefreshTeammateCacheIfNeeded();
            return TeammateAccountIds.Contains(member.AccountId);
        }

        private static void RefreshTeammateCacheIfNeeded()
        {
            if (Time.time < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.time + 5f;

            try
            {
                string response = RequestHandler.GetJson(TeammatesRoute);
                if (string.IsNullOrWhiteSpace(response))
                {
                    return;
                }

                JToken root = JToken.Parse(response);
                JToken? dataToken = root.Type == JTokenType.Array ? root : root["data"];

                if (dataToken is not JArray teammates)
                {
                    return;
                }

                TeammateAccountIds.Clear();
                foreach (JToken teammate in teammates)
                {
                    string? accountId = teammate?["Aid"]?.ToString() ?? teammate?["aid"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(accountId))
                    {
                        TeammateAccountIds.Add(accountId!);
                    }
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo("[UI] Failed to refresh teammate cache for social context actions.");
                Modules.Logger.LogError(ex);
            }
        }
    }

    internal class FriendRequestProfileViewPatch : ModulePatch
    {
        private const string RecruitInvitationIdPrefix = "pitfireteam-recruit-";

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.UI.Chat.ChatMemberContextInteractions), nameof(EFT.UI.Chat.ChatMemberContextInteractions.WatchPlayerProfile));
        }

        [PatchPrefix]
        private static bool PatchPrefix(EFT.UI.Chat.ChatMemberContextInteractions __instance)
        {
            UpdatableChatMember? profileMember = __instance?._invitation?.From;
            if (profileMember == null)
            {
                return true;
            }

            bool isRecruitInvitation =
                __instance._invitation?._id?.StartsWith(RecruitInvitationIdPrefix, StringComparison.Ordinal) == true;
            string currentAccountId = __instance._selectedMember?.AccountId;
            bool hasCurrentAccountId = !string.IsNullOrWhiteSpace(currentAccountId) && currentAccountId != "0";
            if (hasCurrentAccountId && !isRecruitInvitation)
            {
                return true;
            }

            string profileAccountId = hasCurrentAccountId ? currentAccountId : profileMember.AccountId;
            if (string.IsNullOrWhiteSpace(profileAccountId) || profileAccountId == "0")
            {
                return true;
            }

            OtherPlayerProfileScreenPatch.ClearPendingRecruitProfileView();
            if (isRecruitInvitation)
            {
                OtherPlayerProfileScreenPatch.PreparePendingRecruitProfileView(profileAccountId);
            }

            Task<OtherPlayerProfileScreen.OtherPlayerProfileScreenController> profileTask =
                ItemUiContext.Instance.ShowPlayerProfileScreen(profileAccountId, EItemViewType.OtherPlayerProfile);
            profileTask.ContinueWith(task =>
            {
                if (task.IsCanceled || task.IsFaulted || task.Result == null)
                {
                    OtherPlayerProfileScreenPatch.ClearPendingRecruitProfileView(profileAccountId);
                }
            }).HandleExceptions();
            return false;
        }
    }

    internal class FriendListInvitePlayerPanelPatch : ModulePatch
    {
        private const string TeammatesRoute = "/singleplayer/pitfireteam/teammates";

        private sealed class TeammateInviteEntry
        {
            public string AccountId;
            public string Id;
            public string Nickname;
            public int Level;
            public EChatMemberSide Side;
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(FriendListInvitePlayerPanel), nameof(FriendListInvitePlayerPanel.Show));
        }

        [PatchPrefix]
        private static void PatchPrefix(ref Diz.Binding.BindableList<UpdatableChatMember> friendsList)
        {
            friendsList = BuildInviteableFriendsList(friendsList);
        }

        private static Diz.Binding.BindableList<UpdatableChatMember> BuildInviteableFriendsList(Diz.Binding.BindableList<UpdatableChatMember> source)
        {
            List<UpdatableChatMember> filtered = new List<UpdatableChatMember>();
            HashSet<string> seenAccountIds = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, TeammateInviteEntry> teammatesByAccountId = LoadTeammateInviteEntries();

            if (source != null)
            {
                foreach (UpdatableChatMember member in source)
                {
                    if (!ShouldIncludeInviteMember(member))
                    {
                        continue;
                    }

                    string accountId = GetStableAccountId(member);
                    if (teammatesByAccountId.ContainsKey(accountId))
                    {
                        NormalizeInviteTeammateMember(member);
                    }

                    if (!seenAccountIds.Add(accountId))
                    {
                        continue;
                    }

                    filtered.Add(member);
                }
            }

            foreach (TeammateInviteEntry teammate in teammatesByAccountId.Values)
            {
                if (!seenAccountIds.Add(teammate.AccountId))
                {
                    continue;
                }

                UpdatableChatMember teammateMember = UpdatableChatMember.FindOrCreate(teammate.Id, static memberId => new UpdatableChatMember(memberId));
                teammateMember.AccountId = teammate.AccountId;
                teammateMember.Info.Nickname = teammate.Nickname;
                teammateMember.Info.Level = teammate.Level;
                teammateMember.Info.Side = teammate.Side;
                NormalizeInviteTeammateMember(teammateMember);
                filtered.Add(teammateMember);
            }

            return new Diz.Binding.BindableList<UpdatableChatMember>(filtered);
        }

        private static Dictionary<string, TeammateInviteEntry> LoadTeammateInviteEntries()
        {
            Dictionary<string, TeammateInviteEntry> teammatesByAccountId = new Dictionary<string, TeammateInviteEntry>(StringComparer.Ordinal);

            try
            {
                string response = RequestHandler.GetJson(TeammatesRoute);
                if (string.IsNullOrWhiteSpace(response))
                {
                    return teammatesByAccountId;
                }

                JToken root = JToken.Parse(response);
                JToken dataToken = root.Type == JTokenType.Array ? root : root["data"];
                if (dataToken is not JArray teammates)
                {
                    return teammatesByAccountId;
                }

                foreach (JToken teammate in teammates)
                {
                    string accountId = teammate?["Aid"]?.ToString() ?? teammate?["aid"]?.ToString();
                    string id = teammate?["Id"]?.ToString() ?? teammate?["id"]?.ToString();
                    string nickname = teammate?["Info"]?["Nickname"]?.ToString() ?? teammate?["info"]?["nickname"]?.ToString();

                    if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(nickname))
                    {
                        continue;
                    }

                    teammatesByAccountId[accountId] = new TeammateInviteEntry
                    {
                        AccountId = accountId,
                        Id = id,
                        Nickname = nickname,
                        Level = ParseInt(teammate?["Info"]?["Level"]?.ToString() ?? teammate?["info"]?["level"]?.ToString()),
                        Side = ParseSide(teammate?["Info"]?["Side"]?.ToString() ?? teammate?["info"]?["side"]?.ToString())
                    };
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo("[UI] Failed to build teammate invite list.");
                Modules.Logger.LogError(ex);
            }

            return teammatesByAccountId;
        }

        private static void NormalizeInviteTeammateMember(UpdatableChatMember member)
        {
            if (member?.Info == null)
            {
                return;
            }

            member.Info.MemberCategory = EMemberCategory.Unheard;
            member.Info.SelectedMemberCategory = EMemberCategory.Unheard;
        }

        private static bool ShouldIncludeInviteMember(UpdatableChatMember member)
        {
            if (member == null)
            {
                return false;
            }

            if (member.Info == null)
            {
                return false;
            }

            if (member.Info.MemberCategory == EMemberCategory.Developer)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(GetStableAccountId(member));
        }

        private static string GetStableAccountId(UpdatableChatMember member)
        {
            return !string.IsNullOrWhiteSpace(member?.AccountId) ? member.AccountId : member?.Id ?? string.Empty;
        }

        private static int ParseInt(string value)
        {
            return int.TryParse(value, out int parsed) ? parsed : 1;
        }

        private static EChatMemberSide ParseSide(string side)
        {
            if (string.Equals(side, "Bear", StringComparison.OrdinalIgnoreCase))
            {
                return EChatMemberSide.Bear;
            }

            if (string.Equals(side, "Savage", StringComparison.OrdinalIgnoreCase))
            {
                return EChatMemberSide.Savage;
            }

            return EChatMemberSide.Usec;
        }
    }

    internal class TeammateGroupContextMenuButtonsPatch : ModulePatch
    {
        private const string TeammatesRoute = "/singleplayer/pitfireteam/teammates";
        private static readonly HashSet<string> TeammateAccountIds = new HashSet<string>(StringComparer.Ordinal);
        private static float _nextRefreshTime;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.UI.Matchmaker.RaidGroupContextInteractions), nameof(EFT.UI.Matchmaker.RaidGroupContextInteractions.IsActive));
        }

        [PatchPrefix]
        private static bool PatchPrefix(EFT.UI.Matchmaker.RaidGroupContextInteractions __instance, ERaidPlayerButton button, ref bool __result)
        {
            if (button == ERaidPlayerButton.RemovePlayer)
            {
                return true;
            }

            EFT.GroupPlayer groupMember = __instance?._selectedPlayer;
            EFT.UI.Matchmaker.IMatchmakerController controller = __instance?._matchmakerPlayersController;
            if (groupMember == null || controller == null || string.IsNullOrWhiteSpace(groupMember.AccountId))
            {
                return true;
            }

            if (!controller.IsInGroup(groupMember.AccountId))
            {
                return true;
            }

            if (!IsTeammateAccountId(groupMember.AccountId))
            {
                return true;
            }

            __result = false;
            return false;
        }

        private static bool IsTeammateAccountId(string accountId)
        {
            RefreshTeammateCacheIfNeeded();
            return TeammateAccountIds.Contains(accountId);
        }

        private static void RefreshTeammateCacheIfNeeded()
        {
            if (Time.time < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.time + 5f;

            try
            {
                string response = RequestHandler.GetJson(TeammatesRoute);
                if (string.IsNullOrWhiteSpace(response))
                {
                    return;
                }

                JToken root = JToken.Parse(response);
                JToken dataToken = root.Type == JTokenType.Array ? root : root["data"];

                if (dataToken is not JArray teammates)
                {
                    return;
                }

                TeammateAccountIds.Clear();
                foreach (JToken teammate in teammates)
                {
                    string accountId = teammate?["Aid"]?.ToString() ?? teammate?["aid"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(accountId))
                    {
                        TeammateAccountIds.Add(accountId);
                    }
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo("[UI] Failed to refresh teammate cache for group context actions.");
                Modules.Logger.LogError(ex);
            }
        }
    }

    /** Refresh friends list whenever we complete a quest **/
    internal class QuestClassPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.Quests.Quest), "SetStatus");
        }

        [PatchPostfix]
        private static void PatchPostfix(EFT.Quests.Quest __instance)
        {
            if (__instance.QuestStatus == EQuestStatus.Success)
            {
                SocialNetworkClassPatch.RefreshFriendsList();
            }
        }

    }
}

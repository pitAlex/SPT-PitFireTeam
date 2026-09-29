using System;
using ChatShared;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using pitTeam.Patches;
using SPT.Common.Http;
using SPT.Reflection.Patching;

namespace pitTeam.Modules
{
    internal static class TeammateDeletion
    {
        internal static InventoryController InventoryController;
        private static bool busy;
        private sealed class DeleteResponse
        {
            public bool deleted;
            public JsonType.FlatItem[] playerStashItems;
        }

        internal static string ConfirmationText(string nickname, int? price) => price.HasValue
            ? string.Format(pitFireTeam.GetSocialUiText("RemoveRecruitedTeammatePrompt"), nickname, price.Value.ToString("N0"))
            : string.Format(pitFireTeam.GetSocialUiText("RemoveTeammatePrompt"), nickname);

        internal static async Task<bool> DeleteAsync(string accountId)
        {
            if (busy) return false;
            busy = true;
            try
            {
                var session = ItemUiContext.Instance?.Session;
                if (session?.Profile == null || InventoryController == null)
                    throw new InvalidOperationException("Player inventory is unavailable for deletion payment refresh");
                string json = await Task.Run(() => RequestHandler.PostJson("/singleplayer/pitfireteam/teammate/delete",
                    JsonConvert.SerializeObject(new { accountId })));
                var response = JsonConvert.DeserializeObject<FriendlyTeammateBodyResponse<DeleteResponse>>(json);
                if (response == null || response.err != 0 || response.data == null)
                {
                    string key = response?.errmsg ?? "TeammateDeleteFailed";
                    await ItemUiContext.Instance.ShowMessageWindow(out _, pitFireTeam.GetSocialUiText(key),
                        key == "TeammateDeleteInsufficientFunds" ? "ragfair/Not enough money".Localized()
                            : pitFireTeam.GetSocialUiText("RemoveTeammateTitle"), true);
                    return false;
                }
                OtherPlayerProfileScreenPatch.ApplyServerSavedPlayerStash(session.Profile, InventoryController,
                    session.RagFair, response.data.playerStashItems);
                SocialNetworkClassPatch.RefreshFriendsList(true);
                return response.data.deleted;
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError($"[UI] Teammate deletion failed: {ex}");
                AddTeammateCreationFlow.ShowToast(pitFireTeam.GetSocialUiText("TeammateDeleteFailed"));
                return false;
            }
            finally { busy = false; }
        }

        internal static async Task DeleteSocialAsync(SocialNetwork network, UpdatableChatMember member, string accountId, int? price, Callback callback)
        {
            bool deleted = false;
            try
            {
                if (await ItemUiContext.Instance.ShowMessageWindow(out _, ConfirmationText(member.Info.Nickname, price),
                    pitFireTeam.GetSocialUiText("RemoveTeammateTitle"), true))
                    deleted = await DeleteAsync(accountId);
                if (deleted)
                {
                    network.FriendsList.Remove(member);
                    Components.SquadControlMenuUi.RequestRosterRefreshNowOrNextInject();
                }
            }
            catch (Exception ex) { pitFireTeam.Log.LogError($"[UI] Social teammate deletion failed: {ex}"); }
            callback?.Invoke(deleted ? SuccessfulResult.New : new FailedResult(pitFireTeam.GetSocialUiText("TeammateDeleteFailed"), 0));
        }
    }
}

namespace pitTeam.Patches
{
    // Stock removal drops the member immediately, even when the backend rejects payment.
    internal sealed class TeammateSocialDeletionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(SocialNetwork), nameof(SocialNetwork.RemoveFromFriendsList));

        [PatchPrefix]
        private static bool Prefix(SocialNetwork __instance, UpdatableChatMember member, Callback callback)
        {
            try
            {
                JToken root = JToken.Parse(RequestHandler.GetJson("/singleplayer/pitfireteam/teammates"));
                var teammates = (root as JArray) ?? root["data"] as JArray;
                if (teammates == null) throw new InvalidOperationException("Unable to resolve teammate roster");
                var entry = teammates.FirstOrDefault(item =>
                    (item["Aid"] ?? item["aid"])?.ToString() == member.AccountId
                    || (item["Id"] ?? item["id"])?.ToString() == member.Id);
                if (entry == null) return true;
                string accountId = (entry["Aid"] ?? entry["aid"])?.ToString();
                int? price = (entry["RecruitmentGearPrice"] ?? entry["recruitmentGearPrice"])?.Value<int?>();
                Modules.TeammateDeletion.DeleteSocialAsync(__instance, member, accountId, price, callback).HandleExceptions();
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError($"[UI] Could not validate social deletion: {ex}");
                callback?.Invoke(new FailedResult(pitFireTeam.GetSocialUiText("TeammateDeleteFailed"), 0));
            }
            return false;
        }
    }
}

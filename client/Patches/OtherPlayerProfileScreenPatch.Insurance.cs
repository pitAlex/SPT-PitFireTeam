using EFT.InventoryLogic;
using Comfort.Common;
using JsonType;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using EFT.UI.Insurance;
using SPT.Common.Http;

namespace pitTeam.Patches
{
    internal class FriendlyTeammateInsuredItem
    {
        public string ItemId { get; set; }
        public string TraderId { get; set; }
    }

    internal partial class OtherPlayerProfileScreenPatch
    {
        private static List<FriendlyTeammateInsuredItem> LoadoutEditorFollowerInsurance;
        private static bool LoadoutEditorInsuranceActive;
        private static bool InsurancePurchaseBusy;
        private static string InsuranceRefreshRequiredForAid;
        private static string InsuranceUiStage;
        private const string FollowerInsuranceRoute = "/singleplayer/pitfireteam/teammate/profile/insurance";

        internal static async Task PurchaseFollowerInsuranceAsync(InsuranceCompany insurance, List<InsuredItem> items, Callback callback)
        {
            if (InsurancePurchaseBusy || InsuranceRefreshRequiredForAid != null)
            {
                callback?.Invoke(new FailedResult(GetSocialUiText("FollowerInsuranceRefreshRequired"), 0));
                return;
            }
            InsurancePurchaseBusy = true;
            InsuranceUiStage = "prepare";
            SetLoadoutEditorBusy(true);
            var profile = ViewedProfile;
            IResult result;
            try
            {
                var selected = items.Where(item => item?.Item != null && insurance.ItemTypeAvailableForInsurance(item)
                    && !insurance.Insured(item.Id)).ToList();
                if (profile == null || !pitFireTeam.IsFollowerLoadoutLootableMode() || selected.Count == 0
                    || selected.Any(item => !IsLoadoutEditorEquipmentItem(item.Item)))
                    throw new InvalidOperationException("FollowerInsuranceInvalidSelection");
                string traderId = insurance.SelectedInsurerId;
                int quotedTotal = insurance.ItemsPrice(selected);
                string[] ids = selected.Select(item => item.Id).Distinct().ToArray();
                pitFireTeam.Log.LogInfo($"[FollowerInsurance:Confirm] teammateAid='{profile.AccountId}' traderId='{traderId}' quotedRoubles={quotedTotal} itemIds='{string.Join(",", ids)}'");

                // Same explicit service boundary as repair: persist staged equipment before buying coverage.
                if (HasPendingLoadoutEditorRealChanges())
                {
                    var committed = await CommitLoadoutEditorStateBeforeServiceAsync(profile);
                    if (committed.Failed) throw new InvalidOperationException("FollowerInsurancePurchaseFailed");
                }
                InsuranceRefreshRequiredForAid = profile.AccountId;
                InsuranceUiStage = "purchase";
                string json = await Task.Run(() => RequestHandler.PostJson(FollowerInsuranceRoute, SerializeBody(new
                {
                    aid = profile.AccountId, traderId, itemIds = ids, quotedTotal
                })));
                var response = DeserializeBodySuccess<FriendlyTeammateDefaultEquipmentResponse>(json)?.data;
                if (response?.playerStashItems == null) throw new InvalidOperationException("FollowerInsurancePurchaseFailed");
                ApplyFollowerInsuranceResponse(response);
                InsuranceRefreshRequiredForAid = null;
                foreach (var item in selected)
                {
                    insurance.RemoveItemFromInsuranceQueue(item, false);
                    insurance.OnItemInsured.Invoke(item);
                }
                pitFireTeam.Log.LogInfo($"[FollowerInsurance:Paid] teammateAid='{profile.AccountId}' paidRoubles={response.insurancePaid}");
                result = SuccessfulResult.New;
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError($"[FollowerInsurance:Purchase] {ex}");
                // A lost HTTP response may hide a successful charge. Recover from the server; never
                // allow the pre-payment editor stash to overwrite money until this succeeds.
                if (InsuranceRefreshRequiredForAid != null)
                {
                    try { RefreshInsurancePlayerState(); }
                    catch (Exception refreshError) { pitFireTeam.Log.LogError($"[FollowerInsurance:Recovery] {refreshError}"); }
                }
                string key = ex.Message.StartsWith("FollowerInsurance", StringComparison.Ordinal)
                    ? ex.Message : "FollowerInsurancePurchaseFailed";
                result = new FailedResult(GetSocialUiText(key), 0);
                EFT.Communications.NotificationManager.DisplayWarningNotification(
                    GetSocialUiText(key), EFT.Communications.ENotificationDurationType.Default);
            }
            finally
            {
                InsurancePurchaseBusy = false;
                SetLoadoutEditorBusy(false);
            }
            InsuranceUiStage = "stock-confirmation-callback";
            LogInsuranceUiState(InsuranceUiStage);
            try { callback?.Invoke(result); }
            catch (Exception ex) { pitFireTeam.Log.LogError($"[FollowerInsurance:Callback] {ex}"); }
            LogInsuranceUiState("after-stock-callback");
            try
            {
                if (profile != null)
                {
                    InsuranceUiStage = "selector-refresh";
                    RefreshCurrentTeammateLoadoutSelector(profile);
                    MarkSquadRosterDirty(profile.AccountId);
                }
                LogInsuranceUiState($"finished-success={result.Succeed}");
            }
            finally { InsuranceUiStage = null; }
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogInsuranceUiState(string stage)
        {
            bool exists = LoadoutEditorOverlayRoot != null;
            pitFireTeam.Log.LogInfo($"[FollowerInsurance:UiResult] stage='{stage}' editorOpen={exists} editorActive={exists && LoadoutEditorOverlayRoot.activeInHierarchy}");
        }

        private static void RefreshInsurancePlayerState()
        {
            string json = RequestHandler.PostJson(FollowerInsuranceRoute,
                SerializeBody(new { aid = InsuranceRefreshRequiredForAid, readOnly = true }));
            var response = DeserializeBodySuccess<FriendlyTeammateDefaultEquipmentResponse>(json)?.data;
            if (response?.playerStashItems == null) throw new InvalidOperationException("FollowerInsuranceRefreshRequired");
            ApplyFollowerInsuranceResponse(response);
            InsuranceRefreshRequiredForAid = null;
        }

        private static void ApplyFollowerInsuranceResponse(FriendlyTeammateDefaultEquipmentResponse response)
        {
            InsuranceUiStage = "payment-refresh";
            // Equipment moves were committed before purchase. Insurance only changes money;
            // do not rebuild the entire live stash (and its bound views) a second time.
            ApplyInsurancePaymentState(response);
            if (LoadoutEditorProfile?.Inventory?.Stash != null)
            {
                // Payment only changes/removes rouble stacks. Preserve the item instances bound to
                // the open stash grids, just as repair does, and reset the post-payment save baseline.
                RebuildLoadoutEditorItemIndexes();
                var editorMoneyIds = LoadoutEditorStashItemsById.Values.Where(item =>
                    CurrencyUtil.TryGetCurrencyType(new MongoID?(item.TemplateId), out ECurrencyType currency)
                    && currency == ECurrencyType.RUB).Select(item => item.Id).ToHashSet();
                var savedIds = response.insurancePlayerRoubles.Select(item => item._id.ToString()).ToHashSet();
                ApplyServerRepairLoadoutEditorStashChanges(new FriendlyTeammateRepairEquipmentResponse
                {
                    playerChangedStashItems = response.insurancePlayerRoubles
                        .Where(item => editorMoneyIds.Contains(item._id.ToString())).ToArray(),
                    playerDeletedStashItemIds = editorMoneyIds.Where(id => !savedIds.Contains(id)).ToArray()
                }, requireInPlace: true);
            }
            // Register against the refreshed editor instances on both success and response recovery.
            ApplyCommittedInsurance(response);
            LogInsuranceUiState("after-payment-refresh");
        }

        private static void ApplyInsurancePaymentState(FriendlyTeammateDefaultEquipmentResponse response)
        {
            var player = ActiveProfileSession?.Profile;
            var controller = ResolveActiveProfileInventoryControllerForBackendUpdate(player, ActiveProfileInventoryController)
                as OfflineInventoryController;
            if (player?.Inventory == null || controller == null || response.insurancePlayerRoubles == null)
                throw new InvalidOperationException("FollowerInsuranceRefreshRequired");

            // Stock payment can also consume money from player equipment/secure containers.
            // A complete money snapshot makes response recovery safe even after partial UI application.
            var currentMoney = player.Inventory.AllRealPlayerItems.Where(item =>
                CurrencyUtil.TryGetCurrencyType(new MongoID?(item.TemplateId), out ECurrencyType currency)
                && currency == ECurrencyType.RUB).ToDictionary(item => item.Id);
            var savedIds = response.insurancePlayerRoubles.Select(item => item._id.ToString()).ToHashSet();
            var updater = new EFT.ProfileUpdatesHandler(player, controller, null, ActiveProfileSession.RagFair);
            ((EFT.IProfileUpdatesHandler)updater).UpdateProfile(new EFT.ProfileChanges
            {
                Stash = new EFT.StashChangesResponse
                {
                    @new = response.insurancePlayerRoubles.Where(item => !currentMoney.ContainsKey(item._id.ToString())).ToArray(),
                    change = response.insurancePlayerRoubles.Where(item => currentMoney.ContainsKey(item._id.ToString())).ToArray(),
                    del = CreateDeletedFlatItems(currentMoney.Keys.Where(id => !savedIds.Contains(id)))
                },
                Skills = response.insurancePlayerSkills,
                TradersData = response.insurancePlayerTraders
            });
        }

        private static void OpenLoadoutEditorInsurance(string accountId)
        {
            if (InsuranceRefreshRequiredForAid != null) RefreshInsurancePlayerState();
            var options = TryLoadProfileOptions(accountId);
            LoadoutEditorFollowerInsurance = options?.InsuredItems ?? new List<FriendlyTeammateInsuredItem>();
            SetPlayerInsurancePolicies(options?.PlayerInsuredItems);
            LoadoutEditorInsuranceActive = true;
            RefreshLoadoutEditorInsurance(true);
        }

        private static void SetPlayerInsurancePolicies(List<FriendlyTeammateInsuredItem> policies)
        {
            if (policies == null || ActiveProfileSession?.Profile == null) return;
            ActiveProfileSession.Profile.InsuredItems = policies.Select(policy => new InsuredProfileItems
            {
                ItemId = policy.ItemId, TraderId = policy.TraderId
            }).ToArray();
        }

        private static void ApplyCommittedInsurance(FriendlyTeammateDefaultEquipmentResponse response)
        {
            if (response == null) return;
            SetPlayerInsurancePolicies(response.playerInsuredItems);
            if (response.followerInsuredItems != null)
                LoadoutEditorFollowerInsurance = response.followerInsuredItems;
            RefreshLoadoutEditorInsurance(LoadoutEditorInsuranceActive);
        }

        private static void CloseLoadoutEditorInsurance()
        {
            if (!LoadoutEditorInsuranceActive) return;
            LoadoutEditorInsuranceActive = false;
            RefreshLoadoutEditorInsurance(false);
            LoadoutEditorFollowerInsurance = null;
        }

        private static void RefreshLoadoutEditorInsurance(bool includeFollower)
        {
            var insurance = ActiveProfileSession?.InsuranceCompany;
            var player = ActiveProfileSession?.Profile;
            if (insurance == null || player?.Inventory == null) return;
            try
            {
                IEnumerable<Item> items = player.Inventory.AllRealPlayerItems;
                if (includeFollower && LoadoutEditorProfile?.Inventory != null)
                    items = EnumerateLoadoutEditorItemTree(LoadoutEditorProfile.Inventory.Equipment)
                        .Concat(EnumerateLoadoutEditorItemTree(LoadoutEditorProfile.Inventory.Stash)).Concat(items);
                var itemsById = items.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
                var policies = (player.InsuredItems ?? Array.Empty<InsuredProfileItems>()).AsEnumerable();
                if (includeFollower && LoadoutEditorFollowerInsurance != null)
                    policies = policies.Concat(LoadoutEditorFollowerInsurance.Select(policy => new InsuredProfileItems
                    {
                        ItemId = policy.ItemId, TraderId = policy.TraderId
                    }));
                insurance.ClearInsuredItems();
                // RegisterInsuredItems leaves its missing-item cache untouched for an empty list.
                insurance._missingInsuredItems?.Clear();
                insurance.RegisterInsuredItems(
                    policies.GroupBy(policy => policy.ItemId).Select(group => group.First()).ToArray(),
                    itemsById.Values);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError($"[FollowerInsurance:Display] {ex}");
            }
        }
    }
}

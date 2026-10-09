using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Screens;
using Newtonsoft.Json;
using pitTeam.Components;
using UnityEngine;

namespace pitTeam.Modules
{
    /// <summary>Owns one local, cancelable draft. Only Apply may touch the live inventories.</summary>
    internal sealed class TeammateGearSwap
    {
        internal static readonly HashSet<EquipmentSlot> VisibleSlots = new HashSet<EquipmentSlot>
        {
            EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon,
            EquipmentSlot.TacticalVest, EquipmentSlot.ArmorVest, EquipmentSlot.Headwear,
            EquipmentSlot.FaceCover, EquipmentSlot.Eyewear, EquipmentSlot.Earpiece
        };
        internal static bool IsVisibleFollowerSlot(EquipmentSlot slot, bool allowBackpack) =>
            VisibleSlots.Contains(slot) || (allowBackpack && slot == EquipmentSlot.Backpack);
        internal static readonly EquipmentSlot[] FirearmSlots =
            { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster };
        internal static TeammateGearSwap Current { get; private set; }
        internal readonly GamePlayerOwner Owner;
        internal readonly BotOwner Bot;
        internal readonly BotFollowerPlayer Follower;
        internal readonly GearSwapInventoryController PlayerDraft, BotDraft;
        internal InventoryEquipment PlayerEquipment => PlayerDraft.Inventory.Equipment;
        internal InventoryEquipment FollowerEquipment => BotDraft.Inventory.Equipment;
        internal bool Applying { get; private set; }
        internal bool Mutating { get; private set; }
        internal readonly bool AllowBackpackSwap;
        private readonly HashSet<string> _opaqueBackpackIds = new HashSet<string>();
        private readonly HashSet<string> _initialOpaqueBackpackIds = new HashSet<string>();
        private readonly List<GearSwapEdit> _edits = new List<GearSwapEdit>();
        private readonly Dictionary<string, Item> _draftItems;
        private readonly string _playerBefore, _botBefore;
        private readonly HashSet<string> _botIds, _returnIds;
        private readonly HashSet<string> _playerOwnedIds;
        private readonly InventoryScreen.RaidInventoryScreenController _screen;
        private readonly GearSwapDiagnostics _diagnostics;
        private bool _closed, _cancelled, _committed;
        private string _cancelReason;
        private float _openedAt;
        private readonly Dictionary<GameObject, bool> _hiddenTabs = new Dictionary<GameObject, bool>();

        private TeammateGearSwap(EftGamePlayerOwner owner, BotOwner bot, BotFollowerPlayer follower)
        {
            Owner = owner; Bot = bot; Follower = follower;
            AllowBackpackSwap = follower.IsSpawnedSquadMate;
            _diagnostics = new GearSwapDiagnostics(owner.Player, bot);
            _playerBefore = Snapshot(owner.Player.InventoryController.Inventory.Equipment);
            _botBefore = Snapshot(bot.GetPlayer.InventoryController.Inventory.Equipment);
            _botIds = new HashSet<string>(Tree(bot.GetPlayer.InventoryController.Inventory.Equipment).Select(i => (string)i.Id));
            _returnIds = new HashSet<string>();
            var tracked = new HashSet<string>(InteractableObjects.GetStoredItems(bot.ProfileId) ?? new List<string>());
            foreach (Item item in Tree(bot.GetPlayer.InventoryController.Inventory.Equipment).Where(i => tracked.Contains(i.Id)))
                _returnIds.UnionWith(Tree(item).Select(i => (string)i.Id));
            _playerOwnedIds = new HashSet<string>(Tree(owner.Player.InventoryController.Inventory.Equipment).Select(i => (string)i.Id));
            _playerOwnedIds.UnionWith(_returnIds);
            Profile playerProfile = Clone(owner.Player.Profile);
            Profile botProfile = Clone(bot.Profile);
            PlayerDraft = new GearSwapInventoryController(playerProfile) { Session = this };
            BotDraft = new GearSwapInventoryController(botProfile) { Session = this };
            if (AllowBackpackSwap)
            {
                RememberBackpack(FollowerEquipment, _opaqueBackpackIds);
                _initialOpaqueBackpackIds.UnionWith(_opaqueBackpackIds);
            }
            _draftItems = Index(PlayerEquipment, FollowerEquipment);
            var liveItems = Index(owner.Player.InventoryController.Inventory.Equipment, bot.GetPlayer.InventoryController.Inventory.Equipment);
            if (_draftItems.Any(pair => liveItems.TryGetValue(pair.Key, out Item live) && ReferenceEquals(live, pair.Value)))
                throw new InvalidOperationException("Draft inventory contains a live item reference.");
            _screen = new InventoryScreen.RaidInventoryScreenController((IEftSession)owner.Session, playerProfile,
                owner.Player.HealthController, PlayerDraft, owner.Player.QuestController,
                owner.Player.AchievementsController, owner.Player.PrestigeController, FollowerEquipment,
                EInventoryTab.Gear, false, EItemViewType.InventoryWithoutDiscard);
            _screen.OnClose += OnScreenClosed;
        }

        internal static void Open(GamePlayerOwner owner)
        {
            if (Current != null || !(owner is EftGamePlayerOwner eftOwner) ||
                !EftScreenManager.Instance.CheckCurrentScreen(EEftScreenType.BattleUI)) return;
            BotOwner bot = TeammateBackpackInspection.ResolveInteractionTargetPlayer(owner.Player, false)?.AIData?.BotOwner;
            BotFollowerPlayer follower = BossPlayers.Instance?.GetFollower(bot);
            if (!Safe(owner, bot, follower)) { Warn("SwapGearUnavailable"); return; }
            try
            {
                var session = new TeammateGearSwap(eftOwner, bot, follower);
                Current = session;
                session._openedAt = Time.time;
                owner.Player.SetInventoryOpened(true);
                session._screen.ShowScreen(EScreenState.Queued);
                Logger.LogInfo($"[SwapGear] Open follower='{bot.Profile.Nickname}' draftOnly=true backpackSwap={session.AllowBackpackSwap}");
                session._diagnostics.State("open");
                session._diagnostics.Draft("open", session.PlayerEquipment, session.FollowerEquipment);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                Current?.Close();
                Warn("SwapGearUnavailable");
            }
        }

        internal static bool Holds(BotFollowerPlayer follower) => Current != null && ReferenceEquals(Current.Follower, follower);
        internal static bool Holds(BotWeaponManager manager) => Current != null && ReferenceEquals(Current.Bot.WeaponManager, manager);
        internal bool IsLiveLocked(Item item) => !Mutating && item != null &&
            (ReferenceEquals(item.Owner, Owner.Player.InventoryController) || ReferenceEquals(item.Owner, Bot.GetPlayer.InventoryController));
        internal static bool TreatCommitAddressKnown(ItemAddress address) => Current?.Mutating == true &&
            CanPlace(address, Current.Owner.Player.InventoryController.Inventory.Equipment, Current.Bot.GetPlayer.InventoryController.Inventory.Equipment,
                Current.AllowBackpackSwap);
        internal static bool TreatCommitItemKnown(Item item, ItemAddress address = null) =>
            // Menu weapon presets are detached roots: Parent throws even when item is non-null.
            // Do not resolve any item address unless a real exchange is committing.
            Current?.Mutating == true && TreatCommitAddressKnown(address ?? item?.CurrentAddress);

        internal static void Update(GamePlayerOwner owner)
        {
            var session = Current;
            if (session == null || !ReferenceEquals(session.Owner, owner)) return;
            try
            {
                var tabs = CommonUI.Instance?.InventoryScreen?._tabDictionary;
                if (tabs != null)
                    foreach (var tab in tabs.Where(t => t.Key != EInventoryTab.Gear))
                    {
                        var obj = tab.Value.gameObject;
                        if (!session._hiddenTabs.ContainsKey(obj)) session._hiddenTabs.Add(obj, obj.activeSelf);
                        obj.SetActive(false);
                    }
                string reason = UnsafeReason(owner, session.Bot, session.Follower, session.Applying);
                if (reason == null && Time.time - session._openedAt > 1f &&
                    !EftScreenManager.Instance.CheckCurrentScreen(EEftScreenType.Inventory)) reason = "inventoryScreenChanged";
                if (reason != null)
                {
                    session.Cancel(reason);
                    if (!session.Applying) session.Close();
                }
            }
            catch (Exception ex) { Logger.LogError(ex); session.Cancel("updateException:" + ex.GetType().Name); if (!session.Applying) session.Close(); }
        }

        private void Cancel(string reason)
        {
            if (_cancelled) return;
            _cancelled = true;
            _cancelReason = reason;
            _diagnostics.Write("cancel", $"reason={reason} applying={Applying} committed={_committed}");
            _diagnostics.State("cancel");
        }

        private static bool Safe(GamePlayerOwner owner, BotOwner bot, BotFollowerPlayer follower, bool applying = false) =>
            UnsafeReason(owner, bot, follower, applying) == null;

        private static string UnsafeReason(GamePlayerOwner owner, BotOwner bot, BotFollowerPlayer follower, bool applying)
        {
            // This is the safety gate itself, evaluated once; diagnostics never repeat medical/AI queries.
            if (owner?.Player?.HealthController?.IsAlive != true) return "playerDeadOrUnavailable";
            if (bot == null || follower == null) return "followerUnavailable";
            if (follower.GetBoss()?.realPlayer != owner.Player) return "followerOwnershipChanged";
            if (bot.IsDead || bot.BotState != EBotState.Active || bot.GetPlayer?.HealthController?.IsAlive != true)
                return "followerDeadOrInactive";
            float distance = Vector3.Distance(owner.Player.Position, bot.GetPlayer.Position);
            if (distance > 2.5f) return $"outOfRange:{distance:0.00}m";
            if (follower.HasKnownEnemy()) return "knownEnemy";
            if (bot.Memory?.HaveEnemy == true) return "nativeEnemy";
            if (bot.Memory?.IsUnderFire == true) return "underFire";
            if (TeammateBackpackInspection.HasActiveOrPendingHealWork(bot)) return "healing";
            if (TeammateBackpackInspection.HasActiveOrPendingPickupWork(bot, follower)) return "looting";
            if (applying) return null;
            if (bot.WeaponManager?.Selector?.IsChanging == true) return "followerWeaponChanging";
            if (bot.WeaponManager?.Grenades?.ThrowindNow == true) return "throwingGrenade";
            if (bot.WeaponManager?.info?.Values.Any(i => i?.Reload?.Reloading == true) == true) return "reloading";
            if (owner.Player.InventoryController.IsChangingWeapon) return "playerWeaponChanging";
            return bot.WeaponManager?.CanChangeHands() == true ? null : "followerHandsBusy";
        }

        private static Profile Clone(Profile source)
        {
            Profile profile = source.Clone();
            profile.Inventory = new InventoryDescriptor(source.Inventory, FullySearchedSearchController.Instance).ToInventory();
            return profile;
        }

        internal static IEnumerable<Item> Tree(Item item)
        {
            if (item == null) return Enumerable.Empty<Item>();
            return item is CompoundItem compound ? compound.GetAllItems().Prepend(item).Distinct() : new[] { item };
        }
        private static Dictionary<string, Item> Index(InventoryEquipment player, InventoryEquipment bot) =>
            Tree(player).Concat(Tree(bot)).ToDictionary(i => (string)i.Id, i => i);
        private static string Snapshot(InventoryEquipment equipment) =>
            GearSwapSnapshot.Normalize(JsonConvert.SerializeObject(
                ItemBinarySerializer.SerializeItem(equipment, FullySearchedSearchController.Instance)));

        internal static bool CanEdit(Item item, InventoryEquipment player, InventoryEquipment bot,
            bool allowBackpack = false, ISet<string> opaqueBackpacks = null)
        {
            if (item == null || ReferenceEquals(item, player) || ReferenceEquals(item, bot)) return false;
            return CanPlace(item.CurrentAddress, player, bot, allowBackpack, opaqueBackpacks);
        }
        internal static bool CanPlace(ItemAddress address, InventoryEquipment player, InventoryEquipment bot,
            bool allowBackpack = false, ISet<string> opaqueBackpacks = null)
        {
            for (int depth = 0; address != null && depth < 64; depth++)
            {
                Item parent = address.Container.ParentItem;
                // A whole bag may move, but its contents stay sealed for this draft even
                // after moving the bag onto the player's side or nesting it in another bag.
                if (parent != null && opaqueBackpacks?.Contains(parent.Id) == true) return false;
                if (player != null && ReferenceEquals(parent, player)) return true;
                if (ReferenceEquals(parent, bot))
                    return Enum.TryParse(address.Container.ID, out EquipmentSlot slot) &&
                        IsVisibleFollowerSlot(slot, allowBackpack) && (slot != EquipmentSlot.Backpack || depth == 0);
                address = parent?.CurrentAddress;
            }
            return false;
        }
        internal static bool IsInOpaqueBackpack(Item item, ISet<string> opaqueBackpacks)
        {
            for (int depth = 0; item != null && depth < 64; depth++)
            {
                if (opaqueBackpacks.Contains(item.Id)) return true;
                item = item.CurrentAddress?.Container?.ParentItem;
            }
            return false;
        }
        internal bool IsDraft(Item item) => item != null && _draftItems.TryGetValue(item.Id, out Item found) && ReferenceEquals(found, item);
        internal bool CanSeeFollowerSlot(EquipmentSlot slot) => IsVisibleFollowerSlot(slot, AllowBackpackSwap);
        internal bool IsOpaqueBackpack(Item item) => IsDraft(item) && IsInOpaqueBackpack(item, _opaqueBackpackIds);
        internal bool CanEditDraft(Item item) => CanEdit(item, PlayerEquipment, FollowerEquipment, AllowBackpackSwap, _opaqueBackpackIds);
        internal bool CanPlaceDraft(ItemAddress address) => CanPlace(address, PlayerEquipment, FollowerEquipment, AllowBackpackSwap, _opaqueBackpackIds);
        internal bool CanEditPlateSlot(Slot slot, bool allowCommit = false)
        {
            if (_closed || _cancelled || !(slot is ArmorSlot) || slot.Locked || slot.ParentItem == null) return false;
            Item carrier = slot.ParentItem;
            if (IsDraft(carrier)) return !Applying && CanEditDraft(carrier);
            // Replay uses the original carriers. Only the synchronous, validated Apply
            // transaction may bypass their equipped-state check; ordinary live UI may not.
            return allowCommit && Mutating && TreatCommitAddressKnown(carrier.CurrentAddress);
        }
        private static void RememberBackpack(InventoryEquipment equipment, ISet<string> opaqueBackpacks)
        {
            Item backpack = equipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
            if (backpack != null) opaqueBackpacks.Add(backpack.Id);
        }

        internal void Stage(GearSwapEdit edit, InventoryController controller)
        {
            if (_closed || _cancelled || Applying) throw new InvalidOperationException("Draft is closed.");
            edit.ValidateProvenance(_playerOwnedIds);
            _diagnostics.Edit("stage", _edits.Count + 1, edit, _draftItems);
            IOperationResult applied = edit.Execute(_draftItems, controller, CanEditDraft, CanPlaceDraft);
            if (AllowBackpackSwap) RememberBackpack(FollowerEquipment, _opaqueBackpackIds);
            _edits.Add(edit);
            edit.UpdateProvenance(_playerOwnedIds);
            // UI notifications affect only owners of the cloned items.
            applied.RaiseEvents(controller, CommandStatus.Begin);
            applied.RaiseEvents(controller, CommandStatus.Succeed);
            Logger.LogInfo($"[SwapGear] Staged edit={_edits.Count} playerEvents={PlayerDraft.ActiveEvents.Count} followerEvents={BotDraft.ActiveEvents.Count}");
            _diagnostics.Draft("staged", PlayerEquipment, FollowerEquipment);
        }

        private void ValidateLive()
        {
            string reason = _closed ? "sessionClosed" : _cancelled ? _cancelReason : UnsafeReason(Owner, Bot, Follower, Applying);
            if (reason != null)
            {
                Cancel(reason);
                throw new InvalidOperationException("Swap interrupted: " + reason);
            }
            if (_playerBefore != Snapshot(Owner.Player.InventoryController.Inventory.Equipment) ||
                _botBefore != Snapshot(Bot.GetPlayer.InventoryController.Inventory.Equipment))
            {
                _diagnostics.Write("stale", "live equipment differs from opening snapshot");
                throw new GearSwapValidationException("SwapGearStale");
            }
        }

        internal async Task Apply()
        {
            if (Applying || _closed) return;
            if (_edits.Count == 0) { Close(); return; }
            bool committed = false;
            bool handsStarted = false;
            bool refreshFailed = false;
            bool keepDraft = false;
            Item playerHands = Owner.Player.HandsController?.Item;
            Item botHands = Bot.GetPlayer.HandsController?.Item;
            try
            {
                _diagnostics.State("apply-start");
                _diagnostics.Draft("accepted-draft", PlayerEquipment, FollowerEquipment);
                ValidateLive();
                ValidateFinalEquipment();
                Applying = true;
                handsStarted = true;
                // Hands must release original item references before any equipment changes.
                await EmptyHands(Owner.Player);
                await EmptyHands(Bot.GetPlayer);
                _diagnostics.State("hands-released");
                ValidateLive();
                var player = Owner.Player.InventoryController;
                var bot = Bot.GetPlayer.InventoryController;
                var items = Index(player.Inventory.Equipment, bot.Inventory.Equipment);
                var replayOpaqueBackpacks = new HashSet<string>(_initialOpaqueBackpackIds);
                List<IOperationResult> applied;
                Mutating = true;
                try
                {
                    applied = GearSwapTransaction.Execute(_edits.Select((edit, index) => (Func<IOperationResult>)(() =>
                    {
                        _diagnostics.Edit("replay", index + 1, edit, items);
                        var result = edit.Execute(items, player,
                            i => CanEdit(i, player.Inventory.Equipment, bot.Inventory.Equipment, AllowBackpackSwap, replayOpaqueBackpacks),
                            a => CanPlace(a, player.Inventory.Equipment, bot.Inventory.Equipment, AllowBackpackSwap, replayOpaqueBackpacks));
                        // Match draft order: player contents edited before handing over a bag
                        // must replay before that replacement becomes sealed on the follower.
                        if (AllowBackpackSwap) RememberBackpack(bot.Inventory.Equipment, replayOpaqueBackpacks);
                        return result;
                    })),
                        result => result.RollBack(), () =>
                        {
                            string playerActual = Snapshot(player.Inventory.Equipment), playerExpected = Snapshot(PlayerEquipment);
                            string followerActual = Snapshot(bot.Inventory.Equipment), followerExpected = Snapshot(FollowerEquipment);
                            if (playerActual != playerExpected || followerActual != followerExpected)
                            {
                                // Capture the difference before rollback restores the original equipment.
                                _diagnostics.SnapshotMismatch("player", playerActual, playerExpected);
                                _diagnostics.SnapshotMismatch("follower", followerActual, followerExpected);
                                throw new InvalidOperationException("Replay did not match the accepted draft.");
                            }
                        });
                }
                finally { Mutating = false; }
                committed = true;
                _committed = true;
                _diagnostics.State("replay-complete");
                // Establish provenance first: a visual/event subscriber failing must not lose returns.
                var publicationErrors = new List<Exception>();
                try { TrackOwnership(); }
                catch (Exception ex) { publicationErrors.Add(ex); }
                // No await between mutation and publication. Native events update armor, visuals and weights.
                foreach (IOperationResult result in applied)
                {
                    try { result.RaiseEvents(player, CommandStatus.Begin); }
                    catch (Exception ex) { publicationErrors.Add(ex); }
                    try { result.RaiseEvents(player, CommandStatus.Succeed); }
                    catch (Exception ex) { publicationErrors.Add(ex); }
                }
                _diagnostics.State("events-published");
                foreach (Item item in Tree(player.Inventory.Equipment).Where(i => _botIds.Contains(i.Id)))
                    TeammateBackpackInspection.MarkItemTreeVisible(Owner.Player.SearchController, item);
                RefreshWeapons();
                _diagnostics.State("weapon-cache-refreshed");
                Bot.WeaponManager.Grenades.SetDirty();
                Bot.WeaponManager.Grenades.UpdateCheck();
                if (publicationErrors.Count > 0) throw new AggregateException("Equipment event publication failed.", publicationErrors);
            }
            catch (Exception ex)
            {
                refreshFailed = true;
                // Correctable draft errors occur before any hands or live inventory changes.
                // Preserve all staged edits so the player can fix the arrangement and retry.
                keepDraft = !handsStarted && ex is GearSwapValidationException draftError &&
                    (draftError.Key == "SwapGearMagazineSpace" || draftError.Key == "SwapGearNeedsWeapon");
                _diagnostics.State(committed ? "apply-refresh-error" : "apply-rejected");
                if (keepDraft) Logger.LogInfo($"[SwapGear] Draft needs correction; kept open: {ex.Message}");
                else Logger.LogError("[SwapGear] Apply " + (committed ? "committed; refresh failed" : "rejected/rolled back") + ": " + ex);
                Warn(committed ? "SwapGearRefreshFailed" : ex is AggregateException ? "SwapGearRecoveryFailed" :
                    ex is GearSwapValidationException validation ? validation.Key : "SwapGearApplyFailed");
            }
            finally
            {
                try
                {
                    if (Applying)
                    {
                        if (committed) RefreshWeapons();
                        _diagnostics.State("before-hands-restore");
                        await RestoreHands(Bot.GetPlayer, committed ? null : botHands);
                        _diagnostics.State("after-follower-hands-restore");
                        if (Bot.GetPlayer.HealthController.IsAlive && Bot.GetPlayer.HandsController != null)
                        {
                            Bot.WeaponManager.UpdateHandsController(Bot.GetPlayer.HandsController, out bool allFine);
                            _diagnostics.Write("native-hands-binding", $"allFine={allFine}");
                            _diagnostics.State("native-hands-bound");
                            if (!allFine) throw new InvalidOperationException("Native follower hands binding was rejected.");
                            if (committed) SainEquipmentBridge.RefreshAfterExchange(Bot);
                            _diagnostics.State("sain-cache-refreshed");
                        }
                    }
                }
                catch (Exception ex)
                {
                    refreshFailed = true;
                    Logger.LogError("[SwapGear] Hands/equipment refresh failed: " + ex);
                    Warn("SwapGearRefreshFailed");
                }
                try
                {
                    if (Applying) await RestoreHands(Owner.Player, playerHands);
                    _diagnostics.State("after-player-hands-restore");
                }
                catch (Exception ex)
                {
                    refreshFailed = true;
                    Logger.LogError("[SwapGear] Player hands restore failed: " + ex);
                    Warn("SwapGearRefreshFailed");
                }
                if (committed && !refreshFailed)
                    Logger.LogInfo($"[SwapGear] Applied follower='{Bot.Profile.Nickname}' edits={_edits.Count}; hands verified");
                Applying = false;
                if (!keepDraft) Close();
                if (handsStarted) _diagnostics.Settled();
            }
        }

        private void ValidateFinalEquipment()
        {
            // Empty intermediate slots are allowed; do not release a bot with no functional firearm.
            if (!FirearmSlots.Select(s => FollowerEquipment.GetSlot(s).ContainedItem).OfType<Weapon>()
                .Any(w => !w.MissingVitalParts.Any()))
                throw new GearSwapValidationException("SwapGearNeedsWeapon");
            bool magazineRoom = pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(FollowerEquipment, out string reserveDetail);
            _diagnostics.Write("reload-space", $"fits={magazineRoom} {reserveDetail}");
            if (!magazineRoom)
                throw new GearSwapValidationException("SwapGearMagazineSpace");
        }

        private void TrackOwnership()
        {
            var equipment = Bot.GetPlayer.InventoryController.Inventory.Equipment;
            var items = Tree(equipment).ToArray();
            var ids = new HashSet<string>(items.Select(i => (string)i.Id));
            foreach (string id in _botIds.Where(id => !ids.Contains(id)))
                InteractableObjects.RemoveStoredItem(Bot.ProfileId, id);
            var returned = _returnIds.Where(id => !ids.Contains(id)).ToArray();
            if (returned.Length > 0)
                InteractableObjects.RemoveProtectedRaidItemIds(returned, "Swap Gear returned to player", synchronous: true);
            // Track each new descendant too: a plate or magazine may later leave its original parent.
            InteractableObjects.StoreGearSwapReturnItems(Bot,
                items.Where(i => _playerOwnedIds.Contains(i.Id) && !ReferenceEquals(i, equipment)));
            foreach (EquipmentSlot slot in VisibleSlots)
            {
                Item root = equipment.GetSlot(slot).ContainedItem;
                if (root != null) InteractableObjects.ClearStrictCargoTree(Bot, root);
            }
            var magazines = items.OfType<EFT.InventoryLogic.Magazine>()
                .Where(m => CanPlace(m.CurrentAddress, null, equipment) && !InteractableObjects.IsStrictCargoItem(Bot, m)).ToArray();
            foreach (Weapon weapon in FirearmSlots.Select(s => equipment.GetSlot(s).ContainedItem).OfType<Weapon>())
            {
                if (_playerOwnedIds.Contains(weapon.Id)) InteractableObjects.RegisterLootedWeaponTree(Bot, weapon);
                Slot magazineSlot = weapon.GetMagazineSlot();
                if (magazineSlot == null) continue;
                foreach (var magazine in magazines.Where(m => magazineSlot.CanAccept(m)))
                    InteractableObjects.RegisterLootedWeaponMagazine(Bot, weapon, magazine);
            }
        }

        private void RefreshWeapons()
        {
            FollowerLootedPrimaryWeaponBinding.ReconcileExchangedEquipment(Bot);
        }

        private Task EmptyHands(Player player)
        {
            return GearSwapHandsTransition.Run<IEmptyHandsController>(
                () => player?.HealthController?.IsAlive == true,
                () => HandsIdle(player),
                complete =>
                {
                    _diagnostics.Actor("empty-hands-request", player);
                    player.SetEmptyHands(result =>
                    {
                        _diagnostics.Hands("empty-hands-callback", player, null, result.Value, result.Succeed, result.Error);
                        complete(result.Value, result.Succeed, result.Error);
                        _diagnostics.Actor("empty-hands-callback-exit", player);
                    });
                },
                returned => returned != null && player.HandsController is Player.EmptyHandsController &&
                    !player.HandsController.Destroyed,
                attempt => _diagnostics.Write("empty-hands-mismatch", $"attempt={attempt}"));
        }
        private Task RestoreHands(Player player, Item preferred)
        {
            if (player?.HealthController?.IsAlive != true) return Task.CompletedTask;
            return GearSwapBodyRefresh.Run(
                () => player?.HealthController?.IsAlive == true,
                () => player.PlayerBody.SlotViews.Where(view => view.LoadingJob != null).Select(view => view.LoadingJob).ToArray(),
                () => RestoreHandsCore(player, preferred),
                () => VerifyHeldWeaponBody(player),
                phase => _diagnostics.Actor(phase, player));
        }

        private static void VerifyHeldWeaponBody(Player player)
        {
            if (!(player.HandsController?.Item is Weapon held)) return;
            foreach (EquipmentSlot slot in FirearmSlots)
            {
                if (player.InventoryController.Inventory.Equipment.GetSlot(slot).ContainedItem?.Id != held.Id) continue;
                var views = player.PlayerBody.SlotViews;
                var view = views.ContainsKey(slot) ? views.GetByKey(slot) : null;
                // EFT's item-in-hands binding must suppress the held gun's body-slot model.
                // Do not destroy arbitrary pooled objects or rebuild the whole player body.
                if (view == null || view._item != null || view.Model != null)
                    throw new InvalidOperationException($"Held weapon still has a body-slot binding/model: {slot}, {held.Id}.");
            }
        }

        private Task RestoreHandsCore(Player player, Item preferred)
        {
            if (player?.HealthController?.IsAlive != true) return Task.CompletedTask;
            var equipment = player.InventoryController.Inventory.Equipment;
            // Never restore a gun that the accepted draft moved into cargo, or an incomplete firearm.
            bool preferredEquipped = preferred?.CurrentAddress?.Container?.ParentItem == equipment &&
                (!(preferred is Weapon preferredWeapon) || !preferredWeapon.MissingVitalParts.Any());
            Item item = preferredEquipped ? preferred : FirearmSlots
                .Select(s => equipment.GetSlot(s).ContainedItem).OfType<Weapon>()
                .FirstOrDefault(w => !w.MissingVitalParts.Any());
            // A player may deliberately give away their last firearm; null is not a valid
            // argument to EFT's item-generic SetInHands and must use the empty-hands path.
            if (item == null) return EmptyHands(player);
            return GearSwapHandsTransition.Run<IHandsController>(
                () => player?.HealthController?.IsAlive == true,
                () => HandsIdle(player),
                complete =>
                {
                    _diagnostics.Hands("restore-hands-request", player, item, null, false, null);
                    player.SetInHands(item, result =>
                    {
                        _diagnostics.Hands("restore-hands-callback", player, item, result.Value, result.Succeed, result.Error);
                        complete(result.Value, result.Succeed, result.Error);
                        _diagnostics.Actor("restore-hands-callback-exit", player);
                    });
                },
                returned => returned?.Item?.Id == item.Id && player.HandsController?.Item?.Id == item.Id &&
                    player.HandsController?.Destroyed == false &&
                    (!(item is Weapon) || returned is IFirearmHandsController),
                attempt => _diagnostics.Write("restore-hands-mismatch", $"attempt={attempt} requested={GearSwapDiagnostics.Item(item)}"));
        }
        private static bool HandsIdle(Player player) => player.ProcessStatus == Player.EProcessStatus.None &&
            !player.InventoryController.IsChangingWeapon;
        private void OnScreenClosed()
        {
            if (!_committed) Cancel("inventoryScreenClosed");
            if (!Applying) Cleanup();
        }
        internal void Close()
        {
            if (!_closed) _screen.CloseScreen();
            Cleanup();
        }
        private void Cleanup()
        {
            if (_closed) return;
            _closed = true;
            _screen.OnClose -= OnScreenClosed;
            Owner.Player.SetInventoryOpened(false);
            PlayerDraft.Session = null;
            BotDraft.Session = null;
            foreach (var tab in _hiddenTabs)
                if (tab.Key != null) tab.Key.SetActive(tab.Value);
            _hiddenTabs.Clear();
            if (ReferenceEquals(Current, this)) Current = null;
            _diagnostics.State("closed");
            Logger.LogInfo($"[SwapGear] Closed; {(_committed ? "exchange committed" : "draft discarded")}; stagedEdits={_edits.Count}.");
        }
        private static void Warn(string key) => EFT.Communications.NotificationManager.DisplayWarningNotification(
            pitFireTeam.GetSocialUiText(key), EFT.Communications.ENotificationDurationType.Default);

        private sealed class GearSwapValidationException : InvalidOperationException
        {
            internal readonly string Key;
            internal GearSwapValidationException(string key) : base(key) { Key = key; }
        }
    }
}

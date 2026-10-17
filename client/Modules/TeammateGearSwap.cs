using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
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
            EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster,
            EquipmentSlot.TacticalVest, EquipmentSlot.ArmorVest, EquipmentSlot.Headwear,
            EquipmentSlot.FaceCover, EquipmentSlot.Eyewear, EquipmentSlot.Earpiece, EquipmentSlot.Pockets
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
        private readonly List<GearSwapEdit> _edits = new List<GearSwapEdit>();
        private readonly Dictionary<string, Item> _draftItems;
        private readonly string _playerBefore, _botBefore;
        private readonly HashSet<string> _botIds, _returnIds;
        private readonly HashSet<string> _playerOwnedIds;
        private readonly InventoryScreen.RaidInventoryScreenController _screen;
        private bool _closed, _cancelled, _committed;
        private bool _worldEnding, _movementHeld, _previousMoverPause;
        private GearSwapApplyScope _applyScope;
        private string _cancelReason;
        private float _openedAt;
        private readonly Dictionary<GameObject, bool> _hiddenTabs = new Dictionary<GameObject, bool>();

        private TeammateGearSwap(EftGamePlayerOwner owner, BotOwner bot, BotFollowerPlayer follower)
        {
            Owner = owner; Bot = bot; Follower = follower;
            // Capture before the editor's patrol inspection hold pauses movement.
            _previousMoverPause = bot.Mover?.Pause ?? false;
            AllowBackpackSwap = follower.IsSpawnedSquadMate;
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
            string rejection = UnsafeReason(owner, bot, follower, false);
            if (rejection != null)
            {
                Warn("SwapGearUnavailable");
                return;
            }
            try
            {
                var session = new TeammateGearSwap(eftOwner, bot, follower);
                Current = session;
                GearSwapInsuranceDiagnostics.Begin(bot.ProfileId, session._botIds.Concat(session._playerOwnedIds));
                session.TraceInsurance("open", draft: true);
                session._openedAt = Time.time;
                owner.Player.SetInventoryOpened(true);
                session._screen.ShowScreen(EScreenState.Queued);
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
        internal static bool Protects(Player player) => Current != null && ReferenceEquals(Current.Bot.GetPlayer, player)
            && Current.Applying && Current._applyScope?.Active == true && !Current._worldEnding
            && player != null && !player.Destroyed && player.HealthController?.IsAlive == true;
        internal static bool Protects(EFT.HealthSystem.ActiveHealthController health) => Current != null
            && ReferenceEquals(Current.Bot.GetPlayer?.ActiveHealthController, health) && Protects(Current.Bot.GetPlayer);

        internal static void EndRaid()
        {
            var session = Current;
            if (session == null) return;
            session._worldEnding = true;
            session.Cancel("raidEnded");
            session._applyScope?.Stop();
            session.ReleaseMovement();
            try { session.Close(); }
            catch (Exception ex) { Logger.LogError("[SwapGear] Teardown UI cleanup failed: " + ex); }
        }

        internal static void ActorDying(EFT.HealthSystem.ActiveHealthController health)
        {
            var session = Current;
            if (session == null || health == null) return;
            if (ReferenceEquals(session.Owner.Player?.ActiveHealthController, health)) EndRaid();
            else if (ReferenceEquals(session.Bot.GetPlayer?.ActiveHealthController, health))
            {
                session.Cancel("followerDied");
                session.ReleaseMovement();
                if (!session.Applying)
                {
                    try { session.Close(); }
                    catch (Exception ex) { Logger.LogError("[SwapGear] Death UI cleanup failed: " + ex); }
                }
            }
        }

        internal static void CheckApplyDeadline()
        {
            var session = Current;
            if (session?.Applying != true || session._applyScope?.Active != false) return;
            session.Cancel("applyDeadline");
            session.ReleaseMovement();
        }

        private bool CanContinue(Player player) => !_closed && !_worldEnding && ReferenceEquals(Current, this)
            && _applyScope?.Active == true && player != null && !player.Destroyed && player.HealthController?.IsAlive == true;

        private void HoldMovement()
        {
            _movementHeld = true;
            Bot.Mover.Pause = true;
            Bot.GetPlayer.Move(Vector2.zero);
            Utils.FollowerRecovery.StopShooting(Bot);
        }

        private void ReleaseMovement()
        {
            if (!_movementHeld) return;
            _movementHeld = false;
            if (Bot?.Mover != null) Bot.Mover.Pause = _previousMoverPause;
        }
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
                CheckApplyDeadline();
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
            _applyScope?.CancelPreparation();
        }

        private static string UnsafeReason(GamePlayerOwner owner, BotOwner bot, BotFollowerPlayer follower, bool applying)
        {
            // Evaluate the safety gate once without changing medical/AI state.
            if (owner?.Player?.HealthController?.IsAlive != true) return "playerDeadOrUnavailable";
            if (bot == null || follower == null) return "followerUnavailable";
            if (follower.GetBoss()?.realPlayer != owner.Player) return "followerOwnershipChanged";
            // Field recruitment must not grant access to strip a friendly bot's kit.
            // Keep this before the Apply hands-transition exception as well as at opening.
            if (!follower.IsSpawnedSquadMate) return "followerNotSpawnedSquadMate";
            if (bot.IsDead || bot.BotState != EBotState.Active || bot.GetPlayer?.HealthController?.IsAlive != true)
                return "followerDeadOrInactive";
            float distance = Vector3.Distance(owner.Player.Position, bot.GetPlayer.Position);
            if (distance > 2.5f) return $"outOfRange:{distance:0.00}m";
            // An accepted protected Apply finishes without clearing newly acquired enemy knowledge.
            if (applying) return null;
            if (follower.HasKnownEnemy()) return "knownEnemy";
            if (bot.Memory?.HaveEnemy == true) return "nativeEnemy";
            if (bot.Memory?.IsUnderFire == true) return "underFire";
            if (TeammateBackpackInspection.HasActiveOrPendingHealWork(bot)) return "healing";
            if (TeammateBackpackInspection.HasActiveOrPendingPickupWork(bot, follower)) return "looting";
            if (bot.WeaponManager?.Selector?.IsChanging == true) return "followerWeaponChanging";
            if (bot.WeaponManager?.Grenades?.ThrowindNow == true) return "throwingGrenade";
            // EFT polls only the active reload record. A stale flag on a stowed
            // weapon is not an in-flight hands operation; native hands safety still follows.
            if (bot.WeaponManager?._currentWeaponInfo?.Reload?.Reloading == true) return "reloading";
            if (owner.Player.InventoryController.IsChangingWeapon) return "playerWeaponChanging";
            return bot.WeaponManager?.CanChangeHands() == true ? null : "followerHandsBusy";
        }

        private static Profile Clone(Profile source)
        {
            Profile profile = source.Clone();
            profile.Inventory = new InventoryDescriptor(source.Inventory, FullySearchedSearchController.Instance).ToInventory();
            return profile;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void TraceInsurance(string phase, bool draft)
        {
            // Read exact-ID presentation only. Never register clone items or modify policies.
            try
            {
                var insurance = (Owner.Session as IEftSession)?.InsuranceCompany;
                var player = draft ? PlayerEquipment : Owner.Player.InventoryController.Inventory.Equipment;
                var bot = draft ? FollowerEquipment : Bot.GetPlayer.InventoryController.Inventory.Equipment;
                var playerItems = Tree(player).ToArray();
                var botItems = Tree(bot).ToArray();
                GearSwapInsuranceDiagnostics.Summary(phase, FollowerInsuranceRaidDisplay.DiagnosticState(),
                    playerItems.Count(i => FollowerInsuranceRaidDisplay.IsInsured(i.Id)),
                    botItems.Count(i => FollowerInsuranceRaidDisplay.IsInsured(i.Id)));
                foreach (var pair in new[] { new { Name = "player", Equipment = player, Items = playerItems },
                    new { Name = "follower", Equipment = bot, Items = botItems } })
                {
                    var rootIds = new HashSet<string>(VisibleSlots.Concat(new[] { EquipmentSlot.Backpack })
                        .Select(slot => pair.Equipment.GetSlot(slot)?.ContainedItem).Where(i => i != null).Select(i => (string)i.Id));
                    foreach (Item item in pair.Items.Where(i => rootIds.Contains(i.Id) || FollowerInsuranceRaidDisplay.IsInsured(i.Id)))
                        GearSwapInsuranceDiagnostics.Item(phase, item.Id, item.TemplateId, pair.Name + (draft ? ":draft" : ":live"),
                            insurance?.Insured(item.Id) == true, FollowerInsuranceRaidDisplay.IsInsured(item.Id));
                }
            }
            catch (Exception ex) { Logger.LogError("[SwapGear:Insurance] Passive snapshot failed: " + ex); }
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
            bool allowBackpack = false)
        {
            if (item == null || ReferenceEquals(item, player) || ReferenceEquals(item, bot)) return false;
            return CanPlace(item.CurrentAddress, player, bot, allowBackpack);
        }
        internal static bool CanPlace(ItemAddress address, InventoryEquipment player, InventoryEquipment bot,
            bool allowBackpack = false)
        {
            bool special = false;
            for (int depth = 0; address != null && depth < 64; depth++)
            {
                Item parent = address.Container.ParentItem;
                special |= address.Container.ID?.StartsWith("SpecialSlot", StringComparison.Ordinal) == true;
                if (player != null && ReferenceEquals(parent, player)) return true;
                if (ReferenceEquals(parent, bot))
                    return !special && Enum.TryParse(address.Container.ID, out EquipmentSlot slot) &&
                        IsVisibleFollowerSlot(slot, allowBackpack) && (slot != EquipmentSlot.Pockets || depth > 0);
                address = parent?.CurrentAddress;
            }
            return false;
        }
        internal bool IsDraft(Item item) => item != null && _draftItems.TryGetValue(item.Id, out Item found) && ReferenceEquals(found, item);
        internal bool CanSeeFollowerSlot(EquipmentSlot slot) => IsVisibleFollowerSlot(slot, AllowBackpackSwap);
        internal bool IsFollowerPockets(Item item) => item != null &&
            ReferenceEquals(item, FollowerEquipment.GetSlot(EquipmentSlot.Pockets).ContainedItem);
        internal bool CanEditDraft(Item item) => CanEdit(item, PlayerEquipment, FollowerEquipment, AllowBackpackSwap);
        internal bool CanPlaceDraft(ItemAddress address) => CanPlace(address, PlayerEquipment, FollowerEquipment, AllowBackpackSwap);
        internal bool CanEditPlateSlot(Slot slot, bool allowCommit = false)
        {
            if (_closed || _cancelled || !(slot is ArmorSlot) || slot.Locked || slot.ParentItem == null) return false;
            Item carrier = slot.ParentItem;
            if (IsDraft(carrier)) return !Applying && CanEditDraft(carrier);
            // Replay uses the original carriers. Only the synchronous, validated Apply
            // transaction may bypass their equipped-state check; ordinary live UI may not.
            return allowCommit && Mutating && TreatCommitAddressKnown(carrier.CurrentAddress);
        }

        internal void Stage(GearSwapEdit edit, InventoryController controller)
        {
            if (_closed || _cancelled || Applying) throw new InvalidOperationException("Draft is closed.");
            edit.ValidateProvenance(_playerOwnedIds);
            IOperationResult applied = edit.Execute(_draftItems, controller, CanEditDraft, CanPlaceDraft);
            _edits.Add(edit);
            edit.UpdateProvenance(_playerOwnedIds);
            // UI notifications affect only owners of the cloned items.
            applied.RaiseEvents(controller, CommandStatus.Begin);
            applied.RaiseEvents(controller, CommandStatus.Succeed);
            TraceInsurance("staged:" + _edits.Count, draft: true);
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
                throw new GearSwapValidationException("SwapGearStale");
            }
        }

        internal async Task Apply()
        {
            if (Applying || _closed) return;
            if (_edits.Count == 0) { Close(); return; }
            TraceInsurance("applyDraft", draft: true);
            TraceInsurance("applyBefore", draft: false);
            bool committed = false;
            bool handsStarted = false;
            bool keepDraft = false;
            Item playerHands = Owner.Player.HandsController?.Item;
            Item botHands = Bot.GetPlayer.HandsController?.Item;
            try
            {
                ValidateLive();
                ValidateFinalEquipment();
                Applying = true;
                _applyScope = new GearSwapApplyScope();
                HoldMovement();
                handsStarted = true;
                // Hands must release original item references before any equipment changes.
                await EmptyHands(Owner.Player);
                await EmptyHands(Bot.GetPlayer);
                _applyScope.Preparation.ThrowIfCancellationRequested();
                ValidateLive();
                var player = Owner.Player.InventoryController;
                var bot = Bot.GetPlayer.InventoryController;
                var items = Index(player.Inventory.Equipment, bot.Inventory.Equipment);
                List<IOperationResult> applied;
                Mutating = true;
                try
                {
                    applied = GearSwapTransaction.Execute(_edits.Select(edit => (Func<IOperationResult>)(() =>
                    {
                        var result = edit.Execute(items, player,
                            i => CanEdit(i, player.Inventory.Equipment, bot.Inventory.Equipment, AllowBackpackSwap),
                            a => CanPlace(a, player.Inventory.Equipment, bot.Inventory.Equipment, AllowBackpackSwap));
                        return result;
                    })),
                        result => result.RollBack(), () =>
                        {
                            string playerActual = Snapshot(player.Inventory.Equipment), playerExpected = Snapshot(PlayerEquipment);
                            string followerActual = Snapshot(bot.Inventory.Equipment), followerExpected = Snapshot(FollowerEquipment);
                            if (playerActual != playerExpected || followerActual != followerExpected)
                            {
                                throw new InvalidOperationException("Replay did not match the accepted draft.");
                            }
                        });
                }
                finally { Mutating = false; }
                committed = true;
                _committed = true;
                _applyScope.Commit();
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
                foreach (Item item in Tree(player.Inventory.Equipment).Where(i => _botIds.Contains(i.Id)))
                    TeammateBackpackInspection.MarkItemTreeVisible(Owner.Player.SearchController, item);
                TraceInsurance("applyAfter", draft: false);
                RefreshWeapons();
                Bot.WeaponManager.Grenades.SetDirty();
                Bot.WeaponManager.Grenades.UpdateCheck();
                if (publicationErrors.Count > 0) throw new AggregateException("Equipment event publication failed.", publicationErrors);
            }
            catch (Exception ex)
            {
                // Correctable draft errors occur before any hands or live inventory changes.
                // Preserve all staged edits so the player can fix the arrangement and retry.
                keepDraft = !handsStarted && ex is GearSwapValidationException draftError &&
                    (draftError.Key == "SwapGearMagazineSpace" || draftError.Key == "SwapGearNeedsWeapon");
                if (!keepDraft) Logger.LogError("[SwapGear] Apply " + (committed ? "committed; refresh failed" : "rejected/rolled back") + ": " + ex);
                if (!_worldEnding) Warn(committed ? "SwapGearRefreshFailed" : ex is AggregateException ? "SwapGearRecoveryFailed" :
                    ex is GearSwapValidationException validation ? validation.Key : "SwapGearApplyFailed");
            }
            finally
            {
                try
                {
                    if (Applying && CanContinue(Bot.GetPlayer))
                    {
                        if (committed) RefreshWeapons();
                        await RestoreHands(Bot.GetPlayer, committed ? null : botHands);
                        if (CanContinue(Bot.GetPlayer) && Bot.GetPlayer.HandsController != null)
                        {
                            Bot.WeaponManager.UpdateHandsController(Bot.GetPlayer.HandsController, out bool allFine);
                            if (!allFine) throw new InvalidOperationException("Native follower hands binding was rejected.");
                            if (committed) SainEquipmentBridge.RefreshAfterExchange(Bot);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("[SwapGear] Hands/equipment refresh failed: " + ex);
                    if (!_worldEnding) Warn("SwapGearRefreshFailed");
                }
                try
                {
                    if (Applying && CanContinue(Owner.Player)) await RestoreHands(Owner.Player, playerHands);
                }
                catch (Exception ex)
                {
                    Logger.LogError("[SwapGear] Player hands restore failed: " + ex);
                    if (!_worldEnding) Warn("SwapGearRefreshFailed");
                }
                _applyScope?.Dispose();
                ReleaseMovement();
                Applying = false;
                if (!keepDraft) Close();
            }
        }

        private void ValidateFinalEquipment()
        {
            // Empty intermediate slots are allowed; do not release a bot with no functional firearm.
            if (!FirearmSlots.Select(s => FollowerEquipment.GetSlot(s).ContainedItem).OfType<Weapon>()
                .Any(w => !w.MissingVitalParts.Any()))
                throw new GearSwapValidationException("SwapGearNeedsWeapon");
            bool magazineRoom = pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(FollowerEquipment);
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
            foreach (EquipmentSlot slot in VisibleSlots.Where(slot => slot != EquipmentSlot.Pockets))
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

        private Task EmptyHands(Player player, bool restoring = false)
        {
            return GearSwapHandsTransition.Run<IEmptyHandsController>(
                () => CanContinue(player),
                () => HandsIdle(player),
                complete =>
                {
                    player.SetEmptyHands(result =>
                    {
                        complete(result.Value, result.Succeed, result.Error);
                    });
                },
                returned => returned != null && player.HandsController is Player.EmptyHandsController &&
                    !player.HandsController.Destroyed,
                timeoutMs: Math.Min(10000, _applyScope.RemainingMs),
                cancellation: restoring ? _applyScope.Lifetime : _applyScope.Preparation);
        }
        private Task RestoreHands(Player player, Item preferred)
        {
            if (!CanContinue(player)) return Task.CompletedTask;
            return GearSwapBodyRefresh.Run(
                () => CanContinue(player),
                () => player.PlayerBody.SlotViews.Where(view => view.LoadingJob != null).Select(view => view.LoadingJob).ToArray(),
                () => RestoreHandsCore(player, preferred),
                () => VerifyHeldWeaponBody(player), timeoutMs: Math.Min(10000, _applyScope.RemainingMs),
                cancellation: _applyScope.Lifetime);
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
            if (!CanContinue(player)) return Task.CompletedTask;
            var equipment = player.InventoryController.Inventory.Equipment;
            // Never restore a gun that the accepted draft moved into cargo, or an incomplete firearm.
            bool preferredEquipped = preferred?.CurrentAddress?.Container?.ParentItem == equipment &&
                (!(preferred is Weapon preferredWeapon) || !preferredWeapon.MissingVitalParts.Any());
            Item item = preferredEquipped ? preferred : FirearmSlots
                .Select(s => equipment.GetSlot(s).ContainedItem).OfType<Weapon>()
                .FirstOrDefault(w => !w.MissingVitalParts.Any());
            // A player may deliberately give away their last firearm; null is not a valid
            // argument to EFT's item-generic SetInHands and must use the empty-hands path.
            if (item == null) return EmptyHands(player, restoring: true);
            return GearSwapHandsTransition.Run<IHandsController>(
                () => CanContinue(player),
                () => HandsIdle(player),
                complete =>
                {
                    player.SetInHands(item, result =>
                    {
                        complete(result.Value, result.Succeed, result.Error);
                    });
                },
                returned => returned?.Item?.Id == item.Id && player.HandsController?.Item?.Id == item.Id &&
                    player.HandsController?.Destroyed == false &&
                    (!(item is Weapon) || returned is IFirearmHandsController),
                timeoutMs: Math.Min(10000, _applyScope.RemainingMs), cancellation: _applyScope.Lifetime);
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
            try { if (!_closed) _screen.CloseScreen(); }
            finally { Cleanup(); }
        }
        private void Cleanup()
        {
            if (_closed) return;
            _closed = true;
            _applyScope?.Stop();
            ReleaseMovement();
            TraceInsurance(_committed ? "closeCommitted" : "closeCancelled", draft: false);
            GearSwapInsuranceDiagnostics.End(_committed);
            try
            {
                _screen.OnClose -= OnScreenClosed;
                if (Owner.Player != null && !Owner.Player.Destroyed) Owner.Player.SetInventoryOpened(false);
                foreach (var tab in _hiddenTabs)
                    if (tab.Key != null) tab.Key.SetActive(tab.Value);
            }
            finally
            {
                PlayerDraft.Session = null;
                BotDraft.Session = null;
                _hiddenTabs.Clear();
                if (ReferenceEquals(Current, this)) Current = null;
            }
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

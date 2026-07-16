using EFT;
using EFT.InventoryLogic;
using pitTeam.Utils;
using System;

namespace pitTeam.Modules
{
    internal static class FollowerLootedPrimaryWeaponBinding
    {
        private const int SwitchMaxAttempts = 8;
        private const int TransientSwitchMaxAttempts = 40;
        private const int StuckSelectorRecoveryAttempt = 6;
        private const int StuckSelectorRecoveryInterval = 6;
        private const float SwitchRetryDelaySeconds = 0.45f;
        private const float PostLootSelectionDelaySeconds = 2f;

        internal static void RebindAndSelect(BotOwner bot, Weapon weapon, string context)
        {
            if (!TryRebindWeaponInfo(bot, weapon, context, out string reason))
            {
                Logger.LogInfo($"[LootCommand] Skipped looted primary rebind ({context}): {reason}");
                return;
            }

            TryEnsureSelected(bot, weapon, context, 0);
        }

        internal static void SelectAfterLootCompletion(
            BotOwner bot,
            Weapon weapon,
            string context)
        {
            if (weapon == null)
            {
                return;
            }

            if (bot?.AITaskManager == null)
            {
                RunPostLootSelection(bot, weapon, context);
                return;
            }

            try
            {
                // The item transaction has completed, but EFT can keep the inventory/request
                // controller occupied until the next UI/interaction tick. Wait for that release,
                // then apply the same recovery reset used by the Attention command before asking
                // vanilla BotWeaponSelector to take the new primary.
                bot.AITaskManager.RegisterDelayedTask(
                    bot,
                    PostLootSelectionDelaySeconds,
                    () => RunPostLootSelection(bot, weapon, context));
                Logger.LogInfo(
                    $"[LootCommand][WeaponRegistration] follower='{bot.Profile?.Nickname ?? bot.ProfileId ?? "unknown"}' " +
                    $"weapon={weapon.TemplateId} context={context} result=postLootSelectionQueued " +
                    $"delay={PostLootSelectionDelaySeconds:0.0}");
            }
            catch (Exception ex)
            {
                Logger.LogInfo(
                    $"[LootCommand] Post-loot primary selection could not be queued for " +
                    $"'{bot?.Profile?.Nickname ?? bot?.ProfileId ?? "unknown"}' ({context}): {ex.Message}");
            }
        }

        private static void RunPostLootSelection(
            BotOwner bot,
            Weapon weapon,
            string context)
        {
            if (!CanContinueWeaponSwitch(bot, out string inactiveReason))
            {
                LogAbortedSwitch(bot, weapon, context, inactiveReason);
                return;
            }

            // Attention uses this soft reset after clearing its command/combat ownership. Loot
            // completion has already cleared the loot command, so this is the equivalent narrow
            // recovery step without erasing enemy memory or playing Attention's voice response.
            FollowerRecovery.SoftReset(bot);
            RebindAndSelect(bot, weapon, context);
        }

        internal static void RegisterSupport(BotOwner bot, Weapon weapon, string context)
        {
            try
            {
                BotWeaponManager weaponManager = bot?.WeaponManager;
                BotWeaponSelector selector = weaponManager?.Selector;
                if (weaponManager == null || selector == null)
                {
                    Logger.LogInfo(
                        $"[LootCommand][WeaponRegistration] follower='{bot?.Profile?.Nickname ?? bot?.ProfileId ?? "unknown"}' " +
                        $"support={weapon?.TemplateId ?? "unknown"} context={context} result=skipped reason=weaponManagerMissing");
                    return;
                }

                // UpdateWeaponsList is the vanilla source of SupportWeapon and
                // CanChangeToSupportWeapons. The per-slot BotWeaponInfo still needs to be created
                // because this weapon was not present when the bot initialized.
                selector.UpdateWeaponsList();
                if (!TryRegisterTrackedSupportWeapon(
                        bot,
                        weaponManager,
                        selector,
                        weapon,
                        context,
                        out string reason))
                {
                    Logger.LogInfo(
                        $"[LootCommand][WeaponRegistration] follower='{bot?.Profile?.Nickname ?? bot?.ProfileId ?? "unknown"}' " +
                        $"support={weapon?.TemplateId ?? "unknown"} context={context} result=skipped reason={reason}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogInfo(
                    $"[LootCommand][WeaponRegistration] follower='{bot?.Profile?.Nickname ?? bot?.ProfileId ?? "unknown"}' " +
                    $"support={weapon?.TemplateId ?? "unknown"} context={context} result=failed reason={ex.Message}");
            }
        }

        private static bool TryRebindWeaponInfo(
            BotOwner bot,
            Weapon weapon,
            string context,
            out string reason)
        {
            reason = string.Empty;
            if (weapon == null)
            {
                reason = "weaponMissing";
                return false;
            }

            if (bot?.WeaponManager == null)
            {
                reason = "weaponManagerMissing";
                return false;
            }

            try
            {
                BotWeaponManager weaponManager = bot.WeaponManager;
                BotWeaponSelector selector = weaponManager.Selector;
                if (selector == null)
                {
                    reason = "selectorMissing";
                    return false;
                }

                Weapon slottedPrimary = bot.GetPlayer?.InventoryController?.Inventory?.Equipment
                    ?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem as Weapon;
                if (!IsSameItem(slottedPrimary, weapon))
                {
                    reason = "weaponNotInPrimarySlot";
                    return false;
                }

                // EFT caches a bot's weapon roles at spawn. Refresh the physical slots, then
                // explicitly create the primary info record for this raid-looted weapon.
                selector.UpdateWeaponsList();
                if (!IsSameItem(selector.FirstPrimaryWeaponItem, weapon))
                {
                    reason = "selectorPrimaryCacheMismatch";
                    return false;
                }

                selector.MainWeapon = EquipmentSlot.FirstPrimaryWeapon;
                BotWeaponInfo mainInfo = new BotWeaponInfo(
                    bot,
                    weapon,
                    EquipmentSlot.FirstPrimaryWeapon,
                    weaponManager.method_5);
                weaponManager.Info[EquipmentSlot.FirstPrimaryWeapon] = mainInfo;

                TryRegisterTrackedSupportWeapon(
                    bot,
                    weaponManager,
                    selector,
                    null,
                    context,
                    out _);

                if (weaponManager.CurrentWeaponInfo == null ||
                    selector.LastEquipmentSlot == EquipmentSlot.FirstPrimaryWeapon)
                {
                    weaponManager.CurrentWeaponInfo = mainInfo;
                }

                selector.IsWeaponReady = true;
                selector.NextChangeTime = 0f;
                reason = "ok";
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private static bool TryRegisterTrackedSupportWeapon(
            BotOwner bot,
            BotWeaponManager weaponManager,
            BotWeaponSelector selector,
            Weapon expectedSupportWeapon,
            string context,
            out string reason)
        {
            reason = string.Empty;
            Weapon primaryWeapon = bot.GetPlayer?.InventoryController?.Inventory?.Equipment
                ?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem as Weapon;
            Weapon supportWeapon = bot.GetPlayer?.InventoryController?.Inventory?.Equipment
                ?.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem as Weapon;
            if (primaryWeapon == null)
            {
                reason = "primaryMissing";
                return false;
            }

            if (supportWeapon == null)
            {
                reason = "secondaryMissing";
                return false;
            }

            if (expectedSupportWeapon != null && !IsSameItem(expectedSupportWeapon, supportWeapon))
            {
                reason = "secondarySlotMismatch";
                return false;
            }

            if (supportWeapon.GetItemComponent<KnifeComponent>() != null ||
                !InteractableObjects.IsLootedWeapon(bot, supportWeapon) ||
                InteractableObjects.IsStrictCargoItem(bot, supportWeapon))
            {
                reason = "supportNotEligible";
                return false;
            }

            if (!IsSameItem(selector.SecondPrimaryWeaponItem, supportWeapon))
            {
                reason = "selectorSecondaryCacheMismatch";
                return false;
            }

            if (weaponManager.Info.TryGetValue(
                    EquipmentSlot.SecondPrimaryWeapon,
                    out BotWeaponInfo existingInfo) &&
                IsSameItem(existingInfo?.weapon, supportWeapon))
            {
                reason = "alreadyRegistered";
                return true;
            }

            // Vanilla only treats second primary as a usable support role after first primary
            // exists. The looted weapon may have occupied second primary earlier, when no main
            // weapon existed, so create the missing per-slot reload/weapon state now.
            BotWeaponInfo supportInfo = new BotWeaponInfo(
                bot,
                supportWeapon,
                EquipmentSlot.SecondPrimaryWeapon,
                weaponManager.method_5);
            weaponManager.Info[EquipmentSlot.SecondPrimaryWeapon] = supportInfo;
            Logger.LogInfo(
                $"[LootCommand][WeaponRegistration] follower='{bot.Profile?.Nickname ?? bot.ProfileId ?? "unknown"}' " +
                $"support={supportWeapon.TemplateId} context={context} result=registered " +
                $"supportSlot={selector.SupportWeapon} canChange={selector.CanChangeToSupportWeapons}");
            reason = "registered";
            return true;
        }

        private static bool TryEnsureSelected(BotOwner bot, Weapon weapon, string context, int attempt)
        {
            try
            {
                if (!CanContinueWeaponSwitch(bot, out string inactiveReason))
                {
                    LogAbortedSwitch(bot, weapon, context, inactiveReason);
                    return false;
                }

                if (!TryRebindWeaponInfo(bot, weapon, context, out string rebindReason))
                {
                    LogFinalFailure(bot, weapon, context, attempt, rebindReason);
                    return false;
                }

                BotWeaponManager weaponManager = bot.WeaponManager;
                BotWeaponSelector selector = weaponManager.Selector;
                if (IsSelected(weaponManager, selector, weapon))
                {
                    Logger.LogInfo(
                        $"[LootCommand][WeaponRegistration] follower='{bot.Profile?.Nickname ?? bot.ProfileId ?? "unknown"}' " +
                        $"weapon={weapon.TemplateId} context={context} result=selected attempt={attempt}");
                    return true;
                }

                TryRecoverStuckSelectorTransition(bot, selector, attempt);
                string blockReason = GetSwitchBlockReason(weaponManager, selector);
                if (string.IsNullOrEmpty(blockReason))
                {
                    // CanChangeHands is intentionally not a prerequisite here. It includes broad
                    // interaction and controller-state checks which may remain false until EFT is
                    // asked to start a weapon process. ChangeToMain owns that scheduled vanilla
                    // hand-off and its OnWeaponTaken retry path.
                    blockReason = selector.ChangeToMain()
                        ? "switchRequested"
                        : "selectorRejectedChangeToMain";
                }

                // Inventory-driven weapon appearance and the selector callback are asynchronous.
                // Keep the short limit only for a non-transient ChangeToMain rejection.
                int maxAttempts = IsTransientSwitchBlockReason(blockReason)
                    ? TransientSwitchMaxAttempts
                    : SwitchMaxAttempts;
                if (attempt >= maxAttempts)
                {
                    LogFinalFailure(bot, weapon, context, attempt, blockReason);
                    return false;
                }

                QueueRetry(bot, weapon, context, attempt + 1);
                return false;
            }
            catch (Exception ex)
            {
                if (attempt >= SwitchMaxAttempts)
                {
                    LogFinalFailure(bot, weapon, context, attempt, ex.Message);
                }
                else
                {
                    QueueRetry(bot, weapon, context, attempt + 1);
                }

                return false;
            }
        }

        private static bool IsSelected(
            BotWeaponManager weaponManager,
            BotWeaponSelector selector,
            Weapon weapon)
        {
            Weapon activeWeapon = weaponManager?.ShootController?.Item ?? weaponManager?.CurrentWeapon;
            return selector?.LastEquipmentSlot == EquipmentSlot.FirstPrimaryWeapon &&
                   IsSameItem(activeWeapon, weapon) &&
                   IsSameItem(weaponManager?.MainWeaponInfo?.weapon, weapon);
        }

        private static string GetSwitchBlockReason(
            BotWeaponManager weaponManager,
            BotWeaponSelector selector)
        {
            if (weaponManager == null)
            {
                return "weaponManagerMissing";
            }

            if (selector == null)
            {
                return "selectorMissing";
            }

            if (selector.IsChanging)
            {
                return "selectorChanging";
            }

            if (!selector.IsWeaponReady)
            {
                return "weaponNotReady";
            }

            if (weaponManager.Reload?.Reloading == true)
            {
                return "reloading";
            }

            return string.Empty;
        }

        private static bool CanContinueWeaponSwitch(BotOwner bot, out string reason)
        {
            reason = string.Empty;
            if (bot == null)
            {
                reason = "botMissing";
                return false;
            }

            if (bot.IsDead)
            {
                reason = "botDead";
                return false;
            }

            if (bot.BotState != EBotState.Active)
            {
                reason = $"botState:{bot.BotState}";
                return false;
            }

            Player player = bot.GetPlayer;
            if (player == null)
            {
                reason = "playerMissing";
                return false;
            }

            if (player.HealthController?.IsAlive != true)
            {
                reason = "playerDead";
                return false;
            }

            if (player.HandsController == null)
            {
                reason = "handsControllerMissing";
                return false;
            }

            return true;
        }

        private static void TryRecoverStuckSelectorTransition(
            BotOwner bot,
            BotWeaponSelector selector,
            int attempt)
        {
            if (selector?.IsChanging != true ||
                attempt < StuckSelectorRecoveryAttempt ||
                (attempt - StuckSelectorRecoveryAttempt) % StuckSelectorRecoveryInterval != 0)
            {
                return;
            }

            try
            {
                // LootingBots uses the same recovery after inventory-driven weapon changes. Give
                // the normal draw animation several ticks first, then finish only a stuck current
                // hands state so EFT's pending selector callback can complete normally.
                bot.GetPlayer.HandsController.FastForwardCurrentState();
                Logger.LogInfo(
                    $"[LootCommand][WeaponRegistration] follower='{bot.Profile?.Nickname ?? bot.ProfileId ?? "unknown"}' " +
                    $"result=selectorRecovery attempt={attempt}");
            }
            catch (Exception ex)
            {
                Logger.LogInfo(
                    $"[LootCommand][WeaponRegistration] follower='{bot?.Profile?.Nickname ?? bot?.ProfileId ?? "unknown"}' " +
                    $"result=selectorRecoveryFailed attempt={attempt} reason={ex.Message}");
            }
        }

        private static void QueueRetry(BotOwner bot, Weapon weapon, string context, int attempt)
        {
            try
            {
                if (bot?.AITaskManager == null)
                {
                    Logger.LogInfo(
                        $"[LootCommand] Looted primary switch retry unavailable for " +
                        $"'{bot?.Profile?.Nickname ?? bot?.ProfileId ?? "unknown"}' ({context}): taskManagerMissing");
                    return;
                }

                bot.AITaskManager.RegisterDelayedTask(
                    bot,
                    SwitchRetryDelaySeconds,
                    () => TryEnsureSelected(bot, weapon, context, attempt));
            }
            catch (Exception ex)
            {
                Logger.LogInfo(
                    $"[LootCommand] Looted primary switch retry failed for " +
                    $"'{bot?.Profile?.Nickname ?? bot?.ProfileId ?? "unknown"}' ({context}): {ex.Message}");
            }
        }

        private static void LogFinalFailure(
            BotOwner bot,
            Weapon weapon,
            string context,
            int attempt,
            string reason)
        {
            int maxAttempts = IsTransientSwitchBlockReason(reason)
                ? TransientSwitchMaxAttempts
                : SwitchMaxAttempts;
            if (attempt < maxAttempts)
            {
                return;
            }

            Logger.LogInfo(
                $"[LootCommand] Looted primary switch did not complete for " +
                $"'{bot?.Profile?.Nickname ?? bot?.ProfileId ?? "unknown"}' ({context}): " +
                $"{weapon?.TemplateId ?? "unknown"} reason={reason}");
        }

        private static void LogAbortedSwitch(
            BotOwner bot,
            Weapon weapon,
            string context,
            string reason)
        {
            Logger.LogInfo(
                $"[LootCommand] Looted primary switch stopped for " +
                $"'{bot?.Profile?.Nickname ?? bot?.ProfileId ?? "unknown"}' ({context}): " +
                $"{weapon?.TemplateId ?? "unknown"} reason={reason}");
        }

        private static bool IsTransientSwitchBlockReason(string reason)
        {
            return string.Equals(reason, "selectorChanging", StringComparison.Ordinal) ||
                   string.Equals(reason, "weaponNotReady", StringComparison.Ordinal) ||
                   string.Equals(reason, "reloading", StringComparison.Ordinal) ||
                   string.Equals(reason, "switchRequested", StringComparison.Ordinal);
        }

        private static bool IsSameItem(Item first, Item second)
        {
            if (ReferenceEquals(first, second))
            {
                return true;
            }

            return !string.IsNullOrEmpty(first?.Id) &&
                   !string.IsNullOrEmpty(second?.Id) &&
                   string.Equals(first.Id, second.Id, StringComparison.Ordinal);
        }
    }
}

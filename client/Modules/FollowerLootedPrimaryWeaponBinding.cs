using EFT;
using EFT.InventoryLogic;
using System;

namespace pitTeam.Modules
{
    internal static class FollowerLootedPrimaryWeaponBinding
    {
        private const int SwitchMaxAttempts = 8;
        private const float SwitchRetryDelaySeconds = 0.45f;

        internal static void RebindAndSelect(BotOwner bot, Weapon weapon, string context)
        {
            if (!TryRebindWeaponInfo(bot, weapon, context, out string reason))
            {
                Logger.LogInfo($"[LootCommand] Skipped looted primary rebind ({context}): {reason}");
                return;
            }

            TryEnsureSelected(bot, weapon, context, 0);
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

                string blockReason = GetSwitchBlockReason(weaponManager, selector);
                if (string.IsNullOrEmpty(blockReason))
                {
                    blockReason = selector.ChangeToMain()
                        ? "switchRequested"
                        : "selectorRejectedChangeToMain";
                }

                if (attempt >= SwitchMaxAttempts)
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

            if (!weaponManager.CanChangeHands())
            {
                return "handsBusy";
            }

            return string.Empty;
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
            if (attempt < SwitchMaxAttempts)
            {
                return;
            }

            Logger.LogInfo(
                $"[LootCommand] Looted primary switch did not complete for " +
                $"'{bot?.Profile?.Nickname ?? bot?.ProfileId ?? "unknown"}' ({context}): " +
                $"{weapon?.TemplateId ?? "unknown"} reason={reason}");
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

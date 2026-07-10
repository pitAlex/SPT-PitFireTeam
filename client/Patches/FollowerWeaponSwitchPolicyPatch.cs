using EFT;
using EFT.InventoryLogic;
using pitTeam.BigBrain;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace pitTeam.Patches
{
    internal static class FollowerWeaponSwitchPolicyRuntime
    {
        private const float EnemyNullReloadCooldownSeconds = 3.0f;
        private const float MidCombatRecentSeenSeconds = 2.5f;
        private static readonly Dictionary<string, float> EnemyLostAtByFollower = new Dictionary<string, float>();
        private static readonly ConditionalWeakTable<BotOwner, DeadFollowerWeaponCallbackMarker> DeadFollowerWeaponCallbacks =
            new ConditionalWeakTable<BotOwner, DeadFollowerWeaponCallbackMarker>();

        private sealed class DeadFollowerWeaponCallbackMarker
        {
        }

        public static void MarkDeadFollowerWeaponCallbacks(BotOwner botOwner)
        {
            if (botOwner != null)
            {
                DeadFollowerWeaponCallbacks.GetValue(botOwner, _ => new DeadFollowerWeaponCallbackMarker());
            }
        }

        private static bool IsKnownFollowerForWeaponCallbacks(BotOwner botOwner)
        {
            return botOwner != null &&
                   (BossPlayers.IsFollower(botOwner) || DeadFollowerWeaponCallbacks.TryGetValue(botOwner, out _));
        }

        public static void UpdateEnemyState(BotOwner botOwner)
        {
            if (botOwner == null)
            {
                return;
            }

            string key = GetFollowerKey(botOwner);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            EnemyInfo goalEnemy = botOwner.Memory?.GoalEnemy;
            if (goalEnemy != null)
            {
                EnemyLostAtByFollower.Remove(key);
                return;
            }

            if (!EnemyLostAtByFollower.ContainsKey(key))
            {
                EnemyLostAtByFollower[key] = Time.time;
            }
        }

        public static bool IsInEnemyNullCooldown(BotOwner botOwner)
        {
            if (botOwner == null)
            {
                return false;
            }

            if (botOwner.Memory?.GoalEnemy != null)
            {
                return false;
            }

            string key = GetFollowerKey(botOwner);
            if (string.IsNullOrEmpty(key) || !EnemyLostAtByFollower.TryGetValue(key, out float lostAt))
            {
                return false;
            }

            return Time.time - lostAt <= EnemyNullReloadCooldownSeconds;
        }

        public static bool ShouldAllowSupportNoAmmoMainSwitch(BotOwner botOwner, BotReload reload)
        {
            if (botOwner == null || reload == null)
            {
                return false;
            }

            BotWeaponManager weaponManager = botOwner.WeaponManager;
            BotWeaponSelector selector = weaponManager?.Selector;
            if (weaponManager == null || selector == null)
            {
                return false;
            }

            // This policy is only for support/secondary -> main fallback routing.
            if (selector.LastEquipmentSlot != EquipmentSlot.SecondPrimaryWeapon)
            {
                return false;
            }

            int supportBulletCount = reload.BulletCount;
            if (supportBulletCount > 0)
            {
                return false;
            }

            int mainBulletCount = weaponManager.MainWeaponInfo?.Reload?.BulletCount ?? 0;
            if (mainBulletCount <= 0)
            {
                return false;
            }

            EnemyInfo goalEnemy = botOwner.Memory?.GoalEnemy;
            if (goalEnemy == null)
            {
                return false;
            }

            bool midCombat = goalEnemy.IsVisible || (Time.time - goalEnemy.TimeLastSeen <= MidCombatRecentSeenSeconds);
            return midCombat;
        }

        private static string GetFollowerKey(BotOwner botOwner)
        {
            return botOwner?.ProfileId ?? botOwner?.Profile?.Id ?? string.Empty;
        }

        public static bool ShouldSuppressFollowerWeaponSelectorManualUpdate(BotWeaponSelector selector)
        {
            if (selector == null)
            {
                return false;
            }

            BotOwner botOwner = GetSelectorBotOwner(selector);
            if (botOwner == null || !BossPlayers.IsFollower(botOwner))
            {
                return false;
            }

            if (!selector.CanChangeToSupportWeapons || !selector.IsWeaponReady)
            {
                return false;
            }

            if (selector.LastEquipmentSlot != selector.SupportWeapon)
            {
                return false;
            }

            EnemyInfo goalEnemy = botOwner.Memory?.GoalEnemy;
            if (FollowerCombatLayer.IsFollowerCombatLayerActive(botOwner))
            {
                return goalEnemy == null || Time.time - goalEnemy.TimeLastSeen > 30f;
            }

            return ShouldSuppressPatrolSupportAutoReturn(botOwner, selector, goalEnemy);
        }

        public static void PreserveManualUpdateStuckFlag(BotWeaponSelector selector)
        {
            if (selector == null)
            {
                return;
            }

            if (!selector.ErrorStuckLog &&
                selector.StartChangeTime > 0f &&
                Time.time - selector.StartChangeTime > 20f)
            {
                selector.ErrorStuckLog = true;
            }
        }

        public static BotOwner GetSelectorBotOwner(BotWeaponSelector selector)
        {
            return selector != null
                ? Traverse.Create(selector).Field("BotOwner_0").GetValue<BotOwner>()
                : null;
        }

        public static bool ShouldSuppressDeadFollowerWeaponTaken(BotWeaponSelector selector)
        {
            BotOwner botOwner = GetSelectorBotOwner(selector);
            if (!IsKnownFollowerForWeaponCallbacks(botOwner))
            {
                return false;
            }

            if (!DeadFollowerWeaponCallbacks.TryGetValue(botOwner, out _) &&
                botOwner.HealthController?.IsAlive == true)
            {
                return false;
            }

            return true;
        }

        public static bool TryRecoverFollowerWeaponTakenException(BotWeaponSelector selector, Exception exception)
        {
            if (selector == null || exception == null)
            {
                return false;
            }

            BotOwner botOwner = GetSelectorBotOwner(selector);
            if (!IsKnownFollowerForWeaponCallbacks(botOwner))
            {
                return false;
            }

            if (DeadFollowerWeaponCallbacks.TryGetValue(botOwner, out _) ||
                botOwner.HealthController?.IsAlive != true)
            {
                selector.IsChanging = false;
                selector.IsWeaponReady = true;
                Modules.Logger.LogInfo(
                    $"[WeaponSwitch] Suppressed dead follower OnWeaponTaken exception for '{botOwner.Profile?.Nickname ?? botOwner.ProfileId ?? "unknown"}': " +
                    exception.Message);
                return true;
            }

            try
            {
                selector.IsChanging = false;
                selector.IsWeaponReady = true;
                selector.UpdateWeaponsList();

                BotWeaponManager weaponManager = botOwner.WeaponManager;
                Weapon handsWeapon = botOwner.GetPlayer?.HandsController?.Item as Weapon;
                if (weaponManager != null && handsWeapon != null)
                {
                    EquipmentSlot? slot = ResolveHandsWeaponSlot(botOwner, handsWeapon);
                    if (slot.HasValue && !weaponManager.Info.ContainsKey(slot.Value))
                    {
                        weaponManager.Info[slot.Value] = new BotWeaponInfo(
                            botOwner,
                            handsWeapon,
                            slot.Value,
                            weaponManager.method_5);
                    }
                }
            }
            catch (Exception recoverException)
            {
                Modules.Logger.LogInfo($"[WeaponSwitch] Failed to recover follower OnWeaponTaken exception: {recoverException.Message}");
            }

            Modules.Logger.LogInfo(
                $"[WeaponSwitch] Suppressed follower OnWeaponTaken exception for '{botOwner.Profile?.Nickname ?? botOwner.ProfileId ?? "unknown"}': " +
                exception.Message);
            return true;
        }

        private static EquipmentSlot? ResolveHandsWeaponSlot(BotOwner botOwner, Weapon weapon)
        {
            if (botOwner?.GetPlayer?.InventoryController?.Inventory?.Equipment == null || weapon == null)
            {
                return null;
            }

            InventoryEquipment equipment = botOwner.GetPlayer.InventoryController.Inventory.Equipment;
            foreach (EquipmentSlot slot in new[]
                     {
                         EquipmentSlot.FirstPrimaryWeapon,
                         EquipmentSlot.SecondPrimaryWeapon,
                         EquipmentSlot.Holster,
                         EquipmentSlot.Scabbard
                     })
            {
                if (string.Equals(equipment.GetSlot(slot)?.ContainedItem?.Id, weapon.Id, StringComparison.Ordinal))
                {
                    return slot;
                }
            }

            return null;
        }

        private static bool ShouldSuppressPatrolSupportAutoReturn(
            BotOwner botOwner,
            BotWeaponSelector selector,
            EnemyInfo goalEnemy)
        {
            if (goalEnemy != null)
            {
                return false;
            }

            if (!string.Equals(botOwner.Brain?.Agent?.UsingLayer, "pitTeam.FollowerPatrol", StringComparison.Ordinal))
            {
                return false;
            }

            BotReload reload = botOwner.WeaponManager?.Reload;
            Weapon currentWeapon = botOwner.WeaponManager?.CurrentWeapon;
            if (reload == null || currentWeapon == null)
            {
                return false;
            }

            MagazineItemClass currentMagazine = currentWeapon.GetCurrentMagazine();
            if (currentMagazine == null || currentMagazine.MaxCount <= 0)
            {
                return false;
            }

            int currentCount = currentWeapon.GetCurrentMagazineCount();
            if (currentCount >= currentMagazine.MaxCount)
            {
                return false;
            }

            return FollowerOutOfCombatReloadPolicy.CanTopOffWeapon(botOwner, currentWeapon);
        }
    }

    internal sealed class FollowerWeaponTakenAfterDeathPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotWeaponSelector), nameof(BotWeaponSelector.OnWeaponTaken));
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotWeaponSelector __instance)
        {
            if (!FollowerWeaponSwitchPolicyRuntime.ShouldSuppressDeadFollowerWeaponTaken(__instance))
            {
                return true;
            }

            __instance.IsChanging = false;
            __instance.IsWeaponReady = true;
            return false;
        }

        [PatchFinalizer]
        private static Exception PatchFinalizer(BotWeaponSelector __instance, Exception __exception)
        {
            return FollowerWeaponSwitchPolicyRuntime.TryRecoverFollowerWeaponTakenException(__instance, __exception)
                ? null
                : __exception;
        }
    }

    internal sealed class FollowerWeaponSelectorManualUpdatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotWeaponSelector), "ManualUpdate", Type.EmptyTypes);
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotWeaponSelector __instance)
        {
            if (!FollowerWeaponSwitchPolicyRuntime.ShouldSuppressFollowerWeaponSelectorManualUpdate(__instance))
            {
                return true;
            }

            FollowerWeaponSwitchPolicyRuntime.PreserveManualUpdateStuckFlag(__instance);
            return false;
        }
    }

    internal sealed class FollowerSupportNoAmmoMainSwitchPolicyPatch : ModulePatch
    {
        private struct SwitchPolicyState
        {
            public bool OverrodeSetting;
            public bool OriginalValue;
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GClass461), "CanReload", new[]
            {
                typeof(bool),
                typeof(MagazineItemClass).MakeByRefType(),
                typeof(List<AmmoItemClass>).MakeByRefType(),
            });
        }

        [PatchPrefix]
        private static void PatchPrefix(GClass461 __instance, out SwitchPolicyState __state)
        {
            __state = default;

            BotOwner botOwner = Traverse.Create(__instance).Field("BotOwner_0").GetValue<BotOwner>();
            if (botOwner == null || !BossPlayers.IsFollower(botOwner))
            {
                return;
            }

            FollowerWeaponSwitchPolicyRuntime.UpdateEnemyState(botOwner);

            bool original = botOwner.Settings?.FileSettings?.Shoot?.CHANGE_TO_MAIN_WHEN_SUPPORT_NO_AMMO ?? false;
            if (!original)
            {
                return;
            }

            bool allowSwitch = FollowerWeaponSwitchPolicyRuntime.ShouldAllowSupportNoAmmoMainSwitch(botOwner, __instance);
            if (allowSwitch)
            {
                return;
            }

            __state.OverrodeSetting = true;
            __state.OriginalValue = original;
            botOwner.Settings.FileSettings.Shoot.CHANGE_TO_MAIN_WHEN_SUPPORT_NO_AMMO = false;
        }

        [PatchPostfix]
        private static void PatchPostfix(GClass461 __instance, SwitchPolicyState __state)
        {
            if (!__state.OverrodeSetting)
            {
                return;
            }

            BotOwner botOwner = Traverse.Create(__instance).Field("BotOwner_0").GetValue<BotOwner>();
            if (botOwner == null)
            {
                return;
            }

            botOwner.Settings.FileSettings.Shoot.CHANGE_TO_MAIN_WHEN_SUPPORT_NO_AMMO = __state.OriginalValue;
        }
    }

    internal sealed class FollowerHoldLingerReloadSuppressPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReload), "TryReload", Type.EmptyTypes);
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReload __instance, ref bool __result)
        {
            BotOwner botOwner = Traverse.Create(__instance).Field("BotOwner_0").GetValue<BotOwner>();
            if (botOwner == null || !BossPlayers.IsFollower(botOwner))
            {
                return true;
            }

            FollowerWeaponSwitchPolicyRuntime.UpdateEnemyState(botOwner);

            if (!FollowerWeaponSwitchPolicyRuntime.IsInEnemyNullCooldown(botOwner))
            {
                return true;
            }

            BotLogicDecision? lastDecision = botOwner.Brain?.LastDecision;
            if (lastDecision == null || lastDecision.Value != BotLogicDecision.holdPosition)
            {
                return true;
            }

            if (pitFireTeam.IsDebugBuild)
                Logger.LogInfo($"[WeaponPolicy] suppress reload during hold-linger cooldown follower={botOwner.Profile?.Nickname ?? botOwner.name}");

            __result = false;
            return false;
        }
    }
}

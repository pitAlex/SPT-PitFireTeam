using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace pitTeam.Modules
{
    internal static class FollowerTransitStateCache
    {
        private static readonly Dictionary<string, TransitFollowerState> StatesByKey =
            new Dictionary<string, TransitFollowerState>(StringComparer.Ordinal);

        private static readonly HashSet<string> TransitSpawnProfileIds =
            new HashSet<string>(StringComparer.Ordinal);

        private static readonly Dictionary<string, List<string>> ProtectedEquipmentIdsByProfileId =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        private static readonly Dictionary<string, List<string>> TrackedReturnItemIdsByProfileId =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        private static readonly HashSet<string> TransitCooledWeaponIds =
            new HashSet<string>(StringComparer.Ordinal);

        public static bool TryCapture(
            BotOwner bot,
            IEnumerable<string> protectedEquipmentIds,
            IEnumerable<string> trackedReturnItemIds,
            out Profile profile)
        {
            profile = null;
            if (bot?.Profile == null)
            {
                return false;
            }

            try
            {
                profile = CreateProfileSnapshot(bot);
                if (profile == null)
                {
                    return false;
                }

                List<string> protectedIds = protectedEquipmentIds?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList() ?? new List<string>();

                List<string> trackedReturnIds = trackedReturnItemIds?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList() ?? new List<string>();

                TransitFollowerState state = new TransitFollowerState(profile, protectedIds, trackedReturnIds);
                StoreState(profile.AccountId, state);
                StoreState(bot.AccountId, state);
                StoreState(profile.ProfileId, state);
                StoreState(bot.ProfileId, state);

                WildSpawnType? role = profile.Info?.Settings?.Role;
                if (role.HasValue)
                {
                    StoreState(GetRoleKey(role.Value), state);
                }

                Modules.Logger.LogInfo(
                    $"[Transit] Captured carried follower state for '{profile.Nickname ?? profile.ProfileId}' " +
                    $"protectedEquipmentIds={protectedIds.Count} trackedReturnItemIds={trackedReturnIds.Count}.");
                return true;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[Transit] Failed to capture follower state for '{bot.Profile?.Nickname ?? bot.ProfileId}'.");
                Modules.Logger.LogError(ex);
                profile = null;
                return false;
            }
        }

        public static bool TryConsumeProfile(string memberId, WildSpawnType role, out Profile profile)
        {
            profile = null;

            if (TryTakeState(memberId, out TransitFollowerState state) ||
                TryTakeState(GetRoleKey(role), out state))
            {
                profile = state.Profile?.Clone();
                if (profile == null)
                {
                    return false;
                }

                TrackTransitSpawnProfile(profile, state);
                Modules.Logger.LogInfo(
                    $"[Transit] Reusing carried follower profile for '{profile.Nickname ?? profile.ProfileId}' instead of fetching server storage.");
                return true;
            }

            return false;
        }

        public static bool IsTransitSpawnProfile(string profileId)
        {
            return !string.IsNullOrWhiteSpace(profileId) &&
                   TransitSpawnProfileIds.Contains(profileId);
        }

        public static bool TryConsumeProtectedEquipmentIds(Profile profile, out List<string> protectedEquipmentIds)
        {
            protectedEquipmentIds = null;
            if (profile == null)
            {
                return false;
            }

            if (TryRemoveProtectedEquipmentIds(profile.ProfileId, out protectedEquipmentIds) ||
                TryRemoveProtectedEquipmentIds(profile.AccountId, out protectedEquipmentIds))
            {
                return true;
            }

            return false;
        }

        public static bool TryConsumeTrackedReturnItemIds(Profile profile, out List<string> trackedReturnItemIds)
        {
            trackedReturnItemIds = null;
            if (profile == null)
            {
                return false;
            }

            if (TryRemoveTrackedReturnItemIds(profile.ProfileId, out trackedReturnItemIds) ||
                TryRemoveTrackedReturnItemIds(profile.AccountId, out trackedReturnItemIds))
            {
                return true;
            }

            return false;
        }

        public static void Clear()
        {
            StatesByKey.Clear();
            TransitSpawnProfileIds.Clear();
            ProtectedEquipmentIdsByProfileId.Clear();
            TrackedReturnItemIdsByProfileId.Clear();
            TransitCooledWeaponIds.Clear();
        }

        private static Profile CreateProfileSnapshot(BotOwner bot)
        {
            CompleteProfileDescriptorClass descriptor = new CompleteProfileDescriptorClass(bot.Profile, GClass2240.Instance);

            Inventory liveInventory = bot.GetPlayer?.InventoryController?.Inventory;
            if (liveInventory != null)
            {
                descriptor.Inventory = new EFTInventoryClass(liveInventory, GClass2240.Instance);
            }

            if (bot.GetPlayer?.ActiveHealthController != null)
            {
                try
                {
                    descriptor.Health = bot.GetPlayer.ActiveHealthController.Store(
                        Singleton<BackendConfigSettingsClass>.Instance.transitSettings,
                        null);
                }
                catch
                {
                    descriptor.Health = bot.GetPlayer.ActiveHealthController.Store(null);
                }
            }

            if (bot.GetPlayer?.Skills != null)
            {
                descriptor.Skills = new SkillsDescriptorClass(bot.GetPlayer.Skills);
            }

            Profile profile = new Profile(descriptor);
            try
            {
                int cooledWeapons = NormalizeTransitWeaponOverheat(profile);
                if (cooledWeapons > 0)
                {
                    Modules.Logger.LogInfo(
                        $"[Transit] Normalized stale overheat visual state on {cooledWeapons} carried follower weapon(s) for '{profile.Nickname ?? profile.ProfileId}'.");
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[Transit] Failed to normalize stale carried weapon overheat for '{profile.Nickname ?? profile.ProfileId}'.");
                Modules.Logger.LogError(ex);
            }

            return profile;
        }

        public static void ResetTransitWeaponHeatVisuals(WeaponPrefab weaponPrefab, Weapon weapon)
        {
            if (weaponPrefab == null ||
                weapon == null ||
                string.IsNullOrWhiteSpace(weapon.Id) ||
                !TransitCooledWeaponIds.Contains(weapon.Id))
            {
                return;
            }

            try
            {
                float ambientWeaponTemperature = HotObject.ConvertHeat2Celsio(0f);
                int resetCount = 0;
                HashSet<HotObject> resetObjects = new HashSet<HotObject>();

                foreach (HotObject hotObject in weaponPrefab.HotObjects ?? Enumerable.Empty<HotObject>())
                {
                    if (ResetHotObject(hotObject, ambientWeaponTemperature, resetObjects))
                    {
                        resetCount++;
                    }
                }

                foreach (HotObject hotObject in weaponPrefab.GetComponentsInChildren<HotObject>(true))
                {
                    if (ResetHotObject(hotObject, ambientWeaponTemperature, resetObjects))
                    {
                        resetCount++;
                    }
                }

                if (resetCount > 0)
                {
                    Modules.Logger.LogInfo(
                        $"[Transit] Reset stale heat renderer state on carried follower weapon '{weapon.Id}' hotObjects={resetCount}.");
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[Transit] Failed to reset stale heat renderer state on carried follower weapon '{weapon.Id}'.");
                Modules.Logger.LogError(ex);
            }
        }

        private static bool ResetHotObject(HotObject hotObject, float temperatureCelsio, HashSet<HotObject> resetObjects)
        {
            if (hotObject == null || resetObjects == null || !resetObjects.Add(hotObject))
            {
                return false;
            }

            hotObject.SetTemperatureToRenderer(temperatureCelsio, true);
            return true;
        }

        private static int NormalizeTransitWeaponOverheat(Profile profile)
        {
            InventoryEquipment equipment = profile?.Inventory?.Equipment;
            if (equipment == null)
            {
                return 0;
            }

            int cooledWeapons = 0;
            foreach (Item item in equipment.GetAllItems())
            {
                if (item is Weapon weapon && NormalizeWeaponOverheat(weapon))
                {
                    cooledWeapons++;
                }
            }

            return cooledWeapons;
        }

        private static bool NormalizeWeaponOverheat(Weapon weapon)
        {
            Weapon.WeaponMalfunctionStateClass malfState = weapon?.MalfState;
            if (malfState == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(weapon.Id))
            {
                TransitCooledWeaponIds.Add(weapon.Id);
            }

            // Keep real malfunction state and weapon durability, but do not carry the stale
            // previous-raid heat values that repaint suppressors red on follower world models.
            malfState.LastShotOverheat = 0f;
            malfState.LastShotTime = 0f;
            malfState.SlideOnOverheatReached = false;
            malfState.OverheatFirerateMult = 0f;
            malfState.OverheatFirerateMultInited = false;
            malfState.AutoshotChanceInited = false;
            malfState.AutoshotTime = -1f;
            malfState.OverheatBarrelMoveMult = 0f;
            malfState.OverheatBarrelMoveDir = Vector2.zero;
            return true;
        }

        private static void StoreState(string key, TransitFollowerState state)
        {
            if (string.IsNullOrWhiteSpace(key) || state == null)
            {
                return;
            }

            StatesByKey[key] = state;
        }

        private static bool TryTakeState(string key, out TransitFollowerState state)
        {
            state = null;
            if (string.IsNullOrWhiteSpace(key) || !StatesByKey.TryGetValue(key, out state))
            {
                return false;
            }

            TransitFollowerState capturedState = state;
            foreach (string stateKey in StatesByKey
                         .Where(pair => ReferenceEquals(pair.Value, capturedState))
                         .Select(pair => pair.Key)
                         .ToList())
            {
                StatesByKey.Remove(stateKey);
            }

            return true;
        }

        private static void TrackTransitSpawnProfile(Profile profile, TransitFollowerState state)
        {
            if (!string.IsNullOrWhiteSpace(profile.ProfileId))
            {
                TransitSpawnProfileIds.Add(profile.ProfileId);
                ProtectedEquipmentIdsByProfileId[profile.ProfileId] = state.ProtectedEquipmentIds.ToList();
                TrackedReturnItemIdsByProfileId[profile.ProfileId] = state.TrackedReturnItemIds.ToList();
            }

            if (!string.IsNullOrWhiteSpace(profile.AccountId))
            {
                ProtectedEquipmentIdsByProfileId[profile.AccountId] = state.ProtectedEquipmentIds.ToList();
                TrackedReturnItemIdsByProfileId[profile.AccountId] = state.TrackedReturnItemIds.ToList();
            }
        }

        private static bool TryRemoveProtectedEquipmentIds(string key, out List<string> protectedEquipmentIds)
        {
            protectedEquipmentIds = null;
            if (string.IsNullOrWhiteSpace(key) ||
                !ProtectedEquipmentIdsByProfileId.TryGetValue(key, out protectedEquipmentIds))
            {
                return false;
            }

            ProtectedEquipmentIdsByProfileId.Remove(key);
            protectedEquipmentIds = protectedEquipmentIds.ToList();
            return true;
        }

        private static bool TryRemoveTrackedReturnItemIds(string key, out List<string> trackedReturnItemIds)
        {
            trackedReturnItemIds = null;
            if (string.IsNullOrWhiteSpace(key) ||
                !TrackedReturnItemIdsByProfileId.TryGetValue(key, out trackedReturnItemIds))
            {
                return false;
            }

            TrackedReturnItemIdsByProfileId.Remove(key);
            trackedReturnItemIds = trackedReturnItemIds.ToList();
            return true;
        }

        private static string GetRoleKey(WildSpawnType role)
        {
            return $"role:{role}";
        }

        private sealed class TransitFollowerState
        {
            public TransitFollowerState(
                Profile profile,
                List<string> protectedEquipmentIds,
                List<string> trackedReturnItemIds)
            {
                Profile = profile;
                ProtectedEquipmentIds = protectedEquipmentIds ?? new List<string>();
                TrackedReturnItemIds = trackedReturnItemIds ?? new List<string>();
            }

            public Profile Profile { get; }
            public List<string> ProtectedEquipmentIds { get; }
            public List<string> TrackedReturnItemIds { get; }
        }
    }
}

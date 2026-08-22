using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using pitTeam.Modules;
using SAIN;
using SAIN.Components;
using SAIN.Preset.Shared.BotSettings.SAINSettings;
using SAIN.SAINComponent.Classes.WeaponFunction;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace pitTeam.SAINAddon
{
    internal static class SAINFollowerRecoilPatch
    {
        private const float FollowerRecoilTuningMultiplier = 7f;
        private const float FollowerSniperTuningMultiplier = 10f;
        private static readonly BotDifficulty BossKnightBaselineDifficulty = BotDifficulty.hard;

        private static FieldInfo? _currentRecoilHorizAngleField;
        private static FieldInfo? _currentRecoilVertAngleField;
        private static readonly Dictionary<BotDifficulty, float> BossKnightRecoilMultiplierCache = new();

        // Cache active followers for per-shot performance (O(1) lookup vs dictionary + potential linear search).
        private static readonly HashSet<BotOwner> _followerCache = new();

        public static void Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(typeof(Recoil), "calculateRecoil", new[] { typeof(Weapon) });
            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find SAIN Recoil.calculateRecoil for follower recoil patch.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(SAINFollowerRecoilPatch), nameof(Postfix_CalculateRecoil)));
            Modules.Logger.LogInfo("[Init] SAIN follower recoil patch applied.");
        }

        /// <summary>
        /// Lifecycle event handler for follower recruitment, dismissal, and raid end.
        /// </summary>
        internal static void OnFollowerLifecycleEvent(BotOwner bot, FollowerLifecycleEvent eventType)
        {
            switch (eventType)
            {
                case FollowerLifecycleEvent.OnRecruited:
                    if (bot != null)
                    {
                        _followerCache.Add(bot);
                    }
                    break;

                case FollowerLifecycleEvent.OnDismiss:
                    if (bot != null)
                    {
                        _followerCache.Remove(bot);
                    }
                    break;

                case FollowerLifecycleEvent.OnRaidEnd:
                    _followerCache.Clear();
                    break;
            }
        }

        private static void Postfix_CalculateRecoil(Recoil __instance, Weapon __0)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }

                BotOwner? owner = __instance.BotOwner;
                // Fast path: check cache first (O(1)) instead of BossPlayers.IsFollower (potential O(n)).
                if (owner == null || !_followerCache.Contains(owner))
                {
                    return;
                }

                float bossKnightMultiplier = GetBossKnightRecoilMultiplier(BossKnightBaselineDifficulty);



                _currentRecoilHorizAngleField ??= AccessTools.Field(typeof(Recoil), "_currentRecoilHorizAngle");
                _currentRecoilVertAngleField ??= AccessTools.Field(typeof(Recoil), "_currentRecoilVertAngle");

                if (_currentRecoilHorizAngleField == null || _currentRecoilVertAngleField == null)
                {
                    return;
                }

                bool isSingleFire = __0?.SelectedFireMode == Weapon.EFireMode.single || __0?.SelectedFireMode == Weapon.EFireMode.semiauto;
                float tuningMultiplier = isSingleFire ? FollowerSniperTuningMultiplier : FollowerRecoilTuningMultiplier;
                float multiplierRatio = bossKnightMultiplier / tuningMultiplier;
                float currentHorizontal = (float)_currentRecoilHorizAngleField.GetValue(__instance);
                float currentVertical = (float)_currentRecoilVertAngleField.GetValue(__instance);

                _currentRecoilHorizAngleField.SetValue(__instance, currentHorizontal * multiplierRatio);
                _currentRecoilVertAngleField.SetValue(__instance, currentVertical * multiplierRatio);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[SAIN] Failed in follower recoil patch.");
                Modules.Logger.LogError(ex);
            }
        }

        private static float GetBossKnightRecoilMultiplier(BotDifficulty difficulty)
        {
            if (BossKnightRecoilMultiplierCache.TryGetValue(difficulty, out float cachedMultiplier))
            {
                return cachedMultiplier;
            }

            SAINSettingsClass? settings = SAINPlugin.LoadedPreset?.BotSettings?.GetSAINSettings(WildSpawnType.bossKnight, difficulty);
            float resolvedMultiplier = settings?.Shoot?.RecoilMultiplier ?? 1f;
            BossKnightRecoilMultiplierCache[difficulty] = resolvedMultiplier;
            return resolvedMultiplier;
        }
    }
}

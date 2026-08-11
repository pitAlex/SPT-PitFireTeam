using System;
using UnityEngine;
using EFT;

namespace pitTeam.Modules
{
    /// <summary>
    /// Centralized combat distance configuration that adapts based on map context.
    /// Factory and Labs require tighter distances for close-quarters gameplay.
    /// Larger maps (Customs, Woods, etc.) use standard open-engagement distances.
    /// </summary>
    internal sealed class CombatDistanceConfiguration
    {
        private static CombatDistanceConfiguration? instance;
        public static CombatDistanceConfiguration Instance
        {
            get
            {
                instance ??= new CombatDistanceConfiguration();
                return instance;
            }
        }

        private bool isFactoryMode;
        private bool isUrbanDetourMode;

        // Default map settings (Customs, Woods, Interchange, Reserve, etc.)
        private const float DefaultBossCoverSearchRadius = 30f;
        private const float DefaultStartCloseCoverDistance = 25f;
        private const float DefaultVisiblePushDistance = 18f;
        private const float DefaultBlindPushDistance = 32f;
        private const float DefaultCombatCoverMaxDistance = 120f;
        private const float DefaultHealCoverSearchRadius = 30f;
        private const float DefaultHealCoverMaxNavDistance = 35f;
        private const float DefaultHealCoverRetreatDistance = 14f;
        private const float DefaultCloseQuarterDistance = 25f;
        private const float DefaultClosePushDistance = 25f;
        private const float DefaultBossSupportShootCoverRadius = 30f;
        private const float DefaultSoundHeard = 35f;
        private const float DefaultTooClose = 8f;
        private const float DefaultCloseThreatAutoAcquireDistance = 6f;
        private const float DefaultBulletHearDistanceSqr = 50f * 50f;
        private const float DefaultBulletImpactDispersionSqr = 5f * 5f;
        private const float DefaultMarksmanRegroupMultiplier = 1.5f;
        private const float DefaultUrbanDetourDirectDistance = 45f;
        private const float DefaultUrbanDetourMaxPathDistance = 75f;
        private const float DefaultUrbanDetourMaxExtraDistance = 28f;
        private const float DefaultUrbanDetourMaxRatio = 2.4f;
        private const float DefaultRegroupBossMoveRefreshDistance = 10f;


        // Factory/Labs map settings (compressed for close-quarters gameplay)
        private const float FactoryBossCoverSearchRadius = 15f;
        private const float FactoryStartCloseCoverDistance = 12f;
        private const float FactoryVisiblePushDistance = 10f;
        private const float FactoryBlindPushDistance = 18f;
        private const float FactoryCombatCoverMaxDistance = 60f;
        private const float FactoryHealCoverSearchRadius = 18f;
        private const float FactoryHealCoverMaxNavDistance = 20f;
        private const float FactoryHealCoverRetreatDistance = 8f;
        private const float FactoryCloseQuarterDistance = 12f;
        private const float FactoryClosePushDistance = 12f;
        private const float FactoryBossSupportShootCoverRadius = 18f;
        private const float FactorySoundHeard = 15f;
        private const float FactoryTooClose = 5f;
        private const float FactoryCloseThreatAutoAcquireDistance = 5f;
        private const float FactoryBulletHearDistanceSqr = 25f * 25f;
        private const float FactoryMarksmanRegroupMultiplier = 2f;
        private const float FactoryRegroupBossMoveRefreshDistance = 8f;


        public void SetFactoryMode(bool isFactory)
        {
            isFactoryMode = isFactory;
        }

        public void UpdateForCurrentMap(string? mapName)
        {
            SetFactoryMode(UsesFactoryDistanceProfile(mapName));
            isUrbanDetourMode = UsesUrbanDetourProfile(mapName);
        }

        public static bool UsesFactoryDistanceProfile(string? mapName)
        {
            return !string.IsNullOrEmpty(mapName) &&
                   (mapName!.IndexOf("factory", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    string.Equals(mapName, "Factory4_day", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(mapName, "Factory4_night", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(mapName, "laboratory", StringComparison.OrdinalIgnoreCase));
        }

        public static bool UsesUrbanDetourProfile(string? mapName)
        {
            return string.Equals(mapName, "TarkovStreets", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mapName, "Sandbox", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(mapName, "Sandbox_high", StringComparison.OrdinalIgnoreCase);
        }

        // Boss and cover search radii
        public float GetBossCoverSearchRadius()
        {
            return isFactoryMode ? FactoryBossCoverSearchRadius : DefaultBossCoverSearchRadius;
        }

        public float GetBossRegroupTriggerDistance(BotOwner? botOwner = null)
        {
            float baseDistance = pitFireTeam.regroupRadius?.Value ?? 18f;
            return Mathf.Clamp(baseDistance, 10f, 38f);
        }

        // Close-quarter and starting distances
        public float GetStartCloseCoverDistance()
        {
            return isFactoryMode ? FactoryStartCloseCoverDistance : DefaultStartCloseCoverDistance;
        }

        public float GetCloseQuarterDistance()
        {
            return isFactoryMode ? FactoryCloseQuarterDistance : DefaultCloseQuarterDistance;
        }

        public float GetClosePushDistance()
        {
            return isFactoryMode ? FactoryClosePushDistance : DefaultClosePushDistance;
        }

        public float GetSoundHeardDistance()
        {
            return isFactoryMode ? FactorySoundHeard : DefaultSoundHeard;
        }

        public float GetTooCloseDistance()
        {
            return isFactoryMode ? FactoryTooClose : DefaultTooClose;
        }

        public float GetCloseThreatAutoAcquireDistance()
        {
            return isFactoryMode ? FactoryCloseThreatAutoAcquireDistance : DefaultCloseThreatAutoAcquireDistance;
        }

        public float GetBulletHearDistanceSqr()
        {
            return isFactoryMode ? FactoryBulletHearDistanceSqr : DefaultBulletHearDistanceSqr;
        }

        public float GetBulletImpactDispersionSqr()
        {
            return DefaultBulletImpactDispersionSqr;
        }

        // Push distances
        public float GetVisiblePushDistance()
        {
            return isFactoryMode ? FactoryVisiblePushDistance : DefaultVisiblePushDistance;
        }

        public float GetBlindPushDistance()
        {
            return isFactoryMode ? FactoryBlindPushDistance : DefaultBlindPushDistance;
        }

        // Combat cover distances
        public float GetCombatCoverMaxDistance()
        {
            return isFactoryMode ? FactoryCombatCoverMaxDistance : DefaultCombatCoverMaxDistance;
        }

        public float GetCombatCoverMaxDistanceSqr()
        {
            float distance = GetCombatCoverMaxDistance();
            return distance * distance;
        }

        // Heal cover distances
        public float GetHealCoverSearchRadius()
        {
            return isFactoryMode ? FactoryHealCoverSearchRadius : DefaultHealCoverSearchRadius;
        }

        public float GetHealCoverMaxNavDistance()
        {
            return isFactoryMode ? FactoryHealCoverMaxNavDistance : DefaultHealCoverMaxNavDistance;
        }

        public float GetHealCoverRetreatDistance()
        {
            return isFactoryMode ? FactoryHealCoverRetreatDistance : DefaultHealCoverRetreatDistance;
        }

        // Marksman-specific distances
        public float GetRegroupNeededDistanceMarksman(BotOwner? botOwner = null)
        {
            float multiplier = isFactoryMode
                ? FactoryMarksmanRegroupMultiplier
                : DefaultMarksmanRegroupMultiplier;
            return GetBossRegroupTriggerDistance(botOwner) * multiplier;
        }

        public float GetBossSupportShootCoverRadius()
        {
            return isFactoryMode ? FactoryBossSupportShootCoverRadius : DefaultBossSupportShootCoverRadius;
        }

        public bool IsUrbanDetourRegroup(float directDistance, float navDistance)
        {
            return isUrbanDetourMode &&
                   directDistance <= DefaultUrbanDetourDirectDistance &&
                   navDistance > DefaultUrbanDetourMaxPathDistance &&
                   navDistance > directDistance + DefaultUrbanDetourMaxExtraDistance &&
                   navDistance > directDistance * DefaultUrbanDetourMaxRatio;
        }

        public float GetRegroupBossMoveRefreshDistance()
        {
            return isFactoryMode ? FactoryRegroupBossMoveRefreshDistance : DefaultRegroupBossMoveRefreshDistance;
        }

        public bool IsFactoryMode => isFactoryMode;

        public bool IsUrbanDetourMode => isUrbanDetourMode;
    }
}

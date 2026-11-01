using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    internal static class SyntheticTeammateRaidGuard
    {
        private static readonly TimeSpan SyntheticRaidStartErrorWindow = TimeSpan.FromSeconds(30);
        private static DateTime _lastSyntheticRaidStartUtc = DateTime.MinValue;

        public static bool HasSyntheticTeammates()
        {
            return MainMenuControllerPatch.GroupPlayers != null && MainMenuControllerPatch.GroupPlayers.Count > 0;
        }

        internal static bool HasLocalCompanions(Profile profile, RaidSettings settings) =>
            HasSyntheticTeammates() || QuestCompanionPreview.RolesFor(profile, settings).Count > 0;

        internal static bool KeepLocal(Profile profile, RaidSettings settings, RaidSettings offlineSettings = null)
        {
            if (settings == null || !HasLocalCompanions(profile, settings)) return false;
            settings.RaidMode = ERaidMode.Local;
            // Native Ready resets IsPveOffline, then applies this second copy.
            // Updating only the displayed settings can restore Online here.
            if (offlineSettings != null) offlineSettings.RaidMode = ERaidMode.Local;
            return true;
        }

        public static bool TryForceLocalRaid(TarkovApplication application, string reason)
        {
            if (application == null)
            {
                return false;
            }

            RaidSettings raidSettings = AccessTools.Field(typeof(TarkovApplication), "_raidSettings").GetValue(application) as RaidSettings;
            if (raidSettings == null)
            {
                pitFireTeam.Log.LogWarning($"[Raid] Failed to force local raid at {reason}: raid settings missing.");
                return false;
            }

            ERaidMode previousMode = raidSettings.RaidMode;
            if (!KeepLocal(application.GetClientBackEndSession()?.Profile, raidSettings)) return false;
            raidSettings.IsPveOffline = true;
            _lastSyntheticRaidStartUtc = DateTime.UtcNow;
            Modules.Logger.LogInfo($"[Raid] Forced local companion raid at {reason}. selectedMembers={MainMenuControllerPatch.GroupPlayers.Count} location={raidSettings.LocationId} previousMode={previousMode}");
            return true;
        }

        public static bool IsRecentSyntheticRaidStart()
        {
            return DateTime.UtcNow - _lastSyntheticRaidStartUtc <= SyntheticRaidStartErrorWindow;
        }
    }

    // Run before EFT chooses LocalGameMatching versus NetworkGameMatching.
    // Quest preview membership is removed on screen close; use current quest/map/
    // party admission, never the temporary visual or a stale prepared profile.
    internal class TarkovApplicationLocalRaidStartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.OnReadyToStartMatchingAsync));

        [PatchPrefix]
        private static void PatchPrefix(TarkovApplication __instance) =>
            SyntheticTeammateRaidGuard.TryForceLocalRaid(__instance, nameof(TarkovApplication.OnReadyToStartMatchingAsync));
    }
}

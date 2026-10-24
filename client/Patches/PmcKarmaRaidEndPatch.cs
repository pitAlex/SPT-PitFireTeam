using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    internal sealed class PmcKarmaRaidEndPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(LocalGame), nameof(LocalGame.Stop),
            new[] { typeof(string), typeof(ExitStatus), typeof(string), typeof(float) });

        [PatchPrefix]
        private static void PatchPrefix(string profileId, ExitStatus exitStatus, string exitName)
        {
            GiveGearInsurance.EndRaid(profileId);
            try { AllegianceScavCargo.EndRaid(profileId, exitStatus, exitName); }
            catch (Exception ex) { Logger.LogError("[ScavCargo] Raid-end capture failed: " + ex); }
            try { PmcKarmaRuntime.EndRaid(profileId, exitStatus); }
            catch (Exception ex) { Logger.LogError("[PmcKarma] Raid-end capture failed: " + ex); }
        }
    }
}

using System;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    internal sealed class FollowerInsuranceMailClaimPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ClientBackendSession),
            nameof(ClientBackendSession.SendCallback), new[] { typeof(object[]), typeof(Result<ClientBackendSession.QueueData>) });

        [PatchPostfix]
        private static void PatchPostfix(ClientBackendSession __instance, object[] commandsToSend,
            Result<ClientBackendSession.QueueData> result)
        {
            try
            {
                if (!result.Succeed || result.Value == null || result.Value.InventoryWarnings?.Any() == true
                    || __instance?.InsuranceCompany == null || pitFireTeam.Instance == null) return;
                var ids = FollowerInsuranceMailClaim.ClaimedIds(commandsToSend, __instance.Profile);
                if (ids.Count > 0) pitFireTeam.Instance.StartCoroutine(FollowerInsuranceMailClaim.Refresh(__instance, ids));
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning($"[FollowerInsurance:MailDisplay] Claim completed without display refresh: {ex}");
            }
        }
    }
}

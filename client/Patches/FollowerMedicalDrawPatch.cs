using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using pitTeam.Modules;
using pitTeam.Utils;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace pitTeam.Patches
{
    internal sealed class FollowerMedicalDrawPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player), nameof(Player.SetInHands),
            new[] { typeof(Meds), typeof(EBodyPart), typeof(int), typeof(Callback<IMedsController>) });

        [PatchPrefix]
        private static void Prefix(Player __instance, Meds __0, ref Callback<IMedsController> callback)
        {
            BotAbstractMedsToPart? medicine = callback?.Target switch
            {
                BotFirstAid.CG_ApplyToSelf firstAid => firstAid.BotFirstAid,
                BotSurgicalKit.CG_ApplyToCurrentPart surgery => surgery.BotSurgicalKit,
                _ => null
            };
            var owner = medicine?._owner;
            if (owner == null || !ReferenceEquals(owner.GetPlayer, __instance) ||
                !BossPlayers.IsFollower(owner)) return;

            callback = FollowerMedicalOwnership.TrackDraw(medicine!, __0, callback);
        }
    }
}

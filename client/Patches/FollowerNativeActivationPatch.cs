using System.Reflection;
using EFT;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    // Native Goon followers request an AI-group patrol chooser even for a human boss.
    // Keep SetToFollow and follower mode intact; use the ordinary chooser as Knight does.
    internal sealed class FollowerHumanPatrolChooserPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(
            typeof(PatrollingData), nameof(PatrollingData.GetPointChooser),
            new[] { typeof(BotOwner), typeof(PatrolMode), typeof(IGetProfileData) });

        [PatchPrefix]
        private static void Prefix(BotOwner __0, ref PatrolMode __1)
        {
            if (__1 == PatrolMode.groupMoving &&
                __0?.BotFollower?.BossToFollow is pitAIBossPlayer leader && leader.realPlayer?.IsYourPlayer == true)
                __1 = PatrolMode.simple;
        }
    }

    // SPT's PMC test uses side alone. Managed native Goons/Rogues retain USEC presentation,
    // but their role/brain must not enter the PMC-only random-brain weight table.
    internal sealed class FollowerNativeBrainActivationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(
            AccessTools.TypeByName("SPT.Custom.Patches.CustomAiPatch"), "PatchPrefix",
            new[] { typeof(WildSpawnType).MakeByRefType(), typeof(StandartBotBrain) });

        [PatchPrefix]
        private static bool Prefix(ref WildSpawnType __0, StandartBotBrain __1, ref bool __result)
        {
            var owner = __1?._owner;
            if (owner?.Profile?.Info?.Settings == null ||
                (!Props.BossFollowersType.Contains(owner.Profile.Info.Settings.Role) &&
                 owner.Profile.Info.Settings.Role != WildSpawnType.exUsec) || !BossPlayers.IsFollower(owner))
                return true;

            // The skipped SPT prefix normally initializes its paired postfix state.
            __0 = owner.Profile.Info.Settings.Role;
            __result = true; // Run native StandartBotBrain.Activate with the original role.
            return false;
        }
    }

    // ExUsec strategy construction probes stationary weapons before the first voxel
    // update on some player spawn points. Do not fabricate a connection id or alter
    // ambient Rogue behavior; decline only an unsafe teammate lookup and allow retry.
    internal sealed class FollowerRogueStationaryInitializationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(
            typeof(BotStationaryWeaponData), nameof(BotStationaryWeaponData.TryFindClosests),
            new[] { typeof(UnityEngine.Vector3), typeof(bool), typeof(StationaryWeaponLink).MakeByRefType() });

        [PatchPrefix]
        private static bool Prefix(BotStationaryWeaponData __instance, ref StationaryWeaponLink __2, ref bool __result)
        {
            var owner = __instance?._owner;
            if (owner?.Profile?.Info?.Settings?.Role != WildSpawnType.exUsec || !BossPlayers.IsFollower(owner))
                return true;

            var cover = owner.Memory?.BotCurrentCoverInfo?.LastCover;
            bool connectionReady = owner.Memory?.BotCurrentCoverInfo != null && owner.VoxelesPersonalData != null &&
                (cover != null ? cover.CorePointInGame != null :
                 owner.VoxelesPersonalData?.CurVoxel?.PointStartSearch?.CorePointInGame != null);
            if (connectionReady) return true;

            __2 = null;
            __result = false;
            return false;
        }
    }
}

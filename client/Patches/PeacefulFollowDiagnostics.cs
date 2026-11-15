#if DEBUG
using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using UnityEngine;
using Logger = pitTeam.Modules.Logger;

namespace pitTeam.Patches
{
    internal static class PeacefulFollowDiagnostics
    {
        internal static void Apply(Harmony harmony)
        {
            Install(harmony, AccessTools.Method(typeof(BotMover), nameof(BotMover.method_12)),
                nameof(BeforeNavMeshCorrection), nameof(AfterNavMeshCorrection));
            Install(harmony, AccessTools.Method(typeof(Player), nameof(Player.Teleport),
                new[] { typeof(Vector3), typeof(bool) }), nameof(BeforeTeleport));
            Install(harmony, AccessTools.Method(typeof(BotMoverImpostor), nameof(BotMoverImpostor.OnMotionApplied)),
                nameof(BeforeSprintMotion), nameof(AfterSprintMotion));
        }

        private static void Install(Harmony harmony, MethodInfo target, string prefix, string? postfix = null)
        {
            try
            {
                if (target == null) throw new MissingMethodException(prefix);
                harmony.Patch(target, new HarmonyMethod(typeof(PeacefulFollowDiagnostics), prefix),
                    postfix == null ? null : new HarmonyMethod(typeof(PeacefulFollowDiagnostics), postfix));
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PeacefulFollowDiagnostics] Could not install {prefix}: {ex}");
            }
        }

        private static void BeforeNavMeshCorrection(BotMover __instance, out Vector3? __state)
        {
            __state = null;
            if (!BattleRecorder.IsRecordingEnabled) return;
            try
            {
                BotOwner bot = __instance._owner;
                if (BattleRecorder.CanRecordPeacefulFollowCorrection(bot)) __state = bot.Position;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PeacefulFollowDiagnostics] Could not sample position: {ex}");
            }
        }

        private static void AfterNavMeshCorrection(BotMover __instance, Vector3? __state)
        {
            if (!__state.HasValue) return;
            try
            {
                BotOwner bot = __instance._owner;
                BattleRecorder.RecordPeacefulFollowCorrection(bot, __state.Value, bot.Position);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PeacefulFollowDiagnostics] Could not record position correction: {ex}");
            }
        }

        private static void BeforeTeleport(Player __instance, Vector3 __0)
        {
            if (!BattleRecorder.IsRecordingEnabled) return;
            try
            {
                BotOwner? bot = __instance.AIData?.BotOwner;
                if (bot != null && BattleRecorder.CanRecordPeacefulFollowCorrection(bot))
                {
                    BattleRecorder.RecordPeacefulFollowTeleport(bot, bot.Position, __0);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PeacefulFollowDiagnostics] Could not record teleport: {ex}");
            }
        }

        private static void BeforeSprintMotion(
            BotMoverImpostor __instance, out (Vector3 PositionOnWay, bool ContextUsed)? __state)
        {
            __state = null;
            if (!__instance.Sprinting || !BattleRecorder.IsRecordingEnabled) return;
            try
            {
                if (BattleRecorder.CanRecordPeacefulFollowCorrection(__instance._owner))
                {
                    __state = (__instance.PositionOnWay, __instance._lastContext.Used);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PeacefulFollowDiagnostics] Could not sample sprint motion: {ex}");
            }
        }

        private static void AfterSprintMotion(
            BotMoverImpostor __instance, CollisionFlags __0, Vector3 __1,
            (Vector3 PositionOnWay, bool ContextUsed)? __state)
        {
            if (!__state.HasValue) return;
            try
            {
                var (positionOnWay, contextUsed) = __state.Value;
                BattleRecorder.RecordPeacefulFollowMotion(__instance._owner, __1,
                    __instance.PositionOnWay - positionOnWay, (__0 & CollisionFlags.Sides) != 0, contextUsed);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PeacefulFollowDiagnostics] Could not record sprint motion: {ex}");
            }
        }
    }
}
#endif

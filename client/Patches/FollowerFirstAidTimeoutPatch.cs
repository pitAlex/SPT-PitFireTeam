using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    internal sealed class FollowerFirstAidTimeoutPatch : ModulePatch
    {
        private static readonly ConditionalWeakTable<BotFirstAid, object> LatestAttempt = new();

        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(BotFirstAid), nameof(BotFirstAid.ApplyToSelf));

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var register = AccessTools.Method(typeof(AITaskManager), nameof(AITaskManager.RegisterDelayedTask),
                new[] { typeof(BotOwner), typeof(float), typeof(Action) });
            var stop = AccessTools.Method(typeof(BotFirstAid), nameof(BotFirstAid.StopUse));
            var timeoutCalls = code.Where(instruction => instruction.Calls(register)).ToList();
            if (timeoutCalls.Count != 1 || !code.Any(instruction => instruction.opcode == OpCodes.Ldftn &&
                Equals(instruction.operand, stop)))
            {
                Logger.LogWarning("[Medical] First-aid timeout protection unavailable: native scheduling changed.");
                return code;
            }

            timeoutCalls[0].opcode = OpCodes.Call;
            timeoutCalls[0].operand = AccessTools.Method(typeof(FollowerFirstAidTimeoutPatch), nameof(RegisterTimeout));
            return code;
        }

        private static int RegisterTimeout(AITaskManager tasks, BotOwner owner, float delay, Action stop)
        {
            if (!BossPlayers.IsFollower(owner) || stop?.Target is not BotFirstAid firstAid)
                return tasks.RegisterDelayedTask(owner, delay, stop);

            // EFT leaves a StopUse callback scheduled even when the dose completes.
            // A stale callback must never take the weapon back during another treatment.
            var attempt = new object();
            LatestAttempt.Remove(firstAid);
            LatestAttempt.Add(firstAid, attempt);
            return tasks.RegisterDelayedTask(owner, delay, () =>
            {
                if (!LatestAttempt.TryGetValue(firstAid, out object latest) || !ReferenceEquals(latest, attempt))
                    return;

                LatestAttempt.Remove(firstAid);
                if (firstAid.Using)
                    stop();
            });
        }
    }
}

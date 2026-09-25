using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.Modules;

namespace pitTeam.SAINAddon;

// Hook Core's accepted Attention command before any shared group contacts are
// removed. No hearing provider or ordinary SAIN bot is patched.
internal static class SainAttentionBridge
{
    internal static void Apply(Harmony harmony)
    {
        var method = AccessTools.Method(typeof(pitAIBossPlayer), "HandleAttentionCommand", Type.EmptyTypes);
        var field = AccessTools.Field(typeof(pitAIBossPlayer), "_lastAttentionCommandAt");
        if (method == null || method.IsStatic || method.ReturnType != typeof(void) ||
            field == null || field.FieldType != typeof(float))
            throw new MissingMethodException("Core Attention acceptance boundary changed.");
        harmony.Patch(method, transpiler: new HarmonyMethod(typeof(SainAttentionBridge), nameof(CaptureAfterAcceptance)));
    }

    private static IEnumerable<CodeInstruction> CaptureAfterAcceptance(IEnumerable<CodeInstruction> instructions)
    {
        var accepted = AccessTools.Field(typeof(pitAIBossPlayer), "_lastAttentionCommandAt");
        var capture = AccessTools.Method(typeof(SainAttentionBridge), nameof(Capture));
        int count = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, accepted))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, capture);
                count++;
            }
        }
        if (count != 1) throw new MissingMethodException("Core Attention acceptance assignment changed.");
    }

    private static void Capture(pitAIBossPlayer boss)
    {
        foreach (var owner in boss.Followers)
        {
            try { SAINFollowerRuntime.RememberAttentionContacts(owner); }
            catch (Exception ex) { Logger.LogError($"[SAIN] Attention contact capture failed: {ex}"); }
        }
    }
}

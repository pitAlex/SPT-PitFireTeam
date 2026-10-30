using EFT;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

namespace pitTeam.Patches
{
    /// <summary>Preserve EFT's swallowed activation exception for pending followers only.</summary>
    internal static class FollowerActivationDiagnostics
    {
        internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var catches = code.Where(instruction => instruction.blocks.Any(block =>
                block.blockType == ExceptionBlockType.BeginCatchBlock)).ToList();
            // The verified native method has one Exception catch beginning with pop.
            // If another version/patch changes that shape, leave the method intact.
            if (catches.Count != 1 || catches[0].opcode != OpCodes.Pop ||
                !catches[0].blocks.Any(block => block.blockType == ExceptionBlockType.BeginCatchBlock &&
                    block.catchType == typeof(Exception)))
            {
                try { Modules.Logger.LogError("[FollowerSpawn] Native activation exception capture unavailable: catch layout changed."); }
                catch { }
                return code;
            }

            int index = code.IndexOf(catches[0]);
            // At catch entry the exception is already on the stack. Replace its
            // discard with Report(exception, this); retain all labels/blocks.
            code[index].opcode = OpCodes.Ldarg_0;
            code[index].operand = null;
            code.Insert(index + 1, new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(FollowerActivationDiagnostics), nameof(Report))));
            return code;
        }

        private static void Report(Exception error, BotOwner owner)
        {
            try
            {
                string id = owner?.ProfileId;
                if (id == null || !BotOwnerManualUpdatePatch.BotOwnerUpdate.ContainsKey(id)) return;
                // Log the exception before reading optional profile metadata: a
                // broken profile getter must not hide the original stack trace.
                Modules.Logger.LogError($"[FollowerSpawn] Native activation failed profile={id}: {error}");
            }
            catch
            {
                // Diagnostics must not escape EFT's catch or change ActiveFail.
            }
        }
    }
}

using EFT;
using HarmonyLib;
using pitTeam.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace pitTeam.Patches
{
    internal static class FollowerSainVisionRaycastPatch
    {
        internal static void Apply(Harmony harmony)
        {
            try
            {
                Type? jobType = Type.GetType("SAIN.Components.VisionRaycastJob, SAIN");
                MethodInfo? createCommands = jobType != null
                    ? AccessTools.Method(jobType, "CreateCommands")
                    : null;
                if (createCommands == null)
                {
                    Modules.Logger.LogError("[SAIN] Follower foliage mask patch skipped: CreateCommands was not found.");
                    return;
                }

                harmony.Patch(
                    createCommands,
                    transpiler: new HarmonyMethod(
                        typeof(FollowerSainVisionRaycastPatch).GetMethod(
                            nameof(TranspileMasks), BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[SAIN] Follower foliage mask patch could not be applied: {ex}");
            }
        }

        private static IEnumerable<CodeInstruction> TranspileMasks(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator,
            MethodBase __originalMethod)
        {
            List<CodeInstruction> codes = instructions.ToList();
            try
            {
                Type? jobType = __originalMethod.DeclaringType;
                Type? botBaseType = jobType?.Assembly.GetType("SAIN.SAINComponent.BotBase");
                MethodInfo? getBot = botBaseType != null
                    ? AccessTools.PropertyGetter(botBaseType, "Bot")
                    : null;
                MethodInfo? getBotOwner = getBot != null
                    ? AccessTools.PropertyGetter(getBot.ReturnType, "BotOwner")
                    : null;
                FieldInfo?[] maskFields =
                {
                    jobType != null ? AccessTools.Field(jobType, "_losParams") : null,
                    jobType != null ? AccessTools.Field(jobType, "_visParams") : null,
                    jobType != null ? AccessTools.Field(jobType, "_shootParams") : null,
                };

                if (getBot == null || getBot.IsStatic ||
                    getBotOwner == null || getBotOwner.IsStatic ||
                    getBotOwner.ReturnType != typeof(BotOwner) ||
                    maskFields.Any(field => field == null || !field.IsStatic || field.FieldType != typeof(QueryParameters)))
                {
                    Modules.Logger.LogError("[SAIN] Follower foliage mask patch skipped: unsupported vision member layout.");
                    return codes;
                }

                List<int> botCalls = new List<int>();
                List<int>[] maskLoads = { new List<int>(), new List<int>(), new List<int>() };
                for (int i = 0; i < codes.Count; i++)
                {
                    CodeInstruction code = codes[i];
                    if ((code.opcode == OpCodes.Callvirt || code.opcode == OpCodes.Call) &&
                        Equals(code.operand, getBot))
                    {
                        botCalls.Add(i);
                    }

                    for (int mask = 0; mask < maskFields.Length; mask++)
                    {
                        if (code.opcode == OpCodes.Ldsfld && Equals(code.operand, maskFields[mask]))
                        {
                            maskLoads[mask].Add(i);
                        }
                    }
                }

                // Match the complete SAIN 4.5 shape before changing any instruction.
                // A changed/previously patched builder must retain its native code, not a partial bypass.
                if (botCalls.Count != 1 ||
                    maskLoads.Any(loads => loads.Count != 1 || loads[0] <= botCalls[0]))
                {
                    Modules.Logger.LogError("[SAIN] Follower foliage mask patch skipped: expected one bot lookup and three distinct mask loads.");
                    return codes;
                }

                MethodInfo isFollower = AccessTools.Method(typeof(FollowerSainVisionRaycastPatch), nameof(IsFollower));
                MethodInfo selectMask = AccessTools.Method(typeof(FollowerSainVisionRaycastPatch), nameof(SelectMask));
                LocalBuilder followerLocal = generator.DeclareLocal(typeof(bool));
                LocalBuilder[] maskLocals =
                {
                    generator.DeclareLocal(typeof(QueryParameters)),
                    generator.DeclareLocal(typeof(QueryParameters)),
                    generator.DeclareLocal(typeof(QueryParameters)),
                };
                List<CodeInstruction> result = new List<CodeInstruction>(codes.Count + 16);
                for (int i = 0; i < codes.Count; i++)
                {
                    int maskIndex = Array.FindIndex(maskLoads, loads => loads[0] == i);
                    if (maskIndex >= 0)
                    {
                        CodeInstruction replacement = new CodeInstruction(OpCodes.Ldloc, maskLocals[maskIndex]);
                        replacement.labels.AddRange(codes[i].labels);
                        replacement.blocks.AddRange(codes[i].blocks);
                        result.Add(replacement);
                    }
                    else
                    {
                        result.Add(codes[i]);
                    }

                    if (i == botCalls[0])
                    {
                        // get_Bot leaves its component on the stack. Inspect a duplicate once per
                        // enemy, then let SAIN consume the original and build every ray normally.
                        result.Add(new CodeInstruction(OpCodes.Dup));
                        result.Add(new CodeInstruction(OpCodes.Callvirt, getBotOwner));
                        result.Add(new CodeInstruction(OpCodes.Call, isFollower));
                        result.Add(new CodeInstruction(OpCodes.Stloc, followerLocal));
                        for (int mask = 0; mask < maskFields.Length; mask++)
                        {
                            result.Add(new CodeInstruction(OpCodes.Ldsfld, maskFields[mask]));
                            result.Add(new CodeInstruction(OpCodes.Ldloc, followerLocal));
                            result.Add(new CodeInstruction(OpCodes.Call, selectMask));
                            result.Add(new CodeInstruction(OpCodes.Stloc, maskLocals[mask]));
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[SAIN] Follower foliage mask patch skipped; keeping native vision code: {ex}");
                return codes;
            }
        }

        private static bool IsFollower(BotOwner botOwner)
        {
            try
            {
                return BossPlayers.IsFollower(botOwner);
            }
            catch
            {
                return false;
            }
        }

        private static QueryParameters SelectMask(QueryParameters nativeParams, bool follower)
        {
            // Same follower parameters as the former replacement builder, without its per-part
            // reflection/boxing. Non-followers keep SAIN's complete original query parameters.
            return follower
                ? new QueryParameters(LayersMaskController.HighPolyWithTerrainMask)
                : nativeParams;
        }
    }
}

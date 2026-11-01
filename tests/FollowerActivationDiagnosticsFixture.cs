using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Patches;

namespace EFT
{
    public class BotOwner
    {
        public string ProfileId;
        public bool Fail;
        public bool Active;
        public bool ActiveFail;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Activate()
        {
            try { ActivateSubsystem(); Active = true; }
            catch (Exception) { ActiveFail = true; }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ActivateSubsystem()
        {
            if (Fail) throw new InvalidOperationException("original subsystem failure");
        }
    }
}
namespace pitTeam.Patches
{
    internal static class BotOwnerManualUpdatePatch
    {
        public static Dictionary<string, Action<BotOwner>> BotOwnerUpdate = new Dictionary<string, Action<BotOwner>>();
    }
}
namespace pitTeam.Modules
{
    internal static class Logger
    {
        public static readonly List<string> Messages = new List<string>();
        public static bool Fail;
        public static void LogError(string message)
        {
            if (Fail) throw new Exception("logger unavailable");
            Messages.Add(message);
        }
    }
}
internal static class Program
{
    private static int checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        checks++;
    }
    private static BotOwner Owner(string id, bool fail)
    {
        return new BotOwner { ProfileId = id, Fail = fail };
    }
    public static void Main()
    {
        var harmony = new Harmony("pitfireteam.fixture.activation");
        harmony.Patch(AccessTools.Method(typeof(BotOwner), "Activate"), transpiler:
            new HarmonyMethod(AccessTools.Method(typeof(FollowerActivationDiagnostics), "Transpile")));
        BotOwnerManualUpdatePatch.BotOwnerUpdate["birdeye"] = _ => { };
        var success = Owner("birdeye", false);
        success.Activate();
        Check(success.Active && !success.ActiveFail, "successful native activation preserved");
        Check(pitTeam.Modules.Logger.Messages.Count == 0, "success produces no diagnostic");
        var fail = Owner("birdeye", true);
        fail.Activate();
        Check(!fail.Active && fail.ActiveFail, "native catch still sets ActiveFail");
        Check(pitTeam.Modules.Logger.Messages.Count == 1, "one diagnostic for pending follower");
        string message = pitTeam.Modules.Logger.Messages.Single();
        Check(message.Contains("profile=birdeye"), "identity captured");
        Check(message.Contains("System.InvalidOperationException: original subsystem failure"), "original exception retained");
        Check(message.Contains("ActivateSubsystem"), "original subsystem stack retained");
        var ordinary = Owner("ordinary", true);
        ordinary.Activate();
        Check(ordinary.ActiveFail && pitTeam.Modules.Logger.Messages.Count == 1, "ordinary bot unchanged and excluded");
        BotOwnerManualUpdatePatch.BotOwnerUpdate.Remove("birdeye");
        Owner("birdeye", true).Activate();
        Check(pitTeam.Modules.Logger.Messages.Count == 1, "completed or aborted registration excluded");
        Owner(null, true).Activate();
        Check(pitTeam.Modules.Logger.Messages.Count == 1, "missing identity safe");
        BotOwnerManualUpdatePatch.BotOwnerUpdate["birdeye"] = _ => { };
        pitTeam.Modules.Logger.Fail = true;
        var brokenLogger = Owner("birdeye", true);
        brokenLogger.Activate();
        Check(brokenLogger.ActiveFail, "logging failure cannot escape native catch");
        pitTeam.Modules.Logger.Fail = false;

        var plain = new[] { new CodeInstruction(OpCodes.Ret) };
        Check(FollowerActivationDiagnostics.Transpile(plain).Single() == plain[0], "unrecognized method left intact");
        var unexpected = new CodeInstruction(OpCodes.Stloc_0);
        unexpected.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, typeof(Exception)));
        Check(FollowerActivationDiagnostics.Transpile(new[] { unexpected }).Single().opcode == OpCodes.Stloc_0,
            "changed catch entry left intact");
        var original = new CodeInstruction(OpCodes.Pop);
        var block = new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, typeof(Exception));
        original.blocks.Add(block);
        var il = new DynamicMethod("labels", typeof(void), Type.EmptyTypes).GetILGenerator();
        var label = il.DefineLabel();
        original.labels.Add(label);
        var captured = FollowerActivationDiagnostics.Transpile(new[] { original }).ToList();
        Check(captured.Count == 2 && captured[0].opcode == OpCodes.Ldarg_0 && captured[1].opcode == OpCodes.Call,
            "catch consumes exception through reporter");
        Check(captured[0].labels.Single().Equals(label) && captured[0].blocks.Single().Equals(block),
            "catch metadata and labels preserved");
        var extra = new CodeInstruction(OpCodes.Pop);
        extra.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, typeof(Exception)));
        var ambiguous = new CodeInstruction(OpCodes.Pop);
        ambiguous.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, typeof(Exception)));
        Check(FollowerActivationDiagnostics.Transpile(new[] { extra, ambiguous }).Count() == 2 && extra.opcode == OpCodes.Pop,
            "multiple catches left intact");
        Console.WriteLine(checks + " follower activation diagnostic checks passed (real Harmony).");
    }
}

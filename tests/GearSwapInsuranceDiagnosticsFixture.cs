using System;
using System.Collections.Generic;
using System.Linq;
using pitTeam.Modules;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }
    private static void Icon(string id, bool draft = true, bool native = false, bool cached = false, bool visible = false)
    {
        GearSwapInsuranceDiagnostics.Icon(id, "SlotItemView", draft, native, cached, cached, cached, visible, "cacheMatches=True cachedIds=1");
    }
    private static void Main(string[] args)
    {
        if (args.Contains("--release"))
        {
            GearSwapInsuranceDiagnostics.Begin("follower", new[] { "gun" });
            GearSwapInsuranceDiagnostics.Summary("open", "cache", 1, 1);
            GearSwapInsuranceDiagnostics.Item("open", "gun", "tpl", "draft", true, true);
            Icon("gun", cached: true);
            GearSwapInsuranceDiagnostics.End(true);
            Check(Logger.Messages.Count == 0, "Release call sites do not execute diagnostic methods.");
            Console.WriteLine("PASS: Release insurance diagnostics are compiled out.");
            return;
        }
        GearSwapInsuranceDiagnostics.Clear();
        Icon("gun");
        Check(Logger.Messages.Count == 0, "No observations outside an exchange.");
        GearSwapInsuranceDiagnostics.Begin("follower", new[] { "gun", "scope", "gun", null });
        Check(Logger.Messages.Single().Contains("watched=2"), "Watched identities deduplicate and exclude missing IDs.");
        Icon("unrelated");
        Check(Logger.Messages.Count == 1, "Unrelated inventory is not traced.");
        Icon("GUN");
        Check(Logger.Messages.Count == 2, "Exact watched IDs use case-insensitive matching.");
        Icon("GUN");
        Check(Logger.Messages.Count == 2, "Unchanged icon states are deduplicated.");
        Icon("GUN", cached: true, visible: true);
        Check(Logger.Messages.Count == 3 && Logger.Messages.Last().Contains("iconVisible=True"), "Cache/visibility changes are recorded.");
        Icon("GUN", draft: false, cached: true);
        Check(Logger.Messages.Count == 4, "Live and draft presentations are separately observed.");
        GearSwapInsuranceDiagnostics.Item("staged:1", "gun", "template", "player:draft", false, true);
        Check(Logger.Messages.Last().Contains("location='player:draft'"), "A staged transfer keeps the exact item's trace.");
        GearSwapInsuranceDiagnostics.Summary("applyAfter", "cacheMatches=False", 1, 0);
        Check(Logger.Messages.Last().Contains("cacheMatches=False"), "A mismatched raid cache is visible without being repaired.");
        GearSwapInsuranceDiagnostics.End(true);
        Check(Logger.Messages.Last().Contains("committed=True"), "Committed close is distinguished.");
        int before = Logger.Messages.Count;
        UnityEngine.Time.time = 59f;
        Icon("scope", draft: false);
        Check(Logger.Messages.Count == before + 1, "Post-close live observations remain enabled briefly.");
        UnityEngine.Time.time = 61f;
        Icon("scope", draft: false, cached: true);
        Check(Logger.Messages.Count == before + 1, "Post-close tracing expires without timers or retained views.");
        GearSwapInsuranceDiagnostics.Begin("otherFollower", new[] { "other" });
        before = Logger.Messages.Count;
        Icon("gun");
        Check(Logger.Messages.Count == before, "A new exchange discards earlier watched IDs.");
        GearSwapInsuranceDiagnostics.End(false);
        Check(Logger.Messages.Last().Contains("committed=False"), "Cancellation does not claim Apply committed.");
        GearSwapInsuranceDiagnostics.Clear();
        before = Logger.Messages.Count;
        Icon("other");
        Check(Logger.Messages.Count == before, "Raid teardown clears observations.");
        GearSwapInsuranceDiagnostics.Begin("bounded", Enumerable.Range(0, 600).Select(i => "item" + i));
        Check(Logger.Messages.Last().Contains("watched=512"), "Watched identity storage is bounded.");
        before = Logger.Messages.Count;
        Icon("item599");
        Check(Logger.Messages.Count == before, "Out-of-bound identity is ignored.");
        Logger.Throw = true;
        GearSwapInsuranceDiagnostics.Begin("loggerFailure", new[] { "gun" });
        Icon("gun");
        GearSwapInsuranceDiagnostics.End(true);
        Check(true, "Logging failures cannot interrupt an exchange or its native UI.");
        Logger.Throw = false;
        Console.WriteLine("PASS: " + checks + " passive Swap Gear insurance diagnostics checks.");
    }
}
namespace UnityEngine { internal static class Time { internal static float time; } }
namespace pitTeam.Modules
{
    internal static class Logger
    {
        internal static readonly List<string> Messages = new List<string>();
        internal static bool Throw;
        internal static void LogInfo(string message)
        {
            if (Throw) throw new Exception("Simulated log failure.");
            Messages.Add(message);
        }
    }
}

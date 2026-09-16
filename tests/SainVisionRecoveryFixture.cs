using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using pitTeam.Modules;
using pitTeam.Patches;

internal static class VisionRecoveryChecks
{
    private static int checks;
    internal static Type ResolveType(string name) => typeof(SAIN.Components.VisionRaycastJob);
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }
    private static IEnumerator Healthy(object marker) { yield return marker; yield return null; }
    private static IEnumerator Broken() { throw new InvalidOperationException("collection modified"); }
    private static IEnumerator Mutate(HashSet<int> enemies, Action visit)
    {
        yield return "native wait";
        foreach (int enemy in enemies)
        {
            visit();
            enemies.Add(2);
        }
        yield return "group 2 reached";
    }
    public static void Main()
    {
        var harmony = new Harmony("pitTeam.vision.recovery.fixture");
        SainVisionRecoveryPatch.Apply(harmony);
        Check(Logger.Messages.Count == 1 && Logger.Messages[0].Contains("enabled"), "core patch installs without addon/follower state");
        var enemies = new HashSet<int> { 1 };
        int visits = 0;
        var job = new SAIN.Components.VisionRaycastJob { Factory = () => Mutate(enemies, () => visits++) };
        IEnumerator loop = job.Create();
        Check(loop is SainVisionRecoveryEnumerator && job.Created == 1, "one wrapper around native factory");
        Check(loop.MoveNext() && (string)loop.Current == "native wait", "native yield preserved");
        Check(loop.MoveNext() && loop.Current is UnityEngine.WaitForSeconds wait && wait.Seconds == 1f, "real HashSet mutation contained with one second delay");
        Check(job.Created == 1 && Logger.Messages.Count == 2, "no synchronous restart and original exception logged");
        Check(loop.MoveNext() && job.Created == 2 && (string)loop.Current == "native wait", "fresh iterator starts on following scheduled tick");
        Check(loop.MoveNext() && (string)loop.Current == "group 2 reached" && visits == 3, "shared loop resumes remaining bots after transient mutation");
        Check(!loop.MoveNext() && !loop.MoveNext() && job.Created == 2, "normal completion never restarts");

        int attempts = 0;
        job = new SAIN.Components.VisionRaycastJob { Factory = () => { attempts++; return BrokenIterator(); } };
        loop = job.Create();
        UnityEngine.Time.realtimeSinceStartup = 40;
        int logs = Logger.Messages.Count;
        for (int i = 0; i < 5; i++) Check(loop.MoveNext() && loop.Current is UnityEngine.WaitForSeconds, "persistent fault yields retry wait " + i);
        Check(attempts == 5 && job.Created == 5, "each retry replaces exactly one iterator without nested wrappers");
        Check(Logger.Messages.Count == logs + 1, "persistent fault reports throttled");
        UnityEngine.Time.realtimeSinceStartup = 70;
        Check(loop.MoveNext() && Logger.Messages.Count == logs + 2 && Logger.Messages[Logger.Messages.Count-1].Contains("failures=6"), "later report includes cumulative failures");
        int created = job.Created;
        job.Dispose();
        Check(!loop.MoveNext() && job.Created == created, "native job disposal cancels pending restart");

        job = new SAIN.Components.VisionRaycastJob { Factory = BrokenIterator };
        loop = job.Create(); loop.MoveNext(); job.BotController.Destroyed = true;
        Check(!loop.MoveNext() && job.Created == 1, "destroyed Unity controller cancels pending restart");
        job = new SAIN.Components.VisionRaycastJob { Factory = () => Healthy("ok") };
        loop = job.Create(); ((IDisposable)loop).Dispose();
        Check(!loop.MoveNext() && job.Created == 1, "explicit wrapper disposal is terminal");

        bool firstFactory = true;
        job = new SAIN.Components.VisionRaycastJob { Factory = () => {
            if (firstFactory) { firstFactory = false; return BrokenIterator(); }
            throw new Exception("factory failed");
        }};
        loop = job.Create(); loop.MoveNext();
        Check(loop.MoveNext() && loop.Current is UnityEngine.WaitForSeconds, "replacement factory exception also contained");
        job.Factory = () => Healthy("factory recovered");
        Check(loop.MoveNext() && (string)loop.Current == "factory recovered", "factory failure does not strand bypass flag or wrapper");

        var odd = new BadEnumerator();
        var recovery = new SainVisionRecoveryEnumerator(odd, () => Healthy("later"), () => true,
            () => 0, "retry", (e,n) => { throw new Exception("logger failed"); });
        Check(recovery.MoveNext() && (string)recovery.Current == "retry" && odd.Disposed, "Current, disposal and logging failures cannot kill recovery");
        Check(recovery.MoveNext() && (string)recovery.Current == "later", "continues after diagnostic failure");
        harmony.UnpatchSelf();
        Console.WriteLine(checks + " vision recovery checks passed.");
    }
    private static IEnumerator BrokenIterator() { yield return Broken(); }
    private sealed class BadEnumerator : IEnumerator, IDisposable
    {
        public bool Disposed;
        public object Current => throw new Exception("Current failed");
        public bool MoveNext() => true;
        public void Reset() => throw new NotSupportedException();
        public void Dispose() { Disposed = true; throw new Exception("Dispose failed"); }
    }
}
namespace SAIN.Components
{
    public sealed class VisionRaycastJob
    {
        private bool _disposed;
        public UnityEngine.Object BotController { get; } = new UnityEngine.Object();
        public Func<IEnumerator> Factory;
        public int Created;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private IEnumerator UpdateEFTVision() { Created++; return Factory(); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public IEnumerator Create() => UpdateEFTVision();
        public void Dispose() { _disposed = true; }
    }
}
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) =>
            (ReferenceEquals(a,null) || a.Destroyed) == (ReferenceEquals(b,null) || b.Destroyed);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object b) => ReferenceEquals(this,b);
        public override int GetHashCode() => base.GetHashCode();
    }
    public static class Time { public static float realtimeSinceStartup; }
    public sealed class WaitForSeconds { public readonly float Seconds; public WaitForSeconds(float seconds) { Seconds = seconds; } }
}
namespace pitTeam.Modules
{
    public static class Logger
    {
        public static readonly List<string> Messages = new List<string>();
        public static void LogInfo(object value) { Messages.Add(value.ToString()); }
        public static void LogError(object value) { Messages.Add(value.ToString()); }
    }
}

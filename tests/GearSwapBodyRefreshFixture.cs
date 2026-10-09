using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using pitTeam.Modules;

static class GearSwapBodyRefreshFixture
{
    private static int _assertions;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _assertions++;
    }

    private sealed class Pump : SynchronizationContext
    {
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        public override void Post(SendOrPostCallback callback, object state) => _queue.Enqueue(() => callback(state));
        internal void Run(Func<Task> action)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                Task task = action();
                DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                while (!task.IsCompleted)
                {
                    if (DateTime.UtcNow > deadline) throw new Exception("Test pump timed out");
                    if (_queue.TryDequeue(out Action next)) next();
                    else Thread.Sleep(1);
                }
                task.GetAwaiter().GetResult();
            }
            finally { SetSynchronizationContext(previous); }
        }
    }

    private static async Task Expect<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { Check(true, typeof(T).Name + " propagated"); return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private static async Task Cases(Pump pump)
    {
        int unityThread = Thread.CurrentThread.ManagedThreadId;
        bool held = true, duplicate = false;
        var legacy = new TaskCompletionSource<bool>();
        pump.Post(_ => { duplicate = held; legacy.SetResult(true); }, null);
        await legacy.Task;
        Check(duplicate, "Old immediate hands restoration reproduces the late holstered copy");

        held = false; duplicate = false;
        var first = new TaskCompletionSource<bool>();
        var second = new TaskCompletionSource<bool>();
        Task current = first.Task;
        int restores = 0, verifies = 0, reads = 0;
        var phases = new List<string>();
        pump.Post(_ =>
        {
            duplicate = held;
            current = second.Task;
            first.SetResult(true);
            pump.Post(__ => { duplicate |= held; second.SetResult(true); }, null);
        }, null);
        await GearSwapBodyRefresh.Run(() => true, () =>
        {
            reads++;
            Check(Thread.CurrentThread.ManagedThreadId == unityThread, "Model reads stay on Unity thread");
            return new[] { current };
        }, () =>
        {
            Check(first.Task.IsCompleted && second.Task.IsCompleted, "Hands wait for original and replacement jobs");
            held = true; restores++;
            Check(Thread.CurrentThread.ManagedThreadId == unityThread, "Hands restoration stays on Unity thread");
            return Task.CompletedTask;
        }, () =>
        {
            verifies++;
            Check(!duplicate && held, "No late holstered copy after synchronized restore");
            Check(Thread.CurrentThread.ManagedThreadId == unityThread, "Final verification stays on Unity thread");
        }, phases.Add);
        Check(restores == 1 && verifies == 1 && reads >= 3, "Restore once and resample native jobs");
        Check(string.Join(",", phases) == "body-refresh-wait,body-refresh-ready,body-refresh-after-hands,body-refresh-verified",
            "Diagnostics bracket the synchronized sequence");

        var afterHands = new TaskCompletionSource<bool>();
        current = Task.CompletedTask;
        await GearSwapBodyRefresh.Run(() => true, () => new[] { current }, () =>
        {
            current = afterHands.Task;
            pump.Post(_ => afterHands.SetResult(true), null);
            return Task.CompletedTask;
        }, () => Check(afterHands.Task.IsCompleted, "Final verification waits for loads started during hands restoration"), null);

        var canceled = new TaskCompletionSource<bool>();
        canceled.SetCanceled();
        await GearSwapBodyRefresh.Run(() => true, () => new[] { null, canceled.Task, Task.CompletedTask },
            () => Task.CompletedTask, () => Check(true, "Native canceled/absent jobs do not reject refresh"), null);

        restores = 0; verifies = 0;
        await Expect<InvalidOperationException>(() => GearSwapBodyRefresh.Run(() => true,
            () => new[] { Task.FromException(new InvalidOperationException("model failure")) },
            () => { restores++; return Task.CompletedTask; }, () => verifies++, null));
        Check(restores == 1 && verifies == 0, "Failed load still restores hands but cannot report verified success");

        restores = 0;
        var stuck = new TaskCompletionSource<bool>();
        await Expect<TimeoutException>(() => GearSwapBodyRefresh.Run(() => true, () => new[] { stuck.Task },
            () => { restores++; return Task.CompletedTask; }, () => verifies++, null, timeoutMs: 25));
        Check(restores == 1, "Stuck load is bounded and does not strand hands");

        await Expect<OperationCanceledException>(() => GearSwapBodyRefresh.Run(() => false, () => new Task[0],
            () => Task.CompletedTask, () => verifies++, null));
        await Expect<InvalidOperationException>(() => GearSwapBodyRefresh.Run(() => true, () => new Task[0],
            () => Task.FromException(new InvalidOperationException("hands failure")), () => verifies++, null));
        Check(verifies == 0, "Death and failed hands cannot reach final visual verification");
        await Expect<InvalidOperationException>(() => GearSwapBodyRefresh.Run(() => true, () => new Task[0],
            () => Task.CompletedTask, () => throw new InvalidOperationException("held model remains"), null));
    }

    public static int Main()
    {
        try
        {
            var pump = new Pump();
            pump.Run(() => Cases(pump));
            Console.WriteLine($"Gear Swap body refresh: {_assertions} assertions passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

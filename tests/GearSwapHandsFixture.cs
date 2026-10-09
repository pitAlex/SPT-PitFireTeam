using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using pitTeam.Modules;

static class GearSwapHandsFixture
{
    private static int _assertions;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        _assertions++;
    }

    // Unity-like single-thread pump: completion callbacks run before native process unlock.
    private sealed class Pump : SynchronizationContext
    {
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        public override void Post(SendOrPostCallback callback, object state) => _queue.Enqueue(() => callback(state));
        internal void Run(Func<Task> action)
        {
            SynchronizationContext previous = Current;
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

    private static async Task Expect<T>(Func<Task> action, string name) where T : Exception
    {
        try { await action(); }
        catch (T) { Check(true, name); return; }
        throw new Exception("Missing expected exception: " + name);
    }

    private static async Task Cases(Pump pump)
    {
        int unityThread = Thread.CurrentThread.ManagedThreadId;
        bool idle = true, insideCallback = false, legacyRestoredWhileLocked = false;
        var legacyCompletion = new TaskCompletionSource<bool>();
        Func<Task> legacy = async () =>
        {
            await legacyCompletion.Task;
            legacyRestoredWhileLocked = !idle && insideCallback;
        };
        Task legacyTask = legacy();
        pump.Post(_ =>
        {
            idle = false; insideCallback = true;
            legacyCompletion.TrySetResult(true);
            insideCallback = false; idle = true;
        }, null);
        await legacyTask;
        Check(legacyRestoredWhileLocked, "Original inline callback continuation reproduces locked restore");
        // Allow the native callback to finish before starting the corrected case.
        await Task.Delay(10);

        int starts = 0, mismatches = 0;
        await GearSwapHandsTransition.Run<string>(() => true, () => idle, complete =>
        {
            starts++;
            Check(idle, "Request waits for native idle");
            Check(Thread.CurrentThread.ManagedThreadId == unityThread, "Request stays on Unity thread");
            pump.Post(_ =>
            {
                idle = false; insideCallback = true;
                complete("weapon", true, null);
                insideCallback = false; idle = true;
            }, null);
        }, returned =>
        {
            Check(!insideCallback && idle, "Verification runs after callback unwinds and unlocks");
            Check(Thread.CurrentThread.ManagedThreadId == unityThread, "Verification stays on Unity thread");
            return returned == "weapon";
        }, _ => mismatches++);
        Check(starts == 1 && mismatches == 0, "Correct weapon succeeds once");

        idle = false; starts = 0;
        pump.Post(_ => idle = true, null);
        await GearSwapHandsTransition.Run<string>(() => true, () => idle,
            complete => { Check(idle, "Initially busy process settles before request"); starts++; complete("weapon", true, null); },
            returned => returned == "weapon", null);
        Check(starts == 1, "Initial busy state does not create duplicate requests");

        starts = 0; mismatches = 0;
        await GearSwapHandsTransition.Run<string>(() => true, () => true,
            complete => complete(++starts == 1 ? "emptyHands" : "weapon", true, null),
            returned => returned == "weapon", _ => mismatches++);
        Check(starts == 2 && mismatches == 1, "Successful empty-hands fallback is retried, not accepted");

        starts = 0; mismatches = 0;
        await Expect<InvalidOperationException>(() => GearSwapHandsTransition.Run<string>(() => true, () => true,
            complete => { starts++; complete("wrongWeapon", true, null); },
            returned => returned == "weapon", _ => mismatches++), "Wrong weapon fails after bounded retries");
        Check(starts == 3 && mismatches == 3, "Exactly three wrong-result attempts");

        starts = 0;
        await Expect<InvalidOperationException>(() => GearSwapHandsTransition.Run<string>(() => true, () => true,
            complete => { starts++; complete(null, false, "nativeRejected"); },
            returned => true, null), "Native failure is reported");
        Check(starts == 1, "Native failures are not blindly retried");

        starts = 0;
        await Expect<TimeoutException>(() => GearSwapHandsTransition.Run<string>(() => true, () => false,
            complete => starts++, returned => true, null, timeoutMs: 25), "Stuck native process is bounded");
        Check(starts == 0, "Locked inventory is never bypassed");

        await Expect<TimeoutException>(() => GearSwapHandsTransition.Run<string>(() => true, () => true,
            complete => { }, returned => true, null, timeoutMs: 25), "Missing callback is bounded");

        starts = 0;
        await Expect<OperationCanceledException>(() => GearSwapHandsTransition.Run<string>(() => false, () => true,
            complete => starts++, returned => true, null), "Death cancels before request");
        Check(starts == 0, "No hands request on dead actor");

        bool alive = true;
        await Expect<OperationCanceledException>(() => GearSwapHandsTransition.Run<string>(() => alive, () => true,
            complete => { alive = false; complete("weapon", true, null); },
            returned => true, null), "Death during callback is not accepted as success");

        idle = true;
        await Expect<TimeoutException>(() => GearSwapHandsTransition.Run<string>(() => true, () => idle,
            complete => { idle = false; complete("weapon", true, null); },
            returned => true, null, timeoutMs: 25), "Callback success cannot bypass pending native unlock");
    }

    public static int Main()
    {
        try
        {
            var pump = new Pump();
            pump.Run(() => Cases(pump));
            Console.WriteLine($"Gear Swap hands transition: {_assertions} assertions passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

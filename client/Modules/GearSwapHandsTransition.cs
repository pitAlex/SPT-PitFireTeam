using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace pitTeam.Modules
{
    /// <summary>Wait for the native process to unlock and verify the actual result, not just callback success.</summary>
    internal static class GearSwapHandsTransition
    {
        private sealed class Outcome<T>
        {
            internal T Value;
            internal bool Success;
            internal string Error;
        }

        internal static async Task Run<T>(Func<bool> alive, Func<bool> idle,
            Action<Action<T, bool, string>> begin, Func<T, bool> matches, Action<int> mismatch,
            int attempts = 3, int timeoutMs = 10000)
        {
            var elapsed = Stopwatch.StartNew();
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                await WaitForIdle(alive, idle, elapsed, timeoutMs);
                // EFT calls completion before ExecuteNext clears ProcessStatus. Never run the
                // caller's continuation inline within that callback. Await retains Unity's context.
                var completion = new TaskCompletionSource<Outcome<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
                begin((value, success, error) => completion.TrySetResult(new Outcome<T>
                    { Value = value, Success = success, Error = error }));
                int remaining = Math.Max(1, timeoutMs - (int)elapsed.ElapsedMilliseconds);
                if (await Task.WhenAny(completion.Task, Task.Delay(remaining)) != completion.Task)
                    throw new TimeoutException("Hands callback did not complete before the exchange deadline.");
                Outcome<T> outcome = await completion.Task;
                await WaitForIdle(alive, idle, elapsed, timeoutMs);
                if (!outcome.Success) throw new InvalidOperationException("Hands transition failed: " + outcome.Error);
                if (matches(outcome.Value)) return;
                mismatch?.Invoke(attempt);
            }
            throw new InvalidOperationException("Hands transition returned the wrong controller/item after bounded retries.");
        }

        private static async Task WaitForIdle(Func<bool> alive, Func<bool> idle, Stopwatch elapsed, int timeoutMs)
        {
            while (true)
            {
                if (!alive()) throw new OperationCanceledException("Actor died during hands transition.");
                if (elapsed.ElapsedMilliseconds >= timeoutMs)
                    throw new TimeoutException("Native hands process did not unlock before the exchange deadline.");
                if (idle()) return;
                await Task.Delay(10);
            }
        }
    }
}

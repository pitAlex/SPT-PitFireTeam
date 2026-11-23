using System;
using System.Threading.Tasks;

namespace pitTeam.Modules
{
    internal static class FollowerSpawnReadiness
    {
        internal static async Task AwaitSignal(Task signal, Func<bool> valid, Func<Task> poll,
            Func<bool> canComplete = null, Action onPending = null)
        {
            while (true)
            {
                if (signal.IsFaulted || signal.IsCanceled) await signal;
                if (!valid()) throw new OperationCanceledException("Follower spawn no longer belongs to a live raid or actor");
                if (signal.IsCompleted)
                {
                    if (canComplete == null || canComplete()) return;
                    // Registration can finish before the raid is ready for the combat handoff.
                    await poll();
                    continue;
                }

                onPending?.Invoke();
                await Task.WhenAny(signal, poll());
            }
        }

        internal static async Task Wait(Func<bool> valid, Func<bool> ready, Func<Task> poll, int attempts = 100)
        {
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                if (!valid()) throw new InvalidOperationException("Follower disappeared before combat readiness");
                if (ready()) return;
                await poll();
            }
            throw new TimeoutException("Follower combat readiness timed out");
        }
    }
}

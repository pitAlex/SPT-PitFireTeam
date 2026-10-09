using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace pitTeam.Modules
{
    /// <summary>Serialize native body loads and hands restoration, retaining Unity's context.</summary>
    internal static class GearSwapBodyRefresh
    {
        internal static async Task Run(Func<bool> alive, Func<Task[]> loadingJobs,
            Func<Task> restoreHands, Action verify, Action<string> trace, int timeoutMs = 10000)
        {
            Exception loadingError = null;
            trace?.Invoke("body-refresh-wait");
            try
            {
                await Wait(alive, loadingJobs, timeoutMs);
                trace?.Invoke("body-refresh-ready");
            }
            catch (Exception ex) { loadingError = ex; }

            // A visual-load failure must not strand an otherwise living actor in empty hands.
            // Preserve the failure so recovery cannot subsequently be reported as full success.
            await restoreHands();
            if (loadingError != null) ExceptionDispatchInfo.Capture(loadingError).Throw();

            trace?.Invoke("body-refresh-after-hands");
            await Wait(alive, loadingJobs, timeoutMs);
            verify();
            trace?.Invoke("body-refresh-verified");
        }

        private static async Task Wait(Func<bool> alive, Func<Task[]> loadingJobs, int timeoutMs)
        {
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                if (!alive()) throw new OperationCanceledException("Actor died during body refresh.");
                if (elapsed.ElapsedMilliseconds >= timeoutMs)
                    throw new TimeoutException("Native body models did not finish loading before the exchange deadline.");
                // Re-read every turn: equipment/child events can replace a slot's LoadingJob.
                // Native cancellation is normal when a newer model supersedes an older one.
                Task[] jobs = loadingJobs().Where(job => job != null).ToArray();
                if (jobs.All(job => job.IsCompleted))
                {
                    foreach (Task job in jobs.Where(job => !job.IsCanceled)) await job;
                    return;
                }
                await Task.Delay(10);
            }
        }
    }
}

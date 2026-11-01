using System;
using System.Threading.Tasks;

namespace pitTeam.Modules
{
    internal static class FollowerSpawnReadiness
    {
        internal static async Task<T> Registration<T>(Task<T> registration, TimeSpan timeout,
            Func<bool> valid, Func<bool> started, Func<Task> poll) where T : class
        {
            // Native pre-created bots stay NonActive until the raid's CheckActivation.
            // Loading time cannot consume the active-bot registration deadline.
            while (true)
            {
                if (!valid()) throw new OperationCanceledException("Raid ended before follower registration");
                if (registration.IsFaulted || registration.IsCanceled) return await registration;
                if (started()) break;
                await poll();
            }
            return await Registration(registration, timeout);
        }

        internal static async Task<T> Registration<T>(Task<T> registration, TimeSpan timeout) where T : class
        {
            if (await Task.WhenAny(registration, Task.Delay(timeout)) != registration)
                throw new TimeoutException("Follower registration timed out");
            return await registration ?? throw new InvalidOperationException("Follower registration failed");
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

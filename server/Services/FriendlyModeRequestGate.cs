namespace pitTeam.Server.Services;

// Mode changes and roster requests share one gate so a request cannot read from one
// database and then write its result into the other after an intervening mode change.
public static class FriendlyModeRequestGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HashSet<string> ActiveRaids = new(StringComparer.Ordinal);
    public static long Generation { get; private set; }
    public static void ModeChanged() => Generation++;

    public static async ValueTask<string> Run(Func<ValueTask<string>> action)
    {
        await Gate.WaitAsync();
        try { return await action(); }
        finally { Gate.Release(); }
    }

    public static async Task RunIfCurrent(long generation, Func<Task> action)
    {
        await Run(async () =>
        {
            if (generation == Generation) await action();
            return string.Empty;
        });
    }

    // Called only while holding the route gate.
    public static bool HasActiveRaid => ActiveRaids.Count > 0;
    public static void StartRaid(string sessionId) => ActiveRaids.Add(sessionId);
    public static void EndRaid(string sessionId) => ActiveRaids.Remove(sessionId);
}

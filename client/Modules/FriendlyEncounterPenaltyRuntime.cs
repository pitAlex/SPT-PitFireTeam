using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using pitTeam.Shared;

namespace pitTeam.Modules
{
    internal static class FriendlyEncounterPenaltyRuntime
    {
        private const string StatusRoute = "/singleplayer/pitfireteam/friendly-encounter-penalties";
        private static readonly object Sync = new object();
        private static readonly SemaphoreSlim Requests = new SemaphoreSlim(1, 1);
        private static List<FriendlyEncounterPenaltyEntry> entries = new List<FriendlyEncounterPenaltyEntry>();
        private static readonly Dictionary<string, FriendlyEncounterPenaltyEntry> Pending = new Dictionary<string, FriendlyEncounterPenaltyEntry>();
        private static long clockOffsetMs;
        private static int version;
        internal static string RaidId { get; private set; } = Guid.NewGuid().ToString("N");
        internal static int Version { get { lock (Sync) return version; } }
        internal static long NowUnixMs { get { lock (Sync) return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + clockOffsetMs; } }

        internal static void BeginRaid()
        {
            RaidId = Guid.NewGuid().ToString("N");
            ReloadAsync().GetAwaiter().GetResult();
        }

        internal static int GetPoints()
        {
            if (!GameplayModeRuntime.IsAllegiance) return 0;
            lock (Sync) return FriendlyEncounterPenaltyPolicy.GetPoints(entries, NowUnixMs);
        }

        internal static List<FriendlyEncounterPenaltyEntry> GetActiveEntries()
        {
            lock (Sync) return entries.Where(entry => entry.ExpiresAtUnixMs > NowUnixMs).ToList();
        }

        internal static async Task ReloadAsync()
        {
            await Requests.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!GameplayModeRuntime.IsAllegiance)
                {
                    lock (Sync) { entries.Clear(); version++; }
                    return;
                }
                ApplyResponse(await Task.Run(() => RequestHandler.GetJson(StatusRoute)).ConfigureAwait(false));
            }
            catch (Exception ex) { Logger.LogError("[FriendlyEncounters] Failed to load penalties: " + ex); }
            finally { Requests.Release(); }
        }

        internal static Task SendKillReport(string route, string json, string victimId, long killedAtUnixMs, string raidId)
        {
            string id = raidId + "/" + victimId;
            bool penalized = killedAtUnixMs > 0;
            if (penalized)
            {
                lock (Sync)
                {
                    var entry = new FriendlyEncounterPenaltyEntry { Id = id, ExpiresAtUnixMs = killedAtUnixMs + FriendlyEncounterPenaltyPolicy.DurationMs };
                    Pending[id] = entry;
                    entries.Add(entry);
                    version++;
                }
            }
            // Register before starting the worker, so mode switching cannot overtake it.
            return GameplayModeRuntime.RunRosterRequest(() =>
            {
                Requests.Wait();
                try
                {
                    for (int attempt = 0; ; attempt++)
                    {
                        try
                        {
                            string response = RequestHandler.PostJson(route, json);
                            var body = JObject.Parse(response);
                            if (body["err"]?.Value<int?>() != 0) throw new InvalidOperationException("Kill report rejected.");
                            if (penalized)
                            {
                                lock (Sync) Pending.Remove(id);
                                ApplyResponse(response);
                            }
                            return;
                        }
                        catch when (attempt < 2) { Task.Delay(250 * (attempt + 1)).GetAwaiter().GetResult(); }
                    }
                }
                catch (Exception ex) { Logger.LogError("[FriendlyEncounters] Failed to save kill report for " + victimId + ": " + ex); }
                finally { Requests.Release(); }
            });
        }

        private static void ApplyResponse(string response)
        {
            var body = JObject.Parse(response);
            if (body["err"]?.Value<int?>() != 0) throw new InvalidOperationException("Penalty response rejected.");
            var state = (body["data"] ?? body).ToObject<FriendlyEncounterPenaltyState>();
            if (state?.Entries == null || state.ServerNowUnixMs <= 0) throw new InvalidOperationException("Invalid penalty state.");
            lock (Sync)
            {
                clockOffsetMs = state.ServerNowUnixMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                entries = state.Entries;
                foreach (var pending in Pending.Values)
                    if (!entries.Any(entry => entry.Id == pending.Id)) entries.Add(pending);
                version++;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using EFT;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace pitTeam.Modules
{
    // Captured on the game thread; background sends never consult the next raid's mutable state.
    internal static class FollowerInsuranceRaidReports
    {
        private static readonly object Gate = new object();
        private static RaidReports Current;

        internal static string CurrentServerId { get { lock (Gate) return Current?.ServerId; } }

        internal sealed class RaidReports
        {
            internal string ServerId;
            internal bool Sealed;
            internal bool Failed;
            internal readonly List<Report> Reports = new List<Report>();
        }

        internal sealed class Report
        {
            internal RaidReports Raid;
            internal string Id;
            internal string Json;
            internal readonly TaskCompletionSource<bool> Finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            internal string Send(string route)
            {
                try
                {
                    string response = RequestHandler.PostJson(route, Json);
                    var body = JObject.Parse(response);
                    if (body["err"]?.Value<int?>() != 0)
                        throw new InvalidOperationException("Post-raid report response was not successful.");
                    Finished.TrySetResult(true);
                    return response;
                }
                catch
                {
                    lock (Gate) { if (Raid != null) Raid.Failed = true; }
                    Finished.TrySetResult(false);
                    throw;
                }
            }
        }

        internal static void Reset() { lock (Gate) Current = null; }

        internal static void Begin(LocalGame game)
        {
            try
            {
                var settings = game == null ? null : AccessTools.Field(game.GetType(), "_raidSettings")?.GetValue(game) as LocalRaidSettings;
                string id = settings?.serverId;
                if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Local raid serverId unavailable.");
                lock (Gate)
                {
                    if (Current?.ServerId == id) return;
                    Current = new RaidReports { ServerId = id };
                }
                Logger.LogInfo($"[FollowerInsurance:ReportStart] serverId='{id}'");
            }
            catch (Exception ex) { Logger.LogError($"[FollowerInsurance:ReportStart] {ex}"); }
        }

        internal static Report Prepare(string json)
        {
            // Register before Task.Run, so teardown sees even sends that have not started yet.
            lock (Gate)
            {
                var report = new Report { Raid = Current, Id = Guid.NewGuid().ToString("N"), Json = json };
                if (Current == null) return report; // Legacy/uninitialized observation cannot pass the server barrier.
                if (Current.Sealed) Current.Failed = true;
                Current.Reports.Add(report);
                try
                {
                    using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
                    var body = JObject.Load(reader);
                    body["InsuranceServerId"] = Current.ServerId;
                    body["InsuranceReportId"] = report.Id;
                    report.Json = body.ToString(Formatting.None);
                }
                catch (Exception ex)
                {
                    Current.Failed = true;
                    Logger.LogError($"[FollowerInsurance:ReportPrepare] {ex}");
                }
                return report;
            }
        }

        internal static void Fail() { lock (Gate) { if (Current != null) Current.Failed = true; } }

        internal static void Complete()
        {
            RaidReports raid;
            Report[] reports;
            lock (Gate)
            {
                raid = Current;
                if (raid == null || raid.Sealed) return;
                raid.Sealed = true;
                reports = raid.Reports.ToArray();
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    // Never wait on the Unity thread. A stalled report keeps the server unresolved.
                    await Task.WhenAll(reports.Select(report => report.Finished.Task));
                    bool failed;
                    lock (Gate) failed = raid.Failed || raid.Reports.Count != reports.Length;
                    string json = JsonConvert.SerializeObject(new
                    {
                        InsuranceServerId = raid.ServerId,
                        ReportIds = reports.Select(report => report.Id).ToArray(),
                        Failed = failed
                    });
                    RequestHandler.PostJson("/singleplayer/pitfireteam/insurance/raid-reports-complete", json);
                    Logger.LogInfo($"[FollowerInsurance:ReportsComplete] serverId='{raid.ServerId}' reports={reports.Length} failed={failed}");
                }
                catch (Exception ex) { Logger.LogError($"[FollowerInsurance:ReportsComplete] {ex}"); }
            });
        }
    }
}

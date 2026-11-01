using Newtonsoft.Json.Linq;
using pitTeam.GoingRogue;
using pitTeam.Modules;

internal static class SpawnPreflightTests
{
    internal static async Task Run(Action<bool, string> check)
    {
        JObject Row(string role = "bossKnight") => new()
        {
            ["Aid"] = "knight", ["Role"] = role, ["Tactic"] = "Rifleman", ["Aggression"] = 0,
            ["SelfDetermination"] = 90, ["Proficiency"] = new JObject
            {
                ["VisionDistance"] = 125, ["VisionSpeed"] = 75, ["Accuracy"] = 140, ["AimSpeed"] = 100
            }
        };
        string Json(JObject row) => new JArray(row).ToString();
        void Rejected(Action action, string reason)
        {
            try { action(); } catch { check(true, reason); return; }
            check(false, reason);
        }
        var state = new RogueSelectionState();
        state.Begin(new[] { "knight" });
        check(state.Unresolved && state.HasGoons && state.HasRogues, "unresolved selection closes Goons/allegiance gates");
        foreach (string failed in new[] { "broken", "[]", "{\"err\":1,\"data\":[]}", "{\"data\":{}}" })
        {
            Rejected(() => state.Resolve(failed), "invalid roster cannot publish an empty selection");
            check(state.Unresolved && state.HasGoons, "failed roster keeps hostile Goons blocked");
        }
        state.Resolve(Json(Row()));
        check(!state.Unresolved && state.HasGoons, "successful retry confirms selected Knight");
        state.Begin(new[] { "knight" }); state.Resolve(Json(Row("pmcUSEC")));
        check(!state.Unresolved && !state.HasGoons && !state.HasRogues, "validated ordinary teammate releases conservative selection gates");
        state.Begin(new[] { "knight" }); state.Resolve(Json(Row("exUsec")));
        check(!state.HasGoons && state.HasRogues, "exUsec is distinct from Goons selection");
        state.Begin(Array.Empty<string>());
        check(!state.Unresolved && !state.HasGoons && !state.HasRogues, "new empty selection clears previous raid state");

        var snapshot = FollowerSpawnMetadata.Read(Json(Row()), new[] { "knight" }, true);
        check(snapshot["knight"]["Aggression"].Value<float>() == 0 && snapshot["knight"]["SelfDetermination"].Value<float>() == 90 &&
            snapshot["knight"]["Proficiency"]["Accuracy"].Value<float>() == 140, "validated snapshot preserves saved values including zero");
        foreach (string key in new[] { "Role", "Tactic", "Aggression", "SelfDetermination", "Proficiency" })
        {
            var row = Row(); row.Remove(key);
            Rejected(() => FollowerSpawnMetadata.Read(Json(row), new[] { "knight" }, true), "missing " + key + " cannot fall back to defaults");
        }
        foreach (string key in new[] { "VisionDistance", "VisionSpeed", "Accuracy", "AimSpeed" })
        {
            var row = Row(); ((JObject)row["Proficiency"]).Remove(key);
            Rejected(() => FollowerSpawnMetadata.Read(Json(row), new[] { "knight" }, true), "partial proficiency rejected: " + key);
        }
        foreach (double value in new[] { -1d, 201d, double.NaN, double.PositiveInfinity })
        {
            var row = Row(); row["Proficiency"]["Accuracy"] = value;
            Rejected(() => FollowerSpawnMetadata.Read(Json(row), new[] { "knight" }, true), "invalid proficiency rejected");
        }
        var duplicate = new JArray(Row(), Row()).ToString();
        Rejected(() => FollowerSpawnMetadata.Read(duplicate, new[] { "knight" }, true), "duplicate selected metadata rejected");
        Rejected(() => FollowerSpawnMetadata.Read(Json(Row()), new[] { "missing" }, true), "missing selected metadata rejected");
        var lower = JObject.Parse(Row().ToString().ToLowerInvariant());
        check(FollowerSpawnMetadata.Read(Json(lower), new[] { "knight" }, true).Count == 1, "wire property casing is supported");

        async Task Fails(Func<Task> action, string reason)
        {
            try { await action(); } catch { check(true, reason); return; }
            check(false, reason);
        }
        var registered = new object();
        check(ReferenceEquals(await FollowerSpawnReadiness.Registration(Task.FromResult(registered), TimeSpan.FromSeconds(1)), registered),
            "registration preserves the real follower result");
        await Fails(() => FollowerSpawnReadiness.Registration(Task.FromResult<object>(null), TimeSpan.FromSeconds(1)), "null registration cannot announce Ready");
        await Fails(() => FollowerSpawnReadiness.Registration(Task.FromException<object>(new Exception("init failed")), TimeSpan.FromSeconds(1)), "failed initialization propagates to rollback");
        await Fails(() => FollowerSpawnReadiness.Registration(new TaskCompletionSource<object>().Task, TimeSpan.Zero), "missing active-bot callback times out");
        int loadingPolls = 0;
        var loadingRegistration = new TaskCompletionSource<object>();
        var afterLoading = await FollowerSpawnReadiness.Registration(loadingRegistration.Task, TimeSpan.Zero,
            () => true, () => loadingPolls >= 480, () =>
            {
                loadingPolls++;
                if (loadingPolls == 480) loadingRegistration.SetResult(registered);
                return Task.CompletedTask;
            });
        check(loadingPolls == 480 && ReferenceEquals(afterLoading, registered),
            "48 seconds of simulated loading cannot consume the registration timeout");
        loadingPolls = 0;
        await FollowerSpawnReadiness.Registration(Task.FromResult(registered), TimeSpan.Zero,
            () => true, () => loadingPolls == 2, () => { loadingPolls++; return Task.CompletedTask; });
        check(loadingPolls == 2, "early registration still waits for native raid start before combat readiness");
        await Fails(() => FollowerSpawnReadiness.Registration(new TaskCompletionSource<object>().Task, TimeSpan.Zero,
            () => true, () => true, () => throw new Exception("unexpected loading poll")),
            "missing callback remains bounded once the raid has started");
        loadingPolls = 0;
        await Fails(() => FollowerSpawnReadiness.Registration(new TaskCompletionSource<object>().Task, TimeSpan.Zero,
            () => loadingPolls < 2, () => false, () => { loadingPolls++; return Task.CompletedTask; }),
            "raid teardown cancels registration while loading");
        check(loadingPolls == 2, "canceled loading does not keep polling an old raid");
        loadingPolls = 0;
        await Fails(() => FollowerSpawnReadiness.Registration(Task.FromException<object>(new Exception("init failed")), TimeSpan.Zero,
            () => true, () => false, () => { loadingPolls++; return Task.CompletedTask; }),
            "initialization failure propagates during loading");
        check(loadingPolls == 0, "known initialization failure does not wait for raid start");
        await Fails(() => FollowerSpawnReadiness.Registration(Task.FromCanceled<object>(new CancellationToken(true)), TimeSpan.Zero,
            () => true, () => false, () => throw new Exception("unexpected loading poll")),
            "canceled native registration propagates during loading");
        int polls = 0;
        await FollowerSpawnReadiness.Wait(() => true, () => polls == 2, () => { polls++; return Task.CompletedTask; });
        check(polls == 2, "readiness waits for the actual combat claim");
        polls = 0;
        await Fails(() => FollowerSpawnReadiness.Wait(() => true, () => false, () => { polls++; return Task.CompletedTask; }, 3), "missing combat claim times out");
        check(polls == 3, "combat wait is bounded");
        bool probed = false;
        await Fails(() => FollowerSpawnReadiness.Wait(() => false, () => { probed = true; return true; }, () => Task.CompletedTask), "removed/dead follower cannot become ready");
        check(!probed, "invalid registration is rejected before probing combat readiness");
    }
}

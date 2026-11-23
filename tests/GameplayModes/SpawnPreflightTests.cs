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
        int loadingPolls = 0, pendingChecks = 0;
        var loadingRegistration = new TaskCompletionSource<object>();
        await FollowerSpawnReadiness.AwaitSignal(loadingRegistration.Task, () => true, () =>
            {
                loadingPolls++;
                if (loadingPolls == 680) loadingRegistration.SetResult(registered);
                return Task.CompletedTask;
            }, () => loadingPolls >= 480, () => pendingChecks++);
        check(loadingPolls == 680 && pendingChecks == 680 && ReferenceEquals(await loadingRegistration.Task, registered),
            "48 seconds of loading and 20 seconds of activation can still produce the actual follower");

        loadingPolls = 0;
        await FollowerSpawnReadiness.AwaitSignal(Task.FromResult(registered), () => true,
            () => { loadingPolls++; return Task.CompletedTask; }, () => loadingPolls == 2);
        check(loadingPolls == 2, "early registration still waits for native raid start before combat readiness");

        foreach (bool started in new[] { false, true })
        {
            loadingPolls = 0;
            await Fails(() => FollowerSpawnReadiness.AwaitSignal(new TaskCompletionSource<object>().Task,
                () => loadingPolls < 200, () => { loadingPolls++; return Task.CompletedTask; }, () => started),
                "invalid raid or actor cancels an unfinished spawn");
            check(loadingPolls == 200, "pending registration polls until the raid or actor becomes invalid");
        }

        loadingPolls = 0;
        await Fails(() => FollowerSpawnReadiness.AwaitSignal(Task.FromException(new Exception("init failed")),
            () => true, () => { loadingPolls++; return Task.CompletedTask; }, () => false),
            "initialization failure propagates during loading");
        check(loadingPolls == 0, "known initialization failure does not wait for raid start");
        await Fails(() => FollowerSpawnReadiness.AwaitSignal(Task.FromCanceled(new CancellationToken(true)),
            () => true, () => throw new Exception("unexpected loading poll"), () => false),
            "canceled native registration propagates during loading");

        var nativeFailure = new InvalidOperationException("activation failed");
        var activation = new TaskCompletionSource<object>();
        var poll = new TaskCompletionSource<object>();
        var waiting = FollowerSpawnReadiness.AwaitSignal(activation.Task, () => true, () => poll.Task, () => true);
        activation.SetException(nativeFailure);
        var winner = await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(1)));
        check(ReferenceEquals(waiting, winner) && !poll.Task.IsCompleted,
            "native failure wakes the lifecycle wait without waiting for the next poll");
        try { await waiting; check(false, "activation failure must be reported"); }
        catch (InvalidOperationException ex) { check(ReferenceEquals(ex, nativeFailure), "the original pending-stage exception propagates"); }

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

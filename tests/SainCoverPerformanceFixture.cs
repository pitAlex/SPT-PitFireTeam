using System;
using pitTeam.SAINAddon;
using SAIN.SAINComponent.SubComponents.CoverFinder;
using UnityEngine;

public static partial class CombatChecks
{
    private static void TestCoverPlanningBudget()
    {
        foreach (float step in new[] { 0.05f, 0.3f })
        {
            Time.time += 10;
            var b = CoverBot("forwardBudget" + step);
            b.Sain.GoalEnemy.KnownPlaces.LastKnownPosition = new Vector3(100, 0, 0);
            var finder = new SAINFollowerCoverFinder(b.Sain);
            for (int i = 0; i < 32; i++)
                SainBotCoverData.Scene.Add(new SainBotColliderData { Collider = new Collider { Point = CoverAt(10 + i * .4f) } });
            Physics.Blocked = false;
            int rays = Physics.LinecastCalls, polls = 0, found;
            do
            {
                Time.time += step;
                int before = Physics.LinecastCalls + CoverAnalyzer.Creates + CoverAnalyzer.Rechecks;
                found = finder.FindForward(b.Sain.GoalEnemy).Count;
                if (finder.Pending) finder.FindForward(b.Sain.GoalEnemy); // Same-frame pending polls share the budget.
                Check(Physics.LinecastCalls + CoverAnalyzer.Creates + CoverAnalyzer.Rechecks - before <= 4,
                    "forward cover combined probe budget: " + step + "/" + polls);
                polls++;
            } while (finder.Pending && polls < 40);
            Check(!finder.Pending && found == 32, "forward scan completes without slow-poll starvation: " + step);
            Check(Physics.LinecastCalls - rays == 32, "forward scan checks each firing lane once: " + step);
            Console.WriteLine("PERF forward32 step=" + step + " rays=" + (Physics.LinecastCalls - rays) + " polls=" + polls);
        }

        Time.time += 10;
        var single = CoverBot("planningLaneInvalidation");
        single.Sain.GoalEnemy.KnownPlaces.LastKnownPosition = new Vector3(100, 0, 0);
        var cover = CoverAt(15); single.Sain.Cover.CoverPoints.Add(cover);
        var lane = new SAINFollowerCoverFinder(single.Sain);
        Physics.Blocked = true;
        Check(lane.FindForward(single.Sain.GoalEnemy).Count == 0, "blocked planning lane rejected");
        int count = Physics.LinecastCalls;
        lane.FindForward(single.Sain.GoalEnemy);
        Check(Physics.LinecastCalls == count, "negative firing lane reused on unchanged poll");
        Time.time += 1.1f; Physics.Blocked = false;
        Check(lane.FindForward(single.Sain.GoalEnemy).Count == 1 && Physics.LinecastCalls == count + 1,
            "completed planning lane expires and can become clear");
        Time.time += .05f; cover.Position += new Vector3(.2f, 0, 0); count = Physics.LinecastCalls;
        lane.FindForward(single.Sain.GoalEnemy);
        Check(Physics.LinecastCalls == count + 1, "moved cover invalidates planning ray");
        Time.time += .05f; single.Sain.GoalEnemy.KnownPlaces.LastKnownPosition += new Vector3(.2f, 0, 0); count = Physics.LinecastCalls;
        lane.FindForward(single.Sain.GoalEnemy);
        Check(Physics.LinecastCalls == count + 1, "changed threat invalidates planning ray");
        lane.Clear(); Time.time += .05f; count = Physics.LinecastCalls; lane.FindForward(single.Sain.GoalEnemy);
        Check(Physics.LinecastCalls == count + 1, "finder clear discards planning cache");

        Time.time += 10;
        var regroup = CoverBot("regroupRouteBudget");
        regroup.Sain.GoalEnemy.KnownPlaces.LastKnownPosition = new Vector3(100, 0, 0);
        regroup.Sain.GoalEnemy.IsVisible = regroup.Sain.GoalEnemy.CanShoot = false;
        pitTeam.Modules.CombatDistanceConfiguration.Instance.Trigger = 18;
        var policy = SAINFollowerRuntime.GetCover(regroup); policy.RegroupCompleted(18);
        for (int i = 0; i < 32; i++)
            SainBotCoverData.Scene.Add(new SainBotColliderData { Collider = new Collider { Point = CoverAt(25 + i * .3f) } });
        pitTeam.Utils.Utils.PathComplete = false;
        int paths = pitTeam.Utils.Utils.PathCalls, max = 0;
        for (int i = 0; i < 40 && pitTeam.Utils.Utils.PathCalls - paths < 32; i++)
        {
            Time.time += .05f;
            int before = pitTeam.Utils.Utils.PathCalls, probes = before + CoverAnalyzer.Creates + CoverAnalyzer.Rechecks;
            Check(policy.TrySelect(false, out var selected) && selected == null, "pending regroup preserves local selection ownership");
            policy.TrySelect(false, out _);
            max = Math.Max(max, pitTeam.Utils.Utils.PathCalls - before);
            Check(pitTeam.Utils.Utils.PathCalls + CoverAnalyzer.Creates + CoverAnalyzer.Rechecks - probes <= 4,
                "regroup routes share discovery and validation frame budget");
        }
        Check(pitTeam.Utils.Utils.PathCalls - paths == 32 && max <= 4, "regroup checks 32 failed routes without a one-frame spike");
        paths = pitTeam.Utils.Utils.PathCalls;
        Time.time += .5f; policy.TrySelect(false, out _);
        Check(pitTeam.Utils.Utils.PathCalls == paths, "unchanged regroup retry does not repeat all failed routes");
        Console.WriteLine("PERF regroup32 maxRoutesPerPoll=" + max + " unchangedRetryRoutes=" + (pitTeam.Utils.Utils.PathCalls - paths));
        pitTeam.Utils.Utils.PathComplete = true;
        Time.time += 2f;
        bool recovered = false;
        for (int i = 0; i < 32 && !recovered; i++)
        { Time.time += .05f; policy.TrySelect(false, out var selected); recovered = selected != null; }
        Check(recovered, "failed regroup routes expire and a recovered route can be selected");
        SainBotCoverData.Scene.Clear();

        Time.time += 10;
        var route = new SAINFollowerCoverFinder(single.Sain);
        var point = CoverAt(30); var boss = new Vector3(40, 0, 0);
        pitTeam.Utils.Utils.PathScale = 1.5f;
        Check(route.InsideBossRoute(point, boss, 16), "valid player route admitted");
        paths = pitTeam.Utils.Utils.PathCalls;
        Check(!route.InsideBossRoute(point, boss, 14) && pitTeam.Utils.Utils.PathCalls == paths,
            "radius changes use cached distance with current limit");
        pitTeam.Utils.Utils.PathScale = 1f;
        Time.time += .05f;
        route.InsideBossRoute(point, boss + new Vector3(.2f, 0, 0), 12);
        Check(pitTeam.Utils.Utils.PathCalls == paths + 1, "player movement invalidates cached route");
        Time.time += .05f; point.Position += new Vector3(.2f, 0, 0);
        route.InsideBossRoute(point, boss + new Vector3(.2f, 0, 0), 12);
        Check(pitTeam.Utils.Utils.PathCalls == paths + 2, "cover movement invalidates cached player route");
    }
}

using EFT;
using pitTeam.SAINAddon;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes;
using UnityEngine;

public static partial class CombatChecks
{
    private static BotOwner ArrivedRegroup(string id)
    {
        Time.time += 10;
        var b = CoverBot(id);
        RegroupDecision(b);
        var objective = SAINFollowerRuntime.GetRegroup(b);
        b.GetPlayer.Position = new Vector3(34,0,0);
        Time.time += 1; objective.Observe();
        b.Sain.Cover.CoverInUse = CoverAt(20);
        Time.time += 2; objective.Observe();
        Check(!objective.Active && b.Sain.Cover.CoverInUse == null, "completed regroup releases pre-regroup native cover: " + id);
        b.Sain.Decision.CurrentSquadDecision = ESquadDecision.None;
        b.Sain.Decision.CurrentCombatDecision = ECombatDecision.SeekCover;
        return b;
    }

    private static void TestRegroupChurn()
    {
        var b = ArrivedRegroup("regroupCoverEnvelope");
        var old = CoverAt(20); var near = CoverAt(35);
        b.Sain.Cover.CoverPoints.Add(old); b.Sain.Cover.NativePoint = old;
        Check(b.Sain.Cover.FindForTest() == null && b.Sain.Cover.NativeCalls == 0,
            "completed regroup cannot fall back to old cover outside completion envelope");
        Time.time += 10;
        Check(b.Sain.Cover.FindForTest() == null, "post-regroup protection is geometric, not another cooldown");
        b.Sain.Cover.CoverPoints.Add(near);
        Check(b.Sain.Cover.FindForTest() == near, "post-regroup cover accepts safe player-local cover");
        Arrive(b,near); Time.time += 4;
        Check(!RegroupDecision(b), "accepted arrival cover cannot immediately retrigger distance regroup");

        var detour = ArrivedRegroup("regroupCoverRoute");
        var deceptive = CoverAt(30); detour.Sain.Cover.CoverPoints.Add(deceptive); detour.Sain.Cover.NativePoint=old;
        pitTeam.Utils.Utils.PathScale = 2;
        Check(detour.Sain.Cover.FindForTest() == null, "cover inside direct radius but outside route envelope is rejected");
        pitTeam.Utils.Utils.PathScale = 1; pitTeam.Utils.Utils.PathComplete = false;
        Check(detour.Sain.Cover.FindForTest() == null, "post-regroup cover requires a complete player route");
        pitTeam.Utils.Utils.PathComplete = true;
        deceptive.MovementAccepted = false;
        Check(detour.Sain.Cover.FindForTest() == null, "failed local movement cannot reopen outward native fallback");
        detour.Memory.IsUnderFire = true;
        Check(detour.Sain.Cover.FindForTest() == old, "incoming fire can still use urgent native cover outside envelope");
        detour.Memory.IsUnderFire = false; detour.Follower.CombatIndependent = true;
        Check(detour.Sain.Cover.FindForTest() == old, "On Your Own bypasses post-regroup cover constraint");

        var renewed = ArrivedRegroup("regroupRenewedContact");
        renewed.Sain.Cover.CoverPoints.Add(old); renewed.Sain.GoalEnemy.IsVisible=true; renewed.Sain.GoalEnemy.CanShoot=true;
        Check(renewed.Sain.Cover.FindForTest()==old, "renewed visible fire opportunity releases regroup-area preference");
        var moved = ArrivedRegroup("regroupNewAnchor"); moved.Sain.Cover.CoverPoints.Add(old);
        moved.Sain.GoalEnemy.KnownPlaces.LastKnownPosition = new Vector3(58,0,0);
        Check(moved.Sain.Cover.FindForTest()==old, "meaningfully changed enemy anchor permits fresh combat cover");
        var ordered = ArrivedRegroup("regroupNewOrder"); ordered.Sain.Cover.CoverPoints.Add(old);
        ordered.Follower.SetPushEnemy(20);
        Check(ordered.Sain.Cover.FindForTest()==old, "accepted Go Forward releases post-regroup cover constraint");
        var cleared = ArrivedRegroup("regroupClear"); cleared.Sain.Cover.CoverPoints.Add(old);
        SAINFollowerRuntime.GetCover(cleared).Clear();
        Check(cleared.Sain.Cover.FindForTest()==old, "combat release clears post-regroup selection state");

        var waiting = CoverBot("regroupPublicationWait");
        var layer = new SAINFollowerSquadCombatLayer(waiting,75); Tick();
        RegroupDecision(waiting); Check(layer.IsActive(), "active regroup owns squad layer");
        layer.GetNextAction();
        waiting.GetPlayer.Position = new Vector3(34,0,0);
        Time.time += 1; SAINFollowerRuntime.GetRegroup(waiting).Observe();
        Time.time += 2; SAINFollowerRuntime.GetRegroup(waiting).Observe();
        layer.NativeDecisionChanged = true;
        bool ended = false;
        for (int i=0;i<8;i++) ended |= layer.IsCurrentActionEnding();
        Check(!ended && !layer.NativeDecisionChanged, "completed regroup drains stale event and never restarts before publication");
        waiting.Sain.Decision.CurrentSelfDecision=ESelfActionType.FirstAid;
        Check(!layer.IsActive(), "medical selection still preempts waiting squad action");
        waiting.Sain.Decision.CurrentSelfDecision=ESelfActionType.None;
        waiting.Sain.Decision.CurrentSquadDecision=ESquadDecision.Suppress;
        layer.NativeDecisionChanged=true;
        Check(layer.IsCurrentActionEnding() && layer.GetNextAction().Type.Name=="SuppressAction", "new native squad publication ends wait and selects support");
        waiting.Sain.Decision.CurrentSquadDecision=ESquadDecision.None;
        Check(!layer.IsActive(), "native solo publication releases squad ownership");
    }
}

using System;
using EFT;
using UnityEngine;
using pitTeam.BigBrain;

namespace UnityEngine
{
    public static class Time { public static float time; }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); public static float Abs(float a) => Math.Abs(a); }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static float Distance(Vector3 a, Vector3 b) => (float)Math.Sqrt((a - b).sqrMagnitude);
    }
}
public enum BotLogicDecision { holdPosition, shootFromPlace, shootFromCover, goToPoint, runToCover, attackMoving, attackMovingWithSuppress, dogFight, heal }
public enum CustomBotDecisions { attackRetreat = 100 }
public enum EPhraseTrigger { Negative }
public enum EInteraction { NoGesture }
public class CoreActionResultParams { }
public struct AICoreActionResult<T, P>
{
    public T Action; public string Reason;
    public AICoreActionResult(T action, string reason) { Action = action; Reason = reason; }
}
public struct AICoreActionEnd
{
    public string Reason; public bool Value;
    public AICoreActionEnd(string reason, bool value) { Reason = reason; Value = value; }
}
namespace EFT
{
    public class EnemyInfo { public bool Alive = true, IsVisible, CanShoot; public string ProfileId = "woods-bear"; public Vector3 Position = new Vector3(40, 0, 0); }
    public class Memory { public EnemyInfo GoalEnemy; public bool IsUnderFire; }
    public class Talk { public void TrySay(EPhraseTrigger phrase, bool force) { } }
    public class Gesture { public void TryGestus(EInteraction gesture, bool force) { } }
    public class BotOwner { public Vector3 Position; public Memory Memory = new Memory(); public Talk BotTalk = new Talk(); public Gesture Gesture = new Gesture(); }
}
namespace pitTeam.Components { public class UnusedImport { } }
namespace pitTeam.Utils
{
    public static class Enemy { public enum EnemyDistance { Close } }
    public static class Utils { public static bool TryGetCompletePathDistance(Vector3 a, Vector3 b, out float distance) { distance = Vector3.Distance(a, b); return true; } }
}
namespace pitTeam.Modules
{
    public class CombatEvents { public class PushEvent { public Vector3 EnemyPosition, Destination; public string EnemyProfileId; } }
    public static class BattleRecorder { public static void RecordObjectiveDiagnostic(BotOwner owner, string objective, string action, string reason, Func<object> details = null) { } }
}
namespace pitTeam.BigBrain
{
    // Only the production objective is exercised here. Geometry, sensors, movement
    // execution and shared shot termination are controlled boundary stand-ins.
    internal class FollowerCombatCommon
    {
        private readonly BotOwner owner;
        private AICoreActionResult<BotLogicDecision, CoreActionResultParams> hold;
        internal bool HasPosition, CoverFire, Heal, DogFight, Hit, Arrived, PhysicalArrival = true;
        internal float Until;
        internal int PositionSets, PlannerCalls;
        internal FollowerCombatCommon(BotOwner owner) { this.owner = owner; }
        public static bool IsFinite(Vector3 p) => !float.IsNaN(p.x);
        public static bool WasHitRecently(BotOwner owner, float seconds) => Current.Hit;
        internal static FollowerCombatCommon Current;
        public static Vector3 GetEnemyCurrentPosition(EnemyInfo e) => e.Position;
        public bool HasActiveCombatEnemy(EnemyInfo e) => e?.Alive == true;
        public void TrySwitchBackToPrimaryAtRange(EnemyInfo e, pitTeam.Utils.Enemy.EnemyDistance distance) { }
        public AICoreActionResult<BotLogicDecision, CoreActionResultParams>? TryGetDogFightDecision() => DogFight ? new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.dogFight, "danger") : (AICoreActionResult<BotLogicDecision, CoreActionResultParams>?)null;
        public AICoreActionResult<BotLogicDecision, CoreActionResultParams>? TryGetNeedHealDecision() => Heal ? new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.heal, "heal") : (AICoreActionResult<BotLogicDecision, CoreActionResultParams>?)null;
        public AICoreActionResult<BotLogicDecision, CoreActionResultParams>? TryGetImmediateShootDecision(string reason) => owner.Memory.GoalEnemy?.CanShoot == true && owner.Memory.GoalEnemy.IsVisible ? new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromPlace, reason) : (AICoreActionResult<BotLogicDecision, CoreActionResultParams>?)null;
        public bool CanShootFromCurrentCover(out object target) { target = null; return CoverFire; }
        public bool CanShootFromCurrentCoverOrStandingIntent(out object target) => CanShootFromCurrentCover(out target);
        public void HandleSharedDecisionChanged(AICoreActionResult<BotLogicDecision, CoreActionResultParams> next) { }
        public void HandleCommittedCoverDecisionChanged(AICoreActionResult<BotLogicDecision, CoreActionResultParams> next) { }
        public bool ShouldCommitMovementDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> next, bool push) => next.Action == BotLogicDecision.runToCover;
        public void CommitMovement(AICoreActionResult<BotLogicDecision, CoreActionResultParams> next) { }
        public bool IsSameCommittedMovement(AICoreActionResult<BotLogicDecision, CoreActionResultParams> next) => false;
        public void ClearCommittedMovement() { }
        public void RefreshShootCover() { }
        public void ValidateCommittedCover() { }
        public void AssignCommittedCover() { }
        public bool HasCommittedCover() => false;
        public bool IsBotInCommittedCover() => false;
        public void ResetCommittedCover() { }
        public void ClearInitialDecision() { }
        public void ClearCommittedPosition() { HasPosition = false; }
        public void HoldFor(float seconds) { }
        public void SetCommittedPosition(Vector3 position, AICoreActionResult<BotLogicDecision, CoreActionResultParams> next, float duration) { HasPosition = true; hold = next; Until = Time.time + duration; PositionSets++; }
        public bool HasCommittedPosition(out AICoreActionResult<BotLogicDecision, CoreActionResultParams> next, bool deferCombatBreaks = false)
        {
            next = hold;
            return HasPosition && Time.time < Until && PhysicalArrival &&
                (deferCombatBreaks || owner.Memory.GoalEnemy?.IsVisible != true);
        }
        public AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> current) => owner.Memory.GoalEnemy?.CanShoot == true || CoverFire ? default : new AICoreActionEnd("enemyCannotShoot", true);
        public AICoreActionEnd EndRunToCover(string reason) => new AICoreActionEnd(Arrived ? "arrivedCommittedCover" : "travel", Arrived);
        public AICoreActionEnd EndGoToPoint(bool endWhenEnemyVisibleShootable) => new AICoreActionEnd(Arrived ? "arrivedAtPoint" : "travel", Arrived);
        public bool TryGetCommittedMovementDecision(EnemyInfo e, bool a, bool b, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> next) { next = default; return false; }
        public bool TryForceGoalEnemy(string id, string source, out EnemyInfo e) { e = owner.Memory.GoalEnemy; return e?.ProfileId == id; }
        public bool TryCommitSupportFiringCover(EnemyInfo e, string reason, out string coverReason, bool preferBackline, bool enforceMarksmanPositionPolicy) { PlannerCalls++; coverReason = reason; return true; }
        public AICoreActionResult<BotLogicDecision, CoreActionResultParams> CreateMoveToCommittedCoverDecision(string reason) => new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.runToCover, reason);
        public AICoreActionResult<BotLogicDecision, CoreActionResultParams> CreateCommittedCoverMoveDecision() => CreateMoveToCommittedCoverDecision("sniper.NeedSniper");
        public string LastSupportFiringCoverRejectReason => "none";
        public string LastSupportFiringPositionRejectReason => "none";
        public bool TryCreateFiringPositionDecisionAt(EnemyInfo e, Vector3 point, string reason, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> next, bool preferBackline, bool enforceMarksmanPositionPolicy, bool allowForwardPositions, bool allowBattlefieldPositions, float maxNavDistance) { next = default; return false; }
        public bool TryCreateSupportFiringPositionDecision(EnemyInfo e, Vector3 point, string reason, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> next, bool preferBackline, bool enforceMarksmanPositionPolicy, bool allowForwardPositions, bool allowBattlefieldPositions, float maxNavDistance) { next = default; return false; }
        public bool TryGetActivePushEvent(out pitTeam.Modules.CombatEvents.PushEvent push) { push = null; return false; }
        public bool TryGetAllyEngagementEnemy(out string id, out Vector3 position) { id = null; position = default; return false; }
        public bool TrySelectPreferredSupportEnemy(string id, Vector3 position, out EnemyInfo e, bool preferBackline, bool promoteSelected) { e = owner.Memory.GoalEnemy; return true; }
        public Vector3 GetBossPosition() => default;
    }
}
public static class NeedSniperArrivalChecks
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static FollowerCombatNeedSniperObjective Create(out BotOwner owner, out FollowerCombatCommon common)
    {
        Time.time = 100f;
        owner = new BotOwner(); owner.Memory.GoalEnemy = new EnemyInfo();
        common = new FollowerCombatCommon(owner); FollowerCombatCommon.Current = common;
        var objective = new FollowerCombatNeedSniperObjective(owner, common); objective.Activate(); return objective;
    }
    private static void Arrive(FollowerCombatNeedSniperObjective objective, BotOwner owner, FollowerCombatCommon common, bool point = false)
    {
        var move = objective.GetDecision(owner.Memory.GoalEnemy);
        Check(move.Action == BotLogicDecision.runToCover && !objective.IsComplete, "order commits movement");
        if (point) move = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.goToPoint, "sniper.NeedSniper.position");
        objective.DecisionChanged(null, move); common.Arrived = true; Time.time = 108f;
        Check(objective.ShallEndCurrentDecision(move).Value && common.HasPosition, "physical arrival arms hold");
        Check(common.Until == 110f, "original arrival deadline is two seconds");
    }
    public static int Run()
    {
        var objective = Create(out var owner, out var common); Arrive(objective, owner, common);
        var enemy = owner.Memory.GoalEnemy; enemy.IsVisible = enemy.CanShoot = true;
        var shot = objective.GetDecision(enemy); objective.DecisionChanged(null, shot);
        Check(shot.Action == BotLogicDecision.shootFromPlace && !objective.IsComplete, "first aiming opportunity does not complete arrival order");
        Time.time = 108.13f; enemy.CanShoot = false;
        Check(objective.ShallEndCurrentDecision(shot).Reason == "enemyCannotShoot", "lane loss retains shared shot safety");
        Check(!objective.IsComplete, "brief lane loss cannot yield order to regroup");
        var held = objective.GetDecision(enemy);
        Check(held.Reason == "sniper.NeedSniper.positionHold", "visible but unshootable enemy resumes arrival hold");
        Check(common.PlannerCalls == 1 && common.PositionSets == 1 && common.Until == 110f, "resumption neither replans nor rearms arrival");
        Check(!objective.ShallEndCurrentDecision(held).Value, "hold remains until original deadline");
        Time.time = 108.5f; enemy.CanShoot = true;
        Check(objective.ShallEndCurrentDecision(held).Reason == "needSniperShotReady" && !objective.IsComplete && common.HasPosition,
            "renewed shot preserves objective and position");
        shot = objective.GetDecision(enemy); objective.DecisionChanged(held, shot);
        Time.time = 109.9f; enemy.IsVisible = enemy.CanShoot = false;
        Check(objective.ShallEndCurrentDecision(shot).Value && !objective.IsComplete, "second contact loss remains bounded by same grace");
        held = objective.GetDecision(enemy);
        Check(common.Until == 110f && common.PositionSets == 1, "repeated flicker cannot extend deadline");
        Time.time = 110.01f;
        Check(objective.ShallEndCurrentDecision(held).Reason == "needSniperArrivedSettled" && objective.IsComplete && !common.HasPosition,
            "unproductive hold expires and ordinary routing can resume");

        objective = Create(out owner, out common); Arrive(objective, owner, common, point: true);
        common.CoverFire = true; shot = objective.GetDecision(owner.Memory.GoalEnemy);
        Check(shot.Action == BotLogicDecision.shootFromCover && !objective.IsComplete, "current-cover opportunity also preserves arrival");
        common.CoverFire = false; Time.time = 108.13f;
        Check(objective.ShallEndCurrentDecision(shot).Value && !objective.IsComplete, "cover lane loss stops shooting without discarding order");
        held = objective.GetDecision(owner.Memory.GoalEnemy);
        Check(held.Reason == "sniper.NeedSniper.positionHold", "point arrival resumes same hold");
        common.CoverFire = true; shot = objective.GetDecision(owner.Memory.GoalEnemy); Time.time = 111f;
        Check(!objective.ShallEndCurrentDecision(shot).Value, "productive fire may continue beyond arrival deadline");
        common.CoverFire = false;
        Check(objective.ShallEndCurrentDecision(shot).Value && objective.IsComplete, "lane loss after expiry releases order without new route");

        objective = Create(out owner, out common); owner.Memory.GoalEnemy.IsVisible = owner.Memory.GoalEnemy.CanShoot = true;
        Check(objective.GetDecision(owner.Memory.GoalEnemy).Action == BotLogicDecision.shootFromPlace && objective.IsComplete,
            "pre-arrival immediate fire retains existing completion");
        objective = Create(out owner, out common); Arrive(objective, owner, common); common.Heal = true;
        Check(objective.GetDecision(owner.Memory.GoalEnemy).Action == BotLogicDecision.heal && objective.IsComplete, "medical survival interrupts grace");
        objective = Create(out owner, out common); Arrive(objective, owner, common); common.DogFight = true;
        Check(objective.GetDecision(owner.Memory.GoalEnemy).Action == BotLogicDecision.dogFight && objective.IsComplete, "close danger interrupts grace");
        objective = Create(out owner, out common); Arrive(objective, owner, common); owner.Memory.IsUnderFire = true;
        Check(objective.GetDecision(owner.Memory.GoalEnemy).Reason == "sniper.NeedSniper.selfPreservation" && objective.IsComplete && !common.HasPosition,
            "incoming fire retains existing survival release");
        objective = Create(out owner, out common); Arrive(objective, owner, common); common.Hit = true;
        Check(objective.GetDecision(owner.Memory.GoalEnemy).Reason == "sniper.NeedSniper.selfPreservation" && objective.IsComplete && !common.HasPosition,
            "recent hit retains existing survival release");
        objective = Create(out owner, out common); Arrive(objective, owner, common); held = objective.GetDecision(owner.Memory.GoalEnemy);
        owner.Memory.GoalEnemy.Alive = false;
        Check(objective.ShallEndCurrentDecision(held).Reason == "needSniperEnemyMissing" && objective.IsComplete && !common.HasPosition, "target death immediately releases arrival");
        objective = Create(out owner, out common); Arrive(objective, owner, common); objective.Deactivate();
        Check(!common.HasPosition && !objective.IsComplete, "replacement command or release clears grace");
        Time.time = 120f; objective.Activate(); owner.Memory.GoalEnemy.IsVisible = owner.Memory.GoalEnemy.CanShoot = true;
        Check(objective.GetDecision(owner.Memory.GoalEnemy).Action == BotLogicDecision.shootFromPlace && objective.IsComplete,
            "next order cannot inherit prior arrival deadline");
        return checks;
    }
}

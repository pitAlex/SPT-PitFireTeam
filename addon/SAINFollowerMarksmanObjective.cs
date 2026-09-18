using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes;
using SAIN.SAINComponent.Classes.Decision;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace pitTeam.SAINAddon;

// Core Marksman intent, using only SAIN's existing firing-position finder and execution.
// Throttled native replanning with bounded failed positions; no parallel Core planner.
internal sealed class SAINFollowerMarksmanObjective(BotComponent bot, FiringPositionFinder finder)
{
    internal SainMarksmanWeaponBridge Weapons { get; } = new(bot);
    private bool automaticDestination, automaticMovementStarted;
    internal bool Preparing { get; private set; }
    private string? enemyId;
    private Vector3 anchor;
    private bool attempted;
    private float nextScanAt, nextRetryAt, orderedUntil;
    private readonly Vector3[] failedPositions = new Vector3[4];
    private int failedCount;
    private string rejection;
    private Vector3? lastCandidate;
    private bool ordered;
    private string reason = "idle";
    internal Vector3? Destination { get; private set; }
    internal bool OwnsMovement { get; private set; }
    internal bool Ordered => ordered && (Destination.HasValue || Time.time < orderedUntil);
    private BotFollowerPlayer? Follower => BossPlayers.Instance?.GetFollower(bot.BotOwner);
    private SAINFollowerEngageAttempt? Attempt => SAINFollowerRuntime.GetEngageAttempt(bot.BotOwner);

    internal void Observe()
    {
        if (Follower?.TryGetActiveCommand(out var command, out _) == true && command != FollowerCommandType.NeedSniper)
            Clear("replacementOrder");
    }

    internal bool Filter(Enemy enemy, ECombatDecision solo, ESquadDecision squad, ESelfActionType self,
        bool regrouping, out ECombatDecision result, out ESquadDecision squadResult)
    {
        result = solo; squadResult = squad;
        OwnsMovement = false; Preparing = false;
        if (enemy == null || !enemy.EnemyKnown || !Enemy.IsEnemyActive(enemy) ||
            enemy.EnemyPlayer?.HealthController?.IsAlive != true || (!enemy.LastKnownPosition.HasValue || !Finite(enemy.LastKnownPosition.Value)))
        { Clear("contactLost"); return false; }
        Vector3 known = enemy.LastKnownPosition.Value;
        if (enemyId != enemy.EnemyProfileId || (known - anchor).sqrMagnitude >= 64f)
        { Clear("newContact"); enemyId = enemy.EnemyProfileId; anchor = known; }

        Weapons.Maintain(enemy, automaticDestination);
        var follower = Follower;
        FollowerCommandType command = FollowerCommandType.None;
        bool hasOrder = follower?.TryGetActiveCommand(out command, out _) == true;
        if (hasOrder && command != FollowerCommandType.NeedSniper)
        { ordered = false; ReleaseDestination(); Attempt?.Pause(); return false; }

        // Native survival, medicine, useful fire and existing cover travel retain priority.
        if (Protected(solo, self) || regrouping || squad == ESquadDecision.Regroup || squad == ESquadDecision.Suppress)
        { Attempt?.Pause(); return false; }
        if (SainRegroupBridge.IsUnderFire(bot.BotOwner) || bot.Medical.TimeSinceShot < 1f ||
            bot.Memory.Health.HealthStatus == ETagStatus.BadlyInjured || bot.Memory.Health.HealthStatus == ETagStatus.Dying)
        {
            Attempt?.Pause();
            if (Pursuit(solo, squad)) { result = ECombatDecision.SeekCover; squadResult = ESquadDecision.None; return true; }
            return false;
        }
        if (enemy.IsVisible && enemy.CanShoot)
        {
            if (hasOrder) follower!.ClearCommand("SAIN:NeedSniper");
            Weapons.StopPreparing();
            ReleaseDestination(); attempted = false; ordered = false; failedCount = 0; nextRetryAt = 0f; finder.Clear(); Attempt?.Observe(enemy);
            if (Pursuit(solo, squad))
            { result = ECombatDecision.StandAndShoot; squadResult = ESquadDecision.None; return true; }
            return false;
        }
        if (Weapons.IsClose(enemy) && !bot.Mover.Moving && bot.Cover.CoverPoint_MovingTo == null)
        {
            int preparation = Weapons.Prepare();
            if (preparation == 0)
            { Preparing = true; result = ECombatDecision.StandAndShoot; squadResult = ESquadDecision.None; Attempt?.Pause(); Report("prepareDefensiveWeapon"); return true; }
        }
        if (SAINFollowerRuntime.GetCover(bot.BotOwner)?.HoldsArrival(enemy) == true ||
            (bot.Mover.Moving && !(bot.CurrentAction is SAINFollowerMoveToEngageAction && Destination.HasValue)) ||
            bot.Cover.CoverPoint_MovingTo != null)
        {
            Attempt?.Pause();
            if (Pursuit(solo, squad)) { result = ECombatDecision.SeekCover; squadResult = ESquadDecision.None; return true; }
            return false;
        }
        if (Attempt?.FailedFor(enemy) == true && Destination.HasValue)
        {
            RememberFailed(Destination.Value);
            ReleaseDestination(); attempted = true; ordered = false;
            nextRetryAt = Time.time + 4f;
            Report("attemptFailed");
        }

        if (hasOrder)
        {
            follower!.ClearCommand("SAIN:NeedSniper");
            ordered = true; orderedUntil = Time.time + 2.5f;
            nextRetryAt = 0f; // Explicit support can retry, but never bypass native scan cadence.
            Report("orderedSupport");
        }
        bool passiveCover = solo == ECombatDecision.SeekCover &&
            (bot.Cover.CoverSeekingState == ECoverSeekingState.NoCover || bot.Cover.CoverSeekingState == ECoverSeekingState.HoldInCover);
        bool proposesApproach = Pursuit(solo, squad);
        if (!Destination.HasValue && !Ordered && !proposesApproach && !passiveCover) return false;
        if (!Destination.HasValue && failedCount < failedPositions.Length &&
            Time.time >= nextRetryAt && (Ordered || enemy.Seen || enemy.Heard))
        {
            // Use the native provider's already-calculated candidate first. The public
            // finder is retried only on its bounded cadence at a planning boundary.
            Vector3? point = !attempted && solo == ECombatDecision.MoveToEngage && squad == ESquadDecision.None
                ? bot.Decision.EnemyDecisions.FiringPosition : null;
            if (!point.HasValue)
            {
                if (Time.time < nextScanAt)
                { result = ECombatDecision.SeekCover; squadResult = ESquadDecision.None; return true; }
                nextScanAt = Time.time + 2f;
                finder.Clear(); // Do not reuse the failed/rejected native cached result.
                if (finder.Find(enemy)) point = finder.Position;
            }
            attempted = true; nextRetryAt = Time.time + (Ordered ? 2f : 4f);
            lastCandidate = point; rejection = point.HasValue ? null : "nativeFinderEmpty";
            bool automatic = !Ordered && point.HasValue &&
                (point.Value - known).magnitude + 1.5f < (bot.Position - known).magnitude && Weapons.CanAdvance(enemy);
            if (point.HasValue && Allowed(point.Value, known, automatic))
            {
                // Only an admitted different position resets the per-leg execution budget.
                Attempt?.Clear("marksmanNewPosition");
                automaticDestination = automatic;
                Destination = point; SainRegroupBridge.Claim(bot.BotOwner, point.Value); Report("nativeFiringPosition");
            }
            else Report("noSuitableNativePosition");
        }
        if (Destination.HasValue)
        {
            if (automaticDestination)
            {
                int preparation = Weapons.CanAdvance(enemy) ? Weapons.Prepare() : -1;
                if (preparation < 0)
                { RememberFailed(Destination.Value); ReleaseDestination(); nextRetryAt = Time.time + 4f; Report("automaticWeaponRejected"); result = ECombatDecision.SeekCover; squadResult = ESquadDecision.None; return true; }
                if (preparation == 0)
                { automaticMovementStarted = false; Preparing = true; result = ECombatDecision.StandAndShoot; squadResult = ESquadDecision.None; Attempt?.Pause(); Report("prepareAutomaticSearch"); return true; }
                // Recheck once at the readiness handoff, not on every movement tick.
                // Knowledge can move less than the 8m reset threshold during a draw.
                if (!automaticMovementStarted && !Allowed(Destination.Value, known, true))
                { RememberFailed(Destination.Value); ReleaseDestination(); nextRetryAt = Time.time + 4f; Weapons.Cancel(enemy); Report("preparedPositionInvalidated"); result = ECombatDecision.SeekCover; squadResult = ESquadDecision.None; return true; }
                automaticMovementStarted = true;
            }
            OwnsMovement = true;
            result = ECombatDecision.MoveToEngage; squadResult = ESquadDecision.None;
            return true;
        }
        bool wasOrdered = Ordered;
        if (!wasOrdered) ordered = false;
        if (proposesApproach || wasOrdered || passiveCover)
        { result = ECombatDecision.SeekCover; squadResult = ESquadDecision.None; return true; }
        return false;
    }

    private bool Protected(ECombatDecision solo, ESelfActionType self) =>
        self != ESelfActionType.None || SainAddonBridge.IsUsingMedical(bot.BotOwner) ||
        solo == ECombatDecision.Retreat || solo == ECombatDecision.DogFight || solo == ECombatDecision.MeleeAttack ||
        solo == ECombatDecision.AvoidGrenade || solo == ECombatDecision.ThrowGrenade || solo == ECombatDecision.FightZombies;

    private static bool Pursuit(ECombatDecision solo, ESquadDecision squad) =>
        solo == ECombatDecision.Search || solo == ECombatDecision.RushEnemy || solo == ECombatDecision.MoveToEngage ||
        squad == ESquadDecision.Search || squad == ESquadDecision.GroupSearch || squad == ESquadDecision.Help ||
        squad == ESquadDecision.PushSuppressedEnemy;

    private bool Reject(string why) { rejection = why; return false; }
    private bool Allowed(Vector3 point, Vector3 known, bool automatic)
    {
        if (!Finite(point)) return Reject("nonfinite");
        if ((point - bot.Position).sqrMagnitude <= 4f) return Reject("alreadyAtPosition");
        for (int i = 0; i < failedCount; i++)
            if ((point - failedPositions[i]).sqrMagnitude < 16f) return Reject("failedPosition");
        if (!SainRegroupBridge.SameLevel(point, bot.Position)) return Reject("differentFloor");
        if (!SainRegroupBridge.IsDestinationAvailable(bot.BotOwner, point)) return Reject("reserved");
        if (!SainRegroupBridge.TryGetDistance(bot.Position, point, out float path)) return Reject("incompleteRoute");
        if (path > (Ordered ? 140f : 90f)) return Reject("routeTooLong");
        Vector3 delta = point - known; delta.y = 0f;
        if (delta.sqrMagnitude < 16f * 16f) return Reject("enemyTooClose");
        if (Ordered || automatic) return true;
        if (Follower?.CombatIndependent != true && SainPlayerSquadBridge.TryGetPlayerLeader(bot.BotOwner, out Player player))
        {
            float radius = SainRegroupBridge.GetTriggerDistance(bot.BotOwner);
            if ((point - player.Position).sqrMagnitude > radius * radius) return Reject("outsidePlayerArea");
            if (Vector3.Dot(point - player.Position, known - player.Position) <= 0f) return true;
        }
        return (point - known).magnitude + 1.5f >= (bot.Position - known).magnitude || Reject("closesOnEnemy");
    }

    private void RememberFailed(Vector3 point)
    {
        for (int i = 0; i < failedCount; i++)
            if ((point - failedPositions[i]).sqrMagnitude < 16f) return;
        if (failedCount < failedPositions.Length) failedPositions[failedCount++] = point;
    }

    private static bool Finite(Vector3 point) =>
        !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
        !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
        !float.IsNaN(point.z) && !float.IsInfinity(point.z);

    private void ReleaseDestination()
    {
        if (Destination.HasValue) SainRegroupBridge.Release(bot.BotOwner, Destination.Value);
        Destination = null; automaticDestination = automaticMovementStarted = false;
    }
    internal void Clear(string why)
    {
        // The shared movement attempt retains its own destination across action restarts.
        // Retire a started leg when its objective is cancelled, without resetting its budget.
        // Renewed contact/knowledge or useful fire still unlocks it through Observe.
        var attempt = Attempt;
        if (Destination.HasValue && enemyId != null && attempt?.EnemyId == enemyId)
        { attempt.Fail("marksmanCancelled"); RememberFailed(Destination.Value); }
        ReleaseDestination();
        OwnsMovement = false; Preparing = false; ordered = false;
        Weapons.Cancel(why == "combatEnded" || why == "release" || why == "nativeStateReplaced" || why == "contactLost" ? null : bot.GoalEnemy);
        if (why == "replacementOrder" || why == "regroup" || why == "squadSupport" || why == "suppressionOrder")
            nextRetryAt = Time.time + 4f;
        else
        { attempted = false; enemyId = null; failedCount = 0; nextRetryAt = 0f; }
        lastCandidate = null; rejection = null;
        finder.Clear(); reason = why;
    }
    private void Report(string why)
    {
        if (reason == why) return;
        reason = why;
        if (SainCombatRecorderBridge.IsRecording)
            SainCombatRecorderBridge.RecordEvent(bot.BotOwner, "sainMarksman", Snapshot);
    }
    internal object Snapshot => new { reason, enemyId, attempted, ordered = Ordered, moving = OwnsMovement, failure = Attempt?.Failure,
        automaticDestination, preparing = Preparing, weapon = Weapons.Snapshot,
        destination = SAINFollowerRecorder.Point(Destination), failedPositions = failedCount,
        candidate = SAINFollowerRecorder.Point(lastCandidate), rejection, retryIn = Mathf.Max(0f, nextRetryAt - Time.time) };
}

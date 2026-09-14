using EFT;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.SAINComponent;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace pitTeam.SAINAddon;

// One destination per remembered contact. Kept outside the action so SAIN decision
// events and BigBrain restarts cannot reset a failed attempt or its progress budget.
internal sealed class SAINFollowerEngageAttempt(BotComponent bot) : BotBase(bot)
{
    internal const float AttemptSeconds = 20f, StallSeconds = 6f, ArrivalSeconds = 2f;
    internal string EnemyId { get; private set; }
    internal Vector3? Destination { get; private set; }
    internal string Failure { get; private set; }
    internal bool Failed => Failure != null;
    internal float ActiveSeconds { get; private set; }
    private Vector3 anchor, progressPosition;
    private float lastTick = -1f, noProgressSeconds, arrivalSeconds;

    internal bool Independent => BossPlayers.Instance?.GetFollower(BotOwner)?.CombatIndependent == true;

    internal void Observe(Enemy enemy)
    {
        if (Independent || enemy == null || !Enemy.IsEnemyActive(enemy) || enemy.EnemyPlayer?.HealthController?.IsAlive != true)
        { Clear("contextEnded"); return; }
        var known = enemy.KnownPlaces.LastKnownPosition;
        if (EnemyId != null && (enemy.EnemyPlayer.ProfileId != EnemyId ||
            (known.HasValue && (known.Value - anchor).sqrMagnitude >= 64f))) Clear("newContact");
        if (enemy.IsVisible && enemy.CanShoot) Clear("firingOpportunity");
    }

    internal bool FailedFor(Enemy enemy)
    {
        Observe(enemy);
        return Failed && EnemyId == enemy?.EnemyPlayer?.ProfileId;
    }

    internal Vector3? Tick(Enemy enemy, Vector3? candidate)
    {
        Observe(enemy);
        if (enemy == null || !Enemy.IsEnemyActive(enemy) || enemy.EnemyPlayer?.HealthController?.IsAlive != true) return null;
        if (Independent) return candidate;
        if (EnemyId == null)
        {
            EnemyId = enemy.EnemyPlayer.ProfileId;
            anchor = enemy.KnownPlaces.LastKnownPosition ?? Bot.Position;
            Destination = candidate;
            progressPosition = Bot.Position;
            lastTick = Time.time;
            Report("started");
            if (!candidate.HasValue) Fail("noFiringPosition");
        }
        if (Failed) return null; // The publication bridge chooses cover or regroup; no new outbound path.
        float elapsed = lastTick < 0f ? 0f : Mathf.Max(0f, Time.time - lastTick);
        lastTick = Time.time;
        ActiveSeconds += elapsed;
        if ((Bot.Position - progressPosition).sqrMagnitude >= 0.5625f)
        { progressPosition = Bot.Position; noProgressSeconds = 0f; }
        else noProgressSeconds += elapsed;
        if (Destination.HasValue && (Bot.Position - Destination.Value).sqrMagnitude <= 4f)
        {
            arrivalSeconds += elapsed;
            if (arrivalSeconds >= ArrivalSeconds) Fail("arrivedWithoutShot");
        }
        else
        {
            arrivalSeconds = 0f;
            if (noProgressSeconds >= StallSeconds) Fail("noProgress");
        }
        if (!Failed && ActiveSeconds >= AttemptSeconds) Fail("attemptExpired");
        return Failed ? null : Destination;
    }

    internal void Resume() => lastTick = Time.time;
    internal void Pause() => lastTick = -1f;
    internal void Fail(string reason)
    {
        if (Failed || Independent) return;
        Failure = reason;
        Report(reason);
    }
    internal void Clear(string reason)
    {
        if (EnemyId != null) Report(reason);
        EnemyId = null; Destination = null; Failure = null;
        ActiveSeconds = 0f; noProgressSeconds = 0f; arrivalSeconds = 0f; lastTick = -1f;
    }
    private void Report(string reason)
    {
        if (SainCombatRecorderBridge.IsRecording)
            SainCombatRecorderBridge.RecordEvent(BotOwner, "sainEngageAttempt", new {
                reason, enemyId = EnemyId, failure = Failure, activeSeconds = ActiveSeconds,
                destination = SAINFollowerRecorder.Point(Destination)
            });
    }
}

// Adapted from SAIN 4.5.1 MoveToEngageAction (Solarint, MIT; SAIN-LICENSE.txt).
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using SAIN.Layers;
using SAIN.Models.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace pitTeam.SAINAddon;

internal sealed class SAINFollowerMoveToEngageAction(BotOwner bot) : BotAction(bot, nameof(SAINFollowerMoveToEngageAction)), IBotAction
{
    private float recalcPathTime;
    private object ownedPath;
    private SAINFollowerPushObjective? Push => SAINFollowerRuntime.GetPush(BotOwner);
    private bool wasPush;
    private SAINFollowerEngageAttempt? Attempt => SAINFollowerRuntime.GetEngageAttempt(BotOwner);

    public override void Start() { base.Start(); recalcPathTime = 0f; wasPush = Push?.OwnsMovement == true; if (!wasPush) Attempt?.Resume(); }
    public override void Stop()
    {
        Push?.Pause();
        Attempt?.Pause();
        StopOwnedPath();
        base.Stop();
    }
    public override void Update(CustomLayer.ActionData data)
    {
        Push?.Observe();
        Enemy enemy = Bot.GoalEnemy;
        if (enemy == null)
        { if (wasPush) { StopOwnedPath(); Push?.Pause(); } Bot.Steering.SteerByPriority(); return; }
        Bot.Mover.SetTargetPose(1f);
        Bot.Mover.SetTargetMoveSpeed(1f);
        if (enemy.IsVisible && Shoot.ShootAnyVisibleEnemies(enemy))
        {
            Attempt?.Clear("shot");
            if (Bot.Mover.Moving) Bot.Mover.Stop();
            Bot.Steering.SteerByPriority(enemy);
            return;
        }
        var marksman = SAINFollowerRuntime.GetMarksman(BotOwner);
        if (marksman != null && !marksman.OwnsMovement)
        { StopOwnedPath(); Attempt?.Pause(); return; }
        bool pushing = Push?.OwnsMovement == true;
        // A failure/cancellation can precede the next native publication. Do not let
        // this still-running action silently create a different engagement attempt.
        if (!pushing && (wasPush || Push?.Active == true) && Push?.NativeEngagementAllowed != true)
        { StopOwnedPath(); Push?.Pause(); Bot.Steering.SteerByPriority(enemy); return; }
        if (pushing != wasPush) { StopOwnedPath(); recalcPathTime = 0f; Attempt?.Pause(); wasPush = pushing; }
        var attempt = pushing ? null : Attempt;
        Vector3? candidate = marksman != null ? marksman.Destination : Bot.Decision.EnemyDecisions.FiringPosition;
        Vector3? destination = pushing ? Push.TickMovement() : attempt != null ? attempt.Tick(enemy, candidate) : candidate;
        if (!destination.HasValue)
        {
            if (pushing || attempt?.Failed == true) StopOwnedPath();
            Bot.Steering.SteerByPriority(enemy);
            return;
        }
        // Hold the one reached firing position long enough for vision to update; don't
        // accept a replacement candidate just because SAIN's finder has rearmed.
        if (attempt != null && !attempt.Independent && !attempt.Failed &&
            (destination.Value - Bot.Position).sqrMagnitude <= 4f) return;
        if (recalcPathTime > Time.time) return;
        recalcPathTime = Time.time + 2f;
        bool sprint = !BotOwner.Memory.IsUnderFire && (destination.Value - Bot.Position).magnitude > 15f;
        bool moved = sprint && Bot.Mover.RunToPoint(destination.Value, true, -1f, ESprintUrgency.Middle);
        if (!moved) moved = Bot.Mover.WalkToPoint(destination.Value, true);
        if (moved) ownedPath = Bot.Mover.ActivePath;
        else { if (pushing) Push.Fail("pathRejected"); else attempt?.Fail("pathRejected"); StopOwnedPath(); }
    }
    private void StopOwnedPath()
    {
        if (ownedPath != null && ReferenceEquals(Bot.Mover.ActivePath, ownedPath)) Bot.Mover.Stop();
        ownedPath = null;
    }
    public override void OnSteeringTicked()
    {
        Enemy enemy = Bot.GoalEnemy;
        if (TryShootAnyTarget(enemy)) { Bot.Steering.SteerByPriority(enemy, false); return; }
        if (Bot.Mover.Moving && Bot.Steering.LookToMovingDirection()) return;
        Bot.Steering.LookToLastKnownEnemyPosition(enemy);
    }
}

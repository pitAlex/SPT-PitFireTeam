// Follower regroup extension of SAIN 4.5.1 RegroupAction (Solarint, MIT; SAIN-LICENSE.txt).
// Native SAIN movers and shooting/steering serve a player-anchored, two-mode objective.
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using SAIN.Layers;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace pitTeam.SAINAddon;

public class SAINFollowerSquadRegroupAction(BotOwner bot) : BotAction(bot, nameof(SAINFollowerSquadRegroupAction)), IBotAction
{
    private float nextMove;
    private object ownedPath;
    private bool runRequested;

    public override void Start()
    {
        base.Start();
        Bot.Mover.Stop();
        Shoot.EndShoot();
        nextMove = 0f; runRequested = false;
        ownedPath = null;
    }

    public override void Update(CustomLayer.ActionData data)
    {
        var objective = SAINFollowerRuntime.GetRegroup(BotOwner);
        if (objective == null) { StopOwnedPath(); return; }
        objective.Observe();
        if (!objective.Active || objective.Settling) { StopOwnedPath(); return; }
        if (Time.time < nextMove) return;
        nextMove = Time.time + 0.5f;
        if (!objective.TryGetTarget(out Vector3 target, out bool sprint)) { StopOwnedPath(); return; }
        Bot.Mover.SetTargetPose(1f);
        Bot.Mover.SetTargetMoveSpeed(1f);
        if (sprint && !runRequested) Shoot.EndShoot();
        runRequested = sprint;
        bool moved = sprint && Bot.Mover.RunToPoint(target);
        if (!moved) moved = Bot.Mover.WalkToPoint(target);
        if (moved) ownedPath = Bot.Mover.ActivePath;
        else { StopOwnedPath(); objective.PathFailed(); }
    }

    public override void OnSteeringTicked()
    {
        var objective = SAINFollowerRuntime.GetRegroup(BotOwner);
        if (objective?.Active != true) return;
        if (runRequested && !objective.PersonalContactHot(2.5f))
        { Bot.Steering.LookToMovingDirection(); return; }
        Enemy enemy = Bot.GoalEnemy;
        if (!Shoot.ShootAnyVisibleEnemies(enemy))
            Bot.Suppression.TrySuppressAnyEnemy(enemy, Bot.EnemyController.KnownEnemies);
        if (!Bot.Steering.SteerByPriority(enemy)) Bot.Steering.LookToMovingDirection();
    }

    public override void Stop()
    {
        StopOwnedPath();
        SAINFollowerRuntime.GetRegroup(BotOwner)?.ReleaseTarget();
        base.Stop();
    }

    private void StopOwnedPath()
    {
        if (ownedPath != null && ReferenceEquals(Bot.Mover.ActivePath, ownedPath)) Bot.Mover.Stop();
        ownedPath = null;
    }
}

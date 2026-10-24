// Adapted from SAIN 4.5.1 FollowSearchParty (Solarint, MIT; SAIN-LICENSE.txt).
// Native follow movement/steering, targeting the retained search initiator instead of squad leadership.
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using SAIN.Models.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;
using UnityEngine.AI;

using SAIN.Layers;
using pitTeam.Modules;
namespace pitTeam.SAINAddon;

public class SAINFollowerFollowSearchPartyAction(BotOwner bot) : BotAction(bot, nameof(SAINFollowerFollowSearchPartyAction)), IBotAction
{
    public override void Update(CustomLayer.ActionData data)
    {
        var leader = SAINFollowerRuntime.GetSearchLeader(BotOwner);
        if (leader == null)
        {
            StopOwnedPath();
            _leaderId = null;
            _hasLeadPosition = false;
            return; // Wait for normal publication; never substitute the player.
        }
        if (_leaderId != leader.ProfileId)
        {
            StopOwnedPath();
            _leaderId = leader.ProfileId;
            _hasLeadPosition = false;
            _nextUpdatePosTime = 0f;
        }
        if (_enemy != Bot.GoalEnemy || _enemy == null || !Enemy.IsEnemyActive(_enemy) || !_enemy.CheckValid())
        {
            if (_enemy != null)
            {
                Bot.Search.ToggleSearch(false, _enemy);
            }

            _enemy = Bot.GoalEnemy;
            if (_enemy != null)
            {
                Bot.Search.ToggleSearch(true, _enemy);
            }
        }

        if (_nextUpdatePosTime < Time.time)
        {
            MoveToLead(out float nextTime);
            _nextUpdatePosTime = Time.time + nextTime;
        }
    }

    public override void OnSteeringTicked()
    {
        if (!Shoot.ShootAnyVisibleEnemies(_enemy))
        {
            Bot.Suppression.TrySuppressAnyEnemy(_enemy, Bot.EnemyController.KnownEnemies);
        }
        if (!Bot.Steering.SteerByPriority(_enemy, false))
        {
            Bot.Steering.LookToMovingDirection();
        }
    }

    private void MoveToLead(out float nextUpdateTime)
    {
        var leader = SAINFollowerRuntime.GetSearchLeader(BotOwner);
        if (leader == null)
        {
            nextUpdateTime = 1f;
            return;
        }
        if (_hasLeadPosition && (_LastLeadPos - leader.Position).sqrMagnitude < 1f)
        {
            nextUpdateTime = 1f;
            return;
        }
        Vector3? movePosition = GetPosNearLead(leader.Position);
        if (movePosition == null)
        {
            nextUpdateTime = 0.25f;
            return;
        }

        float moveDistance = (movePosition.Value - Bot.Position).sqrMagnitude;
        if (moveDistance < 1f)
        {
            nextUpdateTime = 1f;
            return;
        }

        if (moveDistance > 20f * 20f && Bot.Mover.RunToPoint(movePosition.Value, false, -1, ESprintUrgency.Middle, true))
        {
            RememberMove(leader.Position);
            nextUpdateTime = 2f;
            return;
        }
        if (Bot.Mover.Running)
        {
            nextUpdateTime = 2f;
            return;
        }
        nextUpdateTime = 1f;
        if (Bot.Mover.WalkToPoint(movePosition.Value, false)) RememberMove(leader.Position);
    }

    private Vector3? GetPosNearLead(Vector3 leadPos)
    {
        Vector3? result = null;
        if (NavMesh.SamplePosition(leadPos, out var leadHit, 3f, -1))
        {
            Vector3 leadDir = Bot.Position - leadHit.position;
            leadDir.y = 0;
            leadDir = leadDir.normalized * 2f;
            if (NavMesh.Raycast(leadHit.position, (leadDir + leadHit.position), out var rayHit, -1))
            {
                result = rayHit.position;
            }
            else
            {
                result = leadDir + leadHit.position;
            }
        }
        return result;
    }

    private object _ownedPath;
    private string _leaderId;
    private bool _hasLeadPosition;
    private void RememberMove(Vector3 position)
    {
        _LastLeadPos = position;
        _hasLeadPosition = true;
        _ownedPath = Bot.Mover.ActivePath;
    }
    private void StopOwnedPath()
    {
        if (_ownedPath != null && ReferenceEquals(_ownedPath, Bot.Mover.ActivePath)) Bot.Mover.Stop();
        _ownedPath = null;
    }

    private float _nextUpdatePosTime;
    private Vector3 _LastLeadPos;

    public override void Start()
    {
        base.Start();
        _nextUpdatePosTime = 0f;
        _LastLeadPos = Vector3.zero;
        _hasLeadPosition = false;
        _leaderId = null;
        _ownedPath = null;
    }

    private Enemy _enemy;

    public override void Stop()
    {
        StopOwnedPath();
        base.Stop();
        Bot.Search.ToggleSearch(false, _enemy);
        _enemy = null;
    }
}

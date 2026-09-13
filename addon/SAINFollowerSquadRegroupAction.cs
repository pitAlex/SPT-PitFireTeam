// Adapted from SAIN 4.5.1 RegroupAction (Solarint, MIT; SAIN-LICENSE.txt).
// Same movement/steering policy; the leader is the real player instead of a BotComponent.
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

using SAIN.Layers;
using pitTeam.Modules;
namespace pitTeam.SAINAddon;

public class SAINFollowerSquadRegroupAction(BotOwner bot) : BotAction(bot, nameof(SAINFollowerSquadRegroupAction)), IBotAction
{
    public override void Update(CustomLayer.ActionData data)
    {
        Enemy enemy = Bot.GoalEnemy;
        Vector3? SquadLeadPos = SainPlayerSquadBridge.TryGetPlayerLeader(BotOwner, out Player leader) && leader.HealthController?.IsAlive == true ? leader.Position : (Vector3?)null;
        if (SquadLeadPos != null)
        {
            bool hasEnemy = enemy != null;
            bool enemyLOS = enemy?.InLineOfSight == true;
            float leadDist = (SquadLeadPos.Value - BotOwner.Position).magnitude;
            float enemyDist = hasEnemy ? enemy.KnownPlaces.BotDistanceFromLastKnown : 999f;

            bool sprint = hasEnemy && leadDist > 30f && !enemyLOS && enemyDist > 50f;

            if (_nextChangeSprintTime < Time.time)
            {
                _nextChangeSprintTime = Time.time + 1f;
                if (sprint)
                {
                    Bot.Mover.RunToPoint(SquadLeadPos.Value);
                }
                else
                {
                    Bot.Mover.WalkToPoint(SquadLeadPos.Value);
                }
            }
        }

        Bot.Mover.SetTargetPose(1f);
        Bot.Mover.SetTargetMoveSpeed(1f);
    }

    public override void OnSteeringTicked()
    {
        Enemy enemy = Bot.GoalEnemy;
        if (!Shoot.ShootAnyVisibleEnemies(enemy))
        {
            Bot.Suppression.TrySuppressAnyEnemy(enemy, Bot.EnemyController.KnownEnemies);
        }
        if (!Bot.Steering.SteerByPriority(enemy))
        {
            Bot.Steering.LookToMovingDirection();
        }
    }

    private float _nextChangeSprintTime;
}

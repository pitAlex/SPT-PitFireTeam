using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using SAIN.Layers;
using SAIN.Models.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.SAINAddon
{
    internal sealed class SAINFollowerCombatFollowBossSearchAction : BotAction
    {
        private float _nextUpdatePosTime;
        private Vector3 _lastBossPos;
        private Enemy? _enemy;

        public SAINFollowerCombatFollowBossSearchAction(BotOwner botOwner)
            : base(botOwner, nameof(SAINFollowerCombatFollowBossSearchAction))
        {
        }

        public override void Start()
        {
            base.Start();
            _nextUpdatePosTime = 0f;
            _lastBossPos = Vector3.zero;
            _enemy = null;
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (_enemy == null || !Enemy.IsEnemyActive(_enemy) || !_enemy.CheckValid())
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
                MoveToBoss(out float nextTime);
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

        public override void Stop()
        {
            base.Stop();
            Bot.Search.ToggleSearch(false, _enemy);
            _enemy = null;
        }

        private void MoveToBoss(out float nextUpdateTime)
        {
            if (!TryGetBossPosition(out Vector3 bossPosition))
            {
                nextUpdateTime = 1f;
                return;
            }

            if ((_lastBossPos - bossPosition).sqrMagnitude < 1f)
            {
                nextUpdateTime = 1f;
                return;
            }

            Vector3? movePosition = GetPosNearBoss(bossPosition);
            if (movePosition == null)
            {
                nextUpdateTime = 0.25f;
                return;
            }

            _lastBossPos = bossPosition;
            float moveDistance = (movePosition.Value - Bot.Position).sqrMagnitude;
            if (moveDistance < 1f)
            {
                nextUpdateTime = 1f;
                return;
            }

            if (moveDistance > 20f * 20f &&
                Bot.Mover.RunToPoint(movePosition.Value, false, -1, ESprintUrgency.Middle, true))
            {
                nextUpdateTime = 2f;
                return;
            }

            if (Bot.Mover.Running)
            {
                nextUpdateTime = 2f;
                return;
            }

            nextUpdateTime = 1f;
            Bot.Mover.WalkToPoint(movePosition.Value, false);
        }

        private Vector3? GetPosNearBoss(Vector3 bossPosition)
        {
            if (!NavMesh.SamplePosition(bossPosition, out NavMeshHit bossHit, 3f, -1))
            {
                return null;
            }

            Vector3 bossDir = Bot.Position - bossHit.position;
            if (bossDir == Vector3.zero)
            {
                bossDir = Vector3.forward;
            }

            bossDir.y = 0f;
            bossDir = bossDir.normalized * 2f;
            if (NavMesh.Raycast(bossHit.position, bossDir + bossHit.position, out NavMeshHit rayHit, -1))
            {
                return rayHit.position;
            }

            return bossDir + bossHit.position;
        }

        private bool TryGetBossPosition(out Vector3 position)
        {
            position = default;
            if (BotOwner?.BotFollower?.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                return false;
            }

            string enemyProfileId = Bot.GoalEnemy?.EnemyPlayer?.ProfileId;
            if (!string.IsNullOrEmpty(enemyProfileId) &&
                SAINFollowerRuntimeBridge.TryGetSearchPartyLeaderPosition(boss, enemyProfileId, BotOwner?.ProfileId, out position))
            {
                return true;
            }

            position = FollowerCombatAnchor.GetAnchorPosition(BotOwner);
            return true;
        }
    }
}

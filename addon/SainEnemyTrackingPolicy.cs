using System;
using pitTeam.Modules;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace pitTeam.SAINAddon;

internal static partial class SainEnemyTracking
{
    internal static bool Simple(Enemy enemy) => enemy != null &&
        FollowerEnemyTracking.Mode == EnemyTrackingMode.Simple && pitFireTeam.UseSainFollowerCombat(enemy.BotOwner) &&
        SainAddonBridge.HasAcceptedGoalEnemy(enemy.BotOwner) &&
        ReferenceEquals(enemy.EnemyInfo, enemy.BotOwner.Memory?.GoalEnemy) &&
        enemy.EnemyKnown && enemy.KnownPlaces.LastKnownPlace != null && Enemy.IsEnemyActive(enemy) &&
        Time.time - enemy.KnownPlaces.TimeLastKnownUpdated <= FollowerEnemyTracking.RememberSeconds;

    internal static Vector3? Position(Enemy enemy) => Simple(enemy) ? enemy.EnemyPosition : enemy?.LastKnownPosition;
}

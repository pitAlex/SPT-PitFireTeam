using System.Collections.Generic;
using EFT;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace pitTeam.SAINAddon;

// Attention dismisses heard preparation for these identities in the current player
// sector. Native hearing/memory and accepted combat remain owned by their providers.
internal sealed class SAINFollowerAttentionIgnore
{
    private readonly HashSet<string> enemies = new();
    private Vector3 anchor;

    internal void Remember(BotComponent bot, Vector3 player)
    {
        Refresh(player);
        anchor = player;
        Add(bot.GoalEnemy);
        if (bot.EnemyController?.KnownEnemies != null)
            foreach (Enemy enemy in bot.EnemyController.KnownEnemies) Add(enemy);
    }

    private void Add(Enemy enemy)
    {
        if (enemy?.EnemyPlayer?.HealthController?.IsAlive == true &&
            !string.IsNullOrEmpty(enemy.EnemyProfileId))
            enemies.Add(enemy.EnemyProfileId);
    }

    internal void Refresh(Vector3 player)
    {
        float radius = SainRegroupBridge.BossMoveRefreshDistance;
        if (enemies.Count > 0 && (player - anchor).sqrMagnitude > radius * radius) Clear();
    }

    internal bool Contains(Enemy enemy, Vector3 player)
    {
        Refresh(player);
        return enemy != null && enemies.Contains(enemy.EnemyProfileId);
    }

    internal void Clear() => enemies.Clear();
}

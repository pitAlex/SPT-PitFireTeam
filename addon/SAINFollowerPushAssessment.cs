using EFT;
using pitTeam.BigBrain;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

namespace pitTeam.SAINAddon;

// Rifleman risk policy, using only SAIN-known locations for enemy geometry.
internal sealed class SAINFollowerPushAssessment(BotComponent bot)
{
    private readonly SainPushRiskBridge inputs = new(bot.BotOwner);
    private readonly HashSet<string> counted = new();
    private float nextPathCheck;
    private Vector3 lastBot, lastEnemy, lastBoss;
    private bool havePaths, pathsComplete, wasIndependent, effectivePathsComplete;
    private float enemyRoute, currentBossRoute, projectedBossRoute;
    internal bool SafetyBlocked { get; private set; }
    internal bool WeaponBlocked { get; private set; }
    internal bool AllowsAutomatic { get; private set; }
    internal bool Cautious { get; private set; }
    internal string Reason { get; private set; }
    internal int EnemyCount { get; private set; }
    internal float RequiredAggression { get; private set; }
    private float aggression, threat, pull, ratio, role;
    private int weaponPolicy;
    private bool magazineRestricted;

    internal void Evaluate(Enemy enemy, float effectiveAggression, bool independent)
    {
        var data = inputs.Read(enemy.EnemyInfo);
        Vector3 known = enemy.LastKnownPosition.GetValueOrDefault();
        counted.Clear(); counted.Add(enemy.EnemyProfileId);
        foreach (Enemy contact in bot.EnemyController.KnownEnemies)
        {
            if (contact == null || !contact.WasValid || !contact.EnemyKnown || string.IsNullOrEmpty(contact.EnemyProfileId) || !Enemy.IsEnemyActive(contact) ||
                contact.EnemyPlayer?.HealthController?.IsAlive != true || !contact.LastKnownPosition.HasValue) continue;
            // Count hostile contacts at this location, never hidden live positions or
            // unseen squad members obtained through a world-space overlap.
            if ((contact.LastKnownPosition.Value - known).sqrMagnitude <= FollowerPushRiskPolicy.ClusterRadius * FollowerPushRiskPolicy.ClusterRadius)
                counted.Add(contact.EnemyProfileId);
        }
        EnemyCount = counted.Count;
        bool leaderKnown = SainPlayerSquadBridge.TryGetPlayerLeader(bot.BotOwner, out Player leader);
        Vector3 boss = leaderKnown ? leader.Position : bot.Position;
        if (!havePaths || Time.time >= nextPathCheck || wasIndependent != independent ||
            (lastBot - bot.Position).sqrMagnitude > 4f || (lastEnemy - known).sqrMagnitude > 4f || (lastBoss - boss).sqrMagnitude > 4f)
        {
            havePaths = true; nextPathCheck = Time.time + 0.5f; wasIndependent = independent;
            lastBot = bot.Position; lastEnemy = known; lastBoss = boss;
            pathsComplete = SainRegroupBridge.TryGetDistance(bot.Position, known, out enemyRoute);
            currentBossRoute = projectedBossRoute = 0f;
            if (!independent)
            {
                bool current = SainRegroupBridge.TryGetDistance(bot.Position, boss, out currentBossRoute);
                bool projected = SainRegroupBridge.TryGetDistance(known, boss, out projectedBossRoute);
                pathsComplete &= leaderKnown && current && projected;
            }
        }
        bool complete = pathsComplete && enemy.Path.PathToEnemyStatus == NavMeshPathStatus.PathComplete;
        effectivePathsComplete = complete;
        ratio = data.EquipmentRatio; role = data.RoleMultiplier; weaponPolicy = data.WeaponPolicy;
        threat = FollowerPushRiskPolicy.Threat(ratio, role, EnemyCount, weaponPolicy);
        pull = independent || !complete ? 0f : FollowerPushRiskPolicy.PlayerPull(enemyRoute, currentBossRoute, projectedBossRoute);
        RequiredAggression = FollowerPushRiskPolicy.Required(enemyRoute, threat, pull);
        aggression = effectiveAggression;
        SafetyBlocked = data.Medical || bot.Memory.Health.HealthStatus == ETagStatus.BadlyInjured ||
            bot.Memory.Health.HealthStatus == ETagStatus.Dying || SainRegroupBridge.IsUnderFire(bot.BotOwner) ||
            bot.Medical.TimeSinceShot < 1f || bot.Suppression.IsHeavySuppressed;
        WeaponBlocked = !data.WeaponReady;
        float direct = (known - bot.Position).magnitude;
        magazineRestricted = data.MagazineRestricted && !(data.CloseShotgun && direct <= 20f);
        Cautious = EnemyCount >= 2 || role > 1.1f || weaponPolicy != 0 || threat >= -2f;
        Reason = SafetyBlocked ? "survivalOrMedical" : WeaponBlocked ? "weaponNotReady" :
            !(enemy.Seen || enemy.Heard || enemy.IsVisible) ? "unreliableEnemyLocation" : !complete ? "incompleteNavPath" :
            weaponPolicy == 2 && direct >= 18f ? "weaponThreat" : magazineRestricted ? "magazineReadiness" :
            effectiveAggression < RequiredAggression ? "riskScore" : "scorePassed";
        AllowsAutomatic = Reason == "scorePassed";
    }
    internal object Snapshot => new { reason = Reason, enemiesAtLocation = EnemyCount, equipmentRatio = ratio,
        roleMultiplier = role, weaponPolicy, magazineRestricted, safetyBlocked = SafetyBlocked, weaponBlocked = WeaponBlocked,
        cautious = Cautious, allowsAutomatic = AllowsAutomatic, aggression, requiredAggression = RequiredAggression,
        threat, playerPull = pull, enemyRoute, currentBossRoute, projectedBossRoute, pathsComplete = effectivePathsComplete };
}

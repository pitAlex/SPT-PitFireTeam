// Follower-local candidate discovery using SAIN 4.5.1's native cover validator.
using System;
using System.Collections.Generic;
using EFT;
using pitTeam.BigBrain;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.Preset.Shared.GlobalSettings;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.SubComponents.CoverFinder;
using UnityEngine;

namespace pitTeam.SAINAddon;

internal sealed class SAINFollowerCoverFinder(BotComponent bot)
{
    private readonly CoverAnalyzer analyzer = new(bot, bot.Cover.CoverFinder);
    private readonly SainBotCoverData colliders = new();
    private readonly List<CoverPoint> candidates = new();
    private readonly List<CoverPoint> ranked = new();
    private const int ProbeBudget = 4;
    private readonly Dictionary<CoverPoint, Validation> validation = new();
    private struct Validation
    {
        internal bool Valid;
        internal float Until;
        internal int Pass;
        internal Vector3 Bot, Threat, Point;
        internal string Enemy;
    }
    // Planning geometry only. Cache positive and negative results through a pending
    // pass so a slow decision cadence cannot endlessly restart its first probes.
    private readonly Dictionary<CoverPoint, PlanningProbe> firingLanes = new();
    private readonly Dictionary<CoverPoint, PlanningProbe> bossRoutes = new();
    private struct PlanningProbe
    {
        internal Vector3 Point, Target;
        internal string Enemy;
        internal bool Clear;
        internal float Distance, Until;
        internal int Pass;
    }
    private int scanIndex, probeFrame = -1, probes, validationPass;
    internal bool Pending { get; private set; }
    private Comparison<CoverPoint> bossComparison;
    private Vector3 rankBoss;
    private bool rankNearby, rankProtective;
    private Vector3 rankEnemyDirection;
    private float rankEnemyDistance, protectiveRange;
    private float nearbyCoverDistance = 25f;
    private bool scanned;
    private string contact;
    private Vector3 bossAnchor, botAnchor, enemyAnchor;
    internal int LastScanCount { get; private set; }

    internal List<CoverPoint> Find(Enemy enemy, Vector3 boss, bool preferNearby = false, bool preferProtective = false)
    {
        ranked.Clear(); if (!Pending) validationPass++; Pending = false;
        if (enemy?.LastKnownPosition == null) return ranked;
        rankNearby = preferNearby; rankProtective = preferNearby && preferProtective;
        rankBoss = boss;
        if (rankProtective)
        {
            Vector3 toEnemy = enemy.LastKnownPosition.Value - boss; toEnemy.y = 0f;
            rankEnemyDistance = toEnemy.magnitude; rankEnemyDirection = toEnemy.normalized;
            protectiveRange = SainCoverGeometry.SearchRadius;
        }
        nearbyCoverDistance = NearbyCoverRange;
        Scan(enemy, boss, SainCoverGeometry.SearchRadius, SainRegroupBridge.BossMoveRefreshDistance);
        foreach (CoverPoint point in candidates) Add(point, boss);
        foreach (CoverPoint point in bot.Cover.CoverPoints) Add(point, boss);
        if (Pending) { ranked.Clear(); return ranked; }
        ranked.Sort(bossComparison ??= CompareBoss);
        return ranked;
    }

    // Mirror Core combat-start ranges without changing its internal configuration API.
    internal static float NearbyCoverRange
    {
        get
        {
            string location = Comfort.Common.Singleton<GameWorld>.Instance?.LocationId;
            return !string.IsNullOrEmpty(location) &&
                (location.IndexOf("factory", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 string.Equals(location, "laboratory", StringComparison.OrdinalIgnoreCase)) ? 12f : 25f;
        }
    }

    internal List<CoverPoint> FindPreparation(Enemy enemy)
    {
        // Reuse the native validator, route ranking and four-probe frame budget.
        // No player-area or unrestricted native fallback before combat admission.
        List<CoverPoint> points = Find(enemy, bot.Position, preferNearby: true);
        for (int i = points.Count - 1; i >= 0; i--)
            if (!IsNearby(points[i])) points.RemoveAt(i);
        return points;
    }

    private void Scan(Enemy enemy, Vector3 boss, float radius, float refresh)
    {
        Vector3 threat = enemy.LastKnownPosition.GetValueOrDefault();
        if (!scanned || contact != enemy.EnemyProfileId || (boss - bossAnchor).sqrMagnitude >= refresh * refresh ||
            (bot.Position - botAnchor).sqrMagnitude >= refresh * refresh || (threat - enemyAnchor).sqrMagnitude >= 64f)
        {
            scanned = true; contact = enemy.EnemyProfileId;
            bossAnchor = boss; botAnchor = bot.Position; enemyAnchor = threat;
            candidates.Clear(); validation.Clear(); firingLanes.Clear(); bossRoutes.Clear(); LastScanCount = 0; scanIndex = 0;
            colliders.OverlapBoxAndFilter(new SainBotCoverData.BotColliderQueryParams {
                origin = boss + Vector3.up * 0.25f, halfExtents = new Vector3(radius, 5f, radius),
                mask = LayersMaskController.HighPolyWithTerrainNoGrassMask,
                minColliderSize = new Vector3(0.25f, GlobalSettingsClass.Instance.General.Cover.CoverMinHeight, 0.25f),
                maxColliderSize = new Vector3(30f, 30f, 30f)
            });
            colliders.HandleLists(boss);
        }
        // Discovery, validation, firing lanes and boss routes share one frame budget.
        while (scanIndex < colliders.ValidCollidersList.Count && LastScanCount < 32)
        {
            if (!TakeProbe()) { Pending = true; break; }
            var data = colliders.ValidCollidersList[scanIndex++];
            if (data.Collider == null) continue;
            LastScanCount++;
            if (analyzer.CheckCreateNewCoverPoint(data.Collider, threat, bot.NavMeshPosition,
                (threat - bot.NavMeshPosition).normalized, out CoverPoint point, out _))
            {
                candidates.Add(point);
                Remember(point, enemy, true);
            }
        }
    }

    private bool TakeProbe()
    {
        if (probeFrame != Time.frameCount) { probeFrame = Time.frameCount; probes = 0; }
        if (probes >= ProbeBudget) return false;
        probes++; return true;
    }
    private int CompareBoss(CoverPoint a, CoverPoint b)
    {
        if (rankNearby)
        {
            bool aProtective = IsProtective(a), bProtective = IsProtective(b);
            if (aProtective != bProtective) return aProtective ? -1 : 1;
            bool aNearby = IsNearby(a), bNearby = IsNearby(b);
            if (aNearby != bNearby) return aNearby ? -1 : 1;
            int distance = aNearby
                ? RouteDistance(a).CompareTo(RouteDistance(b))
                : (a.Position - rankBoss).sqrMagnitude.CompareTo((b.Position - rankBoss).sqrMagnitude);
            return distance != 0 ? distance : a.PathData.PathLength.CompareTo(b.PathData.PathLength);
        }
        int tier = Tier(a, rankBoss).CompareTo(Tier(b, rankBoss));
        return tier != 0 ? tier : Score(a, rankBoss).CompareTo(Score(b, rankBoss));
    }

    // Same forward-progress/short-route criteria as core ordered push. Native
    // cover validation supplies protection; the ray checks a potential firing lane,
    // never grants visibility or permission to shoot a remembered contact.
    internal List<CoverPoint> FindForward(Enemy enemy, bool requireFiringLane = true)
    {
        ranked.Clear(); if (!Pending) validationPass++; Pending = false;
        if (enemy?.LastKnownPosition == null) return ranked;
        Vector3 threat = enemy.LastKnownPosition.Value;
        Vector3 direction = threat - bot.Position; direction.y = 0f;
        Scan(enemy, bot.Position + direction.normalized * 15f, 20f, 8f);
        foreach (CoverPoint point in candidates) AddForward(point, enemy, threat, requireFiringLane);
        foreach (CoverPoint point in bot.Cover.CoverPoints) AddForward(point, enemy, threat, requireFiringLane);
        if (Pending) { ranked.Clear(); return ranked; }
        ranked.Sort((a,b) => a.PathData.PathLength.CompareTo(b.PathData.PathLength));
        return ranked;
    }
    private void AddForward(CoverPoint point, Enemy enemy, Vector3 threat, bool requireFiringLane)
    {
        if (point == null || ranked.Contains(point) || !ValidateCandidate(point, enemy) ||
            !SainRegroupBridge.SameLevel(point.Position, bot.Position) ||
            !SainRegroupBridge.IsDestinationAvailable(bot.BotOwner, point.Position)) return;
        // Native validation already calculated a complete route to this point.
        float distance = Mathf.Max(point.PathData.PathLength, (point.Position - bot.Position).magnitude);
        if (!FollowerPushGeometry.IsForwardPosition(bot.Position, threat, point.Position) ||
            distance > FollowerPushGeometry.MaxForwardRoute) return;
        if (requireFiringLane && !HasFiringLane(point, enemy, threat)) return;
        ranked.Add(point);
    }

    private bool Reuse(PlanningProbe cached, CoverPoint point, Vector3 target, string enemy) =>
        (Time.time < cached.Until || cached.Pass == validationPass) && cached.Enemy == enemy &&
        (cached.Point - point.Position).sqrMagnitude < 0.01f && (cached.Target - target).sqrMagnitude < 0.01f;

    private void RememberPlanning(Dictionary<CoverPoint, PlanningProbe> cache, CoverPoint point,
        Vector3 target, string enemy, bool clear, float distance = 0f)
    {
        if (cache.Count >= 128 && !cache.ContainsKey(point)) cache.Clear();
        cache[point] = new PlanningProbe { Point = point.Position, Target = target, Enemy = enemy,
            Clear = clear, Distance = distance, Until = Time.time + 1f, Pass = validationPass };
    }

    private bool HasFiringLane(CoverPoint point, Enemy enemy, Vector3 threat)
    {
        if (firingLanes.TryGetValue(point, out var cached) && Reuse(cached, point, threat, enemy.EnemyProfileId)) return cached.Clear;
        if (!TakeProbe()) { Pending = true; return false; }
        bool clear = !Physics.Linecast(point.Position + Vector3.up * 1.5f, threat + Vector3.up * 1.1f,
            LayersMaskController.HighPolyWithTerrainNoGrassMask);
        RememberPlanning(firingLanes, point, threat, enemy.EnemyProfileId, clear);
        return clear;
    }

    // Called in ranked order, after native candidate validation. A depleted budget
    // is pending work, not a rejected route or permission to select a lower rank.
    internal bool InsideBossRoute(CoverPoint point, Vector3 boss, float radius)
    {
        if ((point.Position - boss).sqrMagnitude > radius * radius || !SainRegroupBridge.SameLevel(point.Position, boss)) return false;
        if (bossRoutes.TryGetValue(point, out var cached) && Reuse(cached, point, boss, contact))
            return cached.Clear && cached.Distance <= radius;
        if (!TakeProbe()) { Pending = true; return false; }
        bool complete = SainRegroupBridge.TryGetDistance(point.Position, boss, out float distance);
        RememberPlanning(bossRoutes, point, boss, contact, complete, distance);
        return complete && distance <= radius;
    }

    // Use the native validated route: a cover across a wall may be close in space
    // but require a long trip. No extra path or physics probes for this preference.
    private float RouteDistance(CoverPoint point) =>
        Mathf.Max(point.PathData.PathLength, (point.Position - bot.Position).magnitude);
    internal bool IsNearby(CoverPoint point) => RouteDistance(point) <= nearbyCoverDistance &&
        SainRegroupBridge.SameLevel(point.Position, bot.Position);

    // A defensive preference among native-validated candidates, not permission to advance.
    // Core uses 1.5m for the boss line and a 0.9m destination fire-lane radius.
    // Keep lateral room on either side and do not promote an expensive route or a flank
    // beyond the target. Geometry uses native knowledge, never hidden live coordinates.
    internal bool IsProtective(CoverPoint point)
    {
        if (!rankProtective || point == null || rankEnemyDistance <= 1.5f ||
            !SainRegroupBridge.SameLevel(point.Position, rankBoss) ||
            RouteDistance(point) > protectiveRange ||
            (point.Position - rankBoss).sqrMagnitude > protectiveRange * protectiveRange) return false;
        Vector3 offset = point.Position - rankBoss; offset.y = 0f;
        float forward = Vector3.Dot(offset, rankEnemyDirection);
        float lateralSqr = Mathf.Max(0f, offset.sqrMagnitude - forward * forward);
        return forward > 1.5f && forward < rankEnemyDistance &&
            lateralSqr > 0.9f * 0.9f && lateralSqr <= forward * forward;
    }

    private int Tier(CoverPoint point, Vector3 boss) =>
        (point.Position - boss).magnitude <= SainCoverGeometry.SearchRadius ? 0 : 1;
    private float Score(CoverPoint point, Vector3 boss) =>
        SainCoverGeometry.Score(point.PathData.PathLength, (point.Position - boss).magnitude);
    private void Add(CoverPoint point, Vector3 boss)
    {
        if (point == null || point.Spotted || point.CoverData.IsBad || ranked.Contains(point) ||
            !ValidateCandidate(point, bot.GoalEnemy) ||
            !SainRegroupBridge.IsDestinationAvailable(bot.BotOwner, point.Position)) return;
        float bossDistance = (point.Position - boss).magnitude;
        if (bossDistance < 2f) return;
        if (rankNearby && IsNearby(point)) { ranked.Add(point); return; }
        if (!SainRegroupBridge.SameLevel(point.Position, boss)) return;
        // Ordinary SeekCover tries nearby cover, then player-area cover, then native
        // fallback. Commanded relocation retains its existing bossward candidates.
        if (Tier(point, boss) != 0 && (rankNearby || bossDistance >= (bot.Position - boss).magnitude - 2f)) return;
        ranked.Add(point);
    }
    private bool ValidateCandidate(CoverPoint point, Enemy enemy)
    {
        if (point == null || point.Spotted || point.CoverData.IsBad || enemy?.LastKnownPosition == null) return false;
        if (validation.TryGetValue(point, out Validation cached) && (Time.time < cached.Until || cached.Pass == validationPass) &&
            cached.Enemy == enemy.EnemyProfileId && (cached.Bot - bot.NavMeshPosition).sqrMagnitude < 64f &&
            (cached.Threat - enemy.LastKnownPosition.Value).sqrMagnitude < 64f &&
            (cached.Point - point.Position).sqrMagnitude < 0.01f) return cached.Valid;
        if (!TakeProbe()) { Pending = true; return false; }
        return Validate(point, enemy);
    }
    // Committed cover has its own one-second observation cadence and must be
    // rechecked immediately when that observer asks, independently of selection.
    internal bool Validate(CoverPoint point, Enemy enemy)
    {
        if (point == null || point.Spotted || point.CoverData.IsBad || enemy?.LastKnownPosition == null) return false;
        Vector3 threat = enemy.LastKnownPosition.Value;
        bool valid = analyzer.RecheckCoverPoint(point, threat, (threat - bot.NavMeshPosition).normalized,
            bot.NavMeshPosition, out _);
        Remember(point, enemy, valid);
        return valid;
    }
    private void Remember(CoverPoint point, Enemy enemy, bool valid)
    {
        if (validation.Count >= 128 && !validation.ContainsKey(point)) validation.Clear();
        validation[point] = new Validation { Valid = valid, Until = Time.time + 1f, Pass = validationPass, Bot = bot.NavMeshPosition,
            Threat = enemy.LastKnownPosition.GetValueOrDefault(), Point = point.Position, Enemy = enemy.EnemyProfileId };
    }
    internal void Clear()
    {
        scanned = false; Pending = false; candidates.Clear(); ranked.Clear(); validation.Clear(); firingLanes.Clear(); bossRoutes.Clear();
    }
}

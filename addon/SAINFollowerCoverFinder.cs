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
    private int scanIndex, probeFrame = -1, probes, validationPass;
    internal bool Pending { get; private set; }
    private Comparison<CoverPoint> bossComparison;
    private Vector3 rankBoss;
    private bool scanned;
    private string contact;
    private Vector3 bossAnchor, botAnchor, enemyAnchor;
    internal int LastScanCount { get; private set; }

    internal List<CoverPoint> Find(Enemy enemy, Vector3 boss)
    {
        ranked.Clear(); if (!Pending) validationPass++; Pending = false;
        if (enemy?.LastKnownPosition == null) return ranked;
        Scan(enemy, boss, SainCoverGeometry.SearchRadius, SainRegroupBridge.BossMoveRefreshDistance);
        foreach (CoverPoint point in candidates) Add(point, boss);
        foreach (CoverPoint point in bot.Cover.CoverPoints) Add(point, boss);
        if (Pending) { ranked.Clear(); return ranked; }
        rankBoss = boss;
        ranked.Sort(bossComparison ??= CompareBoss);
        return ranked;
    }

    private void Scan(Enemy enemy, Vector3 boss, float radius, float refresh)
    {
        Vector3 threat = enemy.LastKnownPosition.GetValueOrDefault();
        if (!scanned || contact != enemy.EnemyProfileId || (boss - bossAnchor).sqrMagnitude >= refresh * refresh ||
            (bot.Position - botAnchor).sqrMagnitude >= refresh * refresh || (threat - enemyAnchor).sqrMagnitude >= 64f)
        {
            scanned = true; contact = enemy.EnemyProfileId;
            bossAnchor = boss; botAnchor = bot.Position; enemyAnchor = threat;
            candidates.Clear(); validation.Clear(); LastScanCount = 0; scanIndex = 0;
            colliders.OverlapBoxAndFilter(new SainBotCoverData.BotColliderQueryParams {
                origin = boss + Vector3.up * 0.25f, halfExtents = new Vector3(radius, 5f, radius),
                mask = LayersMaskController.HighPolyWithTerrainNoGrassMask,
                minColliderSize = new Vector3(0.25f, GlobalSettingsClass.Instance.General.Cover.CoverMinHeight, 0.25f),
                maxColliderSize = new Vector3(30f, 30f, 30f)
            });
            colliders.HandleLists(boss);
        }
        // Discovery and validation share a frame budget even if both layers poll.
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
        if (requireFiringLane && Physics.Linecast(point.Position + Vector3.up * 1.5f, threat + Vector3.up * 1.1f,
            LayersMaskController.HighPolyWithTerrainNoGrassMask)) return;
        ranked.Add(point);
    }

    private int Tier(CoverPoint point, Vector3 boss) =>
        (point.Position - boss).magnitude <= SainCoverGeometry.SearchRadius ? 0 : 1;
    private float Score(CoverPoint point, Vector3 boss) =>
        SainCoverGeometry.Score(point.PathData.PathLength, (point.Position - boss).magnitude);
    private void Add(CoverPoint point, Vector3 boss)
    {
        if (point == null || point.Spotted || point.CoverData.IsBad || ranked.Contains(point) ||
            !ValidateCandidate(point, bot.GoalEnemy) || !SainRegroupBridge.SameLevel(point.Position, boss) ||
            !SainRegroupBridge.IsDestinationAvailable(bot.BotOwner, point.Position)) return;
        float bossDistance = (point.Position - boss).magnitude;
        // If the player is outside our reachable cover envelope, accept a safe intermediate
        // step toward them. A cover farther away is left to native survival fallback.
        if (bossDistance < 2f || (Tier(point, boss) != 0 && bossDistance >= (bot.Position - boss).magnitude - 2f)) return;
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
        scanned = false; Pending = false; candidates.Clear(); ranked.Clear(); validation.Clear();
    }
}

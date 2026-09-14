// Follower-local candidate discovery using SAIN 4.5.1's native cover validator.
using System.Collections.Generic;
using EFT;
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
    private bool scanned;
    private string contact;
    private Vector3 bossAnchor, botAnchor, enemyAnchor;
    internal int LastScanCount { get; private set; }

    internal List<CoverPoint> Find(Enemy enemy, Vector3 boss)
    {
        ranked.Clear();
        if (enemy?.LastKnownPosition == null) return ranked;
        Vector3 threat = enemy.LastKnownPosition.Value;
        float refresh = SainRegroupBridge.BossMoveRefreshDistance;
        if (!scanned || contact != enemy.EnemyProfileId || (boss - bossAnchor).sqrMagnitude >= refresh * refresh ||
            (bot.Position - botAnchor).sqrMagnitude >= refresh * refresh || (threat - enemyAnchor).sqrMagnitude >= 64f)
        {
            scanned = true; contact = enemy.EnemyProfileId;
            bossAnchor = boss; botAnchor = bot.Position; enemyAnchor = threat;
            candidates.Clear(); LastScanCount = 0;
            float radius = SainCoverSelectionBridge.SearchRadius;
            colliders.OverlapBoxAndFilter(new SainBotCoverData.BotColliderQueryParams {
                origin = boss + Vector3.up * 0.25f, halfExtents = new Vector3(radius, 5f, radius),
                mask = LayersMaskController.HighPolyWithTerrainNoGrassMask,
                minColliderSize = new Vector3(0.25f, GlobalSettingsClass.Instance.General.Cover.CoverMinHeight, 0.25f),
                maxColliderSize = new Vector3(30f, 30f, 30f)
            });
            colliders.HandleLists(boss);
            // One bounded boss-sector scan, not a per-frame expansion of SAIN's five-point pool.
            foreach (var data in colliders.ValidCollidersList)
            {
                if (LastScanCount >= 32) break;
                if (data.Collider == null) continue;
                LastScanCount++;
                if (analyzer.CheckCreateNewCoverPoint(data.Collider, threat, bot.NavMeshPosition,
                    (threat - bot.NavMeshPosition).normalized, out CoverPoint point, out _)) candidates.Add(point);
            }
        }
        foreach (CoverPoint point in candidates) Add(point, boss);
        foreach (CoverPoint point in bot.Cover.CoverPoints) Add(point, boss);
        ranked.Sort((a, b) => {
            int tier = Tier(a, boss).CompareTo(Tier(b, boss));
            return tier != 0 ? tier : Score(a, boss).CompareTo(Score(b, boss));
        });
        return ranked;
    }

    private int Tier(CoverPoint point, Vector3 boss) =>
        (point.Position - boss).magnitude <= SainCoverSelectionBridge.SearchRadius ? 0 : 1;
    private float Score(CoverPoint point, Vector3 boss) =>
        SainCoverSelectionBridge.Score(point.PathData.PathLength, (point.Position - boss).magnitude);
    private void Add(CoverPoint point, Vector3 boss)
    {
        if (point == null || point.Spotted || point.CoverData.IsBad || ranked.Contains(point) ||
            !Validate(point, bot.GoalEnemy) || !SainRegroupBridge.SameLevel(point.Position, boss) ||
            !SainRegroupBridge.IsDestinationAvailable(bot.BotOwner, point.Position)) return;
        float bossDistance = (point.Position - boss).magnitude;
        // If the player is outside our reachable cover envelope, accept a safe intermediate
        // step toward them. A cover farther away is left to native survival fallback.
        if (bossDistance < 2f || (Tier(point, boss) != 0 && bossDistance >= (bot.Position - boss).magnitude - 2f)) return;
        ranked.Add(point);
    }
    internal bool Validate(CoverPoint point, Enemy enemy)
    {
        if (point == null || point.Spotted || point.CoverData.IsBad || enemy?.LastKnownPosition == null) return false;
        Vector3 threat = enemy.LastKnownPosition.Value;
        return analyzer.RecheckCoverPoint(point, threat, (threat - bot.NavMeshPosition).normalized,
            bot.NavMeshPosition, out _);
    }
    internal void Clear()
    {
        scanned = false; candidates.Clear(); ranked.Clear();
    }
}

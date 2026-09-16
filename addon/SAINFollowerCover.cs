using System;
using System.Collections.Generic;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.Models.Enums;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.SubComponents.CoverFinder;
using UnityEngine;

namespace pitTeam.SAINAddon;

// Shares the core combat contract: choose boss-oriented cover, reach it, then use it
// before ordinary movement may be reconsidered. Real combat/orders still interrupt.
internal sealed class SAINFollowerCover(BotComponent bot)
{
    private readonly SAINFollowerCoverFinder finder = new(bot);
    private CoverPoint selected;
    private bool recovery, hasArrival, claimed, reportedFailure;
    private Vector3 arrival, claimedPosition;
    private float holdUntil, nextValidation, nextSelectionAttempt;
    private Vector3 attemptedBoss, attemptedBot, attemptedThreat;
    private string attemptedEnemy;
    private bool attemptedRegroup;
    private int attemptedNativeCount;
    private string selectionReason = "nativeFallback";
    private float regroupRadius;
    private string regroupEnemy;
    private Vector3 regroupEnemyAnchor;
    private bool reportedRegroupNoCover;
    private BotFollowerPlayer? Follower => BossPlayers.Instance?.GetFollower(bot.BotOwner);
    private readonly Dictionary<Enemy, MedicalCoverProbe> medicalProbes = new();
    private string medicalReason = "notChecked";
    private float medicalCheckedAt = -1f;
    private struct MedicalCoverProbe { internal Vector3 Position, Threat; internal float Until; internal bool Safe; }
    internal bool WaitingForSelection => finder.Pending && !Independent && !Recovery &&
        bot.Cover.CoverInUse == null && bot.Cover.CoverPoint_MovingTo == null &&
        bot.Decision.CurrentCombatDecision == ECombatDecision.SeekCover &&
        bot.Decision.CurrentSelfDecision == ESelfActionType.None &&
        !(bot.GoalEnemy?.IsVisible == true && bot.GoalEnemy.CanShoot);

    // Core's reached-heal-cover contract, using SAIN knowledge and native cover ownership.
    // Eligibility/item selection and execution stay in SAIN/EFT.
    internal bool CanTreatAtCover()
    {
        medicalCheckedAt = Time.time; medicalReason = "coverUnready";
        CoverPoint point = bot.Cover.CoverInUse;
        if (point == null || point.Spotted || point.CoverData.IsBad || bot.Mover.Moving ||
            (point.Position - bot.Position).sqrMagnitude > 1.75f * 1.75f) return false;
        medicalReason = "pressure";
        if (SainRegroupBridge.IsUnderFire(bot.BotOwner) || bot.Medical.TimeSinceShot < 3f ||
            bot.Suppression.IsHeavySuppressed) return false;
        int checkedEnemies = 0;
        foreach (Enemy enemy in bot.EnemyController.KnownEnemies)
        {
            if (enemy == null || !Enemy.IsEnemyActive(enemy) || enemy.EnemyPlayer?.HealthController?.IsAlive != true ||
                (!enemy.Seen && !enemy.Heard)) continue;
            medicalReason = "activeOrUncertainThreat";
            if (++checkedEnemies > 32 || !enemy.WasValid || !enemy.EnemyKnown || enemy.IsVisible || enemy.CanShoot ||
                (enemy.Seen && enemy.TimeSinceSeen < 3f) || !enemy.LastKnownPosition.HasValue) return false;
            Vector3 threat = enemy.LastKnownPosition.Value;
            // Match core's very-close exclusion; hearing does not require a fictitious sight age.
            medicalReason = "closeThreat";
            if ((threat - bot.Position).sqrMagnitude < 17f * 17f) return false;
            if (!medicalProbes.TryGetValue(enemy, out MedicalCoverProbe probe) || Time.time >= probe.Until ||
                (probe.Position - bot.Position).sqrMagnitude > 0.01f || (probe.Threat - threat).sqrMagnitude > 1f)
            {
                if (medicalProbes.Count >= 32 && !medicalProbes.ContainsKey(enemy)) medicalProbes.Clear();
                probe = new MedicalCoverProbe { Position = bot.Position, Threat = threat, Until = Time.time + 0.5f,
                    Safe = pitTeam.Utils.Covers.IsHardCoverFromThreat(bot.Position, threat) };
                medicalProbes[enemy] = probe;
            }
            medicalReason = "exposedCover";
            if (!probe.Safe) return false;
        }
        medicalReason = checkedEnemies > 0 ? "protectedCover" : "noKnownThreat";
        return checkedEnemies > 0;
    }

    private bool Independent => Follower?.CombatIndependent != false;
    private bool Recovery => bot.BotOwner.Memory.IsUnderFire || bot.Medical?.TimeSinceShot < 0.75f ||
        bot.Decision.CurrentCombatDecision == ECombatDecision.Retreat || bot.Decision.CurrentSelfDecision != ESelfActionType.None;

    internal bool TrySelect(bool sprint, out CoverPoint point)
    {
        point = null;
        selectionReason = "nativeFallback";
        if (Independent) return false;
        if (Recovery) { selectionReason = "nativeRecovery"; return false; }
        if (!SainPlayerSquadBridge.TryGetPlayerLeader(bot.BotOwner, out Player player) || player.HealthController?.IsAlive != true) return false;
        bool limitToRegroup = RetainRegroupArea();
        Enemy enemy = bot.GoalEnemy;
        Vector3 threat = enemy?.LastKnownPosition ?? default;
        if (Time.time < nextSelectionAttempt && attemptedNativeCount == bot.Cover.CoverPoints.Count && attemptedEnemy == enemy?.EnemyProfileId && attemptedRegroup == limitToRegroup &&
            (attemptedBoss - player.Position).sqrMagnitude < 4f && (attemptedBot - bot.Position).sqrMagnitude < 4f &&
            (attemptedThreat - threat).sqrMagnitude < 4f) return limitToRegroup;
        foreach (CoverPoint candidate in finder.Find(bot.GoalEnemy, player.Position))
        {
            if (limitToRegroup && !InsideRegroupArea(candidate.Position, player.Position)) continue;
            if (!bot.Mover.GoToCoverPoint(candidate, sprint, ESprintUrgency.High)) continue;
            selectionReason = limitToRegroup ? "regroupCover" : "bossCover";
            reportedRegroupNoCover = false;
            point = candidate;
            return true;
        }
        // Incomplete work is not a failed search and must not choose an outward fallback.
        if (finder.Pending) return true;
        nextSelectionAttempt = Time.time + 0.5f;
        attemptedBoss = player.Position; attemptedBot = bot.Position; attemptedThreat = threat;
        attemptedEnemy = enemy?.EnemyProfileId; attemptedRegroup = limitToRegroup; attemptedNativeCount = bot.Cover.CoverPoints.Count;
        if (limitToRegroup)
        {
            // A native fallback outside the completed envelope would undo regroup again.
            // No valid local cover is a handled empty selection, not a new outward journey.
            selectionReason = "regroupNoCover";
            if (!reportedRegroupNoCover) { Record("regroupNoCover"); reportedRegroupNoCover = true; }
            return true;
        }
        return false;
    }

    internal void RegroupCompleted(float radius)
    {
        Clear();
        // Native SeekCover otherwise reuses CoverInUse before asking for a new point.
        bot.Cover.StopSeekingCover();
        Enemy enemy = bot.GoalEnemy;
        if (enemy?.LastKnownPosition == null) return;
        regroupRadius = radius;
        regroupEnemy = enemy.EnemyProfileId;
        regroupEnemyAnchor = enemy.LastKnownPosition.Value;
        Record("regroupCompleted");
    }

    private bool RetainRegroupArea()
    {
        if (regroupRadius <= 0f) return false;
        Enemy enemy = bot.GoalEnemy;
        if (enemy?.LastKnownPosition == null || enemy.EnemyProfileId != regroupEnemy ||
            (enemy.IsVisible && enemy.CanShoot) ||
            (enemy.LastKnownPosition.Value - regroupEnemyAnchor).sqrMagnitude >= 64f)
        {
            regroupRadius = 0f;
            return false;
        }
        foreach (Enemy known in bot.EnemyController.KnownEnemies)
            if (known != null && Enemy.IsEnemyActive(known) && known.IsVisible && known.CanShoot)
            {
                regroupRadius = 0f;
                return false;
            }
        return true;
    }

    private bool InsideRegroupArea(Vector3 point, Vector3 player)
    {
        float radius = Mathf.Min(regroupRadius, Mathf.Max(2f, SainRegroupBridge.GetTriggerDistance(bot.BotOwner) - 2f));
        return (point - player).sqrMagnitude <= radius * radius &&
            SainRegroupBridge.SameLevel(point, player) &&
            SainRegroupBridge.TryGetDistance(point, player, out float distance) && distance <= radius;
    }

    internal void Selected(CoverPoint point)
    {
        if (Independent || point == null) return;
        ReleaseClaim(); selected = point; recovery = Recovery; nextValidation = 0f;
        Claim(point.Position);
        Record("selected");
    }

    internal void Observe()
    {
        try { ObserveCore(); }
        catch (Exception ex)
        {
            Clear();
            if (!reportedFailure)
            {
                reportedFailure = true;
                pitTeam.Modules.Logger.LogError($"[SAIN] Cover observation failed for {bot.BotOwner.ProfileId}; retaining native selection. {ex}");
            }
        }
    }

    private void ObserveCore()
    {
        if (Independent) { Clear(); return; }
        RetainRegroupArea();
        if (hasArrival && (bot.Position - arrival).sqrMagnitude > 16f) { hasArrival = false; holdUntil = 0f; }
        if (selected == null) return;
        bool ownsCover = ReferenceEquals(bot.Cover.CoverInUse, selected) || ReferenceEquals(bot.Cover.CoverPoint_MovingTo, selected);
        if (!ownsCover) { ReleaseClaim(); return; }
        if (Time.time >= nextValidation)
        {
            nextValidation = Time.time + 1f;
            if (!finder.Validate(selected, bot.GoalEnemy))
            {
                // Native UpdateCover can retain a bad moving target, especially within its
                // close-cover latch. Release only this cover and its matching path, then let
                // native selection recover; never cancel another action's destination.
                selected.CoverData.IsBad = true; holdUntil = 0f; ReleaseClaim();
                var path = bot.Mover.ActivePath;
                if (path != null && (path.Destination - selected.Position).sqrMagnitude <= 4f) bot.Mover.Stop();
                bot.Cover.StopSeekingCover();
                Record("invalidated"); selected = null; return;
            }
            Claim(selected.Position);
        }
        if (ReferenceEquals(bot.Cover.CoverInUse, selected) &&
            (bot.Position - selected.Position).sqrMagnitude <= 1.75f * 1.75f &&
            (!hasArrival || (selected.Position - arrival).sqrMagnitude > 4f))
        {
            hasArrival = true; arrival = selected.Position;
            holdUntil = Time.time + SainCoverGeometry.ArrivalHoldSeconds(recovery);
            Record("arrivalHold");
        }
    }

    internal bool HoldsArrival(Enemy enemy)
    {
        Observe();
        if (!hasArrival || Time.time >= holdUntil || selected == null || selected.Spotted || selected.CoverData.IsBad ||
            !ReferenceEquals(bot.Cover.CoverInUse, selected) || Follower?.TryGetActiveCommand(out _, out _) == true) return false;
        if (!recovery && Recovery) return false;
        foreach (Enemy known in bot.EnemyController.KnownEnemies)
            if (known != null && Enemy.IsEnemyActive(known) && known.IsVisible && known.CanShoot) return false;
        return enemy == null || !enemy.IsVisible || !enemy.CanShoot;
    }

    internal bool TryHoldDecision(Enemy enemy, ECombatDecision solo, ESquadDecision squad, ESelfActionType self) =>
        squad == ESquadDecision.None && self == ESelfActionType.None &&
        (solo == ECombatDecision.Search || solo == ECombatDecision.MoveToEngage || solo == ECombatDecision.ShiftCover) && HoldsArrival(enemy);

    internal void EndArrivalHold(string reason)
    {
        regroupRadius = 0f;
        nextSelectionAttempt = 0f;
        holdUntil = 0f;
        Record(reason);
    }

    internal void Clear()
    {
        ReleaseClaim(); selected = null; hasArrival = false; holdUntil = 0f; nextSelectionAttempt = 0f; finder.Clear();
        regroupRadius = 0f; regroupEnemy = null; reportedRegroupNoCover = false; medicalProbes.Clear(); medicalCheckedAt = -1f; medicalReason = "notChecked";
    }
    private void Claim(Vector3 position)
    {
        if (claimed && (claimedPosition - position).sqrMagnitude > 0.01f) ReleaseClaim();
        SainRegroupBridge.Claim(bot.BotOwner, position); claimedPosition = position; claimed = true;
    }
    private void ReleaseClaim()
    {
        if (claimed) SainRegroupBridge.Release(bot.BotOwner, claimedPosition);
        claimed = false;
    }
    internal object Snapshot => new { reason = selectionReason, arrivalHoldRemaining = Mathf.Max(0f, holdUntil - Time.time),
        recovery, scanCount = finder.LastScanCount, regroupRadius,
        medicalCover = new { reason = medicalReason, checkedAt = medicalCheckedAt } };
    private void Record(string reason)
    {
        if (SainCombatRecorderBridge.IsRecording)
            SainCombatRecorderBridge.RecordEvent(bot.BotOwner, "sainCover", new { reason, policy = selectionReason, recovery,
                position = selected == null ? null : (object)new { x = selected.Position.x, y = selected.Position.y, z = selected.Position.z },
                holdUntil, scanCount = finder.LastScanCount });
    }
}

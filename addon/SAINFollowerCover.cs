using System;
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
    private float holdUntil, nextValidation;
    private string selectionReason = "nativeFallback";
    private BotFollowerPlayer? Follower => BossPlayers.Instance?.GetFollower(bot.BotOwner);
    private bool Independent => Follower?.CombatIndependent != false;
    private bool Recovery => bot.BotOwner.Memory.IsUnderFire || bot.Medical?.TimeSinceShot < 0.75f ||
        bot.Decision.CurrentCombatDecision == ECombatDecision.Retreat || bot.Decision.CurrentSelfDecision != ESelfActionType.None;

    internal bool TrySelect(bool sprint, out object point)
    {
        point = null;
        selectionReason = "nativeFallback";
        if (Independent) return false;
        if (Recovery) { selectionReason = "nativeRecovery"; return false; }
        if (!SainPlayerSquadBridge.TryGetPlayerLeader(bot.BotOwner, out Player player) || player.HealthController?.IsAlive != true) return false;
        foreach (CoverPoint candidate in finder.Find(bot.GoalEnemy, player.Position))
        {
            if (!bot.Mover.GoToCoverPoint(candidate, sprint, ESprintUrgency.High)) continue;
            selectionReason = "bossCover";
            point = candidate;
            return true;
        }
        return false;
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
            holdUntil = Time.time + SainCoverSelectionBridge.ArrivalHoldSeconds(recovery);
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
        holdUntil = 0f;
        Record(reason);
    }

    internal void Clear()
    {
        ReleaseClaim(); selected = null; hasArrival = false; holdUntil = 0f; finder.Clear();
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
        recovery, scanCount = finder.LastScanCount };
    private void Record(string reason)
    {
        if (SainCombatRecorderBridge.IsRecording)
            SainCombatRecorderBridge.RecordEvent(bot.BotOwner, "sainCover", new { reason, policy = selectionReason, recovery,
                position = selected == null ? null : (object)new { x = selected.Position.x, y = selected.Position.y, z = selected.Position.z },
                holdUntil, scanCount = finder.LastScanCount });
    }
}

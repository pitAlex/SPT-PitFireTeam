using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent;
using SAIN.SAINComponent.Classes;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.SubComponents.CoverFinder;
using UnityEngine;

namespace pitTeam.SAINAddon;

internal enum SAINRegroupMode { None, Auto, Command }

// A follower-local objective shared by the squad provider and its regroup action.
// Native SAIN still decides medical, grenade and dogfight interruptions and publishes decisions.
internal sealed class SAINFollowerRegroupObjective(BotComponent bot) : BotBase(bot)
{
    internal SAINRegroupMode Mode { get; private set; }
    internal bool Tight { get; private set; }
    internal bool Settling => settleUntil > 0f;
    internal bool Active => Mode != SAINRegroupMode.None;
    // Last real policy evaluation only; snapshot readers never run the decision provider.
    internal string AutoReason { get; private set; } = "notEvaluated";
    internal float AutoCheckedAt { get; private set; } = -1f;
    internal float? AutoDistance { get; private set; }
    internal float? AutoTrigger { get; private set; }
    private float settleUntil, nextDistanceCheck, distance = float.PositiveInfinity, nextTargetAttempt;
    private bool completePath, hasTarget;
    private Vector3 measuredPlayer, measuredBot, target, targetPlayer;
    private CoverPoint cover;
    private string movement;
    private float autoRetryAt;

    private BotFollowerPlayer? Follower => BossPlayers.Instance?.GetFollower(BotOwner);
    private bool Interrupted => SainAddonBridge.IsUsingMedical(BotOwner) ||
        Bot.Decision.CurrentSelfDecision != ESelfActionType.None ||
        Bot.Decision.CurrentCombatDecision == ECombatDecision.DogFight ||
        Bot.Decision.CurrentCombatDecision == ECombatDecision.MeleeAttack ||
        Bot.Decision.CurrentCombatDecision == ECombatDecision.ThrowGrenade ||
        Bot.Decision.CurrentCombatDecision == ECombatDecision.AvoidGrenade;

    // Also runs from layer handoff checks: orders can break a native cover-movement latch,
    // and completion/cancellation cannot depend on SAIN calling the squad provider that tick.
    internal void Observe()
    {
        var follower = Follower;
        if (follower == null || BotOwner.IsDead || !SainAddonBridge.IsAddonTacticSelected(BotOwner) ||
            !SainPlayerSquadBridge.TryGetPlayerLeader(BotOwner, out Player player) || player.HealthController?.IsAlive != true ||
            !SAINFollowerCombatHandoff.HasLiveEnemy(Bot))
        {
            Clear("combatEnded");
            return;
        }

        follower.TryGetActiveCommand(out FollowerCommandType command, out _);
        if (command != FollowerCommandType.None && command != FollowerCommandType.RegroupNearBoss)
        {
            Clear("replacementOrder");
            return; // Leave that order to its owner; automatic regroup must not consume it.
        }
        if (Mode == SAINRegroupMode.Auto && follower.CombatIndependent) Clear("independent");
        if (Interrupted) return;

        if (command == FollowerCommandType.RegroupNearBoss)
        {
            bool tight = follower.TightRegroupRequested;
            follower.ClearOrderedPushTargetLock("SAIN:Regroup");
            follower.ClearCommand("SAIN:ConsumeRegroup");
            Begin(SAINRegroupMode.Command, tight);
            follower.SetCombatRegroupBossAnchor(true);
            // Cancels only this follower's old action/path through native lifecycle APIs.
            Bot.Mover.Stop();
            Bot.Decision.ResetDecisions(false);
        }

        if (!Active) return;
        Measure(player.Position);
        if (hasTarget && cover != null && (cover.Spotted || cover.CoverData.IsBad || !PersonalContactHot(2.5f) ||
            (targetPlayer - player.Position).sqrMagnitude > SainRegroupBridge.BossMoveRefreshDistance * SainRegroupBridge.BossMoveRefreshDistance))
            ReleaseTarget();
        if (Settling)
        {
            if (!AtPlayer(player.Position)) { settleUntil = 0f; ReleaseTarget(); }
            else if (Time.time >= settleUntil || PersonalContactHot(2.5f)) Complete("arrived");
            return;
        }
        if (AtPlayer(player.Position) && (cover == null || !hasTarget || (target - BotOwner.Position).sqrMagnitude <= 4f))
        {
            // A committed cover move must reach cover; entering the player's radius alone
            // is not arrival at that destination. Reached the shared regroup envelope. Return immediately to combat under
            // pressure; otherwise use the core's short arrival think window.
            if (PersonalContactHot(2.5f)) { Complete("arrivedHot"); return; }
            ReleaseTarget();
            settleUntil = Time.time + 1.5f;
            SainRegroupBridge.Record(BotOwner, Mode.ToString(), "arrivalSettle");
        }
    }

    internal bool GetDecision()
    {
        Observe();
        if (Interrupted || !SAINFollowerCombatHandoff.HasLiveEnemy(Bot)) return false;
        return Active;
    }

    // Main-mod regroup is a next-decision fallback after useful combat/commitments.
    // The squad provider runs BEFORE native solo selection, so only the publication
    // bridge may start Auto, using that tick's result without evaluating SAIN twice.
    internal bool TryBeginAuto(Enemy enemy, ECombatDecision solo, ESquadDecision squad, ESelfActionType self)
    {
        AutoCheckedAt = Time.time; AutoDistance = AutoTrigger = null;
        if (Active) return RejectAuto("active");
        if (enemy == null || !Enemy.IsEnemyActive(enemy) || enemy.EnemyPlayer?.HealthController?.IsAlive != true)
            return RejectAuto("enemyUnavailable");
        if (self != ESelfActionType.None || Interrupted) return RejectAuto("survival");
        if (squad != ESquadDecision.None || Bot.Decision.CurrentSquadDecision != ESquadDecision.None)
            return RejectAuto("squadAction");
        if (SAINFollowerRuntime.GetCover(BotOwner)?.HoldsArrival(enemy) == true) return RejectAuto("arrivalHold");
        var attempt = SAINFollowerRuntime.GetEngageAttempt(BotOwner);
        var push = SAINFollowerRuntime.GetPush(BotOwner);
        // An unfinished order owns its leg/contact grace. An exhausted same-target order
        // has no advance left to protect; retain its failure while allowing escort recovery.
        if (push?.Ordered == true && (!push.Exhausted || push.AwaitingTarget || push.EnemyId != enemy.EnemyProfileId))
            return RejectAuto("orderedPush");
        bool failedPush = solo == ECombatDecision.SeekCover && push is { Exhausted: true } &&
            push.EnemyId == enemy.EnemyProfileId && Bot.Cover.CoverPoint_MovingTo == null &&
            (!Bot.Mover.Moving || Bot.CurrentAction is SAINFollowerMoveToEngageAction);
        bool failedEngage = failedPush || solo == ECombatDecision.MoveToEngage && attempt?.FailedFor(enemy) == true &&
            (Bot.Decision.CurrentCombatDecision == ECombatDecision.MoveToEngage ||
             (!Bot.Mover.Moving && Bot.Cover.CoverPoint_MovingTo == null));
        if (!failedEngage && (solo != ECombatDecision.SeekCover ||
            Bot.Decision.CurrentCombatDecision != ECombatDecision.SeekCover)) return RejectAuto("combatAction");
        if (!failedEngage && (Bot.Mover.Moving || Bot.Cover.CoverPoint_MovingTo != null))
            return RejectAuto("coverTravel");

        // SeekCover also means "find/move to cover". Let the action attempt it first;
        // only an established passive hold or exhausted cover search is a fallback.
        var coverState = Bot.Cover.CoverSeekingState;
        if (!failedEngage && coverState != ECoverSeekingState.NoCover && coverState != ECoverSeekingState.HoldInCover)
            return RejectAuto("coverSelection");
        if (!failedEngage && coverState == ECoverSeekingState.HoldInCover &&
            (Bot.Cover.CoverInUse == null || Bot.Cover.CoverInUse.Spotted || Bot.Cover.CoverInUse.CoverData.IsBad))
            return RejectAuto("coverRecovery");
        // Native LOS/shoot rays describe geometry, not accepted personal sight. Preserve
        // real firing opportunities; passive non-shootable sight uses Core's bounded grace.
        if (enemy.IsVisible && enemy.CanShoot) return RejectAuto("firingOpportunity");
        foreach (Enemy known in Bot.EnemyController.KnownEnemies)
            if (known != null && Enemy.IsEnemyActive(known) && known.EnemyPlayer?.HealthController?.IsAlive == true &&
                known.IsVisible && known.CanShoot) return RejectAuto("knownFiringOpportunity");
        var follower = Follower;
        if (follower == null) return RejectAuto("followerUnavailable");
        if (follower.CombatIndependent) return RejectAuto("independent");
        if (Time.time < autoRetryAt) return RejectAuto("retryDelay");
        if (follower.TryGetActiveCommand(out _, out _)) return RejectAuto("pendingOrder");
        if (!SainPlayerSquadBridge.TryGetPlayerLeader(BotOwner, out Player player) || player.HealthController?.IsAlive != true)
            return RejectAuto("leaderUnavailable");
        Measure(player.Position);
        float trigger = SainRegroupBridge.GetTriggerDistance(BotOwner);
        AutoDistance = completePath ? distance : null; AutoTrigger = trigger;
        if (!completePath) return RejectAuto("incompletePlayerPath");
        if (distance <= trigger) return RejectAuto("insidePlayerRadius");
        if (SainRegroupBridge.SameLevel(BotOwner.Position, player.Position) &&
            SainRegroupBridge.IsUrbanDetour((player.Position - BotOwner.Position).magnitude, distance))
            return RejectAuto("urbanDetour");
        // Like core escort regroup, retain four seconds of personal fight grace except
        // beyond the 1.6x extreme-distance boundary. Direct orders bypass this gate.
        if (distance < trigger * 1.6f && HotContact(4f)) return RejectAuto("recentFight");
        string reason = failedPush ? "pushExhausted" : failedEngage ? "engage." + attempt.Failure :
            coverState == ECoverSeekingState.NoCover ? "passiveNoCover" : "passiveCoverHold";
        AutoReason = reason;
        Begin(SAINRegroupMode.Auto, false, reason);
        return true;
    }

    private bool RejectAuto(string reason)
    {
        AutoReason = reason;
        return false;
    }

    private void Begin(SAINRegroupMode mode, bool tight, string reason = "activate")
    {
        ReleaseTarget();
        Mode = mode; Tight = tight; settleUntil = 0f; nextDistanceCheck = 0f; movement = null;
        SainRegroupBridge.Record(BotOwner, Mode.ToString(), tight ? "activateTight" : reason);
    }

    internal void Clear(string reason)
    {
        if (!Active) return;
        ReleaseTarget();
        Mode = SAINRegroupMode.None; Tight = false; settleUntil = 0f; movement = null;
        Follower?.SetCombatRegroupBossAnchor(false);
        SainRegroupBridge.Record(BotOwner, "None", reason);
    }

    internal void Complete(string reason)
    {
        if (Active) SAINFollowerRuntime.GetCover(BotOwner)?.RegroupCompleted(CompleteDistance);
        Clear(reason);
        autoRetryAt = Time.time + 2f;
    }

    private void Measure(Vector3 player)
    {
        if (Time.time < nextDistanceCheck && (player - measuredPlayer).sqrMagnitude < 1f &&
            (BotOwner.Position - measuredBot).sqrMagnitude < 1f) return;
        measuredPlayer = player; measuredBot = BotOwner.Position; nextDistanceCheck = Time.time + 0.5f;
        completePath = SainRegroupBridge.TryGetDistance(BotOwner.Position, player, out distance);
    }

    // Shared with push assessment; measuring does not activate regroup.
    internal bool TryGetPlayerDistance(Vector3 player, out float result)
    {
        Measure(player); result = distance; return completePath;
    }

    private float CompleteDistance => Mode == SAINRegroupMode.Auto
        ? Mathf.Min(SainRegroupBridge.GetCompleteDistance(BotOwner, false), Mathf.Max(2f, SainRegroupBridge.GetTriggerDistance(BotOwner) - 2f))
        : SainRegroupBridge.GetCompleteDistance(BotOwner, Tight);
    private bool AtPlayer(Vector3 player) => completePath && distance <= CompleteDistance && SainRegroupBridge.SameLevel(BotOwner.Position, player);

    // Gait follows Core's personal-sight clock and is rechecked during movement.
    // Native LOS, our own shots and shared knowledge must not renew withdrawal.
    internal bool PersonalContactHot(float seconds)
    {
        if (PersonallySeen(Bot.GoalEnemy, seconds)) return true;
        foreach (Enemy enemy in Bot.EnemyController.KnownEnemies)
            if (PersonallySeen(enemy, seconds)) return true;
        return false;
    }
    private static bool PersonallySeen(Enemy enemy, float seconds) => enemy != null &&
        Enemy.IsEnemyActive(enemy) && enemy.EnemyPlayer?.HealthController?.IsAlive == true &&
        (enemy.IsVisible || enemy.Seen && enemy.TimeSinceSeen < seconds);

    internal bool HotContact(float seconds)
    {
        if (SainRegroupBridge.IsUnderFire(BotOwner)) return true;
        float lastShot = SainRegroupBridge.LastShotTime(BotOwner);
        if (lastShot > 0f && Time.time - lastShot <= seconds) return true;
        Enemy enemy = Bot.GoalEnemy;
        if (enemy != null && Enemy.IsEnemyActive(enemy) && enemy.EnemyPlayer?.HealthController?.IsAlive == true &&
            (enemy.IsVisible || (enemy.Seen && enemy.TimeSinceSeen <= seconds))) return true;
        foreach (Enemy known in Bot.EnemyController.KnownEnemies)
            if (known != null && Enemy.IsEnemyActive(known) && known.EnemyPlayer?.HealthController?.IsAlive == true &&
                (known.IsVisible || known.Seen && known.TimeSinceSeen <= seconds)) return true;
        return false;
    }

    internal bool TryGetTarget(out Vector3 destination, out bool sprint)
    {
        destination = default; sprint = false;
        Observe();
        if (!Active || Settling || Interrupted || !SainPlayerSquadBridge.TryGetPlayerLeader(BotOwner, out Player player)) return false;
        bool hot = PersonalContactHot(2.5f);
        float refresh = SainRegroupBridge.BossMoveRefreshDistance;
        if (hasTarget && ((targetPlayer - player.Position).sqrMagnitude > refresh * refresh ||
            (target - BotOwner.Position).sqrMagnitude <= 4f ||
            (cover != null && (cover.Spotted || cover.CoverData.IsBad || !hot)))) ReleaseTarget();
        if (!hasTarget)
        {
            if (Time.time < nextTargetAttempt) return false;
            nextTargetAttempt = Time.time + 0.75f;
            float best = float.PositiveInfinity;
            if (hot && !Tight)
            {
                foreach (CoverPoint candidate in Bot.Cover.CoverPoints)
                {
                    if (candidate == null || candidate.Spotted || candidate.CoverData.IsBad ||
                        !SainRegroupBridge.SameLevel(candidate.Position, player.Position) ||
                        (candidate.Position - player.Position).magnitude > CompleteDistance ||
                        (candidate.Position - BotOwner.Position).sqrMagnitude <= 4f ||
                        !SainRegroupBridge.IsDestinationAvailable(BotOwner, candidate.Position) ||
                        !SainRegroupBridge.TryGetDistance(BotOwner.Position, candidate.Position, out float path) ||
                        path >= best || path > distance + CompleteDistance) continue;
                    cover = candidate; target = candidate.Position; best = path; hasTarget = true;
                }
            }
            if (!hasTarget) hasTarget = SainRegroupBridge.TrySpreadDestination(BotOwner, player.Position, Tight, out target);
            if (!hasTarget) { ReportMovement("noCompletePath"); return false; }
            targetPlayer = player.Position;
        }
        SainRegroupBridge.Claim(BotOwner, target);
        destination = target;
        sprint = !hot && (target - BotOwner.Position).sqrMagnitude > 100f;
        ReportMovement(sprint ? "run" : hot ? "withdraw" : "walk");
        return true;
    }

    internal void PathFailed()
    {
        ReleaseTarget(); nextTargetAttempt = Time.time + 0.75f;
        ReportMovement("pathRejected");
    }

    internal void ReleaseTarget()
    {
        if (hasTarget) SainRegroupBridge.Release(BotOwner, target);
        hasTarget = false; cover = null; nextTargetAttempt = 0f;
    }

    private void ReportMovement(string next)
    {
        if (movement == next) return;
        movement = next;
        SainRegroupBridge.Record(BotOwner, Mode.ToString(), next);
    }
}

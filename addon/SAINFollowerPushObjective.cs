using EFT;
using pitTeam.Components;
using pitTeam.BigBrain;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.SubComponents.CoverFinder;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.SAINAddon;

internal enum SAINPushMode { None, Automatic, Ordered }
internal enum SAINPushPhase { None, Approach, Pressure, Recovery, Assessing, Exhausted }

// Core-style intent/commitment, executed by SAIN MoveToEngage, StandAndShoot,
// SeekCover and native urgent actions. Never promotes a hidden live transform.
internal sealed class SAINFollowerPushObjective(BotComponent bot)
{
    internal SAINPushMode Mode { get; private set; }
    internal SAINPushPhase Phase { get; private set; }
    internal bool Active => Mode != SAINPushMode.None;
    internal bool Ordered => Mode == SAINPushMode.Ordered;
    internal bool AwaitingTarget => Ordered && !targetBound && Time.time < bindUntil;
    internal bool Exhausted => Phase == SAINPushPhase.Exhausted;
    internal bool OwnsMovement { get; private set; }
    internal bool NativeEngagementAllowed { get; private set; }
    internal bool HoldsPosition => Active && Phase == SAINPushPhase.Pressure && target == bot.GoalEnemy;
    internal string EnemyId { get; private set; }
    internal string Reason { get; private set; }
    internal Vector3? Destination { get; private set; }
    private readonly SAINFollowerCoverFinder finder = new(bot);
    private readonly SAINFollowerPushAssessment assessment = new(bot);
    private bool riskHeld;
    private float riskRetryAt;
    private string lastRiskSignature;
    private Enemy? target;
    private CoverPoint cover;
    private Vector3 anchor, progress;
    private float nextPlan, holdUntil, recoveryUntil, lastTick = -1f, activeSeconds, stalledSeconds, nextValidation;
    private bool pressureLatch, targetBound;
    private float bindUntil;
    private BotFollowerPlayer? Follower => BossPlayers.Instance?.GetFollower(bot.BotOwner);

    internal void BeginOrdered()
    {
        Follower?.TryConsumeOrderedPushCancelRequest(out _);
        string id = bot.BotOwner.Memory?.GoalEnemy?.ProfileId;
        if (string.IsNullOrEmpty(id)) return;
        if (Active && EnemyId == id) { Mode = SAINPushMode.Ordered; Record("orderedAgain"); return; }
        Begin(SAINPushMode.Ordered, id);
    }
    private void Begin(SAINPushMode mode, string id)
    {
        Clear("replaced"); Mode = mode; EnemyId = id; nextPlan = 0f;
        target = FindTarget(); targetBound = target != null; bindUntil = Time.time + 3f;
        anchor = target?.LastKnownPosition ?? bot.Position;
        Phase = SAINPushPhase.Approach; Record("begin");
    }
    private Enemy? FindTarget()
    {
        if (bot.GoalEnemy?.EnemyProfileId == EnemyId && Valid(bot.GoalEnemy)) return bot.GoalEnemy;
        foreach (Enemy enemy in bot.EnemyController.KnownEnemies)
            if (enemy?.EnemyProfileId == EnemyId && Valid(enemy)) return enemy;
        return null;
    }
    private static bool Valid(Enemy? enemy) => enemy != null && enemy.WasValid && enemy.EnemyKnown &&
        Enemy.IsEnemyActive(enemy) && enemy.EnemyPlayer?.HealthController?.IsAlive == true && enemy.LastKnownPosition.HasValue && Finite(enemy.LastKnownPosition.Value);
    private static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) &&
        !float.IsNaN(p.y) && !float.IsInfinity(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.z);

    internal void Observe()
    {
        if (!Active) return;
        var follower = Follower;
        if (follower == null || bot.IsDead || !SainAddonBridge.IsSainManSelected(bot.BotOwner) ||
            !SAINFollowerCombatHandoff.AllowsEnemyCombat(bot.BotOwner)) { Clear("combatEnded"); return; }
        if (follower.TryConsumeOrderedPushCancelRequest(out string cancellation))
        { Clear(cancellation); return; }
        if (follower.TryGetActiveCommand(out _, out _) ||
            (Ordered && (!follower.IsTemporaryCombatAggressionOverrideActive || follower.EffectiveCombatAggression < 100f)) ||
            (!Ordered && (follower.CombatIndependent || follower.EffectiveCombatAggression <= 0f)))
        { Clear("replacementOrder"); return; }
        target = FindTarget();
        if (target == null)
        { if (targetBound || Time.time >= bindUntil) Clear("targetLost"); return; }
        if (!targetBound) { targetBound = true; anchor = target.LastKnownPosition.GetValueOrDefault(); }
        Vector3 known = target.LastKnownPosition.GetValueOrDefault();
        if ((known - anchor).sqrMagnitude >= 64f || (Exhausted && target.IsVisible && target.CanShoot))
        {
            ReleaseDestination(); anchor = known; Phase = SAINPushPhase.Approach; nextPlan = 0f; Record("newContact");
        }
    }

    // Runs at SelectEnemy's result boundary, before SAIN publishes the chosen enemy.
    // Native close combat, visible contacts and recent attackers retain priority.
    internal Enemy? PreferEnemy(Enemy? native)
    {
        Observe();
        if (!Active || target == null || native == null || native == target ||
            !bot.EnemyController.KnownEnemies.Contains(target) || native.IsVisible || native.CanShoot ||
            bot.Decision.DogFightDecision.DogFightActive || bot.Medical.TimeSinceShot < 2f ||
            SainRegroupBridge.IsUnderFire(bot.BotOwner) || SainAddonBridge.IsUsingMedical(bot.BotOwner)) return native;
        return target;
    }

    internal bool GetDecision(Enemy enemy, ECombatDecision solo, ESquadDecision squad, ESelfActionType self,
        bool regroupActive, out ECombatDecision result)
    {
        result = solo; OwnsMovement = false; NativeEngagementAllowed = false;
        Observe();
        bool urgent = self != ESelfActionType.None || SainAddonBridge.IsUsingMedical(bot.BotOwner) ||
            solo == ECombatDecision.AvoidGrenade || solo == ECombatDecision.ThrowGrenade ||
            solo == ECombatDecision.DogFight || solo == ECombatDecision.MeleeAttack ||
            solo == ECombatDecision.FightZombies || solo == ECombatDecision.Retreat;
        if (urgent || regroupActive) { Pause(); return false; }
        bool approach = (squad == ESquadDecision.None && (solo == ECombatDecision.Search || solo == ECombatDecision.RushEnemy)) ||
            squad == ESquadDecision.PushSuppressedEnemy;
        if (!Active && approach && Valid(enemy) && Follower?.CombatIndependent == false &&
            Follower.EffectiveCombatAggression > 0f && !Follower.TryGetActiveCommand(out _, out _) &&
            SAINFollowerRuntime.GetCover(bot.BotOwner)?.HoldsArrival(enemy) != true)
            Begin(SAINPushMode.Automatic, enemy.EnemyProfileId);
        if (!Active || enemy != target)
        { NativeEngagementAllowed = solo == ECombatDecision.MoveToEngage; Pause(); return false; }
        if (!Ordered && squad != ESquadDecision.None && squad != ESquadDecision.PushSuppressedEnemy)
        { Pause(); return false; }
        if (solo == ECombatDecision.StandAndShoot || solo == ECombatDecision.ShootDistantEnemy)
        { Pause(); return false; }
        if (enemy.IsVisible && enemy.CanShoot)
        {
            Pause(); result = ECombatDecision.StandAndShoot; return true;
        }
        if (Exhausted) { Pause(); result = ECombatDecision.SeekCover; return true; }
        assessment.Evaluate(enemy, Follower?.EffectiveCombatAggression ?? 50f, Follower?.CombatIndependent == true);
        string signature = $"{assessment.Reason}|{assessment.EnemyCount}|{assessment.Cautious}";
        if (signature != lastRiskSignature) { lastRiskSignature = signature; Record("risk." + assessment.Reason); }
        bool pressured = assessment.SafetyBlocked || assessment.WeaponBlocked;
        if (pressured && !pressureLatch)
        { recoveryUntil = Time.time + 3f; ReleaseDestination(); Phase = SAINPushPhase.Recovery; Record("pressureRecovery"); }
        pressureLatch = pressured;
        if (pressured || Time.time < recoveryUntil)
        { Pause(); result = ECombatDecision.SeekCover; return true; }
        if (Phase == SAINPushPhase.Recovery)
        {
            if ((bot.Mover.Moving && bot.Cover.CoverPoint_MovingTo != null) ||
                SAINFollowerRuntime.GetCover(bot.BotOwner)?.HoldsArrival(enemy) == true)
            { Pause(); result = ECombatDecision.SeekCover; return true; }
            Phase = SAINPushPhase.Approach; nextPlan = 0f; Record("resume");
        }
        // Reassess ordinary risk between movement legs. Medical/weapon safety above
        // can interrupt immediately; a score fluctuation cannot churn a committed path.
        bool betweenLegs = !Destination.HasValue || Phase == SAINPushPhase.Pressure && Time.time >= holdUntil;
        if (!Ordered && betweenLegs && !assessment.AllowsAutomatic)
        {
            HoldForAssessment(assessment.Reason); result = ECombatDecision.SeekCover; return true;
        }
        if (riskHeld)
        {
            if ((bot.Mover.Moving && bot.CurrentAction is not SAINFollowerMoveToEngageAction) ||
                SAINFollowerRuntime.GetCover(bot.BotOwner)?.HoldsArrival(enemy) == true)
            { Pause(); result = ECombatDecision.SeekCover; return true; }
            if (Time.time < riskRetryAt) { Pause(); result = ECombatDecision.SeekCover; return true; }
            riskHeld = false; Phase = SAINPushPhase.Approach; nextPlan = 0f;
        }
        // Preserve the separate native unreachable/sniper attempt and its failure latch.
        if (SAINFollowerRuntime.GetEngageAttempt(bot.BotOwner)?.FailedFor(enemy) == true)
        { Fail("firingPositionExhausted"); result = ECombatDecision.SeekCover; return true; }
        if (enemy.Path.PathToEnemyStatus != NavMeshPathStatus.PathComplete)
        { NativeEngagementAllowed = solo == ECombatDecision.MoveToEngage; Pause(); return false; }
        if (Destination.HasValue && cover != null && Time.time >= nextValidation)
        {
            nextValidation = Time.time + 1f;
            if (!finder.Validate(cover, enemy)) { Fail("coverInvalidated"); result = ECombatDecision.SeekCover; return true; }
        }
        if (Destination.HasValue && (Destination.Value - bot.Position).sqrMagnitude <= 4f && Phase != SAINPushPhase.Pressure)
        { Phase = SAINPushPhase.Pressure; holdUntil = Time.time + 3f; Pause(); Record("arrivalHold"); }
        if (Phase == SAINPushPhase.Pressure)
        {
            if (Time.time < holdUntil) { result = ECombatDecision.StandAndShoot; return true; }
            if ((enemy.LastKnownPosition.GetValueOrDefault() - bot.Position).sqrMagnitude <= 9f)
            { Fail("lastKnownReached"); result = ECombatDecision.SeekCover; return true; }
            ReleaseDestination(); Phase = SAINPushPhase.Approach;
        }
        if (Destination.HasValue && cover == null && Phase == SAINPushPhase.Approach && Time.time >= nextPlan)
        {
            nextPlan = Time.time + 2f;
            foreach (CoverPoint candidate in finder.FindForward(enemy))
            {
                ReleaseDestination(); cover = candidate; Commit(candidate.Position, "forwardCoverAvailable"); break;
            }
        }
        if (!Destination.HasValue && Time.time >= nextPlan)
        {
            nextPlan = Time.time + 2f;
            Plan(enemy);
        }
        if (!Destination.HasValue) { result = Exhausted || riskHeld ? ECombatDecision.SeekCover : ECombatDecision.StandAndShoot; return true; }
        OwnsMovement = true; result = ECombatDecision.MoveToEngage; return true;
    }

    private void Plan(Enemy enemy)
    {
        foreach (CoverPoint candidate in finder.FindForward(enemy))
        { cover = candidate; Commit(candidate.Position, "forwardFiringCover"); return; }
        if (assessment.Cautious)
        {
            foreach (CoverPoint candidate in finder.FindForward(enemy, requireFiringLane: false))
            { cover = candidate; Commit(candidate.Position, "cautiousApproachCover"); return; }
            if (!Ordered && !enemy.IsVisible) { HoldForAssessment("noCoveredApproach"); return; }
        }
        Vector3 known = enemy.LastKnownPosition.GetValueOrDefault();
        Vector3 direction = known - bot.Position;
        Vector3 provisional = bot.Position + direction.normalized * Mathf.Min(20f, direction.magnitude);
        if (NavMesh.SamplePosition(provisional, out NavMeshHit hit, 2f, NavMesh.AllAreas) &&
            SainRegroupBridge.TryGetDistance(bot.Position, hit.position, out float distance) && distance <= FollowerPushGeometry.MaxForwardRoute &&
            (hit.position - known).magnitude < (bot.Position - known).magnitude &&
            SainRegroupBridge.IsDestinationAvailable(bot.BotOwner, hit.position))
        { Commit(hit.position, "provisionalAdvance"); return; }
        Fail("noCompleteApproach");
    }
    private void HoldForAssessment(string reason)
    {
        if (!riskHeld)
        {
            // Retain a completed geometry scan when no destination was committed.
            if (Destination.HasValue) ReleaseDestination();
            OwnsMovement = false; riskHeld = true; riskRetryAt = Time.time + 3f;
            Phase = SAINPushPhase.Assessing; Record("hold." + reason);
        }
        Pause();
    }
    private void Commit(Vector3 destination, string reason)
    {
        Destination = destination; progress = bot.Position; activeSeconds = stalledSeconds = 0f; lastTick = -1f;
        Phase = SAINPushPhase.Approach; SainRegroupBridge.Claim(bot.BotOwner, destination); Record(reason);
    }
    internal Vector3? TickMovement()
    {
        if (!OwnsMovement || !Destination.HasValue) { Pause(); return null; }
        float elapsed = lastTick < 0f ? 0f : Mathf.Max(0f, Time.time - lastTick); lastTick = Time.time;
        activeSeconds += elapsed;
        if ((bot.Position - progress).sqrMagnitude >= 0.5625f) { progress = bot.Position; stalledSeconds = 0f; }
        else stalledSeconds += elapsed;
        if (activeSeconds >= 20f || stalledSeconds >= 6f) { Fail(stalledSeconds >= 6f ? "noProgress" : "approachExpired"); return null; }
        SainRegroupBridge.Claim(bot.BotOwner, Destination.Value);
        return Destination;
    }
    internal void Pause() => lastTick = -1f;
    internal void Fail(string reason)
    {
        if (Exhausted) return;
        ReleaseDestination(); Phase = SAINPushPhase.Exhausted; Record(reason);
    }
    private void ReleaseDestination()
    {
        if (Destination.HasValue) SainRegroupBridge.Release(bot.BotOwner, Destination.Value);
        Destination = null; cover = null; OwnsMovement = false; Pause(); finder.Clear();
    }
    internal void Clear(string reason)
    {
        if (Active) Record(reason);
        ReleaseDestination(); Mode = SAINPushMode.None; Phase = SAINPushPhase.None; EnemyId = null; target = null;
        holdUntil = recoveryUntil = riskRetryAt = 0f; riskHeld = false; lastRiskSignature = null;
        pressureLatch = targetBound = false; NativeEngagementAllowed = false;
    }
    internal object Snapshot => new { mode = Mode.ToString(), phase = Phase.ToString(), enemyId = EnemyId,
        reason = Reason, destination = SAINFollowerRecorder.Point(Destination), activeSeconds, risk = assessment.Snapshot,
        arrivalHoldRemaining = Mathf.Max(0f, holdUntil - Time.time), recoveryRemaining = Mathf.Max(0f, recoveryUntil - Time.time) };
    private void Record(string reason)
    {
        Reason = reason;
        if (SainCombatRecorderBridge.IsRecording)
            SainCombatRecorderBridge.RecordEvent(bot.BotOwner, "sainObjective", new { objective = "Push", reason, state = Snapshot });
    }
}

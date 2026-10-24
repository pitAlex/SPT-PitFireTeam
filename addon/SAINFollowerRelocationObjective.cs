using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.SubComponents.CoverFinder;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.SAINAddon;

internal enum SAINRelocationMode { None, There, ComeHere }

// One-shot Core combat gestures, with SAIN movement and perception. This is not
// regroup: There keeps its point, and ComeHere commits a closer boss-area cover.
internal sealed class SAINFollowerRelocationObjective(BotComponent bot)
{
    private readonly SAINFollowerCoverFinder finder = new(bot);
    internal SAINRelocationMode Mode { get; private set; }
    internal bool Active => Mode != SAINRelocationMode.None;
    internal bool OwnsAction { get; private set; }
    internal bool Settling => holdUntil > 0f;
    internal Vector3? Destination { get; private set; }
    internal string Reason { get; private set; }
    private CoverPoint cover;
    private object ownedPath;
    private Vector3 bossAnchor;
    private float planningUntil, holdUntil, nextMove, nextValidation, bestDistance, stalledSeconds, lastTick = -1f;
    private BotFollowerPlayer? Follower => BossPlayers.Instance?.GetFollower(bot.BotOwner);

    internal void Observe()
    {
        if (!Active) return;
        if (bot.IsDead || !SainAddonBridge.IsAddonTacticSelected(bot.BotOwner) ||
            !SAINFollowerCombatHandoff.HasLiveEnemy(bot)) { Clear("combatEnded"); return; }
        if (Follower == null || Follower.TryGetActiveCommand(out _, out _))
        { Clear("replacementOrder"); return; }
        if (Settling && Time.time >= holdUntil) Finish("arrivalComplete");
    }

    private static bool Protected(ECombatDecision solo, ESelfActionType self) =>
        self != ESelfActionType.None || solo == ECombatDecision.Retreat ||
        solo == ECombatDecision.AvoidGrenade || solo == ECombatDecision.ThrowGrenade ||
        solo == ECombatDecision.DogFight || solo == ECombatDecision.MeleeAttack || solo == ECombatDecision.FightZombies;

    internal bool GetDecision(Enemy enemy, ECombatDecision solo, ESelfActionType self, out ECombatDecision decision)
    {
        decision = solo; OwnsAction = false;
        Observe();
        // Keep pending commands within their original timeout while medicine or
        // survival owns the bot. Never interrupt an ongoing medical relocation.
        if (Protected(solo, self) || SainAddonBridge.IsUsingMedical(bot.BotOwner))
        { if (Active) Clear("survival"); return false; }
        var follower = Follower;
        if (!Active && SAINFollowerCombatHandoff.HasLiveEnemy(bot) &&
            follower?.TryGetActiveCommand(out FollowerCommandType command, out Vector3 point) == true &&
            (command == FollowerCommandType.CombatMoveToPointTactical || command == FollowerCommandType.CombatComeToBossCover))
        {
            if (!SainPlayerSquadBridge.TryGetPlayerLeader(bot.BotOwner, out Player player) || player.HealthController?.IsAlive != true)
            { RejectPending(follower, "missingPlayer"); return false; }
            bossAnchor = player.Position;
            Mode = command == FollowerCommandType.CombatMoveToPointTactical ? SAINRelocationMode.There : SAINRelocationMode.ComeHere;
            follower.ClearCommand("SAIN:ConsumeCombatGesture");
            SAINFollowerRuntime.GetPush(bot.BotOwner)?.Clear("combatGesture");
            SAINFollowerRuntime.GetRegroup(bot.BotOwner)?.Clear("combatGesture");
            SAINFollowerRuntime.GetCover(bot.BotOwner)?.Clear();
            bot.Cover.StopSeekingCover(); bot.Mover.Stop();
            planningUntil = Time.time + 8f; holdUntil = 0f; nextMove = nextValidation = 0f; lastTick = -1f; stalledSeconds = 0f;
            Record("begin");
            if (Mode == SAINRelocationMode.There && !TryPoint(point))
            { Reject("invalidTacticalPoint"); return false; }
        }
        if (!Active) return false;
        if (!Destination.HasValue && !PlanComeHere(enemy))
        {
            if (!Active) return false;
        }
        if (ShouldYieldToFight(enemy)) { Finish("immediateFight"); return false; }
        OwnsAction = true; decision = ECombatDecision.MoveToEngage;
        return true;
    }

    private bool TryPoint(Vector3 point)
    {
        if (!FollowerCombatCommandGeometry.IsFinite(point) ||
            !NavMesh.SamplePosition(point, out NavMeshHit hit, 1f, NavMesh.AllAreas) ||
            !SainRegroupBridge.SameLevel(hit.position, point) ||
            !SainRegroupBridge.TryGetDistance(bot.Position, hit.position, out _) ||
            !SainRegroupBridge.IsDestinationAvailable(bot.BotOwner, hit.position)) return false;
        Commit(hit.position, null, "tacticalPoint"); return true;
    }

    private bool PlanComeHere(Enemy enemy)
    {
        if (Time.time >= planningUntil) { Reject("coverPlanningExpired"); return false; }
        float maxBossDistance = Mathf.Max(0f, (bot.Position - bossAnchor).magnitude - FollowerCombatCommandGeometry.ComeCoverMinimumProgress);
        foreach (CoverPoint candidate in finder.Find(enemy, bossAnchor))
        {
            float distance = (candidate.Position - bossAnchor).magnitude;
            if (distance > SainCoverGeometry.SearchRadius || distance > maxBossDistance) continue;
            Commit(candidate.Position, candidate, "bossCover"); return true;
        }
        if (finder.Pending) return false;
        if (FollowerCombatCommandGeometry.TryBossApproach(bot.Position, bossAnchor, out Vector3 point) &&
            SainRegroupBridge.TryGetDistance(bot.Position, point, out _) && SainRegroupBridge.IsDestinationAvailable(bot.BotOwner, point))
        { Commit(point, null, "bossApproach"); return true; }
        Reject("noBossApproach"); return false;
    }

    private void Commit(Vector3 point, CoverPoint selected, string reason)
    {
        Destination = point; cover = selected; bestDistance = (point - bot.Position).magnitude;
        stalledSeconds = 0f; lastTick = -1f;
        SainRegroupBridge.Claim(bot.BotOwner, point); Record(reason);
    }

    // Core tactical point movement ends for a real shot or incoming fire; a
    // cover approach may keep walking and firing until cover is reached.
    private bool ShouldYieldToFight(Enemy enemy) =>
        (cover == null || Settling) && ((enemy?.IsVisible == true && enemy.CanShoot) || SainRegroupBridge.IsUnderFire(bot.BotOwner));

    internal void Tick()
    {
        Observe();
        if (!Active || !OwnsAction) { StopOwnedPath(); return; }
        if (Protected(bot.Decision.CurrentCombatDecision, bot.Decision.CurrentSelfDecision) ||
            SainAddonBridge.IsUsingMedical(bot.BotOwner) || ShouldYieldToFight(bot.GoalEnemy))
        { Finish("combatInterrupt"); return; }
        if (!Destination.HasValue)
        {
            StopOwnedPath();
            if (Time.time >= planningUntil) Reject("coverPlanningExpired");
            return;
        }
        Vector3 point = Destination.Value;
        if (Time.time >= nextValidation)
        {
            nextValidation = Time.time + 1f;
            if (!SainRegroupBridge.IsDestinationAvailable(bot.BotOwner, point) ||
                (cover != null && !finder.Validate(cover, bot.GoalEnemy)))
            { Reject("destinationInvalidated"); return; }
        }
        SainRegroupBridge.Claim(bot.BotOwner, point);
        if (Settling) { StopOwnedPath(); return; }
        float distance = (point - bot.Position).magnitude;
        if (distance <= FollowerCombatCommandGeometry.ArrivalDistance && SainRegroupBridge.SameLevel(bot.Position, point))
        {
            StopOwnedPath(); holdUntil = Time.time + SainCoverGeometry.ArrivalHoldSeconds(false); Record("arrivalHold"); return;
        }
        float elapsed = lastTick < 0f ? 0f : Mathf.Max(0f, Time.time - lastTick); lastTick = Time.time;
        if (distance <= bestDistance - FollowerCombatCommandGeometry.ProgressDistance)
        { bestDistance = distance; stalledSeconds = 0f; }
        else stalledSeconds += elapsed;
        if (stalledSeconds > FollowerCombatCommandGeometry.StallSeconds) { Reject("noProgress"); return; }
        if (Time.time < nextMove) return;
        nextMove = Time.time + 0.5f;
        bot.Mover.SetTargetPose(1f); bot.Mover.SetTargetMoveSpeed(1f);
        if (bot.Mover.WalkToPoint(point, true, FollowerCombatCommandGeometry.ArrivalDistance)) ownedPath = bot.Mover.ActivePath;
        else Reject("pathRejected");
    }

    internal void Pause() { StopOwnedPath(); lastTick = -1f; nextMove = 0f; }
    private void StopOwnedPath()
    {
        if (ownedPath != null && ReferenceEquals(bot.Mover.ActivePath, ownedPath)) bot.Mover.Stop();
        ownedPath = null;
    }
    private void RejectPending(BotFollowerPlayer follower, string reason)
    { follower.ClearCommand("SAIN:CombatGesture:" + reason); Reject(reason); }
    private void Reject(string reason)
    {
        bot.BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
        bot.BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
        Finish(reason);
    }
    private void Finish(string reason)
    {
        // Keep a quiet action until the next native publication. Completion must
        // not run a stale MoveToEngage result as a different native movement.
        bool owned = OwnsAction; Clear(reason); OwnsAction = owned;
    }
    internal void Clear(string reason)
    {
        if (Active) Record(reason);
        StopOwnedPath();
        if (Destination.HasValue) SainRegroupBridge.Release(bot.BotOwner, Destination.Value);
        Mode = SAINRelocationMode.None; Destination = null; cover = null; OwnsAction = false;
        holdUntil = 0f; lastTick = -1f; finder.Clear();
    }
    internal object Snapshot => new { mode = Mode.ToString(), reason = Reason,
        destination = SAINFollowerRecorder.Point(Destination), settling = Settling, stalledSeconds };
    private void Record(string reason)
    {
        Reason = reason;
        if (SainCombatRecorderBridge.IsRecording)
            SainCombatRecorderBridge.RecordEvent(bot.BotOwner, "sainObjective", new { objective = "Relocation", reason, state = Snapshot });
    }
}

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using EFT;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.Models.Enums;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace pitTeam.SAINAddon;

// Reads native state and listens to published decisions. It never calls a decision
// provider, changes a path or updates the gameplay handoff to obtain a snapshot.
internal sealed class SAINFollowerRecorder : IDisposable
{
    private readonly BotComponent bot;
    private readonly SAINFollowerEngageAttempt attempt;
    private readonly SAINFollowerRegroupObjective regroup;
    private string phase = "Released", selectedLayer, selectedAction;
    private int actionId;
    private bool reportedFailure;
    private float actionStarted;
    internal bool Active { get; private set; }
    internal SAINFollowerRecorder(BotComponent bot, SAINFollowerEngageAttempt attempt, SAINFollowerRegroupObjective regroup)
    {
        this.bot = bot; this.attempt = attempt; this.regroup = regroup;
        bot.Decision.DecisionManager.OnDecisionMade += DecisionMade;
    }
    internal void ObservePhase(SAINFollowerCombatPhase value)
    {
        phase = value.ToString();
        Active = value == SAINFollowerCombatPhase.Linger ||
            (value == SAINFollowerCombatPhase.Combat && (bot.Decision.HasDecision || SainAddonBridge.IsUsingMedical(bot.BotOwner)));
        SainCombatRecorderBridge.RecordState(bot.BotOwner, Active, phase);
    }
    private void DecisionMade(ECombatDecision solo, ESquadDecision squad, ESelfActionType self, Enemy enemy, BotComponent source)
    {
        if (!pitFireTeam.UseSainFollowerCombat(bot.BotOwner)) return;
        try
        {
            if ((solo != ECombatDecision.None || squad != ESquadDecision.None || self != ESelfActionType.None) &&
                SAINFollowerCombatHandoff.AllowsDecision(bot, solo, self))
            { Active = true; SainCombatRecorderBridge.RecordState(bot.BotOwner, true, "nativeDecision"); }
            if (SainCombatRecorderBridge.IsRecording)
                SainCombatRecorderBridge.RecordEvent(bot.BotOwner, "sainDecision", new {
                    solo = solo.ToString(), squad = squad.ToString(), self = self.ToString(),
                    enemyId = enemy?.EnemyPlayer?.ProfileId, actionInstanceId = actionId,
                    firingPosition = Point(bot.Decision.EnemyDecisions.FiringPosition),
                    coverState = bot.Cover.CoverSeekingState.ToString(), engageFailure = attempt.Failure
                });
        }
        catch (Exception ex)
        {
            if (!reportedFailure) { reportedFailure = true; pitTeam.Modules.Logger.LogError($"[SAIN] Native decision recording failed: {ex}"); }
        }
    }

    internal void Selected(string layer, Type action, string reason)
    {
        if (!Active || !pitFireTeam.UseSainFollowerCombat(bot.BotOwner)) return;
        Ended(selectedLayer, "nextAction");
        selectedLayer = layer; selectedAction = action.Name; actionStarted = Time.time; actionId++;
        if (SainCombatRecorderBridge.IsRecording)
            SainCombatRecorderBridge.RecordEvent(bot.BotOwner, "sainActionSelected", new {
                layer, action = selectedAction, reason, actionInstanceId = actionId
            });
    }
    internal void Ended(string layer, string reason)
    {
        if (selectedAction == null || layer != selectedLayer) return;
        if (SainCombatRecorderBridge.IsRecording)
            SainCombatRecorderBridge.RecordEvent(bot.BotOwner, "sainActionEnd", new {
                layer, action = selectedAction, reason, actionInstanceId = actionId, duration = Time.time - actionStarted
            });
        selectedAction = null;
    }
    internal SainCombatSnapshot Capture()
    {
        var path = bot.Mover.ActivePath;
        var enemy = bot.GoalEnemy;
        var coverPoint = bot.Cover.CoverInUse ?? bot.Cover.CoverPoint_MovingTo;
        object coverSnapshot = new { owner = "sain", inCover = bot.Cover.CoverInUse != null,
            state = bot.Cover.CoverSeekingState.ToString(), position = Point(coverPoint?.Position),
            distance = coverPoint == null ? (float?)null : (coverPoint.Position - bot.Position).magnitude,
            spotted = coverPoint?.Spotted, bad = coverPoint?.CoverData.IsBad,
            movingTo = Point(bot.Cover.CoverPoint_MovingTo?.Position) };
        return new SainCombatSnapshot {
            InCombat = Active,
            ControlsMovement = bot.ActiveLayer != ESAINLayer.None,
            HasPath = path != null, Moving = bot.Mover.Moving, Running = bot.Mover.Running,
            Arrived = path?.Status == EBotMoveStatus.Complete,
            Destination = path?.Destination, Cover = coverSnapshot,
            Details = new {
                phase, activeLayer = bot.ActiveLayer.ToString(), action = bot.CurrentAction?.Name,
                selectedLayer, selectedAction, actionInstanceId = actionId,
                personality = SAINFollowerRuntime.GetPersonalitySnapshot(bot.BotOwner),
                enemyCombatAllowed = SAINFollowerCombatHandoff.AllowsEnemyCombat(bot.BotOwner),
                medical = CaptureMedical(),
                coverPolicy = SAINFollowerRuntime.GetCover(bot.BotOwner)?.Snapshot,
                decisions = new { solo = bot.Decision.CurrentCombatDecision.ToString(), squad = bot.Decision.CurrentSquadDecision.ToString(), self = bot.Decision.CurrentSelfDecision.ToString() },
                enemy = enemy == null ? null : new { profileId = enemy.EnemyPlayer?.ProfileId, enemy.IsVisible, enemy.CanShoot,
                    knownPosition = Point(enemy.KnownPlaces.LastKnownPosition), enemy.KnownPlaces.TimeSinceLastKnownUpdated,
                    pathStatus = enemy.Path.PathToEnemyStatus.ToString(), pathLength = enemy.Path.PathLength },
                firingPosition = Point(bot.Decision.EnemyDecisions.FiringPosition),
                cover = new { state = bot.Cover.CoverSeekingState.ToString(), inUse = Point(bot.Cover.CoverInUse?.Position),
                    movingTo = Point(bot.Cover.CoverPoint_MovingTo?.Position), bad = bot.Cover.CoverInUse?.CoverData.IsBad,
                    spotted = bot.Cover.CoverInUse?.Spotted },
                movement = new { bot.Mover.Moving, bot.Mover.Running, path = path == null ? null : new {
                    id = RuntimeHelpers.GetHashCode(path), status = path.Status.ToString(), navStatus = path.PathStatus.ToString(),
                    destination = Point(path.Destination), path.TimeStarted, path.PathLength, path.CurrentIndex,
                    path.OnLastCorner, sprintStatus = path.CurrentSprintStatus.ToString(), path.SprintReason,
                    points = path.PathPoints?.Take(16).Select(p => Point(p)).ToArray()
                } },
                regroup = new { mode = regroup.Mode.ToString(), regroup.Settling, regroup.Tight },
                engageAttempt = new { attempt.EnemyId, destination = Point(attempt.Destination), attempt.Failure, attempt.ActiveSeconds }
            }
        };
    }
    private object CaptureMedical()
    {
        var firstAid = bot.BotOwner.Medecine?.FirstAid;
        bool needsContext = firstAid?.Have2Do == true || firstAid?.Using == true ||
            bot.Memory.Health.HealthStatus != ETagStatus.Healthy;
        // Do not call ShallStartUse/CanUseFirstAid: those can select medicine and
        // change its target. Capture cached inputs, including non-goal threats.
        return new {
            healthStatus = bot.Memory.Health.HealthStatus.ToString(),
            timeSinceHit = bot.Medical?.TimeSinceShot,
            firstAidItemSelected = firstAid?.HaveSmth2Use,
            firstAidItemId = firstAid?.CurUsingMeds?.Id,
            firstAidBodyPart = firstAid?._bodyPartToHeal?.ToString(),
            bleeding = firstAid?.IsBleeding,
            knownEnemyCount = bot.EnemyController.KnownEnemies.Count,
            enemies = !needsContext ? null : bot.EnemyController.KnownEnemies.Where(e => e != null).Take(32).Select(e => new {
                profileId = e.EnemyPlayer?.ProfileId, alive = e.EnemyPlayer?.HealthController?.IsAlive,
                e.Seen, e.Heard, e.IsVisible, e.InLineOfSight, e.TimeSinceSeen,
                timeSinceKnown = e.TimeSinceLastKnownUpdated, pathDistance = e.EPathDistance.ToString()
            }).ToArray()
        };
    }

    internal static object? Point(Vector3? position) => position.HasValue ? new { x = position.Value.x, y = position.Value.y, z = position.Value.z } : null;
    public void Dispose()
    {
        bot.Decision.DecisionManager.OnDecisionMade -= DecisionMade;
        Ended(selectedLayer, "release");
        Active = false;
        SainCombatRecorderBridge.RecordState(bot.BotOwner, false, "addonRelease");
    }
}

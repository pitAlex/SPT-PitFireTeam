using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Modules;
using SAIN.Layers;
using SAIN.Models.Enums;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.SubComponents.CoverFinder;
using UnityEngine;

namespace pitTeam.SAINAddon;

// Defensive execution for native heard-from-peace decisions only. Native combat
// actions inherit fire/suppression and no-cover advance, which need Core admission.
internal sealed class SAINFollowerPreparationAction(BotOwner owner)
    : BotAction(owner, nameof(SAINFollowerPreparationAction)), IBotAction
{
    private SAINFollowerCoverFinder finder;
    private Enemy contact;
    private CoverPoint cover;
    private Vector3 knownPosition;
    private bool active, attempted, routeChecked, canMove;
    private float progressAt, bestDistance;

    private bool Allowed => active && pitFireTeam.UseSainFollowerCombat(BotOwner) &&
        SAINFollowerCombatHandoff.AllowsAmbushPreparation(Bot, Bot.GoalEnemy,
            Bot.Decision.CurrentCombatDecision, Bot.Decision.CurrentSquadDecision, Bot.Decision.CurrentSelfDecision);

    public override void Start()
    {
        base.Start();
        active = true;
        contact = null;
        finder ??= new SAINFollowerCoverFinder(Bot);
        Bot.Mover.Stop();
        StopFire();
    }

    public override void Update(CustomLayer.ActionData data)
    {
        if (!Allowed) return;
        StopFire();
        Enemy enemy = Bot.GoalEnemy;
        if (enemy.LastKnownPosition is not Vector3 heard) return;
        if (!ReferenceEquals(contact, enemy) || (heard - knownPosition).sqrMagnitude >= 64f ||
            !SainRegroupBridge.SameLevel(heard, knownPosition))
        {
            ReleaseCover();
            finder.Clear();
            contact = enemy; knownPosition = heard;
            attempted = false; routeChecked = false;
        }
        if (cover != null)
        {
            SainRegroupBridge.Claim(BotOwner, cover.Position);
            float distance = (Bot.Position - cover.Position).sqrMagnitude;
            if (cover.Spotted || cover.CoverData.IsBad) ReleaseCover();
            else if (distance <= 4f)
            {
                StopOwnedPath();
            }
            else
            {
                if (distance < bestDistance - 1f) { bestDistance = distance; progressAt = Time.time; }
                if (Time.time - progressAt >= 6f) ReleaseCover();
            }
        }
        else if (!attempted && Bot.Decision.CurrentCombatDecision != ECombatDecision.Freeze)
        {
            if (!routeChecked)
            {
                routeChecked = true;
                // A sound through a ceiling is not immediate pressure. Only remembered
                // coordinates participate; never inspect the hidden player's live position.
                canMove = SainRegroupBridge.SameLevel(Bot.Position, heard) ||
                    (SainRegroupBridge.TryGetDistance(Bot.Position, heard, out float route) &&
                     route <= SAINFollowerCoverFinder.NearbyCoverRange);
            }
            if (!canMove) attempted = true;
            else
            {
                var points = finder.FindPreparation(enemy);
                if (!finder.Pending)
                {
                    attempted = true;
                    foreach (CoverPoint point in points)
                    {
                        if (!Bot.Mover.GoToCoverPoint(point, false, ESprintUrgency.Low)) continue;
                        cover = point;
                        SainRegroupBridge.Claim(BotOwner, point.Position);
                        bestDistance = (Bot.Position - point.Position).sqrMagnitude;
                        progressAt = Time.time;
                        break;
                    }
                }
            }
        }
        if (cover != null && Bot.Mover.Moving) Bot.Mover.SetTargetMoveSpeed(1f);
        Bot.Mover.Pose.SetPoseToCover(enemy);
    }

    public override void OnSteeringTicked()
    {
        if (!Allowed) return;
        StopFire();
        if (Bot.GoalEnemy.LastKnownPosition is Vector3 heard)
            Bot.Steering.LookToPoint(heard + (Bot.Transform.WeaponRoot - Bot.Position));
    }

    private void StopFire()
    {
        Bot.Shoot.EndShoot();
        Bot.Aim.LoseAimTarget();
        Bot.Suppression.ResetSuppressing();
    }

    private void StopOwnedPath()
    {
        var path = Bot.Mover.ActivePath;
        if (cover != null && path != null && (path.Destination - cover.Position).sqrMagnitude <= 4f)
            Bot.Mover.Stop();
    }

    private void ReleaseCover()
    {
        if (cover == null) return;
        StopOwnedPath();
        SainRegroupBridge.Release(BotOwner, cover.Position);
        cover = null;
    }

    public override void Stop()
    {
        active = false;
        ReleaseCover();
        finder?.Clear();
        contact = null;
        base.Stop();
    }
}

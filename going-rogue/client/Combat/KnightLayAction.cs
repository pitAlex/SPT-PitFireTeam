using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.BigBrain.Actions;
using UnityEngine;

namespace pitTeam.GoingRogue.Combat;

internal sealed class KnightLayAction : FollowerCombatActionBase
{
    private readonly LayNode node;
    private float aimStartedAt;
    public KnightLayAction(BotOwner owner) : base(owner) => node = new LayNode(owner);
    public override void Start()
    {
        base.Start();
        aimStartedAt = 0f;
        node._nextCheckTryLay = node._nextCheckLookUpdate = node._nextLookToEnemyUpdate = 0f;
        node._canRotate = true;
        SetCombatSprint(false);
    }
    public override void Update(CustomLayer.ActionData data)
    {
        BotOwner.StopMove();
        // Native LayNode pose/look timing, without its autonomous launcher switching.
        // All explosives belong to the shared planned grenade objective.
        if (BotOwner.GetPlayer.MovementContext.IsInPronePose && BotOwner.GetPlayer.PoseLevel <= 0f)
            node._canRotate = true;
        else
        {
            if (Time.time >= node._nextCheckTryLay)
            {
                node._nextCheckTryLay = Time.time + 2f;
                BotOwner.BotLay.TryLay();
            }
            if (Time.time >= node._nextCheckLookUpdate)
            {
                node._canRotate = !node._canRotate;
                node._nextCheckLookUpdate = Time.time + 6f;
            }
        }
        var enemy = BotOwner.Memory?.GoalEnemy;
        if (enemy?.Person?.HealthController?.IsAlive != true) { StopCombatShooting(); return; }
        if (StopUnownedGrenadeLauncherFire(GetReason(data), enemy)) return;
        if (!BotOwner.WeaponManager.HaveBullets)
        {
            StopCombatShooting();
            BotOwner.WeaponManager.Reload.TryReload();
            return;
        }
        if (!enemy.IsVisible) { StopCombatShooting(); node.LookNormal(); return; }
        if (enemy.CanShoot)
        {
            if (StopIfFriendlyInCurrentFireLane(enemy) ||
                WaitForEnemyAimAlignment(ref aimStartedAt, maxAngle: 15f, timeout: 0.18f)) return;
            node._aiming.UpdateNodeByBrain(GetData<AimingResultParams>(data));
        }
        else
        {
            StopCombatShooting();
            if (node._canRotate && Time.time >= node._nextLookToEnemyUpdate)
            {
                node._nextLookToEnemyUpdate = Time.time + 2f;
                BotOwner.Steering.LookToDirection(enemy.Direction);
            }
        }
    }
    public override void Stop()
    {
        StopCombatShooting();
        BotOwner.BotLay.GetUp(false);
        base.Stop();
    }
}

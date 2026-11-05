using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.GoingRogue.Combat
{
    internal abstract class GoonMemberCombatLogic : FollowerCombatLogicBase
    {
        protected GoonMemberCombatLogic(BotOwner owner) : base(owner) { }
        protected override FollowerCombatObjectiveBase GetObjective() => defaultObjective;
        private float nextKnightDirectiveProbe;
        public override AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> current)
        {
            if (currentObjective == CombatObjectiveKind.OrderedPush && Time.time >= nextKnightDirectiveProbe &&
                CanInterruptForOrderedPushOrder(current) && !combatCommon.HasActiveOrPendingHealWork() &&
                !combatCommon.HasImmediateExplosiveDanger() && !BotOwner.Memory.IsUnderFire &&
                !FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f) &&
                BotOwner.WeaponManager.IsWeaponReady && BotOwner.WeaponManager.Selector?.IsChanging != true &&
                (BotOwner.WeaponManager.ShootController as Player.FirearmController)?.IsInReloadOperation() != true &&
                BotOwner.WeaponManager.Reload?.Reloading != true &&
                BotOwner.WeaponManager.Grenades?.ThrowindNow != true && BotOwner.WeaponManager.Grenades?.ReadyToThrow != true &&
                !(BotOwner.Memory.GoalEnemy?.IsVisible == true && BotOwner.Memory.GoalEnemy.CanShoot) &&
                ExUsecCombatExtension.TryGetPushOverride(BotOwner, out var enemy, out var point))
            {
                nextKnightDirectiveProbe = Time.time + 1f;
                if (combatCommon.TryPlanAssignedFiringPositionDecision(enemy, point, "bigPipe.knightReassign", out var destination,
                        out var next, preferBackline: false, enforceMarksmanPositionPolicy: false) &&
                    (BotOwner.Profile.Info.Settings.Role != WildSpawnType.exUsec ||
                        RoguePositionCoordinator.TryReserve(BotOwner, enemy, destination)))
                {
                    if (!combatCommon.TryPrepareAdmittedTargetTransition(current, "knightReassignPush", enemy, next))
                    {
                        if (BotOwner.Profile.Info.Settings.Role == WildSpawnType.exUsec)
                            RoguePositionCoordinator.ReleaseReservation(BotOwner);
                        return base.ShallEndCurrentDecision(current);
                    }
                    var follower = BossPlayers.Instance.GetFollower(BotOwner);
                    follower.ClearCommand("KnightReassignPush");
                    follower.ClearTemporaryCombatAggressionOverride("KnightReassignPush");
                    combatCommon.ClearCommittedPushDecision("KnightReassignPush");
                    combatCommon.ClearCommittedMovement("KnightReassignPush");
                    combatCommon.ClearCommittedPosition("KnightReassignPush");
                    combatCommon.ClearCommittedCover("KnightReassignPush");
                    ActivatePrimaryObjective("knightReassignPush");
                    BotOwner.GoToSomePointData.SetPoint(destination);
                    GoonPushLeadership.Complete(BotOwner);
                    return new AICoreActionEnd("knightReassignPush", true);
                }
            }
            return base.ShallEndCurrentDecision(current);
        }
        protected override bool ShouldConsumePushCommand(BotFollowerPlayer follower, EnemyInfo enemy)
            => follower != null && follower.TryPeekActiveCommand(out var command, out _, out _) && command == FollowerCommandType.PushEnemy;
        protected override bool ShouldConsumeSuppressCommand(BotFollowerPlayer follower, EnemyInfo enemy)
            => follower != null && follower.TryGetActiveCommand(out var command, out _) && command == FollowerCommandType.SuppressEnemy;
    }

    internal sealed class BigPipeCombatLogic : GoonMemberCombatLogic
    {
        internal BigPipeCombatLogic(BotOwner owner) : base(owner) { }
        protected override FollowerCombatObjectiveBase CreateDefaultObjective(BotOwner owner, FollowerCombatCommon common)
            => new BigPipeCombatObjective(owner, common);
    }
}

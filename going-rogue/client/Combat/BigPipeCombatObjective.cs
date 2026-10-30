using EFT;
using pitTeam.BigBrain;
using pitTeam.Modules;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.GoingRogue.Combat
{
    internal sealed class BigPipeCombatObjective : GoonCombatObjective
    {
        private float nextLauncherProbe;
        private float nextGroupProbe;
        private string groupSearchEnemy;
        private string groupStepEnemy;
        private Vector3? groupStepTarget;
        private Vector3 groupProgressPosition;
        private float groupProgressAt;
        private float nextGroupGroundDiagnostic;
        private const string GroupSearch = "bigPipe.groupSearch";
        internal BigPipeCombatObjective(BotOwner owner, FollowerCombatCommon common)
            : base(owner, common, new BigPipeVanillaCombat(owner), "bigPipe") { }

        public override void Reset() { EndGroupSearch(); base.Reset(); nextLauncherProbe = nextGroupProbe = nextGroupGroundDiagnostic = 0f; }
        public override void Activate() { base.Activate(); nextGroupProbe = 0f; }
        public override void Deactivate() { EndGroupSearch(); base.Deactivate(); }
        private void EndGroupSearch(bool releaseStep = true)
        {
            if (groupSearchEnemy != null) FollowerEnemyTracking.EndSearch(BotOwner, groupSearchEnemy);
            groupSearchEnemy = null;
            if (releaseStep) { groupStepTarget = null; groupStepEnemy = null; }
        }
        public override void DecisionChanged(AICoreActionResult<BotLogicDecision, CoreActionResultParams>? previous,
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> next)
        {
            base.DecisionChanged(previous, next);
            if (next.Reason?.StartsWith(GroupSearch, System.StringComparison.Ordinal) != true || next.Reason == GroupSearch + ".release")
            { EndGroupSearch(); return; }
            var enemy = BotOwner.Memory.GoalEnemy;
            // The new decision has already committed its step; replacing the old
            // enemy's search tracking must not discard the new movement transaction.
            if (groupSearchEnemy != enemy?.ProfileId) EndGroupSearch(releaseStep: false);
            if (enemy != null) { groupSearchEnemy = enemy.ProfileId; FollowerEnemyTracking.SearchTick(enemy); }
        }

        private bool CanJoinSearch(EnemyInfo enemy, out BotOwner knight)
        {
            knight = null;
            return CombatCommon.HasActiveCombatEnemy(enemy) && !enemy.IsVisible && !enemy.CanShoot &&
                !(enemy.PersonalLastSeenTime > 0f && Time.time - enemy.PersonalLastSeenTime < 10f) &&
                !CombatCommon.HasRecoveryPressure(1f) && !CombatCommon.HasActiveOrPendingHealWork() &&
                !CombatCommon.HasImmediateExplosiveDanger() && !BotOwner.Medecine.Using &&
                BotOwner.WeaponManager.Reload?.Reloading != true && BotOwner.WeaponManager.IsWeaponReady &&
                BotOwner.WeaponManager.Selector?.IsChanging != true &&
                (BotOwner.WeaponManager.ShootController as Player.FirearmController)?.IsInReloadOperation() != true &&
                BotOwner.WeaponManager.Grenades?.ThrowindNow != true && BotOwner.WeaponManager.Grenades?.ReadyToThrow != true &&
                !CombatCommon.HasPendingLauncherPrimaryFallback() &&
                GoonPushLeadership.TryGetSearchLeader(BotOwner, enemy, out knight);
        }

        private bool PlanSearch(EnemyInfo enemy, out Vector3 point,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            point = default; decision = default;
            if (!CanJoinSearch(enemy, out var knight)) return false;
            if (CombatCommon.TryPlanCombatFollowDecision(enemy, knight.Position, GroupSearch + ".move", out point, out decision)) return true;
            decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.holdPosition,
                Vector3.Distance(BotOwner.Position, knight.Position) <= 4f ? GroupSearch + ".spacing" : GroupSearch + ".blocked");
            return true;
        }

        protected override bool TryGetGroupSearchDecision(EnemyInfo enemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            if (!PlanSearch(enemy, out var point, out decision)) return false;
            CombatCommon.RequestLauncherPrimaryFallback(GroupSearch);
            if (CombatCommon.TryCreatePendingLauncherPrimaryFallbackDecision(out var primary))
            { decision = primary; return true; }
            CommitSearchPoint(point, decision);
            return true;
        }

        private void CommitSearchPoint(Vector3 point, AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            nextGroupProbe = Time.time + 0.75f;
            groupStepTarget = null;
            groupStepEnemy = null;
            if (decision.Action != BotLogicDecision.goToPoint) return;
            BotOwner.GoToSomePointData.SetPoint(point);
            groupStepTarget = point;
            groupStepEnemy = BotOwner.Memory.GoalEnemy?.ProfileId;
            groupProgressPosition = BotOwner.Position;
            groupProgressAt = Time.time;
        }

        private bool HasProgressingGroupStep()
        {
            if (!groupStepTarget.HasValue || groupStepEnemy != BotOwner.Memory.GoalEnemy?.ProfileId ||
                BotOwner.Mover?.HasPathAndNoComplete != true ||
                BotOwner.GoToSomePointData?.HaveTarget() != true || BotOwner.GoToSomePointData.IsCome() ||
                (BotOwner.GoToSomePointData.Point - groupStepTarget.Value).sqrMagnitude > 0.01f) return false;
            if ((BotOwner.Position - groupProgressPosition).sqrMagnitude >= 0.25f)
            {
                groupProgressPosition = BotOwner.Position;
                groupProgressAt = Time.time;
            }
            return Time.time - groupProgressAt < 3f;
        }

        private bool CanRefreshGroupStep()
        {
            if (BotOwner.GetPlayer?.MovementContext?.IsGrounded == true &&
                FollowerCombatCommon.HasSupportedCombatGround(BotOwner.Position)) return true;
            if (Time.time >= nextGroupGroundDiagnostic)
            {
                nextGroupGroundDiagnostic = Time.time + 5f;
                BattleRecorder.RecordObjectiveDiagnostic(BotOwner, GroupSearch, "refreshDeferred", "unsupportedFooting", () => new
                {
                    position = BotOwner.Position,
                    destination = BotOwner.GoToSomePointData?.Point,
                    hasPath = BotOwner.Mover?.HasPathAndNoComplete,
                    grounded = BotOwner.GetPlayer?.MovementContext?.IsGrounded,
                    progressAge = Time.time - groupProgressAt
                });
            }
            return false;
        }

        protected override bool TryGroupSearchTransition(AICoreActionResult<BotLogicDecision, CoreActionResultParams> current,
            out AICoreActionEnd end)
        {
            end = default;
            bool active = current.Reason?.StartsWith(GroupSearch, System.StringComparison.Ordinal) == true;
            if (Time.time < nextGroupProbe || FollowerCombatCommon.IsMedicalDecision(current) ||
                !active && (current.Action != BotLogicDecision.holdPosition || !CombatCommon.HasCommittedHolderSettled(1f))) return false;
            nextGroupProbe = Time.time + 0.75f;
            // A failed *next* follow sample must not cancel a valid native stair route.
            // Keep common emergency/arrival handling below authoritative by returning false.
            if (active && current.Action == BotLogicDecision.goToPoint &&
                CanJoinSearch(BotOwner.Memory.GoalEnemy, out _) &&
                (HasProgressingGroupStep() || !CanRefreshGroupStep())) return false;
            bool planned = PlanSearch(BotOwner.Memory.GoalEnemy, out var point, out var next);
            if (!planned && !active) return false;
            if (!planned) next = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.holdPosition, GroupSearch + ".release");
            if (active && current.Action == BotLogicDecision.holdPosition && next.Action == BotLogicDecision.holdPosition &&
                current.Reason == next.Reason) return false;
            if (!CombatCommon.TryPrepareDecisionTransition(current, GroupSearch + ".refresh", next)) return false;
            BattleRecorder.RecordObjectiveDiagnostic(BotOwner, GroupSearch, "refreshPrepared", next.Reason, () => new
            {
                previousReason = current.Reason,
                previousDestination = BotOwner.GoToSomePointData?.Point,
                nextDestination = next.Action == BotLogicDecision.goToPoint ? (Vector3?)point : null,
                position = BotOwner.Position,
                hasPath = BotOwner.Mover?.HasPathAndNoComplete,
                grounded = BotOwner.GetPlayer?.MovementContext?.IsGrounded,
                progressAge = Time.time - groupProgressAt
            });
            // Clear the old ordinary recovery hold only after a real successor is prepared.
            CombatCommon.ClearCommittedCover(GroupSearch + ".handoff");
            CombatCommon.ClearCommittedPosition(GroupSearch + ".handoff");
            CombatCommon.ClearCommittedMovement();
            CommitSearchPoint(point, next);
            end = new AICoreActionEnd(GroupSearch + ".refresh", true);
            return true;
        }

        public override AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> current)
        {
            if (current.Reason == GroupSearch + ".release") return new AICoreActionEnd(GroupSearch + ".released", true);
            if (current.Action == BotLogicDecision.holdPosition && current.Reason?.StartsWith(GroupSearch, System.StringComparison.Ordinal) == true)
            {
                if (!CombatCommon.HasActiveCombatEnemy()) return new AICoreActionEnd(GroupSearch + ".enemyGone", true);
                if (TryGroupSearchTransition(current, out var end)) return end;
                return FollowerCombatCommon.Continue();
            }
            return base.ShallEndCurrentDecision(current);
        }

        protected override bool TryGetRoleSupportDecision(EnemyInfo enemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            if (Time.time < nextLauncherProbe || !BigPipeDecisionPolicy.InLauncherRange(FollowerEnemyTracking.Distance(enemy)) ||
                Enemy.IsMemoryOnlyAcquisitionWithoutPersonalContact(enemy) ||
                !FollowerCombatCommon.HasUsableEquippedGrenadeLauncher(BotOwner)) return false;
            nextLauncherProbe = Time.time + 1f;
            if (CombatCommon.IsGrenadeLauncherSuppressCooldownActive(ordered: false, out _) ||
                CombatCommon.HasPendingLauncherPrimaryFallback() ||
                !CombatCommon.HasAutonomousGrenadeLauncherTarget(enemy, out _)) return false;
            decision = FollowerCombatGrenadierObjective.CreateAutonomousActivationDecision();
            nextLauncherProbe = Time.time + FollowerCombatGrenadierObjective.OpportunityWindowSeconds + 1f;
            return true;
        }
    }
}

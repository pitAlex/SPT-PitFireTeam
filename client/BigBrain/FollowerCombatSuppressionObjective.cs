using EFT;
using System;
using UnityEngine;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatSuppressionObjective : FollowerCombatObjectiveBase
    {
        internal const string ReasonPrefix = "objectiveSuppress";
        private const string WeaponSwitchToPrimaryReason = "objectiveSuppress.weaponSwitchToPrimary";
        private const string AutomaticSecondarySwitchReason = "objectiveSuppress.autoSecondarySwitch";
        private const string AutomaticSecondarySettleReason = "objectiveSuppress.autoSecondarySettle";
        private const float WeaponSwitchRetrySeconds = 0.25f;
        private const float AutomaticSecondarySwitchTimeoutSeconds = 1.5f;

        private bool complete;
        private bool negativeSaid;
        private bool forceWeaponSuppress;
        private bool useAutomaticSecondarySuppress;
        private float weaponSwitchRetryUntil;
        private bool automaticSecondarySwitchPending;
        private float automaticSecondarySwitchUntil;
        private float automaticSecondarySettleUntil;

        public FollowerCombatSuppressionObjective(BotOwner botOwner, FollowerCombatCommon combatCommon)
            : base(botOwner, combatCommon)
        {
        }

        public override bool IsComplete => complete;

        public override void Reset()
        {
            complete = false;
            negativeSaid = false;
            forceWeaponSuppress = false;
            useAutomaticSecondarySuppress = false;
            weaponSwitchRetryUntil = 0f;
            automaticSecondarySwitchPending = false;
            automaticSecondarySwitchUntil = 0f;
            automaticSecondarySettleUntil = 0f;
        }

        public override void Activate()
        {
            Activate(requireLauncher: false);
        }

        public void Activate(bool requireLauncher, bool forceWeapon = false, bool useAutomaticSecondary = false)
        {
            Reset();
            _ = requireLauncher;
            forceWeaponSuppress = forceWeapon;
            useAutomaticSecondarySuppress = useAutomaticSecondary;
            ClearObjectiveCommitments();
        }

        public override void Deactivate()
        {
            ClearObjectiveCommitments();
            complete = false;
        }

        public override void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            CombatCommon.HandleSharedDecisionChanged(nextDecision);
            CombatCommon.HandleFollowerSuppressDecisionChanged(nextDecision);
        }

        public override void StartDecision()
        {
        }

        public override AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy)
        {
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                complete = true;
                return Hold("noEnemy");
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? dogFight = CombatCommon.TryGetDogFightDecision();
            if (dogFight != null)
            {
                return dogFight.Value;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? healDecision = CombatCommon.TryGetNeedHealDecision();
            if (healDecision != null)
            {
                return healDecision.Value;
            }

            if (useAutomaticSecondarySuppress)
            {
                if (!TryGetAutomaticSecondarySwitchDecision(
                        out AICoreActionResultStruct<BotLogicDecision, GClass26> switchDecision,
                        out bool automaticSecondaryReady))
                {
                    SayNegativeOnce();
                    complete = true;
                    ClearObjectiveCommitments();
                    return Hold("automaticSecondaryUnavailable");
                }

                if (!automaticSecondaryReady)
                {
                    return switchDecision;
                }
            }

            if (CombatCommon.TryCreateOrderedSuppressWeaponFallbackDecision(
                    goalEnemy,
                    ReasonPrefix,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> fallbackDecision))
            {
                if (string.Equals(fallbackDecision.Reason, WeaponSwitchToPrimaryReason, StringComparison.Ordinal))
                {
                    weaponSwitchRetryUntil = Time.time + WeaponSwitchRetrySeconds;
                }

                return fallbackDecision;
            }

            if (CombatCommon.TryCreateSuppressDecision(
                    goalEnemy,
                    ReasonPrefix,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
                    allowObstructedSuppression: true))
            {
                return decision;
            }

            SayNegativeOnce();
            complete = true;
            return Hold("noSuppressionDecision");
        }

        public override AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            if (!IsSuppressionObjectiveReason(currentDecision.Reason))
            {
                return CombatCommon.ShallEndCurrentDecision(currentDecision);
            }

            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                complete = true;
                ClearObjectiveCommitments();
                return new AICoreActionEndStruct("suppressionEnemyMissing", true);
            }

            if (currentDecision.Action == BotLogicDecision.suppressFire)
            {
                AICoreActionEndStruct end = CombatCommon.EndSuppressFire(currentDecision.Reason);
                if (end.Value)
                {
                    complete = true;
                    ClearObjectiveCommitments();
                }

                return end;
            }

            if (currentDecision.Action == BotLogicDecision.holdPosition)
            {
                if (string.Equals(currentDecision.Reason, AutomaticSecondarySwitchReason, StringComparison.Ordinal))
                {
                    if (CombatCommon.IsEligibleAutomaticSecondarySelectedAndReady())
                    {
                        automaticSecondarySwitchPending = false;
                        automaticSecondarySwitchUntil = 0f;
                        return new AICoreActionEndStruct("suppressionAutomaticSecondaryReady", true);
                    }

                    if (!automaticSecondarySwitchPending || Time.time >= automaticSecondarySwitchUntil)
                    {
                        SayNegativeOnce();
                        complete = true;
                        ClearObjectiveCommitments();
                        return new AICoreActionEndStruct("suppressionAutomaticSecondaryFailed", true);
                    }

                    CombatCommon.HoldFor(Mathf.Max(0.05f, automaticSecondarySwitchUntil - Time.time));
                    return default;
                }

                if (string.Equals(currentDecision.Reason, AutomaticSecondarySettleReason, StringComparison.Ordinal))
                {
                    if (CombatCommon.IsEligibleAutomaticSecondarySelectedAndReady() ||
                        CombatCommon.IsWeaponSelectionSettledForAutomaticSecondaryRequest())
                    {
                        automaticSecondarySettleUntil = 0f;
                        return new AICoreActionEndStruct("suppressionAutomaticSecondarySettled", true);
                    }

                    if (Time.time >= automaticSecondarySettleUntil)
                    {
                        SayNegativeOnce();
                        complete = true;
                        ClearObjectiveCommitments();
                        return new AICoreActionEndStruct("suppressionAutomaticSecondarySettleFailed", true);
                    }

                    CombatCommon.HoldFor(Mathf.Max(0.05f, automaticSecondarySettleUntil - Time.time));
                    return default;
                }

                if (string.Equals(currentDecision.Reason, WeaponSwitchToPrimaryReason, StringComparison.Ordinal))
                {
                    if (Time.time >= weaponSwitchRetryUntil)
                    {
                        return new AICoreActionEndStruct("suppressionWeaponSwitchRetry", true);
                    }

                    CombatCommon.HoldFor(Mathf.Max(0.05f, weaponSwitchRetryUntil - Time.time));
                    return default;
                }

                complete = true;
                ClearObjectiveCommitments();
                return new AICoreActionEndStruct("suppressionNoAction", true);
            }

            return CombatCommon.ShallEndCurrentDecision(currentDecision);
        }

        private void SayNegativeOnce()
        {
            if (negativeSaid)
            {
                return;
            }

            negativeSaid = true;
            BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
        }

        internal static bool IsSuppressionObjectiveReason(string? reason)
        {
            return reason != null && reason.StartsWith(ReasonPrefix, StringComparison.Ordinal);
        }

        private bool TryGetAutomaticSecondarySwitchDecision(
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            out bool ready)
        {
            decision = default;
            ready = CombatCommon.IsEligibleAutomaticSecondarySelectedAndReady();
            if (ready)
            {
                automaticSecondarySwitchPending = false;
                automaticSecondarySwitchUntil = 0f;
                automaticSecondarySettleUntil = 0f;
                return true;
            }

            if (automaticSecondarySwitchPending)
            {
                if (Time.time >= automaticSecondarySwitchUntil)
                {
                    return false;
                }

                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.holdPosition,
                    AutomaticSecondarySwitchReason);
                return true;
            }

            if (!CombatCommon.IsWeaponSelectionSettledForAutomaticSecondaryRequest())
            {
                if (automaticSecondarySettleUntil <= Time.time)
                {
                    automaticSecondarySettleUntil = Time.time + AutomaticSecondarySwitchTimeoutSeconds;
                }

                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.holdPosition,
                    AutomaticSecondarySettleReason);
                return true;
            }

            automaticSecondarySettleUntil = 0f;

            if (!CombatCommon.HasLoadedAutomaticSecondaryForPush() ||
                !CombatCommon.TryRequestEligibleAutomaticSecondary())
            {
                return false;
            }

            automaticSecondarySwitchPending = true;
            automaticSecondarySwitchUntil = Time.time + AutomaticSecondarySwitchTimeoutSeconds;
            automaticSecondarySettleUntil = 0f;
            CombatCommon.HoldFor(0.25f);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                AutomaticSecondarySwitchReason);
            return true;
        }

        private void ClearObjectiveCommitments()
        {
            CombatCommon.ClearFollowerSuppressState();
            CombatCommon.ClearCommittedMovement();
            CombatCommon.ClearCommittedPosition();
            CombatCommon.ClearInitialDecision();
        }

        private static AICoreActionResultStruct<BotLogicDecision, GClass26> Hold(string suffix)
        {
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                $"{ReasonPrefix}.{suffix}");
        }
    }
}

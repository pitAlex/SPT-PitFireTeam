using EFT;
using pitTeam.BigBrain;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace pitTeam.Modules
{
    /// <summary>
    /// Preserves EFT/SAIN's native body-part choice and only promotes a non-head choice to a
    /// verified head lane when follower Precision permits it. Per-enemy state keeps that optional
    /// enhancement on the native retarget cadence instead of rerolling every frame.
    /// </summary>
    internal static class FollowerAimTargetPolicy
    {
        private const float MinimumHeadPreference = 10f;
        private const float NeutralHeadPreference = 40f;
        private const float MaximumHeadPreference = 70f;
        private const float MinimumRetargetSeconds = 0.1f;
        private static readonly ConditionalWeakTable<EnemyInfo, HeadEnhancementState> HeadEnhancementStates = new();

        private sealed class HeadEnhancementState
        {
            internal float NextRollTime;
            internal float RetainHeadUntil;
        }

        // SAIN's weighted choice may be a limb even with an open torso. Match the main
        // follower path's body-first baseline before applying the existing head enhancement.
        internal static bool TryGetBodyFirstShootPoint(
            EnemyInfo enemyInfo, out EnemyPart? body, out Vector3 point)
        {
            body = null;
            point = default;
            if (enemyInfo.Owner?.WeaponManager?.UnderbarrelLauncherController?.IsActive == true)
            {
                return false;
            }

            bool corrected = FollowerEnemyInfoCorrection.TryGetVerifiedShootParts(
                enemyInfo, out bool headShootable, out bool bodyShootable);
            body = GetEligiblePart(enemyInfo, BodyPartType.body, corrected, headShootable, bodyShootable);
            if (body == null) return false;
            point = body.GetPartPositionWithOffset();
            enemyInfo.LastPartToShoot = body;
            return true;
        }

        internal static float GetHeadPreference(float precisionPercent)
        {
            float precision = FollowerProficiencyModifierValues.NormalizePercent(precisionPercent);
            if (precision <= FollowerProficiencyModifierValues.DefaultPercent)
            {
                return MinimumHeadPreference +
                       (NeutralHeadPreference - MinimumHeadPreference) *
                       (precision / FollowerProficiencyModifierValues.DefaultPercent);
            }

            return NeutralHeadPreference +
                   (MaximumHeadPreference - NeutralHeadPreference) *
                   ((precision - FollowerProficiencyModifierValues.DefaultPercent) /
                    FollowerProficiencyModifierValues.DefaultPercent);
        }

        /// <summary>
        /// Returns false for non-followers so EFT/SAIN retains complete ownership. For followers,
        /// the native point is preserved when the native selector chose the head or no verified
        /// head lane exists. A non-head choice can be promoted on the Precision roll, while a sole
        /// exposed head replaces an invalid non-head fallback without a probability gate.
        /// </summary>
        internal static bool TryEnhanceFollowerShootPoint(
            EnemyInfo? enemyInfo,
            Vector3 nativeShootPoint,
            bool nativeSelectedHead,
            EnemyPart? nativeSelectedPart,
            out Vector3 shootPoint)
        {
            shootPoint = nativeShootPoint;
            BotOwner? botOwner = enemyInfo?.Owner;
            if (botOwner == null ||
                !FollowerProficiency.TryGetValues(botOwner, out FollowerProficiencyValues? proficiency) ||
                proficiency == null)
            {
                return false;
            }

            if (botOwner.WeaponManager?.UnderbarrelLauncherController?.IsActive == true)
            {
                return true;
            }

            if (nativeSelectedHead)
            {
                ClearRetainedHead(enemyInfo!);
                return true;
            }

            bool hasCorrectedParts = FollowerEnemyInfoCorrection.TryGetVerifiedShootParts(
                enemyInfo!,
                out bool correctedHead,
                out bool correctedBody);
            EnemyPart? head = GetEligiblePart(
                enemyInfo!,
                BodyPartType.head,
                hasCorrectedParts,
                correctedHead,
                correctedBody);
            if (head == null)
            {
                ClearRetainedHead(enemyInfo!);
                return true;
            }

            HeadEnhancementState state = HeadEnhancementStates.GetOrCreateValue(enemyInfo!);
            float now = Time.time;
            if (state.RetainHeadUntil > now)
            {
                enemyInfo!.LastPartToShoot = head;
                shootPoint = head.GetPartPositionWithOffset();
                return true;
            }

            if (state.NextRollTime > now)
            {
                return true;
            }

            EnemyPart? body = GetEligiblePart(
                enemyInfo!,
                BodyPartType.body,
                hasCorrectedParts,
                correctedHead,
                correctedBody);
            EnemyPart? leftArm = GetEligiblePart(
                enemyInfo!,
                BodyPartType.leftArm,
                hasCorrectedParts,
                correctedHead,
                correctedBody);
            EnemyPart? rightArm = GetEligiblePart(
                enemyInfo!,
                BodyPartType.rightArm,
                hasCorrectedParts,
                correctedHead,
                correctedBody);
            EnemyPart? leftLeg = GetEligiblePart(
                enemyInfo!,
                BodyPartType.leftLeg,
                hasCorrectedParts,
                correctedHead,
                correctedBody);
            EnemyPart? rightLeg = GetEligiblePart(
                enemyInfo!,
                BodyPartType.rightLeg,
                hasCorrectedParts,
                correctedHead,
                correctedBody);

            int nonHeadCount = CountNonNull(body, leftArm, rightArm, leftLeg, rightLeg);
            float precisionPercent = proficiency.Modifiers.GetPrecisionPercent();
            float headPreference = GetHeadPreference(precisionPercent);
            bool forcedHead = head != null && nonHeadCount == 0;
            bool headRollAttempted = head != null && nonHeadCount > 0;
            bool headRollSucceeded = headRollAttempted && MyExtensions.RandomBool(headPreference);
            state.NextRollTime = now + Mathf.Max(
                MinimumRetargetSeconds,
                BotInternalSettingsController.Core.SHOOT_TO_CHANGE_RND_PART_DELTA);
            bool enhancementApplied = forcedHead || headRollSucceeded;
            EnemyPart? selectedPart = nativeSelectedPart;
            if (enhancementApplied)
            {
                state.RetainHeadUntil = state.NextRollTime;
                enemyInfo!.LastPartToShoot = head;
                shootPoint = head.GetPartPositionWithOffset();
                selectedPart = head;
            }
            else
            {
                state.RetainHeadUntil = 0f;
            }

            BattleRecorder.RecordAimTargetSelection(
                botOwner,
                enemyInfo!,
                nativeSelectedPart,
                nativeSelectedHead,
                nativeShootPoint,
                precisionPercent,
                headPreference,
                hasCorrectedParts,
                correctedHead,
                correctedBody,
                head != null,
                body != null,
                nonHeadCount,
                forcedHead,
                headRollAttempted,
                headRollSucceeded,
                enhancementApplied,
                selectedPart,
                shootPoint);
            return true;
        }

        private static void ClearRetainedHead(EnemyInfo enemyInfo)
        {
            if (HeadEnhancementStates.TryGetValue(enemyInfo, out HeadEnhancementState state))
            {
                state.NextRollTime = 0f;
                state.RetainHeadUntil = 0f;
            }
        }

        private static EnemyPart? GetEligiblePart(
            EnemyInfo enemyInfo,
            BodyPartType bodyPartType,
            bool hasCorrectedParts,
            bool correctedHead,
            bool correctedBody)
        {
            if (!enemyInfo._allParts.TryGetValue(bodyPartType, out EnemyPart part) ||
                !IsEligiblePart(
                    enemyInfo,
                    part,
                    hasCorrectedParts,
                    correctedHead,
                    correctedBody))
            {
                return null;
            }

            return part;
        }

        private static bool IsEligiblePart(
            EnemyInfo enemyInfo,
            EnemyPart? part,
            bool hasCorrectedParts,
            bool correctedHead,
            bool correctedBody)
        {
            if (part == null)
            {
                return false;
            }

            if (hasCorrectedParts)
            {
                if (part.BodyPartType == BodyPartType.head)
                {
                    return correctedHead;
                }

                if (part.BodyPartType == BodyPartType.body)
                {
                    return correctedBody;
                }
            }

            return part.CanShoot &&
                   enemyInfo._allPartsVision.TryGetValue(part.BodyPartType, out EnemyPartVision vision) &&
                   vision.Visible;
        }

        private static int CountNonNull(
            EnemyPart? body,
            EnemyPart? leftArm,
            EnemyPart? rightArm,
            EnemyPart? leftLeg,
            EnemyPart? rightLeg)
        {
            int count = 0;
            if (body != null) count++;
            if (leftArm != null) count++;
            if (rightArm != null) count++;
            if (leftLeg != null) count++;
            if (rightLeg != null) count++;
            return count;
        }

    }
}
